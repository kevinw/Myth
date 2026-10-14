using Foster.Framework;
using System.Numerics;
namespace Myth;

[GameActor(HideFromPalette = true)]
public class Bud : Pawn, ICarryable, IHitByHitbox
{
	[InspectorGroup("Rig")] public bool RigDraw = true;
	[ActorSettings(typeof(HumanoidRigSettings))] public readonly HumanoidRigSettings Rig = new(); // body and palette from Assets/Actors/bud.json
	protected override HumanoidRigSettings? DrawRig => RigDraw ? Rig : null;
	static float RigLookRange => 64f;
	protected override Vector2? RigLookTarget => Game.GetFirst<Player>() is { } player &&
		Vector2.DistanceSquared(player.Position, Position) < RigLookRange * RigLookRange ? player.Position - Vector2.UnitY * 10 : null;
	public static bool SecondControllerControlEnabled = true;
	internal static readonly Color Tint = new(0x7fb6ff);
	// His speech reads as his own blue, lifted toward white so it stays legible over dark rooms.
	internal static readonly Color SpeechColor = Color.Lerp(Tint, Color.White, 0.45f);
	private const int StruckSpeechEnemyDistance = Game.TileSize * 4;
	private const float EnterDelay = 1.0f;
	private const int EnterGroundProbeDepth = 4;
	private const int EnterSearchRadiusTiles = 5;
	private const int EnterWalkDistance = Game.TileSize * 3;
	private const int EnterWalkOffscreenPixels = Game.TileSize - 1;
	private const int EnterWalkPathStep = 2;
	private const int EnterWalkReachDistance = 2;
	static float BurrowRideRadius => 14f;
	static float FallRespawnDelay => 3f;
	static float BurrowPopDelay => 0.25f;
	static float BurrowPopInputMirrorTime => 0.25f;
	// He gathers himself briefly for a sure thing and visibly longer for a risky leap, and longer still for
	// one that's already failed him.
	static ReachManner Manner => new(WindupMin: 0.06f, WindupMax: 0.5f, MissHesitation: 0.3f);
	static Vector2 WindupCrouch => new(1.25f, 0.75f);
	static float RiskTremble => 0.12f;
	static float TrembleRate => 55f;
	static Vector2 MissFlinch => new(1.35f, 0.7f);
	// This long without a way to the player and he says so; with them overhead, he looks up at them.
	static float StuckPatience => 2.5f;
	static int LookUpHeight => Game.TileSize * 2;
	int missesSeen;
	bool frettedAboutReach;
	readonly ReachPilot pilot;
	private RoomChange? pendingRoomChange;
	private float enterTimer;
	private Point2? enterWalkTarget;
	private bool enterWalkDucking;
	private bool roomSpawnSkipped;
	private bool enteredRoom;
	private Point2 entryPosition;
	static float LaunchLingerTime => 2.5f;
	static float BackDashSpeed => 120;
	static float BackDashDuration => 0.22f;
	static float BackDashFriction => 260;
	static float BackDashCooldown => 0.6f;
	float backDashTimer;
	float backDashCooldownTimer;
	bool ridingPlayerBurrow;
	[NoSerialize] public Player? CarriedBy { get; private set; }
	bool launched;     // in flight from a carry launch; the linger starts on landing
	float lingerTimer; // stay put where the launch landed before nav resumes following
	float burrowPopDelayTimer;
	float fallRespawnTimer; // counts down out of sight after falling out of the room
	float burrowPopInputMirrorTimer;
	Point2 burrowPopPosition;
	Vector2 burrowPopVelocity;
	Vector2 burrowPopSquish;
	Vector2 burrowPopOutward;
	Signs burrowPopFacing;
	float burrowPopJumpTime;
	bool secondControllerControlling;
	Light? playerLight;
	float burrowLightBlend;
	static float BaseLightIntensity => 0.45f;
	static float BurrowLightIntensity => 0.85f;
	static Color BaseLightColor => 0xffcfa0;
	static Color BurrowLightColor => 0xb98cff;
	static float BurrowLightBlendRate => 6f;
	protected override float GroundSpeedMultiplier => 1.0f;
	protected override float AirSpeedMultiplier => 1.0f;
	protected override float IdleBreathingRate => 0.7f;
	protected override int? RumbleControllerIndex =>
		secondControllerControlling ? Game.Controls.BudControllerIndex : null;
	protected override PawnAbilities Abilities => PawnAbilities.Burrow;
	public bool CanSay => Game.CanShowBudSpeech();

