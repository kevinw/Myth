using Foster.Framework;
using System.Numerics;

namespace TinyLink;

public class Boss : Pawn
{
	private static readonly Color Tint = new(0xff7f7f);
	private const float RetargetInterval = 3.5f;
	private const float ThrowInterval = 4.0f;
	private const float ThrowPauseTime = 0.55f;

	// Wall-jump escape: when the player crowds the boss and there's a wall on his
	// far side, he leaps at that wall, clings, then wall-jumps back over the
	// player and bolts away. This is a dynamic, sensor-driven move (the trigger
	// geometry depends on the live player position), so it lives here rather than
	// in the static nav graph.
	private const float WallJumpEscapeMinDistance = 6f;
	private const float WallJumpEscapeMaxDistance = 40f;
	private const float WallJumpEscapeVerticalRange = 14f;
	private const int WallJumpWallProbeDistance = 26;
	private const float WallJumpLeapTimeout = 0.7f;
	private const float WallJumpClingMinTime = 0.10f;
	private const float WallJumpClingTimeout = 0.45f;
	private const float WallJumpFleeTime = 0.8f;
	private const float WallJumpJumpHoldTime = 0.18f;
	private const float WallJumpStartChance = 0.5f;
	private const float WallJumpRetryDelay = 0.6f;
	private const float WallJumpAbortCooldown = 2.0f;
	private const float WallJumpCooldownMin = 4.0f;
	private const float WallJumpCooldownMax = 7.0f;

	private enum EscapePhase { None, Leap, Cling, Flee }

	private readonly Navigator nav;
	private Vector2? navTarget;
	private float retargetTimer;
	private float throwTimer = ThrowInterval;
	private float throwPauseTimer;

	private EscapePhase escapePhase = EscapePhase.None;
	private float escapeTimer;
	private float escapeCooldownTimer;
	private int escapeFleeDir;    // direction the boss flees: toward and over the player
	private int escapeWallSide;   // direction from the boss to the wall he leaps at
	private bool escapeLeftGround;
	private bool escapeJumpIssued;
	private float escapeJumpHeldTimer;

	public Boss()
	{
		nav = new(this);
		Sprite = Assets.GetSprite("bud");
		Hitbox = new(new RectInt(-4, -12, 8, 12));
		Depth = -4;
		Play("idle");
	}

	public override PawnInput GetInput()
	{
		var player = Game.GetFirst<Player>();

		if (escapeCooldownTimer > 0)
			escapeCooldownTimer -= Time.Delta;

		// An in-progress wall-jump escape overrides throwing and navigation.
		if (escapePhase != EscapePhase.None)
			return UpdateWallJumpEscape();

		// Tick throw cooldown; start a throw when it expires
		throwTimer -= Time.Delta;
		if (throwTimer <= 0 && throwPauseTimer <= 0)
		{
			throwPauseTimer = ThrowPauseTime;
			ThrowAtPlayer(player);
			throwTimer = ThrowInterval;
		}

		// Stand still while the throw wind-up plays out
		if (throwPauseTimer > 0)
		{
			throwPauseTimer -= Time.Delta;
			nav.UpdateJumpHeld(false);
			return new(MoveX: 0, MoveY: 0, JumpPressed: false, JumpDown: false, DashPressed: false);
		}

		// Sometimes bail out over the player when cornered against a wall
		if (escapeCooldownTimer <= 0 && ShouldStartWallJumpEscape(player, out var fleeDir, out var wallSide))
		{
			if (Random.Shared.NextSingle() < WallJumpStartChance)
			{
				StartWallJumpEscape(fleeDir, wallSide);
				return UpdateWallJumpEscape();
			}

			// Eligible but didn't roll it this time -- wait a beat before re-rolling.
			escapeCooldownTimer = WallJumpRetryDelay;
		}

		// Pick or refresh the random nav target
		retargetTimer -= Time.Delta;
		if (navTarget == null || retargetTimer <= 0 || nav.IsNearNode(navTarget.Value))
		{
			navTarget = FindRandomNode();
			retargetTimer = RetargetInterval;
		}

		var moveX = 0;
		var moveY = 0;
		var jumpPressed = false;

		if (navTarget != null && !nav.TryExecuteActiveEdge(out moveX, out moveY, out jumpPressed))
		{
			if (!nav.TryNavigateToPosition(navTarget.Value, out moveX, out moveY, out jumpPressed))
				moveX = Math.Sign(navTarget.Value.X - Position.X);
		}

		nav.UpdateJumpHeld(jumpPressed);

		return new(
			MoveX: moveX,
			MoveY: moveY,
			JumpPressed: jumpPressed,
			JumpDown: nav.JumpHeld,
			DashPressed: false,
			AllowWallJump: false
		);
	}

	public override void Render(Batcher batcher)
	{
		RenderSpriteSubtexture(batcher, Tint);
	}

	private void ThrowAtPlayer(Player? player)
	{
		var dir = player != null ? Math.Sign(player.Position.X - Position.X) : (int)Facing;
		if (dir == 0) dir = (int)Facing;
		Facing = dir;

		Game.Create<Bullet>(Position + new Point2(dir * 6, -8), bullet =>
		{
			bullet.Velocity = new Vector2(dir * 65, -20);
		});
	}

