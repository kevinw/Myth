using Foster.Framework;

namespace Myth;

[GameActor(Sprite = "door", BoundsW = Game.TileSize, BoundsH = Game.TileSize * 2, Tags = ActorTag.Door,
	LivePositionEditable = true, LiveResizable = true)]
public class RevealDoor : Actor, IResizable
{
	public enum RevealDirection
	{
		Left,
		Right,
		Up,
		Down
	}

	public static readonly Point2 DefaultSize = new(Game.TileSize, Game.TileSize * 2);

	public Point2 Size { get; set; } = DefaultSize;
	public RevealDirection Direction = RevealDirection.Right;
	public string TileType = "";

	public RevealDoor()
	{
		Visible = false;
		CollidesWithSolids = false;
		Hitbox = new(new RectInt(0, 0, 0, 0));
	}
}
