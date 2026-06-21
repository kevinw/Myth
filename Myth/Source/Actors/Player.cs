using System.Numerics;
using Foster.Framework;

namespace Myth;

public readonly record struct PawnInput(
	int MoveX,
	int MoveY,
	bool JumpPressed,
	bool JumpDown,
	bool DashPressed,
	bool AllowWallJump = true);

// A marker for where the player should start in a room, if they are first spawned there.
public class PlayerStart : Actor
{
	public PlayerStart()
	{
		Sprite = Assets.GetSprite("player_start");
		CollidesWithSolids = false;
	}

	public override void Render(Batcher batcher)
	{
	}

	public override void RenderShadow(Batcher batcher, Color color) { }

	public override void RenderReflection(Batcher batcher, Color color) { }

	public override void RenderEmissive(Batcher batcher)
	{
		if (!Game.Manager.playing)
			base.Render(batcher);
	}
}

public abstract class Pawn : Actor
{
	protected bool PlayerSpecificAudioEvents = false;

	public enum States
	{
		Normal,
		Roll,
		Dash,
		Attack,
		BlinkStrike,
		Hurt,
		Burrow
	}
	protected static float FastFallMultiplier => 1.25f;
	protected static float JumpBufferTime => 0.08f;

	protected static float MaxGroundSpeed => 93;
	protected static float MaxAirSpeed => 100;
	protected static float CrawlSpeed => 28;
	protected static float GroundAccel => 400;
	protected static float AirAccel => 310;
	protected static float Friction => 480;
	protected static float Gravity => 450;

	protected static float WallHugGravityMultiplier => 0.25f;
	protected static float JumpForce => -125;
	protected static float JumpTime => 0.19f;
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
	// While burrowing the player is effectively a point that rides the surface, so the
	// follower probes with a tiny 2x2 footprint: the burrow center sits on the surface
	// (not a hitbox-width away) and never snags on a nearby ceiling/wall.
	protected static RectInt BurrowHitboxRect => new(-1, -1, 2, 2);

	protected readonly StateMachine<States> state = new(States.Normal);
	protected float jumpTimer;
	protected float coyoteJumpTimer;
	protected int coyoteJumpY;
	protected float jumpBufferTimer;
	protected bool grounded;
	// GetWallJumpDirection runs two solid-overlap probes and is called up to 3x per
	// Update (gravity wall-hug, NormalState wall-slide, jump handling). Position is
	// fixed for the whole Update after Move() runs, and each pawn only queries it from
	// a single phase (Player after grounded is set, Boss during GetInput), so the
	// result is invariant within a frame. Cache it, invalidated once per Update.
	int wallJumpDirectionCache;
	bool wallJumpDirectionCached;
	protected bool ducking;
	protected float wallJumpInputLockTimer;
	protected int wallJumpDirection;
	protected int dashDirection;
	protected float dashCooldownTimer;
	protected float dashRemainder;
	protected float jumpForce = JumpForce;
	protected bool jumpCutImmune; // suppresses the short-hop cut (e.g. for the burrow pop)
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
	public bool IsDucking => ducking;
	public bool IsWallJumpLocked => wallJumpInputLockTimer > 0;

	public abstract PawnInput GetInput();

