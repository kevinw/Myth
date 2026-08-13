using Foster.Framework;

namespace Myth;

[GameActor(Sprite = "Editor/particles", Anchor = SpriteAnchor.TopLeft, Tags = ActorTag.Decoration,
	LivePositionEditable = true, LiveResizable = true)]
public class RuinsWall : ProceduralWall
{
	public Color MortarColor = new(0x343936);
	public Color StoneColor1 = new(0x667069);
	public Color StoneColor2 = new(0x858477);
	public Color StoneColor3 = new(0x4d5d59);
	public Color RootColor = new(0x536044);
	public Color ArtifactColor = new(0x77c7b2);
	public float BlockScale = 1;
	public float BlockDepth = 1;
	public float CourseVariation = 1;
	public float Damage = 0.45f;
	public float ArchDensity = 1;
	public float ArchScale = 1;
	public float RootDensity = 1;
	public float RootWidth = 3;
	public float CrackDensity = 1;
	public float CrackWidth = 1;
	public float ArtifactDensity = 1;
	public float ArtifactGlow = 0.65f;
	public float RuinDetail = 0.9f;

	public RuinsWall()
	{
		PatternTexture = "patterns/pattern_68";
		DetailTexture = "patterns/noise/Noise_055";
		TextureScale = 0.7f;
		DetailScale = 1.45f;
		Specular = 0.16f;
		DitherPattern = MachineWallDitherPattern.Crosshatch;
		DitherScale = 0.9f;
		DitherDetailPreservation = 0.78f;
	}
}
