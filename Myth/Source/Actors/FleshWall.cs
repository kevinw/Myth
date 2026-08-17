using Foster.Framework;

namespace Myth;

[GameActor(Sprite = "Editor/particles", Anchor = SpriteAnchor.TopLeft, Tags = ActorTag.Decoration,
	LivePositionEditable = true, LiveResizable = true)]
public class FleshWall : ProceduralWall
{
	public Color MembraneColor = new(0x703743);
	public Color MuscleColor1 = new(0xc86772);
	public Color MuscleColor2 = new(0x974451);
	public Color VeinColor = new(0x536d9a);
	public Color ValveColor = new(0xdf8c87);
	public Color OrganColor = new(0xb64b67);
	public Color NerveGlowColor = new(0xf1ca79);
	[I(0, 4)] public float MembraneDensity = 1.25f;
	[I(0, 3)] public float MembraneDepth = 1;
	[I(0, 4)] public float MuscleDensity = 1.35f;
	[I(1, 6)] public float MuscleWidthMin = 4;
	[I(6, 24)] public float MuscleWidthMax = 12;
	[I(0, 3)] public float MuscleDepth = 1;
	[I(0, 4)] public float VeinDensity = 1.3f;
	[I(0.5f, 5)] public float VeinWidth = 1.5f;
	[I(0, 4)] public float OrganDensity = 1.45f;
	[I(0.5f, 3)] public float OrganScale = 1.15f;
	[I(0, 3)] public float PulseAmount = 1;
	[I(0.1f, 3)] public float PulseRate = 1;
	[I(0, 1)] public float Wetness = 0.8f;
	[I(0.25f, 4)] public float FleshDetail = 1.2f;

	public FleshWall()
	{
		PatternTexture = "patterns/pattern_34";
		DetailTexture = "patterns/noise/Noise_069";
		TextureScale = 0.75f;
		DetailScale = 1.5f;
		Specular = 0.72f;
		DitherPattern = MachineWallDitherPattern.Noise;
		DitherScale = 0.72f;
		DitherDetailPreservation = 0.82f;
	}
}
