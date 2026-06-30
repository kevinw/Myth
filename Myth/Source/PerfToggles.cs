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
		BurrowParticles = false;
		BudAIEnabled = false;
	}

	public static void RevealOne(Game game, bool reveal)
	{
		//
		// TODO: do a bit of metaprogramming + field attributes above to avoid hardcoding here...
		//
		Func<string>[] toggles = [
			() => {
				if (HatPhysics != reveal) {
					HatPhysics = reveal;
					return "HatPhysics";
				}
				return "";
			},
			() => {
				if (Crt != reveal) {
					Crt = reveal;
					return "Crt";
				}
				return "";
			},
			() => {
				if (Reflections != reveal) {
					Reflections = reveal;
					return "Reflections";
				}
				return "";
			},
			() => {
				if (Lighting != reveal) {
					Lighting = reveal;
					return "Lighting";
				}
				return "";
			},
			() => {
				if (Emissive != reveal) {
					Emissive = reveal;
					return "Emissive";
				}
				return "";
			},
			() => {
				if (LightingNoise != reveal) {
					LightingNoise = reveal;
					return "LightingNoise";
				}
				return "";
			},
			() => {
				if (NormalBuffer != reveal) {
					NormalBuffer = reveal;
					return "NormalBuffer";
				}
				return "";
			},
			() => {
				if (AmbientOcclusion != reveal) {
					AmbientOcclusion = reveal;
					return "AmbientOcclusion";
				}
				return "";
			},
			() => {
				if (reveal) {
					if (game.Manager.LightingDitherStrength != Manager.LightingDitherStrengthDefault) {
						game.Manager.LightingDitherStrength = reveal ? 0.155f : Manager.LightingDitherStrengthDefault;
						return "LightingDitherStrength";
					}
				} else {
					if (game.Manager.LightingDitherStrength != 0) {
						game.Manager.LightingDitherStrength = 0;
						return "LightingDitherStrength";
					}
				}
				return "";
			},
			() => {
				if (DitherAndTileField != reveal) {
					DitherAndTileField = reveal;
					return "DitherAndTileField";
				}
				return "";
			},



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
