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
	private const float OpposingJumpVelocityThreshold = 24.0f;
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

	private readonly List<PlatformNavGraph.Edge> navPath = [];
	public List<PlatformNavGraph.Edge> NavPath => navPath;
	private EdgeState activeEdge;

	// Owner shortcuts
	private Game Game => owner.Game;
	private Vector2 Position => owner.Position;
	private bool IsGrounded => owner.IsGrounded;
	private bool ShouldBuildDebugNavLabel => owner.Game.IsPlatformNavDebugVisible;

	// Public state Bud/Boss read to build PawnInput
	public bool JumpHeld { get; private set; }

	// Debug state — settable so Bud's custom nav logic can update it directly
	public int? DebugNavStartNodeId { get; set; }
	public int? DebugNavGoalNodeId { get; set; }
	public int? DebugNavTargetNodeId { get; set; }
	public string DebugNavLabel { get; set; } = "idle";
	public IReadOnlyList<PlatformNavGraph.Edge> DebugNavPath => navPath;
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
			return false;
		}

		var start = graph.FindNearestNode(Position, NavSearchDistance);
		var goal = graph.FindNearestNode(target, GoalNodeSearchDistance);
		if (!start.HasValue || !goal.HasValue || start.Value.Id == goal.Value.Id)
		{
			SetDebugNavState(start.HasValue && goal.HasValue ? "at target" : "no node");
			return false;
		}

		if (!graph.TryFindPath(start.Value.Id, goal.Value.Id, navPath) || navPath.Count <= 0)
		{
			DebugNavStartNodeId = start.Value.Id;
			DebugNavGoalNodeId = goal.Value.Id;
			DebugNavTargetNodeId = null;
			DebugNavLabel = ShouldBuildDebugNavLabel ? $"no path {start.Value.Id}->{goal.Value.Id}" : "no path";
			return false;
		}

		if (!TryGetActionableEdge(graph, out var edge))
		{
			DebugNavStartNodeId = start.Value.Id;
			DebugNavGoalNodeId = goal.Value.Id;
			DebugNavTargetNodeId = null;
			DebugNavLabel = "edge reached";
			return false;
		}

		if (edge.Kind is PlatformNavGraph.EdgeKind.Jump or PlatformNavGraph.EdgeKind.Fall)
		{
			DebugNavStartNodeId = start.Value.Id;
			DebugNavGoalNodeId = goal.Value.Id;
			DebugNavTargetNodeId = edge.To;
			StartActiveEdge(edge);
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
			ClearActiveEdge(ShouldBuildDebugNavLabel ? $"{edge.Kind} timeout" : "timeout");
			return false;
		}

		switch (edge.Kind)
		{
			case PlatformNavGraph.EdgeKind.Jump:
				if (!TryExecuteJumpEdge(edge, from, to, target, out moveX, out jumpPressed))
					return false;
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

	public void StartActiveEdge(PlatformNavGraph.Edge edge) => activeEdge = EdgeState.Start(edge);
	public void ClearActiveEdge(string label)
	{
		activeEdge = default;
		DebugNavProbeRect = null;
		DebugNavProbeHit = false;
		DebugNavLabel = label;
	}

	public void SetDebugNavState(string label)
	{
		DebugNavStartNodeId = null;
		DebugNavGoalNodeId = null;
		DebugNavTargetNodeId = null;
		navPath.Clear();
		activeEdge = default;
		DebugNavProbeRect = null;
		DebugNavProbeHit = false;
		DebugNavLabel = label;
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
		StartActiveEdge(next);
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
		var takeoffDirection = plan.Direction != 0 ? plan.Direction : Math.Sign(target.X - from.Position.X);
		if (takeoffDirection == 0)
			takeoffDirection = owner.Facing == 0 ? 1 : owner.Facing;

		if (!activeEdge.JumpPlanStarted)
		{
			if (!CanStartJumpPlan(from.Position))
			{
				moveX = Math.Sign(from.Position.X - Position.X);
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
				activeEdge.JumpPrepareTimer += owner.Time.Delta;
				DebugNavLabel = ShouldBuildDebugNavLabel ? $"{edge.Kind} settle {edge.From}->{edge.To} vx {owner.Velocity.X:0}" : "settle";
				return true;
			}

			activeEdge.JumpPlanStarted = true;
			activeEdge.JumpPlanTimer = 0;
			activeEdge.Timer = 0;
		}

		activeEdge.JumpPlanTimer += owner.Time.Delta;
		moveX = PlannedJumpMoveX(plan, target);
		if (moveX == 0)
			moveX = takeoffDirection;

		if (!activeEdge.JumpStarted && activeEdge.JumpPlanTimer >= plan.RunupTime)
			StartPlannedJump(plan, out jumpPressed);
		else if (!activeEdge.JumpStarted && IsGrounded && moveX != 0 && !HasGroundSupportNextStep(moveX))
			StartPlannedJump(plan, out jumpPressed);

		if (activeEdge.JumpStarted)
			activeEdge.JumpHoldTimer = Calc.Approach(activeEdge.JumpHoldTimer, 0, owner.Time.Delta);

		if (activeEdge.JumpStarted && !IsGrounded)
			moveX = AirSteerTowardLanding(plan, from.Position, target, moveX);
		else
		{
			DebugNavProbeRect = null;
			DebugNavProbeHit = false;
		}

		if (activeEdge.JumpStarted && IsGrounded && activeEdge.JumpPlanTimer > plan.RunupTime + 0.12f &&
			(IsNearNode(target) || MathF.Abs(Position.Y - target.Y) <= NavNodeReachDistance))
		{
			ClearActiveEdge(ShouldBuildDebugNavLabel ? $"{edge.Kind} done" : "done");
			return false;
		}

		DebugNavLabel = ShouldBuildDebugNavLabel ? $"{edge.Kind} {edge.From}->{edge.To} p {activeEdge.JumpPlanTimer:0.0}" : "jump";
		return true;
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
		activeEdge.JumpHoldTimer = Math.Clamp(plan.HoldTime, 0.055f, JumpInputHoldTime);
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
		   MathF.Abs(Position.X - source.X) <= NavNodeReachDistance * 1.5f &&
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

	private int AirSteerTowardLanding(PlatformNavGraph.JumpPlan plan, Vector2 takeoff, Vector2 landing, int plannedMoveX)
	{
		var dx = landing.X - Position.X;
		var remainingX = MathF.Abs(dx);
		var steer = plannedMoveX;
		var height = MathF.Max(0, takeoff.Y - landing.Y);
		var targetDirection = Math.Sign(dx);
		var velocityDirection = Math.Sign(owner.Velocity.X);
		if (height >= PrecisionLandingBrakeMinHeight &&
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

		if (owner.Velocity.Y >= LandingStabilizeVerticalSpeed && remainingX <= LandingStabilizeDistance)
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

	private struct EdgeState
	{
		public PlatformNavGraph.Edge? Edge;
		public float Timer;
		public float JumpHoldTimer;
		public bool JumpStarted;
		public bool JumpPlanStarted;
		public float JumpPrepareTimer;
		public float JumpPlanTimer;

		public static EdgeState Start(PlatformNavGraph.Edge edge) => new() { Edge = edge };
	}
}
