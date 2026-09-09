using System.Numerics;
using Foster.Framework;

namespace Myth;

// Emits outward-travelling concentric ripples into any neural texture it overlaps. Each ripple
// is a ring of small erasure stamps at an expanding radius; the CA heals behind the ring between
// steps, so a moving ring reads as a wave. New rings spawn on a fixed spacing, giving continuous
// concentric ripples that fade as they reach MaxRadius. Placement point (Center anchor) is the
// ripple origin.
[GameActor(Sprite = "Editor/neural_texture_ripple", BoundsW = Game.TileSize * 2, BoundsH = Game.TileSize * 2,
	Anchor = SpriteAnchor.Center, Tags = ActorTag.System, LivePositionEditable = true)]
public class NeuralTextureRippleActor : Actor, INeuralTextureAgent
{
	[ActorRef] public string? SourceActorId;
	bool sourceWasActive;
	float sourceStoppedAt = -1;
	float sourceStoppedLeadRadius;
	// World units a ring travels per second.
	[I(10, 200)] public float RingSpeed = 70f;
	// World-unit gap between successive rings; the ripple period is RingSpacing / RingSpeed.
	[I(8, 128)] public float RingSpacing = 44f;
	// Rings fade to nothing and recycle once they reach this radius from the origin.
	[I(32, 320)] public float MaxRadius = DefaultMaxRadius;
	// Peak erase strength at the origin, before the per-ring distance fade (clamped to 1 in shader).
	[I(0.05f, 1)] public float Strength = 0.5f;
	// Base ring thickness (stamp radius). Rings also thicken as they expand so they stay unbroken.
	[I(1, 16)] public float Thickness = 5f;
	// Stamps placed around each ring. More closes gaps on large rings at the cost of the shared
	// 64-stamp perturbation budget.
	[I(4, 32)] public int StampsPerRing = 16;
	static float DefaultMaxRadius => 130f;

	public NeuralTextureRippleActor()
	{
		Visible = false;
		CollidesWithSolids = false;
		Hitbox = new(new RectInt(0, 0, 0, 0));
	}

	public void PerturbNeuralTexture(NeuralTextureActor.PerturbContext context)
	{
		if (RingSpeed <= 0 || RingSpacing <= 0 || MaxRadius <= 0 || StampsPerRing <= 0 || Strength <= 0)
			return;

		var now = Time.SecondsF;
		var sourceActive = SourceActorId == null || Game.GetActorById(SourceActorId) != null;
		if (sourceActive)
		{
			sourceWasActive = true;
			sourceStoppedAt = -1;
		}
		else if (!sourceWasActive)
			return;
		else if (sourceStoppedAt < 0)
		{
			sourceStoppedAt = now;
			sourceStoppedLeadRadius = (now * RingSpeed) % RingSpacing;
		}

		var center = (Vector2)Position;
		// Cull textures the ripple can't reach (loose corner test, but cheap and safe).
		if (!context.Texture.GetVisualBounds().Inflate(MaxRadius).Contains(center))
			return;

		// Leading ring's distance past the last spawn boundary; the rest trail by RingSpacing.
		var leadRadius = sourceStoppedAt < 0
			? (now * RingSpeed) % RingSpacing
			: sourceStoppedLeadRadius + (now - sourceStoppedAt) * RingSpeed;

		for (var radius = leadRadius; radius <= MaxRadius; radius += RingSpacing)
		{
			PerturbRing(context, center, radius, MaxRadius, Strength, Thickness, StampsPerRing);
			if (context.IsFull)
				return;
		}
	}

	public static void PerturbRing(NeuralTextureActor.PerturbContext context, Vector2 center,
		float radius, float maxRadius, float strength, float thickness, int stampCount,
		bool fadeWithRadius = true, float stampRadiusMultiplier = 0.6f,
		float arcAngle = MathF.Tau, float direction = 0, float? previousRadius = null, float push = 0)
	{
		if (radius <= 0.5f || radius > maxRadius || (radius == maxRadius && !previousRadius.HasValue) ||
			(strength <= 0 && push <= 0) || thickness <= 0 || stampCount <= 0 ||
			!context.Texture.GetVisualBounds().Inflate(maxRadius).Contains(center))
			return;

		if (fadeWithRadius)
		{
			strength *= 1 - radius / maxRadius;
			push *= 1 - radius / maxRadius;
		}
		if (strength <= 0.001f && push <= 0.001f)
			return;

		var arc = arcAngle < MathF.Tau;
		var step = arcAngle / stampCount;
		var gap = step * radius;
		var stampRadius = MathF.Max(thickness, gap * stampRadiusMultiplier);
		var phase = arc ? direction - arcAngle * 0.5f + step * 0.5f : radius * 0.05f;
		for (var i = 0; i < stampCount; i++)
		{
			var angle = i * step + phase;
			var ray = Calc.AngleToVector(angle);
			var point = center + ray * radius;
			var from = center + ray * (previousRadius ?? radius);
			var taper = arc ? MathF.Sin((i + 0.5f) / stampCount * MathF.PI) : 1f;
			if (!context.AddStamp(from, point, stampRadius, strength * taper * taper, ray * (push * taper * taper)))
				return;
		}
	}

#if EDITOR
	sealed class Editor : Manager.ActorEditor<NeuralTextureRippleActor>
	{
		internal override void Gizmos(Manager.EditorActor actor, ref Manager.EditorGizmos gizmos)
		{
			var center = actor.Bounds.CenterF;
			var radius = ActorSpecs.GetResolvedProperty(actor.Json, nameof(MaxRadius))?.GetValue<float>() ?? DefaultMaxRadius;
			var color = new Color(0x8de8ff);
			gizmos.CircleDashed(center, radius, gizmos.LineWeight * 2, 64, color * 0.8f,
				MathF.Max(3, gizmos.LineWeight * 6));

			var centerMark = MathF.Max(3, gizmos.LineWeight * 3);
			gizmos.Line(center - Vector2.UnitX * centerMark, center + Vector2.UnitX * centerMark, gizmos.LineWeight, color);
			gizmos.Line(center - Vector2.UnitY * centerMark, center + Vector2.UnitY * centerMark, gizmos.LineWeight, color);
		}
	}
#endif
}
