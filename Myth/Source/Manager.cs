using System.Diagnostics;
using System.Drawing;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Foster.Framework;
using ImGuiNET;
using Color = Foster.Framework.Color;

namespace Myth;

/// Runs the Game and, when the game is not running, displays a tile-based Level Editor (see Manager.Editor.cs)
public partial class Manager : App
{
	public readonly Audio Audio = new();
	readonly ImGuiRenderer imgui = null!;

	// Dedicated ImGui context for the game menu. Unlike `imgui` (editor/browser
	// only) this exists in every build and owns its own Scale, so the menu can lay
	// out in game-pixel units regardless of the editor's UI scale. See Manager.GameMenu.cs.
	readonly ImGuiRenderer menuImgui = null!;

	public const string DefaultMusicArea = "Silence";
	public const string BackgroundMusicAreaParameter = "Area";
	public const string RoomReverbParameter = "RoomReverb";
	public const string MusicMuffleParameter = "RoomMusicMuffle";

	const string WindowSettingsFileName = "window.json";
#if EDITOR
	const string EditorSettingsFileName = "editor-settings.json";
#endif
	readonly Point MinimumRestoredWindowSize = new(640, 360);

	const float DEFAULT_UI_SCALE = 2.5f;
	float Scale => imgui?.Scale ?? DEFAULT_UI_SCALE;
	static float ButtonSize => 34;
	static float ButtonSpacing => 2;
#if !EDITOR
	static Color highlight => 0x5fcde4;
#endif
	// Anything the game doesn't draw over: the letterbox bands around the 16:9 frame (the
	// Steam Deck's 16:10 panel), the boot warmup screen, and the title screen before a room
	// preview exists. Rooms fill the frame opaquely via DrawBackdrop, so play is unaffected.
	Color WINDOW_CLEAR_COLOR = Color.Black;

	string? selectedRoomId;
#if EDITOR
	(string RoomId, Point2 Position)? playtestPlayerPosition;
	bool preservePlaytestSaveState;
#endif
	// Dither is authored per-room (Room.LightingDither*/NormalDither*). This global scale is
	// multiplied onto each room's lighting dither strength so performance paths (PerfToggles,
	// --simple) can suppress dither without touching authored values.
	public float DitherPerfScale = 1.0f;

	// Ambient occlusion: background tiles darken beneath foreground overhangs.
	public float AmbientOcclusionStrength = 1.0f;
	public float AmbientOcclusionBlur = 8.0f;
	public Vector2 AmbientOcclusionOffset = new(0, -3);
	public float AmbientOcclusionMinVisibility = 0.35f;

	public static float SunShadowSoftness => 2.3f;
	public static float SpriteShadowStrength => 0.5f;
	public static float SpriteShadowMinLength => 4.0f;
	public static float SpriteShadowMaxLength => 28.0f;

	// Illuminated spore fog: world-space participating medium between the background and foreground.
	public static float SporeFogScale => 1.0f / 80.0f;
	public static Vector2 SporeFogDrift => new(2.0f, -0.5f);
	public static float SporeFogContrast => 1.8f;
	public static Color SporeFogTint => new(0.72f, 1.0f, 0.78f, 1.0f);
	public static float SporeFogPointStrength => 0.65f;
	public static float SporeFogSunStrength => 0.3f;

	/// The room renderer / simulation.
	internal Game? game;
	internal bool playing;
	internal event Action<Manager>? ScenarioUpdate;
	internal event Action<Manager>? ScenarioRender;
	bool playOnStartup;
	readonly bool skipStartupMenu;
	readonly bool checkRooms;
	bool startingPlay;
	// Boot loading screen: warm GPU pipelines a step at a time while showing "Loading...", then
	// call EnterInitialScreen(). See UpdateBootWarmup/RenderBootWarmup and ShaderWarmup.
	bool bootWarming;
	int warmupIndex;
	// A warm step blocks Update for seconds, freezing whatever was last presented. So we hold off
	// warming until the window has reached its final (fullscreen) size for a few frames — the
	// frozen frame must be the correct full-size one, not the tiny pre-resize first frame.
	const int BootSettleFrames = 3;
	Point2 bootLastSize = new(-1, -1);
	int bootStableFrames;
	readonly bool noWarmup;
	SaveState saveState = new();
	bool saveDirty;
	bool saveFlushScheduled;
	float saveTimer;
	static float SaveDebounceDelay => 0.75f;

	// Automated screenshot capture (see StartArgs / --screenshot).
	readonly string? screenshotPath;
	readonly int screenshotFrame;
	readonly int screenshotInterval;
	readonly int screenshotCount;
	int screenshotsTaken;
	int playingFrame;
#if IOS
	const float PlayModePinchInThreshold = 0.82f;
	float playModePinchStartDistance;
#endif
	const float PlayTransitionDuration = 0.25f;
	float playTransition;
	Rect playTransitionFrom, playTransitionTo;
	int fpsOverlayFrames;
	long fpsOverlayLastTimestamp;
	float fpsOverlayValue;

#if DEBUG
	long threadAllocatedBytes;
	long threadAllocatedBytesOverlayValue;
	TimeSpan gcTotalPauseDuration;
	long lastAllocated = -1;
#endif

#if EDITOR
	int fpsOverlayMode;
	bool showTransitionViewer = false;
#endif
	bool fpsOverlayVisible = false;

	/// Eases in/out as the Game starts/stops playing
	float gameEase = 1.0f;

	/// Sprite Batcher for general Editor visuals
	readonly Batcher batcher;
	readonly Controls controls;
	internal readonly DebugControls debugControls;
	internal InputPrompts? InputPrompts { get; private set; }

	/// Visible Space
	Window RequiredWindow => Window ?? throw new InvalidOperationException("Window is not available in headless mode.");

	Rect View
	{
		get
		{
			var window = RequiredWindow;
			return new Rect(0, 0, window.WidthInPixels / Scale, window.HeightInPixels / Scale);
		}
	}

	/// Play Workspace when the Game is Open
	Rect GameOpenRect => View;


#if !EDITOR
	Rect GameViewRect => GameOpenRect;
#endif

	public Vector2 GameMouseWorld(Game gameInstance)
	{
		var viewport = (RectInt)(GameViewRect * Scale);
		return gameInstance.WindowToWorld(Input.Mouse.Position, viewport);
	}

	/// Mouse Cursor relative to Scale
	Vector2 Cursor => Input.Mouse.Position / Scale;

	/// Line Weight for outlines, relative to Scale
	float LineWeight => 1.0f / Scale * 4;

	/// Tries to get the Existing Room
	Room? CurrentRoom
	{
		get
		{
			if (selectedRoomId != null && Assets.GetRoom(selectedRoomId) is { } byId)
				return byId;

			return null;
		}
	}

	readonly List<string> musicAreas = [];
	readonly bool dumpAtlases;
	readonly int maxFrames;
	readonly float maxTimeSeconds;
	int completedGameFrames;
	readonly int allocWarmupFrames;
	readonly int allocMeasureFrames;
	int allocProfileFrames;
	int allocProfileMeasuredFrames;
	long allocProfileBytes;
	bool allocProfileStarted;
	bool allocProfileComplete;
	readonly string? playRoomId;
	readonly bool buildAssetPack;
	readonly bool bakeWorldMap;
	readonly bool useAssetPack;
	readonly string? assetPackPath;
	bool musicEnabled = true;
	bool alwaysOnTop;
#if !IOS && !BROWSER
	bool runInBackground;
	bool windowFocused;
	bool backgroundSuspended;
	static int BackgroundSleepMilliseconds => 50;
#endif
	internal bool PlatformNavDebugVisible { get; set; }
	readonly NavTrace? navTrace;

	public bool MusicEnabled {
		get => musicEnabled;
		set {
			musicEnabled = value;
			if (value)
				game?.MusicWasRestarted();
			else
				game?.StopMusic(true);
		}
	}
#if !IOS && !BROWSER
	void WindowFocusGained() => windowFocused = true;
	void WindowFocusLost() => windowFocused = false;

	bool UpdateBackgroundSuspension()
	{
		// Automated captures run unattended and may never take focus, so they must keep
		// stepping regardless of it -- otherwise the run suspends and never reaches its frame.
		var runsInBackground = runInBackground || screenshotPath != null || gymMode;
#if EDITOR
		runsInBackground |= editorGameRunInBackground;
#endif
		var suspend = !runsInBackground && !windowFocused;
		if (backgroundSuspended == suspend)
		{
			if (suspend)
				Thread.Sleep(BackgroundSleepMilliseconds);
			return suspend;
		}

		backgroundSuspended = suspend;
		Audio.SetPaused(suspend);
		if (suspend)
		{
			Thread.Sleep(BackgroundSleepMilliseconds);
		}
		return true;
	}
#endif
	public bool WantsVirtualGameController => playing || startingPlay;
	public bool AudioEnabled = true;
	public bool UserQuitRequested { get; private set; }
	public event Action<bool>? VirtualGameControllerVisibilityChanged;
	public const string BackgroundMusicEventPath = "event:/background_music";

	public static Manager Create(StartArgs args) {
		var settings = GetWindowSettingsFromJSON();
		args.fullscreen ??= settings?.Fullscreen;
		return new(args);
	}

