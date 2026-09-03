
using System.Numerics;
using Foster.Framework;

namespace Kit;

// Where an actor's Position sits within its sprite. This single anchor drives the sprite
// draw origin, the horizontal flip pivot, the editor's draggable box, and Hitbox.FromSprite,
// so they always agree.
public enum SpriteAnchor
{
	// Use the sprite's slice pivot if it has one, otherwise fall back to BottomCenter.
	// This is the default: artists who set a pivot get exactly that "contact point", and
	// pivot-less sprites still ground sensibly without any annotation.
	Auto,

	Pivot,        // Force the sprite's baked slice pivot (origin as authored), even if it's (0,0).
	BottomCenter, // Centered horizontally, sitting on the bottom of the visible pixels. Grounded enemies/objects.
	Center,       // Centered horizontally and vertically. Flying/decorative actors (lights, projectiles).
	TopLeft,      // Top-left of the visible pixels.
}

public class Sprite(string name, Vector2 origin)
{
	public readonly record struct Frame(Subtexture Subtexture, Subtexture VisibleSubtexture, Subtexture NormalSubtexture, Subtexture EmissiveSubtexture, float Duration, string? UserData = null);
	public readonly record struct Animation(string Name, int FrameStart, int FrameCount, float Duration);
	public readonly record struct Socket(Vector2 Position, Vector2 Normal, bool Visible = true);

	public string Name { get; } = name;
	public Vector2 Origin { get; private set; } = origin;
	public readonly List<Frame> Frames = new();
	public readonly List<Animation> Animations = new();
	// Named Aseprite slices: a slice is one region that spans the whole animation (we take its
	// first key's canvas-space bounds). Origin lookups use the pivot slice; these are for
	// gameplay regions like an attack hitbox. See SliceByName.
	public readonly Dictionary<string, RectInt> Slices = new(StringComparer.OrdinalIgnoreCase);
	// Per-frame attachment poses authored by *_socket layers in Aseprite.
	public readonly Dictionary<string, Socket[]> Sockets = new(StringComparer.OrdinalIgnoreCase);

	// Bounds of a named slice in sprite-local (canvas) pixels, or null. Use Hitbox.FromSlice
	// to turn it into an origin-relative, facing-aware hitbox.
	public RectInt? SliceByName(string name) => Slices.TryGetValue(name, out var rect) ? rect : null;

	public bool TryGetSocket(string name, int frameIndex, out Socket socket)
	{
		if (Sockets.TryGetValue(name, out var frames) && frameIndex >= 0 && frameIndex < frames.Length)
		{
			socket = frames[frameIndex];
			return true;
		}

		socket = default;
		return false;
	}

	public bool TryGetVisibleSocket(string name, int frameIndex, out Socket socket)
		=> TryGetSocket(name, frameIndex, out socket) && socket.Visible;

	// The cel userdata label authored on a frame (e.g. "impact"), or null. Pair with
	// AnimationFrameIndex to react on the exact frame an artist tagged.
	public string? FrameUserData(int frameIndex)
		=> frameIndex >= 0 && frameIndex < Frames.Count ? Frames[frameIndex].UserData : null;

	// Resolves the draw/flip/hitbox origin for the given anchor, in sprite-local pixels (the
	// same space as Origin). Anything other than Pivot is derived from the first frame's
	// visible (trimmed) pixels so it tracks the actual art, not the padded frame.
	public Vector2 ResolveOrigin(SpriteAnchor anchor)
	{
		if (anchor == SpriteAnchor.Pivot || Frames.Count == 0)
			return Origin;

		if (anchor == SpriteAnchor.Auto)
			return Origin != Vector2.Zero ? Origin : ResolveOrigin(SpriteAnchor.BottomCenter);

		var sub = Frames[0].Subtexture;
		float left = -sub.Frame.X;
		float top = -sub.Frame.Y;
		float w = sub.Source.Width;
		float h = sub.Source.Height;

		// Snap derived anchors to the pixel grid. An odd width/height puts the true center on a
		// half-pixel, and drawing there smears the sprite across texel boundaries; round to the
		// nearest whole pixel so it stays crisp (landing up to half a pixel off exact-center is
		// fine — off the grid isn't). Even dimensions already land on integers and are unchanged.
		return anchor switch
		{
			SpriteAnchor.TopLeft => new Vector2(left, top),
			SpriteAnchor.Center => new Vector2(MathF.Round(left + w / 2f), MathF.Round(top + h / 2f)),
			SpriteAnchor.BottomCenter => new Vector2(MathF.Round(left + w / 2f), top + h),
			_ => Origin,
		};
	}

	public void Reload(Vector2 origin, List<Frame> frames, List<Animation> animations,
		IReadOnlyDictionary<string, RectInt>? slices = null, IReadOnlyDictionary<string, Socket[]>? sockets = null)
	{
		Origin = origin;

		Frames.Clear();
		Frames.AddRange(frames);

		Animations.Clear();
		Animations.AddRange(animations);

		Slices.Clear();
		if (slices != null)
			foreach (var (name, rect) in slices)
				Slices[name] = rect;

		Sockets.Clear();
		if (sockets != null)
			foreach (var (name, poses) in sockets)
				Sockets[name] = poses;
	}

	public Frame? FrameForAnimationIndex(int animationIndex, float time, bool loop)
	{
		if (animationIndex >= Animations.Count)
			return null;

		return GetFrameAt(Animations[animationIndex], time, loop);
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

		for (int i = animation.FrameStart; i < animation.FrameStart + animation.FrameCount && i < Frames.Count; i++)
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
		for (int i = frameStart; i < frameStart + frameCount; i++)
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
