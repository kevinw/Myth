using System.Numerics;
using Foster.Framework;

namespace Myth;

// Burrow-specific logic
public abstract partial class Pawn
{
	static float BurrowDuration => 1.133f;
	static float BurrowUpgradeSeconds => 0.5f; // each BurrowUpgrade pickup adds this much burrow time

	// Max burrow time, derived from the collected BurrowUpgrade pickups (no summed save state).
	internal float MaxBurrowDuration => BurrowDurationFor(Game);
	internal static float BurrowDurationFor(Game game) => BurrowDuration + game.PickupCount(PickupKind.BurrowUpgrade) * BurrowUpgradeSeconds;

	// HUD readouts for the stamina bar: capacity drives its width, while fill stays depleted and
	// dimmed after a burrow until the existing CanBurrow gate says the ability has re-armed.
	internal float BurrowCapacitySeconds => MaxBurrowDuration;
	internal float BurrowFill => burrowHudFill;
	internal float BurrowBarOpacity => burrowHudSpent ? 0.5f : 1f;
	internal static float BurrowMaxSpeed => 150f;
	internal static float DashBurrowSpeedMultiplier => 1.3f;
	internal static float BurrowAccel => 500f;            // ease up to speed instead of snapping
	internal static float BurrowFriction => 600f;         // glide to a stop when the stick is released
	static float BurrowReverseFlipSpeed => 8f;   // brake below this before flipping heading on a reversal
	static float BurrowJunctionSeekDistance => 8f;
	static int BurrowTorchFlickerRadius => 4;
	static float BurrowSwitchMagnetDistance => 7f;
	static float BurrowSwitchMagnetSpeed => 42f;
	internal static float BurrowClimbSpeedMultiplier => 0.59f; // straight-up wall climb is half speed; 45° lands in between
													 // The burrow pop is meant to read as stronger than a full held jump: a bigger launch
													 // velocity, plus a longer reduced-gravity float window (jumpTimer) than a normal jump.
	static float BurrowPopForce => JumpForce * 1.18f;
	protected static float BurrowPopTime => JumpTime * 0.9f;
	protected static float BurrowWallSlideLaunch => JumpForce * 1.2f; // strong upward velocity when a wall climb pops into a slide
	const int BurrowSubmergeDepth = 4;
	const int BurrowJumpthruSubmergeDepth = 0; // thin one-way platforms: ride on top, don't sink through
	internal const int BurrowAttachProbe = 8;
	const float BurrowGlowArc = 15f;       // contour length traced in each direction (~30px total)
	static float BurrowGlowWeight => 1.5f;
	static float BurrowGlowCycleSpeed => 0.6f; // rainbow hue cycles per second
	static float BurrowGlowHueScale => 0.03f;  // hue shift per world px along the line
	static float BudBurrowDotRadius => 2.25f;
	static int BurrowIntervals => 3;           // burrow time is split into this many countdown intervals
	static float BurrowPulseTime => 0.22f;     // how long the line flashes brighter at each interval boundary
	static float BurrowRumblePulseStep => 0.0125f;
	static float BurrowRumbleDuration => 0.05f;
	static float BurrowStuckPulseSpeed => 3.5f;
	static float BurrowStuckPulseScale => 0.55f;
	static float BurrowMinSpread => 0.4f;      // arc shrinks to this fraction of full by the final interval
	static float BurrowSpreadEaseRate => 1.4f; // per-second ease of the arc toward the current interval's spread
	const float BurrowEnterSquishTime = 0.12f; // sprite stays visible this long to play the squash-in
	const float BurrowSquashNormal = 0.45f;    // peak compression into the surface at the end of the window
	const float BurrowSquashTangent = 1.6f;    // peak spread along the surface at the end of the window
	const float BurrowExitGrace = 0.1f;        // suppress the landing squish briefly so the pop squish survives
	const float BurrowTangentDeadzone = 0.3f;  // |input·tangent| below this counts as orthogonal to the line

	float burrowEnterTimer;
	protected float burrowExitTimer;
	float burrowDuration;
	protected float burrowHudFill = 1;
	protected bool burrowHudSpent;
	protected Bud? burrowPassenger;
	BurrowCursor burrow = new() { Heading = new(1, 0), Toward = Vector2.UnitY };
	Vector2 burrowIntent;
	Vector2 burrowLastIntent;           // last frame's input; a change re-decides the orbit (even without a zero gap)
	bool burrowOrbiting;                // latched movement: started by a tangential press, held through corners, dropped on release
	bool burrowReverseQueued;           // a reversal is pending: brake to a stop, then flip heading
	protected Vector2 burrowExitOutward;          // away-from-surface dir at the last pop, biases the unstick nudge
	float burrowMoveRemainder;          // sub-pixel accumulator for boundary advancement
	float burrowSpeed;                  // current travel speed; eases via accel/friction
	bool burrowSealed;                  // a crusher closed over us this burrow (see UpdateBurrowSeam)
	bool burrowOnJumpthru;              // current surface is a one-way platform (ride on top, no submerge)
	Point2 burrowRoomTransitionDirection;
	readonly List<Vector2> glowPoints = [];
	readonly List<Vector2> burrowLineGlowBack = [];
	Point2 burrowGlowAnchor;
	protected int burrowSparkEffect = -1;         // particle effect slot for the digging sparks
	float burrowSparkAccumulator;
	const float BurrowSparkRate = 160f; // sparks/sec at full burrow speed
	protected SoundInstance? burrowSound;

	float burrowGlowExitTimer; // current visual burrow glow exit timer
	static float BurrowGlowExitTime => 0.13f; // how long to render the line after you pop out of burrowing

	int burrowIntervalIndex;      // which countdown interval we're in (0-based); a change fires a pulse + tick
	float burrowPulseTimer;       // counts down after each interval boundary; flashes the line brighter
	float burrowSpreadScale = 1f; // eased multiplier on the glow arc length; shrinks as intervals elapse

	float burrowLeapCoyote;               // time left to still pop UP after rounding a downward corner
	Vector2 burrowLeapExitDir;            // remembered outward dir (from riding a top surface) for that leap
	float burrowLeapTravelX;              // remembered horizontal glide so the coyote leap keeps its arc
	static float BurrowLeapCoyoteTime => 0.12f;
	internal float BurrowSpeed => burrowSpeed;
	internal Vector2 BurrowTravelHeading => burrow.Travel;
	internal Actor? BurrowSeam => State == States.Burrow ? burrow.Seam : null;
	internal bool FollowsBurrowSeam => burrow.SeamFollow;
	internal bool AtBurrowLineEndpoint(BurrowLine line, int pointIndex)
		=> State == States.Burrow && burrow.Line == line &&
			Vector2.DistanceSquared(Position, line.WorldPoint(pointIndex)) <= 0.25f;

