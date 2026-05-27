using System.Numerics;
using Foster.Framework;

namespace TinyLink;

public class Saw : Actor
{
	public float Speed = 110;
	public float Gravity = 360;
	public float LandingProbeDistance = 1;
	public float SurfaceProbeDistance = 10;
	public float CornerProbeDistance = 14;
	public float MaxSurfaceSnapDistance = 1;
	public bool DebugMovement;

	private Vector2 remainder;
	private Vector2 towardSurface = Vector2.UnitY;
	private Vector2 exitDirection;
	private bool attached;
	private bool exitingRoom;

	public Saw()
	{
		Hitbox = new(new RectInt(0, 0, 8, 8));
		Mask = Masks.None;
		Sprite = Assets.GetSprite("saw");
		if (Sprite != null)
			Console.WriteLine($"saw origin {Sprite.Origin}");
		Depth = -5;
		CollidesWithSolids = false;
		//DebugMovement = true;
	}

	public override void Added()
	{
		attached = AttachToSurface(CornerProbeDistance);
		if (!attached && !exitingRoom)
			StartAirborne();
	}

	public override void Update()
	{
		if (exitingRoom)
		{
			MoveExitingRoom();
			return;
		}

		if (attached)
		{
			Velocity = Vector2.Zero;
			CollidesWithSolids = false;
			base.Update();
			FollowSurface();
		}
		else
		{
			CollidesWithSolids = false;
			Velocity.X = (float)Facing * Speed;
			Velocity.Y += Gravity * Time.Delta;
			var motion = Velocity * Time.Delta;
			var velocity = Velocity;
			Velocity = Vector2.Zero;
			base.Update();
			Velocity = velocity;
			MoveAirborne(motion);
			TryAttachAfterAirMove();
		}

		if (Timer > 2.5f && Time.BetweenInterval(0.05f))
			Visible = !Visible;

		if (Timer > 3.0f)
			Game.Destroy(this);
	}

	private void StartAirborne()
	{
		attached = false;
		remainder = Vector2.Zero;
		Velocity = new Vector2((float)Facing * Speed, Velocity.Y);
		CollidesWithSolids = false;
	}

	private void MoveAirborne(Vector2 motion)
	{
		remainder += motion;
		var move = (Point2)remainder;
		remainder -= (Vector2)move;

		while (move.X != 0 && !attached)
		{
			var sign = Math.Sign(move.X);
			if (!TryMoveAirborneStep(new Point2(sign, 0)))
			{
				Velocity.X = 0;
				break;
			}

			move.X -= sign;
		}

		while (move.Y != 0 && !attached)
		{
			var sign = Math.Sign(move.Y);
			if (!TryMoveAirborneStep(new Point2(0, sign)))
			{
				Velocity.Y = 0;
				break;
			}

			move.Y -= sign;
		}
	}

	private bool TryMoveAirborneStep(Point2 step)
	{
		var bounds = Bounds();
		var hit = Game.OverlapsSolid(this, bounds + step);
		if (!hit)
		{
			Position += step;
			return true;
		}

		var didAttach = TryAttachToSurface((Vector2)step, CornerProbeDistance);
		return false;
	}

	private void TryAttachAfterAirMove()
	{
		if (attached)
			return;

		if (TryAttachToSurface(Vector2.UnitY, LandingProbeDistance))
			return;

		TryAttachToSurface(new Vector2((float)Facing, 0), LandingProbeDistance);
	}

	private bool TryAttachToSurface(Vector2 direction, float distance)
	{
		if (direction.LengthSquared() <= 0.0001f)
			return false;

		towardSurface = NormalizeOrDefault(direction, Vector2.UnitY);
		if (!AttachToSurface(distance))
		{
			return false;
		}

		attached = true;
		Velocity = Vector2.Zero;
		CollidesWithSolids = false;
		remainder = Vector2.Zero;
		return true;
	}

	private void FollowSurface()
	{
		AttachToSurface(CornerProbeDistance, MaxSurfaceSnapDistance);

		var desired = Tangent() * Speed * Time.Delta;
		remainder += desired;
		var move = (Point2)remainder;
		remainder -= (Vector2)move;

		var steps = Math.Max(Math.Abs(move.X), Math.Abs(move.Y));
		for (var i = 0; i < steps; i++)
		{
			var step = new Point2(
				i < Math.Abs(move.X) ? Math.Sign(move.X) : 0,
				i < Math.Abs(move.Y) ? Math.Sign(move.Y) : 0);

			if (!TryMoveStep(step))
				break;
		}
	}

	private Vector2 Tangent()
		=> new Vector2(towardSurface.Y, -towardSurface.X) * (float)Facing;