	public Manager(StartArgs args) : base(CreateAppConfig(args.gym, args.headless, args.unthrottled,
		offscreen: args.bakeWorldMap, fullscreen: args.fullscreen))
	{
#if EDITOR
		particleEditor = new(undo);
#if !IOS && !BROWSER
		proceduralSoundEditor = new(Audio);
#endif
#endif
		AudioEnabled = args.audio;
		dumpAtlases = args.dumpAtlases;
		gymMode = args.gym;
		headless = args.headless;
		checkRooms = args.checkRooms;
		unthrottled = args.unthrottled;
		gym = new()
		{
			exitAfterAll = args.gymExitAfterAll
		};
		foreach (var goalId in args.gymGoalIds)
			gym.goalIds.Add(goalId);
		buildAssetPack = args.buildAssetPack;
		bakeWorldMap = args.bakeWorldMap;
		useAssetPack = args.useAssetPack;
		assetPackPath = args.assetPackPath;
		Assets.ConfigureContentZip(args.contentZipPath);
		Inbetweens.BenchMode = args.bench;
		Inbetweens.BenchSourceFrames = args.benchSourceFrames;
		maxFrames = args.maxFrames;
		allocWarmupFrames = args.allocWarmupFrames;
		allocMeasureFrames = args.allocMeasureFrames;
		maxTimeSeconds = args.maxTimeSeconds;
		playRoomId = args.playRoomId;
		playOnStartup = args.play;
		noWarmup = args.noWarmup;
		skipStartupMenu = args.play || args.gym || args.headless;
		screenshotPath = args.screenshotPath;
		screenshotFrame = args.screenshotFrame;
		screenshotInterval = args.screenshotInterval;
		screenshotCount = args.screenshotCount;
		navTrace = NavTrace.Create(new(
			args.traceNav,
			args.traceJsonPath,
			args.traceRoomId,
			args.traceGoalId,
			args.traceEdgeFrom,
			args.traceEdgeTo,
			args.traceNavFrames
		));
		if (args.simple)
		{
			PerfToggles.UseSimpleDefaults();
			DitherPerfScale = 0;
		}

		batcher = new(GraphicsDevice);
		controls = new(Input);
		debugControls = new(Input);
		menuImgui = new(this, enableGamepadNav: true, frameRounding: 0);
		OnExitRequested = RequestQuit;
		if (Window != null)
		{
			Window.OnCloseRequested = RequestQuit;
#if !IOS && !BROWSER
			Window.OnFocusGain += WindowFocusGained;
			Window.OnFocusLost += WindowFocusLost;
#endif
		}
#if EDITOR
		var editorFontPath = Path.Join(AppContext.BaseDirectory, "EditorAssets", "Fonts", "InterVariable.ttf");
		//var editorFontPath = Path.Join(AppContext.BaseDirectory, "EditorAssets", "Fonts", "InterTightVariable.ttf");
		var editorFontFace = 0;

		imgui = new(this, editorFontPath, editorFontFace,
			iniPath: Path.Join(UserPath, "editor-layout.ini"),
			mergedFontPath: Path.Join(AppContext.BaseDirectory, "EditorAssets", "Fonts", "fa-solid-900.ttf"),
			mergedFontGlyphRanges: EditorIcons.GlyphRanges,
			scale: DEFAULT_UI_SCALE);
#elif BROWSER
		imgui = new(this, scale: 1);
#endif
	}

	public const string APPLICATION_NAME = "Human Questions";

	internal void RequestQuit()
	{
		UserQuitRequested = true;
		Exit();
	}

	internal static bool DOTNET_WATCH = Environment.GetEnvironmentVariable("DOTNET_WATCH") == "1";

	static AppConfig CreateAppConfig(bool gym = false, bool headless = false, bool unthrottled = false,
		bool offscreen = false, bool? fullscreen = null)
	{
		const string windowTitle = APPLICATION_NAME;
		Point2 windowSize = new(1280, 720);
		GraphicsDriver graphicsDriver = GraphicsDriver.None; // no preference
		bool resizable = true;
		var updateMode = UpdateMode.UnlockedStep();
		if (fullscreen == null) {
#if DEBUG
			fullscreen = false;
#elif !BROWSER
			fullscreen = true;
#endif
		}
		AppFlags appFlags = AppFlags.None;
#if BROWSER
		graphicsDriver = GraphicsDriver.WebGPU;
#elif IOS
		graphicsDriver = GraphicsDriver.Metal;
		fullscreen = true;
		resizable = false;
#endif
		if (headless)
		{
			graphicsDriver = GraphicsDriver.Headless;
			windowSize = new(1, 1);
			resizable = false;
			updateMode = UpdateMode.FixedStep(60);
			appFlags |= AppFlags.Headless;
		}
		else if (offscreen)
		{
			windowSize = new(1, 1);
			fullscreen = false;
			resizable = false;
			appFlags |= AppFlags.Offscreen;
		}
		if (unthrottled)
		{
			updateMode = UpdateMode.UnthrottledStep(60);
		}

		// Opt-in GPU validation: set MYTH_GPU_DEBUG=1 to enable SDL_gpu's debug layer
		// (pairs with Metal validation env vars to catch buffer overruns / hazards).
		var gpuDebug = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("MYTH_GPU_DEBUG"));
		if (gpuDebug)
		{
			appFlags |= AppFlags.GraphicsDebugging;
		}

		return new AppConfig(
			ApplicationName: APPLICATION_NAME,
			WindowTitle: windowTitle,
			Width: windowSize.X,
			Height: windowSize.Y,
			Fullscreen: fullscreen ?? false,
			Resizable: resizable,
			UpdateMode: updateMode,
			PreferredGraphicsDriver: graphicsDriver,
			Flags: appFlags
		);
	}

	bool hasStartedGame;

	protected override void Startup()
	{
#if !IOS && !BROWSER
		windowFocused = Window?.Focused == true;
#endif
#if !BROWSER
		LoadWindowSettings();
#endif

		GraphicsDevice.VSync = true; // Make this an option? SDL3 crashes my Mac when I fullscreen the window... @kev 2026

#if !BROWSER
		Assets.ConfigureUserRoomsPath(Path.Join(UserPath, "Rooms"));
#endif
		if (buildAssetPack)
		{
			Assets.BuildVisualAssetPack(GraphicsDevice, assetPackPath, dumpAtlases);
			Exit();
			return;
		}

#if BROWSER
		Assets.ConfigureVisualAssetPack(assetPackPath, true);
#else
		Assets.ConfigureVisualAssetPack(assetPackPath, useAssetPack);
#endif
		Assets.Load(GraphicsDevice, dumpAtlases);
		if (Inbetweens.BenchMode)
		{
			Console.WriteLine("[Bench] complete — exiting");
			Exit();
			return;
		}
		InputPrompts = new(GraphicsDevice, Input);
#if EDITOR
		EditorIcons.LoadPaletteFont(GraphicsDevice);
		saveState = preservePlaytestSaveState ? SaveStateStore.Load(this) : new();
#else
		saveState = SaveStateStore.Load(this);
#endif
		if (playRoomId != null)
			selectedRoomId = playRoomId;
		else if (saveState.LastGrounded is { } respawn && Assets.GetRoom(respawn.RoomId) != null)
			selectedRoomId = respawn.RoomId;
		ResolveSelectedRoom();
		Factory.RegisterTypes();
		Assets.RebuildRoomGeometry();
		if (checkRooms)
		{
			CheckRooms();
			Exit();
			return;
		}
		SpriteSdf.Generate(GraphicsDevice); // build the player + bud silhouette SDF atlas
		if (AudioEnabled)
			LoadAudioBanks();

#if EDITOR
		if (bakeWorldMap)
		{
			BakeWorldMap();
			Exit();
			return;
		}
#endif

#if !EDITOR && !BROWSER
		// Normal windowed play: cover GPU pipeline warmup with a loading screen so the first
		// neural room doesn't hitch. Gym/headless/screenshot runs have no loading UI, so warm
		// inline and enter immediately. EnterInitialScreen() runs once warmup finishes — see
		// UpdateBootWarmup(). --no-warmup skips all of it so pipelines compile lazily on first
		// room entry (baseline for A/B testing against a cold driver cache).
		if (noWarmup)
		{
			EnterInitialScreen();
		}
		else if (!headless && !gymMode && screenshotPath == null)
		{
			bootWarming = true;
			warmupIndex = 0;
		}
		else
		{
			if (!headless)
				ShaderWarmup.WarmAll(GraphicsDevice);
			EnterInitialScreen();
		}
#else
		EnterInitialScreen();
#endif

#if EDITOR

// Watch development assets
#if DEBUG && !IOS && !BROWSER
		StartAssetWatcher();
#endif

		if (!playOnStartup)
			SyncEditorGame();
		if (pendingEditorDockLayoutReset)
		{
			pendingEditorDockLayoutReset = false;
			ResetEditorToolWindowLayout();
		}
#endif
	}

	void CheckRooms()
	{
		var timer = Stopwatch.StartNew();
		var loaded = 0;
		foreach (var room in Assets.RoomsById.Values.OrderBy(static room => room.Id, StringComparer.Ordinal))
		{
			try
			{
				using var game = new Game(this, room, audioEventsEnabled: false, spawnBud: false,
					enableParticles: false, loadGoals: true, save: new());
			}
			catch (Exception exception)
			{
				throw new InvalidOperationException($"Failed to load room '{room.Id}'.", exception);
			}
			loaded++;
		}
		Console.WriteLine($"[CheckRooms] Loaded {loaded}/{Assets.RoomsById.Count} rooms in {timer.Elapsed.TotalSeconds:F2}s.");
	}

	// Enters the first on-screen state after boot/warmup: the title menu, immediate play, or a
	// gym run. Extracted from Startup so the windowed build can defer it behind a loading screen.
	void EnterInitialScreen()
	{
#if BROWSER || !EDITOR
		if (!skipStartupMenu)
			ShowTitleMenu();
		else
			playOnStartup = true; // explicit play/test runs still boot straight into play
#endif
		if (playOnStartup)
			StartGame(immediate: playRoomId != null);
#if !BROWSER
		if (unthrottled)
			Console.WriteLine("[Manager] unthrottled: running fixed 60fps steps as fast as possible (no wall-clock wait)");
#endif

		// Gym mode: auto-start with rooms that have a Goal actor
		if (gymMode)
		{
			if (!gym.CollectGymRooms(playRoomId))
			{
				Console.WriteLine("[Gym] no rooms with Goal found — exiting");
				Environment.ExitCode = 1;
				Exit();
				return;
			}

			// The looping tune mode rebuilds the graph every trial; silence the per-build
			// dump so the per-trial result lines stay readable. The one-shot --gym-cli keeps it.
			PlatformNavGraph.VerboseBuildLog = gym.exitAfterAll;

			gym.runIndex = 0;
			var run = gym.runs![gym.runIndex];
			Console.WriteLine($"[Gym] found {gym.runs.Count} goal run(s), starting {run.DisplayId}@{run.StartDisplay}");
			StartGymRun(run);
		}
	}

	// Drives the boot loading screen: once the window has settled, warm one ShaderWarmup step per
	// frame, then reveal the game.
	void UpdateBootWarmup()
	{
		if (bootStableFrames < BootSettleFrames)
			return;

		if (warmupIndex < ShaderWarmup.Steps.Length)
		{
			ShaderWarmup.Warm(ShaderWarmup.Steps[warmupIndex], GraphicsDevice);
			warmupIndex++;
			return;
		}

		bootWarming = false;
		EnterInitialScreen();
	}

	void RenderBootWarmup()
	{
		var window = RequiredWindow;
		window.Clear(WINDOW_CLEAR_COLOR);

		// The batcher draws in window-pixel space (see View: WidthInPixels/Scale). Track the size
		// so UpdateBootWarmup only blocks on a warm compile once it has stopped changing.
		var size = new Point2(window.WidthInPixels, window.HeightInPixels);
		if (size == bootLastSize)
			bootStableFrames++;
		else
		{
			bootStableFrames = 0;
			bootLastSize = size;
		}

		// SpeechFont is a crisp pixel-perfect 16px raster; scale it by an integer so it stays sharp
		// (the 8px UI Font upscaled non-integer smears). Centered in the window via justify.
		if ((Assets.SpeechFont ?? Assets.Font) is { } font)
		{
			var scale = Math.Max(1, (int)(size.Y / 360f));
			batcher.PushMatrix(Matrix3x2.CreateScale(scale));
			batcher.Text(font, "Loading...", new Vector2(size.X, size.Y) / (2f * scale), new Vector2(0.5f, 0.5f), Color.White);
			batcher.PopMatrix();
		}

		batcher.Render(window);
		batcher.Clear();
	}

	protected override void Shutdown()
	{
		ShutdownGame();
		Audio.Shutdown();
	}

	void ShutdownGame()
	{
#if !MYTHSHIP && !BROWSER && !IOS
		try { if (MythCapture.IsRecording) StopMovieRecording(reveal: false); } catch { /* best-effort finalize */ }
#endif
		FlushSaveState();
#if !BROWSER
		SaveWindowSettings();
#endif
		game?.Dispose();
		navTrace?.Dispose();
		menuImgui?.Dispose();
		InputPrompts?.Dispose();
		InputPrompts = null;
		foreach (var cursor in imguiCursors.Values)
			cursor.Dispose();
		imguiCursors.Clear();
#if EDITOR
		ClearRoomPreviewCache(deferDisposal: false);
		ClearSurfacePreviewCache(deferDisposal: false);
		surfacePreviewBatcher?.Dispose();
		DisposeDeferredRoomPreviews();
		DisposeEditorCursors();
		imgui?.Dispose();
		EditorIcons.UnloadPaletteFont();
#if DEBUG && !IOS
		DisposeAssetWatchers();
#endif
#endif

		Assets.Unload();
		SpriteSdf.Unload();
		Factory.Clear();
	}

	static string WindowSettingsPath => Path.Join(UserPathForApplicationName(APPLICATION_NAME), WindowSettingsFileName);
