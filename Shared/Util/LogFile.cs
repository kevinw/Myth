using System.Text;

namespace Myth;

// Mirrors Console.Out/Error to a log file under the user's app-data dir so shipped WinExe
// (console-less) Windows builds still leave a diagnosable log. It tees rather than replaces,
// so a terminal-launched run keeps printing to the inherited console as well.
public static class LogFile
{
	public static void Install(string applicationName)
	{
		try
		{
			var dir = Path.Combine(
				Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
				applicationName, "logs");
			Directory.CreateDirectory(dir);
			var path = Path.Combine(dir, "log.txt");

			// Keep the previous run's log for comparison; ignore failures (e.g. file locked).
			try
			{
				if (File.Exists(path))
					File.Copy(path, Path.Combine(dir, "log.prev.txt"), overwrite: true);
			}
			catch { }

			var file = new StreamWriter(path, append: false, new UTF8Encoding(false)) { AutoFlush = true };
			Console.SetOut(new TeeTextWriter(Console.Out, file));
			Console.SetError(new TeeTextWriter(Console.Error, file));
			Console.WriteLine($"[Log] {DateTime.Now:yyyy-MM-dd HH:mm:ss} — {path}");
		}
		catch (Exception e)
		{
			// Logging setup must never take down startup.
			Console.Error.WriteLine($"[Log] failed to install file log: {e.Message}");
		}
	}
}

// Writes every output to two underlying writers. Console.SetOut already wraps this in a
// synchronized writer, so it needs no locking of its own.
sealed class TeeTextWriter(TextWriter primary, TextWriter secondary) : TextWriter
{
	public override Encoding Encoding => primary.Encoding;

	public override void Write(char value) { primary.Write(value); secondary.Write(value); }
	public override void Write(string? value) { primary.Write(value); secondary.Write(value); }
	public override void Write(char[] buffer, int index, int count)
	{
		primary.Write(buffer, index, count);
		secondary.Write(buffer, index, count);
	}
	public override void WriteLine(string? value) { primary.WriteLine(value); secondary.WriteLine(value); }
	public override void Flush() { primary.Flush(); secondary.Flush(); }
}
