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
		var step = MathF.Tau / StampsPerRing;

		for (var radius = leadRadius; radius <= MaxRadius; radius += RingSpacing)
		{
			if (radius <= 0.5f)
				continue;

			var fade = 1f - radius / MaxRadius; // dissipate as the ring expands
			var strength = Strength * fade;
			if (strength <= 0.001f)
				continue;

			// Keep the ring continuous as its circumference grows.
			var gap = MathF.Tau * radius / StampsPerRing;
			var stampRadius = MathF.Max(Thickness, gap * 0.6f);
			// Offset each ring's spokes so successive rings don't line up radially.
			var phase = radius * 0.05f;

			for (var i = 0; i < StampsPerRing; i++)
			{
				var angle = i * step + phase;
				var point = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
				if (!context.AddStamp(point, point, stampRadius, strength))
					return; // budget exhausted
			}
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