	// Only the player drags Bud along and flips the room's plane, and only the player has hands
	// to catch a Carryable on the way out; Bud burrows without any of it.
	protected virtual Bud? TryTakeBurrowPassenger() => null;
	protected virtual bool TryToggleBurrowRoomInverse() => false;
	protected virtual bool TryCatchBurrowPopCarryable() => false;

	// Landing with jump held dives straight into a burrow, ahead of the normal landing.
	protected bool TryBurrowOnLanding(Vector2 previousVelocity)
		=> input.JumpDown && !burrowNeedsGrounding && TryStartBurrow(previousVelocity);

	protected bool TryStartBurrow(Vector2 previousVelocity)
		=> CanBurrow() && TryStartBurrow(previousVelocity, Vector2.UnitY, sealedBy: null);

	// `sealedBy`: a crusher pressing us into the surface toward `toward`. The snap looks past it, and the burrow
	// starts already sealed in the seam.
	bool TryStartBurrow(Vector2 previousVelocity, Vector2 toward, Actor? sealedBy)
	{
		Hitbox = new(BurrowHitboxRect);
		burrow.Toward = toward;
		bool snapped;
		using (Game.WalkingSeam(sealedBy, follow: false))
			snapped = Game.SnapToSurface(ref Position, ref burrow.Toward, BurrowAttachProbe);
		if (!snapped)
		{
			if (Game.BurrowTraceEnabled)
				Game.TraceBurrow($"start rejected: no surface near={Position}");
			Hitbox = new(StandingHitboxRect); // no surface in reach; we're staying in Normal
			return false;
		}

		Velocity = Vector2.Zero;
		burrowPassenger = TryTakeBurrowPassenger();
		burrowDuration = MaxBurrowDuration;
		burrowTimer = burrowDuration;
		burrowHudFill = 1;
		burrowHudSpent = false;
		burrowIntervalIndex = 0;
		burrowPulseTimer = BurrowPulseTime; // flash the first interval's start too (the line igniting)
		burrow.Blocked = false;
		burrowSpreadScale = 1f;
		burrowLeapCoyote = 0;
		burrowEnterTimer = BurrowEnterSquishTime;
		burrowMoveRemainder = 0;
		burrowLastIntent = Vector2.Zero; // a tangential press establishes the orbit
		burrowOrbiting = false;
		burrowReverseQueued = false;
		burrow.Line = null;
		burrow.LineExit = null;
		burrow.Seam = sealedBy;
		burrow.SeamFollow = false;
		burrowSealed = sealedBy != null;
		burrowRoomTransitionDirection = Point2.Zero;
		ResetQuantumTunnel();
		// Complementing the room preserves the boundary in place, but moves its solid to the
		// other side. Keep the tangent heading so the burrow continues along the same contour.
		burrow.SolidLeft = false;
		burrow.Heading = new Point2((int)MathF.Round(burrow.Toward.Y), -(int)MathF.Round(burrow.Toward.X));
		if (TryToggleBurrowRoomInverse())
		{
			burrow.Toward = -burrow.Toward;
			burrow.SolidLeft = true;
		}

		// Carry the landing's horizontal momentum into the burrow. Head the way we were
		// moving (reversing heading + handedness keeps the normal pointing into the solid),
		// and seed the speed with the incoming horizontal velocity so a fast run/roll dives
		// straight into a fast burrow instead of starting from a standstill.
		burrowIntent = GetBurrowIntent();
		var entryDirection = previousVelocity.X != 0
			? Math.Sign(previousVelocity.X)
			: input.DashDown ? Math.Sign(horizontalInput) : 0;
		if (entryDirection != 0 && burrow.Heading.X != 0
			&& entryDirection != Math.Sign(burrow.Heading.X))
		{
			burrow.Heading = new Point2(-burrow.Heading.X, -burrow.Heading.Y);
			burrow.SolidLeft = !burrow.SolidLeft;
		}
		var maxSpeed = BurrowMaxSpeed * (input.DashDown ? DashBurrowSpeedMultiplier : 1f);
		burrowSpeed = MathF.Min(MathF.Abs(previousVelocity.X), maxSpeed);

		State = States.Burrow;
		burrowOnJumpthru = Game.SurfaceIsJumpthru(Position, burrow.Toward);
		UpdateBurrowShift();
		UpdateBurrowGlow();

		if (burrowSparkEffect < 0)
			burrowSparkEffect = AcquireParticleEffect("sparks");
		StopAndReleaseSound(ref burrowSound);
		burrowSound = Game.CreateAudioEvent("player/burrow", Position);
		burrowSound?.Start();
		UpdateBurrowAudio();
		AudioEvent("player/burrow_start");
		if (Game.BurrowTraceEnabled)
			Game.TraceBurrow(
				$"start position={Position} heading={burrow.Heading} solidLeft={burrow.SolidLeft} normal={burrow.Toward} speed={burrowSpeed:0.##} passenger={burrowPassenger != null}");
		return true;
	}

	// Screen-axis squish aligned to a surface normal: `normalScale` is applied along
	// the TowardSurface axis, `tangentScale` along the surface. Slopes fall back to
	// whichever axis the normal leans toward (Squish can't rotate).
	static Vector2 SurfaceSquish(Vector2 toward, float normalScale, float tangentScale)
	{
		return new Vector2(tangentScale, normalScale);
	}

	// True once the entry squish has finished and the player is fully submerged.
	protected bool BurrowHidesSprite
	{
		get
		{
			if (PerfToggles.HeadSlide) return false;
			return State == States.Burrow && burrowEnterTimer <= 0;
		}
	}

