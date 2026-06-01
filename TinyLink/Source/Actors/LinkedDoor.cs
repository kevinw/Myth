using Foster.Framework;

namespace TinyLink;

public class LinkedDoor : Actor
{
	public static readonly Point2 DefaultSpawnOffset = Point2.Zero;

	public string TargetActorId = "";
	public Point2 SpawnOffset = DefaultSpawnOffset;

	public LinkedDoor()
	{
		Sprite = Assets.GetSprite("back_door");
		Hitbox = new(new RectInt(-8, -16, 16, 16));
		Depth = 1;
		Play("idle");
	}

	public override void Update()
	{
		base.Update();

		if (string.IsNullOrWhiteSpace(TargetActorId))
			return;
		if (Game.GetFirst<Player>() is not { } player)
			return;
		if (player.State != Player.States.Normal || !player.IsGrounded)
			return;
		if (!OverlapsAny(Point2.Zero, player))
			return;
		if (Game.Controls.Move.IntValue.Y >= 0)
			return;

		Game.TransitionToLinkedDoor(TargetActorId);
	}
}
