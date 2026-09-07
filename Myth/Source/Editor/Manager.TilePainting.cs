#if EDITOR
using System.Numerics;
using System.Text.Json.Nodes;
using Foster.Framework;
using ImGuiNET;
using static ImGuiNET.ImGui;

namespace Myth;

public partial class Manager
{
	const string LiveTilePaintUndoName = "Paint Tiles While Playing";

	static float LiveTileStickDeadzone => 0.45f;
	static float LiveTileCursorFirstRepeat => 0.18f;
	static float LiveTileCursorRepeat => 0.055f;
	
	bool LiveTileControllerModeOpen => liveTileController.Active || liveTileController.BlockGameUpdate;

	struct TilePaintStroke
	{
		public bool Active;
		public string? RoomId;
		public Point2 From;
		public Point2 To;
		public Factory.TileLayer Layer;
		public char Tile;
		public bool Slope;
		public readonly RectInt Rect => NormalizeTileRect(From, To);
		public readonly bool ToRight => To.X >= From.X;
		public readonly bool Underhang => To.Y < From.Y;

		public void Begin(string roomId, Point2 tile, Factory.TileLayer layer, char paint, bool slope)
		{
			Active = true;
			RoomId = roomId;
			From = To = tile;
			Layer = layer;
			Tile = paint;
			Slope = slope;
		}

		public void Update(Point2 tile) => To = tile;

		public void Cancel()
		{
			Active = false;
			RoomId = null;
		}
	}

	TilePaintStroke tilePaintStroke;
	LiveTileControllerState liveTileController;
	bool liveTileBrushArmed;
	string liveTileLabel = "";
	
	struct LiveTileControllerState
	{
		public bool Active;
		public bool BlockGameUpdate;
		public Point2 Cursor;
		public float RepeatTimer;
	}

	static bool TrySampleVisibleTile(Room room, Point2 tile, Factory.TileLayer fallbackLayer,
		out Factory.TileLayer layer, out char sampled)
	{
		var foreground = room.Tiles[tile.X, tile.Y];
		var background = room.BackgroundTiles[tile.X, tile.Y];
		layer = foreground != '0'
			? Factory.TileLayer.Foreground
			: background != '0' ? Factory.TileLayer.Background : fallbackLayer;
		sampled = layer == Factory.TileLayer.Foreground ? foreground : background;
		return sampled != '0' && Factory.Find(sampled) != null;
	}

	static void ApplyTileStroke(Room room, in TilePaintStroke stroke)
	{
		if (stroke.Slope)
			ApplySlopeStroke(room, stroke);
		else
		{
			var rect = ClampTileRect(stroke.Rect, room);
			for (var y = rect.Top; y < rect.Bottom; y++)
				for (var x = rect.Left; x < rect.Right; x++)
					room.SetTile(stroke.Layer, new Point2(x, y), stroke.Tile, rebuildGeometry: false);
		}
		room.RebuildGeometry(TileStrokeDirtyBounds(room, stroke.Rect));
	}

	// Fill the dragged rectangle with a slope staircase whose descent matches the drag direction:
	// down-right/left = a floor ramp (solid below), up-right/left = an underhang (solid above).
	static void ApplySlopeStroke(Room room, in TilePaintStroke stroke)
	{
		var rect = ClampTileRect(stroke.Rect, room);
		var w = rect.Width;
		var h = rect.Height;
		if (w <= 0 || h <= 0)
			return;

		for (var x = 0; x < w; x++)
		{
			var fromHigh = stroke.ToRight ? x : w - 1 - x;
			var step = Math.Min(h - 1, fromHigh * h / w);
			var surfaceRow = stroke.Underhang ? h - 1 - step : step;
			var col = rect.Left + x;
			var surfaceTile = new Point2(col, rect.Top + surfaceRow);
			room.SetTile(stroke.Layer, surfaceTile, stroke.Tile, rebuildGeometry: false);
			room.SetSlope(surfaceTile, true, stroke.Layer, rebuildGeometry: false);

			if (stroke.Underhang)
				for (var y = 0; y < surfaceRow; y++)
					room.SetTile(stroke.Layer, new Point2(col, rect.Top + y), stroke.Tile, rebuildGeometry: false);
			else
				for (var y = surfaceRow + 1; y < h; y++)
					room.SetTile(stroke.Layer, new Point2(col, rect.Top + y), stroke.Tile, rebuildGeometry: false);
		}

		// Cap the high corner with solid so the topmost run has a solid neighbour on its high side.
		var highCol = rect.Left + (stroke.ToRight ? 0 : w - 1);
		var highRow = rect.Top + (stroke.Underhang ? h - 1 : 0);
		room.SetTile(stroke.Layer, new Point2(highCol, highRow), stroke.Tile, rebuildGeometry: false);
	}

