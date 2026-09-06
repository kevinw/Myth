using System.Numerics;
using Foster.Framework;

namespace Myth;

[GameActor(Sprite = "Editor/text", Icon = "magnifying-glass", Tags = ActorTag.Decoration,
	BoundsX = -12, BoundsY = -24, BoundsW = 24, BoundsH = 24,
	LivePositionEditable = true, ShowFacing = false)]
public sealed class InspectPoint : Actor
{
	[Multiline(4)] public string Text = "It seems dead.";

	bool consumed;
	bool showInteract;
	float interactTime;
	Game.StorySpeechBubble? speech;

	static readonly Color TextColor = Color.FloralWhite;
	static RectInt InteractionBounds => new(-12, -24, 24, 24);

	public InspectPoint()
	{
		Sprite = null;
		Hitbox = new(InteractionBounds);
		CollidesWithSolids = false;
	}

	public override bool CastsSpriteShadow => false;
	public override bool CastsReflection => false;

	public override void Update()
	{
		base.Update();
		showInteract = false;

		if (Game.GetFirst<Player>() is not { } player || !OverlapsAny(Point2.Zero, player))
		{
			consumed = false;
			interactTime = 0;
			return;
		}

		if (consumed || speech is { Finished: false } || player.State != Player.States.Normal || !player.IsGrounded ||
			string.IsNullOrWhiteSpace(Text))
			return;

		showInteract = true;
		interactTime += Time.Delta;
		if (Game.Controls.Move.PressedUp && Game.Controls.Move.IntValue.X == 0 && (speech = Speak()) != null)
		{
			consumed = true;
			showInteract = false;
		}
	}

	Game.StorySpeechBubble? Speak()
	{
		var text = Text.Trim();
		const string budPrefix = "Bud:";
		if (text.StartsWith(budPrefix, StringComparison.OrdinalIgnoreCase))
			return Game.GetFirst<Bud>()?.Say(text[budPrefix.Length..].TrimStart());
		const string playerPrefix = "Player:";
		if (text.StartsWith(playerPrefix, StringComparison.OrdinalIgnoreCase))
			return Game.GetFirst<Player>()?.Say(text[playerPrefix.Length..].TrimStart());

		return Game.ShowInspectText(this, new Vector2(0, InteractionBounds.Top), text, TextColor);
	}

	public override void RenderEmissive(Batcher batcher)
	{
		if (!showInteract || Assets.GetSprite("interact") is not { Animations.Count: > 0 } sprite)
			return;
		var frame = sprite.GetFrameAt(sprite.Animations[0], interactTime, true);
		batcher.Image(frame.Subtexture, new Vector2(-8, -39), sprite.Origin, Vector2.One, 0, Color.White);
	}

	public override Rect GetVisualBounds() => InteractionBounds + Position;
}
