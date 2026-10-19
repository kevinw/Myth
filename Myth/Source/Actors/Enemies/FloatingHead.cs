using System.Numerics;
using Foster.Framework;

namespace Myth;

// A disembodied sculpted head. It drifts asleep until it sees the player, then its eyes light (glowing into the room)
// and it flies at them along a flow field around the room's solids, mouth working. Now and then, close with a clear
// line, it stops, draws back trembling with its eyes flaring, and lunges. Each hit caves a big dent into
// it, sends it sailing, wobbles it like jelly and knocks chunks off; the third drops it, eyes out, to lie darkened in
// the background.
[GameActor(Sprite = "player", Tags = ActorTag.Enemy | ActorTag.Flying, BoundsX = -12, BoundsY = -12, BoundsW = 24,
	BoundsH = 24, LivePositionEditable = true)]
public sealed class FloatingHead : Actor
{
	enum State { Asleep, Noticing, Hunting, Windup, Dashing, Knocked, Falling, Remains }

	[I(5, 32)] public float Radius = 12;
	internal static ContentRef<HeadShapeContent> ElderHead => new("elder");
	public ContentRef<HeadShapeContent> HeadShapeId = ElderHead;
	public Color HeadColor = new(0xd9c2a7);
	public Color EyeColor = new(0xff5a3c);
	public Color BruiseColor = new(0x5a1a38);

	static float SightRange => 150f;
	static float NoticeTime => 0.45f;
	static float HuntSpeed => 48f;
	static float HuntAcceleration => 170f;
	static float AsleepBob => 5f;

	static float DashInterval => 3.5f; // average seconds of hunting between dashes
	static float DashRange => 110f;
	static float DashWindup => 0.55f;
	static float DashAimLock => 0.75f; // fraction of the windup after which it stops tracking the player
	static float DashRecoil => 18f;
	static float DashSpeedScale => 3f;
	static float DashDistance => 80f;

	static float KnockbackSpeed => 210f;
	static float KnockbackLift => 70f;
	static float KnockbackDrag => 3.5f; // per second, exponential
	static float KnockbackTime => 0.55f;
	static float WallRestitution => 0.6f;

	static float DentRadius => 0.45f; // head radii
	static float DentDepth => 0.26f;
	static float WobbleKick => 0.09f;
	static float WobbleDecay => 4.5f;
	static float WobbleRate => 30f;
	static float SpinKick => 5f; // radians/sec of yaw and roll a hit throws in

	static float ContactReach => 1f; // radii: walls nearer than this press into the head
	static float PressSquash => 0.7f; // squash per radius pressed in
	static float ImpactSquash => 0.025f; // squash spring kick per px/sec of impact
	static float SquashStiffness => 260f;
	static float SquashDamping => 9f;
	static float RemainsSag => 0.12f;

	static float FallGravity => 520f;
	static float GroundBounce => 0.3f;
	static int RemainsDepth => 100;
	static Color RemainsTint => new(0x4a4656);
	static float RemainsFade => 0.8f;

	static float FaceResponse => 10f; // per second, how fast its face eases toward its mood
	static Expression HuntingFace => new(0.4f, -0.1f, 0.35f, -0.3f); // sizing you up, one brow cocked
	static Expression AsleepFace => new(BrowRaise: -0.4f, Smile: 0.2f);
	static Expression DeadFace => new(-0.3f, -0.6f, 0, -0.2f);

	static int CellSize => 8;
	static float PathRefresh => 0.25f;

	State state;
	int health = 3;
	float stateTime;
	float eyes; // 0 dark .. 1 lit
	float mouth;
	float yaw, yawVelocity, pitch, pitchVelocity, roll, rollVelocity;
	float wobble, wobblePhase;
	float darken;
	float charge; // 0 .. 1 over a dash windup, held through the dash
	Expression face;
	float dashTimer;
	Vector2 dashDirection, dashFrom;
	// Per axis (side walls, floor/ceiling): which way the nearest wall face or slope lies, or the last thing it hit, and
	// how far (px) from the center, MaxValue when out of reach.
	readonly (Vector2 Toward, float Gap)[] walls = new (Vector2, float)[2];
	Vector2 impact, impactVelocity; // extra squash along each axis, sprung, so impacts compress and rebound
	Vector2 home;
	readonly Vector4[] dents = new Vector4[SculptedHead.MaxDents];
	int dentCount;
	SculptedHead? head;
	int chunkEffect = -1;

