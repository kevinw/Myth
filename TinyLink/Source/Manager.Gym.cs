using System.Text.Json;
using System.Numerics;
using Foster.Framework;

namespace TinyLink;

partial class Manager
{
	readonly bool gymMode;
	readonly bool headless;
	readonly bool unthrottled;
	readonly bool gymExitAfterAll;
	List<GymRun>? gymRuns;
	int gymRunIndex;
	double gymRoomStartTime;
	double nextGymLog;
	List<GymResult>? gymResults;

	private sealed record GymRun(Room Room, string GoalId, Point2 GoalLocalPosition);
	private sealed record GymResult(string Room, string Goal, double SimTime, bool Success, string Reason);

	private void CollectGymRooms()
	{
		gymRuns = [];
		foreach (var room in Assets.Rooms.Values)
		{
			foreach (var actor in room.Actors)
			{
				if (!string.Equals(ActorSpecs.ReadString(ActorSpecs.Resolve(actor), "Type"), "Goal", StringComparison.OrdinalIgnoreCase))
					continue;

				var goalId = ActorSpecs.ReadString(actor, "Id");
				var position = ActorSpecs.ReadPoint(actor, "Position") ?? Point2.Zero;
				gymRuns.Add(new(room, string.IsNullOrWhiteSpace(goalId) ? $"{position.X},{position.Y}" : goalId, position));
			}
		}
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
			gymResults.Add(new GymResult(current.Room.Id, current.GoalId, simTime, success,
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
#endif

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
		Console.WriteLine($"[Gym] {current.Room.Cell.X},{current.Room.Cell.Y}:{current.GoalId} → {room.Cell.X},{room.Cell.Y}:{run.GoalId}");
		gymRoomStartTime = Time.Seconds;
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
			if (string.Equals(goal.ActorId, run.GoalId, StringComparison.OrdinalIgnoreCase))
			{
				game.GymTarget = goal;
				return;
			}
		}

		var worldPosition = run.Room.WorldBounds.TopLeft + run.GoalLocalPosition;
		game.GymTarget = game.Actors.OfType<Goal>()
			.OrderBy(goal => Vector2.DistanceSquared(goal.Position, worldPosition))
			.FirstOrDefault();
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
