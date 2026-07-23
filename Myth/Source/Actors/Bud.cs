using Foster.Framework;
using System.Numerics;
namespace Myth;

public class Bud : Pawn
{
	internal static readonly Color Tint = new(0x7fb6ff);
	private const int FollowStopDistance = 18;
	private const int DesiredPlayerSpacing = 20;
	private const float EnterDelay = 1.0f;
	private const float MirrorPlayerCrawlDelay = 0.25f;
	private const float MirrorPlayerCrawlReleaseDelay = 0.25f;
	static float BurrowRideRadius => 14f;
	static float BurrowPopDelay => 0.25f;
	static float BurrowPopInputMirrorTime => 0.25f;
	private readonly Navigator nav;
	private RoomChange? pendingRoomChange;
	private float enterTimer;
	private bool enteredRoom;
	private Point2 entryPosition;
	private float mirrorPlayerCrawlTimer;
	private float mirrorPlayerCrawlReleaseTimer;
	private bool mirrorPlayerCrawl;
	bool ridingPlayerBurrow;
	float burrowPopDelayTimer;
	float burrowPopInputMirrorTimer;
	Point2 burrowPopPosition;
	Vector2 burrowPopVelocity;
	Vector2 burrowPopSquish;
	Vector2 burrowPopOutward;
	Signs burrowPopFacing;
	float burrowPopJumpTime;
	protected override float GroundSpeedMultiplier => 1.0f;
	protected override float AirSpeedMultiplier => 1.0f;
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
		Depth = -4;
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

	public override PawnInput GetInput()
	{
		if (!PerfToggles.BudAIEnabled) return new();
		if (burrowPopInputMirrorTimer > 0 && Game.GetFirst<Player>() is { } burrowPlayer)
			return burrowPlayer.GetInput();

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
			if (nav.TryRecoverLanding(out moveX, out jumpPressed, out recoverWallJump))
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

		burrowPopInputMirrorTimer = Calc.Approach(burrowPopInputMirrorTimer, 0, Time.Delta);

		if (pendingRoomChange is { } change)
		{
			enterTimer = Calc.Approach(enterTimer, 0, Time.Delta);
			nav.SetDebugNavState(Game.IsPlatformNavDebugVisible ? $"entering {enterTimer:0.0}s" : "entering");
			if (enterTimer <= 0)
				EnterRoom(change);
			return;
		}

		base.Update();

		if (!enteredRoom)
		{
			entryPosition = Position;
			enteredRoom = true;
		}

		if (Position.Y > Game.Bounds.Bottom + 12)
		{
			Game.GymBudDied = true;
			Game.NavTrace?.Event(Game, "budFell", ("pos", NavTrace.Vec(Position)), ("vel", NavTrace.Vec(Velocity)));
			Stop();
			Position = entryPosition;
		}

		Game.NavTrace?.Frame(Game, this);
	}

	public override void Render(Batcher batcher)
	{
		RenderSpriteSubtexture(batcher, Tint);
	}

	public void Say(string text, OutLoud.Emotion emotion = OutLoud.Emotion.Neutral)
		=> Game.ShowBudSpeech(Position + new Vector2(0, -32), text, emotion);

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

		var preferredSide = Position.X <= player.Position.X ? -1 : 1;
		if (TryNavigateSameSurfaceBesidePlayer(graph, player, preferredSide, out moveX, out moveY))
			return true;
		if (TryNavigateSameSurfaceBesidePlayer(graph, player, -preferredSide, out moveX, out moveY))
			return true;

		var start = graph.FindNearestNode(Position, Navigator.NavSearchDistance);
		PlatformNavGraph.Node? goal;
		PlatformNavGraph.Node cachedGoal = default;
		bool pathFound;
		var reusedPath = start.HasValue && nav.TryReuseNavPath(graph, start.Value.Id, player.Position, out cachedGoal);
		if (reusedPath)
		{
			goal = cachedGoal;
			pathFound = true;
		}
		else
			goal = FindGoalNodeBesidePlayer(graph, player, start, out pathFound);
		if (!start.HasValue || !goal.HasValue || start.Value.Id == goal.Value.Id)
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
			nav.RememberNavPath(graph, start.Value.Id, goal.Value.Id, player.Position);
		nav.UpdateDebugSurfacePath(graph, start.Value.Id, goal.Value.Id);

		if (!nav.TryGetActionableEdge(graph, out var edge))
		{
			nav.DebugNavStartNodeId = start.Value.Id;
			nav.DebugNavGoalNodeId = goal.Value.Id;
			nav.DebugNavTargetNodeId = null;
			nav.DebugNavLabel = "edge reached";
			return false;
		}

		if (edge.Kind is PlatformNavGraph.EdgeKind.Jump or PlatformNavGraph.EdgeKind.Fall or PlatformNavGraph.EdgeKind.WellClimb or PlatformNavGraph.EdgeKind.Rope)
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
		if (ridingPlayerBurrow || burrowPopDelayTimer > 0)
		{
			pendingRoomChange = null;
			nav.ClearBlockedPathEdges();
			return;
		}

		pendingRoomChange = change;
		enterTimer = EnterDelay;
		Visible = false;
		CollidesWithSolids = false;
		Velocity = Vector2.Zero;
		nav.ClearBlockedPathEdges();
	}

	private void EnterRoom(RoomChange change)
	{
		pendingRoomChange = null;
		Position = FindEnterPosition(change);
		entryPosition = Position;
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
			var hitbox = Hitbox.Rect + position;
			if (!Game.OverlapsRoomCollision(hitbox))
				return position;
		}

		return basePosition;
	}
}
