using System.Numerics;
using Foster.Framework;

namespace Myth;

// Burrow-specific logic
public partial class Player : Pawn
{
	static float BurrowDuration => 1.8f;
	static float BurrowMaxSpeed => 150f;
	static float BurrowAccel => 500f;            // ease up to speed instead of snapping
	static float BurrowFriction => 600f;         // glide to a stop when the stick is released
	static float BurrowReverseFlipSpeed => 8f;   // brake below this before flipping heading on a reversal
	static float BurrowClimbSpeedMultiplier => 0.5f; // straight-up wall climb is half speed; 45° lands in between
													 // The burrow pop is meant to read as stronger than a full held jump: a bigger launch
													 // velocity, plus a longer reduced-gravity float window (jumpTimer) than a normal jump.
	static float BurrowPopForce => JumpForce * 0.80f;
	static float BurrowPopTime => JumpTime * 0.9f;
	static float BurrowWallSlideLaunch => JumpForce * 1.2f; // strong upward velocity when a wall climb pops into a slide
	const int BurrowSubmergeDepth = 4;
	const int BurrowJumpthruSubmergeDepth = 0; // thin one-way platforms: ride on top, don't sink through
	const int BurrowAttachProbe = 8;
	const float BurrowGlowArc = 15f;       // contour length traced in each direction (~30px total)
	static float BurrowGlowWeight => 1.5f;
	const int BurrowGlowColor = 0xffcfa0;  // matches the player light
	const float BurrowEnterSquishTime = 0.12f; // sprite stays visible this long to play the squash-in
	const float BurrowSquashNormal = 0.45f;    // peak compression into the surface at the end of the window
	const float BurrowSquashTangent = 1.6f;    // peak spread along the surface at the end of the window
	const float BurrowExitGrace = 0.1f;        // suppress the landing squish briefly so the pop squish survives
	const float BurrowTangentDeadzone = 0.3f;  // |input·tangent| below this counts as orthogonal to the line

	float burrowEnterTimer;
	float burrowExitTimer;
	Point2 burrowHeading = new(1, 0);   // current world travel direction along the surface
	bool burrowSolidLeft;               // walk handedness (= orbit direction); only flips on a fresh press
	Vector2 burrowLastIntent;           // last frame's input; a change re-decides the orbit (even without a zero gap)
	bool burrowOrbiting;                // latched movement: started by a tangential press, held through corners, dropped on release
	bool burrowReverseQueued;           // a reversal is pending: brake to a stop, then flip heading
	Vector2 burrowExitOutward;          // away-from-surface dir at the last pop, biases the unstick nudge
	Vector2 burrowToward = Vector2.UnitY; // surface normal (into solid) at the burrow point
	float burrowMoveRemainder;          // sub-pixel accumulator for boundary advancement
	float burrowSpeed;                  // current travel speed; eases via accel/friction
	bool burrowOnJumpthru;              // current surface is a one-way platform (ride on top, no submerge)
	readonly List<Vector2> glowPoints = [];
	Point2 burrowGlowAnchor;
	int burrowSparkEffect = -1;         // particle effect slot for the digging sparks
	float burrowSparkAccumulator;
	const float BurrowSparkRate = 160f; // sparks/sec at full burrow speed
	SoundInstance? burrowSound;

	float burrowGlowExitTimer; // current visual burrow glow exit timer
	static float BurrowGlowExitTime => 0.13f; // how long to render the line after you pop out of burrowing

	void TryStartBurrow(Vector2 previousVelocity)
	{
		if (!CanBurrow()) return;

		Hitbox = new(BurrowHitboxRect);
		burrowToward = Vector2.UnitY;
		if (!Game.SnapToSurface(ref Position, ref burrowToward, BurrowAttachProbe))
		{
			Hitbox = new(StandingHitboxRect); // no surface in reach; we're staying in Normal
			return;
		}

		Velocity = Vector2.Zero;
		burrowTimer = BurrowDuration;
		burrowEnterTimer = BurrowEnterSquishTime;
		burrowMoveRemainder = 0;
		burrowLastIntent = Vector2.Zero; // a tangential press establishes the orbit
		burrowOrbiting = false;
		burrowReverseQueued = false;
		// Initialize the walk state: heading = +tangent, solid on the right (matches +tangent).
		burrowSolidLeft = false;
		burrowHeading = new Point2((int)MathF.Round(burrowToward.Y), -(int)MathF.Round(burrowToward.X));

		// Carry the landing's horizontal momentum into the burrow. Head the way we were
		// moving (reversing heading + handedness keeps the normal pointing into the solid),
		// and seed the speed with the incoming horizontal velocity so a fast run/roll dives
		// straight into a fast burrow instead of starting from a standstill.
		if (previousVelocity.X != 0 && burrowHeading.X != 0
			&& Math.Sign(previousVelocity.X) != Math.Sign(burrowHeading.X))
		{
			burrowHeading = new Point2(-burrowHeading.X, -burrowHeading.Y);
			burrowSolidLeft = !burrowSolidLeft;
		}
		burrowSpeed = MathF.Min(MathF.Abs(previousVelocity.X), BurrowMaxSpeed);

		State = States.Burrow;
		burrowOnJumpthru = Game.SurfaceIsJumpthru(Position, burrowToward);
		UpdateBurrowShift();
		UpdateBurrowGlow();

		StopAndReleaseSound(ref burrowSound);
		burrowSound = Game.CreateAudioEvent("player/burrow", Position);
		burrowSound?.Start();
		UpdateBurrowAudio();
		AudioEvent("player/burrow_start");
	}

