namespace Myth;
using Foster.Framework;

public class BurrowBomb : Actor
{
	public override bool LightingDithered => false;
	
	float explodeTimer = 2.0f;
	float tickTimer;
	static float TickDivisions => 4;
	
	public BurrowBomb()
	{
		Depth = -20;
		CollidesWithSolids = false;
	}

	public override void Added()
	{
		tickTimer = explodeTimer / TickDivisions;
		Squish = new(0.4f, 0.4f);
		Hitbox = Hitbox.FromSprite(Sprite);
	}

	public override void Update()
	{
		base.Update();
		explodeTimer = Calc.Approach(explodeTimer, 0, Time.Delta);
		tickTimer = Calc.Approach(tickTimer, 0, Time.Delta);
		if (tickTimer <= 0)
		{
			Squish = new(1.22f, 1.22f);
			tickTimer = explodeTimer / TickDivisions;
		}
		if (explodeTimer <= 0)
		{
			var blast = Hitbox.Rect.Inflate(5) + Position;
			Game.DamageActors(this, blast);
			Game.BreakTiles(blast);
			Game.Create<Pop>(Position);
			Game.Destroy(this);
		}
	}
}