	public string DebugNavLabel => pilot.Label;

	public Bud()
	{
		pilot = new(this, () => Manner);
		Hitbox = new(new RectInt(-4, -12, 8, 12));
		Depth = Depths.BehindPlayer;
		PlayPawnAnimation("idle");
	}

	public override void Added()
	{
		Game.RoomChanged += OnRoomChanged;
	}

	public override void Destroyed()
	{
		base.Destroyed();
		Game.RoomChanged -= OnRoomChanged;
		if (playerLight != null)
		{
			Game.Destroy(playerLight);
			playerLight = null;
		}
	}

	// While carried the standing height defines the combined body — the live hitbox may be
	// the ducking rect (caught mid-crawl) and our Update, which would restore it, is
	// suspended in hand.
	public int CarryHeight => StandingHitboxRect.Height;

	// Free-standing and running his own AI: not in hand, not riding or popping out of the
	// player's burrow, not mid room-entry. Mirrors the TryEnterPlayerBurrow eligibility guards.
	bool Interruptible => CarriedBy == null && Visible && !ridingPlayerBurrow
		&& burrowPopDelayTimer <= 0 && pendingRoomChange == null && State == States.Normal;

	// In the future, we'll animate Bud to ask for a carry in specific places maybe? Right now carrying Bud is too annoying and interrupts you too often.
	static bool BudWantsToBeCarried => false;

	// The carry and the burrow-ride are separate relationships: a Bud riding the player's
	// burrow (position-synced to the player, invisible) must not be "caught" by the pop
	// that ends the ride.
	public bool CanBeCaught => Interruptible && BudWantsToBeCarried;

	// One-line state summary for the Game.Show carry-debug overlay (Player.Carry.cs).
	internal string CarryDebugState =>
		$"pos={Position} carried={CarriedBy != null} riding={ridingPlayerBurrow} " +
		$"popDelay={burrowPopDelayTimer:0.00} vis={Visible} state={State} " +
		$"launched={launched} linger={lingerTimer:0.0}";

	public void OnPickedUp(Player player)
	{
		CarriedBy = player;
		enterWalkTarget = null;
		enterWalkDucking = false;
		CollidesWithSolids = false;
		Stop();
		ducking = false;
		Hitbox = new(StandingHitboxRect);
		launched = false;
		lingerTimer = 0;
		State = States.Normal;
		Squish = new Vector2(0.8f, 1.25f);
		PlayPawnAnimation("idle");
		Game.TryBudStorylet("bud_carried");
	}

	public void Release(Point2 position, Vector2 velocity)
	{
		CarriedBy = null;
		CollidesWithSolids = true;
		Position = position;
		Velocity = velocity;
		State = States.Normal;
		// The launch is Bud's own leap off the player's head: full jump float, never cut
		// short; his mid-air recovery steers the landing.
		jumpTimer = JumpTime;
		jumpCutImmune = true;
		launched = velocity.LengthSquared() > 1; // a gentle set-down skips the linger
		if (velocity.X != 0)
			Facing = Math.Sign(velocity.X);
		Squish = new Vector2(0.65f, 1.4f);
		TryUnstick(new Vector2(0, -1));
		if (launched)
		{
			AudioEvent("jump");
			Game.TryBudStorylet("bud_launched");
		}
	}

	// Caught by a swing of the player's sword (the attack hitbox, not a damage source --
	// Bud has no health). He rounds on whoever swung and hops out of reach.
	public bool HitByHitbox(RectInt hitbox)
	{
		if (!Interruptible || backDashCooldownTimer > 0 || !hitbox.Overlaps(Hitbox.Rect + Position))
			return false;

		StartBackDash(Game.GetFirst<Player>()?.Position.X ?? hitbox.Center.X);
		return true;
	}

