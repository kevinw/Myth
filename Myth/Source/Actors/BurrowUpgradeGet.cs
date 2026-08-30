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
	static float LabelTravelTime => 0.7f;
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
		SetHud(0);
	}

	public override void Update()
	{
		base.Update();
		timer += Game.Manager.Time.Delta; // real time; the sim is frozen
		if (timer < ScaleTime)
			SetHud(timer / ScaleTime);
		else if (timer < ScaleTime + HoldTime)
			SetHud(1, (timer - ScaleTime) / HoldTime);
		else if (timer < ScaleTime + HoldTime + GrowTime)
			SetHud(1, 1, growing: true);
		else
		{
			var emphasis = 1 - Math.Clamp((timer - ScaleTime - HoldTime - GrowTime) / ScaleTime, 0f, 1f);
			SetHud(emphasis, emphasis);
		}

		if (timer >= Duration)
		{
			Game.BurrowUpgradeHud = default;
			Game.TimeScale = previousTimeScale;
			Game.Destroy(this);
		}
	}

	void SetHud(float emphasis, float particles = 0, bool growing = false)
		=> Game.BurrowUpgradeHud = new()
		{
			Active = true,
			Emphasis = emphasis,
			Particles = particles,
			Growing = growing,
			LabelWorldOrigin = Position,
			LabelTravel = Math.Clamp(timer / LabelTravelTime, 0f, 1f),
		};
}
