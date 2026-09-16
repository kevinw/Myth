using System.Globalization;

namespace Myth;

public struct StartArgs
{
	public bool dumpAtlases;
	public bool gym;
	public bool audio = true;
	public bool headless;
	public bool showControllerInputs;
	public bool checkRooms;
	public bool unthrottled;
	public bool gymExitAfterAll;
	public bool traceNav;
	public bool traceNavFrames;
	public bool simple;
	public bool bench;
	public bool devHost;
	public string? devCommand;
	public bool? fullscreen;
	public bool? vsync;
	public int framesInFlight = 2;
	// Force the 4:3 portrait-style presentation on desktop for testing (--aspect-four-three):
	// pillarbox the 16:9 game into a centered 4:3 rect and crop the sides (tap/hold to un-crop).
	public bool aspectFourThree;
	public bool play; // if true, play immediately on startup even in desktop/editor-capable environments
	// Skip the boot shader/pipeline warmup + loading screen (--no-warmup). For A/B testing the
	// warmup against a cold driver shader cache: with this set, pipelines compile lazily on first
	// room entry (the original hitch) instead of up front. See tools/windows/clear-system-shader-caches.mjs.
	public bool noWarmup;
	public bool noNav; // skip Bud spawn + navgraph (--no-nav); isolates per-frame allocs from nav work
	public bool buildAssetPack;
	public bool writeAllRooms;
	// Render every room into the world map image the map overlay samples, then exit
	// (--bake-world-map). Needs a real graphics device, so it runs windowed rather than
	// through the headless --build-asset-pack.
	public WorldMap.Mode? bakeWorldMap = null;
	// Boot straight to the title screen even in editor builds (which normally open the editor),
	// so the attract demo can be tested without a shipped build (--title).
	public bool title;
	public bool useAssetPack;
	public string? assetPackPath;
	// Load bundled content from this zip instead of the Assets folder (--content-zip).
	public string? contentZipPath;
	// Write a Content.zip of the runtime Assets slice to this path and exit (--build-content-zip).
	public string? buildContentZipPath;
	public string? contentTarget;
	public string? contentWorld;
	// Exit after this many completed gameplay frames (excluding loading, editing, and pauses).
	public int maxFrames = int.MaxValue;
	public int allocWarmupFrames;
	public int allocMeasureFrames;
	// Maximum source animation frames per sprite compared by --bench (minimum 2).
	public int benchSourceFrames = int.MaxValue;

	// Exit after this many game seconds (for automated/profiling runs).
	public float maxTimeSeconds = float.PositiveInfinity;
	// Start play immediately in this room (by Room.Id) on startup.
	public string? playRoomId;
	public string? worldId;
	public string? traceJsonPath;
	public string? traceRoomId;
	public string? traceGoalId;
	public string[] gymGoalIds = [];
	public int? traceEdgeFrom;
	public int? traceEdgeTo;

	// Automated screenshot capture (desktop). When screenshotPath is set the
	// game plays immediately, captures Game.Screen at screenshotFrame and every
	// screenshotInterval frames after, screenshotCount times, then exits.
	public string? screenshotPath;
	public int screenshotFrame = 60;
	public int screenshotInterval = 60;
	public int screenshotCount = 1;

