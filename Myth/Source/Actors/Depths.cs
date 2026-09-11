namespace Myth;

// Draw order for Actor.Depth. The render list is sorted descending by depth
// (Game.Rendering.cs, PrepareRenderingList) and drawn in that order, so the
// highest value is drawn first and ends up furthest back: larger is behind,
// smaller is in front, and negatives sit closest to the camera.
//
// Ties sort arbitrarily -- List.Sort is unstable -- so two actors that must
// hold a fixed order need distinct values.
public static class Depths
{
	public const int BackgroundGrid = 10;
	public const int BackgroundDualGrid = 9;
	public const int BackgroundProp = 8;

	public const int ForegroundDualGrid = 7;
	public const int ForegroundVisualGrid = 6;
	public const int ForegroundGrid = 5;
	public const int ForegroundProp = 4;

	// Lights, wildlife, particle emitters, linked doors: set dressing that sits behind
	// the characters without falling back into the tile layers.
	public const int Ambient = 2;

	// Companions -- behind the player, in front of the ambient dressing.
	public const int BehindPlayer = 1;

	public const int Player = 0;

	public const int Door = -1;
	public const int Button = -2;
	public const int Character = -4;
	public const int Enemy = -5;
	public const int ForegroundSmoke = -10;
	public const int Pickup = -20;
	public const int Goal = -100;
	public const int HiddenCover = -1000;
	public const int Cutscene = -10000;
}
