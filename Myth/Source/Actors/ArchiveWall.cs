using Foster.Framework;

namespace Myth;

[GameActor(Sprite = "Editor/particles", Icon = "box-archive", Anchor = SpriteAnchor.TopLeft, Tags = ActorTag.Decoration,
	LivePositionEditable = true, LiveResizable = true)]
public class ArchiveWall : ProceduralWall
{
	internal static ContentRef<VisualTextureContent> ArchivePatternTexture => new("patterns/pattern_57");
	internal static ContentRef<VisualTextureContent> ArchiveDetailTexture => new("patterns/noise/Noise_041");

	public Color CabinetColor = new(0x293332);
	public Color ShelfColor = new(0x59625b);
	public Color LabelColor = new(0xd6c99d);
	public Color BookColor1 = new(0x8e504b);
	public Color BookColor2 = new(0x436f76);
	public Color BookColor3 = new(0x66724e);
	public Color BookColor4 = new(0xa1844d);
	public Color CableColor = new(0x282c2b);
	public Color RelicColor = new(0xb7eee2);
	[I(0.5f, 3)] public float BayScale = 1;
	[I(0.5f, 6)] public float ShelfThickness = 2;
	[I(0, 3)] public float ShelfDepth = 1;
	[I(0, 3)] public float BookDensity = 1;
	[I(0, 3)] public float DrawerDensity = 1;
	[I(0, 3)] public float CompartmentMotion = 1;
	[I(0, 3)] public float CableDensity = 1;
	[I(0.5f, 6)] public float CableWidth = 2;
	[I(0, 3)] public float ReliquaryDensity = 1;
	[I(0, 2)] public float RelicGlow = 1;
	[I(0.25f, 3)] public float ArchiveDetail = 0.85f;

	public ArchiveWall()
	{
		PatternTexture = ArchivePatternTexture;
		DetailTexture = ArchiveDetailTexture;
		TextureScale = 0.75f;
		DetailScale = 1.25f;
		Specular = 0.24f;
		DitherPattern = MachineWallDitherPattern.Crosshatch;
		DitherScale = 0.7f;
		DitherDetailPreservation = 0.86f;
	}
}