	// The flow field: each cell's step count to the player's cell, -1 unreachable; blocked is where the head won't fit.
	Room? gridRoom;
	RectInt gridBounds;
	Point2 gridSize;
	bool[] blocked = [];
	int[] distance = [];
	int[] queue = [];
	float pathTimer;

	public FloatingHead()
	{
		Mask = Masks.Enemy;
		Depth = -5;
	}

	internal override bool ContactDamageEnabled => state is State.Noticing or State.Hunting or State.Windup or State.Dashing;
	public override string HitSoundType => "flesh";
	Vector2 Center => Position;

	public override void Added()
	{
		var half = (int)(Radius * 0.85f);
		Hitbox = new(new RectInt(-half, -half, half * 2, half * 2));
		home = Position;
		chunkEffect = AcquireParticleEffect("head_chunks");
		base.Added();
	}

	public override void Destroyed()
	{
		ReleaseParticleEffect(ref chunkEffect);
		head?.Dispose();
		head = null;
	}

	public override void Update()
	{
		stateTime += Time.Delta;
		var player = Game.GetFirst<Player>();
		var toPlayer = player != null ? player.Position + player.Hitbox.Rect.Center - Center : Vector2.Zero;
		switch (state)
		{
			case State.Asleep:
				Velocity = new(Calc.Approach(Velocity.X, (home.X - Position.X) * 2, 40 * Time.Delta),
					MathF.Sin(Timer * 1.6f) * AsleepBob + (home.Y - Position.Y));
				if (player != null && Time.OnInterval(0.2f) && CanSee(toPlayer, SightRange))
					Enter(State.Noticing);
				break;

			case State.Noticing:
				// It stops, opens its eyes and gapes; then it comes.
				Velocity = Calc.Approach(Velocity, Vector2.Zero, 200 * Time.Delta);
				var t = Math.Clamp(stateTime / NoticeTime, 0, 1);
				eyes = Ease.Cube.Out(t);
				mouth = 0.8f * t;
				if (stateTime >= NoticeTime)
					Enter(State.Hunting);
				break;

			case State.Hunting:
				if (player != null)
					Hunt(player, toPlayer);
				mouth = 0.35f + 0.25f * MathF.Sin(Timer * 7);
				dashTimer -= Time.Delta;
				if (dashTimer <= 0 && CanSee(toPlayer, DashRange))
					Enter(State.Windup);
				break;

			case State.Windup:
				// It draws back from the player, clenched and trembling, aim locking just before it goes.
				charge = Math.Clamp(stateTime / DashWindup, 0, 1);
				if (charge < DashAimLock)
					dashDirection = toPlayer.Normalized(dashDirection);
				Velocity = Calc.Approach(Velocity, -dashDirection * DashRecoil, 300 * Time.Delta);
				mouth = Calc.Approach(mouth, 0, Time.Delta * 6);
				wobble = MathF.Max(wobble, 0.025f * charge);
				if (stateTime >= DashWindup)
					Enter(State.Dashing);
				break;

			case State.Dashing:
				Velocity = dashDirection * HuntSpeed * DashSpeedScale;
				mouth = Calc.Approach(mouth, 1, Time.Delta * 8);
				if (Vector2.Distance(Center, dashFrom) >= DashDistance)
					Enter(State.Hunting);
				break;

			case State.Knocked:
				Velocity *= MathF.Exp(-KnockbackDrag * Time.Delta);
				mouth = Calc.Approach(mouth, 0.2f, Time.Delta * 2);
				if (stateTime >= KnockbackTime)
					Enter(State.Hunting);
				break;

			case State.Falling:
				Velocity.Y += FallGravity * Time.Delta;
				Velocity.X *= MathF.Exp(-0.8f * Time.Delta);
				// It rolls along with what it travels.
				rollVelocity = Calc.Approach(rollVelocity, Velocity.X / Radius, 20 * Time.Delta);
				eyes = Calc.Approach(eyes, 0, Time.Delta * 3);
				mouth = Calc.Approach(mouth, 1, Time.Delta * 4);
				break;

			case State.Remains:
				Velocity = Vector2.Zero;
				darken = Calc.Approach(darken, 1, Time.Delta / RemainsFade);
				mouth = Calc.Approach(mouth, 0.6f, Time.Delta);
				break;
		}
		if (state != State.Asleep && state != State.Noticing && state < State.Falling)
			eyes = Calc.Approach(eyes, 1, Time.Delta * 2);
		if (state is not State.Windup and not State.Dashing)
			charge = Calc.Approach(charge, 0, Time.Delta * 3);
		UpdateLook(toPlayer);
		face = Expression.Lerp(face, Mood, 1 - MathF.Exp(-FaceResponse * Time.Delta));
		wobble *= MathF.Exp(-WobbleDecay * Time.Delta);
		if (wobble < 0.004f)
			wobble = 0;
		wobblePhase += WobbleRate * Time.Delta;
		Spring.Step(ref impact, ref impactVelocity, state == State.Remains ? new(0, RemainsSag) : Vector2.Zero,
			SquashStiffness, SquashDamping, Time.Delta);
		base.Update();
		FindWalls();
	}

