using System.Numerics;
using Foster.Framework;

namespace Myth;

public enum RaftLook { Crust, Driftwood, Ice }

// Floats on whatever liquid is under it -- a LiquidVolume or authored water -- riding the surface as it
// rises, falls and ripples. A rider presses it down with a splash; once the liquid drains out from
// under it, it settles on the ground. The look is procedural: a seeded outline shaded by the look's
// style, and crust glows along its underside and seams while it sits on lava.
[GameActor(Tags = ActorTag.Obstacle, Icon = "water", Anchor = SpriteAnchor.TopLeft,
	BoundsW = 32, BoundsH = 6, LivePositionEditable = true, LiveResizable = true)]
public class Raft : Actor, IResizable
{
	public static readonly Point2 DefaultSize = new(32, 6);

	public Point2 Size { get; set; } = DefaultSize;
	public RaftLook Look;
	[I(0, 1)] public float Draft = 0.35f; // share of the height riding below the surface
	[I(0, 8)] public float WeightSink = 2f; // px a rider presses it down

	// How hard it chases the surface, and how fast the bob dies out. Lava is viscous: a slower,
	// heavier follow that barely overshoots.
	static float Spring => 60f;
	static float Damping => 6f;
	static float LavaSpring => 18f;
	static float LavaDamping => 9f;
	static float Gravity => 300f;
	static float LandingKick => 40f;
	static float LandingSplash => 30f;
	static float MaxTilt => 2f;
	static float GlowStirBoost => 1.5f;

	readonly record struct Style(Color Top, Color Body, Color Under, Color Grain, Color Glow, float Roughness, int GrainSpacing, bool Rounded);

	static Style StyleOf(RaftLook look) => look switch
	{
		RaftLook.Driftwood => new(new(0x9a7a52), new(0x6e5236), new(0x4a3624), new(0x5a4029), Color.Transparent, 0.5f, 2, true),
		RaftLook.Ice => new(new(0xf0faff), new(0xa8d4ee), new(0x7ab0d4), new(0xc8e6f8), Color.Transparent, 0.3f, 0, false),
		_ => new(new(0x5a4038), new(0x2e2220), new(0x1c1414), new(0x3a2a26), LiquidVolume.LavaFoam, 2.5f, 0, false),
	};

	float velocity, remainder, weight, heat, tiltLeft, tiltRight, age;
	bool hadRider, placed;
	uint seed;

	public Raft()
	{
		Sprite = null;
		Mask = Masks.Solid;
		Depth = Depths.ForegroundProp;
	}

	public override void Added()
	{
		base.Added();
		Hitbox = new(new RectInt(0, 0, Size.X, Size.Y));
		seed = Hash32.Mix((uint)Position.X);
	}

	float CenterX => Position.X + Size.X / 2f;

	public override void Update()
	{
		base.Update();
		age += Time.Delta;
		var surface = FloatingOn(out var lava);
		var rider = HasRider();
		weight = Calc.Approach(weight, rider ? 1 : 0, Time.Delta * 6);
		if (surface != null && rider && !hadRider)
		{
			velocity += LandingKick;
			surface.Splash(CenterX, LandingSplash);
		}
		hadRider = rider;

		// Arrive already floating instead of dropping in when the room loads.
		if (!placed && surface != null)
		{
			MoveVertically(Target(surface) - Position.Y);
			velocity = remainder = 0;
		}
		placed |= surface != null || age > 0.1f;

		var (spring, damping) = lava ? (LavaSpring, LavaDamping) : (Spring, Damping);
		velocity += surface == null
			? Gravity * Time.Delta
			: ((Target(surface) - Position.Y - remainder) * spring - velocity * damping) * Time.Delta;
		MoveVertically(velocity * Time.Delta);

		var time = Time.SecondsF;
		(tiltLeft, tiltRight) = surface == null ? (0, 0) : (Tilt(surface, Position.X, time), Tilt(surface, Position.X + Size.X, time));
		var stir = Game.LiquidStir.At(new Vector2(CenterX, Position.Y + Size.Y));
		heat = Calc.Approach(heat, lava ? 1 + stir * GlowStirBoost : 0, Time.Delta * 2);
	}

	float Tilt(WaterSurface surface, float x, float time)
		=> Calc.Clamp(surface.TopAt(x, time) - surface.TopAt(CenterX, time), -MaxTilt, MaxTilt);

	// Whatever liquid is right under our middle holds us up, so a raft over a drained basin just falls.
	WaterSurface? FloatingOn(out bool lava)
	{
		lava = false;
		if (Game.WaterSurfaceAt(new Vector2(CenterX, Position.Y + Size.Y)) is not { } surface
			|| Game.LiquidAt(new Point2((int)CenterX, (int)surface.SurfaceY + 2)) is not { } kind)
			return null;
		lava = kind == LiquidKind.Lava;
		return surface;
	}

