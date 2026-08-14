using Foster.Framework;

namespace Myth;

// Process-wide pool of render Targets keyed by size + attachment format, so reconstructing
// a Game / GameView reuses the previous targets instead of reallocating them. All targets here
// are single-attachment with Flags.None, and there is one GraphicsDevice per process.
public static class RenderTargetPool
{
	private readonly record struct Key(int Width, int Height, TextureFormat Format, SampleCount SampleCount);

	private sealed class Entry(Target target, Key key, long bytes)
	{
		public readonly Target Target = target;
		public readonly Key Key = key;
		public readonly long Bytes = bytes;
		public LinkedListNode<Entry> KeyNode = null!;
		public LinkedListNode<Entry> LruNode = null!;
	}

	private static readonly Dictionary<Key, LinkedList<Entry>> pool = [];
	private static readonly LinkedList<Entry> lru = [];
	private static long pooledBytes;

	private const int MaxPerKey = 48;
	private const long MaxPooledBytes = 256L * 1024 * 1024;

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
		var bytes = (long)target.Width * target.Height * attachment.Format.Size() * Samples(attachment.SampleCount);
		lock (pool)
		{
			if (bytes > MaxPooledBytes)
			{
				target.Dispose();
				return;
			}
			if (!pool.TryGetValue(key, out var entries))
				pool[key] = entries = [];
			if (entries.Count >= MaxPerKey)
			{
				target.Dispose();
				return;
			}

			var entry = new Entry(target, key, bytes);
			entry.KeyNode = entries.AddLast(entry);
			entry.LruNode = lru.AddLast(entry);
			pooledBytes += bytes;
			while (pooledBytes > MaxPooledBytes)
				EvictOldest();
		}
	}

	private static Target? Rent(Key key)
	{
		lock (pool)
		{
			if (!pool.TryGetValue(key, out var entries) || entries.Last is not { } node)
				return null;
			var entry = node.Value;
			Remove(entry);
			return entry.Target;
		}
	}

	private static void EvictOldest()
	{
		var entry = lru.First!.Value;
		Remove(entry);
		entry.Target.Dispose();
	}

	private static void Remove(Entry entry)
	{
		var entries = pool[entry.Key];
		entries.Remove(entry.KeyNode);
		lru.Remove(entry.LruNode);
		pooledBytes -= entry.Bytes;
		if (entries.Count == 0)
			pool.Remove(entry.Key);
	}

	private static int Samples(SampleCount count) => count switch
	{
		SampleCount.One => 1,
		SampleCount.Two => 2,
		SampleCount.Four => 4,
		SampleCount.Eight => 8,
		_ => 1,
	};
}
