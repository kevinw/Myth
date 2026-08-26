using System.Numerics;
using Foster.Framework;

namespace Myth;

// One collision-traced laser ray. The owner decides how it moves, grows, deals damage, emits
// particles, and contributes lights; this owns the shared geometry and rendering.
public sealed class LaserRay
{
	public float Range = 260;
	public float Thickness = 3;
	public float StartPadding = 5;
	public float EndPadding = 2;
	public float GlowThickness = 2;
	public Color Color = 0x5fcde4;
	public Color GlowColor = Color.White;
	public ContentRef<SurfaceContent> SurfaceId = new("");

	public Vector2 Origin { get; private set; }
	public Vector2 Direction { get; private set; } = Vector2.UnitX;
	public Vector2 End { get; private set; }
	public float Length => Vector2.Distance(Origin, End);
	public bool HitObstacle => Length < Range;

	public void Trace(Game game, Vector2 origin, Vector2 direction)
	{
		Origin = origin;
		Direction = direction.Normalized(Vector2.UnitX);
		var step = MathF.Max(1, Thickness);
		var distance = 0f;
		End = origin;
		while (distance < Range)
		{
			distance = MathF.Min(distance + step, Range);
			var position = origin + Direction * distance;
			if (game.OverlapsRoomCollision(SampleRect(position)))
				break;
			End = position;
		}
	}

	public Vector2 ScaledEnd(float scale) => Origin + (End - Origin) * scale;

	public Actor? FirstOverlap(Game game, Actor.Masks mask, float scale = 1)
	{
		var end = ScaledEnd(scale);
		var length = Vector2.Distance(Origin, end);
		var step = MathF.Max(1, Thickness);
		for (var distance = 0f; distance <= length; distance += step)
			if (game.OverlapsFirst(SampleRect(Origin + Direction * distance), mask) is { } actor)
				return actor;
		return null;
	}

	// localOrigin is Origin in the caller's current batcher space: Vector2.Zero for an actor whose
	// Position is the ray origin, or an eye offset for a ray attached to a rig joint.
	public void Render(Batcher batcher, Vector2 localOrigin, float scale, float alpha, float time)
	{
		var startOffset = Direction * StartPadding;
		var endOffset = (End + Direction * EndPadding - Origin) * scale;
		if (Vector2.DistanceSquared(startOffset, endOffset) <= 0.001f)
			return;

		var start = localOrigin + startOffset;
		var end = localOrigin + endOffset;
		batcher.Line(start, end, Thickness + GlowThickness, GlowColor * 0.75f * alpha);
		if (Assets.GetSurface(SurfaceId) is { } surface)
			RenderSurface(batcher, surface, start, end, startOffset, endOffset, alpha, time);
		else
			batcher.Line(start, end, Thickness, Color * alpha);
		batcher.Circle(end, Thickness * 0.5f, 12, Color * alpha);
	}

	void RenderSurface(Batcher batcher, Surface surface, Vector2 start, Vector2 end,
		Vector2 startOffset, Vector2 endOffset, float alpha, float time)
	{
		var side = new Vector2(-Direction.Y, Direction.X) * (Thickness * 0.5f);
		var a = start - side;
		var b = start + side;
		var c = end + side;
		var d = end - side;
		var worldStart = Origin + startOffset;
		var worldEnd = Origin + endOffset;
		Span<Surface.Vertex> triangles = stackalloc Surface.Vertex[]
		{
			new(a, worldStart - side), new(b, worldStart + side), new(c, worldEnd + side),
			new(a, worldStart - side), new(c, worldEnd + side), new(d, worldEnd - side)
		};
		surface.Render(batcher, triangles, time, alpha);
	}

	RectInt SampleRect(Vector2 position)
	{
		var size = Math.Max(1, (int)MathF.Ceiling(Thickness));
		return new RectInt(
			(int)MathF.Floor(position.X - size * 0.5f),
			(int)MathF.Floor(position.Y - size * 0.5f),
			size,
			size);
	}
}
