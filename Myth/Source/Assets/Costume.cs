using System.Text.Json;
using System.Text.Json.Serialization;
using Foster.Framework;

namespace Myth;

[Flags]
public enum RigTags
{
	None = 0,
	Trunk = 1 << 0, Waist = 1 << 1, Neck = 1 << 2,
	Shoulder = 1 << 3, UpperArm = 1 << 4, Forearm = 1 << 5, Hand = 1 << 6,
	Hip = 1 << 7, Thigh = 1 << 8, Shin = 1 << 9, Foot = 1 << 10, Tail = 1 << 11,
	Left = 1 << 12, Right = 1 << 13,
}

public enum CostumePieceKind { Sleeve, Stud, Spikes }

// Covers every bone matching Bones between From and To, as fractions along the bone. Width scales the
// bone's width. Sleeve: Count bands (stripes when > 1). Stud: Count discs. Spikes: Count triangles,
// Length bone-widths tall, on the other side when negative.
public sealed class CostumePiece
{
	const RigTags Sides = RigTags.Left | RigTags.Right;

	public CostumePieceKind Kind { get; set; }
	public RigTags Bones { get; set; }
	public float From { get; set; }
	public float To { get; set; } = 1;
	public int Count { get; set; } = 1;
	public float Width { get; set; } = 1;
	public float Length { get; set; } = 1;
	public string Color { get; set; } = "ffffff";
	public string Surface { get; set; } = "";

	[JsonIgnore] public Color Fill { get; private set; }

	public bool Matches(RigTags tags)
		=> (tags & Bones & ~Sides) != 0 && ((Bones & Sides) == 0 || (tags & Bones & Sides) != 0);

	internal void Resolve()
	{
		if (!Enum.IsDefined(Kind) || !float.IsFinite(From) || !float.IsFinite(To) || From < 0 || To > 1 || From >= To ||
			Count < 1 || Count > 64 || !float.IsFinite(Width) || Width <= 0 || !float.IsFinite(Length))
			throw new InvalidDataException("Costume pieces need a span within 0–1, 1–64 repeats, a positive width, and finite dimensions.");
		Fill = SurfaceSpec.ParseTint(Color);
	}
}

// On-disk authoring spec (Sources/Costumes/<id>.json); pieces draw in list order.
public sealed class Costume
{
	public List<CostumePiece> Pieces { get; set; } = [];

	public static Costume Parse(string json)
	{
		var costume = JsonSerializer.Deserialize(json, CostumeJsonContext.Default.Costume) ?? new Costume();
		foreach (var piece in costume.Pieces)
			piece.Resolve();
		return costume;
	}
}

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNameCaseInsensitive = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(Costume))]
internal partial class CostumeJsonContext : JsonSerializerContext;

public static partial class Assets
{
	public static readonly Dictionary<string, Costume> Costumes = new(StringComparer.OrdinalIgnoreCase);

	public static Costume? GetCostume(ContentRef<CostumeContent> reference)
		=> Costumes.GetValueOrDefault(reference.Name ?? "");

	internal static void LoadCostumes(StorageContainer storage, string dir)
	{
		Costumes.Clear();
		if (!storage.DirectoryExists(dir))
			return;
		foreach (var file in storage.EnumerateDirectory(dir, "*.json", SearchOption.TopDirectoryOnly))
		{
			try { Costumes[Path.GetFileNameWithoutExtension(file)] = Costume.Parse(storage.ReadAllText(file)); }
			catch (Exception e) { Console.Error.WriteLine($"Failed to load costume '{file}': {e.Message}"); }
		}
	}
}
