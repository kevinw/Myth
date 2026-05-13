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
		=> new (View.X, View.Y, View.Width, ToolbarHeight);

	/// <summary>
	/// Workspace, which is the full space below the Toolbar
	/// </summary>
	private Rect WorkRect
		=> new (View.X, View.Y + ToolbarSpacing + ToolbarHeight, View.Width, View.Height - ToolbarHeight - ToolbarSpacing);

	/// <summary>
	/// Palette View when the Editor is Open
	/// </summary>
	private Rect PaletteOpenRect
		=> new (WorkRect.X, WorkRect.Y, PaletteWidth, WorkRect.Height);

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

	private Rect GameViewRect => WorkspaceRect.Inflate(-BoxPadding * 2);

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

		GraphicsDevice.VSync = true; // Make this an option? SDL3 crashes my mac when I fullscreen the window... @kev 2026

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
		game?.StopMusic();
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

	private void SelectRoom(Point2 targetCell)
	{
		cell = targetCell;
		selectedRoomId = Assets.GetRoom(targetCell)?.Id;
		roomCreatePosition = new Point2(targetCell.X * Game.Columns, targetCell.Y * Game.Rows);
	}

	private void SelectEmptyRoomPosition(Point2 position)
	{
		selectedRoomId = null;
		cell = new Point2(position.X / Game.Columns, position.Y / Game.Rows);
		roomCreatePosition = position;
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
			var currentRoom = game?.CurrentRoom ?? CurrentRoom;
			var currentCell = currentRoom?.Cell ?? cell;
			var settings = new WindowSettings
			{
				X = position.X,
				Y = position.Y,
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
		public string? RoomId { get; init; }
		public int RoomX { get; init; }
		public int RoomY { get; init; }
		public bool MusicEnabled { get; init; } = true;
		public bool AlwaysOnTop { get; init; }
	}

	protected override void Update()
	{
#if DEBUG
		UpdateWindowTitleFps();
#endif
		UpdateAssetReload();
#if DEBUG
		UpdateShaderReload();
#endif

		// Misc. Hotkeys
		if (Input.Keyboard.Pressed(Keys.F1) && !playing && !startingPlay)
			ReloadAssets(true);
		if (Input.Keyboard.Pressed(Keys.F4))
			Window.Fullscreen = !Window.Fullscreen;
		if (Input.Keyboard.Pressed(Keys.Escape))
		{
			CurrentRoom?.Save();
			Exit();
		}
		if (ToggleEditorPressed())
			ToggleGame();

		// Run Game
		if (startingPlay)
		{
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
			gameEase = Calc.Approach(gameEase, 1.0f, Time.Delta * 4.0f);
			game?.Update();
		}
		// Show Editor
		else
		{
			gameEase = Calc.Approach(gameEase, 0.0f, Time.Delta * 4.0f);
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
		else
			RenderEditorActiveRoom();
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
	/// Shifts to display a new room
	/// </summary>
	private void ShiftRoom(Point2 direction)
	{
		var current = CurrentRoom;
		if (current == null)
			return;

		var exitPosition = EditorExitPosition(current, direction);
		if (Assets.FindAdjacentRoom(current, direction, exitPosition) is not { } adjacent)
			return;

		current.Save();
		SelectRoom(adjacent);
		SyncEditorGame();
		FocusEditorOnCurrentRoom();
	}

	private static Vector2 EditorExitPosition(Room room, Point2 direction)
	{
		var bounds = room.WorldBounds;
		if (direction.X > 0)
			return new Vector2(bounds.Right, bounds.CenterY);
		if (direction.X < 0)
			return new Vector2(bounds.Left - 1, bounds.CenterY);
		if (direction.Y > 0)
			return new Vector2(bounds.CenterX, bounds.Bottom);
		if (direction.Y < 0)
			return new Vector2(bounds.CenterX, bounds.Top - 1);

		return bounds.Center;
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

		if (ImGuiIconButton("SmallBrush", Assets.GetSubtexture("buttons/7"), "Small Brush", !brushBig))
			brushBig = false;
		ImGui.SameLine();
		if (ImGuiIconButton("BigBrush", Assets.GetSubtexture("buttons/8"), "Big Brush", brushBig))
			brushBig = true;
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

		if (CurrentRoom is { } room && musicAreas.Count > 0)
		{
			ImGui.SameLine(0, ToolbarSpacing);
			var area = room.MusicArea ?? DefaultMusicArea;
			if (ImGuiTextButton($"{area}##MusicArea", new Vector2(96, ButtonSize), room.MusicArea != null))
				CycleMusicArea(room);
		}

		var displayedRoom = CurrentRoom;
		DrawCenteredToolbarText(displayedRoom != null ? $"Room {displayedRoom.Id}" : $"Room {cell.X} x {cell.Y}");

		ImGui.End();
		ImGui.PopStyleColor();
		ImGui.PopStyleVar(3);
	}

	private bool ImGuiIconButton(string id, Subtexture icon, string tooltip, bool selected = false)
		=> ImGuiSubtextureButton(id, icon, tooltip, selected, new Vector2(ButtonSize, ButtonSize));

	private bool ImGuiSubtextureButton(string id, Subtexture icon, string tooltip, bool selected, Vector2 size)
	{
		var pressed = imgui.ImageButton(id, icon, size, selected, highlight);
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

	private static void DrawCenteredToolbarText(string text)
	{
		var size = ImGui.CalcTextSize(text);
		var windowPos = ImGui.GetWindowPos();
		var windowSize = ImGui.GetWindowSize();
		var pos = new Vector2(
			windowPos.X + windowSize.X * 0.5f - size.X * 0.5f,
			windowPos.Y + windowSize.Y * 0.5f - size.Y * 0.5f);
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
			game?.StopMusic(true);
			game = new Game(this, room);
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
			SelectRoom(room);
		MatchEditorCameraToGame();
		game?.StopMusic(true);
		startingPlay = false;
		playing = false;
		SyncEditorGame();
	}

	private void SyncEditorGame()
	{
		if (playing || startingPlay)
			return;

		game?.StopMusic(true);
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

	void CycleMusicArea(Room room)
	{
		if (musicAreas.Count <= 0)
			return;

		var current = room.MusicArea ?? DefaultMusicArea;
		var index = musicAreas.IndexOf(current);
		if (index < 0)
			index = 0;

		room.MusicArea = musicAreas[(index + 1) % musicAreas.Count];
	}

}
