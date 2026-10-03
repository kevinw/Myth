using Foster.Framework;
using System.Numerics;
namespace Myth;

// Watches the lanes off its flagged faces -- each runs from the face to the first solid -- and once the player
// steps into one, it winds up (trembling, its leading edge heating) and slams along the lane, accelerating until
// it hits solid. Whatever it pins is crushed; a burrower caught underneath is sealed into the seam instead and can
// travel it (see Pawn.UpdateBurrowSeam). Then it rests and grinds back home.
[GameActor(Tags = ActorTag.Obstacle, Sprite = "Editor/moving_platform", Anchor = SpriteAnchor.TopLeft)]
class Crusher : DualGridRect
{
	[Flags]
	public enum Directions { None = 0, Up = 1, Down = 2, Left = 4, Right = 8 }

	public Directions Direction = Directions.Down;
	[I(0, 3)] public float Delay = 0.5f;
	[I(50, 3000)] public float Acceleration = 900;
	[I(30, 800)] public float MaxSpeed = 360;
	[I(0, 5)] public float ReturnDelay = 1f;
	[I(5, 160)] public float ReturnSpeed = 40;

	enum States { Idle, Windup, Slam, Hold, Return }
	States state;
	float timer, speed, remainder;
	Point2 heading;
	SoundInstance? moveSound;
	int dustEffect = -1;

	static float TrembleAmount => 1.5f;
	static float TrembleRate => 60;
	static Color HeatColor => new(0xff5a30);
	static float ImpactShake => 0.3f;

	public static readonly Point2 DefaultSize = new(Game.TileSize * 3, Game.TileSize * 2);
	public Crusher() : base(DefaultSize)
	{
		CollidesWithSolids = true;
		Depth = Depths.ForegroundProp; // behind characters like tiles, so whatever it flattens stays visible
	}

	public override void Added()
	{
		base.Added();
		dustEffect = AcquireParticleEffect("elp_ground_dust");
	}

	public override void Destroyed()
	{
		StopAndReleaseSound(ref moveSound, false);
		ReleaseParticleEffect(ref dustEffect);
	}

	public override void Update()
	{
		switch (state)
		{
			case States.Idle:
				if (SpotPlayer() is { } lane)
				{
					heading = lane;
					(state, timer) = (States.Windup, Delay);
				}
				break;

			case States.Windup:
				if ((timer -= Time.Delta) <= 0)
					(state, speed, remainder) = (States.Slam, 0, 0);
				break;

			case States.Slam:
				speed = Calc.Approach(speed, MaxSpeed, Acceleration * Time.Delta);
				if (Travel(heading, speed))
					Impact();
				break;

			case States.Hold:
				if ((timer -= Time.Delta) <= 0)
				{
					(state, remainder) = (States.Return, 0);
					moveSound = Game.CreateAudioEvent("moving_stone", Position);
					moveSound?.Start();
				}
				break;

			case States.Return:
				Travel(-heading, ReturnSpeed);
				if (Position == originalPosition)
				{
					state = States.Idle;
					StopAndReleaseSound(ref moveSound, false);
				}
				break;
		}
		if (moveSound != null)
			Game.SetAudioEventPosition(moveSound, Position);
	}

	// Steps whole pixels toward `dir` at `pixelsPerSecond`. True when solid (or the room's edge) stops us.
	bool Travel(Point2 dir, float pixelsPerSecond)
	{
		remainder += pixelsPerSecond * Time.Delta;
		for (; remainder >= 1; remainder--)
		{
			if (state == States.Return && Position == originalPosition)
				break;
			if (!Clear(dir))
				return true;
			MoveSolid(dir);
		}
		return false;
	}

	// RectInt.Contains is strict, so the room is grown a pixel: resting flush against its edge still counts as inside.
	bool Clear(Point2 offset) => !OverlapsAny(offset, Masks.Solid) && Game.Bounds.Inflate(1).Contains(Bounds() + offset);

