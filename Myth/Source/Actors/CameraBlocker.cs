using System.Numerics;
using Foster.Framework;

namespace Myth;

// An authored rectangle of "negative space" the gameplay camera refuses to show into, so it
// can shape what's visible without changing room geometry. Used two ways:
//   * permanent: bite the dead corner out of an L-shaped room so the camera locks to the
//     occupied arm (the rect is never entered, never revealed);
//   * secret: hide an area until the player crosses into it, or until a RevealDoor whose
//     revealed area intersects this rect opens.
// Blockers should bite in from a room edge/corner (not float in the interior) so the camera
// clamp stays continuous -- see Game.ClampViewToRoomAndBlockers.
[GameActor(Sprite = "door", BoundsW = Game.TileSize * 4, BoundsH = Game.TileSize * 4, LivePositionEditable = true)]
public class CameraBlocker : Actor, IResizable
{
	public static readonly Point2 DefaultSize = new(Game.TileSize * 4, Game.TileSize * 4);

	public Point2 Size { get; set; } = DefaultSize;

	// Runtime-only: blockers reset to active each time the room (re)loads, so reveals last
	// for the visit but not across reloads.
	[NoSerialize] public bool Active = true;

	public CameraBlocker()
	{
		Visible = false;
		CollidesWithSolids = false;
		Hitbox = new(new RectInt(0, 0, 0, 0));
	}

	// World-space rect this blocker occupies (Position is the rect's top-left corner, matching
	// how the editor authors resizable room rects).
	public RectInt WorldRect => new(Position.X, Position.Y, Size.X, Size.Y);

	public override void Update()
	{
		base.Update();
		if (!Active)
			return;

		// Crossing the border reveals it.
		if (Game.GetFirst<Player>() is { } player && WorldRect.Contains((Vector2)player.Position))
			Deactivate();
	}

	// Turn the bite off; the camera notices the clamp target jump and eases over it.
	public void Deactivate()
	{
		if (!Active)
			return;
		Active = false;
	}
}
