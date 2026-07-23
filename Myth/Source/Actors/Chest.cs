using System;
using System.Numerics;
using Foster.Framework;
namespace Myth;

[GameActor]
public class Chest : Actor
{
	const int MycelonDrop = 3;              // Mycelons scattered when the chest is broken

	public Chest()
	{
		Mask = Masks.Solid | Masks.Destructible;
		Hitbox = Hitbox.FromSprite(Sprite);
	}

	public override void OnWasHit(Actor by)
	{
		Game.Create<Pop>(Position);
		Pickup.Scatter(Game, Position, PickupKind.Mycelon, MycelonDrop);
		Game.HitStun(0.22f);
		Game.DestroyPersistently(this);
	}
}
