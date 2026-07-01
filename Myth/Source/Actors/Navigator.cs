using Foster.Framework;
using System.Numerics;

namespace Myth;

/// <summary>
/// Encapsulates platform nav-graph navigation for a Pawn.
/// Hold one as a field and call into it from GetInput().
/// </summary>
public sealed class Navigator(Pawn owner)
{
	public const int NavNodeReachDistance = 8;
	public const float NavSearchDistance = 180;
	public const float GoalNodeSearchDistance = 56;
	static int GroundSupportLookAhead => 6;
	static int GroundSupportProbeWidth => 4;
	static int GroundSupportCastDepth => 10;
	static int ImmediateGroundSupportCastDepth => 4;
	static float SafeLandingSearchDistance => Game.TileSize * 1.5f;
	static float ActiveEdgeTimeout => 1.35f;
	static float OpposingJumpVelocityThreshold => 8.0f;
	static float JumpPrepareMaxTime => 0.15f;
	static float PrecisionLandingBrakeMinHeight => 16.0f;
	static float PrecisionLandingBrakeDistance => 32.0f;
	static float PrecisionLandingBrakeSpeed => 45.0f;
	static float LandingStabilizeDistance => 32.0f;
	static float LandingStabilizeVerticalSpeed => 45.0f;
	static float LandingStabilizeMaxSpeed => 64.0f;
	static float LandingStabilizeMinSpeed => 12.0f;
	static float LandingStabilizeGain => 5.0f;
	static float LandingStabilizeBrakeAccel => 600.0f;
	static float TakeoffApproachBrakeDistance => 10.0f;
	static float TakeoffApproachBrakeAccel => 1600.0f;
	static float BlockedPathTargetResetDistance => 16.0f;
	static float DirectGoalJumpMaxHorizontalDistance => 64.0f;
	static float DirectGoalJumpMaxVerticalDistance => 56.0f;
	static float DirectGoalJumpMinVerticalDistance => 12.0f;
	// Nominal time a jump spends airborne, used to convert "horizontal distance to the
	// landing" into a target takeoff speed. Matches a typical single hop (~0.5s up-and-down).
	static float NominalJumpAirtime => 0.5f;
	static float MaxAirSpeedApprox => 100.0f;
	// Forward runup: long jumps (reach beyond this) want real launch speed. Bud rolls forward
	// up to RunupForwardMax px toward the takeoff lip to build it — bounded so it never turns
	// a mid-surface takeoff into a big displacement, and never backs up off a ledge.
	static float RunupRequiredReach => 44.0f;
	static float MaxGroundSpeedApprox => 93.0f;
	static float RunupSpeedPerReachPx => 0.7f;
	static float RunupForwardMax => 12.0f;
	// Engage runup only when the takeoff node has less than this much surface ahead of it
	// before the edge (a cramped lip the plan's runup can't build speed on). Nodes with more
	// room are left untouched so their landings don't shift and cascade into later jumps.
	static float RunupRunwayThreshold => 10.0f;
	// A jump whose target is no higher than this above the takeoff counts as level/descending
	// and needs launch speed (it can't trade height for a shorter horizontal return).
	static float LevelJumpTolerance => 4.0f;
	// Pawn ground friction (Pawn.Friction) and half-width, used to estimate how far Bud
	// slides after landing so he doesn't overshoot a narrow platform's far edge into a pit.
	static float GroundFrictionApprox => 480.0f;
	static float PawnHalfWidth => 4.0f;
	// Landing brake only engages when ground is within this many px below Bud's feet
	// (i.e. the final descent onto a platform), so it never fires while arcing over a gap.
	static int LandingBrakeGroundProbeDepth => 14;
	// A descent kicks off the wall once it has slid to within this many px above the landing
	// node's level — low enough that the kick arc threads under the overhang into the pocket.
	static float DescentKickHeight => 0.0f;

	readonly List<PlatformNavGraph.Edge> navPath = [];
	public List<PlatformNavGraph.Edge> NavPath => navPath;
	readonly List<PlatformNavGraph.SurfaceEdge> surfaceNavPath = [];
	readonly List<PlatformNavGraph.Node> goalCandidates = [];
	readonly HashSet<(int From, int To)> blockedPathEdges = [];
	Vector2? blockedPathTarget;
	EdgeState activeEdge;
	bool activeEdgeBlocksPath;
	string? lastTraceStateKey;

	// Owner shortcuts
	Game Game => owner.Game;
	Vector2 Position => owner.Position;
	bool IsGrounded => owner.IsGrounded;
	bool ShouldBuildDebugNavLabel => !SuppressDebug && (owner.Game.IsPlatformNavDebugVisible || owner.Game.GymMode);

	// Set on the throwaway Navigator the nav-graph build drives during edge validation:
	// skips the per-frame debug-label/trace formatting (pure overhead off-screen).
	public bool SuppressDebug { get; set; }

	// Public state Bud/Boss read to build PawnInput
	public bool JumpHeld { get; private set; }
	public bool ActiveEdgeAllowsWallJump => activeEdge.Edge is { Jump: { WallJump: true } };

	// Terminal result of the most recently run active edge. The nav-graph build drives
	// a Navigator over a simulated pawn to validate jump edges, then reads this to decide
	// whether the runtime controller could actually land the edge. Active = still running.
	public enum EdgeRunState { Active, Landed, Missed }
	public EdgeRunState LastEdgeRunState { get; private set; }

	// When set, the active-edge executor reads node positions from this graph instead of
	// the Game's live graph. The build uses it to run the controller against the graph
	// currently being constructed (which isn't yet installed on the Game).
	public PlatformNavGraph? GraphOverride { get; set; }

	// Debug state — settable so Bud's custom nav logic can update it directly
	public int? DebugNavStartNodeId { get; set; }
	public int? DebugNavGoalNodeId { get; set; }
	public int? DebugNavTargetNodeId { get; set; }
	public string DebugNavLabel { get; set; } = "idle";
	public IReadOnlyList<PlatformNavGraph.Edge> DebugNavPath => navPath;
	public IReadOnlyList<PlatformNavGraph.SurfaceEdge> DebugSurfaceNavPath => surfaceNavPath;
	public RectInt? DebugNavProbeRect { get; set; }
	public bool DebugNavProbeHit { get; set; }

	public void UpdateJumpHeld(bool jumpPressed)
	{
		// A descent's kick off the wall must be a short tap, not a held jump: cut it the frame
		// after it fires so the arc stays flat and threads under the overhang into the pocket.
		// (A normal jump holds through the rise for height — that's the default below.)
		if (activeEdge.Edge is { Jump.Descend: true } && activeEdge.Jump.WallKickCount > 0)
			JumpHeld = false;
		else if (activeEdge.Jump.HoldTimer > 0)
			JumpHeld = true;
		else if (jumpPressed)
			JumpHeld = true;
		else if (owner.IsGrounded || owner.Velocity.Y >= 0)
			JumpHeld = false;
	}