	// Rebuild the glow from the burrow's own travel state, including the same guide-entry
	// probe movement uses before choosing the next solid edge.
	void UpdateBurrowGlow()
	{
		glowPoints.Clear();
		float arcLength;

		if (State == States.Burrow) // currently burrowing
			arcLength = BurrowGlowArc * (1f - burrowEnterTimer / BurrowEnterSquishTime) * burrowSpreadScale * QuantumGlowScale;
		else if (burrowGlowExitTimer > 0)
		{

			// already exited; drawing the leftover line
			var factor = burrowGlowExitTimer / BurrowGlowExitTime;
			arcLength = BurrowGlowArc * factor;
			// Console.WriteLine($"factor: {factor} arcLength: {arcLength}");
		}
		else
			return;

		if (arcLength <= 0)
			return;

		if (State == States.Burrow)
		{
			burrowGlowAnchor = Position;
		}
		if (burrow.Line != null)
			burrow.Line.Trace(burrow.Segment, burrow.T, burrow.Direction,
				burrowIntent, arcLength, glowPoints, burrowLineGlowBack);
		else
		{
			using var _ = Game.WalkingSeam(burrow.Seam, burrow.SeamFollow);
			Game.TraceBurrowPathFromState(burrowGlowAnchor, burrow.Heading, burrow.SolidLeft, burrow.LineExit,
				arcLength, glowPoints, burrowIntent);
		}
	}

	Vector2 GetBurrowIntent()
	{
		var intent = new Vector2(input.MoveX, input.MoveY);
		if (!input.DashDown)
			return intent;

		dashRunArmed = true;
		horizontalInput = UpdateDashRunDirection(input.MoveX);
		if (intent == Vector2.Zero)
			intent.X = horizontalInput;
		return intent;
	}

	// Split the burrow's lifetime into BurrowIntervals countdown steps. Crossing into a new
	// interval flashes the line (burrowPulseTimer) and fires a tick the player can hear, and
	// steps the line's target spread down — so the arc tightens under the player as the
	// automatic pop-out nears, making the remaining time readable at a glance.
	void UpdateBurrowIntervals()
	{
		var intervalLength = burrowDuration / BurrowIntervals;
		var elapsed = burrowDuration - burrowTimer;
		var interval = Math.Clamp((int)(elapsed / intervalLength), 0, BurrowIntervals - 1);
		if (interval != burrowIntervalIndex)
		{
			burrowIntervalIndex = interval;
			burrowPulseTimer = BurrowPulseTime;
			AudioEvent("player/burrow_tick", "Interval", (float)burrowIntervalIndex);
		}

		// Full spread at the first interval, BurrowMinSpread at the last; ease toward it.
		var t = BurrowIntervals > 1 ? 1f - burrowIntervalIndex / (float)(BurrowIntervals - 1) : 1f;
		var targetSpread = Calc.Lerp(BurrowMinSpread, 1f, t);
		burrowSpreadScale = Calc.Approach(burrowSpreadScale, targetSpread, BurrowSpreadEaseRate * Time.Delta);
	}

	// Burrow-leap coyote: while riding a surface whose pop would fling you upward (normal points
	// down into the solid, i.e. you're on its top), remember it for a brief window — so burrowing
	// off the lip and gliding down the far side of a corner still leaps you up as if you'd popped
	// at the top instead of getting the sideways pop off the vertical face.
	void UpdateBurrowLeapCoyote()
	{
		if (burrow.Line != null)
		{
			burrowLeapCoyote = Calc.Approach(burrowLeapCoyote, 0, Time.Delta);
			return;
		}

		if (burrow.Toward.Y > 0.5f)
		{
			burrowLeapCoyote = BurrowLeapCoyoteTime;
			burrowLeapExitDir = burrow.Toward;
			burrowLeapTravelX = burrow.Heading.X != 0 ? Math.Sign(burrow.Heading.X) * burrowSpeed : 0f;
		}
		else
			burrowLeapCoyote = Calc.Approach(burrowLeapCoyote, 0, Time.Delta);
	}

	void UpdateBurrowShift()
	{
		if (PerfToggles.HeadSlide) return;
		var depth = burrowOnJumpthru ? BurrowJumpthruSubmergeDepth : BurrowSubmergeDepth;
		Shift = State == States.Burrow ? burrow.Toward * depth : new();
	}

	static float BurrowCooldown => 0.25f;
	static float ShortBurrowDampenTime => 0.09f; // burrows briefer than this pop softly, as if holding Down

	// Is the burrow point against the room boundary in the direction we're driving? Only then
	// can a surface or authored line plausibly continue in the adjacent room. Room bounds are
	// half-open [Left, Right) / [Top, Bottom); a small tolerance covers ending a pixel shy.
	const int BurrowRoomEdgeTolerance = 2;
	static int BurrowRoomEntryInset => BurrowRoomEdgeTolerance + 1;
	bool TryBurrowRoomTransition(Vector2 travelHeading)
	{
		var bounds = Game.Bounds;
		var direction = MathF.Abs(travelHeading.X) >= MathF.Abs(travelHeading.Y)
			? new Point2(Math.Sign(travelHeading.X), 0)
			: new Point2(0, Math.Sign(travelHeading.Y));
		if (burrowRoomTransitionDirection != Point2.Zero && direction == -burrowRoomTransitionDirection)
		{
			if (Game.BurrowTraceEnabled)
				Game.TraceBurrow($"reverse room transition guarded position={Position} direction={direction}");
			return false;
		}
		var atEdge = direction.X > 0 ? Position.X >= bounds.Right - BurrowRoomEdgeTolerance
			: direction.X < 0 ? Position.X <= bounds.Left + BurrowRoomEdgeTolerance
			: direction.Y > 0 ? Position.Y >= bounds.Bottom - BurrowRoomEdgeTolerance
			: direction.Y < 0 && Position.Y <= bounds.Top + BurrowRoomEdgeTolerance;
		return this is Player && atEdge && Game.Transition(direction);
	}

	void UpdateBurrowRoomTransitionGuard()
	{
		if (burrowRoomTransitionDirection == Point2.Zero)
			return;

		var bounds = Game.Bounds;
		if (burrowRoomTransitionDirection.X > 0 && Position.X > bounds.Left + BurrowRoomEdgeTolerance ||
			burrowRoomTransitionDirection.X < 0 && Position.X < bounds.Right - BurrowRoomEdgeTolerance ||
			burrowRoomTransitionDirection.Y > 0 && Position.Y > bounds.Top + BurrowRoomEdgeTolerance ||
			burrowRoomTransitionDirection.Y < 0 && Position.Y < bounds.Bottom - BurrowRoomEdgeTolerance)
			burrowRoomTransitionDirection = Point2.Zero;
	}

