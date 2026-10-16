using System.Numerics;
using System.Runtime.InteropServices;
using Foster.Framework;

namespace Myth;

// A piece of a SculptedBody: a round cone from A to B (a sphere when they meet), in rig-local pixels with z toward
// the viewer, in its own color. Blend is the smooth-union radius into the pieces before it (0 = a hard seam); Carve
// cuts it away. Material picks the surface (0 skin, 1 metal).
readonly record struct BodyPrimitive(Vector3 A, float RadiusA, Vector3 B, float RadiusB, float Blend, Color Color,
	int Material = 0, bool Carve = false, BodyCombat Combat = BodyCombat.Vulnerable | BodyCombat.Harmful);

// What a body piece does in a fight: take hits (and dents), hurt whoever touches it, both, or neither.
[Flags]
enum BodyCombat { None = 0, Vulnerable = 1, Harmful = 2 }

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
// screen tiles, so each pixel only marches the few that can reach it. Pieces and bins go up in storage buffers:
// Vulkan binds only the first 4 KB of a uniform block.
sealed class SculptedBody(GraphicsDevice device) : IDisposable
{
	public const int MaxLights = 4;
	const int TileSize = 8;
	const int TargetStep = 32; // targets grow in steps so a moving pose doesn't reallocate them

	static readonly BlendMode Overwrite = new(BlendOp.Add, BlendFactor.One, BlendFactor.Zero);
	static readonly TextureSampler Nearest = new(TextureFilter.Nearest, TextureWrap.Clamp, TextureWrap.Clamp);

	// Mirrors SculptedBody.hlsl's Piece.
	[StructLayout(LayoutKind.Sequential)]
	readonly record struct GpuPiece(Vector3 A, float RadiusA, Vector3 B, float RadiusB, float Blend, uint Material,
		uint Carve, uint Rgb);

	readonly Batcher batcher = new(device, "SculptedBody");
	readonly Material bodyMaterial = new(null, LoadShader(device, "SculptedBody", samplers: 0, storageBuffers: 2));
	readonly Material maskMaterial = new(null, LoadShader(device, "SculptedHeadMask"));
	readonly StorageBuffer<GpuPiece> pieceBuffer = new(device, "SculptedBodyPieces");
	readonly StorageBuffer<uint> binBuffer = new(device, "SculptedBodyBins");
	readonly float[] uniforms = new float[(7 + MaxLights * 2) * 4];
	readonly List<GpuPiece> gpuPieces = [];
	readonly List<uint> bins = [];
	readonly List<Rect> reach = [];
	Target? marched; // color, raw normal (alpha = hit), specular
	Target? dilated; // mask, normal

	public Texture? ColorTexture => marched?.Attachments[0];
	public Texture? NormalTexture => dilated?.Attachments[1];
	public Texture? MaskTexture => dilated?.Attachments[0];
	public Texture? SpecularTexture => marched?.Attachments[2];
	public Vector2 Origin { get; private set; } // the texture's top-left, rig-local

	static Shader LoadShader(GraphicsDevice device, string name, int samplers = 1, int storageBuffers = 0)
	{
		var file = $"{name}.fragment.{device.Driver.GetShaderExtension()}";
		return new Shader(device, ShaderStage.Fragment, Assets.TryReadCompiledShader(file)
			?? throw new FileNotFoundException($"{name} shader not found at 'Shaders/Compiled/{file}'.", file),
			samplerCount: samplers, uniformBufferCount: 1, storageBufferCount: storageBuffers, entryPoint: "fragment_main", name: name);
	}

