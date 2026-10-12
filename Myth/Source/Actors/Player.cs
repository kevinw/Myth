using System.Numerics;
using Foster.Framework;
namespace Myth;

public readonly record struct PawnInput(
	float MoveX,          // analog horizontal thrust in [-1, 1]; AI can output fractional values for
	                      // precise steering/braking; human sticks saturate before full travel.
	int MoveY,
	bool JumpPressed,
	bool JumpDown,
	bool DashPressed,
	bool DashDown = false,
	bool AllowWallJump = true,
	bool AttackPressed = false);


// What a pawn is capable of at all, as opposed to what it can do right now (CanBurrow()).
[Flags]
public enum PawnAbilities
{
	None = 0,
	Burrow = 1 << 0,
}

public abstract partial class Pawn : Actor
{
	protected virtual PawnAbilities Abilities => PawnAbilities.None;

	protected int jumpParticlesEffect = -1;
	int splashParticlesEffect = -1;
	int embersParticlesEffect = -1;

	override public void Added()
	{
		base.Added();

		jumpParticlesEffect = AcquireParticleEffect("jump");
		splashParticlesEffect = AcquireParticleEffect("splash");
		embersParticlesEffect = AcquireParticleEffect("embers");

		Game.RoomChanged += OnRoomChanged;
	}

	public override void Destroyed()
	{
		Game.RoomChanged -= OnRoomChanged;
		ReleaseParticleEffect(ref jumpParticlesEffect);
		ReleaseParticleEffect(ref splashParticlesEffect);
		ReleaseParticleEffect(ref embersParticlesEffect);
		ReleaseBurrowParticles();
		StopAndReleaseSound(ref wallSlideSound, true);
		StopAndReleaseSound(ref burrowSound, true);
	}

