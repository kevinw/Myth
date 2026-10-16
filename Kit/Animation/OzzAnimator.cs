using System.Numerics;

namespace Kit;

public sealed class OzzAnimator : IDisposable
{
	const int MaxLayers = 4;

	readonly OzzRigInstance instance;
	readonly int[] clipIndices = new int[MaxLayers];
	readonly float[] times = new float[MaxLayers];
	readonly float[] weights = new float[MaxLayers];
	readonly byte[] additive = new byte[MaxLayers];
	readonly byte[] masked = new byte[MaxLayers];
	readonly float[] jointWeights;
	OzzRig.Clip? outgoingClip;
	OzzRig.Clip? layerClip;
	OzzJointMask? layerMask;
	OzzRig.Clip? overrideClip;
	OzzJointMask? overrideMask;
	float overrideTime;
	float overrideWeight;
	float outgoingTime;
	float outgoingStart;
	float outgoingEnd;
	float layerTime;
	float layerWeight;
	float blendDuration;
	float blendRemaining;
	bool outgoingLoop = true;
	bool loop = true;
	float rangeStart;
	float rangeEnd;

	public OzzRig Rig { get; }
	public OzzRig.Clip ActiveClip { get; private set; }
	public float Time { get; private set; }
	public float RangeStart => rangeStart;
	public float RangeEnd => rangeEnd;
	public bool Finished => !loop && Time >= rangeEnd;

	public OzzAnimator(OzzRig rig, string initialClip)
	{
		Rig = rig;
		instance = rig.CreateInstance();
		jointWeights = new float[MaxLayers * rig.JointCount];
		ActiveClip = rig.GetClip(initialClip);
		rangeEnd = ActiveClip.Duration;
	}

	public void SetAdditive(string name, float time, float weight, OzzJointMask mask)
		=> SetAdditive(Rig.GetClip(name), time, weight, mask);

	public void SetAdditive(OzzRig.Clip clip, float time, float weight, OzzJointMask mask)
	{
		CheckMask(mask);
		layerClip = clip;
		layerTime = Math.Clamp(time, 0, clip.Duration);
		layerWeight = weight;
		layerMask = mask;
	}

	public void ClearLayer() => layerClip = null;

	// Replaces the playing clip on the masked joints, by weight × the mask's: the clip shows through only where it's not.
	public void SetOverride(OzzRig.Clip clip, float time, float weight, OzzJointMask mask)
	{
		CheckMask(mask);
		overrideClip = clip;
		overrideTime = Math.Clamp(time, 0, clip.Duration);
		overrideWeight = Math.Clamp(weight, 0, 1);
		overrideMask = mask;
	}

	public void ClearOverride() => overrideClip = null;

	void CheckMask(OzzJointMask mask)
	{
		if (!ReferenceEquals(mask.Rig, Rig))
			throw new ArgumentException("Joint mask belongs to a different rig.", nameof(mask));
	}

	public void Play(string name, float transitionDuration, bool restart = false, float startTime = 0)
		=> Play(name, 0, Rig.GetClip(name).Duration, transitionDuration, restart, startTime);

	public void Play(string name, float rangeStart, float rangeEnd, float transitionDuration,
		bool restart = false, float? startTime = null)
	{
		var clip = Rig.GetClip(name);
		rangeStart = Math.Clamp(rangeStart, 0, clip.Duration);
		rangeEnd = Math.Clamp(rangeEnd, rangeStart, clip.Duration);
		if (clip == ActiveClip && this.rangeStart == rangeStart && this.rangeEnd == rangeEnd && !restart)
			return;
		if (transitionDuration > 0)
		{
			outgoingClip = ActiveClip;
			outgoingTime = Time;
			outgoingStart = this.rangeStart;
			outgoingEnd = this.rangeEnd;
			outgoingLoop = loop;
			blendDuration = blendRemaining = transitionDuration;
		}
		else
		{
			outgoingClip = null;
			blendDuration = blendRemaining = 0;
		}
		ActiveClip = clip;
		this.rangeStart = rangeStart;
		this.rangeEnd = rangeEnd;
		Time = Math.Clamp(startTime ?? rangeStart, rangeStart, rangeEnd);
	}

	public void SetTime(float value)
	{
		Time = Math.Clamp(value, rangeStart, rangeEnd);
		outgoingClip = null;
		blendDuration = blendRemaining = 0;
	}

	public void Advance(float animationDelta, float transitionDelta, bool loop)
	{
		this.loop = loop;
		Time = AdvanceTime(Time, animationDelta, rangeStart, rangeEnd, loop);
		if (outgoingClip == null)
			return;
		outgoingTime = AdvanceTime(outgoingTime, animationDelta, outgoingStart, outgoingEnd, outgoingLoop);
		blendRemaining = MathF.Max(0, blendRemaining - transitionDelta);
		if (blendRemaining <= 0)
			outgoingClip = null;
	}

	public void Sample(Span<Vector3> pose, Span<Quaternion> rotations)
	{
		var layerCount = 1;
		Array.Clear(additive);
		Array.Clear(masked);
		if (outgoingClip == null)
		{
			clipIndices[0] = ActiveClip.Index;
			times[0] = Time;
			weights[0] = 1;
		}
		else
		{
			var amount = 1 - blendRemaining / blendDuration;
			amount = amount * amount * (3 - 2 * amount);
			clipIndices[0] = outgoingClip.Index;
			clipIndices[1] = ActiveClip.Index;
			times[0] = outgoingTime;
			times[1] = Time;
			weights[0] = 1 - amount;
			weights[1] = amount;
			layerCount = 2;
		}

		if (overrideClip != null && overrideMask != null && overrideWeight > 0)
		{
			var joints = Rig.JointCount;
			for (var layer = 0; layer < layerCount; layer++)
			{
				masked[layer] = 1;
				for (var j = 0; j < joints; j++)
					jointWeights[layer * joints + j] = 1 - overrideWeight * overrideMask[j];
			}
			clipIndices[layerCount] = overrideClip.Index;
			times[layerCount] = overrideTime;
			weights[layerCount] = overrideWeight;
			masked[layerCount] = 1;
			overrideMask.Weights.CopyTo(jointWeights, layerCount * joints);
			layerCount++;
		}

		if (layerClip != null && layerMask != null && layerWeight != 0)
		{
			clipIndices[layerCount] = layerClip.Index;
			times[layerCount] = layerTime;
			weights[layerCount] = layerWeight;
			additive[layerCount] = 1;
			masked[layerCount] = 1;
			layerMask.Weights.CopyTo(jointWeights, layerCount * Rig.JointCount);
			layerCount++;
		}
		instance.Sample(clipIndices.AsSpan(0, layerCount), times.AsSpan(0, layerCount),
			weights.AsSpan(0, layerCount), additive.AsSpan(0, layerCount), masked.AsSpan(0, layerCount),
			jointWeights.AsSpan(0, layerCount * Rig.JointCount), pose, rotations);
	}

	static float AdvanceTime(float time, float delta, float start, float end, bool loop)
	{
		time += delta;
		var duration = end - start;
		return loop && duration > 0
			? start + ((time - start) % duration + duration) % duration
			: Math.Clamp(time, start, end);
	}

	public void Dispose() => instance.Dispose();
}
