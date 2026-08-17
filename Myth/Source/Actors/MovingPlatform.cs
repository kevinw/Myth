using Foster.Framework;
using System.Numerics;
namespace Myth;

[GameActor(Tags = ActorTag.Obstacle, Sprite = "Editor/moving_platform", Anchor = SpriteAnchor.TopLeft)]
class MovingPlatform : Actor, IResizable, IShapeOccluder
{
	SoundInstance? moveSound;
	public static readonly Point2 DefaultSize = new(48, 32);
	public static readonly Point2 DefaultCircleCenterOffset = new(0, 80);

	public Point2 Size { get; set; } = DefaultSize;

	// Dual (marching-squares) tileset the platform is skinned with -- supplies color + normals so it
	// lights and reflects like solid tiles. Editor-pickable; see Manager.Editor material picker.
	public string Material = "metal-dual";
	[I(0, 5)] public float WaitTime = WAIT_TIME;
	const float WAIT_TIME = 0.856f;
	[I(5, 160)] public float Speed = 30f;
	[I(0, 24)] public float StartFriction = 8f;
	float currentSpeed;
	[I(8, 512)] public int TravelDistance = 80;
	public bool ActivatedByWeight;
	// Weight-activated platforms travel back to where they started once nothing is standing on
	// them; turn this off for a one-way lift that parks at the far end.
	public bool ReturnsWhenUnweighted = true;
	public bool RenderActivationLights;

	public enum MovementMode
	{
		Horizontal,
		Vertical,
		Circle
	}

	const MovementMode DEFAULT_MODE = MovementMode.Horizontal;
	public MovementMode Mode = DEFAULT_MODE;

	// Room-local center of the circular orbit. Null places the initial authoring handle below us.
	public Point2? CircleCenter { get; set; }
	int direction;
	float circleAngle;
	Vector2 circleOffset;
	Vector2 circleCenterOffset;
	float waitTimer = WAIT_TIME;
	bool activationLightsOn;
	Vector2 moveRemainder;             // sub-pixel travel accumulator (we move in whole pixels)
	static readonly List<Actor> riders = [];  // reusable scratch: actors standing on top this frame
	Point2 originalPosition;
	RectInt routeBounds;

	internal bool SupportsPlatformNav => Mode == MovementMode.Horizontal && TravelDistance > 0 && ActorId != null;
	internal Point2 NavStartPosition => routeBounds.TopLeft;
	internal float NavRouteLength => TravelDistance;
	internal float NavRoutePosition => Math.Clamp(Position.X - routeBounds.Left, 0, TravelDistance);
	internal int NavRouteDirection => direction;
	internal bool NavWaiting => waitTimer > 0;
	internal float NavVelocityX => waitTimer > 0 ? 0 : direction * currentSpeed;

	static Cardinal? StartingDirection(MovementMode mode, Signs facing) => mode switch
	{
		MovementMode.Horizontal => facing.CardinalX,
		MovementMode.Vertical => facing.CardinalY,
		_ => null
	};

	public MovingPlatform()
	{
		Mask = Masks.Solid;
		CollidesWithSolids = true;
	}

	public override void Destroyed() {
		StopAndReleaseSound(ref moveSound, false);
	}

	public override void Added()
	{
		base.Added();
		// Size is applied from the spec before Added (editor resizes write it), so build the
		// hitbox here to match the authored footprint rather than the construction-time default.
		Hitbox = new Hitbox(new RectInt(0, 0, Size.X, Size.Y));
		originalPosition = Position;
		waitTimer = WaitTime;
		activationLightsOn = !ActivatedByWeight || Mode == MovementMode.Circle;
		if (StartingDirection(Mode, Facing) is { } startingDirection)
		{
			direction = Facing;
			routeBounds = RectInt.Between(originalPosition, originalPosition + startingDirection * TravelDistance);
		}
		var platformCenter = (Vector2)Position + (Vector2)Size / 2;
		circleCenterOffset = CircleCenter is { } center
			? (Vector2)(Room?.WorldBounds.TopLeft ?? Point2.Zero) + center - platformCenter
			: DefaultCircleCenterOffset;
	}

