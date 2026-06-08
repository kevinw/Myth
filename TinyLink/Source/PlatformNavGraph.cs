using System.Numerics;
using System.Diagnostics;
using Foster.Framework;

namespace TinyLink;

public sealed class PlatformNavGraph
{
	public enum NodeKind
	{
		Ground,
		Landing,
		Ledge,
		RoomExit
	}

	public enum EdgeKind
	{
		Walk,
		Crawl,
		Fall,
		Jump,
		RoomExit
	}

	public enum RoomExitKind
	{
		Edge,
		LinkedDoor
	}

	public readonly record struct RoomExit(RoomExitKind Kind, string? TargetRoomId, string? TargetActorId, Point2 Direction);
	public readonly record struct Node(int Id, Vector2 Position, NodeKind Kind, RoomExit? Exit = null, bool RequiresDucking = false);
	public readonly record struct JumpPlan(int Direction, float RunupTime, float HoldTime, int AirDirection, float SwitchTime);
	public readonly record struct Edge(int From, int To, EdgeKind Kind, JumpPlan? Jump = null);

	private const int SampleStep = Game.TileSize;
	private const float SimDelta = 1.0f / 60.0f;
	private const float SimMaxTime = 1.60f;
	private const float SimAirAccel = 260;
	private const float SimMaxAirSpeed = 99;
	private const float SimGravity = 450;
	private const float SimJumpForce = -105;
	private const float SimJumpTime = 0.18f;
	private const int SimLandingTolerance = 12;
	private const int SimMinimumLandingDistance = Game.TileSize * 2;
	private static readonly RectInt PawnProbe = new(-4, -12, 8, 12);
	private static readonly RectInt DuckingPawnProbe = new(-4, -6, 8, 6);
	private static readonly JumpPlan[] JumpPlans =
	[
		new(1, 0.00f, 0.18f, 1, 99.0f),
		new(-1, 0.00f, 0.18f, -1, 99.0f),
		new(1, 0.08f, 0.18f, 1, 99.0f),
		new(-1, 0.08f, 0.18f, -1, 99.0f),
		new(1, 0.14f, 0.18f, 1, 99.0f),
		new(-1, 0.14f, 0.18f, -1, 99.0f),
		new(1, 0.08f, 0.10f, 1, 99.0f),
		new(-1, 0.08f, 0.10f, -1, 99.0f),
		new(1, 0.08f, 0.18f, -1, 0.32f),
		new(-1, 0.08f, 0.18f, 1, 0.32f),
		new(1, 0.24f, 0.18f, 1, 99.0f),
		new(-1, 0.24f, 0.18f, -1, 99.0f),
		new(1, 0.36f, 0.18f, 1, 99.0f),
		new(-1, 0.36f, 0.18f, -1, 99.0f),
		new(1, 0.48f, 0.18f, 1, 99.0f),
		new(-1, 0.48f, 0.18f, -1, 99.0f),
	];

	public readonly List<Node> Nodes = [];
	public readonly List<Edge> Edges = [];
	public IEnumerable<Node> RoomExits => Nodes.Where(node => node.Kind == NodeKind.RoomExit);

	private readonly Dictionary<Point2, int> nodeLookup = [];
	private readonly PriorityQueue<int, float> pathFrontier = new();
	private readonly Dictionary<int, Edge> pathCameFrom = [];
	private readonly Dictionary<int, float> pathCostSoFar = [];

	public enum BuildPhase { Idle, Sampling, AddLandingNodes, WalkEdges, FallEdges, JumpEdges, RoomExits, Ready }

	private BuildPhase phase = BuildPhase.Idle;
	private Game? rebuildGame;
	private Room? rebuildRoom;
	private RectInt sampleBounds;
	private int sampleX;
	private int sampleY;
	private int nextJumpSourceId;
	private long rebuildStartTimestamp;
	private int rebuildFrameCount;

	public bool IsReady => phase == BuildPhase.Ready;

