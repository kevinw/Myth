using System.Diagnostics;
using System.Drawing;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;
using Foster.Framework;
using ImGuiNET;
using Color = Foster.Framework.Color;

namespace Myth;

/// Runs the Game and, when the game is not running, displays a tile-based Level Editor (see Manager.Editor.cs)
public partial class Manager : App
{
	public readonly Audio Audio = new();
	readonly StartArgs args;
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
	internal VisualTuning Visuals = new();

	/// The room renderer / simulation.
	internal Game? game;
	internal bool playing;
	internal event Action<Manager>? ScenarioUpdate;
	internal event Action<Manager>? ScenarioRender;
	bool playOnStartup;
	bool titleAttractMode; // --title: hold "play mode" at the title so editor builds don't draw the editor
	// The attract demo runs with playing==true; live editing/ tooling must treat this as "not a real
	// play session" and stay out of it. So must a pak session, whose rooms are read-only.
	bool UneditableSession => attract != null || titleAttractMode || EditorLocked;
#if EDITOR
	// Content.pak is read-only, so an editor build running from it is play-only.
	static bool EditorLocked => Assets.ContentIsPak;
#else
	static bool EditorLocked => false;
#endif
	// --play-room runs from an empty save and never touches the one on disk.
	internal bool EphemeralSave => args.playRoomId != null;
	bool skipStartupMenu => args.play || args.gym || args.headless || args.checkRooms;
	bool startingPlay;
	static float PlayMouseHideDelay => 3f;
	float playMouseIdleTimer;
	bool mousePlayMode;
	bool mouseVisible = true;
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
	SaveState saveState = new();
	bool saveDirty;
	bool saveFlushScheduled;
	float saveTimer;
	static float SaveDebounceDelay => 0.75f;

	// Automated screenshot capture (see StartArgs / --screenshot).
	int screenshotsTaken;
	int playingFrame;
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

	// Steam only exports SteamDeck=1 for titles it launches itself, so a devkit build started
	// any other way would look like a desktop. The DMI vendor is the hardware talking.
	internal static readonly bool IsSteamDeck =
		Environment.GetEnvironmentVariable("SteamDeck") == "1" || DmiVendorIsValve();

	static bool DmiVendorIsValve()
	{
		try
		{
			return OperatingSystem.IsLinux() &&
				File.ReadAllText("/sys/class/dmi/id/sys_vendor").Trim() == "Valve";
		}
		catch
		{
			return false;
		}
	}

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
	int completedGameFrames;
	long[]? frameTimestamps;
	Stopwatch? fpsLogTimer;
	ulong fpsLogStartFrame;
	int allocProfileFrames;
	int allocProfileMeasuredFrames;
	long allocProfileBytes;
	bool allocProfileStarted;
	bool allocProfileComplete;
	// Volume steps, 0..VolumeSteps. Music at 0 doesn't start the music event at all.
	const int VolumeSteps = 10;
	int musicVolume = VolumeSteps;
	int sfxVolume = VolumeSteps;
	bool vSyncEnabled = true;
	bool lowLatency;
	internal readonly VideoSettings Video = new();
	bool alwaysOnTop;
#if !BROWSER
	bool runInBackground;
	bool windowFocused;
	bool backgroundSuspended;
	static int BackgroundSleepMilliseconds => 50;
#endif
	internal bool PlatformNavDebugVisible { get; set; }
	readonly NavTrace? navTrace;

	public bool MusicEnabled => musicVolume > 0;

	int MusicVolume
	{
		get => musicVolume;
		set
		{
			var wasEnabled = MusicEnabled;
			musicVolume = value;
			ApplyVolumes();
			if (MusicEnabled && !wasEnabled)
				game?.MusicWasRestarted();
			else if (!MusicEnabled && wasEnabled)
				game?.StopMusic(true);
		}
	}

	int SfxVolume
	{
		get => sfxVolume;
		set
		{
			sfxVolume = value;
			ApplyVolumes();
		}
	}

	// The FMOD project assigns groups to these VCAs (UI sounds follow SFX). Squared, so each step
	// sounds about as big as the last: 50% is roughly -12 dB.
	void ApplyVolumes()
	{
		Audio.SetVcaVolume("vca:/Music", MathF.Pow(musicVolume / (float)VolumeSteps, 2));
		Audio.SetVcaVolume("vca:/SFX", MathF.Pow(sfxVolume / (float)VolumeSteps, 2));
	}
#if !BROWSER
	void WindowFocusGained() => windowFocused = true;
	void WindowFocusLost() => windowFocused = false;

	// The swapchain is acquired before input is polled; low latency also runs the CPU and GPU in
	// lockstep with one frame in flight.
	void ApplyLatencyMode()
	{
		GraphicsDevice.FramesInFlight = args.framesInFlight ?? (lowLatency ? 1 : 2);
		GraphicsDevice.WaitBeforeUpdate = args.gpuWaitBeforeUpdate ?? true;
	}