	void StartBackDash(float attackerX)
	{
		var toAttacker = Math.Sign(attackerX - Position.X);
		if (toAttacker != 0)
			Facing = toAttacker;

		backDashTimer = BackDashDuration;
		backDashCooldownTimer = BackDashCooldown;
		Velocity.X = -(int)Facing * BackDashSpeed;
		jumpTimer = 0;
		Squish = new Vector2(0.75f, 1.3f);
		pilot.Cancel(); // whatever he was in the middle of, re-path after landing
		State = States.BackDash;
		PlayPawnAnimation("back_dash", looping: false);
		AudioEvent("hurt");

		// enable to have Bud say something when you attack him
		//if (!EnemyNearby())
			//Game.TryBudStorylet("bud_struck");
	}

	// A swing that catches him mid-fight was aimed at the enemy, not at him, so he ducks out of the
	// way without remarking on it. The bark is for being swung at with nothing else around.
	private bool EnemyNearby()
	{
		var reach = new RectInt(
			Position.X - StruckSpeechEnemyDistance,
			Position.Y - StruckSpeechEnemyDistance,
			StruckSpeechEnemyDistance * 2,
			StruckSpeechEnemyDistance * 2);
		return Game.OverlapsFirst(reach, Masks.Enemy) != null;
	}

	protected override void CustomState()
	{
		if (State != States.BackDash)
			return;

		backDashTimer = Calc.Approach(backDashTimer, 0, Time.Delta);
		Velocity.X = Calc.Approach(Velocity.X, 0, BackDashFriction * Time.Delta);
		if (backDashTimer <= 0)
			EndBackDash();
	}

	void EndBackDash()
	{
		backDashTimer = 0;
		State = States.Normal;
		PlayPawnAnimation("idle");
	}

	public override PawnInput GetInput()
	{
		if (State == States.BackDash) return new();
		if (burrowPopInputMirrorTimer > 0 && Game.GetFirst<Player>() is { } burrowPlayer)
			return burrowPlayer.GetInput();
		if (Game.GetFirst<Player>()?.ScriptedInput == null &&
			Game.Controls.TryGetBudControllerInput(Game.Manager.Time.Elapsed, out var controllerInput))
		{
			if (!secondControllerControlling)
				pilot.Cancel();
			secondControllerControlling = true;
			return controllerInput;
		}
		secondControllerControlling = false;

		if (!PerfToggles.BudAIEnabled) return new();
		if (lingerTimer > 0 && State == States.Normal)
			return new(); // lingering where the carry launch landed us

		if (TryWalkInFromEntrance(out var enterX, out var enterY))
			return new(enterX, enterY, false, false, false);
		var input = Game.reach.Field is { } field ? pilot.GetInput(field) : default;
		var target = Game.GymMode ? Game.GymTarget : Game.GetFirst<Player>();
		// Gathering for a move he looks where he's going; otherwise at whoever he's after.
		var look = pilot.Phase is ReachPilot.Phases.Winding or ReachPilot.Phases.Timing ? pilot.Heading : target?.Position;
		if (input.MoveX == 0 && State == States.Normal && look is { } at && Math.Sign(at.X - Position.X) is var face and not 0)
			Facing = face;
		if (pilot.Phase == ReachPilot.Phases.Stuck && grounded && target != null && Position.Y - target.Position.Y > LookUpHeight)
			input = input with { MoveY = -1 };
		return input;
	}

