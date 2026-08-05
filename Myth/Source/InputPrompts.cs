using System.Numerics;
using Foster.Framework;

namespace Myth;

enum InputPromptAction
{
	Jump,
	Move,
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
	GlyphSet activeSet;

	public InputPrompts(GraphicsDevice graphics, Input input)
	{
		this.input = input;
		activeSet = isSteamDeck ? GlyphSet.SteamDeck : GlyphSet.Keyboard;
		keyboard = LoadFont(graphics, "Fonts/kenney-input-keyboard-mouse.ttf", [0xE025, 0xE05E, 0xE062, 0xE0CC]);
		generic = LoadFont(graphics, "Fonts/kenney-input-generic.ttf", [0xE000, 0xE001, 0xE01B]);
		playStation = LoadFont(graphics, "Fonts/kenney-input-playstation.ttf", [0xE041, 0xE04B, 0xE064]);
		xbox = LoadFont(graphics, "Fonts/kenney-input-xbox.ttf", [0xE004, 0xE006, 0xE04F]);
		nintendo = LoadFont(graphics, "Fonts/kenney-input-nintendo-switch.ttf", [0xE004, 0xE006, 0xE05A]);
		steamDeck = LoadFont(graphics, "Fonts/kenney-input-steam-deck.ttf", [0xE001, 0xE003, 0xE030]);
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
				InputPromptAction.Confirm => 0xE05E,
				InputPromptAction.Back => 0xE062,
				_ => 0xE0CC
			}),
			GlyphSet.PlayStation => (playStation, action switch
			{
				InputPromptAction.Move => 0xE064,
				InputPromptAction.Back => 0xE041,
				_ => 0xE04B
			}),
			GlyphSet.Xbox => (xbox, action switch
			{
				InputPromptAction.Move => 0xE04F,
				InputPromptAction.Back => 0xE006,
				_ => 0xE004
			}),
			GlyphSet.Nintendo => (nintendo, action switch
			{
				InputPromptAction.Move => 0xE05A,
				InputPromptAction.Back => 0xE004,
				_ => 0xE006
			}),
			GlyphSet.SteamDeck => (steamDeck, action switch
			{
				InputPromptAction.Move => 0xE030,
				InputPromptAction.Back => 0xE003,
				_ => 0xE001
			}),
			_ => (generic, action switch
			{
				InputPromptAction.Move => 0xE01B,
				InputPromptAction.Back => 0xE001,
				_ => 0xE000
			})
		};

	static bool TryReadAction(ReadOnlySpan<char> text, out InputPromptAction action, out int length)
	{
		if (text.StartsWith("{Jump}", StringComparison.OrdinalIgnoreCase))
			return Found(InputPromptAction.Jump, 6, out action, out length);
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
