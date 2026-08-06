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
		RoomExit,
		// A volume of water the pawn can swim through, not a standable point. Its bounds
		// live in Node.Area; Position is the area center. Reached/left via Swim edges.
		Swim
	}

	public enum EdgeKind
	{
		Walk,
		Crawl,
		Fall,
		Jump,
		RoomExit,
		// Traversal involving water: portals between adjacent swim areas, entering water
		// from a standable node, or a surface hop-out onto a standable node above.
		Swim,
		// Chained wall kicks up a walled vertical shaft (a well) from a node at its mouth to
		// a node at its top opening — the input-driven climb the runtime controller executes.
		WellClimb,
		Rope,
		MovingPlatform
	}

	public enum RoomExitKind
	{
		Edge,
		LinkedDoor
	}

	public readonly record struct RoomExit(RoomExitKind Kind, string? TargetRoomId, string? TargetActorId, Point2 Direction);
	public readonly record struct Node(int Id, Vector2 Position, NodeKind Kind, RoomExit? Exit = null, bool RequiresDucking = false, RectInt? Area = null);
	public readonly record struct SurfaceSpan(
		int Id,
		int Left,
		int Right,
		int Top,
		int Bottom,
		bool RequiresDucking,
		int NodeCount,
		int PointStart,
		int PointCount)
	{
		public bool IsSloped => Top != Bottom;
	}
	public readonly record struct SurfacePoint(int SpanId, Vector2 Position, float DistanceSquared);
	public readonly record struct SurfaceEdge(int From, int To, EdgeKind Kind, int FromLeft, int FromRight, int ToLeft, int ToRight, JumpPlan? Jump = null, int NodeEdgeCount = 0);
	// WallJump: if set, the jump is taken toward Direction, and the first time the
	// pawn hits a wall mid-air it kicks off it (away from the wall) with a fresh jump,
	// then steers in AirDirection. Lets the planner route through a wall kick where a
	// single jump can't make the gap/height (e.g. under a low ceiling).
	// Descend: enter the maneuver by FALLING instead of an up-jump — the pawn drifts toward a
	// wall (Direction), wall-hugs down, then fires a single wall kick off it (WallJump) toward
	// AirDirection. Used to drop into a shaft and kick into a pocket under an overhang, which no
	// upward jump arc can reach.
	public readonly record struct JumpPlan(int Direction, float RunupTime, float HoldTime, int AirDirection, float SwitchTime, bool WallJump = false, int MaxWallKicks = 1, bool Descend = false);
	public readonly record struct MovingPlatformRide(float BoardStart, float BoardEnd, float ExitStart, float ExitEnd, int Direction, bool StayAboard = false);
	public readonly record struct MovingPlatformAttachment(string ActorId, int NodeId, float RouteStart, float RouteEnd);
	public readonly record struct Edge(int From, int To, EdgeKind Kind, JumpPlan? Jump = null, string? ConnectorActorId = null, MovingPlatformRide? PlatformRide = null);

	const int SampleStep = Game.TileSize;
	const float SimDelta = 1.0f / 60.0f;
	const float SimMaxTime = 1.60f;
	const float SimChainMaxTime = 4.00f;
	const int SimLandingTolerance = 12;
	const int SimMinimumLandingDistance = Game.TileSize * 2;
	static int FallMaxHorizontal => Game.TileSize * 4;
	// Clearance rects the whole build probes with. Default matches the Bud/player body; a
	// graph built with a larger probe (see Stalker) naturally drops the edges that body
	// cannot fit through — crawl spaces produce no edges at all when the probes are equal.
	readonly RectInt PawnProbe = new(-4, -12, 8, 12);
	readonly RectInt DuckingPawnProbe = new(-4, -6, 8, 6);

	public PlatformNavGraph() { }

	public PlatformNavGraph(RectInt pawnProbe, RectInt duckingPawnProbe)
	{
		PawnProbe = pawnProbe;
		DuckingPawnProbe = duckingPawnProbe;
	}
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

	// Descent maneuvers: walk off a ledge toward a wall, hug-slide down for SlideTime, then one
	// kick off it (away from the wall). Discovers routes into pockets under overhangs that no
	// upward jump arc can reach. Direction = toward the wall; AirDirection = kick (away) side.
	static readonly JumpPlan[] DescentPlans = BuildDescentPlans();
	static JumpPlan[] BuildDescentPlans()
	{
		var plans = new List<JumpPlan>();
		foreach (var dir in new[] { -1, 1 })
			foreach (var slide in new[] { 0.0f, 0.2f, 0.4f, 0.6f, 0.8f, 0.9f, 1.0f, 1.1f })
				plans.Add(new JumpPlan(dir, slide, 0.055f, -dir, 99.0f, WallJump: true, MaxWallKicks: 1, Descend: true));
		return [.. plans];
	}

	public readonly List<Node> Nodes = [];
	public readonly List<Edge> Edges = [];
	public readonly List<SurfaceSpan> SurfaceSpans = [];
	public readonly List<Vector2> SurfacePoints = [];
	public readonly List<SurfaceEdge> SurfaceEdges = [];
	public readonly List<MovingPlatformAttachment> MovingPlatformAttachments = [];
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
	readonly List<List<Edge>> outgoingEdges = [];
	readonly Dictionary<int, List<Node>> landingRows = [];
	readonly Dictionary<int, List<int>> landingCandidateRows = [];
	readonly List<int> nodeSurfaceSpanIds = [];
	readonly Dictionary<SurfaceEdgeKey, SurfaceEdgeAccumulator> surfaceEdgeLookup = [];
	readonly List<List<SurfaceEdge>> outgoingSurfaceEdges = [];
	readonly Dictionary<int, (JumpPlan Plan, float Cost, int PlanIndex)> jumpCandidates = [];
	readonly Dictionary<int, (JumpPlan Plan, float Slide)> descentCandidates = [];

	// Discover descent-and-kick edges (drop into a shaft, wall-slide, kick into an under-overhang
	// pocket). Off until the runtime can execute them. The maneuver's the same wall-kick the
	// climb uses, entered by falling — see JumpPlan.Descend.
	internal static bool EmitDescentEdges = true;
	readonly PriorityQueue<int, float> pathFrontier = new();
	readonly Dictionary<int, Edge> pathCameFrom = [];
	readonly Dictionary<int, float> pathCostSoFar = [];
	readonly PriorityQueue<int, float> surfacePathFrontier = new();
	readonly Dictionary<int, SurfaceEdge> surfacePathCameFrom = [];
	readonly Dictionary<int, float> surfacePathCostSoFar = [];
	long pathQueries, pathExpandedNodes, pathEdgesExamined;
	long surfacePathQueries, surfacePathExpandedNodes, surfacePathEdgesExamined;

	public enum BuildPhase { Idle, Sampling, AddLandingNodes, WalkEdges, FallEdges, JumpEdges, RopeEdges, MovingPlatforms, WaterNodes, RoomExits, Ready }

	BuildPhase phase = BuildPhase.Idle;
	int revision;
	Game? rebuildGame;
	Room? rebuildRoom;
	RectInt sampleBounds;
	int sampleX;
	int sampleY;
	int nextJumpSourceId;
	int nextJumpPlanIndex;
	int nextDescentPlanIndex;
	bool currentSourceCanAttemptChainedWallJump;
	JumpBuildStage jumpBuildStage;
	long rebuildStartTimestamp;
	int rebuildFrameCount;

	enum JumpBuildStage { StartSource, Plans, CommitPlans, Descents, CommitDescents }

	// jumpthru actors in the room, snapshotted once per Tick so HasStandableSupport
	// doesn't re-scan (and mask-filter) every actor on each of its ~100k calls.
	static readonly List<Actor> jumpthruActors = [];
	readonly List<Actor> simulationCollisionActors = [];
	readonly List<Actor> simulationWindActors = [];
	readonly Dictionary<int, (Node Node, float Distance)> ropeAttachments = [];
	readonly List<Node> ropeAttachmentNodes = [];
	readonly Dictionary<int, MovingPlatformAttachmentCandidate> movingPlatformAttachments = [];
	readonly List<MovingPlatformAttachmentCandidate> movingPlatformAttachmentList = [];

	// --- profiling instrumentation (temporary) ---
	static bool collectBuildStats;
	static long collisionQueries;
	static long supportChecks;
	static long simsRun, simsReused, simSteps, simLanded, simStalled, simTimedOut, simOutOfBounds, landingSearchIterations;
	static long jumpSimTicks, landingSearchTicks;

	// per-plan coverage: where each JumpPlan lands and how often it wins an edge,
	// so we can see which plans own unique reach vs. overlap before trimming.
	struct PlanStat { public int Landed, Kept; public float MinReach, MaxReach, SumReach, MinDy, MaxDy; }
	static readonly PlanStat[] planStats = new PlanStat[JumpPlans.Length];
	readonly double[] phaseElapsedMs = new double[(int)BuildPhase.Ready + 1];
	readonly long[] phaseCollisionCounts = new long[(int)BuildPhase.Ready + 1];

	readonly record struct SurfaceEdgeKey(int From, int To, EdgeKind Kind, bool HasJump, JumpPlan Jump);
	readonly record struct JumpAttemptKey(Point2 From, JumpPlan Plan);
	readonly record struct JumpAttempt(bool Landed, Vector2 Landing, RectInt DependencyBounds);
	readonly record struct MovingPlatformAttachmentCandidate(Node Node, float RouteStart, float RouteEnd);

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

	readonly Dictionary<JumpAttemptKey, JumpAttempt> jumpAttemptCache = [];
	RectInt? rebuildDirtyBounds;
	int completedBuildActorFingerprint;
	int rebuildActorFingerprint;
	bool rebuildingIncrementally;

	public bool IsReady => phase == BuildPhase.Ready;
	public int Revision => revision;

	public Node? FindNearestNode(Vector2 position, float maxDistance, bool includeRoomExits = false, bool includeSwim = false)
	{
		Node? nearest = null;
		var nearestDistanceSquared = maxDistance * maxDistance;
		foreach (var node in Nodes)
		{
			if (!includeRoomExits && node.Kind == NodeKind.RoomExit)
				continue;
			if (!includeSwim && node.Kind == NodeKind.Swim)
				continue;

			var distanceSquared = NodeDistanceSquared(node, position);
			if (distanceSquared >= nearestDistanceSquared)
				continue;

			nearest = node;
			nearestDistanceSquared = distanceSquared;
		}
		return nearest;
	}

	public void FindNearestNodes(Vector2 position, float maxDistance, List<Node> results, bool includeRoomExits = false, bool includeSwim = false)
	{
		results.Clear();
		var maxDistanceSquared = maxDistance * maxDistance;
		foreach (var node in Nodes)
		{
			if (!includeRoomExits && node.Kind == NodeKind.RoomExit)
				continue;
			if (!includeSwim && node.Kind == NodeKind.Swim)
				continue;

			if (NodeDistanceSquared(node, position) >= maxDistanceSquared)
				continue;

			results.Add(node);
		}
		// Sort via a cached comparer reading a field, so the per-call closure (this runs every
		// nav frame) doesn't allocate a fresh delegate capturing `position`.
		nodeSortPosition = position;
		nodeSortComparison ??= (a, b) => NodeDistanceSquared(a, nodeSortPosition).CompareTo(NodeDistanceSquared(b, nodeSortPosition));
		results.Sort(nodeSortComparison);
	}

	Vector2 nodeSortPosition;
	Comparison<Node>? nodeSortComparison;

	// A swim node covers a whole water volume, so measure distance to its area (0 when the
	// point is inside it) rather than to its center. Lets a pawn in the water resolve to
	// its containing area, and a target above/in water resolve to the area beneath it.
	static float NodeDistanceSquared(Node node, Vector2 position)
	{
		if (node.Kind == NodeKind.Swim && node.Area is { } area)
		{
			var dx = MathF.Max(MathF.Max(area.Left - position.X, position.X - area.Right), 0);
			var dy = MathF.Max(MathF.Max(area.Top - position.Y, position.Y - area.Bottom), 0);
			return dx * dx + dy * dy;
		}
		return Vector2.DistanceSquared(position, node.Position);
	}

	public SurfacePoint? FindNearestSurface(Vector2 position, float maxDistance)
	{
		SurfacePoint? nearest = null;
		var nearestDistanceSquared = maxDistance * maxDistance;
		foreach (var span in SurfaceSpans)
		{
			var point = NearestSurfacePoint(span, position);
			var distanceSquared = Vector2.DistanceSquared(position, point);
			if (distanceSquared >= nearestDistanceSquared)
				continue;

			nearest = new SurfacePoint(span.Id, point, distanceSquared);
			nearestDistanceSquared = distanceSquared;
		}
		return nearest;
	}

	public Vector2 SurfacePointAtX(int spanId, float x)
		=> spanId >= 0 && spanId < SurfaceSpans.Count ? SurfacePointAtX(SurfaceSpans[spanId], x) : default;

	public Vector2 SurfacePointAtX(SurfaceSpan span, float x)
	{
		var first = SurfacePoints[span.PointStart];
		if (span.PointCount == 1 || x <= first.X)
			return first;
		var last = SurfacePoints[span.PointStart + span.PointCount - 1];
		if (x >= last.X)
			return last;

		var lo = 1;
		var hi = span.PointCount - 1;
		while (lo < hi)
		{
			var mid = (lo + hi) / 2;
			if (SurfacePoints[span.PointStart + mid].X < x)
				lo = mid + 1;
			else
				hi = mid;
		}
		var to = SurfacePoints[span.PointStart + lo];
		var from = SurfacePoints[span.PointStart + lo - 1];
		return Vector2.Lerp(from, to, (x - from.X) / (to.X - from.X));
	}

	Vector2 NearestSurfacePoint(SurfaceSpan span, Vector2 position)
		=> SurfacePointAtX(span, position.X);

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
		pathQueries++;
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
			pathExpandedNodes++;
			if (current == goalId)
			{
				BuildPath(startId, goalId, pathCameFrom, path);
				return true;
			}

			pathEdgesExamined += outgoingEdges[current].Count;
			foreach (var edge in outgoingEdges[current])
			{
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
		surfacePathQueries++;
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
			surfacePathExpandedNodes++;
			if (current == goalId)
			{
				BuildSurfacePath(startId, goalId, surfacePathCameFrom, path);
				return true;
			}

			surfacePathEdgesExamined += outgoingSurfaceEdges[current].Count;
			foreach (var edge in outgoingSurfaceEdges[current])
			{
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

	public void LogPathStats()
	{
		if (!NavGraphLogging() || pathQueries + surfacePathQueries == 0)
			return;

		Console.WriteLine($"[NavGraph] paths: {pathQueries} node queries, {pathExpandedNodes} nodes / {pathEdgesExamined} edges examined; {surfacePathQueries} surface queries, {surfacePathExpandedNodes} spans / {surfacePathEdgesExamined} edges examined");
	}

	internal void ResetPathStats()
	{
		pathQueries = pathExpandedNodes = pathEdgesExamined = 0;
		surfacePathQueries = surfacePathExpandedNodes = surfacePathEdgesExamined = 0;
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
			// Swimming is a normal option but not preferred: a per-edge surcharge makes a dry
			// walk/jump win a jumpable gap, while a real crossing (no land route) still routes
			// through the water since it's the only path.
			EdgeKind.Swim => distance + 120,
			// A long multi-kick maneuver: dependable but slow, so shorter routes win when they
			// exist while a well remains far cheaper than an unreliable chained-jump plan.
			EdgeKind.WellClimb => distance + 350,
			EdgeKind.Rope => distance + 120,
			EdgeKind.MovingPlatform => distance + 300,
			_ => distance
		};
	}

	internal float PathCost(IReadOnlyList<Edge> path)
	{
		var cost = 0f;
		foreach (var edge in path)
			cost += EdgeCost(edge);
		return cost;
	}

	float SurfaceEdgeCost(SurfaceEdge edge)
	{
		var from = SurfaceSpans[edge.From];
		var to = SurfaceSpans[edge.To];
		var fromX = (edge.FromLeft + edge.FromRight) * 0.5f;
		var toX = (edge.ToLeft + edge.ToRight) * 0.5f;
		var distance = Vector2.Distance(SurfacePointAtX(from, fromX), SurfacePointAtX(to, toX));
		return edge.Kind switch
		{
			EdgeKind.Walk => MathF.Max(1, distance),
			EdgeKind.Crawl => MathF.Max(1, distance) + 12,
			EdgeKind.Fall => distance + 20,
			EdgeKind.Jump => distance + 180,
			EdgeKind.RoomExit => distance + 5,
			EdgeKind.Rope => distance + 120,
			EdgeKind.MovingPlatform => distance + 300,
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

	public void Rebuild(Game game, Room room, RectInt? dirtyBounds = null)
	{
		var actorFingerprint = NavActorFingerprint(game);
		var canReuseJumpAttempts =
			rebuildRoom == room &&
			dirtyBounds.HasValue &&
			((phase == BuildPhase.Ready && completedBuildActorFingerprint == actorFingerprint) ||
			 (phase != BuildPhase.Ready && rebuildingIncrementally && rebuildActorFingerprint == actorFingerprint));
		if (canReuseJumpAttempts)
		{
			var dirty = dirtyBounds.GetValueOrDefault().Inflate(1);
			rebuildDirtyBounds = phase != BuildPhase.Ready && rebuildDirtyBounds is { } previous
				? previous.Conflate(dirty)
				: dirty;
			rebuildingIncrementally = true;
		}
		else
		{
			jumpAttemptCache.Clear();
			rebuildDirtyBounds = null;
			rebuildingIncrementally = false;
		}

		revision++;
		Nodes.Clear();
		Edges.Clear();
		SurfaceSpans.Clear();
		SurfacePoints.Clear();
		SurfaceEdges.Clear();
		MovingPlatformAttachments.Clear();
		nodeLookup.Clear();
		edgeLookup.Clear();
		outgoingEdges.Clear();
		surfaceEdgeLookup.Clear();
		outgoingSurfaceEdges.Clear();
		nodeSurfaceSpanIds.Clear();
		rebuildGame = game;
		rebuildRoom = room;
		rebuildActorFingerprint = actorFingerprint;
		sampleBounds = room.WorldBounds;
		sampleX = sampleBounds.Left + SampleStep / 2;
		sampleY = sampleBounds.Top + Math.Abs(DuckingPawnProbe.Top);
		nextJumpSourceId = 0;
		nextJumpPlanIndex = 0;
		nextDescentPlanIndex = 0;
		jumpBuildStage = JumpBuildStage.StartSource;
		collectBuildStats = VerboseBuildLog && NavGraphLogging();
		rebuildStartTimestamp = collectBuildStats ? Stopwatch.GetTimestamp() : 0;
		rebuildFrameCount = 0;
		collisionQueries = 0;
		supportChecks = 0;
		simsRun = simsReused = simSteps = simLanded = simStalled = simTimedOut = simOutOfBounds = landingSearchIterations = 0;
		jumpSimTicks = landingSearchTicks = 0;
		pathQueries = pathExpandedNodes = pathEdgesExamined = 0;
		surfacePathQueries = surfacePathExpandedNodes = surfacePathEdgesExamined = 0;
		for (var i = 0; i < planStats.Length; i++)
			planStats[i] = new PlanStat { MinReach = float.MaxValue, MaxReach = float.MinValue, MinDy = float.MaxValue, MaxDy = float.MinValue };
		Array.Clear(phaseElapsedMs);
		Array.Clear(phaseCollisionCounts);
		phase = BuildPhase.Sampling;
	}

	static int NavActorFingerprint(Game game)
	{
		var hash = new HashCode();
		foreach (var actor in game.Actors)
		{
			if (!actor.Mask.Has(Actor.Masks.Solid | Actor.Masks.Jumpthru) && actor is not Wind)
				continue;
			hash.Add(actor.GetType());
			if (actor is MovingPlatform platform)
			{
				hash.Add(platform.NavStartPosition);
				hash.Add(platform.Size);
				hash.Add(platform.TravelDistance);
				hash.Add(platform.Mode);
				hash.Add(platform.Speed);
				hash.Add(platform.StartFriction);
				hash.Add(platform.WaitTime);
			}
			else
				hash.Add(actor.Position);
			hash.Add(actor.Hitbox.Rect);
			hash.Add(actor.Mask);
			if (actor is Wind wind)
			{
				hash.Add(wind.Size);
				hash.Add(wind.Direction);
				hash.Add(wind.Acceleration);
				hash.Add(wind.MaxSpeed);
				hash.Add(wind.Targets);
			}
		}
		return hash.ToHashCode();
	}

	public void Tick(float budgetMs)
	{
		if (phase == BuildPhase.Ready || phase == BuildPhase.Idle)
			return;

		rebuildFrameCount++;
		var unbounded = budgetMs == float.MaxValue;
		var start = Stopwatch.GetTimestamp();

		// refresh the jumpthru-actor snapshot for this frame (actors are static
		// within a frame, so this matches reading game.Actors live per call)
		jumpthruActors.Clear();
		simulationCollisionActors.Clear();
		simulationWindActors.Clear();
		foreach (var actor in rebuildGame!.Actors)
		{
			if (actor.Mask.Has(Actor.Masks.Jumpthru))
				jumpthruActors.Add(actor);
			if (actor is not MovingPlatform && actor.Mask.Has(Actor.Masks.Solid | Actor.Masks.Jumpthru))
				simulationCollisionActors.Add(actor);
			if (actor is Wind)
				simulationWindActors.Add(actor);
		}

		do
		{
			var active = phase;
			var iterStart = collectBuildStats ? Stopwatch.GetTimestamp() : 0;
			var collisionsBefore = collectBuildStats ? collisionQueries : 0;

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
					BuildSurfaceSpans();
					phase = BuildPhase.FallEdges;
					break;
				case BuildPhase.FallEdges:
					BuildFallEdges(rebuildGame!);
					phase = BuildPhase.JumpEdges;
					break;
				case BuildPhase.JumpEdges:
					TickJumpEdges();
					break;
				case BuildPhase.RopeEdges:
					BuildRopeEdges(rebuildGame!);
					phase = BuildPhase.MovingPlatforms;
					break;
				case BuildPhase.MovingPlatforms:
					BuildMovingPlatformEdges(rebuildGame!);
					phase = BuildPhase.WaterNodes;
					break;
				case BuildPhase.WaterNodes:
					BuildWaterAreaNodes(rebuildGame!, rebuildRoom!);
					phase = BuildPhase.RoomExits;
					break;
				case BuildPhase.RoomExits:
					BuildRoomExitNodes(rebuildRoom!);
					BuildWellClimbEdges(rebuildGame!);
					phase = BuildPhase.Ready;
					break;
			}

			if (collectBuildStats)
			{
				phaseElapsedMs[(int)active] += Stopwatch.GetElapsedTime(iterStart).TotalMilliseconds;
				phaseCollisionCounts[(int)active] += collisionQueries - collisionsBefore;
			}
		} while (phase < BuildPhase.Ready && (unbounded || Stopwatch.GetElapsedTime(start).TotalMilliseconds < budgetMs));

		if (phase == BuildPhase.Ready)
		{
			BuildSurfaceEdges();
			completedBuildActorFingerprint = rebuildActorFingerprint;
			if (collectBuildStats)
				LogRebuildStats();
		}
	}

	// Per-rebuild stats/plan/graph dump. Off during the gym tune loop (the graph rebuilds
	// every trial there and the dump would bury the per-trial result lines).
	internal static bool VerboseBuildLog = true;

	static bool NavGraphLogging() => Environment.GetEnvironmentVariable("LOG_NAV_GRAPH") == "1";

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
		Console.WriteLine($"  jump sim: {simsRun} run, {simsReused} reused ({simLanded} landed, {simStalled} stalled, {simOutOfBounds} out of bounds, {simTimedOut} timed out), {simSteps} pawn updates, {landingSearchIterations} landing-search iters");
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

	// Debug: list nodes within `radius` of a point with their kind and outgoing edges.
	public void DumpNodesNear(Vector2 pos, float radius)
	{
		Console.WriteLine($"[NavGraph] nodes near ({pos.X:0},{pos.Y:0}) r{radius:0}:");
		foreach (var node in Nodes)
		{
			if (Vector2.Distance(node.Position, pos) > radius)
				continue;
			var outs = string.Join(", ", Edges.Where(e => e.From == node.Id).Select(e => $"{e.Kind}->{e.To}"));
			var ins = string.Join(", ", Edges.Where(e => e.To == node.Id).Select(e => $"{e.Kind}<-{e.From}"));
			var surface = TryGetNodeSurfaceSpan(node.Id, out var span)
				? $" span {span.Id}[{span.Left}..{span.Right}] y[{span.Top}..{span.Bottom}]"
				: "";
			Console.WriteLine($"  node {node.Id} {node.Kind} ({node.Position.X:0},{node.Position.Y:0}){surface} out[{outs}] in[{ins}]");
		}
	}

	// Order-independent fingerprint of the built graph. Counts/sums don't care
	// about node or edge ordering (which an optimization may shuffle), but they
	// flag real topology changes so we can diff before/after a refactor.
	void LogGraphSummary()
	{
		Span<int> nodeKinds = stackalloc int[5];
		Span<int> edgeKinds = stackalloc int[(int)EdgeKind.MovingPlatform + 1];
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
		Console.WriteLine($"  nodes: Ground {nodeKinds[(int)NodeKind.Ground]}, Landing {nodeKinds[(int)NodeKind.Landing]}, Ledge {nodeKinds[(int)NodeKind.Ledge]}, RoomExit {nodeKinds[(int)NodeKind.RoomExit]}, Swim {nodeKinds[(int)NodeKind.Swim]}");
		Console.WriteLine($"  edges: Walk {edgeKinds[(int)EdgeKind.Walk]}, Crawl {edgeKinds[(int)EdgeKind.Crawl]}, Fall {edgeKinds[(int)EdgeKind.Fall]}, Jump {edgeKinds[(int)EdgeKind.Jump]}, RoomExit {edgeKinds[(int)EdgeKind.RoomExit]}, Swim {edgeKinds[(int)EdgeKind.Swim]}, WellClimb {edgeKinds[(int)EdgeKind.WellClimb]}, Rope {edgeKinds[(int)EdgeKind.Rope]}, MovingPlatform {edgeKinds[(int)EdgeKind.MovingPlatform]}");
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
		var slopedSpans = 0;
		var totalNodesOnSpans = 0;
		foreach (var span in SurfaceSpans)
		{
			if (span.RequiresDucking)
				duckingSpans++;
			if (span.NodeCount == 1)
				singletonSpans++;
			if (span.IsSloped)
				slopedSpans++;
			totalNodesOnSpans += span.NodeCount;
		}

		var walkPairs = new HashSet<(int From, int To)>();
		var crawlPairs = new HashSet<(int From, int To)>();
		var fallPairs = new HashSet<(int From, int To)>();
		var jumpPairs = new HashSet<(int From, int To)>();
		var ropePairs = new HashSet<(int From, int To)>();
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
				case EdgeKind.Rope:
					ropePairs.Add(pair);
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

		Span<int> surfaceEdgeKinds = stackalloc int[(int)EdgeKind.MovingPlatform + 1];
		var jumpSurfaceNodeEdges = 0;
		foreach (var edge in SurfaceEdges)
		{
			surfaceEdgeKinds[(int)edge.Kind]++;
			if (edge.Kind == EdgeKind.Jump)
				jumpSurfaceNodeEdges += edge.NodeEdgeCount;
		}

		if (NavGraphLogging()) {
			var avgNodesPerSpan = SurfaceSpans.Count > 0 ? totalNodesOnSpans / (float)SurfaceSpans.Count : 0;
			var avgJumpNodesPerSurfaceEdge = surfaceEdgeKinds[(int)EdgeKind.Jump] > 0 ? jumpSurfaceNodeEdges / (float)surfaceEdgeKinds[(int)EdgeKind.Jump] : 0;
			Console.WriteLine($"  surfaces: {SurfaceSpans.Count} spans ({slopedSpans} sloped, {duckingSpans} ducking, {singletonSpans} singleton), avg {avgNodesPerSpan:0.0} nodes/span");
			Console.WriteLine($"  surface edge collapse: Walk {edgeKinds[(int)EdgeKind.Walk]}->{walkPairs.Count}, Crawl {edgeKinds[(int)EdgeKind.Crawl]}->{crawlPairs.Count}, Fall {edgeKinds[(int)EdgeKind.Fall]}->{fallPairs.Count}, Jump {edgeKinds[(int)EdgeKind.Jump]}->{jumpPairs.Count} span pairs / {jumpPlanPairs.Count} plan pairs, Rope {edgeKinds[(int)EdgeKind.Rope]}->{ropePairs.Count}");
			Console.WriteLine($"  surface interval edges: Walk {surfaceEdgeKinds[(int)EdgeKind.Walk]}, Crawl {surfaceEdgeKinds[(int)EdgeKind.Crawl]}, Fall {surfaceEdgeKinds[(int)EdgeKind.Fall]}, Jump {surfaceEdgeKinds[(int)EdgeKind.Jump]} (avg {avgJumpNodesPerSurfaceEdge:0.0} point jump edges), Rope {surfaceEdgeKinds[(int)EdgeKind.Rope]}");
			if (jumpPairs.Count > 0)
				Console.WriteLine($"  jump interval pressure: {multiJumpPairs}/{jumpPairs.Count} span pairs have multiple point edges, max {maxJumpEdgesPerPair}");
			if (unmappedEdges > 0)
				Console.WriteLine($"  surface unmapped nav edges: {unmappedEdges}");
		}
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
				{
					// Skip footing that's underwater: standing there is really swimming, and
					// the dry-land Walk/Jump edges built from it would be physically wrong.
					// The swim-area nodes cover the water; footing at/above the surface stays.
					if (IsWaterTileAtWorld(rebuildRoom!, position + new Point2(0, -Game.TileSize / 2)))
						continue;
					AddNode(position, IsLedge(rebuildGame!, position) ? NodeKind.Ledge : NodeKind.Ground, requiresDucking: requiresDucking);
				}
			}
			sampleX = sampleBounds.Left + SampleStep / 2;
			sampleY++;
			return;
		}
		phase = BuildPhase.AddLandingNodes;
	}

	void TickJumpEdges()
	{
		while (nextJumpSourceId < Nodes.Count)
		{
			var from = Nodes[nextJumpSourceId];
			switch (jumpBuildStage)
			{
				case JumpBuildStage.StartSource:
					if (from.RequiresDucking)
					{
						nextJumpSourceId++;
						continue;
					}
					jumpCandidates.Clear();
					nextJumpPlanIndex = 0;
					currentSourceCanAttemptChainedWallJump = CanAttemptChainedWallJump(rebuildGame!, from.Position);
					jumpBuildStage = JumpBuildStage.Plans;
					continue;

				case JumpBuildStage.Plans:
					while (nextJumpPlanIndex < JumpPlans.Length)
					{
						var pi = nextJumpPlanIndex++;
						var plan = JumpPlans[pi];
						if (plan.MaxWallKicks > 1 && !currentSourceCanAttemptChainedWallJump)
							continue;
						SimulateJumpPlan(from, plan, pi);
						return;
					}
					jumpBuildStage = JumpBuildStage.CommitPlans;
					continue;

				case JumpBuildStage.CommitPlans:
					foreach (var (toId, jump) in jumpCandidates)
					{
						if (CullPitFallJumps && !ValidateJumpWithController(from, Nodes[toId], jump.Plan))
							continue;
						if (AddEdge(from.Id, toId, EdgeKind.Jump, jump.Plan) && collectBuildStats)
							planStats[jump.PlanIndex].Kept++;
					}
					if (EmitDescentEdges && from.Kind == NodeKind.Ledge)
					{
						descentCandidates.Clear();
						nextDescentPlanIndex = 0;
						jumpBuildStage = JumpBuildStage.Descents;
					}
					else
						FinishJumpSource();
					continue;

				case JumpBuildStage.Descents:
					if (nextDescentPlanIndex < DescentPlans.Length)
					{
						SimulateDescentPlan(from, DescentPlans[nextDescentPlanIndex++]);
						return;
					}
					jumpBuildStage = JumpBuildStage.CommitDescents;
					continue;

				case JumpBuildStage.CommitDescents:
					foreach (var (toId, candidate) in descentCandidates)
						AddEdge(from.Id, toId, EdgeKind.Jump, candidate.Plan);
					FinishJumpSource();
					continue;
			}
		}

		phase = BuildPhase.RopeEdges;
	}

	void SimulateJumpPlan(Node from, JumpPlan plan, int planIndex)
	{
		var t0 = collectBuildStats ? Stopwatch.GetTimestamp() : 0;
		var simulated = TrySimulateJump(rebuildGame!, from, plan, out var landing);
		if (collectBuildStats)
			jumpSimTicks += Stopwatch.GetTimestamp() - t0;
		if (!simulated)
			return;

		if (collectBuildStats)
		{
			var reach = MathF.Abs(landing.X - from.Position.X);
			var dy = landing.Y - from.Position.Y;
			ref var stats = ref planStats[planIndex];
			stats.Landed++;
			stats.SumReach += reach;
			if (reach < stats.MinReach) stats.MinReach = reach;
			if (reach > stats.MaxReach) stats.MaxReach = reach;
			if (dy < stats.MinDy) stats.MinDy = dy;
			if (dy > stats.MaxDy) stats.MaxDy = dy;
		}

		var t1 = collectBuildStats ? Stopwatch.GetTimestamp() : 0;
		var to = FindNearestLandingNode(from.Id, landing, SimLandingTolerance);
		if (collectBuildStats)
			landingSearchTicks += Stopwatch.GetTimestamp() - t1;
		if (!to.HasValue || HasEdge(from.Id, to.Value.Id))
			return;

		var cost = JumpPlanCost(from, to.Value, plan);
		if (!jumpCandidates.TryGetValue(to.Value.Id, out var existing) || cost < existing.Cost)
			jumpCandidates[to.Value.Id] = (plan, cost, planIndex);
	}

	void SimulateDescentPlan(Node from, JumpPlan plan)
	{
		var t0 = collectBuildStats ? Stopwatch.GetTimestamp() : 0;
		var simulated = TrySimulateJump(rebuildGame!, from, plan, out var landing);
		if (collectBuildStats)
			jumpSimTicks += Stopwatch.GetTimestamp() - t0;
		if (!simulated)
			return;

		var t1 = collectBuildStats ? Stopwatch.GetTimestamp() : 0;
		var to = FindNearestLandingNode(from.Id, landing, SimLandingTolerance);
		if (collectBuildStats)
			landingSearchTicks += Stopwatch.GetTimestamp() - t1;
		if (!to.HasValue || HasEdge(from.Id, to.Value.Id))
			return;
		if (!descentCandidates.TryGetValue(to.Value.Id, out var existing) || plan.RunupTime < existing.Slide)
			descentCandidates[to.Value.Id] = (plan, plan.RunupTime);
	}

	void FinishJumpSource()
	{
		nextJumpSourceId++;
		jumpBuildStage = JumpBuildStage.StartSource;
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
		outgoingEdges.Add([]);
		nodeLookup[position] = id;
		return id;
	}

	readonly List<int> swimNodeIds = [];
	static float RopeAttachmentMaxHorizontal => 56;
	static float RopeAttachmentVerticalGrace => 12;
	static float RopeAttachmentMaxRise => Game.TileSize * 3;
	static float MovingPlatformHorizontalReach => 56;
	static float MovingPlatformMaxRise => 30;
	static float MovingPlatformMaxDrop => 64;

	void BuildRopeEdges(Game game)
	{
		foreach (var actor in game.Actors)
		{
			if (actor is not Rope { Broken: false, StartCoiled: false } rope || rope.ActorId == null)
				continue;

			ropeAttachments.Clear();
			var ropeX = rope.Position.X + rope.Size.X * 0.5f;
			var ropeTop = rope.Position.Y;
			var ropeBottom = rope.Position.Y + rope.Size.Y;
			foreach (var node in Nodes)
			{
				var spanId = NodeSurfaceSpanId(node.Id);
				if (spanId < 0 || node.RequiresDucking)
					continue;

				var grabY = node.Position.Y + PawnProbe.Center.Y;
				if (grabY < ropeTop - RopeAttachmentVerticalGrace || grabY > ropeBottom + RopeAttachmentMaxRise)
					continue;

				var distance = MathF.Abs(node.Position.X - ropeX);
				if (distance > RopeAttachmentMaxHorizontal)
					continue;
				if (!ropeAttachments.TryGetValue(spanId, out var existing) || distance < existing.Distance)
					ropeAttachments[spanId] = (node, distance);
			}

			ropeAttachmentNodes.Clear();
			foreach (var attachment in ropeAttachments.Values)
				ropeAttachmentNodes.Add(attachment.Node);

			for (var i = 0; i < ropeAttachmentNodes.Count; i++)
				for (var j = i + 1; j < ropeAttachmentNodes.Count; j++)
				{
					var a = ropeAttachmentNodes[i];
					var b = ropeAttachmentNodes[j];
					if (MathF.Abs(a.Position.Y - b.Position.Y) < Game.TileSize * 2)
						continue;
					AddEdge(a.Id, b.Id, EdgeKind.Rope, connectorActorId: rope.ActorId);
					AddEdge(b.Id, a.Id, EdgeKind.Rope, connectorActorId: rope.ActorId);
				}
		}
	}

	void BuildMovingPlatformEdges(Game game)
	{
		foreach (var actor in game.Actors)
		{
			if (actor is not MovingPlatform { SupportsPlatformNav: true } platform || platform.ActorId == null)
				continue;

			movingPlatformAttachments.Clear();
			var platformTop = platform.NavStartPosition.Y;
			var routeLength = platform.NavRouteLength;
			var platformWidth = platform.Size.X;
			foreach (var node in Nodes)
			{
				var spanId = NodeSurfaceSpanId(node.Id);
				if (spanId < 0 || node.RequiresDucking)
					continue;

				var platformBelow = platformTop - node.Position.Y;
				if (platformBelow > MovingPlatformMaxDrop || platformBelow < -MovingPlatformMaxRise)
					continue;

				var routeStart = Math.Clamp(
					node.Position.X - platform.NavStartPosition.X - platformWidth - MovingPlatformHorizontalReach,
					0,
					routeLength);
				var routeEnd = Math.Clamp(
					node.Position.X - platform.NavStartPosition.X + MovingPlatformHorizontalReach,
					0,
					routeLength);
				if (routeStart > routeEnd ||
					node.Position.X < platform.NavStartPosition.X - MovingPlatformHorizontalReach ||
					node.Position.X > platform.NavStartPosition.X + routeLength + platformWidth + MovingPlatformHorizontalReach)
					continue;

				var attachment = new MovingPlatformAttachmentCandidate(node, routeStart, routeEnd);
				if (!movingPlatformAttachments.TryGetValue(spanId, out var existing) ||
					routeEnd - routeStart > existing.RouteEnd - existing.RouteStart)
					movingPlatformAttachments[spanId] = attachment;
			}

			movingPlatformAttachmentList.Clear();
			foreach (var attachment in movingPlatformAttachments.Values)
			{
				movingPlatformAttachmentList.Add(attachment);
				MovingPlatformAttachments.Add(new(platform.ActorId, attachment.Node.Id, attachment.RouteStart, attachment.RouteEnd));
			}

			for (var i = 0; i < movingPlatformAttachmentList.Count; i++)
				for (var j = 0; j < movingPlatformAttachmentList.Count; j++)
				{
					if (i == j)
						continue;
					var from = movingPlatformAttachmentList[i];
					var to = movingPlatformAttachmentList[j];
					var direction = Math.Sign(
						(to.RouteStart + to.RouteEnd) -
						(from.RouteStart + from.RouteEnd));
					if (direction == 0)
						continue;

					AddEdge(
						from.Node.Id,
						to.Node.Id,
						EdgeKind.MovingPlatform,
						connectorActorId: platform.ActorId,
						platformRide: new(
							from.RouteStart,
							from.RouteEnd,
							to.RouteStart,
							to.RouteEnd,
							direction));
				}
		}
	}

	// Minimum length of the open face two swim areas must share to be joined by a portal
	// (so a pawn can actually fit through the gap between the obstacles that split them).
	static int SwimPortalMinSpan => Game.TileSize;
	// How far above a water surface a standable node can sit and still be dropped in from
	// (enter) or hopped out onto (exit). Exit tracks the pawn's surface-hop reach.
	static int SwimEnterDropMax => 2 * Game.TileSize;
	static int SwimExitHopMax => 40;

	int AddSwimNode(RectInt area)
	{
		var id = Nodes.Count;
		var center = new Vector2(area.X + area.Width / 2f, area.Y + area.Height / 2f);
		Nodes.Add(new Node(id, center, NodeKind.Swim, Area: area));
		outgoingEdges.Add([]);
		swimNodeIds.Add(id);
		return id;
	}

	// Portal edges between swim areas that share a wide-enough open face. Areas from the
	// same connected pool touch face-to-face where the greedy decomposition split them;
	// areas separated by solid obstacles don't touch, so they stay unconnected.
	void BuildWaterEdges(Game game, Room room)
	{
		for (var i = 0; i < swimNodeIds.Count; i++)
			for (var j = i + 1; j < swimNodeIds.Count; j++)
			{
				if (Nodes[swimNodeIds[i]].Area is not { } a || Nodes[swimNodeIds[j]].Area is not { } b)
					continue;
				if (!SwimAreaTraversable(game, a) || !SwimAreaTraversable(game, b))
					continue;
				if (!SwimAreasConnected(a, b))
					continue;
				AddEdge(swimNodeIds[i], swimNodeIds[j], EdgeKind.Swim);
				AddEdge(swimNodeIds[j], swimNodeIds[i], EdgeKind.Swim);
			}

		// Bridge the swim maze to the standable graph: a land node at a water rim can be
		// dropped/stepped into (enter), and one just above the surface can be hopped out
		// onto (exit). Without these the swim areas stay their own island.
		foreach (var swimId in swimNodeIds)
		{
			if (Nodes[swimId].Area is not { } area || !SwimAreaTraversable(game, area))
				continue;
			foreach (var node in Nodes)
			{
				if (node.Kind is NodeKind.Swim or NodeKind.RoomExit)
					continue;
				if (CanEnterWater(game, room, node, area))
					AddEdge(node.Id, swimId, EdgeKind.Swim);
				if (CanExitWater(game, room, area, node))
					AddEdge(swimId, node.Id, EdgeKind.Swim);
			}
		}

	}

	// A one-tile-tall water area is only passable as a crawl tunnel, which needs solid floor
	// to duck-walk on. Floorless one-tile gaps (open water above and below) can't be swum
	// through standing nor crawled, so they aren't real passages — leave them unconnected.
	static bool SwimAreaTraversable(Game game, RectInt area)
	{
		if (area.Height > Game.TileSize)
			return true;
		// Crawlable only with a continuous solid floor under the whole span; a single gap
		// below means open water there, so it's a floorless squeeze rather than a tunnel.
		for (var x = area.Left; x < area.Right; x += Game.TileSize)
			if (!OverlapsCollision(game, new RectInt(x + 1, area.Bottom, Game.TileSize - 2, 2)))
				return false;
		return true;
	}

	// The area's top is an actual air-water surface at this x — open air just above it. If solid
	// caps the water there, land beside it is walled off from the water (no drop-in, and an
	// "exit" would be a hop into rock); if more water sits above, it's an interior boundary
	// between stacked areas, not a place the pawn can break the surface.
	static bool SwimSurfaceOpen(Game game, Room room, RectInt area, float worldX)
	{
		var probeX = Math.Clamp((int)worldX, area.Left + 2, area.Right - 2);
		if (OverlapsCollision(game, new RectInt(probeX - 2, area.Top - Game.TileSize, 4, Game.TileSize)))
			return false;
		return !IsWaterTileAtWorld(room, new Point2(probeX, area.Top - Game.TileSize / 2));
	}

	// A land node at/just above the water rim, over or beside an open surface, so the pawn can
	// step off and sink in.
	static bool CanEnterWater(Game game, Room room, Node node, RectInt area)
	{
		var x = node.Position.X;
		var y = node.Position.Y;
		return x >= area.Left - Game.TileSize && x <= area.Right + Game.TileSize
			&& y <= area.Top + 4 && y >= area.Top - SwimEnterDropMax
			&& SwimSurfaceOpen(game, room, area, x);
	}

	// A land node at/above an open water surface within hop-out reach. Rim nodes sit at the
	// waterline (climb out sideways) up to a hop above.
	static bool CanExitWater(Game game, Room room, RectInt area, Node node)
	{
		var x = node.Position.X;
		var y = node.Position.Y;
		return x >= area.Left - Game.TileSize && x <= area.Right + Game.TileSize
			&& y <= area.Top + 2 && area.Top - y <= SwimExitHopMax
			&& SwimSurfaceOpen(game, room, area, x);
	}

	static bool IsWaterTileAtWorld(Room room, Point2 world)
	{
		var local = world - room.WorldBounds.TopLeft;
		var tile = new Point2(
			(int)MathF.Floor(local.X / (float)Game.TileSize),
			(int)MathF.Floor(local.Y / (float)Game.TileSize));
		var entry = room.GetEntry(tile) ?? room.GetBackgroundEntry(tile);
		return entry is { Substance: Factory.WaterSubstance };
	}

	static bool SwimAreasConnected(RectInt a, RectInt b)
	{
		// Side-by-side: touch along a vertical face, overlap measured in Y.
		if (a.Right == b.Left || b.Right == a.Left)
			return Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Top, b.Top) >= SwimPortalMinSpan;
		// Stacked: touch along a horizontal face, overlap measured in X.
		if (a.Bottom == b.Top || b.Bottom == a.Top)
			return Math.Min(a.Right, b.Right) - Math.Max(a.Left, b.Left) >= SwimPortalMinSpan;
		return false;
	}

	// Turn the room's water tiles into a small set of rectangular swim-area nodes. Greedy
	// maximal-rectangle decomposition (grow right across the row, then down while the full
	// width stays water) so an open pool collapses to a handful of areas, not one per tile.
	void BuildWaterAreaNodes(Game game, Room room)
	{
		var w = room.Size.X;
		var h = room.Size.Y;
		if (w <= 0 || h <= 0)
			return;

		swimNodeIds.Clear();
		var top = room.WorldBounds.TopLeft;
		var water = new bool[w, h];
		var used = new bool[w, h];
		var any = false;
		for (var y = 0; y < h; y++)
			for (var x = 0; x < w; x++)
			{
				var tile = new Point2(x, y);
				var entry = room.GetEntry(tile) ?? room.GetBackgroundEntry(tile);
				if (entry is { Substance: Factory.WaterSubstance })
				{
					water[x, y] = true;
					any = true;
				}
			}
		if (NavGraphLogging())
			Console.WriteLine($"[NavGraph] water tiles: {(any ? "found" : "none")} in {room.Id}");
		if (!any)
			return;

		for (var y = 0; y < h; y++)
			for (var x = 0; x < w; x++)
			{
				if (!water[x, y] || used[x, y])
					continue;

				var x2 = x;
				while (x2 + 1 < w && water[x2 + 1, y] && !used[x2 + 1, y])
					x2++;

				var y2 = y;
				while (y2 + 1 < h && RowIsFreeWater(water, used, x, x2, y2 + 1))
					y2++;

				for (var yi = y; yi <= y2; yi++)
					for (var xi = x; xi <= x2; xi++)
						used[xi, yi] = true;

				AddSwimNode(new RectInt(
					top.X + x * Game.TileSize,
					top.Y + y * Game.TileSize,
					(x2 - x + 1) * Game.TileSize,
					(y2 - y + 1) * Game.TileSize));
			}

		BuildWaterEdges(game, room);
	}

	static bool RowIsFreeWater(bool[,] water, bool[,] used, int x0, int x1, int y)
	{
		for (var x = x0; x <= x1; x++)
			if (!water[x, y] || used[x, y])
				return false;
		return true;
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
		SurfacePoints.Clear();
		nodeSurfaceSpanIds.Clear();
		for (var i = 0; i < Nodes.Count; i++)
			nodeSurfaceSpanIds.Add(-1);

		var parents = new int[Nodes.Count];
		for (var i = 0; i < Nodes.Count; i++)
			parents[i] = Nodes[i].Kind == NodeKind.RoomExit ? -1 : i;

		foreach (var edge in Edges)
		{
			if (edge.Kind is not (EdgeKind.Walk or EdgeKind.Crawl) ||
				Nodes[edge.From].RequiresDucking != Nodes[edge.To].RequiresDucking)
				continue;
			UnionSurfaceNodes(parents, edge.From, edge.To);
		}

		var groups = new Dictionary<int, List<int>>();
		for (var i = 0; i < parents.Length; i++)
		{
			if (parents[i] < 0)
				continue;
			var root = FindSurfaceRoot(parents, i);
			if (!groups.TryGetValue(root, out var group))
			{
				group = [];
				groups[root] = group;
			}
			group.Add(i);
		}

		foreach (var group in groups.Values)
			AddSurfaceSpan(group);
	}

	static int FindSurfaceRoot(int[] parents, int node)
	{
		while (parents[node] != node)
		{
			parents[node] = parents[parents[node]];
			node = parents[node];
		}
		return node;
	}

	static void UnionSurfaceNodes(int[] parents, int a, int b)
	{
		a = FindSurfaceRoot(parents, a);
		b = FindSurfaceRoot(parents, b);
		if (a != b)
			parents[b] = a;
	}

	void AddSurfaceSpan(List<int> nodes)
	{
		nodes.Sort((a, b) =>
		{
			var x = Nodes[a].Position.X.CompareTo(Nodes[b].Position.X);
			return x != 0 ? x : Nodes[a].Position.Y.CompareTo(Nodes[b].Position.Y);
		});

		var pointStart = SurfacePoints.Count;
		var top = int.MaxValue;
		var bottom = int.MinValue;
		foreach (var nodeId in nodes)
		{
			var point = Nodes[nodeId].Position;
			if (SurfacePoints.Count > pointStart && SurfacePoints[^1].X == point.X)
				continue;
			SurfacePoints.Add(point);
			top = Math.Min(top, (int)point.Y);
			bottom = Math.Max(bottom, (int)point.Y);
		}

		var id = SurfaceSpans.Count;
		SurfaceSpans.Add(new(
			id,
			(int)SurfacePoints[pointStart].X,
			(int)SurfacePoints[^1].X,
			top,
			bottom,
			Nodes[nodes[0]].RequiresDucking,
			nodes.Count,
			pointStart,
			SurfacePoints.Count - pointStart));

		foreach (var nodeId in nodes)
			nodeSurfaceSpanIds[nodeId] = id;
	}

	void BuildSurfaceEdges()
	{
		SurfaceEdges.Clear();
		surfaceEdgeLookup.Clear();
		outgoingSurfaceEdges.Clear();
		for (var i = 0; i < SurfaceSpans.Count; i++)
			outgoingSurfaceEdges.Add([]);

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
			var edge = new SurfaceEdge(
				key.From,
				key.To,
				key.Kind,
				accumulator.FromLeft,
				accumulator.FromRight,
				accumulator.ToLeft,
				accumulator.ToRight,
				key.HasJump ? key.Jump : null,
				accumulator.Count);
			SurfaceEdges.Add(edge);
			outgoingSurfaceEdges[edge.From].Add(edge);
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
		if (collectBuildStats)
			collisionQueries++;
		return game.OverlapsRoomCollision(bounds);
	}

	bool IsStandable(Game game, Point2 position)
	{
		return TryGetStandableProbe(game, position, out _);
	}

	bool TryGetStandableProbe(Game game, Point2 position, out bool requiresDucking)
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
		if (collectBuildStats)
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

	bool IsLedge(Game game, Point2 position)
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
				AddFallEdge(node, leftLanding);

			if (!IsStandable(game, (Point2)node.Position + new Point2(SampleStep, 0)) &&
				FindFallLanding(game, node, SampleStep) is { } rightLanding)
				AddFallEdge(node, rightLanding);
		}
	}

	void AddFallEdge(Node from, Node to)
	{
		AddEdge(from.Id, to.Id, EdgeKind.Fall);
		var delta = from.Position - to.Position;
		if (delta.X != 0 &&
			delta.Y == -Game.TileSize * 3 &&
			MathF.Abs(delta.X) <= FallMaxHorizontal &&
			TryGetNodeSurfaceSpan(from.Id, out var fromSpan) && fromSpan.NodeCount == 1 &&
			TryGetNodeSurfaceSpan(to.Id, out var toSpan) && toSpan.NodeCount == 1)
		{
			var direction = Math.Sign(delta.X);
			AddEdge(to.Id, from.Id, EdgeKind.Jump, new(direction, 0, 0.18f, direction, 99));
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
				if (collectBuildStats)
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

	bool AddEdge(int from, int to, EdgeKind kind, JumpPlan? jump = null, string? connectorActorId = null, MovingPlatformRide? platformRide = null)
	{
		if (!edgeLookup.Add((from, to)))
			return false;
		var edge = new Edge(from, to, kind, jump, connectorActorId, platformRide);
		Edges.Add(edge);
		outgoingEdges[from].Add(edge);
		return true;
	}

	Node? FindFallLanding(Game game, Node from, int offsetX)
	{
		var start = (Point2)from.Position + new Point2(offsetX, 0);
		var direction = Math.Sign(offsetX);
		for (var pass = 0; pass < 2; pass++)
		{
			var maxHorizontal = pass == 0 ? 0 : FallMaxHorizontal;
			Node? best = null;
			foreach (var to in Nodes)
			{
				var deltaX = (int)to.Position.X - start.X;
				if (to.Position.Y <= from.Position.Y || deltaX * direction < 0 || Math.Abs(deltaX) > maxHorizontal)
					continue;

				if (best is { } current &&
					(to.Position.Y > current.Position.Y ||
						to.Position.Y == current.Position.Y &&
						Math.Abs(deltaX) >= Math.Abs(current.Position.X - start.X)))
					continue;

				if (!FallProbeClear(game, start, (Point2)to.Position, from.RequiresDucking))
					continue;

				best = to;
			}
			if (best.HasValue)
				return best;
		}
		return null;
	}

	EdgeKind? WalkEdgeKind(Game game, Node from, Node to)
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

	bool FallProbeClear(Game game, Point2 from, Point2 to, bool requiresDucking)
	{
		var steps = Math.Max(1, (int)MathF.Ceiling(Vector2.Distance(from, to) / 2));
		for (var i = 0; i < steps; i++)
		{
			var position = (Point2)Vector2.Lerp(from, to, i / (float)steps);
			if (requiresDucking && !OverlapsCollision(game, PawnProbe + position))
				requiresDucking = false;
			if (OverlapsCollision(game, (requiresDucking ? DuckingPawnProbe : PawnProbe) + position))
				return false;
		}

		return true;
	}

	bool TrySimulateJump(Game game, Node from, JumpPlan plan, out Vector2 landing)
	{
		var key = new JumpAttemptKey((Point2)from.Position, plan);
		if (rebuildDirtyBounds is { } dirty &&
			jumpAttemptCache.TryGetValue(key, out var cached) &&
			!cached.DependencyBounds.Overlaps(dirty))
		{
			simsReused++;
			landing = cached.Landing;
			return cached.Landed;
		}

		using var _ = game.BeginSimulation();
		var sim = new JumpSimulationPawn(game, from.Position, plan, PawnProbe, simulationCollisionActors, simulationWindActors);
		var landed = sim.Run(out landing);
		jumpAttemptCache[key] = new(landed, landing, sim.DependencyBounds.Inflate(1));
		return landed;
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

		public JumpValidationPawn(Game game, PlatformNavGraph graph, Node from, Node to, JumpPlan plan, float entryVx, Vector2? startPosition = null)
		{
			this.game = game;
			Game = game;
			Position = (Point2)(startPosition ?? from.Position);
			Hitbox = new(graph.PawnProbe);
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
			return new(
				MoveX: moveX,
				MoveY: moveY,
				JumpPressed: jumpPressed,
				JumpDown: nav.JumpHeld,
				DashPressed: false,
				AllowWallJump: nav.ActiveEdgeAllowsWallJump);
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

	// Wells: vertical shafts with solid walls close on both sides, too tall for any jump plan,
	// climbable by chained wall kicks. Emits WellClimb edges from a node at a well's mouth to
	// nodes at its top opening, so A* can route up shafts (e.g. a spiral's ascending arms).
	const int WellClimbMinRise = 48;      // below this a jump/wall-kick plan already covers it
	const int WellClimbMaxRise = 420;
	const int WellClimbMaxLateral = 40;   // top node must sit close to the well's column
	const int WellClimbWallReach = 28;    // both walls within this of the well center

	void BuildWellClimbEdges(Game game)
	{
		for (var bi = 0; bi < Nodes.Count; bi++)
		{
			var bottom = Nodes[bi];
			if (bottom.Kind is not (NodeKind.Ground or NodeKind.Ledge or NodeKind.Landing))
				continue;

			// A well must rise from just above this node's head; measure how far up it goes.
			var wellTop = MeasureWellAbove(game, bottom.Position);
			if (bottom.Position.Y - wellTop < WellClimbMinRise)
				continue;

			for (var ti = 0; ti < Nodes.Count; ti++)
			{
				if (ti == bi)
					continue;
				var top = Nodes[ti];
				if (top.Kind is not (NodeKind.Ground or NodeKind.Ledge or NodeKind.Landing))
					continue;
				var rise = bottom.Position.Y - top.Position.Y;
				if (rise < WellClimbMinRise || rise > WellClimbMaxRise)
					continue;
				if (MathF.Abs(top.Position.X - bottom.Position.X) > WellClimbMaxLateral)
					continue;
				// The well must reach at least up to the top node's level for the climb to
				// exit there.
				if (wellTop > top.Position.Y + Game.TileSize)
					continue;
				if (HasEdge(bottom.Id, top.Id))
					continue;
				AddEdge(bottom.Id, top.Id, EdgeKind.WellClimb);
			}
		}
	}

	// Lowest Y (highest point) still inside a continuous walled shaft above `from`, or
	// from.Y when there's no well. Scans upward a tile at a time requiring open space at
	// the column with solid walls within reach on both sides.
	float MeasureWellAbove(Game game, Vector2 from)
	{
		var x = (int)from.X;
		var top = from.Y;
		// start above head height so the mouth (where side walls may be the corridor's own
		// walls or absent) doesn't fail the scan
		for (var y = (int)from.Y - Game.TileSize * 3; ; y -= Game.TileSize)
		{
			if (game.OverlapsRoomCollision(new RectInt(x - 3, y, 6, Game.TileSize)))
				break;
			var wallLeft = false;
			var wallRight = false;
			for (var d = 4; d <= WellClimbWallReach && !(wallLeft && wallRight); d += 4)
			{
				if (!wallLeft && game.OverlapsRoomCollision(new RectInt(x - d - 2, y, 2, Game.TileSize)))
					wallLeft = true;
				if (!wallRight && game.OverlapsRoomCollision(new RectInt(x + d, y, 2, Game.TileSize)))
					wallRight = true;
			}
			if (!wallLeft || !wallRight)
				break;
			top = y;
		}
		return top;
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
		readonly List<Actor> simulationCollisionActors;
		readonly List<Actor> simulationWindActors;
		readonly RectInt pawnProbe;
		float elapsed;
		bool leftStart;
		bool leftGround;
		JumpPlanExecution.State jump;
		int lastX;
		int stalledFrames;
		public RectInt DependencyBounds { get; private set; }

		internal override List<Actor> CollisionActors => simulationCollisionActors;
		internal override List<Actor> WindActors => simulationWindActors;

		public JumpSimulationPawn(Game game, Vector2 from, JumpPlan plan, RectInt pawnProbe, List<Actor> simulationCollisionActors, List<Actor> simulationWindActors)
		{
			this.game = game;
			this.from = from;
			this.plan = plan;
			this.pawnProbe = pawnProbe;
			this.simulationCollisionActors = simulationCollisionActors;
			this.simulationWindActors = simulationWindActors;
			Game = game;
			Position = (Point2)from;
			Hitbox = new(pawnProbe);
			Facing = plan.Direction == 0 ? Signs.Positive : (Signs)plan.Direction;
			DependencyBounds = pawnProbe + Position;
			grounded = Grounded();
			lastX = Position.X;
		}

		public override PawnInput GetInput()
		{
			jump.Timer = elapsed;
			var moveX = JumpPlanExecution.MoveX(plan, jump);
			var jumpPressed = false;

			if (plan.Descend)
			{
				// Don't jump up — fall into the wall-hug. Mark Started so the wall-kick logic
				// below engages; the actual kick only fires once we're hugging the wall.
				jump.Started = true;
			}
			else if (JumpPlanExecution.ShouldStartJump(plan, jump))
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
			// For a descent, slide down the wall for RunupTime first (steer into it, no kick),
			// then fire the single kick — so we launch from the right height, not on first touch.
			var kickReady = !plan.Descend || elapsed >= plan.RunupTime;
			if (plan.Descend && !kickReady)
				moveX = plan.Direction; // keep hugging the wall while sliding
			else if (JumpPlanExecution.NeedsWallKickInput(plan, jump, IsGrounded, IsWallJumpLocked))
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
			if (collectBuildStats)
				simsRun++;
			var dt = game.Time.Delta;
			if (dt <= 0)
				dt = SimDelta;

			var maxTime = plan.MaxWallKicks > 1 ? SimChainMaxTime : SimMaxTime;
			for (elapsed = 0.0f; elapsed < maxTime; elapsed += dt)
			{
				if (collectBuildStats)
					simSteps++;
				Update();
				DependencyBounds = DependencyBounds.Conflate(pawnProbe + Position);

				if (Vector2.DistanceSquared((Vector2)Position, from) > 9)
					leftStart = true;
				if (!IsGrounded)
					leftGround = true;

				// Require the pawn to have actually left the ground before accepting a landing.
				// A descent walks along its start surface before falling; without this the landing
				// check fires on the first few px of grounded movement, before the maneuver begins.
				if (leftStart && leftGround && Velocity.Y >= 0 && IsGrounded)
				{
					if (collectBuildStats)
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
					if (collectBuildStats)
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
					if (collectBuildStats)
						simStalled++;
					landing = default;
					return false;
				}
			}

			if (collectBuildStats)
				simTimedOut++;
			landing = default;
			return false;
		}

		bool HasGroundSupportNextStep(int moveX)
			=> PlatformNavGraph.HasStandableSupport(game, pawnProbe + Position + new Point2(Math.Sign(moveX), 0));
	}
}
