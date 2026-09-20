using System.Numerics;
using Foster.Framework;

namespace Myth;

// A "texture bucket" is the library of source images that Surfaces draw their layers
// from. Images are loaded directly from image files (no Aseprite bake) and classified by
// content: a fully-opaque black-and-white image becomes a 1-channel R8 texture (4x
// smaller), everything else stays RGBA, and a *_n / *_normal image is tagged as a
// normal map. Each texture is keyed by a stable path (e.g. "patterns/pattern_07") so
// room/surface references survive re-ordering or additions to the bucket.
public static partial class Assets
{
	// How a sampled texel should be expanded to RGBA in a shader. Derived from the
	// texture's format at load and handed to the shader per layer, so the runtime
	// otherwise never cares what format a texture is in.
	public enum TextureSwizzle
	{
		Rgba = 0,      // sample as-is
		Luminance = 1, // R8: rgb = r.rrr, a = 1
		Normal = 2,    // tangent-space normal map (rgb used directly)
	}

	public sealed class BucketTexture(string key, byte[] sourceImage, Texture texture, TextureSwizzle swizzle, Subtexture thumbnail)
	{
		public readonly string Key = key;
		public readonly byte[] SourceImage = sourceImage; // encoded PNG/QOI, for re-decoding CPU pixels (normal bake)
		public readonly Texture Texture = texture;
		public readonly TextureSwizzle Swizzle = swizzle;
		public readonly Subtexture Thumbnail = thumbnail; // small RGBA preview for the editor

		// Height-derived normal+albedo texture for procedural walls, built on first use (see
		// ProceduralWallRenderer.GetMaterialTexture). It is a pure function of SourceImage, so it
		// belongs to the bucket: cached on the renderer it was rebuilt per Game -- meaning a CPU
		// Sobel over the whole image on entry to every room with a procedural wall. Living here it
		// is also invalidated correctly, since a reload replaces the whole bucket.
		public Texture? MaterialTexture;
	}

	public sealed class TextureBucket(Texture thumbnailAtlas, Dictionary<string, BucketTexture> textures, List<string> order) : IDisposable
	{
		readonly Texture thumbnailAtlas = thumbnailAtlas;
		public readonly Dictionary<string, BucketTexture> Textures = textures;
		public readonly List<string> Order = order; // stable display order (sorted keys)

		public BucketTexture? Get(string? key)
			=> key != null && Textures.TryGetValue(key, out var t) ? t : null;
		public BucketTexture? Get(ContentRef<VisualTextureContent> reference) => Get(reference.Name);

		public void Dispose()
		{
			thumbnailAtlas.Dispose();
			foreach (var t in Textures.Values)
			{
				t.Texture.Dispose();
				t.MaterialTexture?.Dispose();
			}
		}
	}

	// The library every Surface samples from. Loaded from Sources/Textures/<bucket>/*.png in dev
	// and from packed QOI files in browser/shipped builds.
	public static TextureBucket? Textures2D { get; private set; }

	// Room-referenced Surface assets, keyed by id (the spec filename).
	public static readonly Dictionary<string, Surface> Surfaces = new(StringComparer.OrdinalIgnoreCase);

	public static Surface? GetSurface(string? id)
		=> id != null && Surfaces.TryGetValue(id, out var s) ? s : null;
	public static Surface? GetSurface(ContentRef<SurfaceContent> reference) => GetSurface(reference.Name);

	internal static void SetSurface(GraphicsDevice gfx, SurfaceSpec spec)
	{
		if (Textures2D == null)
			return;
		if (Surfaces.Remove(spec.Id, out var old))
			old.Dispose();
		Surfaces[spec.Id] = Surface.Build(gfx, spec, Textures2D);
	}

	internal static void RemoveSurface(string id)
	{
		if (Surfaces.Remove(id, out var surface))
			surface.Dispose();
	}

