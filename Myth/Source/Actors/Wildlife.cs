using System.Numerics;
using Foster.Framework;

namespace Myth;

[GameActor(Sprite = "Editor/wildlife", BoundsX = -4, BoundsY = -4, BoundsW = 8, BoundsH = 8, Tags = ActorTag.Decoration)]
public class Wildlife : Actor
{
	public int FishCount = 12;
	public int BugCount = 10;
	public int FlyCount = 6;

	static int MaxCount => 64;
	static float FishSpeed => 10f;
	static float FishFleeSpeed => 24f;
	static float BugFleeRadius => 28f;
	static float FishFleeRadius => 40f;
	static float FlyFleeRadius => 32f;
	static float BugGravity => 90f;
	static Color FishColor => 0x8ad4cc;
	static Color FishTailColor => 0x568f91;
	static Color BugColor => 0xc6a15b;
	static Color FlyColor => 0xd8d1ba;
	static Color FlyWingColor => new(0xd8d1ba, 0.45f);

	struct FishState
	{
		public Vector2 Position;
		public Vector2 Velocity;
		public float TurnTimer;
		public float Phase;
	}

	readonly struct GrassSurface(Vector2 left, Vector2 right)
	{
		public readonly Vector2 Left = left;
		public readonly Vector2 Right = right;
		public float YAt(float x) => Calc.Lerp(Left.Y, Right.Y, Calc.ClampedMap(x, Left.X, Right.X));
	}

	struct BugState
	{
		public GrassSurface Surface;
		public Vector2 Position;
		public Vector2 Velocity;
		public float HopTimer;
		public bool Grounded;
	}

	struct FlyState
	{
		public Vector2 Position;
		public Vector2 Velocity;
		public float TurnTimer;
		public float Phase;
	}

	static readonly List<Point2> waterCells = [];
	static readonly List<Point2> airCells = [];
	static readonly List<GrassSurface> grassSurfaces = [];
	FishState[] fish = [];
	BugState[] bugs = [];
	FlyState[] flies = [];
	Rng rng;

	public Wildlife()
	{
		CollidesWithSolids = false;
		Depth = 1;
	}

	public override bool CastsSpriteShadow => false;
	public override bool CastsReflection => false;

	public override void Added()
	{
		base.Added();
		if (Game.CurrentRoom is not { } room)
			return;

		var tile = (Position - room.WorldBounds.TopLeft) / Game.TileSize;
		rng = new(Factory.StableVariantSeed(room, nameof(Wildlife), tile));
		BuildSpawnCandidates(room);
		SpawnFish(room);
		SpawnBugs();
		SpawnFlies(room);
	}

	void BuildSpawnCandidates(Room room)
	{
		waterCells.Clear();
		airCells.Clear();
		grassSurfaces.Clear();

		for (var y = 0; y < room.Size.Y; y++)
			for (var x = 0; x < room.Size.X; x++)
			{
				var tile = new Point2(x, y);
				var entry = room.GetEntry(tile);
				if (entry is { SurfaceType: Factory.WaterSurfaceType })
					waterCells.Add(tile);
				else if (entry is not { BlocksMovement: true })
					airCells.Add(tile);

				if (entry is not { BlocksMovement: true, SurfaceType: Factory.DefaultSurfaceType }
					|| room.GetEntry(tile + Point2.Up) is { BlocksMovement: true })
					continue;

				if (room.TryGetSlopeTile(tile, out var slope))
				{
					if (slope.Fill != Room.SlopeFill.BelowLine)
						continue;
					room.GetSlopeSurface(slope, out var left, out var right);
					grassSurfaces.Add(new(left, right));
				}
				else
				{
					var left = (Vector2)room.WorldBounds.TopLeft + tile * Game.TileSize;
					grassSurfaces.Add(new(left, left + new Vector2(Game.TileSize, 0)));
				}
			}
	}

