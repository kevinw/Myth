using System.Runtime.InteropServices;
using Foster.Framework;

namespace Myth;

[GameActor(Sprite = "editor_prop", Anchor = PropSpriteAnchor, Tags = ActorTag.Decoration)]
public class Prop : Actor
{
	const SpriteAnchor PropSpriteAnchor = SpriteAnchor.TopLeft;

	[Flags]
	public enum PropFlags
	{
		None = 0,
		Destroyable = 1 << 0,
		DestroyPersists = 1 << 1,
	}

	public PropFlags propFlags = PropFlags.None;

	static Rng rng = new();

	public string SpriteName = "fire";
	public Factory.TileLayer Layer;
	public bool Decal;

	public override bool CastsSpriteShadow => !Decal;
	public override bool CastsReflection => !Decal;

	public Prop()
	{
		Anchor = SpriteAnchor.TopLeft;
		CollidesWithSolids = false;
	}

	public override void Added()
	{
		base.Added();
		Sprite = Assets.GetSprite(SpriteName);
		Depth = Layer == Factory.TileLayer.Background ? 8 : 4;
		Play();
		SetAnimationTime(rng.Float(Animation.Duration));
		if (propFlags.HasFlag(PropFlags.Destroyable) || propFlags.HasFlag(PropFlags.DestroyPersists))
		{
			Mask |= Masks.Destructible;
			Hitbox = Hitbox.FromSprite(Sprite, PropSpriteAnchor);
		}
	}

	public override void OnWasHit(Actor by) {
		Game.Create<Pop>(Position);
		if (propFlags.HasFlag(PropFlags.DestroyPersists))
			Game.DestroyPersistently(this);
		else
			Game.Destroy(this);
	}
}
