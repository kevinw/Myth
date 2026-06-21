using System.Text.Json;
using System.Numerics;
using Foster.Framework;

namespace Myth;

// A single gym attempt: reach GoalLocalPosition starting from StartLocalPosition
// (a PlayerStart marker). StartLocalPosition is null when the room has no marker,
// in which case the default player spawn is used.
sealed record GymRun(Room Room, string GoalId, string? GoalName, Point2 GoalLocalPosition, Point2? StartLocalPosition, string? StartLabel)
{
	public string DisplayId => string.IsNullOrWhiteSpace(GoalId) ? GoalName ?? $"{GoalLocalPosition.X},{GoalLocalPosition.Y}" : GoalId;
	public string StartDisplay => StartLabel ?? "default";
}
sealed record GymResult(string Room, string Goal, string Start, double SimTime, bool Success, string Reason);


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

			var goals = new List<(string Id, string? Name, Point2 Position)>();
			var starts = new List<(Point2 Position, string Label)>();
			foreach (var actor in room.Actors)
			{
				var type = ActorSpecs.ReadString(ActorSpecs.Resolve(actor), "Type");
				var position = ActorSpecs.ReadPoint(actor, "Position") ?? Point2.Zero;
				if (string.Equals(type, "Goal", StringComparison.OrdinalIgnoreCase))
				{
					var goalId = ActorSpecs.ReadString(actor, "Id");
					var goalName = ActorSpecs.ReadString(actor, "Name");
					var runGoalId = string.IsNullOrWhiteSpace(goalId) ? $"{position.X},{position.Y}" : goalId;
					goals.Add((runGoalId, goalName, position));
				}
				else if (string.Equals(type, "PlayerStart", StringComparison.OrdinalIgnoreCase))
				{
					starts.Add((position, $"{position.X},{position.Y}"));
				}
			}

			if (goals.Count == 0)
				continue;

			// Try each goal from each PlayerStart marker (so N goals × M starts =
			// N×M runs). A room with no marker falls back to a single run per goal
			// at the default player spawn (null start).
			foreach (var goal in goals)
			{
				if (starts.Count == 0)
					runs.Add(new(room, goal.Id, goal.Name, goal.Position, null, null));
				else
					foreach (var start in starts)
						runs.Add(new(room, goal.Id, goal.Name, goal.Position, start.Position, start.Label));
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

	// World position the player (and thus Bud) should spawn at for the current gym
	// run, taken from the run's PlayerStart marker. Null means use the default spawn.
	Point2? gymStartOverride;

	static float GymSingleGoalTimeout = 30.0f; // TODO: we should probably make this based on the number of nodes from start to end for when we have bigger/mazier rooms and goal paths

	// The looping tune mode (plain --gym, no --gym-cli): the gym cycles the rooms forever so
	// hot-reloaded tuning values get re-read each pass. Logs one compact line per trial and a
	// pass/fail tally per cycle instead of the per-event chatter the one-shot --gym-cli prints.
	bool GymLoop => gymMode && !gym.exitAfterAll;
	int gymCyclePass;
	int gymCycleTotal;

	void GymTick()
	{
		if (gym.runs == null || game == null) return;

		if (game.GymBudDied)
		{
			if (!GymLoop) Console.WriteLine("[Gym] Bud fell off world");
			AdvanceGym(success: false);
		}
		else if (game.GymTarget is Goal goal && game.GetFirst<Bud>() is { } bud && goal.OverlapsAny(Point2.Zero, bud))
		{
			if (!GymLoop) Console.WriteLine("[Gym] Bud reached goal");
			AdvanceGym(success: true);
		}
		else if (Time.Seconds - gym.roomStartTime > GymSingleGoalTimeout)
		{
			if (!GymLoop) Console.WriteLine($"[Gym] timeout ({Time.Seconds - gym.roomStartTime:F1}s), advancing");
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
		var reason = success ? "goal" : game?.GymBudDied == true ? "fell" : "timeout";
		if (gym.exitAfterAll)
		{
			gym.results ??= [];
			gym.results.Add(new GymResult(current.Room.Id, current.DisplayId, current.StartDisplay, simTime, success, reason));
		}
		if (GymLoop)
		{
			gymCycleTotal++;
			if (success) gymCyclePass++;
			Console.WriteLine($"  {(success ? "GOAL" : reason.ToUpperInvariant()),-7} {current.Room.Id}:{current.DisplayId}@{current.StartDisplay} {simTime:0.00}s");
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
			if (GymLoop)
			{
				Console.WriteLine($"[Gym] cycle {gymCyclePass}/{gymCycleTotal} passed");
				gymCyclePass = 0;
				gymCycleTotal = 0;
			}
			gym.runIndex = 0;
		}

		var run = gym.runs[gym.runIndex];
		if (!GymLoop)
			Console.WriteLine($"[Gym] {current.Room.Cell.X},{current.Room.Cell.Y}:{current.DisplayId}@{current.StartDisplay} → {run.Room.Cell.X},{run.Room.Cell.Y}:{run.DisplayId}@{run.StartDisplay}");
		StartGymRun(run);
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

		if (!GymLoop) Console.WriteLine($"[Gym] no path for {stuck:F1}s, advancing");
		AdvanceGym(success: false);
		return true;
	}

	// Begin a gym run: load its room, position the player at the run's PlayerStart
	// marker (if any), and aim Bud at the goal.
	private void StartGymRun(GymRun run)
	{
		var room = run.Room;
		cell = room.Cell;
		selectedRoomId = null;
		gym.roomStartTime = Time.Seconds;
		gymNoPathStartTime = -1;
		gymStartOverride = run.StartLocalPosition is { } startLocal
			? room.WorldBounds.TopLeft + startLocal
			: null;
		StartGame(immediate: true);
		AssignGymTarget(run);
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
