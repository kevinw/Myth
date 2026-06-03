using Foster.Framework;
using System.Numerics;
namespace TinyLink;

public class Bud : Pawn
{
	private static readonly Color Tint = new(0x7fb6ff);
	private const int FollowStopDistance = 18;
	private const int DesiredPlayerSpacing = 20;
	private const int NavNodeReachDistance = 8;
	private const float NavSearchDistance = 180;
	private const float GoalNodeSearchDistance = 56;
	private const float EnterDelay = 1.0f;
	private const float ActiveEdgeTimeout = 1.35f;
	private const float JumpInputHoldTime = 0.22f;
	private const float OpposingJumpVelocityThreshold = 24.0f;
	private const float JumpPrepareMaxTime = 0.15f;
	private const float DifficultJumpMinHeight = 12.0f;
	private const float DifficultJumpMinDistance = 28.0f;
	private const float DifficultJumpTakeoffDistance = 4.0f;
	private const float DifficultJumpWindupMinTime = 0.25f;
	private const float DifficultJumpWindupMaxTime = 0.75f;
	private const float DifficultJumpWindupVelocity = 4.0f;
	private const float MirrorPlayerCrawlDelay = 0.25f;
	private const float MirrorPlayerCrawlReleaseDelay = 0.25f;
	private RoomChange? pendingRoomChange;
	private float enterTimer;
	private readonly List<PlatformNavGraph.Edge> navPath = [];
	private bool jumpHeld;
	private float mirrorPlayerCrawlTimer;
	private float mirrorPlayerCrawlReleaseTimer;
	private bool mirrorPlayerCrawl;
	private bool activeJumpPlanStarted;
	private float activeJumpPrepareTimer;
	private PlatformNavGraph.Edge? activeEdge;
	private float activeEdgeTimer;
	private float activeJumpHoldTimer;
	private bool activeJumpStarted;
	public int? DebugNavStartNodeId { get; private set; }
	public int? DebugNavGoalNodeId { get; private set; }
	public int? DebugNavTargetNodeId { get; private set; }
	public string DebugNavLabel { get; private set; } = "idle";
	public IReadOnlyList<PlatformNavGraph.Edge> DebugNavPath => navPath;
	protected override float GroundSpeedMultiplier => 0.75f;
	protected override float AirSpeedMultiplier => 0.80f;
	public bool CanSay => Game.CanShowBudSpeech();

	public Bud()
	{
		Sprite = Assets.GetSprite("companion") ?? Assets.GetSprite("player");
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
		var moveX = 0;
		var moveY = 0;
		var jumpPressed = false;
		var player = Game.GetFirst<Player>();
		var followTarget = player != null ? DesiredFollowPosition(player) : (Vector2?)null;
		if (!TryExecuteActiveEdge(out moveX, out moveY, out jumpPressed))
		{
			if (player == null || !TryNavigateToPlayer(player, out moveX, out moveY, out jumpPressed))
			{
				moveX = GetFallbackMoveX(followTarget);
				moveY = ShouldMirrorPlayerCrawl(player) ? 1 : 0;
				if (player == null)
					SetDebugNavState("no player");
				else
					SetDebugNavState(moveX == 0 ? "beside player" : $"fallback walk {moveX}");
			}
		}

		if (activeJumpHoldTimer > 0)
			jumpHeld = true;
		else if (jumpPressed)
			jumpHeld = true;
		else if (grounded || Velocity.Y >= 0)
			jumpHeld = false;

		if (moveX == 0 && player != null)
		{
			var face = Math.Sign(player.Position.X - Position.X);
			if (face != 0)
				Facing = face;
		}

		return new(
			MoveX: moveX,
			MoveY: moveY,
			JumpPressed: jumpPressed,
			JumpDown: jumpHeld,
			DashPressed: false,
			AllowWallJump: false
		);
	}

	public override void Update()
	{
		if (pendingRoomChange is { } change)
		{
			enterTimer = Calc.Approach(enterTimer, 0, Time.Delta);
			SetDebugNavState($"entering {enterTimer:0.0}s");
			if (enterTimer <= 0)
				EnterRoom(change);
			return;
		}

		base.Update();
	}

	public override void Render(Batcher batcher)
	{
		RenderSpriteSubtexture(batcher, Tint);
	}

	public void Say(string text)
		=> Game.ShowBudSpeech(Position + new Vector2(0, -32), text);