	void SpawnFish(Room room)
	{
		fish = new FishState[waterCells.Count == 0 ? 0 : Math.Clamp(FishCount, 0, MaxCount)];
		for (var i = 0; i < fish.Length; i++)
		{
			var cell = waterCells[rng.Int(waterCells.Count)];
			var topLeft = (Vector2)room.WorldBounds.TopLeft + cell * Game.TileSize;
			var direction = rng.Boolean() ? -1f : 1f;
			fish[i] = new()
			{
				Position = topLeft + new Vector2(rng.Float(1, Game.TileSize - 1), rng.Float(1, Game.TileSize - 1)),
				Velocity = new Vector2(direction, rng.Float(-0.3f, 0.3f)).Normalized() * FishSpeed,
				TurnTimer = rng.Float(0.3f, 2.0f),
				Phase = rng.Float(MathF.Tau),
			};
		}
	}

	void SpawnBugs()
	{
		bugs = new BugState[grassSurfaces.Count == 0 ? 0 : Math.Clamp(BugCount, 0, MaxCount)];
		for (var i = 0; i < bugs.Length; i++)
		{
			var surface = grassSurfaces[rng.Int(grassSurfaces.Count)];
			var x = rng.Float(surface.Left.X + 1, surface.Right.X - 1);
			bugs[i] = new()
			{
				Surface = surface,
				Position = new(x, surface.YAt(x)),
				HopTimer = rng.Float(0.2f, 2.5f),
				Grounded = true,
			};
		}
	}

	void SpawnFlies(Room room)
	{
		flies = new FlyState[airCells.Count == 0 ? 0 : Math.Clamp(FlyCount, 0, MaxCount)];
		for (var i = 0; i < flies.Length; i++)
		{
			var cell = airCells[rng.Int(airCells.Count)];
			var topLeft = (Vector2)room.WorldBounds.TopLeft + cell * Game.TileSize;
			flies[i] = new()
			{
				Position = topLeft + new Vector2(rng.Float(1, Game.TileSize - 1), rng.Float(1, Game.TileSize - 1)),
				Velocity = rng.Vec2() * rng.Float(5, 12),
				TurnTimer = rng.Float(0.05f, 0.5f),
				Phase = rng.Float(MathF.Tau),
			};
		}
	}

	public override void Update()
	{
		base.Update();
		if (Game.CurrentRoom is not { } room)
			return;

		var playerActor = Game.GetFirst<Player>();
		var player = playerActor != null ? (Vector2?)playerActor.Position : null;
		for (var i = 0; i < fish.Length; i++)
			UpdateFish(ref fish[i], player);
		for (var i = 0; i < bugs.Length; i++)
			UpdateBug(ref bugs[i], player);
		for (var i = 0; i < flies.Length; i++)
			UpdateFly(ref flies[i], room, player);
	}

	void UpdateFish(ref FishState state, Vector2? player)
	{
		state.TurnTimer -= Time.Delta;
		var away = player.HasValue ? state.Position - player.Value : Vector2.Zero;
		if (player.HasValue && away.LengthSquared() < FishFleeRadius * FishFleeRadius)
			state.Velocity = Calc.Approach(state.Velocity, away.Normalized(state.Velocity.Normalized()) * FishFleeSpeed, 80 * Time.Delta);
		else if (state.TurnTimer <= 0)
		{
			var direction = rng.Boolean() ? -1f : 1f;
			state.Velocity = new Vector2(direction, rng.Float(-0.35f, 0.35f)).Normalized() * FishSpeed;
			state.TurnTimer = rng.Float(0.6f, 2.4f);
		}

		var next = state.Position + state.Velocity * Time.Delta;
		if (!Game.IsWaterAt(next.RoundToPoint2()))
		{
			var horizontal = state.Position + new Vector2(state.Velocity.X * Time.Delta, 0);
			var vertical = state.Position + new Vector2(0, state.Velocity.Y * Time.Delta);
			if (!Game.IsWaterAt(horizontal.RoundToPoint2())) state.Velocity.X = -state.Velocity.X;
			if (!Game.IsWaterAt(vertical.RoundToPoint2())) state.Velocity.Y = -state.Velocity.Y;
			state.TurnTimer = rng.Float(0.2f, 0.8f);
			next = state.Position + state.Velocity * Time.Delta;
		}
		if (Game.IsWaterAt(next.RoundToPoint2()))
			state.Position = next;
		state.Phase += Time.Delta * 11;
	}