	public override void Update()
	{
		if (ActivatedByWeight && Mode != MovementMode.Circle)
		{
			// Weight sends us out along the route, losing it brings us home: the leg we're on is
			// decided fresh every frame, so stepping off mid-travel turns us around where we are.
			activationLightsOn = HasStandingWeight();
			var forward = (int)Facing;
			var heading = activationLightsOn ? forward : ReturnsWhenUnweighted ? -forward : 0;
			if (heading == 0 || IsAtRouteEnd(heading))
			{
				Park();
				return;
			}
			if (heading != direction)
			{
				direction = heading;
				Park(); // the wait plays out before we set off the other way
			}
		}
		else
			activationLightsOn = true;

		if (waitTimer > 0)
		{
			waitTimer -= Time.Delta;
			if (waitTimer <= 0) {
				moveSound?.Stop();
				moveSound = Game.CreateAudioEvent("moving_stone", Position);
				moveSound?.Start();
			}
			return;
		}

		currentSpeed = Calc.Approach(currentSpeed, Speed, StartFriction * Time.Delta);
		switch (Mode) {
			case MovementMode.Circle:
				MoveCircle(currentSpeed * Time.Delta);
				break;
			case MovementMode.Horizontal:
			case MovementMode.Vertical:
				MoveStraight(currentSpeed * Time.Delta);
				break;
		}

		if (moveSound != null) {
			Game.SetAudioEventPosition(moveSound, Position);
		}
	}

	void MoveStraight(float distance)
	{
		// Accumulate fractional travel and move in whole pixels, clamped to the patrol range so
		// the platform and its riders never drift apart by rounding.
		moveRemainder += (Mode == MovementMode.Vertical ? Vector2.UnitY : Vector2.UnitX) * direction * distance;
		var step = (Position + TakeMoveStep()).Clamp(routeBounds) - Position;

		if (step != Point2.Zero)
			MoveSolid(step);

		if (IsAtRouteEnd(direction))
		{
			moveRemainder = Vector2.Zero;
			currentSpeed = 0;
			StopAndReleaseSound(ref moveSound, false);
			if (ActivatedByWeight)
				return; // Update turns us around when the weight on top changes

			waitTimer = WaitTime;
			direction = -direction;
		}
	}

	// Only the bound we're heading toward ends the leg — checking both would trip the min
	// bound instantly, since the platform starts there.
	bool IsAtRouteEnd(int heading)
		=> Position == (heading > 0 ? routeBounds.BottomRight : routeBounds.TopLeft);

	// Sit still with the departure wait armed, so the next leg starts from a standstill.
	void Park()
	{
		waitTimer = WaitTime;
		currentSpeed = 0;
		moveRemainder = Vector2.Zero;
		StopAndReleaseSound(ref moveSound, false);
	}

	void MoveCircle(float distance)
	{
		var radiusVector = -circleCenterOffset;
		var radius = radiusVector.Length();
		if (radius <= 0)
			return;

		circleAngle = Calc.AngleWrap(circleAngle + distance / radius);
		var nextOffset = circleCenterOffset + Calc.AngleToVector(radiusVector.Angle() + circleAngle, radius);
		moveRemainder += nextOffset - circleOffset;
		circleOffset = nextOffset;

		var step = TakeMoveStep();
		if (step != Point2.Zero)
			MoveSolid(step);
	}

	Point2 TakeMoveStep()
	{
		var step = (Point2)moveRemainder;
		moveRemainder -= (Vector2)step;
		return step;
	}

