using System.Diagnostics;
using System.Drawing;
using System.Numerics;
using System.Text.Json;
using Foster.Framework;
#if EDITOR
using ImGuiNET;
#endif
using Color = Foster.Framework.Color;

namespace TinyLink;

/// <summary>
/// Runs the Game and, when the game is not running, displays a tile-based Level Editor (see Manager.Editor.cs)
/// </summary>
public partial class Manager : App
{
	public const string DefaultMusicArea = "Calm";
	public const string BackgroundMusicAreaParameter = "Area";
	public const string RoomReverbParameter = "RoomReverb";

	private const string WindowSettingsFileName = "window.json";
	private const string FmodProjectPath = "Sounds/FMOD/TinyLink.fspro";
	private readonly Point MinimumRestoredWindowSize = new(640, 360);

	static float Scale => 2.7f;
	static float ButtonSize => 34;
	static float ButtonSpacing => 2;
#if !EDITOR
	static Color highlight => 0x5fcde4;
#endif

	/// <summary>
	/// The current Room Cell we're viewing, used as a compatibility cursor for old room navigation.
	/// </summary>
	private Point2 cell = new(0, 0);
	private string? selectedRoomId;
#if EDITOR
	private (string RoomId, Point2 Position)? playtestPlayerPosition;
#endif
	public bool LightingDitherEnabled = true;
	public float LightingDitherScale = 1.0f;
	internal const float LightingDitherStrengthDefault = 0.155f;
	public float LightingDitherStrength = LightingDitherStrengthDefault;

	/// <summary>
	/// The room renderer / simulation.
	/// </summary>
	internal Game? game;
	internal bool playing;
	bool startingPlay;
#if IOS
	private const float PlayModePinchInThreshold = 0.82f;
	private float playModePinchStartDistance;
#endif
	private const float PlayTransitionDuration = 0.25f;
	float playTransition;
	Vector2 playTransitionFromCenter;
	Vector2 playTransitionFromSize;
	Vector2 playTransitionToCenter;
	Vector2 playTransitionToSize;
	int fpsOverlayFrames;
	long fpsOverlayLastTimestamp;
	float fpsOverlayValue;

#if DEBUG
	private long threadAllocatedBytes;
	private long threadAllocatedBytesOverlayValue;
	private long lastAllocated = -1;
#endif

#if EDITOR
	int fpsOverlayMode;
#endif
	bool fpsOverlayVisible = false;

	/// <summary>
	/// Eases in/out as the Game starts/stops playing
	/// </summary>
	float gameEase = 1.0f;

	/// <summary>
	/// Sprite Batcher for general Editor visuals
	/// </summary>
	private readonly Batcher batcher;
	private readonly Controls controls;

	/// <summary>
	/// Visible Space
	/// </summary>
	private Window RequiredWindow =>
		Window ?? throw new InvalidOperationException("Window is not available in headless mode.");

	private Rect View
	{
		get
		{
			var window = RequiredWindow;
			return new Rect(0, 0, window.WidthInPixels / Scale, window.HeightInPixels / Scale);
		}
	}

	/// <summary>
	/// Play Workspace when the Game is Open
	/// </summary>
	private Rect GameOpenRect => View;


#if !EDITOR
	private Rect GameViewRect => GameOpenRect;
#endif

	public Vector2 GameMouseWorld(Game gameInstance)
	{
		var viewport = (RectInt)(GameViewRect * Scale);
		var scale = Calc.Min(viewport.Width / (float)gameInstance.Screen.Width, viewport.Height / (float)gameInstance.Screen.Height);

		var size = new Vector2(gameInstance.Screen.Width, gameInstance.Screen.Height) * scale;
		var topLeft = (Vector2)viewport.Center - size / 2;
		var local = (Input.Mouse.Position - topLeft) / scale;
		return gameInstance.Camera + local;
	}

