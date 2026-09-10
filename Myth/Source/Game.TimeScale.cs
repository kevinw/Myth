namespace Myth;

using Foster.Framework;

// Time bending is a registry of factors rather than a single value with save/restore. Every source
// that wants to slow, freeze, or speed up the sim holds a TimeFactor and sets its value; the sim's
// effective TimeScale is the product of all live factors. This composes in any order -- a source
// releasing only removes its own contribution -- so overlapping effects can't stomp each other the
// way the old "remember previous, put it back" pattern did. A full freeze is just factor 0, which
// dominates the product no matter what else is active.
public partial class Game
{
	public sealed class TimeFactor
	{
		internal object? owner;
		public float Factor;
	}

	readonly List<TimeFactor> timeFactors = [];

	// owner is optional. Pass the requesting Actor and the factor stops counting the moment that
	// actor is destroyed (and is dropped for good when it's removed), so paths that mass-destroy
	// actors -- player death, room transitions -- can never strand the sim frozen.
	public TimeFactor PushTimeFactor(float factor = 1f, object? owner = null)
	{
		var f = new TimeFactor { Factor = factor, owner = owner };
		timeFactors.Add(f);
		return f;
	}

	public void ReleaseAndSetNull(ref TimeFactor? factor)
	{
		if (factor != null)
		{
			timeFactors.Remove(factor);
			factor = null;
		}
	}

	internal float TimeScale
	{
		get
		{
			var scale = 1f;
			foreach (var f in timeFactors)
			{
				// An actor queued for destruction is logically gone; ignore its pull now so the
				// sim unfreezes this frame instead of waiting for RemoveDestroyedActors.
				if (f.owner is Actor a && destroying.Contains(a))
					continue;
				scale *= f.Factor;
			}
			return MathF.Max(0, scale);
		}
	}

	// The transient "juice" slowdown (hit landings etc.): one Game-owned factor that eases back to
	// 1 every frame. Callers just nudge it down; nothing has to release it.
	TimeFactor? slowdownFactor;
	public void TimeSlowdown(float slowdown)
	{
		slowdownFactor ??= PushTimeFactor();
		slowdownFactor.Factor = MathF.Min(slowdownFactor.Factor, slowdown);
	}

	void UpdateTimeFactors()
	{
		if (slowdownFactor != null)
			slowdownFactor.Factor = Calc.Approach(slowdownFactor.Factor, 1f, Manager.Time.Delta);
	}

	void DropTimeFactorsOwnedBy(Actor actor)
	{
		for (var i = timeFactors.Count - 1; i >= 0; i--)
			if (ReferenceEquals(timeFactors[i].owner, actor))
				timeFactors.RemoveAt(i);
	}

	// Debug time knob (see DebugControls): one Game-owned factor the dev cranks up or down.
	TimeFactor? debugTimeFactor;
	internal void ChangeTimeScale(int direction)
	{
		ReadOnlySpan<float> scales = [0.00001f, 0.05f, 0.1f, 0.25f, 0.5f, 1.0f, 2.0f, 4.0f];
		debugTimeFactor ??= PushTimeFactor();

		var index = 0;
		var bestDistance = float.MaxValue;
		for (var i = 0; i < scales.Length; i++)
		{
			var distance = MathF.Abs(scales[i] - debugTimeFactor.Factor);
			if (distance < bestDistance)
			{
				bestDistance = distance;
				index = i;
			}
		}

		index = Math.Clamp(index + direction, 0, scales.Length - 1);
		debugTimeFactor.Factor = scales[index];
		DisplayMessage($"Time x{debugTimeFactor.Factor:0.##}");
	}
}
