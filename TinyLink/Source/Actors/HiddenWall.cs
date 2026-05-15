using Foster.Framework;

namespace TinyLink;

public class HiddenWall : Actor
{
	public static readonly Point2 DefaultSize = new(Game.TileSize * 2, Game.TileSize * 2);

	public Point2 Size = DefaultSize;

	public HiddenWall()
	{
		Visible = false;
		CollidesWithSolids = false;
		Hitbox = new(new RectInt(0, 0, 0, 0));
	}
}