	void Impact()
	{
		var strength = speed / MaxSpeed;
		Game.Shake(ImpactShake * strength, strength);
		AudioEvent("tile_break");
		if (Game.Particles is { } particles)
		{
			// Dust squirts out of both ends of the seam, harder the faster we landed.
			var (a, b) = Face(heading);
			var along = Vector2.Normalize(b - a);
			particles.EmitLineBetween(dustEffect, a, b);
			var count = (int)(12 * strength) + 2;
			particles.Emit(dustEffect, a, count, (-along).Angle(), 1 + 2 * strength, MathF.PI / 4);
			particles.Emit(dustEffect, b, count, along.Angle(), 1 + 2 * strength, MathF.PI / 4);
		}
		(state, timer, speed, remainder) = (States.Hold, ReturnDelay, 0, 0);
	}

	protected override void Pinned(Actor actor, Point2 step) => actor.Crushed(this, step);

	// The nearest-first lane holding the player, if any. Each flagged face's lane is checked against the
	// room-long strip first, so the solid sweep only runs once the player is lined up.
	Point2? SpotPlayer()
	{
		if (Game.GetFirst<Player>() is not { Health: > 0 } player)
			return null;
		var target = player.Hitbox.Rect + player.Position;
		var roomSpan = Math.Max(Game.Bounds.Width, Game.Bounds.Height);
		foreach (var (flag, dir) in Lanes)
			if (Direction.Has(flag) && Lane(dir, roomSpan).Overlaps(target) && Lane(dir, Reach(dir)).Overlaps(target))
				return dir;
		return null;
	}

	static readonly (Directions, Point2)[] Lanes =
		[(Directions.Down, Point2.Down), (Directions.Up, Point2.Up), (Directions.Left, Point2.Left), (Directions.Right, Point2.Right)];

	RectInt Lane(Point2 dir, int reach)
	{
		var (a, b) = Face(dir);
		return RectInt.Between(a, b + dir * reach);
	}

	// How far we can slide toward `dir` before touching solid. Coarse steps no longer than our own depth
	// can't skip over anything, then single pixels find the contact.
	int Reach(Point2 dir)
	{
		var step = Math.Min(Game.TileSize, Math.Abs(dir.X != 0 ? Size.X : Size.Y));
		var reach = 0;
		while (Clear(dir * (reach + step)))
			reach += step;
		while (Clear(dir * (reach + 1)))
			reach++;
		return reach;
	}

	// Bud can plan to stand on us where we rest, unless we'd launch him into the ceiling.
	internal RectInt? NavFootprint => Direction.Has(Directions.Up) ? null : new RectInt(originalPosition, Size);

	// The face leading toward `dir`, as its two world corners.
	(Point2 A, Point2 B) Face(Point2 dir)
	{
		var b = Bounds();
		return dir.X > 0 ? (b.TopRight, b.BottomRight)
			: dir.X < 0 ? (b.TopLeft, b.BottomLeft)
			: dir.Y > 0 ? (b.BottomLeft, b.BottomRight)
			: (b.TopLeft, b.TopRight);
	}

	// 0..1 menace: climbs through the windup, holds through the slam, cools while we rest. Drives the tremble
	// and the heat on the leading edge, so the one value reads as both.
	float Menace => state switch
	{
		States.Windup => Delay > 0 ? 1 - timer / Delay : 1,
		States.Slam => 1,
		States.Hold => ReturnDelay > 0 ? timer / ReturnDelay : 0,
		_ => 0
	};

	protected override Vector2 RenderOffset => state == States.Windup
		? (new Vector2(MathF.Sin(Time.SecondsF * TrembleRate), MathF.Cos(Time.SecondsF * TrembleRate * 1.3f)) * TrembleAmount * Menace).RoundToPoint2()
		: Vector2.Zero;

	/*
	public override void RenderEmissive(Batcher batcher)
	{
		if (Menace <= 0)
			return;
		var (a, b) = Face(heading);
		batcher.Rect(RectInt.Between(a - heading, b) - Position + RenderOffset.RoundToPoint2(), HeatColor * Menace);
	}
	*/

#if EDITOR
	sealed class Editor : Manager.ActorEditor<Crusher>
	{
		internal override void Gizmos(Manager.EditorActor actor, ref Manager.EditorGizmos gizmos)
		{
			var directions = actor.GetEnum(nameof(Direction), Directions.Down);
			var center = actor.Bounds.CenterF;
			var color = new Color(0xff5a30);
			foreach (var (flag, dir) in Lanes)
				if (directions.Has(flag))
					gizmos.Arrow(center, center + dir * (float)Game.TileSize * 2, gizmos.LineWeight * 2, color);
		}
	}
#endif
}
