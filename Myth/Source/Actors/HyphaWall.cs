using Foster.Framework;

namespace Myth;

[GameActor(Sprite = "Editor/particles", Anchor = SpriteAnchor.TopLeft, Tags = ActorTag.Decoration,
	LivePositionEditable = true, LiveResizable = true)]
public class HyphaWall : ProceduralWall
{
	public Color SurfaceColor = new(0x31463a);
	public Color PlateColor = new(0x596b50);
	public Color VeinColor = new(0x9ba47b);
	public Color GlowColor1 = new(0x75e69b);
	public Color GlowColor2 = new(0xd7f2b0);
	public float GrowthScale = 1;
	public float BranchDensity = 1.25f;
	public float BranchWidthMin = 2;
	public float BranchWidthMax = 9;
	public float Taper = 0.7f;
	public float Curl = 1;
	public float PlateDensity = 1;
	public float PlateSize = 1;
	public float PlateDepth = 1;
	public float GlowDensity = 1;
	public float GlowPulse = 1;
	public float BreathingAmount = 1;
	public float Wetness = 0.7f;
	public float RootDetail = 1;

	public HyphaWall()
	{
		PatternTexture = "patterns/pattern_68";
		DetailTexture = "patterns/noise/Noise_036";
		TextureScale = 0.8f;
		DetailScale = 1.4f;
		Specular = 0.65f;
		DitherPattern = MachineWallDitherPattern.Crosshatch;
		DitherScale = 0.8f;
		DitherDetailPreservation = 0.75f;
	}
}
