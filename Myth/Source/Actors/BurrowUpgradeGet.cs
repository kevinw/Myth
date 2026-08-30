namespace Myth;

// The quick, fully-modal beat when a burrow upgrade is collected: freezes the world (TimeScale 0)
// scales up the HUD, grows the stamina bar, then scales the HUD back down before restoring time.
// Spawned by Pickup.Grant after the capacity has already increased.
[GameActor(HideFromPalette = true)]
public class BurrowUpgradeGet : Actor
{
	static float ScaleTime => 0.7f;
	static float HoldTime => 0.35f;
	static float GrowTime => 0.95f;
	static float Duration => ScaleTime * 2 + HoldTime + GrowTime;

	float timer;
	float previousTimeScale;

	public BurrowUpgradeGet()
	{
		Mask = Masks.None;
		CollidesWithSolids = false;
		Visible = false;
	}

	public override void Added()
	{
		base.Added();
		previousTimeScale = Game.TimeScale;
		Game.TimeScale = 0; // fully modal
		Game.BurrowUpgradeHud = new() { Active = true };
	}

	public override void Update()
	{
		base.Update();
		timer += Game.Manager.Time.Delta; // real time; the sim is frozen
		if (timer < ScaleTime)
			Game.BurrowUpgradeHud = new() { Active = true, Emphasis = timer / ScaleTime };
		else if (timer < ScaleTime + HoldTime)
			Game.BurrowUpgradeHud = new()
			{
				Active = true,
				Emphasis = 1,
				Particles = (timer - ScaleTime) / HoldTime,
			};
		else if (timer < ScaleTime + HoldTime + GrowTime)
			Game.BurrowUpgradeHud = new() { Active = true, Emphasis = 1, Particles = 1, Growing = true };
		else
		{
			var emphasis = 1 - Math.Clamp((timer - ScaleTime - HoldTime - GrowTime) / ScaleTime, 0f, 1f);
			Game.BurrowUpgradeHud = new() { Active = true, Emphasis = emphasis, Particles = emphasis };
		}

		if (timer >= Duration)
		{
			Game.BurrowUpgradeHud = default;
			Game.TimeScale = previousTimeScale;
			Game.Destroy(this);
		}
	}
}
