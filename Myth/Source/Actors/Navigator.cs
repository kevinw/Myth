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
	private const int GroundSupportLookAhead = 6;
	private const int GroundSupportProbeWidth = 4;
	private const int GroundSupportCastDepth = 10;
	private const int ImmediateGroundSupportCastDepth = 4;
	private const float SafeLandingSearchDistance = Game.TileSize * 1.5f;
	private const float ActiveEdgeTimeout = 1.35f;
	private const float JumpInputHoldTime = 0.22f;
	private const float OpposingJumpVelocityThreshold = 8.0f;
	private const float JumpPrepareMaxTime = 0.15f;
	private const float PrecisionLandingBrakeMinHeight = 16.0f;
	private const float PrecisionLandingBrakeDistance = 32.0f;
	private const float PrecisionLandingBrakeSpeed = 45.0f;
	private const float LandingStabilizeDistance = 32.0f;
	private const float LandingStabilizeVerticalSpeed = 45.0f;
	private const float LandingStabilizeMaxSpeed = 64.0f;
	private const float LandingStabilizeMinSpeed = 12.0f;
	private const float LandingStabilizeGain = 5.0f;
	private const float LandingStabilizeBrakeAccel = 600.0f;
	private const float TakeoffApproachBrakeDistance = 10.0f;
	private const float TakeoffApproachBrakeAccel = 1600.0f;
	private const float BlockedPathTargetResetDistance = 16.0f;
	private const float DirectGoalJumpMaxHorizontalDistance = 64.0f;
	private const float DirectGoalJumpMaxVerticalDistance = 56.0f;
	private const float DirectGoalJumpMinVerticalDistance = 12.0f;
	private const float JumpLandingDoneDistance = 2.0f;

	private readonly List<PlatformNavGraph.Edge> navPath = [];
	public List<PlatformNavGraph.Edge> NavPath => navPath;
	private readonly List<PlatformNavGraph.SurfaceEdge> surfaceNavPath = [];
	private readonly List<PlatformNavGraph.Node> goalCandidates = [];
	private readonly HashSet<(int From, int To)> blockedPathEdges = [];
	private Vector2? blockedPathTarget;
	private EdgeState activeEdge;
	private bool activeEdgeBlocksPath;
	private string? lastTraceStateKey;

	// Owner shortcuts
	private Game Game => owner.Game;
	private Vector2 Position => owner.Position;
	private bool IsGrounded => owner.IsGrounded;
	private bool ShouldBuildDebugNavLabel => owner.Game.IsPlatformNavDebugVisible || owner.Game.GymMode;

	// Public state Bud/Boss read to build PawnInput
	public bool JumpHeld { get; private set; }
	public bool ActiveEdgeAllowsWallJump => activeEdge.Edge is { Jump: { WallJump: true } };

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
		if (activeEdge.JumpHoldTimer > 0)
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

		if (TryNavigateSameSurface(graph, target, false, out moveX, out moveY))
			return true;

		var start = graph.FindNearestNode(Position, NavSearchDistance);
		graph.FindNearestNodes(target, GoalNodeSearchDistance, goalCandidates);
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
				DebugNavStartNodeId = start.Value.Id;
				DebugNavGoalNodeId = goalCandidates[0].Id;
				DebugNavTargetNodeId = null;
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
			DebugNavStartNodeId = start.Value.Id;
			DebugNavGoalNodeId = goal.Value.Id;
			DebugNavTargetNodeId = null;
			DebugNavLabel = "edge reached";
			TraceState($"edge-reached:{start.Value.Id}:{goal.Value.Id}", "edgeReached", ("startNode", start.Value.Id), ("goalNode", goal.Value.Id));
			return false;
		}

		if (edge.Kind is PlatformNavGraph.EdgeKind.Jump or PlatformNavGraph.EdgeKind.Fall)
		{
			DebugNavStartNodeId = start.Value.Id;
			DebugNavGoalNodeId = goal.Value.Id;
			DebugNavTargetNodeId = edge.To;
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
					DebugNavStartNodeId = start.Value.Id;
					DebugNavGoalNodeId = goal.Value.Id;
					DebugNavTargetNodeId = to.Id;
					DebugNavLabel = ShouldBuildDebugNavLabel ? $"edge stop {edge.Kind} {edge.From}->{edge.To}" : "edge stop";
					return false;
				}
				if (moveX == 0 && !IsNearNode(edgeTarget))
				{
					DebugNavStartNodeId = start.Value.Id;
					DebugNavGoalNodeId = goal.Value.Id;
					DebugNavTargetNodeId = to.Id;
					DebugNavLabel = ShouldBuildDebugNavLabel ? $"stalled {edge.Kind} {edge.From}->{edge.To}" : "stalled";
					return false;
				}
				break;

			case PlatformNavGraph.EdgeKind.RoomExit:
				moveX = MoveTowardNavTarget(edgeTarget);
				moveY = 0;
				break;

			default:
				DebugNavLabel = ShouldBuildDebugNavLabel ? $"unsupported {edge.Kind}" : "unsupported";
				return false;
		}

		DebugNavStartNodeId = start.Value.Id;
		DebugNavGoalNodeId = goal.Value.Id;
		DebugNavTargetNodeId = to.Id;
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
		DebugNavStartNodeId = null;
		DebugNavGoalNodeId = null;
		DebugNavTargetNodeId = null;
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
		if (activeEdge.Edge is not { } edge || Game.GetPlatformNavGraph() is not { } graph)
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

		if (activeEdge.Timer > ActiveEdgeTimeout)
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
				if (!TryExecuteJumpEdge(edge, from, to, target, out moveX, out jumpPressed))
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
					ClearActiveEdge(ShouldBuildDebugNavLabel ? $"{edge.Kind} done" : "done");
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
					ClearActiveEdge(ShouldBuildDebugNavLabel ? $"{edge.Kind} done" : "done");
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

	public void ClearActiveEdge(string label)
	{
		if (activeEdge.Edge is { } edge)
			Game.NavTrace?.EdgeEvent(Game, "edgeClear", edge, ("label", label), ("timer", activeEdge.Timer), ("pos", NavTrace.Vec(Position)), ("vel", NavTrace.Vec(owner.Velocity)));
		activeEdge = default;
		activeEdgeBlocksPath = false;
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
		DebugNavStartNodeId = null;
		DebugNavGoalNodeId = null;
		DebugNavTargetNodeId = null;
		navPath.Clear();
		surfaceNavPath.Clear();
		activeEdge = default;
		activeEdgeBlocksPath = false;
		DebugNavProbeRect = null;
		DebugNavProbeHit = false;
		DebugNavLabel = label;
	}

	private void ResetBlockedPathEdgesForTarget(Vector2 target)
	{
		if (blockedPathTarget.HasValue &&
			Vector2.DistanceSquared(blockedPathTarget.Value, target) <= BlockedPathTargetResetDistance * BlockedPathTargetResetDistance)
			return;

		blockedPathEdges.Clear();
		blockedPathTarget = target;
	}

	private bool TryDirectGoalJump(Vector2 target, out int moveX, out bool jumpPressed)
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
		if (IsGrounded)
			jumpPressed = true;

		navPath.Clear();
		surfaceNavPath.Clear();
		activeEdge = default;
		activeEdgeBlocksPath = false;
		DebugNavStartNodeId = null;
		DebugNavGoalNodeId = null;
		DebugNavTargetNodeId = null;
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

	private bool TryFindDirectJumpLaunchPath(PlatformNavGraph graph, int startId, Vector2 target, out PlatformNavGraph.Node goal)
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

	private bool CanDirectJumpFrom(Vector2 from, Vector2 target)
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

	private bool HasFootSupport(int moveX, int lookAhead, int castDepth)
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

	private bool OverlapsJumpthruSupport(RectInt probe)
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

		DebugNavStartNodeId = startNodeId;
		DebugNavGoalNodeId = goalNodeId;
		DebugNavTargetNodeId = next.To;
		StartActiveEdge(next, blockPathOnTimeout: true);
		DebugNavLabel = ShouldBuildDebugNavLabel ? $"jump handoff {blockedEdge.From}->{blockedEdge.To}->{next.To}" : "jump handoff";
		return TryExecuteActiveEdge(out moveX, out moveY, out jumpPressed);
	}

	private bool TryExecuteJumpEdge(
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
			activeEdge.JumpPlanTimer = 0;
			activeEdge.Timer = 0;
			Game.NavTrace?.EdgeEvent(Game, "jumpPlanStart", edge, ("plan", JumpPlanTrace(plan)), ("pos", NavTrace.Vec(Position)), ("vel", NavTrace.Vec(owner.Velocity)));
		}

		activeEdge.JumpPlanTimer += owner.Time.Delta;
		moveX = PlannedJumpMoveX(plan, target);
		if (moveX == 0 && hasPlannedHorizontal)
			moveX = takeoffDirection;

		if (!activeEdge.JumpStarted && activeEdge.JumpPlanTimer >= plan.RunupTime)
			StartPlannedJump(plan, out jumpPressed);
		else if (!activeEdge.JumpStarted && IsGrounded && moveX != 0 && !HasGroundSupportNextStep(moveX))
			StartPlannedJump(plan, out jumpPressed);

		if (activeEdge.JumpStarted && !jumpPressed)
			activeEdge.JumpHoldTimer = Calc.Approach(activeEdge.JumpHoldTimer, 0, owner.Time.Delta);

		if (activeEdge.JumpStarted && !IsGrounded)
		{
			activeEdge.Phase = ActiveEdgePhase.Airborne;
			if (plan.WallJump && !activeEdge.WallKicked)
			{
				// Steer into the wall and keep the jump pressed so the wall kick fires
				// the instant Bud touches it. The runtime input-lock then carries him
				// away; once we see the kick, switch to steering toward the landing.
				moveX = plan.Direction;
				jumpPressed = true;
				if (owner.IsWallJumpLocked)
				{
					activeEdge.WallKicked = true;
					activeEdge.JumpHoldTimer = Math.Clamp(plan.HoldTime, 0.055f, JumpInputHoldTime);
				}
			}
			else
				moveX = AirSteerTowardLanding(plan, from.Position, target, to.Kind, moveX);
		}
		else
		{
			if (activeEdge.JumpStarted)
				activeEdge.Phase = ActiveEdgePhase.Landing;
			DebugNavProbeRect = null;
			DebugNavProbeHit = false;
		}

		if (activeEdge.JumpStarted && IsGrounded && activeEdge.JumpPlanTimer > plan.RunupTime + 0.12f)
		{
			var landingDx = target.X - Position.X;
			var landingDy = MathF.Abs(target.Y - Position.Y);
			if (MathF.Abs(landingDx) <= JumpLandingDoneDistance && landingDy <= NavNodeReachDistance)
				ClearActiveEdge(ShouldBuildDebugNavLabel ? $"{edge.Kind} done" : "done");
			else if (MathF.Abs(landingDx) <= NavNodeReachDistance && landingDy <= NavNodeReachDistance)
			{
				moveX = Math.Sign(landingDx);
				if (moveX != 0 && HasGroundSupportAhead(moveX))
				{
					activeEdge.Phase = ActiveEdgePhase.Settle;
					DebugNavLabel = ShouldBuildDebugNavLabel ? $"{edge.Kind} settle landing {edge.From}->{edge.To}" : "settle";
					return true;
				}
				ClearActiveEdge(ShouldBuildDebugNavLabel ? $"{edge.Kind} done" : "done");
			}
			else
			{
				Game.NavTrace?.EdgeEvent(Game, "edgeMiss", edge, ("timer", activeEdge.Timer), ("pos", NavTrace.Vec(Position)), ("target", NavTrace.Vec(target)), ("vel", NavTrace.Vec(owner.Velocity)));
				ClearActiveEdge(ShouldBuildDebugNavLabel ? $"{edge.Kind} miss {edge.From}->{edge.To}" : "miss");
			}
			return false;
		}

		DebugNavLabel = ShouldBuildDebugNavLabel ? $"{edge.Kind} {edge.From}->{edge.To} {activeEdge.Phase} p {activeEdge.JumpPlanTimer:0.0}" : "jump";
		return true;
	}

	private void BrakeForTakeoffApproach(float dx)
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

	private static Vector2 ResolveSafeJumpLandingTarget(PlatformNavGraph graph, PlatformNavGraph.Node to)
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

	private void StartPlannedJump(PlatformNavGraph.JumpPlan plan, out bool jumpPressed)
	{
		jumpPressed = true;
		activeEdge.JumpStarted = true;
		activeEdge.Phase = ActiveEdgePhase.Airborne;
		activeEdge.JumpHoldTimer = Math.Clamp(plan.HoldTime, 0.055f, JumpInputHoldTime);
		if (activeEdge.Edge is { } edge)
			Game.NavTrace?.EdgeEvent(Game, "jumpTakeoff", edge, ("plan", JumpPlanTrace(plan)), ("pos", NavTrace.Vec(Position)), ("vel", NavTrace.Vec(owner.Velocity)));
	}

	private static PlatformNavGraph.JumpPlan DefaultJumpPlan(Vector2 from, Vector2 target)
	{
		var direction = Math.Sign(target.X - from.X);
		if (direction == 0) direction = 1;
		var height = MathF.Max(0, from.Y - target.Y);
		var distance = MathF.Abs(target.X - from.X);
		var hold = 0.055f;
		hold += Math.Clamp(height / 32.0f, 0, 1) * 0.105f;
		hold += Math.Clamp((distance - 16) / 40.0f, 0, 1) * 0.035f;
		return new(direction, 0, Math.Clamp(hold, 0.055f, JumpInputHoldTime), direction, 99.0f);
	}

	private bool CanStartJumpPlan(Vector2 source)
		=> IsGrounded &&
		   MathF.Abs(Position.X - source.X) <= NavNodeReachDistance * 0.25f &&
		   MathF.Abs(Position.Y - source.Y) <= Game.TileSize;

	private int PlannedJumpMoveX(PlatformNavGraph.JumpPlan plan, Vector2 landing)
	{
		if (activeEdge.JumpPlanTimer < plan.SwitchTime)
			return plan.Direction;
		if (plan.AirDirection != plan.Direction)
		{
			var remainingDirection = Math.Sign(landing.X - Position.X);
			if (remainingDirection == plan.Direction && MathF.Abs(landing.X - Position.X) > NavNodeReachDistance)
				return plan.Direction;
		}
		return plan.AirDirection;
	}

	private int AirSteerTowardLanding(
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
			["wall"] = plan.WallJump
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
		public float JumpHoldTimer;
		public bool JumpStarted;
		public bool JumpPlanStarted;
		public float JumpPrepareTimer;
		public float JumpPlanTimer;
		public bool WallKicked;

		public static EdgeState Start(PlatformNavGraph.Edge edge)
			=> new()
			{
				Edge = edge,
				Phase = edge.Kind == PlatformNavGraph.EdgeKind.Jump ? ActiveEdgePhase.ApproachTakeoff : ActiveEdgePhase.Runup
			};
	}
}
