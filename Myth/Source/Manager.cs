using System.Diagnostics;
using System.Drawing;
using System.Numerics;
using System.Text.Json;
using Foster.Framework;
using ImGuiNET;
using Color = Foster.Framework.Color;

namespace Myth;

/// Runs the Game and, when the game is not running, displays a tile-based Level Editor (see Manager.Editor.cs)
public partial class Manager : App
{
	readonly ImGuiRenderer imgui = null!;

	public const string DefaultMusicArea = "Calm";
	public const string BackgroundMusicAreaParameter = "Area";
	public const string RoomReverbParameter = "RoomReverb";

	const string WindowSettingsFileName = "window.json";
	const string FmodProjectPath = "Sounds/FMOD/Myth.fspro";
	readonly Point MinimumRestoredWindowSize = new(640, 360);

	static float uiScale = 2.7f;
	static float Scale => uiScale;
	static float ButtonSize => 34;
	static float ButtonSpacing => 2;
#if !EDITOR
	static Color highlight => 0x5fcde4;
#endif

	/// The current Room Cell we're viewing, used as a compatibility cursor for old room navigation.
	Point2 cell = new(0, 0);
	string? selectedRoomId;
#if EDITOR
	(string RoomId, Point2 Position)? playtestPlayerPosition;
#endif
	public bool LightingDitherEnabled = true;
	public float LightingDitherScale = 1.0f;
	internal const float LightingDitherStrengthDefault = 0.155f;
	public float LightingDitherStrength = LightingDitherStrengthDefault;
	public float NormalDitherSlopeBoost = 0.3f;
	public bool NormalDitherQuantization = true;

	// Ambient occlusion: background tiles darken beneath foreground overhangs.
	public float AmbientOcclusionStrength = 1.0f;
	public float AmbientOcclusionBlur = 8.0f;
	public Vector2 AmbientOcclusionOffset = new(0, -3);
	public float AmbientOcclusionMinVisibility = 0.35f;

	// Soft shadows: per-light shadow edges soften with distance from the light.
	public float SoftShadowPenumbra = 2.3f;
	public float SoftShadowMinSoftness = 0.0f;

	/// The room renderer / simulation.
	internal Game? game;
	internal bool playing;
	bool playOnStartup;
	bool startingPlay;

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

			return Assets.GetRoom(cell);
		}
	}

	readonly Lock assetReloadLock = new();
	readonly List<string> musicAreas = [];
	readonly bool dumpAtlases;
	readonly float maxTimeSeconds;
	readonly string? playRoomId;
	readonly bool buildAssetPack;
	readonly bool useAssetPack;
	readonly string? assetPackPath;
#if BROWSER
	bool musicEnabled;
#else
	bool musicEnabled = true;
#endif
	bool alwaysOnTop;
	internal bool PlatformNavDebugVisible { get; set; }
	readonly NavTrace? navTrace;

	public bool MusicEnabled => musicEnabled;
	public bool WantsVirtualGameController => playing || startingPlay;
	// When set (desktop --aspect-four-three), force the 4:3 portrait-style cropped presentation.
	public bool AspectFourThree { get; set; }
	public bool AudioEnabled = true;
	public event Action<bool>? VirtualGameControllerVisibilityChanged;
	public const string BackgroundMusicEventPath = "event:/background_music";

	public Manager(StartArgs args) : base(CreateAppConfig(args.gym, args.headless, args.unthrottled))
	{
		AudioEnabled = args.audio;
		dumpAtlases = args.dumpAtlases;
		gymMode = args.gym;
		headless = args.headless;
		unthrottled = args.unthrottled;
		AspectFourThree = args.aspectFourThree;
		gym = new()
		{
			exitAfterAll = args.gymExitAfterAll
		};
		foreach (var goalId in args.gymGoalIds)
			gym.goalIds.Add(goalId);
		buildAssetPack = args.buildAssetPack;
		useAssetPack = args.useAssetPack;
		assetPackPath = args.assetPackPath;
		Inbetweens.BenchMode = args.bench;
		Inbetweens.MaxFrames = args.maxFrames;
		maxTimeSeconds = args.maxTimeSeconds;
		playRoomId = args.playRoomId;
		playOnStartup = args.play;
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
			LightingDitherStrength = 0;
		}

		uiScale = 2.7f;
		if (OperatingSystem.IsWindows())
		{
			uiScale = 1.5f;
		}

		batcher = new(GraphicsDevice);
		controls = new(Input);
