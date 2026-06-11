using Foster.Framework;
using System.Numerics;

namespace TinyLink;

/// <summary>
/// Pawn subclass that adds platform-nav-graph navigation infrastructure.
/// Subclasses implement GetInput() to choose where to go.
/// </summary>
public abstract class NavPawn : Pawn
{
	protected const int NavNodeReachDistance = 8;
	protected const int GroundSupportLookAhead = 6;
	protected const int GroundSupportProbeWidth = 4;
	protected const int GroundSupportCastDepth = 10;
	protected const int ImmediateGroundSupportCastDepth = 4;
	protected const float SafeLandingSearchDistance = Game.TileSize * 1.5f;
	protected const float NavSearchDistance = 180;
	protected const float GoalNodeSearchDistance = 56;
	protected const float ActiveEdgeTimeout = 1.35f;
	protected const float JumpInputHoldTime = 0.22f;
	protected const float OpposingJumpVelocityThreshold = 24.0f;
	protected const float JumpPrepareMaxTime = 0.15f;
	protected const float PrecisionLandingBrakeMinHeight = 16.0f;
	protected const float PrecisionLandingBrakeDistance = 32.0f;
	protected const float PrecisionLandingBrakeSpeed = 45.0f;
	protected const float LandingStabilizeDistance = 32.0f;
	protected const float LandingStabilizeVerticalSpeed = 45.0f;
	protected const float LandingStabilizeMaxSpeed = 64.0f;
	protected const float LandingStabilizeMinSpeed = 12.0f;
	protected const float LandingStabilizeGain = 5.0f;
	protected const float LandingStabilizeBrakeAccel = 600.0f;

	protected readonly List<PlatformNavGraph.Edge> navPath = [];
	protected bool jumpHeld;
	protected ActiveNavEdgeState activeNavEdge;

	public int? DebugNavStartNodeId { get; protected set; }
	public int? DebugNavGoalNodeId { get; protected set; }
	public int? DebugNavTargetNodeId { get; protected set; }
	public string DebugNavLabel { get; protected set; } = "idle";
	public IReadOnlyList<PlatformNavGraph.Edge> DebugNavPath => navPath;
	public RectInt? DebugNavProbeRect { get; protected set; }
	public bool DebugNavProbeHit { get; protected set; }

	protected bool ShouldBuildDebugNavLabel => Game.IsPlatformNavDebugVisible;

	/// <summary>
	/// Call at the end of GetInput() to maintain the jumpHeld field correctly.
	/// </summary>
	protected void UpdateJumpHeld(bool jumpPressed)
	{
		if (activeNavEdge.JumpHoldTimer > 0)
			jumpHeld = true;
		else if (jumpPressed)
			jumpHeld = true;
		else if (grounded || Velocity.Y >= 0)
			jumpHeld = false;
	}

	protected bool TryNavigateToPosition(Vector2 target, out int moveX, out int moveY, out bool jumpPressed)
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

	protected void StartActiveEdge(PlatformNavGraph.Edge edge)
		=> activeNavEdge = ActiveNavEdgeState.Start(edge);

	protected bool TryGetActionableEdge(PlatformNavGraph graph, out PlatformNavGraph.Edge edge)
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

	protected int MoveTowardNavTarget(Vector2 target)
	{
		var dx = target.X - Position.X;
		if (MathF.Abs(dx) > NavNodeReachDistance * 0.5f)
			return Math.Sign(dx);
		return 0;
	}