	protected void BurrowState()
	{
		Velocity = Vector2.Zero;
		var previousPosition = Position;
		UpdateBurrowRoomTransitionGuard();

		// Ramp the squash UP toward peak as the entry window elapses, overriding the
		// base per-frame decay so it intensifies right up until the sprite vanishes.
		if (burrowEnterTimer > 0)
		{
			burrowEnterTimer = Calc.Approach(burrowEnterTimer, 0, Time.Delta);
			var t = 1f - burrowEnterTimer / BurrowEnterSquishTime;
			var peak = SurfaceSquish(burrow.Toward, BurrowSquashNormal, BurrowSquashTangent);
			Squish = Vector2.Lerp(Vector2.One, peak, t);
		}

		// Resolve travel from the world input. Movement is a latched "orbit" that you
		// can only start (or redirect) by pressing ALONG the surface (tangential to the
		// heading). A purely perpendicular input (up/down on a horizontal floor, or
		// left/right on a wall) does nothing. Re-evaluation happens whenever the pressed
		// direction CHANGES — including slamming the opposite direction with no zero gap
		// (right -> left) — not just after a release. While a direction is held the
		// intent is constant, so the heading auto-rotates around corners (hold one way to
		// loop a whole shape) and the perpendicular input you were holding when you turned
		// a corner keeps carrying you. Releasing drops the latch.
		var intent = burrowIntent = GetBurrowIntent();
		var dashBurrowing = input.DashDown;
		var pressed = intent != Vector2.Zero;
		var changed = intent != burrowLastIntent;
		var proj = Vector2.Dot(intent, BurrowTravelHeading);
		var tangential = MathF.Abs(proj) > BurrowTangentDeadzone;

		if (!pressed)
		{
			if (burrowOrbiting && Game.BurrowTraceEnabled)
				Game.TraceBurrow("intent released: stop orbiting");
			burrowOrbiting = false;
		}
		else if (changed && tangential)
		{
			// Pressing opposite the heading queues a reversal (brakes first, below);
			// pressing forward cancels any pending reversal.
			burrowReverseQueued = proj < 0;
			burrowOrbiting = true;
			if (Game.BurrowTraceEnabled)
				Game.TraceBurrow(
					$"intent accepted intent={intent} travel={BurrowTravelHeading} projection={proj:0.##} reverseQueued={burrowReverseQueued}");
		}
		else if (changed && Game.BurrowTraceEnabled)
			Game.TraceBurrow(
				$"intent ignored as non-tangential intent={intent} travel={BurrowTravelHeading} projection={proj:0.##}");
		burrowLastIntent = intent;
		UpdateBurrowSeam(intent);
		if (pressed && !burrowOrbiting && burrowSpeed <= BurrowReverseFlipSpeed && burrow.Line != null &&
			burrow.Line.TryFindJunction(burrow.Segment, burrow.T, intent, BurrowJunctionSeekDistance,
				burrow.Direction, out var seekDirection, out var seekDistance))
		{
			burrow.Direction = seekDirection;
			burrow.LineHeading = burrow.Line.SegmentHeading(burrow.Segment, seekDirection);
			burrowReverseQueued = false;
			burrowOrbiting = true;
			if (Game.BurrowTraceEnabled)
				Game.TraceBurrow(
					$"junction seek intent={intent} direction={seekDirection} distance={seekDistance:0.##} heading={burrow.LineHeading}");
		}

		var move = burrowOrbiting || dashBurrowing;

		// Speed eases in and out (accel + friction) rather than snapping, and climbing slows
		// you down: a straight-up wall heading runs at BurrowClimbSpeedMultiplier, while a 45°
		// slope lands in between on its own (the cardinal walker alternates up/flat steps).
		// Descending counts as flat, so you keep momentum going down. A queued reversal
		// targets zero speed (friction) so a direction switch decelerates before flipping.
		var targetSpeed = move && !burrowReverseQueued ? burrow.CruiseSpeed(dashBurrowing) : 0f;
		if (!burrowReverseQueued && burrow.Line?.TryGetSwitchDistance(
			burrow.Segment, burrow.T, burrow.Direction, out var switchDistance) == true)
		{
			if (switchDistance <= 0.01f)
				targetSpeed = 0;
			else if (switchDistance <= BurrowSwitchMagnetDistance)
				targetSpeed = MathF.Max(targetSpeed, Calc.Lerp(12, BurrowSwitchMagnetSpeed,
					1 - switchDistance / BurrowSwitchMagnetDistance));
		}
		var rate = targetSpeed > burrowSpeed ? BurrowAccel : BurrowFriction;
		burrowSpeed = Calc.Approach(burrowSpeed, targetSpeed, rate * Time.Delta);

		// Once braked nearly to a stop, perform the queued reversal and accelerate the
		// other way next frame.
		if (burrowReverseQueued && burrowSpeed <= BurrowReverseFlipSpeed)
		{
			var oldHeading = BurrowTravelHeading;
			if (burrow.Line != null)
			{
				burrow.Direction *= -1;
				burrow.LineHeading = -burrow.LineHeading;
			}
			else
			{
				burrow.Heading = new(-burrow.Heading.X, -burrow.Heading.Y);
				burrow.SolidLeft = !burrow.SolidLeft;
			}
			burrowReverseQueued = false;
			if (Game.BurrowTraceEnabled)
				Game.TraceBurrow(
					$"reversal applied oldHeading={oldHeading} newHeading={BurrowTravelHeading} onLine={burrow.Line != null}");
		}

		burrowMoveRemainder += burrowSpeed * Time.Delta;
		var pixels = (int)burrowMoveRemainder;
		burrowMoveRemainder -= pixels;
		if (pixels > 0)
			switch (Game.TravelBurrow(ref Position, ref burrow, pixels, intent, this is Player, out var remaining))
			{
				case BurrowTravel.Door:
					burrowOnJumpthru = false;
					burrowMoveRemainder += remaining;
					UpdateBurrowShift();
					UpdateBurrowGlow();
					return;

				case BurrowTravel.Switch:
					burrowSpeed = 0;
					burrowMoveRemainder = 0;
					break;

				case BurrowTravel.LineEnd:
					if (TryBurrowRoomTransition(burrow.LineHeading))
					{
						burrowMoveRemainder += remaining;
						if (Game.BurrowTraceEnabled)
							Game.TraceBurrow(
								$"line crossing room edge id={burrow.Line?.ActorId ?? "?"} position={Position} heading={burrow.LineHeading}");
						return;
					}

					if (Game.AttachBurrow(ref Position, ref burrow, intent))
						break;
					if (CanExitBurrowLine(out var exitDucking))
					{
						if (Game.BurrowTraceEnabled)
							Game.TraceBurrow("line has no surface: popping out");
						ducking = exitDucking;
						PopOutOfBurrow();
						return;
					}
					burrow.Blocked = true;
					burrowSpeed = 0;
					burrowMoveRemainder = 0;
					if (Game.BurrowTraceEnabled)
						Game.TraceBurrow("line endpoint blocked: waiting for a valid exit");
					break;

				case BurrowTravel.DeadEnd:
					if (Game.BurrowTraceEnabled)
						Game.TraceBurrow(
							$"surface dead end position={Position} heading={burrow.Heading} solidLeft={burrow.SolidLeft} moving={move}");
					// A dead end. If we're driving into it and the surface exits the room into an
					// adjacent one, transition and keep burrowing on the far side. Otherwise pop out;
					// if we merely coasted here on friction, kill the glide and stay put.
					if (move && burrow.Seam == null)
					{
						if (TryBurrowRoomTransition(burrow.Heading))
							return;
						PopOutOfBurrow();
						return;
					}
					burrowSpeed = 0;
					burrowMoveRemainder = 0;
					break;
			}
		FlickerTorchesAlongBurrow(previousPosition);

		Game.ReleaseBurrowLineExit(ref burrow, Position);
		burrowOnJumpthru = burrow.Line == null && Game.SurfaceIsJumpthru(Position, burrow.Toward);
		UpdateBurrowLeapCoyote();
		UpdateBurrowShift();
		UpdateBurrowIntervals();
		UpdateQuantumTunnel(intent);
		UpdateBurrowGlow();
		UpdateBurrowAudio();
		UpdateBurrowRumble();
		UpdateBurrowBomb();
		EmitBurrowSparks();
		if (!PerfToggles.HeadSlide) Play("burrow");

		if (input.MoveX != 0)
			Facing = Math.Sign(input.MoveX);

		// Charging a crossing holds the clock: lining one up shouldn't be the thing that ends the burrow.
		if (burrow.Line == null && !quantumArmed)
			burrowTimer = MathF.Max(0, burrowTimer - Time.Delta);
		burrowHudFill = burrowDuration > 0 ? burrowTimer / burrowDuration : 0;
		var wantsExit = burrow.Line == null && burrow.Seam == null && (burrowTimer <= 0 || !input.JumpDown);
		if (burrow.Blocked && (burrow.Line == null || CanExitBurrowLine(out _)))
			burrow.Blocked = false;
		if (wantsExit)
		{
			if (!CanExitBurrowLine(out var exitDucking))
			{
				if (!burrow.Blocked && Game.BurrowTraceEnabled)
					Game.TraceBurrow(
						$"pop blocked inside guide id={burrow.Line?.ActorId ?? "?"} position={Position}");
				burrow.Blocked = true;
				return;
			}

			ducking = exitDucking;
			if (burrowTimer <= 0)
				AudioEvent("player/burrow_tick", "Interval", (float)BurrowIntervals);
			PopOutOfBurrow();
		}
	}

