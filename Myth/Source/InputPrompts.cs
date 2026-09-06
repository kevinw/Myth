using System.Numerics;
using Foster.Framework;

namespace Myth;

enum InputPromptAction
{
	Jump,
	Attack,
	Move,
	MoveUp,
	MoveDown,
	MoveLeft,
	MoveRight,
	Confirm,
	Back
}

readonly record struct InputPromptGlyph(InputPromptAction Action, Vector2 Center, float Height, Color Color, bool MapOverlay);

sealed class InputPrompts : IDisposable
{
	static float InlineGlyphScale => 1.3f;

	enum GlyphSet
	{
		Keyboard,
		Generic,
		PlayStation,
		Xbox,
		Nintendo,
		SteamDeck
	}

	readonly Input input;
	readonly SpriteFont keyboard;
	readonly SpriteFont generic;
	readonly SpriteFont playStation;
	readonly SpriteFont xbox;
	readonly SpriteFont nintendo;
	readonly SpriteFont steamDeck;
	readonly bool isSteamDeck = Environment.GetEnvironmentVariable("SteamDeck") == "1";
	TimeSpan mouseWheelTimestamp;
	ControllerID movementGlyphController;
	TimeSpan movementGlyphTimestamp;
	bool movementGlyphsUseDPad;
	GlyphSet activeSet;

	public InputPrompts(GraphicsDevice graphics, Input input)
	{
		this.input = input;
		activeSet = isSteamDeck ? GlyphSet.SteamDeck : GlyphSet.Keyboard;
		keyboard = LoadFont(graphics, "Fonts/kenney-input-keyboard-mouse.ttf", [0xE025, 0xE023, 0xE01D, 0xE01F, 0xE021, 0xE05A, 0xE05E, 0xE062, 0xE0CC]);
		generic = LoadFont(graphics, "Fonts/kenney-input-generic.ttf", [0xE000, 0xE001, 0xE007, 0xE01B, 0xE01C, 0xE01E, 0xE020, 0xE021]);
		playStation = LoadFont(graphics, "Fonts/kenney-input-playstation.ttf", [0xE041, 0xE04B, 0xE051, 0xE055, 0xE058, 0xE05C, 0xE05F, 0xE061, 0xE064, 0xE065, 0xE067, 0xE069, 0xE06A]);
		xbox = LoadFont(graphics, "Fonts/kenney-input-xbox.ttf", [0xE004, 0xE006, 0xE01E, 0xE022, 0xE025, 0xE029, 0xE02C, 0xE036, 0xE04F, 0xE050, 0xE052, 0xE054, 0xE055]);
		nintendo = LoadFont(graphics, "Fonts/kenney-input-nintendo-switch.ttf", [0xE004, 0xE006, 0xE01A, 0xE031, 0xE034, 0xE038, 0xE03B, 0xE03D, 0xE05A, 0xE05B, 0xE05D, 0xE05F, 0xE060]);
		steamDeck = LoadFont(graphics, "Fonts/kenney-input-steam-deck.ttf", [0xE001, 0xE003, 0xE01D, 0xE021, 0xE024, 0xE028, 0xE02B, 0xE02D, 0xE030, 0xE031, 0xE033, 0xE035, 0xE036]);
	}

	public void Update(TimeSpan elapsed)
	{
		if (input.Mouse.Wheel != Vector2.Zero)
			mouseWheelTimestamp = elapsed;

		var keyboardMouseTimestamp = input.Keyboard.InputTimestamp;
		for (var i = 0; i < MouseState.MaxButtons; i++)
			if (input.Mouse.PressedTimestamp((MouseButtons)i) > keyboardMouseTimestamp)
				keyboardMouseTimestamp = input.Mouse.PressedTimestamp((MouseButtons)i);
		if (mouseWheelTimestamp > keyboardMouseTimestamp)
			keyboardMouseTimestamp = mouseWheelTimestamp;

		ControllerState? latest = null;
		foreach (var controller in input.Controllers)
			if (controller.Connected && controller.IsGamepad &&
				(latest == null || controller.InputTimestamp > latest.InputTimestamp))
				latest = controller;

		if (latest == null || latest.InputTimestamp <= keyboardMouseTimestamp)
		{
			if (keyboardMouseTimestamp > TimeSpan.Zero || !isSteamDeck)
				activeSet = GlyphSet.Keyboard;
			return;
		}

		UpdateMovementGlyphSource(latest);
		activeSet = isSteamDeck
			? GlyphSet.SteamDeck
			: latest.GamepadProvider switch
			{
				GamepadProviders.PlayStation => GlyphSet.PlayStation,
				GamepadProviders.Xbox => GlyphSet.Xbox,
				GamepadProviders.Nintendo => GlyphSet.Nintendo,
				_ => GlyphSet.Generic
			};
	}

