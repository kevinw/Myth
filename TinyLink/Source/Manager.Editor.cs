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

		game.Render(viewport, showHud: false, scissor: clip);
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

		if (!playing)
		{
			var upImg = Assets.GetSubtexture("buttons/6");
			var leftImg = Assets.GetSubtexture("buttons/4");
			var downImg = Assets.GetSubtexture("buttons/3");
			var rightImg = Assets.GetSubtexture("buttons/5");

			var upExits = HasAdjacentEditorRoom(Point2.Up);
			var leftExits = HasAdjacentEditorRoom(Point2.Left);
			var downExits = HasAdjacentEditorRoom(Point2.Down);
			var rightExits = HasAdjacentEditorRoom(Point2.Right);

			batcher.PushLayer(-1);
			if (Button(arrow + bounds.TopCenter, upImg, "Move Up Room [W]", upExits))
				ShiftRoom(Point2.Up);
			if (Button(arrow + bounds.CenterLeft, leftImg, "Move Left Room [A]", leftExits))
				ShiftRoom(Point2.Left);
			if (Button(arrow + bounds.BottomCenter, rightImg, "Move Down Room [S]", downExits))
				ShiftRoom(Point2.Down);
			if (Button(arrow + bounds.CenterRight, downImg, "Move Right Room [D]", rightExits))
				ShiftRoom(Point2.Right);
			batcher.PopLayer();
		}

		UpdateEditorWorldView(inner);

		batcher.PushScissor((RectInt)(inner * Scale));
		var worldMatrix =
			Matrix3x2.CreateTranslation(-editorCamera) *
			Matrix3x2.CreateScale(editorZoom) *
			Matrix3x2.CreateTranslation(inner.Center);
		batcher.PushMatrix(worldMatrix);
		DrawEditorRooms();

		if (inner.Contains(Cursor) && Input.Mouse.LeftPressed)
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
			var tileOver = Point2.Zero;
			if (Matrix3x2.Invert(worldMatrix, out var inverse))
			{
				var worldMouse = Vector2.Transform(Cursor, inverse);
				localMouse = worldMouse - roomTopLeft;
				tileOver = (Point2)(localMouse / Game.TileSize);
			}
			var overGame = new Rect(0, 0, width, height).Contains(localMouse);

			for (int x = 1; x < editing.Size.X; x ++)
				batcher.Line(roomTopLeft + new Vector2(x * Game.TileSize, 0),  roomTopLeft + new Vector2(x * Game.TileSize, height), gridWeight, gridColor);
			for (int y = 1; y < editing.Size.Y; y ++)
				batcher.Line(roomTopLeft + new Vector2(0, y * Game.TileSize),  roomTopLeft + new Vector2(width, y * Game.TileSize), gridWeight, gridColor);

			if (lastCell == cell && overGame && (Input.Mouse.LeftPressed || Input.Mouse.RightPressed))
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
				var cursorColor = tileTypePlacing == '0' ? Color.Red : highlight;
				batcher.RectLine(cursorRect, gridWeight * 2, cursorColor);

				if (Factory.Find(brush) is {} placing)
				{
					var position = roomTopLeft + tileOver * Game.TileSize + placing.Offset;

					for (int x = shift; x < shift + size; x ++)
						for (int y = shift; y < shift + size; y ++)
							batcher.Image(placing.Image, position + new Point2(x, y) * Game.TileSize, placing.Origin, Vector2.One, 0, Color.White);
				}
			}

			if (tilePlacing && (Input.Mouse.LeftDown || Input.Mouse.RightDown))
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
		}

		batcher.PopMatrix();
		batcher.PopScissor();

		if (CurrentRoom == null && Button(arrow + bounds.Center, Assets.GetSubtexture("buttons/2"), "Create New Room Here"))
		{
			var it = new Room(roomCreatePosition, Room.DefaultSize);
			Assets.Rooms[it.Cell] = it;
			Assets.RoomsById[it.Id] = it;
			SelectRoom(it);
			it.Save();
			SyncEditorGame();
			FocusEditorOnCurrentRoom();
		}

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
}