	// Screen-axis squish aligned to a surface normal: `normalScale` is applied along
	// the TowardSurface axis, `tangentScale` along the surface. Slopes fall back to
	// whichever axis the normal leans toward (Squish can't rotate).
	static Vector2 SurfaceSquish(Vector2 toward, float normalScale, float tangentScale)
	{
		return new Vector2(tangentScale, normalScale);
	}

	// True once the entry squish has finished and the player is fully submerged.
	private bool BurrowHidesSprite
	{
		get
		{
			if (PerfToggles.HeadSlide) return false;
			return State == States.Burrow && burrowEnterTimer <= 0;
		}
	}

	// Rebuild the glow polyline by tracing the boundary from the burrow's own walk state,
	// so the line is always centered on the burrow point (no re-seed mis-anchoring at corners).
	void UpdateBurrowGlow()
	{
		glowPoints.Clear();
		float arcLength;

		if (State == States.Burrow) // currently burrowing
			arcLength = BurrowGlowArc * (1f - burrowEnterTimer / BurrowEnterSquishTime);
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
		Game.TraceSurfaceFromState(burrowGlowAnchor, burrowHeading, burrowSolidLeft, arcLength, glowPoints);
	}

	void UpdateBurrowShift()
	{
		if (PerfToggles.HeadSlide) return;
		var depth = burrowOnJumpthru ? BurrowJumpthruSubmergeDepth : BurrowSubmergeDepth;
		Shift = State == States.Burrow ? burrowToward * depth : new();
	}

	static float BurrowCooldown => 0.25f;

	// Is the burrow point against the room boundary in the direction we're driving? Only then
	// can the surface plausibly continue in the adjacent room. Room bounds are half-open
	// [Left, Right) / [Top, Bottom); a small tolerance covers the surface ending a pixel shy.
	const int BurrowRoomEdgeTolerance = 2;
	bool BurrowAtRoomEdge()
	{
		var bounds = Game.Bounds;
		if (burrowHeading.X > 0) return Position.X >= bounds.Right - BurrowRoomEdgeTolerance;
		if (burrowHeading.X < 0) return Position.X <= bounds.Left + BurrowRoomEdgeTolerance;
		if (burrowHeading.Y > 0) return Position.Y >= bounds.Bottom - BurrowRoomEdgeTolerance;
		if (burrowHeading.Y < 0) return Position.Y <= bounds.Top + BurrowRoomEdgeTolerance;
		return false;
	}