	static RectInt TileStrokeDirtyBounds(Room room, RectInt rect)
		=> new RectInt(
			room.WorldBounds.TopLeft + new Point2(rect.X, rect.Y) * Game.TileSize,
			new Point2(rect.Width, rect.Height) * Game.TileSize).Inflate(Game.TileSize);

	bool UpdateLiveTileEditing(Game liveGame, bool inputBlocked)
	{
		if (AttractMode) // the attract demo isn't an editable session
			return false;

		liveTileController.BlockGameUpdate = false;
		if (!liveGame.AcceptsLiveInput)
		{
			CancelTilePaint();
			liveTileController.Active = false;
			return false;
		}

		if (liveTileBrushArmed && !inputBlocked && Input.Keyboard.Pressed(Keys.Escape))
		{
			controls.Map.ConsumePress();
			CancelLiveTileEditing();
			DisplayMessage("Live tile paint off");
			return true;
		}

		if (liveTileBrushArmed && !inputBlocked && UpdateLiveTileUndoShortcut())
			return true;

		if (inputBlocked || liveGame.CurrentRoom is not { } runtimeRoom)
		{
			if (tilePaintStroke.Active && !Input.Mouse.LeftDown && !Input.Mouse.RightDown)
				CancelTilePaint();
			return liveTileBrushArmed;
		}

		if (UpdateLiveTileControllerEditing(liveGame, runtimeRoom))
			return true;

		var viewport = GameViewRect;
		var overGame = viewport.Contains(Cursor) && !TweakRegistry.Hovered && (!imgui.WantsMouseInput || gameViewHovered);
		if (!overGame)
		{
			if (tilePaintStroke.Active && !Input.Mouse.LeftDown && !Input.Mouse.RightDown)
				CancelTilePaint();
			return liveTileBrushArmed;
		}

		var worldMouse = GameMouseWorld(liveGame);
		var overRoom = runtimeRoom.WorldBounds.Contains(worldMouse);
		var tileOver = overRoom
			? ((worldMouse - (Vector2)runtimeRoom.WorldBounds.TopLeft) / Game.TileSize).FloorToPoint2()
			: Point2.Zero;

		if (Input.Keyboard.Alt && Input.Mouse.LeftPressed && overRoom)
		{
			CancelTilePaint();
			SampleLiveTile(runtimeRoom, tileOver);
			return true;
		}

		if (!liveTileBrushArmed)
			return Input.Keyboard.Alt;

		if (!tilePaintStroke.Active && overRoom && (Input.Mouse.LeftPressed || Input.Mouse.RightPressed))
			tilePaintStroke.Begin(runtimeRoom.Id, tileOver, activeTileLayer,
				Input.Mouse.LeftPressed ? brush : '0', slope: false);

		if (tilePaintStroke.Active)
		{
			if (runtimeRoom.Id != tilePaintStroke.RoomId)
				CancelTilePaint();
			else if ((Input.Mouse.LeftDown || Input.Mouse.RightDown) && overRoom)
				tilePaintStroke.Update(tileOver);
			else if (!Input.Mouse.LeftDown && !Input.Mouse.RightDown)
				CommitLiveTileStroke(liveGame, runtimeRoom);
		}

		return true;
	}

