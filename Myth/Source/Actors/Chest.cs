using System;
using System.Numerics;
using Foster.Framework;
namespace Myth;

[GameActor]
public class Chest : Actor
{
	const int MycelonDrop = 3;              // Mycelons scattered when the chest is broken
	const float MycelonSpreadArc = 1.4f;   // total width of the upward fan, radians (~80°)

	public Chest()
	{
		Mask = Masks.Solid | Masks.Destructible;
		Hitbox = Hitbox.FromSprite(Sprite);
	}

	public override void OnWasHit(Actor by)
	{
		Game.Create<Pop>(Position);

		// Fan the Mycelons across an upward arc, evenly spaced and centered on straight up.
		var up = -MathF.PI / 2f;
		for (var i = 0; i < MycelonDrop; i++)
		{
			var t = MycelonDrop > 1 ? i / (float)(MycelonDrop - 1) : 0.5f;
			var angle = up - MycelonSpreadArc / 2f + MycelonSpreadArc * t;
			var speed = 60f + Random.Shared.NextSingle() * 40f;
			var velocity = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * speed;
			Game.Create<Pickup>(Position, p =>
			{
				p.Kind = PickupKind.Mycelon;
				p.Velocity = velocity;
			});
		}

		Game.HitStun(0.22f);
		Game.DestroyForThisSession(this);
	}
}
