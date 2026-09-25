using Foster.Framework;
using System.Numerics;
namespace Myth;

[GameActor(Tags = ActorTag.Obstacle, Sprite = "Editor/moving_platform", Anchor = SpriteAnchor.TopLeft)]
class MovingPlatform : Actor, IResizable, IShapeOccluder, IDualGridRect
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
	[I(8, 512)] public int TravelDistance = 80;
	[I(1f, 5f)] public float ReverseSpeedMultiplier = 1f;
	public bool ActivatedByWeight;
	// Weight-activated platforms travel back to where they started once nothing is standing on
	// them; turn this off for a one-way lift that parks at the far end.
	public bool ReturnsWhenUnweighted = true;
	public bool RenderActivationLights;
	[ActorRef] public string AppearAfterActorDestroyed = "";

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
	// Where we are along the route, apart from Position. Advance steps it the same way for Update and for
	// PredictPosition, so a prediction is the real thing run ahead.
	struct Motion
	{
		public int Direction;
		public float Speed, Wait, CircleAngle;
		public Vector2 CircleOffset;
		public Vector2 Remainder; // sub-pixel travel accumulator (we move in whole pixels)
	}
	Motion motion = new() { Wait = WAIT_TIME };
	Vector2 circleCenterOffset;
	bool activationLightsOn;
	static readonly List<Actor> riders = [];  // reusable scratch: actors standing on top this frame
	Point2 originalPosition;
	RectInt routeBounds;
	ActorDestructionTrigger? appearanceTrigger;

	internal bool SupportsPlatformNav => Mode == MovementMode.Horizontal && TravelDistance > 0 && ActorId != null;
	internal Point2 NavStartPosition => routeBounds.TopLeft;
	internal float NavRouteLength => TravelDistance;
	internal float NavRoutePosition => Math.Clamp(Position.X - routeBounds.Left, 0, TravelDistance);
	internal int NavRouteDirection => motion.Direction;
	internal bool NavWaiting => motion.Wait > 0;
	internal float NavVelocityX => motion.Wait > 0 ? 0 : motion.Direction * motion.Speed;

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
		motion.Wait = WaitTime;
		activationLightsOn = !ActivatedByWeight || Mode == MovementMode.Circle;
		if (StartingDirection(Mode, Facing) is { } startingDirection)
		{
			motion.Direction = Facing;
			routeBounds = RectInt.Between(originalPosition, originalPosition + startingDirection * TravelDistance);
		}
		var platformCenter = (Vector2)Position + (Vector2)Size / 2;
		circleCenterOffset = CircleCenter is { } center
			? (Vector2)(Room?.WorldBounds.TopLeft ?? Point2.Zero) + center - platformCenter
			: DefaultCircleCenterOffset;
		if (!string.IsNullOrWhiteSpace(AppearAfterActorDestroyed))
		{
			appearanceTrigger = new(this, AppearAfterActorDestroyed);
			if (!appearanceTrigger.Triggered)
			{
				Visible = false;
				Mask = Masks.None;
			}
		}
	}

	public override void Update()
	{
		if (appearanceTrigger is { Triggered: false })
		{
			if (!appearanceTrigger.Update())
				return;
			Visible = true;
			Mask = Masks.Solid;
			for (var x = Game.TileSize / 2; x < Size.X; x += Game.TileSize)
				Game.Create<Pop>(Position + new Point2(x, Size.Y / 2));
		}

		if (ActivatedByWeight && Mode != MovementMode.Circle)
		{
			// Weight sends us out along the route, losing it brings us home: the leg we're on is
			// decided fresh every frame, so stepping off mid-travel turns us around where we are.
			//
			bool hasWeightThisFrame = HasStandingWeight();
			standingWeightValue = Calc.Approach(standingWeightValue, hasWeightThisFrame ? 1 : 0, Time.Delta * (hasWeightThisFrame ? 2.0f : 0.5f));
			activationLightsOn = standingWeightValue > 0;

			var forward = (int)Facing;
			var heading = activationLightsOn ? forward : ReturnsWhenUnweighted ? -forward : 0;
			if (heading == 0 || IsAtRouteEnd(Position, heading))
			{
				Park();
				return;
			}
			if (heading != motion.Direction)
			{
				motion.Direction = heading;
				Park(); // the wait plays out before we set off the other way
			}
		}
		else
			activationLightsOn = true;

		var wasWaiting = motion.Wait > 0;
		var step = Advance(ref motion, Position, Time.Delta);
		if (step != Point2.Zero)
			MoveSolid(step);
		if (wasWaiting && motion.Wait <= 0)
		{
			moveSound?.Stop();
			moveSound = Game.CreateAudioEvent("moving_stone", Position);
			moveSound?.Start();
		}
		else if (!wasWaiting && motion.Speed == 0)
			StopAndReleaseSound(ref moveSound, false); // arrived at a route end
		if (moveSound != null)
			Game.SetAudioEventPosition(moveSound, Position);
	}

	// Where we'll be after this long, left to ourselves: the same stepping Update does, on a copy. A
	// weight-activated platform nobody is standing on stays put (or heads home, which it predicts the same).
	internal Point2 PredictPosition(float seconds)
	{
		if (ActivatedByWeight && Mode != MovementMode.Circle && !activationLightsOn)
			return Position;
		var ahead = motion;
		var position = Position;
		const float Step = 1f / 60;
		for (var t = 0f; t < seconds; t += Step)
			position += Advance(ref ahead, position, MathF.Min(Step, seconds - t));
		return position;
	}

	// Steps the route by dt: the wait at each end, the speed ramp, and whole-pixel travel along the line or
	// around the circle. Returns how far to move.
	Point2 Advance(ref Motion m, Point2 position, float dt)
	{
		if (m.Wait > 0)
		{
			m.Wait -= dt;
			return Point2.Zero;
		}

		var mult = m.Direction == -1 ? ReverseSpeedMultiplier : 1f;
		m.Speed = Calc.Approach(m.Speed, Speed * mult, StartFriction * dt * mult);
		var distance = m.Speed * dt;
		if (Mode == MovementMode.Circle)
		{
			var radiusVector = -circleCenterOffset;
			var radius = radiusVector.Length();
			if (radius <= 0)
				return Point2.Zero;
			m.CircleAngle = Calc.AngleWrap(m.CircleAngle + distance / radius);
			var nextOffset = circleCenterOffset + Calc.AngleToVector(radiusVector.Angle() + m.CircleAngle, radius);
			m.Remainder += nextOffset - m.CircleOffset;
			m.CircleOffset = nextOffset;
			return TakeMoveStep(ref m);
		}

		// Clamped to the patrol range so the platform and its riders never drift apart by rounding.
		m.Remainder += (Mode == MovementMode.Vertical ? Vector2.UnitY : Vector2.UnitX) * m.Direction * distance;
		var step = (position + TakeMoveStep(ref m)).Clamp(routeBounds) - position;
		if (IsAtRouteEnd(position + step, m.Direction))
		{
			m.Remainder = Vector2.Zero;
			m.Speed = 0;
			if (!ActivatedByWeight) // weight-activated: Update turns us around when the weight on top changes
			{
				m.Wait = WaitTime;
				m.Direction = -m.Direction;
			}
		}
		return step;
	}

	// Only the bound we're heading toward ends the leg — checking both would trip the min
	// bound instantly, since the platform starts there.
	bool IsAtRouteEnd(Point2 position, int heading)
		=> position == (heading > 0 ? routeBounds.BottomRight : routeBounds.TopLeft);

	// Sit still with the departure wait armed, so the next leg starts from a standstill.
	void Park()
	{
		motion.Wait = WaitTime;
		motion.Speed = 0;
		motion.Remainder = Vector2.Zero;
		StopAndReleaseSound(ref moveSound, false);
	}

	static Point2 TakeMoveStep(ref Motion m)
	{
		var step = (Point2)m.Remainder;
		m.Remainder -= (Vector2)step;
		return step;
	}

	// Move the platform by a whole-pixel delta, carrying actors riding on top and pushing any
	// caught in its path. While moving actors we go intangible (Mask cleared) so a carried rider
	//  isn't "grounded" on us (lets it follow us down) and a pushed actor only collides with
	// real walls (so it can squish, not snag on us).
	void MoveSolid(Point2 step)
	{
		// TODO: riders are re-derived from overlap probes every step, so each new way of holding on
		// (side huggers, burrowers) needs another probe here. The replacement is an explicit
		// attachment: whoever grabs a surface records the actor and its offset -- the burrow walk
		// already knows which actor's face it is on -- and applies that actor's per-frame delta
		// itself. That drops the guessing below, survives diagonal motion (circle mode steps X and Y
		// together, so a rider straddling a face can lose contact on both axes at once), and gives
		// riders a defined order against their own movement instead of one that depends on where the
		// platform lands in Game.Actors.
		riders.Clear();
		foreach (var actor in Game.Actors) if (CanRidePlatform(actor))
		{
			// Riding = standing on top, embedded in us, or — while airborne — hugging a side face (a
			// wall-slide). Carrying side huggers keeps the player glued as we move, so wall-slide
			// contact doesn't flicker open 1px at a time (which toggles the slow-slide gravity and
			// makes the player vibrate). The !Grounded gate excludes things merely standing beside us.
			//
			// Embedded is the burrowing player: its 2x2 hitbox straddles our face by a single pixel,
			// so a step away from the trailing face breaks contact outright. The post-move push below
			// only catches the leading face (we move into that one), and the wall-hug branch can't
			// help because a burrower on a vertical face reads as Grounded -- its box shifted down
			// still overlaps us. Left behind, its boundary walk finds no edge and the burrow pops.
			if (IsStandingOnPlatform(actor) || actor.OverlapsAny(Point2.Zero, this)
				|| (!actor.Grounded() && (actor.OverlapsAny(Point2.Left, this) || actor.OverlapsAny(Point2.Right, this))))
				riders.Add(actor);
		}

		var savedMask = Mask;
		Mask = Masks.None;

		Position += step;
		Game.ActorMoved(this, step);

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

	float standingWeightValue = 0;

	bool HasStandingWeight()
	{
		var hasWeight = false;
		foreach (var actor in Game.Actors)
		{
			if (!CanRidePlatform(actor)) continue;
			if (IsStandingOnPlatform(actor))
			{
				hasWeight = true;
				break;
			}
			if (IsBurrowingOnPlatform(actor))
			{
				hasWeight = true;
				break;
			}
		}
		return hasWeight;
	}

	bool CanRidePlatform(Actor actor)
		=> actor.AttachmentTarget != this && (IsCarryable(actor) || actor is ICarryable { CarriedBy: null });

	bool IsStandingOnPlatform(Actor actor)
		=> actor.OverlapsAny(Point2.Down, this) && !actor.OverlapsAny(Point2.Zero, this);

	bool IsBurrowingOnPlatform(Actor actor) {
		var rect = Hitbox.Rect.Inflate(2) + Position; // not sure about this
		if (actor is Pawn pawn)
		{
			return pawn.State == Pawn.States.Burrow && rect.Contains(actor.Position);
		}
		return false;
	}

	// Occlude light like the dual tiles it's skinned with: DualGrid traces occluder segments from the
	// tileset art, so the shadow follows the rounded corners instead of a bare rectangle.
	public bool CastsShadow => Visible;

	public void AppendOccluderSegments(List<ShadowSegment> segments) => this.AppendOccluders(segments);

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
		if (CanRenderActivationLights)
		{
			var alpha = MathF.Min(1f, standingWeightValue + 0.2f);
			batcher.Circle(NorthwestActivationLight, ActivationLightLensRadius, ActivationLightSteps, ActivationLightColor * alpha);
			batcher.Circle(NortheastActivationLight, ActivationLightLensRadius, ActivationLightSteps, ActivationLightColor * alpha);
		}
	}
	// Position, Visible and Material are Actor fields, which can't satisfy interface properties.
	Point2 IDualGridRect.Position => Position;
	bool IDualGridRect.Visible => Visible;
	string IDualGridRect.Material => Material;

	internal Point2 DitherAnchorPosition => originalPosition;
	public Vector2 DitherAnchorOffset => originalPosition - Position;
	public Point2 VariantTileOrigin => originalPosition / Game.TileSize;
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
	public RectInt SurfaceReflectionBounds => this.Bounds();

	void RenderMaterial(Batcher batcher, bool normals, Color color) => this.Render(batcher, normals, color);

	static float ActivationLightInset => 5;
	static float ActivationLightRadius => 3;
	static float ActivationLightLensRadius => 1.75f;
	static float ActivationLightNormalSlope => 0.8f;
	const int ActivationLightSteps = 12;
	static readonly Color ActivationLightHousingColor = new(0x171319);
	static readonly Color ActivationLightOffColor = new(0x3a3550);
	static readonly Color ActivationLightColor = new(0x79cfff);

	bool CanRenderActivationLights
		=> RenderActivationLights && MaterialSize.X >= ActivationLightInset * 2 + ActivationLightRadius * 2 && MaterialSize.Y >= ActivationLightInset + ActivationLightRadius;
	Vector2 NorthwestActivationLight => new(ActivationLightInset, ActivationLightInset);
	Vector2 NortheastActivationLight => new(MaterialSize.X - ActivationLightInset, ActivationLightInset);

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
		var fillMode = new Color(0, 0, 255, 0);
		var flat = new Color(0x8080ff);
		Span<BatcherVertex> verts = stackalloc BatcherVertex[ActivationLightSteps + 1];
		Span<int> idxs = stackalloc int[ActivationLightSteps * 3];
		verts[0] = new(center, flat, fillMode);
		for (var i = 0; i < ActivationLightSteps; i++)
		{
			var a = Calc.AngleToVector(i / (float)ActivationLightSteps * Calc.TAU);
			verts[i + 1] = new(center + a * ActivationLightRadius, ActivationLightInsetNormal(a), fillMode);
			idxs[i * 3] = 0; idxs[i * 3 + 1] = i + 1; idxs[i * 3 + 2] = (i + 1) % ActivationLightSteps + 1;
		}
		batcher.Vertices(verts, idxs);
	}

	static Color ActivationLightInsetNormal(Vector2 direction)
	{
		var normal = Vector3.Normalize(new Vector3(-direction * ActivationLightNormalSlope, 1));
		return new(normal * 0.5f + Vector3.One * 0.5f);
	}

	Point2 MaterialSize => new(this.TileColumns() * Game.TileSize, this.TileRows() * Game.TileSize);

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