	public override void Update()
	{
		var previousVelocity = Velocity;
		base.Update();
		wallJumpDirectionCached = false;
		input = GetInput();
		if (input.JumpPressed)
			jumpBufferTimer = JumpBufferTime;
		AfterActorUpdate(previousVelocity);

		// While burrowing the player rides the surface as a point; skip the normal
		// grounded/landing machinery so it doesn't re-trigger landings mid-burrow.
		var nowGrounded = State != States.Burrow && Velocity.Y >= 0 && Grounded();
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
		if (jumpBufferTimer > 0)
			jumpBufferTimer = Calc.Approach(jumpBufferTimer, 0, Time.Delta);

		wallSliding = false;
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
		if (State == States.Burrow)
			Hitbox = new(BurrowHitboxRect);
		else if (ducking || State == States.Roll)
			Hitbox = new(DuckingHitboxRect);
		else
			Hitbox = new(StandingHitboxRect);

		if (jumpTimer > 0)
		{
			if (!input.JumpDown && !jumpCutImmune)
			{
				// releasing jump button before jump timer expires (cutting jump short) -- we want to reduce your velocity so you fall faster.
				//
				jumpTimer = 0;
				Velocity.Y *= 0.5f;
			}
			else
				jumpTimer -= Time.Delta;
		}

		if (!grounded && State != States.Dash && State != States.Burrow)
		{
			var grav = Gravity;
			if (jumpTimer > 0)
				grav *= 0.35f;
			else if (Velocity.Y > 0)
				grav *= FastFallMultiplier;
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
	protected virtual bool TryRollCancel() => false;
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

	bool wallSliding = false;

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
		string? jumpAnim = null;
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
				if (PlayerSpecificAudioEvents)
					AudioEventDebounced("player/skid", 0.5f);
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
				wallSliding = true;
			}
			else if (JumpUpSquish > 0 && !grounded)
			{
				jumpAnim = "jump_squish";
			}
			else
			{
				if (Velocity.X != 0 && moveX != 0 && Math.Sign(Velocity.X) != Math.Sign(moveX))
					jumpAnim = "jump_turn";
				else
					jumpAnim = "jump";
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
			{
				RunStartSkid = true;
			}
		}

		var wallJump = GetWallJumpDirection();
		var canCoyoteJump = CanCoyoteJump();
		var canGroundJump = grounded && CanStandUp() || canCoyoteJump;
		var canWallJump = input.AllowWallJump && wallJump != 0 && moveX != 0;
		var jumpPressed = input.JumpPressed;
		if (!jumpPressed && canGroundJump && jumpBufferTimer > 0)
			jumpPressed = true;
		if (jumpPressed && (canGroundJump || canWallJump))
		{
			jumpBufferTimer = 0;
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

		// Dive plays while holding jump and falling toward the ground (the burrow approach).
		// Use JumpDown (held), not jumpPressed — jumpPressed is only true the single frame
		// the button goes down, which is what made the dive flicker on for one frame then
		// revert to the jump anim. Falling is Velocity.Y > 0 here (gravity adds to Y).
		// Gate on CanBurrow() so the pose only shows when a burrow can actually result
		// (not during the cooldown or before we've re-grounded).
		bool jumpAnimLooping = true;
		if (!grounded && input.JumpDown && Velocity.Y > -30 && CanBurrow() && !wallSliding)
		{
			jumpAnim = "dive";
			jumpAnimLooping = false;
		}

		if (jumpAnim != null)
		{
			Play(jumpAnim, looping: jumpAnimLooping);
		}

		if (input.DashPressed && TryStartDash(moveX))
			return;

		TryStartPawnAction(moveX);
	}

	protected float burrowTimer = 0;
	protected bool burrowNeedsGrounding; // after a pop, must touch ground again before it can re-arm
	protected bool CanBurrow()
	{
		if (wallSliding) return false;
		if (burrowNeedsGrounding) return false;
		if (burrowTimer < 0) return false; // the ability is still cooling down
		if (State != States.Normal) return false;
		return true;
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

		if (TryRollCancel())
			return;

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
		jumpCutImmune = false; // normal jumps can be cut short by releasing
		Velocity.Y = force;
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

	protected int GetWallJumpDirection()
	{
		if (wallJumpDirectionCached)
			return wallJumpDirectionCache;

		wallJumpDirectionCached = true;
		wallJumpDirectionCache = ComputeWallJumpDirection();
		return wallJumpDirectionCache;
	}

	int ComputeWallJumpDirection()
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
		State = States.Normal;
		PlayerSpecificAudioEvents = true;
	}

	public override void Added()
	{
		light = Game.Create<Light>(Position, it =>
		{
			it.Radius = 37.5f;
			it.Intensity = 0.45f;
			it.LightColor = 0xffcfa0;
			it.Height = 16;
			it.FlickerFrequency = 1.5f;
			it.FlickerIntensity = 0.02f;
			it.DrawSprite = false;
			it.KeepOnRoomTransition = true;
		});
		UpdateLightPosition();

		if (Game.Particles is { Supported: true } particles)
			burrowSparkEffect = particles.AcquireEffect("sparks");
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
		if (input.JumpDown && !burrowNeedsGrounding)
		{
			TryStartBurrow(previousVelocity);
			AudioEvent("land", "Type", Game.GetSurfaceTypeBelow(this, searchOutward: true));
			return;
		}
		// A horizontal wall-pop lands grounded immediately; keep the pop squish
		// rather than stamping the flat landing squish over it.
		if (burrowExitTimer > 0)
		{
			AudioEvent("land", "Type", Game.GetSurfaceTypeBelow(this, searchOutward: true));
			CanPlaySquishSound = true;
			return;
		}
		base.OnLanded(previousVelocity);
		AudioEvent("land", "Type", Game.GetSurfaceTypeBelow(this, searchOutward: true));
		CanPlaySquishSound = true;
	}

	protected override void AfterStateUpdate(Vector2 previousVelocity)
	{
		if (burrowNeedsGrounding && grounded)
			burrowNeedsGrounding = false;
		UpdateAura(Time.Delta);
		UpdateFootsteps();
		if (duckAttackCooldownTimer > 0)
			duckAttackCooldownTimer = Calc.Approach(duckAttackCooldownTimer, 0, Time.Delta);
		if (burrowExitTimer > 0)
			burrowExitTimer = Calc.Approach(burrowExitTimer, 0, Time.Delta);
		if (burrowTimer < 0)
		{
			burrowTimer += Time.Delta;
			if (burrowTimer > 0) burrowTimer = 0;
		}
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

		if (State == States.Burrow)
		{
			// invulnerable while underground
		}
		else if (State == States.Dash)
		{
			if (OverlapsFirst(Masks.Hazard) is { } hit)
				hit.Hit(this);
		}
		else if (OverlapsFirst(Masks.Enemy | Masks.Hazard) is { } hit)
			hit.Hit(this);

		// Safety net: if the hitbox ended up embedded in solid (e.g. popping out of a
		// burrow inside a slope/wall), nudge to the nearest free spot. The follower
		// keeps us off surfaces during burrow, so skip it there.
		if (State != States.Burrow)
			TryUnstick(burrowExitTimer > 0 ? burrowExitOutward : new Vector2(0, -1));

		// TODO: invert this maybe, it probably belongs in FungalGrowthActor with some
		// centralized / efficient "sensor" logic
		var footPosition = new Vector2(Position.X, Position.Y);
		foreach (var actor in Game.Actors)
			if (actor is FungalGrowthActor fungus && fungus.GetVisualBounds().Contains(footPosition))
				fungus.OnPlayerStep(footPosition);

		UpdateBurrowAlways();
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
			case States.Burrow:
				BurrowState();
				break;
		}

	}

