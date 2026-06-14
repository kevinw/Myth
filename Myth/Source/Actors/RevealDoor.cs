using Foster.Framework;

namespace Myth;

public class RevealDoor : Actor
{
	public enum RevealDirection
	{
		Left,
		Right,
		Up,
		Down
	}

	public static readonly Point2 DefaultSize = new(Game.TileSize, Game.TileSize * 2);

	public Point2 Size = DefaultSize;
	public RevealDirection Direction = RevealDirection.Right;
	public string TileType = "";

	public RevealDoor()
	{
		Visible = false;
		CollidesWithSolids = false;
		Hitbox = new(new RectInt(0, 0, 0, 0));
	}
}
