// In-process EventPipe allocation tracing for local dev builds.
#if KIT_DIAGNOSTICS
using System.Diagnostics.Tracing;
using Microsoft.Diagnostics.NETCore.Client;

namespace Myth;

internal sealed class AllocationTrace : IDisposable
{
	static readonly byte[] CopyBuffer = new byte[128 * 1024];
	static bool warmed;

	readonly EventPipeSession session;
	readonly Task copyTask;

	public readonly string Path;

	AllocationTrace(string path, EventPipeSession session)
	{
		Path = path;
		this.session = session;
		copyTask = Task.Run(() => Copy(session.EventStream, path));
	}

	public static AllocationTrace Start(string path)
		=> new(path, StartSession());

	public bool Stop(int timeoutMilliseconds = 10000)
	{
		session.Stop();
		return copyTask.Wait(timeoutMilliseconds);
	}

	public static void Warmup(int timeoutMilliseconds = 10000)
	{
		if (warmed)
			return;
		warmed = true;

		using var session = StartSession();
		var copyTask = Task.Run(() => Copy(session.EventStream, Stream.Null));
		session.Stop();
		if (!copyTask.Wait(timeoutMilliseconds))
			Console.Error.WriteLine("Allocation trace warmup stream did not finish within 10 seconds.");
	}

	public void Dispose()
		=> session.Dispose();

	static EventPipeSession StartSession()
	{
		var providers = new List<EventPipeProvider>
		{
			new("Microsoft-Windows-DotNETRuntime", EventLevel.Verbose, 0x8003)
		};
		return new DiagnosticsClient(Environment.ProcessId).StartEventPipeSession(providers, requestRundown: true);
	}

	static void Copy(Stream stream, string path)
	{
		using var file = File.Create(path);
		Copy(stream, file);
	}

	static void Copy(Stream stream, Stream output)
	{
		while (true)
		{
			var read = stream.Read(CopyBuffer);
			if (read <= 0)
				break;
			output.Write(CopyBuffer.AsSpan(0, read));
		}
	}
}
#endif
