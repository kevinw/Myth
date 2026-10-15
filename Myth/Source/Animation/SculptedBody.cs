using System.Numerics;
using Foster.Framework;

namespace Myth;

// A piece of a SculptedBody: a round cone from A to B (a sphere when they meet), in rig-local pixels with z toward
// the viewer, in its own color. Blend is the smooth-union radius into the pieces before it (0 = a hard seam); Carve
// cuts it away. Material picks the surface (0 skin, 1 metal).
readonly record struct BodyPrimitive(Vector3 A, float RadiusA, Vector3 B, float RadiusB, float Blend, Color Color,
	int Material = 0, bool Carve = false);

// A scene light the body catches highlights from, in world pixels; Color is premultiplied by intensity.
readonly record struct BodyLight(Vector2 Position, float Height, float Radius, Vector3 Color);

// Mirrors SculptedBody.hlsl's Shading.x.
public enum BodyShadeMode { Banded, Smooth, Dithered }

// How a SculptedBody is shaded. Shadow tints a piece's color in its darkest band; pieces DepthRange (px) behind
// DepthCenter darken by DepthShade. Materials are skin, metal as (metallic, roughness, specular, 0).
readonly record struct BodyShading(BodyShadeMode Mode, Color Shadow, Vector4 Skin, Vector4 Metal, float OcclusionProbe,
	float DepthCenter, float DepthRange, float DepthShade);

// A whole body as one signed distance field, ray-marched on the GPU (SculptedBody.hlsl) into a texture sized to the
// pose: each piece's color shaded by light and cavity (or, for metal, by reflection), screen-space normals,
// scene-light highlights for the emissive pass, and the 1px-dilated mask (SculptedHeadMask). Pieces are binned to
// screen tiles, so each pixel only marches the few that can reach it.
sealed class SculptedBody(GraphicsDevice device) : IDisposable
{
	public const int MaxPrimitives = 96;
	public const int MaxLights = 4;
	const int MaxTiles = 512;
	const int MaxTileEntries = 4104; // 3 to a float
	const int PrimitivesAt = (7 + MaxLights * 2) * 4; // uniform offsets, in floats; mirror SculptedBody.hlsl
	const int TilesAt = PrimitivesAt + MaxPrimitives * 12;
	const int EntriesAt = TilesAt + MaxTiles;
	const int TargetStep = 32; // targets grow in steps so a moving pose doesn't reallocate them

	static readonly BlendMode Overwrite = new(BlendOp.Add, BlendFactor.One, BlendFactor.Zero);
	static readonly TextureSampler Nearest = new(TextureFilter.Nearest, TextureWrap.Clamp, TextureWrap.Clamp);

	readonly Batcher batcher = new(device, "SculptedBody");
	readonly Material bodyMaterial = new(null, LoadShader(device, "SculptedBody"));
	readonly Material maskMaterial = new(null, LoadShader(device, "SculptedHeadMask"));
	readonly float[] uniforms = new float[EntriesAt + MaxTileEntries / 3];
	readonly uint[] entries = new uint[MaxTileEntries / 3];
	readonly Rect[] reach = new Rect[MaxPrimitives];
	Target? marched; // color, raw normal (alpha = hit), specular
	Target? dilated; // mask, normal

	public Texture? ColorTexture => marched?.Attachments[0];
	public Texture? NormalTexture => dilated?.Attachments[1];
	public Texture? MaskTexture => dilated?.Attachments[0];
	public Texture? SpecularTexture => marched?.Attachments[2];
	public Vector2 Origin { get; private set; } // the texture's top-left, rig-local
	public int TileSize { get; private set; }

	static Shader LoadShader(GraphicsDevice device, string name)
	{
		var file = $"{name}.fragment.{device.Driver.GetShaderExtension()}";
		return new Shader(device, ShaderStage.Fragment, Assets.TryReadCompiledShader(file)
			?? throw new FileNotFoundException($"{name} shader not found at 'Shaders/Compiled/{file}'.", file),
			samplerCount: 1, uniformBufferCount: 1, entryPoint: "fragment_main", name: name);
	}

