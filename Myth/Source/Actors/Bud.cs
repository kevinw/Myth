using Foster.Framework;
using System.Numerics;
namespace Myth;

public class Bud : Pawn, ICarryable, IHitByHitbox
{
	public static bool SecondControllerControlEnabled = true;
	internal static readonly Color Tint = new(0x7fb6ff);
	// His speech reads as his own blue, lifted toward white so it stays legible over dark rooms.
	internal static readonly Color SpeechColor = Color.Lerp(Tint, Color.White, 0.45f);
	private const int FollowStopDistance = 18;
	private const int DesiredPlayerSpacing = 20;
	private const int StruckSpeechEnemyDistance = Game.TileSize * 4;
	private const float EnterDelay = 1.0f;
	private const float MirrorPlayerCrawlDelay = 0.25f;
	private const float MirrorPlayerCrawlReleaseDelay = 0.25f;
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
	private readonly Navigator nav;
	private RoomChange? pendingRoomChange;
	private float enterTimer;
	private Point2? enterWalkTarget;
	private bool enterWalkDucking;
	private bool enteredRoom;
	private Point2 entryPosition;
	private float mirrorPlayerCrawlTimer;
	private float mirrorPlayerCrawlReleaseTimer;
	private bool mirrorPlayerCrawl;
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
	protected override float GroundSpeedMultiplier => 1.0f;
	protected override float AirSpeedMultiplier => 1.0f;
	protected override float IdleBreathingRate => 0.7f;
	public bool CanSay => Game.CanShowBudSpeech();

	// Forwarded for editor nav debug visualization
	public int? DebugNavStartNodeId => nav.DebugNavStartNodeId;
	public int? DebugNavGoalNodeId => nav.DebugNavGoalNodeId;
	public int? DebugNavTargetNodeId => nav.DebugNavTargetNodeId;
	public string DebugNavLabel => nav.DebugNavLabel;
	public IReadOnlyList<PlatformNavGraph.Edge> DebugNavPath => nav.DebugNavPath;
	public IReadOnlyList<PlatformNavGraph.SurfaceEdge> DebugSurfaceNavPath => nav.DebugSurfaceNavPath;
	public RectInt? DebugNavProbeRect => nav.DebugNavProbeRect;
	public bool DebugNavProbeHit => nav.DebugNavProbeHit;

	public Bud()
	{
		nav = new(this);
		Hitbox = new(new RectInt(-4, -12, 8, 12));
		Depth = Depths.BehindPlayer;
		Play("idle");
	}

	public override void Added()
	{
		Game.RoomChanged += OnRoomChanged;
	}

