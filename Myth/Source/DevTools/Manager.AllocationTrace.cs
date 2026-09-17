#if KIT_DIAGNOSTICS && !BROWSER
using System.Diagnostics;

namespace Myth;

public partial class Manager
{
	static bool AllocationTraceRevealInFinder => false;

	AllocationTrace? allocationTrace;
	string? allocationTracePath;
	string? allocationTraceReportPath;
	bool allocationTraceStopping;

	void ToggleAllocationTrace()
	{
		if (allocationTrace == null)
			StartAllocationTrace();
		else if (!allocationTraceStopping)
			StopAllocationTrace();
	}

	void StartAllocationTrace()
	{
		if (!ShortTempEnabled())
		{
			game?.DisplayMessage("Restart Myth with TMPDIR=/tmp for allocation tracing");
			Console.Error.WriteLine("Allocation tracing needs Myth launched with TMPDIR=/tmp TMP=/tmp TEMP=/tmp so dotnet diagnostics sockets fit macOS path limits.");
			return;
		}

		AllocationTrace.Warmup();

		var outputDir = Path.GetFullPath("profile-output");
		Directory.CreateDirectory(outputDir);
		var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
		allocationTracePath = Path.Join(outputDir, $"MythLive_{stamp}.nettrace");
		allocationTraceReportPath = Path.ChangeExtension(allocationTracePath, ".memory.txt");

		try
		{
			allocationTrace = AllocationTrace.Start(allocationTracePath);
			game?.DisplayMessage("Allocation trace started");
			Console.WriteLine($"Allocation trace started: {allocationTracePath}");
		}
		catch (Exception e)
		{
			allocationTrace = null;
			allocationTracePath = null;
			allocationTraceReportPath = null;
			game?.DisplayMessage("Allocation trace failed to start");
			Console.Error.WriteLine($"Allocation trace failed to start: {e}");
		}
	}

	void StopAllocationTrace()
	{
		var trace = allocationTrace;
		var tracePath = allocationTracePath;
		var reportPath = allocationTraceReportPath;
		if (trace == null || tracePath == null || reportPath == null)
			return;

		allocationTraceStopping = true;
		game?.DisplayMessage("Stopping allocation trace...");
		Task.Run(() => StopAllocationTrace(trace, tracePath, reportPath));
	}

	void StopAllocationTrace(AllocationTrace trace, string tracePath, string reportPath)
	{
		try
		{
			try
			{
				if (!trace.Stop())
					Console.Error.WriteLine("Allocation trace stream did not finish within 10 seconds.");
			}
			catch (Exception e)
			{
				Console.Error.WriteLine($"Allocation trace stop warning: {e}");
			}

			if (File.Exists(tracePath))
			{
				WriteAllocationTraceReport(tracePath, reportPath);
				OpenAllocationTraceReport(reportPath);
				Console.WriteLine($"Allocation trace report: {reportPath}");
			}
			else
			{
				Console.Error.WriteLine($"Allocation trace file was not created: {tracePath}");
			}
		}
		catch (Exception e)
		{
			Console.Error.WriteLine($"Allocation trace stop failed: {e}");
		}
		finally
		{
			trace.Dispose();
			allocationTrace = null;
			allocationTracePath = null;
			allocationTraceReportPath = null;
			allocationTraceStopping = false;
		}
	}

	static void WriteAllocationTraceReport(string tracePath, string reportPath)
	{
		var localProfilerDll = "/Users/kev/src/Asynkron.Profiler/src/ProfileTool/bin/Debug/net10.0/ProfileTool.dll";
		var localProfiler = "/Users/kev/src/Asynkron.Profiler/src/ProfileTool/ProfileTool.csproj";
		var useLocalDll = File.Exists(localProfilerDll);
		var useLocalProject = !useLocalDll && File.Exists(localProfiler);
		var info = useLocalDll || useLocalProject ? new ProcessStartInfo("dotnet") : new ProcessStartInfo("asynkron-profiler");
		info.UseShellExecute = false;
		info.RedirectStandardOutput = true;
		info.RedirectStandardError = true;
		SetShortTemp(info);
		if (useLocalDll)
		{
			info.ArgumentList.Add(localProfilerDll);
		}
		else if (useLocalProject)
		{
			info.ArgumentList.Add("run");
			info.ArgumentList.Add("--no-restore");
			info.ArgumentList.Add("--project");
			info.ArgumentList.Add(localProfiler);
			info.ArgumentList.Add("--");
		}
		info.ArgumentList.Add("--memory");
		info.ArgumentList.Add("--input");
		info.ArgumentList.Add(tracePath);

		using var process = Process.Start(info) ?? throw new InvalidOperationException("Could not start asynkron-profiler.");
		var output = process.StandardOutput.ReadToEnd();
		var error = process.StandardError.ReadToEnd();
		process.WaitForExit();

		File.WriteAllText(reportPath, output);
		if (process.ExitCode != 0 || !string.IsNullOrWhiteSpace(error))
			File.AppendAllText(reportPath, $"\n\nSTDERR\n{error}");
	}

	static void OpenAllocationTraceReport(string reportPath)
	{
		if (AllocationTraceRevealInFinder)
		{
			RevealInFinder(reportPath);
			return;
		}

#if EDITOR
		ZedLauncher.OpenFile(reportPath);
#else
		Console.WriteLine($"Allocation trace report written to {reportPath}");
#endif
	}

	static void SetShortTemp(ProcessStartInfo info)
	{
		info.Environment["TMPDIR"] = "/tmp";
		info.Environment["TMP"] = "/tmp";
		info.Environment["TEMP"] = "/tmp";
	}

	static bool ShortTempEnabled()
		=> Environment.GetEnvironmentVariable("TMPDIR") == "/tmp";
}
#endif
