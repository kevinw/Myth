using Foster.Framework;
using System.Numerics;
namespace TinyLink;

public class Bud : Pawn
{
	private static readonly Color Tint = new(0x7fb6ff);
	private const int FollowStopDistance = 18;
	private const int DesiredPlayerSpacing = 20;
	private const int NavNodeReachDistance = 8;
	private const int GroundSupportLookAhead = 6;
	private const int GroundSupportProbeWidth = 4;
	private const int GroundSupportCastDepth = 10;
	private const int ImmediateGroundSupportCastDepth = 4;
	private const float SafeLandingSearchDistance = Game.TileSize * 1.5f;
	private const float NavSearchDistance = 180;
	private const float GoalNodeSearchDistance = 56;
	private const float EnterDelay = 1.0f;
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
	private const float MirrorPlayerCrawlDelay = 0.25f;
	private const float MirrorPlayerCrawlReleaseDelay = 0.25f;
	private RoomChange? pendingRoomChange;
	private float enterTimer;
	private bool enteredRoom;
	private Point2 entryPosition;
	private readonly List<PlatformNavGraph.Edge> navPath = [];
	private bool jumpHeld;
	private float mirrorPlayerCrawlTimer;
	private float mirrorPlayerCrawlReleaseTimer;
	private bool mirrorPlayerCrawl;
	private bool activeJumpPlanStarted;
	private float activeJumpPrepareTimer;
	private PlatformNavGraph.Edge? activeEdge;
	private float activeEdgeTimer;
	private float activeJumpHoldTimer;
	private bool activeJumpStarted;
	private float activeJumpPlanTimer;
	public int? DebugNavStartNodeId { get; private set; }
	public int? DebugNavGoalNodeId { get; private set; }
	public int? DebugNavTargetNodeId { get; private set; }
	public string DebugNavLabel { get; private set; } = "idle";
	public IReadOnlyList<PlatformNavGraph.Edge> DebugNavPath => navPath;
	public RectInt? DebugNavProbeRect { get; private set; }
	public bool DebugNavProbeHit { get; private set; }
	protected override float GroundSpeedMultiplier => 1.0f;
	protected override float AirSpeedMultiplier => 1.0f;
	public bool CanSay => Game.CanShowBudSpeech();

	public Bud()
	{
		Sprite = Assets.GetSprite("companion") ?? Assets.GetSprite("player");
		Hitbox = new(new RectInt(-4, -12, 8, 12));
		Depth = -4;
		Play("idle");
	}

	public override void Added()
	{
		Game.RoomChanged += OnRoomChanged;
	}

	public override void Destroyed()
	{
		Game.RoomChanged -= OnRoomChanged;
	}

	public override PawnInput GetInput()
	{
		var moveX = 0;
		var moveY = 0;
		var jumpPressed = false;
		var player = Game.GetFirst<Player>();
		var gymTarget = Game.GymMode ? Game.GymTarget : null;
		var followTarget = gymTarget != null
			? (Vector2?)gymTarget.Position
			: player != null ? DesiredFollowPosition(player) : null;
		if (!TryExecuteActiveEdge(out moveX, out moveY, out jumpPressed))
		{
			if (gymTarget != null)
			{
				if (!TryNavigateToPosition(gymTarget.Position, out moveX, out moveY, out jumpPressed))
				{
					moveX = GetFallbackMoveX(followTarget);
					moveY = 0;
					SetDebugNavState("gym fallback");
				}
			}
			else if (player == null || !TryNavigateToPlayer(player, out moveX, out moveY, out jumpPressed))
			{
				var navLabel = DebugNavLabel;
				moveX = GetFallbackMoveX(followTarget, player);
				moveY = ShouldMirrorPlayerCrawl(player) ? 1 : 0;
				if (grounded && moveX != 0 && !HasGroundSupportAhead(moveX))
					moveX = 0;
				if (player == null)
					SetDebugNavState("no player");
				else
					SetDebugNavState(moveX == 0 ? (IsBesidePlayer(player) ? "beside player" : navLabel) : $"fallback walk {moveX}");
			}
		}

		if (activeJumpHoldTimer > 0)
			jumpHeld = true;
		else if (jumpPressed)
			jumpHeld = true;
		else if (grounded || Velocity.Y >= 0)
			jumpHeld = false;

		if (moveX == 0)
		{
			var faceTarget = gymTarget?.Position ?? player?.Position;
			if (faceTarget != null)
			{
				var face = Math.Sign(faceTarget.Value.X - Position.X);
				if (face != 0)
					Facing = face;
			}
		}

		return new(
			MoveX: moveX,
			MoveY: moveY,
			JumpPressed: jumpPressed,
			JumpDown: jumpHeld,
			DashPressed: false,
			AllowWallJump: false
		);
	}