	bool UpdateLiveTileControllerEditing(Game liveGame, Room runtimeRoom)
	{
		if (LiveTileControllerTogglePressed())
		{
			if (liveTileController.Active)
				CancelLiveTileEditing();
			else
				StartLiveTileControllerMode(liveGame, runtimeRoom);
			liveTileController.BlockGameUpdate = true;
			controls.Move.Clear();
			return true;
		}

		if (!liveTileController.Active)
			return false;

		controls.Move.Clear();
		UpdateLiveTileControllerCursor(liveGame, runtimeRoom);

		var tile = liveTileController.Cursor.Clamp(new RectInt(Point2.Zero, runtimeRoom.Size - Point2.One));
		liveTileController.Cursor = tile;

		if (Input.AnyControllerButtonPressed(Buttons.West))
		{
			SampleLiveTile(runtimeRoom, tile);
			liveTileController.Active = true;
			return true;
		}

		if (Input.AnyControllerButtonPressed(Buttons.North))
		{
			activeTileLayer = activeTileLayer == Factory.TileLayer.Foreground ? Factory.TileLayer.Background : Factory.TileLayer.Foreground;
			UpdateLiveTileLabel();
			DisplayMessage(activeTileLayer == Factory.TileLayer.Foreground ? "Live tile layer: foreground" : "Live tile layer: background");
			return true;
		}

		if (Input.AnyControllerButtonPressed(Buttons.South))
		{
			CommitLiveTileAt(liveGame, runtimeRoom, tile, brush);
			return true;
		}

		if (Input.AnyControllerButtonPressed(Buttons.East))
		{
			CommitLiveTileAt(liveGame, runtimeRoom, tile, '0');
			return true;
		}

		return true;
	}

	bool LiveTileControllerTogglePressed()
	{
		foreach (var controller in Input.Controllers)
			if (controller.Connected &&
				controller.Down(Buttons.LeftShoulder) &&
				controller.Down(Buttons.RightShoulder) &&
				controller.Pressed(Buttons.Up))
				return true;
		return false;
	}

	void StartLiveTileControllerMode(Game liveGame, Room runtimeRoom)
	{
		liveTileBrushArmed = true;
		liveTileController.Active = true;
		liveTileController.Cursor = LiveTileInitialCursor(liveGame, runtimeRoom);
		liveTileController.RepeatTimer = 0;
		UpdateLiveTileLabel();
		DisplayMessage("Live tile brush");
	}

	Point2 LiveTileInitialCursor(Game liveGame, Room runtimeRoom)
	{
		if (liveGame.GetFirst<Player>() is { } player && runtimeRoom.WorldBounds.Contains(player.Position))
			return ((player.Position - (Vector2)runtimeRoom.WorldBounds.TopLeft) / Game.TileSize).FloorToPoint2()
				.Clamp(new RectInt(Point2.Zero, runtimeRoom.Size - Point2.One));
		return (runtimeRoom.Size / 2).Clamp(new RectInt(Point2.Zero, runtimeRoom.Size - Point2.One));
	}

	void UpdateLiveTileControllerCursor(Game liveGame, Room runtimeRoom)
	{
		if (TryLiveTileTouchpadCursor(liveGame, runtimeRoom, out var touchTile))
		{
			liveTileController.Cursor = touchTile;
			liveTileController.RepeatTimer = 0;
			return;
		}

		var direction = LiveTileControllerMoveDirection();
		if (direction == Point2.Zero)
		{
			liveTileController.RepeatTimer = 0;
			return;
		}

		if (liveTileController.RepeatTimer <= 0)
		{
			liveTileController.Cursor += direction;
			liveTileController.RepeatTimer = LiveTileCursorFirstRepeat;
		}
		else
		{
			liveTileController.RepeatTimer -= Time.Delta;
			if (liveTileController.RepeatTimer <= 0)
			{
				liveTileController.Cursor += direction;
				liveTileController.RepeatTimer = LiveTileCursorRepeat;
			}
		}
	}