	public bool TryNavigateToPosition(Vector2 target, out int moveX, out int moveY, out bool jumpPressed)
	{
		moveX = 0;
		moveY = 0;
		jumpPressed = false;

		if (Game.GetPlatformNavGraph() is not { } graph)
		{
			SetDebugNavState("no graph");
			TraceState("no-graph", "navNoGraph");
			return false;
		}

		ResetBlockedPathEdgesForTarget(target);

		if (TryDirectGoalJump(target, out moveX, out jumpPressed))
		{
			moveY = 0;
			TraceState($"direct:{moveX}:{(int)target.X}:{(int)target.Y}", "directGoalJump", ("moveX", moveX), ("target", NavTrace.Vec(target)));
			return true;
		}

		// Same-surface walking is for dry land; while submerged it would snap Bud to a nearby
		// standable ledge and stall him bobbing there instead of following the swim path.
		if (!owner.InWater && TryNavigateSameSurface(graph, target, false, out moveX, out moveY))
			return true;

		var start = graph.FindNearestNode(Position, NavSearchDistance, includeSwim: true);
		graph.FindNearestNodes(target, GoalNodeSearchDistance, goalCandidates, includeSwim: true);
		if (!start.HasValue || goalCandidates.Count <= 0)
		{
			SetDebugNavState("no node");
			TraceState("no-node", "navNoNode", ("target", NavTrace.Vec(target)));
			return false;
		}
		if (goalCandidates.Count == 1 && goalCandidates[0].Id == start.Value.Id)
		{
			SetDebugNavState("at target");
			TraceState($"at-target:{start.Value.Id}", "navAtTarget", ("node", start.Value.Id));
			return false;
		}

		PlatformNavGraph.Node? goal = null;
		foreach (var candidate in goalCandidates)
		{
			if (candidate.Id == start.Value.Id)
				continue;

			if (!graph.TryFindPath(start.Value.Id, candidate.Id, navPath, blockedPathEdges) || navPath.Count <= 0)
				continue;

			goal = candidate;
			break;
		}

		if (!goal.HasValue)
		{
			if (TryFindDirectJumpLaunchPath(graph, start.Value.Id, target, out var launchGoal))
				goal = launchGoal;
			else
			{
				surfaceNavPath.Clear();
				UpdateDebugNav(start.Value.Id, goalCandidates[0].Id, null);
				DebugNavLabel = ShouldBuildDebugNavLabel ? $"no path {start.Value.Id}->{goalCandidates[0].Id}" : "no path";
				TraceState(
					$"no-path:{start.Value.Id}:{goalCandidates[0].Id}:{(int)target.X}:{(int)target.Y}",
					"navNoPath",
					("startNode", start.Value.Id),
					("goalNode", goalCandidates[0].Id),
					("candidateCount", goalCandidates.Count),
					("target", NavTrace.Vec(target))
				);
				return false;
			}
		}

		UpdateDebugSurfacePath(graph, start.Value.Id, goal.Value.Id);
		TraceState(
			$"path:{start.Value.Id}:{goal.Value.Id}:{navPath.Count}",
			"pathChosen",
			("startNode", start.Value.Id),
			("goalNode", goal.Value.Id),
			("pathLength", navPath.Count),
			("target", NavTrace.Vec(target))
		);

		if (!TryGetActionableEdge(graph, out var edge))
		{
			UpdateDebugNav(start.Value.Id, goal.Value.Id, null);
			DebugNavLabel = "edge reached";
			TraceState($"edge-reached:{start.Value.Id}:{goal.Value.Id}", "edgeReached", ("startNode", start.Value.Id), ("goalNode", goal.Value.Id));
			return false;
		}

		if (edge.Kind is PlatformNavGraph.EdgeKind.Jump or PlatformNavGraph.EdgeKind.Fall)
		{
			UpdateDebugNav(start.Value.Id, goal.Value.Id, edge.To);
			StartActiveEdge(edge, blockPathOnTimeout: true);
			return TryExecuteActiveEdge(out moveX, out moveY, out jumpPressed);
		}

		var to = graph.Nodes[edge.To];
		var edgeTarget = to.Position;
		switch (edge.Kind)
		{
			case PlatformNavGraph.EdgeKind.Walk:
			case PlatformNavGraph.EdgeKind.Crawl:
				moveX = MoveTowardNavTarget(edgeTarget);
				moveY = edge.Kind == PlatformNavGraph.EdgeKind.Crawl || to.RequiresDucking ? 1 : 0;
				if (moveX != 0 && !HasGroundSupportAhead(moveX))
				{
					if (TryContinueWithJumpAfterBlockedWalk(graph, edge, start.Value.Id, goal.Value.Id, out moveX, out moveY, out jumpPressed))
						return true;
					UpdateDebugNav(start.Value.Id, goal.Value.Id, to.Id);
					DebugNavLabel = ShouldBuildDebugNavLabel ? $"edge stop {edge.Kind} {edge.From}->{edge.To}" : "edge stop";
					return false;
				}
				if (moveX == 0 && !IsNearNode(edgeTarget))
				{
					UpdateDebugNav(start.Value.Id, goal.Value.Id, to.Id);
					DebugNavLabel = ShouldBuildDebugNavLabel ? $"stalled {edge.Kind} {edge.From}->{edge.To}" : "stalled";
					return false;
				}
				break;

			case PlatformNavGraph.EdgeKind.RoomExit:
				moveX = MoveTowardNavTarget(edgeTarget);
				moveY = 0;
				break;

			case PlatformNavGraph.EdgeKind.Swim:
				(moveX, moveY, jumpPressed) = ExecuteSwimEdge(graph, edge);
				break;

			default:
				DebugNavLabel = ShouldBuildDebugNavLabel ? $"unsupported {edge.Kind}" : "unsupported";
				return false;
		}

		UpdateDebugNav(start.Value.Id, goal.Value.Id, to.Id);
		DebugNavLabel = ShouldBuildDebugNavLabel ? $"{edge.Kind} {edge.From}->{edge.To} len {navPath.Count}" : "following path";
		return true;
	}

	public bool TryNavigateSameSurface(PlatformNavGraph graph, Vector2 target, bool forceDucking, out int moveX, out int moveY)
	{
		moveX = 0;
		moveY = 0;

		var start = graph.FindNearestSurface(Position, NavSearchDistance);
		var goal = graph.FindNearestSurface(target, GoalNodeSearchDistance);
		if (!start.HasValue || !goal.HasValue || start.Value.SpanId != goal.Value.SpanId)
			return false;

		var span = graph.SurfaceSpans[start.Value.SpanId];
		moveX = MoveTowardNavTarget(goal.Value.Position);
		moveY = forceDucking || span.RequiresDucking ? 1 : 0;
		if (moveX == 0)
			return false;

		if (!HasGroundSupportAhead(moveX))
			return false;

		navPath.Clear();
		surfaceNavPath.Clear();
		activeEdge = default;
		activeEdgeBlocksPath = false;
		UpdateDebugNav(null, null, null);
		DebugNavProbeRect = null;
		DebugNavProbeHit = false;
		DebugNavLabel = ShouldBuildDebugNavLabel ? $"surface walk {span.Id}" : "following surface";
		return true;
	}

