using Foster.Framework;

namespace TinyLink;

public class Mosquito : Actor
{
	int Health = 2;
	private SoundInstance? buzz;

	public Mosquito()
	{
		Sprite = Assets.GetSprite("mosquito");
		Hitbox = new(new RectInt(-5, -5, 10, 10));
		Mask = Masks.Enemy;
		Depth = -5;
		CollidesWithSolids = false;
		Play("fly");
	}

	public override void Added()
	{
		if (!Game.AudioEventsEnabled)
			return;

		try
		{
			buzz = Game.CreateAudioEvent("fly/idle", Position);
			buzz?.Start();
		}
		catch(Exception e)
		{
			Console.WriteLine(e);
			buzz?.Release();
			buzz = null;
		}
	}

	public override void Update()
	{
		base.Update();

		if (Game.GetFirst(Masks.Player) is {} player)
		{
			var diff = player.Position.X - Position.X;
			var dist = MathF.Abs(diff);
			var sign = MathF.Sign(diff);

			if (dist < 100)
				Velocity.X += sign * 100 * Time.Delta;
			else
				Velocity.X = Calc.Approach(Velocity.X, 0, 100 * Time.Delta);

			if (MathF.Abs(Velocity.X) > 50)
				Velocity.X = Calc.Approach(Velocity.X, MathF.Sign(Velocity.X) * 50, 800 * Time.Delta);
		}

		Velocity.Y = MathF.Sin(Timer * 4) * 10;
		var relativeTo = Game.GetFirst(Masks.Player)?.Position;
		Game.SetAudioEventPosition(buzz, Position, relativeTo);
	}

	public override void OnWasHit(Actor by)
	{
		Health--;

		if (Health <= 0)
		{
			AudioEvent("fly/die");
			Game.Create<Pop>(Position);
			StopBuzz(true);
			Game.Destroy(this);

		}
		else
		{
			AudioEvent("fly/hurt");
			var sign = MathF.Sign(Position.X - by.Position.X);
			Velocity.X = sign * 140;
		}
	}

	public override void Destroyed()
	{
		StopBuzz(true);
	}

	private void StopBuzz(bool immediate)
	{
		if (buzz == null)
			return;

		buzz.Stop(immediate);
		buzz.Release();
		buzz = null;
	}
}
