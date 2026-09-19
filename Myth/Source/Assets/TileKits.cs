using System.Text.Json;
using System.Text.Json.Nodes;
using Foster.Framework;

namespace Myth;

/// Generative dual-tileset kits authored as JSON (Assets/Tilesets/&lt;name&gt;.kit.json)
/// instead of .ase files. A spec carries everything the .ase kits encoded in pixels —
/// palette, fill patterns, edge-noise weights, heights, params — plus the Factory
/// registration info (substance, chars, reflection) so a new tile type is pure data.
public sealed class TileKitSpec
{
	public required string Name;
	public string MaskKit = "stone-kit";
	public long Seed = 1;
	public string EdgeWidth = "2";
	public float Wobble = 0.6f;
	public string Substance = "Brick";
	public Factory.TileLayer Layer = Factory.TileLayer.Foreground;
	public char PreferredChar;
	public char SlopePreferredChar;
	public Factory.ReflectionMode Reflection = Factory.ReflectionMode.None;
	public float ReflectionOpacity = 0.5f;
	public float AcousticReflectivity = 1.0f;
	public int EdgeTileCount = 4;
	// How many game tiles one fill pattern spans, per side. 1 is an ordinary kit; 2 means fills come
	// in 2x2 groups that reassemble into a 16px pattern across four tiles, 3 into a 24px one. Lets
	// authored art bigger than a tile keep its native resolution instead of being downsampled.
	public int FillTiles = 1;
	// How many trailing fill groups are authored exceptions: the positional hash never picks them,
	// only a room's variant pin (V in the room editor) does.
	public int PinnedGroups;
	public readonly Dictionary<char, Color> Palette = [];
	public readonly Dictionary<char, float> Heights = [];
	public readonly List<string[]> Fills = [];
	// Optional per-pixel relief, one '0'-'9' grid per fill tile. When present it replaces the
	// per-color Heights (see RasterizeHeights) so a tile can bevel along its shapes -- per-color
	// heights can only ever step between colors.
	public readonly List<string[]> HeightMaps = [];
	public readonly List<(Color Color, float Weight)> Edge = [];
	public readonly List<(Color Color, float Weight)> EdgeTop = [];
	public readonly List<(Color Color, float Weight)> EdgeBottom = [];

	public string DualName => $"{Name}-dual";
	public string SlopeEntryName => $"{char.ToUpperInvariant(Name[0])}{Name[1..]} Slope";
	public bool HasEdges => Edge.Count > 0;
	/// Fills per group: one per tile of the macro block.
	public int FillStride => FillTiles * FillTiles;
	public int FillGroups => Fills.Count / FillStride;
}

/// The Factory-facing subset of a kit spec: everything registration needs and nothing the
/// image bake needs. In dev it derives from the JSON specs; in pack mode it is read from
/// the visual asset pack manifest, so a pack is self-describing and can never skew against
/// separately-shipped spec files.
public sealed record TileKitRegistration(
	string Name,
	char PreferredChar,
	char SlopePreferredChar,
	string Substance,
	Factory.TileLayer Layer,
	Factory.ReflectionMode Reflection,
	float ReflectionOpacity,
	float AcousticReflectivity,
	bool HasSlope)
{
	public string DualName => $"{Name}-dual";
	public string SlopeEntryName => $"{char.ToUpperInvariant(Name[0])}{Name[1..]} Slope";

	public static TileKitRegistration From(TileKitSpec spec)
		=> new(spec.Name, spec.PreferredChar, spec.SlopePreferredChar, spec.Substance,
			spec.Layer, spec.Reflection, spec.ReflectionOpacity, spec.AcousticReflectivity,
			spec.HasEdges);
}

public static class TileKits
{
	public const string Extension = ".kit.json";

	/// A 4x4 fill group is 32px of authored art across -- past that the palette and the fill editor
	/// stop being workable, and the atlas cost grows with the square.
	public const int MaxFillTiles = 4;