	public bool TryExecuteActiveEdge(out int moveX, out int moveY, out bool jumpPressed)
	{
		moveX = 0;
		moveY = 0;
		jumpPressed = false;
		if (activeEdge.Edge is not { } edge || (GraphOverride ?? Game.GetPlatformNavGraph()) is not { } graph)
			return false;

		if (edge.From < 0 || edge.From >= graph.Nodes.Count || edge.To < 0 || edge.To >= graph.Nodes.Count)
		{
			ClearActiveEdge("bad edge");
			return false;
		}

		activeEdge.Timer += owner.Time.Delta;
		var from = graph.Nodes[edge.From];
		var to = graph.Nodes[edge.To];
		var target = edge.Kind == PlatformNavGraph.EdgeKind.Jump
			? ResolveSafeJumpLandingTarget(graph, to)
			: to.Position;

		if (activeEdge.Timer > ActiveEdgeTimeoutFor(edge))
		{
			Game.NavTrace?.EdgeEvent(Game, "edgeTimeout", edge, ("timer", activeEdge.Timer), ("pos", NavTrace.Vec(Position)), ("vel", NavTrace.Vec(owner.Velocity)));
			if (activeEdgeBlocksPath)
				blockedPathEdges.Add((edge.From, edge.To));
			ClearActiveEdge(ShouldBuildDebugNavLabel ? $"{edge.Kind} timeout {edge.From}->{edge.To}" : "timeout");
			return false;
		}

		switch (edge.Kind)
		{
			case PlatformNavGraph.EdgeKind.Jump:
				if (!TryExecuteJumpEdge(graph, edge, from, to, target, out moveX, out jumpPressed))
					return false;
				// If we're hopping up onto a spot that has to be ducked into,
				// tuck as we arrive (descending or grounded) so Bud fits under the
				// ceiling and is already crawling instead of easing off the jump and
				// sliding back off the ledge before the crawl edge takes over.
				if (to.RequiresDucking && (IsGrounded || owner.Velocity.Y >= 0))
					moveY = 1;
				break;

			case PlatformNavGraph.EdgeKind.Crawl:
				moveX = MoveTowardNavTarget(target);
				moveY = 1;
				if (moveX != 0 && !HasGroundSupportAhead(moveX))
				{
					ClearActiveEdge(ShouldBuildDebugNavLabel ? $"{edge.Kind} edge stop" : "edge stop");
					return false;
				}
				if (moveX == 0 && IsNearNode(target))
				{
					ClearActiveEdge(ShouldBuildDebugNavLabel ? $"{edge.Kind} done" : "done", EdgeRunState.Landed);
					return false;
				}
				DebugNavLabel = ShouldBuildDebugNavLabel ? $"{edge.Kind} {edge.From}->{edge.To} t {activeEdge.Timer:0.0}" : "active edge";
				break;

			case PlatformNavGraph.EdgeKind.Fall:
				moveX = Math.Sign(to.Position.X - from.Position.X);
				if (!IsGrounded && MathF.Abs(target.X - Position.X) > NavNodeReachDistance * 0.5f)
					moveX = Math.Sign(target.X - Position.X);
				if (IsGrounded && activeEdge.Timer > 0.12f && IsNearNode(target))
				{
					ClearActiveEdge(ShouldBuildDebugNavLabel ? $"{edge.Kind} done" : "done", EdgeRunState.Landed);
					return false;
				}
				DebugNavLabel = ShouldBuildDebugNavLabel ? $"{edge.Kind} {edge.From}->{edge.To} t {activeEdge.Timer:0.0}" : "active edge";
				break;

			default:
				ClearActiveEdge("inactive");
				return false;
		}

		DebugNavTargetNodeId = to.Id;
		return true;
	}

	public bool TryGetActionableEdge(PlatformNavGraph graph, out PlatformNavGraph.Edge edge)
	{
		foreach (var candidate in navPath)
		{
			if (candidate.To < 0 || candidate.To >= graph.Nodes.Count)
				continue;
			var to = graph.Nodes[candidate.To];
			if (candidate.Kind is PlatformNavGraph.EdgeKind.Walk or PlatformNavGraph.EdgeKind.Crawl or PlatformNavGraph.EdgeKind.RoomExit &&
				MoveTowardNavTarget(to.Position) == 0 &&
				IsNearNode(to.Position))
				continue;
			edge = candidate;
			return true;
		}
		edge = default;
		return false;
	}

	public void StartActiveEdge(PlatformNavGraph.Edge edge, bool blockPathOnTimeout = false)
	{
		activeEdge = EdgeState.Start(edge);
		activeEdgeBlocksPath = blockPathOnTimeout;
		LastEdgeRunState = EdgeRunState.Active;
		lastTraceStateKey = null;
		Game.NavTrace?.EdgeEvent(Game, "edgeStart", edge, ("blockPathOnTimeout", blockPathOnTimeout), ("pos", NavTrace.Vec(Position)), ("vel", NavTrace.Vec(owner.Velocity)));
	}
	public void UpdateDebugSurfacePath(PlatformNavGraph graph, int startNodeId, int goalNodeId)
	{
		surfaceNavPath.Clear();
		if (ShouldBuildDebugNavLabel)
			graph.TryFindSurfacePath(startNodeId, goalNodeId, surfaceNavPath);
	}

	public void ClearDebugSurfacePath() => surfaceNavPath.Clear();

	public void ClearActiveEdge(string label, EdgeRunState outcome = EdgeRunState.Missed)
	{
		if (activeEdge.Edge is { } edge)
			Game.NavTrace?.EdgeEvent(Game, "edgeClear", edge, ("label", label), ("timer", activeEdge.Timer), ("pos", NavTrace.Vec(Position)), ("vel", NavTrace.Vec(owner.Velocity)));
		activeEdge = default;
		activeEdgeBlocksPath = false;
		LastEdgeRunState = outcome;
		DebugNavProbeRect = null;
		DebugNavProbeHit = false;
		DebugNavLabel = label;
	}

	public void ClearBlockedPathEdges()
	{
		blockedPathEdges.Clear();
		blockedPathTarget = null;
	}

	public void SetDebugNavState(string label)
	{
		UpdateDebugNav(null, null, null);
		navPath.Clear();
		surfaceNavPath.Clear();
		activeEdge = default;
		activeEdgeBlocksPath = false;
		DebugNavProbeRect = null;
		DebugNavProbeHit = false;
		DebugNavLabel = label;
	}

	void ResetBlockedPathEdgesForTarget(Vector2 target)
	{
		if (blockedPathTarget.HasValue &&
			Vector2.DistanceSquared(blockedPathTarget.Value, target) <= BlockedPathTargetResetDistance * BlockedPathTargetResetDistance)
			return;

		blockedPathEdges.Clear();
		blockedPathTarget = target;
	}

