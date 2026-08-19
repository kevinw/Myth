using System.Numerics;
using Foster.Framework;

namespace Myth;

[GameActor(Anchor = SpriteAnchor.TopLeft, BoundsW = Game.TileSize, BoundsH = Game.TileSize,
	LivePositionEditable = true, LiveResizable = true)]
public class Jumpthru : Actor, IResizable
{
	public static readonly Point2 DefaultSize = new(Game.TileSize, Game.TileSize);
	public static readonly ResizeAxes ResizableAxes = ResizeAxes.X;

	// Only the width is editable; height stays one tile (see ResizableAxes). The collision is the
	// thin top strip the player lands on, spanning the full width.
	Point2 size = DefaultSize;
	public Point2 Size
	{
		get => size;
		set
		{
			size = value;
			Hitbox = new(new RectInt(0, 0, size.X, Game.TileSize / 4));
		}
	}

	public Jumpthru()
	{
		Mask = Masks.Jumpthru;
		Depth = 5;
		Play("idle");
	}

	public override void Added()
	{
		base.Added();
		// Size is applied from the spec before Added (editor resizes write it), so size the hitbox here.
		Hitbox = new(new RectInt(0, 0, Size.X, Game.TileSize / 4));
	}

	public override void Render(Batcher batcher) => RenderTiled(batcher, normals: false);
	public override void RenderNormals(Batcher batcher) => RenderTiled(batcher, normals: true);
	public override void RenderReflection(Batcher batcher, Color color) => RenderTiled(batcher, normals: true, color: color);

	public override bool CastsReflection => true;
	public override string? SurfaceSubstance => "Wood";

	// Repeat the one-tile sprite across the authored width, clipping the final tile so widths that
	// aren't a whole number of tiles still tile seamlessly (the atlas-friendly stand-in for a
	// wrapping sampler -- a Repeat sampler would bleed into neighbouring atlas sprites).
	void RenderTiled(Batcher batcher, bool normals, Color? color = null)
	{
		if (Sprite is not { Frames.Count: > 0 } sprite)
			return;

		var index = AnimationFrameIndex;
		if (index < 0 || index >= sprite.Frames.Count)
			index = 0;

		var frame = sprite.Frames[index];
		var tile = normals ? frame.NormalSubtexture : frame.Subtexture;
		if (tile.Texture == null)
			return;

		var step = Math.Max(1, (int)tile.Width);
		var height = tile.Height;
		for (var x = 0; x < Size.X; x += step)
		{
			var width = Math.Min(step, Size.X - x);
			batcher.Image(tile, new Rect(0, 0, width, height), new Vector2(x, 0), Vector2.Zero, Vector2.One, 0, color ?? Color.White);
		}
	}
}
