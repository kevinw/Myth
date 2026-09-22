namespace Myth;

internal static class PerfToggles
{
	internal static bool HeadSlide = false;
	internal static bool HatPhysics = true;
	internal static bool Lighting = true;
	internal static bool DitherAndTileField = true;
	internal static bool NormalBuffer = true;
	internal static bool Emissive = true;
	internal static bool Glitch = true;
	internal static bool Crt = true;
	internal static bool Reflections = true;
	internal static bool LightingNoise = true;
	internal static bool AmbientOcclusion = true;
	internal static bool GlobalIllumination = true;
	internal static bool SoftShadows = true;
	internal static bool Particles = true;
	internal static bool BurrowParticles = true;
	internal static bool BudAIEnabled = true;

	internal static void UseSimpleDefaults() // for launching the game without any fancy graphics; useful for testing other sprite based visuals etc
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
		GlobalIllumination = false;
		SoftShadows = false;
		BudAIEnabled = false;
	}

	internal static void RevealOne(Game game, bool reveal)
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
			() => Set(ref GlobalIllumination, nameof(GlobalIllumination)),
			() => {
				if (reveal) {
					if (game.Manager.Visuals.DitherStrength != 1.0f) {
						game.Manager.Visuals.DitherStrength = 1.0f;
						return nameof(game.Manager.Visuals.DitherStrength);
					}
				} else {
					if (game.Manager.Visuals.DitherStrength != 0f) {
						game.Manager.Visuals.DitherStrength = 0f;
						return nameof(game.Manager.Visuals.DitherStrength);
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


	internal static string Summary =>
		$"lighting={State(Lighting)} dither={State(DitherAndTileField)} normal={State(NormalBuffer)} " +
		$"emissive={State(Emissive)} glitch={State(Glitch)} crt={State(Crt)}";

	internal static void Log(string name, bool enabled)
		=> Console.WriteLine($"Perf: {name}={State(enabled)} ({Summary})");

	internal static void LogPostEffects()
		=> Console.WriteLine($"Perf: post={State(Glitch || Crt)} ({Summary})");

	private static string State(bool enabled)
		=> enabled ? "on" : "off";
}