	public Node? FindNearestNode(Vector2 position, float maxDistance, bool includeRoomExits = false)
	{
		Node? nearest = null;
		var nearestDistanceSquared = maxDistance * maxDistance;
		foreach (var node in Nodes)
		{
			if (!includeRoomExits && node.Kind == NodeKind.RoomExit)
				continue;

			var distanceSquared = Vector2.DistanceSquared(position, node.Position);
			if (distanceSquared >= nearestDistanceSquared)
				continue;

			nearest = node;
			nearestDistanceSquared = distanceSquared;
		}
		return nearest;
	}

	public bool TryFindPath(int startId, int goalId, List<Edge> path)
	{
		path.Clear();
		if (startId == goalId)
			return true;

		pathFrontier.Clear();
		pathCameFrom.Clear();
		pathCostSoFar.Clear();
		pathFrontier.Enqueue(startId, 0);
		pathCameFrom[startId] = default;
		pathCostSoFar[startId] = 0;

		while (pathFrontier.Count > 0)
		{
			var current = pathFrontier.Dequeue();
			if (current == goalId)
			{
				BuildPath(startId, goalId, pathCameFrom, path);
				return true;
			}

			foreach (var edge in Edges)
			{
				if (edge.From != current)
					continue;

				var nextCost = pathCostSoFar[current] + EdgeCost(edge);
				if (pathCostSoFar.TryGetValue(edge.To, out var existingCost) && nextCost >= existingCost)
					continue;

				pathCostSoFar[edge.To] = nextCost;
				pathCameFrom[edge.To] = edge;
				pathFrontier.Enqueue(edge.To, nextCost);
			}
		}

		return false;
	}

	private float EdgeCost(Edge edge)
	{
		var distance = Vector2.Distance(Nodes[edge.From].Position, Nodes[edge.To].Position);
		return edge.Kind switch
		{
			EdgeKind.Walk => distance,
			EdgeKind.Crawl => distance + 12,
			EdgeKind.Fall => distance + 20,
			EdgeKind.Jump => distance + 180 + LandingRiskCost(Nodes[edge.To]),
			EdgeKind.RoomExit => distance + 5,
			_ => distance
		};
	}

	private static float LandingRiskCost(Node node)
		=> node.Kind switch
		{
			NodeKind.Landing => -24,
			NodeKind.Ledge => 48,
			_ => 0
		};

	private static void BuildPath(int startId, int goalId, Dictionary<int, Edge> cameFrom, List<Edge> path)
	{
		var current = goalId;
		while (current != startId)
		{
			var edge = cameFrom[current];
			path.Add(edge);
			current = edge.From;
		}
		path.Reverse();
	}

	public void Rebuild(Game game, Room room)
	{
		Nodes.Clear();
		Edges.Clear();
		nodeLookup.Clear();
		rebuildGame = game;
		rebuildRoom = room;
		sampleBounds = room.WorldBounds;
		sampleX = sampleBounds.Left + SampleStep / 2;
		sampleY = sampleBounds.Top + Math.Abs(DuckingPawnProbe.Top);
		nextJumpSourceId = 0;
		rebuildStartTimestamp = Stopwatch.GetTimestamp();
		rebuildFrameCount = 0;
		phase = BuildPhase.Sampling;
	}