	void OnRoomChanged(RoomChange change) {
		// don't get stuck in Climb when leaving a room
		if (state.Current == States.Climb) {
			state.Change(States.Normal);
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
		Burrow,
		Climb,
		BackDash
	}
	public static float FastFallMultiplier => 1.25f;
	protected static float JumpBufferTime => 0.08f;

	public static float MaxGroundSpeed => 93.T();
	public static float MaxAirSpeed => 100.T();
	protected static float CrawlSpeed => 28.T();
	public static float GroundAccel => 400.T();
	public static float AirAccel => 310.T();
	public static float Friction => 480.T();
	static float DashRunGroundSpeed => 145.T();
	static float DashRunAirSpeed => 155.T();
	static float DashRunReverseAccelMultiplier => 0.85f.T();
	static float DashRunAnimationRate => 1.4f.T();
	protected static float DashCancelFriction => 900f.T();
	protected static int DashGroundParticleCount => 1;
	// Analog MoveX below this magnitude counts as no horizontal input for physics and facing.
	static float MoveInputDeadzone => 0.1f;
	static float GroundLocomotionAnimationHold => 0.1f;
	static float GroundLocomotionAnimationSpeed => 2.0f;
	// Walking into a wall, OnCollideX zeroes Velocity.X every frame, so the achieved speed reads
	// "stopped" while the legs are still working. Drive the cycle from the effort instead, a touch
	// under full rate so it reads as strain, and shove a squish into the wall on each footfall.
	static float WallPushAnimationRate => 0.9f;
	static float WallPushHold => 0.1f;
	static float LandingRumbleMinSpeed => 60f;
	static float LandingRumbleFullSpeed => 300f;
	// Impact speed lerps between these; only the low motor runs, so a landing stays a thud.
	static RumbleEffect LandingRumbleSoft => new(0.03f, 0, 0.05f);
	static RumbleEffect LandingRumbleHard => new(0.22f, 0, 0.11f);
	static float RunningOutOfBreathRate => 0.14f;
	static float JumpOutOfBreath => 0.16f;
	static float OutOfBreathRecoveryRate => 0.08f;
	static float OutOfBreathAnimationBoost => 0.3f;
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
	public static float WaterSurfaceTensionMultiplier => 0.5501f.T();
	// Horizontal water resistance: lower top speed, plus a drag that bleeds off speed when
	// no direction is held (even airborne, unlike the grounded-only dry friction).
	public static float WaterHorizontalSpeedMultiplier => 0.6f.T();
	public static float WaterHorizontalDrag => 300f.T();
	public static float WaterRollSpeedMultiplier => 0.55f.T();
	public static float WaterRollDamping => 0f.T();
	public static float WaterRollSinkSpeed => 45f.T();
	static float WaterRollSurfaceDepth => 3f;
	static float WaterRollSurfaceCatchHeight => 6f;
	static float WaterRollSplashSpacing => 16f;
	static float WaterRollSplashStrength => 20f;
	static float WaterRollHopHeight => 3f;
	// Lava: the same swim rules, but thick -- it grabs harder as you plunge in, you sink and
	// drift slower, and each stroke lifts you less.
	public static float LavaGravityMultiplier => 0.2f.T();
	public static float LavaMaxSinkSpeed => 22f.T();
	public static float LavaSinkDamping => 400f.T();
	public static float LavaSwimStroke => -42f.T();
	public static float LavaSurfaceTensionMultiplier => 0.3f.T();
	public static float LavaHorizontalSpeedMultiplier => 0.4f.T();
	public static float LavaHorizontalDrag => 600f.T();
	static float LavaSplashImpulseScale => 0.5f; // thick liquid ripples less than water
	static float LavaSkimBurnScale => 0.5f; // skimming burns at this fraction of the submerged rate

	protected float SwimStroke => InLava ? LavaSwimStroke : WaterSwimStroke;

	protected static float WallHugGravityMultiplier => 0.25f;
	protected static float WallSlideMaxFallSpeed => 80f;  // capped, slower descent while wall sliding
	protected static float WallSlideFallDamping => 320f;  // eases a fast fall down to the slide cap
	// Playtesters holding jump beside a wall expected to dive into the floor and burrow, but the
	// wall slide caught them on the way down and they slid instead. Holding the burrow button
	// drops the slide, so the fall reaches the floor and the landing burrow takes.
	static bool BurrowButtonDisablesWallSlide => true;
	static float WallSlideFeedbackMinSpeed => 15f;
	static float WallSlideRumbleIdle => 0.0075f;
	static float WallSlideRumbleSpeed => 0.0125f;
	static float WallSlideRumbleDuration => 0.05f;
	static RumbleEffect WallContactRumble => new(0.08f, 0.025f, 0.06f);
	static RumbleEffect WallJumpRumble => new(0.055f, 0.04f, 0.06f);
	static float WallJumpCoyoteTime => 0.08f;
	public static float JumpForce => -139;
	public static float JumpTime => 0.22f;
	// Gravity is scaled by this while the jump button is held (floatier ascent), and
	// vertical velocity is multiplied by JumpCutMultiplier when the jump is released early.
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
	static float AirDrag => 150; // how fast launched speed above MaxAirSpeed bleeds away
	static float AirborneRollDistance => Game.TileSize * 2f;
	protected static float DashSpeed => 250;
	protected static float DashDuration => 0.15f;
	protected static float DashCooldown => 0.154f;
	// Dash as a mode rather than a launch: the press skips the fixed-speed burst and its
	// particles, and only engages the faster run (held). False restores the burst.
	protected static bool DashEngagesRunOnly => true;
	// Applies while dash-running under DashEngagesRunOnly, since there's no burst to carry
	// you up to the raised top speed.
	protected static float DashRunAccelMultiplier => 1.5f.T();
	// Letting go of the stick while dash-running keeps you running.
	protected static bool DashAutoRun => false;
	// How fast you must already be moving for a neutral stick to keep dash-running you along.
	protected static float DashRunAutoRunSpeed => 8f.T();
	// Rope climbing: hold up near a rope to grab on (see Rope + ClimbState). Movement-toy
	// tuning: swinging is meant to carry into release jumps, so RopeSwingCarry rewards a
	// pumped-up pendulum.
	protected static float ClimbUpSpeed => 55f.T();
	protected static float ClimbDownSpeed => 85f.T();
	static float RopeGrabRadius => 9f.T();
	static float RopeRegrabTime => 0.3f;
	static float RopeSwingCarry => 1.25f.T();
	static float RopeJumpKick => 55f.T();
	static float RopeGrabMomentumTransfer => 0.7f.T();
	static float RopePumpReversalSpeed => 2f;
	static float RopePumpSkew => 0.1f;
	static float RopeCreakCenterThreshold => 1.5f;
	static float RopeCreakMinSpeed => 12f;
	static float RopeCreakFullRumbleSpeed => 120f;
	static float RopeCreakMinRumble => 0.08f;
	static float RopeCreakMaxRumble => 0.25f;
	static float RopeCreakRumbleDuration => 0.08f;
	static float RopeDetachSnapDistance => 24f; // rope yanked further than this in one frame (rewind, teleport) -> let go

	internal static RectInt StandingHitboxRect => new(-4, -12, 8, 12);
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

	bool canAirHop = false;
	float lastGroundedJumpTimer = 0;

	bool rollingOnWater;
	float waterRollSplashDistance;
	float airBubbleTimer;
	// GetWallJumpDirection runs two solid-overlap probes and is called up to 3x per
	// Update (gravity wall-hug, NormalState wall-slide, jump handling). Position is
	// fixed for the state/physics phase, and each pawn only queries it from a single
	// phase (Player after grounded is set, Boss during GetInput), so the
	// result is invariant within a frame. Cache it, invalidated once per Update.
	int wallJumpDirectionCache;
	bool wallJumpDirectionCached;
	int wallJumpCoyoteDirection;
	float wallJumpCoyoteTimer;
	protected bool ducking;
	SoundInstance? wallSlideSound;
	bool wasWallSliding;
	float wallSlidePulseTimer;
	protected float wallJumpInputLockTimer;
	protected int wallJumpDirection;
	protected int dashDirection;
	protected float dashCooldownTimer;
	protected float dashRemainder;
	protected bool dashNeedsGrounding;
	protected bool dashRunArmed;
	protected int dashRunDirection;
	protected float horizontalInput;
	protected bool dashCancelBraking;
	protected float jumpForce = JumpForce;
	protected bool jumpCutImmune; // suppresses the short-hop cut (e.g. for the burrow pop)
	float carrySpeed; // launched speed the air clamp only bleeds down from (see Launch)
	Vector2 lift; // what the ground under us is carrying us at (Actor.LiftVelocity)
	float airborneRollDistance;
	protected PawnInput input;
	internal Vector2 ButtonPressIntent => new(input.MoveX, input.MoveY);
	Rope? climbRope;
	Rope.Chain? climbChain;
	float climbIndex;   // float point index along the chain (0 = anchor)
	int ropePumpInputDirection;
	int ropePumpDirection;
	bool ropePumpStarted;
	int ropeCreakSide;
	float regrabTimer;
	protected float JumpUpSquish;
	protected float RunStartTimer;
	protected bool RunStartSkid;
	float groundLocomotionAnimationTimer;
	float wallPushTimer;
	Wind.PawnInfluence windInfluence;
	internal PawnInput? ScriptedInput;
	internal PawnInput ConsumedInput => input; // the input this pawn acted on this frame (for replay recording)
	internal float ScriptedClimbSpeed = 1;

	public States State
	{
		get => state.Current;
		set
		{
			if (value != States.Normal && value != States.Burrow)
			{
				EndDashRun();
				dashCancelBraking = false;
			}
			state.Change(value);
		}
	}
	public bool IsGrounded => grounded;
	public bool IsDucking=> ducking;
	public bool IsWallJumpLocked => wallJumpInputLockTimer > 0;
	[NoSerialize] public float OutOfBreath { get; private set; }
	protected virtual float IdleBreathingRate => 1;
	protected override float AnimationRate
		=> IsPlaying("idle") ? IdleBreathingRate * (1 + OutOfBreath * OutOfBreathAnimationBoost)
		: IsDashRunning() && (IsPlaying("run") || IsPlaying("run_start")) ? DashRunAnimationRate
		: IsPlaying("walk") || IsPlaying("run") || IsPlaying("run_start") || IsPlaying("crawl") ? GroundLocomotionAnimationRate
		: 1;
	float GroundLocomotionAnimationRate
		=> wallPushTimer > 0 ? WallPushAnimationRate * MathF.Abs(horizontalInput)
		: Math.Clamp(MathF.Abs(Velocity.X) /
			((IsPlaying("crawl") ? CrawlSpeed : MaxGroundSpeed) * GroundSpeedMultiplier), 0, 1);

	public abstract PawnInput GetInput();
	PawnInput prevInput = new();

	// Crushed, the player and Bud are held flat where they were caught for a beat, then taken out of play
	// like a fall. A burrower is never crushed: it's sealed into the seam instead (UpdateBurrowSeam).
	static float CrushedHoldTime => 0.4f;
	float crushedTimer;
	Vector2 crushedSquish;

	internal override void Crushed(Actor by, Point2 direction)
	{
		if (this is not (Player or Bud))
		{
			base.Crushed(by, direction);
			return;
		}
		// Holding burrow with one to spend dives into the surface we're being pressed into, straight into the seam.
		if (crushedTimer > 0 || State == States.Burrow ||
			input.JumpDown && BurrowAvailable() && TryStartBurrow(Vector2.Zero, (Vector2)direction, by))
			return;
		crushedTimer = CrushedHoldTime;
		crushedSquish = direction.X != 0 ? new(0.25f, 1.4f) : new(1.7f, 0.25f);
		Stop();
		GrantIFrames(CrushedHoldTime);
		AudioEvent("player/squish");
	}

	protected virtual void CrushRecovered() { }

	public override void Update()
	{
		if (Game.Rewinding) return;

		// Gym runs test Bud; the stand-in player is scenery. Freeze it so it can't wander,
		// fall into a pit, or die and drag the room through a transition mid-run.
		if (Game.GymMode && this is Player) return;

		if (crushedTimer > 0)
		{
			Squish = crushedSquish;
			crushedTimer = Calc.Approach(crushedTimer, 0, Time.Delta);
			if (crushedTimer <= 0)
				CrushRecovered();
			return;
		}

		var previousVelocity = Velocity;
		AdvanceFrame();
		if (rollingOnWater && State != States.Roll)
			StopWaterRoll();
		windInfluence = Wind.ApplyTo(this);
		wallJumpDirectionCached = false;
		prevInput = input;
		input = ScriptedInput ?? GetInput();
		if (!input.DashDown)
			EndDashRun();
		horizontalInput = dashRunArmed && input.DashDown
			? UpdateDashRunDirection(input.MoveX)
			: input.MoveX;
		if (input.JumpPressed)
			jumpBufferTimer = JumpBufferTime;

		FollowDeformingGround();
		RefreshGrounded(previousVelocity);
		UpdateLift();
		if (!grounded)
		{
			if (coyoteJumpTimer > 0)
				coyoteJumpTimer = Calc.Approach(coyoteJumpTimer, 0, Time.Delta);
			if (wallJumpInputLockTimer > 0)
				wallJumpInputLockTimer = Calc.Approach(wallJumpInputLockTimer, 0, Time.Delta);
			if (wallJumpCoyoteTimer > 0)
				wallJumpCoyoteTimer = Calc.Approach(wallJumpCoyoteTimer, 0, Time.Delta);
		}
		if (!grounded) // a wall, or braking against it, takes the floor down with the speed
			carrySpeed = MathF.Min(Calc.Approach(carrySpeed, 0, AirDrag * Time.Delta), MathF.Abs(Velocity.X));
		if (dashCooldownTimer > 0)
			dashCooldownTimer = Calc.Approach(dashCooldownTimer, 0, Time.Delta);
		if (jumpBufferTimer > 0)
			jumpBufferTimer = Calc.Approach(jumpBufferTimer, 0, Time.Delta);
		if (regrabTimer > 0)
			regrabTimer = Calc.Approach(regrabTimer, 0, Time.Delta);

		UpdateLiquidState();

		if (!rollingOnWater && State == States.Roll)
			TryStartWaterRoll();
		if (rollingOnWater)
		{
			fluidContact |= Game.LiquidAt(Position + Point2.Down * 2) == LiquidKind.Lava ? FluidContact.Lava : FluidContact.Water;
			inLiquid = true;
			atLiquidSurface = true;
		}
		var enteredFluid = fluidContact & ~previousFluidContact;
		var exitedFluid = previousFluidContact & ~fluidContact;
		// Only real liquids (water, lava) own the splash presentation and surface tension. Neural fields still
		// use the same swim physics, but their actors handle their own crossing feedback.
		var crossedLava = ((enteredFluid | exitedFluid) & FluidContact.Lava) != 0;
		if ((enteredFluid & FluidContact.Liquid) != 0)
		{
			OnCrossedLiquidSurface(Velocity.Y, exiting: false, crossedLava); // downward plunge -> dip
			if (!rollingOnWater)
				Velocity *= InLava ? LavaSurfaceTensionMultiplier : WaterSurfaceTensionMultiplier;
		}
		if ((exitedFluid & FluidContact.Liquid) != 0)
			OnCrossedLiquidSurface(-Velocity.Y, exiting: true, crossedLava); // upward exit -> crown
		previousFluidContact = fluidContact;
		if (inLiquid)
			EndDashRun();
		UpdateAirBubbles();

		var integratesVelocity = State is not (States.Burrow or States.Climb);
		wallSliding = false;
		switch (State)
		{
			case States.Normal:
				NormalState();
				break;
			case States.Roll:
				RollState();
				break;
			case States.Climb:
				ClimbState();
				break;
			case States.Burrow:
				BurrowState();
				break;
			default:
				CustomState();
				break;
		}

		UpdateBurrowRecharge();

		if (ducking && State != States.Normal && State != States.Roll && !AllowDuckingInState())
			ducking = false;
		UpdateHitbox();

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

		if (!grounded && State != States.Burrow && State != States.Climb && !rollingOnWater)
		{
			var grav = Gravity;
			if (inLiquid)
				grav *= InLava ? LavaGravityMultiplier : WaterGravityMultiplier;
			else if (jumpTimer > 0)
				grav *= JumpHoldGravityMultiplier;
			else if (Velocity.Y > 0)
				grav *= FastFallMultiplier;
			if (!inLiquid && State == States.Normal && Velocity.Y > 0 && IsWallHugging())
				grav *= WallHugGravityMultiplier;
			Velocity.Y += grav * Time.Delta;
			if (Velocity.Y > TerminalVelocity) Velocity.Y = TerminalVelocity;
			var maxSink = (InLava ? LavaMaxSinkSpeed : WaterMaxSinkSpeed) * (input.MoveY > 0 ? WaterFastSinkMultiplier : 1);
			if (inLiquid && Velocity.Y > maxSink)
				Velocity.Y = Calc.Approach(Velocity.Y, maxSink, (InLava ? LavaSinkDamping : WaterSinkDamping) * Time.Delta);
			if (!inLiquid && State == States.Normal && Velocity.Y > WallSlideMaxFallSpeed && IsWallHugging())
				Velocity.Y = Calc.Approach(Velocity.Y, WallSlideMaxFallSpeed, WallSlideFallDamping * Time.Delta);

			JumpUpSquish -= Time.Delta;
			if (JumpUpSquish <= 0)
				JumpUpSquish = 0;
		}

		var movementState = State;
		if (integratesVelocity)
			MoveByVelocity();
		RefreshGrounded(previousVelocity);
		if (State != movementState)
			UpdateHitbox();
		AfterActorUpdate(previousVelocity);
		AfterStateUpdate(previousVelocity);
		UpdateWallSlideFeedback();
		wallPushTimer = Calc.Approach(wallPushTimer, 0, Time.Delta);
		UpdateBurrowAlways();
		// Safety net: if the hitbox ended up embedded in solid (e.g. popping out of a
		// burrow inside a slope/wall), nudge to the nearest free spot. The follower
		// keeps us off surfaces during burrow, so skip it there.
		if (State != States.Burrow)
			TryUnstick(burrowExitTimer > 0 ? burrowExitOutward : new Vector2(0, -1));
		AfterMovementUpdate(previousVelocity);
		UpdateOutOfBreath();
		state.Update(State == States.Roll && RollingDownSlope() ? 0 : Time.Delta);
	}

	// A surface that deforms under us -- a root growing or shrinking -- moves a pixel or two a frame.
	// Left alone that reads as a fall: the ground thins away and we land again one pixel lower,
	// stamping a landing sound every time, or it swells into us and a jumpthru we now overlap stops
	// counting as ground at all, so we drop straight through. Instead, step back onto the surface we
	// were already standing on and keep the contact. Rising is always safe to follow, so it gets more
	// room than dropping -- past the drop limit the root really has pulled away, and letting go is
	// the honest outcome.
	// Budgets are in surface-normal pixels: how far the surface itself may travel and still carry us.
	static float GroundFollowRise => 3;
	static float GroundFollowDrop => 2;
	// How much a surface must face up to be followed at all (1 flat, 0 vertical). ~72 degrees.
	static float GroundFollowMinFacing => 0.3f;

	Actor? groundActor;

	void FollowDeformingGround()
	{
		if (groundActor is not { } ground || !grounded || Velocity.Y < 0 ||
			State is States.Burrow or States.Climb || StandsOn(ground, 0))
			return;

		// The surface moves along its own normal, but we can only probe straight up and down: on a
		// slope, a normal step of n pixels reads as n / cos(theta) vertically. Convert the budget
		// through the normal so a steep root gets the same real tolerance a flat one does -- without
		// it a swelling slope outruns a fixed vertical budget and drops the rider through.
		var facing = Math.Clamp(-ground.SurfaceNormalAt(Position).Y, GroundFollowMinFacing, 1);
		var rise = (int)MathF.Ceiling(GroundFollowRise / facing);
		var drop = (int)MathF.Ceiling(GroundFollowDrop / facing);

		for (var step = 1; step <= rise; step++)
			if (StandsOn(ground, -step))
			{
				MovePixels(new Point2(0, -step));
				return;
			}
		for (var step = 1; step <= drop; step++)
			if (StandsOn(ground, step))
			{
				MovePixels(new Point2(0, step));
				return;
			}
	}

	// Actor.GroundActor's test against one actor, with us shifted dy pixels.
	bool StandsOn(Actor ground, int dy)
	{
		var at = new Point2(0, dy);
		return OverlapsAny(at + Point2.Down, ground)
			&& !(ground.Mask.Has(Masks.Jumpthru) && OverlapsAny(at, ground));
	}

	void RefreshGrounded(Vector2 previousVelocity)
	{
		// While burrowing the player rides the surface as a point; skip the normal
		// grounded/landing machinery so it doesn't re-trigger landings mid-burrow.
		groundActor = GroundActor();
		var nowGrounded = State != States.Burrow && State != States.Climb && Velocity.Y >= 0 &&
			(groundActor != null || Game.OverlapsRoomCollision(this, Point2.Down));
		if (nowGrounded && !grounded && !TryBurrowOnLanding(previousVelocity))
			OnLanded(previousVelocity);
		grounded = nowGrounded;
		if (!grounded)
		{
			groundActor = null;
			return;
		}

		coyoteJumpTimer = CoyoteJumpTime;
		coyoteJumpY = Position.Y;
		wallJumpInputLockTimer = 0;
		wallJumpCoyoteTimer = 0;
		wallJumpCoyoteDirection = 0;
		carrySpeed = 0;
	}

	void UpdateHitbox()
	{
		if (State == States.Burrow)
			Hitbox = new(BurrowHitboxRect);
		else if (ducking || State == States.Roll)
			Hitbox = new(CurrentDuckingHitboxRect);
		else
			Hitbox = new(CurrentStandingHitboxRect);
	}

	void UpdateWallSlideFeedback()
	{
		wallSlidePulseTimer = Calc.Approach(wallSlidePulseTimer, 0, Time.Delta);
		var started = wallSliding && !wasWallSliding;
		wasWallSliding = wallSliding;
		if (started)
		{
			Rumble(WallContactRumble);
			wallSlidePulseTimer = WallContactRumble.Duration;
		}

		if (!wallSliding || Velocity.Y < WallSlideFeedbackMinSpeed)
		{
			StopAndReleaseSound(ref wallSlideSound);
			return;
		}

		wallSlideSound ??= Game.CreateAudioEvent("player/wall_slide", Position, start: true);
		Game.SetAudioEventPosition(wallSlideSound, Position);
		if (wallSlidePulseTimer <= 0)
			Rumble(WallSlideRumbleIdle + Calc.ClampedMap(Velocity.Y,
					WallSlideFeedbackMinSpeed, WallSlideMaxFallSpeed) * WallSlideRumbleSpeed,
				0, WallSlideRumbleDuration);
	}

	void UpdateOutOfBreath()
	{
		var running = grounded && State == States.Normal && !ducking &&
			MathF.Abs(horizontalInput) >= 1 &&
			MathF.Abs(Velocity.X) > GroundLocomotionAnimationSpeed;
		OutOfBreath = running
			? Calc.Approach(OutOfBreath, 1, RunningOutOfBreathRate * Time.Delta)
			: Calc.Approach(OutOfBreath, 0, OutOfBreathRecoveryRate * Time.Delta);
	}

	internal bool JumpButtonDown => input.JumpDown;

	protected virtual void AfterActorUpdate(Vector2 previousVelocity) { }
	protected virtual void OnLanded(Vector2 previousVelocity)
	{
		RumbleLanding(previousVelocity.Y);
		Squish = new (1.5f, 0.70f);
		TryStartRoll(previousVelocity);
	}

	void RumbleLanding(float speed)
	{
		if (inLiquid || speed < LandingRumbleMinSpeed)
			return;
		Rumble(RumbleEffect.Lerp(LandingRumbleSoft, LandingRumbleHard,
			Calc.ClampedMap(speed, LandingRumbleMinSpeed, LandingRumbleFullSpeed)));
	}
	protected virtual void AfterStateUpdate(Vector2 previousVelocity) { }
	protected virtual void AfterMovementUpdate(Vector2 previousVelocity) { }
	protected virtual void CustomState() { }
	protected virtual bool AllowDuckingInState() => false;
	// Hands-full flag (Player: holding a Carryable). Gates duck/roll/dash/burrow, the wall
	// moves, and rope grabs; the standing hitbox is read through CurrentStandingHitboxRect
	// so a carrier can swell it to the combined player+cargo body.
	protected virtual bool IsCarrying => false;
	protected virtual RectInt CurrentStandingHitboxRect => StandingHitboxRect;
	// Bodies that don't fit the default crouch (see Stalker) size their own.
	protected virtual RectInt CurrentDuckingHitboxRect => DuckingHitboxRect;
	protected virtual bool TryStartPawnAction(int moveX) => false;
	protected virtual void OnPawnJump() { }
	protected virtual void OnRollStarted() { }
	protected virtual void OnRollStopped(bool immediate)
	{
	}
	protected virtual void OnDashImpact() { }
	protected virtual bool TryRollCancel() => false;
	protected virtual void UpdateRollAudioPosition() { }
	protected virtual float GroundSpeedMultiplier => 1.0f;
	protected virtual float AirSpeedMultiplier => 1.0f;
	protected virtual int? RumbleControllerIndex => null;
	internal void Rumble(float lowFrequency, float highFrequency, float duration)
	{
		if (RumbleControllerIndex is { } controller)
			Game.Manager.Input.Rumble(controller, lowFrequency, highFrequency, duration);
	}
	internal void Rumble(RumbleEffect effect) => Rumble(effect.Low, effect.High, effect.Duration);

	public override void OnCollideX()
	{
		if (grounded && State == States.Normal && MathF.Abs(horizontalInput) >= MoveInputDeadzone)
			wallPushTimer = WallPushHold;
		var dashCollision = State != States.Burrow && (DashBursting || IsDashRunning());
		var dashImpact = dashCollision && Grounded();
		base.OnCollideX();
		if (State != States.Burrow)
		{
			dashCooldownTimer = 0;
			EndDashRun();
		}
		if (State == States.Roll)
		{
			StopWaterRoll();
			OnRollStopped(false);
			State = States.Normal;
		}
		if (dashCollision && PlayerSpecificAudioEvents)
			AudioEvent("player/dash_wall");
		if (dashImpact)
			OnDashImpact();
	}

	bool wallSliding = false;
	double lastSkidParticlesTime;

	// The launch window: the fixed-speed burst is driving movement (and its fx). Distinct from
	// dashCooldownTimer's other job, which is gating the next dash press.
	protected bool DashBursting => !DashEngagesRunOnly && dashCooldownTimer > 0;

	protected bool IsDashRunning() =>
		state.Current == States.Normal && dashRunArmed && input.DashDown && !ducking && !inLiquid;

	protected void EndDashRun()
	{
		dashRunArmed = false;
		dashRunDirection = 0;
		horizontalInput = input.MoveX;
	}

	protected void BeginDashCancelBrake()
	{
		dashCancelBraking = true;
		dashCooldownTimer = 0;
		EndDashRun();
	}

	protected float UpdateDashRunDirection(float moveX)
	{
		if (MathF.Abs(moveX) >= MoveInputDeadzone)
			dashRunDirection = Math.Sign(moveX);
		else if (CanAutoRunOnNeutralStick)
		{
			if (dashRunDirection == 0)
				dashRunDirection = Facing == 0 ? Signs.Positive : Facing;
			moveX = dashRunDirection;
		}
		return moveX;
	}

	// Letting go of the stick while dash-running keeps you running. In the mode-only dash
	// that carry needs motion to carry: without the launch, it would otherwise walk you off
	// from a standstill the moment you held the button.
	bool CanAutoRunOnNeutralStick
		=> DashAutoRun && (!DashEngagesRunOnly || MathF.Abs(Velocity.X) >= DashRunAutoRunSpeed);

	protected int GetHorizontalMovementInput(out float movementInput)
	{
		// Digital projection (with a deadzone) drives facing and the friction gate, while the
		// raw analog value scales horizontal thrust.
		var moveX = MathF.Abs(horizontalInput) < MoveInputDeadzone ? 0 : Math.Sign(horizontalInput);
		movementInput = moveX == 0 ? 0 : horizontalInput;
		if (!grounded && wallJumpInputLockTimer > 0)
			movementInput = wallJumpDirection;
		return moveX;
	}

	protected void UpdateHorizontalMovement(int moveX, float movementInput)
	{
		var speedMultiplier = grounded ? GroundSpeedMultiplier : AirSpeedMultiplier;
		var dashRunning = IsDashRunning();
		var dashBursting = State == States.Normal && DashBursting;
		float maxSpeed;
		if (grounded)
			maxSpeed = (dashRunning ? DashRunGroundSpeed : MaxGroundSpeed) * GroundSpeedMultiplier;
		else
			maxSpeed = (dashRunning ? DashRunAirSpeed : MaxAirSpeed) * AirSpeedMultiplier;
		if (ducking)
			maxSpeed = CrawlSpeed * GroundSpeedMultiplier;
		if (inLiquid)
			maxSpeed *= InLava ? LavaHorizontalSpeedMultiplier : WaterHorizontalSpeedMultiplier;
		if (grounded && movementInput != 0 && !dashBursting)
			maxSpeed *= MathF.Abs(movementInput);

		var acceleration = grounded ? GroundAccel * GroundSpeedMultiplier : AirAccel * AirSpeedMultiplier;
		if (dashRunning && DashEngagesRunOnly)
			acceleration *= DashRunAccelMultiplier;
		if (dashRunning && wallJumpInputLockTimer <= 0 && movementInput != 0 && Velocity.X != 0 &&
			Math.Sign(movementInput) != Math.Sign(Velocity.X))
			acceleration *= DashRunReverseAccelMultiplier;
		Velocity.X += movementInput * acceleration * Time.Delta;
		if (dashBursting)
			Velocity.X = dashDirection * DashSpeed;
		maxSpeed = windInfluence.HorizontalSpeedLimit(maxSpeed, Velocity.X);
		if (dashCancelBraking)
			maxSpeed = MathF.Max(maxSpeed, MathF.Abs(Velocity.X));
		if (!grounded)
			maxSpeed = MathF.Max(maxSpeed, carrySpeed);
		if (MathF.Abs(Velocity.X) > maxSpeed)
			Velocity.X = Calc.Approach(Velocity.X, MathF.Sign(Velocity.X) * maxSpeed, 2000 * Time.Delta);
		if (dashCancelBraking)
		{
			var target = ducking ? movementInput * CrawlSpeed * GroundSpeedMultiplier : 0;
			Velocity.X = Calc.Approach(Velocity.X, target, DashCancelFriction * Time.Delta);
			dashCancelBraking = Velocity.X != target;
		}
		else if (moveX == 0 && grounded && !dashBursting)
			Velocity.X = Calc.Approach(Velocity.X, 0, Friction * Time.Delta);
		else if (moveX == 0 && inLiquid)
			Velocity.X = Calc.Approach(Velocity.X, 0, (InLava ? LavaHorizontalDrag : WaterHorizontalDrag) * Time.Delta);
	}

	protected void NormalState()
	{
		if (TryWallDive())
			return;
		if (input.MoveY < 0 && TryGrabRope())
			return;

		if (grounded && input.MoveY > 0 && !IsCarrying)
			ducking = true;
		else if (ducking)
			ducking = !CanStandUp();
		else
			ducking = false;
		if (ducking && dashRunArmed)
			BeginDashCancelBrake();
		else if (ducking)
			EndDashRun();

		var moveX = GetHorizontalMovementInput(out var movementInput);
		if (!ducking && moveX != 0)
			dashCancelBraking = false;
		if (grounded && moveX == 0 && MathF.Abs(Velocity.X) <= GroundLocomotionAnimationSpeed)
			groundLocomotionAnimationTimer = 0;
		else if (grounded && (moveX != 0 || MathF.Abs(Velocity.X) > GroundLocomotionAnimationSpeed))
			groundLocomotionAnimationTimer = GroundLocomotionAnimationHold;
		else
			groundLocomotionAnimationTimer = Calc.Approach(groundLocomotionAnimationTimer, 0, Time.Delta);
		var animateGroundLocomotion = groundLocomotionAnimationTimer > 0;

		int facing = moveX;

		var didRun = false;
		var didStartRunning = false;
		string? jumpAnim = null;
		if (grounded)
		{
			if (ducking)
				Play(animateGroundLocomotion ? "crawl" : "duck");
			else if (!animateGroundLocomotion)
			{
				if (input.MoveY < 0)
				{
					Play("look_up");
				}
				else
				{
					Play("idle");
				}
			}
			else if (Velocity.X > 0 && moveX < 0 || Velocity.X < 0 && moveX > 0)
			{
				facing *= -1;
				Play("skid", looping: false);
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
						Play("skid", looping: false);
					else
						Play("run_start");
				}
				else
					Play(MathF.Abs(movementInput) < 1 ? "walk" : "run");
			}
		}
		else
		{
			var wallJumpDir = GetWallJumpDirection();
			if (!inLiquid && IsWallHugging(wallJumpDir))
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
				else if (Velocity.Y > 0 && input.MoveY == 1)
					jumpAnim = "jump_down";
				else
					jumpAnim = "jump";
			}
		}


