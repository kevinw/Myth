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

	Game? previewGame;

	GameMenuPage gameMenuPage;
	GameMenuPage gameMenuReturnPage;
	bool confirmRestart = false;

	// Set when a page is shown; makes the next build reset the focused-slot tracker so
	// the auto-focus of the first item doesn't fire the move cue. (The focus itself is
	// handled by SetItemDefaultFocus on each page's freshly-appearing window.)
	bool menuFocusDirty;

	// Per-build button bookkeeping: index of the button currently being emitted, the
	// total emitted last build (drives the title/hint layout), the slot ImGui reports
	// focused (for the move cue), and whether a button fired this build.
	int menuButtonIndex;
	int menuItemCount;
	int menuFocusedIndex = -1;
	bool menuActivated;

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
		confirmRestart = false;
	}

	void ShowGameMenuPage(GameMenuPage page)
	{
		gameMenuPage = page;
		menuFocusDirty = true;
	}

	void MenuAudioEvent(string path) => Audio.CreateEvent($"event:/{path}")?.Start();

	// Input-phase handling that is NOT navigation: opening the pause menu, and the
	// back/close gesture. Movement and activation are owned by ImGui nav (see the
	// per-page methods below), so they are intentionally absent here.
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

	// ---------------------------------------------------------------------------
	// Menus, immediate-mode: each page is just a list of buttons. Adding, removing,
	// reordering, or platform-gating an item is a one-line edit right here.
	// ---------------------------------------------------------------------------

	void TitleMenu()
	{
		if (MenuButton(hasStartedGame ? "continue" : "start", "menu/show"))
			StartFromMenu();
		if (MenuButton("options"))
			OpenOptionsPage();
#if !BROWSER
		if (MenuButton("quit", "menu/back"))
			Exit();
#endif
	}

	void PauseMenu()
	{
		if (MenuButton("resume", "menu/show"))
			CloseGameMenu();
		if (MenuButton("map"))
			OpenMapFromMenu();
		if (MenuButton("options"))
			OpenOptionsPage();
#if EDITOR
		if (MenuButton("editor", "menu/back"))
		{
			CloseGameMenu();
			StopGame();
		}
#elif BROWSER
		if (MenuButton("title", "menu/back"))
			ReturnToTitleFromMenu();
#else
		if (MenuButton("quit", "menu/back"))
			Exit();
#endif
	}

	void OptionsMenu()
	{
		if (MenuButton(musicEnabled ? "music on" : "music off"))
			MusicEnabled = !MusicEnabled;
#if !IOS && !BROWSER
		if (MenuButton(Window?.Fullscreen == true ? "fullscreen" : "windowed") && Window is { } window)
			window.Fullscreen = !window.Fullscreen;
#endif
		if (MenuButton(confirmRestart ? "you sure?" : "erase game"))
			EraseGameStep();
		if (MenuButton("back", "menu/back"))
			ShowGameMenuPage(gameMenuReturnPage);
	}

	void StartFromMenu()
	{
		CloseGameMenu();
		StartGame(immediate: true);
	}

	void OpenOptionsPage()
	{
		gameMenuReturnPage = gameMenuPage;
		ShowGameMenuPage(GameMenuPage.Options);
	}

	void OpenMapFromMenu()
	{
		if (game?.CurrentRoom is { } room)
		{
			game.Minimap.Open = true;
			game.Minimap.Rebuild(room);
		}
		CloseGameMenu();
	}

	// "erase game" is a two-step confirm: the first press arms it (the label becomes
	// "you sure?"), the second erases the save and restarts.
	void EraseGameStep()
	{
		if (confirmRestart)
		{
			CloseGameMenu();
			EraseSaveGame();
			StartGame(immediate: true);
		}
		else
		{
			confirmRestart = true;
		}
	}

#if BROWSER
	void ReturnToTitleFromMenu()
	{
		game?.Dispose();
		game = null;
		ShowTitleMenu();
		VirtualGameControllerVisibilityChanged?.Invoke(WantsVirtualGameController);
	}
