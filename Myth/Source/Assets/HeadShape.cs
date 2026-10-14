using System.Text.Json;
using System.Text.Json.Serialization;
using Foster.Framework;

namespace Myth;

// On-disk authoring spec for a sculpted head (Sources/Heads/<id>.json). Units are head radii: x to the head's right,
// y up, z out of the face. Defaults are the stock head; each knob moves one feature.
public sealed class HeadShape
{
	public float CraniumWidth { get; set; } = 0.84f;
	public float CraniumHeight { get; set; } = 0.92f;
	public float CraniumDepth { get; set; } = 1f;
	public float CraniumLift { get; set; } = 0.14f;

	public float JawWidth { get; set; } = 0.64f;
	public float JawHeight { get; set; } = 0.48f;
	public float JawDepth { get; set; } = 0.7f;
	public float JawDrop { get; set; } = 0.46f;
	public float JawForward { get; set; } = 0.2f;
	public float JawBlend { get; set; } = 0.22f;

	public float BrowSize { get; set; } = 0.15f;
	public float BrowWidth { get; set; } = 0.44f;
	public float BrowHeight { get; set; } = 0.26f;
	public float BrowForward { get; set; } = 0.8f;

	public float NoseSize { get; set; } = 0.15f;
	public float NoseLength { get; set; } = 0.3f;
	public float NoseHeight { get; set; } = 0.16f;
	public float NoseDroop { get; set; } = 0.34f;
	public float NoseFlat { get; set; } // 0 a rounded nose .. 1 a flat-faced snout
	public float NostrilSize { get; set; }

	public float EyeSpacing { get; set; } = 0.34f;
	public float EyeHeight { get; set; } = 0.06f;
	public float EyeSize { get; set; } = 0.25f;
	public float EyeDepth { get; set; } = 0.72f;

	public float MouthWidth { get; set; } = 0.3f;
	public float MouthHeight { get; set; } = -0.52f;
	public float MouthSize { get; set; } = 0.05f;

	public float EarSize { get; set; }
	public float EarHeight { get; set; }

	// Bumped by every edit, so rendered heads know to redraw.
	[JsonIgnore] public int Version { get; set; }

	public static readonly HeadShape Default = new();

	public static HeadShape Parse(string json)
		=> JsonSerializer.Deserialize(json, HeadShapeJsonContext.Default.HeadShape) ?? new HeadShape();

	public void Save(string path)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		File.WriteAllText(path, JsonSerializer.Serialize(this, HeadShapeJsonContext.Default.HeadShape) + "\n");
	}
}

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(HeadShape))]
internal partial class HeadShapeJsonContext : JsonSerializerContext;

public static partial class Assets
{
	public static readonly Dictionary<string, HeadShape> HeadShapes = new(StringComparer.OrdinalIgnoreCase);

	// A rig with no head shape picked wears Sources/Heads/stock.json, or the built-in stock head without it.
	public const string StockHeadShape = "stock";

	public static HeadShape GetHeadShape(ContentRef<HeadShapeContent> reference)
		=> HeadShapes.GetValueOrDefault(reference.Name is { Length: > 0 } name ? name : StockHeadShape) ?? HeadShape.Default;

	internal static void LoadHeadShapes(StorageContainer storage, string dir)
		=> LoadJsonLibrary(HeadShapes, storage, dir, HeadShape.Parse, "head shape");
}
