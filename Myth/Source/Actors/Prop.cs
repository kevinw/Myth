using System.Runtime.InteropServices;
using Foster.Framework;

namespace Myth;

[GameActor(Sprite = "Editor/prop", Anchor = PropSpriteAnchor, Tags = ActorTag.Decoration, LivePositionEditable = true)]
public class Prop : Actor
{
	const SpriteAnchor PropSpriteAnchor = SpriteAnchor.TopLeft;
	public Color PropColor = Color.White;

	[Flags]
	public enum PropFlags
	{
		None = 0,
		Destroyable = 1 << 0,
		DestroyPersists = 1 << 1,
	}

	[Flags]
	internal enum RuntimePropFlags
	{
		RenderEmissive = 1 << 0,
	}
	internal RuntimePropFlags runtimePropFlags = RuntimePropFlags.RenderEmissive;

	public PropFlags propFlags = PropFlags.None;

	[ActorRef] public string ChangeOnActorDestroyed = "";
	public enum PropChangeType
	{
		EmissiveOff,
	}
	public PropChangeType propChangeType = PropChangeType.EmissiveOff;


	static Rng rng = new();

	public string SpriteName = "fire";
	public string Category = "";
	public Factory.TileLayer Layer;
	public bool Decal;
	public bool EmitsLight = true;
	public int Health = 1;
	[I(0, 1)] public float AnimationSpeedRandomness;
	float animationRate = 1;

	public override bool CastsSpriteShadow => !Decal;
	public override bool CastsReflection => !Decal;
	public override float HitKnockback => 10f;
	protected override float AnimationRate => animationRate;

	public Prop()
	{
		Anchor = SpriteAnchor.TopLeft;
		CollidesWithSolids = false;
	}

	public override void Render(Batcher batcher) {
		RenderSpriteSubtexture(batcher, SubtextureType.Color, PropColor);
	}

	public override void RenderEmissive(Batcher batcher)
	{
		if (runtimePropFlags.Has(RuntimePropFlags.RenderEmissive))
			RenderSpriteSubtexture(batcher, SubtextureType.Emissive, PropColor);
	}
	public override void RenderLightEmission(Batcher batcher)
	{
		if (EmitsLight)
			RenderEmissive(batcher);
	}

	ActorDestructionTrigger? destroyTrigger;

	public override void Update()
	{
		base.Update();
		if (destroyTrigger?.Update() == true)
			UpdateRuntimeFlags();
	}

	void UpdateRuntimeFlags()
	{
		if (destroyTrigger?.Triggered ?? false)
			if (propChangeType == PropChangeType.EmissiveOff)
				runtimePropFlags &= ~RuntimePropFlags.RenderEmissive;
	}

	public override void Added()
	{
		base.Added();

		if (!string.IsNullOrWhiteSpace(ChangeOnActorDestroyed))
			destroyTrigger = new(this, ChangeOnActorDestroyed);
		UpdateRuntimeFlags();

		Sprite = Assets.GetSprite(SpriteName);
		if (Facing == Signs.Negative && Sprite?.Frames.Count > 0)
			Shift.X = -Sprite.Frames[0].Subtexture.Source.Width;
		Depth = Layer == Factory.TileLayer.Background ? 8 : 4;
		Play();
		SetAnimationTime(rng.Float(Animation.Duration));
		animationRate = MathF.Max(0.01f, 1 + rng.Float(-AnimationSpeedRandomness, AnimationSpeedRandomness));
		if (propFlags.Has(PropFlags.Destroyable | PropFlags.DestroyPersists))
		{
			Mask |= Masks.Destructible;
			Hitbox = Hitbox.FromSprite(Sprite, PropSpriteAnchor);
		}
	}

	internal override void OnWasHit(HitInfo hitInfo) {
		Game.Create<Pop>(Position + Hitbox.Rect.Center);
		if (--Health > 0)
		{
			Game.HitStun(0.1f);
			return;
		}
		else
		{
			if (propFlags.Has(PropFlags.DestroyPersists))
			{
				AudioEvent("prop_break_temp");
				if (Game.DestroyPersistently(this) && string.Equals(Category, "duck", StringComparison.OrdinalIgnoreCase))
				{
					Game.DuckDestroyed(GetVisualBounds().TopCenter);
				}
				Game.Shake(0.6f, intensity: 2.0f);
				Game.HitStun(0.4f);
			}
			else
			{
				Game.Destroy(this);
				Game.HitStun(0.1f);
			}
		}
	}
}
