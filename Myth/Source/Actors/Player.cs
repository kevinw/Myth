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



public abstract class Pawn : Actor
{
	int jumpParticlesEffect = -1;
	int splashParticlesEffect = -1;

	override public void Added()
	{
		base.Added();

		if (Game.Particles is { Supported: true } particles)
		{
			jumpParticlesEffect = particles.AcquireEffect("jump");
			splashParticlesEffect = particles.AcquireEffect("splash");
		}
	}

	protected bool PlayerSpecificAudioEvents = false;

	public enum States
	{
		Normal,
		Roll,
		Attack,
		BlinkStrike,
		Hurt,
		Burrow
	}
	public static float FastFallMultiplier => 1.25f;
	protected static float JumpBufferTime => 0.08f;

	public static float MaxGroundSpeed => 93.T();
	public static float MaxAirSpeed => 100.T();
	protected static float CrawlSpeed => 28.T();
	protected static float GroundAccel => 400.T();
	public static float AirAccel => 310.T();
	public static float Friction => 480.T();
	public static float Gravity => 450.T();
	public static float TerminalVelocity => 500.T();

	// Water: while the torso is submerged, gravity is scaled down and downward speed is
	// capped at MaxSinkSpeed (damped so a fast plunge eases into the sink instead of
	// snapping). Jump becomes a repeatable upward stroke; burrowing is disabled.
	public static float WaterGravityMultiplier => 0.3f.T();
	public static float WaterMaxSinkSpeed => 40f.T();
	public static float WaterFastSinkMultiplier => 1.5f.T(); // holding down sinks 50% faster
	public static float WaterSinkDamping => 200f.T();
	public static float WaterSwimStroke => -70f.T();
	// The frame you break the surface, velocity is multiplied by this once to simulate
	// surface tension grabbing you as you plunge in (a sharper slowdown than the ongoing
	// water drag/sink cap that follow on later frames).
	public static float WaterSurfaceTensionMultiplier => 0.55f.T();
	// Horizontal water resistance: lower top speed, plus a drag that bleeds off speed when
	// no direction is held (even airborne, unlike the grounded-only dry friction).
	public static float WaterHorizontalSpeedMultiplier => 0.6f.T();
	public static float WaterHorizontalDrag => 300f.T();
	public static float WaterRollSpeedMultiplier => 0.55f.T();

	protected static float WallHugGravityMultiplier => 0.25f.T();
	protected static float WallSlideMaxFallSpeed => 80f.T();  // capped, slower descent while wall sliding
	protected static float WallSlideFallDamping => 320f.T();  // eases a fast fall down to the slide cap
	public static float JumpForce => -125.T();
	public static float JumpTime => 0.19f.T();
	// Gravity is scaled by this while the jump button is held (floatier ascent), and
	// vertical velocity is multiplied by JumpCutMultiplier when the jump is released early.
	// Centralized here so the jump-arc sweep (JumpPhysics) reads the same numbers as Update.
	public const float JumpHoldGravityMultiplier = 0.35f;
	public const float JumpCutMultiplier = 0.5f;
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
	protected bool inWater;
	bool wasInWater; // previous frame's inWater, to catch the surface-entry transition
	protected bool atWaterSurface; // torso submerged but head is above the waterline
	float airBubbleTimer;
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
	protected bool dashNeedsGrounding;
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
	public bool InWater => inWater;
	public bool AtWaterSurface => atWaterSurface;
	public bool IsDucking => ducking;
	public bool IsWallJumpLocked => wallJumpInputLockTimer > 0;

	public abstract PawnInput GetInput();

	public override void Update()
	{
		if (Game.Rewinding) return;

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

		var waterBounds = Hitbox.Rect + Position;
		waterBounds = waterBounds.Inflate(8);
		inWater = Game.IsWaterAt(waterBounds.Center);
		atWaterSurface = inWater && !Game.IsWaterAt(new Point2(waterBounds.Center.X, waterBounds.Top));
		// Crossing the surface (either direction) kicks it into a splash + droplets scaled by
		// the crossing speed. On entry, surface tension also gives an extra one-time slowdown.
		if (inWater && !wasInWater)
		{
			OnCrossedWaterSurface(Velocity.Y, exiting: false); // downward plunge -> dip
			Velocity *= WaterSurfaceTensionMultiplier;
			AudioEventDebounced("player/splash", 0.2f);
		}
		else if (!inWater && wasInWater)
		{
			OnCrossedWaterSurface(-Velocity.Y, exiting: true); // upward exit -> crown
			AudioEventDebounced("player/splash", 0.2f);
		}
		wasInWater = inWater;
		UpdateAirBubbles();

		wallSliding = false;
		switch (State)
		{
			case States.Normal:
				NormalState();
				break;
			case States.Roll:
				RollState();
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
				Velocity.Y *= JumpCutMultiplier;
			}
			else
				jumpTimer -= Time.Delta;
		}