	private Vector2? FindRandomNode()
	{
		if (Game.GetPlatformNavGraph() is not { } graph)
			return null;

		// Count valid candidates (skip room exits)
		var count = 0;
		foreach (var node in graph.Nodes)
			if (node.Kind != PlatformNavGraph.NodeKind.RoomExit)
				count++;

		if (count == 0)
			return null;

		var pick = Random.Shared.Next(count);
		var i = 0;
		foreach (var node in graph.Nodes)
		{
			if (node.Kind == PlatformNavGraph.NodeKind.RoomExit)
				continue;
			if (i++ == pick)
				return node.Position;
		}

		return null;
	}

	// --- Wall-jump escape ---------------------------------------------------

	private bool ShouldStartWallJumpEscape(Player? player, out int fleeDir, out int wallSide)
	{
		fleeDir = 0;
		wallSide = 0;

		if (player == null || !IsGrounded)
			return false;

		var dx = player.Position.X - Position.X;
		var adx = MathF.Abs(dx);
		if (adx < WallJumpEscapeMinDistance || adx > WallJumpEscapeMaxDistance)
			return false;
		if (MathF.Abs(player.Position.Y - Position.Y) > WallJumpEscapeVerticalRange)
			return false;

		var playerDir = Math.Sign(dx);
		var wallDir = -playerDir;
		if (!WallOnSide(wallDir, WallJumpWallProbeDistance))
			return false;

		// Flee back over the player; the wall jump on the far wall launches us that way.
		fleeDir = playerDir;
		wallSide = wallDir;
		return true;
	}

	private void StartWallJumpEscape(int fleeDir, int wallSide)
	{
		escapePhase = EscapePhase.Leap;
		escapeTimer = 0;
		escapeFleeDir = fleeDir;
		escapeWallSide = wallSide;
		escapeLeftGround = false;
		escapeJumpIssued = false;
		escapeJumpHeldTimer = 0;
	}

	private void EndWallJumpEscape(bool aborted)
	{
		escapePhase = EscapePhase.None;
		escapeCooldownTimer = aborted
			? WallJumpAbortCooldown
			: WallJumpCooldownMin + Random.Shared.NextSingle() * (WallJumpCooldownMax - WallJumpCooldownMin);
	}

	private PawnInput UpdateWallJumpEscape()
	{
		escapeTimer += Time.Delta;
		if (escapeJumpHeldTimer > 0)
			escapeJumpHeldTimer -= Time.Delta;
		if (!IsGrounded)
			escapeLeftGround = true;

		switch (escapePhase)
		{
			case EscapePhase.Leap:
			{
				// Reached the wall mid-air -> start clinging.
				if (escapeLeftGround && !IsGrounded && GetWallJumpDirection() != 0)
				{
					escapePhase = EscapePhase.Cling;
					escapeTimer = 0;
					return WallJumpInput(MoveX: escapeWallSide, JumpPressed: false);
				}

				// Never made it (landed again or ran out of time).
				if (escapeTimer > WallJumpLeapTimeout || (escapeLeftGround && IsGrounded))
				{
					EndWallJumpEscape(aborted: true);
					return WallJumpInput(MoveX: 0, JumpPressed: false);
				}

				var leapJump = false;
				if (!escapeJumpIssued && IsGrounded)
				{
					leapJump = true;
					escapeJumpIssued = true;
					escapeJumpHeldTimer = WallJumpJumpHoldTime;
				}
				return WallJumpInput(MoveX: escapeWallSide, JumpPressed: leapJump);
			}

			case EscapePhase.Cling:
			{
				// Slid off or hit the floor before launching.
				if (escapeTimer > WallJumpClingTimeout || IsGrounded || GetWallJumpDirection() == 0)
				{
					EndWallJumpEscape(aborted: true);
					return WallJumpInput(MoveX: 0, JumpPressed: false);
				}

				// Hug the wall for a beat, then kick off it back over the player.
				if (escapeTimer >= WallJumpClingMinTime)
				{
					escapePhase = EscapePhase.Flee;
					escapeTimer = 0;
					escapeJumpHeldTimer = WallJumpJumpHoldTime;
					return WallJumpInput(MoveX: escapeWallSide, JumpPressed: true);
				}

				return WallJumpInput(MoveX: escapeWallSide, JumpPressed: false);
			}

			case EscapePhase.Flee:
			default:
			{
				if (escapeTimer >= WallJumpFleeTime)
				{
					EndWallJumpEscape(aborted: false);
					return WallJumpInput(MoveX: escapeFleeDir, JumpPressed: false);
				}
				return WallJumpInput(MoveX: escapeFleeDir, JumpPressed: false);
			}
		}
	}

	private PawnInput WallJumpInput(int MoveX, bool JumpPressed) => new(
		MoveX: MoveX,
		MoveY: 0,
		JumpPressed: JumpPressed,
		JumpDown: JumpPressed || escapeJumpHeldTimer > 0,
		DashPressed: false,
		AllowWallJump: true
	);

	private bool WallOnSide(int dir, int distance)
	{
		if (dir == 0)
			return false;

		var rect = Hitbox.Rect + Position;
		var probe = dir > 0
			? new RectInt(rect.Right, rect.Top + 1, distance, rect.Height - 2)
			: new RectInt(rect.Left - distance, rect.Top + 1, distance, rect.Height - 2);
		return Game.OverlapsRoomCollision(probe);
	}
}
