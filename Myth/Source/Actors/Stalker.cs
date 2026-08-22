using Foster.Framework;
using System.Numerics;
namespace Myth;

// A slow, relentless hunter: a double-size Bud that paths to the player with the same
// Navigator machinery, and never stops. It is dormant until it first sees the player, then
// hunts across rooms forever — leaving a room doesn't unload it. Instead the chase continues
// off-screen as a timer: how far it still had to travel to the exit you took, at its own
// travel speed, is how long before it walks in through the entry of your new room.
//
// Because its body is twice Bud's, it navigates on its own graph (Game.GetStalkerNavGraph),
// probed with the bigger rects below. Gaps that body can't fit produce no edges at all, so
// crawl spaces are genuinely safe from it.
//
// Only one exists at a time: the authored instance becomes the persistent one, and any
// duplicate created by re-entering its home room removes itself in Added.
[GameActor(Sprite = "Editor/stalker", Tags = ActorTag.Enemy, Anchor = SpriteAnchor.BottomCenter,
	LivePositionEditable = true)]
public class Stalker : Pawn
{
	public static readonly RectInt NavProbe = new(-8, -24, 16, 24);
	public static readonly RectInt NavDuckingProbe = new(-8, -12, 16, 12);

	// Slower than the player on purpose: in-room it can always be outrun. The threat is that
	// it never stops, not that it wins a footrace.
	[I(0.25f, 1.5f)] public float SpeedMultiplier = 0.75f;
	[I(32, 320)] public float SightRange = 160f;
	// Off-screen pursuit speed, px/sec, used to turn distance-to-exit into an arrival delay.
	[I(10, 120)] public float TravelSpeed = 35f;
	[I(0, 4)] public float MinArrivalDelay = 1.5f;
	[I(5, 24)] public float MaxArrivalDelay = 12f;
	// Never materialize right on top of the player; hold the arrival until there's room to react.
	[I(16, 160)] public float MinArrivalDistance = 56f;
	[I(12, 40)] public float RigPixelsPerMeter = 23f;
	[I(-60, 60)] public float RigProjectionYaw = 18f;
	[I(1, 60)] public float RigVisualFPS = 16f;
	[I(0.5f, 8)] public float RigBootThickness = 2.5f;

	static float TelegraphTime => 0.9f;

	readonly Navigator nav;
	readonly HumanoidRigVisual rigVisual;
	DitherCorruption? corruption;
	bool rigReady;
	bool hunting;
	string? homeRoomId;
	Point2 homePosition;

	// Off-screen pursuit: where we conceptually are (the entry we're walking toward) and how
	// long until we get there.
	bool pendingArrival;
	Point2 virtualPosition;
	float arrivalTimer;
	bool telegraphed;

	protected override float GroundSpeedMultiplier => SpeedMultiplier;
	protected override float AirSpeedMultiplier => SpeedMultiplier;
	protected override RectInt CurrentStandingHitboxRect => NavProbe;
	protected override RectInt CurrentDuckingHitboxRect => NavDuckingProbe;

	internal override int DamageValue => Player.MaxHealth;

	public Stalker()
	{
		nav = new(this);
		rigVisual = new(this)
		{
			Decorated = true,
			TurnWhileMoving = true,
			CoreColor = new(0x4b334f),
			ClothColor = new(0x17131f),
			ClothHighlightColor = new(0x4a354f),
			SkinColor = new(0x8f7777),
			BootColor = new(0x0d0b12),
			OutlineColor = new(0x09070d),
			AccentColor = new(0xf02245),
		};
		Sprite = Assets.GetSprite(nameof(Bud));
		Hitbox = new(NavProbe);
		Mask = Masks.Enemy;
		Depth = -5;
		Play("idle");
	}

	public override void Added()
	{
		base.Added();

		// The persistent instance outlives room changes, so re-entering the room it was
		// authored in would spawn a second one. First one wins.
		foreach (var actor in Game.Actors)
			if (actor is Stalker other && other != this && !Game.destroying.Contains(other))
			{
				Game.Destroy(this);
				return;
			}

		SyncRigSettings();
		rigVisual.Initialize("Zombie_Idle_Loop");
		rigReady = true;
		KeepOnRoomTransition = true;
		corruption = Game.Create<DitherCorruption>(Position, it =>
		{
			it.Owner = this;
			it.KeepOnRoomTransition = true;
		});
		homeRoomId = Room?.Id;
		homePosition = Position;
		Game.RoomChanged += OnRoomChanged;
	}

	public override void Destroyed()
	{
		Game.RoomChanged -= OnRoomChanged;
		rigVisual.Dispose();
		if (corruption != null)
			Game.Destroy(corruption);
	}

	// Unkillable, but the body still yields to the strike.
	public override void OnWasHit(Actor by)
	{
		if (rigReady)
			rigVisual.Hit(Position.X < by.Position.X ? -1 : 1);
	}

