
using System.Numerics;
using System.Runtime.CompilerServices;
using Foster.Framework;

namespace Myth;

public partial class Game
{
	static float AttackBlockOverlapThreshold => 0.5f;

	internal int DamageActors(Actor fromActor, RectInt it, HashSet<Actor>? attackConsumed = null)
		=> DamageActors(fromActor, it, attackConsumed, out _);

	// overlappedTarget reports whether the hitbox touched any damageable actor at all, including ones
	// already in attackConsumed; the return value counts only the ones newly hit this call.
	internal int DamageActors(Actor fromActor, RectInt it, HashSet<Actor>? attackConsumed, out bool overlappedTarget)
	{
		var hitCount = 0;
		overlappedTarget = false;
		for (int i = 0, count = Actors.Count; i < count; i++)
		{
			var hit = Actors[i];

			if (destroying.Contains(hit) || !hit.Mask.Has(Actor.Masks.Enemy | Actor.Masks.Hazard | Actor.Masks.Destructible) || !hit.OverlapsCombatBounds(it))
				continue;

			overlappedTarget = true;
			if (attackConsumed == null || !attackConsumed.Contains(hit))
			{
				attackConsumed?.Add(hit);
				fromActor.Hit(hit, new(it));
				hitCount += 1;
			}
		}
		return hitCount;
	}
	
	internal bool IsAttackBlocked(Actor fromActor, in RectInt hitbox, bool checkOnlyForStrongActorBlock = false)
	{
		var attackPath = hitbox.Conflate(fromActor.Position);
		foreach (var actor in Actors)
		{
			if (actor != fromActor && !destroying.Contains(actor))
			{
				if (actor.BlocksAttack(fromActor, attackPath))
					return true;
			}
		}

		if (checkOnlyForStrongActorBlock)
			return false;

		if (GetRoomCollisionOverlapRatio(hitbox, includeBreakableTiles: false) >= AttackBlockOverlapThreshold)
			return true;

		return false;
	}

}
