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
	public bool DrawSprite = true;

	public float FlickerScale => 1 + MathF.Sin(Timer * FlickerFrequency * MathF.Tau) * FlickerIntensity;
	public override bool CastsSpriteShadow => false;
	public override bool CastsReflection => DrawSprite;

	public Light()
	{
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
