namespace Myth;

using Foster.Framework;

[GameActor(Tags = ActorTag.Enemy)]
class PotatoMan : Actor
{
	enum State { Idle, ReadyingAttack, Attacking }
	readonly StateMachine<State> state = new();

	public PotatoMan()
	{
		Hitbox = Hitbox.FromSprite(Sprite, inset: 3);
		Mask = Masks.Enemy;
	}

	internal override void OnWasHit(HitInfo hitInfo)
	{
		var by = hitInfo.By;
		if (state.Current == State.Idle && by is Player)
		{
			state.Change(State.ReadyingAttack);
		}
	}

	internal override int DamageValue => 0;

	public override void Update()
	{
		base.Update();
		switch (state.Update(Time.Delta))
		{
			case State.Idle:
				Play("idle");
				break;
			case State.ReadyingAttack:
				if (PlayOnce("windup") == AnimState.Finished)
					state.Change(State.Attacking);
				break;
			case State.Attacking:
				// The artist tags the impact cel "impact": shake once as it lands (edge), and
				// run the attack_hitbox slice for that frame's duration (level), body otherwise.
				if (AnimationEntered("impact"))
					Game.Shake(0.4f);
				Hitbox = AnimationTraversed("impact")
					? Hitbox.FromSlice(this, "attack_hitbox")
					: Hitbox.FromSprite(Sprite, inset: 3);
				if (PlayOnce("attack") == AnimState.Finished)
					state.Change(State.Idle);
				break;
		}

	}



}