		if (!grounded && State != States.Burrow)
		{
			var grav = Gravity;
			if (inWater)
				grav *= WaterGravityMultiplier;
			else if (jumpTimer > 0)
				grav *= JumpHoldGravityMultiplier;
			else if (Velocity.Y > 0)
				grav *= FastFallMultiplier;
			if (!inWater && State == States.Normal && Velocity.Y > 0 && IsWallHugging())
				grav *= WallHugGravityMultiplier;
			Velocity.Y += grav * Time.Delta;
			if (Velocity.Y > TerminalVelocity) Velocity.Y = TerminalVelocity;
			var maxSink = input.MoveY > 0 ? WaterMaxSinkSpeed * WaterFastSinkMultiplier : WaterMaxSinkSpeed;
			if (inWater && Velocity.Y > maxSink)
				Velocity.Y = Calc.Approach(Velocity.Y, maxSink, WaterSinkDamping * Time.Delta);
			if (!inWater && State == States.Normal && Velocity.Y > WallSlideMaxFallSpeed && IsWallHugging())
				Velocity.Y = Calc.Approach(Velocity.Y, WallSlideMaxFallSpeed, WallSlideFallDamping * Time.Delta);

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
	protected virtual void OnRollStopped(bool immediate)
	{
	}
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
	double lastSkidParticlesTime;

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

				var now = Time.Seconds;
				if (now - lastSkidParticlesTime > 0.5f)
				{
					EmitJumpParticles(Position + new Vector2(facing * 8.0f, 0));
					lastSkidParticlesTime = now;
				}
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
			if (!inWater && IsWallHugging(wallJumpDir))
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
		float maxSpeed;
		if (grounded)
		{
			maxSpeed = MaxGroundSpeed * GroundSpeedMultiplier;
		}
		else
		{
			maxSpeed = MaxAirSpeed * AirSpeedMultiplier;
		}
		if (ducking)
		{
			maxSpeed = CrawlSpeed * GroundSpeedMultiplier;
		}
		if (inWater)
		{
			maxSpeed *= WaterHorizontalSpeedMultiplier;
		}



		var lastFacing = Facing;

		Velocity.X += movementInput * (grounded ? GroundAccel * GroundSpeedMultiplier : AirAccel * AirSpeedMultiplier) * Time.Delta;
		if (dashCooldownTimer > 0)
			Velocity.X = dashDirection * DashSpeed;
		if (!grounded && wallJumpInputLockTimer > 0)
			maxSpeed = MathF.Max(maxSpeed, WallJumpSpeed * speedMultiplier);
		if (!grounded && rollJumpCarryTimer > 0)
			maxSpeed = MathF.Max(maxSpeed, RollJumpSpeed * AirSpeedMultiplier);
		if (MathF.Abs(Velocity.X) > maxSpeed)
			Velocity.X = Calc.Approach(Velocity.X, MathF.Sign(Velocity.X) * maxSpeed, 2000 * Time.Delta);
		if (moveX == 0 && grounded && dashCooldownTimer == 0)
			Velocity.X = Calc.Approach(Velocity.X, 0, Friction * Time.Delta);
		else if (moveX == 0 && inWater)
			Velocity.X = Calc.Approach(Velocity.X, 0, WaterHorizontalDrag * Time.Delta);
		if (grounded && moveX != 0)
		{
			Facing = facing;
			if (lastFacing != Facing && didStartRunning)
			{
				RunStartSkid = true;
				EmitJumpParticles(Position);
			}
		}
		// Underwater you swim-turn: face the input direction even while airborne, unlike
		// the usual air control that keeps your facing through a mid-air direction change.
		else if (inWater && moveX != 0)
		{
			Facing = facing;
		}

		// Underwater, jump is a repeatable swim stroke: each press sets a capped upward
		// velocity (Min so chained strokes hold the ascent rather than accelerating), with
		// no ground/coyote requirement. At the surface (head above the waterline) it instead
		// becomes a full-strength jump so you can hop clean out of the water. Ground and wall
		// jumps are suppressed while submerged.
		if (inWater && input.JumpPressed)
		{
			jumpBufferTimer = 0;
			JumpUpSquish = 0;
			if (atWaterSurface)
			{
				Squish = new Vector2(0.65f, 1.4f);
				StartJump(JumpForce, JumpTime);
			}
			else
			{
				Squish = new Vector2(0.8f, 1.2f);
				Velocity.Y = MathF.Min(Velocity.Y, WaterSwimStroke);
			}
			OnPawnJump();
		}

		var wallJump = GetWallJumpDirection();
		var canCoyoteJump = CanCoyoteJump();
		var canGroundJump = grounded && CanStandUp();
		var wasGroundedJump = canGroundJump;
		canGroundJump |= canCoyoteJump;
		var canWallJump = input.AllowWallJump && wallJump != 0 && moveX != 0;
		var jumpPressed = input.JumpPressed;
		if (!jumpPressed && canGroundJump && jumpBufferTimer > 0)
			jumpPressed = true;
		if (!inWater && jumpPressed && (canGroundJump || canWallJump))
		{
			jumpBufferTimer = 0;
			JumpUpSquish = 0;
			Squish = new Vector2(0.65f, 1.4f);
			if (canGroundJump)
			{
				if (canCoyoteJump)
					Position.Y = coyoteJumpY;
				coyoteJumpTimer = 0;
				if (wasGroundedJump)
					EmitJumpParticles(Position);
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
		if (!grounded && input.JumpDown && Velocity.Y > 15 && CanBurrow() && !wallSliding)
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

	void EmitJumpParticles(Vector2 position)
	{
		if (Game.Particles is { Supported: true } particles)
			particles.Emit(jumpParticlesEffect,
				worldPosition: position,
				count: 8,
				directionRadians: MathF.PI / 2.0f,
				speedScale: 2.5f,
				spreadRadians: 59.0f * Calc.DegToRad);
	}

	protected float burrowTimer = 0;
	protected bool burrowNeedsGrounding; // after a pop, must touch ground again before it can re-arm
	protected bool CanBurrow()
	{
		if (inWater) return false;
		if (wallSliding) return false;
		if (burrowNeedsGrounding) return false;
		if (burrowTimer < 0) return false; // the ability is still cooling down
		if (State != States.Normal) return false;
		return true;
	}


	protected bool TryStartRoll(Vector2 impactVelocity)
	{
		if (State != States.Normal) return false;
		if (input.MoveY <= 0) return false; // must be holding "downwards" to roll

		bool onSlope = GetSlopeUnderneath(out var normal);
		if (onSlope)
		{
			// starting a roll on a slope is "easy" and automatically
			// sends you in the direction of the slope
			Facing = Math.Sign(normal.X);
			Vector2 newVelocity = new(Facing * RollSpeed * RollSpeedScale, 0);
			Velocity = Vector2.Lerp(Velocity, newVelocity, Math.Abs(normal.X * 1.7f));
		}
		else
		{
			// on flat ground, starting a roll requires some height and
			// some horizontal velocity
			if (impactVelocity.Y < RollMinFallSpeed) return false;
			if (MathF.Abs(impactVelocity.X) < RollMinHorizontalSpeed) return false;
			// Holding into the fall's opposite direction reads as braking/reversing, not tucking
			// into a roll: falling right + holding left (or falling left + holding right) skips it.
			if (input.MoveX != 0 && Math.Sign(input.MoveX) != Math.Sign(impactVelocity.X)) return false;
			Facing = Math.Sign(impactVelocity.X);
			Velocity = new(Facing * RollSpeed * RollSpeedScale, 0);
		}

		ducking = true;
		Squish = new(1.65f, 0.62f);
		Play("roll");
		State = States.Roll;
		OnRollStarted();
		return true;
	}

	float RollSpeedScale => inWater ? WaterRollSpeedMultiplier : 1f;

	(float, float) TargetRollSpeedAndFriction()
	{
		var rollingDownSlope = RollingDownSlope();
		var targetSpeed = (rollingDownSlope ? DownhillRollSpeed : MaxGroundSpeed) * RollSpeedScale;
		var friction = rollingDownSlope ? Calc.Clamp(DownhillRollFriction, 0, float.PositiveInfinity) : RollFriction;
		return (targetSpeed, friction);
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
		var (targetSpeed, friction) = TargetRollSpeedAndFriction();

		// NOTE: this Velocity set is suspect, because in Update() it gets set with a different maxSpeed anyways.
		// probably needs some kind of unification, or some other way for pieces of code to adjust a max speed,
		// which then gets set in one place.
		//
		// once we have that, it would be fun to gradually increase speed as you roll down many slopes without stopping
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

	bool TryStartDash(int input)
	{
		if (dashCooldownTimer > 0 || dashNeedsGrounding)
			return false;

		dashDirection = input != 0 ? Math.Sign(input) : Facing;
		if (dashDirection == 0)
			dashDirection = Signs.Positive;

		Facing = dashDirection;
		dashRemainder = 0;
		dashCooldownTimer = DashCooldown;
		dashNeedsGrounding = true;
		jumpTimer = 0;
		Velocity.X = dashDirection * 200f;
		Velocity.Y *= 0.1f;
		ducking = false;
		Squish = new Vector2(1.35f, 0.82f);
		Play("jump");
		return true;
	}

	bool GetSlopeUnderneath(out Vector2 normal)
	{
		normal = Vector2.Zero;
		var bounds = Hitbox.Rect + Position;
		if (!Game.TryFindSurface(this, bounds, Vector2.UnitY, 2, out var hit) || !hit.Slope.HasValue)
			return false;

		normal = hit.Normal;
		return true;
	}

	protected bool RollingDownSlope()
	{
		if (!grounded || Velocity.LengthSquared() <= 0.001f)
			return false;

		if (!GetSlopeUnderneath(out var normal))
			return false;

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

	bool CanCoyoteJump()
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

	bool IsWallHugging()
		=> IsWallHugging(GetWallJumpDirection());

	bool IsWallHugging(int wallJumpDirection)
		=> wallJumpDirection != 0 && input.MoveX == -wallJumpDirection;

	// Splash droplet burst tuning (the surface ripple itself is scaled in WaterSurface).
	const float SplashDropletSpeedRef = 60f; // plunge speed that maps to ~1x droplet speed

	// Fired the frame the torso crosses the waterline (entering or leaving). crossingSpeed is
	// the speed into the surface (downward on entry, upward on exit), so a faster crossing
	// splashes harder; a slow drift over the line barely disturbs it. On exit the wave impulse
	// flips to push the surface up into a crown instead of dipping it.
	void OnCrossedWaterSurface(float crossingSpeed, bool exiting)
	{
		// The nav-graph build steps throwaway pawns through Pawn.Update; don't splash the
		// real room during that simulation.
		if (Game.Simulating)
			return;

		var strength = MathF.Max(0f, crossingSpeed);
		Game.SplashWater(Position.X, exiting ? -strength : strength);

		if (splashParticlesEffect >= 0 && Game.Particles is { Supported: true } particles)
		{
			// Emit at the waterline (segment surface Y) rather than the submerged torso.
			var surfaceY = Game.WaterSurfaceAtX(Position.X)?.SurfaceY ?? (Hitbox.Rect + Position).Top;
			var count = (int)Calc.Clamp(6f + strength * 0.25f, 6f, 28f);
			particles.Emit(splashParticlesEffect,
				worldPosition: new Vector2(Position.X, surfaceY),
				count: count,
				directionRadians: -MathF.PI / 2f, // up
				speedScale: Calc.Clamp(strength / SplashDropletSpeedRef, 0.6f, 2.2f),
				spreadRadians: 70f * Calc.DegToRad);
		}
	}

	static float AirBubbleIntervalMin => 0.5f;
	static float AirBubbleIntervalMax => 1.8f;

	void UpdateAirBubbles()
	{
		// The nav-graph build steps throwaway pawns through Pawn.Update; don't spawn real
		// bubbles into the room during that simulation.
		if (!inWater || Game.Simulating)
			return;

		airBubbleTimer -= Time.Delta;
		if (airBubbleTimer > 0)
			return;
		airBubbleTimer = AirBubbleIntervalMin + Random.Shared.NextSingle() * (AirBubbleIntervalMax - AirBubbleIntervalMin);

		var bounds = Hitbox.Rect + Position;
		var spawn = new Point2(bounds.Center.X + Random.Shared.Next(-2, 3), bounds.Top + 2);
		if (Game.IsWaterAt(spawn))
			Game.Create<AirBubble>(spawn);
	}
}

[GameActor(HideFromPalette = true)]
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
	static Vector2 HatSpring => new(130f, 100f);
	static Vector2 HatDamping => new(32f, 12f);
	static float HatMotionResponse => 0.22f;
	static float HatMaxOffset => 3f;
	static float HatAngularSpring => 80f;
	static float HatAngularDamping => 12f;
	static float HatAngularMotionResponse => 0.035f;
	static float HatMaxAngle => 30f * Calc.DegToRad;
	static float HatMaxVelocityDelta => 250f;

	public int Health = MaxHealth;
	public Controls Controls => Game.Controls;

	int footstepFrame = -1;
	float duckAttackCooldownTimer;
	bool duckAttack;
	bool attackBlocked;
	int plasmaStrikeEffect = -1;
	Light? light;
	SoundInstance? rollSound;
	bool CanPlaySquishSound = false;
	readonly Sprite? hatSprite;
	Vector2 hatOffset;
	Vector2 hatOffsetVelocity;
	float hatAngle;
	float hatAngularVelocity;

	public Player()
	{
		Sprite = Assets.GetSprite("player");
		hatSprite = Assets.GetSprite("hat");
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
		base.Added();

		light = Game.Create<Light>(Position, it =>
		{
			it.IsStatic = false;
			it.Radius = 37.5f;
			it.Intensity = BaseLightIntensity;
			it.LightColor = BaseLightColor;
			it.Height = 16;
			it.FlickerFrequency = 1.5f;
			it.FlickerIntensity = 0.02f;
			it.DrawSprite = false;
			it.KeepOnRoomTransition = true;
		});
		UpdateLightPosition();

		if (Game.Particles is { Supported: true } particles)
		{
			burrowSparkEffect = particles.AcquireEffect("sparks");
			plasmaStrikeEffect = particles.AcquireEffect("plasma_strike");
		}
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
		if (grounded)
		{
			burrowNeedsGrounding = false;
			dashNeedsGrounding = false;
		}

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

	void ClampPositionToRoomOrTransitionRooms()
	{
		if (Health <= 0) return;

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
			FallOffCliff();
		}
		else if (Position.Y < Game.Bounds.Top)
		{
			if (Game.Transition(Point2.Up))
				Velocity.Y = -150;
			else
				Position.Y = Game.Bounds.Top;
		}
	}

	static int CliffFallDamage => 1;
	// How far to pull the respawn in from the room-entry edge, and how far up/down to hunt
	// for footing as we step inward (a step follows a ≤45° slope by ~1px per pixel).
	static int RespawnInset => 16; // ~2 tiles
	static int RespawnFootingSearch => 6;

	// Falling off the bottom of the room is no longer instant death: take a normal hit and
	// respawn just inside the room from where we entered. If the hit happens to be fatal,
	// ApplyHurt runs the usual death/reload, so we leave the position alone in that case.
	void FallOffCliff()
	{
		Game.HitStun(0.4f);
		if (!Game.TryRespawnPlayerAtSafeGroundedRespawn(this))
			Position = InsetRespawnPoint();
		Stop();
		ApplyHurt(-CliffFallDamage);
		if (Health > 0)
		{
			Stop(); // cancel the hurt knockback so we don't get flung back off the ledge
			GrantIFrames(); // brief invincibility so we don't get re-hit on the way back in
		}
	}

	// Walks inward from the room-entry point (toward room center) along the ground, one
	// pixel at a time, stopping at the last spot with fully-supported footing before a gap
	// or wall. Falls back to the raw entry point if it isn't itself on footing.
	Point2 InsetRespawnPoint()
	{
		var entry = Game.RoomEntryPosition;
		var inwardX = Math.Sign(Game.Bounds.Center.X - entry.X);
		if (inwardX == 0)
			inwardX = 1;

		if (!TrySnapToFooting(entry, out var best))
			return entry;

		var y = best.Y;
		for (var i = 1; i <= RespawnInset; i++)
		{
			if (!TrySnapToFooting(new Point2(entry.X + inwardX * i, y), out var landed))
				break; // a gap or wall ahead — stay on the last good footing
			best = landed;
			y = landed.Y;
		}
		return best;
	}

	// Scans a short vertical window around `from` for a standing spot that's clear of solids
	// and has both bottom corners of the footprint on the ground; topmost match wins.
	bool TrySnapToFooting(Point2 from, out Point2 result)
	{
		for (var dy = -RespawnFootingSearch; dy <= RespawnFootingSearch; dy++)
		{
			var p = new Point2(from.X, from.Y + dy);
			if (HasSupportedFooting(p))
			{
				result = p;
				return true;
			}
		}
		result = from;
		return false;
	}

	bool HasSupportedFooting(Point2 pos)
	{
		var box = StandingHitboxRect + pos;
		if (Game.OverlapsRoomCollision(box))
			return false; // standing space is blocked

		var groundY = box.Bottom; // first pixel row below the feet
		return Game.OverlapsRoomCollision(new RectInt(box.Left, groundY, 1, 1))
			&& Game.OverlapsRoomCollision(new RectInt(box.Right - 1, groundY, 1, 1));
	}

	bool IsSafeGroundedRespawn()
		=> Health > 0 && grounded && State != States.Burrow && OverlapsFirst(Point2.Down, Masks.Solid) is not MovingPlatform;

	protected override void AfterMovementUpdate(Vector2 previousVelocity)
	{
		if (IsSafeGroundedRespawn())
			Game.NoteSafeGroundedRespawn(Position);

		ClampPositionToRoomOrTransitionRooms();

		if (State == States.Burrow)
		{
			// invulnerable while underground
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
		UpdateHat(previousVelocity);
	}

	void UpdateHat(Vector2 previousVelocity)
	{
		var velocityDelta = Vector2.Clamp(Velocity - previousVelocity, new(-HatMaxVelocityDelta), new(HatMaxVelocityDelta));
		hatOffsetVelocity -= velocityDelta * HatMotionResponse;
		hatAngularVelocity -= velocityDelta.X * HatAngularMotionResponse;

		var dt = Math.Min(Time.Delta, 1f / 30f);
		hatOffsetVelocity.X += -hatOffset.X * HatSpring.X * dt;
		hatOffsetVelocity.Y += -hatOffset.Y * HatSpring.Y * dt;
		hatOffsetVelocity.X *= MathF.Exp(-HatDamping.X * dt);
		hatOffsetVelocity.Y *= MathF.Exp(-HatDamping.Y * dt);
		hatOffset += hatOffsetVelocity * dt;
		hatAngularVelocity += -hatAngle * HatAngularSpring * dt;
		hatAngularVelocity *= MathF.Exp(-HatAngularDamping * dt);
		hatAngle += hatAngularVelocity * dt;

		if (hatOffset.LengthSquared() > HatMaxOffset * HatMaxOffset)
		{
			var outward = Vector2.Normalize(hatOffset);
			hatOffset = outward * HatMaxOffset;
			hatOffsetVelocity -= outward * MathF.Max(0, Vector2.Dot(hatOffsetVelocity, outward));
		}
		if (MathF.Abs(hatAngle) > HatMaxAngle)
		{
			hatAngle = Math.Clamp(hatAngle, -HatMaxAngle, HatMaxAngle);
			if (MathF.Sign(hatAngularVelocity) == MathF.Sign(hatAngle))
				hatAngularVelocity = 0;
		}
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

	protected override void OnPawnJump()
	{
		if (InWater)
			AudioEvent("player/swim");
		else
			AudioEvent("jump");
	}

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

	protected override void OnRollStopped(bool immediate)
	{
		base.OnRollStopped(immediate);
		StopRollSound(immediate);
	}


	protected override void UpdateRollAudioPosition()
		=> Game.SetAudioEventPosition(rollSound, Position, Game.GetFirst(Masks.Player)?.Position);

	void StartRollSound()
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

	void StopRollSound(bool immediate) => StopAndReleaseSound(ref rollSound, immediate);

	static float ATTACK_WINDUP => 0.09f;
	static float ATTACK_DURATION => 0.41f;

	void UpdateFootsteps()
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

	bool attackUp = false;

	public void AttackState()
	{
		if (duckAttack)
			ducking = true;

		if (input.MoveY < 0)
		{
			Game.Show("attack up");
			attackUp = true;
		}

		Play(GetAttackAnimation(), false);

		if (state.JustCrossedTime(ATTACK_WINDUP))
		{
			var hitbox = GetAttackHitbox(duckAttack);
			attackBlocked = AttackThisFrame(hitbox);
			if (attackBlocked)
			{
				Play(GetAttackAnimation(), false);
				AudioEvent("player/sword_block");
			}
			else
			{
				AudioEvent(duckAttack ? "sword_crouched" : "sword");
				EmitPlasmaStrike(duckingAttack: duckAttack, attackUp: attackUp);
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
			attackUp = false;
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

	bool AttackThisFrame(RectInt it)
	{
		if (Facing == Signs.Negative)
			it.X = -(it.X + it.Width);

		it += Position;

		Game.CollisionRectsThisFrame.Add(it);

		var blockedByShield = IsAttackBlocked(it, checkOnlyForStrongActorBlock: true);
		if (blockedByShield)
		{
			attackBlocked = true;
		}

		var hitSomething = false;
		if (!blockedByShield)
		{
			var hit = Game.OverlapsFirst(it, Masks.Enemy | Masks.Hazard | Masks.Destructible);
			if (hit != null)
			{
				Hit(hit);
				hitSomething = true;
			}
		}

		if (!hitSomething && !attackBlocked && (duckAttack || state.Time >= ATTACK_WINDUP) && IsAttackBlocked(it))
		{
			attackBlocked = true;
			Play(GetAttackAnimation(), false);
			AudioEvent("player/sword_block");
		}

		if (!attackBlocked)
		{
			Game.BreakTiles(it);
			Game.HitVisualActors(it);
		}

		return attackBlocked;
	}

	bool IsAttackBlocked(bool duckingAttack, bool checkOnlyForStrongActorBlock = false)
	{
		//
		// TODO: make this false if we would have hit a breakable block or enemy
		//
		var hitbox = GetAttackHitbox(duckingAttack);
		if (Facing == Signs.Negative)
			hitbox.X = -(hitbox.X + hitbox.Width);
		hitbox += Position;
		return IsAttackBlocked(hitbox, checkOnlyForStrongActorBlock);
	}

	bool IsAttackBlocked(in RectInt hitbox, bool checkOnlyForStrongActorBlock = false)
	{
		var attackPath = hitbox.Conflate(Position);
		foreach (var actor in Game.Actors)
		{
			if (actor != this && !Game.destroying.Contains(actor))
			{
				if (actor.BlocksAttack(this, attackPath))
					return true;
			}
		}

		if (checkOnlyForStrongActorBlock)
			return false;

		if (Game.GetRoomCollisionOverlapRatio(hitbox, includeBreakableTiles: false) >= AttackBlockOverlapThreshold)
			return true;

		return false;
	}

	RectInt GetAttackHitbox(bool duckingAttack)
	{
		if (duckingAttack)
			return new(3, -6, 13, 6);
		else if (attackUp)
			return new(0, -20, 6, -13);

		return new(8, -9, 19, 9);
	}

	void EmitPlasmaStrike(bool duckingAttack, bool attackUp = false)
	{
		float normalLength = 12f;
		float baseVelocity = 20f;
		Vector2 addedVelocity = new(Velocity.X, Velocity.Y * 0.9f);
		if (attackUp)
		{
			Game.Particles?.EmitLine(plasmaStrikeEffect, Position + new Vector2(Facing * 1, 0) + new Vector2(0, -1) * 23, new Vector2(0, -1), normalLength, new Vector2(0, baseVelocity) + addedVelocity);
		}
		else
		{
			var direction = Facing == Signs.Positive ? Vector2.UnitX : -Vector2.UnitX;
			var y = duckingAttack ? -3 : -4;
			var startOffsetX = duckingAttack ? 5 : 12;
			var lineLength = duckingAttack ? 7 : normalLength;
			Game.Particles?.EmitLine(plasmaStrikeEffect, Position + direction * startOffsetX + new Vector2(0, y), direction, lineLength: lineLength, baseVelocity: new Vector2(baseVelocity * Facing, 0) + addedVelocity);
		}
	}

	void EmitPlasmaStrike(Vector2 from, Vector2 to)
	{
		Game.Particles?.EmitLineBetween(plasmaStrikeEffect, from, to);
	}

	string GetAttackAnimation()
	{
		if (duckAttack)
			return attackBlocked ? "duck_attack_block" : "duck_attack";

		else if (attackUp)
			return "attack_up";

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
			Game.RespawnPlayerAtLastGrounded(restartMusic: true);
		}
	}

	public void StartState()
	{
		if (state.Time >= 1.0f)
			State = States.Normal;
	}

	public override void OnWasHit(Actor by)
	{
		int healthDelta = -by.DamageValue;
		if (!Game.Manager.GodMode)
			ApplyHurt(healthDelta, fromActor: by);
	}

	void ApplyHurt(int healthDelta = -1, Actor? fromActor = null)
	{
		Game.HitStun(0.1f);
		Game.Shake(0.1f);
		Game.PulseDitherGlitch(HurtGlitchAxis(fromActor), amount: healthDelta == 0 ? 0.7f : 1.0f);
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

	Vector2 HurtGlitchAxis(Actor? fromActor)
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
			RenderHat(batcher, hatSprite?.Frames[0].Subtexture ?? default, Color.White);
		}
		RenderBlink(batcher);
	}

	public override void RenderNormals(Batcher batcher)
	{
		if (!BurrowHidesSprite)
		{
			base.RenderNormals(batcher);
			RenderHat(batcher, hatSprite?.Frames[0].NormalSubtexture ?? default, Color.White);
		}
	}

	public override void RenderShadow(Batcher batcher, Color color)
	{
		if (!BurrowHidesSprite)
		{
			base.RenderShadow(batcher, color);
			RenderHat(batcher, hatSprite?.Frames[0].Subtexture ?? default, color);
		}
	}

	public override void RenderReflection(Batcher batcher, Color color)
	{
		if (!BurrowHidesSprite)
		{
			base.RenderReflection(batcher, color);
			RenderHat(batcher, hatSprite?.Frames[0].Subtexture ?? default, color);
		}
	}

	public override void RenderEmissive(Batcher batcher)
	{
		if (!BurrowHidesSprite)
		{
			base.RenderEmissive(batcher);
			// RenderHat(batcher, hatSprite?.Frames[0].Subtexture ?? default, Color.White);
		}
		RenderBurrowLine(batcher);
	}

	void RenderHat(Batcher batcher, Subtexture subtexture, Color color)
	{
		if (SpriteBlinkHidden || hatSprite == null || hatSprite.Frames.Count == 0 || Sprite?.TryGetSocket("hat", AnimationFrameIndex, out var socket) != true)
			return;

		var hatSpriteOrigin = hatSprite.ResolveOrigin(SpriteAnchor.BottomCenter);
		var socketPosition = socket.Position + new Vector2(1f, 2f);
		var playerScale = new Vector2((int)Facing * Squish.X, Squish.Y);
		var rootPosition = (Shift + socketPosition - Origin) * playerScale;

		Vector2 position;
		float rotation;
		if (PerfToggles.HatPhysics)
		{
			position = rootPosition + (PerfToggles.HatPhysics ? hatOffset : new());
			var normal = socket.Normal * playerScale;
			rotation = MathF.Atan2(normal.Y, normal.X) - MathF.PI * 0.5f + (PerfToggles.HatPhysics ? hatAngle : new());
		}
		else
		{
			position = rootPosition;
			rotation = 0;
		}

		var hatFacing = Facing;
		// a few animation frames are facing Facing but the head is turned the other way
		if (Animation.Name is "jump_turn" or "skid")
		{
			hatFacing = -hatFacing;
			position.X -= Facing; // visual tweak
		}

		// TODO: represent visually somehoe in the hat when you cannot burrow here by checking CanBurrow() and changing the color etc?

		batcher.Image(subtexture, position, hatSpriteOrigin, new Vector2((int)hatFacing, 1), rotation, color);
	}

	// Player light: normally a soft warm glow; while burrowing it swells brighter and shifts
	// color, lerping in and out so entering/leaving the ground eases rather than snaps.
	static float BaseLightIntensity => 0.45f;
	static float BurrowLightIntensity => 0.85f;
	static Color BaseLightColor => 0xffcfa0;
	static Color BurrowLightColor => 0xb98cff;
	static float BurrowLightBlendRate => 6f;
	float burrowLightBlend;

	void UpdateLightPosition()
	{
		if (light == null)
			return;

		light.Position = Position + new Point2(0, -6);

		var target = State == States.Burrow ? 1f : 0f;
		burrowLightBlend = Calc.Approach(burrowLightBlend, target, BurrowLightBlendRate * Time.Delta);
		light.Intensity = BaseLightIntensity + (BurrowLightIntensity - BaseLightIntensity) * burrowLightBlend;
		light.LightColor = Color.Lerp(BaseLightColor, BurrowLightColor, burrowLightBlend);
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
		if (plasmaStrikeEffect >= 0 && Game.Particles is { } particles)
			particles.ReleaseEffect(plasmaStrikeEffect);
		plasmaStrikeEffect = -1;
	}
}
