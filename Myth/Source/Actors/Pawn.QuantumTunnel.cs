using System.Numerics;
using Foster.Framework;

namespace Myth;

// Quantum tunnelling: come to a stop while burrowing and hold straight into a wall only a tile
// thick, and a second burrow glow lights up on the far face, parallel to yours. Yours shrinks as
// it grows; hold until it reaches full length and you surface over there, still burrowing. Nothing
// teaches it -- it only shows itself when you push into a wall that looks too thin to matter.
public abstract partial class Pawn
{
	internal static bool QuantumTunnelEnabled = true;

	static float QuantumTunnelTime => 0.5f;    // hold time from first spark to the crossing
	static float QuantumCollapseRate => 3f;    // per-second collapse once you let go
	static float QuantumStillSpeed => 4f;      // "stopped": the glide has to be spent
	static float QuantumIntoWallDot => 0.85f;  // how square-on the hold has to be
	static float QuantumSeedGlow => 0.18f;     // the far line appears the instant you press
	static float QuantumRumble => 0.05f;
	static float QuantumFlashTime => 0.18f;      // the arc left behind in the gap
	static float QuantumFlashIntensity => 0.7f;  // toned down from a lightning trap's full bolt
	static float QuantumFlashOvershoot => 2f;    // pokes out of both faces so the gap reads
	static float QuantumSparkSpacing => 2.5f;    // sub-bursts along the face, so it isn't a point source
	static float QuantumHueShift => 0.5f; // complementary hue, so it reads as the other side
	static int QuantumMaxThickness => Game.TileSize;

	float quantumCharge;
	Point2 quantumExit;
	readonly List<Vector2> quantumGlowPoints = [];
	int quantumSparkEffect = -1;
	float quantumFlashTimer;
	Vector2 quantumFlashFrom, quantumFlashTo; // world ends of the gap just crossed

	// The near glow gives way to the far one as the charge builds.
	float QuantumGlowScale => 1f - quantumCharge;

	void UpdateQuantumTunnel(Vector2 intent)
	{
		var armed = QuantumTunnelEnabled && burrowLine == null && !burrowOrbiting
			&& burrowSpeed <= QuantumStillSpeed && HoldingIntoSurface(intent) && TryFindQuantumExit(out quantumExit);

		quantumCharge = Calc.Approach(quantumCharge, armed ? 1 : 0,
			(armed ? 1f / QuantumTunnelTime : QuantumCollapseRate) * Time.Delta);

		// The far line doubles as the check that there is a surface to arrive on: if the trace
		// can't walk that boundary there's nothing to see and nothing to cross to.
		quantumGlowPoints.Clear();
		if (quantumCharge > 0 && !Game.TraceBurrowPathFromState(quantumExit, burrowHeading, !burrowSolidLeft, null,
			BurrowGlowArc * burrowSpreadScale * Calc.Lerp(QuantumSeedGlow, 1f, quantumCharge), quantumGlowPoints))
			quantumCharge = 0;
		else if (quantumCharge >= 1)
			TunnelThroughWall();
	}

	bool HoldingIntoSurface(Vector2 intent)
		=> intent.LengthSquared() > 0.25f && Vector2.Dot(Vector2.Normalize(intent), burrowToward) >= QuantumIntoWallDot;

	// Straight through the solid under the burrow point: at most a tile of it, open on the far
	// side. Actor solids count, so a one-tile platform or a phased-in flip block is tunnelable too.
	bool TryFindQuantumExit(out Point2 exit)
	{
		var thickness = 0;
		while (thickness <= QuantumMaxThickness && Game.BurrowLineSolidAt((Vector2)Position + burrowToward * (thickness + 0.5f)))
			thickness++;
		exit = Position + (burrowToward * thickness).RoundToPoint2();
		return thickness > 0 && thickness <= QuantumMaxThickness;
	}

	// Surface on the far face still burrowing: same heading, the solid now on the other hand. The
	// hold that brought you through points out of the new surface, so it can't bounce you back.
	void TunnelThroughWall()
	{
		var entry = (Vector2)Position;
		var entryOutward = -burrowToward;
		Position = quantumExit;
		burrowToward = -burrowToward;
		burrowSolidLeft = !burrowSolidLeft;
		burrowOnJumpthru = Game.SurfaceIsJumpthru(Position, burrowToward);
		burrowSpeed = 0;
		burrowMoveRemainder = 0;
		burrowExitBlocked = false;
		quantumCharge = 0;
		burrowPulseTimer = BurrowPulseTime;
		quantumFlashTimer = QuantumFlashTime;
		quantumFlashFrom = entry + entryOutward * QuantumFlashOvershoot;
		quantumFlashTo = (Vector2)Position - entryOutward * QuantumFlashOvershoot;
		if (Game.BurrowTraceEnabled)
			Game.TraceBurrow($"quantum tunnel {entry} -> {Position} normal={burrowToward} solidLeft={burrowSolidLeft}");
		EmitQuantumSparks(entry, entryOutward);
		EmitQuantumSparks(Position, -burrowToward);
		UpdateBurrowShift();
		AudioEvent("player/burrow_tunnel"); // wants its own event; missing ones are logged once
		AudioEvent("player/burrow_start");
	}

	// Off the face, not along it: the digging sparks fire with zero spread, which on a flat wall
	// lays them down as one collimated line. These are the lightning trap's sparks -- a wide fan in
	// the bolt's own palette -- fired from a few points along the face so the whole gap discharges.
	void EmitQuantumSparks(Vector2 face, Vector2 outward)
	{
		if (Game.Particles is not { } particles)
			return;
		if (quantumSparkEffect < 0)
			quantumSparkEffect = AcquireParticleEffect("lightning_spark");

		var along = outward.TurnLeft();
		for (var i = -1; i <= 1; i++)
			particles.Emit(quantumSparkEffect, face + outward + along * (i * QuantumSparkSpacing), 4, outward.Angle());
	}

	// A few frames of a bolt across the gap, brightest at the face you arrived on.
	void RenderQuantumFlash(Batcher batcher, Vector2 origin)
	{
		if (quantumFlashTimer <= 0)
			return;

		var fade = quantumFlashTimer / QuantumFlashTime * QuantumFlashIntensity;
		ReadOnlySpan<LightningEffectPoint> gap =
		[
			new((quantumFlashFrom - origin).RoundToPoint2(), fade * 0.3f),
			new((quantumFlashTo - origin).RoundToPoint2(), fade),
		];
		LightningEffect.Render(batcher, gap, Time.SecondsF, (int)(quantumFlashFrom.X * 397) ^ (int)quantumFlashFrom.Y);
	}

	void ResetQuantumTunnel()
	{
		quantumCharge = 0;
		quantumGlowPoints.Clear();
	}
}