	float Target(WaterSurface surface)
		=> surface.TopAt(CenterX, Time.SecondsF) - Size.Y * (1 - Draft) + weight * WeightSink;

	bool HasRider()
	{
		foreach (var actor in Game.Actors)
			if (CanCarry(actor) && IsStandingOn(actor))
				return true;
		return false;
	}

	void MoveVertically(float amount)
	{
		remainder += amount;
		var pixels = (int)MathF.Truncate(remainder);
		remainder -= pixels;
		for (var sign = Math.Sign(pixels); pixels != 0; pixels -= sign)
		{
			if (OverlapsAny(new Point2(0, sign), Masks.Solid))
			{
				velocity = remainder = 0;
				return;
			}
			MoveSolid(new Point2(0, sign));
		}
	}

	float Unit(int n) => (Hash32.Mix((uint)n * 2654435761u ^ seed) & 0xffff) / 65535f;

	float Noise(float x) => Calc.Lerp(Unit((int)MathF.Floor(x)), Unit((int)MathF.Floor(x) + 1), x - MathF.Floor(x));

	// One column of the outline: the top follows the waves' tilt, the ends round off or chip, and
	// the underside is as ragged as the style.
	(int Top, int Bottom) Column(int x, in Style style)
	{
		var tilt = (int)MathF.Round(Calc.Lerp(tiltLeft, tiltRight, x / (float)Math.Max(Size.X - 1, 1)));
		var fromEnd = Math.Min(x, Size.X - 1 - x);
		var radius = Size.Y / 2f;
		var inset = style.Rounded
			? fromEnd < radius ? (int)MathF.Round(radius - MathF.Sqrt(radius * radius - MathF.Pow(radius - fromEnd - 0.5f, 2))) : 0
			: fromEnd < 2 ? (int)(Unit(x) * 2) + (fromEnd == 0 ? 1 : 0) : 0;
		var jag = (int)MathF.Round(Noise(x / 3f) * style.Roughness);
		return (tilt + inset, tilt + Size.Y + jag - inset);
	}

	bool IsSeam(int x) => Unit(x + 777) < 0.08f;

	// The editor shows the raft where it'll float once the room starts, not where it was dropped.
	float EditorLift()
		=> Game.Editing && FloatingOn(out _) is { } surface ? MathF.Round(Target(surface) - Position.Y) : 0;

	public override void Render(Batcher batcher)
	{
		var style = StyleOf(Look);
		var seamColor = Color.Lerp(style.Body, style.Glow, Calc.Clamp(heat, 0, 1) * 0.6f);
		batcher.PushMatrix(new Vector2(0, EditorLift()));
		for (var x = 0; x < Size.X; x++)
		{
			var (top, bottom) = Column(x, style);
			if (bottom - top < 2)
				continue;
			batcher.Rect(new Rect(x, top, 1, bottom - top), style.Body);
			batcher.Rect(new Rect(x, top, 1, 1), style.Top);
			batcher.Rect(new Rect(x, bottom - 1, 1, 1), style.Under);
			if (style.GrainSpacing > 0)
				for (var y = top + 2; y < bottom - 1; y += style.GrainSpacing)
					if (Noise(x / 5f + y * 7) > 0.4f)
						batcher.Rect(new Rect(x, y, 1, 1), style.Grain);
			if (style.Glow.A > 0 && IsSeam(x))
				batcher.Rect(new Rect(x, top + 1, 1, bottom - top - 2), seamColor);
		}
		batcher.PopMatrix();
	}

	public override void RenderEmissive(Batcher batcher)
	{
		var style = StyleOf(Look);
		if (heat <= 0 || style.Glow.A == 0)
			return;
		var time = Time.SecondsF;
		for (var x = 0; x < Size.X; x++)
		{
			var (top, bottom) = Column(x, style);
			var glow = style.Glow * (Calc.Clamp(heat, 0, 1) * (0.7f + 0.3f * MathF.Sin(time * 3 + x * 0.7f)));
			batcher.Rect(new Rect(x, bottom - 2, 1, 2), glow);
			if (IsSeam(x))
				batcher.Rect(new Rect(x, top + 1, 1, bottom - top - 3), glow * 0.6f);
		}
	}

	public override void RenderShadow(Batcher batcher, Color color) => batcher.Rect(new Rect(0, 0, Size.X, Size.Y), color);
	public override void RenderNormals(Batcher batcher) { }
	public override bool CastsReflection => false;
	public override Rect GetVisualBounds() => new(Position.X, Position.Y - MaxTilt, Size.X, Size.Y + MaxTilt * 2 + 3);
	public override string? SurfaceSubstance => Look == RaftLook.Driftwood ? "Wood" : null;
}