	protected bool TryContinueWithJumpAfterBlockedWalk(
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

	protected bool HasGroundSupportAhead(int moveX)
	{
		if (!grounded || moveX == 0)
			return true;
		return HasFootSupport(moveX, GroundSupportLookAhead, GroundSupportCastDepth);
	}

	protected bool HasGroundSupportNextStep(int moveX)
	{
		if (!grounded || moveX == 0)
			return true;
		return HasFootSupport(moveX, 1, ImmediateGroundSupportCastDepth);
	}

	protected bool HasFootSupport(int moveX, int lookAhead, int castDepth)
	{
		var bounds = Hitbox.Rect + Position;
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

	protected bool OverlapsJumpthruSupport(RectInt probe)
	{
		foreach (var actor in Game.Actors)
		{
			if (actor == this || !actor.Mask.Has(Masks.Jumpthru))
				continue;
			if (actor.Hitbox.Shape == Hitbox.Shapes.Rect && probe.Overlaps(actor.Hitbox.Rect + actor.Position))
				return true;
		}
		return false;
	}

	protected bool TryExecuteActiveEdge(out int moveX, out int moveY, out bool jumpPressed)
	{
		moveX = 0;
		moveY = 0;
		jumpPressed = false;
		if (activeNavEdge.Edge is not { } edge || Game.GetPlatformNavGraph() is not { } graph)
			return false;

		if (edge.From < 0 || edge.From >= graph.Nodes.Count || edge.To < 0 || edge.To >= graph.Nodes.Count)
		{
			ClearActiveEdge("bad edge");
			return false;
		}

		activeNavEdge.Timer += Time.Delta;
		var from = graph.Nodes[edge.From];
		var to = graph.Nodes[edge.To];
		var target = edge.Kind == PlatformNavGraph.EdgeKind.Jump
			? ResolveSafeJumpLandingTarget(graph, to)
			: to.Position;

		if (activeNavEdge.Timer > ActiveEdgeTimeout)
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
				DebugNavLabel = ShouldBuildDebugNavLabel ? $"{edge.Kind} {edge.From}->{edge.To} t {activeNavEdge.Timer:0.0}" : "active edge";
				break;

			case PlatformNavGraph.EdgeKind.Fall:
				moveX = Math.Sign(to.Position.X - from.Position.X);
				if (!grounded && MathF.Abs(target.X - Position.X) > NavNodeReachDistance * 0.5f)
					moveX = Math.Sign(target.X - Position.X);
				if (grounded && activeNavEdge.Timer > 0.12f && IsNearNode(target))
				{
					ClearActiveEdge(ShouldBuildDebugNavLabel ? $"{edge.Kind} done" : "done");
					return false;
				}
				DebugNavLabel = ShouldBuildDebugNavLabel ? $"{edge.Kind} {edge.From}->{edge.To} t {activeNavEdge.Timer:0.0}" : "active edge";
				break;

			default:
				ClearActiveEdge("inactive");
				return false;
		}

		DebugNavTargetNodeId = to.Id;
		return true;
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
			takeoffDirection = Facing == 0 ? 1 : Facing;

		if (!activeNavEdge.JumpPlanStarted)
		{
			if (!CanStartJumpPlan(from.Position))
			{
				moveX = Math.Sign(from.Position.X - Position.X);
				if (grounded && moveX != 0 && !HasGroundSupportAhead(moveX))
				{
					ClearActiveEdge("approach ledge");
					return false;
				}
				DebugNavLabel = ShouldBuildDebugNavLabel ? $"{edge.Kind} approach {edge.From}->{edge.To}" : "approach";
				return true;
			}

			var opposingVelocity = grounded &&
				takeoffDirection != 0 &&
				MathF.Sign(Velocity.X) == -takeoffDirection &&
				MathF.Abs(Velocity.X) > OpposingJumpVelocityThreshold;
			if (opposingVelocity && activeNavEdge.JumpPrepareTimer < JumpPrepareMaxTime)
			{
				activeNavEdge.JumpPrepareTimer += Time.Delta;
				DebugNavLabel = ShouldBuildDebugNavLabel ? $"{edge.Kind} settle {edge.From}->{edge.To} vx {Velocity.X:0}" : "settle";
				return true;
			}

			activeNavEdge.JumpPlanStarted = true;
			activeNavEdge.JumpPlanTimer = 0;
			activeNavEdge.Timer = 0;
		}

		activeNavEdge.JumpPlanTimer += Time.Delta;
		moveX = PlannedJumpMoveX(plan, target);
		if (moveX == 0)
			moveX = takeoffDirection;

		if (!activeNavEdge.JumpStarted && activeNavEdge.JumpPlanTimer >= plan.RunupTime)
			StartPlannedJump(plan, out jumpPressed);
		else if (!activeNavEdge.JumpStarted && grounded && moveX != 0 && !HasGroundSupportNextStep(moveX))
			StartPlannedJump(plan, out jumpPressed);

		if (activeNavEdge.JumpStarted)
			activeNavEdge.JumpHoldTimer = Calc.Approach(activeNavEdge.JumpHoldTimer, 0, Time.Delta);

		if (activeNavEdge.JumpStarted && !grounded)
			moveX = AirSteerTowardLanding(plan, from.Position, target, moveX);
		else
		{
			DebugNavProbeRect = null;
			DebugNavProbeHit = false;
		}

		if (activeNavEdge.JumpStarted && grounded && activeNavEdge.JumpPlanTimer > plan.RunupTime + 0.12f &&
			(IsNearNode(target) || MathF.Abs(Position.Y - target.Y) <= NavNodeReachDistance))
		{
			ClearActiveEdge(ShouldBuildDebugNavLabel ? $"{edge.Kind} done" : "done");
			return false;
		}

		DebugNavLabel = ShouldBuildDebugNavLabel ? $"{edge.Kind} {edge.From}->{edge.To} p {activeNavEdge.JumpPlanTimer:0.0}" : "jump";
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
		activeNavEdge.JumpStarted = true;
		activeNavEdge.JumpHoldTimer = Math.Clamp(plan.HoldTime, 0.055f, JumpInputHoldTime);
	}

	private static PlatformNavGraph.JumpPlan DefaultJumpPlan(Vector2 from, Vector2 target)
	{
		var direction = Math.Sign(target.X - from.X);
		if (direction == 0)
			direction = 1;
		var height = MathF.Max(0, from.Y - target.Y);
		var distance = MathF.Abs(target.X - from.X);
		var hold = 0.055f;
		hold += Math.Clamp(height / 32.0f, 0, 1) * 0.105f;
		hold += Math.Clamp((distance - 16) / 40.0f, 0, 1) * 0.035f;
		return new(direction, 0, Math.Clamp(hold, 0.055f, JumpInputHoldTime), direction, 99.0f);
	}

	private bool CanStartJumpPlan(Vector2 source)
		=> grounded &&
		   MathF.Abs(Position.X - source.X) <= NavNodeReachDistance * 1.5f &&
		   MathF.Abs(Position.Y - source.Y) <= Game.TileSize;

	private int PlannedJumpMoveX(PlatformNavGraph.JumpPlan plan, Vector2 landing)
	{
		if (activeNavEdge.JumpPlanTimer < plan.SwitchTime)
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
		var velocityDirection = Math.Sign(Velocity.X);
		if (height >= PrecisionLandingBrakeMinHeight &&
			plan.AirDirection == plan.Direction &&
			remainingX <= PrecisionLandingBrakeDistance &&
			targetDirection != 0 &&
			velocityDirection == targetDirection &&
			MathF.Abs(Velocity.X) > PrecisionLandingBrakeSpeed)
		{
			steer = -velocityDirection;
		}
		else if (remainingX <= 18 || Velocity.Y > 0 && remainingX <= 28)
			steer = Math.Sign(dx);

		if (Velocity.Y >= LandingStabilizeVerticalSpeed && remainingX <= LandingStabilizeDistance)
		{
			var desiredVelocity = Math.Clamp(dx * LandingStabilizeGain, -LandingStabilizeMaxSpeed, LandingStabilizeMaxSpeed);
			if (MathF.Abs(dx) <= NavNodeReachDistance)
				desiredVelocity = 0;
			else if (MathF.Abs(desiredVelocity) < LandingStabilizeMinSpeed)
				desiredVelocity = MathF.Sign(desiredVelocity) * LandingStabilizeMinSpeed;

			Velocity.X = Calc.Approach(Velocity.X, desiredVelocity, LandingStabilizeBrakeAccel * Time.Delta);
			var velocityError = desiredVelocity - Velocity.X;
			steer = MathF.Abs(velocityError) > 4.0f ? Math.Sign(velocityError) : Math.Sign(dx);
		}

		DebugNavProbeRect = null;
		DebugNavProbeHit = false;
		return steer;
	}

	protected bool IsNearNode(Vector2 node)
		=> MathF.Abs(Position.X - node.X) <= NavNodeReachDistance &&
		   MathF.Abs(Position.Y - node.Y) <= NavNodeReachDistance;

	protected void ClearActiveEdge(string label)
	{
		activeNavEdge = default;
		DebugNavProbeRect = null;
		DebugNavProbeHit = false;
		DebugNavLabel = label;
	}

	protected void SetDebugNavState(string label)
	{
		DebugNavStartNodeId = null;
		DebugNavGoalNodeId = null;
		DebugNavTargetNodeId = null;
		navPath.Clear();
		activeNavEdge = default;
		DebugNavProbeRect = null;
		DebugNavProbeHit = false;
		DebugNavLabel = label;
	}

	protected struct ActiveNavEdgeState
	{
		public PlatformNavGraph.Edge? Edge;
		public float Timer;
		public float JumpHoldTimer;
		public bool JumpStarted;
		public bool JumpPlanStarted;
		public float JumpPrepareTimer;
		public float JumpPlanTimer;

		public static ActiveNavEdgeState Start(PlatformNavGraph.Edge edge)
			=> new() { Edge = edge };
	}
}