	static readonly Point2[] Sides = [new(-1, 0), new(1, 0), new(0, -1), new(0, 1)];

	void FindWalls()
	{
		walls[0].Gap = walls[1].Gap = float.MaxValue;
		var half = Hitbox.Rect.Width / 2;
		foreach (var side in Sides)
			for (var gap = half; gap < Radius * ContactReach; gap++)
			{
				var probe = Hitbox.Rect + Position + side * (gap - half + 1);
				if (!Game.OverlapsRoomCollision(this, probe))
					continue;
				var axis = side.X != 0 ? 0 : 1;
				var wall = ((Vector2)side, (float)gap);
				// Slopes are floors and ceilings: the vertical probe presses it flat along the slope's own normal.
				if (Game.TryFindSurface(this, probe, side, 0, out var hit) && hit.Slope is { } slope)
				{
					if (axis == 0)
						break;
					Game.CollisionRoomFor(this)!.GetSlopeSurface(slope, out var left, out _);
					wall = (-hit.Normal, Vector2.Dot(Center - left, hit.Normal));
				}
				if (wall.Item2 < walls[axis].Gap)
					walls[axis] = wall;
				break;
			}
	}

	void Impact(int axis, float speed)
	{
		impactVelocity[axis] += MathF.Abs(speed) * ImpactSquash;
		walls[axis].Toward = axis == 0 ? new(MathF.Sign(speed), 0) : new(0, MathF.Sign(speed));
		// Grazing the floor or a wall slides along it; only running into it head-on ends a dash.
		if (state == State.Dashing && MathF.Abs(dashDirection[axis]) > 0.5f)
		{
			wobble = MathF.Max(wobble, WobbleKick * 0.6f);
			Game.Shake(0.1f, 1);
			Enter(State.Hunting);
		}
	}

	void Enter(State next)
	{
		state = next;
		stateTime = 0;
		if (next == State.Noticing)
		{
			Squish = new(1.3f, 0.75f);
			AudioEvent("rig_puppet/swing");
		}
		else if (next == State.Hunting && dashTimer <= 0)
			dashTimer = DashInterval * Game.rng.Float(0.6f, 1.4f);
		else if (next == State.Windup)
			Squish = new(1.15f, 0.85f);
		else if (next == State.Dashing)
		{
			dashFrom = Center;
			Squish = new(0.8f, 1.2f);
			wobble = MathF.Max(wobble, WobbleKick * 0.5f);
			AudioEvent("rig_puppet/swing");
		}
	}