#if EDITOR
	static string EditorSettingsPath => Path.Join(UserPathForApplicationName(APPLICATION_NAME), EditorSettingsFileName);
#endif

#if !EDITOR
	// Editor-less builds (browser, shipped desktop): pick the room to start play in
	// without any editor selection UI. The editor variant lives in Manager.Editor.cs.
	void ResolveSelectedRoom()
	{
		if (selectedRoomId != null && Assets.GetRoom(selectedRoomId) is { } byId)
		{
			selectedRoomId = byId.Id;
			return;
		}

		var first = Assets.RoomsById["start"] ?? Assets.GetRoom(Point2.Zero) ?? Assets.RoomsById.Values.FirstOrDefault();
		selectedRoomId = first?.Id;
	}
#endif

	void LoadAudioBanks()
	{
		if (!AudioEnabled) return;
		// The FMOD project lives in Assets/Sources (authoring) but must build its banks
		// into Assets/Sounds/FMOD/Build (runtime, shipped). If banks ever appear on the
		// Sources side, the project's builtBanksOutputDirectory got reset — say so loudly
		// instead of playing stale audio.
		var misplacedBuild = Path.Join(Assets.SourcesPath, "Sounds", "FMOD", "Build");
		if (Directory.Exists(misplacedBuild))
			Console.Error.WriteLine($"Warning: FMOD banks found at '{misplacedBuild}' — the FMOD project should build to Assets/Sounds/FMOD/Build (check builtBanksOutputDirectory in FMOD Studio's preferences).");

		var fmodPath = Path.Join(Assets.AssetsPath, "Sounds", "FMOD");
		if (!Directory.Exists(fmodPath))
		{
			Console.Error.WriteLine("Sounds/FMOD not found.");
			return;
		}

		var bankRoot = AudioBankRoot(fmodPath);
		var banks = Directory
			.EnumerateFiles(bankRoot, "*.bank", SearchOption.AllDirectories)
			.OrderBy(BankLoadOrder)
			.ThenBy(Path.GetFileName)
			.ToArray();

		if (banks.Length <= 0)
		{
			Console.Error.WriteLine($"No FMOD banks found under '{Path.GetFullPath(bankRoot)}'. Build banks from the FMOD Studio project before playing events.");
			musicAreas.Clear();
			musicAreas.Add(DefaultMusicArea);
			return;
		}

		try
		{
			Audio.ReloadBanks(
				banks,
				liveUpdate:
#if DEBUG && !IOS
				true
#else
				false
#endif
			);
			LoadMusicAreas();
			ApplyGameplayAudioPause();
		}
		catch (Exception e)
		{
			Console.Error.WriteLine($"Failed to load FMOD banks: {e}");
			musicAreas.Clear();
			musicAreas.Add(DefaultMusicArea);
		}
	}

	static string AudioBankRoot(string fmodPath)
	{
		var buildPath = Path.Join(fmodPath, "Build");
#if IOS || BROWSER
		var platformPath = Path.Join(buildPath, "Mobile");
#else
		var platformPath = Path.Join(buildPath, "Desktop");
#endif
		return Directory.Exists(platformPath) ? platformPath : fmodPath;
	}

	void LoadMusicAreas()
	{
		musicAreas.Clear();

		var labels = Audio.GetEventParameterLabels(BackgroundMusicEventPath, BackgroundMusicAreaParameter);
		if (labels.Length <= 0)
		{
			musicAreas.Add(DefaultMusicArea);
			Console.WriteLine($"Warning: could not discover FMOD labels for '{BackgroundMusicAreaParameter}' on background music event; using default area.");
			return;
		}

		musicAreas.AddRange(labels);
		if (!musicAreas.Contains(DefaultMusicArea))
			musicAreas.Insert(0, DefaultMusicArea);
	}

	static int BankLoadOrder(string path)
	{
		var name = Path.GetFileName(path);
		return name switch
		{
			"Master.bank" => 0,
			"Master.strings.bank" => 1,
			_ when name.EndsWith(".strings.bank", StringComparison.OrdinalIgnoreCase) => 2,
			_ => 3
		};
	}

	static WindowSettings? GetWindowSettingsFromJSON() {
		if (!File.Exists(WindowSettingsPath))
			return null;

		try {
			return JsonSerializer.Deserialize(File.ReadAllText(WindowSettingsPath), WindowSettingsJsonContext.Default.WindowSettings);
		} catch (Exception e) {
			Console.WriteLine($"Failed to load window settings: {e}");
			return null;
		}
	}

	void LoadWindowSettings()
	{
		try
		{
			var settings = GetWindowSettingsFromJSON();
#if EDITOR
			LoadEditorSettings();
#endif
			if (settings == null) return;
#if EDITOR
			if (!string.IsNullOrWhiteSpace(settings.WorldId))
				Assets.SelectWorld(settings.WorldId);
#endif
			if (Window != null)
			{
				if (settings is { Width: > 0, Height: > 0 })
					Window.Size = new Point2(
						Math.Max(settings.Width, MinimumRestoredWindowSize.X),
						Math.Max(settings.Height, MinimumRestoredWindowSize.Y));
				Window.Position = new Point2(settings.X, settings.Y);
				Window.AlwaysOnTop = alwaysOnTop;
			}
			hasStartedGame = settings.RoomId != null;
			selectedRoomId = settings.RoomId;
			musicEnabled = settings.MusicEnabled;
			alwaysOnTop = settings.AlwaysOnTop;
#if !IOS && !BROWSER
			runInBackground = settings.RunInBackground;
#endif
		}
		catch (Exception e)
		{
			Console.Error.WriteLine($"Failed to load window settings: {e}");
		}
	}

	void SaveWindowSettings()
	{
#if EDITOR
		SaveEditorSettings();
#endif
		if (Window == null)
			return;

		try
		{
			Directory.CreateDirectory(UserPath);

			var position = Window.Position;
			var size = Window.Size;
			var currentRoom = game?.CurrentRoom ?? CurrentRoom;
			var settings = new WindowSettings {
				X = position.X,
				Y = position.Y,
				Width = size.X,
				Height = size.Y,
				Fullscreen = Window.Fullscreen,
				WorldId = Assets.CurrentWorldId,
				RoomId = currentRoom?.Id ?? selectedRoomId,
				MusicEnabled = musicEnabled,
				AlwaysOnTop = alwaysOnTop,
#if !IOS && !BROWSER
				RunInBackground = runInBackground,
#endif
			};

			File.WriteAllText(WindowSettingsPath, JsonSerializer.Serialize(settings, WindowSettingsJsonContext.Default.WindowSettings));
		}
		catch (Exception e)
		{
			Console.Error.WriteLine($"Failed to save window settings: {e}");
		}
	}

	sealed class WindowSettings
	{
		public int X { get; init; }
		public int Y { get; init; }
		public int Width { get; init; }
		public int Height { get; init; }
		public bool Fullscreen { get; init; }
		public string? WorldId { get; init; }
		public string? RoomId { get; init; }
		public int RoomX { get; init; }
		public int RoomY { get; init; }
		public bool MusicEnabled { get; init; } = true;
		public bool AlwaysOnTop { get; init; }
#if !IOS && !BROWSER
		public bool RunInBackground { get; init; }
#endif
	}

