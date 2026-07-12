using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Foster.Framework;

namespace Myth;

public enum ParticleEmitterShape
{
	Point,
	Circle,
	Cone,
	Line,
	Points, // explicit per-frame point list (code-driven; not authorable as an emitter shape)
}

public enum ParticleRenderStyle
{
	Square,
	Plasma,
}

public enum ParticleEmissionMode
{
	Continuous,
	Burst,
}

public enum ParticleCollisionMode
{
	None,
	Kill,
	Settle,
	Bounce,
}

// Which part of a sprite's border EmitSpriteEdge spawns from.
public enum ParticleEdgeSide
{
	FacingMotion,   // border normals aligned with the emitter's velocity (windward)
	FixedDirection, // border normals near EdgeSideDegrees, in sprite-local space (flip-aware)
	All,            // the whole silhouette
}

public struct TrailColorKey
{
	public float T;
	public Color Color;
}

/// A named, authorable particle effect asset: emitter shape, dynamics, and
/// size/color over lifetime. Loaded/saved as JSON by <see cref="ParticleEffectAssets"/>;
/// <c>ParticleEmitter</c> actors reference one by name.
public sealed class ParticleEffect
{
	// Emitter
	public ParticleEmitterShape Shape = ParticleEmitterShape.Cone;
	public float EmitterRadius = 4.0f;
	public float DirectionDegrees = -90.0f; // screen space, -90 = up
	public float SpreadDegrees = 40.0f;
	public int BurstCount = 256;
	public ParticleEmissionMode Mode = ParticleEmissionMode.Continuous;
	public float EmissionRate = 120.0f; // particles/sec (Continuous)
	public bool Prewarm;

	// Dynamics
	public float SpeedMin = 20.0f;
	public float SpeedMax = 120.0f;
	public float LifeMin = 0.6f;
	public float LifeMax = 1.6f;
	public float Gravity = 60.0f;
	public float Drag = 0.6f;
	public float NoiseAmount = 40.0f;
	public float NoiseScale = 0.05f;

	// Appearance
	public float SizeStart = 2.0f;
	public float SizeEnd = 0.5f;
	public bool Additive = true;
	public ParticleRenderStyle Style;

	// Collision (against the foreground solid mask)
	public ParticleCollisionMode Collision = ParticleCollisionMode.None;
	public float Bounciness = 0.4f;

	// Trail (a ribbon of recent positions behind each particle; 0 = no trail)
	public float TrailSeconds;
	public float TrailWidthStart = 2.0f;
	public float TrailWidthEnd = 0.0f;

	// Sprite-edge emission (used by EmitSpriteEdge; see SpriteSdf for which
	// sprites have baked borders). Attraction pulls particles back onto the
	// emitting sprite's silhouette per second (0 = shed freely; ~60+ snaps
	// them onto it every frame); Flow drives them along it (px/s, sign =
	// direction). Flow needs Attraction > 0 to stay on the edge.
	public ParticleEdgeSide EdgeSide = ParticleEdgeSide.FacingMotion;
	public float EdgeSideDegrees = -90.0f; // sprite-local, -90 = top edge
	public float EdgeArcDegrees = 120.0f;  // how wide a band of border normals qualifies
	public float EdgeAttraction;
	public float EdgeFlow;

	// Color over life: birth -> mid -> death.
	public Color ColorBirth = new(1.0f, 0.95f, 0.6f, 1.0f);
	public Color ColorMid = new(1.0f, 0.5f, 0.15f, 1.0f);
	public Color ColorEnd = new(0.3f, 0.05f, 0.05f, 0.0f);

	// Trail color at keys along the trail (0 = at the particle, 1 = tail end),
	// sorted by T. Empty = reuse the particle's own gradient.
	public List<TrailColorKey> TrailColors = [];

	/// Samples the birth -> mid -> death gradient at t in [0, 1].
	public Color EvaluateColor(float t)
	{
		t = Math.Clamp(t, 0.0f, 1.0f);
		return t < 0.5f
			? new Color(Vector4.Lerp(ColorBirth, ColorMid, t * 2.0f))
			: new Color(Vector4.Lerp(ColorMid, ColorEnd, (t - 0.5f) * 2.0f));
	}

	/// Samples the trail gradient at t in [0, 1] (0 = head, 1 = tail).
	public Color EvaluateTrailColor(float t)
	{
		if (TrailColors.Count == 0)
			return EvaluateColor(t);
		t = Math.Clamp(t, 0.0f, 1.0f);
		if (t <= TrailColors[0].T)
			return TrailColors[0].Color;
		for (var i = 1; i < TrailColors.Count; i++)
		{
			var (a, b) = (TrailColors[i - 1], TrailColors[i]);
			if (t <= b.T)
				return new Color(Vector4.Lerp(a.Color, b.Color, (t - a.T) / MathF.Max(1e-5f, b.T - a.T)));
		}
		return TrailColors[^1].Color;
	}

	/// Hash of the data that affects the baked color LUT.
	public int ColorHash()
	{
		var hash = HashCode.Combine(ColorBirth.RGBA, ColorMid.RGBA, ColorEnd.RGBA);
		foreach (var key in TrailColors)
			hash = HashCode.Combine(hash, key.T, key.Color.RGBA);
		return hash;
	}

	public JsonObject ToJson()
		=> JsonSerializer.SerializeToNode(this, ParticleEffectJsonContext.Default.ParticleEffect)?.AsObject() ?? [];

	public static ParticleEffect FromJson(JsonObject json)
		=> json.Deserialize(ParticleEffectJsonContext.Default.ParticleEffect) ?? new();
}

[JsonSourceGenerationOptions(IncludeFields = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(ParticleEffect))]
internal partial class ParticleEffectJsonContext : JsonSerializerContext;