	/// <summary>
	/// Mouse Cursor relative to Scale
	/// </summary>
	private Vector2 Cursor => Input.Mouse.Position / Scale;

	/// <summary>
	/// Line Weight for outlines, relative to Scale
	/// </summary>
	private float LineWeight => (1.0f / Scale) * 4;

	/// <summary>
	/// Tries to get the Existing Room
	/// </summary>
	private Room? CurrentRoom
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
	bool musicEnabled = true;
	bool alwaysOnTop;
	internal bool PlatformNavDebugVisible { get; set; }

	public bool MusicEnabled => musicEnabled;
	public bool WantsVirtualGameController => playing || startingPlay;
	public event Action<bool>? VirtualGameControllerVisibilityChanged;
	public const string BackgroundMusicEventPath = "event:/background_music";

	public Manager(bool dumpAtlases = false, bool gym = false, bool headless = false, bool unthrottled = false, bool gymExitAfterAll = false, bool simple = false) : base(CreateAppConfig(gym, headless, unthrottled))
	{
		this.dumpAtlases = dumpAtlases;
		this.gymMode = gym;
		this.headless = headless;
		this.unthrottled = unthrottled;
		this.gymExitAfterAll = gymExitAfterAll;
		if (simple)
		{
			PerfToggles.UseSimpleDefaults();
			LightingDitherStrength = 0;
		}
		batcher = new(GraphicsDevice);
		controls = new(Input);
#if EDITOR
		imgui = new(this, Assets.AssetsPath + "/Fonts/Myna-Regular.ttf") { Scale = Scale };
#endif
	}

	private static AppConfig CreateAppConfig(bool gym = false, bool headless = false, bool unthrottled = false)
	{
#if BROWSER
		return new AppConfig(
			ApplicationName: "TinyLink",
			WindowTitle: "TinyLink",
			Width: 1280,
			Height: 720,
			Resizable: true,
			UpdateMode: UpdateMode.UnlockedStep(),
			PreferredGraphicsDriver: GraphicsDriver.WebGPU);
#elif IOS
		return new AppConfig(
			ApplicationName: "TinyLink",
			WindowTitle: "TinyLink",
			Width: 1280,
			Height: 720,
			Fullscreen: true,
			Resizable: false,
			UpdateMode: UpdateMode.UnlockedStep(),
			PreferredGraphicsDriver: GraphicsDriver.Metal);
#else
		if (headless && unthrottled)
			return new AppConfig(
				ApplicationName: "Myth",
				WindowTitle: "Myth",
				Width: 1,
				Height: 1,
				Resizable: false,
				UpdateMode: UpdateMode.UnthrottledStep(60),
				PreferredGraphicsDriver: GraphicsDriver.Headless,
				Flags: AppFlags.Headless);

		if (headless)
			return new AppConfig(
				ApplicationName: "Myth",
				WindowTitle: "Myth",
				Width: 1,
				Height: 1,
				Resizable: false,
				UpdateMode: UpdateMode.FixedStep(60),
				PreferredGraphicsDriver: GraphicsDriver.Headless,
				Flags: AppFlags.Headless);

		if (unthrottled)
			return new AppConfig(
				ApplicationName: "Myth",
				WindowTitle: "Myth",
				Width: 1280,
				Height: 720,
				Resizable: true,
				UpdateMode: UpdateMode.UnthrottledStep(60));

		return new AppConfig(
			ApplicationName: "Myth",
			WindowTitle: "Myth",
			Width: 1280,
			Height: 720,
			Resizable: true,
			UpdateMode: UpdateMode.UnlockedStep());
#endif
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
		Assets.Load(GraphicsDevice, dumpAtlases);
		ResolveSelectedRoom();
		Factory.RegisterTypes();
		Assets.RebuildRoomGeometry();
#if BROWSER_NO_FMOD
		musicAreas.Clear();
		musicAreas.Add(DefaultMusicArea);
#else
		LoadAudioBanks();
#endif
#if BROWSER
		StartGame();
#else
		if (unthrottled)
			Console.WriteLine("[Manager] unthrottled: running fixed 60fps steps as fast as possible (no wall-clock wait)");
#endif

		// Gym mode: auto-start with rooms that have a Goal actor
		if (gymMode)
		{
			CollectGymRooms();
			if (gymRuns is { Count: > 0 })
			{
				gymRunIndex = 0;
				var run = gymRuns[gymRunIndex];
				var room = run.Room;
				cell = room.Cell;
				selectedRoomId = null;
				Console.WriteLine($"[Gym] found {gymRuns.Count} goal run(s), starting {room.Cell.X},{room.Cell.Y}:{run.GoalId}");
				gymRoomStartTime = Time.Seconds;
				StartGame(immediate: true);
				AssignGymTarget(run);
			}
			else
			{
				Console.WriteLine("[Gym] no rooms with Goal found — gym mode idle");
			}
		}
#if EDITOR && DEBUG && !IOS
		StartAssetWatcher();
		StartShaderWatcher();
		StartAudioBankWatcher();
#endif

#if EDITOR
		SyncEditorGame();
#endif
	}

