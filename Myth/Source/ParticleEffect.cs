using System.Numerics;
using System.Text.Json.Nodes;
using Foster.Framework;

namespace Myth;

public enum ParticleEmitterShape
{
	Point,
	Circle,
	Cone,
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

	// Collision (against the foreground solid mask)
	public ParticleCollisionMode Collision = ParticleCollisionMode.None;
	public float Bounciness = 0.4f;

	// Color over life: birth -> mid -> death.
	public Color ColorBirth = new(1.0f, 0.95f, 0.6f, 1.0f);
	public Color ColorMid = new(1.0f, 0.5f, 0.15f, 1.0f);
	public Color ColorEnd = new(0.3f, 0.05f, 0.05f, 0.0f);

	/// Samples the birth -> mid -> death gradient at t in [0, 1].
	public Color EvaluateColor(float t)
	{
		t = Math.Clamp(t, 0.0f, 1.0f);
		return t < 0.5f
			? new Color(Vector4.Lerp(ColorBirth, ColorMid, t * 2.0f))
			: new Color(Vector4.Lerp(ColorMid, ColorEnd, (t - 0.5f) * 2.0f));
	}

	/// Hash of the data that affects the baked color LUT.
	public int ColorHash()
		=> HashCode.Combine(ColorBirth.RGBA, ColorMid.RGBA, ColorEnd.RGBA);

	public JsonObject ToJson() => new()
	{
		["Shape"] = Shape.ToString(),
		["EmitterRadius"] = EmitterRadius,
		["DirectionDegrees"] = DirectionDegrees,
		["SpreadDegrees"] = SpreadDegrees,
		["BurstCount"] = BurstCount,
		["Mode"] = Mode.ToString(),
		["EmissionRate"] = EmissionRate,
		["SpeedMin"] = SpeedMin,
		["SpeedMax"] = SpeedMax,
		["LifeMin"] = LifeMin,
		["LifeMax"] = LifeMax,
		["Gravity"] = Gravity,
		["Drag"] = Drag,
		["NoiseAmount"] = NoiseAmount,
		["NoiseScale"] = NoiseScale,
		["SizeStart"] = SizeStart,
		["SizeEnd"] = SizeEnd,
		["Additive"] = Additive,
		["Collision"] = Collision.ToString(),
		["Bounciness"] = Bounciness,
		["ColorBirth"] = JsonUtil.ColorToHex(ColorBirth),
		["ColorMid"] = JsonUtil.ColorToHex(ColorMid),
		["ColorEnd"] = JsonUtil.ColorToHex(ColorEnd),
	};

	public static ParticleEffect FromJson(JsonObject json)
	{
		var e = new ParticleEffect();
		e.Load(json);
		return e;
	}

	/// Overwrites this instance's fields from JSON in place. Used so undo/redo
	/// restores the shared asset object that emitters already reference.
	public void Load(JsonObject json)
	{
		EmitterRadius = JsonUtil.ReadFloat(json, "EmitterRadius", EmitterRadius);
		DirectionDegrees = JsonUtil.ReadFloat(json, "DirectionDegrees", DirectionDegrees);
		SpreadDegrees = JsonUtil.ReadFloat(json, "SpreadDegrees", SpreadDegrees);
		BurstCount = JsonUtil.ReadInt(json, "BurstCount", BurstCount);
		EmissionRate = JsonUtil.ReadFloat(json, "EmissionRate", EmissionRate);
		SpeedMin = JsonUtil.ReadFloat(json, "SpeedMin", SpeedMin);
		SpeedMax = JsonUtil.ReadFloat(json, "SpeedMax", SpeedMax);
		LifeMin = JsonUtil.ReadFloat(json, "LifeMin", LifeMin);
		LifeMax = JsonUtil.ReadFloat(json, "LifeMax", LifeMax);
		Gravity = JsonUtil.ReadFloat(json, "Gravity", Gravity);
		Drag = JsonUtil.ReadFloat(json, "Drag", Drag);
		NoiseAmount = JsonUtil.ReadFloat(json, "NoiseAmount", NoiseAmount);
		NoiseScale = JsonUtil.ReadFloat(json, "NoiseScale", NoiseScale);
		SizeStart = JsonUtil.ReadFloat(json, "SizeStart", SizeStart);
		SizeEnd = JsonUtil.ReadFloat(json, "SizeEnd", SizeEnd);
		Additive = JsonUtil.ReadBool(json, "Additive", Additive);
		Bounciness = JsonUtil.ReadFloat(json, "Bounciness", Bounciness);
		if (Enum.TryParse<ParticleEmitterShape>(JsonUtil.ReadString(json, "Shape"), out var shape))
			Shape = shape;
		if (Enum.TryParse<ParticleEmissionMode>(JsonUtil.ReadString(json, "Mode"), out var mode))
			Mode = mode;
		if (Enum.TryParse<ParticleCollisionMode>(JsonUtil.ReadString(json, "Collision"), out var collision))
			Collision = collision;
		if (JsonUtil.ReadColor(json, "ColorBirth") is { } cb) ColorBirth = cb;
		if (JsonUtil.ReadColor(json, "ColorMid") is { } cm) ColorMid = cm;
		if (JsonUtil.ReadColor(json, "ColorEnd") is { } ce) ColorEnd = ce;
	}
}