	public override void Update()
	{
		// Early returns below suspend Bud's control as well as his light.
		if (playerLight != null)
			playerLight.Intensity = 0;

		if (CarriedBy != null)
			return; // in the player's hands: AI + physics suspended, the carrier syncs position

		if (ridingPlayerBurrow)
		{
			if (Game.GetFirst<Player>() is { } player)
				Position = player.Position;
			return;
		}

		if (burrowPopDelayTimer > 0)
		{
			burrowPopDelayTimer = Calc.Approach(burrowPopDelayTimer, 0, Time.Delta);
			if (burrowPopDelayTimer <= 0)
				CompletePlayerBurrowPop();
			return;
		}

		if (roomSpawnSkipped)
			return;

		if (fallRespawnTimer > 0)
		{
			fallRespawnTimer = Calc.Approach(fallRespawnTimer, 0, Time.Delta);
			if (fallRespawnTimer <= 0)
				FinishFallRespawn();
			return;
		}

		burrowPopInputMirrorTimer = Calc.Approach(burrowPopInputMirrorTimer, 0, Time.Delta);
		backDashCooldownTimer = Calc.Approach(backDashCooldownTimer, 0, Time.Delta);

		if (pendingRoomChange is { } change)
		{
			enterTimer = Calc.Approach(enterTimer, 0, Time.Delta);
			if (enterTimer <= 0)
				EnterRoom(change);
			return;
		}

		base.Update();

		if (launched)
		{
			if (State == States.Climb)
				launched = false; // grabbed a rope mid-flight: the climb takes over
			else if (grounded || InLiquid)
			{
				launched = false;
				lingerTimer = LaunchLingerTime;
				Game.TryBudStorylet("bud_launch_landed");
			}
		}
		else if (lingerTimer > 0)
			lingerTimer = Calc.Approach(lingerTimer, 0, Time.Delta);

		if (!enteredRoom)
		{
			entryPosition = Position;
			enteredRoom = true;
		}

		if (Position.Y > Game.Bounds.Bottom + 12)
		{
			Game.GymBudDied = true;
			BeginFallRespawn();
		}

		ExpressReach();
		UpdatePlayerLight();
	}

	// Shows what the pilot is up to: a crouch that deepens through a windup (and so counts down to a moving
	// platform's arrival), a tremble as big as the risk, a flinch at a missed move, and a word when he's stuck.
	void ExpressReach()
	{
		if (pilot.Phase is ReachPilot.Phases.Winding or ReachPilot.Phases.Timing)
		{
			Squish = Vector2.Lerp(Vector2.One, WindupCrouch, Ease.Cube.In(pilot.Windup));
			Skew = pilot.Risk * RiskTremble * MathF.Sin(Time.SecondsF * TrembleRate);
		}
		if (pilot.Misses != missesSeen)
		{
			missesSeen = pilot.Misses;
			Squish = MissFlinch;
			Game.TryBudStorylet("bud_missed_move");
		}
		if (pilot.StuckTime < StuckPatience)
			frettedAboutReach = false;
		else if (!frettedAboutReach)
		{
			frettedAboutReach = true;
			Game.TryBudStorylet("bud_cant_reach");
		}
	}

	void UpdatePlayerLight()
	{
		burrowLightBlend = Calc.Approach(burrowLightBlend, State == States.Burrow ? 1f : 0f,
			BurrowLightBlendRate * Time.Delta);
		if (!Visible || !secondControllerControlling || Game.CurrentRoom?.PlayerLightEnabled != true ||
			!Game.Controls.TryGetBudControllerInput(Game.Manager.Time.Elapsed, out _))
			return;

		playerLight ??= Player.CreatePlayerLight(this);
		playerLight.Position = Position + new Point2(0, -6);
		playerLight.Intensity = float.Lerp(BaseLightIntensity, BurrowLightIntensity, burrowLightBlend);
		playerLight.LightColor = Color.Lerp(BaseLightColor, BurrowLightColor, burrowLightBlend);
	}

	public override void Render(Batcher batcher)
	{
		if (!BurrowHidesSprite)
			RenderBody(batcher, RigDrawing ? Color.White : Tint);
		RenderBurrowLine(batcher);
	}

	internal Game.StorySpeechBubble? Say(string text, OutLoud.Emotion emotion = OutLoud.Emotion.Neutral)
		=> Game.ShowSpeech(this, new Vector2(0, -32), text, Game.BudVoiceProfile, SpeechColor, emotion, Game.BudVoiceName);

	internal bool TryEnterPlayerBurrow(Player player)
	{
		if (!Visible || pendingRoomChange != null || ridingPlayerBurrow || burrowPopDelayTimer > 0 || State != States.Normal)
			return false;
		if (secondControllerControlling)
			return false;
		if (Vector2.DistanceSquared(Position, player.Position) > BurrowRideRadius * BurrowRideRadius)
			return false;

		ridingPlayerBurrow = true;
		Visible = false;
		CollidesWithSolids = false;
		Stop();
		Game.Create<Pop>(Position + new Point2(0, -6));
		Position = player.Position;
		return true;
	}

