using Foster.Framework;

namespace Myth;

[GameActor(Sprite = "editor_prop", Anchor = SpriteAnchor.TopLeft, Tags = ActorTag.Decoration)]
public class Prop : Actor
{
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
	}
}
