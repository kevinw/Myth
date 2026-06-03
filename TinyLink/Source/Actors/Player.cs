using System.Numerics;
using Foster.Framework;

namespace TinyLink;

public readonly record struct PawnInput(
	int MoveX,
	int MoveY,
	bool JumpPressed,
	bool JumpDown,
	bool DashPressed,
	bool AllowWallJump = true);

public abstract class Pawn : Actor
{
	public enum States
	{
		Normal,
		Roll,
		Dash,
		Attack,
		BlinkStrike,
		Hurt,
		Start
	}

	protected static float MaxGroundSpeed => 93;
	protected static float MaxAirSpeed => 99;
	protected static float CrawlSpeed => 28;
	protected static float GroundAccel => 400;
	protected static float AirAccel => 260;
	protected static float Friction => 480;
	protected static float Gravity => 450;
	protected static float WallHugGravityMultiplier => 0.25f;
	protected static float JumpForce => -105;
	protected static float JumpTime => 0.18f;
	protected static float CoyoteJumpTime => 0.08f;
	protected static float WallJumpSpeed => 125;
	protected static float WallJumpInputLockTime => 0.22f;
	protected static float RollMinHorizontalSpeed => 45;
	protected static float RollMinFallSpeed => 20;
	protected static float RollSpeed => 145;
	protected static float DownhillRollSpeed => 185;
	protected static float RollDuration => 0.36f;
	protected static float RollFriction => 220;
	protected static float DownhillRollFriction => 90;
	protected static float RollJumpForce => -82;
	protected static float RollJumpTime => 0.11f;
	protected static float RollJumpSpeed => 150;
	protected static float RollJumpCarryTime => 0.28f;
	protected static float DashSpeed => 220;
	protected static float DashDuration => 0.15f;
	protected static float DashCooldown => 0.20f;
	protected static RectInt StandingHitboxRect => new(-4, -12, 8, 12);
	protected static RectInt DuckingHitboxRect => new(-4, -6, 8, 6);

	protected readonly StateMachine<States> state = new(States.Normal);
	protected float jumpTimer;
	protected float coyoteJumpTimer;
	protected int coyoteJumpY;
	protected bool grounded;
	protected bool ducking;
	protected float wallJumpInputLockTimer;
	protected int wallJumpDirection;
	protected int dashDirection;
	protected float dashCooldownTimer;
	protected float dashRemainder;
	protected float jumpForce = JumpForce;
	protected float rollJumpCarryTimer;
	protected PawnInput input;
	protected float JumpUpSquish;
	protected float RunStartTimer;
	protected bool RunStartSkid;

	public States State
	{
		get => state.Current;
		set => state.Change(value);
	}
	public bool IsGrounded => grounded;

	public abstract PawnInput GetInput();

	public override void Update()
	{
		var previousVelocity = Velocity;
		base.Update();
		input = GetInput();
		AfterActorUpdate(previousVelocity);

		var nowGrounded = Velocity.Y >= 0 && Grounded();
		if (nowGrounded && !grounded)
			OnLanded(previousVelocity);
		grounded = nowGrounded;
		if (grounded)
		{
			coyoteJumpTimer = CoyoteJumpTime;
			coyoteJumpY = Position.Y;
			wallJumpInputLockTimer = 0;
			rollJumpCarryTimer = 0;
		}
		else
		{
			if (coyoteJumpTimer > 0)
				coyoteJumpTimer = Calc.Approach(coyoteJumpTimer, 0, Time.Delta);
			if (wallJumpInputLockTimer > 0)
				wallJumpInputLockTimer = Calc.Approach(wallJumpInputLockTimer, 0, Time.Delta);
		}
		if (!grounded && rollJumpCarryTimer > 0)
			rollJumpCarryTimer = Calc.Approach(rollJumpCarryTimer, 0, Time.Delta);
		if (dashCooldownTimer > 0)
			dashCooldownTimer = Calc.Approach(dashCooldownTimer, 0, Time.Delta);

		switch (State)
		{
			case States.Normal:
				NormalState();
				break;
			case States.Roll:
				RollState();
				break;
			case States.Dash:
				DashState();
				break;
			default:
				CustomState();
				break;
		}

		AfterStateUpdate(previousVelocity);

		if (ducking && State != States.Normal && State != States.Roll && !AllowDuckingInState())
			ducking = false;
		if (ducking || State == States.Roll)
			Hitbox = new(DuckingHitboxRect);
		else
			Hitbox = new(StandingHitboxRect);

		if (jumpTimer > 0)
		{
			Velocity.Y = jumpForce;
			jumpTimer -= Time.Delta;
			if (!input.JumpDown)
				jumpTimer = 0;
		}

		if (!grounded && State != States.Dash)
		{
			var grav = Gravity;
			if (State == States.Normal && MathF.Abs(Velocity.Y) < 20 && input.JumpDown)
				grav *= 0.40f;
			if (State == States.Normal && Velocity.Y > 0 && IsWallHugging())
				grav *= WallHugGravityMultiplier;
			Velocity.Y += grav * Time.Delta;

			JumpUpSquish -= Time.Delta;
			if (JumpUpSquish <= 0)
				JumpUpSquish = 0;
		}

		AfterMovementUpdate(previousVelocity);
		state.Update(State == States.Roll && RollingDownSlope() ? 0 : Time.Delta);
	}

