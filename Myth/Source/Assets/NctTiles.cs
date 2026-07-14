using System.Text.Json;
using System.Text.Json.Nodes;

namespace Myth;

/// A neural-cellular-texture tile type, authored as pure data (Assets/Sources/Tilesets/&lt;name&gt;.nct.json).
/// Painted like any tile; at room load each connected island of the tile spawns one region
/// simulation that grows the interior model into the shape and blends a second "edge" model in a
/// distance-feathered band along the boundary (see Game.SpawnNctRegions). Every artistic choice is
/// a field here so a new look is a JSON edit, not a code change.
public sealed class NctTileSpec
{
	public required string Name;
	// Preferred room-legend char; Factory assigns a free one if it's taken.
	public char PreferredChar;
	// Interior fill model (slot 0), a file under NeuralTextureActor.WeightsFolder.
	public required string Interior;
	// Optional boundary model (slot 1). Empty means the interior simply feathers out to nothing.
	public string Edge = "";
	// Feather band width, in 128x128 state cells, over which edge blends into interior (and, with
	// no edge model, over which the interior fades to dead). Scales with island size.
	public int EdgeBandCells = 12;
	// Simulation steps run once at spawn before freezing. 0 keeps the region live (stepping every
	// frame) — reserve that for the few regions that need to react; frozen is far cheaper.
	public int FreezeSteps = 128;
	// Only used while live (FreezeSteps == 0).
	public float StepsPerSecond = 30.0f;
	public float FireRate = 0.5f;
	public Factory.TileLayer Layer = Factory.TileLayer.Background;
	// Blocks movement and casts shadow like a normal foreground tile. Independent of Layer: a
	// background NCT can be non-solid decoration, a foreground one a solid mass you burrow through.
	public bool Solid = false;
	// Collision surface (footsteps, physics) when Solid.
	public string Surface = Factory.DefaultSurfaceType;

	public bool Frozen => FreezeSteps > 0;
}

public static class NctTiles
{
	public const string Extension = ".nct.json";

	public static readonly List<NctTileSpec> Specs = [];

	public static void LoadSpecs(string tilesetsPath)
	{
		Specs.Clear();
		if (!Directory.Exists(tilesetsPath))
			return;
		foreach (var file in Directory.EnumerateFiles(tilesetsPath, $"*{Extension}").Order(StringComparer.OrdinalIgnoreCase))
			Specs.Add(Parse(file));
	}

	static NctTileSpec Parse(string path)
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
			throw new InvalidOperationException($"Neural texture tile '{fileName}': {e.Message}", e);
		}

		var interior = JsonUtil.ReadString(json, "interior");
		if (string.IsNullOrWhiteSpace(interior))
			throw new InvalidOperationException($"Neural texture tile '{name}': missing 'interior' model.");

		var spec = new NctTileSpec { Name = name, Interior = interior };
		var preferred = JsonUtil.ReadString(json, "char");
		spec.PreferredChar = string.IsNullOrEmpty(preferred) ? '\0' : preferred[0];
		spec.Edge = JsonUtil.ReadString(json, "edge", spec.Edge);
		spec.EdgeBandCells = Math.Max(1, JsonUtil.ReadInt(json, "edgeBandCells", spec.EdgeBandCells));
		spec.FreezeSteps = Math.Max(0, JsonUtil.ReadInt(json, "freezeSteps", spec.FreezeSteps));
		spec.StepsPerSecond = JsonUtil.ReadFloat(json, "stepsPerSecond", spec.StepsPerSecond);
		spec.FireRate = JsonUtil.ReadFloat(json, "fireRate", spec.FireRate);
		spec.Solid = JsonUtil.ReadBool(json, "solid", spec.Solid);
		spec.Surface = JsonUtil.ReadString(json, "surface", spec.Surface);
		if (JsonUtil.ReadString(json, "layer") is { Length: > 0 } layer)
			spec.Layer = Enum.Parse<Factory.TileLayer>(layer, ignoreCase: true);
		return spec;
	}

	public static NctTileSpec Clone(NctTileSpec spec) => new()
	{
		Name = spec.Name,
		PreferredChar = spec.PreferredChar,
		Interior = spec.Interior,
		Edge = spec.Edge,
		EdgeBandCells = spec.EdgeBandCells,
		FreezeSteps = spec.FreezeSteps,
		StepsPerSecond = spec.StepsPerSecond,
		FireRate = spec.FireRate,
		Layer = spec.Layer,
		Solid = spec.Solid,
		Surface = spec.Surface,
	};

	public static NctTileSpec CreateDefault(string name) => new()
	{
		Name = name,
		Interior = $"{NeuralTextureActor.DefaultWeightsFile}",
	};

	static string SpecPath(string name) => Path.Join(Assets.SourcesPath, "Tilesets", $"{name}{Extension}");

	public static void Save(NctTileSpec spec)
	{
		using var stream = File.Create(SpecPath(spec.Name));
		using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, NewLine = "\n" });
		writer.WriteStartObject();
		if (spec.PreferredChar != '\0')
			writer.WriteString("char", spec.PreferredChar.ToString());
		writer.WriteString("layer", spec.Layer.ToString());
		writer.WriteBoolean("solid", spec.Solid);
		if (spec.Solid)
			writer.WriteString("surface", spec.Surface);
		writer.WriteString("interior", spec.Interior);
		writer.WriteString("edge", spec.Edge);
		writer.WriteNumber("edgeBandCells", spec.EdgeBandCells);
		writer.WriteNumber("freezeSteps", spec.FreezeSteps);
		if (!spec.Frozen)
			writer.WriteNumber("stepsPerSecond", spec.StepsPerSecond);
		writer.WriteNumber("fireRate", MathF.Round(spec.FireRate, 3));
		writer.WriteEndObject();
	}
}
