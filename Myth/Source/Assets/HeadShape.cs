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
	public float EyeballSize { get; set; } = 0.17f; // 0 leaves bare sockets
	public float IrisSize { get; set; } = 0.55f;
	public float LidDroop { get; set; } = 0.3f; // how far the lids rest closed

	public float MouthWidth { get; set; } = 0.3f;
	public float MouthHeight { get; set; } = -0.52f;
	public float MouthSize { get; set; } = 0.05f;
	public float LipSize { get; set; } = 0.035f; // the band around the mouth's cut that reads as lips
	public float TeethSize { get; set; } = 0.07f;
	public float BrowHair { get; set; } = 0.04f; // how far over the brow its hair grows

	public Color HairColor { get; set; } = new(0x2e2320);
	public Color LipColor { get; set; } = new(0x6e3433);
	public Color TeethColor { get; set; } = new(0xcfbf8a);
	public Color ScleraColor { get; set; } = new(0xd8cba0);
	public Color IrisColor { get; set; } = new(0x5b6a35);

	public float EarSize { get; set; }
	public float EarHeight { get; set; }

	public float FleshSoftness { get; set; } = 1; // how far the jowls, ears, nose and brow lag and bounce; 0 rigid

	public List<HeadAdornment> Adornments { get; set; } = [];

	// A pattern from the texture bucket (e.g. "patterns/pattern_79") stuck to the skin, spreading out from the face's
	// midline: painted in PatternColor, or glowing.
	public string Pattern { get; set; } = "";
	public bool PatternInvert { get; set; }
	public float PatternScale { get; set; } = 1.5f;
	public float PatternSpread { get; set; } = 1;
	public float PatternGlow { get; set; }
	public Color PatternColor { get; set; } = new(0x2a1c18);

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

[JsonConverter(typeof(JsonStringEnumConverter<AdornmentKind>))]
public enum AdornmentKind { Ring, Stud, Horn }

// Jewelry and growths worn on a head, in head radii. A ring hangs from its point, its plane facing front at Yaw 0 and the
// side at 90, and swings as the head moves; a stud is a ball; a horn grows Size long from its point along Yaw and Pitch
// (degrees), tapering from Thickness. Mirror adds the same on the other side.
public sealed class HeadAdornment
{
	public AdornmentKind Kind { get; set; }
	public float X { get; set; }
	public float Y { get; set; }
	public float Z { get; set; }
	public float Size { get; set; } = 0.12f;
	public float Thickness { get; set; } = 0.025f;
	public float Yaw { get; set; }
	public float Pitch { get; set; }
	public bool Mirror { get; set; } = true;
	public bool Metal { get; set; } = true;
	public Color Color { get; set; } = new(0xc9a548);
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
