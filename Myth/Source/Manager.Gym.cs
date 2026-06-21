using System.Text.Json;
using System.Numerics;
using Foster.Framework;

namespace Myth;

sealed record GymGoalResult(string Room, string Goal, int Trials, int Successes, double Rate, double AvgSuccessTime);

sealed record GymRun(Room Room, string GoalId, string? GoalName, Point2 GoalLocalPosition)
{
	public string DisplayId => string.IsNullOrWhiteSpace(GoalId) ? GoalName ?? $"{GoalLocalPosition.X},{GoalLocalPosition.Y}" : GoalId;
}
sealed record GymResult(string Room, string Goal, double SimTime, bool Success, string Reason);


internal struct Gym
{
	public Gym()
	{

	}

	internal bool exitAfterAll;
	internal List<GymRun>? runs;
	internal int runIndex;
	internal double roomStartTime;
	internal double nextLog;
	internal List<GymResult>? results;
	internal HashSet<string> goalIds = new(StringComparer.OrdinalIgnoreCase);

	internal bool CollectGymRooms(string? playRoomId = null)
	{
		var runs = new List<GymRun>();
		foreach (var room in Assets.Rooms.Values)
		{
			// With --play-room, restrict the gym to that one room's goals (and loop
			// through just those); otherwise gather goals from every room as before.
			if (playRoomId != null && !string.Equals(room.Id, playRoomId, StringComparison.OrdinalIgnoreCase))
				continue;

			foreach (var actor in room.Actors)
			{
				if (!string.Equals(ActorSpecs.ReadString(ActorSpecs.Resolve(actor), "Type"), "Goal", StringComparison.OrdinalIgnoreCase))
					continue;

				var goalId = ActorSpecs.ReadString(actor, "Id");
				var goalName = ActorSpecs.ReadString(actor, "Name");
				var position = ActorSpecs.ReadPoint(actor, "Position") ?? Point2.Zero;
				var runGoalId = string.IsNullOrWhiteSpace(goalId) ? $"{position.X},{position.Y}" : goalId;

				runs.Add(new(room, runGoalId, goalName, position));
			}
		}

		this.runs = goalIds.Count > 0 ? FilterGymRuns(runs) : runs;
		return this.runs.Count > 0;
	}


	readonly private List<GymRun> FilterGymRuns(List<GymRun> runs)
	{
		var selected = new List<GymRun>();
		var missing = new List<string>();
		foreach (var selector in goalIds)
		{
			var matches = runs.Where(run => string.Equals(run.GoalId, selector, StringComparison.OrdinalIgnoreCase)).ToList();

			if (matches.Count <= 0)
			{
				matches = [.. runs.Where(run => !string.IsNullOrWhiteSpace(run.GoalName) && string.Equals(run.GoalName, selector, StringComparison.OrdinalIgnoreCase))];
			}

			if (matches.Count <= 0)
			{
				missing.Add(selector);
				continue;
			}

			foreach (var match in matches)
				if (!selected.Contains(match))
					selected.Add(match);
		}

		if (missing.Count > 0)
		{
			Console.Error.WriteLine($"[Gym] no Goal matched --gym-goal: {string.Join(", ", missing)}");
			Environment.Exit(1);
			return [];
		}

		return selected;
	}


	readonly internal void PrintGymResults()
	{
		if (results is not { Count: > 0 })
			return;

		int successCount = 0;
		double totalTime = 0;
		foreach (var r in results)
		{
			if (r.Success) successCount++;
			totalTime += r.SimTime;
			Console.WriteLine(JsonSerializer.Serialize(r));
		}
		Console.WriteLine(JsonSerializer.Serialize(new
		{
			summary = new
			{
				rooms = results.Count,
				success = successCount,
				fail = results.Count - successCount,
				totalTime
			}
		}));
	}
}

partial class Manager
{
	readonly bool gymMode;
	readonly bool headless;
	readonly bool unthrottled;

	// Unthrottled runs have no fixed wall-clock per frame, so a time-budgeted nav-graph
	// rebuild finishes on a different game frame each run (non-deterministic gym results).
	// Callers use this to build the graph in one shot instead.
	internal bool Unthrottled => unthrottled;

	Gym gym;

	// Fail a run early if Bud reports "no path" continuously for this long (virtual seconds): he's stuck with nowhere to go, so there's no point waiting out the
	// full room timeout. -1 means we aren't currently in a no-path stretch.
	const double GymNoPathTimeout = 5.0;
	double gymNoPathStartTime = -1;

