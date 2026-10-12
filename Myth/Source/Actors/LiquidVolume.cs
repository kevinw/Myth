using System.Numerics;
using Foster.Framework;

namespace Myth;

public enum LiquidKind
{
	Water,
	Lava,
}

// A basin whose liquid level moves between Fill and ActiveFill when an IActivatable source
// toggles it. Below the line the volume's liquid wins: lava boils off any authored water pool
// it touches. By default the liquid fills only the open space, cut along slopes; Submerge floods
// over the terrain too, which shows through near the surface.
[GameActor(Sprite = "Editor/particles", Icon = "water", Anchor = SpriteAnchor.TopLeft, Tags = ActorTag.System,
	BoundsW = Game.TileSize * 8, BoundsH = Game.TileSize * 6, LivePositionEditable = true, LiveResizable = true)]
public class LiquidVolume : Actor, IResizable, IActivatable
{
	public static readonly Point2 DefaultSize = new(Game.TileSize * 8, Game.TileSize * 6);

	public Point2 Size { get; set; } = DefaultSize;
	public LiquidKind Kind;
	[I(0, 1)] public float Fill = 0.5f;
	[I(0, 1)] public float ActiveFill = 1;
	[I(1, 240)] public float Speed = 24;
	[I(0, 10)] public float Delay;
	[I(0, 16)] public float TideAmplitude;
	[I(0.5f, 20)] public float TidePeriod = 6;
	public bool Submerge;

	internal static float LavaBurnPerSecond => 25f; // health points; full health lasts a bit over two seconds
	static float HeatHeight => 36f;
	static float HeatStrength => 0.8f;
	static int HeatCell => 4;
	static float EdgeBleed => 3f;
	static float SubmergedCover => 0.35f; // share of lava pixels over terrain at the surface...
	static float SubmergedClearDepth => 24f; // ...rising to all of them this far down
	// Draws only where nothing is drawn yet, so a bleed strip fills the gaps around inset
	// terrain without painting over the terrain itself.
	static readonly BlendMode UnderBlend = new(BlendOp.Add, BlendFactor.OneMinusDstAlpha, BlendFactor.One);
	static float LavaLightSpacing => 40f;
	static float LavaLightRadius => 72f;
	static float LavaLightIntensity => 0.9f;
	static float LavaLightHeight => 12f;
	static readonly Color LavaLightColor = new(1f, 0.45f, 0.15f, 1f);
	internal static readonly Color LavaBody = new(0.85f, 0.28f, 0.06f, 0.9f);
	internal static readonly Color LavaFoam = new(1f, 0.78f, 0.3f, 1f);
	internal static readonly Color LavaCrust = new(0.5f, 0.12f, 0.04f, 1f);
	static float LavaLipCrawl => 0.6f;
	static float BubbleStirHeat => 0.5f;
	static float LavaLightStirBoost => 0.8f;
	static float BubbleInterval => 0.19f;
	static float BubbleRiseRate => 2f;
	static float BubblePopImpulse => 15f;
	static float BubbleFlashTime => 0.15f;
	static float BubbleFlashRadius => 28f;
	static float BubbleFlashIntensity => 0.8f;

	// A capped stretch of one tile column. Its top and bottom run straight across the column, so a
	// slope cuts it along the slope's line.
	readonly record struct Slab(float Left, float TopLeft, float TopRight, float BottomLeft, float BottomRight, bool Terrain)
	{
		public Rect Bounds => Rect.Between(new(Left, MathF.Min(TopLeft, TopRight)), new(Left + Game.TileSize, MathF.Max(BottomLeft, BottomRight)));
	}

	struct Bubble
	{
		public float X, Age, Life, Radius;
		public readonly bool Popped => Age >= Life;
	}

	public WaterSurface? Surface { get; private set; }
	public RectInt WorldRect => new(Position, Size);

	float fill;
	float surfaceY;
	float delayTimer;
	float age;
	bool active;
	readonly HashSet<Actor> activators = [];
	float[] columnBottoms = [];
	Point2 tiles;
	int surfaceRow;
	bool[] wet = [];
	readonly List<Point2> fillQueue = [];
	readonly List<Slab> slabs = [];
	readonly HashSet<Point2> bleedVisited = [];
	int steamEffect = -1;
	readonly List<Point2> touchedWater = [];
	readonly List<Bubble> bubbles = [];
	int emberEffect = -1;
	float bubbleTimer;
	float flowTime;
	SoundInstance? poolSound;
	Rng rng = new((ulong)Environment.TickCount64);

