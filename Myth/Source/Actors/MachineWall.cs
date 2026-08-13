using Foster.Framework;

namespace Myth;

public enum MachineWallDitherPattern
{
	Auto,
	Ordered,
	Crosshatch,
	Noise,
}

[GameActor(Sprite = "Editor/particles", Anchor = SpriteAnchor.TopLeft, Tags = ActorTag.Decoration,
	LivePositionEditable = true, LiveResizable = true)]
public class MachineWall : Actor, IResizable
{
	public static readonly Point2 DefaultSize = new(Game.Width, Game.Height);

	public Point2 Size { get; set; } = DefaultSize;
	public int Seed = 1;
	public Color MetalColor = new(0x667078);
	public float Motion = 1;
	public float GreebleScale = 1;
	public float GreebleDepth = 1;
	public string PatternTexture = MachineWallRenderer.DefaultPatternTexture;
	public string DetailTexture = MachineWallRenderer.DefaultDetailTexture;
	public float TextureScale = 1;
	public float DetailScale = 1;
	public float NormalStrength = 1;
	public float Specular = 1;
	public float PipeDensity = 1;
	public float PipeMinWidth = 2;
	public float PipeMaxWidth = 10;
	public float PipeBend = 1;
	public float PipeDepth = 1;
	public float PipeDetail = 1;
	public float PipeRoughness = 0.34f;
	public Color PipeColor1 = new(0x37a0a4);
	public Color PipeColor2 = new(0xc4a64f);
	public Color PipeColor3 = new(0xa95050);
	public Color PipeColor4 = new(0xcbd4d0);
	public float ControlPanelDensity = 1;
	public float LedBrightness = 1;
	public MachineWallDitherPattern DitherPattern = MachineWallDitherPattern.Auto;
	public float DitherScale = 0.65f;
	public float DitherDetailPreservation = 0.35f;

	public MachineWall()
	{
		Sprite = null;
		CollidesWithSolids = false;
		Depth = 9;
	}

	public override bool CastsSpriteShadow => false;
	public override bool CastsReflection => false;

	public override void Render(Batcher batcher) { }
	public override void RenderEmissiveOccluder(Batcher batcher) { }
	public override void RenderNormals(Batcher batcher) { }
	public override void RenderShadow(Batcher batcher, Color color) { }
	public override void RenderReflection(Batcher batcher, Color color) { }

	public override Rect GetVisualBounds() => new(Position, Size);
}
