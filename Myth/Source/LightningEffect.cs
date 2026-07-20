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