	bool UpdateBackgroundSuspension()
	{
		// Automated captures run unattended and may never take focus, so they must keep
		// stepping regardless of it -- otherwise the run suspends and never reaches its frame.
		var runsInBackground = runInBackground || args.runInBackground || args.screenshotPath != null || gymMode;
#if EDITOR
		runsInBackground |= editorGameRunInBackground;
#endif
		// Never before the title screen: alt-tabbing away during Loading... or the attribution
		// logos shouldn't stall startup, so a player always comes back to a ready title.
		var booting = bootWarming || startupAttributionPhase != StartupAttributionPhase.None;
		var suspend = !runsInBackground && !windowFocused && !booting;
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
		offscreen: args.bakeWorldMap != null || args.writeAllRooms || args.checkRooms, fullscreen: args.fullscreen,
		framesInFlight: args.framesInFlight ?? 2))
	{
		this.args = args;
#if EDITOR
		particleEditor = new(undo);
#if !BROWSER
		proceduralSoundEditor = new(Audio);
#endif
#endif
		AudioEnabled = args.audio;
		gym = new()
		{
			exitAfterAll = args.gymExitAfterAll
		};
		foreach (var goalId in args.gymGoalIds)
			gym.goalIds.Add(goalId);
		Assets.ConfigureContentPak(args.contentPakPath);
		Inbetweens.BenchMode = args.bench;
		Inbetweens.BenchSourceFrames = args.benchSourceFrames;
		playOnStartup = args.play;
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
			Visuals.DitherStrength = 0;
		}
		foreach (var assignment in args.visualOverrides)
			Visuals.Override(assignment);

		batcher = new(GraphicsDevice);
		controls = new(Input);
		debugControls = new(Input);
		menuImgui = new(this, enableGamepadNav: true, frameRounding: 0);
		OnExitRequested = RequestQuit;
		if (Window != null)
		{
			Window.OnCloseRequested = RequestQuit;
#if !BROWSER
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

	public const string APPLICATION_NAME = AppInfo.Name;

	internal void RequestQuit()
	{
#if EDITOR
		if (playing && HasPendingLiveEdits && liveEditReviewAction == LiveEditReviewAction.None &&
			BeginLiveEditReview(LiveEditReviewAction.Quit))
			return;
#endif
		QuitNow();
	}

	void QuitNow()
	{
		UserQuitRequested = true;
		Exit();
	}

	internal static bool DOTNET_WATCH = Environment.GetEnvironmentVariable("DOTNET_WATCH") == "1";

	static AppConfig CreateAppConfig(bool gym = false, bool headless = false, bool unthrottled = false,
		bool offscreen = false, bool? fullscreen = null, int framesInFlight = 1)
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
		// 1280x720 is the same physical size on every platform, not tiny on a scaled Windows display.
		AppFlags appFlags = AppFlags.ScaleWindowToDisplay;
#if BROWSER
		graphicsDriver = GraphicsDriver.WebGPU;
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
			Flags: appFlags,
			FramesInFlight: framesInFlight
		);
	}

	bool hasStartedGame;

	protected override void Startup()
	{
#if !BROWSER
		// Assume focus until the OS says otherwise: the window starts hidden (so it can't report
		// focus yet), and a launch that never grants focus (a launcher, a scheduled task) would
		// otherwise suspend before the first frame.
		windowFocused = true;
#if !MYTHSHIP
		if (OperatingSystem.IsMacOS() && Window is { } window)
		{
			using var icon = new Image(Path.Join(AppContext.BaseDirectory, "AppIcon.png"));
			window.SetIcon(icon);
		}
#endif
#endif
#if !BROWSER
		LoadWindowSettings();
#endif
		vSyncEnabled = args.vsync ?? vSyncEnabled;
		if (args.worldId != null)
			Assets.SelectWorld(args.worldId);
		else if (Assets.ContentIsPak)
			Assets.SelectPackagedWorld();
		if (args.bakeWorldMap != null && !Assets.Content.DirectoryExists($"Rooms/{Assets.CurrentWorldId}"))
			throw new InvalidDataException($"Cannot bake unknown world '{Assets.CurrentWorldId}'.");

#if !BROWSER
		GraphicsDevice.VSync = vSyncEnabled;
		ApplyLatencyMode();
#endif

		if (args.buildAssetPack)
		{
			Assets.BuildVisualAssetPack(GraphicsDevice, args.assetPackPath, args.dumpAtlases);
			Exit();
			return;
		}

#if BROWSER
		Assets.ConfigureVisualAssetPack(args.assetPackPath, true);
#else
		Assets.ConfigureVisualAssetPack(args.assetPackPath, args.useAssetPack);
#endif
		Assets.Load(GraphicsDevice, args.dumpAtlases);
		if (Inbetweens.BenchMode)
		{
			Console.WriteLine("[Bench] complete — exiting");
			Exit();
			return;
		}
		InputPrompts = new(GraphicsDevice, Input);
#if EDITOR
		if (!Assets.ContentIsPak)
			BlobContent.DeleteUnreferenced();
		EditorIcons.LoadPaletteFont(GraphicsDevice);
		saveState = preservePlaytestSaveState ? SaveStateStore.Load(this) : new();
#else
		saveState = SaveStateStore.Load(this);
#endif
		if (args.playRoomId != null)
			selectedRoomId = args.playRoomId;
		else if (saveState.LastGrounded is { } respawn && Assets.GetRoom(respawn.RoomId) != null)
			selectedRoomId = respawn.RoomId;
		ResolveSelectedRoom();
		Factory.RegisterTypes();
		Assets.RebuildRoomGeometry();
		SpriteSdf.Generate(GraphicsDevice); // build the player + bud silhouette SDF atlas
		if (args.checkRooms)
		{
			CheckRooms();
			Exit();
			return;
		}
		if (AudioEnabled)
			LoadAudioBanks();

#if EDITOR
		if (args.bakeWorldMap is { } mode)
		{
			BakeWorldMap(mode);
			Exit();
			return;
		}
#endif
#if !BROWSER
		if (args.writeAllRooms)
		{
			WriteAllRooms();
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
		if (args.noWarmup)
		{
			EnterInitialScreen();
		}
		else if (!headless && !gymMode && args.screenshotPath == null)
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
#if DEBUG && !BROWSER
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
		using var target = new Target(GraphicsDevice, Game.Width, Game.Height, name: "CheckRooms");
		var pixels = new Color[Game.Width * Game.Height];
		foreach (var room in Assets.RoomsById.Values.OrderBy(static room => room.Id, StringComparer.Ordinal))
		{
			try
			{
				using var game = new Game(this, room, audioEventsEnabled: false, spawnBud: false,
					enableParticles: true, loadGoals: true, save: new());
				var bounds = room.WorldBounds;
				target.Clear(Color.Transparent);
				game.RenderWorldToTarget(target, new Rect(bounds.X, bounds.Y, Game.Width, Game.Height));
				target.Attachments[0].GetData<Color>(pixels);
			}
			catch (Exception exception)
			{
				throw new InvalidOperationException($"Failed to load room '{room.Id}'.", exception);
			}
			loaded++;
		}
		Console.WriteLine($"[CheckRooms] Loaded and rendered {loaded}/{Assets.RoomsById.Count} rooms in {timer.Elapsed.TotalSeconds:F2}s.");
	}

	// Enters the first on-screen state after boot/warmup: the title menu, immediate play, or a
	// gym run. Extracted from Startup so the windowed build can defer it behind a loading screen.
	void EnterInitialScreen()
	{
		fpsLogStartFrame = Time.RenderFrame;
		fpsLogTimer = Stopwatch.StartNew();
#if BROWSER || !EDITOR
		if (!skipStartupMenu)
		{
			if (!args.title && !TryBeginStartupAttribution())
				ShowTitleMenu();
		}
		else
			playOnStartup = true; // explicit play/test runs still boot straight into play
#endif
		if (args.title || EditorLocked && !playOnStartup) // --title: force the title screen even in editor builds
		{
			playOnStartup = false;
			ShowTitleMenu();
			titleAttractMode = true; // treat the title as play mode so the editor UI stays hidden
			playing = true;
		}
		if (playOnStartup)
			StartGame(immediate: args.playRoomId != null);
#if !BROWSER
		if (unthrottled)
			Console.WriteLine("[Manager] unthrottled: running fixed 60fps steps as fast as possible (no wall-clock wait)");
#endif

		// Gym mode: auto-start with rooms that have a Goal actor
		if (gymMode)
		{
			if (!gym.CollectGymRooms(args.playRoomId))
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
		if (fpsLogTimer is { } t && t.Elapsed.TotalSeconds >= 1)
		{
			var frames = Time.RenderFrame - fpsLogStartFrame;
			var secs = t.Elapsed.TotalSeconds;
			Console.WriteLine($"[FPS] {frames} frames in {secs:F2}s = {frames / secs:F1} fps");
		}
		LogFrameTimes();
		ShutdownGame();
		Audio.Shutdown();
	}

	void ShutdownGame()
	{
#if !MYTHSHIP && !BROWSER
		try { if (MythCapture.IsRecording) StopMovieRecording(reveal: false); } catch { /* best-effort finalize */ }
#endif
		FlushSaveState();
#if !BROWSER
		SaveWindowSettings();
#endif
		game?.Dispose();
		DisposeStartupAttribution();
		navTrace?.Dispose();
		menuImgui?.Dispose();
		InputPrompts?.Dispose();
		InputPrompts = null;
#if !BROWSER
		foreach (var cursor in imguiCursors.Values)
			cursor.Dispose();
		imguiCursors.Clear();
#endif
#if EDITOR
		ClearRoomPreviewCache(deferDisposal: false);
		ClearSurfacePreviewCache(deferDisposal: false);
		surfacePreviewBatcher?.Dispose();
		DisposeDeferredRoomPreviews();
		DisposeEditorCursors();
		imgui?.Dispose();
		EditorIcons.UnloadPaletteFont();
#if DEBUG
		DisposeAssetWatchers();
#endif
#endif

		Assets.Unload();
		SpriteSdf.Unload();
		Factory.Clear();
		GameView.ClearDeviceResources(GraphicsDevice);
		RenderTargetPool.Clear(GraphicsDevice);
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
#if DEBUG
				true
#else
				false
#endif
			);
			LoadMusicAreas();
			ApplyGameplayAudioPause();
			ApplyVolumes();
#if DEBUG && !BROWSER
			WarnAboutMisconfiguredEvents();
#endif
		}
		catch (Exception e)
		{
			Console.Error.WriteLine($"Failed to load FMOD banks: {e}");
			musicAreas.Clear();
			musicAreas.Add(DefaultMusicArea);
		}
	}