#if EDITOR
	void LoadEditorSettings()
	{
		var migrate = !File.Exists(EditorSettingsPath);
		var path = migrate ? WindowSettingsPath : EditorSettingsPath;
		if (!File.Exists(path))
			return;

		try
		{
			var settings = JsonSerializer.Deserialize(File.ReadAllText(path), WindowSettingsJsonContext.Default.EditorSettings);
			if (settings == null)
				return;
			if (settings.EditorScrollMode is >= EditorScrollMode.Auto and <= EditorScrollMode.Trackpad)
				editorScrollMode = settings.EditorScrollMode;
			if (settings.EditorTheme is >= EditorTheme.Default and <= EditorTheme.SolarizedLight)
				editorTheme = settings.EditorTheme;
			preservePlaytestSaveState = settings.PreservePlaytestSaveState;
			editorGameRunInBackground = settings.EditorGameRunInBackground;
			pasteActorAtViewCenter = settings.PasteActorAtViewCenter;
			editorDockOpen = settings.EditorDockOpen;
			editorDockWidth = settings.EditorDockWidth > 0
				? Math.Clamp(settings.EditorDockWidth, EditorDockMinWidth, EditorDockMaxWidth)
				: DefaultToolDockWidth;
			editorPaletteWidth = settings.EditorPaletteWidth > 0
				? Math.Clamp(settings.EditorPaletteWidth, EditorDockMinWidth, EditorDockMaxWidth)
				: DefaultPaletteDockWidth;
			if (settings.EditorDockLayoutVersion < EditorDockLayoutVersion)
				pendingEditorDockLayoutReset = true;
			SetImGuiScale(settings.UIScale);
			if (migrate)
				SaveEditorSettings();
		}
		catch (Exception e)
		{
			Console.Error.WriteLine($"Failed to load editor settings: {e}");
		}
	}

	void SaveEditorSettings()
	{
		var settings = new EditorSettings
		{
			EditorScrollMode = editorScrollMode,
			EditorTheme = editorTheme,
			PreservePlaytestSaveState = preservePlaytestSaveState,
			EditorGameRunInBackground = editorGameRunInBackground,
			PasteActorAtViewCenter = pasteActorAtViewCenter,
			EditorDockOpen = editorDockOpen,
			EditorDockWidth = editorDockWidth,
			EditorPaletteWidth = editorPaletteWidth,
			EditorDockLayoutVersion = EditorDockLayoutVersion,
			UIScale = imgui.Scale,
		};

		try {
			Directory.CreateDirectory(UserPath);
			File.WriteAllText(EditorSettingsPath, JsonSerializer.Serialize(settings, WindowSettingsJsonContext.Default.EditorSettings));
		} catch (Exception e) {
			Console.Error.WriteLine($"Failed to save editor settings: {e}");
		}
	}

	sealed class EditorSettings
	{
		public EditorScrollMode EditorScrollMode { get; init; }
		public EditorTheme EditorTheme { get; init; } = EditorTheme.Mycelium;
		public bool PreservePlaytestSaveState { get; init; }
		public bool EditorGameRunInBackground { get; init; } = true;
		public bool PasteActorAtViewCenter { get; init; } = true;
		public bool EditorDockOpen { get; init; } = true;
		public float EditorDockWidth { get; init; } = DefaultToolDockWidth;
		public float EditorPaletteWidth { get; init; } = DefaultPaletteDockWidth;
		public int EditorDockLayoutVersion { get; init; } = Manager.EditorDockLayoutVersion;
		public float UIScale { get; init; } = DEFAULT_UI_SCALE;
	}
#endif

	[JsonSerializable(typeof(WindowSettings))]
#if EDITOR
	[JsonSerializable(typeof(EditorSettings))]
#endif
	partial class WindowSettingsJsonContext : JsonSerializerContext;

#if DEBUG
	Point2 playerDragStartPosition;
	Vector2 playerDragPressWorld;
	Vector2 playerDragPressCursor;
	bool playerDragStarted;
	const float PlayerDragThreshold = 4;

	// Debug mouse-drag of the player: a direct press selects, then movement begins only after
	// crossing the same small threshold used by authored actors.
	bool UpdatePlayerMouseDrag(Game g)
	{
		if (!g.AcceptsLiveInput || g.GetFirst<Player>() is not { } player)
		{
			g.draggingPlayer = false;
			playerDragStarted = false;
			return false;
		}

		if (Input.Mouse.LeftPressed)
		{
			var mouse = GameMouseWorld(g);
			if (player.GetVisualBounds().Contains(mouse))
			{
				g.draggingPlayer = true;
				playerDragStartPosition = player.Position;
				playerDragPressWorld = mouse;
				playerDragPressCursor = Cursor;
				playerDragStarted = false;
			}
			else if (Input.Keyboard.Shift)
				g.CutRopes(mouse);
		}

		if (!Input.Mouse.LeftDown)
		{
			var owned = g.draggingPlayer;
			g.draggingPlayer = false;
			playerDragStarted = false;
			return owned;
		}

		if (!g.draggingPlayer)
			return false;

		playerDragStarted |= Vector2.DistanceSquared(Cursor, playerDragPressCursor) > PlayerDragThreshold * PlayerDragThreshold;
		if (playerDragStarted)
		{
			player.Position = (Point2)((Vector2)playerDragStartPosition + GameMouseWorld(g) - playerDragPressWorld);
			player.Velocity = Vector2.Zero;
		}
		return true;
	}
