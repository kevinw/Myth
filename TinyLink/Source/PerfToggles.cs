namespace TinyLink;

public static class PerfToggles
{
	public static bool Lighting = true;
	public static bool DitherAndTileField = true;
	public static bool NormalBuffer = true;
	public static bool Emissive = true;
	public static bool Glitch = true;
	public static bool Crt = true;

	public static string Summary =>
		$"lighting={State(Lighting)} dither={State(DitherAndTileField)} normal={State(NormalBuffer)} " +
		$"emissive={State(Emissive)} glitch={State(Glitch)} crt={State(Crt)}";

	public static void Log(string name, bool enabled)
		=> Console.WriteLine($"Perf: {name}={State(enabled)} ({Summary})");

	public static void LogPostEffects()
		=> Console.WriteLine($"Perf: post={State(Glitch || Crt)} ({Summary})");

	private static string State(bool enabled)
		=> enabled ? "on" : "off";
}
