using System.Numerics;
using Foster.Framework;

namespace Myth;

public interface IPointPath
{
	Point2[] Points { get; set; }
}

public abstract class PointPathActor : Actor, IPointPath
{
	public Point2[] Points { get; set; } = [Point2.Zero, new Point2(Game.TileSize * 8, 0)];

	public Vector2 WorldPoint(int index) => Position + Points[index];

	protected float PathLength()
	{
		var length = 0f;
		for (var i = 1; i < Points.Length; i++)
			length += Vector2.Distance(Points[i - 1], Points[i]);
		return length;
	}

	protected Vector2 PointAtDistance(float distance)
	{
		if (Points.Length == 0)
			return Position;

		for (var i = 1; i < Points.Length; i++)
		{
			var from = (Vector2)Points[i - 1];
			var to = (Vector2)Points[i];
			var length = Vector2.Distance(from, to);
			if (length <= 0)
				continue;
			if (distance <= length)
				return Position + Vector2.Lerp(from, to, distance / length);
			distance -= length;
		}

		return WorldPoint(Points.Length - 1);
	}
}