	protected override void Shutdown()
	{
#if !BROWSER
		SaveWindowSettings();
#endif
		game?.Dispose();
#if EDITOR
		ClearRoomPreviewCache(deferDisposal: false);
		DisposeDeferredRoomPreviews();
		DisposeEditorCursors();
		if (imgui != null)
			imgui.Dispose();
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
		Factory.Clear();
	}

	private string WindowSettingsPath => Path.Join(UserPath, WindowSettingsFileName);


	private void LoadAudioBanks()
	{
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

	private static string AudioBankRoot(string fmodPath)
	{
		var buildPath = Path.Join(fmodPath, "Build");
#if IOS || BROWSER
		var platformPath = Path.Join(buildPath, "Mobile");
#else
		var platformPath = Path.Join(buildPath, "Desktop");
#endif
		return Directory.Exists(platformPath) ? platformPath : fmodPath;
	}

	private void LoadMusicAreas()
	{
		musicAreas.Clear();

		var labels = Audio.GetEventParameterLabels(BackgroundMusicEventPath, BackgroundMusicAreaParameter);
		if (labels.Length <= 0)
		{
			musicAreas.Add(DefaultMusicArea);
			Console.Error.WriteLine($"Could not discover FMOD labels for '{BackgroundMusicAreaParameter}' on background music event.");
			return;
		}

		musicAreas.AddRange(labels);
		if (!musicAreas.Contains(DefaultMusicArea))
			musicAreas.Insert(0, DefaultMusicArea);
	}

	private static int BankLoadOrder(string path)
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

	private void LoadWindowSettings()
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

	private void SaveWindowSettings()
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

	private sealed class WindowSettings
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

	protected override void Update()
	{
		using var updatePerf = PerfStats.Time(PerfStat.Update);

		if (headless)
		{
			if (playing && game != null)
			{
				game.Update(true);
				if (gymRuns != null)
				{
					if (game.GymBudDied)
						AdvanceGym(success: false);
					else if (game.GymTarget is Goal goal && game.GetFirst<Bud>() is { } bud
						&& goal.OverlapsAny(Point2.Zero, bud))
					{
						AdvanceGym(success: true);
					}
					else if (Time.Seconds - gymRoomStartTime > 20.0)
					{
						Console.WriteLine($"[Gym] timeout ({Time.Seconds - gymRoomStartTime:F1}s), advancing");
						AdvanceGym(success: false);
					}

					if (Time.Seconds >= nextGymLog)
					{
						var bud = game.GetFirst<Bud>();
						if (bud != null)
							Console.WriteLine($"[Gym] t={Time.Seconds - gymRoomStartTime:F2}s gst={gymRoomStartTime:F2}s bud=({bud.Position.X:F0},{bud.Position.Y:F0}) lbl={bud.DebugNavLabel}");
						nextGymLog += 5.0;
					}
					if (Time.Seconds - gymRoomStartTime > 1.0 && (int)((Time.Seconds - gymRoomStartTime) * 10) % 10 == 0)
					{
						var bud = game.GetFirst<Bud>();
						if (bud != null)
							Console.WriteLine($"[Gym] t={Time.Seconds - gymRoomStartTime:F2}s bud=({bud.Position.X:F1},{bud.Position.Y:F1}) vel=({bud.Velocity.X:F1},{bud.Velocity.Y:F1}) lbl={bud.DebugNavLabel}");
					}
				}
			}
			return;
		}

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
		var keyboardShortcutsBlocked = ImGuiWantsKeyboard;
#else
		var keyboardShortcutsBlocked = false;
#endif

		// Misc. Hotkeys
		using (PerfStats.Time(PerfDetail.Hotkeys))
		{
			if (!keyboardShortcutsBlocked && Input.Keyboard.Pressed(Keys.F1) && !playing && !startingPlay)
				ReloadAssets(true);

			if (!keyboardShortcutsBlocked && Input.Keyboard.Pressed(Keys.F5))
				ToggleFpsOverlay();
#if !IOS && !BROWSER
			if (Window is { } window && !keyboardShortcutsBlocked && Input.Keyboard.Pressed(Keys.F4))
				window.Fullscreen = !window.Fullscreen;
			if (!keyboardShortcutsBlocked && Input.Keyboard.Pressed(Keys.Escape))
			{
				CurrentRoom?.Save();
				Exit();
			}
#endif
#if EDITOR
			if (!keyboardShortcutsBlocked && ToggleEditorPressed())
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
				using (PerfStats.Time(PerfDetail.GameUpdate))
					game?.Update(!keyboardShortcutsBlocked && !perfToggleChordActive);
			}

			if (gymRuns != null && game != null)
			{
				if (game.GymBudDied)
				{
					Console.WriteLine("[Gym] Bud fell off world");
					AdvanceGym(success: false);
				}
				else if (game.GymTarget is Goal goal && game.GetFirst<Bud>() is { } bud
					&& goal.OverlapsAny(Point2.Zero, bud))
				{
					Console.WriteLine("[Gym] Bud reached goal");
					AdvanceGym(success: true);
				}
				else if (Time.Seconds - gymRoomStartTime > 20.0)
				{
					Console.WriteLine($"[Gym] timeout ({Time.Seconds - gymRoomStartTime:F1}s), advancing");
					AdvanceGym(success: false);
				}
			}
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
#endif
		}

		if (gymRuns != null && unthrottled)
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

#if EDITOR
		// Build our Sprite Batch
		using (PerfStats.Time(PerfDetail.BatchBuild))
		{
			batcher.Clear();
			batcher.PushMatrix(Matrix3x2.CreateScale(Scale));

			// draw editor/game box
			// Box(WorkspaceRect);

			// draw editor stuff if it's open
			if (gameEase < 1.0f && !startingPlay)
			{
				Editor();
			}

			// draw to screen
			batcher.PopMatrix();
		}

		if (imgui != null)
		{
			using var imguiLayoutPerf = PerfStats.Time(PerfDetail.ImGuiLayout);
			imgui.BeginLayout();
			var window = RequiredWindow;
#if IOS
			window.StopTextInput();
#else
			if (imgui.WantsTextInput)
				window.StartTextInput();
			else
				window.StopTextInput();
#endif
			if (gameEase < 1.0f && !startingPlay)
			{
				using (PerfStats.Time(PerfDetail.PaletteImGui))
					PaletteImGui();
			}
			if (!playing && !startingPlay)
			{
				using (PerfStats.Time(PerfDetail.ToolbarImGui))
					ToolbarImGui();
			}
			using (PerfStats.Time(PerfDetail.DebugMessageImGui))
				DebugViewMessageImGui();
			PlatformNavDebugOverlayImGui();
			BudDebugOverlayImGui();
			using (PerfStats.Time(PerfDetail.FpsOverlayImGui))
				FpsOverlayImGui();
			imgui.EndLayout();
		}
#endif
	}