	// Move the platform by a whole-pixel delta, carrying actors riding on top and pushing any
	// caught in its path. While moving actors we go intangible (Mask cleared) so a carried rider
	//  isn't "grounded" on us (lets it follow us down) and a pushed actor only collides with
	// real walls (so it can squish, not snag on us).
	void MoveSolid(Point2 step)
	{
		riders.Clear();
		foreach (var actor in Game.Actors) if (CanRidePlatform(actor))
		{
			// Riding = standing on top, or — while airborne — hugging a side face (a wall-slide).
			// Carrying side huggers keeps the player glued as we move, so wall-slide contact
			// doesn't flicker open 1px at a time (which toggles the slow-slide gravity and makes
			// the player vibrate). The !Grounded gate excludes things merely standing beside us.
			if (IsStandingOnPlatform(actor)
				|| (!actor.Grounded() && (actor.OverlapsAny(Point2.Left, this) || actor.OverlapsAny(Point2.Right, this))))
				riders.Add(actor);
		}

		var savedMask = Mask;
		Mask = Masks.None;

		Position += step;

		foreach (var actor in Game.Actors) if (CanRidePlatform(actor))
		{
			// Overlapping after the move => we ran into it: push. Otherwise, if it was riding on
			// top, carry it. Both move by the same delta with the actor's own collision, so they
			// scrape to a stop against walls instead of clipping through.
			if (actor.OverlapsAny(Point2.Zero, this) || riders.Contains(actor))
				actor.MovePixels(step);
		}

		Mask = savedMask;
	}

	bool HasStandingWeight()
	{
		foreach (var actor in Game.Actors)
			if (CanRidePlatform(actor) && IsStandingOnPlatform(actor))
				return true;
		return false;
	}

	bool CanRidePlatform(Actor actor)
		=> IsCarryable(actor) || actor is ICarryable { CarriedBy: null };

	bool IsStandingOnPlatform(Actor actor)
		=> actor.OverlapsAny(Point2.Down, this) && !actor.OverlapsAny(Point2.Zero, this);

	// Occlude light like the dual tiles it's skinned with: DualGrid traces occluder segments from the
	// tileset art, so the shadow follows the rounded corners instead of a bare rectangle.
	public bool CastsShadow => true;

	public void AppendOccluderSegments(List<ShadowSegment> segments)
	{
		if (Assets.GetTileset(Material) is { } tileset)
			DualGrid.AppendRectOccluders(segments, tileset, TileColumns, TileRows, new Vector2(Position.X, Position.Y), VariantTileOrigin, MaterialTile);
		else
			OccluderShape.FromRect(new RectInt(Position.X, Position.Y, Size.X, Size.Y)).AppendSegments(segments);
	}

	public override void Render(Batcher batcher)
	{
		RenderMaterial(batcher, normals: false, Color.White);
		RenderActivationLightColor(batcher, 1);
	}
	// Spriteless, so the Actor default RenderShadow (which stamps the sprite silhouette into the
	// particle-collision coverage mask) draws nothing and particles pass straight through. Stamp the
	// dual-tile art instead: the CoverageMask shader keys off texture alpha, so the mask picks up the
	// rounded blob corners rather than a bare rectangle.
	public override void RenderShadow(Batcher batcher, Color color) => RenderMaterial(batcher, normals: false, color);
	public override void RenderNormals(Batcher batcher)
	{
		RenderMaterial(batcher, normals: true, Color.White);
		RenderActivationLightNormals(batcher);
	}
	public override void RenderEmissive(Batcher batcher)
	{
		if (CanRenderActivationLights && activationLightsOn)
		{
			batcher.Circle(NorthwestActivationLight, ActivationLightLensRadius, ActivationLightSteps, ActivationLightColor * 0.7f);
			batcher.Circle(NortheastActivationLight, ActivationLightLensRadius, ActivationLightSteps, ActivationLightColor * 0.7f);
		}
	}
	// Stamp the platform into the TileField with per-tile field colors (so its dither variation matches
	// neighboring stone) rather than one flat color. colorForTile maps a cell's anchored world tile to
	// its field color; falls back to a flat fill when the material is missing.
	internal void RenderTileField(Batcher batcher, Func<Point2, Color> colorForTile)
	{
		if (Assets.GetTileset(Material) is { } tileset)
			DualGrid.RenderRectTileField(batcher, tileset, TileColumns, TileRows, VariantTileOrigin, MaterialTile, colorForTile);
		else
			batcher.Rect(new Rect(0, 0, Size.X, Size.Y), colorForTile(VariantTileOrigin));
	}
	internal Point2 DitherAnchorPosition => originalPosition;
	internal Vector2 DitherAnchorOffset => originalPosition - Position;
	internal Point2 VariantTileOrigin => originalPosition / Game.TileSize;
	internal char MaterialTile
	{
		get
		{
			var found = false;
			var result = '0';
			foreach (var (tile, entry) in Factory.Entries)
				if (entry.DualTilesetName == Material && (!found || tile < result))
				{
					found = true;
					result = tile;
				}
			return result;
		}
	}