	// What it feels, by state; the windup sours toward rage as it charges.
	Expression Mood => state switch
	{
		State.Asleep => AsleepFace,
		State.Noticing => Expression.Surprised,
		State.Hunting => HuntingFace,
		State.Windup => Expression.Lerp(HuntingFace, Expression.Angry, charge),
		State.Dashing => Expression.Angry,
		State.Knocked => Expression.Pained,
		_ => DeadFace,
	};

	bool CanSee(Vector2 toPlayer, float range) => toPlayer.LengthSquared() < range * range && ClearLine(Center, Center + toPlayer, new(0, 0, 1, 1));

	// Facing follows the player, springy, so a hit's spin swings through and settles.
	void UpdateLook(Vector2 toPlayer)
	{
		var dead = state >= State.Falling;
		var targetYaw = dead || state == State.Asleep ? 0 : Math.Clamp(toPlayer.X / 60, -1, 1) * 0.75f;
		var targetPitch = dead || state == State.Asleep ? 0 : Math.Clamp(-toPlayer.Y / 80, -1, 1) * 0.35f - charge * 0.3f; // winding up tucks the chin
		Spring.Step(ref yaw, ref yawVelocity, targetYaw, dead ? 0 : 60, dead ? 2 : 9, Time.Delta);
		Spring.Step(ref pitch, ref pitchVelocity, targetPitch, dead ? 0 : 60, dead ? 2 : 9, Time.Delta);
		if (state == State.Falling)
			roll += rollVelocity * Time.Delta;
		else if (state == State.Remains)
			rollVelocity = 0;
		else
			Spring.Step(ref roll, ref rollVelocity, 0, 50, 7, Time.Delta);
	}

	// Straight at the player when the way is clear, otherwise down the flow field.
	void Hunt(Player player, Vector2 toPlayer)
	{
		var target = Center + toPlayer;
		if (!ClearLine(Center, target, Hitbox.Rect))
		{
			pathTimer -= Time.Delta;
			if (pathTimer <= 0)
			{
				pathTimer = PathRefresh;
				BuildFlowField(target);
			}
			if (NextCell() is { } next)
				target = next;
		}
		var toTarget = target - Center;
		var desired = toTarget.LengthSquared() > 1 ? toTarget.Normalized() * HuntSpeed : Vector2.Zero;
		Velocity = Calc.Approach(Velocity, desired, HuntAcceleration * Time.Delta);
	}

	bool ClearLine(Vector2 from, Vector2 to, RectInt bounds)
	{
		var steps = Math.Max(1, (int)MathF.Ceiling(Vector2.Distance(from, to) / 3f));
		for (var i = 1; i <= steps; i++)
			if (Game.OverlapsRoomCollision(this, bounds + Vector2.Lerp(from, to, i / (float)steps).RoundToPoint2()))
				return false;
		return true;
	}

	Point2 CellOf(Vector2 world) => ((world - gridBounds.TopLeft) / CellSize).FloorToPoint2();
	Vector2 CellCenter(Point2 cell) => gridBounds.TopLeft + cell * CellSize + new Vector2(CellSize * 0.5f);
	bool InGrid(Point2 cell) => cell.X >= 0 && cell.Y >= 0 && cell.X < gridSize.X && cell.Y < gridSize.Y;
	int Index(Point2 cell) => cell.Y * gridSize.X + cell.X;

	// Breadth-first out from the player's cell over the cells the head fits in. Which cells fit is found once per room.
	void BuildFlowField(Vector2 target)
	{
		if (Room is not { } room)
			return;
		if (gridRoom != room)
		{
			gridRoom = room;
			gridBounds = room.WorldBounds;
			gridSize = new((gridBounds.Width + CellSize - 1) / CellSize, (gridBounds.Height + CellSize - 1) / CellSize);
			var count = gridSize.X * gridSize.Y;
			blocked = new bool[count];
			distance = new int[count];
			queue = new int[count];
			for (var y = 0; y < gridSize.Y; y++)
				for (var x = 0; x < gridSize.X; x++)
					blocked[y * gridSize.X + x] = Game.OverlapsRoomCollision(this, Hitbox.Rect + CellCenter(new(x, y)).RoundToPoint2());
		}
		Array.Fill(distance, -1);
		var start = CellOf(target);
		if (!InGrid(start))
			return;
		var (read, write) = (0, 0);
		distance[Index(start)] = 0;
		queue[write++] = Index(start);
		while (read < write)
		{
			var at = queue[read++];
			var cell = new Point2(at % gridSize.X, at / gridSize.X);
			foreach (var step in Neighbors)
			{
				var next = cell + step;
				if (!InGrid(next) || blocked[Index(next)] || distance[Index(next)] >= 0)
					continue;
				distance[Index(next)] = distance[at] + 1;
				queue[write++] = Index(next);
			}
		}
	}