	internal void BeginPlayerBurrowPop(Point2 position, Vector2 velocity, Vector2 squish, Signs facing, float jumpTime, Vector2 outward)
	{
		if (!ridingPlayerBurrow)
			return;

		ridingPlayerBurrow = false;
		burrowPopDelayTimer = BurrowPopDelay;
		burrowPopPosition = position;
		burrowPopVelocity = velocity;
		burrowPopSquish = squish;
		burrowPopOutward = outward;
		burrowPopFacing = facing;
		burrowPopJumpTime = jumpTime;
		Position = position;
		Stop();
	}

	void CompletePlayerBurrowPop()
	{
		var playerPosition = Game.GetFirst<Player>()?.Position ?? burrowPopPosition;
		var foundStanding = TryPlaceBurrowPop(burrowPopPosition, StandingHitboxRect)
			|| TryPlaceBurrowPop(playerPosition, StandingHitboxRect);
		if (!foundStanding &&
			!TryPlaceBurrowPop(burrowPopPosition, DuckingHitboxRect) &&
			!TryPlaceBurrowPop(playerPosition, DuckingHitboxRect))
		{
			burrowPopDelayTimer = Time.Delta;
			return;
		}

		Velocity = burrowPopVelocity;
		Squish = burrowPopSquish;
		Facing = burrowPopFacing;
		State = States.Normal;
		ducking = !foundStanding;
		Hitbox = new(ducking ? DuckingHitboxRect : StandingHitboxRect);
		jumpTimer = burrowPopJumpTime;
		jumpCutImmune = true;
		Visible = true;
		CollidesWithSolids = true;
		burrowPopInputMirrorTimer = BurrowPopInputMirrorTime;
		Game.Create<Pop>(Position + new Point2(0, -6));
	}

	bool TryPlaceBurrowPop(Point2 origin, RectInt hitbox)
	{
		Position = origin;
		Hitbox = new(hitbox);
		return TryUnstick(burrowPopOutward);
	}

	// Falling out of the room takes him out of play for FallRespawnDelay before he turns back up
	// where he came in. Hidden and non-colliding for the wait (the same shape as the room-entry
	// delay), so the player can't catch or burrow-ride a Bud who is mid-respawn.
	protected override void CrushRecovered()
	{
		Game.Create<Pop>(Position);
		BeginFallRespawn();
	}

	void BeginFallRespawn()
	{
		Stop();
		fallRespawnTimer = FallRespawnDelay;
		Visible = false;
		CollidesWithSolids = false;
		pilot.Cancel();
	}

	void FinishFallRespawn()
	{
		Position = entryPosition;
		EndFallRespawn();
	}

	// A game-driven placement (player respawn, room reload, save-point warp) has already put him
	// somewhere sensible, so the wait ends there rather than dragging him back to entryPosition.
	internal void CancelFallRespawn()
	{
		if (fallRespawnTimer > 0)
			EndFallRespawn();
	}

	void EndFallRespawn()
	{
		fallRespawnTimer = 0;
		Stop();
		ducking = false;
		Hitbox = new(StandingHitboxRect);
		Visible = true;
		CollidesWithSolids = true;
	}

	private void OnRoomChanged(RoomChange change)
	{
		if (change.Kind == RoomChangeKind.InitialLoad)
		{
			if ((change.To.BudSpawn & Room.BudSpawnFlags.SkipThisRoom) != 0)
				SkipRoomSpawn();
			return;
		}
		if (ridingPlayerBurrow || burrowPopDelayTimer > 0 || CarriedBy != null)
		{
			roomSpawnSkipped = false;
			pendingRoomChange = null;
			enterWalkTarget = null;
			enterWalkDucking = false;
			pilot.Cancel();
			return;
		}
		if ((change.To.BudSpawn & Room.BudSpawnFlags.SkipThisRoom) != 0)
		{
			SkipRoomSpawn();
			return;
		}

		if (State == States.BackDash)
			EndBackDash(); // the entry Update path never reaches CustomState to finish it

		roomSpawnSkipped = false;
		pendingRoomChange = change;
		fallRespawnTimer = 0; // EnterRoom places (and unhides) him; an armed respawn would yank him back
		enterWalkTarget = null;
		enterWalkDucking = false;
		enterTimer = EnterDelay;
		Visible = false;
		CollidesWithSolids = false;
		Velocity = Vector2.Zero;
		pilot.Cancel();
	}

