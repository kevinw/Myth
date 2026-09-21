using System.Numerics;
using System.Text.Json.Nodes;
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
[GameActor(Sprite = "door", BoundsW = Game.TileSize * 4, BoundsH = Game.TileSize * 4, LivePositionEditable = true, ShowFacing = false)]
public class CameraBlocker : Actor, IResizable
{
	public static readonly Point2 DefaultSize = new(Game.TileSize * 4, Game.TileSize * 4);

	public Point2 Size { get; set; } = DefaultSize;
	public bool HighlightOnMap = false;

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
	public RectInt WorldRect => SnapToRoomEdges(new(Position.X, Position.Y, Size.X, Size.Y), Room);

	// A bite only bites when its edge is flush with the room's -- see Game.ClampViewToRoomAndBlockers,
	// which tests `b.Top <= bounds.Top` and the like. Authoring is a drag, so a pixel of slop is easy
	// to leave behind, and it produces a blocker that looks right in the editor and silently does
	// nothing in game. Anything this close to an edge meant to touch it.
	const int EdgeSnap = 2;

	static RectInt SnapToRoomEdges(RectInt rect, Room? room)
	{
		if (room == null)
			return rect;
		var bounds = room.WorldBounds;
		return RectInt.Between(
			new Point2(
				Math.Abs(rect.Left - bounds.Left) <= EdgeSnap ? bounds.Left : rect.Left,
				Math.Abs(rect.Top - bounds.Top) <= EdgeSnap ? bounds.Top : rect.Top),
			new Point2(
				Math.Abs(rect.Right - bounds.Right) <= EdgeSnap ? bounds.Right : rect.Right,
				Math.Abs(rect.Bottom - bounds.Bottom) <= EdgeSnap ? bounds.Bottom : rect.Bottom));
	}

	// The world rect an authored blocker spec occupies, without instantiating it -- room
	// transitions, the editor, and the minimap all need this before the blocker is a live actor.
	// The spec's Position is room-local, so it's offset by the room origin (the live WorldRect's
	// Position is already world-space). Keys are read by member name so a rename can't silently
	// drift them. The single spec-space counterpart to WorldRect; prefer the live WorldRect when
	// an instance exists.
	public static RectInt WorldRectFromSpec(Room room, JsonObject spec)
	{
		var local = ActorSpecs.ReadResolvedPoint(spec, nameof(Position)) ?? Point2.Zero;
		var size = ActorSpecs.ReadResolvedPoint(spec, nameof(Size)) ?? DefaultSize;
		return SnapToRoomEdges(new RectInt(room.WorldBounds.TopLeft + local, size), room);
	}

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
	internal void Deactivate()
	{
		if (!Active) return;
		Active = false;
	}
}
