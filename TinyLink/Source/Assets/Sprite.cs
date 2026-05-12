
using System.Numerics;
using Foster.Framework;

namespace TinyLink;

public class Sprite(string name, Vector2 origin)
{
	public readonly record struct Frame(Subtexture Subtexture, float Duration);
	public readonly record struct Animation(string Name, int FrameStart, int FrameCount, float Duration);

	public string Name { get; } = name;
	public Vector2 Origin { get; private set; } = origin;
	public readonly List<Frame> Frames = new();
	public readonly List<Animation> Animations = new();

	public void Reload(Vector2 origin, List<Frame> frames, List<Animation> animations)
	{
		Origin = origin;

		Frames.Clear();
		Frames.AddRange(frames);

		Animations.Clear();
		Animations.AddRange(animations);
	}

	public Frame GetFrameAt(in Animation animation, float time, bool loop)
	{
		var index = GetFrameIndexAt(animation, time, loop);
		return index < 0 ? new() : Frames[index];
	}

	public int GetFrameIndexAt(in Animation animation, float time, bool loop)
	{
		if (Frames.Count <= 0)
			return -1;

		if (animation.FrameCount <= 0 || animation.FrameStart < 0 || animation.FrameStart >= Frames.Count)
			return 0;

		if (time >= animation.Duration && !loop)
			return Math.Min(animation.FrameStart + animation.FrameCount - 1, Frames.Count - 1);

		if (animation.Duration > 0)
			time %= animation.Duration;

		for (int i = animation.FrameStart; i < animation.FrameStart + animation.FrameCount && i < Frames.Count; i ++)
		{
			time -= Frames[i].Duration;
			if (time <= 0)
				return i;
		}
		return animation.FrameStart;
	}

	public void AddAnimation(string name, int frameStart, int frameCount)
	{
		float duration = 0;
		for (int i = frameStart; i < frameStart + frameCount; i ++)
			duration += Frames[i].Duration;
		Animations.Add(new(name, frameStart, frameCount, duration));
	}

	public Animation? GetAnimation(string? name)
	{
		if (name != null)
		{
			foreach (var it in Animations)
				if (it.Name == name)
					return it;
		}

		return null;
	}
}
