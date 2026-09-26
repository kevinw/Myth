using System.Numerics;

namespace Myth;

// Position-based (verlet) integration, shared by the actors that simulate point chains and rings.
// A point carries its velocity as the gap between where it is and where it was, so anything that
// moves a point -- a constraint, a collision -- changes its velocity too unless it moves the
// previous position along with it.
public static class Verlet
{
	// The longest step the constraint solvers are stable at.
	public static float MaxStep => 1f / 60f;

	// Per-step velocity retention for a per-second bleed rate.
	public static float Damping(float ratePerSecond, float dt) => MathF.Exp(-ratePerSecond * dt);

	// Advances one point under accel. displacementScale rescales the carried velocity when this
	// step is a different length than the one it was measured over -- see Steps.
	public static void Integrate(ref Vector2 point, ref Vector2 previous, Vector2 accel, float dt, float displacementScale, float damping)
	{
		var from = point;
		point = from + (from - previous) * displacementScale * damping + accel * (dt * dt);
		previous = from;
	}

	// Splits a frame into equal sub-steps no longer than MaxStep: slow motion still gets one
	// smooth small step, while fast motion and hitches split rather than handing the solver a
	// timestep it is unstable at.
	//
	// A point's velocity is a position delta measured over the previous step, so the first
	// sub-step has to rescale it for the new step length; without that the sim quietly speeds up
	// and slows down with the frame rate. Drive it with:
	//
	//   for (var steps = new Verlet.Steps(Time.Delta, ref lastDelta); steps.Next(out var dt, out var scale);)
	//       SimulateStep(dt, scale);
	public struct Steps
	{
		readonly float dt;
		int remaining;
		float scale;

		public Steps(float frameDelta, ref float lastDelta)
		{
			var count = Math.Max(1, (int)MathF.Ceiling(frameDelta / MaxStep - 0.0001f));
			dt = frameDelta / count;
			remaining = count;
			scale = dt / lastDelta;
			lastDelta = dt;
		}

		public bool Next(out float stepDelta, out float displacementScale)
		{
			stepDelta = dt;
			displacementScale = scale;
			scale = 1;
			return remaining-- > 0;
		}
	}
}