#endif

	void HeadlessUpdate()
	{
		if (!playing) return;
		if (game == null) return;

		UpdateGameWithAllocationProfile(game);
		CompleteGameFrame();

		if (gym.runs == null) return;
		GymTick();
		// Per-frame state tracing is useful when diagnosing one goal under --gym-cli, but it
		// buries the per-trial result lines in the looping tune mode, so skip it there.
		if (GymLoop) return;
		if (Time.Seconds >= gym.nextLog)
		{
			var bud = game.GetFirst<Bud>();
			if (bud != null)
				Console.WriteLine($"[Gym] t={Time.Seconds - gym.roomStartTime:F2}s gst={gym.roomStartTime:F2}s bud=({bud.Position.X:F0},{bud.Position.Y:F0}) lbl={bud.DebugNavLabel}");
			gym.nextLog += 5.0;
		}
		var traceT = Time.Seconds - gym.roomStartTime;
		// GYM_TRACE_DENSE=<seconds> logs every frame for the first that-many seconds of each run
		// (GYM_TRACE_DENSE=1 => 1s; quick falls need only a few). Otherwise a one-frame burst per
		// whole second keeps the default output readable.
		var denseTrace = Environment.GetEnvironmentVariable("GYM_TRACE_DENSE") is { } dense &&
			double.TryParse(dense, out var denseSeconds) && traceT <= denseSeconds;
		if (denseTrace || (traceT > 1.0 && (int)(traceT * 10) % 10 == 0))
		{
			var bud = game.GetFirst<Bud>();
			if (bud != null)
				Console.WriteLine($"[Gym] t={traceT:F2}s bud=({bud.Position.X:F1},{bud.Position.Y:F1}) vel=({bud.Velocity.X:F1},{bud.Velocity.Y:F1}) lbl={bud.DebugNavLabel}");
			if (denseTrace && bud != null && gymNodesDumpRunIndex != gym.runIndex &&
				game.GetPlatformNavGraph() is { IsReady: true } dumpGraph)
			{
				gymNodesDumpRunIndex = gym.runIndex;
				dumpGraph.DumpNodesNear(bud.Position, 4096);
			}
		}
	}

	protected override void Update()
	{
		UpdateGame();
		ScenarioUpdate?.Invoke(this);
		Audio.Update();
	}

	void UpdateGame()
	{
		using var updatePerf = PerfStats.Time(PerfStat.Update);

		if (Time.Seconds >= maxTimeSeconds)
		{
			Exit();
			return;
		}

		if (headless)
		{
			HeadlessUpdate();
			return;
		}

#if !IOS && !BROWSER
		if (UpdateBackgroundSuspension())
			return;
#endif

		if (bootWarming)
		{
			UpdateBootWarmup();
			if (bootWarming)
				return;
		}

		InputPrompts?.Update(Time.Elapsed);

		if (debugViewMessageTimer > 0)
		{
			debugViewMessageTimer -= Time.Delta;
			if (debugViewMessageTimer <= 0)
				DebugViewMessage = null;
		}

		// Finishes the player SDF atlas once its async GPU readback lands (immediate on desktop, deferred on WebGPU).
		SpriteSdf.Update();

#if BROWSER
		UpdateAudioInterruptionRecovery();
#endif

#if EDITOR
		using (PerfStats.Time(PerfDetail.DeferredRoomPreviewDisposal))
			DisposeDeferredRoomPreviews();
#endif
#if EDITOR && DEBUG && !IOS
		UpdateWindowTitleFps();
		UpdateAssetReload();
		UpdateRoomReload();
		UpdateShaderReload();
		UpdateParticleEffectReload();
		UpdateAudioBankReload();
#endif

#if EDITOR
		UpdateCommandConsoleToggle();
#endif
		var keyboardShortcutsBlocked = ImGuiWantsKeyboard;

#if DEBUG && !BROWSER
		// Toggle the Tweaks panel from anywhere — edit or play — except while actively typing into a
		// text field (so T reaches the search box). Runs before the play/edit branch so both modes see it.
		if (imgui is { WantsTextInput: false } && Input.Keyboard.Pressed(Keys.T))
			TweakRegistry.Open = !TweakRegistry.Open;
#endif
#if EDITOR
		UpdateDebugHelpToggle();
#endif

		if (!keyboardShortcutsBlocked
#if EDITOR
			&& !((liveTileBrushArmed || LiveActorEditingWantsEscape) && Input.Keyboard.Pressed(Keys.Escape))
#endif
		)
			UpdateGameMenuInput();

		var gameMenuPausesGame = GameMenuPausesGame;

#if EDITOR
		if (!gameMenuPausesGame && ToggleEditorPressed(keyboardShortcutsBlocked))
			ToggleGame();
#endif

#if !EDITOR
		var shipRoomCheatChordActive = UpdateShipRoomCheat(gameMenuPausesGame);
#else
		const bool shipRoomCheatChordActive = false;
#endif

		if (!keyboardShortcutsBlocked && playing && !gameMenuPausesGame && GymLoop && debugControls.AdvanceGym.Pressed)
			AdvanceGym(failureReason: "manual");

		// Misc. Hotkeys
		using (PerfStats.Time(PerfDetail.Hotkeys))
			if (!keyboardShortcutsBlocked)
			{

				//if (Input.Keyboard.Pressed(Keys.F1) && !playing && !startingPlay)
				//ReloadAssets(true);

				//if (Input.Keyboard.Pressed(Keys.F2))
				//{
				//Actor.TransitionsEnabled = !Actor.TransitionsEnabled;
				//game?.DisplayMessage($"Transitions {(Actor.TransitionsEnabled ? "On" : "Off")}");
				//}

				if (debugControls.FpsOverlay.Pressed)
					ToggleFpsOverlay();
#if KIT_DIAGNOSTICS && !IOS && !BROWSER
				if (Input.Keyboard.Shift && debugControls.AllocationTrace.Pressed)
					ToggleAllocationTrace();
#endif
#if !IOS && !BROWSER
				if (Window is { } window && !keyboardShortcutsBlocked && debugControls.Fullscreen.Pressed)
					window.Fullscreen = !window.Fullscreen;
#endif
			}

		var perfToggleChordActive = gameMenuPausesGame ? false : UpdatePerfToggles(keyboardShortcutsBlocked);

		// Run Game
		if (startingPlay)
		{
			SetMouseCursor(null);
			gameEase = Calc.Approach(gameEase, 1.0f, Time.Delta * 4.0f);
			playTransition += Time.Delta / PlayTransitionDuration;
			if (playTransition >= 1.0f)
			{
				playTransition = 1.0f;
				startingPlay = false;
				playing = true;
				game?.SnapCameraToGameplayTarget();
			}
		}
		else if (playing)
		{
			SetMouseCursor(null);
			gameEase = Calc.Approach(gameEase, 1.0f, Time.Delta * 4.0f);
			if (!gameMenuPausesGame)
			{
#if IOS
				if (!UpdatePlayModePinchIn())
#endif
				{
					if (game is { } g)
					{
#if EDITOR
						var editorOwnsPlayMouse = UpdateLiveRoomEditing(g, keyboardShortcutsBlocked);
						if (!editorOwnsPlayMouse)
							editorOwnsPlayMouse = UpdateLiveTileEditing(g, keyboardShortcutsBlocked);
						if (!editorOwnsPlayMouse)
							editorOwnsPlayMouse = UpdateLiveActorEditing(g, keyboardShortcutsBlocked);
#if DEBUG
						if (!editorOwnsPlayMouse)
							editorOwnsPlayMouse = UpdatePlayerMouseDrag(g);
#endif
#elif DEBUG
						UpdatePlayerMouseDrag(g);
#endif
						if (!keyboardShortcutsBlocked && !perfToggleChordActive && !shipRoomCheatChordActive)
							g.HandleGameDebugShortcuts(debugControls, Input);
#if EDITOR
							if (!commandConsoleOpen)
#endif
							{
#if EDITOR
								var updatePlayingGame = !LiveRoomCreationModalOpen && !LiveTileControllerModeOpen;
#else
								var updatePlayingGame = true;
#endif
								if (updatePlayingGame)
								{
#if EDITOR
									if (keyboardShortcutsBlocked)
										controls.Clear();
#endif
									using (PerfStats.Time(PerfDetail.GameUpdate))
										UpdateGameWithAllocationProfile(g);
								}
								CompleteGameFrame();
							}
					}
				}

				GymTick();
			}
		}
		// Show Editor
		else
		{
#if EDITOR
			gameEase = Calc.Approach(gameEase, 0.0f, Time.Delta * 4.0f);
			if (!keyboardShortcutsBlocked)
			{
				if (debugControls.UiScaleDown.Pressed) ChangeEditorUiScale(-0.1f);
				if (debugControls.UiScaleUp.Pressed) ChangeEditorUiScale(0.1f);
				using (PerfStats.Time(PerfDetail.EditorNavigation))
					UpdateEditorNavigation();
			}
			// Preview only the selected particle emitter live in the editor.
			string? selectedEmitterId = null;
			if (CurrentRoom is { } previewRoom && selectedRoomActorIndex >= 0 && selectedRoomActorIndex < previewRoom.Actors.Count)
				selectedEmitterId = ActorSpecs.GetActorId(previewRoom.Actors[selectedRoomActorIndex]);
			// Particles animate the active-room preview, so it must re-render each frame while one is selected.
			activeRoomPreviewAnimating = selectedEmitterId != null;
			game?.PreviewParticlesInEditor(Time.Delta, selectedEmitterId);
			particleEditor.Update(Time.Delta);
#endif
		}

		if (gym.runs != null && unthrottled)
		{
			if (Input.Keyboard.Pressed(Keys.Minus, Keys.KeypadMinus))
			{
				UnthrottledStepsPerFrame = Math.Max(1, UnthrottledStepsPerFrame / 2);
				Console.WriteLine($"[Gym] {UnthrottledStepsPerFrame} steps/frame");
			}
			if (Input.Keyboard.Pressed(Keys.Equals, Keys.KeypadPlus))
			{
				UnthrottledStepsPerFrame = Math.Min(5000, UnthrottledStepsPerFrame * 2);
				Console.WriteLine($"[Gym] {UnthrottledStepsPerFrame} steps/frame");
			}
		}

		UpdateSaveStateAutosave();

		// Build the game-menu ImGui layout in its own context before the editor's,
		// so the two never nest (BeginLayout asserts no context is current).
		BuildGameMenu();

		imgui?.BeginLayout();

#if EDITOR
		EnsureEditorTheme();
		// Ahead of Editor(): the game view is a dock window and the world is drawn into the rect
		// it reports. Submitting it after Editor() would draw into last frame's rect, which is a
		// frame of lag on every layout change. It also has to precede every other dockable
		// window, since a dockspace must exist before anything can dock into it.
		if (imgui != null)
			EditorDockImGui();

		// Build our Sprite Batch
		using (PerfStats.Time(PerfDetail.BatchBuild))
		{
			batcher.Clear();
			batcher.PushMatrix(Matrix3x2.CreateScale(Scale));

			// draw editor stuff if it's open
			if (gameEase < 1.0f && !startingPlay)
			{
				Editor();
			}

			// draw to screen
			batcher.PopMatrix();
		}
#endif

		if (imgui != null)
		{
			using var imguiLayoutPerf = PerfStats.Time(PerfDetail.ImGuiLayout);
			var window = RequiredWindow;
#if IOS
			window.StopTextInput();
#else
			if (!OperatingSystem.IsBrowser())
			{
				if (imgui.WantsTextInput) window.StartTextInput();
				else window.StopTextInput();
			}
#endif
#if EDITOR
			EditorUI();
			DrawCommandConsole();
			DrawDebugHelp();
#endif
#if DEBUG && !BROWSER
			TweakRegistry.DrawPanel();
#endif
			using (PerfStats.Time(PerfDetail.DebugMessageImGui))
				DebugViewMessageImGui();

			RenderTargetOverlayImGui();
		}

		imgui?.EndLayout();
		ApplyImGuiMouseCursor();
	}

	void CompleteGameFrame()
	{
		if (maxFrames != int.MaxValue && ++completedGameFrames >= maxFrames)
			Exit();
	}

	bool AllocationProfileEnabled => allocMeasureFrames > 0;

	void UpdateGameWithAllocationProfile(Game g)
	{
		if (!AllocationProfileEnabled || allocProfileComplete)
		{
			g.Update(controls);
			return;
		}

		if (!allocProfileStarted && allocProfileFrames >= allocWarmupFrames)
			StartAllocationProfile();

		if (allocProfileStarted)
		{
			var before = GC.GetAllocatedBytesForCurrentThread();
			g.Update(controls);
			allocProfileBytes += GC.GetAllocatedBytesForCurrentThread() - before;
			allocProfileMeasuredFrames++;
			if (allocProfileMeasuredFrames >= allocMeasureFrames)
				FinishAllocationProfile();
		}
		else
		{
			g.Update(controls);
		}

		allocProfileFrames++;
	}

	void StartAllocationProfile()
	{
		allocProfileStarted = true;
		GC.Collect();
		GC.WaitForPendingFinalizers();
		GC.Collect();
	}

	void FinishAllocationProfile()
	{
		allocProfileComplete = true;
		var bytesPerFrame = allocProfileBytes / (double)allocProfileMeasuredFrames;
		Console.WriteLine(
			$"ALLOC_PROFILE game_update warmup_frames={allocWarmupFrames} measure_frames={allocProfileMeasuredFrames} bytes={allocProfileBytes} bytes_per_frame={bytesPerFrame.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}");
		Exit();
	}

	readonly Dictionary<Foster.Framework.Cursor.SystemTypes, Foster.Framework.Cursor> imguiCursors = [];
	bool imguiCursorApplied;

	// Honor the cursor ImGui asked for this frame (resize arrows over window
	// edges/corners, I-beam over text fields), restoring the default once it
	// goes back to a plain arrow.
	void ApplyImGuiMouseCursor()
	{
		if (imgui == null)
			return;

		var type = imgui.MouseCursor switch
		{
			ImGuiMouseCursor.TextInput => Foster.Framework.Cursor.SystemTypes.Text,
			ImGuiMouseCursor.ResizeAll => Foster.Framework.Cursor.SystemTypes.Move,
			ImGuiMouseCursor.ResizeNS => Foster.Framework.Cursor.SystemTypes.ResizeVertical,
			ImGuiMouseCursor.ResizeEW => Foster.Framework.Cursor.SystemTypes.ResizeHorizontal,
			ImGuiMouseCursor.ResizeNESW => Foster.Framework.Cursor.SystemTypes.ResizeNESW,
			ImGuiMouseCursor.ResizeNWSE => Foster.Framework.Cursor.SystemTypes.ResizeNWSE,
			ImGuiMouseCursor.Hand => Foster.Framework.Cursor.SystemTypes.Pointer,
			ImGuiMouseCursor.NotAllowed => Foster.Framework.Cursor.SystemTypes.NotAllowed,
			_ => Foster.Framework.Cursor.SystemTypes.Default,
		};

		if (type == Foster.Framework.Cursor.SystemTypes.Default)
		{
			if (imguiCursorApplied)
				SetMouseCursor(null);
			imguiCursorApplied = false;
			return;
		}

		if (!imguiCursors.TryGetValue(type, out var cursor))
			imguiCursors[type] = cursor = new Foster.Framework.Cursor(type);
		SetMouseCursor(cursor);
		imguiCursorApplied = true;
	}

