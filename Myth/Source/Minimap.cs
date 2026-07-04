using System.Numerics;
using Foster.Framework;

namespace Myth;

/// Playthrough-durable exploration record (rooms visited, exits taken, reveal doors opened)
/// plus the map overlay built from it. Tracking lives for the whole Game; the drawable map is
/// rebuilt from the bundled rooms each time the overlay opens, so it never goes stale.
public class Minimap
{
	/// A pulsing "more to explore" tick: a border opening (Direction = outward normal, Length
	/// tiles along the edge) to an unvisited room, a linked door (Direction = zero) whose
	/// destination hasn't been seen, or a camera frontier (an edge of the seen area with open
	/// space beyond it). Tile is world tile space.
	public readonly record struct ExitMarker(Point2 Tile, Point2 Direction, int Length);

	public readonly record struct MapRoom(RectInt TileBounds, RectInt SeenRect, List<RectInt> SolidRuns, List<StackList8<Vector2>> SlopePolygons, List<ExitMarker> Markers);

	public bool Open;
	public readonly List<MapRoom> Rooms = [];
	readonly HashSet<string> visitedRoomIds = [];
	readonly HashSet<(string, string)> takenExits = [];
	readonly HashSet<string> revealedDoorIds = [];
	// Per-room union of every camera view framed there, in room-local tile coords: the map only
	// shows what the player has actually seen of a room.
	readonly Dictionary<string, RectInt> seenTiles = [];

	public void NoteRoomChange(Room? from, Room to, RoomChangeKind kind)
	{
		visitedRoomIds.Add(to.Id);
		if (from != null && kind is RoomChangeKind.EdgeTransition or RoomChangeKind.LinkedDoor)
			takenExits.Add(ExitKey(from.Id, to.Id));
	}

	public void NoteDoorRevealed(string doorId)
	{
		if (!string.IsNullOrWhiteSpace(doorId))
			revealedDoorIds.Add(doorId);
	}

	public void NoteCameraView(Room room, RectInt worldTiles)
	{
		var local = new RectInt(worldTiles.Position - room.TileSpacePosition, worldTiles.Size)
			.GetIntersection(new RectInt(room.Size));
		if (local.Width <= 0 || local.Height <= 0)
			return;
		seenTiles[room.Id] = seenTiles.TryGetValue(room.Id, out var seen) ? seen.Conflate(local) : local;
	}

	static (string, string) ExitKey(string a, string b)
		=> string.CompareOrdinal(a, b) <= 0 ? (a, b) : (b, a);

	public void Rebuild(Room currentRoom)
	{
		Rooms.Clear();
		foreach (var id in visitedRoomIds)
		{
			var room = currentRoom.Id == id ? currentRoom : Assets.RoomsById.GetValueOrDefault(id);
			if (room != null)
				Rooms.Add(BuildRoom(room));
		}
	}

	MapRoom BuildRoom(Room room)
	{
		var size = room.Size;
		var seen = seenTiles.TryGetValue(room.Id, out var seenRect) ? seenRect : new RectInt(size);
		var solid = new bool[size.X * size.Y];
		var hidden = new bool[size.X * size.Y];
		if (room.SolidTiles.Length == solid.Length)
			room.SolidTiles.CopyTo(solid, 0);
		HiddenCoverGrid.ForEachUnrevealedHiddenTile(room, revealedDoorIds.Contains, tile =>
		{
			solid[tile.Y * size.X + tile.X] = true;
			hidden[tile.Y * size.X + tile.X] = true;
		});

		// Slopes count as solid for exit/frontier logic but draw as their actual polygons —
		// unless still hidden under an unrevealed cover, where a square must not leak the shape.
		var slopePolygons = new List<StackList8<Vector2>>();
		var drawnAsSlope = new bool[size.X * size.Y];
		foreach (var slope in room.SlopeTiles)
		{
			var index = slope.Tile.Y * size.X + slope.Tile.X;
			var wasHidden = hidden[index];
			solid[index] = true;
			if (wasHidden || !seen.Contains(slope.Tile))
				continue;

			drawnAsSlope[index] = true;
			var polygon = new StackList8<Vector2>();
			foreach (var point in room.GetSlopePolygon(slope))
				polygon.Add((Vector2)point / Game.TileSize - room.TileSpacePosition);
			slopePolygons.Add(polygon);
		}

		var runs = new List<RectInt>();
		for (var y = seen.Top; y < seen.Bottom; y++)
			for (var x = seen.Left; x < seen.Right; x++)
			{
				if (!solid[y * size.X + x] || drawnAsSlope[y * size.X + x])
					continue;
				var start = x;
				while (x + 1 < seen.Right && solid[y * size.X + x + 1] && !drawnAsSlope[y * size.X + x + 1])
					x++;
				runs.Add(new RectInt(start, y, x - start + 1, 1));
			}

		var markers = new List<ExitMarker>();
		CollectEdgeExits(room, solid, seen, Point2.Up, markers);
		CollectEdgeExits(room, solid, seen, Point2.Down, markers);
		CollectEdgeExits(room, solid, seen, Point2.Left, markers);
		CollectEdgeExits(room, solid, seen, Point2.Right, markers);
		CollectLinkedDoorExits(room, seen, markers);
		CollectFrontiers(room, solid, seen, markers);

		return new MapRoom(new RectInt(room.TileSpacePosition, size), seen, runs, slopePolygons, markers);
	}

