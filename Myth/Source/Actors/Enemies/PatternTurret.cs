using System.Numerics;
using Foster.Framework;
using Kit.Patterns;

namespace Myth;

// Fires on a Tidal/Strudel pattern: every step is a shot, its value the angle in degrees, "~" a
// rest. "0 ~ 45 <90 135>" fires right, rests, fires down-right, then alternates each cycle. The
// same pattern is read a little into the future to charge the telegraph, so the glow and the
// windup sound are the shot the pattern is about to take. Edit Shots while the game runs.
[GameActor(Sprite = "Editor/bouncer", Icon = "wave-square", Tags = ActorTag.Enemy, BoundsX = -6, BoundsY = -6, BoundsW = 12, BoundsH = 12, LivePositionEditable = true)]
public class PatternTurret : Actor
{
	[Multiline(2)] public string Shots = "0 ~ 45 <90 135>";
	[I(0.05f, 4)] public float CyclesPerSecond = 0.5f;
	[I(10, 160)] public float Speed = 36;
	[I(0, 1.5f)] public float Telegraph = 0.35f;

	static float Radius => 5f.T();
	public override bool CastsSpriteShadow => false;

	readonly PatternClock clock = new();
	readonly List<Hap<Value>> haps = [];
	Pattern<Value> pattern = Pattern.Silence<Value>();
	string parsed = "";
	float charge;

	public PatternTurret()
	{
		Hitbox = new(new RectInt(-6, -6, 12, 12));
		Mask = Masks.Enemy;
		Depth = -5;
		CollidesWithSolids = false;
		Sprite = null;
	}

	public override void Update()
	{
		base.Update();
		ParseShots();
		clock.Rate = CyclesPerSecond;
		clock.Advance(Time.Delta);

		haps.Clear();
		clock.Triggers(pattern, haps);
		foreach (var hap in haps)
			Fire(hap.Value);

		charge = ChargeToNextShot();
	}

	// The last valid pattern keeps playing while the text is half-typed.
	void ParseShots()
	{
		if (parsed == Shots)
			return;
		parsed = Shots;
		try
		{
			pattern = Mini.Parse(Shots);
		}
		catch (PatternSyntaxException error)
		{
			Game.DisplayMessage($"shots: {error.Message}");
		}
	}

	void Fire(Value value)
	{
		var radians = (value.IsNumber ? (float)value.Number : 0) * Calc.DegToRad;
		BulletPattern.FireArc(Game, Position, new Vector2(MathF.Cos(radians), MathF.Sin(radians)), 1, 0, Speed);
		AudioEvent("bubble");
	}

	// How close the next shot is, as 0..1 over the Telegraph seconds before it.
	float ChargeToNextShot()
	{
		if (Telegraph <= 0)
			return 0;
		var window = Telegraph * clock.Rate;
		haps.Clear();
		pattern.Query(new Arc(clock.Position, clock.Position + Fraction.FromDouble(window)), haps);
		var nearest = double.MaxValue;
		foreach (var hap in haps)
			if (hap.HasOnset)
				nearest = Math.Min(nearest, (hap.WholeOrPart.Begin - clock.Position).ToDouble());
		return nearest == double.MaxValue ? 0 : (float)(1 - nearest / window);
	}

	public override void Render(Batcher batcher)
	{
		batcher.Circle(Vector2.Zero, Radius + 1, 12, new Color(0x264a64));
		batcher.Circle(Vector2.Zero, Radius * (0.6f + charge * 0.4f), 12, Color.Lerp(new Color(0x88e7ff), Color.White, charge));
	}

	public override void RenderEmissive(Batcher batcher)
		=> batcher.Circle(Vector2.Zero, Radius * (0.6f + charge), 12, new Color(0x88e7ff) * charge);
	public override void RenderLightEmission(Batcher batcher) => RenderEmissive(batcher);
}