	public static readonly List<TileKitSpec> Specs = [];

	// What Factory.RegisterTileKits consumes. Follows Specs when they load from disk; the
	// visual-asset-pack path replaces it with the manifest's copy (see SetRegistrations).
	public static readonly List<TileKitRegistration> Registrations = [];

	public static void LoadSpecs(string tilesetsPath)
	{
		Specs.Clear();
		Registrations.Clear();
		if (!Directory.Exists(tilesetsPath))
			return;
		foreach (var file in Directory.EnumerateFiles(tilesetsPath, $"*{Extension}").Order(StringComparer.OrdinalIgnoreCase))
			Specs.Add(Parse(file));
		foreach (var spec in Specs)
			Registrations.Add(TileKitRegistration.From(spec));
	}

	/// Pack mode: registration data comes from the pack manifest (baked together with the
	/// images), overriding anything LoadSpecs may have found on disk.
	public static void SetRegistrations(IEnumerable<TileKitRegistration> registrations)
	{
		Registrations.Clear();
		Registrations.AddRange(registrations);
	}

	static TileKitSpec Parse(string path)
	{
		var fileName = Path.GetFileName(path);
		var name = fileName[..^Extension.Length];
		JsonObject json;
		try
		{
			json = JsonUtil.Parse(File.ReadAllText(path))?.AsObject() ?? throw new InvalidOperationException("root is not an object");
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
		spec.Substance = JsonUtil.ReadString(json, "substance", spec.Substance);
		if (JsonUtil.ReadString(json, "layer") is { } layer)
			spec.Layer = Enum.Parse<Factory.TileLayer>(layer, ignoreCase: true);
		spec.PreferredChar = ReadChar(json, "char");
		spec.SlopePreferredChar = ReadChar(json, "slopeChar");
		spec.ReflectionOpacity = JsonUtil.ReadFloat(json, "reflectionOpacity", spec.ReflectionOpacity);
		spec.AcousticReflectivity = JsonUtil.ReadFloat(json, "acousticReflectivity", spec.AcousticReflectivity);
		spec.EdgeTileCount = Math.Max(1, JsonUtil.ReadInt(json, "edgeTiles", spec.EdgeTileCount));
		spec.FillTiles = Math.Clamp(JsonUtil.ReadInt(json, "fillTiles", spec.FillTiles), 1, MaxFillTiles);
		spec.PinnedGroups = Math.Max(0, JsonUtil.ReadInt(json, "pinnedGroups", spec.PinnedGroups));
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
				var tile = ReadTileRows(fillNode, name, "fill");
				foreach (var key in tile.SelectMany(row => row))
					if (!spec.Palette.ContainsKey(key))
						throw new InvalidOperationException($"Tile kit '{name}': fill uses '{key}' which is not in the palette.");
				spec.Fills.Add(tile);
			}

		if (spec.Fills.Count == 0)
			throw new InvalidOperationException($"Tile kit '{name}': at least one fill tile is required.");
		if (spec.Fills.Count % spec.FillStride != 0)
			throw new InvalidOperationException($"Tile kit '{name}': fillTiles {spec.FillTiles} needs fills in groups of {spec.FillStride} (found {spec.Fills.Count}).");
		if (spec.PinnedGroups >= spec.FillGroups)
			throw new InvalidOperationException($"Tile kit '{name}': pinnedGroups {spec.PinnedGroups} leaves no fill group for the random pool (found {spec.FillGroups}).");

		if (json.TryGetPropertyValue("heightMaps", out var mapsNode) && mapsNode is JsonArray maps)
			foreach (var mapNode in maps)
			{
				var tile = ReadTileRows(mapNode, name, "heightMap");
				if (tile.Any(row => !row.All(char.IsAsciiDigit)))
					throw new InvalidOperationException($"Tile kit '{name}': heightMap tiles must be digits '0'-'9'.");
				spec.HeightMaps.Add(tile);
			}
		if (spec.HeightMaps.Count > 0 && spec.HeightMaps.Count != spec.Fills.Count)
			throw new InvalidOperationException($"Tile kit '{name}': heightMaps needs one tile per fill ({spec.Fills.Count}).");

		if (spec.Heights.Count > 0)
			foreach (var key in spec.Palette.Keys)
				if (!spec.Heights.ContainsKey(key))
					throw new InvalidOperationException($"Tile kit '{name}': heights must cover every palette key (missing '{key}').");

		ReadWeights(json, "edge", spec.Edge, name);
		ReadWeights(json, "edgeTop", spec.EdgeTop, name);
		ReadWeights(json, "edgeBottom", spec.EdgeBottom, name);
		return spec;
	}