	// A Crusher closing over the burrow point seals us into the seam between it and whatever it hit: the one
	// line where two touching solids read apart. Pressing toward the crusher follows its outline (and rides it
	// home); anything else follows the solid it pinned us to. Leaving the seam is one-way, and we can't pop out
	// of it until a tile of room opens in front of us.
	void UpdateBurrowSeam(Vector2 intent)
	{
		if (burrow.Line != null)
			return;
		if (burrow.Seam == null)
		{
			if (Game.BuryingCrusher(Position, burrow.Toward) is not { } crusher)
				return;
			(burrow.Seam, burrow.SeamFollow, burrowSealed) = (crusher, false, true);
			if (Game.BurrowTraceEnabled)
				Game.TraceBurrow($"sealed in seam position={Position} normal={burrow.Toward}");
		}
		else if (!Game.OverlapsSolid(this, RectInt.Centered((Position - burrow.Toward * (Game.TileSize / 2)).RoundToPoint2(), Game.TileSize, Game.TileSize)))
		{
			burrow.Seam = null;
			if (Game.BurrowTraceEnabled)
				Game.TraceBurrow($"left seam position={Position}");
			return;
		}

		// Only while still sandwiched: once the walk has turned a corner out into air there's one side left.
		var towardCrusher = burrow.SeamFollow ? burrow.Toward : -burrow.Toward;
		if (intent == Vector2.Zero || Vector2.Dot(intent, towardCrusher) > BurrowTangentDeadzone == burrow.SeamFollow ||
			!Game.Buried(Position, burrow.Toward, out _, out _))
			return;
		burrow.SeamFollow = !burrow.SeamFollow;
		burrow.Toward = -burrow.Toward;
		burrow.SolidLeft = !burrow.SolidLeft;
	}

	void FlickerTorchesAlongBurrow(Point2 previousPosition)
	{
		if (this is not Player || Position == previousPosition)
			return;

		var path = RectInt.Between(previousPosition, Position).Inflate(BurrowTorchFlickerRadius);
		foreach (var actor in Game.Actors)
			if (actor is Light light)
				light.FlickerFromBurrow(path);
	}

	bool CanExitBurrowLine(out bool exitDucking)
	{
		exitDucking = false;
		if (burrow.Line == null)
			return true;
		if (!Game.OverlapsSolid(this, StandingHitboxRect + Position))
			return true;
		if (!Game.OverlapsSolid(this, DuckingHitboxRect + Position))
		{
			exitDucking = true;
			return true;
		}
		return false;
	}