	public void Tick(float budgetMs)
	{
		if (phase == BuildPhase.Ready || phase == BuildPhase.Idle)
			return;

		rebuildFrameCount++;
		var start = Stopwatch.GetTimestamp();

		do
		{
			switch (phase)
			{
				case BuildPhase.Sampling:
					TickSampling();
					break;
				case BuildPhase.AddLandingNodes:
					AddPlatformLandingNodes(rebuildGame!);
					phase = BuildPhase.WalkEdges;
					break;
				case BuildPhase.WalkEdges:
					BuildWalkEdges(rebuildGame!);
					phase = BuildPhase.FallEdges;
					break;
				case BuildPhase.FallEdges:
					BuildFallEdges(rebuildGame!);
					phase = BuildPhase.JumpEdges;
					break;
				case BuildPhase.JumpEdges:
					TickJumpEdges();
					break;
				case BuildPhase.RoomExits:
					BuildRoomExitNodes(rebuildRoom!);
					phase = BuildPhase.Ready;
					Console.WriteLine($"nav graph rebuild {Stopwatch.GetElapsedTime(rebuildStartTimestamp).TotalMilliseconds}ms, {rebuildFrameCount} frames");
					break;
			}
		} while (phase < BuildPhase.Ready && Stopwatch.GetElapsedTime(start).TotalMilliseconds < budgetMs);
	}

	private void TickSampling()
	{
		while (sampleY < sampleBounds.Bottom)
		{
			for (var x = sampleX; x < sampleBounds.Right; x += SampleStep)
			{
				var position = new Point2(x, sampleY);
				if (TryGetStandableProbe(rebuildGame!, position, out var requiresDucking))
					AddNode(position, IsLedge(rebuildGame!, position) ? NodeKind.Ledge : NodeKind.Ground, requiresDucking: requiresDucking);
			}
			sampleX = sampleBounds.Left + SampleStep / 2;
			sampleY++;
			return;
		}
		phase = BuildPhase.AddLandingNodes;
	}

	private void TickJumpEdges()
	{
		if (nextJumpSourceId >= Nodes.Count)
		{
			phase = BuildPhase.RoomExits;
			return;
		}

		for (var batch = 0; batch < 3 && nextJumpSourceId < Nodes.Count; batch++, nextJumpSourceId++)
		{
			var from = Nodes[nextJumpSourceId];
			if (from.RequiresDucking)
				continue;

			var jumps = new Dictionary<int, (JumpPlan Plan, float Cost)>();
			foreach (var plan in JumpPlans)
			{
				if (!TrySimulateJump(rebuildGame!, from, plan, out var landing))
					continue;

				var to = FindNearestLandingNode(from.Id, landing, SimLandingTolerance);
				if (!to.HasValue || HasEdge(from.Id, to.Value.Id))
					continue;

				var cost = JumpPlanCost(from, to.Value, plan);
				if (!jumps.TryGetValue(to.Value.Id, out var existing) || cost < existing.Cost)
					jumps[to.Value.Id] = (plan, cost);
			}

			foreach (var (toId, jump) in jumps)
			{
				if (!HasEdge(from.Id, toId))
					Edges.Add(new(from.Id, toId, EdgeKind.Jump, jump.Plan));
			}
		}

		if (nextJumpSourceId >= Nodes.Count)
			phase = BuildPhase.RoomExits;
	}

	private int AddNode(Point2 position, NodeKind kind, RoomExit? exit = null, bool requiresDucking = false)
	{
		if (nodeLookup.TryGetValue(position, out var existing))
		{
			var node = Nodes[existing];
			if (kind == NodeKind.Landing && node.Kind != NodeKind.RoomExit)
				Nodes[existing] = node with { Kind = NodeKind.Landing };
			return existing;
		}

		var id = Nodes.Count;
		Nodes.Add(new(id, position, kind, exit, requiresDucking));
		nodeLookup[position] = id;
		return id;
	}

	private void AddPlatformLandingNodes(Game game)
	{
		foreach (var row in Nodes
			.Where(node => node.Kind != NodeKind.RoomExit && !node.RequiresDucking)
			.GroupBy(node => (int)node.Position.Y))
		{
			var span = new List<Node>();
			foreach (var node in row.OrderBy(node => node.Position.X))
			{
				if (span.Count > 0 && node.Position.X - span[^1].Position.X > SampleStep)
				{
					AddLandingNodeForSpan(game, span);
					span.Clear();
				}
				span.Add(node);
			}
			AddLandingNodeForSpan(game, span);
		}
	}

