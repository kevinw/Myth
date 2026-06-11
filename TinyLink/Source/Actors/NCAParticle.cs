using System.Numerics;
using Foster.Framework;

namespace TinyLink;

/// <summary>
/// A small fading debris particle, spawned in a burst when a
/// NeuralCellularActor is cut.
/// </summary>
public class NCAParticle : Actor
{
	public Color Color = Color.White;
	public float Lifetime = 0.52f;

	private static Rng rng = new();

	private Vector2 floatPos;
	private float age;
	private float size;

	public override bool CastsSpriteShadow => false;
	public override bool CastsReflection => false;

	public override void Added()
	{
		base.Added();
		floatPos = Position;
		size = rng.Float(1.0f, 2.5f);
	}

	public override void Update()
	{
		base.Update();

		Velocity.Y += 240.0f * Time.Delta;
		floatPos += Velocity * Time.Delta;
		Position = (Point2)floatPos;

		age += Time.Delta;
		if (age >= Lifetime)
			Game.Destroy(this);
	}

	public override void Render(Batcher batcher)
	{
		var t = Math.Clamp(age / Lifetime, 0.0f, 1.0f);
		var color = Color * (1.0f - t);
		var s = size * (0.7f - t * 0.3f);
		batcher.Rect(new Vector2(-s / 2.0f, -s / 2.0f), new Vector2(s, s), color);
	}

	public override void RenderNormals(Batcher batcher) { }
}
