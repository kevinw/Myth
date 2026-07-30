using System.Numerics;
using Foster.Framework;

namespace Myth;

using Color = Foster.Framework.Color;

// Shows text placed as actor metadata.
[GameActor(Sprite = "Editor/text", Tags = ActorTag.System)]
public class TitleText : Actor
{
	float stateDuration;
	public string Text = string.Empty;
	public float RevealSpeed = 7.0f;
	public override bool CastsSpriteShadow => false;
	public bool Emissive = false;

	public override void Update()
	{
		base.Update();
		stateDuration += Time.Delta * RevealSpeed;
	}

	SpriteFont? ShouldRenderWithFont() {
		if (Game.Simulating) return null;
		if (Assets.Font == null) return null;
		if (string.IsNullOrEmpty(Text)) return null;
		return Assets.Font;
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

		Game.Manager.InputPrompts?.DrawText(
			batcher,
			font,
			Text,
			Vector2.Zero,
			font.Size,
			new Vector2(0.5f),
			Color.White,
			Game.Editing ? int.MaxValue : (int)stateDuration,
			deferredGlyphs,
			drawGlyphs: false);
	}
}
