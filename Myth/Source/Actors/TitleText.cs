using System.Numerics;
using Foster.Framework;

namespace Myth;

using Color = Foster.Framework.Color;

// Shows text placed as actor metadata.
[GameActor(Sprite = "Editor/text", Tags = ActorTag.System, LivePositionEditable = true)]
public class TitleText : Actor
{
	internal const string EmptyEditorText = "<empty>";

	float stateDuration;
	public string Text = string.Empty;
	[I(0.5f, 30)] public float RevealSpeed = 7.0f;
	[I(0.5f, 2)] public float LineHeightMultiplier = 1f;
	public override bool CastsSpriteShadow => false;
	public bool Emissive = false;

	public override void Update()
	{
		base.Update();
		stateDuration += Time.Delta * RevealSpeed;
	}

	SpriteFont? ShouldRenderWithFont() => Game.Simulating ? null : Assets.SpeechFont ?? Assets.Font;
	internal static string EditorText(string? text) => string.IsNullOrEmpty(text) ? EmptyEditorText : text;

	internal static RectInt EditorBounds(Point2 position, string? text, float lineHeightMultiplier = 1)
	{
		if ((Assets.SpeechFont ?? Assets.Font) is not { } font)
			return new(position - new Point2(4, 4), new Point2(8, 8));

		var display = EditorText(text);
		var size = font.SizeOf(display);
		size.Y += font.LineHeight * (lineHeightMultiplier - 1) * display.AsSpan().Count('\n');
		return new(position + (-size * 0.5f).RoundToPoint2(), size.CeilingToPoint2());
	}

	public override void Render(Batcher batcher) => DoRender(batcher, Game.View?.InputPromptGlyphs);
	public override void RenderEmissive(Batcher batcher) {
		if (Emissive) {
			DoRender(batcher, null);
		}
	}

	void DoRender(Batcher batcher, List<InputPromptGlyph>? deferredGlyphs) {
		var font = ShouldRenderWithFont();
		if (font == null) return;
		var text = Game.Editing ? EditorText(Text) : Text;

		Game.Manager.InputPrompts?.DrawText(
			batcher,
			font,
			text,
			Vector2.Zero,
			font.Size,
			new Vector2(0.5f),
			Color.White,
			Game.Editing ? int.MaxValue : (int)stateDuration,
			deferredGlyphs,
			drawGlyphs: false,
			lineHeightMultiplier: LineHeightMultiplier);
	}
}
