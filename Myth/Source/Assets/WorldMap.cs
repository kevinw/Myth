using Foster.Framework;

namespace Myth;

/// One baked image of every room in the world, sampled by the map overlay for its colors.
/// The bake is a GPU render of each room (see Manager.WorldMapBake.cs) so it carries lighting,
/// props and backdrops; it lives beside the visual asset pack and ships with it.
///
/// The image covers <see cref="Bounds"/> (world pixels) linearly, so a world rect maps to a
/// source rect by a subtract-and-scale. Bounds is baked into the sidecar rather than recomputed
/// at load, so moving a room only misplaces the map until the next bake instead of skewing it.
public static class WorldMap
{
	public const string ImageFile = "world-map.qoi";
	public const string BoundsFile = "world-map.json";

	/// Widest the bake is allowed to be; the world scales down to fit and keeps its aspect.
	/// -1 sizes it so one texture pixel is one world tile, matching the map's own tile grid.
	public const int MaxWidth = -1;

	public static Texture? Texture { get; private set; }

	/// World-pixel rect the image covers.
	public static RectInt Bounds { get; private set; }

	public static bool Available => Texture != null && Bounds.Width > 0 && Bounds.Height > 0;

	/// World-pixel rect enclosing every room, the region a bake covers.
	public static RectInt WorldPixelBounds()
	{
		var bounds = new RectInt();
		foreach (var room in Assets.RoomsById.Values)
			bounds = bounds.Width <= 0 ? room.WorldBounds : bounds.Conflate(room.WorldBounds);
		return bounds;
	}

	/// Pixel size a bake of <paramref name="bounds"/> gets, and the scale it is rendered at.
	public static (Point2 Size, float Scale) BakeSize(RectInt bounds)
	{
		if (bounds.Width <= 0 || bounds.Height <= 0)
			return (Point2.Zero, 0);

		var scale = MaxWidth > 0
			? Math.Min(1.0f, MaxWidth / (float)bounds.Width)
			: 1.0f / Game.TileSize;
		return (new Point2(
			Math.Max(1, (int)MathF.Round(bounds.Width * scale)),
			Math.Max(1, (int)MathF.Round(bounds.Height * scale))), scale);
	}

	/// The source rect in the baked image covering a world-pixel rect.
	public static Rect SourceRect(RectInt worldRect)
	{
		if (!Available)
			return new Rect();

		var scaleX = Texture!.Width / (float)Bounds.Width;
		var scaleY = Texture.Height / (float)Bounds.Height;
		return new Rect(
			(worldRect.X - Bounds.X) * scaleX,
			(worldRect.Y - Bounds.Y) * scaleY,
			worldRect.Width * scaleX,
			worldRect.Height * scaleY);
	}

	public static void Load(GraphicsDevice gfx, StorageContainer pack)
	{
		Unload();
		if (!pack.FileExists(ImageFile) || !pack.FileExists(BoundsFile))
			return;

		try
		{
			var bounds = ReadBounds(pack.ReadAllBytes(BoundsFile));
			if (bounds.Width <= 0 || bounds.Height <= 0)
				return;

			using var image = new Image(pack.ReadAllBytes(ImageFile));
			Texture = new Texture(gfx, image, name: "WorldMap");
			Bounds = bounds;
		}
		catch (Exception e)
		{
			Console.WriteLine($"World map failed to load: {e.Message}");
			Unload();
		}
	}

	public static void Unload()
	{
		Texture?.Dispose();
		Texture = null;
		Bounds = new RectInt();
	}

	public static byte[] WriteBounds(RectInt bounds)
		=> System.Text.Encoding.UTF8.GetBytes(
			$"{{\"x\":{bounds.X},\"y\":{bounds.Y},\"width\":{bounds.Width},\"height\":{bounds.Height}}}\n");

	static RectInt ReadBounds(ReadOnlySpan<byte> json)
	{
		var node = System.Text.Json.Nodes.JsonNode.Parse(System.Text.Encoding.UTF8.GetString(json))?.AsObject()
			?? throw new InvalidOperationException($"{BoundsFile} is not an object.");
		return new RectInt(
			JsonUtil.ReadInt(node, "x", 0),
			JsonUtil.ReadInt(node, "y", 0),
			JsonUtil.ReadInt(node, "width", 0),
			JsonUtil.ReadInt(node, "height", 0));
	}
}
