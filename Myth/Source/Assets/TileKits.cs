using System.Text.Json;
using System.Text.Json.Nodes;
using Foster.Framework;

namespace Myth;

/// Generative dual-tileset kits authored as JSON (Assets/Tilesets/&lt;name&gt;.kit.json)
/// instead of .ase files. A spec carries everything the .ase kits encoded in pixels —
/// palette, fill patterns, edge-noise weights, heights, params — plus the Factory
/// registration info (surface, chars, reflection) so a new tile type is pure data.
public sealed class TileKitSpec
{
	public required string Name;
	public string MaskKit = "stone-kit";
	public long Seed = 1;
	public string EdgeWidth = "2";
	public float Wobble = 0.6f;
	public string Surface = "Brick";
	public char PreferredChar;
	public char SlopePreferredChar;
	public Factory.ReflectionMode Reflection = Factory.ReflectionMode.None;
	public float ReflectionOpacity = 0.5f;
	public float AcousticReflectivity = 1.0f;
	public int EdgeTileCount = 4;
	public readonly Dictionary<char, Color> Palette = [];
	public readonly Dictionary<char, float> Heights = [];
	public readonly List<string[]> Fills = [];
	public readonly List<(Color Color, float Weight)> Edge = [];
	public readonly List<(Color Color, float Weight)> EdgeTop = [];
	public readonly List<(Color Color, float Weight)> EdgeBottom = [];

	public string DualName => $"{Name}-dual";
	public string SlopeEntryName => $"{char.ToUpperInvariant(Name[0])}{Name[1..]} Slope";
	public bool HasEdges => Edge.Count > 0;
}

public static class TileKits
{
	public const string Extension = ".kit.json";

	public static readonly List<TileKitSpec> Specs = [];

	public static void LoadSpecs(string tilesetsPath)
	{
		Specs.Clear();
		if (!Directory.Exists(tilesetsPath))
			return;
		foreach (var file in Directory.EnumerateFiles(tilesetsPath, $"*{Extension}").Order(StringComparer.OrdinalIgnoreCase))
			Specs.Add(Parse(file));
	}

	static TileKitSpec Parse(string path)
	{
		var fileName = Path.GetFileName(path);
		var name = fileName[..^Extension.Length];
		JsonObject json;
		try
		{
			json = JsonNode.Parse(File.ReadAllText(path))?.AsObject() ?? throw new InvalidOperationException("root is not an object");
		}
		catch (Exception e)
		{
			throw new InvalidOperationException($"Tile kit '{fileName}': {e.Message}", e);
		}

		var spec = new TileKitSpec { Name = name };
		spec.MaskKit = JsonUtil.ReadString(json, "mask", spec.MaskKit);
		spec.Seed = JsonUtil.ReadInt(json, "seed", 1);
		spec.EdgeWidth = JsonUtil.ReadString(json, "edgeWidth", spec.EdgeWidth);
		spec.Wobble = Math.Clamp(JsonUtil.ReadFloat(json, "wobble", spec.Wobble), 0f, 1f);
		spec.Surface = JsonUtil.ReadString(json, "surface", spec.Surface);
		spec.PreferredChar = ReadChar(json, "char");
		spec.SlopePreferredChar = ReadChar(json, "slopeChar");
		spec.ReflectionOpacity = JsonUtil.ReadFloat(json, "reflectionOpacity", spec.ReflectionOpacity);
		spec.AcousticReflectivity = JsonUtil.ReadFloat(json, "acousticReflectivity", spec.AcousticReflectivity);
		spec.EdgeTileCount = Math.Max(1, JsonUtil.ReadInt(json, "edgeTiles", spec.EdgeTileCount));
		if (JsonUtil.ReadString(json, "reflection") is { } reflection)
			spec.Reflection = Enum.Parse<Factory.ReflectionMode>(reflection, ignoreCase: true);

		if (json.TryGetPropertyValue("palette", out var paletteNode) && paletteNode is JsonObject palette)
			foreach (var (key, value) in palette)
				spec.Palette[SingleChar(key, name, "palette")] = JsonUtil.ReadColor(value)
					?? throw new InvalidOperationException($"Tile kit '{name}': palette '{key}' is not a color.");

		if (json.TryGetPropertyValue("heights", out var heightsNode) && heightsNode is JsonObject heights)
			foreach (var (key, value) in heights)
				spec.Heights[SingleChar(key, name, "heights")] = Math.Clamp((float)(value?.GetValue<double>() ?? 0), 0f, 1f);

		if (json.TryGetPropertyValue("fills", out var fillsNode) && fillsNode is JsonArray fills)
			foreach (var fillNode in fills)
			{
				if (fillNode is not JsonArray rows)
					throw new InvalidOperationException($"Tile kit '{name}': each fill must be an array of {Game.TileSize} row strings.");
				var tile = rows.Select(row => row?.GetValue<string>() ?? "").ToArray();
				if (tile.Length != Game.TileSize || tile.Any(row => row.Length != Game.TileSize))
					throw new InvalidOperationException($"Tile kit '{name}': fill tiles must be {Game.TileSize} rows of {Game.TileSize} characters.");
				foreach (var key in tile.SelectMany(row => row))
					if (!spec.Palette.ContainsKey(key))
						throw new InvalidOperationException($"Tile kit '{name}': fill uses '{key}' which is not in the palette.");
				spec.Fills.Add(tile);
			}

		if (spec.Fills.Count == 0)
			throw new InvalidOperationException($"Tile kit '{name}': at least one fill tile is required.");
		if (spec.Heights.Count > 0)
			foreach (var key in spec.Palette.Keys)
				if (!spec.Heights.ContainsKey(key))
					throw new InvalidOperationException($"Tile kit '{name}': heights must cover every palette key (missing '{key}').");

		ReadWeights(json, "edge", spec.Edge, name);
		ReadWeights(json, "edgeTop", spec.EdgeTop, name);
		ReadWeights(json, "edgeBottom", spec.EdgeBottom, name);
		return spec;
	}