	// Edges of the seen area that cut through the room with open space beyond them: the camera
	// hasn't panned there yet, so they pulse like exits.
	void CollectFrontiers(Room room, bool[] solid, RectInt seen, List<ExitMarker> markers)
	{
		var size = room.Size;
		if (seen.Left > 0 && AnyOpen(solid, size, new RectInt(0, seen.Top, seen.Left, seen.Height)))
			markers.Add(new(room.TileSpacePosition + seen.TopLeft, Point2.Left, seen.Height));
		if (seen.Right < size.X && AnyOpen(solid, size, new RectInt(seen.Right, seen.Top, size.X - seen.Right, seen.Height)))
			markers.Add(new(room.TileSpacePosition + new Point2(seen.Right - 1, seen.Top), Point2.Right, seen.Height));
		if (seen.Top > 0 && AnyOpen(solid, size, new RectInt(seen.Left, 0, seen.Width, seen.Top)))
			markers.Add(new(room.TileSpacePosition + seen.TopLeft, Point2.Up, seen.Width));
		if (seen.Bottom < size.Y && AnyOpen(solid, size, new RectInt(seen.Left, seen.Bottom, seen.Width, size.Y - seen.Bottom)))
			markers.Add(new(room.TileSpacePosition + new Point2(seen.Left, seen.Bottom - 1), Point2.Down, seen.Width));
	}

	static bool AnyOpen(bool[] solid, Point2 size, RectInt region)
	{
		for (var y = region.Top; y < region.Bottom; y++)
			for (var x = region.Left; x < region.Right; x++)
				if (!solid[y * size.X + x])
					return true;
		return false;
	}

	// Walks one border edge grouping contiguous open tiles that face the same unvisited room
	// into a single marker run. Openings the camera hasn't seen yet don't count.
	void CollectEdgeExits(Room room, bool[] solid, RectInt seen, Point2 direction, List<ExitMarker> exits)
	{
		var size = room.Size;
		var along = direction.Y != 0 ? Point2.Right : Point2.Down;
		var length = direction.Y != 0 ? size.X : size.Y;
		var first = new Point2(direction.X > 0 ? size.X - 1 : 0, direction.Y > 0 ? size.Y - 1 : 0);

		Room? runRoom = null;
		var runStart = 0;
		for (var i = 0; i <= length; i++)
		{
			Room? adjacent = null;
			if (i < length)
			{
				var tile = first + along * i;
				if (!solid[tile.Y * size.X + tile.X] && seen.Contains(tile))
				{
					var outside = room.TileSpacePosition + tile + direction;
					if (RoomAtWorldTile(outside) is { } neighbor &&
					    !visitedRoomIds.Contains(neighbor.Id) &&
					    !IsWorldTileSolid(neighbor, outside))
						adjacent = neighbor;
				}
			}

			if (adjacent == runRoom)
				continue;
			if (runRoom != null)
				exits.Add(new(room.TileSpacePosition + first + along * runStart, direction, i - runStart));
			runRoom = adjacent;
			runStart = i;
		}
	}

	void CollectLinkedDoorExits(Room room, RectInt seen, List<ExitMarker> exits)
	{
		foreach (var actor in room.Actors)
		{
			var resolved = ActorSpecs.Resolve(actor);
			if (ActorSpecs.ReadString(resolved, "Type") != "LinkedDoor")
				continue;
			var targetId = ActorSpecs.ReadString(resolved, "TargetActorId");
			if (string.IsNullOrWhiteSpace(targetId))
				continue;
			if (Assets.FindActorById(targetId) is not { } target || visitedRoomIds.Contains(target.Room.Id))
				continue;

			var doorTile = (ActorSpecs.ReadPoint(resolved, "Position") ?? Point2.Zero) / Game.TileSize;
			if (seen.Contains(doorTile))
				exits.Add(new(room.TileSpacePosition + doorTile, Point2.Zero, 1));
		}
	}