	public override PawnInput GetInput()
	{
		if (!hunting || pendingArrival)
			return new();

		var player = Game.GetFirst<Player>();
		if (player == null)
			return new();

		var moveX = 0f;
		var moveY = 0;
		var jumpPressed = false;
		var recoverWallJump = false;

		if (!nav.TryExecuteActiveEdge(out moveX, out moveY, out jumpPressed))
		{
			if (nav.TryRecoverLanding(out moveX, out jumpPressed, out recoverWallJump))
			{
				// Mid-air with no edge to run: steer onto a reachable surface.
			}
			else if (nav.TryClimbWellToward(player.Position, out moveX, out moveY, out jumpPressed, out recoverWallJump))
			{
				// Player overhead in a walled shaft: chained wall kicks are the only way up.
			}
			else if (!nav.TryNavigateToPosition(player.Position, out moveX, out moveY, out jumpPressed))
			{
				if (nav.TryRecoverPit(out moveX, out jumpPressed, out recoverWallJump))
					moveY = 0;
				else
				{
					// No path at all: shamble toward the player so it never just stands there.
					moveX = Math.Sign(player.Position.X - Position.X);
					moveY = 0;
					if (grounded && moveX != 0 && !nav.HasGroundSupportAhead(moveX))
						moveX = 0;
				}
			}
		}

		nav.UpdateJumpHeld(jumpPressed);

		if (moveX == 0)
		{
			var face = Math.Sign(player.Position.X - Position.X);
			if (face != 0)
				Facing = face;
		}

		return new(
			MoveX: moveX,
			MoveY: moveY,
			JumpPressed: jumpPressed,
			JumpDown: nav.JumpHeld,
			DashPressed: false,
			AllowWallJump: nav.ActiveEdgeAllowsWallJump || recoverWallJump
		);
	}

	public override void Update()
	{
		nav.GraphOverride = Game.GetStalkerNavGraph();

		if (pendingArrival)
		{
			UpdatePendingArrival();
			return;
		}

		if (!hunting)
		{
			if (CanSeePlayer())
				BeginHunt();
			// Still runs physics below so it stands on the ground rather than hovering.
		}

		base.Update();
		UpdateRig();
	}

	void SyncRigSettings()
	{
		rigVisual.PixelsPerMeter = RigPixelsPerMeter;
		rigVisual.ProjectionYaw = RigProjectionYaw;
		rigVisual.VisualFPS = RigVisualFPS;
		rigVisual.BootThickness = RigBootThickness;
	}

	void UpdateRig()
	{
		if (!rigReady)
			return;
		SyncRigSettings();
		rigVisual.LookTarget = hunting && Game.GetFirst<Player>() is { } player
			? player.Position + new Vector2(0, -6)
			: null;
		var speed = MathF.Abs(Velocity.X);
		var clip = !IsGrounded
			? "NinjaJump_Idle_Loop"
			: speed > 2 ? "Zombie_Walk_Fwd_Loop" : "Zombie_Idle_Loop";
		rigVisual.Play(clip);
		rigVisual.PlaybackSpeed = clip == "Zombie_Walk_Fwd_Loop" ? Math.Clamp(speed / 22f, 0.65f, 1.8f) : 1;
		rigVisual.Update(IsGrounded, footIK: IsGrounded && !rigVisual.Turning);
	}

	protected override void OnLanded(Vector2 previousVelocity)
	{
		base.OnLanded(previousVelocity);
		if (rigReady)
			rigVisual.Land(previousVelocity.Y);
	}

	void BeginHunt()
	{
		hunting = true;
		Game.Shake(0.15f);
	}

	bool CanSeePlayer()
	{
		if (Game.GetFirst<Player>() is not { } player)
			return false;
		if (Vector2.DistanceSquared(Position, player.Position) > SightRange * SightRange)
			return false;
		return HasClearLine(EyePosition, player.Position + new Vector2(0, -6));
	}

	Vector2 EyePosition => Position + new Vector2(0, NavProbe.Top / 2);

	// Straight-line room-collision check, sampled per pixel-ish step.
	bool HasClearLine(Vector2 from, Vector2 to)
	{
		var delta = to - from;
		var steps = Math.Max(1, (int)MathF.Ceiling(delta.Length() / 2f));
		for (var i = 1; i <= steps; i++)
		{
			var p = (Point2)Vector2.Lerp(from, to, i / (float)steps);
			if (Game.OverlapsRoomCollision(new RectInt(p.X, p.Y, 1, 1)))
				return false;
		}
		return true;
	}

	void UpdatePendingArrival()
	{
		arrivalTimer = Calc.Approach(arrivalTimer, 0, Time.Delta);

		if (arrivalTimer <= TelegraphTime && !telegraphed)
		{
			telegraphed = true;
			Game.Shake(0.2f);
		}

		if (arrivalTimer > 0)
			return;

		// Don't materialize in the player's lap — wait for them to move off the entry.
		var entry = FindEnterPosition();
		if (Game.GetFirst<Player>() is { } player &&
			Vector2.DistanceSquared(entry, player.Position) < MinArrivalDistance * MinArrivalDistance)
			return;

		pendingArrival = false;
		telegraphed = false;
		Position = entry;
		Velocity = Vector2.Zero;
		Visible = true;
		CollidesWithSolids = true;
		Mask = Masks.Enemy;
		TryUnstick(new Vector2(0, -1));
	}

