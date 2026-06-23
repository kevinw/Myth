using Foster.Framework;

namespace Myth;

// Process-wide pool of render Targets keyed by size + attachment format, so reconstructing
// a Game / GameView (every editor room change) reuses the previous one's ~23 targets instead
// of freeing and reallocating them (the EnsureRenderTargets spike). All targets here are
// single-attachment with Flags.None, and there is one GraphicsDevice per process, so the key
// is (width, height, format, sampleCount). Render-thread only (GPU resources are thread-affine).
public static class RenderTargetPool
{
	private readonly record struct Key(int Width, int Height, TextureFormat Format, SampleCount SampleCount);

	private static readonly Dictionary<Key, Stack<Target>> pool = [];

	// Cap per key so navigating many distinct room sizes can't grow GPU memory without bound;
	// one render uses ~23 targets at a single size, so this comfortably covers reuse.
	private const int MaxPerKey = 48;

	public static Target RentColor(GraphicsDevice graphicsDevice, int width, int height, string name)
		=> Rent(new Key(width, height, TextureFormat.Color, SampleCount.One))
			?? new Target(graphicsDevice, width, height, name);

	public static Target RentFormat(GraphicsDevice graphicsDevice, int width, int height, TextureFormat format, string name)
	{
		if (Rent(new Key(width, height, format, SampleCount.One)) is { } reused)
			return reused;
		ReadOnlySpan<TextureFormat> attachments = [format];
		return new Target(graphicsDevice, width, height, attachments, name: name);
	}

	public static void Return(Target target)
	{
		var attachment = target.Attachments[0];
		var key = new Key(target.Width, target.Height, attachment.Format, attachment.SampleCount);
		lock (pool)
		{
			if (!pool.TryGetValue(key, out var stack))
				pool[key] = stack = new();
			if (stack.Count >= MaxPerKey)
				target.Dispose();
			else
				stack.Push(target);
		}
	}

	private static Target? Rent(Key key)
	{
		lock (pool)
			return pool.TryGetValue(key, out var stack) && stack.Count > 0 ? stack.Pop() : null;
	}
}