	public void DrawText(Batcher batcher, SpriteFont textFont, ReadOnlySpan<char> text, Vector2 position,
		float size, Vector2 justify, Color color, int visibleElements = int.MaxValue,
		List<InputPromptGlyph>? deferredGlyphs = null, bool drawGlyphs = true, bool mapOverlay = false,
		float lineHeightMultiplier = 1)
	{
		text = text[..VisibleLength(text, visibleElements)];
		if (text.IsEmpty)
			return;

		var baseLineHeight = textFont.LineHeight * size / textFont.Size;
		var lineHeight = baseLineHeight * lineHeightMultiplier;
		var textHeight = textFont.Height * size / textFont.Size;
		var glyphHeight = textHeight * InlineGlyphScale;
		var blockHeight = textFont.HeightOf(text, size) + (lineHeight - baseLineHeight) * text.Count('\n');
		var at = (position + new Vector2(
			-justify.X * WidthOfLine(textFont, text, size, glyphHeight),
			-justify.Y * blockHeight)).Round() - position;
		var runStart = 0;
		var index = 0;

		while (index < text.Length)
		{
			if (text[index] == '\n')
			{
				at.X += DrawRun(batcher, textFont, text[runStart..index], position + at, size, color);
				index++;
				runStart = index;
				at = (position + new Vector2(
					-justify.X * WidthOfLine(textFont, text[index..], size, glyphHeight),
					at.Y + lineHeight)).Round() - position;
				continue;
			}

			if (TryReadAction(text[index..], out var action, out var tokenLength))
			{
				at.X += DrawRun(batcher, textFont, text[runStart..index], position + at, size, color);
				var glyphPosition = position + at + new Vector2(0, (textHeight - glyphHeight) * 0.5f);
				var glyphWidth = GlyphWidth(action, glyphHeight);
				if (deferredGlyphs != null)
				{
					var center = glyphPosition + new Vector2(glyphWidth, glyphHeight) * 0.5f;
					deferredGlyphs.Add(new(
						action,
						Vector2.Transform(center, batcher.Matrix),
						Vector2.TransformNormal(Vector2.UnitY * glyphHeight, batcher.Matrix).Length(),
						color,
						mapOverlay));
				}
				else if (drawGlyphs)
					DrawGlyph(batcher, action, glyphPosition, glyphHeight, color);
				at.X += glyphWidth;
				index += tokenLength;
				runStart = index;
				continue;
			}

			index++;
		}

		DrawRun(batcher, textFont, text[runStart..index], position + at, size, color);
	}

	float WidthOfLine(SpriteFont textFont, ReadOnlySpan<char> text, float size, float glyphHeight)
	{
		var width = 0f;
		var runStart = 0;
		var index = 0;
		while (index < text.Length && text[index] != '\n')
		{
			if (TryReadAction(text[index..], out var action, out var tokenLength))
			{
				width += textFont.WidthOf(text[runStart..index], size);
				width += GlyphWidth(action, glyphHeight);
				index += tokenLength;
				runStart = index;
				continue;
			}
			index++;
		}
		return width + textFont.WidthOf(text[runStart..index], size);
	}

	static int VisibleLength(ReadOnlySpan<char> text, int visibleElements)
	{
		var index = 0;
		var revealed = 0;
		while (index < text.Length && revealed < visibleElements)
		{
			if (text[index] == '\n')
			{
				index++;
				continue;
			}
			if (TryReadAction(text[index..], out _, out var tokenLength))
				index += tokenLength;
			else
				index++;
			revealed++;
		}
		return index;
	}