	Point2 LiveTileControllerMoveDirection()
	{
		foreach (var controller in Input.Controllers)
		{
			if (!controller.Connected)
				continue;
			if (controller.Repeated(Buttons.Left))
				return Point2.Left;
			if (controller.Repeated(Buttons.Right))
				return Point2.Right;
			if (controller.Repeated(Buttons.Up))
				return Point2.Up;
			if (controller.Repeated(Buttons.Down))
				return Point2.Down;

			var stick = controller.LeftStick;
			if (MathF.Abs(stick.X) >= MathF.Abs(stick.Y) && MathF.Abs(stick.X) > LiveTileStickDeadzone)
				return stick.X > 0 ? Point2.Right : Point2.Left;
			if (MathF.Abs(stick.Y) > LiveTileStickDeadzone)
				return stick.Y > 0 ? Point2.Down : Point2.Up;
		}
		return Point2.Zero;
	}

	bool TryLiveTileTouchpadCursor(Game liveGame, Room runtimeRoom, out Point2 tile)
	{
		foreach (var controller in Input.Controllers)
		{
			if (!controller.Connected)
				continue;
			foreach (var finger in controller.Touchpad)
			{
				if (!finger.Down)
					continue;
				var viewport = GameViewRect;
				var screen = viewport.TopLeft + finger.Position * viewport.Size;
				var world = GameImGuiToWorld(viewport, liveGame, screen);
				if (!runtimeRoom.WorldBounds.Contains(world))
					break;
				tile = ((world - (Vector2)runtimeRoom.WorldBounds.TopLeft) / Game.TileSize).FloorToPoint2()
					.Clamp(new RectInt(Point2.Zero, runtimeRoom.Size - Point2.One));
				return true;
			}
		}

		tile = Point2.Zero;
		return false;
	}

	void CommitLiveTileAt(Game liveGame, Room runtimeRoom, Point2 tile, char paint)
	{
		tilePaintStroke.Begin(runtimeRoom.Id, tile, activeTileLayer, paint, slope: false);
		CommitLiveTileStroke(liveGame, runtimeRoom);
	}

	void SampleLiveTile(Room runtimeRoom, Point2 tile)
	{
		if (!TrySampleVisibleTile(runtimeRoom, tile, activeTileLayer, out var layer, out var sampled))
		{
			DisplayMessage("No tile to sample");
			return;
		}

		if (Factory.Find(sampled) is not { } entry || entry.NeuralTexture != null)
		{
			DisplayMessage("That tile requires a room reload and cannot be painted live yet");
			return;
		}

		SelectTileBrush(layer, sampled);
		liveTileBrushArmed = true;
		UpdateLiveTileLabel();
		DisplayMessage($"Live tile paint: {entry.Name}");
	}

	void UpdateLiveTileLabel()
	{
		var shortLayer = activeTileLayer == Factory.TileLayer.Foreground ? "FG" : "BG";
		var entryName = Factory.Find(brush)?.Name ?? "empty";
		liveTileLabel = liveTileController.Active
			? $"LIVE TILE  |  {shortLayer}  |  {entryName}  |  DPad/Stick move  |  X paint  O erase  □ sample  △ layer"
			: $"LIVE TILE  |  {shortLayer}  |  {entryName}  |  Esc to stop";
	}

