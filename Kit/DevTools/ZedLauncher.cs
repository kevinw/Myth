#if EDITOR || KIT_DIAGNOSTICS
using System.Diagnostics;

namespace Myth;

internal static class ZedLauncher
{
	public static void OpenFile(string filename)
		=> OpenFile(filename, filename);

	public static void OpenFile(string filename, int line, int column = 1)
		=> OpenFile(filename, $"{filename}:{line}:{column}");

	static void OpenFile(string filename, string target)
	{
		try
		{
			Process.Start(CreateStartInfo("zed", "-a", target));
		}
		catch (Exception e) when (OperatingSystem.IsMacOS())
		{
			try
			{
				Process.Start(CreateStartInfo("/usr/bin/open", "-a", "Zed", filename));
			}
			catch (Exception fallback)
			{
				Console.Error.WriteLine($"Failed to open '{filename}' in Zed: {e.Message}; fallback failed: {fallback.Message}");
			}
		}
		catch (Exception e)
		{
			Console.Error.WriteLine($"Failed to open '{filename}' in Zed: {e.Message}");
		}
	}

	static ProcessStartInfo CreateStartInfo(string executable, params string[] arguments)
	{
		var startInfo = new ProcessStartInfo(executable) { UseShellExecute = false };
		foreach (var argument in arguments)
			startInfo.ArgumentList.Add(argument);
		return startInfo;
	}
}
#endif
