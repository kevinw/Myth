using System.Numerics;
using Foster.Framework;

namespace Myth;

[GameActor(Tags = ActorTag.Enemy)]
public class GhostFrog : Actor
{
	private enum States
	{
		Waiting,
		ReadyingAttack,
		PerformSlash,
		Floating,
		Shoot,
		Reflect,
		Dead
	}

	public override string HitSoundType => "metal";

	private const int MaxHealthPhase1 = 10;
	private const int MaxHealthPhase2 = 3;

	private States state = States.Waiting;
	private int health = MaxHealthPhase1;
	private int phase = 0;
	private int side = 1;
	private int reflect_count = 0;
	private float friction = 0;
	private Point2 home;
	private Point2 lastPosition;
	private Point2 playerPosition;
	private readonly Coroutine routine = new();
	private readonly Func<IEnumerator<float>>[] stateRoutines;
	private Rng rng;

	public GhostFrog()
	{
		Depth = -5;
		Hitbox = new(new RectInt(-4, -12, 8, 12));
		Mask = Masks.Enemy;
		rng = new(Guid.NewGuid().Variant);
		Play("sword");

		stateRoutines = new[]
		{
			WaitingRoutine,
			ReadyingAttackRoutine,
			PerformSlashRoutine,
			FloatingRoutine,
			ShootRoutine,
			ReflectRoutine,
			DeadRoutine,
		};

		SetState(States.Waiting);
	}

	public override void Added()
	{
		base.Added();
		home = Position;
	}

	private IEnumerator<float> WaitingRoutine()
	{
		while (true)
			yield return 0;
	}

	private IEnumerator<float> ReadyingAttackRoutine()
	{
		float stateTimer = 0;

		while (true)
		{
			Facing = MathF.Sign(playerPosition.X - Position.X);
			if (Facing == 0)
				Facing = 1;

			float targetX = playerPosition.X + 32 * -Facing;
			Velocity.X = Calc.Approach(Velocity.X, MathF.Sign(targetX - Position.X) * 40, 400 * Time.Delta);
			friction = 100;
			Play("run");

			if (stateTimer > 3.0f || (stateTimer > 1.0f && OverlapsAny(new Point2(-Facing * 8, 0), Masks.Solid)))
			{
				Velocity.X = 0;
				SetState(States.PerformSlash);
			}

			stateTimer += Time.Delta;
			yield return 0;
		}
	}

	private IEnumerator<float> PerformSlashRoutine()
	{
		// start attack anim
		Play("attack", false);
		friction = 500;

		// after 0.8s, do the lunge
		yield return 0.80f;

		Velocity.X = Facing * 250;
		Hitbox = new(new RectInt(-4 + Facing * 4, -12, 8, 12));

		while (!IsFinishedPlaying("attack"))
		{
			RectInt rect = new(8, -8, 20, 8);
			if (Facing < 0)
				rect.X = -(rect.X + rect.Width);

			var attackBox = Position + rect;
			if (Game.OverlapsFirstCombat(attackBox, Masks.Player) is { } hit)
				Hit(hit, new(attackBox));

			yield return 0;
		}

		Hitbox = new(new RectInt(-4, -12, 8, 12));
		if (health > 0)
		{
			SetState(States.ReadyingAttack);
		}
		else
		{
			phase = 1;
			health = MaxHealthPhase2;
			side = rng.Chance(0.50f) ? -1 : 1;
			SetState(States.Floating);
		}
	}

	private IEnumerator<float> FloatingRoutine()
	{
		float stateTimer = 0;
		while (stateTimer < 5.0f)
		{
			Play("float");

			friction = 0;
			CollidesWithSolids = false;

			int targetY = home.Y - 50;
			int targetX = home.X + side * 50;

			if (MathF.Sign(targetY - Position.Y) != MathF.Sign(targetY - lastPosition.Y))
			{
				Velocity.Y = 0;
				Position.Y = targetY;
			}
			else
				Velocity.Y = Calc.Approach(Velocity.Y, MathF.Sign(targetY - Position.Y) * 50, 800 * Time.Delta);

			if (MathF.Abs(Position.Y - targetY) < 8)
				Velocity.X = Calc.Approach(Velocity.X, MathF.Sign(targetX - Position.X) * 120, 800 * Time.Delta);
			else
				Velocity.X = 0;

			if (MathF.Abs(targetX - Position.X) < 8 && MathF.Abs(targetY - Position.Y) < 8)
				break;

			stateTimer += Time.Delta;
			yield return 0;
		}

		SetState(States.Shoot);
	}