#if DEBUG && !BROWSER
	// FMOD Studio makes it easy to leave a new event misconfigured, and both mistakes fail
	// quietly: routed straight to the master bus it skips the Gameplay group (the pause menu can't
	// pause it) and the VCAs (the volume sliders don't reach it); in no bank it never ships. The
	// runtime API can't see either, so read the Studio project's metadata. FMOD Studio's
	// Myth > New SFX Event menu command creates events that avoid both.
	static void WarnAboutMisconfiguredEvents()
	{
		var metadata = Path.Join(Assets.SourcesPath, "Sounds", "FMOD", "Metadata");
		if (!File.Exists(Path.Join(metadata, "Master.xml")))
			return;

		var master = XDocument.Load(Path.Join(metadata, "Master.xml")).Root?.Elements("object")
			.FirstOrDefault(o => (string?)o.Attribute("class") == "MixerMaster")?.Attribute("id")?.Value;
		foreach (var file in Directory.EnumerateFiles(Path.Join(metadata, "Event"), "*.xml"))
		{
			var objects = XDocument.Load(file).Root?.Elements("object").ToArray() ?? [];
			// A nested event's "folder" is its parent event, which it plays through.
			if (objects.FirstOrDefault(o => (string?)o.Attribute("class") == "Event") is not { } ev
				|| File.Exists(Path.Join(metadata, "Event", $"{Relationship(ev, "folder")}.xml")))
				continue;
			var name = ev.Element("property")?.Element("value")?.Value;
			if (objects.Any(o => o.Elements("relationship").Any(r => (string?)r.Attribute("name") == "output" && r.Element("destination")?.Value == master)))
				Console.Error.WriteLine($"Warning: FMOD event '{name}' routes straight to the master bus, so pausing and the volume sliders skip it.");
			if (Relationship(ev, "banks") == null)
				Console.Error.WriteLine($"Warning: FMOD event '{name}' isn't assigned to a bank, so it won't be built or shipped.");
		}

		static string? Relationship(XElement element, string name)
			=> element.Elements("relationship").FirstOrDefault(r => (string?)r.Attribute("name") == name)?.Element("destination")?.Value;
	}
