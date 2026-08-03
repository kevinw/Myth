#if EDITOR
namespace Myth;

static class EditorIcons
{
	public const string Search = "\uf002";
	public const string Visible = "\uf06e";
	public const string Hidden = "\uf070";
	public const string Reset = "\uf0e2";
	public const string SearchActors = Search + "  Actors";
	public const string ResetButton = Reset + "  Reset";
	public const string ResetExperimental = Reset + "  Reset experimental";
	public const string ResetToolWindowLayout = Reset + "  Reset tool window layout";

	public static readonly ushort[] GlyphRanges =
	[
		0xf002, 0xf002,
		0xf06e, 0xf06e,
		0xf070, 0xf070,
		0xf0e2, 0xf0e2,
		0
	];
}
#endif
