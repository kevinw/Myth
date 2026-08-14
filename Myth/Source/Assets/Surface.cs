using Foster.Framework;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Myth;

// A Surface is an authorable, room-referenced material: up to 6 texture layers from
// the shared texture bucket, each with its own blend mode / tint / world-space tiling,
// composited in-shader in one draw, plus an optional normal map (explicit, or derived
// from a layer's brightness). Actors draw it via Render (color) and RenderNormals.
//
// Runtime blend (not baked) so per-layer tiling and future animation stay cheap; the
// heavy per-texture work (format classification, normal bake) is cached at load.

public enum SurfaceBlend { Over = 0, Multiply = 1, Add = 2, Screen = 3, Darken = 4, Lighten = 5, Difference = 6 }
public enum SurfaceRotation { None = 0, Cw90 = 1, Cw180 = 2, Cw270 = 3 }

// On-disk authoring spec (Sources/Surfaces/<id>.json). Texture references are stable
// bucket keys (e.g. "patterns/pattern_07"), so reordering the bucket can't repoint a room.
public sealed class SurfaceLayerSpec
{
	public string Texture { get; set; } = "";
	public SurfaceBlend Blend { get; set; } = SurfaceBlend.Over;
	public string Tint { get; set; } = "ffffffff"; // RGBA hex; white endpoint for luminance textures
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? BlackColor { get; set; } // luminance black endpoint; null preserves legacy tint alpha
	public float Opacity { get; set; } = 1f;
	public float[] Tiling { get; set; } = [48f, 48f]; // world px per tile repeat
	public float[] Offset { get; set; } = [0f, 0f]; // static world px offset before tiling
	public float[] Scroll { get; set; } = [0f, 0f]; // visual motion in world px / second
	public SurfaceRotation Rotation { get; set; }
	public bool FlipX { get; set; }
	public bool FlipY { get; set; }
	public float Contrast { get; set; } = 1f;
	public float Bias { get; set; }
	public float Quantize { get; set; }
}

public sealed class SurfaceSpec
{
	public const int MaxLayers = 6;

	public List<SurfaceLayerSpec> Layers { get; set; } = [];
	public bool NormalFromHeight { get; set; }
	public string? NormalTexture { get; set; }
	public float NormalStrength { get; set; } = 1f;
	public int HeightFromLayer { get; set; } // layer index whose brightness -> height

	[JsonIgnore] public string Id { get; set; } = "";

	public static SurfaceSpec Parse(string json, string id)
	{
		var spec = JsonSerializer.Deserialize(json, SurfaceSpecJsonContext.Default.SurfaceSpec) ?? new SurfaceSpec();
		spec.Id = id;
		if (spec.Layers.Count > MaxLayers)
			spec.Layers = spec.Layers.GetRange(0, MaxLayers);
		return spec;
	}

	public static SurfaceSpec Load(string path)
		=> Parse(File.ReadAllText(path), Path.GetFileNameWithoutExtension(path));

	// Texture keys this spec references (layers + explicit normal), for content pruning.
	public IEnumerable<string> ReferencedTextures()
	{
		foreach (var layer in Layers)
			if (!string.IsNullOrEmpty(layer.Texture))
				yield return layer.Texture;
		if (!string.IsNullOrEmpty(NormalTexture))
			yield return NormalTexture!;
	}

	public void Save(string path)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		File.WriteAllText(path, ToJson());
	}

	public string ToJson() => JsonSerializer.Serialize(this, SurfaceSpecJsonContext.Default.SurfaceSpec);

	public static Color ParseTint(string hex)
	{
		if (uint.TryParse(hex.TrimStart('#'), System.Globalization.NumberStyles.HexNumber, null, out var v))
		{
			if (hex.TrimStart('#').Length <= 6) // RGB -> opaque
				return new Color((byte)(v >> 16), (byte)(v >> 8), (byte)v, 255);
			return new Color((byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v);
		}
		return Color.White;
	}
}

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNameCaseInsensitive = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(SurfaceSpec))]
internal partial class SurfaceSpecJsonContext : JsonSerializerContext;

public sealed class Surface : IDisposable
{
	readonly struct Layer(Assets.BucketTexture texture, SurfaceBlend blend, Color tint, Color blackColor, float opacity, Vector2 tiling, Vector2 offset, Vector2 scroll, SurfaceRotation rotation, bool flipX, bool flipY, float contrast, float bias, float quantize)
	{
		public readonly Assets.BucketTexture Texture = texture;
		public readonly SurfaceBlend Blend = blend;
		public readonly Color Tint = tint;
		public readonly Color BlackColor = blackColor;
		public readonly float Opacity = opacity;
		public readonly Vector2 Tiling = tiling;
		public readonly Vector2 Offset = offset;
		public readonly Vector2 Scroll = scroll;
		public readonly SurfaceRotation Rotation = rotation;
		public readonly bool FlipX = flipX;
		public readonly bool FlipY = flipY;
		public readonly float Contrast = contrast;
		public readonly float Bias = bias;
		public readonly float Quantize = quantize;
	}

