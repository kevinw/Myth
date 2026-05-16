using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using Foster.Framework;
using ImGuiNET;

namespace TinyLink;

internal abstract class Program
{
	public static void Main()
	{
		using var manager = new Manager();
		manager.Run();
	}
}

/// <summary>
/// Runs the Game and, when the game is not running, displays a tiny tile-based Level Editor
/// </summary>
public partial class Manager : App
{
	public const string DefaultMusicArea = "Calm";
	public const string BackgroundMusicAreaParameter = "Area";

	private const float AssetReloadDelay = 0.25f;
	private const float PlayTransitionDuration = 0.25f;

	private const string WindowSettingsFileName = "window.json";
	private const string FmodProjectPath = "Sounds/FMOD/TinyLink.fspro";
	private const int MinimumRestoredWindowWidth = 640;
	private const int MinimumRestoredWindowHeight = 360;

	static float Scale => 3.0f;
	static float ButtonSize => 24;
	static float ButtonSpacing => 2;
	static float BoxPadding => 4;
	static float BoxSpacing => 0;
	static int PaletteColumns => 4;
	static int ToolbarSpacing => 4;
	static float ScreenPadding => 0;
	static float ToolbarHeight => ButtonSize + 4;
	static float EditorInset => 0;
	static float PaletteWidth => BoxPadding * 2 + PaletteColumns * ButtonSize + (PaletteColumns - 1) * ButtonSpacing;
	static Color highlight => 0x5fcde4;

	/// <summary>
	/// The current Room Cell we're viewing, used as a compatibility cursor for old room navigation.
	/// </summary>
	private Point2 cell = new(0, 0);
	private string? selectedRoomId;
	private Point2 roomCreatePosition;
	private Point2? editorRoomEntryDirection;
	private (string RoomId, Point2 Position)? playtestPlayerPosition;
	private bool editingRoomName;
	private bool focusRoomNameEditor;
	private string roomNameEditor = string.Empty;
	private string? roomNameEditingRoomId;
	private string? pendingDeleteRoomId;

	/// <summary>
	/// The room renderer / simulation.
	/// </summary>
	Game? game;
	bool playing;
	bool startingPlay;
	float playTransition;
	Vector2 playTransitionFromCenter;
	Vector2 playTransitionFromSize;
	Vector2 playTransitionToCenter;
	Vector2 playTransitionToSize;

	/// <summary>
	/// Eases in/out as the Game starts/stops playing
	/// </summary>
	float gameEase = 1.0f;

	/// <summary>
	/// Sprite Batcher for general Editor visuals
	/// </summary>
	private readonly Batcher batcher;
	private readonly Controls controls;
	private readonly ImGuiRenderer imgui;

	/// <summary>
	/// Visible Space
	/// </summary>
	private Rect View
		=> new Rect(0, 0, Window.WidthInPixels / Scale, Window.HeightInPixels / Scale).Inflate(-ScreenPadding);

	/// <summary>
	/// Toolbar Space
	/// </summary>
	private Rect ToolbarRect
		=> new(View.X, View.Y, View.Width, ToolbarHeight);

	/// <summary>
	/// Workspace, which is the full space below the Toolbar
	/// </summary>
	private Rect WorkRect
		=> new(View.X, View.Y + ToolbarSpacing + ToolbarHeight, View.Width, View.Height - ToolbarHeight - ToolbarSpacing);

	/// <summary>
	/// Palette View when the Editor is Open
	/// </summary>
	private Rect PaletteOpenRect
		=> new(WorkRect.X, WorkRect.Y, PaletteWidth, WorkRect.Height);

	/// <summary>
	/// Edit Workspace when the Editor is Open
	/// </summary>
	private Rect EditorOpenRect
		=> new Rect(WorkRect.X + PaletteWidth + BoxSpacing, WorkRect.Y, WorkRect.Width - PaletteWidth - BoxSpacing, WorkRect.Height).Inflate(-EditorInset);

	/// <summary>
	/// Play Workspace when the Game is Open
	/// </summary>
	private Rect GameOpenRect => View;

