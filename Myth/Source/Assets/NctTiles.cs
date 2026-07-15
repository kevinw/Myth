using System.Text.Json;
using System.Text.Json.Nodes;
using System.Numerics;
using Foster.Framework;

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
	// Feather band width, in simulation cells, over which edge blends into interior (and, with
	// no edge model, over which the interior fades to dead). Its world width follows FeatureScale.
	public int EdgeBandCells = 12;
	// Analytic rounding of tile-union corners, in output pixels. Does not affect collision.
	public float RoundingPixels;
	// Signed visual offset from collision geometry, in output pixels. Positive grows outward.
	public float OutsetPixels;
	// Simulation steps run once at spawn before freezing. 0 keeps the region live (stepping every
	// frame) — reserve that for the few regions that need to react; frozen is far cheaper.
	public int FreezeSteps = 128;
	// Only used while live (FreezeSteps == 0).
	public float StepsPerSecond = 30.0f;
	public float FireRate = 0.5f;
	public float Contrast = 1f;
	public float Saturation = 1f;
	public float Brightness;
	public Color Tint = Color.White;
	public float NormalStrength = 1f;
	public float SilhouetteNormalStrength = 1f;
	public float BlendCurve = 1f;
	public float EdgeWeight = 1f;
	// World pixels represented by one simulation cell. This keeps the learned feature scale
	// independent of island size; larger values make features larger.
	public float FeatureScale = 1f;
	// Experimental decoder controls. These deliberately affect latent sampling only; the mask
	// remains fixed so even extreme values cannot desynchronize gameplay-facing silhouettes.
	public float DetailScale = 1f;
	public Vector2 DetailPhase;
	public float PatchFrequency = 1f;
	public float SirenFrequency = 10f;
	public float LatentGain = 1f;
	public float LatentBias;
	public bool NearestStateSampling;
	public Factory.TileLayer Layer = Factory.TileLayer.Background;
	// Blocks movement and casts shadow like a normal foreground tile. Independent of Layer: a
	// background NCT can be non-solid decoration, a foreground one a solid mass you burrow through.
	public bool Solid = false;
	// Collision surface (footsteps, physics) when Solid.
	public string Surface = Factory.DefaultSurfaceType;
	// Allows solid foreground cells of this tile to be painted as slopes.
	public bool HasSlope;

	public bool Frozen => FreezeSteps > 0;
}

public static class NctTiles
{
	public const string Extension = ".nct.json";

	public static readonly List<NctTileSpec> Specs = [];

