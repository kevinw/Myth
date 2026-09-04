using System.Diagnostics;
using System.Numerics;
using System.Text.Json.Nodes;
using Foster.Framework;

namespace Myth;

public enum ActorInversion
{
	Both,
	InvertedOnly,
	UninvertedOnly,
}

public class Actor
{
	internal static bool TransitionsEnabled = true;

	[Flags]
	public enum Masks
	{
		None = 0,
		Solid = 1 << 0,
		Jumpthru = 1 << 1,
		Player = 1 << 2,
		Enemy = 1 << 3,
		Hazard = 1 << 4,
		Destructible = 1 << 5
	}

	[Flags]
	public enum EditorFlags {
		None = 0,
		Locked = 1, // no live edit
	}

	public Signs Facing = Signs.Positive;
	public ActorInversion Invert = ActorInversion.Both;
	public EditorFlags editorFlags = EditorFlags.None;
	[InspectorGroup("Attachment"), ActorRef] public string AttachedToActorId = "";
	public Point2 AttachmentOffset;

	// The resolved draw/flip/hitbox origin for this actor's sprite + anchor, in sprite-local
	// pixels. Render, GetVisualBounds and Hitbox.FromSprite all key off this so they agree.
	public Vector2 Origin => sprite?.ResolveOrigin(Anchor) ?? Vector2.Zero;

	internal Vector2 Squish = Vector2.One;
	internal Vector2 Shift = Vector2.Zero;
	// Horizontal lean of the sprite as x-per-y: 0.3 slides the top edge a third of the sprite's
	// height to the right, with the base planted. Set it and it eases back to zero like Squish.
	internal float Skew = 0;
	internal int Depth = 0;
	internal float Timer = 0;
	internal float IFrameTime = 0.50f;

	internal Game Game = null!;
	internal Room? Room;
	internal Point2 Position;
	internal Vector2 Velocity;
	internal Hitbox Hitbox;
	internal Masks Mask = Masks.None;
	internal SpriteAnchor Anchor = SpriteAnchor.Auto;


	internal bool Visible = true;
	internal bool CollidesWithSolids = true;
	internal bool AddedOnce;
	internal bool FlipY;
	internal bool KeepOnRoomTransition;
	[NoSerialize] internal Actor? AttachmentTarget;

	[NoSerialize] public string? ActorId;
#if EDITOR
	[NoSerialize] internal JsonObject? AuthoredSpec;
#endif
	internal ActorTag Tags;
	[NoSerialize] internal string? SessionDestroyKey;
	public virtual bool CastsSpriteShadow => sprite != null;
	public virtual bool CastsReflection => sprite != null;
	public virtual bool LightingDithered => !Mask.Has(Masks.Player | Masks.Enemy);
	public virtual string HitSoundType => "flesh";
	// Substance landing/footstep audio reports for anything standing on this actor (see
	// Game.GetSubstanceBelow). Null means the actor isn't a walkable surface of its own.
	public virtual string? SurfaceSubstance => null;

	internal virtual int DamageValue => 1;
	internal virtual bool ContactDamageEnabled => true;

	public Time Time => Game.Time;
	internal virtual List<Actor> CollisionActors => Game.Actors;
	internal virtual List<Actor> WindActors => Game.Actors;

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

	public Sprite.Animation Animation
	{
		get
		{
			if (sprite == null || sprite.Frames.Count <= 0 || sprite.Animations.Count <= 0)
				return new();

			return sprite.GetAnimation(animationName) ?? sprite.Animations[0];
		}
	}
	public float AnimationTime => animationTime;
	public int AnimationFrameIndex => sprite?.GetFrameIndexAt(Animation, animationTime, animationLooping) ?? -1;
	protected virtual float AnimationRate => 1;
	protected void SetAnimationTime(float time) => animationTime = time;

	Vector2 remainder;
	internal Vector2 SubPixelRemainder => remainder;
	Sprite? sprite;
	string? animationName;
	float animationTime = 0;
	bool animationLooping = true;
	bool targetAnimationLooping = true;
	float hitCooldown = 0;

	// Convention: an actor's sprite defaults to one named after its class (case-insensitive),
	// so Mosquito uses mosquito.ase automatically. Assigning Sprite in a subclass ctor overrides
	// this and marks a deliberate exception (e.g. the blob-family enemies reusing "blob").
	public Actor()
	{
		Sprite = Assets.GetSprite(GetType().Name, logMissing: false);
	}

	public AnimState PlayOnce(string name, bool restart = false)
	{
		return Play(name, looping: false, restart: restart);
	}