#if EDITOR
	void ChangeEditorUiScale(float amount)
		=> SetImGuiScale(imgui.Scale + amount);

	void SetImGuiScale(float scale)
		=> imgui.Scale = Math.Clamp(scale, 0.5f, 4.0f);
#endif


	void UpdateFpsOverlay()
	{
		var now = Stopwatch.GetTimestamp();
		if (fpsOverlayLastTimestamp == 0)
		{
			fpsOverlayLastTimestamp = now;
			return;
		}

		fpsOverlayFrames++;

#if DEBUG
		// Get total allocated bytes; track diff from last frame; accumulate these diffs and report an approximate
		// per frame allocation size alongside FPS.
		var bytes = GC.GetAllocatedBytesForCurrentThread();

		if (lastAllocated != -1)
		{
			var diff = bytes - lastAllocated;
			if (diff > 0)
				threadAllocatedBytes += diff;
		}

		lastAllocated = bytes;
#endif

		var elapsed = Stopwatch.GetElapsedTime(fpsOverlayLastTimestamp, now).TotalSeconds;
		if (elapsed < 0.25f)
			return;

		fpsOverlayValue = (float)(fpsOverlayFrames / elapsed);

#if DEBUG
		threadAllocatedBytesOverlayValue = (long)((float)threadAllocatedBytes / fpsOverlayFrames);
		threadAllocatedBytes = 0;
		gcTotalPauseDuration = GC.GetTotalPauseDuration();
#endif

		PerfStats.Publish();
		fpsOverlayFrames = 0;
		fpsOverlayLastTimestamp = now;
	}

	void ToggleFpsOverlay()
	{
		fpsOverlayVisible = !fpsOverlayVisible;
		game?.DisplayMessage($"FPS Overlay {(fpsOverlayVisible ? "On" : "Off")}");
	}

	bool UpdatePerfToggles(bool keyboardShortcutsBlocked)
	{
#if !MYTHSHIP
#if !BROWSER && !IOS
		if (!keyboardShortcutsBlocked)
		{
			if (debugControls.BudAI.Pressed)
			{
				if (Input.Keyboard.Shift)
					ToggleBudStoryBarks();
				else
					ToggleBudAI();
			}
			if (debugControls.MovieRecording.Pressed) ToggleMovieRecording(Input.Keyboard.Shift);
		}
#endif

		var chordActive = false;
		foreach (var controller in Input.Controllers)
		{
			if (!controller.Connected || !PerfModifierDown(controller))
				continue;

			chordActive = true;
			if (controller.Pressed(Buttons.South)) ToggleLighting();
			if (controller.Pressed(Buttons.East)) ToggleDitherAndTileField();
			if (controller.Pressed(Buttons.West)) ToggleNormalBuffer();
			if (controller.Pressed(Buttons.North)) TogglePostEffects();
		}

		return chordActive;
#else
		return false;
#endif
	}

#if !MYTHSHIP
	static bool PerfModifierDown(ControllerState controller)
		=> !controller.IsGamepad && controller.Down(4); // Left shoulder (LB/L1) in the intended raw joystick mapping.
#endif

#if !EDITOR
	Point2 shipRoomCheatDirection;
	static float ShipRoomCheatStickThreshold => 0.65f;

	bool UpdateShipRoomCheat(bool gameMenuPausesGame)
	{
		var chordActive = false;
		var direction = Point2.Zero;

		if (playing && !gameMenuPausesGame && game?.AcceptsLiveInput == true)
			foreach (var controller in Input.Controllers)
				if (controller.Connected &&
					controller.Down(Buttons.LeftShoulder) &&
					controller.Down(Buttons.RightShoulder) &&
					controller.Down(Buttons.Up))
				{
					chordActive = true;
					direction = ShipRoomCheatDirection(controller.RightStick);
					controls.Move.Clear();
					controls.Look.Clear();
					break;
				}

		if (direction != Point2.Zero && direction != shipRoomCheatDirection)
			game?.CheatShiftRoom(direction);
		shipRoomCheatDirection = direction;
		return chordActive;
	}

	static Point2 ShipRoomCheatDirection(Vector2 stick)
	{
		if (MathF.Max(MathF.Abs(stick.X), MathF.Abs(stick.Y)) < ShipRoomCheatStickThreshold)
			return Point2.Zero;
		return MathF.Abs(stick.X) > MathF.Abs(stick.Y)
			? new(Math.Sign(stick.X), 0)
			: new(0, Math.Sign(stick.Y));
	}
#endif

	void ToggleBudAI()
	{
		PerfToggles.BudAIEnabled = !PerfToggles.BudAIEnabled;
		ShowPerfToggleMessage("BudAIEnabled", PerfToggles.BudAIEnabled);
	}

	void ToggleBudStoryBarks()
	{
		BudStoryBarks = !BudStoryBarks;
		DisplayMessage($"Bud story barks {(BudStoryBarks ? "On" : "Off")}");
	}


	// Transient on-screen status message (debug toggles, music cycle, etc.). Stored on
	// the Manager rather than the Game so it survives Game recreation (e.g. a message
	// shown as a gym run ends persists into the next run).
	public string? DebugViewMessage { get; private set; }
	float debugViewMessageTimer;

	public void DisplayMessage(string message, float duration = 1.85f, bool print = true)
	{
		if (print) Console.WriteLine(message);
		DebugViewMessage = message;
		debugViewMessageTimer = duration;
	}

	void ToggleLighting()
	{
		PerfToggles.Lighting = !PerfToggles.Lighting;
		ShowPerfToggleMessage("Lighting", PerfToggles.Lighting);
	}

	void ToggleDitherAndTileField()
	{
		PerfToggles.DitherAndTileField = !PerfToggles.DitherAndTileField;
		ShowPerfToggleMessage("Dither + Tile Field", PerfToggles.DitherAndTileField);
	}

	void ToggleNormalBuffer()
	{
		PerfToggles.NormalBuffer = !PerfToggles.NormalBuffer;
		ShowPerfToggleMessage("Normal Buffer", PerfToggles.NormalBuffer);
	}

	void ToggleEmissive()
	{
		PerfToggles.Emissive = !PerfToggles.Emissive;
		ShowPerfToggleMessage("Emissive", PerfToggles.Emissive);
	}

	void TogglePostEffects()
	{
		var enabled = !(PerfToggles.Glitch || PerfToggles.Crt);
		PerfToggles.Glitch = enabled;
		PerfToggles.Crt = enabled;
		ShowPerfToggleMessage("Post Effects", enabled);
	}

	void ToggleBurrowParticles()
	{
		PerfToggles.BurrowParticles = !PerfToggles.BurrowParticles;
		ShowPerfToggleMessage("Burrow Particles", PerfToggles.BurrowParticles);
	}

	void ShowPerfToggleMessage(string name, bool enabled)
	{
		var message = $"Perf: {name} {(enabled ? "On" : "Off")}";
		game?.DisplayMessage(message);
	}

	protected override void Render()
	{
		if (Window == null)
			return;

#if !IOS && !BROWSER
		if (backgroundSuspended)
			return;
#endif

		if (bootWarming)
		{
			RenderBootWarmup();
			return;
		}

		PerfStats.Record(PerfStat.Present, GraphicsDevice.LastPresentDuration);
		UpdateFpsOverlay();
		using var renderPerf = PerfStats.Time(PerfStat.Render);
		Window.Clear(WINDOW_CLEAR_COLOR);

		if (startingPlay)
			RenderPlayTransition();
		else if (playing)
			game?.Render((RectInt)(GameViewRect * Scale), showHud: true);
		else {
			#if !EDITOR
			// we render an in progress game
			game?.Render((RectInt)(GameViewRect * Scale), showHud: false);
			#endif
		}
		DrawGameMenu();

		batcher.Render(Window);
		batcher.Clear();
		ScenarioRender?.Invoke(this);

		if (screenshotPath != null && playing)
			UpdateScreenshotCapture();
		imgui?.Render();
		if (menuLayoutBuiltThisFrame)
			menuImgui.Render();
#if !MYTHSHIP && !BROWSER && !IOS
		if (movieTracksPlayer)
			RecordMoviePlayerSample();
#endif
	}

	// Drives automated --screenshot capture: waits screenshotFrame frames after
	// play begins, then captures Game.Screen every screenshotInterval frames,
	// screenshotCount times, and exits.
	void UpdateScreenshotCapture()
	{
		playingFrame++;

		var nextCaptureFrame = screenshotFrame + screenshotsTaken * screenshotInterval;
		if (playingFrame < nextCaptureFrame)
			return;

		CaptureScreenshot(ScreenshotPathFor(screenshotsTaken));
		screenshotsTaken++;

		if (screenshotsTaken >= screenshotCount)
			Exit();
	}

	string ScreenshotPathFor(int index)
	{
		var path = screenshotPath!;
		if (screenshotCount <= 1)
			return path;

		var dir = Path.GetDirectoryName(path) ?? string.Empty;
		var name = Path.GetFileNameWithoutExtension(path);
		var ext = Path.GetExtension(path);
		return Path.Join(dir, $"{name}_{index}{ext}");
	}

	Target CaptureScreenshot(Game game, int width = -1, int height = -1) {
		var w = width < 0 ? Game.Width : width;
		var h = height < 0 ? Game.Height : height;

		// Composite the current gameplay view into a fresh target (Game.Screen
		// is internal scratch); applyCrt is skipped for a clean pixel image.
		// Caller owns the returned target and must dispose it.
		var target = new Target(GraphicsDevice, w, h, "ScreenshotCapture");
		var worldView = new Rect(game.Camera, new Vector2(w, h));
		game.RenderWorldToTarget(target, worldView, showHud: false);

		return target;
	}

	internal bool CaptureScreenshot(string path, int? frame = null)
	{
		if (game == null)
			return false;

		try
		{
			var w = Game.Width;
			var h = Game.Height;

			// Composite the current gameplay view into a fresh target (Game.Screen
			// is internal scratch); applyCrt is skipped for a clean pixel image.
			using var target = CaptureScreenshot(game);

			var pixels = new Color[w * h];
			target.Attachments[0].GetData<Color>(pixels);

			var image = new Image(w, h, pixels);
			var full = Path.GetFullPath(path);
			var dir = Path.GetDirectoryName(full);
			if (!string.IsNullOrEmpty(dir))
				Directory.CreateDirectory(dir);
			image.WritePng(full);
			Console.WriteLine($"[Screenshot] wrote {full} ({w}x{h}) at frame {frame ?? playingFrame}");
			return true;
		}
		catch (Exception e)
		{
			Console.Error.WriteLine($"[Screenshot] failed: {e}");
			return false;
		}
	}

	void RenderPlayTransition()
	{
		if (game == null)
			return;

		var t = Ease.Cube.InOut(Math.Clamp(playTransition, 0.0f, 1.0f));

		var center2 = Vector2.Lerp(playTransitionFrom.Center, playTransitionTo.Center, t);
		var size2 = Vector2.Lerp(playTransitionFrom.Size, playTransitionTo.Size, t);
		var worldView2 = new Rect(center2 - size2 / 2, size2);

		game.RenderWorldView((RectInt)(GameViewRect * Scale), worldView2, showHud: false);
	}

