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
	public float MembraneDensity = 1.25f;
	public float MembraneDepth = 1;
	public float MuscleDensity = 1.35f;
	public float MuscleWidthMin = 4;
	public float MuscleWidthMax = 12;
	public float MuscleDepth = 1;
	public float VeinDensity = 1.3f;
	public float VeinWidth = 1.5f;
	public float OrganDensity = 1.45f;
	public float OrganScale = 1.15f;
	public float PulseAmount = 1;
	public float PulseRate = 1;
	public float Wetness = 0.8f;
	public float FleshDetail = 1.2f;

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
