using Foster.Framework;
using System.Numerics;

namespace Myth;

[GameActor(Tags = ActorTag.Enemy)]
public class ShieldKnight : Actor
{
	bool wasGrounded = true;
	bool grounded = true;

	public ShieldKnight() {
		Mask = Masks.Enemy;
		Depth = -5;
	}

	public override void Added()
	{
		Hitbox = Hitbox.FromSprite(Sprite);
		Play("idle");
		SetState(PatrolRoutine());
	}

	// A state is just its running routine. Transition by handing SetState a fresh one;
	// the routine's own body is the state's per-frame behavior.
	void SetState(IEnumerator<float> next) {
		routineWait = 0;
		routine = next;
	}

	static RectInt ShieldHitbox => new(4, -18, 2, 18);
	static float WalkSpeed => 22f.T();
	static float Gravity => 360f;
	static float Friction => 480.T();

	Rng rng = new();
	double lastBlockTime = 0;
	int health = 2;
	float turnTimer = 2.0f;
	Point2 playerPosition;
	Vector2 toPlayer;
	float routineWait;
	IEnumerator<float>? routine;

	bool CheckForTurnAround() {
		if (Facing != Math.Sign(toPlayer.X)) return false;
		SetState(TurnAroundRoutine());
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
				SetState(rng.Chance(0.5) ? HopRoutine() : StabRoutine());
				yield break;
			}

			var anim = "idle";
			if (turnTimer > 0)
			{
				turnTimer -= Time.Delta;
				if (turnTimer < 0)
				{
					turnTimer = 0;
					anim = "walk";
					Facing *= -1;
				}
			}
			else
			{
				Velocity.X = Facing * WalkSpeed;
				if (Math.Sign(toPlayer.X) == Facing)
				{
					turnTimer = 0.9f;
					anim = "idle";
					Velocity.X = 0;
				}
			}

			if (Time.Seconds - lastBlockTime < BLOCK_FRAME_TIME)
				anim = "block";

			Play(anim);

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
			float timer = 0;
			while (timer < 0.7f) {
				timer += Time.Delta;
				if (!wasGrounded && grounded) {
					Game.Shake(0.14f);
				}
				yield return 0;
			}
			if (CheckForTurnAround()) break;
			if (Math.Abs(toPlayer.X) < 30) {
				SetState(StabRoutine());
				break;
			} else if (rng.Chance(0.3)) {
				continue;
			} else {
				SetState(PatrolRoutine());
				break;
			}
		}
	}

	IEnumerator<float> StabRoutine()
	{
		yield return 0.3f;

		while (Play("stab", looping: false) != AnimState.Finished) {
			if (CurrentSpriteFrame?.UserData == "spear_hit")
			{
				var hurtBox = Hitbox.FromSlice(this, "spear").Rect + Position;
				if (Game.OverlapsFirst(hurtBox, Masks.Player) is { } hit)
					Hit(hit);
			}

			yield return 0;
		}
		SetState(PatrolRoutine());
	}

	IEnumerator<float> TurnAroundRoutine()
	{
		yield return 0.2f;
		Squish.X = 0.8f;
		Facing *= -1;
		yield return 0.1f;
		SetState(PatrolRoutine());
	}

	void RunCurrentRoutine()
	{
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

	public override void OnCollideY() {
		base.OnCollideY();
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

		wasGrounded = grounded;
		grounded = Grounded();

		RunCurrentRoutine();

		Velocity.Y += Gravity * Time.Delta;
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
