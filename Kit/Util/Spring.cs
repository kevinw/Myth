using System.Numerics;

namespace Kit;

public static class Spring
{
	public static void Step(ref float position, ref float velocity, float target,
		float stiffness, float damping, float delta)
	{
		velocity += (target - position) * stiffness * delta;
		velocity *= MathF.Exp(-damping * delta);
		position += velocity * delta;
	}

	public static void Step(ref Vector2 position, ref Vector2 velocity, Vector2 target,
		float stiffness, float damping, float delta)
	{
		velocity += (target - position) * stiffness * delta;
		velocity *= MathF.Exp(-damping * delta);
		position += velocity * delta;
	}

	public static void Step(ref Vector2 position, ref Vector2 velocity, Vector2 target,
		Vector2 stiffness, Vector2 damping, float delta)
	{
		velocity += (target - position) * stiffness * delta;
		velocity *= new Vector2(MathF.Exp(-damping.X * delta), MathF.Exp(-damping.Y * delta));
		position += velocity * delta;
	}

	public static void Step(ref Vector3 position, ref Vector3 velocity, Vector3 target,
		float stiffness, float damping, float delta, float maximumSpeed = float.PositiveInfinity)
	{
		velocity += (target - position) * stiffness * delta;
		velocity *= MathF.Exp(-damping * delta);
		if (velocity.LengthSquared() > maximumSpeed * maximumSpeed)
			velocity = Vector3.Normalize(velocity) * maximumSpeed;
		position += velocity * delta;
	}
}
