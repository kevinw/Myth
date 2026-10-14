#if !BROWSER
using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;

namespace Myth;

static class DevHost
{
	static readonly AutoResetEvent runRequested = new(false);
	static readonly string pipeName = $"myth-dev-{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Environment.CurrentDirectory)))[..12]}";
	static readonly string pidPath = Path.GetFullPath("obj/myth-dev-host.pid");
	static readonly string runRequestPath = Path.GetFullPath("obj/myth-dev-run.request");
	static int sessionState;
	// Commands that touch game state wait here until Pump answers them on the game thread.
	static readonly ConcurrentQueue<(Func<Manager, string> Run, TaskCompletionSource<string> Reply)> pending = new();

	public static void Run(StartArgs args)
	{
		// The pipe name is derived from the working directory, so only one host per directory can own
		// it -- a second would silently steal the name and commands would land on whichever bound
		// last. Ask the pipe rather than the pid file: the pipe is what a command actually reaches,
		// and the file can be stale or name a process that no longer holds it. Separate worktrees
		// hash to different names and still coexist.
		if (TrySend("status", out var owner))
		{
			Console.Error.WriteLine($"Myth dev host is already running for this directory. {owner}");
			Environment.ExitCode = 1;
			return;
		}

		Directory.CreateDirectory(Path.GetDirectoryName(pidPath)!);
		File.WriteAllText(pidPath, Environment.ProcessId.ToString());
		File.Delete(runRequestPath);
		StartServer();
		Console.WriteLine($"Myth dev host ready (PID {Environment.ProcessId}).");

		try
		{
			// One App for the life of the host. Quitting the game parks it (Manager.QuitNow) rather
			// than returning here, because a second Manager in this process renders with shaders the
			// first one's Dispose destroyed -- and exiting would make the next Play wait on a rebuild.
			runRequested.WaitOne();
			Volatile.Write(ref sessionState, 2);
			Console.WriteLine("Starting Myth session.");
			var manager = Manager.Create(args);
			PlatformSpecific.ManagerSetup(manager);
			manager.Run();
			PlatformSpecific.ManagerCleanup(manager);
			Volatile.Write(ref sessionState, 0);
			while (pending.TryDequeue(out var stale))
				stale.Reply.TrySetResult("error: the Myth session closed.");
			Console.WriteLine("Myth dev host stopped.");
		}
		finally
		{
			File.Delete(pidPath);
		}
	}

	// A plain editor launch answers dev commands too, unless another process already owns this directory's pipe.
	public static void Listen()
	{
		if (TrySend("status", out _))
			return;
		Volatile.Write(ref sessionState, 2);
		StartServer();
	}

	static void StartServer() => Task.Run(async () =>
	{
		try
		{
			await Serve();
		}
		catch (Exception e)
		{
			Environment.FailFast("Myth dev-host command server failed.", e);
		}
	});

	public static bool Send(string command)
	{
		if (command == "cs")
			command = "cs " + Console.In.ReadToEnd();
		if (TrySend(command, out var response))
		{
			Console.WriteLine(response);
			return true;
		}

		if (command != "run")
		{
			Console.Error.WriteLine("Myth dev host is not running.");
			return false;
		}

		File.WriteAllText(runRequestPath, "run");
		for (var attempt = 0; attempt < 150; attempt++)
		{
			Thread.Sleep(100);
			if (TrySend(command, out response))
			{
				File.Delete(runRequestPath);
				Console.WriteLine(response);
				return true;
			}
		}

		Console.Error.WriteLine("Myth dev host did not become ready; run the 'Play' Zed task again.");
		return false;
	}

	static bool TrySend(string command, out string? response)
	{
		try
		{
			using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.CurrentUserOnly);
			client.Connect(100);
			using var reader = new StreamReader(client, Encoding.UTF8, leaveOpen: true);
			using var writer = new StreamWriter(client, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };
			// Requests stay one line on the wire; replies run until the server closes the pipe.
			writer.WriteLine(command.ReplaceLineEndings("\0"));
			response = reader.ReadToEnd().TrimEnd();
			return response.Length > 0;
		}
		catch (TimeoutException)
		{
			response = null;
			return false;
		}
		catch (IOException)
		{
			response = null;
			return false;
		}
	}

	// Each connection is answered on its own task, so a command waiting on the game thread never leaves another
	// launch's `status` check unanswered -- it would take that as nobody owning the pipe and start a second host.
	static async Task Serve()
	{
		while (true)
		{
			var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
				PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
			await server.WaitForConnectionAsync();
			_ = Answer(server);
		}
	}

	static async Task Answer(NamedPipeServerStream server)
	{
		await using var _ = server;
		try
		{
			using var reader = new StreamReader(server, Encoding.UTF8, leaveOpen: true);
			using var writer = new StreamWriter(server, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };
			if ((await reader.ReadLineAsync())?.Replace('\0', '\n') is { } command)
				await writer.WriteLineAsync(await Respond(command));
		}
		catch (Exception e) when (e is not IOException)
		{
			Console.Error.WriteLine($"[DevHost] {e}");
		}
		catch (IOException) { } // the client hung up
	}

	static async Task<string> Respond(string command) => command switch
	{
		"run" when Interlocked.CompareExchange(ref sessionState, 1, 0) == 0 => QueueRun(),
		"run" when Volatile.Read(ref sessionState) == 1 => "Myth is starting.",
		"run" => await OnGameThread(m => m.RunDevCommand("wake")),
		"status" => Volatile.Read(ref sessionState) switch
		{
			0 => $"Myth dev host is ready (PID {Environment.ProcessId}).",
			1 => $"Myth is starting (PID {Environment.ProcessId}).",
			_ => $"Myth is running (PID {Environment.ProcessId}).",
		},
#if EDITOR
		"cs-reset" => DevScript.Reset(),
		_ when command.StartsWith("cs ") => await DevScript.Execute(command[3..], fromClaude: true),
#endif
		_ => await OnGameThread(m => m.RunDevCommand(command)),
	};

	static string QueueRun()
	{
		runRequested.Set();
		return "Myth launch requested.";
	}

	// Hand the work to the running game and wait for its answer.
	internal static async Task<string> OnGameThread(Func<Manager, string> run)
	{
		if (Volatile.Read(ref sessionState) != 2)
			return "error: no Myth session is running.";

		var reply = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
		pending.Enqueue((run, reply));
		try
		{
			return await reply.Task.WaitAsync(TimeSpan.FromSeconds(10));
		}
		catch (TimeoutException)
		{
			reply.TrySetCanceled();
			return "error: the game thread did not answer within 10s.";
		}
	}

	// Drains queued commands on the game thread; called once per frame from Manager.Update.
	public static void Pump(Manager manager)
	{
		while (pending.TryDequeue(out var item))
		{
			try { item.Reply.TrySetResult(item.Run(manager)); }
			catch (Exception e) { item.Reply.TrySetResult($"error: {e.Message}"); }
		}
	}
}
#endif
