using System.Numerics;
using Foster.Framework;
using ImGuiNET;

namespace TinyLink;

public partial class Manager
{
	bool editorGameDirty;
	bool editorViewInitialized;
	Vector2 editorCamera;
	float editorZoom = 1.0f;

	/// <summary>
	/// The current Type we're painting in the Editor
	/// </summary>
	private char brush = '0';

	/// <summary>
	/// If it's a big Brush
	/// </summary>
	private bool brushBig = false;

	Point2 tilePlaceFrom;
	char tileTypePlacing;
	bool tilePlacing;
	RectInt? proposedRoomTiles;

	private void FocusEditorOnCurrentRoom()
	{
		if (CurrentRoom is not { } room)
			return;

		var bounds = room.WorldBounds;
		var view = GameViewRect;
		editorCamera = RectCenter(bounds);
		editorZoom = Math.Clamp(Math.Min(view.Width / bounds.Width, view.Height / bounds.Height), 0.15f, 8.0f);
		editorViewInitialized = true;
	}

	private void MatchEditorCameraToGame()
	{
		if (game == null)
			return;

		editorCamera = game.Camera + new Vector2(Game.Width, Game.Height) / 2;
		editorZoom = Math.Clamp(GameViewRect.Width / Game.Width, 0.15f, 8.0f);
		editorViewInitialized = true;
	}

	private void UpdateEditorNavigation()
	{
		var direction = Point2.Zero;
		if (controls.Move.PressedUp)
			direction = Point2.Up;
		if (controls.Move.PressedDown)
			direction = Point2.Down;
		if (controls.Move.PressedLeft)
			direction = Point2.Left;
		if (controls.Move.PressedRight)
			direction = Point2.Right;

		if (direction != Point2.Zero)
		{
			ShiftRoom(direction);
			controls.Move.Clear();
		}
	}

	private void RenderEditorActiveRoom()
	{
		if (game == null || CurrentRoom is not { } room)
			return;

		var viewport = EditorWorldRectToScreen(room.WorldBounds);
		var clip = (RectInt)(GameViewRect * Scale);
		if (!viewport.Overlaps(clip))
			return;

		game.RenderRoomPreview(viewport, scissor: clip);
	}

	private RectInt EditorWorldRectToScreen(RectInt world)
	{
		var inner = GameViewRect;
		var topLeft = inner.Center + ((Vector2)world.TopLeft - editorCamera) * editorZoom;
		var size = new Vector2(world.Width, world.Height) * editorZoom;
		return (RectInt)(new Rect(topLeft, size) * Scale);
	}