	public LiquidVolume()
	{
		Sprite = null;
		CollidesWithSolids = false;
		Hitbox = new(new RectInt(0, 0, 0, 0));
	}

	public override void Added()
	{
		base.Added();
		fill = Fill;
		Game.LiquidVolumes.Add(this);
		steamEffect = AcquireParticleEffect("steam");
		emberEffect = AcquireParticleEffect("embers");
	}

	public override void Destroyed()
	{
		Game.LiquidVolumes.Remove(this);
		ReleaseParticleEffect(ref steamEffect);
		ReleaseParticleEffect(ref emberEffect);
		StopAndReleaseSound(ref poolSound, true);
	}

	// Active while any source holds it, so two buttons on one basin don't fight.
	public void SetActive(Actor source, bool active)
	{
		if (!(active ? activators.Add(source) : activators.Remove(source)) || this.active == activators.Count > 0)
			return;
		this.active = active;
		delayTimer = Delay;
		// A latched source re-fires on room load; arrive already settled instead of replaying the rise.
		if (source is IActivationSource { ActivationIsPersistent: true } && age < 0.1f)
		{
			fill = ActiveFill;
			delayTimer = 0;
		}
	}

	public bool Contains(Point2 worldPoint)
		=> WorldRect.Contains(worldPoint) && worldPoint.Y >= surfaceY && IsWet((worldPoint - Position) / Game.TileSize);

	bool IsWet(Point2 tile)
		=> tile.X >= 0 && tile.Y >= 0 && tile.X < tiles.X && tile.Y < tiles.Y && wet[tile.Y * tiles.X + tile.X];

	public override void Update()
	{
		base.Update();
		age += Time.Delta;
		if (delayTimer > 0)
			delayTimer -= Time.Delta;
		else
			fill = Calc.Approach(fill, active ? ActiveFill : Fill, Speed / MathF.Max(Size.Y, 1) * Time.Delta);
		var surface = Settle();
		surface.Update(Time.Delta);

		if (Kind == LiquidKind.Lava)
		{
			BurnPawns();
			BoilWater();
			UpdateBubbles(surface);
			UpdatePoolSound();
		}
		else
			bubbles.Clear();
		flowTime += Time.Delta * (LevelMoving ? BubbleRiseRate : 1);
	}

	// Places the level and floods it. The editor runs only this, with no time passed, so it previews
	// the room exactly as it starts.
	WaterSurface Settle()
	{
		// Behind the foreground terrain, so the terrain's emissive occluder erases any glow bleed it
		// covers; a submerging volume glows over the terrain instead.
		Depth = Submerge ? Depths.ForegroundProp : Depths.ForegroundDualGrid + 1;
		surfaceY = Calc.Clamp(Position.Y + Size.Y * (1 - fill) + Tide(age), Position.Y, Position.Y + Size.Y);
		var surface = EnsureSurface();
		surface.SurfaceY = surfaceY;
		(surface.Body, surface.Foam, surface.LipCrawl) = Kind == LiquidKind.Lava
			? (LavaBody, LavaFoam, LavaLipCrawl)
			: (WaterSurface.BodyColor, WaterSurface.FoamColor, 0);
		Flood(surface);
		return surface;
	}

	float Tide(float time) => TideAmplitude * MathF.Sin(time * MathF.Tau / TidePeriod);

	bool LevelMoving => delayTimer <= 0 && fill != (active ? ActiveFill : Fill);

	// Bubbles swell on the surface, then pop: the pop dimples the wave sim, throws embers, flares
	// a light and plays a sound. They churn faster while the level is moving.
	void UpdateBubbles(WaterSurface surface)
	{
		bubbleTimer -= Time.Delta * (LevelMoving ? BubbleRiseRate : 1);
		if (bubbleTimer <= 0)
		{
			bubbleTimer = BubbleInterval * rng.Float(0.3f, 1.7f);
			var x = rng.Float(Size.X);
			if (IsWet(new((int)x / Game.TileSize, surfaceRow)) && (Submerge || surface.BottomAt(Position.X + x) > surfaceY))
				bubbles.Add(new() { X = Position.X + x, Life = rng.Float(0.35f, 0.7f), Radius = rng.Float(1.5f, 3.5f) });
		}

		for (var i = bubbles.Count - 1; i >= 0; i--)
		{
			var bubble = bubbles[i];
			var wasSwelling = !bubble.Popped;
			bubble.Age += Time.Delta;
			if (wasSwelling && bubble.Popped)
				Pop(bubble, surface);
			if (bubble.Age >= bubble.Life + BubbleFlashTime)
				bubbles.RemoveAt(i);
			else
				bubbles[i] = bubble;
		}
	}

