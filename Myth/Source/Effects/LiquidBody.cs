using System.Numerics;
using Foster.Framework;

namespace Myth;

// Draws liquid bodies through LiquidBody.hlsl: warped noise cut into band lines over a depth gradient,
// read against the shared LiquidStir field, snapped to a four-color palette. Lava's lines are dark
// crust streaks flowing sideways on a molten body; water's are a bright, wandering caustic web on a
// clear, translucent one. Vertices
// carry their world position as texcoords and their depth below the surface (px) in the color's red.
static class LiquidBody
{
	public readonly record struct Style(
		float NoiseSize,    // world px per noise feature
		float Drift,        // world px per second the noise flows sideways
		float Stretch,      // < 1 stretches features sideways
		float LineWidth,    // half-width of a band line, world px
		float Bands,        // lines per noise swing
		float LineGaps,     // 0 = unbroken lines, 1 = none
		float LineEffect,   // heat a line takes away (crust); negative adds it (caustic)
		float SurfaceHeat,  // palette level just under the surface, 0..1
		float DepthCooling, // level lost by DepthFade px down
		float DepthFade,
		float NoiseContrast,
		float StirBrighten,
		float Dither,       // crosshatches the edges between palette bands
		float CausticCell,  // > 0: lines are a caustic web of cells this size (px), wandering at Drift; 0: flow lines
		Color Deep, Color Body, Color Bright, Color Hottest);

	public static Style Lava => new(
		NoiseSize: 24f, Drift: 5f, Stretch: 0.3f, LineWidth: 0.6f, Bands: 5f, LineGaps: 0.35f, LineEffect: 0.35f,
		SurfaceHeat: 0.74f, DepthCooling: 0.3f, DepthFade: 48f, NoiseContrast: 0.15f, StirBrighten: 0.35f, Dither: 0.05f, CausticCell: 0,
		LiquidVolume.LavaCrust, LiquidVolume.LavaBody, new Color(1f, 0.5f, 0.1f, 1f), LiquidVolume.LavaFoam);

	// Clear: low alpha everywhere, so the room reads through; deeper water darkens it a little more.
	// Stirring only nudges it brighter, so a swimmer stays readable.
	public static Style Water => new(
		NoiseSize: 30f, Drift: 9f, Stretch: 0.7f, LineWidth: 0.6f, Bands: 4f, LineGaps: 0.35f, LineEffect: -0.4f,
		SurfaceHeat: 0.6f, DepthCooling: 0.35f, DepthFade: 80f, NoiseContrast: 0.2f, StirBrighten: 0.12f, Dither: 0.03f, CausticCell: 14f,
		new Color(0.05f, 0.17f, 0.36f, 0.5f), new Color(0.06f, 0.23f, 0.45f, 0.4f),
		new Color(0.09f, 0.31f, 0.54f, 0.32f), new Color(0.3f, 0.58f, 0.82f, 0.3f));

	static Material? material;
	static readonly TextureSampler StirSampler = new(TextureFilter.Linear, TextureWrap.Clamp, TextureWrap.Clamp);

	// Pushes the body material and returns the stir texture to draw with. Draws into both world
	// passes, so the phase comes from GameView like Surface's material.
	public static Texture Push(Batcher batcher, in Style style, float flowTime, LiquidStir stir)
	{
		var texture = stir.Upload(batcher.GraphicsDevice);
		material ??= new Material(null, Surface.LoadFragment(batcher.GraphicsDevice, "LiquidBody", samplerCount: 1));

		var rect = stir.Bounds;
		Span<float> u = stackalloc float[36];
		u[0] = flowTime; u[1] = (float)GameView.CurrentWorldPhase; u[2] = style.DepthFade; u[3] = style.Stretch;
		u[4] = style.NoiseSize; u[5] = style.Drift; u[6] = style.LineWidth; u[7] = style.Bands;
		u[8] = style.SurfaceHeat; u[9] = style.DepthCooling; u[10] = style.NoiseContrast; u[11] = style.LineEffect;
		u[12] = rect.X; u[13] = rect.Y; u[14] = rect.Width; u[15] = rect.Height;
		((Vector4)style.Deep).CopyTo(u[16..]);
		((Vector4)style.Body).CopyTo(u[20..]);
		((Vector4)style.Bright).CopyTo(u[24..]);
		((Vector4)style.Hottest).CopyTo(u[28..]);
		u[32] = style.StirBrighten; u[33] = style.Dither; u[34] = style.LineGaps; u[35] = style.CausticCell;
		material.Fragment.SetUniformBuffer(u);
		batcher.PushSampler(StirSampler);
		batcher.PushMaterial(material);
		return texture;
	}

	public static void Pop(Batcher batcher)
	{
		batcher.PopMaterial();
		batcher.PopSampler();
	}

	public static Color Depth(float px) => new((byte)Calc.Clamp(px, 0, 255), 0, 0, 255);

	public static void Rect(Batcher batcher, Texture texture, Rect r, float topDepth)
	{
		var (top, bottom) = (Depth(topDepth), Depth(topDepth + r.Height));
		batcher.Quad(texture, r.TopLeft, r.TopRight, r.BottomRight, r.BottomLeft,
			r.TopLeft, r.TopRight, r.BottomRight, r.BottomLeft, top, top, bottom, bottom);
	}

	// Refraction: a subtle, slowly turning wobble in the world-distortion mask over a water body, so
	// whatever sits behind or inside it swims a little. Stirred water wobbles harder.
	public const int RefractCell = 8;
	static float RefractStrength => 0.22f;
	static float RefractStirBoost => 0.6f;

	public static void Refract(Batcher batcher, Rect r, float time, LiquidStir stir)
	{
		for (var y = r.Top; y < r.Bottom; y += RefractCell)
			for (var x = r.Left; x < r.Right; x += RefractCell)
			{
				var cell = new Rect(x, y, MathF.Min(RefractCell, r.Right - x), MathF.Min(RefractCell, r.Bottom - y));
				batcher.Rect(cell, Wobble(cell.TopLeft, time, stir), Wobble(cell.TopRight, time, stir),
					Wobble(cell.BottomRight, time, stir), Wobble(cell.BottomLeft, time, stir));
			}
	}

	static Color Wobble(Vector2 p, float time, LiquidStir stir)
	{
		var direction = new Vector2(
			MathF.Sin(p.X * 0.11f + p.Y * 0.07f + time * 1.7f),
			0.6f * MathF.Sin(p.X * 0.05f - p.Y * 0.13f + time * 1.3f));
		return Actor.EncodeWorldDistortion(direction, RefractStrength * (1 + stir.At(p) * RefractStirBoost));
	}
}