	static float DrawRun(Batcher batcher, SpriteFont font, ReadOnlySpan<char> text, Vector2 position, float size, Color color)
	{
		if (text.IsEmpty)
			return 0;
		batcher.Text(font, text, position, size, color);
		return font.WidthOf(text, size);
	}

	public void DrawGlyphCentered(Batcher batcher, InputPromptAction action, Vector2 center, float height, Color color)
	{
		var (font, codepoint) = Glyph(action);
		var size = height * font.Size / font.Height;
		Span<char> glyph = stackalloc char[1] { (char)codepoint };
		batcher.Text(font, glyph, center, new Vector2(0.5f), size, color);
	}

	void DrawGlyph(Batcher batcher, InputPromptAction action, Vector2 position, float height, Color color)
	{
		var (font, codepoint) = Glyph(action);
		var size = height * font.Size / font.Height;
		Span<char> glyph = stackalloc char[1] { (char)codepoint };
		batcher.Text(font, glyph, position, size, color);
	}

	float GlyphWidth(InputPromptAction action, float height)
	{
		var (font, codepoint) = Glyph(action);
		var size = height * font.Size / font.Height;
		Span<char> glyph = stackalloc char[1] { (char)codepoint };
		return font.WidthOf(glyph, size);
	}

	(SpriteFont Font, int Codepoint) Glyph(InputPromptAction action)
		=> activeSet switch
		{
			GlyphSet.Keyboard => (keyboard, action switch
			{
				InputPromptAction.Move => 0xE025,
				InputPromptAction.MoveUp => 0xE023,
				InputPromptAction.MoveDown => 0xE01D,
				InputPromptAction.MoveLeft => 0xE01F,
				InputPromptAction.MoveRight => 0xE021,
				InputPromptAction.Confirm => 0xE05E,
				InputPromptAction.Back => 0xE062,
				InputPromptAction.Attack => 0xE05A,
				_ => 0xE0CC
			}),
			GlyphSet.PlayStation => (playStation, action switch
			{
				InputPromptAction.Back => 0xE041,
				InputPromptAction.Confirm or InputPromptAction.Jump => 0xE04B,
				InputPromptAction.Attack => 0xE051,
				_ => movementGlyphsUseDPad ? DPad(0xE055, action) : Stick(0xE064, action)
			}),
			GlyphSet.Xbox => (xbox, action switch
			{
				InputPromptAction.Back => 0xE006,
				InputPromptAction.Confirm or InputPromptAction.Jump => 0xE004,
				InputPromptAction.Attack => 0xE01E,
				_ => movementGlyphsUseDPad ? DPad(0xE022, action) : Stick(0xE04F, action)
			}),
			GlyphSet.Nintendo => (nintendo, action switch
			{
				InputPromptAction.Back => 0xE004,
				InputPromptAction.Confirm or InputPromptAction.Jump => 0xE006,
				InputPromptAction.Attack => 0xE01A,
				_ => movementGlyphsUseDPad ? DPad(0xE031, action) : Stick(0xE05A, action)
			}),
			GlyphSet.SteamDeck => (steamDeck, action switch
			{
				InputPromptAction.Back => 0xE003,
				InputPromptAction.Confirm or InputPromptAction.Jump => 0xE001,
				InputPromptAction.Attack => 0xE01D,
				_ => movementGlyphsUseDPad ? DPad(0xE021, action) : Stick(0xE030, action)
			}),
			_ => (generic, action switch
			{
				InputPromptAction.Back => 0xE001,
				InputPromptAction.Confirm or InputPromptAction.Jump => 0xE000,
				InputPromptAction.Attack => 0xE007,
				_ => Stick(0xE01B, action)
			})
		};

