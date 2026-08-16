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
	[I(0.25f, 4)] public float GrowthScale = 1;
	[I(0, 4)] public float BranchDensity = 1.25f;
	[I(0.5f, 4)] public float BranchWidthMin = 2;
	[I(4, 20)] public float BranchWidthMax = 9;
	[I(0, 1)] public float Taper = 0.7f;
	[I(0, 3)] public float Curl = 1;
	[I(0, 4)] public float PlateDensity = 1;
	[I(0.5f, 3)] public float PlateSize = 1;
	[I(0, 3)] public float PlateDepth = 1;
	[I(0, 4)] public float GlowDensity = 1;
	[I(0, 3)] public float GlowPulse = 1;
	[I(0, 3)] public float BreathingAmount = 1;
	[I(0, 1)] public float Wetness = 0.7f;
	[I(0.25f, 4)] public float RootDetail = 1;

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
