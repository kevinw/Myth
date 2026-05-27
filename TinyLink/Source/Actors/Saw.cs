using System.Numerics;
using Foster.Framework;

namespace TinyLink;

public class Saw : Actor
{
	public float Speed = 90;
	public float SurfaceProbeDistance = 10;
	public float CornerProbeDistance = 14;

	private Vector2 remainder;
	private Vector2 towardSurface = Vector2.UnitY;

	public Saw()
	{
		Hitbox = new(new RectInt(0, 0, 8, 8));
		Mask = Masks.None;
		Sprite = Assets.GetSprite("saw");
		if (Sprite != null)
			Console.WriteLine($"saw origin {Sprite.Origin}");
		Depth = -5;
		CollidesWithSolids = false;
	}

	public override void Update()
	{
		base.Update();

		FollowSurface();

		if (Timer > 2.5f && Time.BetweenInterval(0.05f))
			Visible = !Visible;

		if (Timer > 3.0f)
			Game.Destroy(this);
	}

	//public override void RenderEmissive(Batcher batcher)
	//=> batcher.Circle(Vector2.Zero, 2, 12, Color.Red);

	private void FollowSurface()
	{
		AttachToSurface(CornerProbeDistance);

		remainder += Tangent() * Speed * Time.Delta;
		var move = (Point2)remainder;
		remainder -= (Vector2)move;

		var steps = Math.Max(Math.Abs(move.X), Math.Abs(move.Y));
		for (var i = 0; i < steps; i++)
		{
			var step = new Point2(
				i < Math.Abs(move.X) ? Math.Sign(move.X) : 0,
				i < Math.Abs(move.Y) ? Math.Sign(move.Y) : 0);

			TryMoveStep(step);
		}
	}

	private Vector2 Tangent()
		=> new Vector2(towardSurface.Y, -towardSurface.X) * (float)Facing;

	private bool TryMoveStep(Point2 step)
	{
		if (step == Point2.Zero)
			return true;

		var bounds = Bounds();
		if (Game.OverlapsSolid(this, bounds + step))
		{
			if (Game.TryFindSurface(this, bounds, (Vector2)step, 1, out var hit))
				towardSurface = -hit.Normal;
			return false;
		}

		Position += step;
		if (AttachToSurface(SurfaceProbeDistance))
			return true;

		towardSurface = -NormalizeOrDefault((Vector2)step, towardSurface);
		AttachToSurface(CornerProbeDistance);
		return true;
	}

	private bool AttachToSurface(float distance)
	{
		if (!Game.TryFindSurface(this, Bounds(), towardSurface, distance, out var hit))
			return false;

		towardSurface = -hit.Normal;
		var snap = MathF.Max(0, hit.Distance - 1);
		if (snap > 0)
			Position += Round(towardSurface * snap);
		return true;
	}

	private RectInt Bounds()
		=> Hitbox.Rect + Position;

	private static Vector2 NormalizeOrDefault(Vector2 value, Vector2 fallback)
		=> value.LengthSquared() <= 0.0001f ? fallback : Vector2.Normalize(value);

	private static Point2 Round(Vector2 value)
		=> new((int)MathF.Round(value.X), (int)MathF.Round(value.Y));

}
