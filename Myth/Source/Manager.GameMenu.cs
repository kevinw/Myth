using System.Numerics;
using System.Text.Json.Nodes;
using Foster.Framework;
using ImGuiNET;
using static ImGuiNET.ImGui;

namespace Myth;

public partial class Manager
{
	enum GameMenuPage
	{
		Closed,
		Title,
		Pause,
		Options,
		DemoTeleport
	}

	readonly record struct DemoTeleportSpot(string Label, string RoomId, Point2 Position);
	readonly List<DemoTeleportSpot> demoTeleportSpots = [];

	Game? previewGame;

	GameMenuPage gameMenuPage;
	GameMenuPage gameMenuReturnPage;
	// The page whose ImGui data was built; a button callback can change gameMenuPage mid-build.
	GameMenuPage gameMenuLayoutPage;
	float gameMenuPageTransition = 1f;
	float gameMenuLayoutEase = 1f;
	Vector2 gameMenuEnterDirection = Vector2.UnitY;
	Vector2 gameMenuLayoutOffset;
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

	// Leave room for each page's longest label and horizontal padding.
	float MenuButtonWidth => gameMenuLayoutPage switch
	{
		GameMenuPage.Options or GameMenuPage.DemoTeleport => 100f,
		GameMenuPage.Pause => 80f,
		_ => 64f,
	};
	static float MenuButtonHeight => 14f;
	static float MenuButtonGap => 3f;
	static float MenuPanelWidth => 174f;
	static float MenuEnterDuration => 0.16f;
	static float MenuEnterDistance => 5f;

	void ShowTitleMenu()
	{
		gameMenuReturnPage = GameMenuPage.Title;
		playOnStartup = false;
		playing = false;
		startingPlay = false;

		ShowGameMenuPage(GameMenuPage.Title);
	}

	void ShowPauseMenu()
	{
		RefreshDemoTeleportSpots();
		MenuAudioEvent("menu/show");
		gameMenuReturnPage = GameMenuPage.Pause;
		ShowGameMenuPage(GameMenuPage.Pause);
	}

	void CloseGameMenu()
	{
		previewGame?.Dispose();
		previewGame = null;

		previewTarget?.Dispose();
		previewTarget = null;

		gameMenuPage = GameMenuPage.Closed;
		ApplyGameplayAudioPause();
		confirmRestart = false;
	}

	void ShowGameMenuPage(GameMenuPage page)
	{
		if (gameMenuPage != page)
		{
			var deeper = MenuPageDepth(page) - MenuPageDepth(gameMenuPage);
			gameMenuEnterDirection = deeper > 0 ? Vector2.UnitX :
				deeper < 0 ? -Vector2.UnitX : Vector2.UnitY;
			gameMenuPageTransition = 0;
		}
		gameMenuPage = page;
		ApplyGameplayAudioPause();
		confirmRestart = false;
		menuFocusDirty = true;
	}

	// How far a page sits from the top level: transitions slide toward the deeper page, and the
	// title/hint layout treats anything below the top level as a sub-page.
	static int MenuPageDepth(GameMenuPage page) => page switch
	{
		GameMenuPage.Options or GameMenuPage.DemoTeleport => 1,
		_ => 0,
	};