	private void AddLandingNodeForSpan(Game game, List<Node> span)
	{
		if (span.Count < 2)
			return;

		var left = span[0];
		var right = span[^1];
		var center = new Point2(
			(int)MathF.Round((left.Position.X + right.Position.X) * 0.5f),
			(int)MathF.Round((left.Position.Y + right.Position.Y) * 0.5f));
		if (!TryGetStandableProbe(game, center, out var requiresDucking) || requiresDucking)
			return;

		AddNode(center, NodeKind.Landing);
	}

	private void BuildRoomExitNodes(Room room)
	{
		var navigationNodes = Nodes.ToArray();
		BuildEdgeExitNodes(room, navigationNodes);
		BuildLinkedDoorExitNodes(room, navigationNodes);
	}

	private void BuildEdgeExitNodes(Room room, Node[] navigationNodes)
	{
		var bounds = room.WorldBounds;
		var directions = new[]
		{
			Point2.Left,
			Point2.Right,
			Point2.Up,
			Point2.Down
		};

		foreach (var from in navigationNodes)
		{
			if (from.Kind == NodeKind.RoomExit)
				continue;

			foreach (var direction in directions)
			{
				if (!IsNearRoomEdge(bounds, from.Position, direction))
					continue;

				if (Assets.FindAdjacentRoom(room, direction, from.Position) is not { } targetRoom)
					continue;

				var exitPosition = RoomEdgeExitPosition(bounds, from.Position, direction);
				var exitId = AddNode(exitPosition, NodeKind.RoomExit, new RoomExit(RoomExitKind.Edge, targetRoom.Id, null, direction));
				Edges.Add(new(from.Id, exitId, EdgeKind.RoomExit));
				Edges.Add(new(exitId, from.Id, EdgeKind.RoomExit));
			}
		}
	}

	private void BuildLinkedDoorExitNodes(Room room, Node[] navigationNodes)
	{
		foreach (var actor in room.Actors)
		{
			var resolved = ActorSpecs.Resolve(actor);
			if (!string.Equals(ActorSpecs.ReadString(resolved, "Type"), "LinkedDoor", StringComparison.OrdinalIgnoreCase))
				continue;

			var targetActorId = ActorSpecs.ReadString(resolved, nameof(LinkedDoor.TargetActorId));
			if (string.IsNullOrWhiteSpace(targetActorId))
				continue;

			var localPosition = ActorSpecs.ReadPoint(resolved, "Position") ?? Point2.Zero;
			var position = room.WorldBounds.TopLeft + localPosition;
			var targetRoomId = Assets.FindActorById(targetActorId) is { } target ? target.Room.Id : null;
			var exitId = AddNode(position, NodeKind.RoomExit, new RoomExit(RoomExitKind.LinkedDoor, targetRoomId, targetActorId, Point2.Zero));
			if (FindNearestNavigationNode(navigationNodes, position, SampleStep * 6) is not { } nearest)
				continue;

			Edges.Add(new(nearest.Id, exitId, EdgeKind.RoomExit));
			Edges.Add(new(exitId, nearest.Id, EdgeKind.RoomExit));
		}
	}

	private static bool IsNearRoomEdge(RectInt bounds, Vector2 position, Point2 direction)
	{
		if (direction.X > 0)
			return position.X >= bounds.Right - SampleStep;
		if (direction.X < 0)
			return position.X <= bounds.Left + SampleStep;
		if (direction.Y > 0)
			return position.Y >= bounds.Bottom - SampleStep * 2;
		if (direction.Y < 0)
			return position.Y <= bounds.Top + SampleStep * 2;

		return false;
	}

	private static Point2 RoomEdgeExitPosition(RectInt bounds, Vector2 position, Point2 direction)
	{
		if (direction.X > 0)
			return new Point2(bounds.Right + 1, (int)position.Y);
		if (direction.X < 0)
			return new Point2(bounds.Left - 1, (int)position.Y);
		if (direction.Y > 0)
			return new Point2((int)position.X, bounds.Bottom + 13);
		if (direction.Y < 0)
			return new Point2((int)position.X, bounds.Top - 1);

		return (Point2)position;
	}