	private bool TryNavigateToPlayer(Player player, out int moveX, out int moveY, out bool jumpPressed)
	{
		moveX = 0;
		moveY = 0;
		jumpPressed = false;

		if (Game.GetPlatformNavGraph() is not { } graph)
		{
			SetDebugNavState("no graph");
			return false;
		}

		var start = graph.FindNearestNode(Position, NavSearchDistance);
		var goal = FindGoalNodeBesidePlayer(graph, player);
		if (!start.HasValue || !goal.HasValue || start.Value.Id == goal.Value.Id)
		{
			SetDebugNavState(start.HasValue && goal.HasValue ? "at goal" : "no node");
			return false;
		}

		if (!graph.TryFindPath(start.Value.Id, goal.Value.Id, navPath) || navPath.Count <= 0)
		{
			DebugNavStartNodeId = start.Value.Id;
			DebugNavGoalNodeId = goal.Value.Id;
			DebugNavTargetNodeId = null;
			DebugNavLabel = $"no path {start.Value.Id}->{goal.Value.Id}";
			return false;
		}

		if (!TryGetActionableEdge(graph, out var edge))
		{
			DebugNavStartNodeId = start.Value.Id;
			DebugNavGoalNodeId = goal.Value.Id;
			DebugNavTargetNodeId = null;
			DebugNavLabel = "edge reached";
			return false;
		}

		if (edge.Kind is PlatformNavGraph.EdgeKind.Jump or PlatformNavGraph.EdgeKind.Fall)
		{
			DebugNavStartNodeId = start.Value.Id;
			DebugNavGoalNodeId = goal.Value.Id;
			DebugNavTargetNodeId = edge.To;
			StartActiveEdge(edge);
			return TryExecuteActiveEdge(out moveX, out moveY, out jumpPressed);
		}

		var from = graph.Nodes[edge.From];
		var to = graph.Nodes[edge.To];
		var target = to.Position;
		switch (edge.Kind)
		{
			case PlatformNavGraph.EdgeKind.Walk:
			case PlatformNavGraph.EdgeKind.Crawl:
				moveX = MoveTowardNavTarget(target);
				moveY = edge.Kind == PlatformNavGraph.EdgeKind.Crawl || to.RequiresDucking || ShouldMirrorPlayerCrawl(player) ? 1 : 0;
				if (moveX == 0 && !IsNearNode(target))
				{
					DebugNavStartNodeId = start.Value.Id;
					DebugNavGoalNodeId = goal.Value.Id;
					DebugNavTargetNodeId = to.Id;
					DebugNavLabel = $"stalled {edge.Kind} {edge.From}->{edge.To}";
					return false;
				}
				break;

			case PlatformNavGraph.EdgeKind.RoomExit:
				moveX = MoveTowardNavTarget(target);
				moveY = ShouldMirrorPlayerCrawl(player) ? 1 : 0;
				break;

			default:
				DebugNavLabel = $"unsupported {edge.Kind}";
				return false;
		}

		DebugNavStartNodeId = start.Value.Id;
		DebugNavGoalNodeId = goal.Value.Id;
		DebugNavTargetNodeId = to.Id;
		DebugNavLabel = $"{edge.Kind} {edge.From}->{edge.To} len {navPath.Count}";
		return true;
	}

	private void StartActiveEdge(PlatformNavGraph.Edge edge)
	{
		activeEdge = edge;
		activeEdgeTimer = 0;
		activeJumpHoldTimer = 0;
		activeJumpStarted = false;
		activeJumpPlanStarted = false;
		activeJumpPrepareTimer = 0;
	}

	private bool TryGetActionableEdge(PlatformNavGraph graph, out PlatformNavGraph.Edge edge)
	{
		foreach (var candidate in navPath)
		{
			if (candidate.To < 0 || candidate.To >= graph.Nodes.Count)
				continue;

			var to = graph.Nodes[candidate.To];
			if (candidate.Kind is PlatformNavGraph.EdgeKind.Walk or PlatformNavGraph.EdgeKind.Crawl or PlatformNavGraph.EdgeKind.RoomExit &&
				MoveTowardNavTarget(to.Position) == 0 &&
				IsNearNode(to.Position))
				continue;

			edge = candidate;
			return true;
		}

		edge = default;
		return false;
	}

	private int MoveTowardNavTarget(Vector2 target)
	{
		var dx = target.X - Position.X;
		if (MathF.Abs(dx) > NavNodeReachDistance * 0.5f)
			return Math.Sign(dx);
		return 0;
	}