	/// <summary>
	/// Eases between the EditView and the PlayView as the Game opens/closes
	/// </summary>
	private Rect WorkspaceRect
	{
		get
		{
			var a = Vector2.Lerp(EditorOpenRect.TopLeft, GameOpenRect.TopLeft, Ease.Cube.InOut(gameEase));
			var b = Vector2.Lerp(EditorOpenRect.BottomRight, GameOpenRect.BottomRight, Ease.Cube.InOut(gameEase));
			return Rect.Between(a, b);
		}
	}

	private Rect GameViewRect => WorkspaceRect;

	public Vector2 GameMouseWorld(Game gameInstance)
	{
		var viewport = (RectInt)(GameViewRect * Scale);


		var scale = Calc.Min(viewport.Width / (float)gameInstance.Screen.Width, viewport.Height / (float)gameInstance.Screen.Height);

		var aspect = viewport.Size / gameInstance.Screen.SizeInPixels();
		var scale2 = Calc.Min(aspect.X, aspect.Y);

		var size = new Vector2(gameInstance.Screen.Width, gameInstance.Screen.Height) * scale;
		var topLeft = (Vector2)viewport.Center - size / 2;
		var local = (Input.Mouse.Position - topLeft) / scale;
		return gameInstance.Camera + local;
	}

	/// <summary>
	/// Eases the PaletteView out/in as the Game opens/closes
	/// </summary>
	/// <value></value>
	private Rect PaletteSlideRect =>
		PaletteOpenRect - Vector2.UnitX * Ease.Cube.InOut(gameEase) * PaletteOpenRect.Right;

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
	FileSystemWatcher? assetWatcher;
#if DEBUG
	FileSystemWatcher? shaderWatcher;
#endif
	bool assetReloadRequested;
	float assetReloadTimer;
#if DEBUG
	readonly Lock shaderReloadLock = new();
	bool shaderReloadRequested;
	float shaderReloadTimer;
#endif
	readonly List<string> musicAreas = [];
	bool musicEnabled = true;
	bool alwaysOnTop;
#if DEBUG
	int fpsFrames;
	float fpsElapsed;
#endif

	public bool MusicEnabled => musicEnabled;
	public const string BackgroundMusicEventPath = "event:/background_music";

	public Manager() : base(new AppConfig()
	{
		ApplicationName = "Myth",
		WindowTitle = "Myth",
		Width = 1280,
		Height = 720,
		Resizable = true
	})
	{
		batcher = new(GraphicsDevice);
		controls = new(Input);
		imgui = new(this) { Scale = Scale };
	}

	protected override void Startup()
	{
		LoadWindowSettings();

		GraphicsDevice.VSync = true; // Make this an option? SDL3 crashes my Mac when I fullscreen the window... @kev 2026

		Assets.Load(GraphicsDevice);
		ResolveSelectedRoom();
		Factory.RegisterTypes();
		Assets.RebuildRoomGeometry();
		LoadAudioBanks();
		StartAssetWatcher();
#if DEBUG
		StartShaderWatcher();
#endif

		SyncEditorGame();
	}

	protected override void Shutdown()
	{
		SaveWindowSettings();
		game?.Dispose();
		ClearRoomPreviewCache(deferDisposal: false);
		DisposeDeferredRoomPreviews();
		DisposeEditorCursors();
		imgui.Dispose();
		assetWatcher?.Dispose();
		assetWatcher = null;
#if DEBUG
		shaderWatcher?.Dispose();
		shaderWatcher = null;
#endif

		Assets.Unload();
		Factory.Clear();
	}

	private string WindowSettingsPath => Path.Join(UserPath, WindowSettingsFileName);

	private void SelectRoom(Room room)
	{
		selectedRoomId = room.Id;
		cell = room.Cell;
		roomCreatePosition = room.Position;
	}

	private void ResolveSelectedRoom()
	{
		if (selectedRoomId != null && Assets.GetRoom(selectedRoomId) is { } byId)
		{
			SelectRoom(byId);
			return;
		}

		if (Assets.GetRoom(cell) is { } byCell)
		{
			SelectRoom(byCell);
			return;
		}

		if (Assets.GetRoom(Point2.Zero) is { } origin)
		{
			SelectRoom(origin);
			return;
		}

		selectedRoomId = null;
		cell = Point2.Zero;
		roomCreatePosition = Point2.Zero;
	}

