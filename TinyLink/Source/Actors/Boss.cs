using Foster.Framework;
using System.Numerics;

namespace TinyLink;

public class Boss : Pawn
{
	private static readonly Color Tint = new(0xff7f7f);
	private const float RetargetInterval = 3.5f;
	private const float ThrowInterval = 4.0f;
	private const float ThrowPauseTime = 0.55f;

	private readonly Navigator nav;
	private Vector2? navTarget;
	private float retargetTimer;
	private float throwTimer = ThrowInterval;
	private float throwPauseTimer;

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
}