	private void SkipRoomSpawn()
	{
		roomSpawnSkipped = true;
		pendingRoomChange = null;
		fallRespawnTimer = 0;
		enterWalkTarget = null;
		enterWalkDucking = false;
		Visible = false;
		CollidesWithSolids = false;
		Velocity = Vector2.Zero;
		pilot.Cancel();
	}

	private void EnterRoom(RoomChange change)
	{
		pendingRoomChange = null;
		ducking = false;
		Hitbox = new(StandingHitboxRect);
		var position = FindEnterPosition(change);
		var walkDucking = false;
		if (TryFindWalkInEntrance(change, position, StandingHitboxRect, out var start, out var target) ||
			(walkDucking = true) && TryFindWalkInEntrance(change, position, DuckingHitboxRect, out start, out target))
		{
			Position = start;
			enterWalkTarget = target;
			enterWalkDucking = walkDucking;
			ducking = enterWalkDucking;
			Hitbox = new(ducking ? DuckingHitboxRect : StandingHitboxRect);
			entryPosition = target;
		}
		else
		{
			Position = position;
			enterWalkTarget = null;
			enterWalkDucking = false;
			entryPosition = position;
		}
		Velocity = Vector2.Zero;
		CollidesWithSolids = true;
		Visible = true;
		Game.TryBudStorylet("bud_entered");
	}

	private Point2 FindEnterPosition(RoomChange change)
	{
		var anchor = Game.RoomEntryPosition;
		var bounds = change.To.WorldBounds;
		var direction = change.Direction;

		var basePosition = anchor;
		if (change.Kind == RoomChangeKind.EdgeTransition && direction != Point2.Zero)
		{
			if (direction.X > 0)
				basePosition.X = bounds.Left + Game.TileSize;
			else if (direction.X < 0)
				basePosition.X = bounds.Right - Game.TileSize;
			else if (direction.Y > 0)
				basePosition.Y = bounds.Top + Game.TileSize * 2;
			else if (direction.Y < 0)
				basePosition.Y = bounds.Bottom - 1;
		}

		basePosition.X = Math.Clamp(basePosition.X, bounds.Left + Game.TileSize, bounds.Right - Game.TileSize);
		basePosition.Y = Math.Clamp(basePosition.Y, bounds.Top + Game.TileSize * 2, bounds.Bottom - 1);

		ReadOnlySpan<Point2> offsets =
		[
			Point2.Zero,
			new Point2(0, -Game.TileSize),
			new Point2(0, -Game.TileSize * 2),
			new Point2(Game.TileSize, 0),
			new Point2(-Game.TileSize, 0),
			new Point2(Game.TileSize * 2, 0),
			new Point2(-Game.TileSize * 2, 0)
		];

		foreach (var offset in offsets)
		{
			var position = basePosition + offset;
			if (CanEnterAt(change.To, position, true))
				return position;
		}

		for (var radius = 1; radius <= EnterSearchRadiusTiles; radius++)
		{
			for (var y = -radius; y <= radius; y++)
			for (var x = -radius; x <= radius; x++)
			{
				if (Math.Abs(x) != radius && Math.Abs(y) != radius)
					continue;

				var position = basePosition + new Point2(x * Game.TileSize, y * Game.TileSize);
				if (CanEnterAt(change.To, position, true))
					return position;
			}
		}

		if (change.Direction.Y > 0)
		{
			foreach (var offset in offsets)
			{
				var position = basePosition + offset;
				if (CanEnterAt(change.To, position, false))
					return position;
			}
		}

		return CanEnterAt(change.To, basePosition, false) ? basePosition : Game.RoomEntryPosition;
	}

	private bool CanEnterAt(Room room, Point2 position, bool requireGround)
		=> CanEnterAt(room, position, StandingHitboxRect, requireGround);

	private bool CanEnterAt(Room room, Point2 position, RectInt hitbox, bool requireGround)
	{
		var body = hitbox + position;
		if (!room.WorldBounds.Contains(body) || Game.OverlapsRoomCollision(room, body))
			return false;

		if (!requireGround)
			return true;

		var ground = new RectInt(body.Left, body.Bottom, body.Width, EnterGroundProbeDepth);
		return Game.OverlapsRoomCollision(room, ground) || OverlapsJumpthruSupport(room, ground);
	}