	// worldOrigin: where rig-local zero sits in the world, for the lights and the dither.
	public void Render(ReadOnlySpan<BodyPrimitive> primitives, in BodyShading shading, ReadOnlySpan<BodyLight> lights,
		Vector2 worldOrigin)
	{
		if (primitives.IsEmpty)
			return;
		var min = new Vector3(float.MaxValue);
		var max = new Vector3(float.MinValue);
		foreach (var p in primitives)
		{
			if (p.Carve)
				continue;
			min = Vector3.Min(min, Vector3.Min(p.A - new Vector3(p.RadiusA), p.B - new Vector3(p.RadiusB)));
			max = Vector3.Max(max, Vector3.Max(p.A + new Vector3(p.RadiusA), p.B + new Vector3(p.RadiusB)));
		}
		var topLeft = (new Vector2(min.X, min.Y) - Vector2.One * 2).FloorToPoint2();
		var size = (new Vector2(max.X, max.Y) + Vector2.One * 2).CeilingToPoint2() - topLeft;
		EnsureTargets(size.X, size.Y);
		Origin = topLeft;

		var offset = new Vector3(topLeft.X, topLeft.Y, 0);
		gpuPieces.Clear();
		reach.Clear();
		foreach (var p in primitives)
		{
			gpuPieces.Add(new(p.A - offset, p.RadiusA, p.B - offset, p.RadiusB, p.Blend, (uint)p.Material, p.Carve ? 1u : 0,
				(uint)(p.Color.R << 16 | p.Color.G << 8 | p.Color.B)));
			// Everything a piece can touch: itself, its blend into its neighbours, and the occlusion probe's reach.
			var inflate = Math.Max(p.RadiusA, p.RadiusB) + p.Blend + shading.OcclusionProbe + 1;
			reach.Add(Rect.Between(new Vector2(p.A.X, p.A.Y) - topLeft, new Vector2(p.B.X, p.B.Y) - topLeft).Inflate(inflate));
		}
		var tiles = Bin(size);
		pieceBuffer.Upload(CollectionsMarshal.AsSpan(gpuPieces));
		binBuffer.Upload(CollectionsMarshal.AsSpan(bins));

		var u = uniforms.AsSpan();
		u.Clear();
		new Vector4(0, shading.OcclusionProbe, max.Z + 1, min.Z - 1).CopyTo(u);
		var textureWorld = worldOrigin + topLeft;
		var lightCount = Math.Min(lights.Length, MaxLights);
		new Vector4((int)shading.Mode, textureWorld.X, textureWorld.Y, lightCount).CopyTo(u[4..]);
		new Vector4(shading.DepthCenter, shading.DepthRange, shading.DepthShade, 0).CopyTo(u[8..]);
		new Vector4(TileSize, tiles.X, tiles.Y, 0).CopyTo(u[12..]);
		((Vector4)shading.Shadow).CopyTo(u[16..]);
		shading.Skin.CopyTo(u[20..]);
		shading.Metal.CopyTo(u[24..]);
		for (var i = 0; i < lightCount; i++)
		{
			var light = lights[i];
			new Vector4(light.Position - textureWorld, light.Height, light.Radius).CopyTo(u[(28 + i * 8)..]);
			new Vector4(light.Color, 0).CopyTo(u[(32 + i * 8)..]);
		}

		bodyMaterial.Fragment.SetUniformBuffer((ReadOnlySpan<float>)uniforms);
		batcher.FragmentStorageBuffers.Add(pieceBuffer);
		batcher.FragmentStorageBuffers.Add(binBuffer);
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

	// Lists, per tile and in draw order, the pieces that reach it: a (first entry, count) header per tile, then the
	// entries.
	Point2 Bin(Point2 size)
	{
		var tiles = (size + new Point2(TileSize - 1, TileSize - 1)) / TileSize;
		var headers = tiles.X * tiles.Y * 2;
		bins.Clear();
		CollectionsMarshal.SetCount(bins, headers);
		for (var y = 0; y < tiles.Y; y++)
			for (var x = 0; x < tiles.X; x++)
			{
				var tile = new Rect(x * TileSize, y * TileSize, TileSize, TileSize);
				var first = bins.Count;
				for (var i = 0; i < reach.Count; i++)
					if (reach[i].Overlaps(tile))
						bins.Add((uint)i);
				bins[(y * tiles.X + x) * 2] = (uint)(first - headers);
				bins[(y * tiles.X + x) * 2 + 1] = (uint)(bins.Count - first);
			}
		return tiles;
	}

	// The CPU copy of SculptedBody.hlsl's field, for gameplay asking where the surface is. Keep the two in step.
	public static float Distance(ReadOnlySpan<BodyPrimitive> pieces, Vector3 p)
	{
		var d = 1e5f;
		foreach (var piece in pieces)
		{
			var di = RoundCone(p, piece);
			d = piece.Carve ? SmoothSubtract(d, di, MathF.Max(piece.Blend, 0.001f))
				: piece.Blend > 0 ? SmoothUnion(d, di, piece.Blend) : MathF.Min(d, di);
		}
		return d;
	}

	public static Vector3 Normal(ReadOnlySpan<BodyPrimitive> pieces, Vector3 p)
	{
		const float e = 0.25f;
		Vector3 a = new(1, -1, -1), b = new(-1, -1, 1), c = new(-1, 1, -1), d = new(1, 1, 1);
		return Vector3.Normalize(a * Distance(pieces, p + a * e) + b * Distance(pieces, p + b * e) +
			c * Distance(pieces, p + c * e) + d * Distance(pieces, p + d * e));
	}

	// The solid piece nearest a point: whose material the surface there shows.
	public static int NearestPiece(ReadOnlySpan<BodyPrimitive> pieces, Vector3 p)
	{
		var (found, nearest) = (-1, float.MaxValue);
		for (var i = 0; i < pieces.Length; i++)
			if (!pieces[i].Carve && RoundCone(p, pieces[i]) is var di && di < nearest)
				(found, nearest) = (i, di);
		return found;
	}

	// Marches toward the viewer's back from in front of every piece, like a body pixel's ray.
	public static bool Raycast(ReadOnlySpan<BodyPrimitive> pieces, Vector2 at, out Vector3 hit)
	{
		var (front, back) = (float.MinValue, float.MaxValue);
		foreach (var piece in pieces)
		{
			front = MathF.Max(front, MathF.Max(piece.A.Z + piece.RadiusA, piece.B.Z + piece.RadiusB));
			back = MathF.Min(back, MathF.Min(piece.A.Z - piece.RadiusA, piece.B.Z - piece.RadiusB));
		}
		hit = new(at, front + 1);
		for (var step = 0; step < 64 && hit.Z > back - 1; step++)
		{
			var distance = Distance(pieces, hit);
			if (distance < 0.02f)
				return true;
			hit.Z -= MathF.Max(distance, 0.01f);
		}
		return false;
	}

	static float RoundCone(Vector3 p, in BodyPrimitive piece)
	{
		var (a, b, r1, r2) = (piece.A, piece.B, piece.RadiusA, piece.RadiusB);
		var ba = b - a;
		var l2 = Vector3.Dot(ba, ba);
		var rr = r1 - r2;
		var a2 = l2 - rr * rr;
		if (a2 <= 0.0001f)
			return r1 > r2 ? Vector3.Distance(p, a) - r1 : Vector3.Distance(p, b) - r2;
		var il2 = 1 / l2;
		var pa = p - a;
		var y = Vector3.Dot(pa, ba);
		var z = y - l2;
		var x2 = (pa * l2 - ba * y).LengthSquared();
		var y2 = y * y * l2;
		var z2 = z * z * l2;
		var k = MathF.Sign(rr) * rr * rr * x2;
		if (MathF.Sign(z) * a2 * z2 > k)
			return MathF.Sqrt(x2 + z2) * il2 - r2;
		if (MathF.Sign(y) * a2 * y2 < k)
			return MathF.Sqrt(x2 + y2) * il2 - r1;
		return (MathF.Sqrt(x2 * a2 * il2) + y * rr) * il2 - r1;
	}

	static float SmoothUnion(float a, float b, float k)
	{
		var h = Math.Clamp(0.5f + 0.5f * (b - a) / k, 0, 1);
		return float.Lerp(b, a, h) - k * h * (1 - h);
	}

	static float SmoothSubtract(float a, float b, float k)
	{
		var h = Math.Clamp(0.5f - 0.5f * (a + b) / k, 0, 1);
		return float.Lerp(a, -b, h) + k * h * (1 - h);
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
		pieceBuffer.Dispose();
		binBuffer.Dispose();
		maskMaterial.Fragment.Shader?.Dispose();
	}
}
