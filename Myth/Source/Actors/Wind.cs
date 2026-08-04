using System.Numerics;
using Foster.Framework;

namespace Myth;

[Flags]
public enum WindTargets
{
	None = 0,
	Player = 1 << 0,
	Bud = 1 << 1,
	Enemies = 1 << 2,
	Projectiles = 1 << 3,
	Ropes = 1 << 4,
}

[GameActor(Sprite = "Editor/particles", Anchor = SpriteAnchor.TopLeft, Tags = ActorTag.System, LivePositionEditable = true)]
public class Wind : Actor, IResizable
{
	internal struct PawnInfluence
	{
		public float PositiveXSpeedLimit;
		public float NegativeXSpeedLimit;
		public bool PositiveXUnlimited;
		public bool NegativeXUnlimited;

		public readonly float HorizontalSpeedLimit(float fallback, float velocityX)
			=> velocityX < 0
				? NegativeXUnlimited ? float.PositiveInfinity : MathF.Max(fallback, NegativeXSpeedLimit)
				: PositiveXUnlimited ? float.PositiveInfinity : MathF.Max(fallback, PositiveXSpeedLimit);
	}

	public static readonly Point2 DefaultSize = new(Game.TileSize * 8, Game.TileSize * 6);

	public Point2 Size { get; set; } = DefaultSize;
	public Point2 Direction = Point2.Up;
	public float Acceleration = 620f;
	public float MaxSpeed = 150f;
	public WindTargets Targets = WindTargets.Player | WindTargets.Bud | WindTargets.Ropes;
	public bool DrawStreaks = true;
	public Color StreakColor = new(0x9edfff);
	public float StreakSpeed = 70f;
	public int StreakSpacing = 16;

	public RectInt WorldRect => new(Position, Size);

	Vector2 DirectionVector => Direction.Normalized();

	public Wind()
	{
		Sprite = null;
		CollidesWithSolids = false;
		Hitbox = new(new RectInt(0, 0, 0, 0));
		Depth = 3;
	}

	public override void Update()
	{
		base.Update();
		var direction = DirectionVector;
		if (direction == Vector2.Zero || Acceleration <= 0)
			return;

		foreach (var actor in Game.Actors)
		{
			if (actor == this || actor is Pawn || !Affects(actor) || !Overlaps(actor))
				continue;

			Accelerate(actor, direction);
		}
	}

	internal static PawnInfluence ApplyTo(Pawn pawn)
	{
		var influence = new PawnInfluence();
		foreach (var actor in pawn.WindActors)
		{
			if (actor is not Wind wind || !wind.Affects(pawn) || !wind.Overlaps(pawn))
				continue;

			var direction = wind.DirectionVector;
			if (direction == Vector2.Zero || wind.Acceleration <= 0)
				continue;

			wind.Accelerate(pawn, direction);
			if (wind.MaxSpeed <= 0)
			{
				if (direction.X > 0)
					influence.PositiveXUnlimited = true;
				else if (direction.X < 0)
					influence.NegativeXUnlimited = true;
				continue;
			}

			var horizontalLimit = MathF.Abs(direction.X) * wind.MaxSpeed;
			if (direction.X > 0)
				influence.PositiveXSpeedLimit = MathF.Max(influence.PositiveXSpeedLimit, horizontalLimit);
			else if (direction.X < 0)
				influence.NegativeXSpeedLimit = MathF.Max(influence.NegativeXSpeedLimit, horizontalLimit);
		}
		return influence;
	}

	// Ropes sample the wind field per verlet point instead of receiving Velocity pushes.
	// SpeedLimit is the horizontal cap along the push direction; 0 means uncapped.
	internal bool TryGetHorizontalPush(out RectInt rect, out float accelX, out float speedLimit)
	{
		var direction = DirectionVector;
		rect = WorldRect;
		accelX = direction.X * Acceleration;
		speedLimit = MaxSpeed > 0 ? MathF.Abs(direction.X) * MaxSpeed : 0;
		return Targets.Has(WindTargets.Ropes) && Acceleration > 0 && accelX != 0;
	}