		if (!didRun)
		{
			RunStartTimer = 0;
			RunStartSkid = false;
		}

		var lastFacing = Facing;
		UpdateHorizontalMovement(moveX, movementInput);
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
		else if (inLiquid && moveX != 0)
		{
			Facing = facing;
		}

		// Underwater, jump is a repeatable swim stroke: each press sets a capped upward
		// velocity (Min so chained strokes hold the ascent rather than accelerating), with
		// no ground/coyote requirement. At the surface (head above the waterline) it instead
		// becomes a full-strength jump so you can hop clean out of the water. Ground and wall
		// jumps are suppressed while submerged.
		if (inLiquid && input.JumpPressed)
		{
			jumpBufferTimer = 0;
			JumpUpSquish = 0;
			if (atLiquidSurface)
			{
				Squish = new Vector2(0.65f, 1.4f);
				StartJump(JumpForce, JumpTime);
			}
			else
			{
				Squish = new Vector2(0.8f, 1.2f);
				Velocity.Y = MathF.Min(Velocity.Y, SwimStroke);
			}
			OnPawnJump();
		}

		var wallJump = GetWallJumpDirection();
		var canCoyoteJump = CanCoyoteJump();
		var canGroundJump = grounded && CanStandUp();
		var wasGroundedJump = canGroundJump;
		canGroundJump |= canCoyoteJump;
		var canWallJump = input.AllowWallJump && !IsCarrying && wallJump != 0 && moveX != 0;
		var jumpPressed = input.JumpPressed;
		if (!jumpPressed && canGroundJump && jumpBufferTimer > 0)
			jumpPressed = true;

