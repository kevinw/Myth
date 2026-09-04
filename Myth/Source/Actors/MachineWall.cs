using Foster.Framework;

namespace Myth;

public enum MachineWallDitherPattern
{
	Auto,
	Ordered,
	Crosshatch,
	Noise,
}

[GameActor(Sprite = "Editor/particles", Icon = "gears", Anchor = SpriteAnchor.TopLeft, Tags = ActorTag.Decoration,
	LivePositionEditable = true, LiveResizable = true)]
public class MachineWall : ProceduralWall
{
	public Color MetalColor = new(0x667078);
	[I(0, 4)] public float Motion = 1;
	[I(0.25f, 4)] public float GreebleScale = 1;
	[I(0, 3)] public float GreebleDepth = 1;
	[I(0, 4)] public float PipeDensity = 1;
	[I(0.5f, 4)] public float PipeMinWidth = 2;
	[I(4, 20)] public float PipeMaxWidth = 10;
	[I(0.1f, 3)] public float PipeBend = 1;
	[I(0, 3)] public float PipeDepth = 1;
	[I(0.25f, 4)] public float PipeDetail = 1;
	[I(0, 1)] public float PipeRoughness = 0.34f;
	public Color PipeColor1 = new(0x37a0a4);
	public Color PipeColor2 = new(0xc4a64f);
	public Color PipeColor3 = new(0xa95050);
	public Color PipeColor4 = new(0xcbd4d0);
	[I(0, 4)] public float ControlPanelDensity = 1;
	[I(0, 2)] public float LedBrightness = 1;
}