	public readonly string Id;
	readonly Layer[] layers;
	readonly Texture? normalTexture;   // owned (baked) or borrowed (bucket); tracked by ownsNormal
	readonly bool ownsNormal;
	readonly Vector2 normalTiling;
	readonly Vector2 normalScroll;
	readonly float normalStrength;

	public bool HasNormal => normalTexture != null;

	Surface(string id, Layer[] layers, Texture? normalTexture, bool ownsNormal, Vector2 normalTiling, Vector2 normalScroll, float normalStrength)
	{
		Id = id;
		this.layers = layers;
		this.normalTexture = normalTexture;
		this.ownsNormal = ownsNormal;
		this.normalTiling = normalTiling;
		this.normalScroll = normalScroll;
		this.normalStrength = normalStrength;
	}

	public struct Vertex(Vector2 pos, Vector2 world)
	{
		public Vector2 Pos = pos;    // local (batcher matrix -> world/screen)
		public Vector2 World = world; // world pixels, drives tiling
	}

	static readonly TextureSampler RepeatSampler = new(TextureFilter.Nearest, TextureWrap.Repeat, TextureWrap.Repeat);

	public static Surface Build(GraphicsDevice gfx, SurfaceSpec spec, Assets.TextureBucket bucket)
	{
		var layers = new List<Layer>(spec.Layers.Count);
		foreach (var layerSpec in spec.Layers)
		{
			if (bucket.Get(layerSpec.Texture) is not { } tex)
				continue;
			var tiling = layerSpec.Tiling is { Length: >= 2 } t ? new Vector2(t[0], t[1]) : new Vector2(48f);
			var offset = layerSpec.Offset is { Length: >= 2 } o ? new Vector2(o[0], o[1]) : Vector2.Zero;
			var scroll = layerSpec.Scroll is { Length: >= 2 } s ? new Vector2(s[0], s[1]) : Vector2.Zero;
			var tint = SurfaceSpec.ParseTint(layerSpec.Tint);
			var blackColor = layerSpec.BlackColor != null
				? SurfaceSpec.ParseTint(layerSpec.BlackColor)
				: new Color(0, 0, 0, tint.A);
			layers.Add(new Layer(tex, layerSpec.Blend, tint, blackColor, layerSpec.Opacity, tiling, offset, scroll, layerSpec.Rotation, layerSpec.FlipX, layerSpec.FlipY, layerSpec.Contrast, layerSpec.Bias, layerSpec.Quantize));
		}

		Texture? normal = null;
		var ownsNormal = false;
		var normalTiling = new Vector2(48f);
		var normalScroll = Vector2.Zero;
		if (!string.IsNullOrEmpty(spec.NormalTexture) && bucket.Get(spec.NormalTexture) is { } explicitNormal)
		{
			normal = explicitNormal.Texture;
			normalTiling = layers.Count > 0 ? layers[0].Tiling : normalTiling;
			normalScroll = layers.Count > 0 ? layers[0].Scroll : normalScroll;
		}
		else if (spec.NormalFromHeight && layers.Count > 0)
		{
			var heightLayer = Math.Clamp(spec.HeightFromLayer, 0, layers.Count - 1);
			var source = layers[heightLayer];
			using var heightImage = new Image(source.Texture.SourceImage);
			using var normalImage = Assets.GenerateNormalImage(heightImage, wrap: true);
			normal = new Texture(gfx, normalImage, name: $"{spec.Id}-normal");
			ownsNormal = true;
			normalTiling = source.Tiling;
			normalScroll = source.Scroll;
		}

		return new Surface(spec.Id, layers.ToArray(), normal, ownsNormal, normalTiling, normalScroll, spec.NormalStrength);
	}