#if EDITOR && DEBUG && !IOS
	void ReloadAudioBanks()
	{
		try
		{
			var musicState = game?.CaptureMusicReloadState();
			LoadAudioBanks();
			if (musicState is { } state)
				game?.RestoreMusicReloadState(state);
			proceduralSoundEditor.AudioBanksReloaded();
			game?.DisplayMessage("Reloaded FMOD banks");
			Console.WriteLine("Reloaded FMOD banks.");
		}
		catch (Exception e)
		{
			Console.Error.WriteLine($"Failed to reload FMOD banks: {e}");
		}
	}

	void CompileAndReloadShaders()
	{
		// Use MSBuild to compile shaders (works on all platforms)
		var projectDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));
		var csproj = Path.Combine(projectDir, "Myth.csproj");

		// Fallback for development: find project file by walking up
		if (!File.Exists(csproj))
		{
			var dir = new DirectoryInfo(AppContext.BaseDirectory);
			while (dir != null)
			{
				var candidate = Path.Combine(dir.FullName, "Myth.csproj");
				if (File.Exists(candidate))
				{
					csproj = candidate;
					projectDir = dir.FullName;
					break;
				}
				dir = dir.Parent;
			}
		}

		if (!File.Exists(csproj))
		{
			Console.Error.WriteLine($"Project file not found. Cannot compile shaders.");
			return;
		}

		try
		{
			var compile = new ProcessStartInfo("dotnet")
			{
				UseShellExecute = false,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				WorkingDirectory = projectDir
			};
			compile.ArgumentList.Add("build");
			compile.ArgumentList.Add("-t:CompileShaders");
			compile.ArgumentList.Add($"/p:ShaderPlatform={CurrentShaderPlatform()}");
			compile.ArgumentList.Add("-v:m"); // Minimal verbosity

			using var process = Process.Start(compile);
			if (process == null)
			{
				Console.Error.WriteLine("Failed to start shader compiler (dotnet build).");
				return;
			}

			var stdout = process.StandardOutput.ReadToEnd();
			var stderr = process.StandardError.ReadToEnd();
			process.WaitForExit();

			if (process.ExitCode != 0)
			{
				Console.Error.WriteLine($"Shader compile failed with exit code {process.ExitCode}.");
				if (!string.IsNullOrWhiteSpace(stdout))
					Console.Error.WriteLine(stdout);
				if (!string.IsNullOrWhiteSpace(stderr))
					Console.Error.WriteLine(stderr);
				return;
			}

			if (!string.IsNullOrWhiteSpace(stdout))
				Console.WriteLine(stdout.Trim());
			if (!string.IsNullOrWhiteSpace(stderr))
				Console.Error.WriteLine(stderr.Trim());

			game?.ReloadShaders();
		}
		catch (Exception e)
		{
			Console.Error.WriteLine($"Failed to compile shaders: {e}");
		}
	}

	static string CurrentShaderPlatform()
	{
		if (OperatingSystem.IsMacOS()) return "mac";
		if (OperatingSystem.IsWindows()) return "windows";
		if (OperatingSystem.IsLinux()) return "linux";

		return "all";
	}
#endif

	void ReloadAssets(bool reloadRooms)
	{
		try
		{
			if (reloadRooms)
			{
				Assets.Unload();
				Assets.Load(GraphicsDevice);
#if EDITOR
				// Every Room was just recreated; undo entries hold targets bound to the old
				// instances, so applying them would edit detached rooms.
				undo.Clear();
#endif
			}
			else
			{
				Assets.Reload(GraphicsDevice);
			}

			Factory.Clear();
			Factory.RegisterTypes();
			Assets.RebuildRoomGeometry();
#if EDITOR
			ClearRoomPreviewCache();
			ClearSurfacePreviewCache();
#endif
			ResolveSelectedRoom();
			if (playing)
				game?.RefreshRoomTiles();
#if EDITOR
			else
				SyncEditorGame();
#endif

			Console.WriteLine("Reloaded assets.");
		}
		catch (Exception e)
		{
			Console.Error.WriteLine($"Failed to reload assets: {e}");
		}
	}

	void EraseSaveGame()
	{
		saveState = new();
		saveDirty = false;
		saveFlushScheduled = false;
		saveTimer = 0;
		selectedRoomId = "start";
		game?.UseSaveState(saveState);
		SaveStateStore.Delete(this);
	}

	internal void MarkSaveDirty()
	{
		saveDirty = true;
		saveFlushScheduled = true;
		saveTimer = SaveDebounceDelay;
	}

	internal void MarkSaveDirtyWithoutAutosave()
		=> saveDirty = true;

	void UpdateSaveStateAutosave()
	{
		if (!saveDirty || !saveFlushScheduled)
			return;
		saveTimer -= Time.Delta;
		if (saveTimer <= 0)
			FlushSaveState();
	}

	internal void FlushSaveState()
	{
		if (!saveDirty)
			return;
		if (game is { } g)
			g.Save.Map = g.Minimap.CaptureState();
		SaveStateStore.Save(this, saveState);
		saveDirty = false;
		saveFlushScheduled = false;
		saveTimer = 0;
	}

	void StartGame(bool immediate = false)
	{
#if EDITOR
		ClearEditorStateForEnteringPlayMode();
#endif
		if (CurrentRoom is not { } room)
		{
			Console.Error.WriteLine("No room found for startup.");
			return;
		}
#if EDITOR
		if (!room.Save())
		{
			Console.Error.WriteLine("Play cancelled: the current room changed on disk and is being reloaded.");
			return;
		}
#endif

#if EDITOR
		if (!preservePlaytestSaveState)
			saveState = new();
		var fallbackPlayerDirection = editorRoomEntryDirection;
		Point2? initialPlayerPosition = playtestPlayerPosition is { } remembered && remembered.RoomId == room.Id ? remembered.Position : null;
#else
		Point2? fallbackPlayerDirection = null;
		Point2? initialPlayerPosition = null;
#endif

		// In the gym, a run may pin the player to a specific PlayerStart marker so we
		// can attempt each goal from each start. This wins over any remembered/editor position.
		if (gymStartOverride is { } gymStart)
			initialPlayerPosition = gymStart;
		else if (initialPlayerPosition == null && playRoomId == null && saveState.LastGrounded is { } respawn && respawn.RoomId == room.Id)
			initialPlayerPosition = respawn.Position;

		// In the gym, carry the live background music across the dispose/recreate so it
		// keeps playing seamlessly between runs instead of restarting on each goal.
		var reusableGymNavGraph = gymMode && game?.CurrentRoom?.Id == room.Id
			? game.ReadyPlatformNavGraph
			: null;
		var carriedMusic = gymMode ? game?.DetachMusic() : null;
		game?.Dispose();
		game = new Game(
			this,
			room,
			fallbackPlayerDirection: fallbackPlayerDirection,
			spawnFallbackPlayer: true,
			initialPlayerPosition: initialPlayerPosition,
			spawnBud: true,
			spawnBudAtPlayer: gymMode,
			loadGoals: gymMode,
			carryMusic: carriedMusic,
			save: saveState
		)
		{
			AudioMuted = gymMode || headless,
			NavTrace = navTrace
		};
		if (reusableGymNavGraph != null)
			game.ReusePlatformNavGraph(reusableGymNavGraph);
		game.NavTrace?.Event(game, "gameStart");
#if IOS
		playTransition = 1.0f;
		playTransitionFrom = new();
		playTransitionTo = new();
		gameEase = 1.0f;
		startingPlay = false;
		playing = true;
		game.SnapCameraToGameplayTarget();
#else
		if (immediate)
		{
			playTransition = 1.0f;
			playTransitionFrom = new();
			playTransitionTo = new();
			gameEase = 1.0f;
			startingPlay = false;
			playing = true;
			game.SnapCameraToGameplayTarget();
		}
#if EDITOR
		else
		{
			var target = game.GetGameplayCameraTarget();
			playTransition = 0.0f;
			playTransitionFrom = Rect.CenteredAt(editorCamera, GameViewRect.Size / editorZoom);
			playTransitionTo = Rect.CenteredAt(target + new Vector2(Game.Width, Game.Height) / 2, new(Game.Width, Game.Height));
			startingPlay = true;
			playing = false;
		}
#else
		else
		{
			playTransition = 1.0f;
			playTransitionFrom = new();
			playTransitionTo = new();
			gameEase = 1.0f;
			startingPlay = false;
			playing = true;
			game.SnapCameraToGameplayTarget();
		}
#endif
#endif
		VirtualGameControllerVisibilityChanged?.Invoke(WantsVirtualGameController);
	}

	enum DebugViewMessagePosition
	{
		TopCenter,
		BottomCenter
	}

	void DebugViewMessageImGui()
	{
		if (DebugViewMessage is not { } message || message == "")
			return;

		const float FONT_SCALE = 1.5f;

		var viewport = ImGui.GetMainViewport().Size;
		var textSize = ImGui.CalcTextSize(message) * FONT_SCALE;
		var padding = new Vector2(10, 6);
		var size = textSize + padding * 2;
		var CenterX = viewport.X * 0.5f;
		var Bottom = viewport.Y;

		DebugViewMessagePosition area = DebugViewMessagePosition.TopCenter;
		Vector2 position = new();
		switch (area)
		{
			case DebugViewMessagePosition.TopCenter:
				position = new Vector2(CenterX - size.X * 0.5f, 8);
#if EDITOR
				if (playing && game != null)
				{
					var editorViewport = GameViewRect;
					var screenPosition = AllocateDebugOverlayTopCenter(editorViewport, size.Y);
					position = new Vector2(screenPosition.X - size.X * 0.5f, screenPosition.Y - size.Y);
					position.X = Math.Clamp(position.X, editorViewport.Left + 4, editorViewport.Right - size.X - 4);
					position.Y = Math.Clamp(position.Y, editorViewport.Top + 4, editorViewport.Bottom - size.Y - 4);
				}
#endif
				break;
			case DebugViewMessagePosition.BottomCenter:
				position = new Vector2(CenterX - size.X * 0.5f, Bottom - size.Y - 8);
				break;
		}

		ImGui.SetNextWindowPos(position, ImGuiCond.Always);
		ImGui.SetNextWindowSize(size, ImGuiCond.Always);
		ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, padding);
		ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0);
		ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0, 0, 0, 0.78f));
		ImGui.Begin(
			"DebugViewMessage",
			ImGuiWindowFlags.NoDecoration |
			ImGuiWindowFlags.NoInputs |
			ImGuiWindowFlags.NoSavedSettings |
			ImGuiWindowFlags.NoFocusOnAppearing |
			ImGuiWindowFlags.NoNav);
		ImGui.SetWindowFontScale(FONT_SCALE);
		ImGui.TextUnformatted(message);
		ImGui.SetWindowFontScale(1.0f);
		ImGui.End();
		ImGui.PopStyleColor();
		ImGui.PopStyleVar(2);
	}

	bool ImGuiWantsKeyboard => imgui != null && (imgui.WantsKeyboardInput || imgui.WantsTextInput)