	private bool TryExecuteActiveEdge(out int moveX, out int moveY, out bool jumpPressed)
	{
		moveX = 0;
		moveY = 0;
		jumpPressed = false;
		if (activeEdge is not { } edge || Game.GetPlatformNavGraph() is not { } graph)
			return false;

		if (edge.From < 0 || edge.From >= graph.Nodes.Count || edge.To < 0 || edge.To >= graph.Nodes.Count)
		{
			ClearActiveEdge("bad edge");
			return false;
		}

		activeEdgeTimer += Time.Delta;
		var from = graph.Nodes[edge.From];
		var to = graph.Nodes[edge.To];
		var target = to.Position;

		if (activeEdgeTimer > ActiveEdgeTimeout)
		{
			ClearActiveEdge($"{edge.Kind} timeout");
			return false;
		}

		switch (edge.Kind)
		{
			case PlatformNavGraph.EdgeKind.Jump:
				if (!TryExecuteJumpEdge(edge, from, to, target, out moveX, out jumpPressed))
					return false;
				break;

			case PlatformNavGraph.EdgeKind.Crawl:
				moveX = MoveTowardNavTarget(target);
				moveY = 1;
				if (moveX == 0 && IsNearNode(target))
				{
					ClearActiveEdge($"{edge.Kind} done");
					return false;
				}
				DebugNavLabel = $"{edge.Kind} {edge.From}->{edge.To} t {activeEdgeTimer:0.0}";
				break;

			case PlatformNavGraph.EdgeKind.Fall:
				moveX = Math.Sign(to.Position.X - from.Position.X);
				if (!grounded && MathF.Abs(target.X - Position.X) > NavNodeReachDistance * 0.5f)
					moveX = Math.Sign(target.X - Position.X);
				if (grounded && activeEdgeTimer > 0.12f && IsNearNode(target))
				{
					ClearActiveEdge($"{edge.Kind} done");
					return false;
				}
				DebugNavLabel = $"{edge.Kind} {edge.From}->{edge.To} t {activeEdgeTimer:0.0}";
				break;

			default:
				ClearActiveEdge("inactive");
				return false;
		}

		DebugNavTargetNodeId = to.Id;
		return true;
	}

	private bool TryExecuteJumpEdge(
		PlatformNavGraph.Edge edge,
		PlatformNavGraph.Node from,
		PlatformNavGraph.Node to,
		Vector2 target,
		out int moveX,
		out bool jumpPressed)
	{
		moveX = 0;
		jumpPressed = false;
		var direction = Math.Sign(target.X - from.Position.X);
		if (direction == 0)
			direction = edge.Jump?.Direction ?? Math.Sign(target.X - Position.X);
		if (direction == 0)
			direction = Facing == 0 ? 1 : Facing;

			var difficult = IsDifficultJump(from.Position, target);
			if (!activeJumpStarted && !CanStartJumpApproach(from.Position, difficult))
			{
				moveX = Math.Sign(from.Position.X - Position.X);
				DebugNavLabel = $"{edge.Kind} approach {edge.From}->{edge.To}";
				return true;
		}

		if (!activeJumpStarted && !activeJumpPlanStarted)
		{
				if (grounded && difficult)
			{
				var needsWindup = MathF.Abs(Velocity.X) > DifficultJumpWindupVelocity ||
					activeJumpPrepareTimer < DifficultJumpWindupMinTime;
				if (needsWindup && activeJumpPrepareTimer < DifficultJumpWindupMaxTime)
				{
					moveX = 0;
					activeJumpPrepareTimer += Time.Delta;
					DebugNavLabel = $"{edge.Kind} windup {edge.From}->{edge.To} vx {Velocity.X:0}";
					return true;
				}
			}

			moveX = direction;
			var opposingVelocity = grounded &&
				direction != 0 &&
				MathF.Sign(Velocity.X) == -direction &&
				MathF.Abs(Velocity.X) > OpposingJumpVelocityThreshold;
			if (opposingVelocity && activeJumpPrepareTimer < JumpPrepareMaxTime)
			{
				activeJumpPrepareTimer += Time.Delta;
				DebugNavLabel = $"{edge.Kind} settle {edge.From}->{edge.To} vx {Velocity.X:0}";
				return true;
			}

			activeJumpPlanStarted = true;
			activeEdgeTimer = 0;
		}

		moveX = direction;

		if (!activeJumpStarted && grounded)
		{
			jumpPressed = true;
			activeJumpStarted = true;
			activeJumpHoldTimer = JumpHoldForEdge(from.Position, target);
		}

		if (activeJumpStarted)
			activeJumpHoldTimer = Calc.Approach(activeJumpHoldTimer, 0, Time.Delta);

		if (activeJumpStarted && grounded && activeEdgeTimer > 0.12f && IsNearNode(target))
		{
			ClearActiveEdge($"{edge.Kind} done");
			return false;
		}

		DebugNavLabel = $"{edge.Kind} {edge.From}->{edge.To} t {activeEdgeTimer:0.0}";
		return true;
	}