	void CommitLiveTileStroke(Game liveGame, Room runtimeRoom)
	{
		var stroke = tilePaintStroke;
		CancelTilePaint();
		if (Assets.GetRoom(runtimeRoom.Id) is not { } authoredRoom)
			return;

		if (!LiveTileRegionSupported(authoredRoom, runtimeRoom, stroke.Rect, stroke.Layer, stroke.Tile))
		{
			DisplayMessage("That stroke touches a generated tile and requires a room reload");
			return;
		}

		using (undo.Capture(LiveTilePaintUndoName,
			new LiveTileRegionUndoTarget(this, authoredRoom, runtimeRoom, stroke.Rect, stroke.Layer)))
		{
			ApplyTileStroke(authoredRoom, stroke);
			ApplyTileStroke(runtimeRoom, stroke);
		}

		authoredRoom.Save();
		MarkRoomPreviewDirty(authoredRoom);
		liveGame.RefreshRoomTiles();
	}

	static bool LiveTileRegionSupported(Room authored, Room runtime, RectInt rect, Factory.TileLayer layer, char paint)
	{
		if (paint != '0' && (Factory.Find(paint) is not { } entry || entry.NeuralTexture != null))
			return false;

		for (var y = rect.Top; y < rect.Bottom; y++)
			for (var x = rect.Left; x < rect.Right; x++)
			{
				var tile = new Point2(x, y);
				if (LiveTileRequiresReload(authored, tile, layer) || LiveTileRequiresReload(runtime, tile, layer))
					return false;
			}
		return true;
	}

	static bool LiveTileRequiresReload(Room room, Point2 tile, Factory.TileLayer layer)
	{
		var value = layer == Factory.TileLayer.Foreground
			? room.Tiles[tile.X, tile.Y]
			: room.BackgroundTiles[tile.X, tile.Y];
		return Factory.Find(value) is { NeuralTexture: not null }
			|| layer == Factory.TileLayer.Foreground && room.IsFungal(tile);
	}

	bool UpdateLiveTileUndoShortcut()
	{
		if (!Input.Keyboard.CtrlOrCommand)
			return false;

		if (Input.Keyboard.Pressed(Keys.Z))
		{
			var redo = Input.Keyboard.Shift;
			if (redo ? undo.RedoName == LiveTilePaintUndoName : undo.UndoName == LiveTilePaintUndoName)
			{
				CancelTilePaint();
				if (redo) undo.Redo(); else undo.Undo();
				DisplayMessage(redo ? "Redid live tile paint" : "Undid live tile paint");
			}
			return true;
		}

		if (Input.Keyboard.Pressed(Keys.Y))
		{
			if (undo.RedoName == LiveTilePaintUndoName)
			{
				CancelTilePaint();
				undo.Redo();
				DisplayMessage("Redid live tile paint");
			}
			return true;
		}

		return false;
	}

	void CancelLiveTileEditing()
	{
		CancelTilePaint();
		liveTileBrushArmed = false;
		liveTileController.Active = false;
		liveTileLabel = "";
	}

	void LiveTileEditingImGui()
	{
		if (!playing || AttractMode || game?.CurrentRoom is not { } room)
			return;

		var viewport = GameViewRect;
		if (liveTileBrushArmed)
			LiveTileStatusOverlayImGui(viewport);

		if (liveTileController.Active)
		{
			DrawLiveTileCursor(room, liveTileController.Cursor, viewport);
			return;
		}

		if ((!liveTileBrushArmed && !Input.Keyboard.Alt) || !viewport.Contains(Cursor)
			|| TweakRegistry.Hovered
			|| imgui.WantsMouseInput && !gameViewHovered)
			return;

		var worldMouse = GameMouseWorld(game);
		if (!room.WorldBounds.Contains(worldMouse))
			return;

		var tileOver = ((worldMouse - (Vector2)room.WorldBounds.TopLeft) / Game.TileSize).FloorToPoint2();
		var rect = tilePaintStroke.Active
			? tilePaintStroke.Rect
			: new RectInt(tileOver.X, tileOver.Y, 1, 1);
		var worldTopLeft = room.WorldBounds.TopLeft + new Point2(rect.Left, rect.Top) * Game.TileSize;
		var worldBottomRight = room.WorldBounds.TopLeft + new Point2(rect.Right, rect.Bottom) * Game.TileSize;
		var topLeft = GameWorldToImGui(viewport, game, worldTopLeft);
		var bottomRight = GameWorldToImGui(viewport, game, worldBottomRight);
		var erasing = tilePaintStroke.Active && tilePaintStroke.Tile == '0';
		var color = erasing ? ImGuiColor(1.0f, 0.25f, 0.20f, 0.95f) : ImGuiColor(0.45f, 1.0f, 0.75f, 0.95f);
		var fill = erasing ? ImGuiColor(1.0f, 0.15f, 0.12f, 0.12f) : ImGuiColor(0.35f, 1.0f, 0.65f, 0.12f);
		var draw = GetForegroundDrawList();
		draw.PushClipRect(viewport.TopLeft, viewport.BottomRight, true);
		draw.AddRectFilled(topLeft, bottomRight, fill);
		draw.AddRect(topLeft, bottomRight, color, 0, ImDrawFlags.None, 2.0f);
		draw.PopClipRect();
	}