	static readonly Point2[] Neighbors = [new(1, 0), new(-1, 0), new(0, 1), new(0, -1)];

	// The downhill neighbor's center; diagonals only where both sides are open, so it doesn't clip corners.
	Vector2? NextCell()
	{
		var cell = CellOf(Center);
		if (gridRoom != Room || !InGrid(cell))
			return null;
		var (best, bestDistance) = ((Point2?)null, distance[Index(cell)] < 0 ? int.MaxValue : distance[Index(cell)]);
		for (var dy = -1; dy <= 1; dy++)
			for (var dx = -1; dx <= 1; dx++)
			{
				var next = cell + new Point2(dx, dy);
				if (!InGrid(next) || distance[Index(next)] < 0 || distance[Index(next)] >= bestDistance)
					continue;
				if (dx != 0 && dy != 0 && (Open(cell + new Point2(dx, 0)) is false || Open(cell + new Point2(0, dy)) is false))
					continue;
				(best, bestDistance) = (next, distance[Index(next)]);
			}
		return best is { } found ? CellCenter(found) : null;

		bool Open(Point2 c) => InGrid(c) && !blocked[Index(c)];
	}

	public override void OnCollideX()
	{
		Impact(0, Velocity.X);
		if (state is State.Knocked or State.Falling)
		{
			Velocity.X = -Velocity.X * WallRestitution;
			wobble = MathF.Max(wobble, WobbleKick * 0.5f);
		}
		else
			StopX();
	}

	public override void OnCollideY()
	{
		Impact(1, Velocity.Y);
		if (state == State.Falling && Velocity.Y > 0)
		{
			Game.Shake(0.15f, 1.5f);
			AudioEvent("boss/stomp");
			wobble = MathF.Max(wobble, WobbleKick * Math.Clamp(Velocity.Y / 300, 0.3f, 1));
			Squish = new(1.25f, 0.8f);
			if (Velocity.Y < 60)
			{
				state = State.Remains;
				Depth = RemainsDepth;
				StopY();
				return;
			}
			Velocity.Y = -Velocity.Y * GroundBounce;
			Velocity.X *= 0.6f;
			return;
		}
		if (state == State.Knocked)
			Velocity.Y = -Velocity.Y * WallRestitution;
		else
			StopY();
	}

	internal override void OnWasHit(HitInfo hitInfo)
	{
		if (state >= State.Falling)
			return;
		var at = hitInfo.AttackBox.Rect.Center;
		var away = (Center - (Vector2)hitInfo.By.Position).Normalized(-Vector2.UnitY);
		Dent(at);
		wobble = WobbleKick;
		yawVelocity += MathF.Sign(away.X) * SpinKick;
		rollVelocity += MathF.Sign(away.X) * SpinKick * 0.6f;
		pitchVelocity -= SpinKick * 0.4f;
		mouth = 1;
		face = Expression.Pained;
		Squish = new(0.75f, 1.25f);
		Game.Particles?.Emit(chunkEffect, at, 18, Calc.Angle(away + new Vector2(0, -0.5f)), 1, 0.9f);
		Velocity = away * KnockbackSpeed - Vector2.UnitY * KnockbackLift;
		health--;
		if (health > 0)
		{
			Game.HitStun(0.12f);
			Game.Shake(0.15f);
			AudioEvent("rig_puppet/hurt");
			Enter(State.Knocked);
			return;
		}
		Game.HitStun(0.22f);
		Game.Shake(0.3f, 2);
		AudioEvent("enemies/drone/death");
		Velocity *= 1.3f;
		Mask = Masks.None;
		Tags &= ~ActorTag.Flying;
		Enter(State.Falling);
		Game.KillForThisSession(this);
	}