	void ApplyGameplayAudioPause() => Audio.SetBusPaused("bus:/Gameplay", GameMenuPausesGame);

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
			if (gameMenuPage is GameMenuPage.DemoTeleport or GameMenuPage.Options)
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
		if (MenuButton(confirmRestart ? "you sure?" : "start over"))
			EraseGameStep();
		if (MenuButton("options"))
			OpenOptionsPage();
#if !BROWSER
		if (MenuButton("quit", "menu/back"))
			RequestQuit();
#endif
	}

	void PauseMenu()
	{
		if (MenuButton("resume", "menu/show"))
			CloseGameMenu();
		// if (MenuButton("map"))
		// 	OpenMapFromMenu();
		if (MenuButton(confirmRestart ? "you sure?" : "start over"))
			EraseGameStep();
		if (game != null && demoTeleportSpots.Count > 0 && MenuButton("demo teleport"))
			ShowGameMenuPage(GameMenuPage.DemoTeleport);
		if (MenuButton("options"))
			OpenOptionsPage();
#if BROWSER
		if (MenuButton("title", "menu/back"))
			ReturnToTitleFromMenu();
#else
		if (MenuButton("quit", "menu/back"))
			RequestQuit();
#endif
	}

	void OptionsMenu()
	{
		if (MenuButton(musicEnabled ? "music: on" : "music: off"))
			MusicEnabled = !MusicEnabled;
#if !IOS && !BROWSER
		if (!IsSteamDeck)
		{
			if (MenuButton(runInBackground ? "run in background: on" : "run in background: off"))
				runInBackground = !runInBackground;
			if (MenuButton(Window?.Fullscreen == true ? "fullscreen" : "windowed") && Window is { } window)
				window.Fullscreen = !window.Fullscreen;
		}
#endif
		if (gameMenuReturnPage == GameMenuPage.Pause && game?.HasLastSavePoint == true && MenuButton("back to save"))
			ReturnToSavePointFromMenu();
		if (MenuButton("back", "menu/back"))
			ShowGameMenuPage(gameMenuReturnPage);
	}

	void DemoTeleportMenu()
	{
		foreach (var spot in demoTeleportSpots)
			if (MenuButton(spot.Label))
				DemoTeleportTo(spot);
		if (MenuButton("back", "menu/back"))
			ShowGameMenuPage(gameMenuReturnPage);
	}

	// Goals are only spawned as actors in goal-loading games (Game.loadGoals), so the list comes
	// from the authored rooms rather than anything live. Rebuilt whenever the pause menu opens, which
	// keeps it current with live room edits.
	void RefreshDemoTeleportSpots()
	{
		demoTeleportSpots.Clear();
		foreach (var room in Assets.RoomsById.Values)
			foreach (var actor in room.Actors)
			{
				if (!string.Equals(ActorSpecs.GetTypeName(actor), nameof(Goal), StringComparison.OrdinalIgnoreCase) ||
					ActorSpecs.GetResolvedProperty(actor, nameof(Goal.DemoTeleportTarget)) is not JsonValue flag ||
					!flag.TryGetValue<bool>(out var enabled) || !enabled)
					continue;

				var name = ActorSpecs.ReadStringValue(ActorSpecs.GetResolvedProperty(actor, nameof(Goal.Name)));
				var local = ActorSpecs.ReadResolvedPosition(actor) ?? Point2.Zero;
				demoTeleportSpots.Add(new(
					string.IsNullOrWhiteSpace(name) ? room.Id : name,
					room.Id,
					room.WorldBounds.TopLeft + local));
			}
		// World X order: the menu reads left-to-right across the map, which is roughly progression.
		demoTeleportSpots.Sort(static (a, b) => a.Position.X.CompareTo(b.Position.X));
	}

	// Close first: the warp's teleport cue plays on bus:/Gameplay, which the pause menu paused.
	void DemoTeleportTo(DemoTeleportSpot spot)
	{
		CloseGameMenu();
		if (game?.GetFirst<Player>() is { } player)
			player.Health = Player.MaxHealth;
		game?.TryWarpTo(spot.RoomId, spot.Position);
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

	// Close first: the warp's teleport cue plays on bus:/Gameplay, which the pause menu has paused
	// until CloseGameMenu lets it go.
	void ReturnToSavePointFromMenu()
	{
		CloseGameMenu();
		game?.TryWarpToLastSavePoint();
	}

	void OpenMapFromMenu()
	{
		if (game?.CurrentRoom is { } room)
		{
			game.Minimap.Open = true;
			game.Minimap.Rebuild(room, game);
		}
		CloseGameMenu();
	}

	// "start over" is a two-step confirm: the first press arms it (the label becomes
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
		var pressed = Button($"###{menuButtonIndex}", new Vector2(MenuButtonWidth, MenuButtonHeight));
		var buttonMin = GetItemRectMin();
		var buttonMax = GetItemRectMax();
		if ((Assets.SpeechFont ?? Assets.Font) is { } font)
		{
			var visible = menuImgui.BeginBatchAt(Rect.Between(buttonMin, buttonMax), out var labelBatch, out var bounds);
			if (visible)
			{
				var x = font['x'];
				var inkCenter = font.Ascent + x.Offset.Y + x.Subtexture.Height * 0.5f;
				labelBatch.Text(font, label, new Vector2(GetStyle().FramePadding.X, bounds.Center.Y - inkCenter), new Color(0xc0cbdc) * gameMenuLayoutEase);
			}
			menuImgui.EndBatch();
		}

		// The first item is the default nav focus when a page's window appears (each
		// page uses a distinct window id, so switching pages re-triggers this).
		if (menuButtonIndex == 0)
			SetItemDefaultFocus();

		// Draw our own selection outline, tight around the button. ImGui's built-in nav
		// cursor (suppressed via a transparent NavCursor color) expands the rect several
		// pixels and spills into the next button's gap. The full-screen clip lets the
		// 1px outset draw past the window edge on the first/last buttons (WindowPadding
		// is 0, so those edges sit on the window bounds and would otherwise be clipped).
		if (IsItemFocused())
		{
			var min = GetItemRectMin() - Vector2.One;
			var max = GetItemRectMax() + Vector2.One;
			var draw = GetWindowDrawList();
			draw.PushClipRectFullScreen();
			draw.AddRect(min, max, ColorConvertFloat4ToU32(ImColor(0x5fcde4, gameMenuLayoutEase)), 0f, ImDrawFlags.None, 1f);
			draw.PopClipRect();
		}

		if (IsItemFocused() && menuFocusedIndex != menuButtonIndex)
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

		var pg = previewGame = new(this, start: room, audioEventsEnabled: false, spawnFallbackPlayer: false, spawnBud: false, loadGoals: true);
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

		var buildLiveEditorMenu =
#if EDITOR
			LiveRoomEditorMenuOpen;
#else
			false;
#endif
		if ((!GameMenuOpen && !buildLiveEditorMenu) || Window == null)
			return;

		// Render the room preview once (drawn behind the menu in DrawGameMenu).
		if (game == null && CurrentRoom is { } previewRoom && previewGame == null)
			InitializePreviewGameForRendering(previewRoom);

		var presentScale = MenuPresentScale;
		if (presentScale <= 0)
			return;

		menuImgui.Scale = presentScale;
		var origin = MenuPresentOffset / presentScale; // game-space (0,0) in ImGui coords
		gameMenuPageTransition = MathF.Min(1f, gameMenuPageTransition + Time.Delta / MenuEnterDuration);
		gameMenuLayoutEase = Ease.Cube.Out(gameMenuPageTransition);
		gameMenuLayoutOffset = gameMenuEnterDirection * ((1f - gameMenuLayoutEase) * MenuEnterDistance);
		gameMenuLayoutPage = gameMenuPage;

		if (menuFocusDirty)
			menuFocusedIndex = -1;

		menuImgui.BeginLayout();
		PushMenuStyle();

		menuButtonIndex = 0;
		menuActivated = false;

		if (GameMenuOpen)
		{
			var y = gameMenuLayoutPage switch {
				GameMenuPage.Title => 27f,
				GameMenuPage.Pause => 27f,
				_ => 7f,
			};
			// Center the (auto-sized) button block on screen; +7 matches the old layout nudge.
			var blockCenter = origin + (Vector2)MenuBounds.Center + new Vector2(0, y) + gameMenuLayoutOffset;
			SetNextWindowPos(blockCenter, ImGuiCond.Always, new Vector2(0.5f, 0.5f));

			// Distinct id per page so switching pages makes the window "appear", which is
			// when SetItemDefaultFocus (in MenuButton) takes effect and shows the selection.
			Begin($"##gamemenu_{gameMenuLayoutPage}", MenuWindowFlags);
			switch (gameMenuLayoutPage)
			{
				case GameMenuPage.Title: TitleMenu(); break;
				case GameMenuPage.Pause: PauseMenu(); break;
				case GameMenuPage.Options: OptionsMenu(); break;
				case GameMenuPage.DemoTeleport: DemoTeleportMenu(); break;
			}
			End();
		}

#if EDITOR
		LiveRoomEditingMenuImGui();
#endif

		menuItemCount = menuButtonIndex; // how many buttons the page emitted, for the title layout

		PopMenuStyle();
		menuFocusDirty = gameMenuPage != gameMenuLayoutPage;
		menuImgui.EndLayout();

		// A button that closed the menu (resume / start / quit) should not linger for a
		// frame; keep flushing only while the menu is still open.
		menuLayoutBuiltThisFrame = GameMenuOpen ? menuActivated ? GameMenuOpen : true : buildLiveEditorMenu;
	}

	void PushMenuStyle()
	{
		// A couple px of window padding so each button's frame border (and the focus
		// outline) has room inside the window's clip rect instead of being cut off on
		// the outer edges.
		PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(2, 2));
		PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(0, MenuButtonGap));
		PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1f);
		PushStyleVar(ImGuiStyleVar.Alpha, gameMenuLayoutEase);
		PushStyleColor(ImGuiCol.Button, ImColor(0x1b102e));
		PushStyleColor(ImGuiCol.ButtonHovered, ImColor(0x323c39));
		PushStyleColor(ImGuiCol.ButtonActive, ImColor(0x323c39));
		PushStyleColor(ImGuiCol.Border, ImColor(0x5b5274));
		PushStyleColor(ImGuiCol.Text, ImColor(0xc0cbdc));
		// Suppress ImGui's built-in nav cursor; MenuButton draws a tighter one.
		PushStyleColor(ImGuiCol.NavCursor, new Vector4(0, 0, 0, 0));
	}

	void PopMenuStyle()
	{
		PopStyleColor(6);
		PopStyleVar(4);
	}

	// Render phase: the preview background, dark scrim, and animated title. The button
	// list itself is ImGui (built in BuildGameMenu, flushed by menuImgui).
	void DrawGameMenu()
	{
		if (!GameMenuOpen || Window == null)
			return;

		// The glitched room snapshot is the title backdrop when nothing else plays behind it; the
		// attract demo (a live game rendered before this) takes precedence when it's running.
		if (previewTarget != null && attract == null)
		{
			batcher.PushMatrix(MenuMatrix);
			batcher.Image(previewTarget, Vector2.Zero, Color.White);
			batcher.PopMatrix();
		}

		// The menu content stays on the 16:9 game canvas, but the scrim covers the
		// actual viewport (notably the Steam Deck's extra 16:10 bands).
		batcher.Rect(MenuViewport, Color.FromHexStringRGBA("0x160b1888"));

		batcher.PushMatrix(MenuMatrix);
		batcher.PushMatrix(Matrix3x2.CreateTranslation(gameMenuLayoutOffset));
		DrawGameMenuTitle();
		batcher.PopMatrix();

		// draw cheeky views of the player and bud
		{
			void Draw(Vector2 offset, int Facing = 1, Color? tint = null, byte alpha = 255, bool hat = false)
			{
				var sprite = Assets.GetSprite("player");
				if (sprite == null) return;
				const int frameIndex = 2;
				var frame = sprite.Frames[frameIndex];
				var origin = sprite.ResolveOrigin(SpriteAnchor.Pivot);
				var img = frame.Subtexture;
				batcher.PushMatrix(Matrix3x2.CreateTranslation(-origin + offset));
				batcher.PushMatrix(Matrix3x2.CreateScale(new Vector2(6 * Facing, 6)));
				var color = tint ?? Color.White;
				color.A = alpha;
				batcher.Image(img, Vector2.Zero, color);
				if (hat && Assets.GetSprite("hat") is { Frames.Count: > 0 } hatSprite &&
					sprite.TryGetSocket("hat", frameIndex, out var socket))
					batcher.Image(hatSprite.Frames[0].Subtexture, socket.Position + new Vector2(1, 2),
						hatSprite.ResolveOrigin(SpriteAnchor.BottomCenter), Vector2.One, 0, color);
				batcher.PopMatrix();
				batcher.PopMatrix();
			}

			byte alpha = 80;
			Draw(new Vector2(-70, 50), alpha: alpha, hat: true);
			Draw(new Vector2(430, 50), -1, Bud.Tint, alpha: alpha);
		}
		batcher.PopMatrix();
	}

	void DrawGameMenuTitle()
	{
		var itemCount = menuItemCount;
		var panelHeight = 76f + itemCount * MenuButtonHeight + (itemCount - 1) * MenuButtonGap;
		var panel = Rect.CenteredAt(MenuBounds.Center, new Vector2(MenuPanelWidth, panelHeight));

		var font = Assets.SpeechFont ?? Assets.Font;
		if (font == null) return;

		var title = gameMenuLayoutPage switch
		{
			GameMenuPage.Title => "Human Questions",
			GameMenuPage.Options => "options",
			GameMenuPage.DemoTeleport => "demo teleport",
			_ => "paused"
		};

		var pos = new Vector2(panel.Center.X, panel.Top + 25);

		if (MenuPageDepth(gameMenuLayoutPage) == 0 && Assets.GetSprite("logo2", logMissing: false) is { Frames.Count: > 0 } logo)
			DrawDistortedLogo(logo.Frames[0].Subtexture, pos, logo.ResolveOrigin(SpriteAnchor.Center), Color.White * gameMenuLayoutEase);
		else
		{
			var useSineWave = false; // TODO: looks bad after the game has been running awhile
			DrawCenteredText(font, title, pos + Vector2.One, font.Size * 2, Color.Lerp(0xfee761, Color.Black, 0.8f) * gameMenuLayoutEase, sine: useSineWave);
			DrawCenteredText(font, title, pos, font.Size * 2, new Color(0xfee761) * gameMenuLayoutEase, sine: useSineWave);
		}

		var hint = gameMenuLayoutPage == GameMenuPage.Pause ? InputPromptAction.Confirm : InputPromptAction.Back;
		InputPrompts?.DrawGlyphCentered(
			batcher,
			hint,
			new Vector2(panel.Center.X, panel.Bottom - (MenuPageDepth(gameMenuLayoutPage) == 0 ? 0f : 17f)),
			12,
			new Color(0x8b81b5) * gameMenuLayoutEase);
	}

	static int LogoGridColumns => 30;
	static int LogoGridRows => 12;
	static float LogoIdleAmplitude => 0.1f;
	static float LogoMouseRadius => 70f;
	static float LogoMousePush => 1f;

	void DrawDistortedLogo(Subtexture subtexture, Vector2 position, Vector2 origin, Color color)
	{
		if (subtexture.Texture == null)
			return;

		var mouse = MenuMouse;
		var time = Time.SecondsF;
		for (var y = 0; y < LogoGridRows; y++)
		{
			var v0 = y / (float)LogoGridRows;
			var v1 = (y + 1) / (float)LogoGridRows;
			for (var x = 0; x < LogoGridColumns; x++)
			{
				var u0 = x / (float)LogoGridColumns;
				var u1 = (x + 1) / (float)LogoGridColumns;

				var p0 = DistortedLogoVertex(subtexture, position, origin, u0, v0, mouse, time);
				var p1 = DistortedLogoVertex(subtexture, position, origin, u1, v0, mouse, time);
				var p2 = DistortedLogoVertex(subtexture, position, origin, u1, v1, mouse, time);
				var p3 = DistortedLogoVertex(subtexture, position, origin, u0, v1, mouse, time);
				var t0 = LogoUv(subtexture, u0, v0);
				var t1 = LogoUv(subtexture, u1, v0);
				var t2 = LogoUv(subtexture, u1, v1);
				var t3 = LogoUv(subtexture, u0, v1);

				batcher.Quad(subtexture.Texture, p0, p1, p2, p3, t0, t1, t2, t3, color);
			}
		}
	}

	static Vector2 DistortedLogoVertex(Subtexture subtexture, Vector2 position, Vector2 origin, float u, float v, Vector2 mouse, float time)
	{
		var local = Vector2.Lerp(Vector2.Lerp(subtexture.DrawCoords[0], subtexture.DrawCoords[1], u),
			Vector2.Lerp(subtexture.DrawCoords[3], subtexture.DrawCoords[2], u), v);
		var point = position + local - origin;
		var ripple = new Vector2(
			MathF.Sin(time * 1.7f + u * 12f + v * 3f),
			MathF.Sin(time * 1.25f + u * 4f + v * 9f)) * LogoIdleAmplitude;

		var delta = point - mouse;
		var distance = delta.Length();
		if (distance < LogoMouseRadius)
		{
			var influence = 1 - distance / LogoMouseRadius;
			influence *= influence;
			var direction = distance > 0.001f ? delta / distance : Vector2.UnitY;
			var tangent = new Vector2(-direction.Y, direction.X);
			ripple += direction * (LogoMousePush * influence) + tangent * (MathF.Sin(time * 7f + distance * 0.08f) * LogoMousePush * 0.35f * influence);
		}

		return point + ripple;
	}

	static Vector2 LogoUv(Subtexture subtexture, float u, float v)
		=> Vector2.Lerp(Vector2.Lerp(subtexture.TexCoords[0], subtexture.TexCoords[1], u),
			Vector2.Lerp(subtexture.TexCoords[3], subtexture.TexCoords[2], u), v);

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

	Vector2 MenuMouse
		=> (Input.Mouse.Position - MenuPresentOffset) / MenuPresentScale - gameMenuLayoutOffset;

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
