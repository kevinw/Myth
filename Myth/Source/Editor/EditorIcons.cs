#if EDITOR
namespace Myth;

static class EditorIcons
{
	public const string Search = "\uf002";
	public const string Reset = "\uf0e2";
	public const string SearchActors = Search + "  Actors";
	public const string ResetButton = Reset + "  Reset";
	public const string ResetExperimental = Reset + "  Reset experimental";
	public const string ResetToolWindowLayout = Reset + "  Reset tool window layout";

	public static readonly ushort[] GlyphRanges =
	[
		0xf002, 0xf002,
		0xf0e2, 0xf0e2,
		0
	];
}
#endif