	static char ReadChar(JsonObject json, string key)
	{
		var value = JsonUtil.ReadString(json, key);
		return string.IsNullOrEmpty(value) ? '\0' : value[0];
	}

	static char SingleChar(string key, string kitName, string section)
		=> key.Length == 1 ? key[0]
			: throw new InvalidOperationException($"Tile kit '{kitName}': {section} key '{key}' must be a single character.");

	static void ReadWeights(JsonObject json, string key, List<(Color, float)> target, string kitName)
	{
		if (!json.TryGetPropertyValue(key, out var node) || node is not JsonArray entries)
			return;
		foreach (var entryNode in entries)
		{
			if (entryNode is not JsonArray pair || pair.Count != 2 ||
				JsonUtil.ReadColor(pair[0]) is not { } color || !JsonUtil.TryReadFloat(pair[1], out var weight) || weight <= 0)
				throw new InvalidOperationException($"Tile kit '{kitName}': '{key}' entries must be [\"RRGGBB\", weight] pairs.");
			target.Add((color, weight));
		}
	}

	public static string SpecPath(string name)
		=> Path.Join(Assets.AssetsPath, "Tilesets", name + Extension);

	public static TileKitSpec Clone(TileKitSpec spec)
	{
		var clone = new TileKitSpec
		{
			Name = spec.Name,
			MaskKit = spec.MaskKit,
			Seed = spec.Seed,
			EdgeWidth = spec.EdgeWidth,
			Wobble = spec.Wobble,
			Surface = spec.Surface,
			PreferredChar = spec.PreferredChar,
			SlopePreferredChar = spec.SlopePreferredChar,
			Reflection = spec.Reflection,
			ReflectionOpacity = spec.ReflectionOpacity,
			AcousticReflectivity = spec.AcousticReflectivity,
			EdgeTileCount = spec.EdgeTileCount,
		};
		foreach (var (key, value) in spec.Palette)
			clone.Palette[key] = value;
		foreach (var (key, value) in spec.Heights)
			clone.Heights[key] = value;
		foreach (var fill in spec.Fills)
			clone.Fills.Add((string[])fill.Clone());
		clone.Edge.AddRange(spec.Edge);
		clone.EdgeTop.AddRange(spec.EdgeTop);
		clone.EdgeBottom.AddRange(spec.EdgeBottom);
		return clone;
	}

	/// A minimal starting spec for the editor's "new kit" flow: two-color body with a few
	/// seam pixels, a grey rim, heights on. Chars are left unset (assigned at registration).
	public static TileKitSpec CreateDefault(string name)
	{
		var spec = new TileKitSpec { Name = name };
		spec.Palette['a'] = new Color(69, 40, 60, 255);
		spec.Palette['b'] = new Color(143, 86, 59, 255);
		spec.Heights['a'] = 0.67f;
		spec.Heights['b'] = 0f;
		spec.Fills.Add([
			"aaabaaaa",
			"bbabbbbb",
			"aaaaabaa",
			"aaaaabaa",
			"babbbbab",
			"aabaaaaa",
			"aabaaaaa",
			"aaaaabaa",
		]);
		spec.Edge.Add((new Color(155, 173, 183, 255), 45));
		spec.Edge.Add((new Color(132, 126, 135, 255), 40));
		spec.Edge.Add((new Color(89, 86, 82, 255), 15));
		spec.Seed = Random.Shared.Next(1, int.MaxValue);
		return spec;
	}