	private void LoadAudioBanks()
	{
		var fmodPath = Path.Join(Assets.AssetsPath, "Sounds", "FMOD");
		if (!Directory.Exists(fmodPath))
		{
			Console.Error.WriteLine("Sounds/FMOD not found.");
			return;
		}

		var banks = Directory
			.EnumerateFiles(fmodPath, "*.bank", SearchOption.AllDirectories)
			.OrderBy(BankLoadOrder)
			.ThenBy(Path.GetFileName)
			.ToArray();

		if (banks.Length <= 0)
		{
			Console.Error.WriteLine($"No FMOD banks found under '{Path.GetFullPath(fmodPath)}'. Build banks from the FMOD Studio project before playing events.");
			musicAreas.Clear();
			musicAreas.Add(DefaultMusicArea);
			return;
		}

		try
		{
			Audio.Initialize(liveUpdate:
#if DEBUG
				true
#else
				false
#endif
			);
			foreach (var bank in banks)
				Audio.LoadBank(bank);
			LoadMusicAreas();
		}
		catch (Exception e)
		{
			Console.Error.WriteLine($"Failed to load FMOD banks: {e}");
			musicAreas.Clear();
			musicAreas.Add(DefaultMusicArea);
		}
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
			if (settings != null)
			{
				if (settings.Width > 0 && settings.Height > 0)
					Window.Size = new Point2(
						Math.Max(settings.Width, MinimumRestoredWindowWidth),
						Math.Max(settings.Height, MinimumRestoredWindowHeight));
				Window.Position = new Point2(settings.X, settings.Y);
				cell = new Point2(settings.RoomX, settings.RoomY);
				selectedRoomId = settings.RoomId;
				musicEnabled = settings.MusicEnabled;
				alwaysOnTop = settings.AlwaysOnTop;
				Window.AlwaysOnTop = alwaysOnTop;
			}
		}
		catch (Exception e)
		{
			Console.Error.WriteLine($"Failed to load window settings: {e}");
		}
	}

	private void SaveWindowSettings()
	{
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
		public string? RoomId { get; init; }
		public int RoomX { get; init; }
		public int RoomY { get; init; }
		public bool MusicEnabled { get; init; } = true;
		public bool AlwaysOnTop { get; init; }
	}

	protected override void Update()
	{
		DisposeDeferredRoomPreviews();
#if DEBUG
		UpdateWindowTitleFps();
#endif
		UpdateAssetReload();
#if DEBUG
		UpdateShaderReload();
#endif

		var keyboardShortcutsBlocked = ImGuiWantsKeyboard;

		// Misc. Hotkeys
		if (!keyboardShortcutsBlocked && Input.Keyboard.Pressed(Keys.F1) && !playing && !startingPlay)
			ReloadAssets(true);
#if DEBUG
		if (!keyboardShortcutsBlocked && Input.Keyboard.Pressed(Keys.F))
			Window.Fullscreen = !Window.Fullscreen;
#endif
		if (!keyboardShortcutsBlocked && Input.Keyboard.Pressed(Keys.F4))
			Window.Fullscreen = !Window.Fullscreen;
		if (!keyboardShortcutsBlocked && Input.Keyboard.Pressed(Keys.Escape))
		{
			CurrentRoom?.Save();
			Exit();
		}
		if (!keyboardShortcutsBlocked && ToggleEditorPressed())
			ToggleGame();

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
				if (game != null)
					game.SnapCameraToGameplayTarget();
			}
		}
		else if (playing)
		{
			SetMouseCursor(null);
			gameEase = Calc.Approach(gameEase, 1.0f, Time.Delta * 4.0f);
			game?.Update(!keyboardShortcutsBlocked);
		}
		// Show Editor
		else
		{
			gameEase = Calc.Approach(gameEase, 0.0f, Time.Delta * 4.0f);
			if (!keyboardShortcutsBlocked)
				UpdateEditorNavigation();
		}

		// Build our Sprite Batch
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

		imgui.BeginLayout();
		if (imgui.WantsTextInput)
			Window.StartTextInput();
		else
			Window.StopTextInput();
		if (gameEase < 1.0f && !startingPlay)
			PaletteImGui();
		if (!playing && !startingPlay)
			ToolbarImGui();
		imgui.EndLayout();
	}