		lastGroundedJumpTimer = Calc.Approach(lastGroundedJumpTimer, 0, Time.Delta);

		if (!inLiquid && jumpPressed)
		{
			if (canGroundJump || canWallJump)
			{
				var jumpingDownThroughJumpthru = input.MoveY > 0 && HitTestUnderneath()?.Actor is Jumpthru;


				jumpBufferTimer = 0;
				JumpUpSquish = 0;
				Squish = new Vector2(0.65f, 1.4f);
				if (canGroundJump)
				{
					lastGroundedJumpTimer = 0.1f;
					if (canCoyoteJump)
						Position.Y = coyoteJumpY;
					coyoteJumpTimer = 0;
					canAirHop = true;
					if (wasGroundedJump)
						EmitJumpParticles(Position);
				}
				else
				{
					wallJumpDirection = wallJump;
					wallSliding = false;
					wallJumpInputLockTimer = WallJumpInputLockTime;
					facing = wallJumpDirection;
					Facing = wallJumpDirection;
					Velocity.X = wallJumpDirection * WallJumpSpeed;
					EmitJumpParticles(Position - Vector2.UnitX * wallJumpDirection * Hitbox.Rect.Width / 2);
					wallJumpCoyoteTimer = 0;
					wallJumpCoyoteDirection = 0;
					dashCooldownTimer = 0;
					EndDashRun();
					Rumble(WallJumpRumble);
				}

				if (jumpingDownThroughJumpthru)
				{
					StartJump(0, 0);
					Velocity.Y = 4;
					Position.Y += 2;
				}
				else
				{
					StartJump(JumpForce, JumpTime);
				}


				OnPawnJump();
			}
			else if (this is Player && input.JumpPressed && !prevInput.JumpPressed && canAirHop && !grounded && lastGroundedJumpTimer == 0)
			{
				canAirHop = false;
				Velocity.X += input.MoveX * 50;
			}
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

	protected void EmitJumpParticles(Vector2 position, int count = 8) =>
		Game.Particles?.Emit(jumpParticlesEffect,
			worldPosition: position,
			count: count,
			directionRadians: MathF.PI / 2.0f,
			speedScale: 2.51f,
			spreadRadians: 59.0f * Calc.DegToRad);

	protected float burrowTimer = 0;
	protected bool burrowNeedsGrounding; // after a pop, must touch ground again before it can re-arm
	protected bool CanBurrow() => !wallSliding && BurrowAvailable();

	// Every burrow condition except the wall slide, so the slide suppression above can ask
	// whether a burrow would follow without depending on the state it is about to prevent.
	bool BurrowAvailable()
	{
		if (!Abilities.Has(PawnAbilities.Burrow)) return false;
		if (inLiquid) return false;
		if (IsCarrying) return false;
		if (burrowNeedsGrounding) return false;
		if (burrowTimer < 0) return false; // the ability is still cooling down
		if (State != States.Normal) return false;
		return true;
	}

	protected bool TryStartRoll(Vector2 impactVelocity)
	{
		if (State != States.Normal) return false;
		if (IsCarrying) return false;
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
		airborneRollDistance = 0;
		OnRollStarted();
		return true;
	}

	float RollSpeedScale => inLiquid ? WaterRollSpeedMultiplier : 1f;

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

		UpdateRollAudioPosition();
		var airborneRolling = false;
		if (rollingOnWater)
		{
			if (!UpdateWaterRoll())
			{
				OnRollStopped(false);
				State = States.Normal;
				ducking = false;
				return;
			}
		}
		else if (!grounded)
		{
			airborneRollDistance += MathF.Abs(Velocity.X) * Time.Delta;
			if (this is not Player || airborneRollDistance >= AirborneRollDistance)
			{
				OnRollStopped(false);
				State = States.Normal;
				return;
			}
			airborneRolling = true;
		}
		else
		{
			airborneRollDistance = 0;
			var (targetSpeed, friction) = TargetRollSpeedAndFriction();

			// NOTE: this Velocity set is suspect, because in Update() it gets set with a different maxSpeed anyways.
			// probably needs some kind of unification, or some other way for pieces of code to adjust a max speed,
			// which then gets set in one place.
			//
			// once we have that, it would be fun to gradually increase speed as you roll down many slopes without stopping
			Velocity.X = Calc.Approach(Velocity.X, Facing * targetSpeed, friction * Time.Delta);
		}

		if (input.JumpPressed && CanStandUp())
		{
			StopWaterRoll();
			OnRollStopped(false);
			JumpUpSquish = 0;
			Squish = new Vector2(0.75f, 1.25f);
			Velocity.X = Facing * MathF.Max(MathF.Abs(Velocity.X), RollJumpSpeed);
			StartJump(RollJumpForce, RollJumpTime);
			State = States.Normal;
			OnPawnJump();
			return;
		}

		if (TryRollCancel())
		{
			StopWaterRoll();
			return;
		}

		if (rollingOnWater || airborneRolling)
			return;

		if (state.Time >= RollDuration || input.MoveY <= 0)
		{
			OnRollStopped(false);
			State = States.Normal;
			ducking = grounded && (input.MoveY > 0 || !CanStandUp());
		}
	}

	void TryStartWaterRoll()
	{
		if (this is not Player || Velocity.Y < 0 || MathF.Abs(Velocity.X) < WaterRollSinkSpeed)
			return;
		if (Game.WaterSurfaceAt(Position) is not { } surface)
			return;
		if (Position.Y < surface.SurfaceY - WaterRollSurfaceCatchHeight || Position.Y > surface.SurfaceY + Game.TileSize)
			return;

		rollingOnWater = true;
		waterRollSplashDistance = WaterRollSplashSpacing;
		SnapRollToWater(surface);
	}

	bool UpdateWaterRoll()
	{
		if (Game.WaterSurfaceAt(Position) is not { } surface)
		{
			StopWaterRoll();
			return false;
		}

		Velocity.X = Calc.Approach(Velocity.X, 0, WaterRollDamping * Time.Delta);
		if (MathF.Abs(Velocity.X) < WaterRollSinkSpeed)
		{
			StopWaterRoll();
			return false;
		}

		SnapRollToWater(surface);
		waterRollSplashDistance -= MathF.Abs(Velocity.X) * Time.Delta;
		if (waterRollSplashDistance <= 0)
		{
			EmitWaterRollSplash(surface);
			waterRollSplashDistance += WaterRollSplashSpacing;
		}
		var hopProgress = 1f - waterRollSplashDistance / WaterRollSplashSpacing;
		Shift.Y = -MathF.Sin(hopProgress * MathF.PI) * WaterRollHopHeight;
		return true;
	}

