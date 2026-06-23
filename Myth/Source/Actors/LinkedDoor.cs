using System.Numerics;
using Foster.Framework;

namespace Myth;

[GameActor(Sprite = "back_door", Tags = ActorTag.Door)]
public class LinkedDoor : Actor
{
	public static readonly Point2 DefaultSpawnOffset = Point2.Zero;

	public string TargetActorId = "";
	public Point2 SpawnOffset = DefaultSpawnOffset;

	private bool showInteract;
	private float interactTime;

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

		showInteract = false;

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

		if (Game.Controls.Move.IntValue.Y >= 0)
			return;

		Game.TransitionToLinkedDoor(TargetActorId);
	}

	public override void RenderEmissive(Batcher batcher)
	{
		if (!showInteract)
			return;

		var interactSpriteMaybe = Assets.GetSprite("interact");
		if (interactSpriteMaybe is not { } interactSprite || interactSprite.Animations.Count == 0)
		{
			Console.WriteLine($"interact sprite not found or has no animations: {interactSpriteMaybe}");
			return;
		}

		var anim = interactSprite.Animations[0];
		var frame = interactSprite.GetFrameAt(anim, interactTime, true);
		batcher.Image(frame.Subtexture, new Vector2(-8, -31), interactSprite.Origin, Vector2.One, 0, Color.White);
	}
}