	void LiveTileStatusOverlayImGui(Rect viewport)
	{
		var textSize = CalcTextSize(liveTileLabel) * 0.82f;
		var swatchSize = new Vector2(14, 14);
		var padding = new Vector2(4, 2);
		var gap = 5;
		var contentSize = new Vector2(swatchSize.X + gap + textSize.X, MathF.Max(swatchSize.Y, textSize.Y));
		var size = contentSize + padding * 2;
		var screenPosition = AllocateDebugOverlayTopCenter(viewport, size.Y);
		var position = new Vector2(screenPosition.X - size.X * 0.5f, screenPosition.Y - size.Y);
		position.X = Math.Clamp(position.X, viewport.Left + 4, viewport.Right - size.X - 4);
		position.Y = Math.Clamp(position.Y, viewport.Top + 4, viewport.Bottom - size.Y - 4);

		SetNextWindowPos(position, ImGuiCond.Always);
		SetNextWindowSize(size, ImGuiCond.Always);
		PushStyleVar(ImGuiStyleVar.WindowPadding, padding);
		PushStyleVar(ImGuiStyleVar.WindowRounding, 1);
		PushStyleColor(ImGuiCol.WindowBg, new Vector4(0, 0, 0, 0.72f));
		Begin(
			"LiveTileEditingStatus",
			ImGuiWindowFlags.NoDecoration |
			ImGuiWindowFlags.NoInputs |
			ImGuiWindowFlags.NoSavedSettings |
			ImGuiWindowFlags.NoFocusOnAppearing |
			ImGuiWindowFlags.NoNav);
		DrawLiveTileBrushSwatch(swatchSize);
		SameLine(0, gap);
		SetWindowFontScale(0.82f);
		TextUnformatted(liveTileLabel);
		SetWindowFontScale(1.0f);
		End();
		PopStyleColor();
		PopStyleVar(2);
	}

	void DrawLiveTileBrushSwatch(Vector2 size)
	{
		if (Factory.Find(brush) is { } entry && entry.Image.Texture != null && imgui != null)
			imgui.Image(entry.Image, size);
		else
			Dummy(size);

		var min = GetItemRectMin();
		var max = GetItemRectMax();
		var draw = GetWindowDrawList();
		draw.AddRect(min, max, ImGuiColor(1, 1, 1, 0.55f), 0, ImDrawFlags.None, 1);
		if (brush == '0')
		{
			draw.AddRectFilled(min + Vector2.One, max - Vector2.One, ImGuiColor(0.12f, 0.12f, 0.12f, 0.8f));
			draw.AddLine(min + new Vector2(2, max.Y - min.Y - 2), max - new Vector2(2, max.Y - min.Y - 2), ImGuiColor(1, 0.25f, 0.2f, 0.85f), 1);
		}
	}

