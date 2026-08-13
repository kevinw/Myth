using Foster.Framework;

namespace Myth;

[GameActor(Sprite = "Editor/particles", Anchor = SpriteAnchor.TopLeft, Tags = ActorTag.Decoration,
	LivePositionEditable = true, LiveResizable = true)]
public class ArchiveWall : ProceduralWall
{
	public Color CabinetColor = new(0x293332);
	public Color ShelfColor = new(0x59625b);
	public Color LabelColor = new(0xd6c99d);
	public Color BookColor1 = new(0x8e504b);
	public Color BookColor2 = new(0x436f76);
	public Color BookColor3 = new(0x66724e);
	public Color BookColor4 = new(0xa1844d);
	public Color CableColor = new(0x282c2b);
	public Color RelicColor = new(0xb7eee2);
	public float BayScale = 1;
	public float ShelfThickness = 2;
	public float ShelfDepth = 1;
	public float BookDensity = 1;
	public float DrawerDensity = 1;
	public float CompartmentMotion = 1;
	public float CableDensity = 1;
	public float CableWidth = 2;
	public float ReliquaryDensity = 1;
	public float RelicGlow = 1;
	public float ArchiveDetail = 0.85f;

	public ArchiveWall()
	{
		PatternTexture = "patterns/pattern_57";
		DetailTexture = "patterns/noise/Noise_041";
		TextureScale = 0.75f;
		DetailScale = 1.25f;
		Specular = 0.24f;
		DitherPattern = MachineWallDitherPattern.Crosshatch;
		DitherScale = 0.7f;
		DitherDetailPreservation = 0.86f;
	}
}