	// StopBurrow stop a burrow
	private void PopOutOfBurrow()
	{
		var exitDir = burrow.Toward;
		if (Game.BurrowTraceEnabled)
			Game.TraceBurrow(
				$"pop position={Position} travel={BurrowTravelHeading} normal={exitDir} speed={burrowSpeed:0.##} onLine={burrow.Line?.ActorId ?? "none"}");

		// Burrow-leap coyote: if we just rounded off a top surface (down a corner) within the
		// window, pop upward as if we were still on top instead of getting the sideways pop off
		// the vertical face (see UpdateBurrowLeapCoyote).
		var leapCoyote = burrowLeapCoyote > 0 && exitDir.Y <= 0.5f;
		if (leapCoyote)
			exitDir = burrowLeapExitDir;

		// This is a bit of a hard coded hack: if we're exiting downwards,
		// move the hitbox down so that its top is at the edge of the surface we
		// are exiting from.
		if (burrow.Line == null && exitDir.Y < 0)
		{
			Position = (Point2)(new Vector2(Position.X, Position.Y + StandingHitboxRect.Height));

			// Additional hack: if there's not room, try a crouch.
			if (Game.OverlapsRoomCollision(this, Point2.Zero))
			{
				ducking = true;
			}
		}

		// Popping up underneath a Carryable at rest catches it overhead — the burrow is the
		// lift verb. Resolved before the launch so the catch can soften it (dampened below,
		// like the hold-Down pop), and before AfterMovementUpdate's TryUnstick sees the
		// swollen carry hitbox.
		var caught = exitDir.Y > 0.5f && TryCatchBurrowPopCarryable();

		// Climbing straight up a vertical wall pops into a wall slide instead of launching
		// off it: drop the outward push, keep hugging the wall, and carry strong upward
		// velocity. Feels better and sets up wall-jump / re-burrow chaining off the launch.
		if (burrow.Line == null && MathF.Abs(burrow.Toward.X) > MathF.Abs(burrow.Toward.Y) && burrow.Heading.Y < 0)
		{
			Velocity = new Vector2(0, BurrowWallSlideLaunch);
			Facing = Math.Sign(burrow.Toward.X); // face into the wall so the slide reads
			Squish = SurfaceSquish(exitDir, normalScale: 0.7f, tangentScale: 1.3f);
		}
		else
		{
			// Launch away from whatever surface we were riding, plus carry the burrow's
			// tangential glide momentum (so popping out of a fast rightward burrow keeps
			// that rightward speed).
			var popMagnitude = MathF.Abs(BurrowPopForce);
			var heading = BurrowTravelHeading;
			var travel = heading != Vector2.Zero
				? Vector2.Normalize(heading) * burrowSpeed
				: Vector2.Zero;
			if (leapCoyote)
				travel = new Vector2(burrowLeapTravelX, 0); // recover the pre-corner horizontal arc, drop the downward glide

			// "Cancel" a big popup jump by holding Down on the Dpad. A very short burrow
			// (a quick tap in and out) gets the same soft pop regardless of input — it
			// clearly wasn't a committed launch, so don't fling the player.
			Velocity = -exitDir * popMagnitude + travel;
			var shortBurrow = burrowDuration - burrowTimer < ShortBurrowDampenTime;
			if (Velocity.Y < 50 && (shortBurrow || caught || input.MoveY > 0))
				Velocity.Y *= 0.5f;

			// Burrow glide can exceed normal air speed; let the carried horizontal
			// momentum persist briefly (then normal air clamping reins it in) instead of
			// being snapped to MaxAirSpeed immediately. Reuses the roll-jump carry, whose
			// cap (RollJumpSpeed) matches BurrowMaxSpeed.
			if (MathF.Abs(travel.X) > MaxAirSpeed)
				rollJumpCarryTimer = RollJumpCarryTime;
			Squish = SurfaceSquish(exitDir, normalScale: 1.4f, tangentScale: 0.65f);
		}

		// Coming out from under a crusher that's still lifting away: rise in place, crouched if that's all the
		// room there is, rather than launching into its underside.
		var cramped = burrowSealed && exitDir.Y > 0.5f && Game.OverlapsSolid(this, StandingHitboxRect + Position);
		if (cramped)
		{
			Velocity = Vector2.Zero;
			ducking = true;
		}
		jumpTimer = cramped ? 0 : BurrowPopTime;
		jumpCutImmune = true; // the pop keeps its full float even when jump is released
		State = States.Normal;
		AudioEvent("jump");
		burrowEnterTimer = 0;
		burrowExitTimer = BurrowExitGrace;
		burrowExitOutward = -exitDir; // away from the surface; bias the unstick nudge this way
		burrowTimer = -BurrowCooldown;
		burrowHudSpent = true;
		burrowGlowExitTimer = BurrowGlowExitTime;
		burrowNeedsGrounding = true; // must touch ground again before another burrow can arm
		burrow.Blocked = false;
		burrow.Seam = null;
		burrowRoomTransitionDirection = Point2.Zero;
		ResetQuantumTunnel();
		UpdateBurrowShift();
		StopAndReleaseSound(ref burrowSound);

		burrowPassenger?.BeginPlayerBurrowPop(Position, Velocity, Squish, Facing, BurrowPopTime, burrowExitOutward);
		burrowPassenger = null;

		// emit a final burst up and opposite the direction of leaving
		if (Game.Particles is { } particles)
		{
			float direction = MathF.Atan2(Velocity.X, Velocity.Y);
			particles.Emit(burrowSparkEffect,
				worldPosition: burrowGlowAnchor - exitDir * 2.0f - Vector2.Normalize(BurrowTravelHeading) * 3.0f,
				count: 20,
				directionRadians: direction + MathF.PI / 2.0f,
				speedScale: 2.5f,
				spreadRadians: 19.0f * Calc.DegToRad);
		}
		UpdateBurrowGlow();
		AudioEvent("player/burrow_end");
	}

	// Called by Game right after a room swap that began mid-burrow. Rooms are contiguous in world
	// space: first reconnect an authored line at the shared edge, otherwise re-anchor to a surface.
	public void ResolveBurrowRoomTransition(Point2 direction)
	{
		if (State != States.Burrow)
			return;

		ResetQuantumTunnel();

		var incomingHeading = BurrowTravelHeading;
		var previousLine = burrow.Line;
		var bounds = Game.Bounds;
		var arrival = Position + direction * BurrowRoomEntryInset;
		if (direction.X > 0) arrival.X = Math.Max(arrival.X, bounds.Left + BurrowRoomEntryInset);
		if (direction.X < 0) arrival.X = Math.Min(arrival.X, bounds.Right - 1 - BurrowRoomEntryInset);
		if (direction.Y > 0) arrival.Y = Math.Max(arrival.Y, bounds.Top + BurrowRoomEntryInset);
		if (direction.Y < 0) arrival.Y = Math.Min(arrival.Y, bounds.Bottom - 1 - BurrowRoomEntryInset);
		arrival = arrival.Clamp(bounds.TopLeft, bounds.BottomRight - Point2.One);
		burrowRoomTransitionDirection = direction;

		if (previousLine != null && Game.TryAttachBurrowLineAcrossRoom(arrival, incomingHeading, BurrowAttachProbe,
			out var nextLine, out burrow.Segment, out burrow.T, out burrow.Direction,
			out var entryPoint, out burrow.LineHeading))
		{
			burrow.Line = nextLine;
			burrow.LineExit = null;
			Position = (Point2)entryPoint;
			burrow.Blocked = false;
			burrowOnJumpthru = false;
			UpdateBurrowShift();
			UpdateBurrowGlow();
			if (Game.BurrowTraceEnabled)
				Game.TraceBurrow(
					$"line room handoff {previousLine.ActorId ?? "?"} -> {burrow.Line?.ActorId ?? "?"} position={Position} heading={burrow.LineHeading}");
			return;
		}

		burrow.Line = null;
		burrow.LineExit = null;
		if (Game.TryAttachBurrowSurface(arrival, incomingHeading, burrowIntent, BurrowAttachProbe,
			out var surfacePosition, out burrow.Heading, out burrow.SolidLeft, out burrow.Toward) &&
			bounds.Contains(surfacePosition))
		{
			Position = surfacePosition;
			burrowOnJumpthru = Game.SurfaceIsJumpthru(Position, burrow.Toward);
			UpdateBurrowShift();
			UpdateBurrowGlow();
			if (Game.BurrowTraceEnabled)
				Game.TraceBurrow(
					$"surface room handoff position={Position} heading={burrow.Heading} solidLeft={burrow.SolidLeft} normal={burrow.Toward}");
		}
		else
		{
			// The route really ended. Pop inward from a point safely inside the destination room.
			Position = arrival;
			burrow.Heading = direction;
			burrow.Toward = -(Vector2)direction;
			if (Game.BurrowTraceEnabled)
				Game.TraceBurrow($"room handoff has no line or surface; popping inward at={Position}");
			PopOutOfBurrow();
		}
	}