	private static Node? FindNearestNavigationNode(Node[] nodes, Vector2 position, float maxDistance)
	{
		Node? nearest = null;
		var nearestDistanceSquared = maxDistance * maxDistance;
		foreach (var node in nodes)
		{
			if (node.Kind == NodeKind.RoomExit)
				continue;

			var distanceSquared = Vector2.DistanceSquared(node.Position, position);
			if (distanceSquared >= nearestDistanceSquared)
				continue;

			nearest = node;
			nearestDistanceSquared = distanceSquared;
		}
		return nearest;
	}

	private static bool IsStandable(Game game, Point2 position)
	{
		return TryGetStandableProbe(game, position, out _);
	}

	private static bool TryGetStandableProbe(Game game, Point2 position, out bool requiresDucking)
	{
		if (IsStandableWithProbe(game, position, PawnProbe))
		{
			requiresDucking = false;
			return true;
		}

		if (IsStandableWithProbe(game, position, DuckingPawnProbe))
		{
			requiresDucking = true;
			return true;
		}

		requiresDucking = false;
		return false;
	}

	private static bool IsStandableWithProbe(Game game, Point2 position, RectInt probe)
	{
		var bounds = probe + position;
		return !game.OverlapsRoomCollision(bounds) && HasStandableSupport(game, bounds);
	}

	private static bool HasStandableSupport(Game game, RectInt bounds)
	{
		if (game.OverlapsRoomCollision(bounds + Point2.Down))
			return true;

		foreach (var actor in game.Actors)
		{
			if (!actor.Mask.Has(Actor.Masks.Jumpthru))
				continue;

			var local = bounds - actor.Position;
			if (actor.Hitbox.Overlaps(local))
				continue;

			if (actor.Hitbox.Overlaps(local + Point2.Down))
				return true;
		}

		return false;
	}

	private static bool IsLedge(Game game, Point2 position)
	{
		var left = position + new Point2(-SampleStep, 0);
		var right = position + new Point2(SampleStep, 0);
		return !IsStandable(game, left) || !IsStandable(game, right);
	}

	private void BuildWalkEdges(Game game)
	{
		for (var i = 0; i < Nodes.Count; i++)
		{
			var from = Nodes[i];
			if (from.Kind == NodeKind.RoomExit)
				continue;

			for (var j = 0; j < Nodes.Count; j++)
			{
				if (i == j)
					continue;

				var to = Nodes[j];
				if (to.Kind == NodeKind.RoomExit)
					continue;

				var deltaX = MathF.Abs(to.Position.X - from.Position.X);
				if (deltaX <= 0 || deltaX > SampleStep)
					continue;

				var deltaY = MathF.Abs(to.Position.Y - from.Position.Y);
				if (deltaY > SampleStep)
					continue;

				var edgeKind = WalkEdgeKind(game, from, to);
				if (!edgeKind.HasValue || HasEdge(from.Id, to.Id))
					continue;

				Edges.Add(new(from.Id, to.Id, edgeKind.Value));
			}
		}
	}

	private void BuildFallEdges(Game game)
	{
		foreach (var node in Nodes)
		{
			if (node.Kind != NodeKind.Ledge)
				continue;

			if (!IsStandable(game, (Point2)node.Position + new Point2(-SampleStep, 0)) &&
				FindFallLanding(game, node, -SampleStep) is { } leftLanding)
				Edges.Add(new(node.Id, leftLanding.Id, EdgeKind.Fall));

			if (!IsStandable(game, (Point2)node.Position + new Point2(SampleStep, 0)) &&
				FindFallLanding(game, node, SampleStep) is { } rightLanding)
				Edges.Add(new(node.Id, rightLanding.Id, EdgeKind.Fall));
		}
	}

