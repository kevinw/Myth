using System.Text.Json;
using System.Text.Json.Serialization;
using Foster.Framework;

namespace Myth;

public enum RibbonStampKind { Band, Sprite }
public enum RibbonSide { Center, Left, Right, Alternate }
public enum RibbonCap { None, Round }

// Placed every Spacing px (± Jitter × Spacing) along a ribbon. Band: a bar across the ribbon, Size
// ribbon-widths long and Thickness widths thick, over an Outline-thick bar in OutlineColor. Sprite: the
// sprite's first frame scaled by Size, up pointing away from the spine when Align is set.
public sealed class RibbonStamp
{
	public RibbonStampKind Kind { get; set; }
	public string Sprite { get; set; } = "";
	public float Spacing { get; set; } = 24;
	public float Jitter { get; set; }
	public RibbonSide Side { get; set; }
	public bool Align { get; set; } = true;
	public float Size { get; set; } = 1;
	public float Thickness { get; set; } = 0.08f;
	public float Outline { get; set; }
	public string Color { get; set; } = "ffffff";
	public string OutlineColor { get; set; } = "000000";

	[JsonIgnore] public Color Fill { get; private set; }
	[JsonIgnore] public Color OutlineFill { get; private set; }

	internal void Resolve()
	{
		if (!float.IsFinite(Spacing) || Spacing < 2 || !float.IsFinite(Jitter) || Jitter is < 0 or > 1 ||
			!float.IsFinite(Size) || Size <= 0 || (Kind == RibbonStampKind.Sprite && Sprite == ""))
			throw new InvalidDataException("Ribbon stamps need Spacing ≥ 2, Jitter within 0–1, a positive Size, and a Sprite for sprite stamps.");
		Fill = SurfaceSpec.ParseTint(Color);
		OutlineFill = SurfaceSpec.ParseTint(OutlineColor);
	}
}

// A line along the ribbon, Offset ribbon-widths from the spine toward the left edge (negative: right).
public sealed class RibbonStripe
{
	public float Offset { get; set; }
	public float Thickness { get; set; } = 0.08f;
	public string Color { get; set; } = "ffffff";
	public float Opacity { get; set; } = 1;

	[JsonIgnore] public Color Fill { get; private set; }

	internal void Resolve() => Fill = SurfaceSpec.ParseTint(Color) * Opacity;
}

// On-disk authoring spec (Sources/RibbonStyles/<id>.json): what kind of thing a CurveRibbon is.
// While a ribbon grows or retracts, GrowParticles (a particle effect name) trail its growth front and
// GrowSound (a looping FMOD event path) plays there, its "speed" parameter set to the front's px/second.
public sealed class RibbonStyle
{
	public string Surface { get; set; } = "";
	public bool Taper { get; set; } = true;
	public bool Branches { get; set; } = true;
	public RibbonCap Cap { get; set; }
	public string GrowParticles { get; set; } = "";
	public string GrowSound { get; set; } = "";
	public List<RibbonStripe> Stripes { get; set; } = [];
	public List<RibbonStamp> Stamps { get; set; } = [];

	public static RibbonStyle Parse(string json)
	{
		var style = JsonSerializer.Deserialize(json, RibbonStyleJsonContext.Default.RibbonStyle) ?? new RibbonStyle();
		foreach (var stripe in style.Stripes)
			stripe.Resolve();
		foreach (var stamp in style.Stamps)
			stamp.Resolve();
		return style;
	}
}

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNameCaseInsensitive = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(RibbonStyle))]
internal partial class RibbonStyleJsonContext : JsonSerializerContext;

public static partial class Assets
{
	public static readonly Dictionary<string, RibbonStyle> RibbonStyles = new(StringComparer.OrdinalIgnoreCase);

	public static RibbonStyle? GetRibbonStyle(ContentRef<RibbonStyleContent> reference)
		=> RibbonStyles.GetValueOrDefault(reference.Name ?? "");

	internal static void LoadRibbonStyles(StorageContainer storage, string dir)
		=> LoadJsonLibrary(RibbonStyles, storage, dir, RibbonStyle.Parse, "ribbon style");
}
