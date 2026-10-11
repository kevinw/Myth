using System.Numerics;
using Foster.Framework;

namespace Myth;

public abstract class ProceduralWall : Actor, IResizable, IPaintedMask
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

	// Painted erase, one cell per tile: alpha is how erased that cell is, so an unpainted mask grows the whole
	// wall. Walls end where it's painted, and a soft brush's skirt becomes a ragged fringe (see Erased).
	public ContentRef<BlobContent> PaintedMask
	{
		get => maskData;
		set => (maskData, mask) = (value, null);
	}
	ContentRef<BlobContent> maskData = new("");
	RoomColorField? mask;

	static Point2 MaskCells(Point2 size) => (size + Point2.One * (Game.TileSize - 1)) / Game.TileSize;

	RoomColorField EraseMask
	{
		get
		{
			if (mask == null || mask.Size != MaskCells(Size))
			{
				mask = new RoomColorField(MaskCells(Size));
				mask.Deserialize(maskData.Name, MaskCells(Size));
			}
			return mask;
		}
	}

	// Changes whenever the mask does, so the renderer can rebuild the layout from it.
	internal int MaskVersion => HashCode.Combine(maskData.Name, EraseMask.Version);
	internal bool HasMask => EraseMask.HasPaint;

	// 0 where the wall grows, 1 where it's erased, bilinear between cell centers.
	internal float Erased(Vector2 world)
	{
		var field = EraseMask;
		if (!field.HasPaint)
			return 0;
		var cell = (world - Position) / Game.TileSize - Vector2.One * 0.5f;
		var floor = cell.FloorToPoint2();
		var t = cell - floor;
		float At(Point2 p)
		{
			p = p.Clamp(Point2.Zero, field.Size - Point2.One);
			return field.Pixels[p.Y * field.Size.X + p.X].A / 255f;
		}
		return float.Lerp(
			float.Lerp(At(floor), At(floor + Point2.UnitX), t.X),
			float.Lerp(At(floor + Point2.UnitY), At(floor + Point2.One), t.X), t.Y);
	}

	// Any slot erases; the eraser (slot < 0) grows the wall back.
	public void PaintMask(Vector2 worldCenter, float worldRadius, float softness, float strength, int slot)
		=> EraseMask.Paint(worldCenter - Position, worldRadius, softness, strength, Color.White, erase: slot < 0);

	public string SerializeMask() => (maskData = EraseMask.Serialize()).Name;

	// A resize that moves our top-left by `offset` pixels keeps the paint where it was in the world.
	public static string ReframeMask(string blob, Point2 newSize, Point2 offset)
		=> NeuralTextureActor.ReframeMask(blob, MaskCells(newSize), ((Vector2)offset / Game.TileSize).FloorToPoint2());

	protected ProceduralWall()
	{
		Sprite = null;
		CollidesWithSolids = false;
		Depth = 9;
	}

	public override bool CastsSpriteShadow => false;
	public override bool CastsReflection => false;

	public override void Render(Batcher batcher) { }
	public override bool OccludesEmissive => false;
	public override void RenderNormals(Batcher batcher) { }
	public override void RenderShadow(Batcher batcher, Color color) { }
	public override void RenderReflection(Batcher batcher, Color color) { }

	public override Rect GetVisualBounds() => new(Position, Size);
}
