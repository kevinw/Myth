using System.Text.Json;
using System.Numerics;
using Foster.Framework;

namespace Myth;

partial class Manager
{
	readonly bool gymMode;
	readonly bool headless;
	readonly bool unthrottled;
	// Unthrottled runs have no fixed wall-clock per frame, so a time-budgeted nav-graph
	// rebuild finishes on a different game frame each run (non-deterministic gym results).
	// Callers use this to build the graph in one shot instead.
	internal bool Unthrottled => unthrottled;
	readonly bool gymExitAfterAll;
	readonly HashSet<string> gymGoalIds = new(StringComparer.OrdinalIgnoreCase);
	List<GymRun>? gymRuns;
	int gymRunIndex;
	double gymRoomStartTime;
	double nextGymLog;
	List<GymResult>? gymResults;

	private sealed record GymRun(Room Room, string GoalId, string? GoalName, Point2 GoalLocalPosition)
	{
		public string DisplayId => string.IsNullOrWhiteSpace(GoalId) ? GoalName ?? $"{GoalLocalPosition.X},{GoalLocalPosition.Y}" : GoalId;
	}
	private sealed record GymResult(string Room, string Goal, double SimTime, bool Success, string Reason);

	private void CollectGymRooms()
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

		gymRuns = gymGoalIds.Count > 0 ? FilterGymRuns(runs) : runs;
	}

	private List<GymRun> FilterGymRuns(List<GymRun> runs)
	{
		var selected = new List<GymRun>();
		var missing = new List<string>();
		foreach (var selector in gymGoalIds)
		{
			var matches = runs
				.Where(run => string.Equals(run.GoalId, selector, StringComparison.OrdinalIgnoreCase))
				.ToList();
			if (matches.Count <= 0)
			{
				matches = runs
					.Where(run => !string.IsNullOrWhiteSpace(run.GoalName) &&
						string.Equals(run.GoalName, selector, StringComparison.OrdinalIgnoreCase))
					.ToList();
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

	private void AdvanceGym(bool success = false)
	{
		if (gymRuns is not { Count: > 0 })
			return;

		var current = gymRuns[gymRunIndex];
		var simTime = Time.Seconds - gymRoomStartTime;
		if (gymExitAfterAll)
		{
			gymResults ??= [];
			gymResults.Add(new GymResult(current.Room.Id, current.DisplayId, simTime, success,
				success ? "goal" : game?.GymBudDied == true ? "fell" : "timeout"));
		}

#if BROWSER
		if (gymExitAfterAll)
		{
			PrintGymResults();
			Exit();
		}
		else
			Console.Error.WriteLine("[Gym] room transition not supported in browser");
		return;
#else
		gymRunIndex++;
		if (gymRunIndex >= gymRuns.Count)
		{
			if (gymExitAfterAll)
			{
				PrintGymResults();
				Exit();
				return;
			}
			gymRunIndex = 0;
		}

		var run = gymRuns[gymRunIndex];
		var room = run.Room;
		cell = room.Cell;
		selectedRoomId = null;
		Console.WriteLine($"[Gym] {current.Room.Cell.X},{current.Room.Cell.Y}:{current.DisplayId} → {room.Cell.X},{room.Cell.Y}:{run.DisplayId}");
		gymRoomStartTime = Time.Seconds;
		StartGame(immediate: true);
		AssignGymTarget(run);
#endif
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

	private void PrintGymResults()
	{
		if (gymResults is not { Count: > 0 })
			return;

		int successCount = 0;
		double totalTime = 0;
		foreach (var r in gymResults)
		{
			if (r.Success) successCount++;
			totalTime += r.SimTime;
			Console.WriteLine(JsonSerializer.Serialize(r));
		}
		Console.WriteLine(JsonSerializer.Serialize(new
		{
			summary = new
			{
				rooms = gymResults.Count,
				success = successCount,
				fail = gymResults.Count - successCount,
				totalTime
			}
		}));
	}
}