	const int UnstickMaxRadius = 16;

	// If the current hitbox is embedded in solid, snap to the nearest free offset,
	// biased toward `preferredDir`. Cheap no-op (one overlap test) when not embedded.
	void TryUnstick(Vector2 preferredDir)
	{
		if (!OverlapsAny(Masks.Solid))
			return;

		if (preferredDir.LengthSquared() > 0.0001f)
			preferredDir = Vector2.Normalize(preferredDir);

		for (var r = 1; r <= UnstickMaxRadius; r++)
		{
			var bestScore = float.MaxValue;
			var best = Point2.Zero;
			var found = false;
			for (var oy = -r; oy <= r; oy++)
				for (var ox = -r; ox <= r; ox++)
				{
					if (Math.Max(Math.Abs(ox), Math.Abs(oy)) != r)
						continue; // only the current ring
					var off = new Point2(ox, oy);
					if (OverlapsAny(off, Masks.Solid))
						continue; // not free here

					var dist = MathF.Sqrt(ox * ox + oy * oy);
					var align = Vector2.Dot(Vector2.Normalize(new Vector2(ox, oy)), preferredDir);
					var score = dist - align; // nearer and better-aligned wins
					if (score < bestScore)
					{
						bestScore = score;
						best = off;
						found = true;
					}
				}

			if (found)
			{
				Position += best;
				return;
			}
		}
	}

