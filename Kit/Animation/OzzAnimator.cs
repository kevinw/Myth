using System.Numerics;

namespace Kit;

public sealed class OzzAnimator : IDisposable
{
	readonly OzzRigInstance instance;
	readonly int[] clipIndices = new int[2];
	readonly float[] times = new float[2];
	readonly float[] weights = new float[2];
	OzzRig.Clip? outgoingClip;
	float outgoingTime;
	float blendDuration;
	float blendRemaining;
	bool outgoingLoop = true;
	bool loop = true;

	public OzzRig Rig { get; }
	public OzzRig.Clip ActiveClip { get; private set; }
	public float Time { get; private set; }

	public OzzAnimator(OzzRig rig, string initialClip)
	{
		Rig = rig;
		instance = rig.CreateInstance();
		ActiveClip = rig.GetClip(initialClip);
	}

	public void Play(string name, float transitionDuration, bool restart = false)
	{
		var clip = Rig.GetClip(name);
		if (clip == ActiveClip && !restart)
			return;
		if (transitionDuration > 0)
		{
			outgoingClip = ActiveClip;
			outgoingTime = Time;
			outgoingLoop = loop;
			blendDuration = blendRemaining = transitionDuration;
		}
		else
		{
			outgoingClip = null;
			blendDuration = blendRemaining = 0;
		}
		ActiveClip = clip;
		Time = 0;
	}

	public void SetTime(float value)
	{
		Time = Math.Clamp(value, 0, ActiveClip.Duration);
		outgoingClip = null;
		blendDuration = blendRemaining = 0;
	}

	public void Advance(float animationDelta, float transitionDelta, bool loop)
	{
		this.loop = loop;
		Time = AdvanceTime(Time, animationDelta, ActiveClip.Duration, loop);
		if (outgoingClip == null)
			return;
		outgoingTime = AdvanceTime(outgoingTime, animationDelta, outgoingClip.Duration, outgoingLoop);
		blendRemaining = MathF.Max(0, blendRemaining - transitionDelta);
		if (blendRemaining <= 0)
			outgoingClip = null;
	}

	public void Sample(Span<Vector3> pose)
	{
		if (outgoingClip == null)
		{
			clipIndices[0] = ActiveClip.Index;
			times[0] = Time;
			weights[0] = 1;
			instance.Sample(clipIndices.AsSpan(0, 1), times.AsSpan(0, 1), weights.AsSpan(0, 1), pose);
			return;
		}

		var amount = 1 - blendRemaining / blendDuration;
		amount = amount * amount * (3 - 2 * amount);
		clipIndices[0] = outgoingClip.Index;
		clipIndices[1] = ActiveClip.Index;
		times[0] = outgoingTime;
		times[1] = Time;
		weights[0] = 1 - amount;
		weights[1] = amount;
		instance.Sample(clipIndices, times, weights, pose);
	}

	static float AdvanceTime(float time, float delta, float duration, bool loop)
	{
		time += delta;
		return loop ? ((time % duration) + duration) % duration : Math.Clamp(time, 0, duration);
	}

	public void Dispose() => instance.Dispose();
}
