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
	public override bool CastsSpriteShadow => false;

	public override void Update()
	{
		stateDuration += Time.Delta;
	}
	
	public override void Render(Batcher batcher)
	{
		if (Assets.Font != null && Game.CurrentRoom != null)
		{
			var text = Game.CurrentRoom.Text;
			var numChars = text.Length;
			var length = (int)stateDuration;
			text = text.Substring(0, Math.Min(text.Length, length));
			batcher.Text(Assets.Font, text, Vector2.Zero, Vector2.One * 0.50f, Color.White);
		}
	}
}