	void DrawLiveTileCursor(Room room, Point2 tile, Rect viewport)
	{
		var rect = new RectInt(tile.X, tile.Y, 1, 1);
		var worldTopLeft = room.WorldBounds.TopLeft + rect.TopLeft * Game.TileSize;
		var worldBottomRight = room.WorldBounds.TopLeft + rect.BottomRight * Game.TileSize;
		var topLeft = GameWorldToImGui(viewport, game!, worldTopLeft);
		var bottomRight = GameWorldToImGui(viewport, game!, worldBottomRight);
		var draw = GetForegroundDrawList();
		draw.PushClipRect(viewport.TopLeft, viewport.BottomRight, true);
		draw.AddRectFilled(topLeft, bottomRight, ImGuiColor(0.35f, 1.0f, 0.65f, 0.14f));
		draw.AddRect(topLeft, bottomRight, ImGuiColor(0.45f, 1.0f, 0.75f, 0.95f), 0, ImDrawFlags.None, 2.0f);
		draw.PopClipRect();
	}

	sealed class LiveTileRegionUndoTarget(
		Manager manager,
		Room authoredRoom,
		Room runtimeRoom,
		RectInt rect,
		Factory.TileLayer layer) : IJsonUndoTarget
	{
		public string UndoId => $"live-tiles:{authoredRoom.Id}:{layer}:{rect.X},{rect.Y},{rect.Width},{rect.Height}";

		public string CaptureJson()
			=> ActorSpecs.ToJsonString(new JsonObject
			{
				["authored"] = CaptureRegion(authoredRoom),
				["runtime"] = CaptureRegion(runtimeRoom)
			});

		public void ApplyJson(string json)
		{
			var state = JsonNode.Parse(json)?.AsObject();
			if (state?["authored"] is JsonObject authoredState)
			{
				ApplyRegion(authoredRoom, authoredState);
				authoredRoom.Save();
				manager.MarkRoomPreviewDirty(authoredRoom);
			}

			if (state?["runtime"] is JsonObject runtimeState &&
				manager.game?.CurrentRoom is { } liveRoom && liveRoom.Id == authoredRoom.Id)
			{
				ApplyRegion(liveRoom, runtimeState);
				manager.game.RefreshRoomTiles();
			}
		}

		JsonObject CaptureRegion(Room room)
		{
			var rows = new JsonArray();
			var flags = new JsonArray();
			for (var y = rect.Top; y < rect.Bottom; y++)
			{
				var row = new char[rect.Width];
				for (var x = rect.Left; x < rect.Right; x++)
				{
					var tile = new Point2(x, y);
					row[x - rect.Left] = layer == Factory.TileLayer.Foreground
						? room.Tiles[x, y]
						: room.BackgroundTiles[x, y];
					flags.Add((JsonNode)JsonValue.Create((int)room.GetTileFlags(tile)));
				}
				rows.Add((JsonNode)JsonValue.Create(new string(row)));
			}
			return new JsonObject { ["tiles"] = rows, ["flags"] = flags };
		}

		void ApplyRegion(Room room, JsonObject state)
		{
			var rows = state["tiles"]?.AsArray();
			var flags = state["flags"]?.AsArray();
			if (rows == null || flags == null)
				return;

			var flagIndex = 0;
			for (var y = rect.Top; y < rect.Bottom; y++)
			{
				var row = rows[y - rect.Top]?.GetValue<string>() ?? "";
				for (var x = rect.Left; x < rect.Right; x++)
				{
					if (x - rect.Left >= row.Length || flagIndex >= flags.Count)
						return;
					var tile = new Point2(x, y);
					room.SetTile(layer, tile, row[x - rect.Left], rebuildGeometry: false);
					room.SetTileFlags(tile, (Room.TileFlags)(flags[flagIndex++]?.GetValue<int>() ?? 0));
				}
			}
			room.RebuildGeometry(TileStrokeDirtyBounds(room, rect));
		}
	}
}
#endif
