using System.Numerics;
using Foster.Framework;

namespace Myth;

public readonly record struct LightningEffectPoint(Point2 Position, float Intensity);

public static class LightningEffect
{
	static float StepLength => 4f;
	static float JitterAmount => 3f;
	static float JitterRate => 12f;
	static Color OuterColor => new(0x287cff);
	static Color InnerColor => new(0x79cfff);

	// Points are in the Batcher's current coordinate space. Pass Time.SecondsF for time.
	public static void Render(Batcher batcher, ReadOnlySpan<LightningEffectPoint> points, float time, int seed = 0)
	{
		if (points.Length < 2)
			return;

		var animation = time * JitterRate;
		var frame = (int)MathF.Floor(animation);
		var blend = animation - frame;
		blend *= blend * (3 - 2 * blend);
		for (var i = 1; i < points.Length; i++)
			RenderSegment(batcher, points[i - 1], points[i], frame, blend, Hash((uint)seed ^ (uint)i * 0x9e3779b9u));
	}

	// Fills a 1-tile-thick band from `from` to `to` with several weaving, crossing filaments over a
	// soft glow, for a beam/wall of lightning (distinct from Render's single travelling bolt).
	// `halfWidth` is half the band's perpendicular thickness. The beam can be lit partially from
	// each end: `reachFromStart`/`reachFromEnd` are 0..1 fractions of the length lit inward from
	// `from` (the emitter) and `to`, and `startGlow`/`endGlow` scale each end's brightness — so a
	// gather can spark in from both ends and connect. Points are in the Batcher's current
	// coordinate space. Pass Time.SecondsF for time.
	public static void RenderBeam(Batcher batcher, Vector2 from, Vector2 to, float halfWidth,
		float time, int seed, float intensity = 1f,
		float reachFromStart = 1f, float reachFromEnd = 1f, float startGlow = 1f, float endGlow = 1f)
	{
		var delta = to - from;
		var length = delta.Length();
		if (length < 0.5f || intensity <= 0.001f)
			return;

		var tangent = delta / length;
		var normal = new Vector2(-tangent.Y, tangent.X);

		DrawGlowBand(batcher, from, tangent, normal * halfWidth, length,
			intensity, reachFromStart, reachFromEnd, startGlow, endGlow);

		var animation = time * JitterRate;
		var frame = (int)MathF.Floor(animation);
		var blend = animation - frame;
		blend *= blend * (3 - 2 * blend);

		const int filaments = 5;
		for (var s = 0; s < filaments; s++)
			RenderFilament(batcher, from, tangent, normal, length, halfWidth, frame, blend,
				Hash((uint)seed ^ (uint)(s + 1) * 0x9e3779b9u),
				intensity, reachFromStart, reachFromEnd, startGlow, endGlow);
	}

	// Local brightness at fraction `a` along the beam, from whichever end's light has reached it.
	static float BeamCoverage(float a, float reachFromStart, float reachFromEnd, float startGlow, float endGlow)
	{
		const float feather = 0.14f;
		var s = Smoothstep((reachFromStart - a) / feather) * startGlow;
		var e = Smoothstep((reachFromEnd - (1 - a)) / feather) * endGlow;
		return MathF.Max(s, e);
	}

	static float Smoothstep(float x)
	{
		x = Math.Clamp(x, 0, 1);
		return x * x * (3 - 2 * x);
	}

	static void DrawGlowBand(Batcher batcher, Vector2 from, Vector2 tangent, Vector2 normal,
		float length, float intensity, float reachFromStart, float reachFromEnd, float startGlow, float endGlow)
	{
		var fade = OuterColor * 0f;
		var segments = Math.Max(1, (int)(length / 6));
		var previous = from;
		var previousColor = OuterColor * (0.22f * intensity * BeamCoverage(0, reachFromStart, reachFromEnd, startGlow, endGlow));
		for (var i = 1; i <= segments; i++)
		{
			var a = i / (float)segments;
			var point = from + tangent * (length * a);
			var color = OuterColor * (0.22f * intensity * BeamCoverage(a, reachFromStart, reachFromEnd, startGlow, endGlow));
			batcher.Quad(previous, point, point - normal, previous - normal, previousColor, color, fade, fade);
			batcher.Quad(previous, point, point + normal, previous + normal, previousColor, color, fade, fade);
			previous = point;
			previousColor = color;
		}
	}