	private static float JumpPlanCost(Node from, Node to, JumpPlan plan)
	{
		var targetDirection = Math.Sign(to.Position.X - from.Position.X);
		var cost = plan.RunupTime * 100 + LandingRiskCost(to);
		if (targetDirection != 0)
		{
			if (plan.Direction != targetDirection)
				cost += 120;
			if (plan.AirDirection != targetDirection)
				cost += 80;
		}
		if (plan.AirDirection != plan.Direction)
			cost += 30;
		return cost;
	}

	private Node? FindNearestLandingNode(int fromId, Vector2 landing, float maxDistance)
	{
		Node? nearest = null;
		var bestScore = maxDistance * maxDistance;
		foreach (var node in Nodes)
		{
			if (node.Id == fromId)
				continue;
			if (node.RequiresDucking)
				continue;
			if (Vector2.DistanceSquared(node.Position, Nodes[fromId].Position) < SimMinimumLandingDistance * SimMinimumLandingDistance)
				continue;

			var distanceSquared = Vector2.DistanceSquared(node.Position, landing);
			if (distanceSquared > maxDistance * maxDistance)
				continue;

			var score = distanceSquared + node.Kind switch
			{
				NodeKind.Landing => -36,
				NodeKind.Ledge => 64,
				_ => 0
			};
			if (score >= bestScore)
				continue;
			nearest = node;
			bestScore = score;
		}
		return nearest;
	}

	private (Node Node, EdgeKind Kind)? FindWalkNeighbor(Game game, Node from, int offsetX)
	{
		Node? best = null;
		EdgeKind bestKind = EdgeKind.Walk;
		var bestDeltaY = int.MaxValue;
		var targetX = (int)from.Position.X + offsetX;
		foreach (var to in Nodes)
		{
			if ((int)to.Position.X != targetX)
				continue;

			var deltaY = Math.Abs((int)(to.Position.Y - from.Position.Y));
			if (deltaY > SampleStep || deltaY >= bestDeltaY)
				continue;

			var edgeKind = WalkEdgeKind(game, from, to);
			if (!edgeKind.HasValue)
				continue;

			best = to;
			bestKind = edgeKind.Value;
			bestDeltaY = deltaY;
		}
		return best.HasValue ? (best.Value, bestKind) : null;
	}

	private bool HasEdge(int from, int to)
		=> Edges.Any(edge => edge.From == from && edge.To == to);

	private Node? FindFallLanding(Game game, Node from, int offsetX)
	{
		Node? best = null;
		var start = (Point2)from.Position + new Point2(offsetX, 0);
		foreach (var to in Nodes)
		{
			if ((int)to.Position.X != start.X || to.Position.Y <= from.Position.Y)
				continue;

			if (best is { } current && to.Position.Y >= current.Position.Y)
				continue;

			if (!FallProbeClear(game, start, (Point2)to.Position))
				continue;

			best = to;
		}
		return best;
	}

	private static EdgeKind? WalkEdgeKind(Game game, Node from, Node to)
	{
		if (!from.RequiresDucking && !to.RequiresDucking && WalkProbeClear(game, from.Position, to.Position, PawnProbe))
			return EdgeKind.Walk;
		if (WalkProbeClear(game, from.Position, to.Position, DuckingPawnProbe))
			return EdgeKind.Crawl;
		return null;
	}

	private static bool WalkProbeClear(Game game, Vector2 from, Vector2 to, RectInt probe)
	{
		var steps = Math.Max(1, (int)MathF.Ceiling(Vector2.Distance(from, to) / 2.0f));
		for (var i = 1; i <= steps; i++)
		{
			var t = i / (float)steps;
			var position = (Point2)Vector2.Lerp(from, to, t);
			if (game.OverlapsRoomCollision(probe + position))
				return false;
		}
		return true;
	}