	static Room? RoomAtWorldTile(Point2 worldTile)
	{
		foreach (var room in Assets.RoomsById.Values)
			if (new RectInt(room.TileSpacePosition, room.Size).Contains(worldTile))
				return room;
		return null;
	}

	static bool IsWorldTileSolid(Room room, Point2 worldTile)
	{
		var local = worldTile - room.TileSpacePosition;
		if (local.X < 0 || local.Y < 0 || local.X >= room.Size.X || local.Y >= room.Size.Y)
			return true;
		return room.SolidTiles.Length == room.Size.X * room.Size.Y && room.SolidTiles[local.Y * room.Size.X + local.X];
	}
}

public sealed partial class GameView
{
	static float MinimapTileScale => 0.5f;       // screen pixels per world tile
	static float MinimapPlayerWidth => 4f;     // mini player sprite width in screen pixels
	static float MinimapDim => 0.8f;          // how dark the game dims behind the map
	static Color MinimapRoomFill => new(0x241a3e);
	static Color MinimapWall => new(0x8b81b5);

	void DrawMinimap(IDrawableTarget target, in RectInt viewport, RectInt? scissor)
	{
		var scale = viewport.Height / (float)Height;
		Batcher.PushMatrix(
			Matrix3x2.CreateScale(scale) *
			Matrix3x2.CreateTranslation(viewport.Left, viewport.Top));
		Batcher.Rect(new Rect(0, 0, Width, Height), Color.Black * MinimapDim);

		var s = MinimapTileScale;
		var player = game.GetFirst<Player>();
		var focus = (Vector2)(player?.Position ?? game.CurrentRoom?.WorldBounds.Center ?? Point2.Zero) / TileSize;
		Vector2 origin = (new Vector2(Width, Height) / 2 - focus * s).FloorToPoint2();

		// Only the camera-seen part of each room is drawn; the rest stays dark until explored.
		foreach (var mapRoom in game.Minimap.Rooms)
			Batcher.Rect(new Rect(origin + (Vector2)(mapRoom.TileBounds.Position + mapRoom.SeenRect.Position) * s, (Vector2)mapRoom.SeenRect.Size * s), MinimapRoomFill);

		foreach (var mapRoom in game.Minimap.Rooms)
		{
			var basePosition = origin + (Vector2)mapRoom.TileBounds.Position * s;
			foreach (var run in mapRoom.SolidRuns)
				Batcher.Rect(new Rect(basePosition + (Vector2)run.Position * s, (Vector2)run.Size * s), MinimapWall);
			foreach (var polygon in mapRoom.SlopePolygons)
				for (var i = 1; i + 1 < polygon.Count; i++)
					Batcher.Triangle(basePosition + polygon[0] * s, basePosition + polygon[i] * s, basePosition + polygon[i + 1] * s, MinimapWall);
		}

		// More-to-explore ticks pulse gently: a thin line outside a border opening or along an
		// unseen camera frontier, or a dot on a linked door whose destination hasn't been seen.
		var pulse = Color.White * (0.25f + 0.15f * MathF.Sin(Time.SecondsF * 4f));
		foreach (var mapRoom in game.Minimap.Rooms)
			foreach (var exit in mapRoom.Markers)
			{
				var p = origin + (Vector2)exit.Tile * s;
				var rect = exit.Direction == Point2.Zero
					? new Rect(p.X + s / 2 - 1, p.Y + s / 2 - 1, 2, 2)
					: exit.Direction.Y < 0 ? new Rect(p.X, p.Y - 1, exit.Length * s, 1)
					: exit.Direction.Y > 0 ? new Rect(p.X, p.Y + s, exit.Length * s, 1)
					: exit.Direction.X < 0 ? new Rect(p.X - 1, p.Y, 1, exit.Length * s)
					: new Rect(p.X + s, p.Y, 1, exit.Length * s);
				Batcher.Rect(rect, pulse);
			}

		// You-are-here: the player's sprite, miniaturized and blinking (visible longer than hidden).
		if (player is { Sprite: { Frames.Count: > 0 } sprite } && Time.BetweenInterval(0.2, 0.6, 0))
		{
			var frame = sprite.GetFrameAt(player.Animation, player.AnimationTime, true);
			var miniScale = MinimapPlayerWidth / Math.Max(1, frame.Subtexture.Width);
			Batcher.Image(frame.Subtexture, origin + focus * s, player.Origin,
				new Vector2((int)player.Facing, 1) * miniScale, 0, Color.White);
		}

		Batcher.PopMatrix();
		Batcher.Render(target, scissor: scissor?.GetIntersection(viewport) ?? viewport);
		Batcher.Clear();
	}
}
