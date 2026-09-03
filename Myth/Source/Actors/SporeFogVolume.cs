using System.Numerics;
using Foster.Framework;

namespace Myth;

[GameActor(Sprite = "Editor/particles", Anchor = SpriteAnchor.TopLeft, Tags = ActorTag.System,
	BoundsW = Game.TileSize * 8, BoundsH = Game.TileSize * 5, LivePositionEditable = true, LiveResizable = true)]
public class SporeFogVolume : Actor, IResizable
{
	public static readonly Point2 DefaultSize = new(Game.TileSize * 8, Game.TileSize * 5);

	public Point2 Size { get; set; } = DefaultSize;
	[I(0, 4)] public float Strength = 1;
	[I(0, 64)] public float Feather = 16;

	public RectInt WorldRect => new(Position, Size);

	public SporeFogVolume()
	{
		Sprite = null;
		CollidesWithSolids = false;
		Hitbox = new(new RectInt(0, 0, 0, 0));
	}

	public override void Render(Batcher batcher)
	{
		if (Game.Editing)
			batcher.Rect(new Rect(Vector2.Zero, Size), Game.Manager.Visuals.SporeFogTint * 0.18f);
	}

	public override void RenderNormals(Batcher batcher) { }
	public override void RenderShadow(Batcher batcher, Color color) { }
	public override void RenderReflection(Batcher batcher, Color color) { }
	public override bool CastsSpriteShadow => false;
	public override bool CastsReflection => false;
}
