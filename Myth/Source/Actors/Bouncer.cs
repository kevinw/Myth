using Foster.Framework;
using System.Numerics;

namespace Myth;

// Bumper and bounce pad in one actor: a squishy verlet ring that flings overlapping actors.
// By default the bounce is radial, snapped to eight directions; the Directional flag makes it a
// bounce pad that always launches along Direction. Actors are bounced once on entry and re-arm
// by leaving, so resting on a pad keeps bouncing but sitting inside a bumper doesn't buzz.
[GameActor(Tags = ActorTag.Obstacle, Sprite = "Editor/bouncer", Anchor = SpriteAnchor.TopLeft,
	LivePositionEditable = true, LiveResizable = true)]
public class Bouncer : Actor, IResizable
{
	public static readonly Point2 DefaultSize = new(Game.TileSize * 2, Game.TileSize * 2);

	public Point2 Size { get; set; } = DefaultSize;
	public float Strength = 80f;
	// Pad mode: always launch along Direction instead of radially away from the center.
	public bool Directional;
	// How much wider the straight-up sector is relative to the other seven. 0.2 = 20% wider.
	public float UpwardBias = 0.2f;
	public Point2 Direction = Point2.Up;
	public Color FillColor = new(0x6d2b56);
	public Color RimColor = new(0xff7ac2);

	// Membrane spring: accel per px of displacement from rest, and per-second velocity bleed.
	static float Stiffness => 3280f;
	static float DampingRate => 11f;
	static float MaxSimStep => 1f / 30f;
	static float VisualRecoilOffset => 4f;

	// Whole-body recoil spring: kicks 2px opposite the launch direction on a bounce.
	static float RecoilStiffness => 900f;
	static float RecoilDamping => 42f;
	Vector2 recoilOffset;
	Vector2 recoilVelocity;

	// The ring simulates in actor-local space so editor drags carry the blob along rigidly.
	Vector2[] points = [];
	Vector2[] prev = [];
	Vector2[] rest = [];
	Point2 builtSize;
	HashSet<Actor> inside = [];
	HashSet<Actor> insideNow = [];

	public Bouncer()
	{
		Sprite = null;
		Mask = Masks.None;
		CollidesWithSolids = false;
		Depth = 2;
	}

	public override bool CastsSpriteShadow => false;

	RectInt WorldRect => new(Position, Size);
	Vector2 LocalCenter => (Vector2)Size / 2;

	public override void Added()
	{
		base.Added();
		Hitbox = new(new RectInt(0, 0, Size.X, Size.Y));
		BuildRing();
	}

	void BuildRing()
	{
		builtSize = Size;
		var radius = LocalCenter;
		var count = Math.Clamp((int)(MathF.PI * (radius.X + radius.Y) / 3), 10, 64);
		points = new Vector2[count];
		prev = new Vector2[count];
		rest = new Vector2[count];
		for (var i = 0; i < count; i++)
		{
			var angle = MathF.Tau * i / count;
			rest[i] = LocalCenter + new Vector2(MathF.Cos(angle) * radius.X, MathF.Sin(angle) * radius.Y);
			points[i] = prev[i] = rest[i];
		}
	}

	public override void Update()
	{
		base.Update();
		if (builtSize != Size)
			BuildRing();

		Simulate(MathF.Min(Time.Delta, MaxSimStep));

		var recoilAccel = -recoilOffset * RecoilStiffness - recoilVelocity * RecoilDamping;
		recoilVelocity += recoilAccel * Time.Delta;
		recoilOffset += recoilVelocity * Time.Delta;

		insideNow.Clear();
		foreach (var actor in Game.Actors)
		{
			if (!Affects(actor) || !actor.Hitbox.Overlaps(WorldRect - actor.Position))
				continue;
			insideNow.Add(actor);
			if (!inside.Contains(actor))
				Bounce(actor);
		}
		(inside, insideNow) = (insideNow, inside);
	}

	void Simulate(float dt)
	{
		if (dt <= 0)
			return;
		var damp = MathF.Exp(-DampingRate * dt);
		for (var i = 0; i < points.Length; i++)
		{
			var accel = (rest[i] - points[i]) * Stiffness;
			var p = points[i];
			points[i] = p + (p - prev[i]) * damp + accel * (dt * dt);
			prev[i] = p;
		}
	}