	private void UpdateFpsOverlay()
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
		threadAllocatedBytesOverlayValue = (long)((float)(threadAllocatedBytes) / fpsOverlayFrames);
		threadAllocatedBytes = 0;
#endif

		PerfStats.Publish();
		fpsOverlayFrames = 0;
		fpsOverlayLastTimestamp = now;
	}

	private void ToggleFpsOverlay()
	{
		fpsOverlayVisible = !fpsOverlayVisible;
		game?.DisplayMessage($"FPS Overlay {(fpsOverlayVisible ? "On" : "Off")}");
	}

	private bool UpdatePerfToggles(bool keyboardShortcutsBlocked)
	{
		if (!keyboardShortcutsBlocked)
		{
			if (Input.Keyboard.Pressed(Keys.F6))
				ToggleLighting();
			if (Input.Keyboard.Pressed(Keys.F7))
				ToggleDitherAndTileField();
			if (Input.Keyboard.Pressed(Keys.F8))
				ToggleNormalBuffer();
			if (Input.Keyboard.Pressed(Keys.F9))
				TogglePostEffects();
			if (Input.Keyboard.Pressed(Keys.F10))
				ToggleEmissive();
		}

		var chordActive = false;
		foreach (var controller in Input.Controllers)
		{
			if (!controller.Connected || !PerfModifierDown(controller))
				continue;

			chordActive = true;
			if (controller.Pressed(Buttons.South))
				ToggleLighting();
			if (controller.Pressed(Buttons.East))
				ToggleDitherAndTileField();
			if (controller.Pressed(Buttons.West))
				ToggleNormalBuffer();
			if (controller.Pressed(Buttons.North))
				TogglePostEffects();
		}

		return chordActive;
	}

	private static bool PerfModifierDown(ControllerState controller)
		=> !controller.IsGamepad && controller.Down(4);

	private void ToggleLighting()
	{
		PerfToggles.Lighting = !PerfToggles.Lighting;
		ShowPerfToggleMessage("Lighting", PerfToggles.Lighting);
	}

	private void ToggleDitherAndTileField()
	{
		PerfToggles.DitherAndTileField = !PerfToggles.DitherAndTileField;
		ShowPerfToggleMessage("Dither + Tile Field", PerfToggles.DitherAndTileField);
	}

	private void ToggleNormalBuffer()
	{
		PerfToggles.NormalBuffer = !PerfToggles.NormalBuffer;
		ShowPerfToggleMessage("Normal Buffer", PerfToggles.NormalBuffer);
	}

	private void ToggleEmissive()
	{
		PerfToggles.Emissive = !PerfToggles.Emissive;
		ShowPerfToggleMessage("Emissive", PerfToggles.Emissive);
	}

	private void TogglePostEffects()
	{
		var enabled = !(PerfToggles.Glitch || PerfToggles.Crt);
		PerfToggles.Glitch = enabled;
		PerfToggles.Crt = enabled;
		ShowPerfToggleMessage("Post Effects", enabled);
	}

	private void ShowPerfToggleMessage(string name, bool enabled)
	{
		var message = $"Perf: {name} {(enabled ? "On" : "Off")}";
		Console.WriteLine($"{message} ({PerfToggles.Summary})");
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
#if EDITOR
		if (imgui != null)
			imgui.Render();
#endif
	}

	private void RenderPlayTransition()
	{
		if (game == null)
			return;

		var t = Ease.Cube.InOut(Math.Clamp(playTransition, 0.0f, 1.0f));
		var center = Vector2.Lerp(playTransitionFromCenter, playTransitionToCenter, t);
		var size = Vector2.Lerp(playTransitionFromSize, playTransitionToSize, t);
		var worldView = new Rect(center - size / 2, size);
		game.RenderWorldView((RectInt)(GameViewRect * Scale), worldView, showHud: false);
	}

