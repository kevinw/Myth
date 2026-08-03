namespace Myth;

// Sweeps the pawn's real vertical jump integration into lookup curves so the navigator
// can choose launch speed / airtime from geometry instead of replaying fixed timers.
//
// It replays Pawn.Update's exact vertical sequence (move → hold-decay → jump-cut → gravity)
// in free fall, reading the same constants the game uses (Pawn.JumpForce/JumpTime/Gravity/
// JumpHoldGravityMultiplier/JumpCutMultiplier/FastFallMultiplier). Tweak those and the
// curves follow on the next run — no hand-maintained tables.
public static class JumpPhysics
{
	const float Dt = 1.0f / 60.0f;
	const int MaxFrames = 240; // 4s safety

	// Peak height in px above the takeoff point for a given jump-hold time.
	public static float ApexHeight(float hold)
	{
		Simulate(hold, float.MaxValue, out var apex, out _);
		return apex;
	}

	// Time airborne (s) from takeoff until the arc descends to `drop` px below takeoff
	// (drop < 0 means a landing higher than takeoff). Returns -1 if the apex never reaches it.
	public static float AirtimeToDrop(float hold, float drop)
	{
		Simulate(hold, drop, out _, out var airtime);
		return airtime;
	}

	static void Simulate(float hold, float drop, out float apexHeight, out float airtimeToDrop)
	{
		var holdTimer = Math.Clamp(hold, 0.055f, JumpPlanExecution.MaxJumpHoldTime);
		var vy = Pawn.JumpForce;       // launch velocity (up, negative)
		var jumpTimer = Pawn.JumpTime; // reduced-gravity window while held
		var y = 0.0f;                  // +y is down; takeoff at 0
		var elapsed = 0.0f;

		// Takeoff frame: gravity is skipped (still grounded that frame) and the jump is held,
		// so only jumpTimer ticks down. Position doesn't move (pre-jump velocity was 0).
		jumpTimer -= Dt;

		apexHeight = 0.0f;
		airtimeToDrop = -1.0f;
		for (var i = 0; i < MaxFrames; i++)
		{
			y += vy * Dt;
			elapsed += Dt;
			if (-y > apexHeight)
				apexHeight = -y;
			if (airtimeToDrop < 0 && vy > 0 && y >= drop)
			{
				airtimeToDrop = elapsed;
				break;
			}

			holdTimer = MathF.Max(0, holdTimer - Dt);
			var jumpDown = holdTimer > 0;
			if (jumpTimer > 0)
			{
				if (!jumpDown)
				{
					jumpTimer = 0;
					vy *= Pawn.JumpCutMultiplier;
				}
				else
					jumpTimer -= Dt;
			}

			var grav = Pawn.Gravity;
			if (jumpTimer > 0)
				grav *= Pawn.JumpHoldGravityMultiplier;
			else if (vy > 0)
				grav *= Pawn.FastFallMultiplier;
			vy += grav * Dt;
		}
	}

	// One-shot sanity dump: apex and full up-and-down airtime across the hold range.
	public static void LogCurves()
	{
		Console.WriteLine("[JumpPhysics] hold -> apex(px), airtime-to-takeoff-level(s):");
		for (var hold = 0.055f; hold <= JumpPlanExecution.MaxJumpHoldTime + 1e-4f; hold += 0.02f)
			Console.WriteLine($"  hold {hold:0.000}: apex {ApexHeight(hold):0.0}px, airtime {AirtimeToDrop(hold, 0):0.000}s");
	}
}
