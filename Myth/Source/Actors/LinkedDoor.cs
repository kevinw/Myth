using System.Numerics;
using Foster.Framework;

namespace Myth;

[GameActor(Sprite = "back_door", Tags = ActorTag.Door)]
public class LinkedDoor : Actor
{
	public static readonly Point2 DefaultSpawnOffset = Point2.Zero;

	public string TargetActorId = "";
	public Point2 SpawnOffset = DefaultSpawnOffset;

	bool showInteract;
	float interactTime;

	[Flags]
	public enum LinkedDoorFlags {
		None = 0,
		InvisibleExit = 1 << 0,
	}

	public LinkedDoorFlags Flags = 0;

	public LinkedDoor()
	{
		Sprite = Assets.GetSprite("back_door");
		Hitbox = new(new RectInt(-8, -16, 16, 16));
		Depth = 1;
		Play("idle");
	}

	public override void Added() {
		Visible = !Flags.Has(LinkedDoorFlags.InvisibleExit);
	}

	public override void Update()
	{
		base.Update();

		showInteract = false;

		if (Flags.Has(LinkedDoorFlags.InvisibleExit))
			return;


		if (string.IsNullOrWhiteSpace(TargetActorId))
			return;
		if (Game.GetFirst<Player>() is not { } player)
			return;
		if (player.State != Player.States.Normal || !player.IsGrounded)
			return;
		if (!OverlapsAny(Point2.Zero, player))
			return;

		showInteract = true;
		interactTime += Time.Delta;

		var moveInt = Game.Controls.Move.IntValue;
		if (moveInt.Y < 0 && moveInt.X == 0)
			Game.TransitionToLinkedDoor(TargetActorId);
	}

	public override void RenderEmissive(Batcher batcher)
	{
		if (!showInteract || Flags.Has(LinkedDoorFlags.InvisibleExit))
			return;

		var interactSpriteMaybe = Assets.GetSprite("interact");
		if (interactSpriteMaybe is not { } interactSprite || interactSprite.Animations.Count == 0)
		{
			Console.WriteLine($"interact sprite not found or has no animations: {interactSpriteMaybe}");
			return;
		}

		if (interactSprite.FrameForAnimationIndex(0, interactTime, true) is not { } frame)
			return;

		batcher.Image(frame.Subtexture, new Vector2(-8, -31), interactSprite.Origin, Vector2.One, 0, Color.White);
	}
}
