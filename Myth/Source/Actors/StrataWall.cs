using Foster.Framework;

namespace Myth;

[GameActor(Sprite = "Editor/particles", Icon = "layer-group", Anchor = SpriteAnchor.TopLeft, Tags = ActorTag.Decoration,
	LivePositionEditable = true, LiveResizable = true)]
public class StrataWall : ProceduralWall
{
	internal static ContentRef<VisualTextureContent> StrataPatternTexture => new("patterns/pattern_37");
	internal static ContentRef<VisualTextureContent> StrataDetailTexture => new("patterns/noise/Noise_022");

	public Color RockColor = new(0x57534a);
	public Color LayerColor = new(0x918674);
	public Color FaultColor = new(0x252724);
	public Color FossilColor = new(0xc1b58f);
	public Color CrystalColor1 = new(0x72b5b0);
	public Color CrystalColor2 = new(0xc6d5bd);
	[I(3, 24)] public int LayerCount = 11;
	[I(0, 3)] public float LayerVariation = 1;
	[I(0, 3)] public float LayerDepth = 1;
	[I(0, 4)] public float FaultDensity = 1;
	[I(0.5f, 8)] public float FaultWidth = 2;
	[I(0, 4)] public float FossilDensity = 1.35f;
	[I(0, 4)] public float CrystalDensity = 1.4f;
	[I(0.5f, 3)] public float CrystalScale = 1;
	[I(0, 1)] public float Erosion = 0.6f;
	[I(0.25f, 4)] public float CrackDetail = 0.7f;

	public StrataWall()
	{
		PatternTexture = StrataPatternTexture;
		DetailTexture = StrataDetailTexture;
		TextureScale = 0.7f;
		DetailScale = 1.6f;
		Specular = 0.18f;
		DitherPattern = MachineWallDitherPattern.Noise;
		DitherScale = 0.9f;
		DitherDetailPreservation = 0.68f;
	}
}