	// Carves a dent where the hit lands on the visible surface, in head-local space so it turns with the head.
	void Dent(Vector2 worldPoint)
	{
		var across = (worldPoint - Center) / Radius * new Vector2(1, -1);
		if (!SculptedHead.Raycast(Assets.GetHeadShape(HeadShapeId), across, yaw, pitch, roll, out var point, out var normal))
			return;
		if (dentCount == dents.Length)
			Array.Copy(dents, 1, dents, 0, --dentCount);
		dents[dentCount++] = new(point + normal * (DentRadius - DentDepth), DentRadius);
	}

	SculptedHead CurrentHead()
	{
		head ??= new(Game.Manager.GraphicsDevice, outline: false);
		var near = Color.Lerp(HeadColor, RemainsTint, darken * 0.5f);
		var far = RigDraw.Multiply(near, new Color(0x705c8c));
		var center = (Vector2)RenderPosition;
		Span<BodyLight> lights = stackalloc BodyLight[SculptedBody.MaxLights];
		// Walls in reach press it flat and squash it; impacts squash it further, then it springs back.
		Span<Vector4> contacts = stackalloc Vector4[SculptedHead.MaxContacts];
		var contactCount = 0;
		for (var axis = 0; axis < 2; axis++)
		{
			var (toward, gap) = walls[axis];
			var wall = gap / Radius;
			var squash = MathF.Round(Math.Clamp(1 - MathF.Max(0, ContactReach - wall) * PressSquash - impact[axis], 0.55f, 1.3f) * 64) / 64;
			if (toward != Vector2.Zero && (wall < ContactReach || squash != 1))
				contacts[contactCount++] = new(toward, MathF.Min(wall, 10), squash);
		}
		head.Render(Assets.GetHeadShape(HeadShapeId), Radius, yaw, pitch, roll, face with { MouthOpen = mouth }, near, Color.Lerp(near, far, 0.5f), far,
			eyes > 0.02f ? Color.Lerp(Color.Black, Color.Lerp(EyeColor, Color.White, charge * 0.4f), eyes) : default, BodyShadeMode.Banded, new(0, 0.5f, 1, 0),
			lights[..HumanoidRigVisual.GatherLights(Game, lights, center, Radius)], center, dents.AsSpan(0, dentCount),
			wobble, wobblePhase, BruiseColor, contacts[..contactCount], Squish);
		return head;
	}

	Color Tint => Color.Lerp(Color.White, RemainsTint, darken);

	void DrawTexture(Batcher batcher, Texture? texture, Color color)
	{
		if (texture != null)
			batcher.Image(texture, Vector2.Zero, new Vector2(head!.Size / 2), Vector2.One, 0, color);
	}

	public override void Render(Batcher batcher)
	{
		if (SpriteBlinkHidden && state < State.Falling)
			return;
		DrawTexture(batcher, CurrentHead().ColorTexture, Tint);
	}

	public override void RenderNormals(Batcher batcher) => DrawTexture(batcher, CurrentHead().NormalTexture, Color.White);
	public override void RenderEmissive(Batcher batcher) => DrawTexture(batcher, CurrentHead().SpecularTexture, Tint);
	public override void RenderShadow(Batcher batcher, Color color) => DrawTexture(batcher, CurrentHead().MaskTexture, color);
	public override void RenderReflection(Batcher batcher, Color color) => RenderShadow(batcher, color);

	public override Rect GetVisualBounds() => Rect.CenteredAt(Center, new Vector2(Radius * 3.2f));

	// The lit eyes throw their color into the room.
	public override void ForEachLight(Action<Vector2, float, float, float, Color> action)
	{
		if (eyes > 0.05f)
			action(Center, Radius * 4 * eyes * (1 + charge * 0.6f), 0.9f * eyes, 8, EyeColor);
	}
}
