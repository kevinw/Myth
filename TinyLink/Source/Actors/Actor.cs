using System.Diagnostics;
using System.Numerics;
using Foster.Framework;

namespace TinyLink;

public class Actor
{
	[Flags]
	public enum Masks
	{
		None	  = 0,
		Solid	 = 1 << 0,
		Jumpthru  = 1 << 1,
		Player	= 1 << 2,
		Enemy	 = 1 << 3,
		Hazard	= 1 << 4,
	}

	[NoSerialize] public Game Game = null!;
	[NoSerialize] public Point2 Position;
	[NoSerialize] public Vector2 Velocity;
	[NoSerialize] public Hitbox Hitbox;
	[NoSerialize] public Masks Mask = Masks.None;
	[NoSerialize] public Signs Facing = Signs.Positive;
	[NoSerialize] public Vector2 Squish = Vector2.One;
	[NoSerialize] public Vector2 Shift = Vector2.Zero;
	[NoSerialize] public int Depth = 0;
	[NoSerialize] public float Timer = 0;
	[NoSerialize] public bool Visible = true;
	[NoSerialize] public float IFrameTime = 0.50f;
	[NoSerialize] public bool CollidesWithSolids = true;
	public bool KeepOnRoomTransition;
	public virtual bool CastsSpriteShadow => sprite != null;
	public virtual bool CastsReflection => sprite != null;

	[NoSerialize]
	public Time Time => Game.Time;

	[NoSerialize]
	public Sprite? Sprite
	{
		get => sprite;
		set
		{
			if (sprite != value)
			{
				sprite = value;
				animationName = value?.Animations.Count > 0 ? value.Animations[0].Name : null;
				animationTime = 0;
			}
		}
	}

	[NoSerialize]
	public Sprite.Animation Animation
	{
		get
		{
			if (sprite == null || sprite.Frames.Count <= 0 || sprite.Animations.Count <= 0)
				return new();

			return sprite.GetAnimation(animationName) ?? sprite.Animations[0];
		}
	}
	[NoSerialize]
	public float AnimationTime => animationTime;
	[NoSerialize]
	public int AnimationFrameIndex => sprite?.GetFrameIndexAt(Animation, animationTime, animationLooping) ?? -1;

	private Vector2 remainder;
	private Sprite? sprite;
	private string? animationName;
	private float animationTime = 0;
	private bool animationLooping = true;
	private float hitCooldown = 0;

	/// <summary>
	/// Plays an Animation from our current Sprite, if it exists
	/// </summary>
	public void Play(string name, bool looping = true, bool restart = false)
	{
		if (sprite != null && sprite.GetAnimation(name) is {} && (restart || animationName != name))
		{
			animationName = name;
			animationTime = 0;
		}

		animationLooping = looping;
	}

	/// <summary>
	/// Checks if an animation is playing
	/// </summary>
	public bool IsPlaying(string name)
		=> Animation.Name == name;

	/// <summary>
	/// Checks if an animation is finished playing.
	/// </summary>
	public bool IsFinishedPlaying(string? name = null)
		=> !animationLooping && animationTime >= Animation.Duration && (name == null || IsPlaying(name));

	public void AudioEvent(string name)
		=> Game.PlayAudioEvent(name, "", "", Position);

	public void AudioEvent(string name, string parameterName, string parameterLabel)
		=> Game.PlayAudioEvent(name, parameterName, parameterLabel, Position);

	/// <summary>
	/// Checks to see if we overlap any actors of the given mask
	/// </summary>
	public bool OverlapsAny(Masks mask)
		=> OverlapsAny(Point2.Zero, mask);

	/// <summary>
	/// Checks to see if we overlap any actors of the given mask
	/// </summary>
	public bool OverlapsAny(Point2 offset, Masks mask)
	{
		if (mask.Has(Masks.Solid) && Game.OverlapsRoomCollision(this, offset))
			return true;

		foreach (var other in Game.Actors)
		{
			if (other is Grid && other.Mask.Has(Masks.Solid))
				continue;

			if (other != this && other.Mask.Has(mask) && OverlapsAny(offset, other))
				return true;
		}

		return false;
	}

