using System.Numerics;
using Foster.Framework;

namespace Myth;

// A FootSoldier's sword thrown end over end at his partner in their pair attack. It flies at the catcher's hand at the
// height it was thrown from (Lane), spinning, cutting whatever it passes, and caught it's the catcher's sword. If he
// isn't there to catch it, it clatters down and lies where it lands until an unarmed soldier picks it up.
public sealed class ThrownBlade : Actor
{
	public FootSoldier? Catcher;
	public int Lane;
	public float Length = 12;
	public Color BladeColor = new(0xc8d4e0);
	public Color HiltColor = new(0x6a4a30);
	public bool Flying { get; private set; } = true;

	static float Speed => 150f;
	static float SpinRate => 18f; // radians/sec
	static float CatchReach => 6f;
	static float Gravity => 500f;
	static float SettleRate => 12f; // how fast a dropped blade's spin settles flat once it's down

	float spin;

	public ThrownBlade()
	{
		Mask = Masks.Hazard;
		Hitbox = new(new RectInt(-4, -4, 8, 8));
		CollidesWithSolids = false;
		Depth = -6;
	}

	internal override bool ContactDamageEnabled => Flying;
	public override bool CastsSpriteShadow => false;

	public override void Update()
	{
		if (Flying)
		{
			spin += SpinRate * Time.Delta;
			if (Catcher is not { Receiving: true } catcher || Game.destroying.Contains(catcher))
				Drop();
			else
			{
				var to = catcher.CatchPoint(Lane) - (Vector2)Position;
				if (to.Length() <= CatchReach && catcher.TryCatch())
				{
					Game.Destroy(this);
					return;
				}
				Velocity = to.Normalized() * Speed;
			}
		}
		else
		{
			Velocity.Y += Gravity * Time.Delta;
			if (Grounded())
				spin = Calc.Approach(spin, MathF.Round(spin / MathF.PI) * MathF.PI, SettleRate * Time.Delta);
		}
		base.Update();
	}

	void Drop()
	{
		Flying = false;
		Mask = Masks.None;
		CollidesWithSolids = true;
		Velocity = new(Velocity.X * 0.3f, -60);
	}

	public override void OnCollideX()
	{
		Velocity.X = -Velocity.X * 0.3f;
		StopX();
	}

	public override void OnCollideY()
	{
		Velocity.X *= 0.5f;
		StopY();
	}

	public override void Render(Batcher batcher)
	{
		var along = Calc.AngleToVector(spin);
		batcher.Line(-along * Length * 0.25f, Vector2.Zero, 2, HiltColor);
		batcher.Line(Vector2.Zero, along * Length * 0.75f, 1, BladeColor);
	}

	public override void RenderEmissive(Batcher batcher)
	{
		if (Flying)
			batcher.Line(Vector2.Zero, Calc.AngleToVector(spin) * Length * 0.75f, 1, BladeColor * 0.4f);
	}
}
