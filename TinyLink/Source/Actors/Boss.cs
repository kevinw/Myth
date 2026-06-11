using Foster.Framework;
using System.Numerics;

namespace TinyLink;

public class Boss : NavPawn
{
	private static readonly Color Tint = new(0xff7f7f);
	private const float RetargetInterval = 3.0f;

	private Vector2? navTarget;
	private float retargetTimer;

	public Boss()
	{
		Sprite = Assets.GetSprite("bud");
		Hitbox = new(new RectInt(-4, -12, 8, 12));
		Depth = -4;
		Play("idle");
	}

	public override PawnInput GetInput()
	{
		var player = Game.GetFirst<Player>();

		retargetTimer -= Time.Delta;
		if (navTarget == null || retargetTimer <= 0 || IsNearNode(navTarget.Value))
		{
			navTarget = FindFarthestNodeFromPlayer(player);
			retargetTimer = RetargetInterval;
		}

		var moveX = 0;
		var moveY = 0;
		var jumpPressed = false;

		if (navTarget != null && !TryExecuteActiveEdge(out moveX, out moveY, out jumpPressed))
		{
			if (!TryNavigateToPosition(navTarget.Value, out moveX, out moveY, out jumpPressed))
				moveX = Math.Sign(navTarget.Value.X - Position.X);
		}

		UpdateJumpHeld(jumpPressed);

		return new(
			MoveX: moveX,
			MoveY: moveY,
			JumpPressed: jumpPressed,
			JumpDown: jumpHeld,
			DashPressed: false,
			AllowWallJump: false
		);
	}

	public override void Render(Batcher batcher)
	{
		RenderSpriteSubtexture(batcher, Tint);
	}

	private Vector2? FindFarthestNodeFromPlayer(Player? player)
	{
		if (Game.GetPlatformNavGraph() is not { } graph)
			return null;

		PlatformNavGraph.Node? best = null;
		var bestDistSq = 0f;
		foreach (var node in graph.Nodes)
		{
			if (node.Kind == PlatformNavGraph.NodeKind.RoomExit)
				continue;

			var distSq = player != null
				? Vector2.DistanceSquared(node.Position, player.Position)
				: Vector2.DistanceSquared(node.Position, Position);

			if (distSq > bestDistSq)
			{
				bestDistSq = distSq;
				best = node;
			}
		}

		return best?.Position;
	}
}
