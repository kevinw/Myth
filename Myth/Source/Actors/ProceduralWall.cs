using Foster.Framework;

namespace Myth;

public abstract class ProceduralWall : Actor, IResizable
{
	public static readonly Point2 DefaultSize = new(Game.Width, Game.Height);
	internal static ContentRef<VisualTextureContent> DefaultPattern => new("patterns/pattern_59");
	internal static ContentRef<VisualTextureContent> DefaultDetail => new("patterns/noise/Noise_014");

	public Point2 Size { get; set; } = DefaultSize;
	[I(1, 9999)] public int Seed = 1;
	public ContentRef<VisualTextureContent> PatternTexture = DefaultPattern;
	public ContentRef<VisualTextureContent> DetailTexture = DefaultDetail;
	[I(0.1f, 4)] public float TextureScale = 1;
	[I(0.1f, 4)] public float DetailScale = 1;
	[I(0, 3)] public float NormalStrength = 1;
	[I(0, 1)] public float Specular = 1;
	public MachineWallDitherPattern DitherPattern = MachineWallDitherPattern.Auto;
	[I(0.1f, 2)] public float DitherScale = 0.65f;
	[I(0, 1)] public float DitherDetailPreservation = 0.35f;

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