	private bool TryFindWalkInEntrance(RoomChange change, Point2 fallbackTarget, RectInt hitbox, out Point2 start, out Point2 target)
	{
		start = default;
		target = default;
		if (change.Kind != RoomChangeKind.EdgeTransition || change.Direction.X == 0)
			return false;

		var room = change.To;
		var bounds = room.WorldBounds;
		var inward = Math.Sign(change.Direction.X);
		var startX = inward > 0
			? bounds.Left - hitbox.Right + 1
			: bounds.Right - hitbox.Left - 1;
		start = new Point2(startX, fallbackTarget.Y);
		if (!CanWalkInFrom(room, start, target: fallbackTarget, hitbox))
			return false;

		target = fallbackTarget;
		if ((target.X - start.X) * inward < EnterWalkDistance)
		{
			var targetX = start.X + inward * EnterWalkDistance;
			target = new Point2(targetX, fallbackTarget.Y);
			if (!CanEnterAt(room, target, hitbox, true))
				target = fallbackTarget;
		}

		return (target.X - start.X) * inward > EnterWalkReachDistance &&
			IsWalkInPathClear(room, start, target, hitbox);
	}

	private bool IsWalkInPathClear(Room room, Point2 start, Point2 target, RectInt hitbox)
	{
		var direction = Math.Sign(target.X - start.X);
		if (direction == 0)
			return false;

		for (var x = start.X; direction > 0 ? x <= target.X : x >= target.X; x += direction * EnterWalkPathStep)
			if (!CanWalkInAt(room, new Point2(x, start.Y), hitbox))
				return false;

		return CanEnterAt(room, target, hitbox, true);
	}

	private bool CanWalkInFrom(Room room, Point2 start, Point2 target, RectInt hitbox)
	{
		var direction = Math.Sign(target.X - start.X);
		if (direction == 0)
			return false;

		var firstInsideX = direction > 0
			? room.WorldBounds.Left - hitbox.Left + 1
			: room.WorldBounds.Right - hitbox.Right - 1;
		for (var x = start.X; direction > 0 ? x <= firstInsideX : x >= firstInsideX; x += direction * EnterWalkPathStep)
			if (!CanWalkInAt(room, new Point2(x, start.Y), hitbox))
				return false;

		return CanEnterAt(room, new Point2(firstInsideX, start.Y), hitbox, true);
	}

	private bool CanWalkInAt(Room room, Point2 position, RectInt hitbox)
	{
		var body = hitbox + position;
		var allowed = room.WorldBounds;
		if (body.Left < room.WorldBounds.Left)
			allowed.Left -= EnterWalkOffscreenPixels + hitbox.Width;
		else if (body.Right > room.WorldBounds.Right)
			allowed.Right += EnterWalkOffscreenPixels + hitbox.Width;

		if (!allowed.Contains(body) || Game.OverlapsRoomCollision(room, body))
			return false;

		var ground = new RectInt(body.Left, body.Bottom, body.Width, EnterGroundProbeDepth);
		return Game.OverlapsRoomCollision(room, ground) || OverlapsJumpthruSupport(room, ground);
	}

	private bool TryWalkInFromEntrance(out float moveX, out int moveY)
	{
		moveX = 0;
		moveY = 0;
		if (enterWalkTarget is not { } target)
			return false;

		var dx = target.X - Position.X;
		if (Math.Abs(dx) <= EnterWalkReachDistance)
		{
			Position = target;
			enterWalkTarget = null;
			enterWalkDucking = false;
			return false;
		}

		moveX = Math.Sign(dx);
		moveY = enterWalkDucking ? 1 : 0;
		return true;
	}

	private bool OverlapsJumpthruSupport(Room room, RectInt probe)
	{
		foreach (var actor in Game.Actors)
		{
			if (actor == this || actor.Room != room || !actor.Mask.Has(Masks.Jumpthru))
				continue;
			if (actor.Hitbox.Shape == Hitbox.Shapes.Rect && probe.Overlaps(actor.Hitbox.Rect + actor.Position))
				return true;
		}
		return false;
	}
}
