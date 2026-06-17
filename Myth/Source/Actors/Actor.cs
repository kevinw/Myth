using System.Diagnostics;
using System.Numerics;
using Foster.Framework;

namespace Myth;

public class Actor
{
	public readonly record struct TransitionKey(string ActorType, string SpriteName, string From, string To);

	public static readonly Dictionary<TransitionKey, int> TransitionCounts = [];
	public static bool TransitionsEnabled = true;

	[Flags]
	public enum Masks
	{
		None = 0,
		Solid = 1 << 0,
		Jumpthru = 1 << 1,
		Player = 1 << 2,
		Enemy = 1 << 3,
		Hazard = 1 << 4,
	}

	[NoSerialize] public Game Game = null!;
	[NoSerialize] public Point2 Position;
	[NoSerialize] public Vector2 Velocity;
	[NoSerialize] public Hitbox Hitbox;
	[NoSerialize] public Masks Mask = Masks.None;
	public Signs Facing = Signs.Positive;
	[NoSerialize] public Vector2 Squish = Vector2.One;
	[NoSerialize] public Vector2 Shift = Vector2.Zero;
	[NoSerialize] public int Depth = 0;
	[NoSerialize] public float Timer = 0;
	[NoSerialize] public bool Visible = true;
	[NoSerialize] public float IFrameTime = 0.50f;
	[NoSerialize] public bool CollidesWithSolids = true;
	[NoSerialize] public string? ActorId;
	[NoSerialize] internal string? SessionDestroyKey;
	internal bool KeepOnRoomTransition;
	public virtual bool CastsSpriteShadow => sprite != null;
	public virtual bool CastsReflection => sprite != null;
	public virtual string HitSoundType => "flesh";

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

	Vector2 remainder;
	Sprite? sprite;
	string? animationName;
	float animationTime = 0;
	bool animationLooping = true;
	bool targetAnimationLooping = true;
	float hitCooldown = 0;

	/// Plays an Animation from our current Sprite, if it exists
	public void Play(string name, bool looping = true, bool restart = false)
	{
		if (sprite != null && sprite.GetAnimation(name) is { } && (restart || animationName != name))
		{
			// If already in a transition to this target, let it play out
			if (!restart && animationName != null && animationName.Contains("->"))
			{
				var target = animationName.Split("->")[1];
				if (target == name)
					return;
			}

			if (!restart && animationName != null)
			{
				var baseFrom = animationName;
				if (baseFrom.Contains("->"))
					baseFrom = baseFrom.Split("->")[1];

				if (baseFrom != name)
					TransitionCounts[new(GetType().Name, sprite.Name, baseFrom, name)] =
						TransitionCounts.TryGetValue(new(GetType().Name, sprite.Name, baseFrom, name), out var c) ? c + 1 : 1;
			}

			// Check for a pre-generated transition animation
			if (TransitionsEnabled && !restart && animationName != null && animationName != name)
			{
				var transitionName = $"{animationName}->{name}";
				// If we're already in a transition, resolve the base name
				if (animationName.Contains("->"))
					transitionName = $"{animationName.Split("->")[1]}->{name}";

				if (sprite.GetAnimation(transitionName) is { })
				{
					// Console.WriteLine($"[Transition] {GetType().Name} \"{sprite.Name}\" playing transition: {transitionName}");
					animationName = transitionName;
					animationTime = 0;
					animationLooping = false;
					targetAnimationLooping = looping;
					return;
				}
			}

			animationName = name;
			animationTime = 0;
		}

		animationLooping = looping;
	}

	/// Checks if an animation is playing
	public bool IsPlaying(string name)
		=> Animation.Name == name;

	public bool Verbose = false;

	/// Checks if an animation is finished playing.
	public bool IsFinishedPlaying(string? name = null) {
		if (Verbose) {
			Console.WriteLine($"IsFinishedPlaying: animationTime={animationTime} Animation.Duration={Animation.Duration} animationLooping={animationLooping} name={name}");
		}
		return !animationLooping && animationTime >= Animation.Duration && (name == null || IsPlaying(name));
	}

