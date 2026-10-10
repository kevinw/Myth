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
// it touches.
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

	static float LavaHopSpeed => 260f;
	static float HeatHeight => 36f;
	static float HeatStrength => 0.8f;
	static int HeatCell => 4;
	static float EdgeBleed => 3f;
	// Draws only where nothing is drawn yet, so a bleed strip fills the gaps around inset
	// terrain without painting over the terrain itself.
	static readonly BlendMode UnderBlend = new(BlendOp.Add, BlendFactor.OneMinusDstAlpha, BlendFactor.One);
	static float LavaLightSpacing => 40f;
	static float LavaLightRadius => 72f;
	static float LavaLightIntensity => 0.9f;
	static float LavaLightHeight => 12f;
	static readonly Color LavaLightColor = new(1f, 0.45f, 0.15f, 1f);
	static readonly Color LavaBody = new(0.85f, 0.28f, 0.06f, 0.9f);
	static readonly Color LavaFoam = new(1f, 0.78f, 0.3f, 1f);

	public WaterSurface? Surface { get; private set; }
	public RectInt WorldRect => new(Position, Size);

	float fill;
	float surfaceY;
	float delayTimer;
	float age;
	bool active;
	float[] columnBottoms = [];
	Point2 tiles;
	int surfaceRow;
	bool[] wet = [];
	readonly List<Point2> fillQueue = [];
	readonly List<RectInt> cappedRects = [];
	readonly HashSet<Point2> bleedVisited = [];
	int steamEffect = -1;
	readonly List<Point2> touchedWater = [];

	public LiquidVolume()
	{
		Sprite = null;
		CollidesWithSolids = false;
		Hitbox = new(new RectInt(0, 0, 0, 0));
		// Behind the foreground terrain, so the terrain's emissive occluder erases any glow bleed it covers.
		Depth = Depths.ForegroundDualGrid + 1;
	}

	public override void Added()
	{
		base.Added();
		fill = Fill;
		Game.LiquidVolumes.Add(this);
		steamEffect = AcquireParticleEffect("steam");
	}

	public override void Destroyed()
	{
		Game.LiquidVolumes.Remove(this);
		ReleaseParticleEffect(ref steamEffect);
	}

	public void SetActive(Actor source, bool active)
	{
		if (this.active == active)
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

		var tide = TideAmplitude * MathF.Sin(Time.SecondsF * MathF.Tau / TidePeriod);
		surfaceY = Calc.Clamp(Position.Y + Size.Y * (1 - fill) + tide, Position.Y, Position.Y + Size.Y);

		var surface = EnsureSurface();
		surface.SurfaceY = surfaceY;
		(surface.Body, surface.Foam) = Kind == LiquidKind.Lava ? (LavaBody, LavaFoam) : (WaterSurface.BodyColor, WaterSurface.FoamColor);
		Flood(surface);
		surface.Update(Time.Delta);

		if (Kind == LiquidKind.Lava)
		{
			BurnPawns();
			BoilWater();
		}
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
		=> Room is { } room && room.IsSolidTile((Position - room.WorldBounds.TopLeft) / Game.TileSize + tile);

	// Liquid pours in from the surface row and spreads to every open tile it can reach below the
	// line, so it flows under overhangs while sealed pockets stay dry. Wet runs that hang off the
	// surface row ride the wave sim; the rest are capped bodies.
	void Flood(WaterSurface surface)
	{
		Array.Clear(wet);
		fillQueue.Clear();
		cappedRects.Clear();
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
			var bottom = surfaceY;
			for (var y = Math.Max(surfaceRow, 0); y < tiles.Y; y++)
			{
				if (!IsWet(new(x, y)))
					continue;
				var top = y;
				while (y + 1 < tiles.Y && IsWet(new(x, y + 1)))
					y++;
				if (top == surfaceRow)
					bottom = Position.Y + (y + 1) * Game.TileSize;
				else
					cappedRects.Add(new(Position + new Point2(x, top) * Game.TileSize, new Point2(1, y - top + 1) * Game.TileSize));
			}
			columnBottoms[x] = bottom;
			surface.SetColumnBottom(x, bottom);
		}
	}

	void TryWet(Point2 tile)
	{
		if (tile.X < 0 || tile.X >= tiles.X || tile.Y < surfaceRow || tile.Y >= tiles.Y || IsWet(tile) || IsSolid(tile))
			return;
		wet[tile.Y * tiles.X + tile.X] = true;
		fillQueue.Add(tile);
	}

	internal void RenderBody(Batcher batcher, float time)
	{
		if (Surface == null)
			return;
		foreach (var rect in cappedRects)
			batcher.Rect(rect, Surface.Body);
		Surface.Render(batcher, time);

		bleedVisited.Clear();
		batcher.PushBlend(UnderBlend);
		foreach (var tile in fillQueue)
			for (var y = -1; y <= 1; y++)
				for (var x = -1; x <= 1; x++)
					if (IsSolid(tile + new Point2(x, y)) && bleedVisited.Add(tile + new Point2(x, y)))
						BleedInto(batcher, tile + new Point2(x, y), Surface.Body);
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

	Rect TileRect(Point2 tile) => new(Position + tile * Game.TileSize, Point2.One * Game.TileSize);

	void BurnPawns()
	{
		foreach (var actor in Game.Actors)
		{
			if (actor is not Player player)
				continue;
			var center = (player.Hitbox.Rect + player.Position).Center;
			if (!Contains(center) || !player.HurtFromEnvironment(this))
				continue;
			player.Velocity.Y = -LavaHopSpeed;
			if (steamEffect >= 0)
				Game.Particles?.Emit(steamEffect, center, 10);
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
				action(new Vector2(Position.X + x, surfaceY - 2), LavaLightRadius, LavaLightIntensity, LavaLightHeight, LavaLightColor);
	}

	public override void Render(Batcher batcher)
	{
		if (!Game.Editing)
			return;
		var color = Kind == LiquidKind.Lava ? LavaBody : WaterSurface.BodyColor;
		batcher.Rect(new Rect(0, Size.Y * (1 - Fill), Size.X, Size.Y * Fill), color * 0.5f);
		var activeY = Size.Y * (1 - ActiveFill);
		batcher.Line(new Vector2(0, activeY), new Vector2(Size.X, activeY), 1, color);
	}

	public override void RenderEmissive(Batcher batcher)
	{
		if (Kind != LiquidKind.Lava || Surface == null)
			return;
		batcher.PushMatrix(-RenderPosition);
		RenderBody(batcher, Time.SecondsF);
		batcher.PopMatrix();
	}

	public override bool HasWorldDistortion => Kind == LiquidKind.Lava && Surface != null;

	// Heat shimmer: a band of wobbling offsets rising off every exposed stretch of lava. It eases in
	// just above the surface so the lava edge itself stays crisp, then fades out with height.
	public override void RenderWorldDistortion(Batcher batcher)
	{
		var time = Time.SecondsF;
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