	void SnapRollToWater(WaterSurface surface)
	{
		Position.Y = (int)MathF.Round(surface.SurfaceY + WaterRollSurfaceDepth);
		StopY();
	}

	void StopWaterRoll()
	{
		rollingOnWater = false;
		Shift.Y = 0;
	}

	internal bool TryGrabRope()
	{
		if (IsCarrying || regrabTimer > 0)
			return false;

		var center = Position + Hitbox.Rect.Center;
		foreach (var actor in Game.Actors)
		{
			if (actor is not Rope rope || !rope.TryGrab(center, RopeGrabRadius, out var chain, out var index))
				continue;

			// Momentum transfer: swinging into a grab sets the rope moving.
			rope.AddPointVelocity(chain, index, Velocity * RopeGrabMomentumTransfer);
			climbRope = rope;
			climbChain = chain;
			climbIndex = index;
			ropeCreakSide = 0;
			Stop();
			ducking = false;
			jumpTimer = 0;
			State = States.Climb;
			Play("climb_idle");
			return true;
		}
		return false;
	}

	protected void ClimbState()
	{
		if (climbRope == null || climbChain == null || !climbRope.Contains(climbChain))
		{
			ExitClimb();
			return;
		}

		var rope = climbRope;
		var chain = climbChain;

		if (chain.Pinned)
		{
			if (input.MoveY > 0) AudioEventDebounced("rope/slide", 1.0f);
			climbIndex += input.MoveY * (input.MoveY > 0 ? ClimbDownSpeed : ClimbUpSpeed) *
				ScriptedClimbSpeed / Rope.SegmentLength * Time.Delta;
			if (climbIndex < 0)
			{
				// Climbed past the anchor: pop up off the top (lands you on the ledge when there is one).
				ExitClimb();
				Squish = new Vector2(0.65f, 1.4f);
				StartJump(JumpForce * 0.85f, JumpTime * 0.8f);
				OnPawnJump();
				return;
			}
			if (climbIndex > Rope.LastIndex(chain))
			{
				// Slid off the loose end.
				var slideVel = rope.PointVelocity(chain, climbIndex);
				ExitClimb();
				Velocity = new Vector2(slideVel.X * RopeSwingCarry, MathF.Max(slideVel.Y, 0));
				return;
			}
		}
		else if (Grounded())
		{
			// Riding a cut-loose remnant down: it landed, we're standing, let go.
			ExitClimb();
			return;
		}

		if (input.JumpPressed)
		{
			var vel = rope.PointVelocity(chain, climbIndex);
			var moveX = input.MoveX;
			ExitClimb();
			Velocity.X = vel.X * RopeSwingCarry + moveX * RopeJumpKick;
			if (moveX != 0)
				Facing = Math.Sign(moveX);
			if (input.MoveY > 0)
				Velocity.Y = MathF.Max(vel.Y, 0); // down+jump: just let go
			else
			{
				Squish = new Vector2(0.65f, 1.4f);
				StartJump(JumpForce, JumpTime);
				OnPawnJump();
			}
			return;
		}

		if (input.MoveX != 0)
			Facing = Math.Sign(input.MoveX);

		var pumpInput = Math.Sign(input.MoveX);
		if (pumpInput != ropePumpInputDirection)
		{
			ropePumpInputDirection = pumpInput;
			ropePumpDirection = pumpInput;
			ropePumpStarted = false;
		}
		var pumpVelocity = rope.PointVelocity(chain, climbIndex).X * ropePumpDirection;
		if (pumpVelocity > RopePumpReversalSpeed)
			ropePumpStarted = true;
		else if (ropePumpStarted && pumpVelocity < -RopePumpReversalSpeed)
			ropePumpDirection = 0;
		rope.SetRider(chain, climbIndex, ropePumpDirection);
		if (ropePumpDirection != 0)
			Skew = ropePumpDirection * RopePumpSkew;

		// Hang with the chest at the rope point; step there collision-aware so a swing scrapes
		// along walls instead of clipping. A one-frame yank beyond the snap distance (rewind,
		// room teleport) means the attachment is no longer sane -- let go.
		var riderPoint = rope.Sample(chain, climbIndex);
		UpdateRopeCreak(rope, chain, riderPoint);
		var target = riderPoint - Hitbox.Rect.Center;
		if ((target - Position).LengthSquared() > RopeDetachSnapDistance * RopeDetachSnapDistance)
		{
			ExitClimb();
			return;
		}
		Stop();
		Move(target - Position);

		if (chain.Pinned && input.MoveY > 0) {
			Play("climb_down");
		} else if (chain.Pinned && input.MoveY < 0) {
			Play("climb");
		} else {
			Play(ropeIsFalling ? "climb_fall" : "climb_idle");
		}
	}

	void UpdateRopeCreak(Rope rope, Rope.Chain chain, Vector2 riderPoint)
	{
		if (!chain.Pinned)
		{
			ropeCreakSide = 0;
			return;
		}

		var offset = riderPoint.X - chain.Points[0].X;
		if (MathF.Abs(offset) < RopeCreakCenterThreshold)
			return;

		var side = Math.Sign(offset);
		var speed = MathF.Abs(rope.PointVelocity(chain, climbIndex).X);
		if (ropeCreakSide != 0 && side != ropeCreakSide && speed >= RopeCreakMinSpeed)
		{
			Game.PlayAudioEvent("rope/creak", position: riderPoint);
			Rumble(Calc.ClampedMap(speed, RopeCreakMinSpeed, RopeCreakFullRumbleSpeed,
				RopeCreakMinRumble, RopeCreakMaxRumble), 0, RopeCreakRumbleDuration);
		}
		ropeCreakSide = side;
	}

	void ExitClimb()
	{
		ropeIsFalling = false;
		climbRope = null;
		climbChain = null;
		ropePumpInputDirection = ropePumpDirection = 0;
		ropePumpStarted = false;
		ropeCreakSide = 0;
		regrabTimer = RopeRegrabTime;
		if (State == States.Climb)
			State = States.Normal;
	}

	bool ropeIsFalling;

	// A Rope we may be attached to was cut at cutIndex: below the cut our chain becomes the
	// falling remnant -- keep riding it down (the cutscene beat); above it, nothing changes.
	public void OnRopeCut(Rope rope, Rope.Chain upper, int cutIndex, Rope.Chain lower)
	{
		if (climbRope != rope || climbChain != upper)
			return;
		if (climbIndex >= cutIndex)
		{
			climbChain = lower;
			climbIndex -= cutIndex;
		}
		ropeIsFalling = true;
	}

	protected bool TryStartDash(int direction)
	{
		if (IsCarrying || dashCooldownTimer > 0 || dashNeedsGrounding || ducking && !CanStandUp())
			return false;

		dashDirection = direction != 0 ? Math.Sign(direction) : Facing;
		if (dashDirection == 0)
			dashDirection = Signs.Positive;

		Facing = dashDirection;
		dashRemainder = 0;
		dashCooldownTimer = DashCooldown;
		dashNeedsGrounding = true;
		dashRunArmed = input.DashDown;
		dashRunDirection = dashDirection;
		ducking = false;
		Squish = new Vector2(1.35f, 0.82f);
		if (!DashEngagesRunOnly)
		{
			jumpTimer = 0;
			Velocity.X = dashDirection * 200f;
			Velocity.Y = 0;
			Play("jump");
		}
		if (!DashEngagesRunOnly && PlayerSpecificAudioEvents)
			AudioEvent("player/dash_start");
		return true;
	}

	SurfaceHit? HitTestUnderneath() {
		var bounds = Hitbox.Rect + Position;
		if (!Game.TryFindSurface(this, bounds, Vector2.UnitY, 2, out var hit))
			return null;
		return hit;
	}

	bool GetSlopeUnderneath(out Vector2 normal) {
		normal = Vector2.Zero;
		if (HitTestUnderneath() is not { } hit || !hit.Slope.HasValue) return false;
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
		OutOfBreath = Math.Clamp(OutOfBreath + JumpOutOfBreath, 0, 1);
		Launch();
	}

	// Leaving ground that was carrying us, or it stopping under us, hands its motion to us: off a belt's end
	// we sail on rather than drop, and a rising crusher that halts throws us on up. Ground moving down
	// isn't kept, so a slam ending under our feet doesn't land like a fall.
	void UpdateLift()
	{
		var next = grounded && State != States.Burrow && HitTestUnderneath()?.Actor is { } ground ? ground.LiftVelocity : Vector2.Zero;
		if (next == Vector2.Zero && lift != Vector2.Zero)
			Launch();
		lift = next;
	}

	// Leaving with speed -- a jump, a burrow pop, ground that carried us -- takes any lift along, and whatever we
	// leave at becomes a floor the air clamp can't rein in below; it only bleeds away at AirDrag. Roll jumps, wall
	// jumps and crusher flings all keep their momentum the same way.
	protected void Launch()
	{
		Velocity += lift with { Y = MathF.Min(lift.Y, 0) };
		lift = Vector2.Zero;
		carrySpeed = MathF.Max(carrySpeed, MathF.Abs(Velocity.X));
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
		var standingBounds = CurrentStandingHitboxRect + Position;
		if (Game.OverlapsRoomCollision(standingBounds))
			return false;

		foreach (var other in CollisionActors)
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
		if (wallJumpDirectionCache != 0)
		{
			wallJumpCoyoteDirection = wallJumpDirectionCache;
			wallJumpCoyoteTimer = WallJumpCoyoteTime;
		}
		else if (wallJumpCoyoteTimer > 0)
			wallJumpDirectionCache = wallJumpCoyoteDirection;
		return wallJumpDirectionCache;
	}

	int ComputeWallJumpDirection()
	{
		if (grounded)
			return 0;

		var touchingLeft = OverlapsAny(Point2.Left, Masks.Solid);
		var touchingRight = OverlapsAny(Point2.Right, Masks.Solid);
		// In a one-tile shaft both probes hit; push off the wall the pawn is pressing toward.
		if (touchingLeft && touchingRight)
			return MathF.Abs(horizontalInput) < MoveInputDeadzone ? 0 : -Math.Sign(horizontalInput);

		return touchingLeft ? 1 : touchingRight ? -1 : 0;
	}

	bool IsWallHugging()
		=> IsWallHugging(GetWallJumpDirection());

	// Every caller is wall-slide behaviour (hug gravity, the fall cap, the slide anim/state), so
	// the burrow-button suppression lives here instead of at each call site. Wall jumping reads
	// GetWallJumpDirection directly and is untouched.
	bool IsWallHugging(int wallJumpDirection)
		=> !IsCarrying && wallJumpDirection != 0 && MathF.Abs(horizontalInput) >= MoveInputDeadzone &&
			Math.Sign(horizontalInput) == -wallJumpDirection &&
			!(BurrowButtonDisablesWallSlide && input.JumpDown && BurrowAvailable());