	void UpdateBug(ref BugState state, Vector2? player)
	{
		var away = player.HasValue ? state.Position - player.Value : Vector2.Zero;
		var threatened = player.HasValue && away.LengthSquared() < BugFleeRadius * BugFleeRadius;
		if (state.Grounded)
		{
			state.HopTimer -= Time.Delta;
			if (state.HopTimer > 0 && !threatened)
				return;

			var direction = threatened && MathF.Abs(away.X) > 0.1f ? MathF.Sign(away.X) : rng.Boolean() ? -1f : 1f;
			state.Velocity = new Vector2(direction * rng.Float(7, 13), -rng.Float(20, 29));
			state.Grounded = false;
			state.HopTimer = rng.Float(0.5f, 2.8f);
		}
		else
		{
			if (threatened)
				state.Velocity.X = Calc.Approach(state.Velocity.X, MathF.Sign(away.X) * 15, 45 * Time.Delta);
			state.Velocity.Y += BugGravity * Time.Delta;
			var next = state.Position + state.Velocity * Time.Delta;
			if (next.X < state.Surface.Left.X || next.X > state.Surface.Right.X)
			{
				next.X = Math.Clamp(next.X, state.Surface.Left.X, state.Surface.Right.X);
				state.Velocity.X *= -0.4f;
			}
			var ground = state.Surface.YAt(next.X);
			if (state.Velocity.Y >= 0 && next.Y >= ground)
			{
				next.Y = ground;
				state.Velocity = Vector2.Zero;
				state.Grounded = true;
			}
			state.Position = next;
		}
	}

	void UpdateFly(ref FlyState state, Room room, Vector2? player)
	{
		state.TurnTimer -= Time.Delta;
		var away = player.HasValue ? state.Position - player.Value : Vector2.Zero;
		if (player.HasValue && away.LengthSquared() < FlyFleeRadius * FlyFleeRadius)
			state.Velocity = Calc.Approach(state.Velocity, away.Normalized(state.Velocity.Normalized()) * 28, 100 * Time.Delta);
		else if (state.TurnTimer <= 0)
		{
			state.Velocity = rng.Vec2() * rng.Float(5, 18);
			state.TurnTimer = rng.Float(0.05f, 0.45f);
		}

		var next = state.Position + state.Velocity * Time.Delta;
		if (!room.WorldBounds.Inflate(-2).Contains(next) || Game.OverlapsRoomCollision(new RectInt(next.FloorToPoint2(), 1, 1)))
		{
			state.Velocity = -state.Velocity;
			state.TurnTimer = 0;
		}
		else
			state.Position = next;
		state.Phase += Time.Delta * 28;
	}

	public override void Render(Batcher batcher)
	{
		for (var i = 0; i < fish.Length; i++)
		{
			ref var state = ref fish[i];
			var at = (state.Position - (Vector2)Position).Round();
			var facing = state.Velocity.X < 0 ? -1 : 1;
			batcher.Rect(facing > 0 ? at.X - 1 : at.X, at.Y, 2, 1, FishColor);
			batcher.Rect(at.X - facing * 2, at.Y + (MathF.Sin(state.Phase) > 0 ? -1 : 1), 1, 1, FishTailColor);
		}

		for (var i = 0; i < bugs.Length; i++)
		{
			var at = (bugs[i].Position - (Vector2)Position).Round();
			batcher.Rect(at.X - 1, at.Y - 1, 2, 1, BugColor);
			if (bugs[i].Grounded)
				batcher.Rect(at.X, at.Y, 1, 1, BugColor * 0.7f);
		}

		for (var i = 0; i < flies.Length; i++)
		{
			var at = (flies[i].Position - (Vector2)Position).Round();
			batcher.Rect(at.X, at.Y, 1, 1, FlyColor);
			var wing = MathF.Sin(flies[i].Phase) > 0 ? -1 : 1;
			batcher.Rect(at.X + wing, at.Y, 1, 1, FlyWingColor);
		}
	}

	public override void RenderNormals(Batcher batcher) { }
	public override void RenderShadow(Batcher batcher, Color color) { }
	public override void RenderReflection(Batcher batcher, Color color) { }
	public override void RenderEmissive(Batcher batcher) { }
	public override void RenderEmissiveOccluder(Batcher batcher) { }
	public override Rect GetVisualBounds() => Game.CurrentRoom?.WorldBounds ?? new Rect(Position.X, Position.Y, 1, 1);
}