	// Re-arms the ability after a pop: the cooldown counts back up, and touching ground clears
	// the "must land first" latch. Without this a pawn burrows exactly once.
	protected void UpdateBurrowRecharge()
	{
		if (grounded)
			burrowNeedsGrounding = false;
		if (burrowExitTimer > 0)
			burrowExitTimer = Calc.Approach(burrowExitTimer, 0, Time.Delta);
		if (burrowTimer < 0)
		{
			burrowTimer += Time.Delta;
			if (burrowTimer > 0) burrowTimer = 0;
		}
		if (burrowHudSpent && CanBurrow())
		{
			burrowHudFill = 1;
			burrowHudSpent = false;
		}
	}

	// The ground a pawn has burrowed through is churned: its path sorts vertically behind it,
	// harder the faster it went, and settles back once it has passed.
	// Streaks run out of the surface (burrow.Toward points into it) rather than always downward,
	// so the churn belongs to the tunnel wall instead of reading as a screen-space filter.
	static PixelSortDirection ChurnDirection(Vector2 toward)
		=> MathF.Abs(toward.X) > MathF.Abs(toward.Y)
			? toward.X > 0 ? PixelSortDirection.Left : PixelSortDirection.Right
			: toward.Y > 0 ? PixelSortDirection.Up : PixelSortDirection.Down;

	readonly PixelSortTrail burrowChurn = new();

	public override void StirPixelSort(PixelSortCanvas canvas) =>
		burrowChurn.Stir(canvas, life: 0.8f, width: 6f, new(PixelSortLayer.Frame, default, 1, Reach: 17f, RelaxRate: 40, Feather: 0.55f));

	protected void UpdateBurrowAlways()
	{
		burrowChurn.Advance(Time.Delta);
		if (State == States.Burrow)
		{
			const float ChurnStrength = 0.6f;
			burrowChurn.Record(Position, ChurnDirection(burrow.Toward), strength: Math.Clamp(burrowSpeed / BurrowMaxSpeed, 0.35f, 1) * ChurnStrength, spacing: 2);
		}
		if (burrowGlowExitTimer > 0)
			UpdateBurrowGlow(); // called here also so we update outside of the burrow state
		burrowGlowExitTimer = Calc.Approach(burrowGlowExitTimer, 0, Time.Delta);
		if (State != States.Burrow && burrowGlowExitTimer <= 0)
			burrow.Line = null;
		burrowPulseTimer = Calc.Approach(burrowPulseTimer, 0, Time.Delta);
		quantumFlashTimer = Calc.Approach(quantumFlashTimer, 0, Time.Delta);
	}

	// Drive the burrow loop from the motion every frame so it can breathe instead of
	// playing a flat tone. Keep the event centered on the listener as we travel (mirrors
	// the roll sound) — otherwise it stays pinned to the entry point and pans away — and
	// expose Speed/Climb so the FMOD event can swell/brighten with glide speed and add
	// tension while scaling a wall. The parameters are no-ops until authored in Studio.
	private void UpdateBurrowAudio()
	{
		if (burrowSound == null)
			return;

		Game.SetAudioEventPosition(burrowSound, Position, Game.GetFirst(Masks.Player)?.Position);

		var speed01 = BurrowMaxSpeed > 0 ? burrowSpeed / BurrowMaxSpeed : 0f;
		var climb = MathF.Max(0, -BurrowTravelHeading.Y);
		Game.SetAudioEventParameter(burrowSound, "Speed", speed01);
		Game.SetAudioEventParameter(burrowSound, "Climb", climb);
	}

	void UpdateBurrowBomb()
	{
		if (input.AttackPressed)
		{
			// TODO: limit to 3 per burrow or something
			Game.Create<BurrowBomb>(Position);
		}
	}

	// A quiet low-motor bed sells contact with the ground. The high motor shares the visual/audio
	// countdown pulse, making the three intervals readable by feel without becoming a metronome.
	void UpdateBurrowRumble()
	{
		// Bail before the per-frame maths, not just at the Rumble call, since this runs all burrow.
		if (RumbleControllerIndex is null)
			return;

		const float BurrowRumbleIdle = 0.025f;
		const float BurrowRumbleSpeed = 0.04f;
		const float BurrowRumbleClimb = 0.015f;
		const float BurrowRumblePulse = 0.025f;
		
		var speed = Math.Clamp(burrowSpeed / BurrowMaxSpeed, 0, 1);
		var climb = MathF.Max(0, -BurrowTravelHeading.Y);
		var low = BurrowRumbleIdle + speed * BurrowRumbleSpeed + climb * BurrowRumbleClimb;
		var pulse = BurrowPulseTime > 0
			? burrowPulseTimer / BurrowPulseTime *
				(BurrowRumblePulse + burrowIntervalIndex * BurrowRumblePulseStep)
			: 0;
		pulse += quantumCharge * quantumCharge * QuantumRumble;
		Rumble(low, pulse, BurrowRumbleDuration);
	}