	private static bool FallProbeClear(Game game, Point2 from, Point2 to)
	{
		if (game.OverlapsRoomCollision(PawnProbe + from))
			return false;

		for (var y = from.Y + 1; y < to.Y; y += 2)
			if (game.OverlapsRoomCollision(PawnProbe + new Point2(from.X, y)))
				return false;

		return true;
	}

	private static bool TrySimulateJump(Game game, Node from, JumpPlan plan, out Vector2 landing)
	{
		var sim = new JumpSimulation(game, from.Position, plan);
		return sim.Run(out landing);
	}

	private sealed class JumpSimulation
	{
		private readonly Game game;
		private readonly Vector2 from;
		private readonly JumpPlan plan;
		private Vector2 position;
		private Vector2 velocity;
		private Vector2 remainder;
		private float jumpTimer;
		private bool leftStart;
		private bool jumpStarted;
		private int horizontalCollisionFrames;

		public JumpSimulation(Game game, Vector2 from, JumpPlan plan)
		{
			this.game = game;
			this.from = from;
			this.plan = plan;
			position = from;
		}

		public bool Run(out Vector2 landing)
		{
			for (var time = 0.0f; time < SimMaxTime; time += SimDelta)
			{
				Step(time);

				if (Vector2.DistanceSquared(position, from) > 9)
					leftStart = true;

				if (leftStart && IsGrounded())
				{
					landing = position;
					return true;
				}

				if (horizontalCollisionFrames > 20 && velocity.Y < 0)
				{
					landing = default;
					return false;
				}
			}

			landing = default;
			return false;
		}

		private void Step(float time)
		{
			var moveX = MoveXAt(time);
			velocity.X += moveX * SimAirAccel * SimDelta;
			if (MathF.Abs(velocity.X) > SimMaxAirSpeed)
				velocity.X = Calc.Approach(velocity.X, MathF.Sign(velocity.X) * SimMaxAirSpeed, 2000 * SimDelta);

			var shouldJump = time >= plan.RunupTime || moveX != 0 && !HasGroundSupportNextStep(moveX);
			if (!jumpStarted && shouldJump && velocity.Y >= 0 && IsGrounded())
			{
				jumpTimer = MathF.Min(plan.HoldTime, SimJumpTime);
				jumpStarted = true;
			}

			if (jumpTimer > 0)
			{
				velocity.Y = SimJumpForce;
				jumpTimer -= SimDelta;
			}

			var grav = SimGravity;
			if (MathF.Abs(velocity.Y) < 20 && jumpTimer > 0)
				grav *= 0.40f;
			velocity.Y += grav * SimDelta;

			Move(velocity * SimDelta);
		}

		private int MoveXAt(float time)
		{
			if (time < plan.RunupTime)
				return plan.Direction;

			if (time >= plan.SwitchTime)
				return plan.AirDirection;

			return plan.Direction;
		}

		private void Move(Vector2 value)
		{
			remainder += value;
			var move = (Point2)remainder;
			remainder -= move;

			while (move.X != 0)
			{
				var sign = Math.Sign(move.X);
				if (Overlaps(position + new Vector2(sign, 0)))
				{
					velocity.X = 0;
					remainder.X = 0;
					horizontalCollisionFrames++;
					break;
				}

				position.X += sign;
				move.X -= sign;
			}

			while (move.Y != 0)
			{
				var sign = Math.Sign(move.Y);
				if (Overlaps(position + new Vector2(0, sign)))
				{
					velocity.Y = 0;
					remainder.Y = 0;
					break;
				}

				position.Y += sign;
				move.Y -= sign;
			}
		}

		private bool IsGrounded()
			=> HasStandableSupport(game, PawnProbe + (Point2)position);

		private bool HasGroundSupportNextStep(int moveX)
			=> HasStandableSupport(game, PawnProbe + (Point2)position + new Point2(Math.Sign(moveX), 0));

		private bool Overlaps(Vector2 at)
			=> game.OverlapsRoomCollision(PawnProbe + (Point2)at);
	}
}
