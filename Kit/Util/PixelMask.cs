using System.Numerics;
using Foster.Framework;

namespace Kit;

// One flag per pixel over Bounds, in the owner's local pixel coordinates.
public sealed class PixelMask(RectInt bounds)
{
	public RectInt Bounds { get; private set; } = bounds;
	bool[] pixels = new bool[bounds.Width * bounds.Height];

	// Aim an existing mask at new bounds and clear it, keeping the buffer whenever it is already big
	// enough. A shape rebuilt every frame -- a CurveRibbon whose Grow is animating -- would otherwise
	// allocate its entire mask per frame; reused, it settles at the largest it has ever needed.
	public void Reset(RectInt bounds)
	{
		var needed = bounds.Width * bounds.Height;
		if (pixels.Length < needed)
			pixels = new bool[needed];
		else
			Array.Clear(pixels, 0, needed);
		Bounds = bounds;
	}

	public bool this[Point2 pixel] => Bounds.Contains(pixel) && pixels[Index(pixel.X, pixel.Y)];

	public bool Overlaps(in RectInt rect)
	{
		var area = Bounds.GetIntersection(rect);
		for (var y = area.Top; y < area.Bottom; y++)
			for (var x = area.Left; x < area.Right; x++)
				if (pixels[Index(x, y)])
					return true;
		return false;
	}

	// Sets every pixel whose center lies inside the triangle (either winding).
	public void FillTriangle(Vector2 a, Vector2 b, Vector2 c)
	{
		var winding = MathF.Sign(Cross(b - a, c - a));
		if (winding == 0)
			return;
		var area = Bounds.GetIntersection(RectInt.Between(Vector2.Min(a, Vector2.Min(b, c)).FloorToPoint2(),
			Vector2.Max(a, Vector2.Max(b, c)).CeilingToPoint2()));
		for (var y = area.Top; y < area.Bottom; y++)
			for (var x = area.Left; x < area.Right; x++)
			{
				var p = new Vector2(x + 0.5f, y + 0.5f);
				if (Cross(b - a, p - a) * winding >= 0 && Cross(c - b, p - b) * winding >= 0 && Cross(a - c, p - c) * winding >= 0)
					pixels[Index(x, y)] = true;
			}
	}

	int Index(int x, int y) => (y - Bounds.Y) * Bounds.Width + x - Bounds.X;
	static float Cross(Vector2 u, Vector2 v) => u.X * v.Y - u.Y * v.X;
}
