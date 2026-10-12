using System.Numerics;
using Foster.Framework;

namespace Myth;

// A horizontally-contiguous run of exposed water-surface tiles with a 1-D wave
// simulation along its top edge. Built per room during geometry rebuild (see
// Room.BuildWaterSurfaces); the sim arrays are transient and rebuilt fresh on room
// load, so there's no stale state to carry across transitions.
//
// The surface is sampled at a fixed pixel spacing. Each sample carries a vertical
// displacement (height, +Y = downward dip) and velocity, coupled to its neighbors as
// a discrete wave equation so a splash dips the surface locally and the ripple spreads
// and settles. Rendering fills a translucent body from the displaced top edge down to
// each column's floor, with a brighter foam line along the top.
public sealed class WaterSurface
{
	public const float SampleSpacing = 2f;                 // world px between samples
	const int SamplesPerTile = (int)(Game.TileSize / SampleSpacing);

	// Wave tuning. These are feel-knobs; expect to tweak once it's on screen.
	static float Spread => 700f;      // neighbor tension coupling (wave propagation speed)
	static float SpringK => 50f;     // restoring pull toward the flat rest line
	static float Damping => 0.5f;    // exponential velocity damping (per second)
	static float MaxHeight => 5.5f;    // clamp on displacement so a big plunge can't tear it
	static int SplashKernel => 5;    // samples each side of the crossing point that get kicked
	static float SplashVelPerSpeed => 0.65f; // crossing speed -> injected velocity magnitude
	static float MaxSplashVel => 90f;
	static int SpongeSamples => 8;     // width of the edge absorption layer, in samples (~1 tile)
	static float SpongeStrength => 10f; // extra per-second attenuation at the very edge, easing to 0 inward
	static float MaxSimulationStep => 1f / 60f;

	// Ambient idle motion, added at render time only (kept out of the sim so still water
	// keeps drifting instead of decaying dead-flat).
	static float AmbientAmplitude => 0.0005f;
	static float AmbientWavelength => 22f;
	static float AmbientSpeed => 11f;

	// Flat, uniform tint (straight alpha). Deliberately no vertical gradient: a depth-based
	// "lighting" gradient didn't match the scene's real lights and produced harsh vertical
	// seams on exposed shafts. One color for the whole body, exposed or capped; the only
	// bright accent is the thin foam line riding the animated surface.
	public static readonly Color BodyColor = new(0.09f, 0.28f, 0.52f, 0.5f);
	public static readonly Color FoamColor = new(0.35f, 0.65f, 0.9f, 0.5f);

	public readonly int Left;        // world X of sample 0
	public float SurfaceY;           // rest world Y of the top edge
	public Color Body = BodyColor;
	public Color Foam = FoamColor;
	public float LipCrawl;           // 0..1: how much the foam line flickers in brightness and thickness
	readonly float[] height;
	readonly float[] velocity;
	readonly float[] bottom;         // per-segment floor world Y at its left sample
	readonly float[] bottomRight;    // ...and at its right sample, so a floor can follow a slope
	readonly bool[] lip;             // per-segment foam line

	public int SampleCount => height.Length;
	public float Right => Left + (SampleCount - 1) * SampleSpacing;

	// tileBottomsWorldY: one floor-Y per tile column of the run, left to right.
	public WaterSurface(int left, float surfaceY, ReadOnlySpan<float> tileBottomsWorldY)
	{
		Left = left;
		SurfaceY = surfaceY;

		var widthTiles = tileBottomsWorldY.Length;
		var count = widthTiles * SamplesPerTile + 1; // +1 so the right edge is included
		height = new float[count];
		velocity = new float[count];
		bottom = new float[count];
		bottomRight = new float[count];
		lip = new bool[count];
		for (var i = 0; i < count; i++)
		{
			var tile = Math.Min(i / SamplesPerTile, widthTiles - 1);
			bottom[i] = bottomRight[i] = tileBottomsWorldY[tile];
			lip[i] = bottom[i] > surfaceY;
		}
	}

	public bool ContainsX(float worldX) => worldX >= Left && worldX <= Right;

	public float TopAt(float worldX, float time)
		=> TopY(Math.Clamp((int)MathF.Round((worldX - Left) / SampleSpacing), 0, SampleCount - 1), time);

	public float BottomAt(float worldX)
		=> bottom[Math.Clamp((int)((worldX - Left) / SampleSpacing), 0, SampleCount - 1)];

	// The floor runs straight from left to right across the column. lipOverFloor keeps the foam line
	// even where the floor rises above the rest line, for liquid drawn over submerged terrain.
	public void SetColumn(int column, float left, float right, bool lipOverFloor)
	{
		var columns = (SampleCount - 1) / SamplesPerTile;
		var start = column * SamplesPerTile;
		var end = column == columns - 1 ? SampleCount : start + SamplesPerTile;
		for (var i = start; i < end; i++)
		{
			var t = (i - start) / (float)SamplesPerTile;
			bottom[i] = Calc.Lerp(left, right, t);
			bottomRight[i] = Calc.Lerp(left, right, t + 1f / SamplesPerTile);
			lip[i] = lipOverFloor || MathF.Max(bottom[i], bottomRight[i]) > SurfaceY;
		}
	}