	private bool TryMoveStep(Point2 step)
	{
		if (step == Point2.Zero)
			return true;

		var bounds = Bounds();
		var nextBounds = bounds + step;
		if (OutsideRoom(nextBounds))
		{
			BeginRoomExit((Vector2)step, "bounds");
			return false;
		}

		var hit = Game.OverlapsSolid(this, nextBounds);
		if (hit)
		{
			if (TryFindSurfaceDebug(bounds, (Vector2)step, 1, out var surfaceHit))
			{
				if (IsRoomEdgeSurface(surfaceHit))
				{
					BeginRoomExit((Vector2)step, surfaceHit);
					return false;
				}

				towardSurface = -surfaceHit.Normal;
			}
			else
			{
			}
			return false;
		}

		Position += step;
		if (AttachToSurface(SurfaceProbeDistance, MaxSurfaceSnapDistance))
			return true;

		var previousTowardSurface = towardSurface;
		towardSurface = -NormalizeOrDefault((Vector2)step, towardSurface);
		if (TryMoveAroundOutsideCorner(previousTowardSurface))
			return true;

		if (AttachToSurface(CornerProbeDistance, MaxSurfaceSnapDistance))
			return true;

		Position -= step;
		towardSurface = previousTowardSurface;
		remainder = Vector2.Zero;
		AttachToSurface(CornerProbeDistance, MaxSurfaceSnapDistance);
		return false;
	}

	private bool AttachToSurface(float distance)
		=> AttachToSurface(distance, float.PositiveInfinity);

	private bool AttachToSurface(float distance, float maxSnap)
	{
		var bounds = Bounds();
		if (!TryFindSurfaceDebug(bounds, towardSurface, distance, out var hit))
		{
			return false;
		}

		if (IsRoomEdgeSurface(hit))
		{
			BeginRoomExit(Tangent(), hit);
			return false;
		}

		towardSurface = -hit.Normal;
		var rawSnap = MathF.Max(0, hit.Distance - 1);
		var snap = MathF.Min(rawSnap, maxSnap);
		if (snap > 0)
		{
			Position += Round(towardSurface * snap);
		}
		return true;
	}

	private RectInt Bounds()
		=> Hitbox.Rect + Position;

	private bool OutsideRoom(in RectInt bounds)
	{
		var roomBounds = Game.Bounds;
		return roomBounds.Width > 0 &&
			(bounds.Left < roomBounds.Left ||
				bounds.Right > roomBounds.Right ||
				bounds.Top < roomBounds.Top ||
				bounds.Bottom > roomBounds.Bottom);
	}

	private bool IsRoomEdgeSurface(in SurfaceHit hit)
	{
		var roomBounds = Game.Bounds;
		if (roomBounds.Width <= 0 || roomBounds.Height <= 0)
			return false;

		return hit.Normal.X < -0.001f && hit.Point.X <= roomBounds.Left ||
			hit.Normal.X > 0.001f && hit.Point.X >= roomBounds.Right ||
			hit.Normal.Y < -0.001f && hit.Point.Y <= roomBounds.Top ||
			hit.Normal.Y > 0.001f && hit.Point.Y >= roomBounds.Bottom;
	}

	private void BeginRoomExit(Vector2 direction, in SurfaceHit hit)
	{
		BeginRoomExit(direction, "surface");
	}

	private void BeginRoomExit(Vector2 direction, string reason)
	{
		if (exitingRoom)
			return;

		exitDirection = NormalizeOrDefault(direction, Tangent());
		exitingRoom = true;
		attached = false;
		Velocity = exitDirection * Speed;
		CollidesWithSolids = false;
		remainder = Vector2.Zero;
	}

	private void MoveExitingRoom()
	{
		Velocity = exitDirection * Speed;
		remainder += Velocity * Time.Delta;
		var move = (Point2)remainder;
		remainder -= (Vector2)move;

		var steps = Math.Max(Math.Abs(move.X), Math.Abs(move.Y));
		for (var i = 0; i < steps; i ++)
		{
			var step = new Point2(
				i < Math.Abs(move.X) ? Math.Sign(move.X) : 0,
				i < Math.Abs(move.Y) ? Math.Sign(move.Y) : 0);

			Position += step;
		}

		if (!Game.Bounds.Overlaps(GetVisualBounds()))
		{
			Game.Destroy(this);
		}
	}

	private bool TryMoveAroundOutsideCorner(Vector2 previousTowardSurface)
	{
		var cornerStep = Round(previousTowardSurface);
		if (cornerStep == Point2.Zero)
			return false;

		var bounds = Bounds();
		var cornerBounds = bounds + cornerStep;
		var blocked = Game.OverlapsSolid(this, cornerBounds);
		if (blocked)
			return false;

		Position += cornerStep;
		if (AttachToSurface(CornerProbeDistance, MaxSurfaceSnapDistance))
			return true;

		Position -= cornerStep;
		return false;
	}

	private bool TryFindSurfaceDebug(in RectInt bounds, Vector2 direction, float distance, out SurfaceHit hit)
	{
		return Game.TryFindSurface(this, bounds, direction, distance, out hit);
	}

	private Vector2 LocalCenter()
		=> Center(Bounds()) - (Vector2)Position;

	private Vector2 ToLocal(Vector2 point)
		=> point - (Vector2)Position;

	private static Vector2 Center(RectInt bounds)
		=> (Vector2)bounds.Center;

	private static Vector2 NormalizeOrDefault(Vector2 value, Vector2 fallback)
		=> value.LengthSquared() <= 0.0001f ? fallback : Vector2.Normalize(value);

	private static Point2 Round(Vector2 value)
		=> new((int)MathF.Round(value.X), (int)MathF.Round(value.Y));


}
