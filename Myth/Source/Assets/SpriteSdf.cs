using System.Numerics;
using Foster.Framework;

namespace Myth;

/// <summary>
/// Generates signed-distance-field textures for sprite silhouettes and packs
/// them into a single atlas. Each frame's SDF stores the exterior distance (in
/// pixels, 0 inside the silhouette) to the nearest opaque pixel, padded by
/// <see cref="Margin"/> so an outward aura up to that distance can be drawn.
///
/// Currently generated for the player only, at startup (see Manager.Startup).
/// </summary>
public static class SpriteSdf
{
	public const int Margin = 24;       // px of distance field kept around the silhouette
	private const int AlphaThreshold = 127;

	private static Texture? atlas;
	private static readonly List<Subtexture> playerFrames = [];

	public static Subtexture? PlayerFrame(int index)
		=> index >= 0 && index < playerFrames.Count ? playerFrames[index] : null;

	public static void Generate(GraphicsDevice gfx)
	{
		atlas?.Dispose();
		atlas = null;
		playerFrames.Clear();

		if (Assets.GetSprite("player") is not { } player || player.Frames.Count == 0)
			return;
		if (Assets.Atlas is not { } colorAtlas)
			return;

		var aw = colorAtlas.Width;
		var ah = colorAtlas.Height;
		var atlasPixels = new Color[aw * ah];
		colorAtlas.GetData<Color>(atlasPixels);

		var packer = new Packer { Trim = false, Padding = 2 };
		for (var i = 0; i < player.Frames.Count; i++)
			packer.Add($"player_sdf_{i}", BuildFrameSdf(atlasPixels, aw, ah, player.Frames[i].Subtexture));

		var output = packer.Pack();
		if (output.Pages.Count == 0)
			return;

		atlas = new Texture(gfx, output.Pages[0], name: "SpriteSdfAtlas");
		var byName = new Dictionary<string, Subtexture>();
		foreach (var entry in output.Entries)
			byName[entry.Name] = new Subtexture(atlas, entry.Source, entry.Frame);

		for (var i = 0; i < player.Frames.Count; i++)
			if (byName.TryGetValue($"player_sdf_{i}", out var sub))
				playerFrames.Add(sub);
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
		playerFrames.Clear();
	}
}
