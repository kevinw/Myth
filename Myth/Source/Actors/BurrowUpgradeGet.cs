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
	float previousTimeScale;
	bool restored;

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
		// Freezing time zeroes Game.Time.Delta, so a hit-stun in progress can never drain and the
		// normal actor loop stops running -- we'd never advance to restore time. Update through the
		// hit-stun list instead, on real time, so the modal always completes.
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
		{
			// Restore inline (not just in Destroyed): a hit-stun can only drain once time is
			// running again, and RemoveDestroyedActors -- which invokes Destroyed -- doesn't run
			// while hit-stun is active. Unfreezing here lets the destroy actually get processed.
			Restore();
			Game.Destroy(this);
		}
	}

	// Safety net for the abnormal paths: player death and room transitions both mass-destroy every
	// other actor, and any of those could otherwise leave the sim frozen at TimeScale 0 -- stuck.
	public override void Destroyed()
	{
		base.Destroyed();
		Restore();
	}

	void Restore()
	{
		if (restored)
			return;
		restored = true;
		Game.BurrowUpgradeHud = default;
		Game.TimeScale = previousTimeScale;
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