	// Splash droplet burst tuning (the surface ripple itself is scaled in WaterSurface).
	const float SplashDropletSpeedRef = 60f; // plunge speed that maps to ~1x droplet speed

	// Fired the frame the torso crosses the waterline (entering or leaving). crossingSpeed is
	// the speed into the surface (downward on entry, upward on exit), so a faster crossing
	// splashes harder; a slow drift over the line barely disturbs it. On exit the wave impulse
	// flips to push the surface up into a crown instead of dipping it.
	void OnCrossedLiquidSurface(float crossingSpeed, bool exiting, bool lava)
	{
		// The nav-graph build steps throwaway pawns through Pawn.Update; don't splash the
		// real room during that simulation.
		if (Game.Simulating)
			return;

		var strength = MathF.Max(0f, crossingSpeed);
		if (lava)
		{
			EmitLavaSplash(strength, exiting);
			AudioEventDebounced("player/lava_splash", 0.2f, "Speed", strength);
		}
		else
		{
			EmitWaterSurfaceSplash(strength, exiting, (int)Calc.Clamp(6f + strength * 0.25f, 6f, 28f));
			AudioEventDebounced("player/splash", 0.2f);
		}
	}

	// A heavy gloop instead of a splash: a smaller dip in the surface, and embers thrown up
	// instead of droplets, scaled by the crossing strength.
	void EmitLavaSplash(float strength, bool exiting)
	{
		Game.SplashWater(Position, (exiting ? -strength : strength) * LavaSplashImpulseScale);
		var surfaceY = Game.WaterSurfaceAt(Position)?.SurfaceY ?? (Hitbox.Rect + Position).Top;
		StirLiquidSurface(surfaceY, strength);
		if (embersParticlesEffect >= 0)
			Game.Particles?.Emit(embersParticlesEffect,
				worldPosition: new Vector2(Position.X, surfaceY),
				count: (int)Calc.Clamp(3f + strength * 0.15f, 3f, 16f),
				directionRadians: -MathF.PI / 2f, // up
				speedScale: Calc.Clamp(strength / SplashDropletSpeedRef, 0.5f, 1.8f),
				spreadRadians: 50f * Calc.DegToRad);
	}

	void EmitWaterRollSplash(WaterSurface surface)
	{
		if (Game.LiquidVolumeAt(new Point2(Position.X, (int)surface.SurfaceY + 2)) is { Kind: LiquidKind.Lava } lava)
		{
			// Each skip pays out the burn for the stretch since the last one, so a faster roll
			// hurts less per bounce and the total tracks time spent on the lava.
			var burned = (this as Player)?.Burn(LiquidVolume.LavaBurnPerSecond * LavaSkimBurnScale * WaterRollSplashSpacing / MathF.Abs(Velocity.X), lava) ?? 0;
			EmitLavaSplash(WaterRollSplashStrength, exiting: false);
			AudioEventDebounced("player/lava_skim", 0.1f, "Damage", burned);
			return;
		}
		EmitWaterSurfaceSplash(WaterRollSplashStrength, exiting: false, count: 6);
		AudioEventDebounced("player/splash", 0.2f);
	}

	// A splash churns the liquid it lands in: lava flares and melts its crust, water aerates.
	void StirLiquidSurface(float surfaceY, float strength)
		=> Game.LiquidStir.Stir(new Vector2(Position.X, surfaceY + 4), 8 + strength * 0.08f, Calc.Clamp(strength / 100f, 0.3f, 1f));

	void EmitWaterSurfaceSplash(float strength, bool exiting, int count)
	{
		Game.SplashWater(Position, exiting ? -strength : strength);
		StirLiquidSurface(Game.WaterSurfaceAt(Position)?.SurfaceY ?? (Hitbox.Rect + Position).Top, strength);

		if (splashParticlesEffect >= 0 && Game.Particles is { Supported: true } particles)
		{
			// Emit at the waterline (segment surface Y) rather than the submerged torso.
			var surfaceY = Game.WaterSurfaceAt(Position)?.SurfaceY ?? (Hitbox.Rect + Position).Top;
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
		if ((fluidContact & FluidContact.Water) == 0 || rollingOnWater || Game.Simulating)
			return;

		airBubbleTimer -= Time.Delta;
		if (airBubbleTimer > 0)
			return;
		airBubbleTimer = Game.rng.Float(AirBubbleIntervalMin, AirBubbleIntervalMax);

		var bounds = Hitbox.Rect + Position;
		var spawn = new Point2(bounds.Center.X + Game.rng.Int(-2, 3), bounds.Top + 2);
		if (Game.IsWaterAt(spawn))
			Game.Create<AirBubble>(spawn);
	}
}

[GameActor(HideFromPalette = true)]
public partial class Player : Pawn, INeuralTextureAgent
{
	protected override int? RumbleControllerIndex => Game.Controls.PlayerControllerIndex;

	// The body pink out of player.ase, lifted toward white the same way Bud's blue is.
	internal static readonly Color BodyColor = new(0xc2698d);
	internal static readonly Color SpeechColor = Color.Lerp(BodyColor, Color.White, 0.45f);

	internal Game.StorySpeechBubble? Say(string text, OutLoud.Emotion emotion = OutLoud.Emotion.Neutral)
		=> Game.ShowSpeech(this, new Vector2(0, -32), text, Game.PlayerVoiceProfile, SpeechColor, emotion, Game.PlayerVoiceName);

	public override PawnInput GetInput()
	{
		return new(
			MoveX: Controls.ShapeMoveX(Controls.Move.Value.X),
			MoveY: Controls.Move.IntValue.Y,
			JumpPressed: Controls.Jump.Pressed,
			JumpDown: Controls.Jump.Down,
			DashPressed: Controls.Dash.Pressed,
			DashDown: Controls.Dash.Down,
			AttackPressed: Controls.Attack.Pressed
		);
	}

	public const int MaxHealth = 99;
	// One hit's worth: six still kill from full, as when health was six hearts, and five don't.
	public const int HitDamage = 17;

	static float AttackFriction => 150;
	static float AttackWaveDuration => 0.39f;
	static float AttackWaveStartRadius => 1f;
	static float AttackWaveMaxRadius => 95f;
	static float AttackWaveArcAngle => 100f * MathF.PI / 180f;
	static float AttackWaveStrength => 0.59f;
	static float AttackWavePush => 18.5f; // Simulation cells of outward displacement per step.
	// Falloff exponents: 1 = linear; higher fades sooner, lower carries farther (keep > 0).
	static float AttackWavePushFalloff => 3f;
	static float AttackWaveStrengthFalloff => 0.75f;
	static float AttackWaveThickness => 1f;
	static RumbleEffect AttackBlockRumble => new(0.45f, 0.65f, 0.11f);
	static float HurtFriction => 200;
	static float DuckAttackCooldown => 0.4f;
	static float HurtDuration => 0.5f;
	static bool DashImpactStun => false;
	static float DashImpactHurtDurationMultiplier => 0.7f;
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

	[I(1, MaxHealth)] public int Health = MaxHealth;
	float deathTimer;
	public Controls Controls => Game.Controls;

	int footstepFrame = -1;
	float duckAttackCooldownTimer;
	bool duckAttack;
	bool attackBlocked;
	// True once this swing has overlapped any target (even one already consumed on an earlier
	// frame); suppresses the wall-block check so landing on an enemy in front of a wall doesn't
	// also register as a blocked attack. Distinct from a per-frame "newly hit" (which drives pogo).
	bool attackHitTarget;
	bool attackDown;
	Pop? attackSwipe;
	float attackWaveStartedAt = float.NegativeInfinity;
	Vector2 attackWaveOrigin;
	float attackWaveDirection;
	readonly HashSet<Actor> attackConsumed = [];
	int plasmaStrikeEffect = -1;
	Light? light;
	SoundInstance? rollSound;
	bool CanPlaySquishSound = false;
	float hurtStateDuration = HurtDuration;
	readonly Sprite? hatSprite;
	Vector2 hatOffset;
	Vector2 hatOffsetVelocity;
	float hatAngle;
	float hatAngularVelocity;

	public Player()
	{
		Sprite = Assets.GetSprite("player_tall");
		hatSprite = Assets.GetSprite("hat");
		Hitbox = new(StandingHitboxRect);
		Mask = Masks.Player;
		Depth = Depths.Player;
		IFrameTime = InvincibleDuration;
		grounded = true;
		Play("sword");
		State = States.Normal;
		PlayerSpecificAudioEvents = true;
	}

	public override void Added()
	{
		base.Added();

		light = CreatePlayerLight(this);
		UpdateLightPosition();

		plasmaStrikeEffect = AcquireParticleEffect("plasma_strike");
	}

	internal static Light CreatePlayerLight(Actor owner)
		=> owner.Game.Create<Light>(owner.Position, it =>
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
			it.SpriteShadowOwner = owner;
		});

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
		// A horizontal wall-pop lands grounded immediately; keep the pop squish
		// rather than stamping the flat landing squish over it.
		if (burrowExitTimer > 0)
		{
			PlayLandingSound();
			CanPlaySquishSound = true;
			return;
		}