#if EDITOR
		imgui = new(this, Assets.AssetsPath + "/Fonts/Myna-Regular.ttf") { Scale = Scale };
#elif BROWSER
		imgui = new(this) { Scale = 1.0f };
#endif
	}

	static AppConfig CreateAppConfig(bool gym = false, bool headless = false, bool unthrottled = false)
	{
		const string applicationName = "Myth";
		const string windowTitle = "Myth";
		Point2 windowSize = new(1280, 720);
		GraphicsDriver graphicsDriver = GraphicsDriver.None; // no preference
		bool resizable = true;
		bool fullscreen = false;
		var updateMode = UpdateMode.UnlockedStep();
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
			ApplicationName: applicationName,
			WindowTitle: windowTitle,
			Width: windowSize.X,
			Height: windowSize.Y,
			Fullscreen: fullscreen,
			Resizable: resizable,
			UpdateMode: updateMode,
			PreferredGraphicsDriver: graphicsDriver,
			Flags: appFlags
		);
	}

	protected override void Startup()
	{
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
		if (playRoomId != null)
			selectedRoomId = playRoomId;
		ResolveSelectedRoom();
		Factory.RegisterTypes();
		Assets.RebuildRoomGeometry();
		SpriteSdf.Generate(GraphicsDevice); // build the player + bud silhouette SDF atlas
		if (AudioEnabled)
			LoadAudioBanks();

#if BROWSER
		playOnStartup = true; // may also be set by --play on desktop
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
			Console.WriteLine($"[Gym] found {gym.runs.Count} goal run(s), starting {run.Room.Cell.X},{run.Room.Cell.Y}:{run.DisplayId}@{run.StartDisplay}");
			StartGymRun(run);
		}
#if EDITOR && DEBUG && !IOS && !BROWSER
		if (!OperatingSystem.IsBrowser())
		{
			StartAssetWatcher();
			StartShaderWatcher();
			if (AudioEnabled)
				StartAudioBankWatcher();
		}
#endif

#if EDITOR
		if (!playOnStartup)
			SyncEditorGame();