	void Pop(Bubble bubble, WaterSurface surface)
	{
		surface.Splash(bubble.X, BubblePopImpulse * bubble.Radius);
		Game.LiquidStir.Stir(new Vector2(bubble.X, surface.SurfaceY + 2), bubble.Radius * 3, BubbleStirHeat);
		var top = new Vector2(bubble.X, surface.TopAt(bubble.X, Time.SecondsF));
		if (emberEffect >= 0)
			Game.Particles?.Emit(emberEffect, top, (int)bubble.Radius + 1);
		if (Room == Game.CurrentRoom)
			Game.PlayAudioEvent("liquid/bubble", "Size", bubble.Radius, top);
	}

	// One looping voice sits on the exposed surface point nearest the player, so a wide pool pans
	// and attenuates from its closest edge without stacking copies of the loop.
	void UpdatePoolSound()
	{
		Vector2? nearest = null;
		var exposed = 0;
		var player = Game.GetFirst(Masks.Player)?.Position;
		if (Room == Game.CurrentRoom && !Game.Editing)
		{
			var listener = player ?? Game.Camera + new Vector2(Game.Width, Game.Height) / 2;
			for (var column = 0; column < tiles.X; column++)
			{
				if (!IsWet(new(column, surfaceRow)))
					continue;
				exposed++;
				var left = Position.X + column * Game.TileSize;
				var point = new Vector2(Calc.Clamp(listener.X, left, left + Game.TileSize), surfaceY);
				if (nearest is not { } best || Vector2.DistanceSquared(point, listener) < Vector2.DistanceSquared(best, listener))
					nearest = point;
			}
		}
		if (nearest is not { } at)
		{
			StopAndReleaseSound(ref poolSound);
			return;
		}
		poolSound ??= Game.CreateAudioEvent("lava/pool", at, start: true);
		Game.SetAudioEventPosition(poolSound, at, player);
		Game.SetAudioEventParameter(poolSound, "Size", exposed * Game.TileSize);
		Game.SetAudioEventParameter(poolSound, "Churn", Game.LiquidStir.At(at + Vector2.UnitY * 2) + (LevelMoving ? 1 : 0));
	}

	WaterSurface EnsureSurface()
	{
		var size = Point2.Max(Size / Game.TileSize, Point2.One);
		if (Surface is { } existing && existing.Left == Position.X && tiles == size)
			return existing;
		tiles = size;
		wet = new bool[size.X * size.Y];
		columnBottoms = new float[size.X];
		Array.Fill(columnBottoms, Position.Y + Size.Y);
		return Surface = new WaterSurface(Position.X, surfaceY, columnBottoms);
	}

	bool IsSolid(Point2 tile)
		=> Room is { } room && room.IsSolidTile(RoomTile(room, tile));

	bool TryGetSlope(Point2 tile, out Vector2 left, out Vector2 right, out bool floor)
	{
		(left, right, floor) = (default, default, false);
		if (Room is not { } room || !room.TryGetSlopeTile(RoomTile(room, tile), out var slope))
			return false;
		room.GetSlopeSurface(slope, out left, out right);
		floor = slope.Fill == Room.SlopeFill.BelowLine;
		return true;
	}

	Point2 RoomTile(Room room, Point2 tile) => (Position - room.WorldBounds.TopLeft) / Game.TileSize + tile;

	// Liquid pours in from the surface row and spreads to every open tile it can reach below the
	// line, so it flows under overhangs while sealed pockets stay dry (Submerge floods every tile).
	// Each column is then walked top-down into open stretches, cut at terrain and along slope lines:
	// the stretch hanging off the surface rides the wave sim, the rest are capped slabs. Submerged
	// terrain becomes slabs of its own.
	bool open, openAtSurface;
	float openLeft, openRight, floorLeft, floorRight, columnLeft;

