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
	public float Wobble = 0.6f;
	public string Substance = "Brick";
	public Factory.TileLayer Layer = Factory.TileLayer.Foreground;
	public char PreferredChar;
	public char SlopePreferredChar;
	public Factory.ReflectionMode Reflection = Factory.ReflectionMode.None;
	public float ReflectionOpacity = 0.5f;
	public float AcousticReflectivity = 1.0f;
	// How many game tiles one fill pattern spans, per side. 1 is an ordinary kit; 2 means fills come
	// in 2x2 groups that reassemble into a 16px pattern across four tiles, 3 into a 24px one. Lets
	// authored art bigger than a tile keep its native resolution instead of being downsampled.
	public int FillTiles = 1;
	/// The .ase under Tilesets/Kits holding this kit's art. The kit's own name is the slice in it, and
	/// "<name>-rim" / "-rimtop" / "-rimbottom" are its rim strips.
	public string? Source;
	// How the relief is read into normals. Sobel is the long-standing bake; Facet is the pixel-art one
	// (see Assets.NormalKernel), and strength scales the tilt for this kit alone.
	public Assets.NormalKernel NormalKernel = Assets.NormalKernel.Sobel;
	public float NormalStrength = 1f;

	public Assets.NormalBake NormalBake => new(NormalKernel, NormalStrength);

	public string DualName => $"{Name}-dual";
	public string SlopeEntryName => $"{char.ToUpperInvariant(Name[0])}{Name[1..]} Slope";
	/// Fills per group: one per tile of the macro block.
	public int FillStride => FillTiles * FillTiles;
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
			HasSlope: true);
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
		if (!Directory.Exists(tilesetsPath))
		{
			Specs.Clear();
			Registrations.Clear();
			return;
		}

		// Parsed aside and swapped in only once every spec is good, so a live reload of a broken kit
		// leaves the running game on the set it already had instead of a half-loaded one.
		var parsed = Directory.EnumerateFiles(tilesetsPath, $"*{Extension}")
			.Order(StringComparer.OrdinalIgnoreCase)
			.Select(Parse)
			.ToList();
		Specs.Clear();
		Specs.AddRange(parsed);
		Registrations.Clear();
		Registrations.AddRange(parsed.Select(TileKitRegistration.From));
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
		spec.Wobble = Math.Clamp(JsonUtil.ReadFloat(json, "wobble", spec.Wobble), 0f, 1f);
		spec.Substance = JsonUtil.ReadString(json, "substance", spec.Substance);
		if (JsonUtil.ReadString(json, "layer") is { } layer)
			spec.Layer = Enum.Parse<Factory.TileLayer>(layer, ignoreCase: true);
		spec.PreferredChar = ReadChar(json, "char");
		spec.SlopePreferredChar = ReadChar(json, "slopeChar");
		spec.ReflectionOpacity = JsonUtil.ReadFloat(json, "reflectionOpacity", spec.ReflectionOpacity);
		spec.AcousticReflectivity = JsonUtil.ReadFloat(json, "acousticReflectivity", spec.AcousticReflectivity);
		spec.FillTiles = Math.Clamp(JsonUtil.ReadInt(json, "fillTiles", spec.FillTiles), 1, MaxFillTiles);
		spec.Source = JsonUtil.ReadString(json, "source");
		if (JsonUtil.ReadString(json, "reflection") is { } reflection)
			spec.Reflection = Enum.Parse<Factory.ReflectionMode>(reflection, ignoreCase: true);
		if (JsonUtil.ReadString(json, "normalKernel") is { } kernel)
			spec.NormalKernel = Enum.Parse<Assets.NormalKernel>(kernel, ignoreCase: true);
		spec.NormalStrength = Math.Clamp(JsonUtil.ReadFloat(json, "normalStrength", spec.NormalStrength), 0f, 4f);

		if (string.IsNullOrWhiteSpace(spec.Source))
			throw new InvalidOperationException($"Tile kit '{name}': \"source\" is required -- it names the .ase under Tilesets/{Assets.KitSourceFolder} holding this kit's art.");
		return spec;
	}

	static char ReadChar(JsonObject json, string key)
	{
		var value = JsonUtil.ReadString(json, key);
		return string.IsNullOrEmpty(value) ? '\0' : value[0];
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
			Wobble = spec.Wobble,
			Substance = spec.Substance,
			Layer = spec.Layer,
			PreferredChar = spec.PreferredChar,
			SlopePreferredChar = spec.SlopePreferredChar,
			Reflection = spec.Reflection,
			ReflectionOpacity = spec.ReflectionOpacity,
			AcousticReflectivity = spec.AcousticReflectivity,
			FillTiles = spec.FillTiles,
			Source = spec.Source,
			NormalKernel = spec.NormalKernel,
			NormalStrength = spec.NormalStrength,
		};
		return clone;
	}

	/// A starting spec for the editor's "new kit" flow. It points at a source file and names a slice
	/// that does not exist yet, so the bake says exactly what to draw. Chars are assigned at
	/// registration.
	public static TileKitSpec CreateDefault(string name, string source)
		=> new() { Name = name, Source = source };

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
		writer.WriteNumber("wobble", MathF.Round(spec.Wobble, 3));
		if (spec.Reflection != Factory.ReflectionMode.None)
		{
			writer.WriteString("reflection", spec.Reflection.ToString());
			writer.WriteNumber("reflectionOpacity", MathF.Round(spec.ReflectionOpacity, 3));
		}
		if (spec.AcousticReflectivity != 1.0f)
			writer.WriteNumber("acousticReflectivity", MathF.Round(spec.AcousticReflectivity, 3));
		if (spec.FillTiles != 1)
			writer.WriteNumber("fillTiles", spec.FillTiles);
		if (spec.Source is { } source)
			writer.WriteString("source", source);
		if (spec.NormalKernel != Assets.NormalKernel.Sobel)
			writer.WriteString("normalKernel", spec.NormalKernel.ToString());
		if (spec.NormalStrength != 1f)
			writer.WriteNumber("normalStrength", MathF.Round(spec.NormalStrength, 3));

		writer.WriteEndObject();
	}

}
