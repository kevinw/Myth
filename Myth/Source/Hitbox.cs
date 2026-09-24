using System.Numerics;
using Foster.Framework;

namespace Myth;

/// Hitbox can test overlaps between Rectangles, Grids and per-pixel masks. A Pixels hitbox's
/// Rect is its mask's bounds.
public readonly struct Hitbox
{
	public enum Shapes
	{
		Rect,
		Grid,
		Pixels
	}

	public readonly Shapes Shape;
	private readonly RectInt rect;
	private readonly bool[,]? grid;
	private readonly PixelMask? pixels;

	public RectInt Rect => rect;

	public Hitbox()
	{
		Shape = Shapes.Rect;
		rect = new RectInt(0, 0, 0, 0);
		grid = null;
	}

	public Hitbox(in RectInt value)
	{
		Shape = Shapes.Rect;
		rect = value;
		grid = null;
	}

	public Hitbox(in bool[,] value)
	{
		Shape = Shapes.Grid;
		grid = value;
	}

	public Hitbox(PixelMask value)
	{
		Shape = Shapes.Pixels;
		rect = value.Bounds;
		pixels = value;
	}

	// Builds a hitbox from a sprite's visible (trimmed) pixels, expressed relative to the
	// resolved anchor origin so it lines up with the rendered sprite and the editor box.
	// `inset` shrinks the box on all sides for forgiving gameplay collisions.
	public static Hitbox FromSprite(Sprite? sprite, int inset = 0) {
		if (sprite == null) return HitboxForNullSprite();
		return FromSprite(sprite, sprite.ResolveOrigin(SpriteAnchor.Auto), inset);
	}

	public static Hitbox FromSprite(Sprite? sprite, SpriteAnchor anchor, int inset = 0) {
		if (sprite == null) return HitboxForNullSprite();
		return FromSprite(sprite, sprite.ResolveOrigin(anchor), inset);
	}

	static Hitbox HitboxForNullSprite() {
#if DEBUG
		Console.WriteLine("Error: Hitbox.FromSprite given null sprite");
#endif
		return new(new RectInt(0, 0, 0, 0));
	}

	public static Hitbox FromSprite(Sprite sprite, Vector2 origin, int inset = 0)
	{
		if (sprite.Frames.Count == 0)
			return new(new RectInt(0, 0, 0, 0));

		// Visible (trimmed) pixels, shifted so the anchor origin sits at (0,0). Floor the
		// top-left and ceil the bottom-right so the integer rect always contains the art.
		var sub = sprite.Frames[0].Subtexture;
		var topLeftF = -origin - sub.Frame.TopLeft;
		var topLeft = topLeftF.FloorToPoint2();
		var bottomRight = (topLeftF + sub.Source.Size).CeilingToPoint2();
		return new(RectInt.Between(topLeft, bottomRight).Inflate(-inset));
	}

	public static Hitbox FromSlice(Actor sprite, string name) {
		if (sprite.Sprite == null)
			return HitboxForNullSprite();
		return FromSlice(sprite.Sprite, name, sprite.Origin, sprite.Facing);
	}

	// Hitbox from a named slice's canvas-space bounds, made origin-relative and mirrored to
	// face `facing` (slices are authored one way, so an attack reaches the wrong side otherwise).
	public static Hitbox FromSlice(Sprite? sprite, string name, Vector2 origin, Signs facing)
	{
		if (sprite?.SliceByName(name) is not { } r)
			return HitboxForNullSprite();

		var left = r.X - origin.X;
		if (facing == Signs.Negative)
			left = -left - r.Width; // reflect the rect across the origin's X axis
		var top = r.Y - origin.Y;
		var topLeft = new Vector2(left, top).FloorToPoint2();
		var bottomRight = new Vector2(left + r.Width, top + r.Height).CeilingToPoint2();
		return new(RectInt.Between(topLeft, bottomRight));
	}

	public bool Overlaps(in RectInt rect) => Overlaps(Point2.Zero, new Hitbox(rect));

	// Whether a local pixel is inside a Rect or Pixels hitbox; Grids answer false.
	public bool Contains(Point2 pixel) => Shape switch
	{
		Shapes.Rect => rect.Contains(pixel),
		Shapes.Pixels => pixels![pixel],
		_ => false,
	};

	public bool Overlaps(in Hitbox other) => Overlaps(Point2.Zero, other);

	public bool Overlaps(in Point2 offset, in Hitbox other)
	{
		switch (Shape)
		{
			case Shapes.Rect:
				switch (other.Shape)
				{
					case Shapes.Rect: return RectToRect(rect + offset, other.rect);
					case Shapes.Grid: return RectToGrid(rect + offset, other.grid!);
					case Shapes.Pixels: return other.pixels!.Overlaps(rect + offset);
				}
				break;
			case Shapes.Grid:
				switch (other.Shape)
				{
					case Shapes.Rect: return RectToGrid(other.rect - offset, grid!);
					case Shapes.Grid: throw new NotImplementedException("Grid->Grid overlap not implemented!");
					case Shapes.Pixels: throw new NotImplementedException("Grid->Pixels overlap not implemented!");
				}
				break;
			case Shapes.Pixels:
				if (other.Shape == Shapes.Rect)
					return pixels!.Overlaps(other.rect - offset);
				throw new NotImplementedException($"Pixels->{other.Shape} overlap not implemented!");
		}

		throw new NotImplementedException();
	}

	private static bool RectToRect(in RectInt a, in RectInt b)
	{
		return a.Overlaps(b);
	}

	private static bool RectToGrid(in RectInt a, in bool[,] grid)
	{
		int left = Calc.Clamp((int)Math.Floor(a.Left / (float)Game.TileSize), 0, grid.GetLength(0));
		int right = Calc.Clamp((int)Math.Ceiling(a.Right / (float)Game.TileSize), 0, grid.GetLength(0));
		int top = Calc.Clamp((int)Math.Floor(a.Top / (float)Game.TileSize), 0, grid.GetLength(1));
		int bottom = Calc.Clamp((int)Math.Ceiling(a.Bottom / (float)Game.TileSize), 0, grid.GetLength(1));

		for (int x = left; x < right; x ++)
		for (int y = top; y < bottom; y ++)
			if (grid[x, y])
				return true;

		return false;
	}

	public void Render(Batcher batcher, Point2 offset, Color color)
	{
		batcher.PushMatrix(offset);

		if (Shape == Shapes.Rect)
		{
			batcher.RectLine(rect + offset, 1, color);
		}
		else if (Shape == Shapes.Pixels && pixels != null)
		{
			for (var y = rect.Top; y < rect.Bottom; y++)
				for (var x = rect.Left; x < rect.Right; x++)
					if (pixels[new Point2(x, y)])
						batcher.Rect(new RectInt(x, y, 1, 1), color);
		}
		else if (Shape == Shapes.Grid && grid != null)
		{
			for (int x = 0; x < grid.GetLength(0); x ++)
			for (int y = 0; y < grid.GetLength(1); y ++)
			{
				if (grid[x, y])
					batcher.RectLine(new RectInt(x, y, 1, 1) * Game.TileSize, 1, color);
			}
		}

		batcher.PopMatrix();
	}

}