	private void BurrowState()
	{
		Velocity = Vector2.Zero;

		// Ramp the squash UP toward peak as the entry window elapses, overriding the
		// base per-frame decay so it intensifies right up until the sprite vanishes.
		if (burrowEnterTimer > 0)
		{
			burrowEnterTimer = Calc.Approach(burrowEnterTimer, 0, Time.Delta);
			var t = 1f - burrowEnterTimer / BurrowEnterSquishTime;
			var peak = SurfaceSquish(burrowToward, BurrowSquashNormal, BurrowSquashTangent);
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
		var intent = new Vector2(input.MoveX, input.MoveY);
		var pressed = intent != Vector2.Zero;
		var changed = intent != burrowLastIntent;
		var proj = Vector2.Dot(intent, (Vector2)burrowHeading);
		var tangential = MathF.Abs(proj) > BurrowTangentDeadzone;

		if (!pressed)
			burrowOrbiting = false;
		else if (changed && tangential)
		{
			// Pressing opposite the heading queues a reversal (brakes first, below);
			// pressing forward cancels any pending reversal.
			burrowReverseQueued = proj < 0;
			burrowOrbiting = true;
		}
		burrowLastIntent = intent;

		var move = burrowOrbiting;

		// Speed eases in and out (accel + friction) rather than snapping, and climbing slows
		// you down: a straight-up wall heading runs at BurrowClimbSpeedMultiplier, while a 45°
		// slope lands in between on its own (the cardinal walker alternates up/flat steps).
		// Descending counts as flat, so you keep momentum going down. A queued reversal
		// targets zero speed (friction) so a direction switch decelerates before flipping.
		var climb = MathF.Max(0, -burrowHeading.Y);
		var targetSpeed = move && !burrowReverseQueued ? BurrowMaxSpeed * (1f - (1f - BurrowClimbSpeedMultiplier) * climb) : 0f;
		var rate = targetSpeed > burrowSpeed ? BurrowAccel : BurrowFriction;
		burrowSpeed = Calc.Approach(burrowSpeed, targetSpeed, rate * Time.Delta);

		// Once braked nearly to a stop, perform the queued reversal and accelerate the
		// other way next frame.
		if (burrowReverseQueued && burrowSpeed <= BurrowReverseFlipSpeed)
		{
			burrowHeading = new Point2(-burrowHeading.X, -burrowHeading.Y);
			burrowSolidLeft = !burrowSolidLeft;
			burrowReverseQueued = false;
		}

		burrowMoveRemainder += burrowSpeed * Time.Delta;
		var pixels = (int)burrowMoveRemainder;
		burrowMoveRemainder -= pixels;
		if (pixels > 0)
		{
			var moved = Game.WalkSurface(ref Position, ref burrowHeading, burrowSolidLeft, pixels);
			burrowToward = Game.SurfaceNormal(burrowHeading, burrowSolidLeft);
			if (moved == 0)
			{
				// A dead end. If we're driving into it and the surface exits the room into an
				// adjacent one, transition and keep burrowing on the far side. The world is laid
				// out in contiguous coordinates, so the surface continues across the shared edge.
				// Otherwise (no adjacent room) pop out; if we merely coasted here on friction,
				// kill the glide and stay put.
				if (move)
				{
					// Only a surface that ran off the room edge can continue in a neighbor. A
					// dead-end away from the edge (e.g. the end of a one-way jumpthru sitting
					// mid-room) has nowhere to go, so pop out instead of letting Transition
					// teleport us into the adjacent screen — it only checks the perpendicular axis.
					if (BurrowAtRoomEdge() && Game.Transition(burrowHeading))
						return; // stay burrowed; resumed by ResolveBurrowRoomTransition after the swap
					PopOutOfBurrow();
					return;
				}
				burrowSpeed = 0;
				burrowMoveRemainder = 0;
			}
		}

		burrowOnJumpthru = Game.SurfaceIsJumpthru(Position, burrowToward);
		UpdateBurrowShift();
		UpdateBurrowGlow();
		UpdateBurrowAudio();
		EmitBurrowSparks();
		if (!PerfToggles.HeadSlide) Play("burrow");

		if (input.MoveX != 0)
			Facing = Math.Sign(input.MoveX);

		burrowTimer -= Time.Delta;
		if (burrowTimer <= 0 || !input.JumpDown)
			PopOutOfBurrow();
	}

	// StopBurrow stop a burrow
	private void PopOutOfBurrow()
	{
		var exitDir = burrowToward;

		// Climbing straight up a vertical wall pops into a wall slide instead of launching
		// off it: drop the outward push, keep hugging the wall, and carry strong upward
		// velocity. Feels better and sets up wall-jump / re-burrow chaining off the launch.
		if (MathF.Abs(burrowToward.X) > MathF.Abs(burrowToward.Y) && burrowHeading.Y < 0)
		{
			Velocity = new Vector2(0, BurrowWallSlideLaunch);
			Facing = Math.Sign(burrowToward.X); // face into the wall so the slide reads
			Squish = SurfaceSquish(exitDir, normalScale: 0.7f, tangentScale: 1.3f);
		}
		else
		{
			// Launch away from whatever surface we were riding, plus carry the burrow's
			// tangential glide momentum (so popping out of a fast rightward burrow keeps
			// that rightward speed).
			var popMagnitude = MathF.Abs(BurrowPopForce);
			var travel = burrowHeading != Point2.Zero
				? Vector2.Normalize((Vector2)burrowHeading) * burrowSpeed
				: Vector2.Zero;
			Velocity = -exitDir * popMagnitude + travel;
			// Burrow glide can exceed normal air speed; let the carried horizontal
			// momentum persist briefly (then normal air clamping reins it in) instead of
			// being snapped to MaxAirSpeed immediately. Reuses the roll-jump carry, whose
			// cap (RollJumpSpeed) matches BurrowMaxSpeed.
			if (MathF.Abs(travel.X) > MaxAirSpeed)
				rollJumpCarryTimer = RollJumpCarryTime;
			Squish = SurfaceSquish(exitDir, normalScale: 1.4f, tangentScale: 0.65f);
		}

		jumpTimer = BurrowPopTime;
		jumpCutImmune = true; // the pop keeps its full float even when jump is released
		State = States.Normal;
		AudioEvent("jump");
		burrowEnterTimer = 0;
		burrowExitTimer = BurrowExitGrace;
		burrowExitOutward = -exitDir; // away from the surface; bias the unstick nudge this way
		burrowTimer = -BurrowCooldown;
		burrowGlowExitTimer = BurrowGlowExitTime;
		burrowNeedsGrounding = true; // must touch ground again before another burrow can arm
		UpdateBurrowShift();
		StopAndReleaseSound(ref burrowSound);

		// emit a final burst up and opposite the direction of leaving
		if (Game.Particles is { } particles)
		{
			float direction = MathF.Atan2(Velocity.X, Velocity.Y);
			particles.Emit(burrowSparkEffect,
				worldPosition: burrowGlowAnchor - exitDir * 2.0f - Vector2.Normalize((Vector2)burrowHeading) * 3.0f,
				count: 20,
				directionRadians: direction + MathF.PI / 2.0f,
				speedScale: 2.5f,
				spreadRadians: 19.0f * Calc.DegToRad);
		}
		UpdateBurrowGlow();
		AudioEvent("player/burrow_end");
	}

	// Called by Game right after a room swap that began mid-burrow (see BurrowState's dead-end
	// handling). Rooms are contiguous in world space, so the surface continues at our current
	// position — re-anchor to it and keep gliding with the same heading/handedness/speed instead
	// of running the normal push-out-of-collision placement (which would shove us off the line).
	public void ResolveBurrowRoomTransition()
	{
		if (State != States.Burrow)
			return;

		// Step one pixel into the new room along the heading before re-snapping. Room bounds
		// are half-open ([Left, Right)/[Top, Bottom)), so when exiting the left/top edge our
		// position lands exactly on the next room's exclusive Right/Bottom edge, where it reads
		// as out-of-room and the snap would fail (this is why left/up transitions popped out).
		Position += burrowHeading;

		if (Game.SnapToSurface(ref Position, ref burrowToward, BurrowAttachProbe))
		{
			burrowOnJumpthru = Game.SurfaceIsJumpthru(Position, burrowToward);
			UpdateBurrowShift();
			UpdateBurrowGlow();
		}
		else
		{
			// The surface didn't continue in the new room — pop out where we arrived.
			PopOutOfBurrow();
		}
	}

	void UpdateBurrowAlways()
	{
		if (burrowGlowExitTimer > 0)
			UpdateBurrowGlow(); // called here also so we update outside of the burrow state
		burrowGlowExitTimer = Calc.Approach(burrowGlowExitTimer, 0, Time.Delta);
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
		var climb = MathF.Max(0, -burrowHeading.Y);
		Game.SetAudioEventParameter(burrowSound, "Speed", speed01);
		Game.SetAudioEventParameter(burrowSound, "Climb", climb);
	}

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

		// Aim out of the solid (burrowToward points into it); fall back to straight up.
		var outward = -burrowToward;
		outward = outward.LengthSquared() > 0.0001f ? Vector2.Normalize(outward) : new Vector2(0, -1);
		var origin = (Vector2)Position + outward * 1.5f;
		var direction = MathF.Atan2(outward.Y, outward.X);
		particles.Emit(burrowSparkEffect, origin, count, direction, 0.4f + 0.6f * speed01);
	}