#endif
	}

	protected override void Shutdown()
	{
#if !BROWSER
		SaveWindowSettings();
#endif
		game?.Dispose();
		navTrace?.Dispose();
#if EDITOR
		ClearRoomPreviewCache(deferDisposal: false);
		DisposeDeferredRoomPreviews();
		DisposeEditorCursors();
		imgui?.Dispose();
#if DEBUG && !IOS
		assetWatcher?.Dispose();
		assetWatcher = null;
		shaderWatcher?.Dispose();
		shaderWatcher = null;
		audioBankWatcher?.Dispose();
		audioBankWatcher = null;
#endif
#endif

		Assets.Unload();
		SpriteSdf.Unload();
		Factory.Clear();
	}

	string WindowSettingsPath => Path.Join(UserPath, WindowSettingsFileName);

	void LoadAudioBanks()
	{
		if (!AudioEnabled) return;
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
			// Expected on the web FMOD bridge (it can't query labels); we fall back
			// to the default area, so this is a warning, not an error.
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

	void LoadWindowSettings()
	{
		try
		{
			if (!File.Exists(WindowSettingsPath))
				return;

			var settings = JsonSerializer.Deserialize<WindowSettings>(File.ReadAllText(WindowSettingsPath));
			if (settings == null) return;
			if (!string.IsNullOrWhiteSpace(settings.WorldId))
				Assets.SelectWorld(settings.WorldId);
			if (Window != null)
			{
				if (settings is { Width: > 0, Height: > 0 })
					Window.Size = new Point2(
						Math.Max(settings.Width, MinimumRestoredWindowSize.X),
						Math.Max(settings.Height, MinimumRestoredWindowSize.Y));
				Window.Position = new Point2(settings.X, settings.Y);
				Window.AlwaysOnTop = alwaysOnTop;
			}
			cell = new Point2(settings.RoomX, settings.RoomY);
			selectedRoomId = settings.RoomId;
			musicEnabled = settings.MusicEnabled;
			alwaysOnTop = settings.AlwaysOnTop;
		}
		catch (Exception e)
		{
			Console.Error.WriteLine($"Failed to load window settings: {e}");
		}
	}

	void SaveWindowSettings()
	{
		if (Window == null)
			return;

		try
		{
			Directory.CreateDirectory(UserPath);

			var position = Window.Position;
			var size = Window.Size;
			var currentRoom = game?.CurrentRoom ?? CurrentRoom;
			var currentCell = currentRoom?.Cell ?? cell;
			var settings = new WindowSettings
			{
				X = position.X,
				Y = position.Y,
				Width = size.X,
				Height = size.Y,
				WorldId = Assets.CurrentWorldId,
				RoomId = currentRoom?.Id ?? selectedRoomId,
				RoomX = currentCell.X,
				RoomY = currentCell.Y,
				MusicEnabled = musicEnabled,
				AlwaysOnTop = alwaysOnTop
			};

			File.WriteAllText(WindowSettingsPath, JsonSerializer.Serialize(settings));
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
		public string? WorldId { get; init; }
		public string? RoomId { get; init; }
		public int RoomX { get; init; }
		public int RoomY { get; init; }
		public bool MusicEnabled { get; init; } = true;
		public bool AlwaysOnTop { get; init; }
	}

#if DEBUG
	// Editor mouse-drag of the player: host input, drives sim state directly.
	void UpdatePlayerMouseDrag(Game g)
	{
		if (!g.AcceptsLiveInput || g.GetFirst<Player>() is not { } player)
		{
			g.draggingPlayer = false;
			return;
		}

		if (Input.Mouse.LeftPressed)
		{
			var mouse = GameMouseWorld(g);
			if (player.GetVisualBounds().Inflate(8).Contains(mouse))
				g.draggingPlayer = true;
		}

		if (!Input.Mouse.LeftDown)
		{
			g.draggingPlayer = false;
			return;
		}

		if (!g.draggingPlayer)
			return;

		player.Position = (Point2)GameMouseWorld(g);
		player.Velocity = Vector2.Zero;
	}
#endif

	void HeadlessUpdate()
	{
		if (!playing) return;
		if (game == null) return;

		game.Update(controls);

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
		if (Time.Seconds - gym.roomStartTime > 1.0 && (int)((Time.Seconds - gym.roomStartTime) * 10) % 10 == 0)
		{
			var bud = game.GetFirst<Bud>();
			if (bud != null)
				Console.WriteLine($"[Gym] t={Time.Seconds - gym.roomStartTime:F2}s bud=({bud.Position.X:F1},{bud.Position.Y:F1}) vel=({bud.Velocity.X:F1},{bud.Velocity.Y:F1}) lbl={bud.DebugNavLabel}");
		}
	}

	protected override void Update()
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
		UpdateShaderReload();
		UpdateAudioBankReload();
#endif

#if EDITOR
		UpdateCommandConsoleToggle();
#endif
		var keyboardShortcutsBlocked = ImGuiWantsKeyboard;

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

				if (Input.Keyboard.Pressed(Keys.F5))
					ToggleFpsOverlay();
#if !IOS && !BROWSER
				if (Window is { } window && !keyboardShortcutsBlocked && Input.Keyboard.Pressed(Keys.F4))
					window.Fullscreen = !window.Fullscreen;
				if (Input.Keyboard.Pressed(Keys.Escape))
				{
					CurrentRoom?.Save();
					Exit();
				}
#endif
#if EDITOR
				if (ToggleEditorPressed())
					ToggleGame();
#endif
			}

		var perfToggleChordActive = UpdatePerfToggles(keyboardShortcutsBlocked);

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
#if IOS
			if (!UpdatePlayModePinchIn())
#endif
			{
				if (game is { } g)
				{
#if DEBUG
					UpdatePlayerMouseDrag(g);
#endif
					if (!keyboardShortcutsBlocked && !perfToggleChordActive)
						g.HandleGameDebugShortcuts(Input);
					using (PerfStats.Time(PerfDetail.GameUpdate))
#if EDITOR
						if (!commandConsoleOpen)
#endif
							g.Update(controls);
				}
			}

			GymTick();
		}
		// Show Editor
		else
		{
#if EDITOR
			gameEase = Calc.Approach(gameEase, 0.0f, Time.Delta * 4.0f);
			if (!keyboardShortcutsBlocked)
			{
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
			UpdateParticleEffectAutosave();
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

		imgui?.BeginLayout();

#if EDITOR
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
#endif
			using (PerfStats.Time(PerfDetail.DebugMessageImGui))
				DebugViewMessageImGui();

			RenderTargetOverlayImGui();
		}

		imgui?.EndLayout();
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
#if !BROWSER && !IOS
		if (!keyboardShortcutsBlocked)
		{
			if (Input.Keyboard.Pressed(Keys.B)) ToggleBudAI();
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
	}

	static bool PerfModifierDown(ControllerState controller)
		=> !controller.IsGamepad && controller.Down(4);

	internal bool BudAIEnabled = true;
	void ToggleBudAI() {
		if (game != null) {
			game.BudAIEnabled = !game.BudAIEnabled;
			game.DisplayMessage(game.BudAIEnabled ? "Bud AI Enabled" : "Bud AI Disabled");
		}
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

		PerfStats.Record(PerfStat.Present, GraphicsDevice.LastPresentDuration);
		UpdateFpsOverlay();
		using var renderPerf = PerfStats.Time(PerfStat.Render);
		Window.Clear(0x2e1426);

		if (startingPlay)
			RenderPlayTransition();
		else if (playing)
			game?.Render((RectInt)(GameViewRect * Scale), showHud: true);

		batcher.Render(Window);

		if (screenshotPath != null && playing)
			UpdateScreenshotCapture();
		imgui?.Render();
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

	void CaptureScreenshot(string path)
	{
		if (game == null)
			return;

		try
		{
			var w = Game.Width;
			var h = Game.Height;

			// Composite the current gameplay view into a fresh target (Game.Screen
			// is internal scratch); applyCrt is skipped for a clean pixel image.
			using var target = new Target(GraphicsDevice, w, h, "ScreenshotCapture");
			var worldView = new Rect(game.Camera, new Vector2(w, h));
			game.RenderWorldToTarget(target, worldView, showHud: false);

			var pixels = new Color[w * h];
			target.Attachments[0].GetData<Color>(pixels);

			var image = new Image(w, h, pixels);
			var full = Path.GetFullPath(path);
			var dir = Path.GetDirectoryName(full);
			if (!string.IsNullOrEmpty(dir))
				Directory.CreateDirectory(dir);
			image.WritePng(full);
			Console.WriteLine($"[Screenshot] wrote {full} ({w}x{h}) at frame {playingFrame}");
		}
		catch (Exception e)
		{
			Console.Error.WriteLine($"[Screenshot] failed: {e}");
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
	void RequestAssetReload()
	{
		lock (assetReloadLock)
		{
			assetReloadRequested = true;
			assetReloadTimer = AssetReloadDelay;
		}
	}


	void UpdateAssetReload()
	{
		var reload = false;

		lock (assetReloadLock)
		{
			if (assetReloadRequested)
			{
				assetReloadTimer -= Time.Delta;
				if (assetReloadTimer <= 0)
				{
					assetReloadRequested = false;
					reload = true;
				}
			}
		}

		if (reload)
			ReloadAssets(false);
	}


	void RequestShaderReload()
	{
		lock (shaderReloadLock)
		{
			shaderReloadRequested = true;
			shaderReloadTimer = AssetReloadDelay;
		}
	}

	void UpdateShaderReload()
	{
		var reload = false;

		lock (shaderReloadLock)
		{
			if (shaderReloadRequested)
			{
				shaderReloadTimer -= Time.Delta;
				if (shaderReloadTimer <= 0)
				{
					shaderReloadRequested = false;
					reload = true;
				}
			}
		}

		if (reload)
			CompileAndReloadShaders();
	}

	void RequestAudioBankReload()
	{
		lock (audioBankReloadLock)
		{
			audioBankReloadRequested = true;
			audioBankReloadTimer = AssetReloadDelay;
		}
	}

	void UpdateAudioBankReload()
	{
		var reload = false;

		lock (audioBankReloadLock)
		{
			if (audioBankReloadRequested)
			{
				audioBankReloadTimer -= Time.Delta;
				if (audioBankReloadTimer <= 0)
				{
					audioBankReloadRequested = false;
					reload = true;
				}
			}
		}

		if (reload)
			ReloadAudioBanks();
	}

	void ReloadAudioBanks()
	{
		try
		{
			var musicState = game?.CaptureMusicReloadState();
			LoadAudioBanks();
			if (musicState is { } state)
				game?.RestoreMusicReloadState(state);
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


	/// Draws a box outline (for palette / workspace)
	void Box(Rect box)
	{
		batcher.Rect(box, 0x000000);
		batcher.RectLine(box, LineWeight, 0xffffff);
	}

	/// Draws a Pressable button with an icon and tooltip
	bool Button(Rect button, Subtexture subtexture, string text, bool selected = false)
	{
		var hovering = button.Contains(Cursor);

		batcher.Rect(button, Color.Black);
		batcher.ImageFit(subtexture, button.Inflate(-LineWeight), Vector2.One * 0.50f, Color.White, false, false);
		batcher.RectRoundedLine(button, 4, LineWeight, hovering ? Color.White : (selected ? highlight : Color.Gray));

		if (hovering && Assets.Font != null)
		{
			var at = new Rect(
				Cursor.X + 32,
				Cursor.Y - Assets.Font.LineHeight / 2,
				Assets.Font.WidthOf(text),
				Assets.Font.LineHeight).Inflate(12, 6, 12, 6);

			at.X = Calc.Clamp(at.X, View.Left + 8, View.Right - at.Width - 8);
			at.Y = Calc.Clamp(at.Y, View.Top + 8, View.Bottom - at.Height - 8);

			if (at.Contains(Cursor))
				at.X = Cursor.X - at.Width - 8;

			batcher.PushLayer(-10);
			batcher.RectRounded(at, 4, Color.DarkGray);
			batcher.RectRoundedLine(at, 4, LineWeight, Color.Gray);
			batcher.Text(Assets.Font, text, at.Center, Vector2.One * 0.50f, Color.White);
			batcher.PopLayer();
		}

		return hovering && Input.Mouse.LeftPressed;
	}

	bool TextButton(Rect button, string text, bool selected = false)
	{
		var hovering = button.Contains(Cursor);

		batcher.Rect(button, Color.Black);
		batcher.RectRoundedLine(button, 4, LineWeight, hovering ? Color.White : (selected ? highlight : Color.Gray));

		if (Assets.Font != null)
			batcher.Text(Assets.Font, text, button.Center, new Vector2(0.50f, 0.50f), Color.White);

		return hovering && Input.Mouse.LeftPressed;
	}

	void StartGame(bool immediate = false)
	{
#if EDITOR
		ClearEditorStateForEnteringPlayMode();
		CurrentRoom?.Save();
#endif
		if (CurrentRoom is not { } room)
		{
#if BROWSER
			Console.Error.WriteLine("No room found for browser startup.");
#endif
			return;
		}

#if EDITOR
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

		game?.Dispose();
		game = new Game(
			this,
			room,
			fallbackPlayerDirection: fallbackPlayerDirection,
			spawnFallbackPlayer: true,
			initialPlayerPosition: initialPlayerPosition,
			spawnBud: true,
			spawnBudAtPlayer: gymMode
		)
		{
			AudioMuted = gymMode || headless,
			NavTrace = navTrace
		};
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

	void DebugViewMessageImGui()
	{
		if (game?.DebugViewMessage is not { } message || message == "")
			return;

		var viewport = ImGui.GetMainViewport().Size;
		var textSize = ImGui.CalcTextSize(message);
		var padding = new Vector2(10, 6);
		var size = textSize + padding * 2;
		var CenterX = viewport.X * 0.5f;
		var Bottom = viewport.Y;
		var position = new Vector2(
			CenterX - size.X * 0.5f,
			Bottom - size.Y - 8);

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
		ImGui.TextUnformatted(message);
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

	bool ImGuiSubtextureButton(ReadOnlySpan<char> id, Subtexture icon, string? tooltip, Tileset? tooltipTileset, bool selected, Vector2 size, bool fitAspectRatio = false)
	{
		var pressed = imgui.ImageButton(id, icon, size, selected, highlight, fitAspectRatio);
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
#if IOS
		playModePinchStartDistance = 0.0f;
#endif
		if (game?.CurrentRoom is { } room)
		{
			if (game.GetFirst<Player>() is { } player)
				playtestPlayerPosition = (room.Id, player.Position);
			SelectRoom(room);
		}
		MatchEditorCameraToGame();
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
		if (game is { IsEditorPreview: true, Disposed: false })
		{
			game.SwitchEditorRoom(room);
			return;
		}

		game?.Dispose();
		game = new Game(this, room, audioEventsEnabled: false, spawnBud: false);
	}

#if !IOS && !BROWSER
	static void OpenFmodStudio()
	{
		var projectPath = Path.GetFullPath(Path.Join(Assets.AssetsPath, FmodProjectPath));
		if (!File.Exists(projectPath))
		{
			Console.Error.WriteLine($"FMOD Studio project not found at '{projectPath}'.");
			return;
		}

		try
		{
			var open = new ProcessStartInfo("/usr/bin/open")
			{
				UseShellExecute = false
			};
			open.ArgumentList.Add("-a");
			open.ArgumentList.Add("FMOD Studio");
			open.ArgumentList.Add(projectPath);
			Process.Start(open);
		}
		catch (Exception e)
		{
			Console.Error.WriteLine($"Failed to open FMOD Studio: {e}");
		}
	}
#endif

#endif
}
