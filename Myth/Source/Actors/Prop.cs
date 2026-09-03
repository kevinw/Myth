using System.Runtime.InteropServices;
using Foster.Framework;

namespace Myth;

[GameActor(Sprite = "Editor/prop", Anchor = PropSpriteAnchor, Tags = ActorTag.Decoration, LivePositionEditable = true)]
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
	[I(0, 1)] public float AnimationSpeedRandomness;
	float animationRate = 1;

	public override bool CastsSpriteShadow => !Decal;
	public override bool CastsReflection => !Decal;
	protected override float AnimationRate => animationRate;

	public Prop()
	{
		Anchor = SpriteAnchor.TopLeft;
		CollidesWithSolids = false;
	}

	public override void Added()
	{
		base.Added();
		Sprite = Assets.GetSprite(SpriteName);
		if (Facing == Signs.Negative && Sprite?.Frames.Count > 0)
			Shift.X = -Sprite.Frames[0].Subtexture.Source.Width;
		Depth = Layer == Factory.TileLayer.Background ? 8 : 4;
		Play();
		SetAnimationTime(rng.Float(Animation.Duration));
		animationRate = MathF.Max(0.01f, 1 + rng.Float(-AnimationSpeedRandomness, AnimationSpeedRandomness));
		if (propFlags.HasFlag(PropFlags.Destroyable) || propFlags.HasFlag(PropFlags.DestroyPersists))
		{
			Mask |= Masks.Destructible;
			Hitbox = Hitbox.FromSprite(Sprite, PropSpriteAnchor);
		}
	}

	internal override void OnWasHit(HitInfo hitInfo) {
		Game.Create<Pop>(Position + Hitbox.Rect.Center);
		if (propFlags.HasFlag(PropFlags.DestroyPersists))
		{
			AudioEvent("prop_break_temp");
			Game.DestroyPersistently(this);
			Game.HitStun(0.4f);
		}
		else
		{
			Game.Destroy(this);
			Game.HitStun(0.1f);
		}
	}
}