	private IEnumerator<float> ShootRoutine()
	{
		float time = 0.0f;
		while (time < 1.0f)
		{
			Velocity = Calc.Approach(Velocity, Vector2.Zero, 300 * Time.Delta);

			var sign = MathF.Sign(playerPosition.X - Position.X);
			Facing = sign == 0 ? Signs.Positive : sign;
			time += Time.Delta;
			yield return 0;
		}

		Play("reflect");
		yield return 0.20f;

		reflect_count = 0;
		Game.Create<Orb>(Position + new Point2(Facing * 12, -8));
		yield return 0.20f;

		Play("float");
		SetState(States.Reflect);
	}

	private IEnumerator<float> ReflectRoutine()
	{
		while (true)
		{
			var orb = Game.GetFirst<Orb>();
			if (orb == null)
				break;

			// wait until orb is coming towards us
			if (orb.TowardsPlayer)
			{
				yield return 0;
				continue;
			}

			var distance = (orb.Position - orb.Target).Length();

			if (reflect_count < 2)
			{
				if (distance < 20)
				{
					var sign = MathF.Sign(orb.Position.X - Position.X);
					if (sign != 0)
						Facing = sign;
					Play("reflect", false);

					var was = orb.Speed;
					orb.Speed = 0;
					yield return 0.1f;

					orb.Speed = was;
					Hit(orb, new (Hitbox.Rect + Position));
					reflect_count++;
					yield return 0.4f;

					Play("float");
					continue;
				}
			}
			else
			{
				if (distance < 8)
					orb.Hit(this, new(Hitbox.Rect + Position));
			}

			yield return 0;
		}

		yield return 1.0f;
		side = -side;
		SetState(States.Floating);
	}

	private IEnumerator<float> DeadRoutine()
	{
		float stateTimer = 0;
		while (stateTimer < 3.0f)
		{
			Play("dead");
			Game.Shake(1.0f);

			if (Time.OnInterval(0.25f))
			{
				var offset = new Point2(rng.Int(-16, 16), rng.Int(-16, 16));
				Game.Create<Pop>(Position + new Point2(0, -8) + offset);
			}

			stateTimer += Time.Delta;
			yield return 0;
		}

		for (int x = -1; x < 2; x ++)
			for (int y = -1; y < 2; y ++)
				Game.Create<Pop>(Position + new Point2(x * 12, -8 + y * 12));

		Game.HitStun(0.3f);
		Game.Shake(0.1f);
		Game.Destroy(this);
	}

	public override void Update()
	{
		base.Update();

		var player = Game.GetFirst(Masks.Player);
		if (player != null)
			playerPosition = player.Position;

		if (friction > 0 && CollidesWithSolids && Grounded())
			Velocity.X = Calc.Approach(Velocity.X, 0, friction * Time.Delta);

		routine.Update(Time.Delta);

		// floaty visual behavior
		if (state is States.Floating or States.Shoot or States.Reflect)
			Shift.Y = MathF.Sin(Timer * 2) * 3;
		else
			Shift.Y = 0;

		lastPosition = Position;
	}

	private void SetState(States nextState)
	{
		state = nextState;
		routine.Start(stateRoutines[(int)state]());
	}

	public override void Destroyed() => routine.Stop();

	internal override void OnWasHit(HitInfo hitInfo)
	{
		if (health <= 0) return;

		health--;
		if (health <= 0 && phase > 0)
			SetState(States.Dead);

		if (state == States.Waiting)
		{
			Game.Create<Pop>(Position + new Point2(0, -8));
			Game.HitStun(0.25f);
			SetState(States.ReadyingAttack);
		}
	}
}
