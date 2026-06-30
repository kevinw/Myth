namespace Myth;

public static class PerfToggles
{
	public static bool HeadSlide = false;
	public static bool HatPhysics = true;
	public static bool Lighting = true;
	public static bool DitherAndTileField = true;
	public static bool NormalBuffer = true;
	public static bool Emissive = true;
	public static bool Glitch = true;
	public static bool Crt = true;
	public static bool Reflections = true;
	public static bool LightingNoise = true;
	public static bool AmbientOcclusion = true;
	public static bool SoftShadows = true;
	public static bool Particles = true;
	public static bool BurrowParticles = true;
	public static bool BudAIEnabled = true;

	public static void UseSimpleDefaults() // for launching the game without any fancy graphics; useful for testing other sprite based visuals etc
	{
		HatPhysics = false;
		Lighting = false;
		DitherAndTileField = false;
		NormalBuffer = false;
		Emissive = false;
		Glitch = false;
		Crt = false;
		Reflections = false;
		LightingNoise = false;
		AmbientOcclusion = false;
		SoftShadows = false;
		BudAIEnabled = false;
	}

	public static void RevealOne(Game game, bool reveal)
	{
		string Set(ref bool value, string name)
		{
			if (value == reveal)
				return "";

			value = reveal;
			return name;
		}

		//
		// TODO: do a bit of metaprogramming + field attributes above to avoid hardcoding here...
		//
		Func<string>[] toggles = [
			() => Set(ref HatPhysics, nameof(HatPhysics)),
			() => Set(ref Crt, nameof(Crt)),
			() => Set(ref Reflections, nameof(Reflections)),
			() => Set(ref Lighting, nameof(Lighting)),
			() => Set(ref Emissive, nameof(Emissive)),
			() => Set(ref LightingNoise, nameof(LightingNoise)),
			() => Set(ref NormalBuffer, nameof(NormalBuffer)),
			() => Set(ref AmbientOcclusion, nameof(AmbientOcclusion)),
			() => {
				if (reveal) {
					if (game.Manager.LightingDitherStrength != Manager.LightingDitherStrengthDefault) {
						game.Manager.LightingDitherStrength = Manager.LightingDitherStrengthDefault;
						return nameof(game.Manager.LightingDitherStrength);
					}
				} else {
					if (game.Manager.LightingDitherStrength != 0) {
						game.Manager.LightingDitherStrength = 0;
						return nameof(game.Manager.LightingDitherStrength);
					}
				}
				return "";
			},
			() => Set(ref DitherAndTileField, nameof(DitherAndTileField)),
		];
		if (!reveal) toggles = [.. toggles.Reverse()];
		foreach (var fn in toggles)
		{
			var result = fn();
			if (!string.IsNullOrEmpty(result))
			{
				game.DisplayMessage($"{result}: {reveal}");
				return;
			}
		}
	}


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
