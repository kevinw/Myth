using System.Numerics;
using System.Text.Json.Nodes;
using Foster.Framework;

namespace Myth;

public enum ParticleEmitterShape
{
	Point,
	Circle,
	Cone,
	Line,
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

	public JsonObject ToJson()
		=> new()
		{
			["Shape"] = Shape.ToString(),
			["EmitterRadius"] = EmitterRadius,
			["DirectionDegrees"] = DirectionDegrees,
			["SpreadDegrees"] = SpreadDegrees,
			["BurstCount"] = BurstCount,
			["Mode"] = Mode.ToString(),
			["EmissionRate"] = EmissionRate,
			["Prewarm"] = Prewarm,
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
			["Style"] = Style.ToString(),
			["Collision"] = Collision.ToString(),
			["Bounciness"] = Bounciness,
			["ColorBirth"] = JsonUtil.ColorToHex(ColorBirth),
			["ColorMid"] = JsonUtil.ColorToHex(ColorMid),
			["ColorEnd"] = JsonUtil.ColorToHex(ColorEnd),
		};

	public static ParticleEffect FromJson(JsonObject json)
	{
		var effect = new ParticleEffect();
		if (JsonUtil.ReadString(json, "Shape") is { } shape)
			effect.Shape = shape switch
			{
				"Point" => ParticleEmitterShape.Point,
				"Circle" => ParticleEmitterShape.Circle,
				"Cone" => ParticleEmitterShape.Cone,
				"Line" => ParticleEmitterShape.Line,
				_ => effect.Shape,
			};
		effect.EmitterRadius = JsonUtil.ReadFloat(json, "EmitterRadius", effect.EmitterRadius);
		effect.DirectionDegrees = JsonUtil.ReadFloat(json, "DirectionDegrees", effect.DirectionDegrees);
		effect.SpreadDegrees = JsonUtil.ReadFloat(json, "SpreadDegrees", effect.SpreadDegrees);
		effect.BurstCount = JsonUtil.ReadInt(json, "BurstCount", effect.BurstCount);
		if (JsonUtil.ReadString(json, "Mode") is { } mode)
			effect.Mode = mode == "Burst" ? ParticleEmissionMode.Burst : effect.Mode;
		effect.EmissionRate = JsonUtil.ReadFloat(json, "EmissionRate", effect.EmissionRate);
		effect.Prewarm = JsonUtil.ReadBool(json, "Prewarm", effect.Prewarm);
		effect.SpeedMin = JsonUtil.ReadFloat(json, "SpeedMin", effect.SpeedMin);
		effect.SpeedMax = JsonUtil.ReadFloat(json, "SpeedMax", effect.SpeedMax);
		effect.LifeMin = JsonUtil.ReadFloat(json, "LifeMin", effect.LifeMin);
		effect.LifeMax = JsonUtil.ReadFloat(json, "LifeMax", effect.LifeMax);
		effect.Gravity = JsonUtil.ReadFloat(json, "Gravity", effect.Gravity);
		effect.Drag = JsonUtil.ReadFloat(json, "Drag", effect.Drag);
		effect.NoiseAmount = JsonUtil.ReadFloat(json, "NoiseAmount", effect.NoiseAmount);
		effect.NoiseScale = JsonUtil.ReadFloat(json, "NoiseScale", effect.NoiseScale);
		effect.SizeStart = JsonUtil.ReadFloat(json, "SizeStart", effect.SizeStart);
		effect.SizeEnd = JsonUtil.ReadFloat(json, "SizeEnd", effect.SizeEnd);
		effect.Additive = JsonUtil.ReadBool(json, "Additive", effect.Additive);
		if (JsonUtil.ReadString(json, "Style") is { } style)
			effect.Style = style == "Plasma" ? ParticleRenderStyle.Plasma : effect.Style;
		if (JsonUtil.ReadString(json, "Collision") is { } collision)
			effect.Collision = collision switch
			{
				"Kill" => ParticleCollisionMode.Kill,
				"Settle" => ParticleCollisionMode.Settle,
				"Bounce" => ParticleCollisionMode.Bounce,
				_ => effect.Collision,
			};
		effect.Bounciness = JsonUtil.ReadFloat(json, "Bounciness", effect.Bounciness);
		effect.ColorBirth = JsonUtil.ReadColor(json, "ColorBirth") ?? effect.ColorBirth;
		effect.ColorMid = JsonUtil.ReadColor(json, "ColorMid") ?? effect.ColorMid;
		effect.ColorEnd = JsonUtil.ReadColor(json, "ColorEnd") ?? effect.ColorEnd;
		return effect;
	}
}
