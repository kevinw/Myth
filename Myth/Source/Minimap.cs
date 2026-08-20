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

	/// A terminal edge of the visible map, softened inward so room-color crops don't read as
	/// hard screenshot boundaries. Tile is world tile space; Direction is the outward normal.
	public readonly record struct EdgeShade(Point2 Tile, Point2 Direction, int Length);

	public readonly record struct MapRoom(RectInt TileBounds, RectInt SeenRect, List<RectInt> SolidRuns, List<StackList8<Vector2>> SlopePolygons, List<ExitMarker> Markers, List<EdgeShade> EdgeShades);

	public bool Open;
	public Vector2 Pan;
	public readonly List<MapRoom> Rooms = [];
	public Action? Changed;

	// A warp destination resolved for the current warp session: the save point's actor
	// id plus its live world-pixel position (used to draw the marker and warp there).
	public readonly record struct WarpTarget(string ActorId, string RoomId, Point2 Position);

	// Warp mode: opened from a save point, shows every activated save point as a
	// selectable, blinking target to travel between. See Game.OpenSavePointWarp.
	public bool WarpMode;
	public int WarpSelection;
	public readonly List<WarpTarget> WarpPoints = [];
	public string? WarpOriginActorId;

	// World-pixel point the warp map is centering on; lerps toward the selected target
	// so changing selection glides the map over (see GameView.DrawMinimap).
	public Vector2 WarpFocus;
	public Vector2 WarpMarkerPosition;

	public WarpTarget? SelectedWarp
		=> WarpMode && WarpSelection >= 0 && WarpSelection < WarpPoints.Count ? WarpPoints[WarpSelection] : null;

	public void BeginWarp(IEnumerable<WarpTarget> targets, string? currentActorId)
	{
		WarpPoints.Clear();
		WarpPoints.AddRange(targets);
		WarpSelection = Math.Max(0, WarpPoints.FindIndex(t => t.ActorId == currentActorId));
		WarpMode = true;
		Open = true;
		WarpOriginActorId = currentActorId;
		WarpFocus = WarpPoints.Count > 0 ? (Vector2)WarpPoints[WarpSelection].Position : Vector2.Zero;
		WarpMarkerPosition = WarpFocus;
	}

	public void CloseWarp()
	{
		WarpMode = false;
		Open = false;
		WarpOriginActorId = null;
	}

	// Moves the warp selection to the nearest other save point in the pressed
	// direction (screen/world space). Returns true if the selection changed.
	public bool MoveWarpSelection(Point2 dir)
	{
		if (WarpPoints.Count == 0 || (dir.X == 0 && dir.Y == 0))
			return false;

		var from = (Vector2)WarpPoints[WarpSelection].Position;
		var d = new Vector2(dir.X, dir.Y);
		var best = -1;
		var bestScore = float.MaxValue;
		for (var i = 0; i < WarpPoints.Count; i++)
		{
			if (i == WarpSelection)
				continue;
			var delta = (Vector2)WarpPoints[i].Position - from;
			var along = Vector2.Dot(delta, d);
			if (along <= 0)
				continue; // only points that lie in the pressed direction
			var perp = (delta - along * d).Length();
			var score = along + perp * 2f; // prefer aligned and close
			if (score < bestScore)
			{
				bestScore = score;
				best = i;
			}
		}
		if (best < 0)
			return false;

		WarpSelection = best;
		return true;
	}
	readonly HashSet<string> visitedRoomIds = [];
	readonly HashSet<(string, string)> takenExits = [];
	readonly HashSet<string> revealedDoorIds = [];
	// Per-room union of every camera view framed there, in room-local tile coords: the map only
	// shows what the player has actually seen of a room.
	readonly Dictionary<string, RectInt> seenTiles = [];

	public void NoteRoomChange(Room? from, Room to, RoomChangeKind kind)
	{
		var changed = visitedRoomIds.Add(to.Id);
		if (from != null && kind is RoomChangeKind.EdgeTransition or RoomChangeKind.LinkedDoor)
			changed |= takenExits.Add(ExitKey(from.Id, to.Id));
		if (changed)
			Changed?.Invoke();
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
		var next = seenTiles.TryGetValue(room.Id, out var seen) ? seen.Conflate(local) : local;
		if (seenTiles.TryGetValue(room.Id, out seen) && seen == next)
			return;
		seenTiles[room.Id] = next;
		Changed?.Invoke();
	}

	static (string, string) ExitKey(string a, string b)
		=> string.CompareOrdinal(a, b) <= 0 ? (a, b) : (b, a);

	public void ResetView()
		=> Pan = Vector2.Zero;

	public void UpdateView(Vector2 move, float dt)
	{
		if (move.LengthSquared() > 1)
			move = Vector2.Normalize(move);
		Pan -= move * MinimapPanSpeed * dt;
	}

	static float MinimapPanSpeed => 92f;

	public MinimapState CaptureState()
	{
		var state = new MinimapState();
		state.VisitedRoomIds.AddRange(visitedRoomIds);
		state.VisitedRoomIds.Sort(StringComparer.Ordinal);
		foreach (var (a, b) in takenExits)
			state.TakenExits.Add(new(a, b));
		state.TakenExits.Sort((a, b) =>
		{
			var cmp = string.CompareOrdinal(a.A, b.A);
			return cmp != 0 ? cmp : string.CompareOrdinal(a.B, b.B);
		});
		foreach (var (id, rect) in seenTiles)
			state.SeenTiles[id] = rect;
		return state;
	}

	public void ApplyState(MinimapState state)
	{
		visitedRoomIds.Clear();
		takenExits.Clear();
		revealedDoorIds.Clear();
		seenTiles.Clear();
		Rooms.Clear();

		foreach (var id in state.VisitedRoomIds)
			if (!string.IsNullOrWhiteSpace(id))
				visitedRoomIds.Add(id);
		foreach (var exit in state.TakenExits)
			if (!string.IsNullOrWhiteSpace(exit.A) && !string.IsNullOrWhiteSpace(exit.B))
				takenExits.Add(ExitKey(exit.A, exit.B));
		foreach (var (id, rect) in state.SeenTiles)
			if (!string.IsNullOrWhiteSpace(id) && rect.Width > 0 && rect.Height > 0)
				seenTiles[id] = rect;
	}

	public void ApplyRevealedDoors(IEnumerable<string> doorIds)
	{
		revealedDoorIds.Clear();
		foreach (var id in doorIds)
			if (!string.IsNullOrWhiteSpace(id))
				revealedDoorIds.Add(id);
	}

	public void Rebuild(Room currentRoom, Game? currentGame = null)
	{
		Rooms.Clear();
		foreach (var id in visitedRoomIds)
		{
			var room = currentRoom.Id == id ? currentRoom : Assets.RoomsById.GetValueOrDefault(id);
			if (room != null && room.Sublayer == currentRoom.Sublayer)
				Rooms.Add(BuildRoom(room, room == currentRoom ? currentGame : null));
		}
	}

	MapRoom BuildRoom(Room room, Game? currentGame)
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
		ApplyCameraBlockers(room, currentGame, solid, hidden);

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

		var edgeShades = new List<EdgeShade>();
		CollectEdgeShades(room, solid, seen, Point2.Up, edgeShades);
		CollectEdgeShades(room, solid, seen, Point2.Down, edgeShades);
		CollectEdgeShades(room, solid, seen, Point2.Left, edgeShades);
		CollectEdgeShades(room, solid, seen, Point2.Right, edgeShades);

		return new MapRoom(new RectInt(room.TileSpacePosition, size), seen, runs, slopePolygons, markers, edgeShades);
	}

	static void ApplyCameraBlockers(Room room, Game? currentGame, bool[] solid, bool[] hidden)
	{
		if (currentGame != null)
		{
			foreach (var actor in currentGame.Actors)
				if (actor is CameraBlocker { Active: true, HighlightOnMap: false } blocker && blocker.Room == room)
					ApplyCameraBlocker(room, blocker.WorldRect, solid, hidden);
			return;
		}

		foreach (var actor in room.Actors)
		{
			if (ActorSpecs.ReadResolvedString(actor, "Type") != nameof(CameraBlocker) ||
			    ActorSpecs.GetResolvedProperty(actor, nameof(CameraBlocker.HighlightOnMap))?.GetValue<bool>() == true)
				continue;
			var local = ActorSpecs.ReadResolvedPoint(actor, "Position") ?? Point2.Zero;
			var size = ActorSpecs.ReadResolvedPoint(actor, "Size") ?? CameraBlocker.DefaultSize;
			ApplyCameraBlocker(room, new RectInt(room.WorldBounds.TopLeft + local, size), solid, hidden);
		}
	}

	static void ApplyCameraBlocker(Room room, RectInt worldRect, bool[] solid, bool[] hidden)
	{
		var topLeft = new Point2(FloorDiv(worldRect.Left, Game.TileSize), FloorDiv(worldRect.Top, Game.TileSize)) - room.TileSpacePosition;
		var bottomRight = new Point2(CeilDiv(worldRect.Right, Game.TileSize), CeilDiv(worldRect.Bottom, Game.TileSize)) - room.TileSpacePosition;
		var local = RectInt.Between(topLeft, bottomRight)
			.GetIntersection(new RectInt(room.Size));
		for (var y = local.Top; y < local.Bottom; y++)
			for (var x = local.Left; x < local.Right; x++)
			{
				var index = y * room.Size.X + x;
				solid[index] = true;
				hidden[index] = true;
			}
	}

	static int FloorDiv(int value, int divisor)
		=> value >= 0 ? value / divisor : -((-value + divisor - 1) / divisor);

	static int CeilDiv(int value, int divisor)
		=> value >= 0 ? (value + divisor - 1) / divisor : -((-value) / divisor);

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

	void CollectEdgeShades(Room room, bool[] solid, RectInt seen, Point2 direction, List<EdgeShade> shades)
	{
		var along = direction.Y != 0 ? Point2.Right : Point2.Down;
		var length = direction.Y != 0 ? seen.Width : seen.Height;
		var first = new Point2(direction.X > 0 ? seen.Right - 1 : seen.Left, direction.Y > 0 ? seen.Bottom - 1 : seen.Top);

		var runStart = 0;
		var inRun = false;
		for (var i = 0; i <= length; i++)
		{
			var terminal = i < length && IsTerminalEdge(room, solid, first + along * i, direction);
			if (terminal == inRun)
				continue;
			if (inRun)
				shades.Add(new(room.TileSpacePosition + first + along * runStart, direction, i - runStart));
			inRun = terminal;
			runStart = i;
		}
	}

	static bool IsTerminalEdge(Room room, bool[] solid, Point2 tile, Point2 direction)
	{
		var size = room.Size;
		var outside = tile + direction;
		if (outside.X >= 0 && outside.Y >= 0 && outside.X < size.X && outside.Y < size.Y)
			return solid[outside.Y * size.X + outside.X];

		var outsideWorld = room.TileSpacePosition + outside;
		return RoomAtWorldTile(outsideWorld, room.Sublayer) == null;
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
					if (RoomAtWorldTile(outside, room.Sublayer) is { } neighbor &&
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
				if (ActorSpecs.ReadResolvedString(actor, "Type") != "LinkedDoor")
					continue;
				var targetId = ActorSpecs.ReadResolvedString(actor, "TargetActorId");
				if (string.IsNullOrWhiteSpace(targetId))
					continue;
				if (Assets.FindActorById(targetId) is not { } target || visitedRoomIds.Contains(target.Room.Id))
					continue;

				var doorTile = (ActorSpecs.ReadResolvedPoint(actor, "Position") ?? Point2.Zero) / Game.TileSize;
				if (seen.Contains(doorTile))
					exits.Add(new(room.TileSpacePosition + doorTile, Point2.Zero, 1));
		}
	}

	static Room? RoomAtWorldTile(Point2 worldTile, int sublayer)
	{
		foreach (var room in Assets.RoomsById.Values)
			if (room.Sublayer == sublayer && new RectInt(room.TileSpacePosition, room.Size).Contains(worldTile))
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
	static float MinimapTileScale => 1f;   // game pixels per world tile
	static float MinimapDim => 0.8f;       // how dark the game dims behind the map
	static float MinimapFadeSpeed => 10f;  // fully fades in or out in 0.1s
	static float WarpFocusLerpSpeed => 9f; // how fast the map glides to a newly selected warp target
	static float WarpMarkerLerpSpeed => 24f;
	static Color MinimapRoomFill => new(0x241a3e, 50);
	static Color MinimapWall => new(0x00, 80);
	static int MinimapEdgeShadeSteps => 2;
	static float MinimapEdgeShadeAlpha => 0.36f;
	static float MinimapWorldColorOpacity => 1f; // how strongly the baked world map reads as the room fill
	const string WarpHelp = "{Move} Move    {Confirm} Warp    {Back} Back";
	const string WarpHelpNoMovement = "{Confirm} Warp    {Back} Back";

	float minimapOpacity;

	// Set by actors each Update to request a world-space contextual prompt on the bottom line
	// (drawn by DrawContextPrompt while the map overlay is down). Consumed once per frame.
	internal string? contextPrompt = null;

	// Drawn into the 320x180 output target before the final composite, so the map shares the
	// game's pixel grid (and CRT).
	void DrawMinimap(Target output)
	{
		minimapOpacity = Calc.Approach(minimapOpacity, game.MinimapOpen ? 1f : 0f, MinimapFadeSpeed * Time.Delta);
		if (minimapOpacity <= 0.001f)
			return;

		var player = game.GetFirst<Player>();
		var focusWorld = (Vector2)(game.CurrentRoom?.WorldBounds.Center ?? Point2.Zero);
		var focus = focusWorld / TileSize;
		var s = MinimapTileScale;
		var origin = new Vector2(Width, Height) / 2 - focus * s + game.Minimap.Pan;

		// In warp mode, glide the map so the selected save point sits at screen center.
		if (game.Minimap.WarpMode && game.Minimap.SelectedWarp is { } sel)
		{
			game.Minimap.WarpFocus = Vector2.Lerp(game.Minimap.WarpFocus, (Vector2)sel.Position, 1f - MathF.Exp(-Time.Delta * WarpFocusLerpSpeed));
			game.Minimap.WarpMarkerPosition = Vector2.Lerp(game.Minimap.WarpMarkerPosition, (Vector2)sel.Position, 1f - MathF.Exp(-Time.Delta * WarpMarkerLerpSpeed));
			origin += (focusWorld - game.Minimap.WarpFocus) / TileSize * s;
		}

		origin = origin.FloorToPoint2();

		Batcher.Rect(new Rect(0, 0, Width, Height), Color.Black * (MinimapDim * minimapOpacity));

		// Only the camera-seen part of each room is drawn; the rest stays dark until explored.
		// The fill is the baked world map where there is one (its colors are the rooms' own),
		// and the flat fill color otherwise.
		var worldMap = WorldMap.Available && game.CurrentRoom?.Sublayer == 0;
		if (worldMap)
			Batcher.PushSampler(new(TextureFilter.Linear, TextureWrap.Clamp, TextureWrap.Clamp));
		foreach (var mapRoom in game.Minimap.Rooms)
		{
			var seenTiles = new RectInt(mapRoom.TileBounds.Position + mapRoom.SeenRect.Position, mapRoom.SeenRect.Size);
			var fill = new Rect(origin + (Vector2)seenTiles.Position * s, (Vector2)seenTiles.Size * s);
			if (worldMap)
				Batcher.ImageStretch(
					new Subtexture(WorldMap.Texture, WorldMap.SourceRect(seenTiles * TileSize)),
					fill,
					Color.White * (MinimapWorldColorOpacity * minimapOpacity));
			else
				Batcher.Rect(fill, MinimapRoomFill * minimapOpacity);
		}
		if (worldMap)
			Batcher.PopSampler();

		foreach (var mapRoom in game.Minimap.Rooms)
		{
			var basePosition = origin + (Vector2)mapRoom.TileBounds.Position * s;
			foreach (var edge in mapRoom.EdgeShades)
				DrawMinimapEdgeShade(origin, edge, s, minimapOpacity);
			foreach (var run in mapRoom.SolidRuns)
				Batcher.Rect(new Rect(basePosition + (Vector2)run.Position * s, (Vector2)run.Size * s), MinimapWall * minimapOpacity);
			foreach (var polygon in mapRoom.SlopePolygons)
				for (var i = 1; i + 1 < polygon.Count; i++)
					Batcher.Triangle(basePosition + polygon[0] * s, basePosition + polygon[i] * s, basePosition + polygon[i + 1] * s, MinimapWall * minimapOpacity);
		}

		// More-to-explore ticks pulse gently: a thin line outside a border opening or along an
		// unseen camera frontier, or a dot on a linked door whose destination hasn't been seen.
		var pulse = Color.White * ((0.25f + 0.15f * MathF.Sin(Time.SecondsF * 4f)) * minimapOpacity);
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

		// Warp mode: every activated save point blinks; the selected one is steady and
		// ringed. Dpad moves the selection, X/South warps there (see UpdateWarpModeInput).
		if (game.Minimap.WarpMode)
		{
			var warpBlink = 0.5f + 0.5f * MathF.Sin(Time.SecondsF * 6f);
			for (var i = 0; i < game.Minimap.WarpPoints.Count; i++)
			{
				var wp = game.Minimap.WarpPoints[i];
				var selected = i == game.Minimap.WarpSelection;
				var p = origin + (selected ? game.Minimap.WarpMarkerPosition : (Vector2)wp.Position) / TileSize * s;
				var half = (selected ? 5f : 3f) / 2f;
				var alpha = selected ? 1f : 0.35f + 0.5f * warpBlink;
				Batcher.Rect(new Rect(p.X - half, p.Y - half, half * 2, half * 2), new Color(0xd23c3c) * (alpha * minimapOpacity));
				if (selected)
					Batcher.RectLine(new Rect(p.X - half - 1, p.Y - half - 1, half * 2 + 2, half * 2 + 2), 1, Color.White * minimapOpacity);
			}
		}

		if (player != null && Time.BetweenInterval(0.2, 0.6, 0))
		{
			var position = (origin + player.Position / TileSize * s).FloorToPoint2() - Point2.One;
			Batcher.Rect(new Rect(position, Point2.One * 2), Color.White * minimapOpacity);
		}

		if (game.Minimap.WarpMode)
		{
			var color = Color.White * minimapOpacity;
			var text = game.Minimap.WarpPoints.Count > 1 ? WarpHelp : WarpHelpNoMovement;
			DrawPromptText(text, color);
		}

		Batcher.Render(output);
		Batcher.Clear();
	}

	// A world-space contextual prompt (e.g. the save point's "press up to warp") that takes the
	// bottom-of-screen line while the map overlay is down. The field is consumed every frame
	// regardless of visibility, so a prompt set the instant before the map opens can't linger.
	// Glyphs are tagged mapOverlay: false so they pass the DrawInputPromptGlyphs gate.
	internal void DrawContextPrompt(Target output, bool mapOverlayVisible)
	{
		var prompt = contextPrompt;
		contextPrompt = null;
		if (mapOverlayVisible || prompt is not { } text)
			return;

		DrawPromptText(text, mapOverlay: false);
		Batcher.Render(output);
		Batcher.Clear();
	}

	internal void DrawPromptText(string text, Color? color = null, bool mapOverlay = true)
	{
		if ((Assets.SpeechFont ?? Assets.Font) is not { } font)
			return;

		Manager.InputPrompts?.DrawText(
			Batcher,
			font,
			text,
			new Vector2(Width * 0.5f, Height - 4),
			font.Size,
			new Vector2(0.5f, 1),
			color ?? Color.White,
			deferredGlyphs: InputPromptGlyphs,
			drawGlyphs: false,
			mapOverlay: mapOverlay);
	}

	void DrawMinimapEdgeShade(Vector2 origin, Minimap.EdgeShade edge, float scale, float opacity)
	{
		var p = origin + (Vector2)edge.Tile * scale;
		for (var i = 0; i < MinimapEdgeShadeSteps; i++)
		{
			var alpha = MinimapEdgeShadeAlpha * (1 - i / (float)MinimapEdgeShadeSteps) * opacity;
			var rect = edge.Direction.Y < 0 ? new Rect(p.X, p.Y + i, edge.Length * scale, 1)
				: edge.Direction.Y > 0 ? new Rect(p.X, p.Y + scale - 1 - i, edge.Length * scale, 1)
				: edge.Direction.X < 0 ? new Rect(p.X + i, p.Y, 1, edge.Length * scale)
				: new Rect(p.X + scale - 1 - i, p.Y, 1, edge.Length * scale);
			Batcher.Rect(rect, Color.Black * alpha);
		}
	}
}