	internal static void BuildSurfaces(GraphicsDevice gfx, StorageContainer storage, string dir)
	{
		foreach (var surface in Surfaces.Values)
			surface.Dispose();
		Surfaces.Clear();

		if (Textures2D == null || !storage.DirectoryExists(dir))
			return;

		foreach (var file in storage.EnumerateDirectory(dir, "*.json", SearchOption.TopDirectoryOnly).Order(StringComparer.Ordinal))
		{
			try
			{
				var spec = SurfaceSpec.Parse(storage.ReadAllText(file), Path.GetFileNameWithoutExtension(file));
				Surfaces[spec.Id] = Surface.Build(gfx, spec, Textures2D);
			}
			catch (Exception e)
			{
				Console.Error.WriteLine($"Failed to load surface '{file}': {e.Message}");
			}
		}
	}

	const int BucketThumbSize = 48;
	const int BucketThumbColumns = 12;
	static readonly string[] BucketImageExtensions = [".png", ".qoi"];

	internal static Texture LoadTexture(GraphicsDevice gfx, StorageContainer storage, string path)
	{
		using var image = new Image(storage.ReadAllBytes(path));
		return new(gfx, image, path);
	}

	// Loads a bucket from a storage container (the dev Assets folder or the baked pack),
	// so the same code serves both. Keys are keyPrefix + the path relative to dir (minus
	// the image extension); pass an empty prefix when the storage layout already encodes the key.
	internal static TextureBucket LoadTextureBucket(GraphicsDevice gfx, StorageContainer storage, string dir, string keyPrefix)
	{
		var textures = new Dictionary<string, BucketTexture>(StringComparer.OrdinalIgnoreCase);
		var order = new List<string>();

		var entries = new List<(string Key, byte[] Encoded, Image Image, Texture Texture, TextureSwizzle Swizzle)>();
		if (storage.DirectoryExists(dir))
		{
			var files = new List<string>();
			foreach (var extension in BucketImageExtensions)
				files.AddRange(storage.EnumerateDirectory(dir, $"*{extension}", SearchOption.AllDirectories));
			files.Sort(StringComparer.Ordinal);
			foreach (var file in files)
			{
				var rel = Path.GetRelativePath(dir, file).Replace(Path.DirectorySeparatorChar, '/');
				var keyRel = rel[..^Path.GetExtension(rel).Length];
				var key = string.IsNullOrEmpty(keyPrefix) ? keyRel : $"{keyPrefix}/{keyRel}";

				var encoded = storage.ReadAllBytes(file);
				var image = new Image(encoded);
				var isNormal = key.EndsWith("_n", StringComparison.OrdinalIgnoreCase)
					|| key.EndsWith("_normal", StringComparison.OrdinalIgnoreCase);

				Texture texture;
				TextureSwizzle swizzle;
				if (!isNormal && IsGrayscaleOpaque(image))
				{
					// B&W -> single channel R8 (a quarter of the memory).
					var data = image.Data;
					var bytes = new byte[data.Length];
					for (var i = 0; i < bytes.Length; i++)
						bytes[i] = data[i].R;
					texture = new Texture(gfx, image.Width, image.Height, TextureFormat.R8, TextureFlags.None, key);
					texture.SetData<byte>(bytes);
					swizzle = TextureSwizzle.Luminance;
				}
				else
				{
					texture = new Texture(gfx, image, key);
					swizzle = isNormal ? TextureSwizzle.Normal : TextureSwizzle.Rgba;
				}

				entries.Add((key, encoded, image, texture, swizzle));
			}
		}

		// Pack downscaled previews into one RGBA atlas for the editor picker (ImGui
		// samples RGBA, so R8 textures can't be previewed directly).
		var columns = Math.Max(1, Math.Min(BucketThumbColumns, entries.Count));
		var rows = Math.Max(1, (entries.Count + columns - 1) / columns);
		using var atlasImage = new Image(columns * BucketThumbSize, rows * BucketThumbSize, Color.Transparent);
		for (var i = 0; i < entries.Count; i++)
		{
			var e = entries[i];
			var origin = new Point2((i % columns) * BucketThumbSize, (i / columns) * BucketThumbSize);
			BoxDownscaleInto(atlasImage, origin, e.Image.Data, e.Image.Width, e.Image.Height, BucketThumbSize, BucketThumbSize);
			e.Image.Dispose();
		}
		var thumbnailAtlas = new Texture(gfx, atlasImage, name: $"{keyPrefix}-thumbnails");

		for (var i = 0; i < entries.Count; i++)
		{
			var e = entries[i];
			var thumb = new Subtexture(thumbnailAtlas,
				new Rect((i % columns) * BucketThumbSize, (i / columns) * BucketThumbSize, BucketThumbSize, BucketThumbSize));
			textures[e.Key] = new BucketTexture(e.Key, e.Encoded, e.Texture, e.Swizzle, thumb);
			order.Add(e.Key);
		}

		return new TextureBucket(thumbnailAtlas, textures, order);
	}

