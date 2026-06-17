using System.Numerics;
using Foster.Framework;

namespace Myth;

/// <summary>
/// Generates signed-distance-field textures for sprite silhouettes and packs
/// them into a single atlas. Each frame's SDF stores the exterior distance (in
/// pixels, 0 inside the silhouette) to the nearest opaque pixel, padded by
/// <see cref="Margin"/> so an outward aura up to that distance can be drawn.
///
/// Generated for a small set of named sprites at startup (see Manager.Startup).
/// </summary>
public static class SpriteSdf
{
	public const int Margin = 24;       // px of distance field kept around the silhouette
	private const int AlphaThreshold = 127;

	// Sprites we build SDFs for.
	private static readonly string[] SpriteNames = ["player", "bud"];

	private static Texture? atlas;
	private static readonly Dictionary<string, List<Subtexture>> framesByName = new();

	public static Texture? Atlas => atlas;
	public static Vector2 AtlasSize => atlas != null ? new Vector2(atlas.Width, atlas.Height) : Vector2.One;

	public static Subtexture? Frame(string sprite, int index)
		=> framesByName.TryGetValue(sprite, out var frames) && index >= 0 && index < frames.Count
			? frames[index]
			: null;

	// The color atlas is read back from the GPU asynchronously, because WebGPU
	// cannot read texture data back synchronously on the browser main thread.
	// Generate() kicks off the request; Update() polls until the pixels arrive
	// (immediately on desktop, a frame or two later in the browser) and then
	// builds the SDF atlas. Until then Frame() returns null and the aura
	// simply isn't drawn.
	private static GraphicsDevice? pendingGfx;
	private static TextureDownload<Color> pendingDownload;
	private static Color[]? pendingPixels;
	private static List<(string Name, Subtexture[] Frames)>? pendingSprites;
	private static int pendingAtlasWidth;
	private static int pendingAtlasHeight;

	public static void Generate(GraphicsDevice gfx)
	{
		atlas?.Dispose();
		atlas = null;
		framesByName.Clear();
		pendingPixels = null;
		pendingSprites = null;

		if (Assets.Atlas is not { } colorAtlas)
			return;

		var sprites = new List<(string, Subtexture[])>();
		foreach (var name in SpriteNames)
		{
			if (Assets.GetSprite(name) is not { } sprite || sprite.Frames.Count == 0)
				continue;
			var frames = new Subtexture[sprite.Frames.Count];
			for (var i = 0; i < frames.Length; i++)
				frames[i] = sprite.Frames[i].Subtexture;
			sprites.Add((name, frames));
		}
		if (sprites.Count == 0)
			return;

		pendingGfx = gfx;
		pendingAtlasWidth = colorAtlas.Width;
		pendingAtlasHeight = colorAtlas.Height;
		pendingPixels = new Color[pendingAtlasWidth * pendingAtlasHeight];
		pendingSprites = sprites;

		// Reuse the same download slot across reloads so we don't leak staging buffers.
		pendingDownload = colorAtlas.RequestDownload<Color>(pendingDownload);

		// Desktop backends complete the readback synchronously, so this finishes
		// the build now; WebGPU returns false here and finishes in a later Update.
		TryFinishDownload();
	}

	/// <summary>
	/// Polls the in-flight atlas readback and builds the SDF atlas once the
	/// pixels are available. No-op when there's no pending download.
	/// </summary>
	public static void Update()
	{
		if (pendingPixels != null)
			TryFinishDownload();
	}

	private static void TryFinishDownload()
	{
		if (pendingPixels is not { } atlasPixels || pendingSprites is not { } sprites || pendingGfx is not { } gfx)
			return;
		if (!pendingDownload.TryRead(atlasPixels))
			return; // GPU copy not ready yet; try again next frame

		var aw = pendingAtlasWidth;
		var ah = pendingAtlasHeight;
		pendingPixels = null;
		pendingSprites = null;

		var packer = new Packer { Trim = false, Padding = 2 };
		foreach (var (name, frames) in sprites)
			for (var i = 0; i < frames.Length; i++)
				packer.Add($"{name}_sdf_{i}", BuildFrameSdf(atlasPixels, aw, ah, frames[i]));

		var output = packer.Pack();
		if (output.Pages.Count == 0)
			return;

		atlas = new Texture(gfx, output.Pages[0], name: "SpriteSdfAtlas");
		var byName = new Dictionary<string, Subtexture>();
		foreach (var entry in output.Entries)
			byName[entry.Name] = new Subtexture(atlas, entry.Source, entry.Frame);

		framesByName.Clear();
		foreach (var (name, frames) in sprites)
		{
			var list = new List<Subtexture>(frames.Length);
			for (var i = 0; i < frames.Length; i++)
				if (byName.TryGetValue($"{name}_sdf_{i}", out var sub))
					list.Add(sub);
			framesByName[name] = list;
		}
	}