	public void Render(Batcher batcher, ReadOnlySpan<Vertex> triangles, float time, float opacity = 1)
	{
		if (layers.Length == 0 || triangles.Length < 3)
			return;

		var material = BlendMaterial(batcher.GraphicsDevice);
		Span<float> u = stackalloc float[4 + SurfaceSpec.MaxLayers * 4 * 6];
		u[0] = layers.Length;
		u[1] = time;
		for (var i = 0; i < SurfaceSpec.MaxLayers; i++)
		{
			var layer = i < layers.Length ? layers[i] : layers[0];
			var cfg = 4 + i * 4;
			u[cfg + 0] = layer.Tiling.X;
			u[cfg + 1] = layer.Tiling.Y;
			u[cfg + 2] = (float)layer.Blend;
			u[cfg + 3] = (float)layer.Texture.Swizzle;
			var tint = (Vector4)layer.Tint;
			var t = 4 + SurfaceSpec.MaxLayers * 4 + i * 4;
			u[t + 0] = tint.X; u[t + 1] = tint.Y; u[t + 2] = tint.Z; u[t + 3] = tint.W;
			var m = 4 + SurfaceSpec.MaxLayers * 8 + i * 4;
			u[m + 0] = layer.Scroll.X; u[m + 1] = layer.Scroll.Y; u[m + 2] = layer.Offset.X; u[m + 3] = layer.Offset.Y;
			var black = (Vector4)layer.BlackColor;
			var b = 4 + SurfaceSpec.MaxLayers * 12 + i * 4;
			u[b + 0] = black.X; u[b + 1] = black.Y; u[b + 2] = black.Z; u[b + 3] = black.W;
			var x = 4 + SurfaceSpec.MaxLayers * 16 + i * 4;
			u[x + 0] = (float)layer.Rotation; u[x + 1] = layer.FlipX ? 1f : 0f; u[x + 2] = layer.FlipY ? 1f : 0f;
			var a = 4 + SurfaceSpec.MaxLayers * 20 + i * 4;
			u[a + 0] = layer.Opacity; u[a + 1] = layer.Contrast; u[a + 2] = layer.Bias; u[a + 3] = layer.Quantize;
			if (i > 0)
				material.Fragment.Samplers[i] = new(layer.Texture.Texture, RepeatSampler);
		}
		material.Fragment.SetUniformBuffer(u);

		batcher.PushMaterial(material);
		DrawTriangles(batcher, layers[0].Texture.Texture, triangles, Color.White * opacity);
		batcher.PopMaterial();
	}

	public void Render(Batcher batcher, Rect bounds, float time, float opacity = 1)
	{
		var topLeft = bounds.TopLeft;
		var topRight = new Vector2(bounds.Right, bounds.Top);
		var bottomLeft = new Vector2(bounds.Left, bounds.Bottom);
		var bottomRight = bounds.BottomRight;
		Span<Vertex> triangles = stackalloc Vertex[]
		{
			new(topLeft, topLeft), new(topRight, topRight), new(bottomRight, bottomRight),
			new(topLeft, topLeft), new(bottomRight, bottomRight), new(bottomLeft, bottomLeft)
		};
		Render(batcher, triangles, time, opacity);
	}

	public void RenderNormals(Batcher batcher, ReadOnlySpan<Vertex> triangles, float time)
	{
		if (normalTexture == null || triangles.Length < 3)
			return;

		var material = NormalMaterial(batcher.GraphicsDevice);
		material.Fragment.SetUniformBuffer(stackalloc float[]
		{
			normalTiling.X, normalTiling.Y, normalStrength, time,
			normalScroll.X, normalScroll.Y, 0f, 0f
		});
		batcher.PushMaterial(material);
		DrawTriangles(batcher, normalTexture, triangles, Color.White);
		batcher.PopMaterial();
	}

	static void DrawTriangles(Batcher batcher, Texture texture, ReadOnlySpan<Vertex> triangles, Color color)
	{
		batcher.PushSampler(RepeatSampler);
		for (var i = 0; i + 2 < triangles.Length; i += 3)
			batcher.Triangle(texture,
				triangles[i].Pos, triangles[i + 1].Pos, triangles[i + 2].Pos,
				triangles[i].World, triangles[i + 1].World, triangles[i + 2].World,
				color);
		batcher.PopSampler();
	}

	public void Dispose()
	{
		if (ownsNormal)
			normalTexture?.Dispose();
	}

	// Shared, process-lifetime materials (uniforms/samplers set per-draw before PushMaterial,
	// which snapshots, so one material object serves every Surface).
	static Material? blendMaterial;
	static Material? normalMaterial;

	static Material BlendMaterial(GraphicsDevice gfx)
		=> blendMaterial ??= new Material(null, LoadFragment(gfx, "SurfaceBlend", samplerCount: 6));

	static Material NormalMaterial(GraphicsDevice gfx)
		=> normalMaterial ??= new Material(null, LoadFragment(gfx, "SurfaceNormal", samplerCount: 1));

	static Shader LoadFragment(GraphicsDevice gfx, string name, int samplerCount)
	{
		var ext = gfx.Driver.GetShaderExtension();
		var code = Assets.TryReadCompiledShader($"{name}.fragment.{ext}")
			?? throw new FileNotFoundException($"{name} shader not found at 'Shaders/Compiled/{name}.fragment.{ext}'.");
		return new Shader(gfx, ShaderStage.Fragment, code, samplerCount: samplerCount, uniformBufferCount: 1,
			entryPoint: "fragment_main", name: $"{name}Fragment");
	}
}
