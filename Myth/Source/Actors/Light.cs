using Foster.Framework;

namespace Myth;

public enum LightCookie
{
	None,
	Mushroom,
	Tendrils,
	Flame,
	Cone,
	Broken
}

[GameActor(Sprite = "fire", OffsetX = 4, OffsetY = 4, Tags = ActorTag.Light)]
public class Light : Actor
{
	internal static float DefaultShadowSourceRadius => 2.3f;

	public float Radius = 48;
	public string SpriteName = "fire";
	public Color LightColor = 0xffd37a;
	public float Intensity = 1.0f;
	public float Height = 16;
	public float FlickerFrequency = 4;
	public float FlickerIntensity = 0.08f;
	public LightCookie Cookie;
	public float CookieAngleDegrees;
	public float CookieSpeedDegrees;
	public float CookieScale = 1.0f;
	public float CookieStrength = 1.0f;
	public float CookieFeather = 0.12f;
	public float ShadowSourceRadius = DefaultShadowSourceRadius;
	public float ShadowSoftness = 1.0f;
	public float ShadowNoise;
	public float ShadowNoiseSpeed = 8.0f;
	readonly float FlickerRandomOffset = 0;
	public bool DrawSprite = true;
	internal Actor? SpriteShadowOwner;

	public float FlickerScale => 1 + CurrentFlicker * FlickerIntensity;
	public float FlickerScaleMax => 1 + FlickerIntensity;
	float CurrentFlicker => MathF.Sin(Timer * FlickerFrequency * MathF.Tau + FlickerRandomOffset);

	public override bool CastsSpriteShadow => false;
	public override bool CastsReflection => DrawSprite;

	static Rng rng = new();

	public bool IsStatic = true; // doesn't move

	public Light()
	{
		FlickerRandomOffset = rng.Float(0, 3.0f);
		Hitbox = new(new RectInt(-4, -4, 8, 8));
		Sprite = Assets.GetSprite(SpriteName);
		Play("idle");
		CollidesWithSolids = false;
		Depth = 1;
	}

	public override void Added()
	{
		base.Added();
		Sprite = Assets.GetSprite(SpriteName);

	}

	public override void Render(Batcher batcher)
	{
		if (DrawSprite)
			base.Render(batcher);
	}

	public override void RenderNormals(Batcher batcher)
	{
		if (DrawSprite)
			base.RenderNormals(batcher);
	}

	public override void RenderEmissive(Batcher batcher)
	{
		if (DrawSprite)
			base.RenderEmissive(batcher);
	}

}