	static string[] ReadTileRows(JsonNode? node, string kitName, string section)
	{
		if (node is not JsonArray rows)
			throw new InvalidOperationException($"Tile kit '{kitName}': each {section} must be an array of {Game.TileSize} row strings.");
		var tile = rows.Select(row => row?.GetValue<string>() ?? "").ToArray();
		if (tile.Length != Game.TileSize || tile.Any(row => row.Length != Game.TileSize))
			throw new InvalidOperationException($"Tile kit '{kitName}': {section} tiles must be {Game.TileSize} rows of {Game.TileSize} characters.");
		return tile;
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
		=> Path.Join(Assets.SourcesPath, "Tilesets", name + Extension);

	public static TileKitSpec Clone(TileKitSpec spec)
	{
		var clone = new TileKitSpec
		{
			Name = spec.Name,
			MaskKit = spec.MaskKit,
			Seed = spec.Seed,
			EdgeWidth = spec.EdgeWidth,
			Wobble = spec.Wobble,
			Substance = spec.Substance,
			Layer = spec.Layer,
			PreferredChar = spec.PreferredChar,
			SlopePreferredChar = spec.SlopePreferredChar,
			Reflection = spec.Reflection,
			ReflectionOpacity = spec.ReflectionOpacity,
			AcousticReflectivity = spec.AcousticReflectivity,
			EdgeTileCount = spec.EdgeTileCount,
			FillTiles = spec.FillTiles,
			PinnedGroups = spec.PinnedGroups,
		};
		foreach (var (key, value) in spec.Palette)
			clone.Palette[key] = value;
		foreach (var (key, value) in spec.Heights)
			clone.Heights[key] = value;
		foreach (var fill in spec.Fills)
			clone.Fills.Add((string[])fill.Clone());
		foreach (var map in spec.HeightMaps)
			clone.HeightMaps.Add((string[])map.Clone());
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
		writer.WriteString("substance", spec.Substance);
		if (spec.Layer != Factory.TileLayer.Foreground)
			writer.WriteString("layer", spec.Layer.ToString());
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
		if (spec.FillTiles != 1)
			writer.WriteNumber("fillTiles", spec.FillTiles);
		if (spec.PinnedGroups > 0)
			writer.WriteNumber("pinnedGroups", spec.PinnedGroups);

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

		WriteTiles(writer, "fills", spec.Fills);
		if (spec.HeightMaps.Count > 0)
			WriteTiles(writer, "heightMaps", spec.HeightMaps);

		WriteWeights(writer, "edge", spec.Edge);
		WriteWeights(writer, "edgeTop", spec.EdgeTop);
		WriteWeights(writer, "edgeBottom", spec.EdgeBottom);
		writer.WriteEndObject();
	}

	static void WriteTiles(Utf8JsonWriter writer, string key, List<string[]> tiles)
	{
		writer.WriteStartArray(key);
		foreach (var tile in tiles)
		{
			writer.WriteStartArray();
			foreach (var row in tile)
				writer.WriteStringValue(row);
			writer.WriteEndArray();
		}
		writer.WriteEndArray();
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
		=> spec.HeightMaps.Count > 0
			? spec.HeightMaps.Select(map => RasterizeTile(map, key => Grey((key - '0') / 9f))).ToList()
			: spec.Heights.Count == 0 ? []
			: spec.Fills.Select(tile => RasterizeTile(tile, key => Grey(spec.Heights[key]))).ToList();

	static Color Grey(float value)
	{
		var level = (byte)Math.Round(Math.Clamp(value, 0f, 1f) * 255);
		return new Color(level, level, level, (byte)255);
	}

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

	// -- height derivation -------------------------------------------------------------

	/// How the Tile Creator reads heights (which become the normal map's bevel) out of a fill
	/// pattern. Every mode is a one-shot authoring action that writes plain numbers into the spec,
	/// so a kit nobody re-derives keeps exactly the heights it was authored with.
	public enum HeightMode
	{
		Flat,
		Luminance,
		Contrast,
		Seams,
		TopLit,
		Relief,
	}

	public const float DefaultHeight = 0.67f;

	public static void DeriveHeights(TileKitSpec spec, HeightMode mode, float strength)
	{
		if (mode == HeightMode.Relief)
		{
			DeriveRelief(spec, strength);
			return;
		}

		spec.HeightMaps.Clear();
		WriteHeights(spec, mode switch
		{
			HeightMode.Luminance => ScorePixels(spec, (fill, x, y) => Luminance(spec, fill, x, y)),
			// Local high pass. Bright-for-its-neighbourhood reads as raised, which stops a dark
			// material reading as uniformly sunken the way raw luminance makes it.
			HeightMode.Contrast => ScorePixels(spec, (fill, x, y) => Luminance(spec, fill, x, y) - Blurred(spec, fill, x, y)),
			// Thin vs fat: mortar, cracks and plank gaps are 1px lines while the body is a blob.
			// Reads shape instead of color, so it gets bright cracks right where luminance inverts.
			HeightMode.Seams => ScorePixels(spec, (fill, x, y) => Neighbours(fill, x, y)),
			// The art is already lit from above, so read the light back out: descending through a
			// groove gets brighter, descending over a bump gets darker.
			HeightMode.TopLit => ScorePixels(spec, (fill, x, y) => Luminance(spec, fill, x, y - 1) - Luminance(spec, fill, x, y + 1)),
			_ => [],
		}, strength);
	}

	/// Per-pixel relief: how deep inside its own color region each pixel sits, so mortar lines stay
	/// low while the pads between them bulge. The one mode that can bevel a shape rather than just
	/// step between colors -- it drops the per-color heights so there is a single source of truth.
	static void DeriveRelief(TileKitSpec spec, float strength)
	{
		spec.Heights.Clear();
		spec.HeightMaps.Clear();
		// Depth maps to levels on a fixed scale rather than normalizing to the tile's own deepest
		// pixel: a noise fill whose single interior pixel is one step in should read as a faint
		// bump, not as a full-height spike.
		const int LevelsPerStep = 3;
		foreach (var fill in spec.Fills)
		{
			var depth = FloodDepth(fill);
			var rows = new string[Game.TileSize];
			for (var y = 0; y < Game.TileSize; y++)
			{
				var row = new char[Game.TileSize];
				for (var x = 0; x < Game.TileSize; x++)
					row[x] = (char)('0' + Math.Clamp((int)MathF.Round(LevelsPerStep * strength * depth[y * Game.TileSize + x]), 0, 9));
				rows[y] = new string(row);
			}
			spec.HeightMaps.Add(rows);
		}
	}

	static readonly Point2[] cardinals = [new(1, 0), new(-1, 0), new(0, 1), new(0, -1)];

	// Wrapped multi-source BFS: 0 on every pixel touching a different color, growing inward. A fill
	// that is pure noise has no interior at all and comes back flat, which is the honest answer.
	static int[] FloodDepth(string[] fill)
	{
		var depth = new int[Game.TileSize * Game.TileSize];
		Array.Fill(depth, -1);
		var queue = new Queue<Point2>();
		for (var y = 0; y < Game.TileSize; y++)
			for (var x = 0; x < Game.TileSize; x++)
				if (Neighbours(fill, x, y) < 1f)
				{
					depth[y * Game.TileSize + x] = 0;
					queue.Enqueue(new Point2(x, y));
				}

		while (queue.Count > 0)
		{
			var at = queue.Dequeue();
			foreach (var step in cardinals)
			{
				var next = new Point2(Wrap(at.X + step.X), Wrap(at.Y + step.Y));
				var index = next.Y * Game.TileSize + next.X;
				if (depth[index] >= 0)
					continue;
				depth[index] = depth[at.Y * Game.TileSize + at.X] + 1;
				queue.Enqueue(next);
			}
		}

		for (var i = 0; i < depth.Length; i++)
			depth[i] = Math.Max(0, depth[i]);
		return depth;
	}

	/// Seeds the rim noise from the fill itself: the three colors covering most of the tile, weighted
	/// by coverage, with the top rim leaning light and the bottom rim leaning dark -- so an imported
	/// material's edges belong to it instead of wearing the default grey.
	public static void DeriveEdges(TileKitSpec spec)
	{
		var coverage = new Dictionary<char, int>();
		foreach (var key in spec.Fills.SelectMany(fill => fill).SelectMany(row => row))
			coverage[key] = coverage.GetValueOrDefault(key) + 1;

		var top = coverage.OrderByDescending(it => it.Value).ThenBy(it => it.Key)
			.Take(3).Select(it => (Color: spec.Palette[it.Key], Weight: (float)it.Value)).ToList();
		if (top.Count == 0)
			return;

		spec.Edge.Clear();
		spec.EdgeTop.Clear();
		spec.EdgeBottom.Clear();
		var total = top.Sum(it => it.Weight);
		foreach (var (color, weight) in top)
			spec.Edge.Add((color, MathF.Max(1, MathF.Round(weight / total * 100))));

		var byLight = top.Select(it => it.Color).OrderByDescending(Luminance).ToList();
		var ramp = (float[])[55, 30, 15];
		for (var i = 0; i < byLight.Count; i++)
		{
			spec.EdgeTop.Add((byLight[i], ramp[i]));
			spec.EdgeBottom.Add((byLight[^(i + 1)], ramp[i]));
		}
		spec.EdgeBottom.Add((Color.Black, 25));
	}

	static Dictionary<char, float> ScorePixels(TileKitSpec spec, Func<string[], int, int, float> score)
	{
		var totals = new Dictionary<char, (float Sum, int Count)>();
		foreach (var fill in spec.Fills)
			for (var y = 0; y < Game.TileSize; y++)
				for (var x = 0; x < Game.TileSize; x++)
				{
					var (sum, count) = totals.GetValueOrDefault(fill[y][x]);
					totals[fill[y][x]] = (sum + score(fill, x, y), count + 1);
				}
		return totals.ToDictionary(it => it.Key, it => it.Value.Sum / it.Value.Count);
	}

	// Scores are only ever compared against each other, so they normalize to the full 0-1 range and
	// `strength` then pulls them back toward the middle. A palette whose keys all score the same
	// (a flat derive, a one-color fill) sits at the neutral default instead of dividing by zero.
	static void WriteHeights(TileKitSpec spec, Dictionary<char, float> scores, float strength)
	{
		spec.Heights.Clear();
		var range = scores.Count > 0 ? scores.Values.Max() - scores.Values.Min() : 0f;
		var lowest = scores.Count > 0 ? scores.Values.Min() : 0f;
		foreach (var key in spec.Palette.Keys)
			spec.Heights[key] = range > 0.0001f && scores.TryGetValue(key, out var score)
				? Math.Clamp(0.5f + ((score - lowest) / range - 0.5f) * strength, 0f, 1f)
				: DefaultHeight;
	}

	static float Luminance(Color color)
		=> (color.R * 0.299f + color.G * 0.587f + color.B * 0.114f) / 255f;

	static float Luminance(TileKitSpec spec, string[] fill, int x, int y)
		=> Luminance(spec.Palette[fill[Wrap(y)][Wrap(x)]]);

	static float Blurred(TileKitSpec spec, string[] fill, int x, int y)
	{
		var sum = 0f;
		for (var dy = -1; dy <= 1; dy++)
			for (var dx = -1; dx <= 1; dx++)
				sum += Luminance(spec, fill, x + dx, y + dy);
		return sum / 9f;
	}

	// Fraction of a pixel's 4 neighbours sharing its color: 1 deep inside a blob, 0.5 on a 1px line.
	static float Neighbours(string[] fill, int x, int y)
	{
		var key = fill[y][x];
		var count = 0;
		foreach (var step in cardinals)
			if (fill[Wrap(y + step.Y)][Wrap(x + step.X)] == key)
				count++;
		return count / 4f;
	}

	static int Wrap(int value) => (value + Game.TileSize) % Game.TileSize;

	// -- sheet import ------------------------------------------------------------------

	public const string PaletteKeys = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";

	/// Pulls one cell of an authored pixel-art sheet (Assets/Sources/Tilesets/*.png) in as fills.
	/// With nativeSize the cell keeps its resolution and becomes an NxN group that reassembles across
	/// that many game tiles; otherwise it is block-downsampled by majority vote into a single tile --
	/// which keeps the pixels crisp where averaging would mush them. Either way the result is
	/// quantized to a few colors, so what lands in the spec is a hand-editable kit, not a photo.
	public static void ImportSheetCell(TileKitSpec spec, Image sheet, Point2 cell, int cellSize, int colors, bool addVariant, bool nativeSize)
	{
		// A variant has to match the group shape the kit already has; only a replace can change it.
		var fillTiles = addVariant ? spec.FillTiles
			: nativeSize ? Math.Clamp(cellSize / Game.TileSize, 1, MaxFillTiles) : 1;
		var stride = fillTiles * fillTiles;
		// Native import reads one game tile per sub-tile (block 1, nothing lost); downsampling folds
		// the whole cell into a single tile.
		var block = Math.Max(1, cellSize / Game.TileSize / fillTiles);
		var origin = cell * cellSize;
		var area = Game.TileSize * Game.TileSize;

		// Sample the whole group before quantizing, so its sub-tiles share one palette and the
		// pattern stays coherent across the seams where they meet.
		var pixels = new Color[stride * area];
		for (var sub = 0; sub < stride; sub++)
		{
			var subOrigin = origin + new Point2(sub % fillTiles, sub / fillTiles) * (Game.TileSize * block);
			for (var y = 0; y < Game.TileSize; y++)
				for (var x = 0; x < Game.TileSize; x++)
					pixels[sub * area + y * Game.TileSize + x] = MajorityColor(sheet, subOrigin + new Point2(x, y) * block, block);
		}
		Quantize(pixels, colors);

		// A replace starts the kit over from the cell; a variant keeps the palette and snaps into it,
		// which is how several cells of one material end up sharing a single tile type (and one char).
		if (!addVariant)
		{
			spec.Palette.Clear();
			spec.Fills.Clear();
			spec.Heights.Clear();
			spec.HeightMaps.Clear();
			spec.FillTiles = fillTiles;
			spec.PinnedGroups = 0;
		}

		// Resolve each distinct color to a key once, against the palette as it stood before this
		// import -- see ResolveKey.
		var mergeInto = spec.Palette.Select(it => (it.Key, it.Value)).ToArray();
		var keys = new Dictionary<Color, char>();
		foreach (var color in pixels)
			if (!keys.ContainsKey(color))
				keys[color] = ResolveKey(spec, mergeInto, color);

		// New variants join the random pool, ahead of the pinned-only groups.
		var insertAt = (spec.FillGroups - spec.PinnedGroups) * stride;
		for (var sub = 0; sub < stride; sub++)
		{
			var rows = new string[Game.TileSize];
			for (var y = 0; y < Game.TileSize; y++)
			{
				var row = new char[Game.TileSize];
				for (var x = 0; x < Game.TileSize; x++)
					row[x] = keys[pixels[sub * area + y * Game.TileSize + x]];
				rows[y] = new string(row);
			}
			spec.Fills.Insert(insertAt + sub, rows);
		}

		// Heights must cover every palette key, and a relief map must cover every fill.
		if (spec.Heights.Count > 0)
			foreach (var key in spec.Palette.Keys.ToArray())
				spec.Heights.TryAdd(key, DefaultHeight);
		if (spec.HeightMaps.Count > 0)
			DeriveRelief(spec, 1f);
	}

	// Most common color in the block, ties to the first seen: a downsampled cell keeps the source's
	// own colors instead of inventing averaged ones. Alpha is dropped -- fills are always opaque.
	static Color MajorityColor(Image sheet, Point2 origin, int block)
	{
		var counts = new Dictionary<Color, int>();
		var best = Color.Black;
		var bestCount = 0;
		for (var y = 0; y < block; y++)
			for (var x = 0; x < block; x++)
			{
				var at = origin + new Point2(x, y);
				if (at.X < 0 || at.Y < 0 || at.X >= sheet.Width || at.Y >= sheet.Height)
					continue;
				var source = sheet[at.X, at.Y];
				var color = new Color(source.R, source.G, source.B, (byte)255);
				var count = counts[color] = counts.GetValueOrDefault(color) + 1;
				if (count > bestCount)
					(best, bestCount) = (color, count);
			}
		return best;
	}

	// Frequency-greedy: keep the colors covering the most pixels, snap the rest to the nearest
	// keeper. These sheets are pixel art (a median of six colors a cell), so it is usually a no-op,
	// and it can never invent a color the artist didn't use.
	static void Quantize(Color[] pixels, int colors)
	{
		var counts = new Dictionary<Color, int>();
		foreach (var pixel in pixels)
			counts[pixel] = counts.GetValueOrDefault(pixel) + 1;
		if (counts.Count <= colors)
			return;

		var keep = counts.OrderByDescending(it => it.Value).ThenBy(it => it.Key.RGBA).Take(colors).Select(it => it.Key).ToArray();
		for (var i = 0; i < pixels.Length; i++)
		{
			var nearest = keep[0];
			foreach (var option in keep)
				if (Distance(option, pixels[i]) < Distance(nearest, pixels[i]))
					nearest = option;
			pixels[i] = nearest;
		}
	}

	// Reuses a palette entry when the color is close enough -- variants of one material share most
	// of their colors -- otherwise claims the next free key. Only `mergeInto` (the palette as it
	// stood before this import) are merge candidates: snapping to a key this same import just added
	// would undo the quantizer's decision and quietly hand back fewer colors than were asked for.
	static char ResolveKey(TileKitSpec spec, (char Key, Color Color)[] mergeInto, Color color)
	{
		const int MergeDistance = 20;
		var nearest = NearestKey(mergeInto, color);
		if (nearest.Key != '\0' && nearest.Distance <= MergeDistance * MergeDistance)
			return nearest.Key;

		foreach (var free in PaletteKeys)
			if (!spec.Palette.ContainsKey(free))
			{
				spec.Palette[free] = color;
				return free;
			}

		// Out of keys: settle for the closest color the palette already has.
		return NearestKey([.. spec.Palette.Select(it => (it.Key, it.Value))], color).Key;
	}

	static (char Key, int Distance) NearestKey((char Key, Color Color)[] palette, Color color)
	{
		(char Key, int Distance) best = ('\0', int.MaxValue);
		foreach (var (key, existing) in palette)
			if (Distance(existing, color) < best.Distance)
				best = (key, Distance(existing, color));
		return best;
	}

	static int Distance(Color a, Color b)
	{
		var (dr, dg, db) = (a.R - b.R, a.G - b.G, a.B - b.B);
		return dr * dr + dg * dg + db * db;
	}
}
