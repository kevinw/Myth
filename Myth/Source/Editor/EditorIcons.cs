#if EDITOR
using Foster.Framework;

namespace Myth;

static class EditorIcons
{
	public const string Search = "\uf002";
	public const string Speaker = "\uf028";
	public const string Add = "\uf067";
	public const string Lock = "\uf023";
	public const string Up = "\uf062";
	public const string Down = "\uf063";
	public const string Visible = "\uf06e";
	public const string Hidden = "\uf070";
	public const string Randomize = "\uf074";
	public const string Unlock = "\uf09c";
	public const string Cut = "\uf0c4";
	public const string Copy = "\uf0c5";
	public const string Close = "\uf00d";
	public const string Reset = "\uf0e2";
	public const string Code = "\uf121";
	public const string Cube = "\uf1b2";
	public const string Pencil = "\uf040";
	public const string Sliders = "\uf1de";
	public const string SourceFile = "\uf1c9";
	public const string Delete = "\uf1f8";
	public const string Duplicate = "\uf24d";
	public const string BoltLightning = "\ue0b7";
	public const string Clapperboard = "\ue131";
	public const string Timeline = "\ue29c";
	public const string WandMagicSparkles = "\ue2ca";
	public const string Font = "\uf031";
	public const string Gears = "\uf085";
	public const string BoxArchive = "\uf187";
	public const string HeartPulse = "\uf21e";
	public const string CommentDots = "\uf27b";
	public const string Microchip = "\uf2db";
	public const string Seedling = "\uf4d8";
	public const string Archway = "\uf557";
	public const string LayerGroup = "\uf5fd";
	public const string Wind = "\uf72e";
	public const string Smog = "\uf75f";
	public const string WaveSquare = "\uf83e";
	public const string Fan = "\uf863";
	public const string SearchActors = Search + "  Actors";
	public const string CutMenuItem = Cut + "  Cut";
	public const string CopyMenuItem = Copy + "  Copy";
	public const string DeleteMenuItem = Delete + "  Delete";
	public const string DuplicateMenuItem = Duplicate + "  Duplicate";
	public const string ResetButton = Reset + "  Reset";
	public const string ResetExperimental = Reset + "  Reset experimental";
	public const string ResetToolWindowLayout = Reset + "  Reset tool window layout";

	public static readonly ushort[] GlyphRanges =
	[
		0xe0b7, 0xe0b7,
		0xe131, 0xe131,
		0xe29c, 0xe29c,
		0xe2ca, 0xe2ca,
		0xf002, 0xf002,
		0xf00d, 0xf00d,
		0xf067, 0xf067,
		0xf023, 0xf023,
		0xf028, 0xf028,
		0xf031, 0xf031,
		0xf040, 0xf040,
		0xf062, 0xf063,
		0xf06e, 0xf06e,
		0xf070, 0xf070,
		0xf074, 0xf074,
		0xf085, 0xf085,
		0xf09c, 0xf09c,
		0xf0c4, 0xf0c5,
		0xf0e2, 0xf0e2,
		0xf121, 0xf121,
		0xf187, 0xf187,
		0xf1b2, 0xf1b2,
		0xf1c9, 0xf1c9,
		0xf1de, 0xf1de,
		0xf1f8, 0xf1f8,
		0xf21e, 0xf21e,
		0xf24d, 0xf24d,
		0xf27b, 0xf27b,
		0xf2db, 0xf2db,
		0xf4d8, 0xf4d8,
		0xf557, 0xf557,
		0xf5fd, 0xf5fd,
		0xf72e, 0xf72e,
		0xf75f, 0xf75f,
		0xf83e, 0xf83e,
		0xf863, 0xf863,
		0
	];

	static SpriteFont? paletteFont;

	public static void LoadPaletteFont(GraphicsDevice graphics)
		=> paletteFont = new SpriteFont(graphics,
			Path.Join(AppContext.BaseDirectory, "EditorAssets", "Fonts", "fa-solid-900.ttf"), 32, GetGlyphCodepoints());

	static int[] GetGlyphCodepoints()
	{
		var count = 0;
		for (var i = 0; GlyphRanges[i] != 0; i += 2)
			count += GlyphRanges[i + 1] - GlyphRanges[i] + 1;

		var codepoints = new int[count];
		var index = 0;
		for (var i = 0; GlyphRanges[i] != 0; i += 2)
			for (var codepoint = GlyphRanges[i]; codepoint <= GlyphRanges[i + 1]; codepoint++)
				codepoints[index++] = codepoint;
		return codepoints;
	}

	public static bool TryGetPaletteIcon(string name, out Subtexture icon)
	{
		var codepoint = name switch
		{
			"speaker" => Speaker[0],
			"bolt-lightning" => BoltLightning[0],
			"clapperboard" => Clapperboard[0],
			"timeline" => Timeline[0],
			"wand-magic-sparkles" => WandMagicSparkles[0],
			"font" => Font[0],
			"gears" => Gears[0],
			"box-archive" => BoxArchive[0],
			"heart-pulse" => HeartPulse[0],
			"comment-dots" => CommentDots[0],
			"microchip" => Microchip[0],
			"seedling" => Seedling[0],
			"archway" => Archway[0],
			"layer-group" => LayerGroup[0],
			"wind" => Wind[0],
			"smog" => Smog[0],
			"wave-square" => WaveSquare[0],
			"fan" => Fan[0],
			"magnifying-glass" => Search[0],
			_ => 0
		};
		icon = paletteFont?[codepoint].Subtexture ?? default;
		return icon.Texture != null;
	}

	public static void UnloadPaletteFont()
	{
		paletteFont?.Dispose();
		paletteFont = null;
	}
}
#endif
