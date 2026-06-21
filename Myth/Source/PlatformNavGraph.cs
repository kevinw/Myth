using System.Numerics;
using System.Diagnostics;
using Foster.Framework;

namespace Myth;

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
	public readonly record struct SurfaceSpan(int Id, int Y, int Left, int Right, bool RequiresDucking, int NodeCount);
	public readonly record struct SurfacePoint(int SpanId, Vector2 Position, float DistanceSquared);
	public readonly record struct SurfaceEdge(int From, int To, EdgeKind Kind, int FromLeft, int FromRight, int ToLeft, int ToRight, JumpPlan? Jump = null, int NodeEdgeCount = 0);
	// WallJump: if set, the jump is taken toward Direction, and the first time the
	// pawn hits a wall mid-air it kicks off it (away from the wall) with a fresh jump,
	// then steers in AirDirection. Lets the planner route through a wall kick where a
	// single jump can't make the gap/height (e.g. under a low ceiling).
	public readonly record struct JumpPlan(int Direction, float RunupTime, float HoldTime, int AirDirection, float SwitchTime, bool WallJump = false, int MaxWallKicks = 1);
	public readonly record struct Edge(int From, int To, EdgeKind Kind, JumpPlan? Jump = null);

	const int SampleStep = Game.TileSize;
	const float SimDelta = 1.0f / 60.0f;
	const float SimMaxTime = 1.60f;
	const float SimChainMaxTime = 4.00f;
	const int SimLandingTolerance = 12;
	const int SimMinimumLandingDistance = Game.TileSize * 2;
	static readonly RectInt PawnProbe = new(-4, -12, 8, 12);
	static readonly RectInt DuckingPawnProbe = new(-4, -6, 8, 6);
	static readonly Point2[] RoomExitDirections = [Point2.Left, Point2.Right, Point2.Up, Point2.Down];
	static readonly Comparison<Node> CompareNodeX = static (a, b) => a.Position.X.CompareTo(b.Position.X);
	static readonly JumpPlan[] JumpPlans =
	[
		new(1, 0.00f, 0.18f, 1, 99.0f),
		new(-1, 0.00f, 0.18f, -1, 99.0f),
		// Pure-vertical hops (no horizontal input): the only way to land on a narrow
		// platform directly overhead. A sideways jump arcs off the platform before it
		// can descend onto it, so taller (≥24px) straight-up gaps in a jumpthru stack
		// need these. Mirrors the real player move of just pressing jump with no steer.
		new(0, 0.00f, 0.20f, 0, 99.0f),
		new(0, 0.00f, 0.18f, 0, 99.0f),
		new(1, 0.00f, 0.20f, 1, 99.0f),
		new(-1, 0.00f, 0.20f, -1, 99.0f),
		new(1, 0.00f, 0.055f, 1, 99.0f),
		new(-1, 0.00f, 0.055f, -1, 99.0f),
		new(1, 0.00f, 0.10f, 1, 99.0f),
		new(-1, 0.00f, 0.10f, -1, 99.0f),
		new(1, 0.08f, 0.18f, 1, 99.0f),
		new(-1, 0.08f, 0.18f, -1, 99.0f),
		new(1, 0.08f, 0.20f, 1, 99.0f),
		new(-1, 0.08f, 0.20f, -1, 99.0f),
		new(1, 0.14f, 0.18f, 1, 99.0f),
		new(-1, 0.14f, 0.18f, -1, 99.0f),
		new(1, 0.08f, 0.10f, 1, 99.0f),
		new(-1, 0.08f, 0.10f, -1, 99.0f),
		new(1, 0.08f, 0.18f, -1, 0.32f),
		new(-1, 0.08f, 0.18f, 1, 0.32f),
		new(1, 0.24f, 0.18f, 1, 99.0f),
		new(-1, 0.24f, 0.18f, -1, 99.0f),
		// Wall kicks: jump toward a wall, kick off it, then try both post-kick
		// steering directions. Some ledges need Bud to keep pressing into the landing
		// after the forced kick-away input lock expires.
		new(1, 0.08f, 0.18f, -1, 99.0f, WallJump: true),
		new(-1, 0.08f, 0.18f, 1, 99.0f, WallJump: true),
		new(1, 0.08f, 0.18f, 1, 99.0f, WallJump: true),
		new(-1, 0.08f, 0.18f, -1, 99.0f, WallJump: true),
		// Chained wall climbs: AirDirection 0 means steer away from the wall Bud
		// just kicked off, carrying him toward the opposite wall for the next kick.
		new(1, 0.08f, 0.18f, 0, 99.0f, WallJump: true, MaxWallKicks: 4),
		new(-1, 0.08f, 0.18f, 0, 99.0f, WallJump: true, MaxWallKicks: 4),
		new(1, 0.08f, 0.18f, 0, 99.0f, WallJump: true, MaxWallKicks: 5),
		new(-1, 0.08f, 0.18f, 0, 99.0f, WallJump: true, MaxWallKicks: 5),
		new(1, 0.08f, 0.18f, 0, 99.0f, WallJump: true, MaxWallKicks: 6),
		new(-1, 0.08f, 0.18f, 0, 99.0f, WallJump: true, MaxWallKicks: 6),
	];

	public readonly List<Node> Nodes = [];
	public readonly List<Edge> Edges = [];
	public readonly List<SurfaceSpan> SurfaceSpans = [];
	public readonly List<SurfaceEdge> SurfaceEdges = [];
	public IEnumerable<Node> RoomExits
	{
		get
		{
			foreach (var node in Nodes)
				if (node.Kind == NodeKind.RoomExit)
					yield return node;
		}
	}

	readonly Dictionary<Point2, int> nodeLookup = [];
	readonly HashSet<(int From, int To)> edgeLookup = [];
	readonly Dictionary<int, List<Node>> landingRows = [];
	readonly Dictionary<int, List<int>> landingCandidateRows = [];
	readonly Dictionary<int, List<int>> surfaceRows = [];
	readonly List<int> nodeSurfaceSpanIds = [];
	readonly Dictionary<SurfaceEdgeKey, SurfaceEdgeAccumulator> surfaceEdgeLookup = [];
	readonly Dictionary<int, (JumpPlan Plan, float Cost, int PlanIndex)> jumpCandidates = [];
	readonly PriorityQueue<int, float> pathFrontier = new();
	readonly Dictionary<int, Edge> pathCameFrom = [];
	readonly Dictionary<int, float> pathCostSoFar = [];
	readonly PriorityQueue<int, float> surfacePathFrontier = new();
	readonly Dictionary<int, SurfaceEdge> surfacePathCameFrom = [];
	readonly Dictionary<int, float> surfacePathCostSoFar = [];

	public enum BuildPhase { Idle, Sampling, AddLandingNodes, WalkEdges, FallEdges, JumpEdges, RoomExits, Ready }

	BuildPhase phase = BuildPhase.Idle;
	Game? rebuildGame;
	Room? rebuildRoom;
	RectInt sampleBounds;
	int sampleX;
	int sampleY;
	int nextJumpSourceId;
	long rebuildStartTimestamp;
	int rebuildFrameCount;

	// jumpthru actors in the room, snapshotted once per Tick so HasStandableSupport
	// doesn't re-scan (and mask-filter) every actor on each of its ~100k calls.
	static readonly List<Actor> jumpthruActors = [];

	// --- profiling instrumentation (temporary) ---
	static long collisionQueries;
	static long supportChecks;
	static long simsRun, simSteps, simLanded, simStalled, simTimedOut, simOutOfBounds, landingSearchIterations;
	static long jumpSimTicks, landingSearchTicks;

	// per-plan coverage: where each JumpPlan lands and how often it wins an edge,
	// so we can see which plans own unique reach vs. overlap before trimming.
	struct PlanStat { public int Landed, Kept; public float MinReach, MaxReach, SumReach, MinDy, MaxDy; }
	static readonly PlanStat[] planStats = new PlanStat[JumpPlans.Length];
	readonly double[] phaseElapsedMs = new double[(int)BuildPhase.Ready + 1];
	readonly long[] phaseCollisionCounts = new long[(int)BuildPhase.Ready + 1];

	readonly record struct SurfaceEdgeKey(int From, int To, EdgeKind Kind, bool HasJump, JumpPlan Jump);

	struct SurfaceEdgeAccumulator
	{
		public int FromLeft, FromRight, ToLeft, ToRight, Count;

		public SurfaceEdgeAccumulator(Node from, Node to)
		{
			FromLeft = FromRight = (int)from.Position.X;
			ToLeft = ToRight = (int)to.Position.X;
			Count = 1;
		}

		public void Add(Node from, Node to)
		{
			var fromX = (int)from.Position.X;
			var toX = (int)to.Position.X;
			if (fromX < FromLeft) FromLeft = fromX;
			if (fromX > FromRight) FromRight = fromX;
			if (toX < ToLeft) ToLeft = toX;
			if (toX > ToRight) ToRight = toX;
			Count++;
		}
	}

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

	public void FindNearestNodes(Vector2 position, float maxDistance, List<Node> results, bool includeRoomExits = false)
	{
		results.Clear();
		var maxDistanceSquared = maxDistance * maxDistance;
		foreach (var node in Nodes)
		{
			if (!includeRoomExits && node.Kind == NodeKind.RoomExit)
				continue;

			if (Vector2.DistanceSquared(position, node.Position) >= maxDistanceSquared)
				continue;

			results.Add(node);
		}
		results.Sort((a, b) => Vector2.DistanceSquared(position, a.Position).CompareTo(Vector2.DistanceSquared(position, b.Position)));
	}

	public SurfacePoint? FindNearestSurface(Vector2 position, float maxDistance)
	{
		SurfacePoint? nearest = null;
		var nearestDistanceSquared = maxDistance * maxDistance;
		foreach (var span in SurfaceSpans)
		{
			var x = Math.Clamp(position.X, span.Left, span.Right);
			var point = new Vector2(x, span.Y);
			var distanceSquared = Vector2.DistanceSquared(position, point);
			if (distanceSquared >= nearestDistanceSquared)
				continue;

			nearest = new SurfacePoint(span.Id, point, distanceSquared);
			nearestDistanceSquared = distanceSquared;
		}
		return nearest;
	}

	// The surface span a node sits on (its platform extent), for sampling a random pixel
	// along it. False for nodes not mapped to a span (e.g. RoomExits).
	public bool TryGetNodeSurfaceSpan(int nodeId, out SurfaceSpan span)
	{
		var id = NodeSurfaceSpanId(nodeId);
		if (id < 0)
		{
			span = default;
			return false;
		}
		span = SurfaceSpans[id];
		return true;
	}

	// True if `position` sits on the same surface span as `nodeId`. A jump succeeds when
	// Bud lands anywhere on the target's platform, not just within a few px of the node
	// point — landing 10px along a wide platform still reaches it.
	public bool IsPositionOnNodeSurface(Vector2 position, int nodeId, float maxDistance = Game.TileSize * 1.5f)
	{
		var span = NodeSurfaceSpanId(nodeId);
		if (span < 0)
			return false;
		var surface = FindNearestSurface(position, maxDistance);
		return surface.HasValue && surface.Value.SpanId == span;
	}

	public bool TryFindPath(int startId, int goalId, List<Edge> path, IReadOnlySet<(int From, int To)>? blockedEdges = null)
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
				if (blockedEdges != null && blockedEdges.Contains((edge.From, edge.To)))
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

	public bool TryFindSurfacePath(int startNodeId, int goalNodeId, List<SurfaceEdge> path)
	{
		path.Clear();
		var startId = NodeSurfaceSpanId(startNodeId);
		var goalId = NodeSurfaceSpanId(goalNodeId);
		if (startId < 0 || goalId < 0)
			return false;
		if (startId == goalId)
			return true;

		surfacePathFrontier.Clear();
		surfacePathCameFrom.Clear();
		surfacePathCostSoFar.Clear();
		surfacePathFrontier.Enqueue(startId, 0);
		surfacePathCameFrom[startId] = default;
		surfacePathCostSoFar[startId] = 0;

		while (surfacePathFrontier.Count > 0)
		{
			var current = surfacePathFrontier.Dequeue();
			if (current == goalId)
			{
				BuildSurfacePath(startId, goalId, surfacePathCameFrom, path);
				return true;
			}

			foreach (var edge in SurfaceEdges)
			{
				if (edge.From != current)
					continue;

				var nextCost = surfacePathCostSoFar[current] + SurfaceEdgeCost(edge);
				if (surfacePathCostSoFar.TryGetValue(edge.To, out var existingCost) && nextCost >= existingCost)
					continue;

				surfacePathCostSoFar[edge.To] = nextCost;
				surfacePathCameFrom[edge.To] = edge;
				surfacePathFrontier.Enqueue(edge.To, nextCost);
			}
		}

		return false;
	}

	float EdgeCost(Edge edge)
	{
		var distance = Vector2.Distance(Nodes[edge.From].Position, Nodes[edge.To].Position);
		// A wall kick is a delicate two-part move the runtime executes far less reliably
		// than a plain jump. Make A* route around it unless it is the only connection
		// (e.g. a 32px climb no single jump can reach).
		var wallJumpSurcharge = edge.Jump is { WallJump: true } ? 400 : 0;
		var chainWallJumpSurcharge = edge.Jump is { MaxWallKicks: > 1 } plan ? 1800 + plan.MaxWallKicks * 250 : 0;
		var maxHoldSurcharge = edge.Jump is { HoldTime: >= 0.20f } ? 90 : 0;
		return edge.Kind switch
		{
			EdgeKind.Walk => distance,
			EdgeKind.Crawl => distance + 12,
			EdgeKind.Fall => distance + 20,
			EdgeKind.Jump => distance + 180 + wallJumpSurcharge + chainWallJumpSurcharge + maxHoldSurcharge + LandingRiskCost(Nodes[edge.To]),
			EdgeKind.RoomExit => distance + 5,
			_ => distance
		};
	}

	float SurfaceEdgeCost(SurfaceEdge edge)
	{
		var from = SurfaceSpans[edge.From];
		var to = SurfaceSpans[edge.To];
		var fromX = (edge.FromLeft + edge.FromRight) * 0.5f;
		var toX = (edge.ToLeft + edge.ToRight) * 0.5f;
		var distance = Vector2.Distance(new Vector2(fromX, from.Y), new Vector2(toX, to.Y));
		return edge.Kind switch
		{
			EdgeKind.Walk => MathF.Max(1, distance),
			EdgeKind.Crawl => MathF.Max(1, distance) + 12,
			EdgeKind.Fall => distance + 20,
			EdgeKind.Jump => distance + 180,
			EdgeKind.RoomExit => distance + 5,
			_ => distance
		};
	}

	static float LandingRiskCost(Node node)
		=> node.Kind switch
		{
			NodeKind.Landing => -24,
			NodeKind.Ledge => 48,
			_ => 0
		};

	static void BuildPath(int startId, int goalId, Dictionary<int, Edge> cameFrom, List<Edge> path)
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

	static void BuildSurfacePath(int startId, int goalId, Dictionary<int, SurfaceEdge> cameFrom, List<SurfaceEdge> path)
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
		SurfaceSpans.Clear();
		SurfaceEdges.Clear();
		nodeLookup.Clear();
		edgeLookup.Clear();
		surfaceEdgeLookup.Clear();
		nodeSurfaceSpanIds.Clear();
		rebuildGame = game;
		rebuildRoom = room;
		sampleBounds = room.WorldBounds;
		sampleX = sampleBounds.Left + SampleStep / 2;
		sampleY = sampleBounds.Top + Math.Abs(DuckingPawnProbe.Top);
		nextJumpSourceId = 0;
		rebuildStartTimestamp = Stopwatch.GetTimestamp();
		rebuildFrameCount = 0;
		collisionQueries = 0;
		supportChecks = 0;
		simsRun = simSteps = simLanded = simStalled = simTimedOut = simOutOfBounds = landingSearchIterations = 0;
		jumpSimTicks = landingSearchTicks = 0;
		for (var i = 0; i < planStats.Length; i++)
			planStats[i] = new PlanStat { MinReach = float.MaxValue, MaxReach = float.MinValue, MinDy = float.MaxValue, MaxDy = float.MinValue };
		Array.Clear(phaseElapsedMs);
		Array.Clear(phaseCollisionCounts);
		phase = BuildPhase.Sampling;
	}

	public void Tick(float budgetMs)
	{
		if (phase == BuildPhase.Ready || phase == BuildPhase.Idle)
			return;

		rebuildFrameCount++;
		var start = Stopwatch.GetTimestamp();

		// refresh the jumpthru-actor snapshot for this frame (actors are static
		// within a frame, so this matches reading game.Actors live per call)
		jumpthruActors.Clear();
		foreach (var actor in rebuildGame!.Actors)
			if (actor.Mask.Has(Actor.Masks.Jumpthru))
				jumpthruActors.Add(actor);

		do
		{
			var active = phase;
			var iterStart = Stopwatch.GetTimestamp();
			var collisionsBefore = collisionQueries;

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
					break;
			}

			phaseElapsedMs[(int)active] += Stopwatch.GetElapsedTime(iterStart).TotalMilliseconds;
			phaseCollisionCounts[(int)active] += collisionQueries - collisionsBefore;
		} while (phase < BuildPhase.Ready && Stopwatch.GetElapsedTime(start).TotalMilliseconds < budgetMs);

		if (phase == BuildPhase.Ready)
		{
			BuildSurfaceEdges();
			LogRebuildStats();
		}
	}

	void LogRebuildStats()
	{
		var totalMs = Stopwatch.GetElapsedTime(rebuildStartTimestamp).TotalMilliseconds;
		var rects = rebuildRoom?.CollisionRects.Count ?? 0;
		var slopes = rebuildRoom?.SlopeTiles.Count ?? 0;
		var actors = rebuildGame?.Actors.Count ?? 0;
		Console.WriteLine($"[NavGraph] rebuild {totalMs:0.0}ms over {rebuildFrameCount} frames, {Nodes.Count} nodes, {Edges.Count} edges, {collisionQueries} collisions, {supportChecks} support checks (room: {rects} rects, {slopes} slopes, {actors} actors)");
		for (var i = 0; i <= (int)BuildPhase.Ready; i++)
			if (phaseElapsedMs[i] > 0)
				Console.WriteLine($"  {(BuildPhase)i,-16} {phaseElapsedMs[i],7:0.0}ms  {phaseCollisionCounts[i],9} collisions");

		var simMs = jumpSimTicks * 1000.0 / Stopwatch.Frequency;
		var landingMs = landingSearchTicks * 1000.0 / Stopwatch.Frequency;
		Console.WriteLine($"  jump sim: {simsRun} sims ({simLanded} landed, {simStalled} stalled, {simOutOfBounds} out of bounds, {simTimedOut} timed out), {simSteps} pawn updates, {landingSearchIterations} landing-search iters");
		Console.WriteLine($"  jump time: TrySimulateJump {simMs:0.0}ms, FindNearestLandingNode {landingMs:0.0}ms");

		// Per-plan coverage map: reach = horizontal distance, dy = vertical offset
		// of landings. kept = edges this plan won after dedup.
		for (var k = 0; k < planStats.Length; k++)
		{
			ref var stat = ref planStats[k];
			var p = JumpPlans[k];
			var sw = p.SwitchTime < 2 ? $"{p.SwitchTime:0.00}" : "  - ";
			var reachStr = stat.Landed > 0 ? $"{stat.MinReach,3:0}..{stat.MaxReach,3:0}" : "   -   ";
			var dyStr = stat.Landed > 0 ? $"{stat.MinDy,4:0}..{stat.MaxDy,3:0}" : "   -   ";
			Console.WriteLine($"  plan[{k,2}] dir {p.Direction,2} air {p.AirDirection,2} runup {p.RunupTime:0.00} hold {p.HoldTime:0.00} switch {sw}: landed {stat.Landed,3}, kept {stat.Kept,3}, reach {reachStr}, dy {dyStr}");
		}

		LogGraphSummary();
	}

	// Order-independent fingerprint of the built graph. Counts/sums don't care
	// about node or edge ordering (which an optimization may shuffle), but they
	// flag real topology changes so we can diff before/after a refactor.
	void LogGraphSummary()
	{
		Span<int> nodeKinds = stackalloc int[4];
		Span<int> edgeKinds = stackalloc int[5];
		foreach (var node in Nodes)
			nodeKinds[(int)node.Kind]++;

		var totalLength = 0.0;
		foreach (var edge in Edges)
		{
			edgeKinds[(int)edge.Kind]++;
			totalLength += Vector2.Distance(Nodes[edge.From].Position, Nodes[edge.To].Position);
		}

		// weakly-connected components via union-find (edges treated undirected)
		var parent = new int[Nodes.Count];
		var degree = new int[Nodes.Count];
		for (var i = 0; i < parent.Length; i++)
			parent[i] = i;

		int Find(int x)
		{
			while (parent[x] != x)
				x = parent[x] = parent[parent[x]];
			return x;
		}

		foreach (var edge in Edges)
		{
			degree[edge.From]++;
			degree[edge.To]++;
			parent[Find(edge.From)] = Find(edge.To);
		}

		int components = 0, isolated = 0;
		for (var i = 0; i < parent.Length; i++)
		{
			if (Find(i) == i)
				components++;
			if (degree[i] == 0)
				isolated++;
		}

		var avgOutDegree = Nodes.Count > 0 ? Edges.Count / (float)Nodes.Count : 0;
		Console.WriteLine($"  nodes: Ground {nodeKinds[(int)NodeKind.Ground]}, Landing {nodeKinds[(int)NodeKind.Landing]}, Ledge {nodeKinds[(int)NodeKind.Ledge]}, RoomExit {nodeKinds[(int)NodeKind.RoomExit]}");
		Console.WriteLine($"  edges: Walk {edgeKinds[(int)EdgeKind.Walk]}, Crawl {edgeKinds[(int)EdgeKind.Crawl]}, Fall {edgeKinds[(int)EdgeKind.Fall]}, Jump {edgeKinds[(int)EdgeKind.Jump]}, RoomExit {edgeKinds[(int)EdgeKind.RoomExit]}");
		Console.WriteLine($"  components {components}, isolated {isolated}, total edge length {totalLength:0}, avg out-degree {avgOutDegree:0.00}");
		if (components > 1)
			LogComponentBounds(parent);
		LogSurfaceSummary(edgeKinds);
	}

	void LogComponentBounds(int[] parent)
	{
		var origin = rebuildRoom?.WorldBounds.TopLeft ?? Point2.Zero;
		var bounds = new Dictionary<int, (int Count, float MinX, float MinY, float MaxX, float MaxY)>();

		int Find(int x)
		{
			while (parent[x] != x)
				x = parent[x];
			return x;
		}

		for (var i = 0; i < Nodes.Count; i++)
		{
			var root = Find(i);
			var p = Nodes[i].Position - (Vector2)origin;
			if (!bounds.TryGetValue(root, out var b))
				b = (0, p.X, p.Y, p.X, p.Y);
			b.Count++;
			if (p.X < b.MinX) b.MinX = p.X;
			if (p.Y < b.MinY) b.MinY = p.Y;
			if (p.X > b.MaxX) b.MaxX = p.X;
			if (p.Y > b.MaxY) b.MaxY = p.Y;
			bounds[root] = b;
		}

		foreach (var (_, b) in bounds)
			Console.WriteLine($"    component: {b.Count} nodes, local ({b.MinX:0},{b.MinY:0})..({b.MaxX:0},{b.MaxY:0})");
	}

	void LogSurfaceSummary(ReadOnlySpan<int> edgeKinds)
	{
		var duckingSpans = 0;
		var singletonSpans = 0;
		var totalNodesOnSpans = 0;
		foreach (var span in SurfaceSpans)
		{
			if (span.RequiresDucking)
				duckingSpans++;
			if (span.NodeCount == 1)
				singletonSpans++;
			totalNodesOnSpans += span.NodeCount;
		}

		var walkPairs = new HashSet<(int From, int To)>();
		var crawlPairs = new HashSet<(int From, int To)>();
		var fallPairs = new HashSet<(int From, int To)>();
		var jumpPairs = new HashSet<(int From, int To)>();
		var jumpPlanPairs = new HashSet<(int From, int To, JumpPlan Plan)>();
		var jumpEdgesPerPair = new Dictionary<(int From, int To), int>();
		var unmappedEdges = 0;

		foreach (var edge in Edges)
		{
			if (edge.Kind == EdgeKind.RoomExit)
				continue;

			var fromSpan = NodeSurfaceSpanId(edge.From);
			var toSpan = NodeSurfaceSpanId(edge.To);
			if (fromSpan < 0 || toSpan < 0)
			{
				unmappedEdges++;
				continue;
			}

			var pair = (fromSpan, toSpan);
			switch (edge.Kind)
			{
				case EdgeKind.Walk:
					walkPairs.Add(pair);
					break;
				case EdgeKind.Crawl:
					crawlPairs.Add(pair);
					break;
				case EdgeKind.Fall:
					fallPairs.Add(pair);
					break;
				case EdgeKind.Jump:
					jumpPairs.Add(pair);
					if (edge.Jump.HasValue)
						jumpPlanPairs.Add((fromSpan, toSpan, edge.Jump.Value));
					jumpEdgesPerPair[pair] = jumpEdgesPerPair.TryGetValue(pair, out var count) ? count + 1 : 1;
					break;
			}
		}

		var maxJumpEdgesPerPair = 0;
		var multiJumpPairs = 0;
		foreach (var count in jumpEdgesPerPair.Values)
		{
			if (count > 1)
				multiJumpPairs++;
			if (count > maxJumpEdgesPerPair)
				maxJumpEdgesPerPair = count;
		}

		Span<int> surfaceEdgeKinds = stackalloc int[5];
		var jumpSurfaceNodeEdges = 0;
		foreach (var edge in SurfaceEdges)
		{
			surfaceEdgeKinds[(int)edge.Kind]++;
			if (edge.Kind == EdgeKind.Jump)
				jumpSurfaceNodeEdges += edge.NodeEdgeCount;
		}

		var avgNodesPerSpan = SurfaceSpans.Count > 0 ? totalNodesOnSpans / (float)SurfaceSpans.Count : 0;
		var avgJumpNodesPerSurfaceEdge = surfaceEdgeKinds[(int)EdgeKind.Jump] > 0 ? jumpSurfaceNodeEdges / (float)surfaceEdgeKinds[(int)EdgeKind.Jump] : 0;
		Console.WriteLine($"  surfaces: {SurfaceSpans.Count} spans ({duckingSpans} ducking, {singletonSpans} singleton), avg {avgNodesPerSpan:0.0} nodes/span");
		Console.WriteLine($"  surface edge collapse: Walk {edgeKinds[(int)EdgeKind.Walk]}->{walkPairs.Count}, Crawl {edgeKinds[(int)EdgeKind.Crawl]}->{crawlPairs.Count}, Fall {edgeKinds[(int)EdgeKind.Fall]}->{fallPairs.Count}, Jump {edgeKinds[(int)EdgeKind.Jump]}->{jumpPairs.Count} span pairs / {jumpPlanPairs.Count} plan pairs");
		Console.WriteLine($"  surface interval edges: Walk {surfaceEdgeKinds[(int)EdgeKind.Walk]}, Crawl {surfaceEdgeKinds[(int)EdgeKind.Crawl]}, Fall {surfaceEdgeKinds[(int)EdgeKind.Fall]}, Jump {surfaceEdgeKinds[(int)EdgeKind.Jump]} (avg {avgJumpNodesPerSurfaceEdge:0.0} point jump edges)");
		if (jumpPairs.Count > 0)
			Console.WriteLine($"  jump interval pressure: {multiJumpPairs}/{jumpPairs.Count} span pairs have multiple point edges, max {maxJumpEdgesPerPair}");
		if (unmappedEdges > 0)
			Console.WriteLine($"  surface unmapped nav edges: {unmappedEdges}");
	}

	int NodeSurfaceSpanId(int nodeId)
		=> nodeId >= 0 && nodeId < nodeSurfaceSpanIds.Count ? nodeSurfaceSpanIds[nodeId] : -1;

	void TickSampling()
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

	void TickJumpEdges()
	{
		if (nextJumpSourceId >= Nodes.Count)
		{
			phase = BuildPhase.RoomExits;
			return;
		}

		// Process a single source node per call so the caller's per-iteration
		// budget check runs between nodes. A full node's jump plans are enough
		// to overflow the frame budget if batched.
		var from = Nodes[nextJumpSourceId];
		nextJumpSourceId++;

		if (!from.RequiresDucking)
		{
			jumpCandidates.Clear();
			for (var pi = 0; pi < JumpPlans.Length; pi++)
			{
				var plan = JumpPlans[pi];
				if (plan.MaxWallKicks > 1 && !CanAttemptChainedWallJump(rebuildGame!, from.Position))
					continue;

				var t0 = Stopwatch.GetTimestamp();
				var simulated = TrySimulateJump(rebuildGame!, from, plan, out var landing);
				jumpSimTicks += Stopwatch.GetTimestamp() - t0;
				if (!simulated)
					continue;

				// record where this plan reaches (its coverage in the height x distance map)
				var reach = MathF.Abs(landing.X - from.Position.X);
				var dy = landing.Y - from.Position.Y;
				ref var st = ref planStats[pi];
				st.Landed++;
				st.SumReach += reach;
				if (reach < st.MinReach) st.MinReach = reach;
				if (reach > st.MaxReach) st.MaxReach = reach;
				if (dy < st.MinDy) st.MinDy = dy;
				if (dy > st.MaxDy) st.MaxDy = dy;

				var t1 = Stopwatch.GetTimestamp();
				var to = FindNearestLandingNode(from.Id, landing, SimLandingTolerance);
				landingSearchTicks += Stopwatch.GetTimestamp() - t1;
				if (!to.HasValue || HasEdge(from.Id, to.Value.Id))
					continue;

				var cost = JumpPlanCost(from, to.Value, plan);
				if (!jumpCandidates.TryGetValue(to.Value.Id, out var existing) || cost < existing.Cost)
					jumpCandidates[to.Value.Id] = (plan, cost, pi);
			}

			foreach (var (toId, jump) in jumpCandidates)
			{
				if (CullPitFallJumps && !ValidateJumpWithController(from, Nodes[toId], jump.Plan))
					continue;
				if (AddEdge(from.Id, toId, EdgeKind.Jump, jump.Plan))
					planStats[jump.PlanIndex].Kept++;
			}
		}

		if (nextJumpSourceId >= Nodes.Count)
			phase = BuildPhase.RoomExits;
	}

	int AddNode(Point2 position, NodeKind kind, RoomExit? exit = null, bool requiresDucking = false)
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

	void AddPlatformLandingNodes(Game game)
	{
		foreach (var row in landingRows.Values)
			row.Clear();

		var nodeCount = Nodes.Count;
		for (var i = 0; i < nodeCount; i++)
		{
			var node = Nodes[i];
			if (node.Kind == NodeKind.RoomExit || node.RequiresDucking)
				continue;

			var y = (int)node.Position.Y;
			if (!landingRows.TryGetValue(y, out var row))
			{
				row = [];
				landingRows[y] = row;
			}
			row.Add(node);
		}

		foreach (var row in landingRows.Values)
		{
			if (row.Count <= 0)
				continue;

			row.Sort(CompareNodeX);
			var left = row[0];
			var right = left;
			var count = 1;
			for (var i = 1; i < row.Count; i++)
			{
				var node = row[i];
				if (node.Position.X - right.Position.X > SampleStep)
				{
					AddLandingNodeForSpan(game, left, right, count);
					left = node;
					count = 0;
				}
				right = node;
				count++;
			}
			AddLandingNodeForSpan(game, left, right, count);
		}

		BuildLandingCandidateRows();
		BuildSurfaceSpans();
	}

	void BuildLandingCandidateRows()
	{
		foreach (var row in landingCandidateRows.Values)
			row.Clear();

		for (var i = 0; i < Nodes.Count; i++)
		{
			var node = Nodes[i];
			if (node.Kind == NodeKind.RoomExit || node.RequiresDucking)
				continue;

			var y = (int)node.Position.Y;
			if (!landingCandidateRows.TryGetValue(y, out var row))
			{
				row = [];
				landingCandidateRows[y] = row;
			}
			row.Add(i);
		}
	}

	void BuildSurfaceSpans()
	{
		SurfaceSpans.Clear();
		nodeSurfaceSpanIds.Clear();
		for (var i = 0; i < Nodes.Count; i++)
			nodeSurfaceSpanIds.Add(-1);

		foreach (var row in surfaceRows.Values)
			row.Clear();

		for (var i = 0; i < Nodes.Count; i++)
		{
			var node = Nodes[i];
			if (node.Kind == NodeKind.RoomExit)
				continue;

			var y = (int)node.Position.Y;
			if (!surfaceRows.TryGetValue(y, out var row))
			{
				row = [];
				surfaceRows[y] = row;
			}
			row.Add(i);
		}

		foreach (var row in surfaceRows.Values)
		{
			if (row.Count <= 0)
				continue;

			row.Sort((a, b) => Nodes[a].Position.X.CompareTo(Nodes[b].Position.X));
			var startIndex = 0;
			var endIndex = 0;
			var requiresDucking = Nodes[row[0]].RequiresDucking;

			for (var i = 1; i < row.Count; i++)
			{
				var nodeId = row[i];
				var node = Nodes[nodeId];
				var previous = Nodes[row[endIndex]];
				if (node.Position.X - previous.Position.X > SampleStep || node.RequiresDucking != requiresDucking)
				{
					AddSurfaceSpan(row, startIndex, endIndex, requiresDucking);
					startIndex = i;
					requiresDucking = node.RequiresDucking;
				}
				endIndex = i;
			}
			AddSurfaceSpan(row, startIndex, endIndex, requiresDucking);
		}
	}

	void AddSurfaceSpan(List<int> row, int startIndex, int endIndex, bool requiresDucking)
	{
		var firstNodeId = row[startIndex];
		var lastNodeId = row[endIndex];
		var first = Nodes[firstNodeId];
		var last = Nodes[lastNodeId];
		var id = SurfaceSpans.Count;
		SurfaceSpans.Add(new(
			id,
			(int)first.Position.Y,
			(int)first.Position.X,
			(int)last.Position.X,
			requiresDucking,
			endIndex - startIndex + 1));

		for (var i = startIndex; i <= endIndex; i++)
			nodeSurfaceSpanIds[row[i]] = id;
	}

	void BuildSurfaceEdges()
	{
		SurfaceEdges.Clear();
		surfaceEdgeLookup.Clear();

		foreach (var edge in Edges)
		{
			if (edge.Kind == EdgeKind.RoomExit)
				continue;

			var fromSpan = NodeSurfaceSpanId(edge.From);
			var toSpan = NodeSurfaceSpanId(edge.To);
			if (fromSpan < 0 || toSpan < 0)
				continue;

			var jump = edge.Jump ?? default;
			var key = new SurfaceEdgeKey(fromSpan, toSpan, edge.Kind, edge.Jump.HasValue, jump);
			var from = Nodes[edge.From];
			var to = Nodes[edge.To];
			if (surfaceEdgeLookup.TryGetValue(key, out var accumulator))
				accumulator.Add(from, to);
			else
				accumulator = new SurfaceEdgeAccumulator(from, to);
			surfaceEdgeLookup[key] = accumulator;
		}

		foreach (var (key, accumulator) in surfaceEdgeLookup)
		{
			SurfaceEdges.Add(new(
				key.From,
				key.To,
				key.Kind,
				accumulator.FromLeft,
				accumulator.FromRight,
				accumulator.ToLeft,
				accumulator.ToRight,
				key.HasJump ? key.Jump : null,
				accumulator.Count));
		}
	}

	void AddLandingNodeForSpan(Game game, Node left, Node right, int count)
	{
		if (count < 2)
			return;

		var center = new Point2(
			(int)MathF.Round((left.Position.X + right.Position.X) * 0.5f),
			(int)MathF.Round((left.Position.Y + right.Position.Y) * 0.5f));
		if (!TryGetStandableProbe(game, center, out var requiresDucking) || requiresDucking)
			return;

		AddNode(center, NodeKind.Landing);
	}

	void BuildRoomExitNodes(Room room)
	{
		var navigationNodeCount = Nodes.Count;
		BuildEdgeExitNodes(room, navigationNodeCount);
		BuildLinkedDoorExitNodes(room, navigationNodeCount);
	}

	void BuildEdgeExitNodes(Room room, int navigationNodeCount)
	{
		var bounds = room.WorldBounds;
		for (var i = 0; i < navigationNodeCount; i++)
		{
			var from = Nodes[i];
			if (from.Kind == NodeKind.RoomExit)
				continue;

			foreach (var direction in RoomExitDirections)
			{
				if (!IsNearRoomEdge(bounds, from.Position, direction))
					continue;

				if (Assets.FindAdjacentRoom(room, direction, from.Position) is not { } targetRoom)
					continue;

				var exitPosition = RoomEdgeExitPosition(bounds, from.Position, direction);
				var exitId = AddNode(exitPosition, NodeKind.RoomExit, new RoomExit(RoomExitKind.Edge, targetRoom.Id, null, direction));
				AddEdge(from.Id, exitId, EdgeKind.RoomExit);
				AddEdge(exitId, from.Id, EdgeKind.RoomExit);
			}
		}
	}

	void BuildLinkedDoorExitNodes(Room room, int navigationNodeCount)
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
			if (FindNearestNavigationNode(navigationNodeCount, position, SampleStep * 6) is not { } nearest)
				continue;

			AddEdge(nearest.Id, exitId, EdgeKind.RoomExit);
			AddEdge(exitId, nearest.Id, EdgeKind.RoomExit);
		}
	}

	static bool IsNearRoomEdge(RectInt bounds, Vector2 position, Point2 direction)
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

	static Point2 RoomEdgeExitPosition(RectInt bounds, Vector2 position, Point2 direction)
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

	Node? FindNearestNavigationNode(int nodeCount, Vector2 position, float maxDistance)
	{
		Node? nearest = null;
		var nearestDistanceSquared = maxDistance * maxDistance;
		for (var i = 0; i < nodeCount; i++)
		{
			var node = Nodes[i];
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

	static bool OverlapsCollision(Game game, RectInt bounds)
	{
		collisionQueries++;
		return game.OverlapsRoomCollision(bounds);
	}

	static bool IsStandable(Game game, Point2 position)
	{
		return TryGetStandableProbe(game, position, out _);
	}

	static bool TryGetStandableProbe(Game game, Point2 position, out bool requiresDucking)
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

	static bool IsStandableWithProbe(Game game, Point2 position, RectInt probe)
	{
		var bounds = probe + position;
		return !OverlapsCollision(game, bounds) && HasStandableSupport(game, bounds);
	}

	static bool HasStandableSupport(Game game, RectInt bounds)
	{
		supportChecks++;
		if (OverlapsCollision(game, bounds + Point2.Down))
			return true;

		foreach (var actor in jumpthruActors)
		{
			var local = bounds - actor.Position;
			if (actor.Hitbox.Overlaps(local))
				continue;

			if (actor.Hitbox.Overlaps(local + Point2.Down))
				return true;
		}

		return false;
	}

	static bool IsLedge(Game game, Point2 position)
	{
		var left = position + new Point2(-SampleStep, 0);
		var right = position + new Point2(SampleStep, 0);
		return !IsStandable(game, left) || !IsStandable(game, right);
	}

	void BuildWalkEdges(Game game)
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

				AddEdge(from.Id, to.Id, edgeKind.Value);
			}
		}
	}

	void BuildFallEdges(Game game)
	{
		foreach (var node in Nodes)
		{
			if (node.Kind != NodeKind.Ledge)
				continue;

			if (!IsStandable(game, (Point2)node.Position + new Point2(-SampleStep, 0)) &&
				FindFallLanding(game, node, -SampleStep) is { } leftLanding)
				AddEdge(node.Id, leftLanding.Id, EdgeKind.Fall);

			if (!IsStandable(game, (Point2)node.Position + new Point2(SampleStep, 0)) &&
				FindFallLanding(game, node, SampleStep) is { } rightLanding)
				AddEdge(node.Id, rightLanding.Id, EdgeKind.Fall);
		}
	}

	static float JumpPlanCost(Node from, Node to, JumpPlan plan)
	{
		var targetDirection = Math.Sign(to.Position.X - from.Position.X);
		var cost = plan.RunupTime * 100 + LandingRiskCost(to);
		var height = MathF.Max(0, from.Position.Y - to.Position.Y);
		if (height > 0)
			cost -= plan.HoldTime * 30;
		// A wall kick is a delicate two-part move; prefer any plain jump that reaches the
		// same node, so it's only chosen where a single jump can't make it.
		if (plan.WallJump)
			cost += 70 + MathF.Max(0, plan.MaxWallKicks - 1) * 350;
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

	Node? FindNearestLandingNode(int fromId, Vector2 landing, float maxDistance)
	{
		Node? nearest = null;
		var bestScore = maxDistance * maxDistance;
		var minY = (int)MathF.Floor(landing.Y - maxDistance);
		var maxY = (int)MathF.Ceiling(landing.Y + maxDistance);
		for (var y = minY; y <= maxY; y++)
		{
			if (!landingCandidateRows.TryGetValue(y, out var row))
				continue;

			foreach (var nodeId in row)
			{
				landingSearchIterations++;
				if (nodeId == fromId)
					continue;

				var node = Nodes[nodeId];
				var source = Nodes[fromId];
				var isUpwardLanding = node.Position.Y < source.Position.Y;
				if (!isUpwardLanding &&
					Vector2.DistanceSquared(node.Position, source.Position) < SimMinimumLandingDistance * SimMinimumLandingDistance)
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
		}
		return nearest;
	}

	(Node Node, EdgeKind Kind)? FindWalkNeighbor(Game game, Node from, int offsetX)
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

	bool HasEdge(int from, int to) => edgeLookup.Contains((from, to));

	bool AddEdge(int from, int to, EdgeKind kind, JumpPlan? jump = null)
	{
		if (!edgeLookup.Add((from, to)))
			return false;
		Edges.Add(new(from, to, kind, jump));
		return true;
	}

	Node? FindFallLanding(Game game, Node from, int offsetX)
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

	static EdgeKind? WalkEdgeKind(Game game, Node from, Node to)
	{
		if (!from.RequiresDucking && !to.RequiresDucking && WalkProbeClear(game, from.Position, to.Position, PawnProbe))
			return EdgeKind.Walk;
		if (WalkProbeClear(game, from.Position, to.Position, DuckingPawnProbe))
			return EdgeKind.Crawl;
		return null;
	}

	static bool WalkProbeClear(Game game, Vector2 from, Vector2 to, RectInt probe)
	{
		var steps = Math.Max(1, (int)MathF.Ceiling(Vector2.Distance(from, to) / 2.0f));
		for (var i = 1; i <= steps; i++)
		{
			var t = i / (float)steps;
			var position = (Point2)Vector2.Lerp(from, to, t);
			if (OverlapsCollision(game, probe + position))
				return false;
		}
		return true;
	}

	static bool FallProbeClear(Game game, Point2 from, Point2 to)
	{
		if (OverlapsCollision(game, PawnProbe + from))
			return false;

		for (var y = from.Y + 1; y < to.Y; y += 2)
			if (OverlapsCollision(game, PawnProbe + new Point2(from.X, y)))
				return false;

		return true;
	}

	static bool TrySimulateJump(Game game, Node from, JumpPlan plan, out Vector2 landing)
	{
		using var _ = game.BeginSimulation();
		var sim = new JumpSimulationPawn(game, from.Position, plan);
		return sim.Run(out landing);
	}

	// Run the actual runtime controller (Navigator) over a simulated pawn entering at a
	// distance-appropriate speed (what Bud realistically arrives with — a short hop approached
	// slowly, a long jump fast; a fixed full-speed entry would mis-position short hops). Returns
	// false only if the controller drops into a pit — the one outcome that's unambiguously
	// unmakeable and safe to cull. Off-target-but-on-a-platform landings are kept.
	const float ValidationNominalAirtime = 0.5f;
	const float ValidationMaxEntrySpeed = 100.0f;

	// Cull jump edges where the runtime controller (the real Navigator, run over a sim pawn —
	// the unified build/runtime executor) drops Bud into a pit. Conservative on purpose: a
	// full "must land exactly" gate over-prunes the regular jumps wall-kick wells rely on, but
	// an edge that demonstrably falls into a pit is one the runtime can't make either, so it's
	// always safe to remove. Flip LogCulls to see each culled edge and why.
	internal static bool CullPitFallJumps = false;
	internal static bool LogCulls = false;

	bool ValidateJumpWithController(Node from, Node to, JumpPlan plan)
	{
		// Wall-kick chains are sequential, wall-contact-timed moves the single-shot validator
		// can't reproduce from a cold spawn (it has no run-up to set up the kick rhythm). They
		// climb wells where no plain jump reaches, so trust the ballistic discovery and the
		// runtime's dedicated wall-kick path for them; gate only plain ballistic arcs.
		if (plan.WallJump)
			return true;

		using var _ = rebuildGame!.BeginSimulation();
		var entryVx = Math.Clamp((to.Position.X - from.Position.X) / ValidationNominalAirtime, -ValidationMaxEntrySpeed, ValidationMaxEntrySpeed);
		var pawn = new JumpValidationPawn(rebuildGame!, this, from, to, plan, entryVx);
		pawn.Run();
		var keep = !pawn.FellInPit;
		if (!keep && LogCulls)
		{
			var off = pawn.LastPosition - to.Position;
			var toSpan = NodeSurfaceSpanId(to.Id);
			var landSpan = FindNearestSurface(pawn.LastPosition, Game.TileSize * 1.5f)?.SpanId ?? -1;
			Console.WriteLine($"[Cull] {from.Id}@({from.Position.X:0},{from.Position.Y:0})->{to.Id}@({to.Position.X:0},{to.Position.Y:0}) dx={to.Position.X - from.Position.X:0} dy={from.Position.Y - to.Position.Y:0} plan(d{plan.Direction},h{plan.HoldTime:0.00},a{plan.AirDirection},wj{(plan.WallJump ? 1 : 0)}) end=({pawn.LastPosition.X:0},{pawn.LastPosition.Y:0}) off=({off.X:0},{off.Y:0}) toSpan={toSpan} landSpan={landSpan} {pawn.Outcome}");
		}
		return keep;
	}

	// Drives the real Navigator over a throwaway pawn so the build validates exactly what
	// the runtime will do. Same physics (Pawn.Update), same controller, same wall-kick path.
	sealed class JumpValidationPawn : Pawn
	{
		readonly Game game;
		readonly Navigator nav;
		float elapsed;
		public Vector2 LastPosition;
		public string Outcome = "?";
		public bool FellInPit;

		public JumpValidationPawn(Game game, PlatformNavGraph graph, Node from, Node to, JumpPlan plan, float entryVx)
		{
			this.game = game;
			Game = game;
			Position = (Point2)from.Position;
			Hitbox = new(PawnProbe);
			Facing = plan.Direction == 0 ? Signs.Positive : (Signs)plan.Direction;
			Velocity = new Vector2(entryVx, 0);
			grounded = Grounded();
			nav = new Navigator(this) { GraphOverride = graph, SuppressDebug = true };
			nav.StartActiveEdge(new Edge(from.Id, to.Id, EdgeKind.Jump, plan));
		}

		public override PawnInput GetInput()
		{
			nav.TryExecuteActiveEdge(out var moveX, out var moveY, out var jumpPressed);
			nav.UpdateJumpHeld(jumpPressed);
			return new(moveX, moveY, jumpPressed, nav.JumpHeld, false, nav.ActiveEdgeAllowsWallJump);
		}

		public bool Run()
		{
			var dt = game.Time.Delta;
			if (dt <= 0)
				dt = SimDelta;

			for (elapsed = 0.0f; elapsed < SimChainMaxTime; elapsed += dt)
			{
				Update();
				LastPosition = Position;
				if (nav.LastEdgeRunState != Navigator.EdgeRunState.Active)
				{
					Outcome = nav.LastEdgeRunState.ToString();
					return nav.LastEdgeRunState == Navigator.EdgeRunState.Landed;
				}

				if (Position.Y > game.Bounds.Bottom + Game.TileSize)
				{
					Outcome = "pit";
					FellInPit = true;
					return false;
				}
				if (Position.X < game.Bounds.Left - Game.TileSize * 2 ||
					Position.X > game.Bounds.Right + Game.TileSize * 2)
				{
					Outcome = "oob";
					return false;
				}
			}
			Outcome = "timeout";
			return false;
		}
	}

	static bool CanAttemptChainedWallJump(Game game, Vector2 from)
		=> HasNearbyWallForChain(game, from, -1) && HasNearbyWallForChain(game, from, 1);

	static bool HasNearbyWallForChain(Game game, Vector2 from, int direction)
	{
		for (var dx = Game.TileSize; dx <= Game.TileSize * 5; dx += Game.TileSize)
			for (var dy = Game.TileSize; dy <= Game.TileSize * 18; dy += Game.TileSize * 2)
			{
				var x = (int)from.X + direction * dx - 1;
				var y = (int)from.Y - dy - Game.TileSize / 2;
				if (game.OverlapsRoomCollision(new RectInt(x, y, 2, Game.TileSize)))
					return true;
			}

		return false;
	}

	sealed class JumpSimulationPawn : Pawn
	{
		readonly Game game;
		readonly Vector2 from;
		readonly JumpPlan plan;
		float elapsed;
		bool leftStart;
		JumpPlanExecution.State jump;
		int lastX;
		int stalledFrames;

		public JumpSimulationPawn(Game game, Vector2 from, JumpPlan plan)
		{
			this.game = game;
			this.from = from;
			this.plan = plan;
			Game = game;
			Position = (Point2)from;
			Hitbox = new(PawnProbe);
			Facing = plan.Direction == 0 ? Signs.Positive : (Signs)plan.Direction;
			grounded = Grounded();
			lastX = Position.X;
		}

		public override PawnInput GetInput()
		{
			jump.Timer = elapsed;
			var moveX = JumpPlanExecution.MoveX(plan, jump);
			var jumpPressed = false;

			if (JumpPlanExecution.ShouldStartJump(plan, jump))
			{
				JumpPlanExecution.StartJump(ref jump, plan);
				jumpPressed = true;
			}
			else if (!jump.Started && IsGrounded && moveX != 0 && !HasGroundSupportNextStep(moveX))
			{
				JumpPlanExecution.StartJump(ref jump, plan);
				jumpPressed = true;
			}

			JumpPlanExecution.DecayHold(ref jump, game.Time.Delta <= 0 ? SimDelta : game.Time.Delta, jumpPressed);

			JumpPlanExecution.SyncWallKickLock(ref jump, plan, IsWallJumpLocked, Math.Sign(Velocity.X));
			if (JumpPlanExecution.NeedsWallKickInput(plan, jump, IsGrounded, IsWallJumpLocked))
			{
				moveX = JumpPlanExecution.WallApproachDirection(plan, jump);
				jumpPressed = true;
			}

			return new(
				MoveX: moveX,
				MoveY: 0,
				JumpPressed: jumpPressed,
				JumpDown: JumpPlanExecution.JumpDown(jump, jumpPressed),
				DashPressed: false,
				AllowWallJump: plan.WallJump
			);
		}

		public bool Run(out Vector2 landing)
		{
			simsRun++;
			var dt = game.Time.Delta;
			if (dt <= 0)
				dt = SimDelta;

			var maxTime = plan.MaxWallKicks > 1 ? SimChainMaxTime : SimMaxTime;
			for (elapsed = 0.0f; elapsed < maxTime; elapsed += dt)
			{
				simSteps++;
				Update();

				if (Vector2.DistanceSquared((Vector2)Position, from) > 9)
					leftStart = true;

				if (leftStart && Velocity.Y >= 0 && IsGrounded)
				{
					simLanded++;
					landing = Position;
					return true;
				}

				// Once the pawn has dropped below the floor or flown clear of the side
				// walls it can never be within landing tolerance of any node, so end the
				// sim now instead of arcing out the rest of the time budget. This is the
				// dominant cause of timeouts in open/pit rooms and removes no edges (no
				// reachable landing exists out there).
				if (Position.Y > game.Bounds.Bottom + Game.TileSize ||
					Position.X < game.Bounds.Left - Game.TileSize * 2 ||
					Position.X > game.Bounds.Right + Game.TileSize * 2)
				{
					simOutOfBounds++;
					landing = default;
					return false;
				}

				// Detect a pawn wedged against a wall/ceiling (not making horizontal
				// progress while still rising). A pure-vertical plan legitimately keeps
				// X fixed the whole way up, so exclude it or it false-positives and the
				// tall straight-up hop never completes.
				var horizontalPlan = plan.Direction != 0 || plan.AirDirection != 0;
				if (horizontalPlan && Position.X == lastX && jump.Started && !IsGrounded && Velocity.Y < 0)
					stalledFrames++;
				else
					stalledFrames = 0;
				lastX = Position.X;

				if (stalledFrames > 20)
				{
					simStalled++;
					landing = default;
					return false;
				}
			}

			simTimedOut++;
			landing = default;
			return false;
		}

		bool HasGroundSupportNextStep(int moveX)
			=> PlatformNavGraph.HasStandableSupport(game, PawnProbe + Position + new Point2(Math.Sign(moveX), 0));
	}
}