	void Flood(WaterSurface surface)
	{
		Array.Clear(wet);
		fillQueue.Clear();
		slabs.Clear();
		surfaceRow = (int)MathF.Floor((surfaceY - Position.Y) / Game.TileSize);
		if (surfaceRow < tiles.Y)
			for (var x = 0; x < tiles.X; x++)
				TryWet(new(x, surfaceRow));
		for (var head = 0; head < fillQueue.Count; head++)
		{
			var tile = fillQueue[head];
			TryWet(tile + Point2.Left);
			TryWet(tile + Point2.Right);
			TryWet(tile + Point2.Down);
			if (tile.Y > surfaceRow)
				TryWet(tile + Point2.Up);
		}

		for (var x = 0; x < tiles.X; x++)
		{
			columnLeft = Position.X + x * Game.TileSize;
			(floorLeft, floorRight) = (surfaceY, surfaceY);
			for (var y = Math.Max(surfaceRow, 0); y < tiles.Y; y++)
			{
				var tile = new Point2(x, y);
				var top = Position.Y + y * Game.TileSize;
				var bottom = top + Game.TileSize;
				var surfaceTop = y == surfaceRow ? surfaceY : top;
				if (!IsWet(tile))
					Close(top, top);
				else if (IsSolid(tile))
				{
					Close(top, top);
					slabs.Add(new(columnLeft, surfaceTop, surfaceTop, bottom, bottom, true));
				}
				else if (TryGetSlope(tile, out var left, out var right, out var floor) && floor)
				{
					Open(surfaceTop, surfaceTop, y == surfaceRow);
					Close(left.Y, right.Y);
					AddSubmerged(left.Y, right.Y, bottom, bottom);
				}
				else if (TryGetSlope(tile, out left, out right, out _))
				{
					Close(top, top);
					AddSubmerged(surfaceTop, surfaceTop, left.Y, right.Y);
					Open(MathF.Max(left.Y, surfaceY), MathF.Max(right.Y, surfaceY), false);
				}
				else
					Open(surfaceTop, surfaceTop, y == surfaceRow);
			}
			Close(Position.Y + Size.Y, Position.Y + Size.Y);
			surface.SetColumn(x, floorLeft, floorRight, Submerge && IsWet(new(x, surfaceRow)));
		}
	}

	void Open(float left, float right, bool atSurface)
	{
		if (open)
			return;
		(open, openLeft, openRight, openAtSurface) = (true, left, right, atSurface);
	}

	void Close(float left, float right)
	{
		if (!open)
			return;
		open = false;
		if (openAtSurface)
			(floorLeft, floorRight) = (left, right);
		else
			slabs.Add(new(columnLeft, openLeft, openRight, left, right, false));
	}

	void AddSubmerged(float topLeft, float topRight, float bottomLeft, float bottomRight)
	{
		if (Submerge)
			slabs.Add(new(columnLeft, MathF.Max(topLeft, surfaceY), MathF.Max(topRight, surfaceY), bottomLeft, bottomRight, true));
	}

	void TryWet(Point2 tile)
	{
		if (tile.X < 0 || tile.X >= tiles.X || tile.Y < surfaceRow || tile.Y >= tiles.Y || IsWet(tile) || !Submerge && IsSolid(tile))
			return;
		wet[tile.Y * tiles.X + tile.X] = true;
		fillQueue.Add(tile);
	}

	void RenderSlab(Batcher batcher, Texture stir, in Slab slab)
	{
		var right = slab.Left + Game.TileSize;
		Vector2 a = new(slab.Left, slab.TopLeft), b = new(right, slab.TopRight), c = new(right, slab.BottomRight), d = new(slab.Left, slab.BottomLeft);
		batcher.Quad(stir, a, b, c, d, a, b, c, d, SlabDepth(a.Y, slab.Terrain), SlabDepth(b.Y, slab.Terrain), SlabDepth(c.Y, slab.Terrain), SlabDepth(d.Y, slab.Terrain));
	}

	Color SlabDepth(float y, bool terrain)
		=> LiquidBody.Depth(y - surfaceY, terrain ? Calc.Lerp(SubmergedCover, 1, Calc.Clamp((y - surfaceY) / SubmergedClearDepth, 0, 1)) : 1);

