using System.Numerics;
using Foster.Framework;
using ImGuiNET;

namespace Myth;

public partial class Manager
{
	enum GameMenuPage
	{
		Closed,
		Title,
		Pause,
		Options
	}

	enum GameMenuCommand
	{
		Resume,
		Start,
		Map,
		Restart,
		ConfirmRestart,
		Options,
		Music,
		Fullscreen,
		Back,
		Editor,
		Title,
		Quit
	}

	Game? previewGame;

	GameMenuPage gameMenuPage;
	GameMenuPage gameMenuReturnPage;
	int gameMenuSelection;
	bool confirmRestart = false;

	// When true, the next ImGui menu build forces keyboard/gamepad nav focus onto
	// gameMenuSelection (set when a page is shown so focus lands sensibly).
	bool menuFocusDirty;

	// Whether BuildGameMenu produced draw data this frame; gates menuImgui.Render()
	// so a just-closed menu doesn't get re-flushed from stale draw data.
	bool menuLayoutBuiltThisFrame;

	bool GameMenuOpen => gameMenuPage != GameMenuPage.Closed;
	bool GameMenuPausesGame => GameMenuOpen && gameMenuPage != GameMenuPage.Title;

	static float MenuButtonWidth => 110f;
	static float MenuButtonHeight => 18f;
	static float MenuButtonGap => 3f;
	static float MenuPanelWidth => 174f;

	void ShowTitleMenu()
	{
		gameMenuPage = GameMenuPage.Title;
		gameMenuReturnPage = GameMenuPage.Title;
		gameMenuSelection = 0;
		menuFocusDirty = true;
		playOnStartup = false;
		playing = false;
		startingPlay = false;
		confirmRestart = false;
	}

	void ShowPauseMenu()
	{
		MenuAudioEvent("menu/show");
		gameMenuPage = GameMenuPage.Pause;
		gameMenuReturnPage = GameMenuPage.Pause;
		gameMenuSelection = 0;
		menuFocusDirty = true;
		confirmRestart = false;
	}

	void CloseGameMenu()
	{
		previewGame?.Dispose();
		previewGame = null;

		previewTarget?.Dispose();
		previewTarget = null;

		gameMenuPage = GameMenuPage.Closed;
		gameMenuSelection = 0;
		confirmRestart = false;
	}

	void MenuAudioEvent(string path) => Audio.CreateEvent($"event:/{path}")?.Start();

	// Input-phase handling that is NOT navigation: opening the pause menu, and the
	// back/close gesture. Movement and activation are owned by ImGui nav (see
	// BuildGameMenu), so they are intentionally absent here.
	void UpdateGameMenuInput()
	{
		if (!GameMenuOpen)
		{
			if (playing && !startingPlay && game?.MinimapOpen != true && controls.Map.ConsumePress())
				ShowPauseMenu();
			return;
		}

		// Escape / B back out a page; the pause (Start) button toggles the menu
		// closed, mirroring how it opened it.
		var pausePressed = controls.Map.ConsumePress();
		if (Input.Keyboard.Pressed(Keys.Escape) || ControllerPressed(Buttons.East) || pausePressed)
		{
			MenuAudioEvent("menu/back");
			if (gameMenuPage == GameMenuPage.Options)
				ShowGameMenuPage(gameMenuReturnPage);
			else if (gameMenuPage == GameMenuPage.Pause)
				CloseGameMenu();
		}
	}

	bool ControllerPressed(Buttons button) => Input.AnyControllerButtonPressed(button);

	void ShowGameMenuPage(GameMenuPage page)
	{
		gameMenuPage = page;
		gameMenuSelection = Math.Clamp(gameMenuSelection, 0, GameMenuItemCount(page) - 1);
		menuFocusDirty = true;
	}