	/// Plays an Animation from our current Sprite, if it exists
	public AnimState Play(string name = "", bool looping = true, bool restart = false)
	{
		if (name == "")
			name = sprite?.Animations.FirstOrDefault().Name ?? "";

		if (sprite != null && sprite.GetAnimation(name) is { } && (restart || animationName != name))
		{
			// If already in a transition to this target, let it play out
			if (!restart && animationName != null && animationName.Contains("->"))
			{
				var target = animationName.Split("->")[1];
				if (target == name)
					return AnimState.Playing;
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
					return AnimState.Playing;
				}
			}

			animationName = name;
			animationTime = 0;
		}

		animationLooping = looping;

		return IsFinishedPlaying(name) ? AnimState.Finished : AnimState.Playing;
	}

	public enum AnimState { Playing, Finished }

	/// Checks if an animation is playing
	public bool IsPlaying(string name) => Animation.Name == name;

	/// Checks if an animation is finished playing.
	public bool IsFinishedPlaying(string? name = null)
	{
		return !animationLooping && animationTime >= Animation.Duration && (name == null || IsPlaying(name));
	}

	readonly Dictionary<string, float> audioEventDebounceTimes = [];
	public void AudioEventDebounced(string name, float waitSeconds)
	{
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

	public void AudioEvent(string name, string parameterName, float parameterFloat)
		=> Game.PlayAudioEvent(name, parameterName, parameterFloat, Position);

	protected int AcquireParticleEffect(string name)
		=> Game.Particles?.AcquireEffect(name) ?? -1;

	protected void ReleaseParticleEffect(ref int slot)
	{
		Game.Particles?.ReleaseEffect(slot);
		slot = -1;
	}

	/// Checks to see if we overlap any actors of the given mask
	public bool OverlapsAny(Masks mask) => OverlapsAny(Point2.Zero, mask);

	/// Checks to see if we overlap any actors of the given mask
	public bool OverlapsAny(Point2 offset, Masks mask)
	{
		if (mask.Has(Masks.Solid) &&
			(Game.OverlapsEnemyRoomBoundary(this, offset) || Game.OverlapsRoomCollision(this, offset)))
			return true;

		return OverlapsFirst(offset, mask) != null;
	}

	/// Checks to see if we overlap the given actor
	public bool OverlapsAny(Point2 offset, Actor other) => Hitbox.Overlaps(Position + offset - other.Position, other.Hitbox);

	/// Finds the first overlapping actor of the given mask
	public Actor? OverlapsFirst(Masks mask) => OverlapsFirst(Point2.Zero, mask);

	/// Finds the first overlapping actor of the given mask
	public Actor? OverlapsFirst(Point2 offset, Masks mask)
	{
		var actors = (mask & ~(Masks.Solid | Masks.Jumpthru)) == 0 ? CollisionActors : Game.Actors;
		foreach (var other in actors)
		{
			if (other is Grid && other.Mask.Has(Masks.Solid))
				continue;

			// An actor being destroyed this frame no longer blocks: its removal is deferred to the
			// next frame (see Game.RemoveDestroyedActors), but it's already logically gone.
			if (Game.destroying.Contains(other))
				continue;

			if (other != this && other.Mask.Has(mask) && OverlapsAny(offset, other))
				return other;
		}

		return null;
	}

	/// Checks if we're on the Ground
	public bool Grounded()
	{
		if (Game.OverlapsRoomCollision(this, Point2.Down))
			return true;

		foreach (var other in CollisionActors)
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

	/// Moves a single Pixel
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
		Game.ActorMoved(this, sign);
		return true;
	}

	// Move by an exact whole-pixel delta, collision-aware. Used by solids (moving platforms,
	// conveyors) to push and carry actors. Critically this uses MovePixel rather than Move so it
	// does NOT touch the sub-pixel `remainder` accumulator — that accumulator is also fed by the
	// actor's own velocity (e.g. a player pressing into the solid while wall-sliding), and routing
	// the push through it lets a pending fraction cancel the move, leaving the actor stuck 1px
	// inside the solid (false grounding/vibration).
	public void MovePixels(Point2 step)
	{
		for (var n = Math.Abs(step.X); n > 0; n--)
			MovePixel(new Point2(Math.Sign(step.X), 0));
		for (var n = Math.Abs(step.Y); n > 0; n--)
			MovePixel(new Point2(0, Math.Sign(step.Y)));
	}

	// What a moving solid will push or carry: everything but level geometry and other solids.
	internal bool IsCarryable(Actor actor) => actor != this && actor is not Grid && !actor.Mask.Has(Masks.Solid);

	// A single step-up/step-down is a (±1, ∓1) diagonal covering √2 px along the slope, but the
	// X loop only debits 1 px of horizontal budget for it. Charging the √2-1 shortfall keeps the
	// actor's along-slope speed equal to its flat-ground speed instead of √2× too fast.
	const float Sqrt2 = 1.4142135f;

	/// Moves a floating value, which increments an accumulator and only moves in pixel values
	public void Move(Vector2 value)
	{
		remainder += value;
		Point2 move = (Point2)remainder;
		remainder -= move;

		var xdir = Math.Sign(move.X);
		var stepDebt = 0f; // sub-pixel horizontal budget owed by diagonal steps this frame

		while (move.X != 0)
		{
			var sign = Math.Sign(move.X);
			var wasGrounded = CollidesWithSolids && Grounded();
			if (!MovePixel(Point2.UnitX * sign))
			{
				if (TryStepUp(sign))
					move.X -= sign;
				else
				{
					OnCollideX();
					break;
				}
				stepDebt += Sqrt2 - 1;
			}
			else
			{
				move.X -= sign;
				if (wasGrounded && TryStepDown())
					stepDebt += Sqrt2 - 1;
			}

			// Charge whole pixels of accrued diagonal cost against the remaining budget.
			while (stepDebt >= 1 && move.X != 0)
			{
				stepDebt -= 1;
				move.X -= Math.Sign(move.X);
			}
		}

		// Carry the leftover sub-pixel diagonal cost so slope speed stays accurate over frames.
		if (stepDebt != 0 && xdir != 0)
			remainder.X -= xdir * stepDebt;

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
		Game.ActorMoved(this, step);
		return true;
	}

	private bool TryStepDown()
	{
		if (!CollidesWithSolids || Grounded() || OverlapsAny(Point2.Down, Masks.Solid))
			return false;

		Position += Point2.Down;
		Game.ActorMoved(this, Point2.Down);
		return true;
	}

	/// Stops all velocity
	public void Stop()
	{
		Velocity = Vector2.Zero;
		remainder = Vector2.Zero;
	}

	protected static int UnstickMaxRadius => Game.TileSize * 2;

	// If the current hitbox is embedded in solid, snap to the nearest free offset,
	// biased toward preferredDir. Returns false only when no nearby position is free.
	protected bool TryUnstick(Vector2 preferredDir)
	{
		if (!OverlapsAny(Masks.Solid))
			return true;

		if (preferredDir.LengthSquared() > 0.0001f)
			preferredDir = Vector2.Normalize(preferredDir);

		for (var r = 1; r <= UnstickMaxRadius; r++)
		{
			var bestScore = float.MaxValue;
			var best = Point2.Zero;
			var found = false;
			for (var oy = -r; oy <= r; oy++)
				for (var ox = -r; ox <= r; ox++)
				{
					if (Math.Max(Math.Abs(ox), Math.Abs(oy)) != r)
						continue;
					var off = new Point2(ox, oy);
					if (OverlapsAny(off, Masks.Solid))
						continue;

					var dist = MathF.Sqrt(ox * ox + oy * oy);
					var align = Vector2.Dot(Vector2.Normalize(new Vector2(ox, oy)), preferredDir);
					var score = dist - align;
					if (score < bestScore)
					{
						bestScore = score;
						best = off;
						found = true;
					}
				}

			if (found)
			{
				Position += best;
				Game.ActorMoved(this, best);
				return true;
			}
		}

		return false;
	}

	/// Stops X Velocity
	public void StopX()
	{
		Velocity.X = 0;
		remainder.X = 0;
	}

	/// Stops Y Velocity
	public void StopY()
	{
		Velocity.Y = 0;
		remainder.Y = 0;
	}

	/// Grants invincibility frames (damage immunity + the hit blink) for `duration`
	/// seconds, defaulting to IFrameTime. Same cooldown Hit() sets, so it gates the same
	/// damage checks — use it for hurt sources that bypass Hit(), like a respawn.
	protected void GrantIFrames(float? duration = null) => hitCooldown = duration ?? IFrameTime;

	/// Tries to Hit another Actor.
	/// What a "Hit" is, is up to the Actors
	public bool Hit(Actor actor, Hitbox attackBox)
	{
		if (actor.hitCooldown > 0)
			return false;

		actor.hitCooldown = actor.IFrameTime;
		if (actor.Mask == Masks.Enemy && this is Player) {
			Game.NotifyEnemyHitByPlayer(actor);
			var hitSoundType = actor.HitSoundType;
			if (!string.IsNullOrEmpty(hitSoundType))
				AudioEvent("player/sword_hit", "hit_destination", hitSoundType);
		}
		actor.OnWasHit(new HitInfo{By=this, AttackBox=attackBox});
		OnPerformHit(actor);
		return true;
	}

	/// Called when the Actor is added to the Game
	public virtual void Added()
	{
		// Console.WriteLine($"Actor added: {this}");
		Play();
	}

	internal void ApplyAttachmentMovement(Point2 delta)
	{
		Position += delta;
		OnAttachmentMoved(delta);
		Game.ActorMoved(this, delta);
	}

	// Lets simulated children respond to their anchor moving without treating it as their own
	// velocity. Most actors need only the Position change above.
	internal virtual void OnAttachmentMoved(Point2 delta) { }

	/// What to do when we collide with a wall while moving horizontally
	public virtual void OnCollideX() => StopX();

	/// What to do when we collide with a wall while moving vertically
	public virtual void OnCollideY() => StopY();

	internal struct HitInfo {
		internal Actor By;
		internal Hitbox AttackBox;
	}

	/// Called when we were hit by another actor
	internal virtual void OnWasHit(HitInfo hitInfo) { }

	/// Called when we perform a hit on another actor
	public virtual void OnPerformHit(Actor hitting) { }

	public virtual bool BlocksAttack(Actor attacker, in RectInt attackPath) => false;
	internal virtual bool OverlapsCombatBounds(in RectInt bounds) => Hitbox.Overlaps(bounds - Position);
	internal virtual void RenderCollisionDebug(Batcher batcher) { }

	/// Called ones per frame, updates our Sprite and Timer
	public virtual void Update()
	{
		if (Velocity != Vector2.Zero)
			Move(Velocity * Time.Delta);
		Squish = Calc.Approach(Squish, Vector2.One, Time.Delta * 4.0f);
		Skew = Calc.Approach(Skew, 0, Time.Delta * 4.0f);
		hitCooldown = Calc.Approach(hitCooldown, 0, Time.Delta);

		// Snapshot before advancing so we can tell which frames we cross this Update (the
		// playhead can step over short frames whole when Time.Delta is large).
		var prevFrame = AnimationFrameIndex;
		var prevAnim = animationName;

		animationTime += Time.Delta * AnimationRate;
		Timer += Time.Delta;

		// If a transition animation finished, land on the target animation
		if (animationName != null && animationName.Contains("->") && IsFinishedPlaying())
		{
			var target = animationName.Split("->")[1];
			animationName = target;
			animationTime = 0;
			animationLooping = targetAnimationLooping;
		}

		UpdateEnteredFrames(prevFrame, prevAnim);
	}

	// The contiguous range of frames newly entered during the last Update's time advance
	// (inclusive, absolute indices into Frames; framesEnteredFrom < 0 means none). Drives
	// AnimationEntered / AnimationTraversed.
	int framesEnteredFrom = -1;
	int framesEnteredTo = -1;

	void UpdateEnteredFrames(int prevFrame, string? prevAnim)
	{
		framesEnteredFrom = framesEnteredTo = -1;
		if (sprite == null)
			return;

		var cur = AnimationFrameIndex;
		if (cur < 0)
			return;

		if (prevAnim != animationName || prevFrame < 0)
		{
			// Fresh animation (switched, transition-landed, or first play): everything from its
			// start up to the current frame counts as newly entered this Update.
			framesEnteredFrom = Animation.FrameStart;
			framesEnteredTo = cur;
		}
		else if (cur > prevFrame)
		{
			framesEnteredFrom = prevFrame + 1;
			framesEnteredTo = cur;
		}
		else if (cur < prevFrame)
		{
			// Wrapped past the end of a looping animation; treat the new loop's frames up to
			// cur as entered (a tag in the wrapped-off tail is an exotic case we don't track).
			framesEnteredFrom = Animation.FrameStart;
			framesEnteredTo = cur;
		}
		// cur == prevFrame: still on the same frame, nothing newly entered.
	}

	bool AnyFrameTagged(int from, int to, string userData)
	{
		if (from < 0 || sprite == null)
			return false;
		for (var i = from; i <= to && i < sprite.Frames.Count; i++)
			if (sprite.Frames[i].UserData == userData)
				return true;
		return false;
	}

	// Edge-triggered: true only on the Update playback crosses INTO a frame tagged `userData`,
	// firing exactly once per entry even when a large Time.Delta skips intervening frames. Use
	// for one-shots (a hit, a sound, a screen shake) — no manual latch needed.
	public bool AnimationEntered(string userData) => AnyFrameTagged(framesEnteredFrom, framesEnteredTo, userData);

	// Level + skip-safe: true while the playhead is on (or this Update passed through) a frame
	// tagged `userData`, so it stays true for the tagged frame's whole duration. Use for active
	// windows like an attack hitbox.
	public bool AnimationTraversed(string userData)
		=> AnimationEntered(userData)
		|| (sprite is { } s && AnimationFrameIndex is var cur && cur >= 0 && cur < s.Frames.Count && s.Frames[cur].UserData == userData);

	public enum SubtextureType
	{
		Color,
		Normals,
		Emissive
	}

	protected void RenderSpriteSubtexture(Batcher batcher, SubtextureType subtextureType) => RenderSpriteSubtexture(batcher, subtextureType, Color.White);
	protected void RenderSpriteSubtexture(Batcher batcher, Color color) => RenderSpriteSubtexture(batcher, SubtextureType.Color, color);

	protected Sprite.Frame? CurrentSpriteFrame => sprite?.GetFrameAt(Animation, animationTime, animationLooping);

	protected void RenderSpriteSubtexture(Batcher batcher, SubtextureType subtextureType, Color color)
	{
		if (SpriteBlinkHidden)
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
			case SubtextureType.Emissive:
				subtexture = frame.EmissiveSubtexture;
				break;
			default:
				return;
		}

		// Scale first, then shear about the sprite's base: the shear sits outside the Facing
		// flip so a lean stays world-space (a left-facing actor still leans right), and every
		// pass -- color, normals, emissive, shadow, reflection -- comes through here together.
		var transform = Matrix3x2.CreateScale(Facing * Squish.X, (FlipY ? -1 : 1) * Squish.Y);
		if (Skew != 0)
			transform *= SpriteSkewMatrix(frame);
		batcher.PushMatrix(transform);
		batcher.Image(subtexture, Shift, Origin, Vector2.One, 0, color);
		batcher.PopMatrix();
	}

	// Sprite-local y of the bottom edge, the line the lean pivots around.
	float SpriteBaseY(Sprite.Frame frame) => Shift.Y - Origin.Y + frame.Subtexture.Height;

	// x' = x + Skew * (baseY - y): zero displacement at the base, growing toward the top.
	protected Matrix3x2 SpriteSkewMatrix(Sprite.Frame frame)
		=> new(1, 0, -Skew, 1, Skew * SpriteBaseY(frame) * Squish.Y, 0);

	protected bool SpriteBlinkHidden => hitCooldown > 0 && Time.BetweenInterval(0.05f);

	public virtual void RenderEmissive(Batcher batcher) => RenderSpriteSubtexture(batcher, SubtextureType.Emissive);
	public virtual void RenderEmissiveOccluder(Batcher batcher) => Render(batcher);
	public virtual void Render(Batcher batcher) => RenderSpriteSubtexture(batcher, SubtextureType.Color);
	public virtual bool HasWorldDistortion => false;
	public virtual void RenderWorldDistortion(Batcher batcher) { }
	protected static Color EncodeWorldDistortion(Vector2 direction, float strength)
	{
		var encoded = direction * 0.5f + Vector2.One * 0.5f;
		return new(encoded.X * strength, encoded.Y * strength, 0, strength);
	}

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
		var origin = Origin;
		var localLeft = Shift.X - origin.X;
		var localTop = Shift.Y - origin.Y;
		var localRight = localLeft + frame.Subtexture.Width;
		var localBottom = localTop + frame.Subtexture.Height;
		var scaleX = (int)Facing * Squish.X;
		var scaleY = (FlipY ? -1 : 1) * Squish.Y;

		var x0 = Position.X + localLeft * scaleX;
		var x1 = Position.X + localRight * scaleX;
		var y0 = Position.Y + localTop * scaleY;
		var y1 = Position.Y + localBottom * scaleY;

		var left = MathF.Min(x0, x1);
		var top = MathF.Min(y0, y1);
		var bounds = new Rect(left, top, MathF.Max(x0, x1) - left, MathF.Max(y0, y1) - top);
		// A lean carries the top edge sideways; widen both ways so culling keeps it whole.
		return Skew == 0 ? bounds : bounds.InflateX(MathF.Abs(Skew) * bounds.Height);
	}

	/// Called when the Actor was destroyed
	public virtual void Destroyed() { }

	static protected void StopAndReleaseSound(ref SoundInstance? sound, bool immediate = false)
	{
		if (sound == null) return;

		sound.Stop(immediate: immediate);
		sound.Dispose();
		sound = null;
	}
}