	protected virtual void AfterActorUpdate(Vector2 previousVelocity) { }
	protected virtual void OnLanded(Vector2 previousVelocity)
	{
		Squish = new Vector2(1.5f, 0.70f);
		TryStartRoll(previousVelocity);
	}
	protected virtual void AfterStateUpdate(Vector2 previousVelocity) { }
	protected virtual void AfterMovementUpdate(Vector2 previousVelocity) { }
	protected virtual void CustomState() { }
	protected virtual bool AllowDuckingInState() => false;
	protected virtual bool TryStartPawnAction(int moveX) => false;
	protected virtual void OnPawnJump() { }
		protected virtual void OnRollStarted() { }
		protected virtual void OnRollStopped(bool immediate) { }
		protected virtual void UpdateRollAudioPosition() { }
		protected virtual float GroundSpeedMultiplier => 1.0f;
		protected virtual float AirSpeedMultiplier => 1.0f;

	public override void OnCollideX()
	{
		base.OnCollideX();
		if (State == States.Roll)
		{
			OnRollStopped(false);
			State = States.Normal;
		}
	}

	protected void NormalState()
	{
		if (grounded && input.MoveY > 0)
			ducking = true;
		else if (ducking)
			ducking = grounded && !CanStandUp();
		else
			ducking = false;

		var moveX = input.MoveX;

		int facing = moveX;
		var movementInput = moveX;
		if (!grounded && wallJumpInputLockTimer > 0)
			movementInput = wallJumpDirection;

		var didRun = false;
		var didStartRunning = false;
		if (grounded)
		{
			if (ducking)
				Play(moveX == 0 ? "duck" : "crawl");
			else if (moveX == 0)
				Play("idle");
			else if (Velocity.X > 0 && moveX < 0 || Velocity.X < 0 && moveX > 0)
			{
				facing *= -1;
				Play("skid");
				RunStartTimer = 0;
				RunStartSkid = false;
			}
			else
			{
				RunStartTimer += Time.Delta;
				didRun = true;
				if (Velocity.X == 0)
					didStartRunning = true;
				if (RunStartTimer < 0.35f)
				{
					if (RunStartTimer < 0.15f && RunStartSkid)
						Play("skid");
					else
						Play("run_start");
				}
				else
					Play("run");
			}
		}
		else
		{
			var wallJumpDir = GetWallJumpDirection();
			if (IsWallHugging(wallJumpDir))
			{
				Facing = -(Signs)wallJumpDir;
				Play("wall_slide");
			}
			else if (JumpUpSquish > 0 && !grounded)
			{
				Play("jump_squish");
			}
			else
			{
				if (Velocity.X != 0 && moveX != 0 && Math.Sign(Velocity.X) != Math.Sign(moveX))
					Play("jump_turn");
				else
					Play("jump");
			}
		}

		if (!didRun)
		{
			RunStartTimer = 0;
			RunStartSkid = false;
		}

			var speedMultiplier = grounded ? GroundSpeedMultiplier : AirSpeedMultiplier;
			var maxSpeed = grounded ? MaxGroundSpeed * GroundSpeedMultiplier : MaxAirSpeed * AirSpeedMultiplier;
			var lastFacing = Facing;
			if (ducking)
				maxSpeed = CrawlSpeed * GroundSpeedMultiplier;

			Velocity.X += movementInput * (grounded ? GroundAccel * GroundSpeedMultiplier : AirAccel * AirSpeedMultiplier) * Time.Delta;
			if (!grounded && wallJumpInputLockTimer > 0)
				maxSpeed = MathF.Max(maxSpeed, WallJumpSpeed * speedMultiplier);
			if (!grounded && rollJumpCarryTimer > 0)
				maxSpeed = MathF.Max(maxSpeed, RollJumpSpeed * AirSpeedMultiplier);
		if (MathF.Abs(Velocity.X) > maxSpeed)
			Velocity.X = Calc.Approach(Velocity.X, MathF.Sign(Velocity.X) * maxSpeed, 2000 * Time.Delta);
		if (moveX == 0 && grounded)
			Velocity.X = Calc.Approach(Velocity.X, 0, Friction * Time.Delta);
		if (grounded && moveX != 0)
		{
			Facing = facing;
			if (lastFacing != Facing && didStartRunning)
				RunStartSkid = true;
		}

		var wallJump = GetWallJumpDirection();
		var canCoyoteJump = CanCoyoteJump();
		var canGroundJump = grounded && CanStandUp() || canCoyoteJump;
		var canWallJump = input.AllowWallJump && wallJump != 0 && moveX != 0;
		if (input.JumpPressed && (canGroundJump || canWallJump))
		{
			JumpUpSquish = 0;
			Squish = new Vector2(0.65f, 1.4f);
			if (canGroundJump)
			{
				if (canCoyoteJump)
					Position.Y = coyoteJumpY;
				coyoteJumpTimer = 0;
			}
			else
			{
				wallJumpDirection = wallJump;
				wallJumpInputLockTimer = WallJumpInputLockTime;
				facing = wallJumpDirection;
				Facing = wallJumpDirection;
				Velocity.X = wallJumpDirection * WallJumpSpeed;
			}
			StartJump(JumpForce, JumpTime);
			OnPawnJump();
		}

		if (input.DashPressed && TryStartDash(moveX))
			return;

		TryStartPawnAction(moveX);
	}

