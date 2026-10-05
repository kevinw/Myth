using System.Numerics;
using Foster.Framework;

namespace Myth;

// Wall dive: falling along a wall with jump held -- the burrow button -- double-tap up or down and you dive into
// the wall, burrowing that way with a kick of speed. The floor dive is landing with jump held; this is the same
// held button meeting a wall, plus a gesture nobody makes by accident mid-wall-jump.
public abstract partial class Pawn
{
	internal static bool WallDiveEnabled = true;

	static float WallDiveTapWindow => 0.28f; // the second tap has to land this soon after the first
	static float WallDiveTapThreshold => 0.5f;
	static float WallDiveSpeed => BurrowMaxSpeed;
	static bool WallDiveFirstTapGrabs => true;  // the first tap digs in and holds you, so the second isn't a race
	static float WallDiveGrabFallSpeed => 12f;  // how fast you still slip down while gripping
	static float WallDiveGrabRate => 900f;      // how hard the grip bites into a fall

	int wallDiveLastTap;      // -1 up, 1 down: the previous tap's direction
	int wallDiveHeld;         // the vertical direction held last frame, for detecting fresh taps
	float wallDiveTapTimer;   // time left to land the second tap
	bool wallDiveGripping;    // the first tap grabbed the wall

	bool TryWallDive()
	{
		var held = MathF.Abs(input.MoveY) >= WallDiveTapThreshold ? Math.Sign(input.MoveY) : 0;
		var tapped = held != 0 && held != wallDiveHeld ? held : 0;
		wallDiveHeld = held;
		wallDiveTapTimer = MathF.Max(0, wallDiveTapTimer - Time.Delta);
		var wall = -GetWallJumpDirection(); // the side the wall is on
		var canDive = WallDiveEnabled && this is Player && !grounded && wall != 0 && input.JumpDown && !burrowNeedsGrounding && BurrowAvailable();

		// Gripping: slow the fall to a slip until the window runs out or you let go of the wall or the button.
		wallDiveGripping &= canDive && wallDiveTapTimer > 0;
		if (wallDiveGripping)
			Velocity.Y = Calc.Approach(Velocity.Y, WallDiveGrabFallSpeed, WallDiveGrabRate * Time.Delta);
		if (tapped == 0)
			return false;

		var second = wallDiveTapTimer > 0 && wallDiveLastTap == tapped;
		(wallDiveLastTap, wallDiveTapTimer) = second ? (0, 0) : (tapped, WallDiveTapWindow);
		if (!canDive)
			return false;
		if (!second)
		{
			if (WallDiveFirstTapGrabs && !wallDiveGripping)
			{
				wallDiveGripping = true;
				Squish = new Vector2(1.15f, 0.85f);
			}
			return false;
		}
		wallDiveGripping = false;
		if (!TryStartBurrow(Vector2.Zero, new Vector2(wall, 0), sealedBy: null))
			return false;

		// The start heads down the wall; turn around for an upward dive (heading and handedness flip together).
		if (burrow.Heading.Y != tapped)
		{
			burrow.Heading = new Point2(-burrow.Heading.X, -burrow.Heading.Y);
			burrow.SolidLeft = !burrow.SolidLeft;
		}
		burrowSpeed = WallDiveSpeed;
		if (Game.BurrowTraceEnabled)
			Game.TraceBurrow($"wall dive heading={burrow.Heading} solidLeft={burrow.SolidLeft} position={Position}");
		return true;
	}
}
