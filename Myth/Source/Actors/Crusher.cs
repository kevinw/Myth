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

	// Solids attached to us (a belt bolted to our face, say) ride along as part of the crusher, so they
	// don't stop it. RectInt.Contains is strict, so the room is grown a pixel: resting flush against its
	// edge still counts as inside.
	bool Clear(Point2 offset) => !OverlapsAny(offset, Masks.Solid, this) && Game.Bounds.Inflate(1).Contains(Bounds() + offset);

	void Impact()
	{
		var strength = speed / MaxSpeed;
		Game.Shake(ImpactShake * strength, strength);
		AudioEvent("tile_break");
		EmitImpactDust(strength);
		(state, timer, speed, remainder) = (States.Hold, ReturnDelay, 0, 0);
	}

	static readonly List<RectInt> contacts = []; // reusable scratch: the stretches of our face that landed

	// Dust comes off the stretches of the leading face that actually landed on something, so a crusher stopped
	// across a single pillar cracks dust off the pillar rather than along its whole width. Touching nothing at
	// all (we stopped on the room's edge) puffs the whole face, which is what every impact used to do.
	void EmitImpactDust(float strength)
	{
		if (Game.Particles is not { } particles)
			return;

		var (a, b) = Face(heading);
		var step = (b - a).Sign();
		var onFace = -Point2.Min(heading, Point2.Zero); // travelling negative the band sits a pixel out; put the dust back on the face
		Game.SolidRanges(this, ContactBand(a, b), contacts);
		foreach (var run in contacts)
		{
			var from = run.TopLeft + onFace;
			EmitSeamDust(particles, from, from + step * Math.Max(run.Width, run.Height), strength);
		}
		if (contacts.Count == 0)
			EmitSeamDust(particles, a, b, strength);
	}

	// The 1px sliver of world just beyond the leading face, between its two corners. A rect's max edge is
	// exclusive, so travelling positive the sliver starts on the face and travelling negative it starts a
	// pixel before it.
	RectInt ContactBand(Point2 a, Point2 b)
		=> new(RectInt.Between(a, b).TopLeft + Point2.Min(heading, Point2.Zero),
			Math.Max(1, Math.Abs(b.X - a.X)), Math.Max(1, Math.Abs(b.Y - a.Y)));

	// One stretch of seam: dust along it, plus a burst off each end angled 45 degrees out of the contact (along
	// the face and back the way we came), so it escapes rather than crawling along the surface we hit -- which,
	// slamming sideways into a wall, read as dust climbing it.
	void EmitSeamDust(ParticleSystem particles, Vector2 from, Vector2 to, float strength)
	{
		var along = Vector2.Normalize(to - from);
		var back = -(Vector2)heading;
		var count = (int)(12 * strength) + 2;
		particles.EmitLineBetween(dustEffect, from, to);
		particles.Emit(dustEffect, from, count, Vector2.Normalize(back - along).Angle(), 1 + 2 * strength, MathF.PI / 4);
		particles.Emit(dustEffect, to, count, Vector2.Normalize(back + along).Angle(), 1 + 2 * strength, MathF.PI / 4);
	}

	protected override void Pinned(Actor actor, Point2 step) => actor.Crushed(this, step);

	// Nowhere we can reach is safe footing: the corridor swept from home, plus the lane still ahead
	// of each flagged face. Keeps respawns out from under a crusher that only looks idle.
	internal override bool UnsafeFooting(in RectInt standing)
	{
		if (Bounds().Conflate(new RectInt(originalPosition, Size)).Overlaps(standing))
			return true;
		foreach (var (flag, dir) in Lanes)
			if (Direction.Has(flag) && Lane(dir, Reach(dir)).Overlaps(standing))
				return true;
		return false;
	}

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

	// Render-only, and inherited by whatever is bolted to us (Actor.VisualOffset), so the lights, cables and
	// belts riding this crusher tremble with it instead of hanging still off a shaking block.
	internal override Vector2 LocalVisualOffset => state == States.Windup
		? (new Vector2(MathF.Sin(Time.SecondsF * TrembleRate), MathF.Cos(Time.SecondsF * TrembleRate * 1.3f)) * TrembleAmount * Menace).RoundToPoint2()
		: Vector2.Zero;

	/*
	public override void RenderEmissive(Batcher batcher)
	{
		if (Menace <= 0)
			return;
		var (a, b) = Face(heading);
		batcher.Rect(RectInt.Between(a - heading, b) - Position, HeatColor * Menace);
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