	protected void TryStartRoll(Vector2 impactVelocity)
	{
		if (State != States.Normal) return;
		if (input.MoveY <= 0) return;
		if (impactVelocity.Y < RollMinFallSpeed) return;
		if (MathF.Abs(impactVelocity.X) < RollMinHorizontalSpeed) return;

		Facing = Math.Sign(impactVelocity.X);
		Velocity = new(Facing * RollSpeed, 0);
		ducking = true;
		Squish = new(1.65f, 0.62f);
		Play("roll");
		State = States.Roll;
		OnRollStarted();
	}

	protected void RollState()
	{
		ducking = true;
		Play("roll");

		if (!grounded)
		{
			OnRollStopped(false);
			State = States.Normal;
			return;
		}

		UpdateRollAudioPosition();
		var rollingDownSlope = RollingDownSlope();
		var targetSpeed = rollingDownSlope ? DownhillRollSpeed : MaxGroundSpeed;
		var friction = rollingDownSlope ? DownhillRollFriction : RollFriction;
		Velocity.X = Calc.Approach(Velocity.X, Facing * targetSpeed, friction * Time.Delta);

		if (input.JumpPressed)
		{
			OnRollStopped(false);
			JumpUpSquish = 0;
			Squish = new Vector2(0.75f, 1.25f);
			Velocity.X = Facing * MathF.Max(MathF.Abs(Velocity.X), RollJumpSpeed);
			rollJumpCarryTimer = RollJumpCarryTime;
			StartJump(RollJumpForce, RollJumpTime);
			State = States.Normal;
			OnPawnJump();
			return;
		}

		if (state.Time >= RollDuration || input.MoveY <= 0)
		{
			OnRollStopped(false);
			State = States.Normal;
			ducking = grounded && input.MoveY > 0;
		}
	}

	private bool TryStartDash(int input)
	{
		if (dashCooldownTimer > 0)
			return false;

		dashDirection = input != 0 ? Math.Sign(input) : Facing;
		if (dashDirection == 0)
			dashDirection = Signs.Positive;

		Facing = dashDirection;
		dashRemainder = 0;
		dashCooldownTimer = DashCooldown;
		jumpTimer = 0;
		Velocity = Vector2.Zero;
		ducking = false;
		Squish = new Vector2(1.35f, 0.82f);
		Play("jump");
		State = States.Dash;
		return true;
	}