	internal void RenderBody(Batcher batcher, float time)
	{
		if (Surface == null)
			return;
		var stir = LiquidBody.Push(batcher, Kind == LiquidKind.Lava ? LiquidBody.Lava : LiquidBody.Water, flowTime, Game.LiquidStir);
		foreach (var slab in slabs)
			RenderSlab(batcher, stir, slab);
		Surface.RenderBody(batcher, time, stir);
		LiquidBody.Pop(batcher);
		Surface.RenderLip(batcher, time);
		foreach (var bubble in bubbles)
		{
			if (bubble.Popped)
				continue;
			var radius = bubble.Radius * MathF.Sqrt(bubble.Age / bubble.Life);
			var center = new Vector2(bubble.X, Surface.TopAt(bubble.X, time));
			batcher.SemiCircle(center, MathF.PI, MathF.Tau, radius + 1, 8, Surface.Foam);
			batcher.SemiCircle(center, MathF.PI, MathF.Tau, radius, 8, LavaCrust);
		}

		if (Submerge)
			return;
		bleedVisited.Clear();
		batcher.PushBlend(UnderBlend);
		foreach (var tile in fillQueue)
		{
			if (TryGetSlope(tile, out var left, out var right, out var floor))
				BleedUnder(batcher, left, right, Vector2.UnitY * (floor ? EdgeBleed : -EdgeBleed), Surface.Body);
			for (var y = -1; y <= 1; y++)
				for (var x = -1; x <= 1; x++)
					if (IsSolid(tile + new Point2(x, y)) && bleedVisited.Add(tile + new Point2(x, y)))
						BleedInto(batcher, tile + new Point2(x, y), Surface.Body);
		}
		batcher.PopBlend();
	}

	// Each solid tile bordering the liquid gets one set of non-overlapping edge strips and corners,
	// so the under-blend never stacks into a brighter rim.
	void BleedInto(Batcher batcher, Point2 solid, Color color)
	{
		var r = TileRect(solid);
		var b = EdgeBleed;
		var left = IsWet(solid + Point2.Left);
		var right = IsWet(solid + Point2.Right);
		var up = IsWet(solid + Point2.Up);
		var down = IsWet(solid + Point2.Down);
		var innerX = r.X + (left ? b : 0);
		var innerWidth = r.Width - (left ? b : 0) - (right ? b : 0);
		if (left) Bleed(batcher, new(r.X, r.Y, b, r.Height), color);
		if (right) Bleed(batcher, new(r.Right - b, r.Y, b, r.Height), color);
		if (up) Bleed(batcher, new(innerX, r.Y, innerWidth, b), color);
		if (down) Bleed(batcher, new(innerX, r.Bottom - b, innerWidth, b), color);
		if (!left && !up && IsWet(solid + new Point2(-1, -1))) Bleed(batcher, new(r.X, r.Y, b, b), color);
		if (!right && !up && IsWet(solid + new Point2(1, -1))) Bleed(batcher, new(r.Right - b, r.Y, b, b), color);
		if (!left && !down && IsWet(solid + new Point2(-1, 1))) Bleed(batcher, new(r.X, r.Bottom - b, b, b), color);
		if (!right && !down && IsWet(solid + new Point2(1, 1))) Bleed(batcher, new(r.Right - b, r.Bottom - b, b, b), color);
	}

	void Bleed(Batcher batcher, Rect rect, Color color)
	{
		var top = MathF.Max(rect.Y, surfaceY);
		if (rect.Bottom > top && rect.Width > 0)
			batcher.Rect(new Rect(rect.X, top, rect.Width, rect.Bottom - top), color);
	}

	void BleedUnder(Batcher batcher, Vector2 left, Vector2 right, Vector2 offset, Color color)
	{
		Vector2 Clip(Vector2 p) => p with { Y = MathF.Max(p.Y, surfaceY) };
		batcher.Quad(Clip(left), Clip(right), Clip(right + offset), Clip(left + offset), color);
	}

	Rect TileRect(Point2 tile) => new(Position + tile * Game.TileSize, Point2.One * Game.TileSize);

	void BurnPawns()
	{
		foreach (var actor in Game.Actors)
		{
			if (actor is not Player player)
				continue;
			var center = (player.Hitbox.Rect + player.Position).Center;
			if (!Contains(center))
				continue;
			// A puff of steam per point burned, so the sizzle tracks the drain.
			var burned = player.Burn(LavaBurnPerSecond * Time.Delta, this);
			if (burned > 0 && steamEffect >= 0)
				Game.Particles?.Emit(steamEffect, center, burned);
		}
	}

	void BoilWater()
	{
		if (Room is not { } room || room != Game.CurrentRoom)
			return;

		touchedWater.Clear();
		var offset = (Position - room.WorldBounds.TopLeft) / Game.TileSize;
		foreach (var tile in fillQueue)
			if (room.IsWaterTile(offset + tile))
				touchedWater.Add(offset + tile);
		Game.EvaporateWater(touchedWater, steamEffect);
	}