	bool TryDirectGoalJump(Vector2 target, out int moveX, out bool jumpPressed)
	{
		moveX = 0;
		jumpPressed = false;

		var dx = target.X - Position.X;
		var dy = Position.Y - target.Y;
		if (dy < DirectGoalJumpMinVerticalDistance ||
			dy > DirectGoalJumpMaxVerticalDistance ||
			MathF.Abs(dx) > DirectGoalJumpMaxHorizontalDistance)
			return false;

		// A direct hop only works with a clear path to the goal. Thin (1-tile)
		// walls sitting between Bud and the target must count here, or Bud will
		// keep bumping the wall thinking he can jump up to touch the goal.
		if (IsLineToTargetBlocked(Position, target))
			return false;

		moveX = MathF.Abs(dx) > NavNodeReachDistance * 0.5f ? Math.Sign(dx) : 0;
		// In water, pressing jump strokes upward while submerged and becomes a full launch at
		// the surface, so a goal within hop range above the water is reached by holding jump
		// as Bud rises to the surface and hops out through it.
		if (IsGrounded || owner.InWater)
			jumpPressed = true;

		navPath.Clear();
		surfaceNavPath.Clear();
		activeEdge = default;
		activeEdgeBlocksPath = false;
		UpdateDebugNav(null, null, null);
		DebugNavProbeRect = null;
		DebugNavProbeHit = false;
		DebugNavLabel = ShouldBuildDebugNavLabel ? $"direct goal jump {moveX}" : "direct goal jump";
		return true;
	}

	// Samples the straight line from Bud to the target for solid room collision,
	// skipping the ends so Bud's own footing and the target tile don't count.
	// Steps finely enough (≤ half a tile) that a thin 1-tile wall still registers.
	private bool IsLineToTargetBlocked(Vector2 from, Vector2 target)
	{
		var delta = target - from;
		var distance = delta.Length();
		if (distance <= NavNodeReachDistance)
			return false;

		var dir = delta / distance;
		const float step = Game.TileSize * 0.5f;
		const int probeHalf = 2;
		for (var d = (float)NavNodeReachDistance; d <= distance - NavNodeReachDistance * 0.5f; d += step)
		{
			var p = from + dir * d;
			var probe = new RectInt((int)p.X - probeHalf, (int)p.Y - probeHalf, probeHalf * 2, probeHalf * 2);
			if (Game.OverlapsRoomCollision(probe))
				return true;
		}
		return false;
	}

	bool TryFindDirectJumpLaunchPath(PlatformNavGraph graph, int startId, Vector2 target, out PlatformNavGraph.Node goal)
	{
		PlatformNavGraph.Node? best = null;
		var bestScore = float.MaxValue;
		foreach (var node in graph.Nodes)
		{
			if (node.Kind == PlatformNavGraph.NodeKind.RoomExit || node.RequiresDucking)
				continue;
			if (node.Id == startId)
				continue;
			if (!CanDirectJumpFrom(node.Position, target))
				continue;
			if (!graph.TryFindPath(startId, node.Id, navPath, blockedPathEdges) || navPath.Count <= 0)
				continue;

			var score = navPath.Count * 100 + Vector2.Distance(node.Position, target);
			if (score >= bestScore)
				continue;
			best = node;
			bestScore = score;
		}

		if (!best.HasValue)
		{
			goal = default;
			return false;
		}

		graph.TryFindPath(startId, best.Value.Id, navPath, blockedPathEdges);
		goal = best.Value;
		return true;
	}

	bool CanDirectJumpFrom(Vector2 from, Vector2 target)
	{
		var dx = target.X - from.X;
		var dy = from.Y - target.Y;
		return dy >= DirectGoalJumpMinVerticalDistance &&
			dy <= DirectGoalJumpMaxVerticalDistance &&
			MathF.Abs(dx) <= DirectGoalJumpMaxHorizontalDistance &&
			!IsLineToTargetBlocked(from, target);
	}

	public int MoveTowardNavTarget(Vector2 target)
	{
		var dx = target.X - Position.X;
		if (MathF.Abs(dx) > NavNodeReachDistance * 0.5f)
			return Math.Sign(dx);
		return 0;
	}

	// Swim toward the target area: steer horizontally to the nearest point inside it (which
	// sits on the shared portal face when the area is adjacent), and stroke up (jump) when
	// that point is above. Sinking downward needs no input — gravity handles it.
	// Drive a Swim edge: toward the portal (or into water from a land rim) for an area target,
	// or a surface-hop out onto a land target. Shared by both the follow and goal-seek paths.
	public (int MoveX, int MoveY, bool JumpPressed) ExecuteSwimEdge(PlatformNavGraph graph, PlatformNavGraph.Edge edge)
	{
		var from = graph.Nodes[edge.From];
		var to = graph.Nodes[edge.To];
		return to.Kind == PlatformNavGraph.NodeKind.Swim
			? SwimSteer(from, to)
			: SwimHopOutToward(from, to.Position);
	}

	(int MoveX, int MoveY, bool JumpPressed) SwimSteer(PlatformNavGraph.Node from, PlatformNavGraph.Node to)
	{
		if (to.Area is not { } toRect)
			return (0, 0, false);

		Vector2 target;
		if (from.Area is { } fromRect)
		{
			// Head for the actual opening between the two areas, nudged just past it straight
			// through the shared face (perpendicular only — a diagonal nudge toward the far
			// area's center adds a false vertical pull that sends Bud jumping into the ceiling
			// beside a side-by-side portal instead of swimming across through it).
			var portal = SwimPortalMidpoint(fromRect, toRect);
			target =
				fromRect.Right == toRect.Left ? portal + new Vector2(NavNodeReachDistance, 0) :
				toRect.Right == fromRect.Left ? portal - new Vector2(NavNodeReachDistance, 0) :
				fromRect.Bottom == toRect.Top ? portal + new Vector2(0, NavNodeReachDistance) :
				portal - new Vector2(0, NavNodeReachDistance);
		}
		else
		{
			// Entering from a land node: aim a bit inside the area so the arrival deadzone
			// doesn't stop Bud on the rim before he drops in.
			var insetX = Math.Min(NavNodeReachDistance, toRect.Width / 2);
			var insetY = Math.Min(NavNodeReachDistance, toRect.Height / 2);
			target = new Vector2(
				Math.Clamp(Position.X, toRect.Left + insetX, toRect.Right - insetX),
				Math.Clamp(Position.Y, toRect.Top + insetY, toRect.Bottom - insetY));
		}

		var moveX = MathF.Abs(target.X - Position.X) > NavNodeReachDistance * 0.5f ? Math.Sign(target.X - Position.X) : 0;

		// A one-tile-tall water passage won't admit the standing body; duck to crawl through
		// it. Ducking and stroking upward conflict (crawling is a low, grounded move).
		var moveY = NeedsCrawl(moveX) ? 1 : 0;

		// Only stroke upward once lined up under the target: stroking while still moving across
		// drives Bud diagonally into the ceiling beside the opening instead of up through it.
		var aligned = MathF.Abs(target.X - Position.X) <= NavNodeReachDistance;
		var jump = moveY == 0 && aligned && target.Y < Position.Y - NavNodeReachDistance * 0.5f;
		return (moveX, moveY, jump);
	}

