#if EDITOR
namespace Myth;

static class EditorIcons
{
	public const string Search = "\uf002";
	public const string Visible = "\uf06e";
	public const string Hidden = "\uf070";
	public const string Cut = "\uf0c4";
	public const string Copy = "\uf0c5";
	public const string Reset = "\uf0e2";
	public const string SourceFile = "\uf1c9";
	public const string Delete = "\uf1f8";
	public const string Duplicate = "\uf24d";
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
		0xf002, 0xf002,
		0xf06e, 0xf06e,
		0xf070, 0xf070,
		0xf0c4, 0xf0c5,
		0xf0e2, 0xf0e2,
		0xf1c9, 0xf1c9,
		0xf1f8, 0xf1f8,
		0xf24d, 0xf24d,
		0
	];
}
#endif