#endif

	static string AudioBankRoot(string fmodPath)
	{
		var buildPath = Path.Join(fmodPath, "Build");
#if BROWSER
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
			alwaysOnTop = settings.AlwaysOnTop;
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
			musicVolume = Math.Clamp(settings.MusicVolume ?? VolumeSteps, 0, VolumeSteps);
			sfxVolume = Math.Clamp(settings.SfxVolume ?? VolumeSteps, 0, VolumeSteps);
			vSyncEnabled = settings.VSyncEnabled;
			lowLatency = settings.LowLatency;
			Video.ResolutionScale = settings.ResolutionScale;
			// Settings saved before this option existed read back as 0.
			Video.ScreenSizePercent = settings.ScreenSizePercent is >= 50 and <= 100 ? settings.ScreenSizePercent : 100;
			Video.PixelPerfect = settings.PixelPerfect;
#if !BROWSER
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
				MusicVolume = musicVolume,
				SfxVolume = sfxVolume,
				VSyncEnabled = vSyncEnabled,
				LowLatency = lowLatency,
				ResolutionScale = Video.ResolutionScale,
				ScreenSizePercent = Video.ScreenSizePercent,
				PixelPerfect = Video.PixelPerfect,
				AlwaysOnTop = alwaysOnTop,
#if !BROWSER
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
		// Nullable: a key missing from an older settings file reads as null, not a silent 0.
		public int? MusicVolume { get; init; }
		public int? SfxVolume { get; init; }
		public bool VSyncEnabled { get; init; } = true;
		public bool LowLatency { get; init; }
		public int ResolutionScale { get; init; }
		public int ScreenSizePercent { get; init; } = 100;
		public bool PixelPerfect { get; init; }
		public bool AlwaysOnTop { get; init; }
#if !BROWSER
		public bool RunInBackground { get; init; }
#endif
	}

	[JsonSerializable(typeof(WindowSettings))]
#if EDITOR
	[JsonSerializable(typeof(EditorSettings))]
#endif
	partial class WindowSettingsJsonContext : JsonSerializerContext;

	Point2 playerDragStartPosition;
	Vector2 playerDragPressWorld;
	Vector2 playerDragPressCursor;
	bool playerDragStarted;
	const float PlayerDragThreshold = 4;

	// Mouse-dragging the player: a direct press selects, then movement begins only after
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
#if DEBUG
			else if (Input.Keyboard.Shift)
				g.CutRopes(mouse);