	void RenderBurrowLine(Batcher batcher)
	{
		if (!PerfToggles.BurrowParticles) return;

		var shouldRender = (State == States.Burrow || burrowGlowExitTimer > 0) && glowPoints.Count >= 2;

		float start = glowPoints.ElementAtOrDefault(0).X;
		float end = glowPoints.ElementAtOrDefault(glowPoints.Count - 1).X;
		// Console.WriteLine($"{start} to {end} ({glowPoints.Count}) - {shouldRender}");
		if (!shouldRender)
			return;

		Color BurrowGlowColorAt(Vector2 worldPoint)
		{
			// Glow fades from full at the anchor (player's surface point) to nothing at the ends.
			var dist = Vector2.Distance(worldPoint, burrowGlowAnchor);
			var alpha = Math.Clamp(1f - dist / BurrowGlowArc, 0f, 1f);
			return new Color(BurrowGlowColor, alpha);
		}

		// The traced points lie exactly on the solid/air boundary; draw them directly.
		// The emissive pass pre-pushes a matrix at our Position, so draw in local space.
		Vector2 origin = Position;
		for (var i = 0; i + 1 < glowPoints.Count; i++)
			batcher.Line(glowPoints[i] - origin, glowPoints[i + 1] - origin, BurrowGlowWeight, BurrowGlowColorAt(glowPoints[i]), BurrowGlowColorAt(glowPoints[i + 1]));

	}


	void ReleaseBurrowParticles()
	{
		if (burrowSparkEffect >= 0 && Game.Particles is { } particles)
			particles.ReleaseEffect(burrowSparkEffect);
		burrowSparkEffect = -1;
	}
}
