using System.Numerics;
using Foster.Framework;

namespace Myth;

// Something the player can hoist overhead by burrow-popping underneath it (Player.Carry.cs).
// Implementors: Carryable (inert cargo — crates), Bud (a willing passenger). While carried the
// implementor is collision-inert and position-synced to the carrier's head; the player's
// swollen standing hitbox is the combined body.
public interface ICarryable
{
	// The current carrier, or null when free. Maintained by OnPickedUp/Release.
	Player? CarriedBy { get; }

	// False while in a state that can't start a carry: already carried, or (Bud) riding
	// the player's burrow, mid burrow-pop handoff, hidden for a room transition, climbing.
	bool CanBeCaught { get; }

	// Height added on top of the player's standing hitbox while carried.
	int CarryHeight { get; }

	void OnPickedUp(Player player);

	// End the carry at `position` with `velocity` — a throw, a leap, a set-down (~zero
	// velocity), or a hurt-drop.
	void Release(Point2 position, Vector2 velocity);
}
