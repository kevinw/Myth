using System.Numerics;
using Foster.Framework;

namespace Myth;

using Color = Foster.Framework.Color;

// Shows text placed as actor metadata.
[GameActor(Sprite = "Editor/text", Icon = "font", Tags = ActorTag.System, LivePositionEditable = true)]
public class TitleText : Actor
{
	internal const string EmptyEditorText = "<empty>";

	[Multiline(3)] public string Text = string.Empty;
	[I(0.0f, 30)] public float RevealSpeed = 7.0f;
	[I(0.5f, 2)] public float LineHeightMultiplier = 1f;
	public Color Color = Color.WhiteSmoke;

	public int dropShadow = 0;
	public bool OutlineWithShadow = false;

	public override bool CastsSpriteShadow => false;
	public bool Emissive = false;

	float stateDuration;

	public override void Update()
	{
		base.Update();
		if (RevealSpeed > 0)
			stateDuration += Time.Delta * RevealSpeed;
	}

	SpriteFont? ShouldRenderWithFont() => Game.Simulating ? null : Assets.SpeechFont ?? Assets.Font;
	internal static string EditorText(string? text) => string.IsNullOrEmpty(text) ? EmptyEditorText : text;

	// World rect the text occupies, centered on position (blank text measures as the editor
	// placeholder). Drives the editor's actor bounds and the speech bubble's dodging.
	internal static RectInt TextBounds(Point2 position, string? text, float lineHeightMultiplier = 1)
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
		if (Emissive)  DoRender(batcher, null);
	}

	void DoRender(Batcher batcher, List<InputPromptGlyph>? deferredGlyphs)
	{
		var font = ShouldRenderWithFont();
		if (font == null) return;
		if (Game.Manager.InputPrompts is not { } inputPrompts) return;
		var text = Game.Editing ? EditorText(Text) : Text;
		int visibleElements = (Game.Editing || RevealSpeed == 0) ? int.MaxValue : (int)stateDuration;

		var pos = new Vector2();
		var justify = new Vector2(0.5f);

		if (dropShadow != 0) {
			var offset = new Vector2(dropShadow, dropShadow);
			var shadowColor = Color.Black;
			shadowColor.A = 170;
			var shadowPos = pos + offset;
			inputPrompts.DrawText(batcher, font, text, shadowPos, font.Size, justify, shadowColor, visibleElements, deferredGlyphs, drawGlyphs: false, lineHeightMultiplier: LineHeightMultiplier);

			if (OutlineWithShadow) {
				shadowPos = pos - offset;
				inputPrompts.DrawText(batcher, font, text, shadowPos, font.Size, justify, shadowColor, visibleElements, deferredGlyphs, drawGlyphs: false, lineHeightMultiplier: LineHeightMultiplier);
			}
		}

		inputPrompts.DrawText(batcher, font, text, pos, font.Size, justify, Color, visibleElements, deferredGlyphs, drawGlyphs: false, lineHeightMultiplier: LineHeightMultiplier);

	}
}
