using System.Numerics;
using Foster.Framework;

namespace Myth;

public sealed class RadialWave : Actor
{
	const int SpokeCount = 192;
	const int ImpactBucketCount = 48;
	const string ImpactEffect = "radial_wave_impact";

	public Actor? Source;
	public Actor? Target;
	public Func<Actor, bool>? CanHit;
	public float Speed = 220;
	public float MaxRadius = 320;
	public float Thickness = 3.5f;
	public Color Color = new(0xff8fcf);
	public Color CoreColor = new(0xfff4fb);
	public bool Finished { get; private set; }

	readonly Vector2[] directions = new Vector2[SpokeCount];
	readonly bool[] blocked = new bool[SpokeCount];
	readonly bool[] impactBuckets = new bool[ImpactBucketCount];
	float radius;
	bool hitTarget;
	int activeSpokes = SpokeCount;
	int impactEffect = -1;

	static float CollisionStep => 2;

	public RadialWave()
	{
		Sprite = null;
		Mask = Masks.None;
		CollidesWithSolids = false;
		Depth = -100;
	}

	public override void Added()
	{
		base.Added();
		var phase = Game.rng.Angle();
		for (var i = 0; i < directions.Length; i++)
			directions[i] = Calc.AngleToVector(phase + i * MathF.Tau / directions.Length);
		impactEffect = Game.Particles?.AcquireEffect(ImpactEffect) ?? -1;
	}

	public override void Update()
	{
		base.Update();
		if (Finished)
			return;

		var previousRadius = radius;
		radius = MathF.Min(radius + Speed * Time.Delta, MaxRadius);
		Advance(previousRadius, radius);
		if (radius < MaxRadius && activeSpokes > 0)
			return;
		Finished = true;
		Game.Destroy(this);
	}

	void Advance(float fromRadius, float toRadius)
	{
		for (var i = 0; i < directions.Length; i++)
		{
			if (blocked[i])
				continue;
			var direction = directions[i];
			for (var distance = fromRadius; distance < toRadius;)
			{
				distance = MathF.Min(distance + CollisionStep, toRadius);
				var point = (Vector2)Position + direction * distance;
				var sample = SampleRect(point);
				if (HitsSolid(sample))
				{
					blocked[i] = true;
					activeSpokes--;
					EmitImpact(i, point - direction * CollisionStep, direction);
					break;
				}
				TryHitTarget(sample);
			}
		}
	}

	bool HitsSolid(RectInt sample)
		=> Room is { } room && !room.WorldBounds.Contains(sample) ||
			Game.OverlapsRoomCollision(sample) || Game.OverlapsFirst(sample, Masks.Solid) != null;

	void TryHitTarget(RectInt sample)
	{
		if (hitTarget || Source == null || Target == null || Game.destroying.Contains(Source) ||
			Game.destroying.Contains(Target) || !Target.OverlapsCombatBounds(sample) || CanHit?.Invoke(Target) == false)
			return;
		hitTarget = Source.Hit(Target);
	}

	void EmitImpact(int spoke, Vector2 point, Vector2 direction)
	{
		if (impactEffect < 0)
			return;
		var bucket = spoke * impactBuckets.Length / directions.Length;
		if (impactBuckets[bucket])
			return;
		impactBuckets[bucket] = true;
		Game.Particles?.Emit(impactEffect, point, 3, Calc.Angle(-direction), 1, 0.45f);
	}

	RectInt SampleRect(Vector2 point)
	{
		var size = Math.Max(1, (int)MathF.Ceiling(Thickness));
		return new((int)MathF.Floor(point.X - size * 0.5f),
			(int)MathF.Floor(point.Y - size * 0.5f), size, size);
	}

	public override void Render(Batcher batcher) => RenderWave(batcher, false);
	public override void RenderEmissive(Batcher batcher) => RenderWave(batcher, true);

	void RenderWave(Batcher batcher, bool emissive)
	{
		if (Finished || radius <= 0)
			return;
		var fade = 1 - Ease.Cube.In(Math.Clamp((radius / MaxRadius - 0.82f) / 0.18f, 0, 1));
		for (var i = 0; i < directions.Length; i++)
		{
			var next = (i + 1) % directions.Length;
			if (blocked[i] || blocked[next])
				continue;
			var from = directions[i] * radius;
			var to = directions[next] * radius;
			if (!emissive)
				batcher.Line(from, to, Thickness + 2.5f, Color * (fade * 0.32f));
			batcher.Line(from, to, emissive ? Thickness + 1 : Thickness,
				(emissive ? Color : CoreColor) * fade);
		}
	}

	public override void Destroyed()
	{
		Finished = true;
		Game.Particles?.ReleaseEffect(ref impactEffect);
	}

	public override void RenderNormals(Batcher batcher) { }
	public override void RenderShadow(Batcher batcher, Color color) { }
	public override void RenderReflection(Batcher batcher, Color color) { }
	public override Rect GetVisualBounds() => new(Position.X - radius, Position.Y - radius, radius * 2, radius * 2);
}