	void UpdateMovementGlyphSource(ControllerState controller)
	{
		var dpad = controller.Timestamp(Buttons.Up);
		if (controller.Timestamp(Buttons.Down) > dpad) dpad = controller.Timestamp(Buttons.Down);
		if (controller.Timestamp(Buttons.Left) > dpad) dpad = controller.Timestamp(Buttons.Left);
		if (controller.Timestamp(Buttons.Right) > dpad) dpad = controller.Timestamp(Buttons.Right);
		var stick = controller.Timestamp(Axes.LeftX);
		if (controller.Timestamp(Axes.LeftY) > stick) stick = controller.Timestamp(Axes.LeftY);

		if (controller.ID != movementGlyphController)
		{
			movementGlyphController = controller.ID;
			movementGlyphsUseDPad = dpad > stick;
			movementGlyphTimestamp = dpad > stick ? dpad : stick;
		}
		if (dpad > movementGlyphTimestamp)
		{
			movementGlyphsUseDPad = true;
			movementGlyphTimestamp = dpad;
		}
		if (controller.LeftStick.LengthSquared() > Controls.MoveStickDeadzone * Controls.MoveStickDeadzone &&
			stick > movementGlyphTimestamp)
		{
			movementGlyphsUseDPad = false;
			movementGlyphTimestamp = stick;
		}
	}

	// The outline variants visibly fill the requested arm; the solid directional glyphs
	// are indistinguishable from the neutral D-pad in these monochrome fonts.
	static int DPad(int neutral, InputPromptAction action) => neutral + action switch
	{
		InputPromptAction.MoveDown => 3,
		InputPromptAction.MoveLeft => 7,
		InputPromptAction.MoveRight => 10,
		InputPromptAction.MoveUp => 12,
		_ => 0
	};

	// Kenney controller fonts lay out the left-stick directions at a fixed offset
	// from the neutral stick glyph: down +1, left +3, right +5, up +6.
	static int Stick(int neutral, InputPromptAction action) => neutral + action switch
	{
		InputPromptAction.MoveDown => 1,
		InputPromptAction.MoveLeft => 3,
		InputPromptAction.MoveUp => 6,
		InputPromptAction.MoveRight => 5,
		_ => 0
	};

	static bool TryReadAction(ReadOnlySpan<char> text, out InputPromptAction action, out int length)
	{
		if (text.StartsWith("{Jump}", StringComparison.OrdinalIgnoreCase))
			return Found(InputPromptAction.Jump, 6, out action, out length);
		if (text.StartsWith("{Attack}", StringComparison.OrdinalIgnoreCase))
			return Found(InputPromptAction.Attack, 8, out action, out length);
		if (text.StartsWith("{MoveUp}", StringComparison.OrdinalIgnoreCase))
			return Found(InputPromptAction.MoveUp, 8, out action, out length);
		if (text.StartsWith("{MoveDown}", StringComparison.OrdinalIgnoreCase))
			return Found(InputPromptAction.MoveDown, 10, out action, out length);
		if (text.StartsWith("{MoveLeft}", StringComparison.OrdinalIgnoreCase))
			return Found(InputPromptAction.MoveLeft, 10, out action, out length);
		if (text.StartsWith("{MoveRight}", StringComparison.OrdinalIgnoreCase))
			return Found(InputPromptAction.MoveRight, 11, out action, out length);
		if (text.StartsWith("{Move}", StringComparison.OrdinalIgnoreCase))
			return Found(InputPromptAction.Move, 6, out action, out length);
		if (text.StartsWith("{Confirm}", StringComparison.OrdinalIgnoreCase))
			return Found(InputPromptAction.Confirm, 9, out action, out length);
		if (text.StartsWith("{Back}", StringComparison.OrdinalIgnoreCase))
			return Found(InputPromptAction.Back, 6, out action, out length);
		action = default;
		length = 0;
		return false;
	}

	static bool Found(InputPromptAction found, int foundLength, out InputPromptAction action, out int length)
	{
		action = found;
		length = foundLength;
		return true;
	}

	static SpriteFont LoadFont(GraphicsDevice graphics, string path, int[] codepoints)
	{
		using var stream = Assets.Content.OpenRead(path);
		return new SpriteFont(graphics, stream, 128, codepoints);
	}

	public void Dispose()
	{
		keyboard.Dispose();
		generic.Dispose();
		playStation.Dispose();
		xbox.Dispose();
		nintendo.Dispose();
		steamDeck.Dispose();
	}
}