	protected void DashState()
	{
		ducking = false;
		Play("jump");
		Velocity = Vector2.Zero;
		MoveDash(DashSpeed * Time.Delta * dashDirection);

		if (state.Time >= DashDuration)
			State = States.Normal;
	}

	private void MoveDash(float value)
	{
		dashRemainder += value;
		var move = (int)dashRemainder;
		dashRemainder -= move;

		while (move != 0)
		{
			var sign = Math.Sign(move);
			var bounds = Hitbox.Rect + Position + new Point2(sign, 0);
			if (Game.OverlapsRoomCollision(bounds))
			{
				dashRemainder = 0;
				State = States.Normal;
				return;
			}

			Position.X += sign;
			move -= sign;
		}
	}

	protected bool RollingDownSlope()
	{
		if (!grounded || Velocity.LengthSquared() <= 0.001f)
			return false;

		var bounds = Hitbox.Rect + Position;
		if (!Game.TryFindSurface(this, bounds, Vector2.UnitY, 2, out var hit) || !hit.Slope.HasValue)
			return false;

		var normal = hit.Normal;
		if (normal.Y >= -0.001f)
			return false;

		var downhill = new Vector2(normal.Y, -normal.X);
		if (downhill.Y < 0)
			downhill = -downhill;

		return Vector2.Dot(Velocity, downhill) > 1;
	}

	protected void StartJump(float force, float duration)
	{
		jumpForce = force;
		jumpTimer = duration;
	}

	private bool CanCoyoteJump()
	{
		if (grounded || coyoteJumpTimer <= 0)
			return false;

		var originalY = Position.Y;
		Position.Y = coyoteJumpY;
		var canJump = CanStandUp();
		Position.Y = originalY;
		return canJump;
	}

	protected bool CanStandUp()
	{
		var standingBounds = StandingHitboxRect + Position;
		if (Game.OverlapsRoomCollision(standingBounds))
			return false;

		foreach (var other in Game.Actors)
		{
			if (other == this || other is Grid || !other.Mask.Has(Masks.Solid))
				continue;

			if (other.Hitbox.Overlaps(standingBounds - other.Position))
				return false;
		}

		return true;
	}

	private int GetWallJumpDirection()
	{
		if (grounded)
			return 0;

		var touchingLeft = OverlapsAny(Point2.Left, Masks.Solid);
		var touchingRight = OverlapsAny(Point2.Right, Masks.Solid);
		if (touchingLeft == touchingRight)
			return 0;

		return touchingLeft ? 1 : -1;
	}

	private bool IsWallHugging()
		=> IsWallHugging(GetWallJumpDirection());

	private bool IsWallHugging(int wallJumpDirection)
		=> wallJumpDirection != 0 && input.MoveX == -wallJumpDirection;
}

public class Companion : Pawn
{
	private static readonly Color Tint = new(0x7fb6ff);
	private const int FollowStopDistance = 18;
	private const int DesiredPlayerSpacing = 20;
	private const int NavNodeReachDistance = 8;
		private const float NavSearchDistance = 180;
		private const float GoalNodeSearchDistance = 56;
		private const float EnterDelay = 1.0f;
		private const float ActiveEdgeTimeout = 1.35f;
		private const float JumpInputHoldTime = 0.22f;
		private RoomChange? pendingRoomChange;
		private float enterTimer;
		private readonly List<PlatformNavGraph.Edge> navPath = [];
	private bool jumpHeld;
	private PlatformNavGraph.Edge? activeEdge;
	private float activeEdgeTimer;
	private float activeJumpHoldTimer;
	private bool activeJumpStarted;
	public int? DebugNavStartNodeId { get; private set; }
	public int? DebugNavGoalNodeId { get; private set; }
		public int? DebugNavTargetNodeId { get; private set; }
		public string DebugNavLabel { get; private set; } = "idle";
		public IReadOnlyList<PlatformNavGraph.Edge> DebugNavPath => navPath;
		protected override float GroundSpeedMultiplier => 0.75f;
		protected override float AirSpeedMultiplier => 0.80f;
		public bool CanSay => Game.CanShowCompanionSpeech();