	public override void Destroyed()
	{
		Game.RoomChanged -= OnRoomChanged;
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
		Play("idle");
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
	public void HitByHitbox(RectInt hitbox)
	{
		if (!Interruptible || backDashCooldownTimer > 0 || !hitbox.Overlaps(Hitbox.Rect + Position))
			return;

		StartBackDash(Game.GetFirst<Player>()?.Position.X ?? hitbox.Center.X);
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
		nav.ClearActiveEdge("back dash"); // whatever he was in the middle of, re-path after landing
		State = States.BackDash;
		Play("back_dash", looping: false);
		AudioEvent("hurt");
		if (!EnemyNearby())
			Game.TryBudStorylet("bud_struck");
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
		Play("idle");
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
				nav.ClearActiveEdge("controller 2");
			secondControllerControlling = true;
			nav.SetDebugNavState("controller 2");
			return controllerInput;
		}
		secondControllerControlling = false;

		if (!PerfToggles.BudAIEnabled) return new();
		if (lingerTimer > 0 && State == States.Normal)
			return new(); // lingering where the carry launch landed us

		// On a rope: climb toward the player's height, then hop off toward them.
		if (State == States.Climb && !nav.ActiveEdgeIsRope && Game.GetFirst<Player>() is { } climbTarget)
		{
			var dy = climbTarget.Position.Y - Position.Y;
			if (MathF.Abs(dy) <= 10)
			{
				var dir = Math.Sign(climbTarget.Position.X - Position.X);
				return new(dir, 0, JumpPressed: true, JumpDown: true, DashPressed: false);
			}
			return new(0, Math.Sign(dy), false, false, false);
		}

		var moveX = 0f;
		var moveY = 0;
		var jumpPressed = false;
		var player = Game.GetFirst<Player>();
		var gymTarget = Game.GymMode ? Game.GymTarget : null;
		var followTarget = gymTarget != null
			? (Vector2?)gymTarget.Position
			: player != null ? DesiredFollowPosition(player) : null;
		var recoverWallJump = false;
		if (!nav.TryExecuteActiveEdge(out moveX, out moveY, out jumpPressed))
		{
			if (TryWalkInFromEntrance(out moveX, out moveY))
			{
				// Finishing the room-entry reveal; normal following resumes at the target.
			}
			else if (nav.TryRecoverLanding(out moveX, out jumpPressed, out recoverWallJump))
			{
				// Mid-air with no edge to run (a missed/aborted arc): steer onto a reachable
				// surface — or over a pit, onto a wall to kick back up — pathing resumes
				// once grounded.
			}
			else if (gymTarget != null)
			{
				if (nav.TryClimbWellToward(gymTarget.Position, out moveX, out moveY, out jumpPressed, out recoverWallJump))
				{
					// Goal overhead in a walled shaft: chained wall kicks are the only way up.
				}
				else if (!nav.TryNavigateToPosition(gymTarget.Position, out moveX, out moveY, out jumpPressed))
				{
					var navLabel = nav.DebugNavLabel;
					if (nav.TryRecoverPit(out moveX, out jumpPressed, out recoverWallJump))
					{
						moveY = 0;
					}
					else if (navLabel == "no graph")
					{
						moveX = 0;
						moveY = 0;
						nav.SetDebugNavState(navLabel);
					}
					else
					{
						moveX = GetFallbackMoveX(followTarget);
						moveY = 0;
						if (grounded && moveX != 0 && !nav.HasGroundSupportAhead(moveX))
							moveX = 0;
						nav.SetDebugNavState(moveX == 0
							? navLabel
							: Game.IsPlatformNavDebugVisible ? $"gym fallback {moveX}" : "gym fallback");
					}
				}
			}
			else if (player == null || !TryNavigateToPlayer(player, out moveX, out moveY, out jumpPressed))
			{
				var navLabel = nav.DebugNavLabel;
				if (nav.TryRecoverPit(out moveX, out jumpPressed, out recoverWallJump))
				{
					moveY = 0;
				}
				else
				{
					moveX = GetFallbackMoveX(followTarget, player);
					moveY = ShouldMirrorPlayerCrawl(player) ? 1 : 0;
					if (grounded && moveX != 0 && !nav.HasGroundSupportAhead(moveX))
						moveX = 0;
					if (TryApproachRope(player, ref moveX, ref moveY))
						nav.SetDebugNavState("rope approach");
					else if (player == null)
						nav.SetDebugNavState("no player");
					else
						nav.SetDebugNavState(moveX == 0
							? IsBesidePlayer(player) ? "beside player" : navLabel
							: Game.IsPlatformNavDebugVisible ? $"fallback walk {moveX}" : "fallback walk");
				}
			}
		}

		nav.UpdateJumpHeld(jumpPressed);

		if (moveX == 0)
		{
			var faceTarget = gymTarget?.Position ?? player?.Position;
			if (faceTarget != null)
			{
				var face = Math.Sign(faceTarget.Value.X - Position.X);
				if (face != 0)
					Facing = face;
			}
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

		if (fallRespawnTimer > 0)
		{
			fallRespawnTimer = Calc.Approach(fallRespawnTimer, 0, Time.Delta);
			nav.SetDebugNavState(Game.IsPlatformNavDebugVisible ? $"fell {fallRespawnTimer:0.0}s" : "fell");
			if (fallRespawnTimer <= 0)
				FinishFallRespawn();
			return;
		}

		burrowPopInputMirrorTimer = Calc.Approach(burrowPopInputMirrorTimer, 0, Time.Delta);
		backDashCooldownTimer = Calc.Approach(backDashCooldownTimer, 0, Time.Delta);

		if (pendingRoomChange is { } change)
		{
			enterTimer = Calc.Approach(enterTimer, 0, Time.Delta);
			nav.SetDebugNavState(Game.IsPlatformNavDebugVisible ? $"entering {enterTimer:0.0}s" : "entering");
			if (enterTimer <= 0)
				EnterRoom(change);
			return;
		}

		base.Update();

		if (launched)
		{
			if (State == States.Climb)
				launched = false; // grabbed a rope mid-flight: the climb takes over
			else if (grounded || InWater)
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
			Game.NavTrace?.Event(Game, "budFell", ("pos", NavTrace.Vec(Position)), ("vel", NavTrace.Vec(Velocity)));
			BeginFallRespawn();
		}

		Game.NavTrace?.Frame(Game, this);
	}

	public override void Render(Batcher batcher)
	{
		RenderSpriteSubtexture(batcher, Tint);
	}

	internal Game.StorySpeechBubble? Say(string text, OutLoud.Emotion emotion = OutLoud.Emotion.Neutral)
		=> Game.ShowSpeech(this, new Vector2(0, -32), text, Game.BudVoiceProfile, SpeechColor, emotion, Game.BudVoiceName);

	internal bool TryEnterPlayerBurrow(Player player)
	{
		if (!Visible || pendingRoomChange != null || ridingPlayerBurrow || burrowPopDelayTimer > 0 || State != States.Normal)
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
	void BeginFallRespawn()
	{
		Stop();
		fallRespawnTimer = FallRespawnDelay;
		Visible = false;
		CollidesWithSolids = false;
		nav.ClearActiveEdge("fell off the world");
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

	private bool TryNavigateToPlayer(Player player, out float moveX, out int moveY, out bool jumpPressed)
	{
		moveX = 0;
		moveY = 0;
		jumpPressed = false;

		// Following through water needs the swim-capable navigation; the beside-player,
		// same-surface logic below is land-only. Head straight for the player instead.
		if (InWater || player.InWater)
			return nav.TryNavigateToPosition(player.Position, out moveX, out moveY, out jumpPressed);

		if (Game.GetPlatformNavGraph() is not { } graph)
		{
			nav.SetDebugNavState("no graph");
			return false;
		}

		var playerPlatform = player.OverlapsFirst(Point2.Down, Masks.Solid) as MovingPlatform;
		if (playerPlatform is { SupportsPlatformNav: false })
			playerPlatform = null;
		if (playerPlatform != null && nav.TryFollowPlayerOnPlatform(player, playerPlatform, out moveX))
			return true;

		if (playerPlatform == null)
		{
			var preferredSide = Position.X <= player.Position.X ? -1 : 1;
			if (TryNavigateSameSurfaceBesidePlayer(graph, player, preferredSide, out moveX, out moveY))
				return true;
			if (TryNavigateSameSurfaceBesidePlayer(graph, player, -preferredSide, out moveX, out moveY))
				return true;
		}

		var pathTarget = playerPlatform?.NavStartPosition ?? player.Position;
		var start = graph.FindNearestNode(Position, Navigator.NavSearchDistance);
		PlatformNavGraph.Node? goal;
		PlatformNavGraph.Node cachedGoal = default;
		bool pathFound;
		var reusedPath = start.HasValue &&
			nav.TryReuseNavPath(graph, start.Value.Id, pathTarget, out cachedGoal) &&
			PathMatchesPlayerPlatform(playerPlatform);
		if (reusedPath)
		{
			goal = cachedGoal;
			pathFound = true;
		}
		else if (playerPlatform != null)
			goal = FindGoalOnMovingPlatform(graph, playerPlatform, start, out pathFound);
		else
			goal = FindGoalNodeBesidePlayer(graph, player, start, out pathFound);
		if (!start.HasValue || !goal.HasValue || start.Value.Id == goal.Value.Id && !pathFound)
		{
			nav.SetDebugNavState(start.HasValue && goal.HasValue ? "at goal" : "no node");
			return false;
		}

		if ((!pathFound && !graph.TryFindPath(start.Value.Id, goal.Value.Id, nav.NavPath)) || nav.NavPath.Count <= 0)
		{
			nav.ClearDebugSurfacePath();
			nav.DebugNavStartNodeId = start.Value.Id;
			nav.DebugNavGoalNodeId = goal.Value.Id;
			nav.DebugNavTargetNodeId = null;
			nav.DebugNavLabel = Game.IsPlatformNavDebugVisible ? $"no path {start.Value.Id}->{goal.Value.Id}" : "no path";
			return false;
		}
		if (!reusedPath)
			nav.RememberNavPath(graph, start.Value.Id, goal.Value.Id, pathTarget);
		nav.UpdateDebugSurfacePath(graph, start.Value.Id, goal.Value.Id);

		if (!nav.TryGetActionableEdge(graph, out var edge))
		{
			nav.DebugNavStartNodeId = start.Value.Id;
			nav.DebugNavGoalNodeId = goal.Value.Id;
			nav.DebugNavTargetNodeId = null;
			nav.DebugNavLabel = "edge reached";
			return false;
		}

		if (edge.Kind is PlatformNavGraph.EdgeKind.Jump or PlatformNavGraph.EdgeKind.Fall or PlatformNavGraph.EdgeKind.WellClimb or PlatformNavGraph.EdgeKind.Rope or PlatformNavGraph.EdgeKind.MovingPlatform)
		{
			nav.DebugNavStartNodeId = start.Value.Id;
			nav.DebugNavGoalNodeId = goal.Value.Id;
			nav.DebugNavTargetNodeId = edge.To;
			nav.StartActiveEdge(edge);
			return nav.TryExecuteActiveEdge(out moveX, out moveY, out jumpPressed);
		}

		var from = graph.Nodes[edge.From];
		var to = graph.Nodes[edge.To];
		var target = to.Position;
		switch (edge.Kind)
		{
			case PlatformNavGraph.EdgeKind.Walk:
			case PlatformNavGraph.EdgeKind.Crawl:
				moveX = nav.MoveTowardNavTarget(target);
				moveY = edge.Kind == PlatformNavGraph.EdgeKind.Crawl || to.RequiresDucking || ShouldMirrorPlayerCrawl(player) ? 1 : 0;
				if (moveX != 0 && !nav.HasGroundSupportAhead(moveX))
				{
					if (nav.TryContinueWithJumpAfterBlockedWalk(graph, edge, start.Value.Id, goal.Value.Id, out moveX, out moveY, out jumpPressed))
						return true;

					nav.DebugNavStartNodeId = start.Value.Id;
					nav.DebugNavGoalNodeId = goal.Value.Id;
					nav.DebugNavTargetNodeId = to.Id;
					nav.DebugNavLabel = Game.IsPlatformNavDebugVisible ? $"edge stop {edge.Kind} {edge.From}->{edge.To}" : "edge stop";
					return false;
				}
				if (moveX == 0 && !nav.IsNearNode(target))
				{
					nav.DebugNavStartNodeId = start.Value.Id;
					nav.DebugNavGoalNodeId = goal.Value.Id;
					nav.DebugNavTargetNodeId = to.Id;
					nav.DebugNavLabel = Game.IsPlatformNavDebugVisible ? $"stalled {edge.Kind} {edge.From}->{edge.To}" : "stalled";
					return false;
				}
				break;

			case PlatformNavGraph.EdgeKind.RoomExit:
				moveX = nav.MoveTowardNavTarget(target);
				moveY = ShouldMirrorPlayerCrawl(player) ? 1 : 0;
				break;

			case PlatformNavGraph.EdgeKind.Swim:
				(moveX, moveY, jumpPressed) = nav.ExecuteSwimEdge(graph, edge);
				break;

			default:
				nav.DebugNavLabel = Game.IsPlatformNavDebugVisible ? $"unsupported {edge.Kind}" : "unsupported";
				return false;
		}

		nav.DebugNavStartNodeId = start.Value.Id;
		nav.DebugNavGoalNodeId = goal.Value.Id;
		nav.DebugNavTargetNodeId = to.Id;
		nav.DebugNavLabel = Game.IsPlatformNavDebugVisible ? $"{edge.Kind} {edge.From}->{edge.To} len {nav.NavPath.Count}" : "following path";
		return true;
	}

	private bool TryNavigateSameSurfaceBesidePlayer(PlatformNavGraph graph, Player player, int side, out float moveX, out int moveY)
	{
		var desired = player.Position + new Vector2(side * DesiredPlayerSpacing, 0);
		return nav.TryFollowSameSurface(graph, desired, ShouldMirrorPlayerCrawl(player), out moveX, out moveY);
	}

	private bool ShouldMirrorPlayerCrawl(Player? player)
	{
		if (player == null || !grounded || !player.IsGrounded || !player.IsDucking)
			return UpdateMirrorPlayerCrawl(false);

		var nearby = MathF.Abs(player.Position.X - Position.X) <= DesiredPlayerSpacing * 2 &&
			MathF.Abs(player.Position.Y - Position.Y) <= Game.TileSize + 2;
		if (!nearby)
			return UpdateMirrorPlayerCrawl(false);

		return UpdateMirrorPlayerCrawl(true);
	}

	private bool UpdateMirrorPlayerCrawl(bool wantsCrawl)
	{
		if (!wantsCrawl)
		{
			mirrorPlayerCrawlTimer = 0;
			if (!mirrorPlayerCrawl)
				return false;

			mirrorPlayerCrawlReleaseTimer += Time.Delta;
			if (mirrorPlayerCrawlReleaseTimer >= MirrorPlayerCrawlReleaseDelay)
				mirrorPlayerCrawl = false;
			return mirrorPlayerCrawl;
		}

		mirrorPlayerCrawlReleaseTimer = 0;
		if (mirrorPlayerCrawl)
			return true;

		mirrorPlayerCrawlTimer += Time.Delta;
		if (mirrorPlayerCrawlTimer >= MirrorPlayerCrawlDelay)
			mirrorPlayerCrawl = true;
		return mirrorPlayerCrawl;
	}

	private Vector2 DesiredFollowPosition(Player player)
	{
		var side = Position.X <= player.Position.X ? -1 : 1;
		return player.Position + new Vector2(side * DesiredPlayerSpacing, 0);
	}

	private bool IsBesidePlayer(Player player)
		=> MathF.Abs(Position.X - player.Position.X) <= FollowStopDistance &&
		   MathF.Abs(Position.Y - player.Position.Y) <= Game.TileSize + Navigator.NavNodeReachDistance;

	private readonly List<PlatformNavGraph.Node> goalCandidates = [];
	readonly List<PlatformNavGraph.Edge> movingPlatformPathCandidate = [];
	readonly List<PlatformNavGraph.Edge> movingPlatformBestPath = [];

	bool PathMatchesPlayerPlatform(MovingPlatform? platform)
	{
		if (nav.NavPath[^1] is not { Kind: PlatformNavGraph.EdgeKind.MovingPlatform, PlatformRide.StayAboard: true } edge)
			return platform == null;
		return platform?.ActorId == edge.ConnectorActorId;
	}

	PlatformNavGraph.Node? FindGoalOnMovingPlatform(
		PlatformNavGraph graph,
		MovingPlatform platform,
		PlatformNavGraph.Node? start,
		out bool pathFound)
	{
		pathFound = false;
		if (!start.HasValue || platform.ActorId is not { } actorId)
			return null;

		PlatformNavGraph.MovingPlatformAttachment? best = null;
		var bestCost = float.MaxValue;
		var bestDistance = float.MaxValue;
		movingPlatformBestPath.Clear();
		foreach (var attachment in graph.MovingPlatformAttachments)
		{
			if (attachment.ActorId != actorId ||
				!graph.TryFindPath(start.Value.Id, attachment.NodeId, movingPlatformPathCandidate))
				continue;

			var distance = Vector2.DistanceSquared(Position, graph.Nodes[attachment.NodeId].Position);
			var cost = graph.PathCost(movingPlatformPathCandidate);
			if (cost > bestCost || cost == bestCost && distance >= bestDistance)
				continue;

			best = attachment;
			bestCost = cost;
			bestDistance = distance;
			movingPlatformBestPath.Clear();
			movingPlatformBestPath.AddRange(movingPlatformPathCandidate);
		}

		if (best is not { } destination)
			return null;

		nav.NavPath.Clear();
		nav.NavPath.AddRange(movingPlatformBestPath);
		nav.NavPath.Add(new(
			destination.NodeId,
			destination.NodeId,
			PlatformNavGraph.EdgeKind.MovingPlatform,
			ConnectorActorId: actorId,
			PlatformRide: new(
				destination.RouteStart,
				destination.RouteEnd,
				destination.RouteStart,
				destination.RouteEnd,
				0,
				StayAboard: true)));
		pathFound = true;
		return graph.Nodes[destination.NodeId];
	}

	PlatformNavGraph.Node? FindGoalNodeBesidePlayer(PlatformNavGraph graph, Player player, PlatformNavGraph.Node? start, out bool pathFound)
	{
		pathFound = false;
		// Head for the nearest node Bud can actually path to, not just the nearest node:
		// otherwise, standing in a slot, he'd pick a closer-looking node across a thin
		// wall (which is only reachable the long way around) instead of the platform
		// right next to the player.
		if (start.HasValue)
		{
			graph.FindNearestNodes(player.Position, Navigator.GoalNodeSearchDistance, goalCandidates);
			foreach (var node in goalCandidates) // nearest-first
			{
				if (node.Id == start.Value.Id)
					return node;
				if (graph.TryFindPath(start.Value.Id, node.Id, nav.NavPath))
				{
					pathFound = true;
					return node;
				}
			}
		}

		// Nothing reachable found: fall back to the beside-the-player heuristic.
		var preferredSide = Position.X <= player.Position.X ? -1 : 1;
		return FindGoalNodeBesidePlayer(graph, player, preferredSide)
			?? FindGoalNodeBesidePlayer(graph, player, -preferredSide)
			?? graph.FindNearestNode(player.Position, Navigator.GoalNodeSearchDistance);
	}

	private static PlatformNavGraph.Node? FindGoalNodeBesidePlayer(PlatformNavGraph graph, Player player, int side)
	{
		var desired = player.Position + new Vector2(side * DesiredPlayerSpacing, 0);
		PlatformNavGraph.Node? best = null;
		var bestScore = Navigator.GoalNodeSearchDistance * Navigator.GoalNodeSearchDistance;
		foreach (var node in graph.Nodes)
		{
			if (node.Kind == PlatformNavGraph.NodeKind.RoomExit)
				continue;

			var distanceSquared = Vector2.DistanceSquared(node.Position, desired);
			if (distanceSquared >= bestScore)
				continue;

			if (MathF.Abs(node.Position.X - player.Position.X) < DesiredPlayerSpacing * 0.5f)
				continue;

			best = node;
			bestScore = distanceSquared;
		}
		return best;
	}

	private int GetFallbackMoveX(Vector2? target, Player? player = null)
	{
		if (target == null)
			return 0;

		var dx = target.Value.X - Position.X;
		if (MathF.Abs(dx) > FollowStopDistance)
			return Math.Sign(dx);

		if (player != null && !IsBesidePlayer(player))
			return Position.X <= player.Position.X ? -1 : 1;

		return 0;
	}

	// Nav can't path to the player (a sheer drop or climb), but a rope bridges the vertical
	// gap: head for it -- walking off the lip if needed -- and hold up so the Pawn grab takes
	// over once a rope point is in reach. No nav-graph integration yet; this is the "player
	// paved the way with a RopePile" case.
	private bool TryApproachRope(Player? player, ref float moveX, ref int moveY)
	{
		if (player == null || State != States.Normal)
			return false;

		var dy = player.Position.Y - Position.Y;
		if (MathF.Abs(dy) < Game.TileSize * 3)
			return false;

		Rope? best = null;
		var bestDx = Game.TileSize * 5f;
		foreach (var actor in Game.Actors)
		{
			if (actor is not Rope rope || rope.Broken)
				continue;

			// The rope must roughly span from our height toward the player's.
			var bounds = rope.GetVisualBounds();
			var spans = dy > 0
				? bounds.Top <= Position.Y + Game.TileSize && bounds.Bottom >= player.Position.Y - Game.TileSize * 2
				: bounds.Bottom >= Position.Y - Game.TileSize && bounds.Top <= player.Position.Y + Game.TileSize * 2;
			if (!spans)
				continue;

			var dx = MathF.Abs(bounds.Center.X - Position.X);
			if (dx < bestDx)
			{
				bestDx = dx;
				best = rope;
			}
		}
		if (best == null)
			return false;

		var targetX = best.GetVisualBounds().Center.X;
		moveX = MathF.Abs(targetX - Position.X) > 2 ? Math.Sign(targetX - Position.X) : 0;
		moveY = -1; // hold up: grabs as soon as a rope point is in reach
		return true;
	}

	private void OnRoomChanged(RoomChange change)
	{
		if (change.Kind == RoomChangeKind.InitialLoad)
			return;
		if (ridingPlayerBurrow || burrowPopDelayTimer > 0 || CarriedBy != null)
		{
			pendingRoomChange = null;
			enterWalkTarget = null;
			enterWalkDucking = false;
			nav.ClearBlockedPathEdges();
			return;
		}

		if (State == States.BackDash)
			EndBackDash(); // the entry Update path never reaches CustomState to finish it

		pendingRoomChange = change;
		fallRespawnTimer = 0; // EnterRoom places (and unhides) him; an armed respawn would yank him back
		enterWalkTarget = null;
		enterWalkDucking = false;
		enterTimer = EnterDelay;
		Visible = false;
		CollidesWithSolids = false;
		Velocity = Vector2.Zero;
		nav.ClearBlockedPathEdges();
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
		nav.SetDebugNavState(Game.IsPlatformNavDebugVisible ? $"enter walk {target}" : "enter walk");
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