	public override void ForEachLight(Action<Vector2, float, float, float, Color> action)
	{
		if (Kind != LiquidKind.Lava || Surface == null)
			return;
		for (var x = LavaLightSpacing * 0.5f; x < Size.X; x += LavaLightSpacing)
			if (IsWet(new((int)x / Game.TileSize, surfaceRow)))
				action(new Vector2(Position.X + x, surfaceY - 2), LavaLightRadius,
					LavaLightIntensity * (1 + Game.LiquidStir.At(new Vector2(Position.X + x, surfaceY + 2)) * LavaLightStirBoost), LavaLightHeight, LavaLightColor);
		foreach (var bubble in bubbles)
			if (bubble.Popped)
				action(new Vector2(bubble.X, surfaceY - 4), BubbleFlashRadius,
					BubbleFlashIntensity * (1 - (bubble.Age - bubble.Life) / BubbleFlashTime), LavaLightHeight, LavaFoam);
	}

	public override void Render(Batcher batcher)
	{
		// Not simulating -- the editor, or a room preview, which renders without ever updating -- shows the
		// room as it starts.
		if (Game.Editing || age == 0)
		{
			fill = Fill;
			Settle();
		}
		if (!Game.Editing)
			return;
#if EDITOR
		if (Game.Manager.SelectedRoomActorId != ActorId)
			return;
#endif
		// Guides for where the level goes from here: the activated level, and the tide's high and low marks.
		var color = Kind == LiquidKind.Lava ? LavaFoam : WaterSurface.FoamColor;
		var restY = Size.Y * (1 - Fill);
		batcher.Line(new Vector2(0, Size.Y * (1 - ActiveFill)), new Vector2(Size.X, Size.Y * (1 - ActiveFill)), 1, color);
		if (TideAmplitude > 0)
			foreach (var y in (ReadOnlySpan<float>)[restY - TideAmplitude, restY + TideAmplitude])
				batcher.Line(new Vector2(0, y), new Vector2(Size.X, y), 1, color * 0.4f);
	}

	public override void RenderEmissive(Batcher batcher)
	{
		if (Kind != LiquidKind.Lava || Surface == null)
			return;
		batcher.PushMatrix(-RenderPosition);
		RenderBody(batcher, Time.SecondsF);
		batcher.PopMatrix();
	}

	public override bool HasWorldDistortion => Surface != null;

	// Water refracts whatever is behind or inside it. Lava instead throws heat shimmer: a band of
	// wobbling offsets rising off every exposed stretch, easing in just above the surface so the lava
	// edge itself stays crisp, then fading out with height.
	public override void RenderWorldDistortion(Batcher batcher)
	{
		var time = Time.SecondsF;
		if (Kind == LiquidKind.Water)
		{
			batcher.PushMatrix(-RenderPosition);
			foreach (var slab in slabs)
				LiquidBody.Refract(batcher, slab.Bounds, time, Game.LiquidStir);
			Surface!.RenderRefraction(batcher, time, Game.LiquidStir);
			batcher.PopMatrix();
			return;
		}
		var surface = surfaceY - Position.Y;
		for (var column = 0; column < tiles.X; column++)
		{
			if (!IsWet(new(column, surfaceRow)))
				continue;
			for (var x = column * Game.TileSize; x < (column + 1) * Game.TileSize; x += HeatCell)
				for (var y = surface - HeatHeight; y < surface; y += HeatCell)
					batcher.Rect(new Rect(x, y, HeatCell, HeatCell),
						Heat(x, y, surface, time), Heat(x + HeatCell, y, surface, time),
						Heat(x + HeatCell, y + HeatCell, surface, time), Heat(x, y + HeatCell, surface, time));
		}
	}

	static Color Heat(float x, float y, float surface, float time)
	{
		var height = Calc.Clamp((surface - y) / HeatHeight, 0, 1);
		var fade = Calc.Clamp(height / 0.15f, 0, 1) * (1 - height) * (1 - height);
		var direction = new Vector2(
			MathF.Sin(x * 0.21f + y * 0.35f - time * 5.3f),
			0.4f * MathF.Sin(x * 0.13f + y * 0.2f - time * 3.1f));
		return EncodeWorldDistortion(direction, HeatStrength * fade);
	}

	public override void RenderNormals(Batcher batcher) { }
	public override void RenderShadow(Batcher batcher, Color color) { }
	public override void RenderReflection(Batcher batcher, Color color) { }
	public override bool CastsSpriteShadow => false;
	public override bool CastsReflection => false;
}