	public override bool CastsReflection => true;
	public override void RenderReflection(Batcher batcher, Color color)
	{
		RenderMaterial(batcher, normals: false, color);
		RenderActivationLightColor(batcher, color.A / 255.0f);
	}

	// Spriteless, so the sprite-based default is a zero rect; report the real footprint so
	// reflection culling (and any other visual-bounds consumer) sees the whole platform.
	public override Rect GetVisualBounds() => new(Position.X, Position.Y, Size.X, Size.Y);

	// When skinned with a reflective material the top face mirrors sprites like a metal floor would.
	// See Game.Rendering DrawPlatformSurfaceReflections.
	public const float SurfaceReflectionOpacity = 0.7f; // matches the metal tile's reflectionOpacity
	public bool ReflectsSurface => Material == "metal-dual";
	public RectInt SurfaceReflectionBounds => new(Position.X, Position.Y, Size.X, Size.Y);

	// Skin the platform with its dual tileset (DualGrid renders the cols x rows blob); fall back to a
	// flat fill if the material is missing.
	void RenderMaterial(Batcher batcher, bool normals, Color color)
	{
		if (Assets.GetTileset(Material) is { } tileset)
			DualGrid.RenderRect(batcher, tileset, TileColumns, TileRows, normals, color, VariantTileOrigin, MaterialTile);
		else
			batcher.Rect(new Rect(0, 0, Size.X, Size.Y), color);
	}

	static float ActivationLightInset => 5;
	static float ActivationLightRadius => 3;
	static float ActivationLightLensRadius => 1.75f;
	static float ActivationLightNormalSlope => 0.8f;
	const int ActivationLightSteps = 12;
	static readonly Color ActivationLightHousingColor = new(0x171319);
	static readonly Color ActivationLightOffColor = new(0x3a3550);
	static readonly Color ActivationLightColor = new(0x79cfff);

	bool CanRenderActivationLights
		=> RenderActivationLights && Size.X >= ActivationLightInset * 2 + ActivationLightRadius * 2 && Size.Y >= ActivationLightInset + ActivationLightRadius;
	Vector2 NorthwestActivationLight => new(ActivationLightInset, ActivationLightInset);
	Vector2 NortheastActivationLight => new(Size.X - ActivationLightInset, ActivationLightInset);

	void RenderActivationLightColor(Batcher batcher, float opacity)
	{
		if (!CanRenderActivationLights)
			return;
		var lens = (activationLightsOn ? ActivationLightColor : ActivationLightOffColor) * opacity;
		var housing = ActivationLightHousingColor * opacity;
		DrawActivationLightColor(batcher, NorthwestActivationLight, housing, lens);
		DrawActivationLightColor(batcher, NortheastActivationLight, housing, lens);
	}

