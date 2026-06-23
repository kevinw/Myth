using System.Numerics;
using Foster.Framework;

namespace Myth;

[GameActor(Sprite = "arrow_trap", Tags = ActorTag.Hazard)]
public class ArrowTrap : Actor
{
	private bool didFire;

	public ArrowTrap()
	{
		Hitbox = new(new RectInt(-6, -12, 12, 12));
		Mask = Masks.None;
		Sprite = Assets.GetSprite("arrow_trap");
		Depth = -5;
	}


	public override void Update()
	{
		base.Update();

		if (!didFire && Game.GetFirst(Masks.Player) is { } player)
		{
			var diff = player.Position.X - Position.X;
			var dist = MathF.Abs(diff);
			var sign = MathF.Sign(diff);

			if (dist < 100 && sign == Facing)
			{
				didFire = true;
				Game.Create<Arrow>(Position + new Point2(-8 * Facing, -8), (actor) =>
				{
					actor.Velocity = new Vector2(220 * Facing, 0);
					actor.Facing = this.Facing.Negate;
				});
			}
		}
	}
}