#if EDITOR
		|| commandConsoleOpen
#endif
		;

#if EDITOR
	bool ImGuiIconButton(ReadOnlySpan<char> id, Subtexture icon, string? tooltip, bool selected = false, float? imagePadding = null)
		=> ImGuiSubtextureButton(id, icon, tooltip, selected, new Vector2(ButtonSize, ButtonSize), false, imagePadding);

	bool ImGuiSubtextureButton(ReadOnlySpan<char> id, Subtexture icon, string? tooltip, bool selected, Vector2 size, float? imagePadding = null)
		=> ImGuiSubtextureButton(id, icon, tooltip, selected, size, false, imagePadding);

	bool ImGuiSubtextureButton(ReadOnlySpan<char> id, Subtexture icon, string? tooltip, bool selected, Vector2 size, bool fitAspectRatio, float? imagePadding = null)
	{
		var padding = imagePadding.HasValue ? new Vector2(imagePadding.Value) : (Vector2?)null;
		var pressed = imgui.ImageButton(id, icon, size, selected, highlight, fitAspectRatio, padding);
		var hovered = ImGui.IsItemHovered();

		if (hovered && !string.IsNullOrEmpty(tooltip))
			ImGui.SetTooltip(tooltip);

		return pressed;
	}

	bool ImGuiSubtextureButton(string id, Subtexture icon, string? tooltip, Subtexture tooltipImage, bool selected, Vector2 size, bool fitAspectRatio = false)
	{
		var pressed = imgui.ImageButton(id, icon, size, selected, highlight, fitAspectRatio);
		if (ImGui.IsItemHovered() && (!string.IsNullOrEmpty(tooltip) || tooltipImage.Texture != null))
		{
			ImGui.BeginTooltip();
			if (!string.IsNullOrEmpty(tooltip))
				ImGui.TextUnformatted(tooltip);
			if (tooltipImage.Texture != null)
				imgui.Image(tooltipImage, tooltipImage.Size * Scale);
			ImGui.EndTooltip();
		}

		return pressed;
	}

	bool ImGuiSubtextureButton(ReadOnlySpan<char> id, Subtexture icon, string? tooltip, Tileset? tooltipTileset, bool selected, Vector2 size,
		bool fitAspectRatio = false, float? imagePadding = null, bool drawBorder = true)
	{
		var padding = imagePadding.HasValue ? new Vector2(imagePadding.Value) : (Vector2?)null;
		var pressed = imgui.ImageButton(id, icon, size, selected, highlight, fitAspectRatio, padding, drawBorder);
		if (ImGui.IsItemHovered() && (!string.IsNullOrEmpty(tooltip) || tooltipTileset != null))
		{
			ImGui.BeginTooltip();
			if (!string.IsNullOrEmpty(tooltip))
				ImGui.TextUnformatted(tooltip);
			if (tooltipTileset != null)
				DrawImGuiTilesetPreview(tooltipTileset);
			ImGui.EndTooltip();
		}

		return pressed;
	}

	void DrawImGuiTilesetPreview(Tileset tileset)
	{
		var tileSize = new Vector2(Game.TileSize * MathF.Max(2, Scale));
		for (var y = 0; y < tileset.Rows; y++)
		{
			for (var x = 0; x < tileset.Columns; x++)
			{
				if (x > 0)
					ImGui.SameLine(0, 0);
				imgui.Image(tileset.GetVariantTile(x, y, 0), tileSize);
			}
		}
	}

	static bool ImGuiTextButton(ReadOnlySpan<char> label, Vector2 size, bool selected)
	{
		if (selected)
			PushSelectedImGuiButtonStyle();

		var pressed = ImGui.Button(label, size);

		if (selected)
			ImGui.PopStyleColor(3);

		return pressed;
	}

	static void PushSelectedImGuiButtonStyle()
	{
		ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.14f, 0.39f, 0.45f, 1.0f));
		ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.22f, 0.54f, 0.62f, 1.0f));
		ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.10f, 0.30f, 0.35f, 1.0f));
	}
#if EDITOR
	void ToggleGame()
	{
		if (playing || startingPlay)
			StopGame();
		else
			StartGame(immediate: true);
	}
#endif

#if IOS
	bool UpdatePlayModePinchIn()
	{
		if (!TryGetTwoFingerDistance(Input.Touch, out var distance))
		{
			playModePinchStartDistance = 0.0f;
			return false;
		}

		if (playModePinchStartDistance <= 0.0f)
		{
			playModePinchStartDistance = distance;
			return false;
		}

		if (distance > playModePinchStartDistance)
			playModePinchStartDistance = distance;

		if (playModePinchStartDistance <= 0.001f || distance / playModePinchStartDistance > PlayModePinchInThreshold)
			return false;

		playModePinchStartDistance = 0.0f;
		StopGame();
		return true;
	}

	static bool TryGetTwoFingerDistance(TouchState touch, out float distance)
	{
		TouchState.Finger? first = null;
		TouchState.Finger? second = null;
		for (var i = 0; i < touch.Count; i++)
		{
			var finger = touch[i];
			if (!finger.Down)
				continue;

			if (first == null)
			{
				first = finger;
				continue;
			}

			second = finger;
			break;
		}

		if (first is not { } a || second is not { } b)
		{
			distance = 0.0f;
			return false;
		}

		distance = Vector2.Distance(a.Position, b.Position);
		return true;
	}
#endif

	void StopGame()
	{
#if EDITOR
		var selectedLiveActorId = liveSelectedActorId;
		CancelLiveTileEditing();
		CancelLiveActorEditing(restoreDrag: false);
#endif
#if IOS
		playModePinchStartDistance = 0.0f;
#endif
		if (game?.CurrentRoom is { } room)
		{
			if (game.GetFirst<Player>() is { } player)
				playtestPlayerPosition = (room.Id, player.Position);
			SelectRoom(room);
#if EDITOR
			if (selectedLiveActorId is { } actorId && CurrentRoom is { } authoredRoom &&
				FindAuthoredActorIndex(authoredRoom, actorId) is var actorIndex && actorIndex >= 0)
			{
				SelectEditorActorTool();
				selectedRoomActorIndex = actorIndex;
				editorSelectionRect = null;
			}
#endif
		}
		MatchEditorCameraToGame();
		#if EDITOR
		EnteringEditor();
		#endif
		game?.Dispose();
		game = null;
		startingPlay = false;
		playing = false;
		if (gym.runs != null)
		{
			gym.runs = null;
			game?.DisplayMessage("Gym stopped");
			Console.WriteLine("[Gym] stopped (Tab)");
		}
		VirtualGameControllerVisibilityChanged?.Invoke(WantsVirtualGameController);
		editorGameDirty = true;
		SyncEditorGame();
	}

	void SyncEditorGame()
	{
		if (playing || startingPlay)
			return;

		if (CurrentRoom is not { } room)
		{
			game?.Dispose();
			game = null;
			return;
		}

		// Reuse a live editor-preview Game so its GameView (render targets/materials)
		// isn't reallocated on every room switch or edit; only rebuild from scratch
		// when there's no reusable preview (e.g. just exited play).
		// Reuse the live preview Game where possible (see above); either way, once the room is
		// in place let preview-only actors settle and re-render the cached preview.
		if (game is { IsEditorPreview: true, Disposed: false })
			game.SwitchEditorRoom(room);
		else
		{
			game?.Dispose();
			game = new Game(this, room, audioEventsEnabled: false, spawnBud: false, loadGoals: true);
		}
#if EDITOR
		game.RaiseEditorPreviewSync();
		MarkRoomPreviewDirty(room);
#endif
	}

#endif
}