	public static void SetSpecs(IEnumerable<NctTileSpec> specs)
	{
		Specs.Clear();
		foreach (var spec in specs)
			Specs.Add(Clone(spec));
	}

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
		spec.RoundingPixels = Math.Clamp(JsonUtil.ReadFloat(json, "roundingPixels", spec.RoundingPixels), 0f, Game.TileSize * 0.5f);
		spec.OutsetPixels = Math.Clamp(JsonUtil.ReadFloat(json, "outsetPixels", spec.OutsetPixels), -Game.TileSize, Game.TileSize);
		spec.FreezeSteps = Math.Max(0, JsonUtil.ReadInt(json, "freezeSteps", spec.FreezeSteps));
		spec.StepsPerSecond = JsonUtil.ReadFloat(json, "stepsPerSecond", spec.StepsPerSecond);
		spec.FireRate = JsonUtil.ReadFloat(json, "fireRate", spec.FireRate);
		spec.Contrast = Math.Clamp(JsonUtil.ReadFloat(json, "contrast", spec.Contrast), 0f, 4f);
		spec.Saturation = Math.Clamp(JsonUtil.ReadFloat(json, "saturation", spec.Saturation), 0f, 4f);
		spec.Brightness = Math.Clamp(JsonUtil.ReadFloat(json, "brightness", spec.Brightness), -1f, 1f);
		spec.Tint = JsonUtil.ReadColor(json, "tint") ?? spec.Tint;
		spec.NormalStrength = Math.Clamp(JsonUtil.ReadFloat(json, "normalStrength", spec.NormalStrength), 0f, 4f);
		spec.SilhouetteNormalStrength = Math.Clamp(JsonUtil.ReadFloat(json, "silhouetteNormalStrength", spec.SilhouetteNormalStrength), 0f, 4f);
		spec.BlendCurve = Math.Clamp(JsonUtil.ReadFloat(json, "blendCurve", spec.BlendCurve), 0.1f, 4f);
		spec.EdgeWeight = Math.Clamp(JsonUtil.ReadFloat(json, "edgeWeight", spec.EdgeWeight), 0f, 1f);
		spec.FeatureScale = Math.Clamp(JsonUtil.ReadFloat(json, "featureScale", spec.FeatureScale), 0.25f, 8f);
		spec.DetailScale = Math.Clamp(JsonUtil.ReadFloat(json, "detailScale", spec.DetailScale), 0.25f, 4f);
		spec.DetailPhase = Vector2.Clamp(JsonUtil.ReadVector2(json, "detailPhase") ?? spec.DetailPhase, new(-64), new(64));
		spec.PatchFrequency = Math.Clamp(JsonUtil.ReadFloat(json, "patchFrequency", spec.PatchFrequency), 0.1f, 4f);
		spec.SirenFrequency = Math.Clamp(JsonUtil.ReadFloat(json, "sirenFrequency", spec.SirenFrequency), 1f, 20f);
		spec.LatentGain = Math.Clamp(JsonUtil.ReadFloat(json, "latentGain", spec.LatentGain), 0f, 4f);
		spec.LatentBias = Math.Clamp(JsonUtil.ReadFloat(json, "latentBias", spec.LatentBias), -2f, 2f);
		spec.NearestStateSampling = JsonUtil.ReadBool(json, "nearestStateSampling", spec.NearestStateSampling);
		spec.Solid = JsonUtil.ReadBool(json, "solid", spec.Solid);
		spec.Surface = JsonUtil.ReadString(json, "surface", spec.Surface);
		if (JsonUtil.ReadString(json, "layer") is { Length: > 0 } layer)
			spec.Layer = Enum.Parse<Factory.TileLayer>(layer, ignoreCase: true);
		spec.HasSlope = JsonUtil.ReadBool(json, "slopes", spec.HasSlope) && spec.Solid && spec.Layer == Factory.TileLayer.Foreground;
		return spec;
	}

	public static NctTileSpec Clone(NctTileSpec spec) => new()
	{
		Name = spec.Name,
		PreferredChar = spec.PreferredChar,
		Interior = spec.Interior,
		Edge = spec.Edge,
		EdgeBandCells = spec.EdgeBandCells,
		RoundingPixels = spec.RoundingPixels,
		OutsetPixels = spec.OutsetPixels,
		FreezeSteps = spec.FreezeSteps,
		StepsPerSecond = spec.StepsPerSecond,
		FireRate = spec.FireRate,
		Contrast = spec.Contrast,
		Saturation = spec.Saturation,
		Brightness = spec.Brightness,
		Tint = spec.Tint,
		NormalStrength = spec.NormalStrength,
		SilhouetteNormalStrength = spec.SilhouetteNormalStrength,
		BlendCurve = spec.BlendCurve,
		EdgeWeight = spec.EdgeWeight,
		FeatureScale = spec.FeatureScale,
		DetailScale = spec.DetailScale,
		DetailPhase = spec.DetailPhase,
		PatchFrequency = spec.PatchFrequency,
		SirenFrequency = spec.SirenFrequency,
		LatentGain = spec.LatentGain,
		LatentBias = spec.LatentBias,
		NearestStateSampling = spec.NearestStateSampling,
		Layer = spec.Layer,
		Solid = spec.Solid,
		Surface = spec.Surface,
		HasSlope = spec.HasSlope,
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
		if (spec.HasSlope)
			writer.WriteBoolean("slopes", true);
		writer.WriteString("interior", spec.Interior);
		writer.WriteString("edge", spec.Edge);
		writer.WriteNumber("edgeBandCells", spec.EdgeBandCells);
		if (spec.RoundingPixels > 0)
			writer.WriteNumber("roundingPixels", MathF.Round(spec.RoundingPixels, 2));
		if (spec.OutsetPixels != 0)
			writer.WriteNumber("outsetPixels", MathF.Round(spec.OutsetPixels, 2));
		writer.WriteNumber("freezeSteps", spec.FreezeSteps);
		if (!spec.Frozen)
			writer.WriteNumber("stepsPerSecond", spec.StepsPerSecond);
		writer.WriteNumber("fireRate", MathF.Round(spec.FireRate, 3));
		if (spec.Contrast != 1f) writer.WriteNumber("contrast", MathF.Round(spec.Contrast, 3));
		if (spec.Saturation != 1f) writer.WriteNumber("saturation", MathF.Round(spec.Saturation, 3));
		if (spec.Brightness != 0f) writer.WriteNumber("brightness", MathF.Round(spec.Brightness, 3));
		if (spec.Tint != Color.White) writer.WriteString("tint", JsonUtil.ColorToHex(spec.Tint));
		if (spec.NormalStrength != 1f) writer.WriteNumber("normalStrength", MathF.Round(spec.NormalStrength, 3));
		if (spec.SilhouetteNormalStrength != 1f) writer.WriteNumber("silhouetteNormalStrength", MathF.Round(spec.SilhouetteNormalStrength, 3));
		if (spec.BlendCurve != 1f) writer.WriteNumber("blendCurve", MathF.Round(spec.BlendCurve, 3));
		if (spec.EdgeWeight != 1f) writer.WriteNumber("edgeWeight", MathF.Round(spec.EdgeWeight, 3));
		writer.WriteNumber("featureScale", MathF.Round(spec.FeatureScale, 3));
		if (spec.DetailScale != 1f) writer.WriteNumber("detailScale", MathF.Round(spec.DetailScale, 3));
		if (spec.DetailPhase != Vector2.Zero)
		{
			writer.WriteStartArray("detailPhase");
			writer.WriteNumberValue(MathF.Round(spec.DetailPhase.X, 3));
			writer.WriteNumberValue(MathF.Round(spec.DetailPhase.Y, 3));
			writer.WriteEndArray();
		}
		if (spec.PatchFrequency != 1f) writer.WriteNumber("patchFrequency", MathF.Round(spec.PatchFrequency, 3));
		if (spec.SirenFrequency != 10f) writer.WriteNumber("sirenFrequency", MathF.Round(spec.SirenFrequency, 3));
		if (spec.LatentGain != 1f) writer.WriteNumber("latentGain", MathF.Round(spec.LatentGain, 3));
		if (spec.LatentBias != 0f) writer.WriteNumber("latentBias", MathF.Round(spec.LatentBias, 3));
		if (spec.NearestStateSampling) writer.WriteBoolean("nearestStateSampling", true);
		writer.WriteEndObject();
	}
}