	void Accelerate(Actor actor, Vector2 direction)
	{
		var acceleration = Acceleration * Time.Delta;
		if (MaxSpeed > 0)
		{
			var remaining = MaxSpeed - Vector2.Dot(actor.Velocity, direction);
			if (remaining <= 0)
				return;
			acceleration = MathF.Min(acceleration, remaining);
		}
		actor.Velocity += direction * acceleration;
	}

	bool Overlaps(Actor actor) => actor.Hitbox.Overlaps(WorldRect - actor.Position);

	bool Affects(Actor actor)
		=> actor switch
		{
			Player player => player.State != Pawn.States.Burrow && Targets.Has(WindTargets.Player),
			Bud => Targets.Has(WindTargets.Bud),
			Bullet or Arrow => Targets.Has(WindTargets.Projectiles),
			_ => actor.Mask.Has(Masks.Enemy) && Targets.Has(WindTargets.Enemies),
		};

	public override void Render(Batcher batcher)
	{
		if (!DrawStreaks || Size.X <= 0 || Size.Y <= 0 || DirectionVector == Vector2.Zero)
			return;

		var direction = DirectionVector;
		var spacing = Math.Max(4, StreakSpacing);
		var count = Math.Clamp(Size.X * Size.Y / (spacing * spacing), 2, 96);
		var travel = Time.SecondsF * StreakSpeed;
		for (var i = 0; i < count; i++)
		{
			var seed = Hash((uint)i ^ (uint)Position.X * 0x9e3779b9u ^ (uint)Position.Y * 0x85ebca6bu);
			var origin = new Vector2(Unit(seed) * Size.X, Unit(Hash(seed)) * Size.Y);
			var offset = travel + Unit(Hash(seed ^ 0xc2b2ae35u)) * MathF.Max(Size.X, Size.Y);
			var head = new Vector2(
				Wrap(origin.X + direction.X * offset, Size.X),
				Wrap(origin.Y + direction.Y * offset, Size.Y));
			var length = 4 + Unit(Hash(seed ^ 0x27d4eb2du)) * 8;
			var tail = Vector2.Clamp(head - direction * length, Vector2.Zero, Size);
			var alpha = 0.16f + Unit(Hash(seed ^ 0x165667b1u)) * 0.22f;
			batcher.Line(tail.Round(), head.Round(), 1, StreakColor * alpha);
		}
	}

	static float Wrap(float value, int size)
	{
		value %= size;
		return value < 0 ? value + size : value;
	}

	static float Unit(uint value) => (value & 0xffff) / 65535f;

	static uint Hash(uint value)
	{
		value ^= value >> 16;
		value *= 0x7feb352du;
		value ^= value >> 15;
		value *= 0x846ca68bu;
		return value ^ value >> 16;
	}

#if EDITOR
	sealed class Editor : Manager.ActorEditor<Wind>
	{
		internal override void Gizmos(Manager.EditorActor actor, ref Manager.EditorGizmos gizmos)
		{
			var direction = actor.GetRoomPoint(nameof(Direction), Point2.Up).Normalized();
			if (direction == Vector2.Zero)
				return;

			var center = actor.Bounds.CenterF;
			var length = MathF.Max(12, MathF.Min(actor.Bounds.Width, actor.Bounds.Height) * 0.3f);
			var tip = center + direction * length;
			var side = new Vector2(-direction.Y, direction.X);
			var color = new Color(0x9edfff);
			gizmos.Line(center - direction * length * 0.5f, tip, gizmos.LineWeight * 2, color);
			gizmos.Line(tip, tip - direction * 7 + side * 4, gizmos.LineWeight * 2, color);
			gizmos.Line(tip, tip - direction * 7 - side * 4, gizmos.LineWeight * 2, color);
		}
	}
#endif
}
