namespace Myth;

public static class JumpPlanExecution
{
	public const float MaxJumpHoldTime = 0.22f;

	public struct State
	{
		public float Timer;
		public float HoldTimer;
		public bool Started;
		public int WallKickCount;
		public int PostKickDirection;
		public bool WasWallJumpLocked;
	}

	public static void Advance(ref State state, float delta)
		=> state.Timer += delta;

	public static int MoveX(PlatformNavGraph.JumpPlan plan, State state)
	{
		if (plan.WallJump && state.WallKickCount > 0)
		{
			if (plan.AirDirection == 0)
				return state.PostKickDirection;
			return plan.AirDirection;
		}
		if (state.Timer >= plan.SwitchTime)
			return plan.AirDirection;
		return plan.Direction;
	}

	public static bool ShouldStartJump(PlatformNavGraph.JumpPlan plan, State state)
		=> !state.Started && state.Timer >= plan.RunupTime;

	public static bool NeedsWallKickInput(PlatformNavGraph.JumpPlan plan, State state, bool isGrounded, bool isWallJumpLocked)
		=> state.Started && !isGrounded && plan.WallJump && !isWallJumpLocked && state.WallKickCount < plan.MaxWallKicks;

	public static void StartJump(ref State state, PlatformNavGraph.JumpPlan plan)
	{
		state.Started = true;
		ResetHold(ref state, plan);
	}

	public static int WallApproachDirection(PlatformNavGraph.JumpPlan plan, State state)
		=> plan.AirDirection == 0 && state.WallKickCount > 0 ? state.PostKickDirection : plan.Direction;

	public static void SyncWallKickLock(ref State state, PlatformNavGraph.JumpPlan plan, bool isWallJumpLocked, int postKickDirection)
	{
		if (!isWallJumpLocked)
		{
			state.WasWallJumpLocked = false;
			return;
		}

		if (state.WasWallJumpLocked)
			return;

		state.WasWallJumpLocked = true;
		if (state.Started && plan.WallJump && state.WallKickCount < plan.MaxWallKicks)
			MarkWallKicked(ref state, plan, postKickDirection);
	}

	static void MarkWallKicked(ref State state, PlatformNavGraph.JumpPlan plan, int postKickDirection)
	{
		state.WallKickCount++;
		state.PostKickDirection = postKickDirection;
		ResetHold(ref state, plan);
	}

	public static void DecayHold(ref State state, float delta, bool jumpPressed)
	{
		if (state.Started && !jumpPressed)
			state.HoldTimer = MathF.Max(0, state.HoldTimer - delta);
	}

	public static bool JumpDown(State state, bool jumpPressed)
		=> jumpPressed || state.HoldTimer > 0;

	static void ResetHold(ref State state, PlatformNavGraph.JumpPlan plan)
		=> state.HoldTimer = Math.Clamp(plan.HoldTime, 0.055f, MaxJumpHoldTime);
}