	readonly Dictionary<string, float> audioEventDebounceTimes = [];
	public void AudioEventDebounced(string name, float waitSeconds) {
		var now = Time.SecondsF;
		if (audioEventDebounceTimes.TryGetValue(name, out var lastTime) && now - lastTime < waitSeconds)
			return;
		audioEventDebounceTimes[name] = now;
		Game.PlayAudioEvent(name, "", "", Position);
	}

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
			var wasGrounded = CollidesWithSolids && Grounded();
			if (!MovePixel(Point2.UnitX * sign))
			{
				if (TryStepUp(sign))
				{
					move.X -= sign;
					continue;
				}

				OnCollideX();
				break;
			}
			else
			{
				move.X -= sign;
				if (wasGrounded)
					TryStepDown();
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

	private bool TryStepUp(int sign)
	{
		if (!CollidesWithSolids || !Grounded())
			return false;

		var step = new Point2(sign, -1);
		if (OverlapsAny(Point2.Up, Masks.Solid) || OverlapsAny(step, Masks.Solid))
			return false;

		Position += step;
		return true;
	}

	private bool TryStepDown()
	{
		if (!CollidesWithSolids || Grounded() || OverlapsAny(Point2.Down, Masks.Solid))
			return false;

		Position += Point2.Down;
		return true;
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
			if (actor.Mask == Masks.Enemy)
			{
				if (this is Player)
				{
					Game.NotifyEnemyHitByPlayer(actor);
					var hitSoundType = actor.HitSoundType;
					if (!string.IsNullOrEmpty(hitSoundType))
						AudioEvent("player/sword_hit", "hit_destination", hitSoundType);
				}
			}
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

		// If a transition animation finished, land on the target animation
		if (animationName != null && animationName.Contains("->") && IsFinishedPlaying())
		{
			var target = animationName.Split("->")[1];
			animationName = target;
			animationTime = 0;
			animationLooping = targetAnimationLooping;
			if (Verbose) {
				Console.WriteLine($"Landed on target animation: animationName={animationName} animationTime={animationTime} Animation.Duration={Animation.Duration} animationLooping={animationLooping}");
			}
		}
	}

	enum SubtextureType
	{
		Color,
		Normals
	}

	void RenderSpriteSubtexture(Batcher batcher, SubtextureType subtextureType) =>
		RenderSpriteSubtexture(batcher, subtextureType, Color.White);

	protected void RenderSpriteSubtexture(Batcher batcher, Color color) =>
		RenderSpriteSubtexture(batcher, SubtextureType.Color, color);

	void RenderSpriteSubtexture(Batcher batcher, SubtextureType subtextureType, Color color)
	{
		if (hitCooldown > 0 && Time.BetweenInterval(0.05f))
			return;

		if (sprite == null || sprite.Frames.Count <= 0)
			return;

		var animation = Animation;
		var frame = sprite.GetFrameAt(animation, animationTime, animationLooping);

		Subtexture subtexture;
		switch (subtextureType)
		{
			case SubtextureType.Color:
				subtexture = frame.Subtexture;
				break;
			case SubtextureType.Normals:
				subtexture = frame.NormalSubtexture;
				break;
			default:
				return;
		}

		batcher.PushMatrix(Matrix3x2.CreateScale(Facing * Squish.X, Squish.Y));
		batcher.Image(subtexture, Shift, sprite.Origin, Vector2.One, 0, color);
		batcher.PopMatrix();
	}

	public virtual void RenderEmissive(Batcher batcher) { }

	public virtual void Render(Batcher batcher) => RenderSpriteSubtexture(batcher, SubtextureType.Color);
	public virtual void RenderNormals(Batcher batcher) => RenderSpriteSubtexture(batcher, SubtextureType.Normals);
	public virtual void RenderShadow(Batcher batcher, Color color) => RenderSpriteSubtexture(batcher, SubtextureType.Color, color);
	public virtual void RenderReflection(Batcher batcher, Color color) => RenderSpriteSubtexture(batcher, SubtextureType.Color, color);

	public virtual void ForEachLight(Action<Vector2, float, float, float, Color> action) { }


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

	/// <summary>
	/// Dumps transition usage data collected since startup to the given TextWriter.
	/// </summary>
	public static void DumpTransitionUsage(TextWriter writer)
	{
		if (TransitionCounts.Count == 0)
		{
			writer.WriteLine("No transitions recorded.");
			return;
		}

		// Group by sprite then actor type for a clean report
		var bySprite = TransitionCounts
			.GroupBy(kv => kv.Key.SpriteName)
			.OrderBy(g => g.Key);

		foreach (var spriteGroup in bySprite)
		{
			writer.WriteLine($"sprite: {spriteGroup.Key}");

			var byActor = spriteGroup
				.GroupBy(kv => kv.Key.ActorType)
				.OrderBy(g => g.Key);

			foreach (var actorGroup in byActor)
			{
				writer.WriteLine($"  {actorGroup.Key}:");
				foreach (var kv in actorGroup.OrderByDescending(kv => kv.Value))
				{
					writer.WriteLine($"    {kv.Key.From} -> {kv.Key.To}: {kv.Value}");
				}
			}
		}
	}

	protected void StopAndReleaseSound(ref SoundInstance? sound, bool immediate = false)
	{
		if (sound == null) return;

		sound.Stop(immediate: immediate);
		sound.Dispose();
		sound = null;
	}
}