	static Vector2 SwimPortalMidpoint(RectInt a, RectInt b)
	{
		// Vertical shared face (areas side by side): midpoint runs down the touching edge.
		if (a.Right == b.Left || b.Right == a.Left)
		{
			var x = a.Right == b.Left ? a.Right : a.Left;
			var top = Math.Max(a.Top, b.Top);
			var bottom = Math.Min(a.Bottom, b.Bottom);
			return new Vector2(x, (top + bottom) * 0.5f);
		}
		// Horizontal shared face (stacked): midpoint runs along the touching edge.
		var y = a.Bottom == b.Top ? a.Bottom : a.Top;
		var left = Math.Max(a.Left, b.Left);
		var right = Math.Min(a.Right, b.Right);
		return new Vector2((left + right) * 0.5f, y);
	}

	// Exit the water onto a land node above. While submerged, keep stroking to rise to the
	// surface (the stroke becomes a full launch there) while drifting under the reachable part
	// of the area; the land node often sits over a wall the submerged body can't cross, so the
	// hop's air control does the sideways reach. Once airborne, steer at the real node.
	(int MoveX, int MoveY, bool JumpPressed) SwimHopOutToward(PlatformNavGraph.Node from, Vector2 target)
	{
		var aimX = owner.InWater && from.Area is { } rect
			? Math.Clamp(target.X, rect.Left, rect.Right)
			: target.X;
		var dx = aimX - Position.X;
		var moveX = MathF.Abs(dx) > NavNodeReachDistance * 0.5f ? Math.Sign(dx) : 0;
		var moveY = NeedsCrawl(moveX) ? 1 : 0;
		var jump = moveY == 0 && owner.InWater;
		return (moveX, moveY, jump);
	}

	// True when a low ceiling (solid where the standing body's head would be) sits at or just
	// ahead of Bud, so he must duck to fit through the gap.
	bool NeedsCrawl(int moveX)
	{
		// Probe the standing head region from where Bud is out to a bit ahead in his travel
		// direction, so a ceiling he's already under (or about to be) triggers the duck.
		var lookAhead = moveX * 6;
		var headProbe = new RectInt(-4 + Math.Min(0, lookAhead), -12, 8 + Math.Abs(lookAhead), 6)
			+ new Point2((int)Position.X, (int)Position.Y);
		return Game.OverlapsRoomCollision(headProbe);
	}

	public bool IsNearNode(Vector2 node)
		=> MathF.Abs(Position.X - node.X) <= NavNodeReachDistance &&
		   MathF.Abs(Position.Y - node.Y) <= NavNodeReachDistance;

	public bool HasGroundSupportAhead(int moveX)
	{
		if (!IsGrounded || moveX == 0) return true;
		return HasFootSupport(moveX, GroundSupportLookAhead, GroundSupportCastDepth);
	}

	public bool HasGroundSupportNextStep(int moveX)
	{
		if (!IsGrounded || moveX == 0) return true;
		return HasFootSupport(moveX, 1, ImmediateGroundSupportCastDepth);
	}

	// Standable ground (solid or jumpthru) directly beneath Bud's feet within `depth` px.
	// Used to confine landing braking to the final descent onto a platform, never mid-gap.
	bool HasGroundWithin(int depth)
	{
		var bounds = owner.Hitbox.Rect + owner.Position;
		var probe = new RectInt(bounds.Left, bounds.Bottom, bounds.Width, depth);
		return Game.OverlapsRoomCollision(probe) || OverlapsJumpthruSupport(probe);
	}

	bool HasFootSupport(int moveX, int lookAhead, int castDepth)
	{
		var bounds = owner.Hitbox.Rect + owner.Position;
		var direction = Math.Sign(moveX);
		var probeX = direction > 0
			? bounds.Right - GroundSupportProbeWidth + lookAhead
			: bounds.Left - lookAhead;
		var probe = new RectInt(probeX, bounds.Bottom, GroundSupportProbeWidth, castDepth);
		var supported = Game.OverlapsRoomCollision(probe) || OverlapsJumpthruSupport(probe);
		DebugNavProbeRect = probe;
		DebugNavProbeHit = supported;
		return supported;
	}

	bool OverlapsJumpthruSupport(RectInt probe)
	{
		foreach (var actor in Game.Actors)
		{
			if (actor == owner || !actor.Mask.Has(Actor.Masks.Jumpthru))
				continue;
			if (actor.Hitbox.Shape == Hitbox.Shapes.Rect && probe.Overlaps(actor.Hitbox.Rect + actor.Position))
				return true;
		}
		return false;
	}

	public bool TryContinueWithJumpAfterBlockedWalk(
		PlatformNavGraph graph,
		PlatformNavGraph.Edge blockedEdge,
		int startNodeId,
		int goalNodeId,
		out int moveX,
		out int moveY,
		out bool jumpPressed)
	{
		moveX = 0;
		moveY = 0;
		jumpPressed = false;

		var index = navPath.FindIndex(edge => edge.Equals(blockedEdge));
		if (index < 0 || index + 1 >= navPath.Count)
			return false;

		var next = navPath[index + 1];
		if (next.From != blockedEdge.To || next.Kind != PlatformNavGraph.EdgeKind.Jump)
			return false;

		var source = graph.Nodes[next.From].Position;
		if (MathF.Abs(Position.X - source.X) > Game.TileSize || MathF.Abs(Position.Y - source.Y) > Game.TileSize)
			return false;

		UpdateDebugNav(startNodeId, goalNodeId, next.To);
		StartActiveEdge(next, blockPathOnTimeout: true);
		DebugNavLabel = ShouldBuildDebugNavLabel ? $"jump handoff {blockedEdge.From}->{blockedEdge.To}->{next.To}" : "jump handoff";
		return TryExecuteActiveEdge(out moveX, out moveY, out jumpPressed);
	}

	void UpdateDebugNav(int? start, int? goal, int? target)
	{
		DebugNavStartNodeId = start;
		DebugNavGoalNodeId = goal;
		DebugNavTargetNodeId = target;
	}

