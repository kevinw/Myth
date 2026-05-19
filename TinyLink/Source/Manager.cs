using System.Diagnostics;
using System.Drawing;
using System.Numerics;
using System.Text.Json;
using Foster.Framework;
using ImGuiNET;
using Color = Foster.Framework.Color;

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
/// Runs the Game and, when the game is not running, displays a tile-based Level Editor (see Manager.Editor.cs)
/// </summary>
public partial class Manager : App
{
	public const string DefaultMusicArea = "Calm";
	public const string BackgroundMusicAreaParameter = "Area";

	private const string WindowSettingsFileName = "window.json";
	private const string FmodProjectPath = "Sounds/FMOD/TinyLink.fspro";
	private readonly Point MinimumRestoredWindowSize = new (640, 360);

	static float Scale => 3.0f;
	static float ButtonSize => 24;
	static float ButtonSpacing => 2;
	static float BoxPadding => 4;
	static float BoxSpacing => 0;
	static float ScreenPadding => 0;

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
	public bool LightingDitherEnabled = true;
	public float LightingDitherScale = 1.0f;
	public float LightingDitherStrength = 0.155f;

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
	bool musicEnabled = true;
	bool alwaysOnTop;

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
#if DEBUG
		StartAssetWatcher();
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
			if (settings == null) return;
			if (settings is { Width: > 0, Height: > 0 })
				Window.Size = new Point2(
					Math.Max(settings.Width, MinimumRestoredWindowSize.X),
					Math.Max(settings.Height, MinimumRestoredWindowSize.Y));
			Window.Position = new Point2(settings.X, settings.Y);
			cell = new Point2(settings.RoomX, settings.RoomY);
			selectedRoomId = settings.RoomId;
			musicEnabled = settings.MusicEnabled;
			alwaysOnTop = settings.AlwaysOnTop;
			Window.AlwaysOnTop = alwaysOnTop;
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
		UpdateAssetReload();
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
				game?.SnapCameraToGameplayTarget();
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
		DebugViewMessageImGui();
		imgui.EndLayout();
	}

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

	private void RequestAssetReload()
	{
		lock (assetReloadLock)
		{
			assetReloadRequested = true;
			assetReloadTimer = AssetReloadDelay;
		}
	}

#if DEBUG

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