	static Rng rng = new Rng();
	// Spray sparks out of the surface at the burrow point, scaled by glide speed.
	private void EmitBurrowSparks()
	{
		if (burrowSparkEffect < 0 || Game.Particles is not { } particles)
			return;

		var speed01 = BurrowMaxSpeed > 0 ? burrowSpeed / BurrowMaxSpeed : 0f;
		if (speed01 <= 0.05f)
			return;

		burrowSparkAccumulator += BurrowSparkRate * speed01 * Time.Delta;
		var count = (int)burrowSparkAccumulator;
		burrowSparkAccumulator -= count;
		if (count <= 0)
			return;

		// Aim out of the solid (burrow.Toward points into it); fall back to straight up.
		var outward = -burrow.Toward;
		outward = outward.LengthSquared() > 0.0001f ? Vector2.Normalize(outward) : new Vector2(0, -1);
		Vector2 origin;
		if (burrow.Line == null) {
			// Offset a bit by outward so the sparks appear a bit above the surface.
			origin = (Vector2)Position + outward * 1.0f;
		} else {
			// ..But when in a line, jitter the position a bit and don't offset by outward, so it looks more like we're following the line.
			const float A = 0.6f;
			origin = (Vector2)Position + new Vector2(rng.Float(-A, A), rng.Float(-A, A));
		}
		if (outward.X != 0 && outward.Y == 0)
			origin = (Vector2)Position;
		var direction = MathF.Atan2(outward.Y, outward.X);
		particles.Emit(burrowSparkEffect, origin, count, direction, 0.4f + 0.6f * speed01);
	}

	protected void RenderBurrowLine(Batcher batcher)
	{
		if (!PerfToggles.BurrowParticles) return;
		if (State != States.Burrow && burrowGlowExitTimer <= 0)
			return; // the near line can be empty mid-tunnel, when the far one has taken it over

		// The traced points lie exactly on the solid/air boundary; draw them directly.
		// The emissive pass pre-pushes a matrix at our Position, so draw in local space.
		var origin = (Vector2)Position;
		DrawGlowLine(batcher, glowPoints, burrowGlowAnchor, origin, hueShift: 0);
		DrawGlowLine(batcher, quantumGlowPoints, quantumExit, origin, QuantumHueShift);
		RenderQuantumFlash(batcher, origin);

		if (State == States.Burrow && burrow.Line?.TryGetSwitchNear(Position, 0.5f, out var switchPointIndex, out var switchPoint) == true)
		{
			var fill = burrow.Line.SwitchFill(switchPointIndex);
			var span = GlowSpan(glowPoints, burrowGlowAnchor);
			var previous = switchPoint;
			for (var i = 1; i <= 28; i++)
			{
				var t = i / 28f;
				var point = switchPoint + Calc.AngleToVector(t * MathF.Tau * (2.5f + fill) - Time.SecondsF * (3 + fill * 4)) * t * Calc.Lerp(4, 9, fill);
				batcher.Line(previous - origin, point - origin, BurrowGlowWeight,
					GlowColorAt(previous, burrowGlowAnchor, span, 0), GlowColorAt(point, burrowGlowAnchor, span, 0));
				previous = point;
			}
		}

		if (burrowPassenger != null)
			batcher.Circle(burrowGlowAnchor - origin, BudBurrowDotRadius, 12, Color.White, Color.Transparent);
	}

	// Farthest traced point sets the fade length, so a glow always fades to nothing at its
	// current (possibly shortened) ends rather than at the full-arc distance.
	static float GlowSpan(List<Vector2> points, Vector2 anchor)
	{
		var span = 0f;
		foreach (var point in points)
			span = MathF.Max(span, Vector2.Distance(point, anchor));
		return span < 0.001f ? BurrowGlowArc : span;
	}

	void DrawGlowLine(Batcher batcher, List<Vector2> points, Vector2 anchor, Vector2 origin, float hueShift)
	{
		if (points.Count < 2)
			return;
		var span = GlowSpan(points, anchor);
		for (var i = 0; i + 1 < points.Count; i++)
			batcher.Line(points[i] - origin, points[i + 1] - origin, BurrowGlowWeight,
				GlowColorAt(points[i], anchor, span, hueShift), GlowColorAt(points[i + 1], anchor, span, hueShift));
	}

	Color GlowColorAt(Vector2 worldPoint, Vector2 anchor, float span, float hueShift)
	{
		// Glow fades from full at the anchor (the surface point it grows from) to nothing at the
		// ends; its hue cycles along the line and scrolls over time for a rainbow shimmer.
		var dist = Vector2.Distance(worldPoint, anchor);
		var alpha = Math.Clamp(1f - dist / span, 0f, 1f);
		var hue = dist * BurrowGlowHueScale - Time.SecondsF * BurrowGlowCycleSpeed + hueShift;
		var color = new Color(HueRgb(hue - MathF.Floor(hue)), alpha);

		// At each interval boundary flash the whole line brighter/whiter, easing out.
		if (burrowPulseTimer > 0)
		{
			var pulse = burrowPulseTimer / BurrowPulseTime;
			pulse *= pulse; // ease-out flash
			var flash = new Color(1f, 1f, 1f, MathF.Min(1f, alpha + (1f - alpha) * 0.6f));

			pulse *= 0.5f;
			color = Color.Lerp(color, flash, pulse);
		}
		if (burrow.Blocked)
		{
			var pulse = 0.5f + 0.5f * MathF.Sin((Time.SecondsF * BurrowStuckPulseSpeed - dist * BurrowStuckPulseScale) * Calc.TAU);
			var warning = new Color(1f, 0.08f, 0.16f, MathF.Min(1f, alpha + 0.25f));
			color = Color.Lerp(color, warning, 0.45f + pulse * 0.5f);
		}
		return color;
	}

	// Packed 0xRRGGBB for a fully-saturated hue in [0,1); paired with an alpha at the call site.
	static int HueRgb(float hue)
	{
		var h = hue * 6f;
		var x = 1f - MathF.Abs(h % 2f - 1f);
		var (r, g, b) = h switch
		{
			< 1f => (1f, x, 0f),
			< 2f => (x, 1f, 0f),
			< 3f => (0f, 1f, x),
			< 4f => (0f, x, 1f),
			< 5f => (x, 0f, 1f),
			_ => (1f, 0f, x),
		};
		return ((int)(r * 255) << 16) | ((int)(g * 255) << 8) | (int)(b * 255);
	}


	protected void ReleaseBurrowParticles()
	{
		ReleaseParticleEffect(ref burrowSparkEffect);
		ReleaseParticleEffect(ref quantumSparkEffect);
	}
}