	static bool IsGrayscaleOpaque(Image image)
	{
		foreach (var c in image.Data)
			if (c.A != 255 || c.R != c.G || c.G != c.B)
				return false;
		return true;
	}

	// Builds a seamless tangent-space normal map from an image's brightness (the
	// existing height->normal Sobel, whose height is luminance*alpha). Used for a
	// Surface's "normal from height" option.
	//
	// Heights are resolved into a flat array first: sampling them through the Image indexer cost
	// eight indexer reads and eight luminance conversions per pixel -- each re-fetching the pixel
	// span -- to recompute values every neighbouring pixel needed again.
	internal static Image GenerateNormalImage(Image source, bool wrap)
	{
		int width = source.Width, height = source.Height;
		var output = new Image(width, height);
		var heights = HeightField(source, source.Bounds);
		var target = output.Data;
		for (var y = 0; y < height; y++)
			for (var x = 0; x < width; x++)
			{
				var n = NormalFromHeights(heights, width, height, x, y, wrap);
				target[y * width + x] = new Color(n.X * 0.5f + 0.5f, n.Y * 0.5f + 0.5f, n.Z * 0.5f + 0.5f, 1f);
			}
		return output;
	}

	// Luminance*alpha heights for a region of an image. Every Sobel below reads each pixel eight
	// times over (once per neighbour), so resolving heights once up front turns the kernel into
	// plain array reads instead of eight Image indexer fetches plus eight luminance conversions.
	internal static float[] HeightField(Image image, RectInt bounds)
	{
		var heights = new float[bounds.Width * bounds.Height];
		var pixels = image.Data;
		for (var y = 0; y < bounds.Height; y++)
			for (var x = 0; x < bounds.Width; x++)
			{
				var color = pixels[(bounds.Y + y) * image.Width + bounds.X + x];
				heights[y * bounds.Width + x] = (color.R * 0.2126f + color.G * 0.7152f + color.B * 0.0722f) * color.A / (255 * 255);
			}
		return heights;
	}

	internal static Vector3 NormalFromHeights(float[] heights, int width, int height, int x, int y, bool wrap)
	{
		var topLeft = HeightAt(heights, width, height, x - 1, y - 1, wrap);
		var top = HeightAt(heights, width, height, x, y - 1, wrap);
		var topRight = HeightAt(heights, width, height, x + 1, y - 1, wrap);
		var left = HeightAt(heights, width, height, x - 1, y, wrap);
		var right = HeightAt(heights, width, height, x + 1, y, wrap);
		var bottomLeft = HeightAt(heights, width, height, x - 1, y + 1, wrap);
		var bottom = HeightAt(heights, width, height, x, y + 1, wrap);
		var bottomRight = HeightAt(heights, width, height, x + 1, y + 1, wrap);
		var sx = topLeft + 2 * left + bottomLeft - topRight - 2 * right - bottomRight;
		var sy = topLeft + 2 * top + topRight - bottomLeft - 2 * bottom - bottomRight;
		return Vector3.Normalize(new Vector3(sx, sy, 4 / HeightNormalStrength));
	}

	static float HeightAt(float[] heights, int width, int height, int x, int y, bool wrap)
	{
		if (wrap)
		{
			x = (x + width) % width;
			y = (y + height) % height;
		}
		else if (x < 0 || y < 0 || x >= width || y >= height)
			return 0;
		return heights[y * width + x];
	}
}