	void Bounce(Actor actor)
	{
		var direction = BounceDirection(actor);
		
		// Don't squash velocity in the direction we're not bouncing (i.e., let a player keep traveling horizontally for a vertical bounce).
		var hasX = MathF.Abs(direction.X) > 0.01f;
		var hasY = MathF.Abs(direction.Y) > 0.01f;
		if (hasX) actor.Velocity.X = 0.0f;
		if (hasY) actor.Velocity.Y = 0.0f;
		
		actor.Velocity += direction * Strength;

		recoilOffset = -direction * VisualRecoilOffset;
		recoilVelocity = Vector2.Zero;

		// Dent the membrane on the impact side; the spring-back overshoot is the squish.
		var impact = actor.Position + actor.Hitbox.Rect.CenterF - (Position + LocalCenter);
		var side = impact == Vector2.Zero ? -direction : impact.Normalized();
		var dent = Math.Clamp(Strength * 0.045f, 3f, 8f);
		for (var i = 0; i < points.Length; i++)
		{
			var weight = MathF.Max(0, Vector2.Dot((rest[i] - LocalCenter).Normalized(), side));
			points[i] -= side * (weight * weight * dent);
		}

		AudioEvent("bounce");
	}

	Vector2 BounceDirection(Actor actor)
	{
		if (Directional)
			return Direction == Point2.Zero ? -Vector2.UnitY : Direction.Normalized();

		var v = actor.Position + actor.Hitbox.Rect.CenterF - (Position + LocalCenter);
		if (v == Vector2.Zero)
			return -Vector2.UnitY;

		var angle = v.Angle();

		// Each sector is normally π/4 wide (±π/8 from its centre). Check straight up
		// first with an expanded half-width so it wins an extra UpwardBias fraction of
		// the arc, carved equally from the two adjacent upper-diagonal sectors.
		const float UpAngle = -MathF.PI / 2f;
		const float HalfSector = MathF.PI / 8f;
		var delta = angle - UpAngle;
		delta -= MathF.Round(delta / (MathF.PI * 2)) * (MathF.PI * 2); // wrap to [-π, π]
		if (MathF.Abs(delta) <= HalfSector * (1f + UpwardBias))
			return -Vector2.UnitY;

		angle = MathF.Round(angle / (MathF.PI / 4)) * (MathF.PI / 4);
		return Calc.AngleToVector(angle);
	}

	bool Affects(Actor actor)
		=> actor switch
		{
			Player player => player.State != Pawn.States.Burrow,
			Bud => true,
			ICarryable { CarriedBy: null } => true,
			_ => actor.Mask.Has(Masks.Enemy),
		};

	public override void Render(Batcher batcher)
	{
		if (points.Length < 3)
			return;
		batcher.PushMatrix(Matrix3x2.CreateTranslation(recoilOffset));
		var center = LocalCenter;
		for (var i = 0; i < points.Length; i++)
			batcher.Triangle(center, points[i], points[(i + 1) % points.Length], FillColor);
		for (var i = 0; i < points.Length; i++)
			batcher.Line(points[i], points[(i + 1) % points.Length], 1, RimColor);
		batcher.PopMatrix();
	}

	public override Rect GetVisualBounds()
		=> new Rect(Position.X, Position.Y, Size.X, Size.Y).Inflate(6);

#if EDITOR
	sealed class Editor : Manager.ActorEditor<Bouncer>
	{
		internal override void Gizmos(Manager.EditorActor actor, ref Manager.EditorGizmos gizmos)
		{
			if (!actor.GetBool(nameof(Directional), false))
				return;

			var direction = actor.GetRoomPoint(nameof(Direction), Point2.Up).Normalized();
			if (direction == Vector2.Zero)
				return;

			var center = actor.Bounds.CenterF;
			var length = MathF.Max(12, MathF.Min(actor.Bounds.Width, actor.Bounds.Height) * 0.6f);
			var tip = center + direction * length;
			var side = new Vector2(-direction.Y, direction.X);
			var color = new Color(0xff7ac2);
			gizmos.Line(center, tip, gizmos.LineWeight * 2, color);
			gizmos.Line(tip, tip - direction * 7 + side * 4, gizmos.LineWeight * 2, color);
			gizmos.Line(tip, tip - direction * 7 - side * 4, gizmos.LineWeight * 2, color);
		}
	}
#endif
}
