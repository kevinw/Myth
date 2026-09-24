using Foster.Framework;

namespace Kit;

// One flag per pixel over Bounds plus its summed-area table, so "is anything set inside this rect" is four
// reads however big the rect. Set pixels, then Commit() before querying; pixels outside Bounds read as clear.
public sealed class SummedMask
{
	public RectInt Bounds { get; private set; }
	bool[] pixels = [];
	int[] sums = []; // (Width + 1) x (Height + 1): sums[y, x] counts the set pixels above and left of (x, y)

	public void Reset(RectInt bounds)
	{
		var count = bounds.Width * bounds.Height;
		if (pixels.Length < count)
			pixels = new bool[count];
		else
			Array.Clear(pixels, 0, count);
		var sumCount = (bounds.Width + 1) * (bounds.Height + 1);
		if (sums.Length < sumCount)
			sums = new int[sumCount];
		Bounds = bounds;
	}

	public bool this[Point2 pixel]
	{
		get => Bounds.Contains(pixel) && pixels[Index(pixel)];
		set
		{
			if (Bounds.Contains(pixel))
				pixels[Index(pixel)] = value;
		}
	}

	public void Fill(RectInt rect)
	{
		var area = Bounds.GetIntersection(rect);
		for (var y = area.Top; y < area.Bottom; y++)
			Array.Fill(pixels, true, Index(new(area.Left, y)), area.Width);
	}

	public void Commit()
	{
		var stride = Bounds.Width + 1;
		Array.Clear(sums, 0, stride);
		for (var y = 0; y < Bounds.Height; y++)
		{
			var row = 0;
			sums[(y + 1) * stride] = 0;
			for (var x = 0; x < Bounds.Width; x++)
			{
				if (pixels[y * Bounds.Width + x])
					row++;
				sums[(y + 1) * stride + x + 1] = sums[y * stride + x + 1] + row;
			}
		}
	}

	public int Count(RectInt rect)
	{
		var area = Bounds.GetIntersection(rect);
		if (area.Width <= 0 || area.Height <= 0)
			return 0;
		var stride = Bounds.Width + 1;
		int left = area.Left - Bounds.Left, right = area.Right - Bounds.Left;
		int top = (area.Top - Bounds.Top) * stride, bottom = (area.Bottom - Bounds.Top) * stride;
		return sums[bottom + right] - sums[bottom + left] - sums[top + right] + sums[top + left];
	}

	public bool Any(RectInt rect) => Count(rect) > 0;

	public bool SamePixels(SummedMask other)
		=> Bounds == other.Bounds && pixels.AsSpan(0, Bounds.Width * Bounds.Height).SequenceEqual(other.pixels.AsSpan(0, Bounds.Width * Bounds.Height));

	int Index(Point2 pixel) => (pixel.Y - Bounds.Y) * Bounds.Width + pixel.X - Bounds.X;
}
