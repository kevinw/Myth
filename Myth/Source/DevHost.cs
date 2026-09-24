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
	static readonly ConcurrentQueue<(string Command, TaskCompletionSource<string> Reply)> pending = new();

	public static void Run(StartArgs args)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(pidPath)!);
		File.WriteAllText(pidPath, Environment.ProcessId.ToString());
		File.Delete(runRequestPath);
		_ = Task.Run(async () =>
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
		Console.WriteLine($"Myth dev host ready (PID {Environment.ProcessId}).");

		try
		{
			while (true)
			{
				runRequested.WaitOne();
				Volatile.Write(ref sessionState, 2);
				Console.WriteLine("Starting Myth session.");
				var manager = Manager.Create(args);
				PlatformSpecific.ManagerSetup(manager);
				manager.Run();
				PlatformSpecific.ManagerCleanup(manager);
				while (pending.TryDequeue(out var stale))
					stale.Reply.TrySetResult("error: the Myth session closed.");
				if (manager.UserQuitRequested)
				{
					Console.WriteLine("Game quit requested; stopping Myth dev host.");
					return;
				}
				Volatile.Write(ref sessionState, 0);
				Console.WriteLine("Myth session closed; dev host remains ready.");
			}
		}
		finally
		{
			File.Delete(pidPath);
		}
	}

	public static bool Send(string command)
	{
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
			writer.WriteLine(command);
			response = reader.ReadLine();
			return response != null;
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

	static async Task Serve()
	{
		while (true)
		{
			await using var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1,
				PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
			await server.WaitForConnectionAsync();
			using var reader = new StreamReader(server, Encoding.UTF8, leaveOpen: true);
			using var writer = new StreamWriter(server, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };
			var command = await reader.ReadLineAsync();
			if (command == null)
				continue;
			await writer.WriteLineAsync(command switch
			{
				"run" when Interlocked.CompareExchange(ref sessionState, 1, 0) == 0 => QueueRun(),
				"run" => "Myth is already running.",
				"status" => Volatile.Read(ref sessionState) switch
				{
					0 => "Myth dev host is ready.",
					1 => "Myth is starting.",
					_ => "Myth is running.",
				},
				_ => await OnGameThread(command),
			});
		}
	}

	static string QueueRun()
	{
		runRequested.Set();
		return "Myth launch requested.";
	}

	// Hand the command to the running game and wait for its single-line answer.
	static async Task<string> OnGameThread(string command)
	{
		if (Volatile.Read(ref sessionState) != 2)
			return "error: no Myth session is running.";

		var reply = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
		pending.Enqueue((command, reply));
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
			try { item.Reply.TrySetResult(manager.RunDevCommand(item.Command)); }
			catch (Exception e) { item.Reply.TrySetResult($"error: {e.Message}"); }
		}
	}
}
#endif