		public Companion()
		{
		Sprite = Assets.GetSprite("player");
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
		var jumpPressed = false;
		var player = Game.GetFirst<Player>();
		var followTarget = player != null ? DesiredFollowPosition(player) : (Vector2?)null;
		if (!TryExecuteActiveEdge(out moveX, out jumpPressed))
		{
			if (player == null || !TryNavigateToPlayer(player, out moveX, out jumpPressed))
			{
				moveX = GetFallbackMoveX(followTarget);
				if (player == null)
					SetDebugNavState("no player");
				else
					SetDebugNavState(moveX == 0 ? "beside player" : $"fallback walk {moveX}");
			}
		}

		if (activeJumpHoldTimer > 0)
			jumpHeld = true;
		else if (jumpPressed)
			jumpHeld = true;
		else if (grounded || Velocity.Y >= 0)
			jumpHeld = false;

		if (moveX == 0 && player != null)
		{
			var face = Math.Sign(player.Position.X - Position.X);
			if (face != 0)
				Facing = face;
		}

		return new(
			MoveX: moveX,
			MoveY: 0,
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
	}

		public override void Render(Batcher batcher)
		{
			RenderSpriteSubtexture(batcher, Tint);
		}

		public void Say(string text)
			=> Game.ShowCompanionSpeech(Position + new Vector2(0, -32), text);

	private bool TryNavigateToPlayer(Player player, out int moveX, out bool jumpPressed)
	{
		moveX = 0;
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

		var edge = navPath[0];
		if (edge.Kind is PlatformNavGraph.EdgeKind.Jump or PlatformNavGraph.EdgeKind.Fall)
		{
			DebugNavStartNodeId = start.Value.Id;
			DebugNavGoalNodeId = goal.Value.Id;
			DebugNavTargetNodeId = edge.To;
			StartActiveEdge(edge);
			return TryExecuteActiveEdge(out moveX, out jumpPressed);
		}

		var from = graph.Nodes[edge.From];
		var to = graph.Nodes[edge.To];
		var target = to.Position;
		switch (edge.Kind)
		{
			case PlatformNavGraph.EdgeKind.Walk:
				moveX = MoveTowardNavTarget(target);
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
	}

	private int MoveTowardNavTarget(Vector2 target)
	{
		var dx = target.X - Position.X;
		if (MathF.Abs(dx) > NavNodeReachDistance * 0.5f)
			return Math.Sign(dx);
		return 0;
	}

	private bool TryExecuteActiveEdge(out int moveX, out bool jumpPressed)
	{
		moveX = 0;
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
		var target = to.Position;

		if (activeEdgeTimer > ActiveEdgeTimeout)
		{
			ClearActiveEdge($"{edge.Kind} timeout");
			return false;
		}

		switch (edge.Kind)
		{
			case PlatformNavGraph.EdgeKind.Jump:
				if (!activeJumpStarted && Vector2.Distance(Position, from.Position) > NavNodeReachDistance * 2)
				{
					moveX = Math.Sign(from.Position.X - Position.X);
					DebugNavLabel = $"{edge.Kind} approach {edge.From}->{edge.To}";
					break;
				}

				moveX = Math.Sign(target.X - Position.X);
				if (moveX == 0)
					moveX = Math.Sign(target.X - from.Position.X);
				if (!activeJumpStarted && grounded)
				{
					jumpPressed = true;
					activeJumpStarted = true;
					activeJumpHoldTimer = JumpInputHoldTime;
				}
				if (activeJumpStarted)
					activeJumpHoldTimer = Calc.Approach(activeJumpHoldTimer, 0, Time.Delta);
				if (activeJumpStarted && grounded && activeEdgeTimer > 0.12f && IsNearNode(target))
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

	private bool IsNearNode(Vector2 node)
		=> MathF.Abs(Position.X - node.X) <= NavNodeReachDistance &&
		   MathF.Abs(Position.Y - node.Y) <= NavNodeReachDistance;

	private void ClearActiveEdge(string label)
	{
		activeEdge = null;
		activeEdgeTimer = 0;
		activeJumpHoldTimer = 0;
		activeJumpStarted = false;
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
		DebugNavLabel = label;
	}

	private Vector2 DesiredFollowPosition(Player player)
	{
		var side = Position.X <= player.Position.X ? -1 : 1;
		return player.Position + new Vector2(side * DesiredPlayerSpacing, 0);
	}

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

	private int GetFallbackMoveX(Vector2? target)
	{
		if (target == null)
			return 0;

		var dx = target.Value.X - Position.X;
		return MathF.Abs(dx) > FollowStopDistance ? Math.Sign(dx) : 0;
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
		Velocity = Vector2.Zero;
		CollidesWithSolids = true;
		Visible = true;
		Game.TryCompanionStorylet("companion_entered");
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

public partial class Player : Pawn
{
	public override PawnInput GetInput()
	{
		return new(
			MoveX: Controls.Move.IntValue.X,
			MoveY: Controls.Move.IntValue.Y,
			JumpPressed: Controls.Jump.Pressed,
			JumpDown: Controls.Jump.Down,
			DashPressed: Controls.Dash.Pressed
		);
	}

	public const int MaxHealth = 4;

	static float AttackFriction => 150;
	static float HurtFriction => 200;
	static float DuckAttackCooldown => 0.4f;
	static float AttackBlockOverlapThreshold => 0.5f;
	static float HurtDuration => 0.5f;
	static float DeathDuration => 1.5f;
	static float InvincibleDuration => 1.5f;

	public int Health = MaxHealth;
	public Controls Controls => Game.Controls;

	private int footstepFrame = -1;
	private float duckAttackCooldownTimer;
	private bool duckAttack;
	private bool attackBlocked;
	private Light? light;
	private SoundInstance? rollSound;
	private bool CanPlaySquishSound = false;

	public Player()
	{
		Sprite = Assets.GetSprite("player");
		Hitbox = new(StandingHitboxRect);
		Mask = Masks.Player;
		IFrameTime = InvincibleDuration;
		grounded = true;
		Play("sword");
		State = States.Start;
	}

	public override void Added()
	{
		light = Game.Create<Light>(Position, it =>
		{
			it.Radius = 36;
			it.Intensity = 0.45f;
			it.LightColor = 0xffcfa0;
			it.Height = 16;
			it.FlickerFrequency = 1.5f;
			it.FlickerIntensity = 0.02f;
			it.DrawSprite = false;
			it.KeepOnRoomTransition = true;
		});
		UpdateLightPosition();
	}

	public override void OnCollideY()
	{
		bool wasGoingUp = Velocity.Y < 0;
		base.OnCollideY();
		if (wasGoingUp && CanPlaySquishSound && State == States.Normal)
		{
			AudioEvent("player/squish");
			CanPlaySquishSound = false;
			JumpUpSquish = 0.15f;
		}
	}

	protected override void AfterActorUpdate(Vector2 previousVelocity)
	{
		UpdateLightPosition();
	}

	protected override void OnLanded(Vector2 previousVelocity)
	{
		base.OnLanded(previousVelocity);
		AudioEvent("land", "Type", Game.GetSurfaceTypeBelow(this, searchOutward: true));
		CanPlaySquishSound = true;
	}

	protected override void AfterStateUpdate(Vector2 previousVelocity)
	{
		UpdateFootsteps();
		if (duckAttackCooldownTimer > 0)
			duckAttackCooldownTimer = Calc.Approach(duckAttackCooldownTimer, 0, Time.Delta);
	}

	protected override void AfterMovementUpdate(Vector2 previousVelocity)
	{
		if (Health > 0)
		{
			if (Position.X > Game.Bounds.Right && !Game.Transition(Point2.Right))
			{
				Position.X = Game.Bounds.Right;
			}
			else if (Position.X < Game.Bounds.Left && !Game.Transition(Point2.Left))
			{
				Position.X = Game.Bounds.Left;
			}
			else if (Position.Y > Game.Bounds.Bottom + 12 && !Game.Transition(Point2.Down))
			{
				ApplyHurt(-1000);
			}
			else if (Position.Y < Game.Bounds.Top)
			{
				if (Game.Transition(Point2.Up))
					Velocity.Y = -150;
				else
					Position.Y = Game.Bounds.Top;
			}
		}

		if (State == States.Dash)
		{
			if (OverlapsFirst(Masks.Hazard) is { } hit)
				hit.Hit(this);
		}
		else if (OverlapsFirst(Masks.Enemy | Masks.Hazard) is { } hit)
			hit.Hit(this);
	}

	protected override void CustomState()
	{
		switch (State)
		{
			case States.Attack:
				AttackState();
				break;
			case States.BlinkStrike:
				BlinkStrikeState();
				break;
			case States.Hurt:
				HurtState();
				break;
			case States.Start:
				StartState();
				break;
		}
	}

	protected override bool AllowDuckingInState()
		=> State == States.Attack && duckAttack;

	protected override bool TryStartPawnAction(int moveX)
	{
		if (Controls.BlinkStrike.ConsumePress())
		{
			blinkStrikeTarget = FindBlinkStrikeTarget();
			if (blinkStrikeTarget != null)
			{
				duckAttack = false;
				attackBlocked = false;
				blinkStrikeResolved = false;
				State = States.BlinkStrike;
				Play(GetAttackAnimation(), false);
				return true;
			}
		}
		else if (Controls.Attack.ConsumePress())
		{
			if (!grounded && moveX != 0)
				Facing = moveX;

			if (ducking && duckAttackCooldownTimer <= 0)
			{
				duckAttack = true;
				attackBlocked = IsAttackBlocked(duckAttack);
				duckAttackCooldownTimer = DuckAttackCooldown;
				State = States.Attack;
				if (grounded)
					StopX();
				return true;
			}
			else if (!ducking)
			{
				duckAttack = false;
				attackBlocked = false;
				State = States.Attack;
				if (grounded)
					StopX();
				return true;
			}
		}
		else if (Controls.Saw.ConsumePress())
		{
			Game.Create<Saw>(Position - new Point2(-5 * Facing, 10), saw => saw.Facing = Facing);
			return true;
		}

		if (Controls.Die.ConsumePress())
		{
			ApplyHurt(-100);
			return true;
		}

		return false;
	}

	protected override void OnPawnJump()
		=> AudioEvent("jump");

	protected override void OnRollStarted()
		=> StartRollSound();

	protected override void OnRollStopped(bool immediate)
		=> StopRollSound(immediate);

	protected override void UpdateRollAudioPosition()
		=> Game.SetAudioEventPosition(rollSound, Position, Game.GetFirst(Masks.Player)?.Position);

	private void StartRollSound()
	{
		StopRollSound(true);

		try
		{
			rollSound = Game.CreateAudioEvent("player/roll", Position);
			rollSound?.Start();
		}
		catch (Exception e)
		{
			Console.WriteLine(e);
			rollSound?.Release();
			rollSound = null;
		}
	}

	private void StopRollSound(bool immediate)
	{
		if (rollSound == null)
			return;

		rollSound.Stop(immediate);
		rollSound.Release();
		rollSound = null;
	}

	private void UpdateFootsteps()
	{
		if (State != States.Normal || !grounded || !IsPlaying("run") || !IsPlaying("run_start"))
		{
			footstepFrame = -1;
			return;
		}

		var frame = AnimationFrameIndex;
		if (frame < 0 || frame == footstepFrame)
			return;

		footstepFrame = frame;

		var animation = Animation;
		var localFrame = frame - animation.FrameStart;
		if (localFrame >= 0 && localFrame % 2 == 0)
		{
			AudioEvent("footstep", "Type", Game.GetSurfaceTypeBelow(this));
		}
	}

	public void AttackState()
	{
		if (duckAttack)
			ducking = true;

		Play(GetAttackAnimation(), false);

		RectInt? hitbox = null;

		if (attackBlocked)
		{
			if (state.PreviousTime <= 0)
			{
				AudioEvent("player/sword_block");
			}
		}
		else if (duckAttack)
		{
			if (state.PreviousTime <= 0)
				AudioEvent("sword_crouched");
			if (state.Time < Animation.Duration)
				hitbox = GetAttackHitbox(duckAttack);
		}
		else if (state.Time < 0.2f)
		{
			hitbox = new(-16, -12, 17, 8);
		}
		else if (state.Time < 0.50f)
		{
			if (state.PreviousTime < 0.2f)
			{
				attackBlocked = IsAttackBlocked(duckAttack);
				if (attackBlocked)
				{
					Play(GetAttackAnimation(), false);
					AudioEvent("player/sword_block");
				}
				else
				{
					AudioEvent("sword");
				}
			}
			if (!attackBlocked)
				hitbox = GetAttackHitbox(duckAttack);
		}

		if (hitbox is { } it)
		{
			if (Facing == Signs.Negative)
				it.X = -(it.X + it.Width);
			it += Position;

			Game.CollisionRectsThisFrame.Add(it);
			if (Game.OverlapsFirst(it, Masks.Enemy | Masks.Hazard) is { } hit)
				Hit(hit);

			Game.BreakTiles(it);
		}

		if (Grounded())
			Velocity.X = Calc.Approach(Velocity.X, 0, AttackFriction * Time.Delta);

		var attackFinished = attackBlocked
			? IsFinishedPlaying(GetAttackAnimation())
			: state.Time >= Animation.Duration;
		if (attackFinished)
		{
			attackBlocked = false;
			if (duckAttack)
			{
				duckAttack = false;
				Play("duck");
			}
			else
			{
				Play("idle");
			}
			State = States.Normal;
		}
	}

	private bool IsAttackBlocked(bool duckingAttack)
	{
		var hitbox = GetAttackHitbox(duckingAttack);
		if (Facing == Signs.Negative)
			hitbox.X = -(hitbox.X + hitbox.Width);
		hitbox += Position;

		return Game.GetRoomCollisionOverlapRatio(hitbox, includeBreakableTiles: false) >= AttackBlockOverlapThreshold;
	}

	private RectInt GetAttackHitbox(bool duckingAttack)
		=> duckingAttack
			? new RectInt(3, -6, 13, 6)
			: new RectInt(8, -9, 16, 9);

	private string GetAttackAnimation()
	{
		if (duckAttack)
		{
			return attackBlocked ? "duck_attack_block" : "duck_attack";
		}

		return attackBlocked ? "attack_block" : "attack";
	}

	public void HurtState()
	{
		if (state.Time <= 0 && Health <= 0)
		{
			foreach (var actor in Game.Actors)
				if (actor != this)
					Game.Destroy(actor);
			Game.Shake(0.1f);
		}

		Velocity.X = Calc.Approach(Velocity.X, 0, HurtFriction * Time.Delta);

		if (state.Time >= HurtDuration && Health > 0)
			State = States.Normal;

		if (state.Time >= DeathDuration && Health <= 0)
		{
			Game.ReloadRoom(restartMusic: true);
		}
	}

	public void StartState()
	{
		if (state.Time >= 1.0f)
			State = States.Normal;
	}

	public override void OnWasHit(Actor by)
	{
		ApplyHurt(fromActor: by);
	}

	private void ApplyHurt(int healthDelta = -1, Actor? fromActor = null)
	{
		Game.HitStun(0.1f);
		Game.Shake(0.1f);
		Game.PulseDitherGlitch(HurtGlitchAxis(fromActor));

		Play("hurt");

		// Apply a hurt velocity
		int signDirection = -Facing;
		if (fromActor != null)
		{
			// Get pushed away from the actor that hurt us.
			var actorDelta = Math.Sign(Position.X - fromActor.Position.X);
			if (actorDelta != 0)
				signDirection = actorDelta;
		}
		Velocity = new Vector2(signDirection * 100, -80);
		State = States.Hurt;

		var wasHealth = Health;
		Health += healthDelta;

		if (Health <= 0 && wasHealth > 0)
		{
			Game.StopMusic();
			AudioEvent("death");
		}
		else
		{
			if (healthDelta < 0 && wasHealth > 0)
				Game.TriggerPlayerHitSnapshot();
			AudioEvent("hurt");
		}
	}

	private Vector2 HurtGlitchAxis(Actor? fromActor)
	{
		if (fromActor != null)
		{
			var axis = Position - fromActor.Position;
			if (axis.LengthSquared() > 0.001f)
				return Vector2.Normalize(axis);
		}

		if (Velocity.LengthSquared() > 0.001f)
			return Vector2.Normalize(Velocity);

		return new Vector2(-Facing, 0);
	}

	public override void Render(Batcher batcher)
	{
		base.Render(batcher);
		RenderBlink(batcher);
	}

	private void UpdateLightPosition()
	{
		if (light != null)
			light.Position = Position + new Point2(0, -6);
	}

	public override void Destroyed()
	{
		StopRollSound(true);

		if (light != null)
		{
			Game.Destroy(light);
			light = null;
		}
	}
}
