using Foster.Framework;

namespace Myth;

[GameActor(Sprite = "Editor/particles", Anchor = SpriteAnchor.TopLeft, Tags = ActorTag.Decoration,
	LivePositionEditable = true, LiveResizable = true)]
public class StrataWall : ProceduralWall
{
	public Color RockColor = new(0x57534a);
	public Color LayerColor = new(0x918674);
	public Color FaultColor = new(0x252724);
	public Color FossilColor = new(0xc1b58f);
	public Color CrystalColor1 = new(0x72b5b0);
	public Color CrystalColor2 = new(0xc6d5bd);
	public int LayerCount = 11;
	public float LayerVariation = 1;
	public float LayerDepth = 1;
	public float FaultDensity = 1;
	public float FaultWidth = 2;
	public float FossilDensity = 1.35f;
	public float CrystalDensity = 1.4f;
	public float CrystalScale = 1;
	public float Erosion = 0.6f;
	public float CrackDetail = 0.7f;

	public StrataWall()
	{
		PatternTexture = "patterns/pattern_37";
		DetailTexture = "patterns/noise/Noise_022";
		TextureScale = 0.7f;
		DetailScale = 1.6f;
		Specular = 0.18f;
		DitherPattern = MachineWallDitherPattern.Noise;
		DitherScale = 0.9f;
		DitherDetailPreservation = 0.68f;
	}
}