	void RenderActivationLightNormals(Batcher batcher)
	{
		if (!CanRenderActivationLights)
			return;
		DrawActivationLightInset(batcher, NorthwestActivationLight);
		DrawActivationLightInset(batcher, NortheastActivationLight);
	}

	static void DrawActivationLightColor(Batcher batcher, Vector2 center, Color housing, Color lens)
	{
		batcher.Circle(center, ActivationLightRadius, ActivationLightSteps, housing);
		batcher.Circle(center, ActivationLightLensRadius, ActivationLightSteps, lens);
	}

	static void DrawActivationLightInset(Batcher batcher, Vector2 center)
	{
		var flat = new Color(0x8080ff);
		for (var i = 0; i < ActivationLightSteps; i++)
		{
			var a = Calc.AngleToVector(i / (float)ActivationLightSteps * Calc.TAU);
			var b = Calc.AngleToVector((i + 1) / (float)ActivationLightSteps * Calc.TAU);
			batcher.Triangle(center, center + a * ActivationLightRadius, center + b * ActivationLightRadius,
				flat, ActivationLightInsetNormal(a), ActivationLightInsetNormal(b));
		}
	}

	static Color ActivationLightInsetNormal(Vector2 direction)
	{
		var normal = Vector3.Normalize(new Vector3(-direction * ActivationLightNormalSlope, 1));
		return new(normal * 0.5f + Vector3.One * 0.5f);
	}

	int TileColumns => Math.Max(1, Size.X / Game.TileSize);
	int TileRows => Math.Max(1, Size.Y / Game.TileSize);

#if EDITOR
	sealed class Editor : Manager.ActorEditor<MovingPlatform>
	{
		internal override void Gizmos(Manager.EditorActor actor, ref Manager.EditorGizmos gizmos)
		{
			if (IsCircle(actor))
			{
				var platform = actor.Bounds.CenterF;
				var center = actor.RoomOrigin + Center(actor);
				var radius = Vector2.Distance(platform, center);
				var color = new Color(0x66d9ff);
				gizmos.CircleDashed(center, radius, gizmos.LineWeight * 2, 64, color * 0.8f,
					MathF.Max(3, gizmos.LineWeight * 6));
				gizmos.Line(platform, center, gizmos.LineWeight, color * 0.65f);
				gizmos.RoomPointHandle(nameof(CircleCenter), center, color, "Move Circle Center");
			}
			else {
				var center = actor.Bounds.CenterF;
				var mode = GetModeFromEditorActor(actor);
				if (StartingDirection(mode, actor.GetFacing()) is { } direction)
				{
					var distance = actor.GetInt(nameof(TravelDistance), 80);
					var color = new Color(0xff3040);
					gizmos.Arrow(center, center + direction * (float)distance, gizmos.LineWeight * 2, color);
					gizmos.CardinalDistanceHandle(nameof(TravelDistance), center, direction, distance, color,
						"Change Travel Distance");
				}
			}
		}

		internal override void BeforeMove(Manager.EditorActor actor) => MaterializeCenter(actor);

		internal override void PropertyChanged(Manager.EditorActor actor, string property)
		{
			if (property == nameof(Mode))
				MaterializeCenter(actor);
		}

		static MovementMode GetModeFromEditorActor(Manager.EditorActor actor) => actor.GetEnum(nameof(Mode), DEFAULT_MODE);

		static bool IsCircle(Manager.EditorActor actor) => GetModeFromEditorActor(actor) == MovementMode.Circle;

		static Point2 Center(Manager.EditorActor actor)
			=> actor.GetRoomPoint(nameof(CircleCenter),
				(actor.Bounds.CenterF - actor.RoomOrigin + DefaultCircleCenterOffset).RoundToPoint2());

		static void MaterializeCenter(Manager.EditorActor actor)
		{
			if (IsCircle(actor) && !actor.Has(nameof(CircleCenter)))
				actor.Set(nameof(CircleCenter), Center(actor));
		}
	}
#endif
}
