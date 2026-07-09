using Foster.Framework;
using System.Numerics;

namespace Myth;

[GameActor(Tags = ActorTag.Enemy)]
public class ShieldKnight : Actor
{
	enum States { Patrol, Hop, Stab, TurnAround }

	IEnumerator<float>? RoutineForState(States state) {
		return state switch {
			States.Patrol => PatrolRoutine(),
			States.Hop => HopRoutine(),
			States.Stab => StabRoutine(),
			States.TurnAround => TurnAroundRoutine(),
			_ => throw new ArgumentOutOfRangeException(nameof(state), state, null),
		};
	}

	public ShieldKnight() {
		Mask = Masks.Enemy;
		Depth = -5;
	}

	public override void Added()
	{
		Hitbox = Hitbox.FromSprite(Sprite);
		Play("idle");
		SetState(States.Patrol);
	}

	void SetState(States nextState) {
		state = nextState;
		routineWait = 0;
		routine = RoutineForState(state);
	}

	static RectInt ShieldHitbox => new(4, -18, 2, 18);
	static float WalkSpeed => 22f.T();
	static float Gravity => 360f;
	static float Friction => 480.T();

	Rng rng = new();
	States state = States.Patrol;
	double lastBlockTime = 0;
	int health = 2;
	float turnTimer = 2.0f;
	Point2 playerPosition;
	Vector2 toPlayer;
	float routineWait;
	IEnumerator<float>? routine;

	bool CheckForTurnAround() {
		if (Facing != Math.Sign(toPlayer.X)) return false;
		SetState(States.TurnAround);
		return true;
	}

	IEnumerator<float> PatrolRoutine()
	{
		float stateTimer = 0;
		while (true)
		{
			stateTimer += Time.Delta;
			if (CheckForTurnAround()) yield break;
			if (stateTimer > 0.5f && MathF.Abs(toPlayer.X) < 30f)
			{
				if (rng.Chance(0.5))
				{
					SetState(States.Hop);
				}
				else
				{
					SetState(States.Stab);
				}
			}

			yield return 0;
		}
	}

	// TODO: return a struct instead of <float>
	// float implicit coercsion
	// also wait for impact frame or anim finish
	//
	static float BLOCK_FRAME_TIME => 0.15f;

	IEnumerator<float> HopRoutine() {
		var facing = Facing;
		while (true) {
			if (Time.Seconds - lastBlockTime < BLOCK_FRAME_TIME)
				Play("block");
			else
				Play("idle");
			Velocity.X = 0;
			yield return 0.4f;
			Squish.Y = 0.25f;
			yield return 0.07f;
			Velocity.Y = -120;
			facing *= -1;
			Velocity.X = facing * 60.0f;
			yield return 0.7f;
			if (CheckForTurnAround()) break;
			if (Math.Abs(toPlayer.X) < 30) {
				SetState(States.Stab);
				break;
			} else if (rng.Chance(0.3)) {
				continue;
			} else {
				SetState(States.Patrol);
				break;
			}
		}
	}

	IEnumerator<float> StabRoutine()
	{
		yield return 0.3f;

		while (Play("stab", looping: false) != AnimState.Finished) {
			if (CurrentSpriteFrame.UserData == "spear_hit")
			{
				var hurtBox = Hitbox.FromSlice(this, "spear").Rect + Position;
				if (Game.OverlapsFirst(hurtBox, Masks.Player) is { } hit)
					Hit(hit);
			}

			yield return 0;
		}
		SetState(States.Patrol);
	}

	IEnumerator<float> TurnAroundRoutine()
	{
		yield return 0.2f;
		Squish.X = 0.8f;
		Facing *= -1;
		yield return 0.1f;
		SetState(States.Patrol);
	}

	void RunCurrentRoutine() {
		if (routine == null)
			return;
		var was = routine;
		if (routineWait > 0)
			routineWait -= Time.Delta;
		else if (routine.MoveNext())
			routineWait = routine.Current;
		else if (was == routine)
			routine = null;
	}

	public override void Update() {
		base.Update();

		var player = Game.GetFirst<Player>();
		if (player == null) {
			Play("idle");
			return;
		}
		playerPosition = player.Position;
		toPlayer = Position - playerPosition;

		RunCurrentRoutine();

		if (state == States.Patrol) {
			var anim = "idle";

			if (turnTimer > 0) {
				turnTimer -= Time.Delta;
				if (turnTimer < 0) {
					turnTimer = 0;
					anim = "walk";
					Facing *= -1;
				}
			} else {
				Velocity.X = Facing * WalkSpeed;
				var facingPlayer = Math.Sign(Position.X - player.Position.X);
				if (facingPlayer == Facing) {
					turnTimer = 0.9f;
					anim = "idle";
					Velocity.X = 0;
				}
			}

			if (Time.Seconds - lastBlockTime < BLOCK_FRAME_TIME)
				anim = "block";

			Play(anim);
		}

		Velocity.Y += Gravity * Time.Delta;
		var grounded = Grounded();
		if (grounded)
			Velocity.X = Calc.Approach(Velocity.X, 0, Friction * Time.Delta);
	}

	public override bool BlocksAttack(Actor attacker, in RectInt attackPath) {
		var blocks = MathF.Sign(attacker.Position.X - Position.X) == Facing &&
			attackPath.Overlaps(ShieldHitbox.ScaleX((int)Facing) + Position);

		if (blocks)
			lastBlockTime = Time.Seconds;

		return blocks;
	}

	public override void OnWasHit(Actor by)
	{
		if (health <= 0) return;

		health--;
		if (health <= 0) {
			for (int y = 0; y < 2; y++)
				Game.Create<Pop>(Position + new Point2(0, -8 + y * 12));
			Game.HitStun(0.3f);
			Game.DestroyForThisSession(this);
		}
	}
}
