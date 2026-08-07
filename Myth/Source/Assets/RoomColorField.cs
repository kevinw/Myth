using System.Numerics;
using Foster.Framework;

namespace Myth;

public sealed class RoomColorField
{
	Color[] pixels;

	public Point2 Size { get; private set; }
	public ReadOnlySpan<Color> Pixels => pixels;
	public bool HasPaint { get; private set; }
	public bool Dirty { get; private set; }
	public int Version { get; private set; }
	public string? SourcePath { get; private set; }

	public RoomColorField(Point2 size)
	{
		Size = size;
		pixels = new Color[Math.Max(1, size.X * size.Y)];
	}

	public RoomColorField Clone()
	{
		var clone = new RoomColorField(Size);
		pixels.CopyTo(clone.pixels, 0);
		clone.HasPaint = HasPaint;
		return clone;
	}

	public void CopyFrom(RoomColorField source)
	{
		if (Size != source.Size)
		{
			Size = source.Size;
			pixels = new Color[source.pixels.Length];
		}
		source.pixels.CopyTo(pixels, 0);
		HasPaint = source.HasPaint;
		Dirty = source.Dirty;
		Version++;
	}

	public void Paint(Vector2 localCenter, float worldRadius, float softness, float strength, Color color, bool erase)
	{
		var center = localCenter / Game.TileSize - Vector2.One * 0.5f;
		var radius = MathF.Max(worldRadius / Game.TileSize, 0.5f);
		var falloff = MathF.Max(softness, 0.01f);
		var reach = radius * (1.0f + falloff * 2.0f);
		var min = (center - Vector2.One * reach).FloorToPoint2().Clamp(Point2.Zero, Size - Point2.One);
		var max = (center + Vector2.One * reach).CeilingToPoint2().Clamp(Point2.Zero, Size - Point2.One);
		var changed = false;

		for (var y = min.Y; y <= max.Y; y++)
			for (var x = min.X; x <= max.X; x++)
			{
				var distance = (new Vector2(x, y) - center).Length() / radius;
				if (distance >= 1.0f + falloff * 2.0f)
					continue;

				var skirt = MathF.Max(distance - (1.0f - falloff), 0.0f);
				var weight = Math.Clamp(strength * MathF.Exp(-skirt * skirt / (2.0f * falloff * falloff)), 0.0f, 1.0f);
				if (weight <= 0.001f)
					continue;

				var index = y * Size.X + x;
				var current = pixels[index].ToVector4();
				var next = erase
					? current * (1.0f - weight)
					: Vector4.Lerp(current, new Vector4(color.ToVector3(), 1.0f), weight);
				var painted = new Color(Vector4.Clamp(next, Vector4.Zero, Vector4.One));
				if (painted == pixels[index])
					continue;
				pixels[index] = painted;
				changed = true;
			}

		if (!changed)
			return;

		RefreshHasPaint();
		Dirty = true;
		Version++;
	}

	public void Clear()
	{
		if (!HasPaint)
			return;
		Array.Clear(pixels);
		HasPaint = false;
		Dirty = true;
		Version++;
	}

	public void Resize(Point2 size, Point2 sourceOffset)
	{
		if (size == Size && sourceOffset == Point2.Zero)
			return;

		var previous = pixels;
		var previousSize = Size;
		Size = size;
		pixels = new Color[Math.Max(1, size.X * size.Y)];
		for (var y = 0; y < size.Y; y++)
			for (var x = 0; x < size.X; x++)
			{
				var source = new Point2(x, y) + sourceOffset;
				if (source.X >= 0 && source.Y >= 0 && source.X < previousSize.X && source.Y < previousSize.Y)
					pixels[y * size.X + x] = previous[source.Y * previousSize.X + source.X];
			}

		RefreshHasPaint();
		Dirty = true;
		Version++;
	}

	public void Load(ReadOnlySpan<byte> encoded, Point2 expectedSize, string? sourcePath)
	{
		using var image = new Image(encoded.ToArray());
		Size = new(image.Width, image.Height);
		pixels = image.Data.ToArray();
		SourcePath = sourcePath;
		Dirty = false;
		RefreshHasPaint();
		Version++;
		if (Size != expectedSize)
			Resize(expectedSize, Point2.Zero);
	}

	public string Serialize()
	{
		if (!HasPaint)
			return "";
		using var image = new Image(Size.X, Size.Y, pixels);
		return Convert.ToBase64String(image.WritePng());
	}

	public void Deserialize(string? encoded, Point2 expectedSize)
	{
		if (string.IsNullOrEmpty(encoded))
		{
			Size = expectedSize;
			pixels = new Color[Math.Max(1, expectedSize.X * expectedSize.Y)];
			HasPaint = false;
			Dirty = true;
			Version++;
			return;
		}

		Load(Convert.FromBase64String(encoded), expectedSize, SourcePath);
		Dirty = true;
	}

	public byte[] WritePng()
	{
		using var image = new Image(Size.X, Size.Y, pixels);
		return image.WritePng();
	}

	public void MarkSaved(string? sourcePath)
	{
		SourcePath = sourcePath;
		Dirty = false;
	}

	void RefreshHasPaint()
	{
		HasPaint = false;
		for (var i = 0; i < pixels.Length; i++)
			if (pixels[i].A != 0)
			{
				HasPaint = true;
				return;
			}
	}
}