	bool TryExecuteJumpEdge(
		PlatformNavGraph graph,
		PlatformNavGraph.Edge edge,
		PlatformNavGraph.Node from,
		PlatformNavGraph.Node to,
		Vector2 target,
		out int moveX,
		out bool jumpPressed)
	{
		moveX = 0;
		jumpPressed = false;
		var plan = edge.Jump ?? DefaultJumpPlan(from.Position, target);
		var hasPlannedHorizontal = plan.Direction != 0 || plan.AirDirection != 0;
		var takeoffDirection = plan.Direction != 0 ? plan.Direction : hasPlannedHorizontal ? Math.Sign(target.X - from.Position.X) : 0;
		if (hasPlannedHorizontal && takeoffDirection == 0)
			takeoffDirection = owner.Facing == 0 ? 1 : owner.Facing;

		if (!activeEdge.JumpPlanStarted)
		{
			activeEdge.Phase = ActiveEdgePhase.ApproachTakeoff;
			if (!CanStartJumpPlan(from.Position))
			{
				if (!IsGrounded)
				{
					DebugNavProbeRect = null;
					DebugNavProbeHit = false;
					DebugNavLabel = ShouldBuildDebugNavLabel ? $"{edge.Kind} wait ground {edge.From}->{edge.To}" : "wait ground";
					return true;
				}

				var dx = from.Position.X - Position.X;
				moveX = Math.Sign(dx);
				BrakeForTakeoffApproach(dx);
				if (IsGrounded && moveX != 0 && !HasGroundSupportAhead(moveX))
				{
					ClearActiveEdge("approach ledge");
					return false;
				}
				DebugNavLabel = ShouldBuildDebugNavLabel ? $"{edge.Kind} approach {edge.From}->{edge.To}" : "approach";
				return true;
			}

			// Forward runup for long jumps: a far jump taken flat-footed from the lip of a
			// cramped node lands short and clips the wall below the target. Roll forward toward
			// the takeoff edge (bounded, never backward) to launch with speed a few px closer.
			// Only engages when the takeoff node is CRAMPED — little surface ahead before the
			// edge, so the plan's own runup can't build speed. A node with room is left alone,
			// or rolling it forward would shift its landing and cascade into the next jump.
			var runupReach = MathF.Abs(target.X - from.Position.X);
			if (hasPlannedHorizontal && !plan.WallJump && takeoffDirection != 0 &&
				runupReach > RunupRequiredReach && IsGrounded &&
				graph.TryGetNodeSurfaceSpan(edge.From, out var srcSpan))
			{
				var frontEdge = takeoffDirection > 0 ? srcSpan.Right : srcSpan.Left;
				var runway = (frontEdge - Position.X) * takeoffDirection;
				var forwardSpeed = owner.Velocity.X * takeoffDirection;
				var targetSpeed = MathF.Min(MaxGroundSpeedApprox, runupReach * RunupSpeedPerReachPx);
				// Need real speed when the takeoff is cramped (plan can't build it), OR when the
				// jump is long and level/descending — a climbing jump lands higher and returns to
				// level sooner, so it needs less horizontal; a level one must stay aloft the whole
				// arc and undershoots without speed. Climbing jumps with room are left alone.
				var levelOrFalling = target.Y >= from.Position.Y - LevelJumpTolerance;
				if ((runway < RunupRunwayThreshold || levelOrFalling) && forwardSpeed < targetSpeed &&
					activeEdge.RunupForward < RunupForwardMax &&
					HasGroundSupportNextStep(takeoffDirection))
				{
					activeEdge.RunupForward += MathF.Abs(owner.Velocity.X) * owner.Time.Delta;
					moveX = takeoffDirection;
					activeEdge.Phase = ActiveEdgePhase.Runup;
					DebugNavLabel = ShouldBuildDebugNavLabel ? $"{edge.Kind} runup {edge.From}->{edge.To} rw {runway:0} vx {forwardSpeed:0}/{targetSpeed:0}" : "runup";
					return true;
				}
			}

			// A near-vertical hop onto a narrow platform directly overhead must leave the
			// ground with almost no horizontal speed, or the drift carries Bud off the
			// platform. Brake to a near-stop before committing (a plan with no horizontal
			// intent and a target nearly straight up is a vertical hop). Validated the
			// same way at build time, so an edge only survives if this braking succeeds.
			var precisionVertical = !hasPlannedHorizontal &&
				MathF.Abs(target.X - from.Position.X) <= NavNodeReachDistance;
			if (precisionVertical && IsGrounded &&
				MathF.Abs(owner.Velocity.X) > OpposingJumpVelocityThreshold &&
				activeEdge.JumpPrepareTimer < JumpPrepareMaxTime)
			{
				activeEdge.Phase = ActiveEdgePhase.Settle;
				activeEdge.JumpPrepareTimer += owner.Time.Delta;
				owner.Velocity.X = Calc.Approach(owner.Velocity.X, 0, TakeoffApproachBrakeAccel * owner.Time.Delta);
				DebugNavLabel = ShouldBuildDebugNavLabel ? $"{edge.Kind} settle-v {edge.From}->{edge.To} vx {owner.Velocity.X:0}" : "settle";
				return true;
			}

			var opposingVelocity = IsGrounded &&
				takeoffDirection != 0 &&
				MathF.Sign(owner.Velocity.X) == -takeoffDirection &&
				MathF.Abs(owner.Velocity.X) > OpposingJumpVelocityThreshold;
			if (opposingVelocity && activeEdge.JumpPrepareTimer < JumpPrepareMaxTime)
			{
				activeEdge.Phase = ActiveEdgePhase.Settle;
				activeEdge.JumpPrepareTimer += owner.Time.Delta;
				DebugNavLabel = ShouldBuildDebugNavLabel ? $"{edge.Kind} settle {edge.From}->{edge.To} vx {owner.Velocity.X:0}" : "settle";
				return true;
			}

			activeEdge.Phase = ActiveEdgePhase.Runup;
			activeEdge.JumpPlanStarted = true;
			activeEdge.Jump.Timer = 0;
			activeEdge.Timer = 0;
			// A descent enters the maneuver by falling, not jumping: mark Started so the wall-kick
			// logic engages, but never press jump up. The pawn walks off toward the wall and slides.
			if (plan.Descend)
				activeEdge.Jump.Started = true;
			Game.NavTrace?.EdgeEvent(Game, "jumpPlanStart", edge, ("plan", JumpPlanTrace(plan)), ("pos", NavTrace.Vec(Position)), ("vel", NavTrace.Vec(owner.Velocity)));
		}

		JumpPlanExecution.Advance(ref activeEdge.Jump, owner.Time.Delta);
		moveX = PlannedJumpMoveX(plan, target);
		if (moveX == 0 && hasPlannedHorizontal)
			moveX = takeoffDirection;

		if (JumpPlanExecution.ShouldStartJump(plan, activeEdge.Jump))
			StartPlannedJump(plan, out jumpPressed);
		else if (!activeEdge.Jump.Started && IsGrounded && moveX != 0 && !HasGroundSupportNextStep(moveX))
			StartPlannedJump(plan, out jumpPressed);

		// Regulate takeoff speed to the jump distance: a short hop taken at full run speed
		// flies clean past a narrow platform (or into the gap beyond it). At the instant we
		// leave the ground, brake any horizontal speed that exceeds what's needed to cover
		// the remaining horizontal distance over a nominal airtime. Only brakes excess in the
		// travel direction — never fights a long jump that needs the speed, never a wall kick.
		if (jumpPressed && !plan.WallJump)
		{
			var launchDx = target.X - Position.X;
			var desiredVx = Math.Clamp(launchDx / NominalJumpAirtime, -MaxAirSpeedApprox, MaxAirSpeedApprox);
			if (Math.Sign(owner.Velocity.X) == Math.Sign(launchDx) && MathF.Abs(owner.Velocity.X) > MathF.Abs(desiredVx))
				owner.Velocity.X = desiredVx;
		}

		JumpPlanExecution.DecayHold(ref activeEdge.Jump, owner.Time.Delta, jumpPressed);

		if (activeEdge.Jump.Started && !IsGrounded)
		{
			activeEdge.Phase = ActiveEdgePhase.Airborne;
			JumpPlanExecution.SyncWallKickLock(ref activeEdge.Jump, plan, owner.IsWallJumpLocked, Math.Sign(owner.Velocity.X));
			// A descent hugs the wall and slides DOWN to the landing's level before kicking, so it
			// launches low enough to thread under the overhang. Gating on depth (not time) is
			// robust to how fast Bud arrived at the node. Until deep enough, hold into the wall.
			if (plan.Descend && activeEdge.Jump.WallKickCount == 0 && Position.Y < target.Y - DescentKickHeight)
			{
				moveX = plan.Direction;
			}
			else if (JumpPlanExecution.NeedsWallKickInput(plan, activeEdge.Jump, IsGrounded, owner.IsWallJumpLocked))
			{
				// Steer into the wall and keep the jump pressed so the wall kick fires
				// the instant Bud touches it. The runtime input-lock then carries him
				// away; once we see the kick, switch to steering toward the landing.
				moveX = JumpPlanExecution.WallApproachDirection(plan, activeEdge.Jump);
				jumpPressed = true;
			}
			else
			{
				moveX = AirSteerTowardLanding(plan, from.Position, target, to.Kind, moveX);
				if (to.Kind != PlatformNavGraph.NodeKind.Ledge)
					BrakeForLandingFarEdge(graph, edge);
			}
		}
		else
		{
			if (activeEdge.Jump.Started)
				activeEdge.Phase = ActiveEdgePhase.Landing;
			DebugNavProbeRect = null;
			DebugNavProbeHit = false;
		}

		// A descent is grounded at its start node with Started already set, so don't treat that
		// as a landing — only accept one after it has kicked off the wall (WallKickCount > 0).
		var descentNotYetKicked = plan.Descend && activeEdge.Jump.WallKickCount == 0;
		if (activeEdge.Jump.Started && IsGrounded && !descentNotYetKicked && activeEdge.Jump.Timer > plan.RunupTime + 0.12f)
		{
			// Success = grounded anywhere on the target node's platform. The jump arc lands
			// where it lands (often mid-platform, not on the exact node point); fighting
			// landing momentum to reach the precise node just wastes the edge timeout and
			// fails edges Bud actually completed. The next path edge handles fine positioning.
			if (graph.IsPositionOnNodeSurface(Position, edge.To))
				ClearActiveEdge(ShouldBuildDebugNavLabel ? $"{edge.Kind} done" : "done", EdgeRunState.Landed);
			else
			{
				Game.NavTrace?.EdgeEvent(Game, "edgeMiss", edge, ("timer", activeEdge.Timer), ("pos", NavTrace.Vec(Position)), ("target", NavTrace.Vec(target)), ("vel", NavTrace.Vec(owner.Velocity)));
				ClearActiveEdge(ShouldBuildDebugNavLabel ? $"{edge.Kind} miss {edge.From}->{edge.To}" : "miss");
			}
			return false;
		}

		DebugNavLabel = ShouldBuildDebugNavLabel ? $"{edge.Kind} {edge.From}->{edge.To} {activeEdge.Phase} p {activeEdge.Jump.Timer:0.0}" : "jump";
		return true;
	}

