namespace Myth;

public static class TileQuadrants
{
	public const int TopLeft = 1;
	public const int BottomLeft = 2;
	public const int TopRight = 4;
	public const int BottomRight = 8;
	public const int All = TopLeft | BottomLeft | TopRight | BottomRight;
}