	private void PaletteImGui()
	{
		var rect = PaletteSlideRect;
		var flags =
			ImGuiWindowFlags.NoTitleBar |
			ImGuiWindowFlags.NoMove |
			ImGuiWindowFlags.NoResize |
			ImGuiWindowFlags.NoCollapse |
			ImGuiWindowFlags.NoSavedSettings |
			ImGuiWindowFlags.NoBringToFrontOnFocus;

		ImGui.SetNextWindowPos(rect.TopLeft, ImGuiCond.Always);
		ImGui.SetNextWindowSize(rect.Size, ImGuiCond.Always);

		ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(BoxPadding, BoxPadding));
		ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(ButtonSpacing, ButtonSpacing));
		ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, Vector2.Zero);
		ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, LineWeight);
		ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.0f, 0.0f, 0.0f, 1.0f));
		ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(1.0f, 1.0f, 1.0f, 1.0f));

		if (ImGui.Begin("Palette", flags))
		{
			var index = 0;
			foreach (var (id, entry) in Factory.Entries)
			{
				if (index > 0 && index % PaletteColumns != 0)
					ImGui.SameLine();

				if (ImGuiSubtextureButton($"Palette_{id}", entry.Image, entry.Name, brush == id, new Vector2(ButtonSize, ButtonSize)))
					brush = id;

				index++;
			}
		}
		ImGui.End();

		ImGui.PopStyleColor(2);
		ImGui.PopStyleVar(4);
	}

	private void UpdateEditorWorldView(Rect inner)
	{
		if (!inner.Contains(Cursor))
			return;

		if (Input.Mouse.MiddleDown)
			editorCamera -= Input.Mouse.Delta / Scale / editorZoom;

		var wheel = Input.Mouse.Wheel.Y;
		if (wheel == 0)
			return;

		var before = ScreenToEditorWorld(Cursor, inner);
		var zoomFactor = MathF.Pow(1.15f, wheel);
		editorZoom = Math.Clamp(editorZoom * zoomFactor, 0.15f, 8.0f);
		editorCamera = before - (Cursor - inner.Center) / editorZoom;
	}

	private Vector2 ScreenToEditorWorld(Vector2 screen, Rect inner)
		=> editorCamera + (screen - inner.Center) / editorZoom;

	private void DrawEditorRooms()
	{
		foreach (var room in Assets.RoomsById.Values)
		{
			var bounds = room.WorldBounds;
			var selected = CurrentRoom == room;
			var line = MathF.Max(1.0f / editorZoom, LineWeight / editorZoom);
			if (selected)
			{
				batcher.RectLine(bounds, line * 2, highlight);
				continue;
			}

			batcher.Rect(bounds, 0x150e22);

			for (var x = 0; x < room.Size.X; x ++)
			for (var y = 0; y < room.Size.Y; y ++)
			{
				if (Factory.Find(room.Tiles[x, y]) is not { } entry || entry.Image.Texture == null)
					continue;

				var position = (Vector2)bounds.TopLeft + new Vector2(x, y) * Game.TileSize + entry.Offset;
				if (entry.Tile != null)
					batcher.Image(entry.Image, position, entry.Origin, Vector2.One, 0, Color.White);
			}

			for (var x = 0; x < room.Size.X; x ++)
			for (var y = 0; y < room.Size.Y; y ++)
			{
				if (Factory.Find(room.Tiles[x, y]) is not { } entry || entry.Image.Texture == null)
					continue;

				var position = (Vector2)bounds.TopLeft + new Vector2(x, y) * Game.TileSize + entry.Offset;
				if (entry.Spawn != null)
					batcher.Image(entry.Image, position, entry.Origin, Vector2.One, 0, Color.White);
			}

			batcher.RectLine(bounds, line, Color.Gray);
		}
	}

	private Room? GetRoomAtWorld(Vector2 world)
	{
		foreach (var room in Assets.RoomsById.Values)
			if (room.WorldBounds.Contains(world))
				return room;

		return null;
	}

	private void DrawEditorEyedropperCursor(Rect tileRect, float lineWeight)
	{
		var width = lineWeight * 2;
		batcher.RectLine(tileRect, width, highlight);

		var start = tileRect.TopLeft + tileRect.Size * 0.25f;
		var end = tileRect.BottomRight - tileRect.Size * 0.20f;
		batcher.Line(start, end, width, Color.White);
		batcher.Rect(new Rect(start - Vector2.One * width, Vector2.One * width * 2), highlight);
		batcher.Rect(new Rect(end - Vector2.One * width * 0.75f, Vector2.One * width * 1.5f), Color.White);
	}

	private static Vector2 RectCenter(RectInt rect)
		=> new(rect.X + rect.Width * 0.5f, rect.Y + rect.Height * 0.5f);

	private void Editor()
	{
		var bounds = WorkspaceRect;
		var inner = GameViewRect;
		var arrow = new Rect().Inflate(ButtonSize / 2);
		var lastCell = cell;
		if (!editorViewInitialized)
			FocusEditorOnCurrentRoom();

		UpdateEditorWorldView(inner);

		batcher.PushScissor((RectInt)(inner * Scale));
		var worldMatrix =
			Matrix3x2.CreateTranslation(-editorCamera) *
			Matrix3x2.CreateScale(editorZoom) *
			Matrix3x2.CreateTranslation(inner.Center);
		batcher.PushMatrix(worldMatrix);
		DrawEditorRooms();

		var eyedropper = Input.Keyboard.Alt;

		if (inner.Contains(Cursor) && Input.Mouse.LeftPressed && !eyedropper)
		{
			var worldMouse = ScreenToEditorWorld(Cursor, inner);
			if (GetRoomAtWorld(worldMouse) is { } selected && selected != CurrentRoom)
			{
				CurrentRoom?.Save();
				SelectRoom(selected);
				SyncEditorGame();
			}
		}

		if (CurrentRoom is {} editing)
		{
			var gridColor = Color.White * 0.10f;
			var gridWeight = 1.0f / editorZoom;
			var roomBounds = editing.WorldBounds;
			var roomTopLeft = (Vector2)roomBounds.TopLeft;
			var width = roomBounds.Width;
			var height = roomBounds.Height;

			var localMouse = Vector2.Zero;
			var worldMouse = Vector2.Zero;
			var tileOver = Point2.Zero;
			if (Matrix3x2.Invert(worldMatrix, out var inverse))
			{
				worldMouse = Vector2.Transform(Cursor, inverse);
				localMouse = worldMouse - roomTopLeft;
				tileOver = (Point2)(localMouse / Game.TileSize);
			}
			var overGame = new Rect(0, 0, width, height).Contains(localMouse);
			proposedRoomTiles = overGame ? null : GetProposedRoomTiles(editing, worldMouse);

			for (int x = 1; x < editing.Size.X; x ++)
				batcher.Line(roomTopLeft + new Vector2(x * Game.TileSize, 0),  roomTopLeft + new Vector2(x * Game.TileSize, height), gridWeight, gridColor);
			for (int y = 1; y < editing.Size.Y; y ++)
				batcher.Line(roomTopLeft + new Vector2(0, y * Game.TileSize),  roomTopLeft + new Vector2(width, y * Game.TileSize), gridWeight, gridColor);

			if (eyedropper)
				tilePlacing = false;

			if (eyedropper && overGame && Input.Mouse.LeftPressed)
			{
				brush = editing.Tiles[tileOver.X, tileOver.Y];
				tileTypePlacing = brush;
			}
			else if (lastCell == cell && overGame && (Input.Mouse.LeftPressed || Input.Mouse.RightPressed))
			{
				tilePlaceFrom = tileOver;
				tileTypePlacing = Input.Mouse.LeftPressed ? brush : '0';
				tilePlacing = true;
			}

			if (!Input.Mouse.RightDown)
				tileTypePlacing = brush;

			var shift = brushBig ? -1 : 0;
			var size = brushBig ? 3 : 1;

			if (overGame)
			{
				var cursorRect = ((new Rect(tileOver.X + shift, tileOver.Y + shift, size, size) * Game.TileSize) + roomTopLeft).Inflate(gridWeight * 2);
				if (eyedropper)
				{
					var sampleRect = ((new Rect(tileOver.X, tileOver.Y, 1, 1) * Game.TileSize) + roomTopLeft).Inflate(gridWeight * 2);
					DrawEditorEyedropperCursor(sampleRect, gridWeight);
				}
				else
				{
					var cursorColor = tileTypePlacing == '0' ? Color.Red : highlight;
					batcher.RectLine(cursorRect, gridWeight * 2, cursorColor);
				}

				if (!eyedropper && Factory.Find(brush) is {} placing)
				{
					var position = roomTopLeft + tileOver * Game.TileSize + placing.Offset;

					for (int x = shift; x < shift + size; x ++)
						for (int y = shift; y < shift + size; y ++)
							batcher.Image(placing.Image, position + new Point2(x, y) * Game.TileSize, placing.Origin, Vector2.One, 0, Color.White);
				}
			}

			if (!eyedropper && tilePlacing && (Input.Mouse.LeftDown || Input.Mouse.RightDown))
			{
				editing.Set(tilePlaceFrom, tileTypePlacing);
				editorGameDirty = true;

				while (tilePlaceFrom != tileOver)
				{
					var diff = tileOver - tilePlaceFrom;
					if (Math.Abs(diff.X) > Math.Abs(diff.Y))
						diff.Y = 0;
					else
						diff.X = 0;
					tilePlaceFrom.X += Math.Sign(diff.X);
					tilePlaceFrom.Y += Math.Sign(diff.Y);

					for (int x = shift; x < shift + size; x ++)
						for (int y = shift; y < shift + size; y ++)
						{
							editing.Set(tilePlaceFrom + new Point2(x, y), tileTypePlacing);
							editorGameDirty = true;
						}
				}
			}

			batcher.RectLine(roomBounds, gridWeight * 2, Color.Black);

			if (proposedRoomTiles is { } proposal)
			{
				var proposalBounds = TileRectToWorldRect(proposal);
				batcher.Rect(proposalBounds, Color.Gray * 0.35f);
				batcher.RectLine(proposalBounds, gridWeight * 2, Color.Gray);

				if (inner.Contains(Cursor) && Input.Mouse.LeftPressed && !eyedropper)
				{
					CreateRoom(proposal.Position, proposal.Size);
					proposedRoomTiles = null;
				}
			}
		}

		batcher.PopMatrix();
		batcher.PopScissor();

		if (!Input.Mouse.LeftDown && !Input.Mouse.RightDown)
		{
			tileTypePlacing = brush;
			tilePlacing = false;
		}

		if (editorGameDirty)
		{
			editorGameDirty = false;
			CurrentRoom?.Save();
			SyncEditorGame();
		}
	}

	private bool HasAdjacentEditorRoom(Point2 direction)
	{
		if (CurrentRoom is not { } room)
			return Assets.GetRoom(cell + direction) != null;

		return Assets.FindAdjacentRoom(room, direction, EditorExitPosition(room, direction)) != null;
	}

	private RectInt? GetProposedRoomTiles(Room source, Vector2 worldMouse)
	{
		var sourceTiles = new RectInt(source.Position, source.Size);
		var mouseTile = new Point2(
			(int)MathF.Floor(worldMouse.X / Game.TileSize),
			(int)MathF.Floor(worldMouse.Y / Game.TileSize));

		if (mouseTile.X >= sourceTiles.Right &&
		    mouseTile.X < sourceTiles.Right + Room.DefaultSize.X &&
		    mouseTile.Y >= sourceTiles.Top &&
		    mouseTile.Y < sourceTiles.Bottom)
			return FitProposedRoom(sourceTiles, Point2.Right, mouseTile.Y);

		if (mouseTile.X < sourceTiles.Left &&
		    mouseTile.X >= sourceTiles.Left - Room.DefaultSize.X &&
		    mouseTile.Y >= sourceTiles.Top &&
		    mouseTile.Y < sourceTiles.Bottom)
			return FitProposedRoom(sourceTiles, Point2.Left, mouseTile.Y);

		if (mouseTile.Y >= sourceTiles.Bottom &&
		    mouseTile.Y < sourceTiles.Bottom + Room.DefaultSize.Y &&
		    mouseTile.X >= sourceTiles.Left &&
		    mouseTile.X < sourceTiles.Right)
			return FitProposedRoom(sourceTiles, Point2.Down, mouseTile.X);

		if (mouseTile.Y < sourceTiles.Top &&
		    mouseTile.Y >= sourceTiles.Top - Room.DefaultSize.Y &&
		    mouseTile.X >= sourceTiles.Left &&
		    mouseTile.X < sourceTiles.Right)
			return FitProposedRoom(sourceTiles, Point2.Up, mouseTile.X);

		return null;
	}

	private RectInt? FitProposedRoom(RectInt source, Point2 direction, int mouseAxis)
	{
		var vertical = direction.X != 0;
		var depth = vertical ? Room.DefaultSize.X : Room.DefaultSize.Y;
		var start = vertical ? source.Top : source.Left;
		var end = vertical ? source.Bottom : source.Right;
		var blocked = new bool[end - start];

		var candidate = direction switch
		{
			{ X: > 0 } => new RectInt(source.Right, source.Top, depth, source.Height),
			{ X: < 0 } => new RectInt(source.Left - depth, source.Top, depth, source.Height),
			{ Y: > 0 } => new RectInt(source.Left, source.Bottom, source.Width, depth),
			{ Y: < 0 } => new RectInt(source.Left, source.Top - depth, source.Width, depth),
			_ => new RectInt()
		};

		foreach (var room in Assets.RoomsById.Values)
		{
			var roomTiles = new RectInt(room.Position, room.Size);
			if (!candidate.Overlaps(roomTiles))
				continue;

			var blockStart = Math.Max(start, vertical ? roomTiles.Top : roomTiles.Left);
			var blockEnd = Math.Min(end, vertical ? roomTiles.Bottom : roomTiles.Right);
			for (var i = blockStart; i < blockEnd; i ++)
				blocked[i - start] = true;
		}

		var index = mouseAxis - start;
		if (index < 0 || index >= blocked.Length || blocked[index])
			return null;

		var freeStart = index;
		while (freeStart > 0 && !blocked[freeStart - 1])
			freeStart --;

		var freeEnd = index + 1;
		while (freeEnd < blocked.Length && !blocked[freeEnd])
			freeEnd ++;

		var axisStart = start + freeStart;
		var axisLength = freeEnd - freeStart;
		if (axisLength <= 0)
			return null;

		return direction switch
		{
			{ X: > 0 } => new RectInt(source.Right, axisStart, depth, axisLength),
			{ X: < 0 } => new RectInt(source.Left - depth, axisStart, depth, axisLength),
			{ Y: > 0 } => new RectInt(axisStart, source.Bottom, axisLength, depth),
			{ Y: < 0 } => new RectInt(axisStart, source.Top - depth, axisLength, depth),
			_ => null
		};
	}

	private static Rect TileRectToWorldRect(RectInt tiles)
		=> new(tiles.X * Game.TileSize, tiles.Y * Game.TileSize, tiles.Width * Game.TileSize, tiles.Height * Game.TileSize);

	private void CreateRoom(Point2 position, Point2 size)
	{
		CurrentRoom?.Save();

		var it = new Room(position, size);
		Assets.Rooms[it.Cell] = it;
		Assets.RoomsById[it.Id] = it;
		SelectRoom(it);
		it.Save();
		SyncEditorGame();
		FocusEditorOnCurrentRoom();
	}
}