	// worldOrigin: where rig-local zero sits in the world, for the lights and the dither.
	public void Render(ReadOnlySpan<BodyPrimitive> primitives, in BodyShading shading, ReadOnlySpan<BodyLight> lights,
		Vector2 worldOrigin)
	{
		var count = Math.Min(primitives.Length, MaxPrimitives);
		if (count == 0)
			return;
		var min = new Vector3(float.MaxValue);
		var max = new Vector3(float.MinValue);
		for (var i = 0; i < count; i++)
		{
			var p = primitives[i];
			if (p.Carve)
				continue;
			min = Vector3.Min(min, Vector3.Min(p.A - new Vector3(p.RadiusA), p.B - new Vector3(p.RadiusB)));
			max = Vector3.Max(max, Vector3.Max(p.A + new Vector3(p.RadiusA), p.B + new Vector3(p.RadiusB)));
		}
		var topLeft = (new Vector2(min.X, min.Y) - Vector2.One * 2).FloorToPoint2();
		var size = (new Vector2(max.X, max.Y) + Vector2.One * 2).CeilingToPoint2() - topLeft;
		EnsureTargets(size.X, size.Y);
		Origin = topLeft;

		var u = uniforms.AsSpan();
		u.Clear();
		new Vector4(count, shading.OcclusionProbe, max.Z + 1, min.Z - 1).CopyTo(u);
		var textureWorld = worldOrigin + topLeft;
		var lightCount = Math.Min(lights.Length, MaxLights);
		new Vector4((int)shading.Mode, textureWorld.X, textureWorld.Y, lightCount).CopyTo(u[4..]);
		new Vector4(shading.DepthCenter, shading.DepthRange, shading.DepthShade, 0).CopyTo(u[8..]);
		((Vector4)shading.Shadow).CopyTo(u[16..]);
		shading.Skin.CopyTo(u[20..]);
		shading.Metal.CopyTo(u[24..]);
		for (var i = 0; i < lightCount; i++)
		{
			var light = lights[i];
			new Vector4(light.Position - textureWorld, light.Height, light.Radius).CopyTo(u[(28 + i * 8)..]);
			new Vector4(light.Color, 0).CopyTo(u[(32 + i * 8)..]);
		}
		var offset = new Vector3(topLeft.X, topLeft.Y, 0);
		for (var i = 0; i < count; i++)
		{
			var p = primitives[i];
			var at = u[(PrimitivesAt + i * 12)..];
			new Vector4(p.A - offset, p.RadiusA).CopyTo(at);
			new Vector4(p.B - offset, p.RadiusB).CopyTo(at[4..]);
			new Vector4(p.Blend, p.Material, p.Carve ? 1 : 0, p.Color.R << 16 | p.Color.G << 8 | p.Color.B).CopyTo(at[8..]);
			// Everything a piece can touch: itself, its blend into its neighbours, and the occlusion probe's reach.
			var inflate = Math.Max(p.RadiusA, p.RadiusB) + p.Blend + shading.OcclusionProbe + 1;
			reach[i] = Rect.Between(new Vector2(p.A.X, p.A.Y) - topLeft, new Vector2(p.B.X, p.B.Y) - topLeft).Inflate(inflate);
		}
		var tiles = Bin(count, size);
		new Vector4(TileSize, tiles.X, tiles.Y, 0).CopyTo(u[12..]);
		for (var i = 0; i < entries.Length; i++)
			u[EntriesAt + i] = entries[i];

		bodyMaterial.Fragment.SetUniformBuffer((ReadOnlySpan<float>)uniforms);
		batcher.PushBlend(Overwrite);
		batcher.PushMaterial(bodyMaterial);
		batcher.Rect(new Rect(0, 0, marched!.Width, marched.Height), Color.White);
		batcher.PopMaterial();
		batcher.PopBlend();
		batcher.Render(marched);
		batcher.Clear();

		maskMaterial.Fragment.SetUniformBuffer(new Vector4(marched.Width, marched.Height, 1, 0));
		batcher.PushBlend(Overwrite);
		batcher.PushMaterial(maskMaterial);
		batcher.PushSampler(Nearest);
		batcher.Image(marched.Attachments[1], Vector2.Zero, Color.White);
		batcher.PopSampler();
		batcher.PopMaterial();
		batcher.PopBlend();
		batcher.Render(dilated!);
		batcher.Clear();
	}

	// Lists, per tile and in draw order, the pieces that reach it: the tile table goes into the uniforms, the packed
	// entries into `entries`. Tiles grow until both fit.
	Point2 Bin(int count, Point2 size)
	{
		var u = uniforms.AsSpan(TilesAt, MaxTiles);
		for (TileSize = 8; ; TileSize *= 2)
		{
			var tiles = (size + new Point2(TileSize - 1, TileSize - 1)) / TileSize;
			if (tiles.X * tiles.Y > MaxTiles)
				continue;
			Array.Clear(entries);
			var used = 0;
			for (var y = 0; y < tiles.Y && used >= 0; y++)
				for (var x = 0; x < tiles.X && used >= 0; x++)
				{
					var tile = new Rect(x * TileSize, y * TileSize, TileSize, TileSize);
					var first = used;
					for (var i = 0; i < count; i++)
					{
						if (!reach[i].Overlaps(tile))
							continue;
						if (used == MaxTileEntries)
						{
							used = -1;
							break;
						}
						entries[used / 3] |= (uint)i << used % 3 * 8;
						used++;
					}
					if (used >= 0)
						u[y * tiles.X + x] = first * 128 + used - first;
				}
			if (used >= 0)
				return tiles;
		}
	}

	void EnsureTargets(int width, int height)
	{
		if (marched != null && marched.Width >= width && marched.Height >= height)
			return;
		width = Math.Max(width, marched?.Width ?? 0);
		height = Math.Max(height, marched?.Height ?? 0);
		width = (width + TargetStep - 1) / TargetStep * TargetStep;
		height = (height + TargetStep - 1) / TargetStep * TargetStep;
		DisposeTargets();
		marched = new Target(device, width, height, [TextureFormat.Color, TextureFormat.Color, TextureFormat.Color], name: "SculptedBody");
		dilated = new Target(device, width, height, [TextureFormat.Color, TextureFormat.Color], name: "SculptedBodyMask");
	}

	void DisposeTargets()
	{
		marched?.Dispose();
		dilated?.Dispose();
		marched = dilated = null;
	}

	public void Dispose()
	{
		DisposeTargets();
		batcher.Dispose();
		bodyMaterial.Fragment.Shader?.Dispose();
		maskMaterial.Fragment.Shader?.Dispose();
	}
}