#if DEBUG
	private void UpdateWindowTitleFps()
	{
		fpsFrames++;
		fpsElapsed += Time.Delta;
		if (fpsElapsed < 1.0f)
			return;

		var fps = fpsFrames / fpsElapsed;
		Window.Title = $"Myth - {fps:0} FPS";
		fpsFrames = 0;
		fpsElapsed = 0;
	}
#endif

	protected override void Render()
	{
		Window.Clear(0x2e1426);

		if (startingPlay)
			RenderPlayTransition();
		else if (playing)
			game?.Render((RectInt)(GameViewRect * Scale), showHud: true);
		batcher.Render(Window);
		imgui.Render();
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

	private void StartAssetWatcher()
	{
		assetWatcher = new FileSystemWatcher(Path.GetFullPath(Assets.AssetsPath), "*.ase")
		{
			IncludeSubdirectories = true,
			NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime
		};

		assetWatcher.Changed += (_, _) => RequestAssetReload();
		assetWatcher.Created += (_, _) => RequestAssetReload();
		assetWatcher.Deleted += (_, _) => RequestAssetReload();
		assetWatcher.Renamed += (_, _) => RequestAssetReload();
		assetWatcher.EnableRaisingEvents = true;
	}

#if DEBUG
	private void StartShaderWatcher()
	{
		var shadersPath = Path.GetFullPath(Path.Join(Assets.AssetsPath, "Shaders"));
		if (!Directory.Exists(shadersPath))
			return;

		shaderWatcher = new FileSystemWatcher(shadersPath, "*.hlsl")
		{
			IncludeSubdirectories = false,
			NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime
		};

		shaderWatcher.Changed += (_, _) => RequestShaderReload();
		shaderWatcher.Created += (_, _) => RequestShaderReload();
		shaderWatcher.Deleted += (_, _) => RequestShaderReload();
		shaderWatcher.Renamed += (_, _) => RequestShaderReload();
		shaderWatcher.EnableRaisingEvents = true;
	}
#endif

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

#if DEBUG
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

	private void CompileAndReloadShaders()
	{
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
		if (OperatingSystem.IsMacOS())
			return "mac";
		if (OperatingSystem.IsWindows())
			return "windows";
		if (OperatingSystem.IsLinux())
			return "linux";

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
			ClearRoomPreviewCache();
			ResolveSelectedRoom();
			if (playing)
				game?.RefreshRoomTiles();
			else
				SyncEditorGame();

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

	/// <summary>
	/// Displays the Toolbar
	/// </summary>
	private void ToolbarImGui()
	{
		var flags =
			ImGuiWindowFlags.NoDecoration |
			ImGuiWindowFlags.NoMove |
			ImGuiWindowFlags.NoSavedSettings |
			ImGuiWindowFlags.NoScrollbar |
			ImGuiWindowFlags.NoScrollWithMouse |
			ImGuiWindowFlags.NoBringToFrontOnFocus;

		ImGui.SetNextWindowPos(Vector2.Zero, ImGuiCond.Always);
		ImGui.SetNextWindowSize(new Vector2(Window.WidthInPixels / imgui.Scale, ToolbarHeight), ImGuiCond.Always);

		ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(2, 2));
		ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(ButtonSpacing, 0));
		ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, Vector2.Zero);
		ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.18f, 0.08f, 0.15f, 1.0f));
		ImGui.Begin("Toolbar", flags);

		if (ImGuiIconButton("Play", Assets.GetSubtexture("buttons/0"), "Play [Tab/R3]"))
			StartGame();
		ImGui.SameLine();

		if (ImGuiIconButton("SmallBrush", Assets.GetSubtexture("buttons/7"), "Smaller Brush", brushSize <= 1))
			brushSize = Math.Max(1, brushSize - 1);
		ImGui.SameLine();
		if (ImGuiIconButton("BigBrush", Assets.GetSubtexture("buttons/8"), "Larger Brush", brushSize > 1))
			brushSize = Math.Min(10, brushSize + 1);
		ImGui.SameLine(0, ToolbarSpacing);

		if (ImGuiIconButton("Music", Assets.GetSubtexture(musicEnabled ? "buttons/9" : "buttons/10"), "Music", musicEnabled))
		{
			musicEnabled = !musicEnabled;
			game?.RefreshMusic();
		}
		ImGui.SameLine();

		if (ImGuiIconButton("AlwaysOnTop", Assets.GetSubtexture(alwaysOnTop ? "buttons/11" : "buttons/12"), "Always on Top", alwaysOnTop))
		{
			alwaysOnTop = !alwaysOnTop;
			Window.AlwaysOnTop = alwaysOnTop;
		}
		ImGui.SameLine();

		if (ImGuiIconButton("OpenFmodStudio", Assets.GetSubtexture("buttons/13"), "Open FMOD Studio"))
			OpenFmodStudio();

		DrawCenteredRoomName(CurrentRoom);
		DrawDeleteRoomPopup();

		ImGui.End();
		ImGui.PopStyleColor();
		ImGui.PopStyleVar(3);
	}

	private bool ImGuiIconButton(string id, Subtexture icon, string tooltip, bool selected = false)
		=> ImGuiSubtextureButton(id, icon, tooltip, selected, new Vector2(ButtonSize, ButtonSize));

	private bool ImGuiSubtextureButton(string id, Subtexture icon, string tooltip, bool selected, Vector2 size)
		=> ImGuiSubtextureButton(id, icon, tooltip, selected, size, false);

	private bool ImGuiSubtextureButton(string id, Subtexture icon, string tooltip, bool selected, Vector2 size, bool fitAspectRatio)
	{
		var pressed = imgui.ImageButton(id, icon, size, selected, highlight, fitAspectRatio);
		var hovered = ImGui.IsItemHovered();

		if (hovered)
			ImGui.SetTooltip(tooltip);

		return pressed;
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

	private void DrawCenteredRoomName(Room? room)
	{
		if (room == null)
		{
			DrawCenteredToolbarText($"Room {cell.X} x {cell.Y}");
			return;
		}

		if (editingRoomName && roomNameEditingRoomId != room.Id)
			CancelRoomRename();

		if (editingRoomName)
		{
			var width = Math.Max(160, ImGui.CalcTextSize(roomNameEditor).X + 24);
			var windowPos = ImGui.GetWindowPos();
			var windowSize = ImGui.GetWindowSize();
			var pos = new Vector2(
				windowPos.X + windowSize.X * 0.5f - width * 0.5f,
				windowPos.Y + windowSize.Y * 0.5f - ImGui.GetFrameHeight() * 0.5f);

			ImGui.SetCursorScreenPos(pos);
			ImGui.SetNextItemWidth(width);
			if (focusRoomNameEditor)
			{
				ImGui.SetKeyboardFocusHere();
				focusRoomNameEditor = false;
			}

			var commit = ImGui.InputText("##RoomNameEditor", ref roomNameEditor, 128, ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.AutoSelectAll);
			var cancel = ImGui.IsKeyPressed(ImGuiKey.Escape);
			if (cancel)
				CancelRoomRename();
			else if (commit || ImGui.IsItemDeactivated())
				CommitRoomRename(room);
			return;
		}

		DrawCenteredToolbarText(room.Id, canDoubleClick: true);
		var textHovered = ImGui.IsItemHovered();
		if (textHovered && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
			BeginRoomRename(room);

		var deleteSize = new Vector2(ButtonSize, ButtonSize);
		var deletePos = ImGui.GetItemRectMax() + new Vector2(ToolbarSpacing, -ButtonSize * 0.5f - ImGui.GetItemRectSize().Y * 0.5f);
		var deleteRect = new Rect(deletePos, deleteSize);
		var mouse = ImGui.GetMousePos();
		var showDelete = textHovered || deleteRect.Contains(mouse);
		if (showDelete)
		{
			ImGui.SetCursorScreenPos(deletePos);
			if (ImGuiSubtextureButton("DeleteRoom", Assets.GetSubtexture("buttons/14"), "Delete room", false, deleteSize))
			{
				pendingDeleteRoomId = room.Id;
				ImGui.OpenPopup("Delete Room?");
			}
		}
	}

	private void BeginRoomRename(Room room)
	{
		editingRoomName = true;
		focusRoomNameEditor = true;
		roomNameEditor = room.Id;
		roomNameEditingRoomId = room.Id;
	}

	private void CancelRoomRename()
	{
		editingRoomName = false;
		focusRoomNameEditor = false;
		roomNameEditor = string.Empty;
		roomNameEditingRoomId = null;
	}

	private void CommitRoomRename(Room room)
	{
		var next = roomNameEditor.Trim();
		if (!Assets.CanRenameRoom(room, next))
		{
			roomNameEditor = room.Id;
			CancelRoomRename();
			return;
		}

		if (Assets.RenameRoom(room, next))
			selectedRoomId = room.Id;

		CancelRoomRename();
	}

	private void DrawDeleteRoomPopup()
	{
		var open = true;
		if (!ImGui.BeginPopupModal("Delete Room?", ref open, ImGuiWindowFlags.AlwaysAutoResize))
			return;

		var room = pendingDeleteRoomId != null ? Assets.GetRoom(pendingDeleteRoomId) : null;
		ImGui.Text(room != null ? $"Delete room '{room.Id}'?" : "Room no longer exists.");
		ImGui.Spacing();

		if (ImGui.Button("Delete", new Vector2(90, 0)))
		{
			if (room != null)
				DeleteRoom(room);
			pendingDeleteRoomId = null;
			ImGui.CloseCurrentPopup();
		}
		ImGui.SameLine();
		if (ImGui.Button("Cancel", new Vector2(90, 0)) || !open)
		{
			pendingDeleteRoomId = null;
			ImGui.CloseCurrentPopup();
		}

		ImGui.EndPopup();
	}

	private void DeleteRoom(Room room)
	{
		if (CurrentRoom == room)
			CurrentRoom?.Save();

		Assets.DeleteRoom(room);
		if (selectedRoomId == room.Id)
			selectedRoomId = null;
		if (playtestPlayerPosition is { } remembered && remembered.RoomId == room.Id)
			playtestPlayerPosition = null;
		InvalidateRoomPreview(room);
		ResolveSelectedRoom();
		SyncEditorGame();
	}

	private static void DrawCenteredToolbarText(string text, bool canDoubleClick = false)
	{
		var size = ImGui.CalcTextSize(text);
		var windowPos = ImGui.GetWindowPos();
		var windowSize = ImGui.GetWindowSize();
		var pos = new Vector2(
			windowPos.X + windowSize.X * 0.5f - size.X * 0.5f,
			windowPos.Y + windowSize.Y * 0.5f - size.Y * 0.5f);
		if (canDoubleClick)
		{
			ImGui.SetCursorScreenPos(pos);
			ImGui.InvisibleButton("##CenteredToolbarText", size);
		}
		ImGui.GetWindowDrawList().AddText(pos, ImGui.GetColorU32(ImGuiCol.Text), text);
	}

	private bool ToggleEditorPressed()
	{
		if (Input.Keyboard.Pressed(Keys.Tab))
			return true;

		foreach (var controller in Input.Controllers)
			if (controller.Connected && controller.Pressed(Buttons.RightStick))
				return true;

		return false;
	}

	private bool ImGuiWantsKeyboard
		=> imgui.WantsKeyboardInput || imgui.WantsTextInput;

	private void ToggleGame()
	{
		if (playing || startingPlay)
			StopGame();
		else
			StartGame();
	}

	private void StartGame()
	{
		CurrentRoom?.Save();
		if (CurrentRoom is { } room)
		{
			game?.Dispose();
			game = new Game(
				this,
				room,
				fallbackPlayerDirection: editorRoomEntryDirection,
				spawnFallbackPlayer: true,
				initialPlayerPosition: playtestPlayerPosition is { } remembered && remembered.RoomId == room.Id ? remembered.Position : null);
			var target = game.GetGameplayCameraTarget();
			playTransition = 0.0f;
			playTransitionFromCenter = editorCamera;
			playTransitionFromSize = GameViewRect.Size / editorZoom;
			playTransitionToCenter = target + new Vector2(Game.Width, Game.Height) / 2;
			playTransitionToSize = new Vector2(Game.Width, Game.Height);
			startingPlay = true;
			playing = false;
		}
	}

	private void StopGame()
	{
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
		SelectEditorActorTool();
		SyncEditorGame();
	}

	private void SyncEditorGame()
	{
		if (playing || startingPlay)
			return;

		game?.Dispose();
		game = CurrentRoom is { } room ? new Game(this, room, audioEventsEnabled: false) : null;
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

}
