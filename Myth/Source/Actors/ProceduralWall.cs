using Foster.Framework;

namespace Myth;

public abstract class ProceduralWall : Actor, IResizable
{
	public static readonly Point2 DefaultSize = new(Game.Width, Game.Height);

	public Point2 Size { get; set; } = DefaultSize;
	public int Seed = 1;
	public string PatternTexture = ProceduralWallRenderer.DefaultPatternTexture;
	public string DetailTexture = ProceduralWallRenderer.DefaultDetailTexture;
	public float TextureScale = 1;
	public float DetailScale = 1;
	public float NormalStrength = 1;
	public float Specular = 1;
	public MachineWallDitherPattern DitherPattern = MachineWallDitherPattern.Auto;
	public float DitherScale = 0.65f;
	public float DitherDetailPreservation = 0.35f;

	protected ProceduralWall()
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
