using System.ComponentModel.DataAnnotations;
using System.Drawing;
using System.Numerics;
using System.Reflection.Metadata;
using Foster.Framework;

namespace TinyLink;

using Color = Foster.Framework.Color;

/// <summary>
/// Uses the Room's Text Value to show text.
/// </summary>
public class TitleText : Actor
{
	private float stateDuration = 0;
	public float RevealSpeed = 7.0f;
	public override bool CastsSpriteShadow => false;

	public override void Update()
	{
		base.Update();
		stateDuration += Time.Delta * RevealSpeed;
	}

	public override void Render(Batcher batcher)
	{
		if (Assets.Font == null || Game.CurrentRoom == null) return;
		var text = Game.CurrentRoom.Text;
		var length = (int)stateDuration;
		var span = text.AsSpan()[..Math.Min(text.Length, length)];
		batcher.Text(Assets.Font, span, Vector2.Zero, Vector2.One * 0.50f, Color.White);
	}
}