	// Builds a padded SDF image for one frame: the trimmed silhouette is placed in
	// its full-frame slot, then offset by Margin, and the exterior distance field
	// is computed across the whole padded image.
	private static Image BuildFrameSdf(Color[] atlasPixels, int aw, int ah, in Subtexture frame)
	{
		var fw = (int)MathF.Round(frame.Frame.Width);
		var fh = (int)MathF.Round(frame.Frame.Height);
		var pw = fw + Margin * 2;
		var ph = fh + Margin * 2;

		var source = frame.Source;
		var sx0 = (int)MathF.Round(source.X);
		var sy0 = (int)MathF.Round(source.Y);
		var sw = (int)MathF.Round(source.Width);
		var sh = (int)MathF.Round(source.Height);
		// Trim offset: where the trimmed source sits within the full frame.
		var ox = Margin - (int)MathF.Round(frame.Frame.X);
		var oy = Margin - (int)MathF.Round(frame.Frame.Y);

		var inside = new bool[pw * ph];
		for (var y = 0; y < sh; y++)
			for (var x = 0; x < sw; x++)
			{
				var ax = sx0 + x;
				var ay = sy0 + y;
				if (ax < 0 || ay < 0 || ax >= aw || ay >= ah)
					continue;
				if (atlasPixels[ay * aw + ax].A <= AlphaThreshold)
					continue;
				var px = ox + x;
				var py = oy + y;
				if (px >= 0 && py >= 0 && px < pw && py < ph)
					inside[py * pw + px] = true;
			}

		var dist = DistanceField(inside, pw, ph);
		var pixels = new Color[pw * ph];
		for (var i = 0; i < pixels.Length; i++)
		{
			var d = (byte)Math.Min((int)MathF.Round(dist[i]), 255);
			pixels[i] = new Color(d, d, d, (byte)255);
		}
		return new Image(pw, ph, pixels);
	}

	// 8SSEDT: exterior Euclidean distance to the nearest "inside" pixel.
	private static float[] DistanceField(bool[] inside, int w, int h)
	{
		const int inf = 9999;
		var dx = new int[w * h];
		var dy = new int[w * h];
		for (var i = 0; i < dx.Length; i++)
		{
			dx[i] = inside[i] ? 0 : inf;
			dy[i] = inside[i] ? 0 : inf;
		}

		void Compare(ref int pdx, ref int pdy, int x, int y, int offx, int offy)
		{
			var nx = x + offx;
			var ny = y + offy;
			int odx, ody;
			if (nx < 0 || ny < 0 || nx >= w || ny >= h)
			{
				odx = inf;
				ody = inf;
			}
			else
			{
				odx = dx[ny * w + nx];
				ody = dy[ny * w + nx];
			}
			odx += offx;
			ody += offy;
			if (odx * odx + ody * ody < pdx * pdx + pdy * pdy)
			{
				pdx = odx;
				pdy = ody;
			}
		}

		for (var y = 0; y < h; y++)
		{
			for (var x = 0; x < w; x++)
			{
				var i = y * w + x;
				int pdx = dx[i], pdy = dy[i];
				Compare(ref pdx, ref pdy, x, y, -1, 0);
				Compare(ref pdx, ref pdy, x, y, 0, -1);
				Compare(ref pdx, ref pdy, x, y, -1, -1);
				Compare(ref pdx, ref pdy, x, y, 1, -1);
				dx[i] = pdx;
				dy[i] = pdy;
			}
			for (var x = w - 1; x >= 0; x--)
			{
				var i = y * w + x;
				int pdx = dx[i], pdy = dy[i];
				Compare(ref pdx, ref pdy, x, y, 1, 0);
				dx[i] = pdx;
				dy[i] = pdy;
			}
		}
		for (var y = h - 1; y >= 0; y--)
		{
			for (var x = w - 1; x >= 0; x--)
			{
				var i = y * w + x;
				int pdx = dx[i], pdy = dy[i];
				Compare(ref pdx, ref pdy, x, y, 1, 0);
				Compare(ref pdx, ref pdy, x, y, 0, 1);
				Compare(ref pdx, ref pdy, x, y, -1, 1);
				Compare(ref pdx, ref pdy, x, y, 1, 1);
				dx[i] = pdx;
				dy[i] = pdy;
			}
			for (var x = 0; x < w; x++)
			{
				var i = y * w + x;
				int pdx = dx[i], pdy = dy[i];
				Compare(ref pdx, ref pdy, x, y, -1, 0);
				dx[i] = pdx;
				dy[i] = pdy;
			}
		}

		var dist = new float[w * h];
		for (var i = 0; i < dist.Length; i++)
			dist[i] = MathF.Sqrt(dx[i] * dx[i] + dy[i] * dy[i]);
		return dist;
	}

	public static void Unload()
	{
		atlas?.Dispose();
		atlas = null;
		framesByName.Clear();
		pendingPixels = null;
		pendingSprites = null;
	}
}
