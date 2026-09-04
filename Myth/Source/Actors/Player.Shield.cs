using System.Numerics;
using Foster.Framework;

namespace Myth;

// Shield: while the Shield control is held, an eased 0..1 amount ramps up and
// drives edge-flow particles that trace the player's silhouette (shield.json).
// The SDF field stays bound while the slot is held so releasing the button
// lets the remaining tracers ride the outline out instead of flying off.
public partial class Player
{
	static float ShieldEaseSpeed => 6f;
	static float ShieldEmitPerSecond => 48f; // at full amount
	static string ShieldEffect => "shield";

	float shieldAmount; // eased 0..1
	float shieldEmitRemainder;
	int shieldSlot = -1;

	public float ShieldAmount => shieldAmount;

	void UpdateShield(float dt)
	{
		var held = Controls.Shield.Down;
		var target = held ? 1f : 0f;
		shieldAmount += (target - shieldAmount) * (1f - MathF.Exp(-dt * ShieldEaseSpeed));

		if (Game.Particles is not { Supported: true } particles || Sprite is not { } sprite)
			return;
		if (shieldSlot < 0)
		{
			if (!held)
				return;
			shieldSlot = particles.AcquireEffectInstance(ShieldEffect);
			if (shieldSlot < 0)
				return;
		}

		var canvasOffset = Shift - sprite.Origin;
		particles.SetEffectField(shieldSlot, "player", AnimationFrameIndex, Position, canvasOffset, Facing.AsInt);

		shieldEmitRemainder += ShieldEmitPerSecond * shieldAmount * dt;
		var count = (int)shieldEmitRemainder;
		if (count > 0)
		{
			shieldEmitRemainder -= count;
			particles.EmitSpriteEdge(shieldSlot, "player", AnimationFrameIndex, Position, canvasOffset, Facing.AsInt, Velocity, count, dt);
		}
	}

	void ReleaseShield()
		=> ReleaseParticleEffect(ref shieldSlot);
}