	/// <summary>
	/// Checks to see if we overlap the given actor
	/// </summary>
	public bool OverlapsAny(Point2 offset, Actor other)
		=> Hitbox.Overlaps(Position + offset - other.Position, other.Hitbox);

	/// <summary>
	/// Finds the first overlapping actor of the given mask
	/// </summary>
	public Actor? OverlapsFirst(Point2 offset, Masks mask)
	{
		foreach (var other in Game.Actors)
		{
			if (other is Grid && other.Mask.Has(Masks.Solid))
				continue;

			if (other != this && other.Mask.Has(mask) && OverlapsAny(offset, other))
				return other;
		}

		return null;
	}

	/// <summary>
	/// Finds the first overlapping actor of the given mask
	/// </summary>
	public Actor? OverlapsFirst(Masks mask)
		=> OverlapsFirst(Point2.Zero, mask);

	/// <summary>
	/// Checks if we're on the Ground
	/// </summary>
	public bool Grounded()
	{
		if (Game.OverlapsRoomCollision(this, Point2.Down))
			return true;

		foreach (var other in Game.Actors)
		{
			if (other is Grid && other.Mask.Has(Masks.Solid))
				continue;

			if (other == this || !other.Mask.Has(Masks.Solid | Masks.Jumpthru))
				continue;

			if (!OverlapsAny(Point2.Down, other))
				continue;

			if (other.Mask.Has(Masks.Jumpthru) && OverlapsAny(Point2.Zero, other))
				continue;

			return true;
		}

		return false;
	}

	/// <summary>
	/// Moves a single Pixel
	/// </summary>
	public bool MovePixel(Point2 sign)
	{
		sign.X = Math.Sign(sign.X);
		sign.Y = Math.Sign(sign.Y);

		if (CollidesWithSolids)
		{
			if (OverlapsAny(sign, Masks.Solid))
				return false;

			if (sign.Y > 0 && Grounded())
				return false;
		}

		Position += sign;
		return true;
	}

	/// <summary>
	/// Moves a floating value, which increments an accumulator and only moves in pixel values
	/// </summary>
	public void Move(Vector2 value)
	{
		remainder += value;
		Point2 move = (Point2)remainder;
		remainder -= move;

		while (move.X != 0)
		{
			var sign = Math.Sign(move.X);
			if (!MovePixel(Point2.UnitX * sign))
			{
				OnCollideX();
				break;
			}
			else
			{
				move.X -= sign;
			}
		}

		while (move.Y != 0)
		{
			var sign = Math.Sign(move.Y);
			if (!MovePixel(Point2.UnitY * sign))
			{
				OnCollideY();
				break;
			}
			else
			{
				move.Y -= sign;
			}
		}
	}

	/// <summary>
	/// Stops all velocity
	/// </summary>
	public void Stop()
	{
		Velocity = Vector2.Zero;
		remainder = Vector2.Zero;
	}

	/// <summary>
	/// Stops X Velocity
	/// </summary>
	public void StopX()
	{
		Velocity.X = 0;
		remainder.X = 0;
	}

	/// <summary>
	/// Stops Y Velocity
	/// </summary>
	public void StopY()
	{
		Velocity.Y = 0;
		remainder.Y = 0;
	}

	/// <summary>
	/// Tries to Hit another Actor.
	/// What a "Hit" is, is up to the Actors
	/// </summary>
	public bool Hit(Actor actor)
	{
		if (actor.hitCooldown <= 0)
		{
			actor.hitCooldown = actor.IFrameTime;
			actor.OnWasHit(this);
			OnPerformHit(actor);
			return true;
		}

		return false;
	}

	/// <summary>
	/// Called when the Actor is added to the Game
	/// </summary>
	public virtual void Added() { }

	/// <summary>
	/// What to do when we collide with a wall while moving horizontally
	/// </summary>
	public virtual void OnCollideX() => StopX();

	/// <summary>
	/// What to do when we collide with a wall while moving vertically
	/// </summary>
	public virtual void OnCollideY() => StopY();

	/// <summary>
	/// Called when we were hit by another actor
	/// </summary>
	public virtual void OnWasHit(Actor by) { }

	/// <summary>
	/// Called when we perform a hit on another actor
	/// </summary>
	public virtual void OnPerformHit(Actor hitting) { }

