using System.Buffers;

namespace Kit;

// XORs each byte with a keystream derived from its position, so any seek works and
// scrambling twice restores the original. Obfuscation against casual browsing, not encryption.
public sealed class ScrambleStream(Stream inner, uint key) : Stream
{
	public override bool CanRead => inner.CanRead;
	public override bool CanSeek => inner.CanSeek;
	public override bool CanWrite => inner.CanWrite;
	public override long Length => inner.Length;
	public override long Position { get => inner.Position; set => inner.Position = value; }

	public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

	public override int Read(Span<byte> buffer)
	{
		var position = inner.Position;
		var read = inner.Read(buffer);
		Scramble(buffer[..read], position);
		return read;
	}

	public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

	public override void Write(ReadOnlySpan<byte> buffer)
	{
		var scrambled = ArrayPool<byte>.Shared.Rent(buffer.Length);
		var span = scrambled.AsSpan(0, buffer.Length);
		buffer.CopyTo(span);
		Scramble(span, inner.Position);
		inner.Write(span);
		ArrayPool<byte>.Shared.Return(scrambled);
	}

	void Scramble(Span<byte> data, long position)
	{
		for (var i = 0; i < data.Length; i++)
			data[i] ^= (byte)Hash32.Mix(key ^ (uint)(position + i));
	}

	public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
	public override void SetLength(long value) => inner.SetLength(value);
	public override void Flush() => inner.Flush();

	protected override void Dispose(bool disposing)
	{
		if (disposing)
			inner.Dispose();
		base.Dispose(disposing);
	}
}