	static int GameMenuItemCount(GameMenuPage page)
		=> page switch
		{
			GameMenuPage.Title => TitleMenuItemCount,
			GameMenuPage.Pause => PauseMenuItemCount,
			GameMenuPage.Options => OptionsMenuItemCount,
			_ => 0
		};

#if BROWSER
	const int TitleMenuItemCount = 2;
#else
	const int TitleMenuItemCount = 3;
#endif

#if EDITOR
	const int PauseMenuItemCount = 5;
#else
	const int PauseMenuItemCount = 5;
#endif

#if IOS || BROWSER
	const int OptionsMenuItemCount = 2;
#else
	const int OptionsMenuItemCount = 3;
#endif

	GameMenuCommand GameMenuCommandAt(GameMenuPage page, int index)
	{
		return page switch
		{
			GameMenuPage.Title => index switch
			{
				0 => GameMenuCommand.Start,
				1 => GameMenuCommand.Options,
				_ => GameMenuCommand.Quit
			},
			GameMenuPage.Pause => index switch
			{
				0 => GameMenuCommand.Resume,
				1 => GameMenuCommand.Map,
				2 => confirmRestart ? GameMenuCommand.ConfirmRestart : GameMenuCommand.Restart,
				3 => GameMenuCommand.Options,
#if EDITOR
				_ => GameMenuCommand.Editor
#elif BROWSER
				_ => GameMenuCommand.Title
#else
				_ => GameMenuCommand.Quit
#endif
			},
			GameMenuPage.Options => index switch
			{
				0 => GameMenuCommand.Music,
				1 => GameMenuCommand.Fullscreen,
				_ => GameMenuCommand.Back
			},
			_ => GameMenuCommand.Resume
		};
	}

	void ActivateGameMenuCommandWithAudio(GameMenuCommand command)
	{
		if (command is GameMenuCommand.Resume or GameMenuCommand.Start)
			MenuAudioEvent("menu/show");
		else if (command is GameMenuCommand.Back or GameMenuCommand.Title or GameMenuCommand.Quit)
			MenuAudioEvent("menu/back");
		else
			MenuAudioEvent("menu/proceed");

		ActivateGameMenuCommand(command);
	}

	void ActivateGameMenuCommand(GameMenuCommand command)
	{
		switch (command)
		{
			case GameMenuCommand.Resume:
				CloseGameMenu();
				break;
			case GameMenuCommand.Start:
				CloseGameMenu();
				StartGame(immediate: true);
				break;
			case GameMenuCommand.Map:
				if (game?.CurrentRoom is { } room)
				{
					game.Minimap.Open = true;
					game.Minimap.Rebuild(room);
				}
				CloseGameMenu();
				break;
			case GameMenuCommand.Restart:
				confirmRestart = true;
				break;
			case GameMenuCommand.ConfirmRestart:
				CloseGameMenu();
				EraseSaveGame();
				StartGame(immediate: true);
				break;
			case GameMenuCommand.Options:
				gameMenuReturnPage = gameMenuPage;
				ShowGameMenuPage(GameMenuPage.Options);
				break;
			case GameMenuCommand.Music:
				MusicEnabled = !MusicEnabled;
				break;
			case GameMenuCommand.Fullscreen:
#if !IOS && !BROWSER
				if (Window is { } window)
					window.Fullscreen = !window.Fullscreen;
#endif
				break;
			case GameMenuCommand.Back:
				ShowGameMenuPage(gameMenuReturnPage);
				break;
			case GameMenuCommand.Editor:
#if EDITOR
				CloseGameMenu();
				StopGame();
#endif
				break;
			case GameMenuCommand.Title:
				game?.Dispose();
				game = null;
				ShowTitleMenu();
				VirtualGameControllerVisibilityChanged?.Invoke(WantsVirtualGameController);
				break;
			case GameMenuCommand.Quit:
				Exit();
				break;
		}
	}

	Target? previewTarget = null;

	void InitializePreviewGameForRendering(Room room)
	{
		if (Window == null) return;

		var pg = previewGame = new(this, start: room, audioEventsEnabled: false, spawnFallbackPlayer: false, spawnBud: false);
		pg.Simulating = true;
		Input.Clear();
		pg.Update(new Controls(Input));
		pg.ditherGlitch = 0.9f;
		pg.ditherGlitchAxis = new(1f, 0);
		previewTarget = CaptureScreenshot(pg, Window.Width, Window.Height);
	}

