using Foster.Framework;

namespace TinyLink;

public class Light : Actor
{
	public float Radius = 48;
	public Color LightColor = 0xffd37a;
	public float Intensity = 1.0f;
	public float Height = 16;
	public float FlickerFrequency = 4;
	public float FlickerIntensity = 0.08f;
	readonly float FlickerRandomOffset = 0;
	public bool DrawSprite = true;

	public float FlickerScale => 1 + MathF.Sin(Timer * FlickerFrequency * MathF.Tau + FlickerRandomOffset) * FlickerIntensity;
	public override bool CastsSpriteShadow => false;
	public override bool CastsReflection => DrawSprite;

	static Rng rng = new Rng();

	public Light()
	{
		FlickerRandomOffset = rng.Float(0, 3.0f);
		Hitbox = new(new RectInt(-4, -4, 8, 8));
		Sprite = Assets.GetSprite("fire");
		Play("idle");
		CollidesWithSolids = false;
		Depth = -5;
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

}
