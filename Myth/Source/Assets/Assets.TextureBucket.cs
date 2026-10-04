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
		// The same height split for pen-drawn actors: a shade mask plus normals (see PenMaterial.Get).
		internal PenMaterial? PenMaterial;
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
				t.PenMaterial?.Dispose();
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
		var files = new List<string>();
		if (storage.DirectoryExists(dir))
			foreach (var extension in BucketImageExtensions)
				files.AddRange(storage.EnumerateDirectory(dir, $"*{extension}", SearchOption.AllDirectories));
		files.Sort(StringComparer.Ordinal);

		// Storage reads stay serial -- a pak is one shared zip -- but at a few hundred patterns
		// the decode, the B&W classification and the thumbnail downscale are the bulk of the
		// load, and each file's are independent of every other's.
		var encoded = new byte[files.Count][];
		for (var i = 0; i < files.Count; i++)
			encoded[i] = storage.ReadAllBytes(files[i]);

		var columns = Math.Max(1, Math.Min(BucketThumbColumns, files.Count));
		var rows = Math.Max(1, (files.Count + columns - 1) / columns);
		// Downscaled previews for the editor picker, packed into one RGBA atlas (ImGui samples
		// RGBA, so R8 textures can't be previewed directly). Each entry owns a disjoint cell.
		using var atlasImage = new Image(columns * BucketThumbSize, rows * BucketThumbSize, Color.Transparent);

		var keys = new string[files.Count];
		var images = new Image[files.Count];
		var swizzles = new TextureSwizzle[files.Count];
		var luminance = new byte[files.Count][]; // R8 bytes, non-null only for a B&W image
		Par.For(0, files.Count, i =>
		{
			var rel = Path.GetRelativePath(dir, files[i]).Replace(Path.DirectorySeparatorChar, '/');
			var keyRel = rel[..^Path.GetExtension(rel).Length];
			var key = keys[i] = string.IsNullOrEmpty(keyPrefix) ? keyRel : $"{keyPrefix}/{keyRel}";

			var image = images[i] = new Image(encoded[i]);
			var isNormal = key.EndsWith("_n", StringComparison.OrdinalIgnoreCase)
				|| key.EndsWith("_normal", StringComparison.OrdinalIgnoreCase);

			if (!isNormal && IsGrayscaleOpaque(image))
			{
				// B&W -> single channel R8 (a quarter of the memory).
				var data = image.Data;
				var bytes = luminance[i] = new byte[data.Length];
				for (var j = 0; j < bytes.Length; j++)
					bytes[j] = data[j].R;
				swizzles[i] = TextureSwizzle.Luminance;
			}
			else
			{
				swizzles[i] = isNormal ? TextureSwizzle.Normal : TextureSwizzle.Rgba;
			}

			BoxDownscaleInto(atlasImage, new Point2((i % columns) * BucketThumbSize, (i / columns) * BucketThumbSize),
				image.Data, image.Width, image.Height, BucketThumbSize, BucketThumbSize);
		});

		var thumbnailAtlas = new Texture(gfx, atlasImage, name: $"{keyPrefix}-thumbnails");
		var textures = new Dictionary<string, BucketTexture>(StringComparer.OrdinalIgnoreCase);
		var order = new List<string>();
		for (var i = 0; i < files.Count; i++)
		{
			var key = keys[i];
			var image = images[i];
			Texture texture;
			if (luminance[i] is { } bytes)
			{
				texture = new Texture(gfx, image.Width, image.Height, TextureFormat.R8, TextureFlags.None, key);
				texture.SetData<byte>(bytes);
			}
			else
			{
				texture = new Texture(gfx, image, key);
			}
			image.Dispose();

			var thumb = new Subtexture(thumbnailAtlas,
				new Rect((i % columns) * BucketThumbSize, (i / columns) * BucketThumbSize, BucketThumbSize, BucketThumbSize));
			textures[key] = new BucketTexture(key, encoded[i], texture, swizzles[i], thumb);
			order.Add(key);
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
		=> GenerateNormalImage(source, wrap, NormalBake.Default);

	internal static Image GenerateNormalImage(Image source, bool wrap, NormalBake bake)
	{
		int width = source.Width, height = source.Height;
		var output = new Image(width, height);
		var heights = HeightField(source, source.Bounds);
		var target = output.Data;
		for (var y = 0; y < height; y++)
			for (var x = 0; x < width; x++)
			{
				var n = NormalFromHeights(heights, width, height, x, y, wrap, bake);
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

	/// How a height field is read into slopes. The choice is about *where a feature's shading lands*,
	/// not how strong it is: a central kernel estimates the slope at the sample point, so a 1px ridge
	/// -- whose slope at its own centre is zero -- comes out flat with its highlight and shadow pushed
	/// onto the neighbours. At 8px a tile that reads as softened, which is the opposite of hand-painted
	/// shading, where the raised pixel is the lit one.
	public enum NormalKernel
	{
		/// 3x3 Sobel. Smooth and symmetric; the 1-2-1 arm also bleeds dots and dashes sideways.
		Sobel,
		/// Central difference with no perpendicular blur. Sobel's placement, but neighbouring detail
		/// stays out of it.
		Crisp,
		/// Backward difference: each pixel is a facet, lit by its own step up from the pixel behind it.
		/// A 1px ridge lights the ridge and shadows the pixel past it -- the pixel-art convention.
		Facet,
	}

	/// Kernel plus a tilt multiplier. Strength 1 with Sobel is the long-standing bake, so anything that
	/// doesn't ask for something else keeps exactly the normals it had.
	public readonly record struct NormalBake(NormalKernel Kernel, float Strength)
	{
		public static NormalBake Default => new(NormalKernel.Sobel, 1f);
	}

	// Each kernel reaches a different maximum over the same 0..1 height field -- Sobel's arms sum to 4,
	// a single difference reaches 1 -- so z has to scale with it or swapping kernel silently changes
	// how steep everything is.
	static float KernelReach(NormalKernel kernel) => kernel == NormalKernel.Sobel ? 4 : 1;

	internal static Vector3 NormalFromHeights(float[] heights, int width, int height, int x, int y, bool wrap)
		=> NormalFromHeights(heights, width, height, x, y, wrap, NormalBake.Default);

	internal static Vector3 NormalFromHeights(float[] heights, int width, int height, int x, int y, bool wrap, NormalBake bake)
	{
		float At(int dx, int dy) => HeightAt(heights, width, height, x + dx, y + dy, wrap);

		float sx, sy;
		switch (bake.Kernel)
		{
			case NormalKernel.Crisp:
				sx = At(-1, 0) - At(1, 0);
				sy = At(0, -1) - At(0, 1);
				break;
			case NormalKernel.Facet:
				sx = At(-1, 0) - At(0, 0);
				sy = At(0, -1) - At(0, 0);
				break;
			default:
				sx = At(-1, -1) + 2 * At(-1, 0) + At(-1, 1) - At(1, -1) - 2 * At(1, 0) - At(1, 1);
				sy = At(-1, -1) + 2 * At(0, -1) + At(1, -1) - At(-1, 1) - 2 * At(0, 1) - At(1, 1);
				break;
		}

		var tilt = HeightNormalStrength * MathF.Max(bake.Strength, 0.01f);
		return Vector3.Normalize(new Vector3(sx, sy, KernelReach(bake.Kernel) / tilt));
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