	// Cap horizontal speed while descending so Bud's post-landing friction slide can't
	// carry him off the FAR edge of the destination platform into a pit. The allowable
	// slide is the room from Bud's current X to that far edge (in the travel direction),
	// less a half-pawn margin; on a wide platform that's large and this is a no-op, on a
	// narrow one it brakes to fit. Only ever reduces toward-edge speed — never adds speed,
	// never fights a jump that still needs to reach the platform (margin is large when short).
	void BrakeForLandingFarEdge(PlatformNavGraph graph, PlatformNavGraph.Edge edge)
	{
		if (owner.Velocity.Y < 0)
			return;
		var vx = owner.Velocity.X;
		var travel = Math.Sign(vx);
		if (travel == 0)
			return;

		// Only brake when there's standable ground right beneath Bud. Otherwise he's still
		// arcing over a gap (e.g. a missed speculative jump that the arc would carry back
		// onto a platform) — killing his momentum there would drop him straight into the pit.
		if (!HasGroundWithin(LandingBrakeGroundProbeDepth))
			return;

		if (!graph.TryGetNodeSurfaceSpan(edge.To, out var span))
			return;

		// Don't touch speed until Bud is actually over the platform: while still short
		// of the near edge he needs every bit of his horizontal speed to reach it, and
		// braking here would drop him into the gap.
		var nearEdge = travel > 0 ? span.Left : span.Right;
		if ((nearEdge - Position.X) * travel > 0)
			return;

		var farEdge = travel > 0 ? span.Right : span.Left;
		var margin = (farEdge - Position.X) * travel - PawnHalfWidth;
		if (margin <= 0)
		{
			owner.Velocity.X = 0;
			return;
		}

		var maxSpeed = MathF.Sqrt(2f * GroundFrictionApprox * margin);
		if (MathF.Abs(vx) > maxSpeed)
			owner.Velocity.X = travel * maxSpeed;
	}

	void BrakeForTakeoffApproach(float dx)
	{
		if (MathF.Abs(dx) > TakeoffApproachBrakeDistance || owner.Velocity.X == 0)
			return;

		var velocityDirection = Math.Sign(owner.Velocity.X);
		var targetDirection = Math.Sign(dx);
		if (targetDirection != 0 && velocityDirection != targetDirection)
			owner.Velocity.X = Calc.Approach(owner.Velocity.X, 0, TakeoffApproachBrakeAccel * owner.Time.Delta);
		else if (!HasGroundSupportNextStep(velocityDirection))
			owner.Velocity.X = 0;
	}

	static float ActiveEdgeTimeoutFor(PlatformNavGraph.Edge edge)
	{
		if (edge.Jump is not { } plan)
			return ActiveEdgeTimeout;
		// A descent walks to the edge, slides for RunupTime, kicks, and lands — give it the
		// slide time plus room for the fall and arc.
		if (plan.Descend)
			return MathF.Max(ActiveEdgeTimeout, plan.RunupTime + 1.4f);
		if (plan.MaxWallKicks <= 1)
			return ActiveEdgeTimeout;
		return MathF.Max(ActiveEdgeTimeout, 0.55f + plan.MaxWallKicks * 0.45f);
	}