	public static void Save(TileKitSpec spec)
	{
		using var stream = File.Create(SpecPath(spec.Name));
		using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, NewLine = "\n" });
		writer.WriteStartObject();
		writer.WriteString("surface", spec.Surface);
		if (spec.PreferredChar != '\0')
			writer.WriteString("char", spec.PreferredChar.ToString());
		if (spec.SlopePreferredChar != '\0')
			writer.WriteString("slopeChar", spec.SlopePreferredChar.ToString());
		writer.WriteString("mask", spec.MaskKit);
		writer.WriteNumber("seed", spec.Seed);
		writer.WriteString("edgeWidth", spec.EdgeWidth);
		writer.WriteNumber("wobble", MathF.Round(spec.Wobble, 3));
		if (spec.Reflection != Factory.ReflectionMode.None)
		{
			writer.WriteString("reflection", spec.Reflection.ToString());
			writer.WriteNumber("reflectionOpacity", MathF.Round(spec.ReflectionOpacity, 3));
		}
		if (spec.AcousticReflectivity != 1.0f)
			writer.WriteNumber("acousticReflectivity", MathF.Round(spec.AcousticReflectivity, 3));
		if (spec.EdgeTileCount != 4)
			writer.WriteNumber("edgeTiles", spec.EdgeTileCount);

		writer.WriteStartObject("palette");
		foreach (var (key, color) in spec.Palette.OrderBy(it => it.Key))
			writer.WriteString(key.ToString(), JsonUtil.ColorToHex(color));
		writer.WriteEndObject();

		if (spec.Heights.Count > 0)
		{
			writer.WriteStartObject("heights");
			foreach (var (key, value) in spec.Heights.OrderBy(it => it.Key))
				writer.WriteNumber(key.ToString(), MathF.Round(value, 3));
			writer.WriteEndObject();
		}

		writer.WriteStartArray("fills");
		foreach (var fill in spec.Fills)
		{
			writer.WriteStartArray();
			foreach (var row in fill)
				writer.WriteStringValue(row);
			writer.WriteEndArray();
		}
		writer.WriteEndArray();

		WriteWeights(writer, "edge", spec.Edge);
		WriteWeights(writer, "edgeTop", spec.EdgeTop);
		WriteWeights(writer, "edgeBottom", spec.EdgeBottom);
		writer.WriteEndObject();
	}

	static void WriteWeights(Utf8JsonWriter writer, string key, List<(Color Color, float Weight)> weights)
	{
		if (weights.Count == 0)
			return;
		writer.WriteStartArray(key);
		foreach (var (color, weight) in weights)
		{
			writer.WriteStartArray();
			writer.WriteStringValue(JsonUtil.ColorToHex(color));
			writer.WriteNumberValue(MathF.Round(weight, 2));
			writer.WriteEndArray();
		}
		writer.WriteEndArray();
	}

	// -- rasterization -----------------------------------------------------------------

	public static List<Image> RasterizeFills(TileKitSpec spec)
		=> spec.Fills.Select(tile => RasterizeTile(tile, key => spec.Palette[key])).ToList();

	public static List<Image> RasterizeHeights(TileKitSpec spec)
		=> spec.Heights.Count == 0 ? [] : spec.Fills.Select(tile => RasterizeTile(tile, key =>
		{
			var value = (byte)Math.Round(spec.Heights[key] * 255);
			return new Color(value, value, value, (byte)255);
		})).ToList();

	static Image RasterizeTile(string[] rows, Func<char, Color> colorFor)
	{
		var image = new Image(Game.TileSize, Game.TileSize);
		for (var y = 0; y < Game.TileSize; y++)
			for (var x = 0; x < Game.TileSize; x++)
				image[x, y] = colorFor(rows[y][x]);
		return image;
	}

	// Weighted noise tiles, deterministic from the spec seed. The rng state is threaded
	// through edge -> edgeTop -> edgeBottom in order, mirroring the retired lua builder.
	public static (List<Image> Edge, List<Image> Top, List<Image> Bottom) RasterizeEdges(TileKitSpec spec)
	{
		var state = spec.Seed;
		return (NoiseTiles(spec.Edge, spec.EdgeTileCount, ref state),
			NoiseTiles(spec.EdgeTop, spec.EdgeTileCount, ref state),
			NoiseTiles(spec.EdgeBottom, spec.EdgeTileCount, ref state));
	}

	static List<Image> NoiseTiles(List<(Color Color, float Weight)> weights, int count, ref long state)
	{
		if (weights.Count == 0)
			return [];
		var total = weights.Sum(it => it.Weight);
		var tiles = new List<Image>(count);
		for (var i = 0; i < count; i++)
		{
			var image = new Image(Game.TileSize, Game.TileSize);
			for (var y = 0; y < Game.TileSize; y++)
				for (var x = 0; x < Game.TileSize; x++)
				{
					var roll = NextFloat(ref state) * total;
					var color = weights[^1].Color;
					foreach (var entry in weights)
					{
						roll -= entry.Weight;
						if (roll < 0)
						{
							color = entry.Color;
							break;
						}
					}
					image[x, y] = color;
				}
			tiles.Add(image);
		}
		return tiles;
	}

	static float NextFloat(ref long state)
	{
		state = (state * 1103515245 + 12345) % 2147483648;
		return state / 2147483648f;
	}
}