	/// <summary>
	/// Called ones per frame, updates our Sprite and Timer
	/// </summary>
	public virtual void Update()
	{
		if (Velocity != Vector2.Zero)
			Move(Velocity * Time.Delta);
		Squish = Calc.Approach(Squish, Vector2.One, Time.Delta * 4.0f);
		hitCooldown = Calc.Approach(hitCooldown, 0, Time.Delta);
		animationTime += Time.Delta;
		Timer += Time.Delta;
	}

	/// <summary>
	/// Draws the current Sprite
	/// </summary>
	public virtual void Render(Batcher batcher)
	{
		if (hitCooldown > 0 && Time.BetweenInterval(0.05f))
			return;

		if (sprite != null && sprite.Frames.Count > 0)
		{
			var animation = Animation;
			var frame = sprite.GetFrameAt(animation, animationTime, animationLooping);
			batcher.PushMatrix(Matrix3x2.CreateScale(Facing * Squish.X, Squish.Y));
			batcher.Image(frame.Subtexture, Shift, sprite.Origin, Vector2.One, 0, Color.White);
			batcher.PopMatrix();
		}
	}

	public virtual void RenderNormals(Batcher batcher)
	{
		if (hitCooldown > 0 && Time.BetweenInterval(0.05f))
			return;

		if (sprite != null && sprite.Frames.Count > 0)
		{
			var animation = Animation;
			var frame = sprite.GetFrameAt(animation, animationTime, animationLooping);
			batcher.PushMatrix(Matrix3x2.CreateScale(Facing * Squish.X, Squish.Y));
			batcher.Image(frame.NormalSubtexture, Shift, sprite.Origin, Vector2.One, 0, Color.White);
			batcher.PopMatrix();
		}
	}

	public virtual void RenderShadow(Batcher batcher, Color color)
	{
		if (hitCooldown > 0 && Time.BetweenInterval(0.05f))
			return;

		if (sprite != null && sprite.Frames.Count > 0)
		{
			var animation = Animation;
			var frame = sprite.GetFrameAt(animation, animationTime, animationLooping);
			batcher.PushMatrix(Matrix3x2.CreateScale(Facing * Squish.X, Squish.Y));
			batcher.Image(frame.Subtexture, Shift, sprite.Origin, Vector2.One, 0, color);
			batcher.PopMatrix();
		}
	}

	public virtual void RenderReflection(Batcher batcher, Color color)
	{
		if (hitCooldown > 0 && Time.BetweenInterval(0.05f))
			return;

		if (sprite != null && sprite.Frames.Count > 0)
		{
			var animation = Animation;
			var frame = sprite.GetFrameAt(animation, animationTime, animationLooping);
			batcher.PushMatrix(Matrix3x2.CreateScale(Facing * Squish.X, Squish.Y));
			batcher.Image(frame.Subtexture, Shift, sprite.Origin, Vector2.One, 0, color);
			batcher.PopMatrix();
		}
	}

	public virtual Rect GetVisualBounds()
	{
		if (sprite == null || sprite.Frames.Count <= 0)
			return new Rect(Position.X, Position.Y, 0, 0);

		var animation = Animation;
		var frame = sprite.GetFrameAt(animation, animationTime, animationLooping);
		var localLeft = Shift.X - sprite.Origin.X;
		var localTop = Shift.Y - sprite.Origin.Y;
		var localRight = localLeft + frame.Subtexture.Width;
		var localBottom = localTop + frame.Subtexture.Height;
		var scaleX = (int)Facing * Squish.X;
		var scaleY = Squish.Y;

		var x0 = Position.X + localLeft * scaleX;
		var x1 = Position.X + localRight * scaleX;
		var y0 = Position.Y + localTop * scaleY;
		var y1 = Position.Y + localBottom * scaleY;

		var left = MathF.Min(x0, x1);
		var top = MathF.Min(y0, y1);
		return new Rect(left, top, MathF.Max(x0, x1) - left, MathF.Max(y0, y1) - top);
	}

	/// <summary>
	/// Called when the Actor was destroyed
	/// </summary>
	public virtual void Destroyed() { }
}