#endif
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
		debugShowValues.Clear();
		UpdateGame();
		ScenarioUpdate?.Invoke(this);
		Audio.Update();
	}

	void UpdateGame()
	{
		using var updatePerf = PerfStats.Time(PerfStat.Update);

		if (Time.Seconds >= args.maxTimeSeconds)
		{
			Exit();
			return;
		}

		if (headless)
		{
			HeadlessUpdate();
			return;
		}

		controls.UpdateControllerAssignment();

#if !BROWSER
		if (UpdateBackgroundSuspension())
			return;
#endif

		if (bootWarming)
		{
			UpdateBootWarmup();
			if (bootWarming)
				return;
		}
		if (UpdateStartupAttribution())
			return;

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
#if EDITOR && DEBUG
		UpdateWindowTitleFps();
		UpdateAssetReload();
		UpdateRoomReload();
		UpdateShaderReload();
		UpdateParticleEffectReload();
		UpdateAudioBankReload();
		UpdateRigReload();
		UpdateCostumeReload();
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

		UpdateAttract();

		var gameMenuPausesGame = GameMenuPausesGame;

#if EDITOR
		ResolveDeferredLiveEditReview();
		if (!EditorLocked && ToggleEditorPressed(keyboardShortcutsBlocked))
		{
			if (titleAttractMode || attract != null)
				ExitAttractToEditor(); // Tab at the title demo → stop replay, leave title mode, edit
			else if (liveEditReviewAction != LiveEditReviewAction.None)
			{ } // review modal already up; ignore the toggle
			else if (!gameMenuPausesGame)
			{
				// Going play → editor with unsaved inspector edits: review them first.
				if (playing && HasPendingLiveEdits && BeginLiveEditReview(LiveEditReviewAction.EnterEditor))
				{ }
				else
					ToggleGame();
			}
		}
#endif

		UpdateMouseVisibility();

#if !EDITOR
		var shipRoomCheatChordActive = UpdateShipRoomCheat(gameMenuPausesGame);
#else
		const bool shipRoomCheatChordActive = false;
#endif
#if !MYTHSHIP
		UpdatePlayRoomSwipe(gameMenuPausesGame);
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
#if KIT_DIAGNOSTICS && !BROWSER
				if (Input.Keyboard.Shift && debugControls.AllocationTrace.Pressed)
					ToggleAllocationTrace();
#endif
#if !BROWSER
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
				if (game is { } g)
				{
#if EDITOR
					var editorOwnsPlayMouse = UpdateLiveRoomEditing(g, keyboardShortcutsBlocked);
					if (!editorOwnsPlayMouse)
						editorOwnsPlayMouse = UpdateLiveTileEditing(g, keyboardShortcutsBlocked);
					if (!editorOwnsPlayMouse)
						editorOwnsPlayMouse = UpdateLiveActorEditing(g, keyboardShortcutsBlocked);
					if (!editorOwnsPlayMouse)
						editorOwnsPlayMouse = UpdatePlayerMouseDrag(g);
#else
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
								replay?.DriveBeforeUpdate(g);
								using (PerfStats.Time(PerfDetail.GameUpdate))
									UpdateGameWithAllocationProfile(g);
							}
							CompleteGameFrame();
							if (replay is { } rp)
							{
								rp.DriveAfterUpdate();
								if (rp.Done)
								{
									if (attract is { } reel) { reel.Advance(); PlayAttractCurrent(); }
									else StartReplay(rp.Clip); // loop the single clip
								}
								else if (attract == null && rp.Cursor % 5 == 0)
									game?.DisplayMessage($"replay frame {rp.Cursor}", 0.25f, print: false); // authoring readout
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
			// Live editor previews must re-render while particles or a rig animation are advancing.
			activeRoomPreviewAnimating = selectedEmitterId != null || UpdateRigAnimationPreviewInEditor();
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
			if (!OperatingSystem.IsBrowser())
			{
				if (imgui.WantsTextInput) window.StartTextInput();
				else window.StopTextInput();
			}
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

	void UpdateMouseVisibility()
	{
		var playMode = playing || startingPlay;
		if (playMode && !mousePlayMode)
			playMouseIdleTimer = PlayMouseHideDelay;
		else if (!playMode || Input.Mouse.Delta != Vector2.Zero || Input.Mouse.Wheel != Vector2.Zero ||
			Input.Mouse.Down(MouseButtons.Left) || Input.Mouse.Down(MouseButtons.Middle) || Input.Mouse.Down(MouseButtons.Right))
			playMouseIdleTimer = 0;
		else
			playMouseIdleTimer += Time.Delta;
		mousePlayMode = playMode;

		var visible = !playMode || playMouseIdleTimer < PlayMouseHideDelay;
		if (visible == mouseVisible)
			return;
		mouseVisible = visible;
		SetMouseVisible(visible);
	}

	void CompleteGameFrame()
	{
#if DEBUG
		game?.Recorder.Tick();
#endif
		if (args.maxFrames == int.MaxValue)
			return;
		(frameTimestamps ??= new long[args.maxFrames])[completedGameFrames] = Stopwatch.GetTimestamp();
		if (++completedGameFrames >= args.maxFrames)
			Exit();
	}

	// --max-frames runs print steady-state frame times on exit, past the room-load frames.
	void LogFrameTimes()
	{
		const int warmupFrames = 120;
		if (frameTimestamps is not { } stamps || completedGameFrames < warmupFrames + 2)
			return;
		var times = new double[completedGameFrames - warmupFrames - 1];
		for (var i = 0; i < times.Length; i++)
			times[i] = Stopwatch.GetElapsedTime(stamps[warmupFrames + i], stamps[warmupFrames + i + 1]).TotalMilliseconds;
		var average = Stopwatch.GetElapsedTime(stamps[warmupFrames], stamps[completedGameFrames - 1]).TotalMilliseconds / times.Length;
		Array.Sort(times);
		double Percentile(double p) => times[(int)(p * (times.Length - 1))];
		Console.WriteLine($"[FrameTime] {times.Length} frames: avg {average:F2} ms ({1000 / average:F1} fps), p50 {Percentile(0.5):F2}, p95 {Percentile(0.95):F2}, p99 {Percentile(0.99):F2}, max {times[^1]:F2}");
	}

	bool AllocationProfileEnabled => args.allocMeasureFrames > 0;

	void UpdateGameWithAllocationProfile(Game g)
	{
		if (!AllocationProfileEnabled || allocProfileComplete)
		{
			g.Update(controls);
			return;
		}

		if (!allocProfileStarted && allocProfileFrames >= args.allocWarmupFrames)
			StartAllocationProfile();

		if (allocProfileStarted)
		{
			var before = GC.GetAllocatedBytesForCurrentThread();
			g.Update(controls);
			allocProfileBytes += GC.GetAllocatedBytesForCurrentThread() - before;
			allocProfileMeasuredFrames++;
			if (allocProfileMeasuredFrames >= args.allocMeasureFrames)
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
			$"ALLOC_PROFILE game_update warmup_frames={args.allocWarmupFrames} measure_frames={allocProfileMeasuredFrames} bytes={allocProfileBytes} bytes_per_frame={bytesPerFrame.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}");
		Exit();
	}

#if !BROWSER
	readonly Dictionary<Foster.Framework.Cursor.SystemTypes, Foster.Framework.Cursor> imguiCursors = [];
	bool imguiCursorApplied;
#endif

	// Honor the cursor ImGui asked for this frame (resize arrows over window
	// edges/corners, I-beam over text fields), restoring the default once it
	// goes back to a plain arrow.
	void ApplyImGuiMouseCursor()
	{
#if !BROWSER
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
#endif
	}


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
#if !BROWSER
		if (!keyboardShortcutsBlocked)
		{
			if (debugControls.BudAI.Pressed && !playing)
			{
				if (Input.Keyboard.Shift)
					ToggleBudStoryBarks();
				else
					ToggleBudAI();
			}
			if (debugControls.MovieRecording.Pressed) ToggleMovieRecording(Input.Keyboard.Shift);
#if DEBUG
			if (debugControls.RecordReplay.Pressed) game?.Recorder.Toggle();
#endif
			if (debugControls.PlayReplay.Pressed) { if (Input.Keyboard.Shift) ToggleAttract(); else ToggleReplay(); }
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

#if !MYTHSHIP
	// Non-shipped dev aid: a two-finger trackpad swipe during play jumps to the adjacent room, like
	// WASD in the editor. Trackpad-only (a notched mouse wheel is ignored), one room per swipe.
	Vector2 playRoomSwipeAccum;
	bool playRoomSwipeArmed = true;
	static float PlayRoomSwipeThreshold => 20f; // accumulated wheel units before a room step fires
	static int PlayRoomSwipeSign => 1;          // 1 = natural (matches the editor's content-follows-fingers pan); -1 = page-turn

	void UpdatePlayRoomSwipe(bool gameMenuPausesGame)
	{
		if (!playing || gameMenuPausesGame || game?.AcceptsLiveInput != true)
		{
			playRoomSwipeAccum = Vector2.Zero;
			playRoomSwipeArmed = true;
			return;
		}

		var wheel = Input.Mouse.Wheel;
		// Re-arm once the swipe (including trackpad momentum) settles, so it's one room per gesture.
		if (wheel == Vector2.Zero)
		{
			playRoomSwipeAccum = Vector2.Zero;
			playRoomSwipeArmed = true;
			return;
		}

		var precise = wheel.X != 0 || wheel.Y != MathF.Truncate(wheel.Y); // trackpad two-finger swipe, not a mouse wheel
		if (!precise || !playRoomSwipeArmed)
			return;

		playRoomSwipeAccum += wheel;
		if (MathF.Max(MathF.Abs(playRoomSwipeAccum.X), MathF.Abs(playRoomSwipeAccum.Y)) < PlayRoomSwipeThreshold)
			return;

		// Natural (editor pan) direction is (+X, -Y) of the raw wheel; PlayRoomSwipeSign flips both
		// together for page-turn. If only one axis feels inverted, split the sign per axis.
		var direction = MathF.Abs(playRoomSwipeAccum.X) > MathF.Abs(playRoomSwipeAccum.Y)
			? new Point2(Math.Sign(playRoomSwipeAccum.X) * PlayRoomSwipeSign, 0)
			: new Point2(0, -Math.Sign(playRoomSwipeAccum.Y) * PlayRoomSwipeSign);
		game?.CheatShiftRoom(direction);
		playRoomSwipeArmed = false;
		playRoomSwipeAccum = Vector2.Zero;
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
	readonly List<string> debugShowValues = [];

	public void DisplayMessage(string message, float duration = 1.85f, bool print = true)
	{
		if (print) Console.WriteLine(message);
		DebugViewMessage = message;
		debugViewMessageTimer = duration;
	}

	internal void ShowDebugValue(string value) => debugShowValues.Add(value);

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

#if !BROWSER
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
		Video.LogIfChanged(Window, (RectInt)(GameOpenRect * Scale)); // the full window, not the editor's docked view, which eases on every play toggle
		if (startupAttributionPhase == StartupAttributionPhase.Logos)
		{
			DrawStartupAttribution();
			batcher.Render(Window);
			batcher.Clear();
			return;
		}

		if (startingPlay)
			RenderPlayTransition();
		else if (playing)
			game?.Render((RectInt)(GameViewRect * Scale), showHud: attract == null); // no gameplay HUD behind the title
		else {
			#if !EDITOR
			// we render an in progress game
			game?.Render((RectInt)(GameViewRect * Scale), showHud: false);
			#endif
		}
		DrawGameMenu();
		DrawControllerInputs();

		batcher.Render(Window);
		batcher.Clear();
		ScenarioRender?.Invoke(this);

		if (args.screenshotPath != null && playing)
			UpdateScreenshotCapture();
		imgui?.Render();
		if (menuLayoutBuiltThisFrame)
			menuImgui.Render();
		if (DrawStartupTitleFade())
		{
			batcher.Render(Window);
			batcher.Clear();
		}
#if !MYTHSHIP && !BROWSER
		if (movieTracksPlayer)
			RecordMoviePlayerSample();
#endif
	}

	void DrawControllerInputs()
	{
		if (!args.showControllerInputs ||!playing || game == null || Window == null)
			return;
		var viewport = (RectInt)(GameViewRect * Scale);
		var present = game.PresentRect(viewport);
		var size = new Vector2(game.Screen.Width, game.Screen.Height);
		var framebuffer = Rect.CenteredAt(present.Center, size * game.PresentScale(present, size.X, size.Y));
		var bounds = ControllerInputDisplay.Bounds(viewport, framebuffer);
		ControllerInputDisplay.Draw(batcher, Input.Controllers[controls.PlayerControllerIndex], bounds, Assets.Font);
	}

	// Drives automated --screenshot capture: waits screenshotFrame frames after
	// play begins, then captures Game.Screen every screenshotInterval frames,
	// screenshotCount times, and exits.
	void UpdateScreenshotCapture()
	{
		playingFrame++;

		var nextCaptureFrame = args.screenshotFrame + screenshotsTaken * args.screenshotInterval;
		if (playingFrame < nextCaptureFrame)
			return;

		CaptureScreenshot(ScreenshotPathFor(screenshotsTaken));
		screenshotsTaken++;

		if (screenshotsTaken >= args.screenshotCount)
			Exit();
	}

	string ScreenshotPathFor(int index)
	{
		var path = args.screenshotPath!;
		if (args.screenshotCount <= 1)
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
		// Default capture is what the player sees (the room's zoom included); an explicit size
		// asks for that many world pixels instead.
		// Default capture is what the player sees, rendered the way they see it (the room's zoom
		// included); an explicit size asks for that many world pixels at 1:1.
		var faithful = width < 0 && height < 0;
		var worldView = faithful ? game.WorldView : new Rect(game.Camera, new Vector2(w, h));
		game.RenderWorldToTarget(target, worldView, showHud: false, zoom: faithful ? game.ZoomLevel : 1.0f);

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

#if EDITOR && DEBUG
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

			// Registering hands out session chars in registration order, so a tile type appearing
			// or disappearing shifts the chars of the ones after it. Every loaded room is still
			// painted with the old chars (their legends were resolved against them), so rebind the
			// grids before anything reads them -- otherwise a tile silently becomes whichever type
			// inherited its char.
			var charsBefore = Factory.SnapshotChars();
			Factory.Clear();
			Factory.RegisterTypes();
			if (Factory.RemapSince(charsBefore) is { Count: > 0 } remap)
			{
				foreach (var room in Assets.RoomsById.Values)
					room.RemapTiles(remap);
#if EDITOR
				// Undo entries hold the chars they were recorded with, so applying one now would
				// paint the wrong tile type.
				undo.Clear();
#endif
			}
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
#if EDITOR
		playtestPlayerPosition = null;
#endif
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

	// Playback is available in all builds (the title-screen demo ships in Release); only recording
	// (ReplayRecorder, F7) is dev-only.
	ReplayPlayer? replay;
	AttractReel? attract;

	// F8: play the newest recorded clip (dev convenience). Toggles off if already playing.
	void ToggleReplay()
	{
		if (replay != null) { StopReplay(); return; }
		if (ReplayPlayer.NewestClipPath(UserPath) is not { } path)
		{
			game?.DisplayMessage("No replays recorded");
			return;
		}
		try { StartReplay(ReplayClip.Load(path)); }
		catch (Exception e) { game?.DisplayMessage($"Replay load failed: {e.Message}"); }
	}

	// Shift+F8: play the authored attract playlist (Assets/Attract/attract.json), looping.
	void ToggleAttract()
	{
		if (attract != null) { attract = null; StopReplay(); return; }
		attract = AttractReel.Load();
		if (attract == null) { game?.DisplayMessage("Attract playlist empty or missing"); return; }
		PlayAttractCurrent();
	}

	void PlayAttractCurrent()
	{
		var entry = attract!.Current;
		if (string.IsNullOrWhiteSpace(entry.Clip))
		{
			DisplayMessage("Attract entry is missing a 'clip' name");
			attract = null;
			return;
		}
		try
		{
			var clip = AttractReel.LoadClip(entry.Clip);
			clip.VisibleFromFrame = entry.Start;
			StartReplay(clip, entry.End);
		}
		catch (Exception e)
		{
			DisplayMessage($"Attract clip '{entry.Clip}' failed: {e.Message}");
			attract = null;
		}
	}

	void StartReplay(ReplayClip clip, int playUntil = -1)
	{
		if (Assets.GetRoom(clip.RoomId) is not { } room)
		{
			game?.DisplayMessage($"Replay room not found: {clip.RoomId}");
			replay = null;
			return;
		}
		game?.Dispose();
		game = new Game(this, room, spawnFallbackPlayer: true,
			initialPlayerPosition: clip.Start.Position, save: SaveState.FromJsonString(clip.SaveJson));
		if (game.GetFirst<Player>() is { } player)
			clip.Start.ApplyTo(player);
		game.RngSeed = clip.Seed;
		replay = new ReplayPlayer(clip, playUntil);
		EnterReplayFixedStep(); // pace playback to the fixed replay rate (real-time on any display)

		// Fast-forward warmup: silently re-simulate frames 0..VisibleFromFrame so the visible
		// playback begins mid-action in a fully-correct sim state. AudioMuted (not Simulating,
		// which would diverge the RNG stream) keeps the burst silent; no render happens here.
		if (clip.VisibleFromFrame > 0)
		{
			var wasMuted = game.AudioMuted;
			game.AudioMuted = true;
			var target = Math.Min(clip.VisibleFromFrame, clip.FrameCount);
			for (var i = 0; i < target; i++)
			{
				replay.DriveBeforeUpdate(game);
				game.Update(controls);
				replay.DriveAfterUpdate();
			}
			game.AudioMuted = wasMuted;
			game.SnapCameraToGameplayTarget();
		}

		playing = true;
		startingPlay = false;
		gameEase = 1f;
		game.DisplayMessage($"Replay: {clip.RoomId} ({clip.FrameCount}f{(clip.VisibleFromFrame > 0 ? $", from {clip.VisibleFromFrame}" : "")})");
	}

	void StopReplay()
	{
		replay = null;
		attract = null;
		if (game?.GetFirst<Player>() is { } player)
			player.ScriptedInput = null;
		game?.EndReplayClock();
		ExitReplayFixedStep();
		game?.DisplayMessage("Replay: stopped");
	}

	// Replays are captured and played at a fixed timestep (ReplayClip.ReplayHz) so dt never needs
	// storing; this holds the app at that rate during record/playback and restores the prior mode
	// (unlocked vsync) afterward. Idempotent via savedUpdateMode so the reel's per-clip restarts and
	// the various teardown paths can call these freely.
	UpdateMode? savedUpdateMode;
	internal void EnterReplayFixedStep()
	{
		if (savedUpdateMode != null) return;
		savedUpdateMode = UpdateMode;
		UpdateMode = UpdateMode.FixedStep(ReplayClip.ReplayHz);
	}
	internal void ExitReplayFixedStep()
	{
		if (savedUpdateMode is not { } mode) return;
		UpdateMode = mode;
		savedUpdateMode = null;
	}

	// Seconds of no input on the title before the attract demo starts. 0 = immediately.
	static float AttractIdleSeconds => 0f;
	// When true, any input stops the demo (arcade attract). When false (default), the demo keeps
	// playing behind the title so you can navigate the menu over it. (Tab-to-editor always exits.)
	static bool AttractInterruptOnInput => false;
	float titleIdleTimer;
	bool attractUnavailable; // playlist missing/empty this title visit; don't re-load every frame

	// Runs the attract reel behind the title: start it after the idle timeout; optionally dismiss on input.
	void UpdateAttract()
	{
		if (gameMenuPage != GameMenuPage.Title)
		{
			titleIdleTimer = 0;
			attractUnavailable = false;
			return;
		}
		var interrupt = AttractInterruptOnInput && AttractInterrupted();
		if (attract != null)
		{
			if (interrupt) { StopAttract(); titleIdleTimer = 0; }
			return;
		}
		if (attractUnavailable) return;
		if (interrupt) { titleIdleTimer = 0; return; }
		titleIdleTimer += Time.Delta;
		if (titleIdleTimer >= AttractIdleSeconds) StartAttract();
	}

	// Tab (or right-stick) while the attract demo is up: stop it, leave title/play mode, and drop
	// into the editor as usual.
	void ExitAttractToEditor()
	{
		titleAttractMode = false;
		StopAttract();     // disposes the demo game; playing = titleAttractMode (now false)
		CloseGameMenu();   // leave the title menu so the editor is visible
	}

	void StartAttract()
	{
		attract = AttractReel.Load();
		if (attract == null) { attractUnavailable = true; return; }
		PlayAttractCurrent();
	}

	void StopAttract()
	{
		replay = null;
		attract = null;
		game?.EndReplayClock();
		ExitReplayFixedStep();
		game?.Dispose();
		game = null;
		playing = titleAttractMode; // stay in play mode at the title (--title) so the editor stays hidden
	}

	bool AttractInterrupted()
		=> Input.Mouse.Delta != default
		|| Input.Mouse.Down(MouseButtons.Left) || Input.Mouse.Down(MouseButtons.Right)
		|| controls.Move.IntValue != Point2.Zero
		|| controls.Jump.Down || controls.Dash.Down || controls.Attack.Down || controls.Map.Down
		|| Input.Keyboard.Down(Keys.Enter, Keys.Space, Keys.Escape);

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
		replay = null; // never let attract/replay drive a real play session
		attract = null;
		titleAttractMode = false;
		ExitReplayFixedStep(); // real gameplay runs at unlocked vsync, not the replay's fixed rate
#if EDITOR
		ClearEditorStateForEnteringPlayMode();
#endif
		if (CurrentRoom is not { } room)
		{
			Console.Error.WriteLine("No room found for startup.");
			return;
		}
#if EDITOR
		if (!EditorLocked && !room.Save())
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
		else if (initialPlayerPosition == null && args.playRoomId == null && saveState.LastGrounded is { } respawn && respawn.RoomId == room.Id)
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
			spawnBud: !args.noNav,
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
			playTransitionTo = Rect.CenteredAt(target + new Vector2(Game.Width, Game.Height) / 2, new Vector2(Game.Width, Game.Height) / game.ZoomLevel);
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
		VirtualGameControllerVisibilityChanged?.Invoke(WantsVirtualGameController);
	}

	enum DebugViewMessagePosition
	{
		TopCenter,
		BottomCenter
	}

	void DebugViewMessageImGui()
	{
		var message = DebugViewMessage;
		if (string.IsNullOrEmpty(message) && debugShowValues.Count == 0)
			return;

		const float FONT_SCALE = 1.5f;

		var viewport = ImGui.GetMainViewport().Size;
		var textSize = Vector2.Zero;
		var lineCount = 0;
		if (!string.IsNullOrEmpty(message))
		{
			textSize = ImGui.CalcTextSize(message) * FONT_SCALE;
			lineCount++;
		}
		for (var i = 0; i < debugShowValues.Count; i++)
		{
			var lineSize = ImGui.CalcTextSize(debugShowValues[i]) * FONT_SCALE;
			textSize.X = MathF.Max(textSize.X, lineSize.X);
			textSize.Y += lineSize.Y;
			lineCount++;
		}
		textSize.Y += Math.Max(0, lineCount - 1) * ImGui.GetStyle().ItemSpacing.Y;
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
		if (!string.IsNullOrEmpty(message))
			ImGui.TextUnformatted(message);
		for (var i = 0; i < debugShowValues.Count; i++)
			ImGui.TextUnformatted(debugShowValues[i]);
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
}
