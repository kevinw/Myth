using Foster.Framework;

namespace Myth;

/// <summary>
/// Placeable particle emitter. References a named <see cref="ParticleEffect"/>
/// asset (pure reference - all emitters using the same name share one GPU slot
/// and retune together). On spawn it acquires a slot and emits at its world
/// position; continuous effects stream, burst effects fire once. Previews when
/// selected in the editor.
/// </summary>
[GameActor(Sprite = "fire", OffsetX = 4, OffsetY = 4, BoundsX = -4, BoundsY = -4, BoundsW = 8, BoundsH = 8, Tags = ActorTag.System)]
public class ParticleEmitter : Actor
{
	public string EffectName = "sparks";

	[NoSerialize] private int effectIndex = -1;
	[NoSerialize] private float emitAccumulator;
	[NoSerialize] private bool burstFired;

	public ParticleEmitter()
	{
		Hitbox = new(new RectInt(-4, -4, 8, 8));
		CollidesWithSolids = false;
		Depth = 1;
	}

	public override bool CastsSpriteShadow => false;
	public override bool CastsReflection => false;

	public override void Added()
	{
		if (Game.Particles is { Supported: true } system)
			effectIndex = system.AcquireEffect(EffectName);
	}

	public override void Destroyed()
	{
		if (effectIndex >= 0 && Game.Particles is { } system)
			system.ReleaseEffect(effectIndex);
		effectIndex = -1;
	}

	public override void Update()
	{
		base.Update();
		EmitStep(Time.Delta);
	}

	/// <summary>
	/// Advances emission by dt. Called from Update during gameplay and from the
	/// editor preview loop (where actor Update does not run).
	/// </summary>
	public void EmitStep(float dt)
	{
		if (effectIndex < 0 || Game.Particles is not { } system)
			return;

		var effect = ParticleEffectAssets.Get(EffectName);

		int count;
		if (effect.Mode == ParticleEmissionMode.Burst)
		{
			if (burstFired)
				return;
			burstFired = true;
			count = effect.BurstCount;
		}
		else
		{
			emitAccumulator += effect.EmissionRate * dt;
			count = (int)emitAccumulator;
			emitAccumulator -= count;
		}

		if (count > 0)
			system.Emit(effectIndex, Position, count);
	}

	public void TestBurst()
	{
		if (effectIndex >= 0 && Game.Particles is { } system)
			system.Emit(effectIndex, Position, ParticleEffectAssets.Get(EffectName).BurstCount);
	}

	// The particles are the visual; the actor itself draws nothing in-world
	// (the editor draws its own selection handles from the actor bounds).
	public override void Render(Batcher batcher) { }
	public override void RenderNormals(Batcher batcher) { }
	public override void RenderEmissive(Batcher batcher) { }
	public override void RenderShadow(Batcher batcher, Color color) { }
	public override void RenderReflection(Batcher batcher, Color color) { }
}