#endif

	// Emits one menu button and returns true when it's activated (click or nav). Plays
	// the move cue when nav focus lands on it, and `sound` when it fires.
	bool MenuButton(string label, string sound = "menu/proceed")
	{
		// Use ### so the id is the slot index only: a button whose label toggles
		// ("music on"/"music off", "fullscreen"/"windowed") keeps a stable id and so
		// keeps nav focus, letting you re-toggle it with repeated South presses.
		var pressed = ImGui.Button($"{label}###{menuButtonIndex}", new Vector2(MenuButtonWidth, MenuButtonHeight));

		// The first item is the default nav focus when a page's window appears (each
		// page uses a distinct window id, so switching pages re-triggers this).
		if (menuButtonIndex == 0)
			ImGui.SetItemDefaultFocus();

		// Draw our own selection outline, tight around the button. ImGui's built-in nav
		// cursor (suppressed via a transparent NavCursor color) expands the rect several
		// pixels and spills into the next button's gap. The full-screen clip lets the
		// 1px outset draw past the window edge on the first/last buttons (WindowPadding
		// is 0, so those edges sit on the window bounds and would otherwise be clipped).
		if (ImGui.IsItemFocused())
		{
			var min = ImGui.GetItemRectMin() - Vector2.One;
			var max = ImGui.GetItemRectMax() + Vector2.One;
			var draw = ImGui.GetWindowDrawList();
			draw.PushClipRectFullScreen();
			draw.AddRect(min, max, ImGui.ColorConvertFloat4ToU32(ImColor(0x5fcde4)), 0f, ImDrawFlags.None, 1f);
			draw.PopClipRect();
		}

		if (ImGui.IsItemFocused() && menuFocusedIndex != menuButtonIndex)
		{
			if (menuFocusedIndex >= 0)
				MenuAudioEvent("menu/select");
			menuFocusedIndex = menuButtonIndex;
		}

		menuButtonIndex++;

		if (pressed)
		{
			MenuAudioEvent(sound);
			menuActivated = true;
		}
		return pressed;
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

	const ImGuiWindowFlags MenuWindowFlags =
		ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove |
		ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse |
		ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoBackground |
		ImGuiWindowFlags.NoBringToFrontOnFocus | ImGuiWindowFlags.NoCollapse |
		ImGuiWindowFlags.AlwaysAutoResize;

	// Layout phase (runs in Manager.Update, before the editor's ImGui layout). Builds
	// the menu's ImGui window in menuImgui's own context, scaled so 1 ImGui unit == 1
	// game pixel on screen and centered so it aligns with the batcher-drawn title
	// (DrawGameMenu, game space via MenuMatrix). The window auto-sizes to its buttons.
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

		if (menuFocusDirty)
			menuFocusedIndex = -1;

		menuImgui.BeginLayout();
		PushMenuStyle();

		// Center the (auto-sized) button block on screen; +7 matches the old layout nudge.
		var blockCenter = origin + (Vector2)MenuBounds.Center + new Vector2(0, 7);
		ImGui.SetNextWindowPos(blockCenter, ImGuiCond.Always, new Vector2(0.5f, 0.5f));

		menuButtonIndex = 0;
		menuActivated = false;

		// Distinct id per page so switching pages makes the window "appear", which is
		// when SetItemDefaultFocus (in MenuButton) takes effect and shows the selection.
		ImGui.Begin($"##gamemenu_{gameMenuPage}", MenuWindowFlags);
		switch (gameMenuPage)
		{
			case GameMenuPage.Title: TitleMenu(); break;
			case GameMenuPage.Pause: PauseMenu(); break;
			case GameMenuPage.Options: OptionsMenu(); break;
		}
		ImGui.End();

		menuItemCount = menuButtonIndex; // how many buttons the page emitted, for the title layout

		PopMenuStyle();
		menuFocusDirty = false;
		menuImgui.EndLayout();

		// A button that closed the menu (resume / start / quit) should not linger for a
		// frame; keep flushing only while the menu is still open.
		menuLayoutBuiltThisFrame = menuActivated ? GameMenuOpen : true;
	}

	void PushMenuStyle()
	{
		// A couple px of window padding so each button's frame border (and the focus
		// outline) has room inside the window's clip rect instead of being cut off on
		// the outer edges.
		ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(2, 2));
		ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(0, MenuButtonGap));
		ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1f);
		ImGui.PushStyleColor(ImGuiCol.Button, ImColor(0x1b102e));
		ImGui.PushStyleColor(ImGuiCol.ButtonHovered, ImColor(0x323c39));
		ImGui.PushStyleColor(ImGuiCol.ButtonActive, ImColor(0x323c39));
		ImGui.PushStyleColor(ImGuiCol.Border, ImColor(0x5b5274));
		ImGui.PushStyleColor(ImGuiCol.Text, ImColor(0xc0cbdc));
		// Suppress ImGui's built-in nav cursor; MenuButton draws a tighter one.
		ImGui.PushStyleColor(ImGuiCol.NavCursor, new Vector4(0, 0, 0, 0));
	}

	void PopMenuStyle()
	{
		ImGui.PopStyleColor(6);
		ImGui.PopStyleVar(3);
	}

	// Render phase: the preview background, dark scrim, and animated title. The button
	// list itself is ImGui (built in BuildGameMenu, flushed by menuImgui).
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
		var itemCount = menuItemCount;
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

	void DrawCenteredText(SpriteFont font, ReadOnlySpan<char> text, Vector2 center, float size, Color color, bool sine = false)
	{
		var position = new Vector2(
			MathF.Round(center.X - font.WidthOf(text, size) * 0.5f),
			MathF.Round(center.Y - font.LineHeight * size / font.Size * 0.5f));
		position.Y += 1.5f;
		if (sine)
			font.DrawSineWave(batcher, text, position, new(), size, color, -0.5f, Time.SecondsF * 0.19f, new(0, Time.SecondsF * 0.01f));
		else
			batcher.Text(font, text, position, size, color);
	}
}