	static float GymSingleGoalTimeout = 30.0f; // TODO: we should probably make this based on the number of nodes from start to end for when we have bigger/mazier rooms and goal paths

	void GymTick()
	{
		if (gym.runs == null || game == null) return;

		if (game.GymBudDied)
		{
			Console.WriteLine("[Gym] Bud fell off world");
			AdvanceGym(success: false);
		}
		else if (game.GymTarget is Goal goal && game.GetFirst<Bud>() is { } bud && goal.OverlapsAny(Point2.Zero, bud))
		{
			Console.WriteLine("[Gym] Bud reached goal");
			AdvanceGym(success: true);
		}
		else if (Time.Seconds - gym.roomStartTime > GymSingleGoalTimeout)
		{
			Console.WriteLine($"[Gym] timeout ({Time.Seconds - gym.roomStartTime:F1}s), advancing");
			AdvanceGym(success: false);
		}
		else
		{
			CheckGymNoPathTimeout();
		}
	}

	private void AdvanceGym(bool success = false)
	{
		if (gym.runs is not { Count: > 0 })
			return;

		var current = gym.runs[gym.runIndex];
		var simTime = Time.Seconds - gym.roomStartTime;
		if (gym.exitAfterAll)
		{
			gym.results ??= [];
			gym.results.Add(new GymResult(current.Room.Id, current.DisplayId, simTime, success,
				success ? "goal" : game?.GymBudDied == true ? "fell" : "timeout"));
		}

#if BROWSER
		if (gym.exitAfterAll)
		{
			gym.PrintGymResults();
			Exit();
		}
		else
			Console.Error.WriteLine("[Gym] room transition not supported in browser");
		return;
#else
		gym.runIndex++;
		if (gym.runIndex >= gym.runs.Count)
		{
			if (gym.exitAfterAll)
			{
				gym.PrintGymResults();
				Exit();
				return;
			}
			gym.runIndex = 0;
		}

		var run = gym.runs[gym.runIndex];
		var room = run.Room;
		cell = room.Cell;
		selectedRoomId = null;
		Console.WriteLine($"[Gym] {current.Room.Cell.X},{current.Room.Cell.Y}:{current.DisplayId} → {room.Cell.X},{room.Cell.Y}:{run.DisplayId}");
		gym.roomStartTime = Time.Seconds;
		gymNoPathStartTime = -1;
		StartGame(immediate: true);
		AssignGymTarget(run);
#endif
	}

	// Returns true if the current run was advanced (failed) because Bud has been
	// reporting "no path" for longer than GymNoPathTimeout. Call only after the
	// death/goal/room-timeout checks so we don't advance twice in one frame.
	private bool CheckGymNoPathTimeout()
	{
		if (game?.GetFirst<Bud>() is not { } bud)
			return false;

		var noPath = bud.DebugNavLabel is { } label && label.StartsWith("no path", StringComparison.Ordinal);
		if (!noPath)
		{
			gymNoPathStartTime = -1;
			return false;
		}

		if (gymNoPathStartTime < 0)
		{
			gymNoPathStartTime = Time.Seconds;
			return false;
		}

		var stuck = Time.Seconds - gymNoPathStartTime;
		if (stuck <= GymNoPathTimeout)
			return false;

		Console.WriteLine($"[Gym] no path for {stuck:F1}s, advancing");
		AdvanceGym(success: false);
		return true;
	}

	private void AssignGymTarget(GymRun run)
	{
		if (game == null)
			return;

		game.GymMode = true;
		foreach (var goal in game.Actors.OfType<Goal>())
		{
			if (string.Equals(goal.ActorId, run.GoalId, StringComparison.OrdinalIgnoreCase) ||
				!string.IsNullOrWhiteSpace(run.GoalName) && string.Equals(goal.Name, run.GoalName, StringComparison.OrdinalIgnoreCase))
			{
				game.GymTarget = goal;
				game.NavTrace?.Event(game, "gymTarget", ("target", NavTrace.Vec(goal.Position)));
				return;
			}
		}

		var worldPosition = run.Room.WorldBounds.TopLeft + run.GoalLocalPosition;
		game.GymTarget = game.Actors.OfType<Goal>()
			.OrderBy(goal => Vector2.DistanceSquared(goal.Position, worldPosition))
			.FirstOrDefault();
		if (game.GymTarget is Goal fallbackGoal)
			game.NavTrace?.Event(game, "gymTarget", ("target", NavTrace.Vec(fallbackGoal.Position)));
	}

}