	private bool CanStartJumpApproach(Vector2 source, bool difficult)
	{
		if (!difficult)
			return Vector2.Distance(Position, source) <= NavNodeReachDistance * 2;

		return MathF.Abs(Position.X - source.X) <= DifficultJumpTakeoffDistance &&
			MathF.Abs(Position.Y - source.Y) <= NavNodeReachDistance;
	}

	private static bool IsDifficultJump(Vector2 from, Vector2 target)
	{
		var height = MathF.Max(0, from.Y - target.Y);
		var distance = MathF.Abs(target.X - from.X);
		return height >= DifficultJumpMinHeight || distance >= DifficultJumpMinDistance;
	}

	private static float JumpHoldForEdge(Vector2 from, Vector2 target)
	{
		var height = MathF.Max(0, from.Y - target.Y);
		var distance = MathF.Abs(target.X - from.X);
		var hold = 0.055f;
		hold += Math.Clamp(height / 32.0f, 0, 1) * 0.105f;
		hold += Math.Clamp((distance - 16) / 40.0f, 0, 1) * 0.035f;
		return Math.Clamp(hold, 0.055f, JumpInputHoldTime);
	}

	private bool IsNearNode(Vector2 node)
		=> MathF.Abs(Position.X - node.X) <= NavNodeReachDistance &&
		   MathF.Abs(Position.Y - node.Y) <= NavNodeReachDistance;

	private bool ShouldMirrorPlayerCrawl(Player? player)
	{
		if (player == null || !grounded || !player.IsGrounded || !player.IsDucking)
		{
			return UpdateMirrorPlayerCrawl(false);
		}

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

	private void ClearActiveEdge(string label)
	{
		activeEdge = null;
		activeEdgeTimer = 0;
		activeJumpHoldTimer = 0;
			activeJumpStarted = false;
			activeJumpPlanStarted = false;
			activeJumpPrepareTimer = 0;
			DebugNavLabel = label;
		}

	private void SetDebugNavState(string label)
	{
		DebugNavStartNodeId = null;
		DebugNavGoalNodeId = null;
		DebugNavTargetNodeId = null;
		navPath.Clear();
		activeEdge = null;
		activeEdgeTimer = 0;
		activeJumpHoldTimer = 0;
		activeJumpStarted = false;
		activeJumpPlanStarted = false;
		activeJumpPrepareTimer = 0;
		DebugNavLabel = label;
	}

	private Vector2 DesiredFollowPosition(Player player)
	{
		var side = Position.X <= player.Position.X ? -1 : 1;
		return player.Position + new Vector2(side * DesiredPlayerSpacing, 0);
	}

	private PlatformNavGraph.Node? FindGoalNodeBesidePlayer(PlatformNavGraph graph, Player player)
	{
		var preferredSide = Position.X <= player.Position.X ? -1 : 1;
		var preferred = FindGoalNodeBesidePlayer(graph, player, preferredSide);
		if (preferred.HasValue)
			return preferred;

		return FindGoalNodeBesidePlayer(graph, player, -preferredSide) ??
			   graph.FindNearestNode(player.Position, GoalNodeSearchDistance);
	}

	private static PlatformNavGraph.Node? FindGoalNodeBesidePlayer(PlatformNavGraph graph, Player player, int side)
	{
		var desired = player.Position + new Vector2(side * DesiredPlayerSpacing, 0);
		PlatformNavGraph.Node? best = null;
		var bestScore = GoalNodeSearchDistance * GoalNodeSearchDistance;
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

	private int GetFallbackMoveX(Vector2? target)
	{
		if (target == null)
			return 0;

		var dx = target.Value.X - Position.X;
		return MathF.Abs(dx) > FollowStopDistance ? Math.Sign(dx) : 0;
	}

	private void OnRoomChanged(RoomChange change)
	{
		if (change.Kind == RoomChangeKind.InitialLoad)
			return;

		pendingRoomChange = change;
		enterTimer = EnterDelay;
		Visible = false;
		CollidesWithSolids = false;
		Velocity = Vector2.Zero;
	}

	private void EnterRoom(RoomChange change)
	{
		pendingRoomChange = null;
		Position = FindEnterPosition(change);
		Velocity = Vector2.Zero;
		CollidesWithSolids = true;
		Visible = true;
		Game.TryBudStorylet("bud_entered");
	}

	private Point2 FindEnterPosition(RoomChange change)
	{
		var player = Game.GetFirst<Player>();
		var anchor = player?.Position ?? (Point2)change.PlayerPosition;
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