	void OnRoomChanged(RoomChange change)
	{
		if (change.Kind == RoomChangeKind.InitialLoad)
			return;

		// A reload is the player dying (or a debug room reset): the hunt resets. If we're not
		// in our home room the authored instance will spawn again when the player returns.
		if (change.Kind == RoomChangeKind.Reload)
		{
			ResetToHome(change);
			return;
		}

		if (!hunting)
		{
			// Never saw the player: stay put in the room we were authored in.
			if (change.To.Id != homeRoomId)
				Game.Destroy(this);
			return;
		}

		// Straight-line distance from where we actually are to the exit the player took. The
		// old room's nav graph is already gone by the time this fires, so this is deliberately
		// a rough measure — it only sets the delay, and generosity here reads as suspense.
		var from = pendingArrival ? (Vector2)virtualPosition : (Vector2)Position;
		var exit = ExitPosition(change);
		var delay = Vector2.Distance(from, exit) / MathF.Max(1f, TravelSpeed);

		arrivalTimer = Math.Clamp(pendingArrival ? arrivalTimer + delay : delay, MinArrivalDelay, MaxArrivalDelay);
		pendingArrival = true;
		telegraphed = false;
		virtualPosition = FindEnterPosition();
		Visible = false;
		CollidesWithSolids = false;
		Mask = Masks.None;
		Velocity = Vector2.Zero;
		nav.ClearBlockedPathEdges();
	}

	void ResetToHome(RoomChange change)
	{
		if (change.To.Id != homeRoomId)
		{
			Game.Destroy(this);
			return;
		}

		hunting = false;
		pendingArrival = false;
		telegraphed = false;
		arrivalTimer = 0;
		Position = homePosition;
		Velocity = Vector2.Zero;
		Visible = true;
		CollidesWithSolids = true;
		Mask = Masks.Enemy;
		nav.ClearBlockedPathEdges();
	}

	// Where the player left the room we were chasing them through: the edge of the old room
	// bounds in the direction they travelled, level with us.
	Vector2 ExitPosition(RoomChange change)
	{
		var bounds = change.From?.WorldBounds ?? change.To.WorldBounds;
		var direction = change.Direction;
		var from = pendingArrival ? (Vector2)virtualPosition : (Vector2)Position;
		if (direction.X > 0) return new Vector2(bounds.Right, from.Y);
		if (direction.X < 0) return new Vector2(bounds.Left, from.Y);
		if (direction.Y > 0) return new Vector2(from.X, bounds.Bottom);
		if (direction.Y < 0) return new Vector2(from.X, bounds.Top);
		return Game.RoomEntryPosition;
	}

	// Same idea as Bud's entry placement, with the bigger body's clearance.
	Point2 FindEnterPosition()
	{
		var bounds = Game.Bounds;
		var basePosition = Game.RoomEntryPosition;
		basePosition.X = Math.Clamp(basePosition.X, bounds.Left + Game.TileSize * 2, bounds.Right - Game.TileSize * 2);
		basePosition.Y = Math.Clamp(basePosition.Y, bounds.Top + Game.TileSize * 3, bounds.Bottom - 1);

		ReadOnlySpan<Point2> offsets =
		[
			Point2.Zero,
			new Point2(0, -Game.TileSize),
			new Point2(0, -Game.TileSize * 2),
			new Point2(Game.TileSize * 2, 0),
			new Point2(-Game.TileSize * 2, 0),
			new Point2(Game.TileSize * 3, 0),
			new Point2(-Game.TileSize * 3, 0)
		];

		foreach (var offset in offsets)
		{
			var position = basePosition + offset;
			if (!Game.OverlapsRoomCollision(NavProbe + position))
				return position;
		}
		return basePosition;
	}

	public override void Render(Batcher batcher)
	{
		if (rigReady && !SpriteBlinkHidden)
			rigVisual.Draw(batcher, Color.White, silhouette: false);
	}

	public override void RenderShadow(Batcher batcher, Color color)
	{
		if (rigReady)
			rigVisual.Draw(batcher, color, silhouette: true);
	}

	public override void RenderReflection(Batcher batcher, Color color)
	{
		if (rigReady)
			rigVisual.Draw(batcher, color, silhouette: true);
	}

	public override void RenderNormals(Batcher batcher)
	{
		if (rigReady)
			rigVisual.Draw(batcher, new Color(0x8080ff), silhouette: true);
	}

	public override Rect GetVisualBounds()
		=> new(Position.X - 38, Position.Y - 46, 76, 56);
}
