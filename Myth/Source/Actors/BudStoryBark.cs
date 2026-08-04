using System.Numerics;
using Foster.Framework;

namespace Myth;

[GameActor(Sprite = "Editor/text", Anchor = SpriteAnchor.TopLeft, BoundsW = Game.TileSize * 4, BoundsH = Game.TileSize * 2,
	Tags = ActorTag.System, LivePositionEditable = true)]
public class BudStoryBark : Actor, IResizable
{
	public static readonly Point2 DefaultSize = new(Game.TileSize * 4, Game.TileSize * 2);

	public Point2 Size { get; set; } = DefaultSize;
	public string Text = "What's this place?";
	bool spoken;

	public BudStoryBark()
	{
		Sprite = null;
		CollidesWithSolids = false;
	}

	public override void Added()
	{
		base.Added();
		Hitbox = new(new RectInt(0, 0, Size.X, Size.Y));
	}

	public override void Update()
	{
		base.Update();
		if (!spoken && !string.IsNullOrWhiteSpace(Text) && Game.CanShowBudSpeech() && Game.GetFirst<Bud>() is { } bud && OverlapsAny(Point2.Zero, bud))
		{
			bud.Say(Text);
			spoken = true;
		}
	}

	public override void Render(Batcher batcher)
	{
		if (Game.Editing)
			batcher.Rect(new Rect(Vector2.Zero, Size), new Color(0x8ad4cc, 0.16f));
	}

	public override void RenderNormals(Batcher batcher) { }
	public override void RenderShadow(Batcher batcher, Color color) { }
	public override void RenderReflection(Batcher batcher, Color color) { }
	public override bool CastsSpriteShadow => false;
	public override bool CastsReflection => false;
}