#if EDITOR && DEBUG && !IOS
	private void RequestAssetReload()
	{
		lock (assetReloadLock)
		{
			assetReloadRequested = true;
			assetReloadTimer = AssetReloadDelay;
		}
	}


	private void UpdateAssetReload()
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


	private void RequestShaderReload()
	{
		lock (shaderReloadLock)
		{
			shaderReloadRequested = true;
			shaderReloadTimer = AssetReloadDelay;
		}
	}

	private void UpdateShaderReload()
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

	private void RequestAudioBankReload()
	{
		lock (audioBankReloadLock)
		{
			audioBankReloadRequested = true;
			audioBankReloadTimer = AssetReloadDelay;
		}
	}

	private void UpdateAudioBankReload()
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

	private void ReloadAudioBanks()
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

	private void CompileAndReloadShaders()
	{
		if (OperatingSystem.IsWindows())
		{
			throw new Exception("TODO: implement compiling shaders on windows with something like compile.sh");
		}

		var script = Path.GetFullPath(Path.Join(Assets.AssetsPath, "Shaders", "compile.sh"));
		if (!File.Exists(script))
		{
			Console.Error.WriteLine($"Shader compile script not found at '{script}'.");
			return;
		}

		try
		{
			var compile = new ProcessStartInfo(script)
			{
				UseShellExecute = false,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				WorkingDirectory = Path.GetDirectoryName(script) ?? Environment.CurrentDirectory
			};
			compile.ArgumentList.Add(CurrentShaderPlatform());

			using var process = Process.Start(compile);
			if (process == null)
			{
				Console.Error.WriteLine("Failed to start shader compiler.");
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

	private static string CurrentShaderPlatform()
	{
		if (OperatingSystem.IsMacOS()) return "mac";
		if (OperatingSystem.IsWindows()) return "windows";
		if (OperatingSystem.IsLinux()) return "linux";

		return "all";
	}
#endif

	private void ReloadAssets(bool reloadRooms)
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


	/// <summary>
	/// Draws a box outline (for palette / workspace)
	/// </summary>
	private void Box(Rect box)
	{
		batcher.Rect(box, 0x000000);
		batcher.RectLine(box, LineWeight, 0xffffff);
	}

	/// <summary>
	/// Draws a Pressable button with an icon and tooltip
	/// </summary>
	private bool Button(Rect button, Subtexture subtexture, string text, bool selected = false)
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

	private bool TextButton(Rect button, string text, bool selected = false)
	{
		var hovering = button.Contains(Cursor);

		batcher.Rect(button, Color.Black);
		batcher.RectRoundedLine(button, 4, LineWeight, hovering ? Color.White : (selected ? highlight : Color.Gray));

		if (Assets.Font != null)
			batcher.Text(Assets.Font, text, button.Center, new Vector2(0.50f, 0.50f), Color.White);

		return hovering && Input.Mouse.LeftPressed;
	}

	private void StartGame(bool immediate = false)
	{
#if EDITOR
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

		game?.Dispose();
		game = new Game(
			this,
			room,
			fallbackPlayerDirection: fallbackPlayerDirection,
			spawnFallbackPlayer: true,
			initialPlayerPosition: initialPlayerPosition);
#if IOS
		playTransition = 1.0f;
		playTransitionFromCenter = Vector2.Zero;
		playTransitionFromSize = Vector2.Zero;
		playTransitionToCenter = Vector2.Zero;
		playTransitionToSize = Vector2.Zero;
		gameEase = 1.0f;
		startingPlay = false;
		playing = true;
		game.SnapCameraToGameplayTarget();
#else
		if (immediate)
		{
			playTransition = 1.0f;
			playTransitionFromCenter = Vector2.Zero;
			playTransitionFromSize = Vector2.Zero;
			playTransitionToCenter = Vector2.Zero;
			playTransitionToSize = Vector2.Zero;
			gameEase = 1.0f;
			startingPlay = false;
			playing = true;
			game.SnapCameraToGameplayTarget();
		}
		else
		{
			var target = game.GetGameplayCameraTarget();
			playTransition = 0.0f;
			playTransitionFromCenter = editorCamera;
			playTransitionFromSize = GameViewRect.Size / editorZoom;
			playTransitionToCenter = target + new Vector2(Game.Width, Game.Height) / 2;
			playTransitionToSize = new Vector2(Game.Width, Game.Height);
			startingPlay = true;
			playing = false;
		}
#endif
		VirtualGameControllerVisibilityChanged?.Invoke(WantsVirtualGameController);
	}

#if EDITOR
	private bool ImGuiIconButton(string id, Subtexture icon, string? tooltip, bool selected = false, float? imagePadding = null)
		=> ImGuiSubtextureButton(id, icon, tooltip, selected, new Vector2(ButtonSize, ButtonSize), false, imagePadding);

	private bool ImGuiSubtextureButton(string id, Subtexture icon, string? tooltip, bool selected, Vector2 size, float? imagePadding = null)
		=> ImGuiSubtextureButton(id, icon, tooltip, selected, size, false, imagePadding);

	private bool ImGuiSubtextureButton(string id, Subtexture icon, string? tooltip, bool selected, Vector2 size, bool fitAspectRatio, float? imagePadding = null)
	{
		var padding = imagePadding.HasValue ? new Vector2(imagePadding.Value) : (Vector2?)null;
		var pressed = imgui.ImageButton(id, icon, size, selected, highlight, fitAspectRatio, padding);
		var hovered = ImGui.IsItemHovered();

		if (hovered && !string.IsNullOrEmpty(tooltip))
			ImGui.SetTooltip(tooltip);

		return pressed;
	}

	private bool ImGuiSubtextureButton(string id, Subtexture icon, string? tooltip, Subtexture tooltipImage, bool selected, Vector2 size, bool fitAspectRatio = false)
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

	private bool ImGuiSubtextureButton(string id, Subtexture icon, string? tooltip, Tileset? tooltipTileset, bool selected, Vector2 size, bool fitAspectRatio = false)
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

	private void DrawImGuiTilesetPreview(Tileset tileset)
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

	private static bool ImGuiTextButton(string label, Vector2 size, bool selected)
	{
		if (selected)
			PushSelectedImGuiButtonStyle();

		var pressed = ImGui.Button(label, size);

		if (selected)
			ImGui.PopStyleColor(3);

		return pressed;
	}

	private static void PushSelectedImGuiButtonStyle()
	{
		ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.14f, 0.39f, 0.45f, 1.0f));
		ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.22f, 0.54f, 0.62f, 1.0f));
		ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.10f, 0.30f, 0.35f, 1.0f));
	}

#if EDITOR
	private bool ImGuiWantsKeyboard
		=> imgui != null && (imgui.WantsKeyboardInput || imgui.WantsTextInput);

	private void ToggleGame()
	{
		if (playing || startingPlay)
			StopGame();
		else
			StartGame(immediate: true);
	}
#endif

#if IOS
	private bool UpdatePlayModePinchIn()
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

	private static bool TryGetTwoFingerDistance(TouchState touch, out float distance)
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

	private void StopGame()
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
		if (gymRuns != null)
		{
			gymRuns = null;
			Console.WriteLine("[Gym] stopped (Tab)");
		}
		VirtualGameControllerVisibilityChanged?.Invoke(WantsVirtualGameController);
		SyncEditorGame();
	}

	private void SyncEditorGame()
	{
		if (playing || startingPlay)
			return;

		game?.Dispose();
		game = CurrentRoom is { } room ? new Game(this, room, audioEventsEnabled: false, spawnBud: false) : null;
	}

	private static void OpenFmodStudio()
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
}