		base.OnLanded(previousVelocity);
		PlayLandingSound();
		if (!InLiquid && previousVelocity.Y >= HardLandingSpeed)
			EmitJumpParticles(Position);
		CanPlaySquishSound = true;
	}

	static float HardLandingSpeed => 200f;

	void PlayLandingSound()
	{
		AudioEvent("land", "Type", Factory.SubstanceSoundType(Game.GetSubstanceBelow(this, searchOutward: true)));
	}

	protected override void AfterStateUpdate(Vector2 previousVelocity)
	{
		if (grounded)
		{
			dashNeedsGrounding = false;
			Game.TriggerCrumbleBelow(this);
		}

		UpdateAura(Time.Delta);
		UpdateDashFx(Time.Delta);
		if (grounded && State == States.Normal && DashBursting)
			EmitJumpParticles(Position, DashGroundParticleCount);
		UpdateFootsteps();
		if (duckAttackCooldownTimer > 0)
			duckAttackCooldownTimer = Calc.Approach(duckAttackCooldownTimer, 0, Time.Delta);
	}

	void ClampPositionToRoomOrTransitionRooms()
	{
		if (Health <= 0) return;

		// Resolve a fall before horizontal edges: otherwise continued outward velocity at a
		// bottom corner can cross and re-clamp X every frame, starving this check forever.
		if (Position.Y > Game.Bounds.Bottom + 12)
		{
			if (!Game.Transition(Point2.Down))
				FallOffCliff();
			return;
		}

		if (Position.X > Game.Bounds.Right && !Game.Transition(Point2.Right))
		{
			Position.X = Game.Bounds.Right;
		}
		else if (Position.X < Game.Bounds.Left && !Game.Transition(Point2.Left))
		{
			Position.X = Game.Bounds.Left;
		}
		else if (Position.Y < Game.Bounds.Top)
		{
			if (Game.Transition(Point2.Up))
				Velocity.Y = inLiquid ? MathF.Min(Velocity.Y, SwimStroke) : -150;
			else
				Position.Y = Game.Bounds.Top;
		}
	}

	static int CliffFallDamage => HitDamage;
	// How far to pull the respawn in from the room-entry edge, and how far up/down to hunt
	// for footing as we step inward (a step follows a ≤45° slope by ~1px per pixel).
	static int RespawnInset => 16; // ~2 tiles
	static int RespawnFootingSearch => 6;

	protected override void CrushRecovered() => FallOffCliff();

	// Falling off the bottom of the room (or being crushed) is no longer instant death: take a normal hit and
	// respawn just inside the room from where we entered. If the hit happens to be fatal,
	// ApplyHurt runs the usual death/reload, so we leave the position alone in that case.
	void FallOffCliff()
	{
		Game.HitStun(0.4f);
		if (!Game.TryRespawnPlayerAtLastSafeGrounded(this))
		{
			TryInsetRespawnPoint(Game.RoomEntryPosition, out var inset);
			Position = inset;
		}
		Stop();
		ApplyHurt(-CliffFallDamage);
		if (Health > 0)
		{
			Stop(); // cancel the hurt knockback so we don't get flung back off the ledge
			GrantIFrames(); // brief invincibility so we don't get re-hit on the way back in
		}
	}

	// Walks inward from a room-entry point (toward room center) along the ground, one pixel at
	// a time, stopping at the last spot with fully-supported footing before a gap or wall.
	// False when the entry isn't on footing at all; `result` is then the raw entry point.
	internal bool TryInsetRespawnPoint(Point2 entry, out Point2 result)
	{
		result = entry;
		var inwardX = Math.Sign(Game.Bounds.Center.X - entry.X);
		if (inwardX == 0)
			inwardX = 1;

		if (!TrySnapToFooting(entry, out var best))
			return false;

		var y = best.Y;
		for (var i = 1; i <= RespawnInset; i++)
		{
			if (!TrySnapToFooting(new Point2(entry.X + inwardX * i, y), out var landed))
				break; // a gap or wall ahead — stay on the last good footing
			best = landed;
			y = landed.Y;
		}
		result = best;
		return true;
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
		=> Game.CurrentRoom is { } current && HasSupportedFooting(current, pos);

	// Room-parameterized so it also answers for a room that isn't loaded yet (spawn placement).
	internal static bool HasSupportedFooting(Room room, Point2 pos)
	{
		var box = StandingHitboxRect + pos;
		if (Game.OverlapsRoomCollision(room, box))
			return false; // standing space is blocked

		var groundY = box.Bottom; // first pixel row below the feet
		return Game.OverlapsRoomCollision(room, new RectInt(box.Left, groundY, 1, 1))
			&& Game.OverlapsRoomCollision(room, new RectInt(box.Right - 1, groundY, 1, 1));
	}

#if EDITOR
	// Death-loop rescue (see Game.RespawnPlayerAtLastSafeGrounded): nearest tile-aligned spot in the
	// current room with supported footing. Tile-aligned candidates land exactly on flat tile
	// ground, which is all a rescue needs.
	public bool TryFindSafeFootingNear(Point2 from, out Point2 result)
	{
		result = from;
		if (Game.CurrentRoom is not { } room)
			return false;

		var origin = room.WorldBounds.TopLeft;
		var bestSq = long.MaxValue;
		for (var ty = 1; ty < room.Size.Y; ty++)
			for (var tx = 0; tx < room.Size.X; tx++)
			{
				var candidate = new Point2(
					origin.X + tx * Game.TileSize + Game.TileSize / 2,
					origin.Y + ty * Game.TileSize);
				if (!HasSupportedFooting(candidate))
					continue;
				long dx = candidate.X - from.X, dy = candidate.Y - from.Y;
				var distSq = dx * dx + dy * dy;
				if (distSq < bestSq)
				{
					bestSq = distSq;
					result = candidate;
				}
			}
		return bestSq != long.MaxValue;
	}
#endif

	// Footing has to hold for a moment before it counts: a one-frame touchdown mid-platforming is
	// not somewhere we want to wake up, and the dwell gives telegraphed hazards time to arm.
	static float SafeGroundedDwell => 0.2f;
	float safeGroundedTimer;

	bool IsSettledOnGround()
		=> Health > 0 && grounded && State != States.Burrow && State != States.Climb &&
		 OverlapsFirst(Point2.Down, Masks.Solid) is not (MovingPlatform or Crusher);

	protected override void AfterMovementUpdate(Vector2 previousVelocity)
	{
		safeGroundedTimer = IsSettledOnGround() ? safeGroundedTimer + Time.Delta : 0;
		if (safeGroundedTimer >= SafeGroundedDwell)
			Game.NoteSafeGrounded(Position, StandingHitboxRect + Position);

		UpdateDeathRecovery();
		ClampPositionToRoomOrTransitionRooms();

		var hitRect = Hitbox.Rect + Position;

		if (State == States.Burrow)
		{
			// invulnerable while underground
		}
		else if (Game.OverlapsFirstCombat(hitRect, Masks.Enemy | Masks.Hazard,
			contactDamageOnly: true) is { } hit)
			hit.Hit(this, new(hitRect));

		// TODO: invert this maybe, it probably belongs in FungalGrowthActor with some
		// centralized / efficient "sensor" logic
		var footPosition = new Vector2(Position.X, Position.Y);
		foreach (var actor in Game.Actors)
			if (actor is FungalGrowthActor fungus && fungus.GetVisualBounds().Contains(footPosition))
				fungus.OnPlayerStep(footPosition);

		UpdateCarriedPosition(previousVelocity);
		// ShowCarryDebug();
		UpdateHat(previousVelocity);
		UpdateShield(Time.Delta);
	}

	void UpdateHat(Vector2 previousVelocity)
	{
		var velocityDelta = Vector2.Clamp(Velocity - previousVelocity, new(-HatMaxVelocityDelta), new(HatMaxVelocityDelta));
		hatOffsetVelocity -= velocityDelta * HatMotionResponse;
		hatAngularVelocity -= velocityDelta.X * HatAngularMotionResponse;

		var dt = Math.Min(Time.Delta, 1f / 30f);
		Spring.Step(ref hatOffset, ref hatOffsetVelocity, Vector2.Zero, HatSpring, HatDamping, dt);
		Spring.Step(ref hatAngle, ref hatAngularVelocity, 0, HatAngularSpring, HatAngularDamping, dt);

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
		}

	}

	protected override PawnAbilities Abilities => PawnAbilities.Burrow;
	protected override Bud? TryTakeBurrowPassenger()
		=> Game.GetFirst<Bud>() is { } bud && bud.TryEnterPlayerBurrow(this) ? bud : null;
	protected override bool TryToggleBurrowRoomInverse() => Game.ToggleBurrowRoomInverse(Position);
	protected override bool TryCatchBurrowPopCarryable() => TryCatchCarryable();

	protected override bool AllowDuckingInState() => State == States.Attack && duckAttack;

	protected override bool TryStartPawnAction(int moveX)
	{
		// Attack flows through PawnInput (recorded/replayable). BlinkStrike/Saw still read the
		// device directly, but both are disabled bindings — route them through PawnInput too if
		// ever re-enabled.
		if (carried == null && Controls.BlinkStrike.ConsumePress())
		{
			blinkStrikeTarget = FindBlinkStrikeTarget();
			if (blinkStrikeTarget != null)
			{
				duckAttack = false;
				attackDown = false;
				attackBlocked = false;
				blinkStrikeResolved = false;
				State = States.BlinkStrike;
				Play(GetAttackAnimation(), false);
				return true;
			}
		}
		else if (input.AttackPressed)
		{
			if (carried != null)
			{
				ThrowCarried();
				return true;
			}

			if (!grounded && moveX != 0)
				Facing = moveX;

			if (ducking && duckAttackCooldownTimer <= 0)
			{
				StartAttack(duck: true);
				return true;
			}
			else if (!ducking)
			{
				StartAttack(duck: false);
				return true;
			}
		}
		else if (carried == null && Controls.Saw.ConsumePress())
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

	void StartAttack(bool duck)
	{
		duckAttack = duck;
		if (duck) {
			duckAttackCooldownTimer = DuckAttackCooldown;
		}
		attackConsumed.Clear();
		attackBlocked = false;
		attackHitTarget = false;
		attackSwipe = null;
		var brakeFromDash = grounded && dashRunArmed;
		State = States.Attack;
		attackDown = !grounded && input.MoveY > 0;
		attackUp = !attackDown && input.MoveY < 0;
		if (brakeFromDash)
			BeginDashCancelBrake();
		else if (grounded)
		{
			// @PlayerAttackDoesntStop
			//StopX();
		}
	}

	protected override void OnPawnJump()
	{
		if (InLiquid)
			AudioEvent("player/swim");
		else
			AudioEvent("jump");
	}

	protected override void OnDashImpact()
	{
		if (DashImpactStun)
			ApplyHurt(0,
				bounceMultiplier: 0.5f,
				hurtDurationMultiplier: DashImpactHurtDurationMultiplier,
				glitch: false);
	}

	protected override bool TryRollCancel()
	{
		if (input.AttackPressed && duckAttackCooldownTimer <= 0)
		{
			duckAttack = true;
			attackDown = false;
			attackConsumed.Clear();
			attackBlocked = false;
			attackHitTarget = false;
			attackSwipe = null;
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

	float ATTACK_WINDUP
	{
		get
		{
			if (Sprite == null) return 0;
			var frameStart = Sprite.GetAnimation("attack")?.FrameStart ?? 0;
			return Sprite.Frames[frameStart].Duration;
		}
	}

	void UpdateFootsteps()
	{
		if (State != States.Normal || !grounded || !IsPlaying("walk") && !IsPlaying("run") && !IsPlaying("run_start"))
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
			AudioEvent("footstep", "Type", Factory.SubstanceSoundType(Game.GetSubstanceBelow(this)));
		}
	}

	bool attackUp = false;

	public void AttackState()
	{
		if (duckAttack)
		{
			ducking = true;
		}

		var moveX = GetHorizontalMovementInput(out var movementInput);
		UpdateHorizontalMovement(moveX, movementInput);

		Play(GetAttackAnimation(), false);

		var hitbox = GetAttackHitbox(duckAttack);
		if (state.Time < 0.2f && !attackBlocked)
			AttackThisFrame(hitbox, canPogo: attackDown);
		if (state.JustCrossedTime(0.001f) && !attackBlocked)
		{
			attackWaveStartedAt = Time.SecondsF;
			attackWaveOrigin = Position + Hitbox.Rect.Center;
			attackWaveDirection = attackDown ? MathF.PI * 0.5f :
				attackUp ? -MathF.PI * 0.5f : Facing < 0 ? MathF.PI : 0f;
			AudioEvent(duckAttack ? "sword_crouched" : "sword");
			var position = Position + hitbox.Center;

			// because we actually try to hit over several frames, try to adjust based on our velocity. note this is a purely visual offset and the hitbox is always based on the player's position
			// each frame.
			position += (Point2)(Velocity * new Vector2(14f, 9f) * Time.Delta);

			if (attackDown)
			{
				position.Y -= 8;
				attackSwipe = Game.Create<Pop>(position, pop =>
				{
					pop.SpriteName = "strike_down_sm";
					pop.Facing = Facing;
					pop.Anchor = SpriteAnchor.Pivot;
				});
			}
			else if (attackUp)
			{
				position.Y += 12;
				attackSwipe = Game.Create<Pop>(position, pop =>
				{
					pop.SpriteName = "strike_down_sm";
					pop.Facing = Facing;
					pop.FlipY = true;
					pop.Anchor = SpriteAnchor.Pivot;
				});
			}
			else if (!duckAttack)
			{
				position.X -= Facing < 0 ? 17 : 17;
				// EmitPlasmaStrike(duckingAttack: false);
				attackSwipe = Game.Create<Pop>(position, pop =>
				{
					pop.SpriteName = "strike_right_sm";
					pop.Facing = Facing;
					pop.Anchor = SpriteAnchor.Pivot;
				});
			}
			else
				EmitPlasmaStrike(duckingAttack: duckAttack, attackUp: attackUp);
		}

		var attackFinished = attackBlocked
			? IsFinishedPlaying(GetAttackAnimation())
			: state.Time >= Animation.Duration;

		if (attackFinished)
		{
			attackConsumed.Clear();
			attackBlocked = false;
			attackHitTarget = false;
			attackDown = false;
			attackUp = false;
			attackSwipe = null;
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

	public void PerturbNeuralTexture(NeuralTextureActor.PerturbContext context)
	{
		var elapsed = Time.SecondsF - attackWaveStartedAt;
		var previousElapsed = context.PreviousStepTime - attackWaveStartedAt;
		if (elapsed < 0 || previousElapsed >= AttackWaveDuration || AttackWaveDuration <= 0)
			return;

		var progress = Math.Clamp(elapsed / AttackWaveDuration, 0f, 1f);
		var previousProgress = Math.Clamp(previousElapsed / AttackWaveDuration, 0f, 1f);
		var radius = Calc.Lerp(AttackWaveStartRadius, AttackWaveMaxRadius, progress);
		var previousRadius = Calc.Lerp(AttackWaveStartRadius, AttackWaveMaxRadius, previousProgress);
		// Sample the fade halfway through the sweep, including the final segment past expiry.
		var fade = 1f - (previousProgress + progress) * 0.5f;
		var strength = AttackWaveStrength * MathF.Pow(fade, AttackWaveStrengthFalloff);
		var push = AttackWavePush * MathF.Pow(fade, AttackWavePushFalloff) / context.Texture.StateScale;
		NeuralTextureRippleActor.PerturbRing(context, attackWaveOrigin, radius, AttackWaveMaxRadius,
			strength, AttackWaveThickness, stampCount: 12, fadeWithRadius: false,
			arcAngle: AttackWaveArcAngle, direction: attackWaveDirection, previousRadius: previousRadius, push: push);
	}

	void AttackThisFrame(RectInt it, bool canPogo)
	{
		if (Facing == Signs.Negative)
			it.X = -(it.X + it.Width);

		var hitSomething = AttackThisHitBoxThisFrame(it + Position);

		if (hitSomething && canPogo)
		{
			Velocity.Y = -125f;
			burrowNeedsGrounding = false;
			burrowTimer = 0f;
		}
	}

	bool AttackThisHitBoxThisFrame(RectInt it) {
		Game.AddCollisionRectForThisFrame(it);

		var blockedByShield = Game.IsAttackBlocked(this, it, checkOnlyForStrongActorBlock: true);
		if (blockedByShield)
			BlockAttack();

		var attackHitActor = false;
		if (!blockedByShield)
		{
			var hitCount = Game.DamageActors(this, it, attackConsumed, out var overlappedTarget);
			attackHitActor = hitCount > 0; // newly hit this frame → drives the pogo
			if (overlappedTarget)
				attackHitTarget = true; // any overlap this swing → don't also count a wall as a block
		}

		if (!attackHitTarget && !attackBlocked && (duckAttack || state.Time >= ATTACK_WINDUP) && Game.IsAttackBlocked(this, it))
			BlockAttack();

		if (!attackBlocked)
		{
			Game.BreakTiles(it);
			Game.HitVisualActors(it, attackConsumed);
		}

		return attackHitActor;
	}

	void BlockAttack()
	{
		if (attackBlocked)
			return;
		attackBlocked = true;
		if (attackSwipe != null && Game.Actors.Contains(attackSwipe) && !Game.destroying.Contains(attackSwipe))
		{
			attackSwipe.Visible = false;
			Game.Destroy(attackSwipe);
		}
		attackSwipe = null;
		Play(GetAttackAnimation(), false);
		AudioEvent("player/sword_block");
		Rumble(AttackBlockRumble);
	}

	RectInt GetAttackHitbox(bool duckingAttack)
	{

		RectInt _GetAttackHitbox(bool duckingAttack)
		{
			if (duckingAttack)   return new(3, -6, 13, 6);
			else if (attackDown) return new(-19, 0, 39, 14);
			else if (attackUp)   return new(-12, -27, 24, 12);
			                     return new(6, -19, 20, 22);
		}


		var velocityAddition = new Point2(); //(Point2)(Velocity * Time.Delta * 14.0f);
		return _GetAttackHitbox(duckingAttack) + velocityAddition;
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

		else if (attackDown)
			return "attack_down";

		else if (attackUp)
			return "attack_up";

		return attackBlocked ? "attack_block" : "attack";
	}

	public void HurtState()
	{
		if (state.Time <= 0 && Health <= 0)
		{
			foreach (var actor in Game.Actors)
				if (actor != this && actor != light)
					Game.Destroy(actor);
			Game.Shake(0.4f);
		}

		Velocity.X = Calc.Approach(Velocity.X, 0, HurtFriction * Time.Delta);

		if (state.Time >= hurtStateDuration && Health > 0)
			State = States.Normal;
	}

	// Death is timed on the player rather than on HurtState, which only runs while we're in
	// States.Hurt. A save-point warp, a cutscene, or a room shift taken during the death
	// animation puts us back in another state, and the respawn would then never fire: we'd
	// keep playing at zero health, where ClampPositionToRoomOrTransitionRooms bails out and
	// room edges stop both transitioning and blocking.
	void UpdateDeathRecovery()
	{
		if (Health > 0)
		{
			deathTimer = 0;
			return;
		}

		deathTimer += Time.Delta;
		const float DeathDuration = 2.2f;
		if (deathTimer >= DeathDuration)
		{
			deathTimer = 0;
			Game.RespawnPlayerAtLastSafeGrounded(restartMusic: true);
		}
	}

	internal override void OnWasHit(HitInfo hitInfo)
	{
		var by = hitInfo.By;
		int healthDelta = -by.DamageValue;
		if (!Game.Manager.GodMode)
			ApplyHurt(healthDelta, fromActor: by);
	}

	void ApplyHurt(int healthDelta = -HitDamage, Actor? fromActor = null, float bounceMultiplier = 1,
		float hurtDurationMultiplier = 1, bool glitch = true)
	{
		if (Game.Simulating) {
			return;
		}

		DropCarriedFromHurt();
		Game.HitStun(0.1f);
		Game.Shake(0.1f);
		if (glitch)
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
		Velocity = new Vector2(signDirection * 100, -80) * bounceMultiplier;
		hurtStateDuration = HurtDuration * hurtDurationMultiplier;
		StopAndReleaseSound(ref burrowSound);
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

	internal bool HurtFromEnvironment(Actor source)
	{
		if (Health <= 0 || Game.Manager.GodMode || Game.Simulating || HasIFrames)
			return false;
		GrantIFrames();
		ApplyHurt(-HitDamage, source, bounceMultiplier: 0, hurtDurationMultiplier: 0.5f);
		return true;
	}

	// Lava and the like drain health while touched, with none of a hit's stun, knockback or iframes:
	// it reads as heat, not a blow, and you can still steer out. Returns the whole points drained.
	internal int Burn(float health, Actor source)
	{
		if (Health <= 0 || Game.Manager.GodMode || Game.Simulating)
			return 0;
		BurnedAt = Time.SecondsF;
		burnDrain += health;
		var drained = (int)burnDrain;
		burnDrain -= drained;
		if (drained >= Health)
			ApplyHurt(-drained, source, bounceMultiplier: 0); // the usual death
		else
		{
			Health -= drained;
			Game.Shake(0.1f);
		}
		return drained;
	}

	// When Burn last drained us, on the game clock: the HUD glows hot for a moment after.
	internal float BurnedAt = float.NegativeInfinity;
	float burnDrain;

	internal void ApplyNeuralBuoyancy(float acceleration)
	{
		if (State is States.Normal or States.Roll && Velocity.Y > 0)
			Velocity.Y = Calc.Approach(Velocity.Y, 0, acceleration * Time.Delta);
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
		if (SpriteBlinkHidden || hatSprite == null || hatSprite.Frames.Count == 0 || CurrentSpriteFrame is not { } playerFrame ||
			Sprite?.TryGetSocket("hat", AnimationFrameIndex, out var socket) != true)
			return;

		var hatSpriteOrigin = hatSprite.ResolveOrigin(SpriteAnchor.BottomCenter);
		var socketPosition = socket.Position + new Vector2(1f, 2f);
		var playerScale = new Vector2((int)Facing * Squish.X, Squish.Y);
		var skewTransform = SpriteSkewMatrix(playerFrame);
		var rootPosition = Vector2.Transform((Shift + socketPosition - Origin) * playerScale, skewTransform);

		Vector2 position;
		float rotation;
		if (PerfToggles.HatPhysics)
		{
			position = rootPosition + (PerfToggles.HatPhysics ? hatOffset : new());
			var normal = Vector2.TransformNormal(socket.Normal * playerScale, skewTransform);
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
		light.Intensity = Game.CurrentRoom?.PlayerLightEnabled == false ? 0 : BaseLightIntensity + (BurrowLightIntensity - BaseLightIntensity) * burrowLightBlend;
		light.LightColor = Color.Lerp(BaseLightColor, BurrowLightColor, burrowLightBlend);
	}

	public override void Destroyed()
	{
		base.Destroyed();
		StopRollSound(true);
		if (light != null)
		{
			Game.Destroy(light);
			light = null;
		}
		ReleaseParticleEffect(ref plasmaStrikeEffect);
		ReleaseDashFx();
		ReleaseShield();
	}
}
