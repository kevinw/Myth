using System.Numerics;
using Foster.Framework;

namespace Myth;

// A collectible that hovers in place with a gentle bob and is collected on contact. Used for
// hidden authored pickups placed "floating in the air", and for the persistent loot a Chest
// re-places on reload. Persistence rides Actor.SessionDestroyKey (authored, or set by the Chest):
// collecting records that key so it never grants twice; uncollected ones reappear. Grant() runs the
// burrow-upgrade modal when Kind is BurrowUpgrade.
[GameActor(Sprite = "mycelon", LivePositionEditable = true)]
public class FloatingPickup : Actor
{
	public PickupKind Kind = PickupKind.Mycelon;

	static float BobSpeed => 3f;
	static int BobAmplitude => 2;      // 2px vertical sine
	static float CollectRadius => 6f;

	int bobBaseY;

	public FloatingPickup()
	{
		Depth = -20;
		Mask = Masks.None;
		Hitbox = new(new RectInt(-3, -3, 6, 6));
		CollidesWithSolids = false;
	}

	public override void Added()
	{
		base.Added();
		Sprite = Assets.GetSprite(Kind == PickupKind.BurrowUpgrade ? "burrow_upgrade" : "mycelon")
			?? Assets.GetSprite("mycelon") ?? Assets.GetSprite("airbubble");
		Play("idle");
		bobBaseY = Position.Y;
	}

	public override bool CastsSpriteShadow => false;

	public override void Update()
	{
		base.Update();
		Position = new Point2(Position.X, bobBaseY + (int)MathF.Round(MathF.Sin(Timer * BobSpeed) * BobAmplitude));

		if (Game.IsEditorPreview || Game.ActiveCutscene != null)
			return;
		if (Game.GetFirst<Player>() is { } player &&
			((Vector2)player.Position - Position).Length() <= CollectRadius)
		{
			AudioEvent("pickup");
			Pickup.Grant(Game, Kind, this);
		}
	}
}
