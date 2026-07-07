using System.Numerics;
using Foster.Framework;

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

	GameMenuPage gameMenuPage;
	GameMenuPage gameMenuReturnPage;
	int gameMenuSelection;
	float gameMenuInputRepeat;
	bool confirmRestart = false;

	bool GameMenuOpen => gameMenuPage != GameMenuPage.Closed;
	bool GameMenuPausesGame => GameMenuOpen && gameMenuPage != GameMenuPage.Title;

	static float MenuButtonWidth => 116f;
	static float MenuButtonHeight => 18f;
	static float MenuButtonGap => 3f;
	static float MenuPanelWidth => 174f;

	void ShowTitleMenu()
	{
		gameMenuPage = GameMenuPage.Title;
		gameMenuReturnPage = GameMenuPage.Title;
		gameMenuSelection = 0;
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
		confirmRestart = false;
	}

	void CloseGameMenu()
	{
		gameMenuPage = GameMenuPage.Closed;
		gameMenuSelection = 0;
		confirmRestart = false;
	}

		void MenuAudioEvent(string path) => Audio.CreateEvent($"event:/{path}")?.Start();

	void UpdateGameMenuInput()
	{
		if (!GameMenuOpen && playing && !startingPlay && game?.MinimapOpen != true && controls.Map.ConsumePress())
		{
			ShowPauseMenu();
			return;
		}

		if (!GameMenuOpen)
			return;

		if (gameMenuInputRepeat > 0)
			gameMenuInputRepeat = Calc.Approach(gameMenuInputRepeat, 0, Time.Delta);

		var itemCount = GameMenuItemCount(gameMenuPage);
		var move = GameMenuMove();
		if (move != 0 && gameMenuInputRepeat <= 0)
		{
			MenuAudioEvent("menu/select");
			gameMenuSelection = (gameMenuSelection + move + itemCount) % itemCount;
			gameMenuInputRepeat = 0.14f;
		}

		if (Input.Keyboard.Pressed(Keys.Escape) || ControllerPressed(Buttons.East))
		{
			MenuAudioEvent("menu/back");
			if (gameMenuPage == GameMenuPage.Options)
				ShowGameMenuPage(gameMenuReturnPage);
			else if (gameMenuPage == GameMenuPage.Pause)
				CloseGameMenu();
			return;
		}

		GameMenuCommand? didActivate = null;
		if (GameMenuMousePressed(out var clicked))
		{
			ActivateGameMenuCommand(clicked);
			didActivate = clicked;
		}
		else if (Input.Keyboard.Pressed(Keys.Enter, Keys.KeypadEnter, Keys.Space) || ControllerPressed(Buttons.South) || controls.Map.ConsumePress())
		{
			var command = GameMenuCommandAt(gameMenuPage, gameMenuSelection);
			ActivateGameMenuCommand(command);
			didActivate = command;
		}

		if (didActivate != null) {
			if (didActivate is GameMenuCommand.Resume or GameMenuCommand.Start)
				MenuAudioEvent("menu/show");
			else if (didActivate is GameMenuCommand.Back or GameMenuCommand.Title or GameMenuCommand.Quit)
				MenuAudioEvent("menu/back");
			else
				MenuAudioEvent("menu/proceed");
		}
	}

	int GameMenuMove()
	{
		if (Input.Keyboard.Pressed(Keys.Up, Keys.W) || ControllerPressed(Buttons.Up)) return -1;
		if (Input.Keyboard.Pressed(Keys.Down, Keys.S) || ControllerPressed(Buttons.Down)) return 1;

		var y = controls.Move.IntValue.Y;
		return y < 0 ? -1 : y > 0 ? 1 : 0;
	}

	bool ControllerPressed(Buttons button) => Input.AnyControllerButtonPressed(button);

	void ShowGameMenuPage(GameMenuPage page)
	{
		gameMenuPage = page;
		gameMenuSelection = Math.Clamp(gameMenuSelection, 0, GameMenuItemCount(page) - 1);
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
				Console.WriteLine("confirmRestart = true");
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

	bool GameMenuMousePressed(out GameMenuCommand command)
	{
		command = default;
		if (!Input.Mouse.LeftPressed)
			return false;

		var itemCount = GameMenuItemCount(gameMenuPage);
		var start = GameMenuListStart(itemCount);
		var cursor = MenuCursor;
		for (var i = 0; i < itemCount; i++)
		{
			var rect = GameMenuButtonRect(start, i);
			if (!rect.Contains(cursor))
				continue;

			gameMenuSelection = i;
			command = GameMenuCommandAt(gameMenuPage, i);
			return true;
		}

		return false;
	}

	void DrawGameMenu()
	{
		if (!GameMenuOpen || Window == null)
			return;

		batcher.PushMatrix(MenuMatrix);
		var bgColor = Color.FromHexStringRGBA("0x160b18aa");
		batcher.Rect(MenuBounds, bgColor);
		DrawGameMenuPanel();
		batcher.PopMatrix();
	}

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

	Vector2 MenuCursor
		=> (Input.Mouse.Position - MenuPresentOffset) / MenuPresentScale;

	void DrawGameMenuPanel()
	{
		var itemCount = GameMenuItemCount(gameMenuPage);
		var panelHeight = 76f + itemCount * MenuButtonHeight + (itemCount - 1) * MenuButtonGap;
		var panel = Rect.CenteredAt(MenuBounds.Center, new Vector2(MenuPanelWidth, panelHeight));

		//batcher.Rect(panel, Color.Black * 0.78f);
		//batcher.RectLine(panel, 2, Color.Lerp(0x8b81b5, Color.Coral, Time.SineWave(0, 1, 10f)));

		var font = Assets.Font;
		if (font == null) return;

		var title = gameMenuPage switch
		{
			GameMenuPage.Title => "mycelon",
			GameMenuPage.Options => "options",
			_ => "paused"
		};


		// menu title
		{
			var pos = new Vector2(panel.Center.X, panel.Top + 25);
			DrawCenteredText(font, title, pos + Vector2.One * 1f, 18, Color.Lerp(0xfee761, Color.Coral, 0.8f));
			DrawCenteredText(font, title, pos, 18, 0xfee761);
		}

		var start = GameMenuListStart(itemCount);
		for (var i = 0; i < itemCount; i++)
		{
			var command = GameMenuCommandAt(gameMenuPage, i);
			DrawGameMenuButton(font, command, GameMenuButtonRect(start, i), i == gameMenuSelection, arrowIsBack: command == GameMenuCommand.Back || command == GameMenuCommand.Editor || command == GameMenuCommand.Title || command == GameMenuCommand.Quit);
		}

		var hint = gameMenuPage == GameMenuPage.Title ? "ENTER / START / A" : "ESC / B";

		{
			var pos = new Vector2(panel.Center.X, panel.Bottom - 17);
			DrawCenteredText(font, hint, pos, 8, 0x8b81b5);
		}
	}

	Vector2 GameMenuListStart(int itemCount)
	{
		var totalHeight = itemCount * MenuButtonHeight + (itemCount - 1) * MenuButtonGap;
		return new Vector2(MenuBounds.Center.X - MenuButtonWidth * 0.5f, MenuBounds.Center.Y - totalHeight * 0.5f + 7f);
	}

	static Rect GameMenuButtonRect(Vector2 start, int index)
		=> new(start.X, start.Y + index * (MenuButtonHeight + MenuButtonGap), MenuButtonWidth, MenuButtonHeight);

	void DrawGameMenuButton(SpriteFont font, GameMenuCommand command, Rect rect, bool selected, bool arrowIsBack = false)
	{
		var hovered = rect.Contains(MenuCursor);
		var active = selected || hovered;
		var activeLineColor = Color.Lerp(0x5fcde4, Color.AntiqueWhite, Time.SineWave(0, 1, 0.3f));
		batcher.Rect(rect, active ? 0x323c39 : 0x1b102e);
		batcher.RectLine(rect, 1, active ? activeLineColor : 0x5b5274);
		if (active) {

			if (arrowIsBack)
			{
				var pos1 = rect.Left + 7;
				var pos2 = rect.Left + 12;
				batcher.Triangle(new Vector2(pos1, rect.Center.Y), new Vector2(pos2, rect.Center.Y - 4), new Vector2(pos2, rect.Center.Y + 4), 0x5fcde4);
			}
			else
			{
				var pos1 = rect.Right - 7;
				var pos2 = rect.Right - 12;
				batcher.Triangle(new Vector2(pos1, rect.Center.Y), new Vector2(pos2, rect.Center.Y - 4), new Vector2(pos2, rect.Center.Y + 4), 0x5fcde4);
			}
		}

		DrawCenteredText(font, GameMenuLabel(command), rect.Center + new Vector2(4, 0), 8, active ? Color.White : 0xc0cbdc);
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

	void DrawCenteredText(SpriteFont font, ReadOnlySpan<char> text, Vector2 center, float size, Color color)
	{
		var position = new Vector2(
			MathF.Round(center.X - font.WidthOf(text, size) * 0.5f),
			MathF.Round(center.Y - font.LineHeight * size / font.Size * 0.5f));
		position.Y += 1.5f;
		batcher.Text(font, text, position, size, color);
	}
}
