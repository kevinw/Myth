using System.Numerics;
using Foster.Framework;

namespace Myth;

public class Splitter : Actor
{
	public int Health = 2;
	public bool Small;
	public float HopInterval = 1.45f;
	public float HopSpeedX = 44;
	public float HopSpeedY = 92;
	public float Gravity = 340;

	private float hopTimer;
	private bool wasGrounded = true;

	public Splitter()
	{
		Sprite = Assets.GetSprite("blob");
		Hitbox = new(new RectInt(-4, -8, 8, 8));
		Mask = Masks.Enemy;
		Depth = -5;
		Play("idle");
	}

	public override void Update()
	{
		base.Update();

		Velocity.Y += Gravity * Time.Delta;
		var grounded = Grounded();
		if (grounded)
		{
			if (!wasGrounded)
			{
				Squish = Small ? new Vector2(1.25f, 0.75f) : new Vector2(1.45f, 0.60f);
				Play("idle");
			}

			Velocity.X = Calc.Approach(Velocity.X, 0, 380 * Time.Delta);
			hopTimer -= Time.Delta;
			if (hopTimer <= 0)
				HopAtPlayer();
		}

		wasGrounded = grounded;
	}

	private void HopAtPlayer()
	{
		hopTimer = HopInterval * (Small ? 0.72f : 1.0f);
		var direction = 1;
		if (Game.GetFirst(Masks.Player) is { } player)
		{
			direction = MathF.Sign(player.Position.X - Position.X) is { } sign && sign != 0 ? (int)sign : direction;
			Facing = direction;
		}

		var sizeScale = Small ? 0.75f : 1.0f;
		Velocity.X = direction * HopSpeedX * sizeScale;
		Velocity.Y = -HopSpeedY * sizeScale;
		Squish = Small ? new Vector2(0.75f, 1.25f) : new Vector2(0.60f, 1.45f);
		Play("jump", restart: true);
	}

	public override void Render(Batcher batcher)
	{
		if (!Small)
		{
			base.Render(batcher);
			return;
		}

		batcher.PushMatrix(Matrix3x2.CreateScale(0.75f, 0.75f));
		base.Render(batcher);
		batcher.PopMatrix();
	}

	public override void OnWasHit(Actor by)
	{
		Health--;
		if (Health > 0)
		{
			Velocity.X = MathF.Sign(Position.X - by.Position.X) * 120;
			return;
		}

		if (!Small)
		{
			SpawnSmall(-1);
			SpawnSmall(1);
		}

		Game.Create<Pop>(Position + new Point2(0, -4));
		Game.Destroy(this);
	}

	private void SpawnSmall(int direction)
	{
		Game.Create<Splitter>(Position + new Point2(direction * 5, -2), child =>
		{
			child.Small = true;
			child.Health = 1;
			child.HopInterval = HopInterval * 0.8f;
			child.HopSpeedX = HopSpeedX * 1.25f;
			child.HopSpeedY = HopSpeedY * 0.85f;
			child.Facing = direction;
			child.Velocity = new Vector2(direction * 70, -70);
			child.Squish = new Vector2(0.65f, 1.35f);
		});
	}
}
