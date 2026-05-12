using System.Numerics;
using Foster.Framework;

namespace TinyLink;

public class Light : Actor
{
	public float Radius = 48;
	public Color LightColor = 0xffd37a;
	public float FlickerFrequency = 4;
	public float FlickerIntensity = 0.08f;

	public float FlickerScale => 1 + MathF.Sin(Timer * FlickerFrequency * MathF.PI * 2) * FlickerIntensity;

	public Light()
	{
		Hitbox = new(new RectInt(-4, -4, 8, 8));
		Sprite = Assets.GetSprite("circle");
		CollidesWithSolids = false;
		Depth = -100;
	}

	public override void Render(Batcher batcher)
	{
		var color = LightColor * 0.65f;
		batcher.CircleLine(Vector2.Zero, Radius * FlickerScale, 1, 32, color);
		batcher.Circle(Vector2.Zero, 2, 12, color);
	}
}
