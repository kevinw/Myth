using System.Numerics;
using Foster.Framework;

namespace Myth;

using Color = Foster.Framework.Color;

/// <summary>
/// Shows text placed as actor metadata.
/// </summary>
[GameActor(Sprite = "heart")]
public class TitleText : Actor
{
	private float stateDuration = 0;
	public string Text = string.Empty;
	public float RevealSpeed = 7.0f;
	public override bool CastsSpriteShadow => false;

	public override void Update()
	{
		base.Update();
		stateDuration += Time.Delta * RevealSpeed;
	}

	public override void Render(Batcher batcher)
	{
		if (Assets.Font == null || string.IsNullOrEmpty(Text)) return;
		var text = Text;
		var length = (int)stateDuration;
		var span = text.AsSpan()[..Math.Min(text.Length, length)];
		batcher.Text(Assets.Font, span, Vector2.Zero, Vector2.One * 0.50f, Color.White);
	}
}