	// Layout phase (runs in Manager.Update, before the editor's ImGui layout).
	// Builds the menu's ImGui window in menuImgui's own context. The window is
	// scaled so 1 ImGui unit == 1 game pixel on screen, aligning the buttons with
	// the batcher-drawn title (DrawGameMenu, game space via MenuMatrix).
	void BuildGameMenu()
	{
		menuLayoutBuiltThisFrame = false;

		if (!GameMenuOpen || Window == null)
			return;

		// Render the room preview once (drawn behind the menu in DrawGameMenu).
		if (game == null && CurrentRoom is { } previewRoom && previewGame == null)
			InitializePreviewGameForRendering(previewRoom);

		var presentScale = MenuPresentScale;
		if (presentScale <= 0)
			return;

		menuImgui.Scale = presentScale;
		var origin = MenuPresentOffset / presentScale; // game-space (0,0) in ImGui coords

		var itemCount = GameMenuItemCount(gameMenuPage);
		var listStart = GameMenuListStart(itemCount);
		var windowPos = origin + listStart;
		var windowSize = new Vector2(MenuButtonWidth, itemCount * MenuButtonHeight + (itemCount - 1) * MenuButtonGap);

		menuImgui.BeginLayout();

		ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
		ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(0, MenuButtonGap));
		ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1f);
		ImGui.PushStyleColor(ImGuiCol.Button, ImColor(0x1b102e));
		ImGui.PushStyleColor(ImGuiCol.ButtonHovered, ImColor(0x323c39));
		ImGui.PushStyleColor(ImGuiCol.ButtonActive, ImColor(0x323c39));
		ImGui.PushStyleColor(ImGuiCol.Border, ImColor(0x5b5274));
		ImGui.PushStyleColor(ImGuiCol.Text, ImColor(0xc0cbdc));
		ImGui.PushStyleColor(ImGuiCol.NavCursor, ImColor(0x5fcde4));

		ImGui.SetNextWindowPos(windowPos);
		ImGui.SetNextWindowSize(windowSize);

		const ImGuiWindowFlags flags =
			ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove |
			ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse |
			ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoBackground |
			ImGuiWindowFlags.NoBringToFrontOnFocus | ImGuiWindowFlags.NoCollapse;

		GameMenuCommand? activated = null;
		ImGui.Begin("##gamemenu", flags);
		for (var i = 0; i < itemCount; i++)
		{
			var command = GameMenuCommandAt(gameMenuPage, i);

			if (menuFocusDirty && i == gameMenuSelection)
				ImGui.SetKeyboardFocusHere();

			if (ImGui.Button($"{GameMenuLabel(command)}##{i}", new Vector2(MenuButtonWidth, MenuButtonHeight)))
			{
				gameMenuSelection = i;
				activated = command;
			}

			// Play the move cue when nav focus lands on a new item.
			if (ImGui.IsItemFocused() && gameMenuSelection != i)
			{
				gameMenuSelection = i;
				MenuAudioEvent("menu/select");
			}
		}
		ImGui.End();

		ImGui.PopStyleColor(6);
		ImGui.PopStyleVar(3);

		menuFocusDirty = false;
		menuImgui.EndLayout();

		// Activation may close the menu / start the game; do it after the ImGui
		// frame is finished. Skip flushing this frame's draw data if it closed,
		// so the menu doesn't linger for a frame.
		if (activated is { } cmd)
		{
			ActivateGameMenuCommandWithAudio(cmd);
			menuLayoutBuiltThisFrame = GameMenuOpen;
		}
		else
		{
			menuLayoutBuiltThisFrame = true;
		}
	}

	// Render phase: the preview background, dark scrim, and animated title. The
	// button list itself is ImGui (built in BuildGameMenu, flushed by menuImgui).
	void DrawGameMenu()
	{
		if (!GameMenuOpen || Window == null)
			return;

		batcher.PushMatrix(MenuMatrix);
		if (previewTarget != null)
			batcher.Image(previewTarget, Vector2.Zero, Color.White);
		batcher.Rect(MenuBounds, Color.FromHexStringRGBA("0x160b1888"));
		DrawGameMenuTitle();
		batcher.PopMatrix();
	}

	void DrawGameMenuTitle()
	{
		var itemCount = GameMenuItemCount(gameMenuPage);
		var panelHeight = 76f + itemCount * MenuButtonHeight + (itemCount - 1) * MenuButtonGap;
		var panel = Rect.CenteredAt(MenuBounds.Center, new Vector2(MenuPanelWidth, panelHeight));

		var font = Assets.Font;
		if (font == null) return;

		var title = gameMenuPage switch
		{
			GameMenuPage.Title => "mycelon",
			GameMenuPage.Options => "options",
			_ => "paused"
		};

		var pos = new Vector2(panel.Center.X, panel.Top + 25);
		DrawCenteredText(font, title, pos + Vector2.One * 1f, 18, Color.Lerp(0xfee761, Color.Coral, 0.8f), sine: true);
		DrawCenteredText(font, title, pos, 18, 0xfee761, sine: true);

		var hint = gameMenuPage == GameMenuPage.Title ? "ENTER / START / A" : "ESC / B";
		DrawCenteredText(font, hint, new Vector2(panel.Center.X, panel.Bottom - 17), 8, 0x8b81b5);
	}

	static Vector4 ImColor(uint rgb, float a = 1f)
		=> new(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, a);

	static Rect MenuBounds => new(0, 0, Game.Width, Game.Height);

	RectInt MenuViewport => (RectInt)(GameViewRect * Scale);

	float MenuPresentScale
	{
		get
		{
			var viewport = MenuViewport;
			return MathF.Min(viewport.Width / (float)Game.Width, viewport.Height / (float)Game.Height);
		}
	}

	Vector2 MenuPresentOffset
	{
		get
		{
			var viewport = MenuViewport;
			var scale = MenuPresentScale;
			return new Vector2(
				viewport.X + (viewport.Width - Game.Width * scale) * 0.5f,
				viewport.Y + (viewport.Height - Game.Height * scale) * 0.5f);
		}
	}

	Matrix3x2 MenuMatrix
		=> Matrix3x2.CreateScale(MenuPresentScale) * Matrix3x2.CreateTranslation(MenuPresentOffset);

	Vector2 GameMenuListStart(int itemCount)
	{
		var totalHeight = itemCount * MenuButtonHeight + (itemCount - 1) * MenuButtonGap;
		return new Vector2(MenuBounds.Center.X - MenuButtonWidth * 0.5f, MenuBounds.Center.Y - totalHeight * 0.5f + 7f);
	}

	string GameMenuLabel(GameMenuCommand command)
		=> command switch
		{
			GameMenuCommand.Resume => "resume",
			GameMenuCommand.Start => hasStartedGame ? "continue" : "start",
			GameMenuCommand.Map => "map",
			GameMenuCommand.Restart => "erase game",
			GameMenuCommand.ConfirmRestart => "you sure?",
			GameMenuCommand.Options => "options",
			GameMenuCommand.Music => musicEnabled ? "music on" : "music off",
			GameMenuCommand.Fullscreen => Window?.Fullscreen == true ? "windowed" : "fullscreen",
			GameMenuCommand.Back => "back",
			GameMenuCommand.Editor => "editor",
			GameMenuCommand.Title => "title",
			GameMenuCommand.Quit => "quit",
			_ => ""
		};

	void DrawCenteredText(SpriteFont font, ReadOnlySpan<char> text, Vector2 center, float size, Color color, bool sine = false)
	{
		var position = new Vector2(
			MathF.Round(center.X - font.WidthOf(text, size) * 0.5f),
			MathF.Round(center.Y - font.LineHeight * size / font.Size * 0.5f));
		position.Y += 1.5f;
		if (sine)
			font.DrawSineWave(batcher, text, position, new(), size, color, 0, Time.SecondsF * 0.3f, new(0, Time.SecondsF * 0.15f));
		else
			batcher.Text(font, text, position, size, color);
	}

}
