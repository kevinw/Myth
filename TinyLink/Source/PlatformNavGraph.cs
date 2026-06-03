using System.Numerics;
using Foster.Framework;

namespace TinyLink;

public sealed class PlatformNavGraph
{
	public enum NodeKind
	{
		Ground,
		Ledge,
		RoomExit
	}

	public enum EdgeKind
	{
		Walk,
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
	public readonly record struct Node(int Id, Vector2 Position, NodeKind Kind, RoomExit? Exit = null);
	public readonly record struct Edge(int From, int To, EdgeKind Kind);

	private const int SampleStep = Game.TileSize;
	private const float SimDelta = 1.0f / 60.0f;
	private const float SimMaxTime = 1.15f;
	private const float SimAirAccel = 260;
	private const float SimMaxAirSpeed = 99;
	private const float SimGravity = 450;
	private const float SimJumpForce = -105;
	private const float SimJumpTime = 0.18f;
	private const int SimLandingTolerance = 12;
	private const int SimMinimumLandingDistance = Game.TileSize * 2;
	private static readonly RectInt PawnProbe = new(-4, -12, 8, 12);
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
	];

	public readonly List<Node> Nodes = [];
	public readonly List<Edge> Edges = [];
	public IEnumerable<Node> RoomExits => Nodes.Where(node => node.Kind == NodeKind.RoomExit);

	private readonly Dictionary<Point2, int> nodeLookup = [];

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

		var frontier = new PriorityQueue<int, float>();
		var cameFrom = new Dictionary<int, Edge>();
		var costSoFar = new Dictionary<int, float>();
		frontier.Enqueue(startId, 0);
		cameFrom[startId] = default;
		costSoFar[startId] = 0;

		while (frontier.Count > 0)
		{
			var current = frontier.Dequeue();
			if (current == goalId)
			{
				BuildPath(startId, goalId, cameFrom, path);
				return true;
			}

			foreach (var edge in Edges)
			{
				if (edge.From != current)
					continue;

				var nextCost = costSoFar[current] + EdgeCost(edge);
				if (costSoFar.TryGetValue(edge.To, out var existingCost) && nextCost >= existingCost)
					continue;

				costSoFar[edge.To] = nextCost;
				cameFrom[edge.To] = edge;
				frontier.Enqueue(edge.To, nextCost);
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
			EdgeKind.Fall => distance + 20,
			EdgeKind.Jump => distance + 180,
			EdgeKind.RoomExit => distance + 5,
			_ => distance
		};
	}

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

		var bounds = room.WorldBounds;
		for (var x = bounds.Left + SampleStep / 2; x < bounds.Right; x += SampleStep)
		{
			for (var y = bounds.Top + Math.Abs(PawnProbe.Top); y < bounds.Bottom; y++)
			{
				var position = new Point2(x, y);
				if (!IsStandable(game, position))
					continue;

				AddNode(position, IsLedge(game, position) ? NodeKind.Ledge : NodeKind.Ground);
			}
		}

		BuildWalkEdges(game);
		BuildFallEdges(game);
		DiscoverJumpEdges(game);
		BuildRoomExitNodes(room);
	}

	private int AddNode(Point2 position, NodeKind kind, RoomExit? exit = null)
	{
		var id = Nodes.Count;
		Nodes.Add(new(id, position, kind, exit));
		nodeLookup[position] = id;
		return id;
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
		var bounds = PawnProbe + position;
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
		foreach (var node in Nodes)
		{
			if (FindWalkNeighbor(game, node, -SampleStep) is { } left)
				Edges.Add(new(node.Id, left.Id, EdgeKind.Walk));
			if (FindWalkNeighbor(game, node, SampleStep) is { } right)
				Edges.Add(new(node.Id, right.Id, EdgeKind.Walk));
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

	private void DiscoverJumpEdges(Game game)
	{
		foreach (var from in Nodes)
		{
			foreach (var plan in JumpPlans)
			{
				if (!TrySimulateJump(game, from, plan, out var landing))
					continue;

				var to = FindNearestLandingNode(from.Id, landing, SimLandingTolerance);
				if (!to.HasValue || HasEdge(from.Id, to.Value.Id))
					continue;

				Edges.Add(new(from.Id, to.Value.Id, EdgeKind.Jump));
			}
		}
	}

	private Node? FindNearestLandingNode(int fromId, Vector2 landing, float maxDistance)
	{
		Node? nearest = null;
		var nearestDistanceSquared = maxDistance * maxDistance;
		foreach (var node in Nodes)
		{
			if (node.Id == fromId)
				continue;
			if (Vector2.DistanceSquared(node.Position, Nodes[fromId].Position) < SimMinimumLandingDistance * SimMinimumLandingDistance)
				continue;

			var distanceSquared = Vector2.DistanceSquared(node.Position, landing);
			if (distanceSquared >= nearestDistanceSquared)
				continue;

			nearest = node;
			nearestDistanceSquared = distanceSquared;
		}
		return nearest;
	}

	private Node? FindWalkNeighbor(Game game, Node from, int offsetX)
	{
		Node? best = null;
		var bestDeltaY = int.MaxValue;
		var targetX = (int)from.Position.X + offsetX;
		foreach (var to in Nodes)
		{
			if ((int)to.Position.X != targetX)
				continue;

			var deltaY = Math.Abs((int)(to.Position.Y - from.Position.Y));
			if (deltaY > SampleStep || deltaY >= bestDeltaY)
				continue;

			if (!WalkProbeClear(game, from.Position, to.Position))
				continue;

			best = to;
			bestDeltaY = deltaY;
		}
		return best;
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

	private static bool WalkProbeClear(Game game, Vector2 from, Vector2 to)
	{
		var steps = Math.Max(1, (int)MathF.Ceiling(Vector2.Distance(from, to) / 2.0f));
		for (var i = 1; i <= steps; i++)
		{
			var t = i / (float)steps;
			var position = (Point2)Vector2.Lerp(from, to, t);
			if (game.OverlapsRoomCollision(PawnProbe + position))
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

	private readonly record struct JumpPlan(int Direction, float RunupTime, float HoldTime, int AirDirection, float SwitchTime);

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

			if (!jumpStarted && time >= plan.RunupTime && velocity.Y >= 0 && IsGrounded())
			{
				jumpTimer = MathF.Min(plan.HoldTime, SimJumpTime);
				jumpStarted = true;
			}

			if (jumpTimer > 0 && time >= plan.RunupTime)
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

		private bool Overlaps(Vector2 at)
			=> game.OverlapsRoomCollision(PawnProbe + (Point2)at);
	}
}