	static Vector2 ResolveSafeJumpLandingTarget(PlatformNavGraph graph, PlatformNavGraph.Node to)
	{
		if (to.Kind == PlatformNavGraph.NodeKind.Landing)
			return to.Position;

		PlatformNavGraph.Node? best = null;
		var bestDistanceSquared = SafeLandingSearchDistance * SafeLandingSearchDistance;
		foreach (var node in graph.Nodes)
		{
			if (node.Kind != PlatformNavGraph.NodeKind.Landing || node.RequiresDucking)
				continue;
			if (MathF.Abs(node.Position.Y - to.Position.Y) > NavNodeReachDistance)
				continue;
			var distanceSquared = Vector2.DistanceSquared(node.Position, to.Position);
			if (distanceSquared >= bestDistanceSquared)
				continue;
			best = node;
			bestDistanceSquared = distanceSquared;
		}
		return best?.Position ?? to.Position;
	}

	void StartPlannedJump(PlatformNavGraph.JumpPlan plan, out bool jumpPressed)
	{
		jumpPressed = true;
		JumpPlanExecution.StartJump(ref activeEdge.Jump, plan);
		activeEdge.Phase = ActiveEdgePhase.Airborne;
		if (Game.NavTrace != null && activeEdge.Edge is { } edge)
			Game.NavTrace.EdgeEvent(Game, "jumpTakeoff", edge, ("plan", JumpPlanTrace(plan)), ("pos", NavTrace.Vec(Position)), ("vel", NavTrace.Vec(owner.Velocity)));
	}

	static PlatformNavGraph.JumpPlan DefaultJumpPlan(Vector2 from, Vector2 target)
	{
		var direction = Math.Sign(target.X - from.X);
		if (direction == 0) direction = 1;
		var height = MathF.Max(0, from.Y - target.Y);
		var distance = MathF.Abs(target.X - from.X);
		var hold = 0.055f;
		hold += Math.Clamp(height / 32.0f, 0, 1) * 0.105f;
		hold += Math.Clamp((distance - 16) / 40.0f, 0, 1) * 0.035f;
		return new(direction, 0, Math.Clamp(hold, 0.055f, JumpPlanExecution.MaxJumpHoldTime), direction, 99.0f);
	}

	bool CanStartJumpPlan(Vector2 source)
		=> IsGrounded &&
		   MathF.Abs(Position.X - source.X) <= NavNodeReachDistance * 0.25f &&
		   MathF.Abs(Position.Y - source.Y) <= Game.TileSize;

	int PlannedJumpMoveX(PlatformNavGraph.JumpPlan plan, Vector2 landing)
	{
		var moveX = JumpPlanExecution.MoveX(plan, activeEdge.Jump);
		if (moveX == plan.Direction)
			return moveX;
		if (plan.AirDirection != plan.Direction)
		{
			var remainingDirection = Math.Sign(landing.X - Position.X);
			if (remainingDirection == plan.Direction && MathF.Abs(landing.X - Position.X) > NavNodeReachDistance)
				return plan.Direction;
		}
		return moveX;
	}

	int AirSteerTowardLanding(
		PlatformNavGraph.JumpPlan plan,
		Vector2 takeoff,
		Vector2 landing,
		PlatformNavGraph.NodeKind targetKind,
		int plannedMoveX)
	{
		var dx = landing.X - Position.X;
		var remainingX = MathF.Abs(dx);
		var steer = plannedMoveX;
		var height = MathF.Max(0, takeoff.Y - landing.Y);
		var targetDirection = Math.Sign(dx);
		var velocityDirection = Math.Sign(owner.Velocity.X);
		if (targetKind != PlatformNavGraph.NodeKind.Ledge &&
			height >= PrecisionLandingBrakeMinHeight &&
			plan.AirDirection == plan.Direction &&
			remainingX <= PrecisionLandingBrakeDistance &&
			targetDirection != 0 &&
			velocityDirection == targetDirection &&
			MathF.Abs(owner.Velocity.X) > PrecisionLandingBrakeSpeed)
		{
			steer = -velocityDirection;
		}
		else if (remainingX <= 18 || owner.Velocity.Y > 0 && remainingX <= 28)
			steer = Math.Sign(dx);

		if (targetKind != PlatformNavGraph.NodeKind.Ledge &&
			owner.Velocity.Y >= LandingStabilizeVerticalSpeed &&
			remainingX <= LandingStabilizeDistance)
		{
			var desiredVelocity = Math.Clamp(dx * LandingStabilizeGain, -LandingStabilizeMaxSpeed, LandingStabilizeMaxSpeed);
			if (MathF.Abs(dx) <= NavNodeReachDistance)
				desiredVelocity = 0;
			else if (MathF.Abs(desiredVelocity) < LandingStabilizeMinSpeed)
				desiredVelocity = MathF.Sign(desiredVelocity) * LandingStabilizeMinSpeed;

			owner.Velocity.X = Calc.Approach(owner.Velocity.X, desiredVelocity, LandingStabilizeBrakeAccel * owner.Time.Delta);
			var velocityError = desiredVelocity - owner.Velocity.X;
			steer = MathF.Abs(velocityError) > 4.0f ? Math.Sign(velocityError) : Math.Sign(dx);
		}

		DebugNavProbeRect = null;
		DebugNavProbeHit = false;
		return steer;
	}

	private void TraceState(string key, string name, params (string Key, object? Value)[] fields)
	{
		if (lastTraceStateKey == key)
			return;
		lastTraceStateKey = key;
		Game.NavTrace?.Event(Game, name, fields);
	}

	private static Dictionary<string, object?> JumpPlanTrace(PlatformNavGraph.JumpPlan plan)
		=> new()
		{
			["direction"] = plan.Direction,
			["runup"] = plan.RunupTime,
			["hold"] = plan.HoldTime,
			["airDirection"] = plan.AirDirection,
			["switch"] = plan.SwitchTime,
			["wall"] = plan.WallJump,
			["maxWallKicks"] = plan.MaxWallKicks
		};

	private enum ActiveEdgePhase
	{
		None,
		ApproachTakeoff,
		Settle,
		Runup,
		Airborne,
		Landing
	}

	private struct EdgeState
	{
		public PlatformNavGraph.Edge? Edge;
		public ActiveEdgePhase Phase;
		public float Timer;
		public JumpPlanExecution.State Jump;
		public bool JumpPlanStarted;
		public float JumpPrepareTimer;
		// Distance Bud has rolled forward toward the takeoff edge to build launch speed for a
		// long jump (bounded; no backing up). Lets him leave from the lip of a cramped node
		// with speed instead of leaping flat-footed and clipping the wall below the target.
		public float RunupForward;

		public static EdgeState Start(PlatformNavGraph.Edge edge)
			=> new()
			{
				Edge = edge,
				Phase = edge.Kind == PlatformNavGraph.EdgeKind.Jump ? ActiveEdgePhase.ApproachTakeoff : ActiveEdgePhase.Runup
			};
	}
}