	public StartArgs(string[] args)
	{
		maxFrames = int.MaxValue;
		benchSourceFrames = int.MaxValue;
		var gymCli = false;
		var benchSourceFramesProvided = false;
		for (int i = 0; i < args.Length; i++)
		{
			var option = args[i];
			switch (option)
			{
				case "--dump-atlases": dumpAtlases = true; break;
				case "--gym": gym = true; break;
				case "--gym-cli": gymCli = true; break;
				case "--headless": headless = true; break;
				case "--show-controller-inputs": showControllerInputs = true; break;
				case "--check-rooms": checkRooms = true; break;
				case "--unthrottled": unthrottled = true; break;
				case "--gym-exit-after-all": gymExitAfterAll = true; break;
				case "--trace-nav": traceNav = true; break;
				case "--trace-nav-frames": traceNavFrames = true; break;
				case "--simple": simple = true; break;
				case "--bench": bench = true; break;
				case "--dev-host": devHost = true; break;
				case "--dev-command": devCommand = ReadValue(args, ref i, option); break;
				case "--fullscreen": fullscreen = true; break;
				case "--vsync": vsync = ReadOnOff(args, ref i, option); break;
				case "--frames-in-flight":
					framesInFlight = ReadInt(args, ref i, option, minimum: 1);
					if (framesInFlight > 3)
						throw Invalid(option, "expected 1, 2, or 3");
					break;
				case "--aspect-four-three": aspectFourThree = true; break;
				case "--no-warmup": noWarmup = true; break;
			case "--no-nav": noNav = true; break;
				case "--play": play = true; break;
				case "--title": title = true; break;
				case "--bake-world-map": bakeWorldMap = WorldMap.Mode.LowResQoi; break;
				case "--bake-hires-world-map": bakeWorldMap = WorldMap.Mode.HighResPng; break;
				case "--max-frames":
					maxFrames = ReadInt(args, ref i, option, minimum: 1);
					break;
				case "--alloc-warmup-frames":
					allocWarmupFrames = ReadInt(args, ref i, option, minimum: 0);
					break;
				case "--alloc-measure-frames":
					allocMeasureFrames = ReadInt(args, ref i, option, minimum: 1);
					break;
				case "--bench-source-frames":
					benchSourceFrames = ReadInt(args, ref i, option, minimum: 2);
					benchSourceFramesProvided = true;
					break;
				case "--screenshot": screenshotPath = ReadValue(args, ref i, option); break;
				case "--screenshot-frame": screenshotFrame = ReadInt(args, ref i, option, minimum: 0); break;
				case "--screenshot-interval": screenshotInterval = ReadInt(args, ref i, option, minimum: 1); break;
				case "--screenshot-count": screenshotCount = ReadInt(args, ref i, option, minimum: 1); break;
				case "--content-zip": contentZipPath = ReadValue(args, ref i, option); break;
				case "--build-content-zip": buildContentZipPath = ReadValue(args, ref i, option); break;
				case "--write-all-rooms": writeAllRooms = true; break;
				case "--content-target": contentTarget = ReadValue(args, ref i, option); break;
				case "--content-world": contentWorld = ReadValue(args, ref i, option); break;
				case "--max-time": maxTimeSeconds = ReadFloat(args, ref i, option, minimum: 0); break;
				case "--play-room":
				case "--room": playRoomId = ReadValue(args, ref i, option); break;
				case "--world": worldId = ReadValue(args, ref i, option); break;
				case "--trace-json": traceJsonPath = ReadValue(args, ref i, option); break;
				case "--trace-room": traceRoomId = ReadValue(args, ref i, option); break;
				case "--trace-goal": traceGoalId = ReadValue(args, ref i, option); break;
				case "--gym-goal":
				{
					var goals = ParseList(ReadValue(args, ref i, option));
					if (goals.Length == 0)
						throw Invalid(option, "expected one or more comma-separated goal IDs");
					gymGoalIds = [..gymGoalIds, ..goals];
					break;
				}
				case "--trace-edge":
					if (!TryParseTraceEdge(ReadValue(args, ref i, option), out traceEdgeFrom, out traceEdgeTo))
						throw Invalid(option, "expected FROM:TO or FROM->TO with non-negative integer node IDs");
					break;

				// The visual pack path is optional; omitting it uses Assets/Packed/Visuals.
				case "--build-asset-pack":
					buildAssetPack = true;
					assetPackPath = ReadOptionalValue(args, ref i);
					break;
				case "--asset-pack":
					useAssetPack = true;
					assetPackPath = ReadOptionalValue(args, ref i);
					break;

				default:
					throw Invalid(option, "unknown argument");
			}
		}

		gym |= gymCli;
		headless |= gymCli;
		if (checkRooms)
		{
			audio = false;
		}
		if (bakeWorldMap != null)
		{
			if (worldId == null)
				throw Invalid("--bake-world-map", "requires --world <world>");
			audio = false;
		}
		if (writeAllRooms)
			audio = false;
		unthrottled |= gymCli;
		gymExitAfterAll |= gymCli;
		traceNav |= traceJsonPath != null;
		play |= screenshotPath != null || playRoomId != null;

		if (benchSourceFramesProvided && !bench)
			throw Invalid("--bench-source-frames", "only valid with --bench");
		if (buildContentZipPath != null && string.IsNullOrWhiteSpace(contentTarget))
			throw Invalid("--build-content-zip", "requires --content-target mac, ios, windows, linux, or browser");
	}

	static string[] ParseList(string value)
		=> value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

	static bool TryParseTraceEdge(string value, out int? from, out int? to)
	{
		from = null;
		to = null;
		var parts = value.Replace("->", ":", StringComparison.Ordinal).Split(':', StringSplitOptions.TrimEntries);
		if (parts.Length != 2 || !int.TryParse(parts[0], out var parsedFrom) || parsedFrom < 0 ||
			!int.TryParse(parts[1], out var parsedTo) || parsedTo < 0)
			return false;
		from = parsedFrom;
		to = parsedTo;
		return true;
	}

	static string ReadValue(string[] args, ref int index, string option)
	{
		if (index + 1 >= args.Length || IsOption(args[index + 1]))
			throw Invalid(option, "missing value");
		var value = args[++index];
		if (string.IsNullOrWhiteSpace(value))
			throw Invalid(option, "value cannot be empty");
		return value;
	}

	static string? ReadOptionalValue(string[] args, ref int index)
		=> index + 1 < args.Length && !IsOption(args[index + 1]) ? args[++index] : null;

	static int ReadInt(string[] args, ref int index, string option, int minimum)
	{
		var text = ReadValue(args, ref index, option);
		if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) || value < minimum)
			throw Invalid(option, $"expected an integer greater than or equal to {minimum}, got '{text}'");
		return value;
	}

	static float ReadFloat(string[] args, ref int index, string option, float minimum)
	{
		var text = ReadValue(args, ref index, option);
		if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !float.IsFinite(value) || value < minimum)
			throw Invalid(option, $"expected a finite number greater than or equal to {minimum}, got '{text}'");
		return value;
	}

	static bool ReadOnOff(string[] args, ref int index, string option)
	{
		var value = ReadValue(args, ref index, option);
		if (string.Equals(value, "on", StringComparison.OrdinalIgnoreCase))
			return true;
		if (string.Equals(value, "off", StringComparison.OrdinalIgnoreCase))
			return false;
		throw Invalid(option, $"expected on or off, got '{value}'");
	}

	static bool IsOption(string value) => value.StartsWith("--", StringComparison.Ordinal);
	static ArgumentException Invalid(string option, string reason) => new($"{option}: {reason}.");
}
