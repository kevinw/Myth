namespace Myth;

using System.Numerics;

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

	public BurrowUpgradeGet()
	{
		Mask = Masks.None;
		CollidesWithSolids = false;
		Visible = false;
	}

	public override void Added()
	{
		base.Added();
		PushTimeFactor(0);
		// Freezing time zeroes Game.Time.Delta, so a hit-stun in progress can never drain and the
		// normal actor loop stops running -- we'd never advance. Update through the hit-stun list
		// instead, on real time, so the modal always completes and releases the freeze.
		Game.HitStunActors.Add(this);
		// if the player was squished when we pause time, it looks weird
		Game.GetFirst<Player>()?.Squish = Vector2.One;
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
			Game.Destroy(this);
	}

	public override void Destroyed()
	{
		base.Destroyed();
		// TimeScale is released automatically (we owned the factor); just clean up our own state.
		Game.BurrowUpgradeHud = default;
		Game.HitStunActors.Remove(this);
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
