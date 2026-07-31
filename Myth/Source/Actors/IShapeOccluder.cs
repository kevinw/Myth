using System.Collections.Generic;
using System.Numerics;
using Foster.Framework;

namespace Myth;

// A solid shape an actor presents to the lighting system as a dynamic shadow caster -- the moving
// equivalent of a solid tile's static occluder. Occluders emit world-space edge segments, so any silhouette -- a
// rect, a circle, or a tile-traced outline -- plugs in the same way and matches its own art.
enum ShapeType
{
	Rect,
	Circle
}

readonly struct OccluderShape
{
	public readonly ShapeType Type;
	public readonly RectInt Rect;     // Type == Rect
	public readonly Vector2 Center;   // Type == Circle
	public readonly float Radius;     // Type == Circle

	OccluderShape(ShapeType type, RectInt rect, Vector2 center, float radius)
	{
		Type = type;
		Rect = rect;
		Center = center;
		Radius = radius;
	}

	public static OccluderShape FromRect(RectInt rect)
		=> new(ShapeType.Rect, rect, default, 0);

	public static OccluderShape FromCircle(Vector2 center, float radius)
		=> new(ShapeType.Circle, default, center, radius);

	// Append the shape's perimeter as world-space segments. A circle becomes an octagon; segments
	// are undirected (both point and sun shadow passes treat them so), so winding doesn't matter.
	public void AppendSegments(List<ShadowSegment> segments)
	{
		if (Type == ShapeType.Rect)
		{
			var tl = (Vector2)Rect.TopLeft;
			var tr = (Vector2)Rect.TopRight;
			var br = (Vector2)Rect.BottomRight;
			var bl = (Vector2)Rect.BottomLeft;
			segments.Add(new(tl, tr));
			segments.Add(new(tr, br));
			segments.Add(new(br, bl));
			segments.Add(new(bl, tl));
			return;
		}

		const int sides = 8;
		var center = Center;
		var radius = Radius;
		var previous = PointOnCircle(sides - 1);
		for (var i = 0; i < sides; i++)
		{
			var point = PointOnCircle(i);
			segments.Add(new(previous, point));
			previous = point;
		}

		Vector2 PointOnCircle(int i)
		{
			var angle = MathF.Tau * (i + 0.5f) / sides;
			return new Vector2(center.X + MathF.Cos(angle) * radius, center.Y + MathF.Sin(angle) * radius);
		}
	}
}

interface IShapeOccluder
{
	// Gate so an occluder can stop casting (e.g. a disabled platform) without leaving the actor list.
	bool CastsShadow { get; }

	// Append this occluder's silhouette as world-space segments.
	void AppendOccluderSegments(List<ShadowSegment> segments);
}