	protected override bool AllowDuckingInState() => State == States.Attack && duckAttack;

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
			var existingSaw = Game.GetFirst<Saw>();
			if (existingSaw != null)
			{
				Position = existingSaw.Position;
				Game.Destroy(existingSaw);
				Game.HitStun(0.1f);
			}
			else
			{
				Game.Create<Saw>(Position - new Point2(-5 * Facing, 10), saw => saw.Facing = Facing);
			}
			return true;
		}

		if (Controls.Die.ConsumePress())
		{
			ApplyHurt(-100);
			return true;
		}

		return false;
	}

	protected override void OnPawnJump() => AudioEvent("jump");

	protected override bool TryRollCancel()
	{
		if (Controls.Attack.ConsumePress() && duckAttackCooldownTimer <= 0)
		{
			duckAttack = true;
			attackBlocked = IsAttackBlocked(duckAttack);
			duckAttackCooldownTimer = DuckAttackCooldown;
			OnRollStopped(false);
			Squish = new(0.65f, 1.4f);
			State = States.Attack;
			Velocity.X *= 0.5f;
			return true;
		}
		return false;
	}

	protected override void OnRollStarted() => StartRollSound();

	protected override void OnRollStopped(bool immediate) => StopRollSound(immediate);

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

	private void StopRollSound(bool immediate) => StopAndReleaseSound(ref rollSound, immediate);

	private void UpdateFootsteps()
	{
		if (State != States.Normal || !grounded || !IsPlaying("run") && !IsPlaying("run_start"))
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
		if (localFrame >= 0 && localFrame % 6 == 0)
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

			foreach (var actor in Game.Actors.ToArray())
			{
				if (actor is NeuralCellularActor nca && nca.GetVisualBounds().Overlaps(new Rect(it.X, it.Y, it.Width, it.Height)))
				{
					nca.Cut(it);
					nca.Hit(new Vector2(it.X + it.Width / 2.0f, it.Y + it.Height / 2.0f));
				}
				else if (actor is FungalGrowthActor fungus && fungus.GetVisualBounds().Overlaps(new Rect(it.X, it.Y, it.Width, it.Height)))
				{
					fungus.Cut(it);
					fungus.Hit(new Vector2(it.X + it.Width / 2.0f, it.Y + it.Height / 2.0f));
				}
			}
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
			return attackBlocked ? "duck_attack_block" : "duck_attack";

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
		if (healthDelta < 0 && Health > 0)
			Game.NotifyPlayerHurt(fromActor, Position);

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
		// While submerged the player is represented only by the surface glow; the
		// sprite stays visible briefly on entry to play the squash-in.
		if (!BurrowHidesSprite)
		{
			RenderAura(batcher); // rainbow merge aura when both ducking (behind the sprite)
			base.Render(batcher);
		}
		RenderBlink(batcher);
	}

	public override void RenderNormals(Batcher batcher)
	{
		if (!BurrowHidesSprite)
			base.RenderNormals(batcher);
	}

	public override void RenderShadow(Batcher batcher, Color color)
	{
		if (!BurrowHidesSprite)
			base.RenderShadow(batcher, color);
	}

	public override void RenderReflection(Batcher batcher, Color color)
	{
		if (!BurrowHidesSprite)
			base.RenderReflection(batcher, color);
	}

	public override void RenderEmissive(Batcher batcher)
	{
		base.RenderEmissive(batcher);
		RenderBurrowLine(batcher);
	}

	void UpdateLightPosition()
	{
		light?.Position = Position + new Point2(0, -6);
	}

	public override void Destroyed()
	{
		StopRollSound(true);
		if (light != null)
		{
			Game.Destroy(light);
			light = null;
		}
		ReleaseBurrowParticles();
	}
}