	static void RenderFilament(Batcher batcher, Vector2 from, Vector2 tangent, Vector2 normal,
		float length, float halfWidth, int frame, float blend, uint seed, float intensity,
		float reachFromStart, float reachFromEnd, float startGlow, float endGlow)
	{
		var steps = Math.Max(3, (int)MathF.Ceiling(length / StepLength));
		// Each filament weaves across the whole band at its own frequency/phase, so they cross.
		var waves = 1.5f + (seed & 3);
		var phase = (seed >> 3 & 0xff) / 255f * Calc.TAU;
		var sway = Calc.Lerp(0.4f, 0.9f, (seed >> 11 & 0xff) / 255f);
		var previous = from;
		var previousIntensity = 0f;
		for (var i = 0; i <= steps; i++)
		{
			var a = i / (float)steps;
			var lateral = MathF.Sin(a * MathF.PI * waves + phase) * halfWidth * sway;
			if (i > 0 && i < steps)
				lateral += AnimatedNoise(seed, i, frame, blend, 0) * JitterAmount;
			var point = from + tangent * (length * a) + normal * lateral;
			point += tangent * AnimatedNoise(seed, i, frame, blend, 1) * JitterAmount * 0.3f;
			point = point.Round();

			var coverage = BeamCoverage(a, reachFromStart, reachFromEnd, startGlow, endGlow) * intensity;
			var flicker = Calc.Lerp(0.7f, 1f, AnimatedNoise(seed, i, frame, blend, 2) * 0.5f + 0.5f);
			if (i > 0)
				RenderLine(batcher, previous, point, previousIntensity, coverage, flicker);
			previous = point;
			previousIntensity = coverage;
		}
	}

	static void RenderSegment(Batcher batcher, LightningEffectPoint start, LightningEffectPoint end,
		int frame, float blend, uint seed)
	{
		var from = (Vector2)start.Position;
		var to = (Vector2)end.Position;
		var delta = to - from;
		var length = delta.Length();
		if (length <= 0)
			return;

		var tangent = delta / length;
		var normal = new Vector2(-tangent.Y, tangent.X);
		var steps = Math.Max(1, (int)MathF.Ceiling(length / StepLength));
		var previous = from;
		var previousIntensity = Math.Clamp(start.Intensity, 0, 1);
		for (var i = 1; i <= steps; i++)
		{
			var amount = i / (float)steps;
			var intensity = Math.Clamp(Calc.Lerp(start.Intensity, end.Intensity, amount), 0, 1);
			var point = Vector2.Lerp(from, to, amount);
			if (i < steps)
			{
				var envelope = MathF.Sin(amount * MathF.PI);
				var strength = JitterAmount * envelope * Calc.Lerp(0.45f, 1f, intensity);
				point += normal * AnimatedNoise(seed, i, frame, blend, 0) * strength;
				point += tangent * AnimatedNoise(seed, i, frame, blend, 1) * strength * 0.35f;
				point = point.Round();
			}

			RenderLine(batcher, previous, point, previousIntensity, intensity,
				Calc.Lerp(0.86f, 1f, AnimatedNoise(seed, i, frame, blend, 2) * 0.5f + 0.5f));
			previous = point;
			previousIntensity = intensity;
		}
	}

	static void RenderLine(Batcher batcher, Vector2 from, Vector2 to, float fromIntensity, float toIntensity, float flicker)
	{
		fromIntensity *= flicker;
		toIntensity *= flicker;
		var intensity = (fromIntensity + toIntensity) * 0.5f;
		if (intensity <= 0.001f)
			return;

		batcher.Line(from, to, Calc.Lerp(1.5f, 3.5f, intensity),
			OuterColor * (fromIntensity * 0.52f), OuterColor * (toIntensity * 0.52f));
		batcher.Line(from, to, Calc.Lerp(0.8f, 1.6f, intensity),
			InnerColor * fromIntensity, InnerColor * toIntensity);
		batcher.Line(from, to, Calc.Lerp(0.45f, 0.85f, intensity),
			Color.White * fromIntensity, Color.White * toIntensity);
	}

	static float AnimatedNoise(uint seed, int point, int frame, float blend, int channel)
	{
		var key = seed ^ (uint)point * 0x85ebca6bu ^ (uint)channel * 0xc2b2ae35u;
		var a = SignedNoise(key ^ unchecked((uint)frame) * 0x27d4eb2du);
		var b = SignedNoise(key ^ unchecked((uint)(frame + 1)) * 0x27d4eb2du);
		return Calc.Lerp(a, b, blend);
	}

	static float SignedNoise(uint value) => (Hash(value) & 0xffff) / 32767.5f - 1f;

	static uint Hash(uint value)
	{
		value ^= value >> 16;
		value *= 0x7feb352du;
		value ^= value >> 15;
		value *= 0x846ca68bu;
		return value ^ value >> 16;
	}
}