	public override void Update()
	{
		if (pendingRoomChange is { } change)
		{
			enterTimer = Calc.Approach(enterTimer, 0, Time.Delta);
			SetDebugNavState($"entering {enterTimer:0.0}s");
			if (enterTimer <= 0)
				EnterRoom(change);
			return;
		}

		base.Update();

		if (!enteredRoom)
		{
			entryPosition = Position;
			enteredRoom = true;
		}

		if (Position.Y > Game.Bounds.Bottom + 12)
		{
			Game.GymBudDied = true;
			Stop();
			Position = entryPosition;
		}
	}

	public override void Render(Batcher batcher)
	{
		RenderSpriteSubtexture(batcher, Tint);
	}

	public void Say(string text)
		=> Game.ShowBudSpeech(Position + new Vector2(0, -32), text);

	private bool TryNavigateToPlayer(Player player, out int moveX, out int moveY, out bool jumpPressed)
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
		var goal = FindGoalNodeBesidePlayer(graph, player);
		if (!start.HasValue || !goal.HasValue || start.Value.Id == goal.Value.Id)
		{
			SetDebugNavState(start.HasValue && goal.HasValue ? "at goal" : "no node");
			return false;
		}

		if (!graph.TryFindPath(start.Value.Id, goal.Value.Id, navPath) || navPath.Count <= 0)
		{
			DebugNavStartNodeId = start.Value.Id;
			DebugNavGoalNodeId = goal.Value.Id;
			DebugNavTargetNodeId = null;
			DebugNavLabel = $"no path {start.Value.Id}->{goal.Value.Id}";
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

		var from = graph.Nodes[edge.From];
		var to = graph.Nodes[edge.To];
		var target = to.Position;
		switch (edge.Kind)
		{
			case PlatformNavGraph.EdgeKind.Walk:
			case PlatformNavGraph.EdgeKind.Crawl:
				moveX = MoveTowardNavTarget(target);
				moveY = edge.Kind == PlatformNavGraph.EdgeKind.Crawl || to.RequiresDucking || ShouldMirrorPlayerCrawl(player) ? 1 : 0;
				if (moveX != 0 && !HasGroundSupportAhead(moveX))
				{
					if (TryContinueWithJumpAfterBlockedWalk(graph, edge, start.Value.Id, goal.Value.Id, out moveX, out moveY, out jumpPressed))
						return true;

					DebugNavStartNodeId = start.Value.Id;
					DebugNavGoalNodeId = goal.Value.Id;
					DebugNavTargetNodeId = to.Id;
					DebugNavLabel = $"edge stop {edge.Kind} {edge.From}->{edge.To}";
					return false;
				}
				if (moveX == 0 && !IsNearNode(target))
				{
					DebugNavStartNodeId = start.Value.Id;
					DebugNavGoalNodeId = goal.Value.Id;
					DebugNavTargetNodeId = to.Id;
					DebugNavLabel = $"stalled {edge.Kind} {edge.From}->{edge.To}";
					return false;
				}
				break;

			case PlatformNavGraph.EdgeKind.RoomExit:
				moveX = MoveTowardNavTarget(target);
				moveY = ShouldMirrorPlayerCrawl(player) ? 1 : 0;
				break;

			default:
				DebugNavLabel = $"unsupported {edge.Kind}";
				return false;
		}

		DebugNavStartNodeId = start.Value.Id;
		DebugNavGoalNodeId = goal.Value.Id;
		DebugNavTargetNodeId = to.Id;
		DebugNavLabel = $"{edge.Kind} {edge.From}->{edge.To} len {navPath.Count}";
		return true;
	}

