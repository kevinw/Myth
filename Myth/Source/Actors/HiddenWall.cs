using Foster.Framework;

namespace Myth;

[GameActor(Sprite = "door", BoundsW = Game.TileSize * 2, BoundsH = Game.TileSize * 2, Tags = ActorTag.Door)]
public class HiddenWall : Actor, IResizable
{
	public static readonly Point2 DefaultSize = new(Game.TileSize * 2, Game.TileSize * 2);

	public Point2 Size { get; set; } = DefaultSize;
	public string TileType = "castle";

	public HiddenWall()
	{
		Visible = false;
		CollidesWithSolids = false;
		Hitbox = new(new RectInt(0, 0, 0, 0));
	}
}
