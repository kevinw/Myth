using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Foster.Framework;

namespace Myth;

/// A named world-space rectangle that supplies the music area for every room inside it.
/// Regions are painted in the editor and cover future rooms automatically; a room's own
/// MusicArea field remains as a per-room override for exceptions. The record is a JSON
/// object per region so later fields (ambient defaults, reverb, ...) can slot in.
public sealed class MusicRegion
{
	public string Area = Manager.DefaultMusicArea;
	public RectInt Rect; // tile space

	public long TileArea => (long)Rect.Width * Rect.Height;
}

public static class MusicRegions
{
	public static readonly List<MusicRegion> Regions = [];

	// Lives next to the world's rooms folder (never inside it: every json in there loads
	// as a room), e.g. Assets/Rooms/Myth.regions.json.
	public static string FilePath => Path.Join(Assets.RoomsPath, $"{Assets.CurrentWorldId}.regions.json");
	static string ContentKey => $"Rooms/{Assets.CurrentWorldId}.regions.json";

	public static void Load()
	{
		Regions.Clear();
		if (!Assets.Content.FileExists(ContentKey))
			return;

		try
		{
			var json = JsonUtil.Parse(Assets.Content.ReadAllText(ContentKey))?.AsObject();
			if (json == null || !json.TryGetPropertyValue("regions", out var regionsNode) || regionsNode is not JsonArray entries)
				return;
			foreach (var node in entries)
			{
				if (node is not JsonObject entry)
					continue;
				var area = JsonUtil.ReadString(entry, "area");
				if (string.IsNullOrEmpty(area))
					continue;
				if (!entry.TryGetPropertyValue("rect", out var rectNode) || rectNode is not JsonArray rect || rect.Count != 4)
					continue;
				if (!JsonUtil.TryReadInt(rect[0], out var x) || !JsonUtil.TryReadInt(rect[1], out var y) ||
					!JsonUtil.TryReadInt(rect[2], out var w) || !JsonUtil.TryReadInt(rect[3], out var h))
					continue;
				Regions.Add(new MusicRegion
				{
					Area = area,
					Rect = new RectInt(x, y, Math.Max(1, w), Math.Max(1, h)),
				});
			}
		}
		catch (Exception e)
		{
			Console.Error.WriteLine($"Failed to load music regions '{FilePath}': {e.Message}");
		}
	}

	public static void Save()
	{
		using var stream = File.Create(FilePath);
		using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, NewLine = "\n" });
		writer.WriteStartObject();
		writer.WriteStartArray("regions");
		foreach (var region in Regions)
		{
			writer.WriteStartObject();
			writer.WriteString("area", region.Area);
			writer.WriteStartArray("rect");
			writer.WriteNumberValue(region.Rect.X);
			writer.WriteNumberValue(region.Rect.Y);
			writer.WriteNumberValue(region.Rect.Width);
			writer.WriteNumberValue(region.Rect.Height);
			writer.WriteEndArray();
			writer.WriteEndObject();
		}
		writer.WriteEndArray();
		writer.WriteEndObject();
	}

	/// The music area a room should play: its own MusicArea override if set, else the
	/// smallest region containing the room's center, else null (caller uses the default).
	public static string? Resolve(Room room)
	{
		if (!string.IsNullOrEmpty(room.MusicArea))
			return room.MusicArea;
		return ResolveRegion(room)?.Area;
	}

	/// The region a room inherits from (ignoring the room's own override).
	public static MusicRegion? ResolveRegion(Room room)
	{
		var center = new Vector2(
			room.TileSpacePosition.X + room.Size.X * 0.5f,
			room.TileSpacePosition.Y + room.Size.Y * 0.5f);
		MusicRegion? best = null;
		foreach (var region in Regions)
		{
			if (!new Rect(region.Rect.X, region.Rect.Y, region.Rect.Width, region.Rect.Height).Contains(center))
				continue;
			if (best == null || region.TileArea < best.TileArea)
				best = region;
		}
		return best;
	}
}
