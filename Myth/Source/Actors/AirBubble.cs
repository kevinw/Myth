using System.Numerics;
using Foster.Framework;

namespace Myth;

// Emitted near a swimming pawn's head: drifts up slowly with a slight side-to-side wander,
// then pops and vanishes the moment it touches a solid.
public class AirBubble : Actor
{
	static float RiseSpeed => 16f;
	static float DriftSpeed => 4f;
	static float DriftInterval => 0.35f;

	bool popped;
	float driftTimer;

	public AirBubble()
	{
		Sprite = Assets.GetSprite("airbubble");
		Hitbox = new(new RectInt(-1, -1, 2, 2));
		Depth = -20;
	}

	public override void Added()
	{
		base.Added();
		Play("idle");
	}

	public override void Update()
	{
		if (!popped)
		{
			driftTimer -= Time.Delta;
			if (driftTimer <= 0)
			{
				driftTimer = DriftInterval;
				Velocity.X = (Random.Shared.NextSingle() * 2f - 1f) * DriftSpeed;
			}
			Velocity.Y = -RiseSpeed;
		}

		base.Update(); // moves by Velocity, advances the animation, pops on a solid via OnCollide*

		if (popped)
		{
			if (IsFinishedPlaying("pop"))
				Game.Destroy(this);
		}
		else if (Position.Y < Game.Bounds.Top)
			Game.Destroy(this); // rose out the top without hitting anything
	}

	void Pop()
	{
		if (popped) return;
		popped = true;
		Velocity = Vector2.Zero;
		Play("pop", false);
	}

	public override void OnCollideX() => Pop();
	public override void OnCollideY() => Pop();

	public override void RenderNormals(Batcher batcher) { }
}
