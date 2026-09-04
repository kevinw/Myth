using Foster.Framework;

namespace Myth;

[GameActor(Sprite = "Editor/particles", Icon = "microchip", Anchor = SpriteAnchor.TopLeft, Tags = ActorTag.Decoration,
	LivePositionEditable = true, LiveResizable = true)]
public class CircuitWall : ProceduralWall
{
	internal static ContentRef<VisualTextureContent> CircuitPatternTexture => new("patterns/pattern_12");
	internal static ContentRef<VisualTextureContent> CircuitDetailTexture => new("patterns/noise/Noise_003");

	public Color BoardColor = new(0x173a35);
	public Color ModuleColor = new(0x344a48);
	public Color TraceColor1 = new(0xb98b42);
	public Color TraceColor2 = new(0x4faaa2);
	public Color TraceColor3 = new(0xb7c4b5);
	public Color ContactColor = new(0xd5c07a);
	public Color SignalColor1 = new(0x5cff86);
	public Color SignalColor2 = new(0xff554c);
	[I(0.25f, 4)] public float GridScale = 1;
	[I(0, 4)] public float ModuleDensity = 1;
	[I(0, 3)] public float ModuleDepth = 1;
	[I(0, 4)] public float TraceDensity = 1;
	[I(0.5f, 6)] public float TraceWidth = 1.5f;
	[I(0, 3)] public float TraceDepth = 1;
	[I(0, 4)] public float ContactDensity = 1;
	[I(0, 4)] public float SignalDensity = 1;
	[I(0, 3)] public float SignalPulse = 1;
	[I(0.25f, 4)] public float TraceDetail = 1.4f;

	public CircuitWall()
	{
		PatternTexture = CircuitPatternTexture;
		DetailTexture = CircuitDetailTexture;
		TextureScale = 0.65f;
		DetailScale = 1.8f;
		Specular = 0.42f;
		DitherPattern = MachineWallDitherPattern.Ordered;
		DitherScale = 0.55f;
		DitherDetailPreservation = 0.9f;
	}
}
