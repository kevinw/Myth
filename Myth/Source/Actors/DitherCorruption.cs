using System.Numerics;
using Foster.Framework;

namespace Myth;

public sealed class DitherCorruption : Actor
{
	const int Family = 7;
	static float Radius => 56f;
	static float EdgeWidth => Game.TileSize * 2f;
	static float VariantRateMin => 4f;
	static float VariantRateMax => 18f;
	static float RateNoiseScale => 3f;

	public Actor? Owner;

	internal bool Active => Visible && Owner?.Visible == true;

	public DitherCorruption()
	{
		Sprite = null;
		CollidesWithSolids = false;
		Depth = -999;
	}

	internal void RenderTileField(Batcher batcher, float time)
	{
		if (Owner is not { } owner)
			return;

		var center = owner.GetVisualBounds().Center;
		var extent = Vector2.One * Radius;
		var first = ((center - extent) / Game.TileSize).FloorToPoint2();
		var last = ((center + extent) / Game.TileSize).CeilingToPoint2();
		for (var y = first.Y; y < last.Y; y++)
			for (var x = first.X; x < last.X; x++)
			{
				var tile = new Point2(x, y);
				var tileCenter = (Vector2)(tile * Game.TileSize + Point2.One * (Game.TileSize / 2));
				var distance = (tileCenter - center).Length();
				var coverage = Math.Clamp((Radius - distance) / EdgeWidth, 0f, 1f);
				var spatialHash = Hash(tile, 0);
				if ((spatialHash & 0xffff) / 65535f > coverage)
					continue;

				var radiusAmount = Math.Clamp(distance / Radius, 0f, 1f);
				var rateNoise = Math.Clamp(RateNoise(tile) + (1f - radiusAmount) * .2f, 0f, 1f);
				var rate = Calc.Lerp(VariantRateMin, VariantRateMax, rateNoise);
				var phase = Hash01(tile, -1) * 4f;
				var frame = (int)MathF.Floor(time * rate + phase);
				var variant = (byte)Hash(tile, frame);
				batcher.Rect((Vector2)(tile * Game.TileSize), Vector2.One * Game.TileSize,
					new Color(variant, Family << 5, 0, 255));
			}
	}

	static float RateNoise(Point2 tile)
	{
		var position = (Vector2)tile / RateNoiseScale;
		var cell = position.FloorToPoint2();
		var amount = position - (Vector2)cell;
		var top = Calc.Lerp(Hash01(cell, 1), Hash01(cell + Point2.Right, 1), amount.X);
		var bottom = Calc.Lerp(Hash01(cell + Point2.Down, 1), Hash01(cell + Point2.One, 1), amount.X);
		return Calc.Lerp(top, bottom, amount.Y);
	}

	static float Hash01(Point2 tile, int salt) => (Hash(tile, salt) & 0xffff) / 65535f;

	static uint Hash(Point2 tile, int frame)
	{
		unchecked
		{
			var hash = (uint)tile.X * 0x8da6b343u;
			hash ^= (uint)tile.Y * 0xd8163841u;
			hash ^= (uint)frame * 0xcb1ab31fu;
			hash ^= hash >> 16;
			hash *= 0x7feb352du;
			hash ^= hash >> 15;
			hash *= 0x846ca68bu;
			return hash ^ hash >> 16;
		}
	}
}