	private bool TryNavigateToPosition(Vector2 target, out int moveX, out int moveY, out bool jumpPressed)
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
			DebugNavLabel = $"no path {start.Value.Id}->{goal.Value.Id}";
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
					DebugNavLabel = $"edge stop {edge.Kind} {edge.From}->{edge.To}";
					return false;
				}
				if (moveX == 0 && !IsNearNode(edgeTarget))
				{
					DebugNavStartNodeId = start.Value.Id;
					DebugNavGoalNodeId = goal.Value.Id;
					DebugNavTargetNodeId = to.Id;
					DebugNavLabel = $"stalled {edge.Kind} {edge.From}->{edge.To}";
					return false;
				}
				break;

			case PlatformNavGraph.EdgeKind.RoomExit:
				moveX = MoveTowardNavTarget(edgeTarget);
				moveY = 0;
				break;

			default:
				DebugNavLabel = $"unsupported {edge.Kind}";
				return false;
		}

		DebugNavStartNodeId = start.Value.Id;
		DebugNavGoalNodeId = goal.Value.Id;
		DebugNavTargetNodeId = to.Id;
		DebugNavLabel = $"{edge.Kind} {edge.From}->{edge.To} len {navPath.Count}";
		return true;
	}

	private void StartActiveEdge(PlatformNavGraph.Edge edge)
	{
		activeEdge = edge;
		activeEdgeTimer = 0;
		activeJumpHoldTimer = 0;
		activeJumpStarted = false;
		activeJumpPlanStarted = false;
		activeJumpPrepareTimer = 0;
		activeJumpPlanTimer = 0;
	}

	private bool TryGetActionableEdge(PlatformNavGraph graph, out PlatformNavGraph.Edge edge)
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

	private int MoveTowardNavTarget(Vector2 target)
	{
		var dx = target.X - Position.X;
		if (MathF.Abs(dx) > NavNodeReachDistance * 0.5f)
			return Math.Sign(dx);
		return 0;
	}

	private bool TryContinueWithJumpAfterBlockedWalk(
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
		DebugNavLabel = $"jump handoff {blockedEdge.From}->{blockedEdge.To}->{next.To}";
		return TryExecuteActiveEdge(out moveX, out moveY, out jumpPressed);
	}

	private bool HasGroundSupportAhead(int moveX)
	{
		if (!grounded || moveX == 0)
			return true;

		return HasFootSupport(moveX, GroundSupportLookAhead, GroundSupportCastDepth);
	}

	private bool HasGroundSupportNextStep(int moveX)
	{
		if (!grounded || moveX == 0)
			return true;

		return HasFootSupport(moveX, 1, ImmediateGroundSupportCastDepth);
	}

	private bool HasFootSupport(int moveX, int lookAhead, int castDepth)
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

	private bool OverlapsJumpthruSupport(RectInt probe)
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

	private bool TryExecuteActiveEdge(out int moveX, out int moveY, out bool jumpPressed)
	{
		moveX = 0;
		moveY = 0;
		jumpPressed = false;
		if (activeEdge is not { } edge || Game.GetPlatformNavGraph() is not { } graph)
			return false;

		if (edge.From < 0 || edge.From >= graph.Nodes.Count || edge.To < 0 || edge.To >= graph.Nodes.Count)
		{
			ClearActiveEdge("bad edge");
			return false;
		}

		activeEdgeTimer += Time.Delta;
		var from = graph.Nodes[edge.From];
		var to = graph.Nodes[edge.To];
		var target = edge.Kind == PlatformNavGraph.EdgeKind.Jump
			? ResolveSafeJumpLandingTarget(graph, to)
			: to.Position;

		if (activeEdgeTimer > ActiveEdgeTimeout)
		{
			ClearActiveEdge($"{edge.Kind} timeout");
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
					ClearActiveEdge($"{edge.Kind} edge stop");
					return false;
				}
				if (moveX == 0 && IsNearNode(target))
				{
					ClearActiveEdge($"{edge.Kind} done");
					return false;
				}
				DebugNavLabel = $"{edge.Kind} {edge.From}->{edge.To} t {activeEdgeTimer:0.0}";
				break;

			case PlatformNavGraph.EdgeKind.Fall:
				moveX = Math.Sign(to.Position.X - from.Position.X);
				if (!grounded && MathF.Abs(target.X - Position.X) > NavNodeReachDistance * 0.5f)
					moveX = Math.Sign(target.X - Position.X);
				if (grounded && activeEdgeTimer > 0.12f && IsNearNode(target))
				{
					ClearActiveEdge($"{edge.Kind} done");
					return false;
				}
				DebugNavLabel = $"{edge.Kind} {edge.From}->{edge.To} t {activeEdgeTimer:0.0}";
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

		if (!activeJumpPlanStarted)
		{
			if (!CanStartJumpPlan(from.Position))
			{
				moveX = Math.Sign(from.Position.X - Position.X);
				if (grounded && moveX != 0 && !HasGroundSupportAhead(moveX))
				{
					ClearActiveEdge("approach ledge");
					return false;
				}
				DebugNavLabel = $"{edge.Kind} approach {edge.From}->{edge.To}";
				return true;
			}

			var opposingVelocity = grounded &&
				takeoffDirection != 0 &&
				MathF.Sign(Velocity.X) == -takeoffDirection &&
				MathF.Abs(Velocity.X) > OpposingJumpVelocityThreshold;
			if (opposingVelocity && activeJumpPrepareTimer < JumpPrepareMaxTime)
			{
				activeJumpPrepareTimer += Time.Delta;
				DebugNavLabel = $"{edge.Kind} settle {edge.From}->{edge.To} vx {Velocity.X:0}";
				return true;
			}

			activeJumpPlanStarted = true;
			activeJumpPlanTimer = 0;
			activeEdgeTimer = 0;
		}

		activeJumpPlanTimer += Time.Delta;
		moveX = PlannedJumpMoveX(plan, target);
		if (moveX == 0)
			moveX = takeoffDirection;

		if (!activeJumpStarted && activeJumpPlanTimer >= plan.RunupTime)
			StartPlannedJump(plan, out jumpPressed);
		else if (!activeJumpStarted && grounded && moveX != 0 && !HasGroundSupportNextStep(moveX))
			StartPlannedJump(plan, out jumpPressed);

		if (activeJumpStarted)
			activeJumpHoldTimer = Calc.Approach(activeJumpHoldTimer, 0, Time.Delta);

		if (activeJumpStarted && !grounded)
			moveX = AirSteerTowardLanding(plan, from.Position, target, moveX);
		else
		{
			DebugNavProbeRect = null;
			DebugNavProbeHit = false;
		}

		if (activeJumpStarted && grounded && activeJumpPlanTimer > plan.RunupTime + 0.12f &&
			(IsNearNode(target) || MathF.Abs(Position.Y - target.Y) <= NavNodeReachDistance))
		{
			ClearActiveEdge($"{edge.Kind} done");
			return false;
		}

		DebugNavLabel = $"{edge.Kind} {edge.From}->{edge.To} p {activeJumpPlanTimer:0.0}";
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
		activeJumpStarted = true;
		activeJumpHoldTimer = Math.Clamp(plan.HoldTime, 0.055f, JumpInputHoldTime);
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
		if (activeJumpPlanTimer < plan.SwitchTime)
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

	private bool IsNearNode(Vector2 node)
		=> MathF.Abs(Position.X - node.X) <= NavNodeReachDistance &&
		   MathF.Abs(Position.Y - node.Y) <= NavNodeReachDistance;

	private bool ShouldMirrorPlayerCrawl(Player? player)
	{
		if (player == null || !grounded || !player.IsGrounded || !player.IsDucking)
		{
			return UpdateMirrorPlayerCrawl(false);
		}

		var nearby = MathF.Abs(player.Position.X - Position.X) <= DesiredPlayerSpacing * 2 &&
			MathF.Abs(player.Position.Y - Position.Y) <= Game.TileSize + 2;
		if (!nearby)
			return UpdateMirrorPlayerCrawl(false);

		return UpdateMirrorPlayerCrawl(true);
	}

	private bool UpdateMirrorPlayerCrawl(bool wantsCrawl)
	{
		if (!wantsCrawl)
		{
			mirrorPlayerCrawlTimer = 0;
			if (!mirrorPlayerCrawl)
				return false;

			mirrorPlayerCrawlReleaseTimer += Time.Delta;
			if (mirrorPlayerCrawlReleaseTimer >= MirrorPlayerCrawlReleaseDelay)
				mirrorPlayerCrawl = false;
			return mirrorPlayerCrawl;
		}

		mirrorPlayerCrawlReleaseTimer = 0;
		if (mirrorPlayerCrawl)
			return true;

		mirrorPlayerCrawlTimer += Time.Delta;
		if (mirrorPlayerCrawlTimer >= MirrorPlayerCrawlDelay)
			mirrorPlayerCrawl = true;
		return mirrorPlayerCrawl;
	}

	private void ClearActiveEdge(string label)
	{
		activeEdge = null;
		activeEdgeTimer = 0;
		activeJumpHoldTimer = 0;
		activeJumpStarted = false;
		activeJumpPlanStarted = false;
		activeJumpPrepareTimer = 0;
		activeJumpPlanTimer = 0;
		DebugNavProbeRect = null;
		DebugNavProbeHit = false;
		DebugNavLabel = label;
	}

	private void SetDebugNavState(string label)
	{
		DebugNavStartNodeId = null;
		DebugNavGoalNodeId = null;
		DebugNavTargetNodeId = null;
		navPath.Clear();
		activeEdge = null;
		activeEdgeTimer = 0;
		activeJumpHoldTimer = 0;
		activeJumpStarted = false;
		activeJumpPlanStarted = false;
		activeJumpPrepareTimer = 0;
		activeJumpPlanTimer = 0;
		DebugNavProbeRect = null;
		DebugNavProbeHit = false;
		DebugNavLabel = label;
	}

	private Vector2 DesiredFollowPosition(Player player)
	{
		var side = Position.X <= player.Position.X ? -1 : 1;
		return player.Position + new Vector2(side * DesiredPlayerSpacing, 0);
	}

	private bool IsBesidePlayer(Player player)
		=> MathF.Abs(Position.X - player.Position.X) <= FollowStopDistance &&
		   MathF.Abs(Position.Y - player.Position.Y) <= Game.TileSize + NavNodeReachDistance;

	private PlatformNavGraph.Node? FindGoalNodeBesidePlayer(PlatformNavGraph graph, Player player)
	{
		var preferredSide = Position.X <= player.Position.X ? -1 : 1;
		var preferred = FindGoalNodeBesidePlayer(graph, player, preferredSide);
		if (preferred.HasValue)
			return preferred;

		return FindGoalNodeBesidePlayer(graph, player, -preferredSide) ??
			   graph.FindNearestNode(player.Position, GoalNodeSearchDistance);
	}

	private static PlatformNavGraph.Node? FindGoalNodeBesidePlayer(PlatformNavGraph graph, Player player, int side)
	{
		var desired = player.Position + new Vector2(side * DesiredPlayerSpacing, 0);
		PlatformNavGraph.Node? best = null;
		var bestScore = GoalNodeSearchDistance * GoalNodeSearchDistance;
		foreach (var node in graph.Nodes)
		{
			if (node.Kind == PlatformNavGraph.NodeKind.RoomExit)
				continue;

			var distanceSquared = Vector2.DistanceSquared(node.Position, desired);
			if (distanceSquared >= bestScore)
				continue;

			if (MathF.Abs(node.Position.X - player.Position.X) < DesiredPlayerSpacing * 0.5f)
				continue;

			best = node;
			bestScore = distanceSquared;
		}
		return best;
	}

	private int GetFallbackMoveX(Vector2? target, Player? player = null)
	{
		if (target == null)
			return 0;

		var dx = target.Value.X - Position.X;
		if (MathF.Abs(dx) > FollowStopDistance)
			return Math.Sign(dx);

		if (player != null && !IsBesidePlayer(player))
			return Position.X <= player.Position.X ? -1 : 1;

		return 0;
	}

	private void OnRoomChanged(RoomChange change)
	{
		if (change.Kind == RoomChangeKind.InitialLoad)
			return;

		pendingRoomChange = change;
		enterTimer = EnterDelay;
		Visible = false;
		CollidesWithSolids = false;
		Velocity = Vector2.Zero;
	}

	private void EnterRoom(RoomChange change)
	{
		pendingRoomChange = null;
		Position = FindEnterPosition(change);
		entryPosition = Position;
		Velocity = Vector2.Zero;
		CollidesWithSolids = true;
		Visible = true;
		Game.TryBudStorylet("bud_entered");
	}

	private Point2 FindEnterPosition(RoomChange change)
	{
		var player = Game.GetFirst<Player>();
		var anchor = player?.Position ?? (Point2)change.PlayerPosition;
		var bounds = change.To.WorldBounds;
		var direction = change.Direction;

		var basePosition = anchor;
		if (change.Kind == RoomChangeKind.EdgeTransition && direction != Point2.Zero)
		{
			if (direction.X > 0)
				basePosition.X = bounds.Left + Game.TileSize;
			else if (direction.X < 0)
				basePosition.X = bounds.Right - Game.TileSize;
			else if (direction.Y > 0)
				basePosition.Y = bounds.Top + Game.TileSize * 2;
			else if (direction.Y < 0)
				basePosition.Y = bounds.Bottom - 1;
		}

		basePosition.X = Math.Clamp(basePosition.X, bounds.Left + Game.TileSize, bounds.Right - Game.TileSize);
		basePosition.Y = Math.Clamp(basePosition.Y, bounds.Top + Game.TileSize * 2, bounds.Bottom - 1);

		ReadOnlySpan<Point2> offsets =
		[
			Point2.Zero,
			new Point2(0, -Game.TileSize),
			new Point2(0, -Game.TileSize * 2),
			new Point2(Game.TileSize, 0),
			new Point2(-Game.TileSize, 0),
			new Point2(Game.TileSize * 2, 0),
			new Point2(-Game.TileSize * 2, 0)
		];

		foreach (var offset in offsets)
		{
			var position = basePosition + offset;
			var hitbox = Hitbox.Rect + position;
			if (!Game.OverlapsRoomCollision(hitbox))
				return position;
		}

		return basePosition;
	}
}