	// Kick the surface around worldX. impulseSpeed is signed: positive dips it down (a pawn
	// plunging in), negative bulges it up (a pawn bursting out). Magnitude sets how big the
	// dip/crown and ripple are.
	public void Splash(float worldX, float impulseSpeed)
	{
		var center = (int)MathF.Round((worldX - Left) / SampleSpacing);
		if (center < 0 || center >= SampleCount)
			return;

		var magnitude = MathF.Min(MathF.Abs(impulseSpeed) * SplashVelPerSpeed, MaxSplashVel);
		var inject = MathF.CopySign(magnitude, impulseSpeed);
		for (var off = -SplashKernel; off <= SplashKernel; off++)
		{
			var i = center + off;
			if (i < 0 || i >= SampleCount)
				continue;
			var falloff = 1f - MathF.Abs(off) / (float)(SplashKernel + 1);
			velocity[i] += inject * falloff;
		}
	}

	public void Update(float dt)
	{
		var steps = Math.Max(1, (int)MathF.Ceiling(dt / MaxSimulationStep));
		var step = dt / steps;
		for (var i = 0; i < steps; i++)
			Step(step);
	}

	void Step(float dt)
	{
		var n = SampleCount;
		// Accumulate forces into velocity, reading the (unchanged) heights.
		for (var i = 0; i < n; i++)
		{
			var left = i > 0 ? height[i - 1] : height[i];       // closed (mirrored) ends
			var right = i < n - 1 ? height[i + 1] : height[i];
			var tension = (left + right) * 0.5f - height[i];
			velocity[i] += (tension * Spread - height[i] * SpringK) * dt;
		}

		// Damp and integrate. Near the ends a "sponge" layer adds extra attenuation that eases
		// to zero inward, so waves are absorbed into the boundary instead of reflecting back and
		// piling into a tall crest against the wall. The same factor pulls height toward the rest
		// line, so the surface smoothly meets the edge flat (no hard pinch).
		var damp = MathF.Exp(-Damping * dt);
		for (var i = 0; i < n; i++)
		{
			var edge = Math.Min(i, n - 1 - i);
			var s = edge < SpongeSamples ? 1f - edge / (float)SpongeSamples : 0f;
			var atten = s > 0f ? MathF.Exp(-SpongeStrength * s * dt) : 1f;
			velocity[i] *= damp * atten;
			height[i] = Math.Clamp((height[i] + velocity[i] * dt) * atten, -MaxHeight, MaxHeight);
		}
	}

	float TopY(int i, float time)
	{
		var worldX = Left + i * SampleSpacing;
		var ambient = AmbientAmplitude * MathF.Sin(worldX / AmbientWavelength + time * AmbientSpeed);
		return SurfaceY + height[i] + ambient;
	}

	// The body columns, drawn for LiquidBody's material (pushed by the caller, which also pushed the
	// world matrix): world position as texcoords, depth below the surface in the vertex color.
	public void RenderBody(Batcher batcher, float time, Texture stir)
	{
		var topLeft = TopY(0, time);
		for (var i = 0; i < SampleCount - 1; i++)
		{
			var xl = Left + i * SampleSpacing;
			var xr = xl + SampleSpacing;
			var topRight = TopY(i + 1, time);
			if (bottom[i] > SurfaceY || bottomRight[i] > SurfaceY)
			{
				var a = new Vector2(xl, topLeft);
				var b = new Vector2(xr, topRight);
				var c = new Vector2(xr, MathF.Max(bottomRight[i], topRight));
				var d = new Vector2(xl, MathF.Max(bottom[i], topLeft));
				var top = LiquidBody.Depth(0);
				batcher.Quad(stir, a, b, c, d, a, b, c, d, top, top, LiquidBody.Depth(c.Y - SurfaceY), LiquidBody.Depth(d.Y - SurfaceY));
			}
			topLeft = topRight;
		}
	}

	// Refraction for the body below the rest line, one tile column at a time.
	public void RenderRefraction(Batcher batcher, float time, LiquidStir stir)
	{
		const int samplesPerColumn = (int)(LiquidBody.RefractCell / SampleSpacing);
		for (var i = 0; i < SampleCount - 1; i += samplesPerColumn)
		{
			var floor = MathF.Min(bottom[i], bottomRight[Math.Min(i + samplesPerColumn, SampleCount) - 1]);
			if (floor > SurfaceY + 2)
				LiquidBody.Refract(batcher, new Rect(Left + i * SampleSpacing, SurfaceY + 2, LiquidBody.RefractCell, floor - SurfaceY - 2), time, stir);
		}
	}

	// Foam line riding the displaced top edge.
	public void RenderLip(Batcher batcher, float time)
	{
		var topLeft = TopY(0, time);
		for (var i = 0; i < SampleCount - 1; i++)
		{
			var xl = Left + i * SampleSpacing;
			var xr = xl + SampleSpacing;
			var topRight = TopY(i + 1, time);
			if (lip[i])
			{
				var crawl = 0.5f + 0.25f * (MathF.Sin(xl * 0.7f + time * 6f) + MathF.Sin(xl * 0.29f - time * 3.7f));
				var lip = LipCrawl > 0 ? Color.Lerp(Body, Foam, 1 - LipCrawl * (1 - crawl)) : Foam;
				var weight = LipCrawl > 0 && crawl > 0.75f ? 2f : 1f;
				batcher.Line(new Vector2(xl, topLeft), new Vector2(xr, topRight), weight, lip);
			}
			topLeft = topRight;
		}
	}
}
