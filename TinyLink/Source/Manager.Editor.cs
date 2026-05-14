using System.Numerics;
using System.Text.Json.Nodes;
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
	/// Square tile brush size.
	/// </summary>
	private int brushSize = 1;

	Point2 tilePlaceFrom;
	char tileTypePlacing;
	bool tilePlacing;
	RectInt? proposedRoomTiles;
	string? selectedActorSpecId;
	int selectedActorPaletteIndex = -1;
	bool actorSpecInspectorOpen;
	bool actorSelectTool;
	int selectedRoomActorIndex = -1;
	bool draggingRoomActor;
	readonly JsonUndoManager undo = new();
	JsonUndoScope? tilePaintUndo;
	JsonUndoScope? actorMoveUndo;
	JsonUndoScope? prefabUndo;
	string? prefabUndoSpecId;

	private IJsonUndoTarget CurrentRoomUndoTarget(Room room)
		=> new RoomUndoTarget(room);

	private IJsonUndoTarget EditorSelectionUndoTarget()
		=> new EditorSelectionTarget(this);

	private JsonUndoScope CaptureRoomUndo(string name, Room room)
		=> undo.Capture(name, CurrentRoomUndoTarget(room), EditorSelectionUndoTarget());

	private JsonUndoScope CaptureSelectionUndo(string name)
		=> undo.Capture(name, EditorSelectionUndoTarget());

	private JsonUndoScope CapturePrefabUndo(string name, string specId)
		=> undo.Capture(name, new ActorSpecUndoTarget(specId));

	private void UndoEditor()
	{
		undo.Undo();
		AfterUndoRedo();
	}

	private void RedoEditor()
	{
		undo.Redo();
		AfterUndoRedo();
	}

	private void AfterUndoRedo()
	{
		if (CurrentRoom is { } room)
		{
			if (selectedRoomActorIndex >= room.Actors.Count)
				selectedRoomActorIndex = -1;
			room.Save();
		}

		draggingRoomActor = false;
		tilePlacing = false;
		actorMoveUndo = null;
		tilePaintUndo = null;
		prefabUndo = null;
		prefabUndoSpecId = null;
		SyncEditorGame();
	}

	private bool UpdateEditorUndoRedo()
	{
		if (imgui.WantsTextInput)
			return false;

		var modifier =
			Input.Keyboard.Down(Keys.LeftControl) ||
			Input.Keyboard.Down(Keys.RightControl) ||
			Input.Keyboard.Down(Keys.LeftOS) ||
			Input.Keyboard.Down(Keys.RightOS);
		if (!modifier)
			return false;

		if (Input.Keyboard.Pressed(Keys.Z))
		{
			if (Input.Keyboard.Shift)
				RedoEditor();
			else
				UndoEditor();
			return true;
		}

		if (Input.Keyboard.Pressed(Keys.Y))
		{
			RedoEditor();
			return true;
		}

		return false;
	}

	private sealed class RoomUndoTarget(Room room) : IJsonUndoTarget
	{
		public string UndoId => $"room:{room.Id}";
		public string CaptureJson() => room.ToJsonString();
		public void ApplyJson(string json) => room.LoadJsonState(json);
	}

	private sealed class EditorSelectionTarget(Manager manager) : IJsonUndoTarget
	{
		public string UndoId => "editor-selection";

		public string CaptureJson()
		{
			var json = new JsonObject
			{
				["roomId"] = manager.selectedRoomId,
				["actorIndex"] = manager.selectedRoomActorIndex,
				["actorSelectTool"] = manager.actorSelectTool,
				["brush"] = manager.brush.ToString(),
				["actorPaletteIndex"] = manager.selectedActorPaletteIndex,
				["prefabId"] = manager.selectedActorSpecId,
				["prefabInspectorOpen"] = manager.actorSpecInspectorOpen,
				["entryDirection"] = manager.editorRoomEntryDirection is { } direction ? new JsonArray(direction.X, direction.Y) : null
			};
			return ActorSpecs.ToJsonString(json);
		}

		public void ApplyJson(string jsonText)
		{
			var json = JsonNode.Parse(jsonText)?.AsObject() ?? [];
			var roomId = ActorSpecs.ReadString(json, "roomId");
			if (roomId != null && Assets.GetRoom(roomId) is { } room)
				manager.SelectRoom(room);

			manager.selectedRoomActorIndex = ReadInt(json, "actorIndex", -1);
			manager.actorSelectTool = ReadBool(json, "actorSelectTool");
			if (ActorSpecs.ReadString(json, "brush") is { Length: > 0 } brush)
				manager.brush = brush[0];
			manager.selectedActorPaletteIndex = ReadInt(json, "actorPaletteIndex", -1);
			manager.selectedActorSpecId = ActorSpecs.ReadString(json, "prefabId");
			manager.actorSpecInspectorOpen = ReadBool(json, "prefabInspectorOpen");
			manager.editorRoomEntryDirection = ActorSpecs.ReadPoint(json, "entryDirection");
		}

		private static int ReadInt(JsonObject json, string key, int fallback)
			=> json.TryGetPropertyValue(key, out var node) && node != null && int.TryParse(node.ToString(), out var value) ? value : fallback;

		private static bool ReadBool(JsonObject json, string key)
			=> json.TryGetPropertyValue(key, out var node) && node != null && bool.TryParse(node.ToString(), out var value) && value;
	}

	private sealed class ActorSpecUndoTarget(string specId) : IJsonUndoTarget
	{
		public string UndoId => $"actor-spec:{specId}";
		public string CaptureJson() => ActorSpecs.CaptureSpecJson(specId);
		public void ApplyJson(string json) => ActorSpecs.ApplySpecJson(specId, json);
	}

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
		var view = EditorOpenRect;
		editorZoom = Math.Clamp(Math.Min(view.Width / Game.Width, view.Height / Game.Height), 0.15f, 8.0f);
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
			using (CaptureSelectionUndo("Select Room"))
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
			if (ImGuiTextButton("Select", new Vector2(ButtonSize * 2 + ButtonSpacing, ButtonSize), actorSelectTool))
			{
				actorSelectTool = true;
				brush = '0';
			}
			ImGui.Spacing();
			DrawTilePaletteSection();
			ImGui.Spacing();
			DrawActorPaletteSection();
		}
		ImGui.End();

		ImGui.PopStyleColor(2);
		ImGui.PopStyleVar(4);

		ActorSpecInspectorImGui();
	}

	private void DrawTilePaletteSection()
	{
		ImGui.TextUnformatted("Tiles");
		var index = 0;
		foreach (var (id, entry) in Factory.Entries)
		{
			if (index > 0 && index % PaletteColumns != 0)
				ImGui.SameLine();

			if (ImGuiSubtextureButton($"Palette_Tiles_{id}", entry.Image, entry.Name, selectedActorPaletteIndex < 0 && brush == id, new Vector2(ButtonSize, ButtonSize)))
			{
				brush = id;
				selectedActorPaletteIndex = -1;
				actorSelectTool = false;
			}

			index++;
		}
	}

	private void DrawActorPaletteSection()
	{
		ImGui.TextUnformatted("Actors");
		var index = 0;
		for (var i = 0; i < Factory.ActorPalette.Count; i ++)
		{
			var entry = Factory.ActorPalette[i];
			if (entry.Image.Texture == null)
				continue;

			if (index > 0 && index % PaletteColumns != 0)
				ImGui.SameLine();

			if (ImGuiSubtextureButton($"Palette_Actors_{i}", entry.Image, entry.Name, selectedActorPaletteIndex == i, new Vector2(ButtonSize, ButtonSize)))
			{
				selectedActorPaletteIndex = i;
				actorSelectTool = false;
				if (entry.ActorSpecId != null)
				{
					selectedActorSpecId = entry.ActorSpecId;
					actorSpecInspectorOpen = true;
				}
			}

			index++;
		}
	}

	private void ActorSpecInspectorImGui()
	{
		if (!actorSpecInspectorOpen || selectedActorSpecId == null)
			return;

		ImGui.SetNextWindowSize(new Vector2(260, 0), ImGuiCond.FirstUseEver);
		var open = actorSpecInspectorOpen;
		if (!ImGui.Begin($"Prefab: {selectedActorSpecId}", ref open, ImGuiWindowFlags.NoSavedSettings))
		{
			actorSpecInspectorOpen = open;
			ImGui.End();
			return;
		}
		actorSpecInspectorOpen = open;

		var changed = false;
		foreach (var property in ActorSpecs.GetEditableProperties(selectedActorSpecId))
		{
			ImGui.PushID(property.JsonName);
			changed |= DrawActorSpecProperty(selectedActorSpecId, property);
			ImGui.PopID();
		}

		if (changed)
			SyncEditorGame();

		if (ImGui.Button("Save"))
			ActorSpecs.SaveSpec(selectedActorSpecId);

		ImGui.End();
	}

	private bool DrawActorSpecProperty(string specId, ActorSpecs.EditableProperty property)
	{
		var changed = false;
		var controlWidth = MathF.Min(150, MathF.Max(96, ImGui.GetContentRegionAvail().X - 72));
		ImGui.TextWrapped(property.Name);
		ImGui.SetNextItemWidth(controlWidth);
		ImGui.PushID("Value");

		if (property.Type == typeof(float))
		{
			var value = property.Value?.GetValue<float>() ?? 0;
			if (ImGui.DragFloat("##Value", ref value, 0.05f))
			{
				BeginPrefabUndo(specId);
				ActorSpecs.SetProperty(specId, property.JsonName, JsonValue.Create(value));
				changed = true;
			}
		}
		else if (property.Type == typeof(int))
		{
			var value = property.Value?.GetValue<int>() ?? 0;
			if (ImGui.DragInt("##Value", ref value))
			{
				BeginPrefabUndo(specId);
				ActorSpecs.SetProperty(specId, property.JsonName, JsonValue.Create(value));
				changed = true;
			}
		}
		else if (property.Type == typeof(bool))
		{
			var value = property.Value?.GetValue<bool>() ?? false;
			if (ImGui.Checkbox("##Value", ref value))
			{
				BeginPrefabUndo(specId);
				ActorSpecs.SetProperty(specId, property.JsonName, JsonValue.Create(value));
				changed = true;
			}
		}
		else if (property.Type == typeof(Color))
		{
			var color = property.Value?.GetValue<string>() ?? "ffffff";
			var rgb = HexToVector3(color);
			if (ImGui.ColorEdit3("##Value", ref rgb, ImGuiColorEditFlags.NoInputs))
			{
				BeginPrefabUndo(specId);
				ActorSpecs.SetProperty(specId, property.JsonName, JsonValue.Create(Vector3ToHex(rgb)));
				changed = true;
			}
		}
		else
		{
			ImGui.TextUnformatted("-");
		}
		ImGui.PopID();
		if (prefabUndoSpecId == specId && ImGui.IsItemDeactivatedAfterEdit())
			CommitPrefabUndo();

		if (property.HasLocalValue)
		{
			ImGui.SameLine();
			if (ImGui.SmallButton("Reset"))
			{
				using (CapturePrefabUndo("Reset Prefab Property", specId))
				{
					ActorSpecs.ResetProperty(specId, property.JsonName);
					changed = true;
				}
			}
		}

		return changed;
	}

	private void BeginPrefabUndo(string specId)
	{
		if (prefabUndoSpecId == specId && prefabUndo != null)
			return;

		CommitPrefabUndo();
		prefabUndoSpecId = specId;
		prefabUndo = CapturePrefabUndo("Edit Prefab", specId);
	}

	private void CommitPrefabUndo()
	{
		prefabUndo?.Commit();
		prefabUndo = null;
		prefabUndoSpecId = null;
	}

	private static Vector3 HexToVector3(string hex)
	{
		hex = hex.TrimStart('#');
		if (hex.Length < 6 || !uint.TryParse(hex[..6], System.Globalization.NumberStyles.HexNumber, null, out var value))
			return Vector3.One;

		return new Vector3(
			((value >> 16) & 0xff) / 255.0f,
			((value >> 8) & 0xff) / 255.0f,
			(value & 0xff) / 255.0f);
	}

	private static string Vector3ToHex(Vector3 value)
	{
		var r = (int)(Math.Clamp(value.X, 0, 1) * 255);
		var g = (int)(Math.Clamp(value.Y, 0, 1) * 255);
		var b = (int)(Math.Clamp(value.Z, 0, 1) * 255);
		return $"{r:X2}{g:X2}{b:X2}";
	}

	private static RectInt RoomActorBounds(Room room, JsonObject actor)
	{
		var local = ActorSpecs.ReadPoint(actor, "position") ?? Point2.Zero;
		var position = room.WorldBounds.TopLeft + local;
		if (FindRoomActorEntry(actor) is { EditorBounds: { } bounds })
			return bounds + position;

		return new RectInt(position.X - 4, position.Y - 4, 8, 8);
	}

	private static Factory.ActorPaletteEntry? FindRoomActorEntry(JsonObject actor)
	{
		var specId = ActorSpecs.ReadString(actor, "from");
		var typeName = ActorSpecs.ReadString(actor, "type");

		foreach (var entry in Factory.ActorPalette)
		{
			if (specId != null && entry.ActorSpecId == specId)
				return entry;
			if (typeName != null && entry.ActorTypeName == typeName && entry.ActorSpecId == null)
				return entry;
		}

		return null;
	}

	private int HitTestRoomActor(Room room, Vector2 world)
	{
		for (var i = room.Actors.Count - 1; i >= 0; i --)
			if (RoomActorBounds(room, room.Actors[i]).Contains(world))
				return i;

		return -1;
	}

	private void DrawRoomActorOverlays(Room room, float lineWeight, int hoveredIndex, Vector2 worldMouse)
	{
		for (var i = 0; i < room.Actors.Count; i ++)
		{
			var selected = i == selectedRoomActorIndex;
			if (draggingRoomActor && !selected)
				continue;

			var hovered = i == hoveredIndex;
			var bounds = RoomActorBounds(room, room.Actors[i]);
			if (!selected && !hovered && !bounds.Inflate(bounds.Width * 1.5f, bounds.Height * 1.5f).Contains(worldMouse))
				continue;

			var color = selected ? highlight : hovered ? Color.White : Color.Gray;
			batcher.RectLine(bounds.Inflate(lineWeight * 2), lineWeight * 2, color);
		}
	}

	private void DrawPlaytestPlayerMarker(Room room)
	{
		if (playtestPlayerPosition is not { } remembered || remembered.RoomId != room.Id)
			return;

		var sprite = Assets.GetSprite("player");
		if (sprite == null || sprite.Frames.Count <= 0)
			return;

		batcher.Image(sprite.Frames[0].Subtexture, remembered.Position, sprite.Origin, Vector2.One, 0, Color.White * 0.25f);
	}

	private void MoveRoomActorTo(Room room, int actorIndex, Vector2 world, Point2 tile, bool snap)
	{
		if (actorIndex < 0 || actorIndex >= room.Actors.Count)
			return;

		var actor = room.Actors[actorIndex];
		var specId = ActorSpecs.ReadString(actor, "from");
		var typeName = ActorSpecs.ReadString(actor, "type");
		var offset = Point2.Zero;
		if (specId != null || typeName != null)
		{
			foreach (var entry in Factory.ActorPalette)
			{
				if ((specId != null && entry.ActorSpecId == specId) ||
				    (typeName != null && entry.ActorTypeName == typeName && entry.ActorSpecId == null))
				{
					offset = entry.Offset;
					break;
				}
			}
		}

		var local = snap ? tile * Game.TileSize + offset : (Point2)(world - (Vector2)room.WorldBounds.TopLeft);
		actor["position"] = new JsonArray(local.X, local.Y);
		editorGameDirty = true;
	}

	private void StampTiles(Room room, Point2 origin, int shift, int size, char tile)
	{
		for (var x = shift; x < shift + size; x ++)
		for (var y = shift; y < shift + size; y ++)
			room.Set(origin + new Point2(x, y), tile);
		editorGameDirty = true;
	}

	private void UpdateEditorWorldView(Rect inner)
	{
		if (imgui.WantsMouseInput)
			return;

		if (!inner.Contains(Cursor))
			return;

		if (Input.Mouse.MiddleDown)
			editorCamera -= Input.Mouse.Delta / Scale / editorZoom;

		var wheel = Input.Mouse.Wheel;
		var wheelAmount = MathF.Abs(wheel.Y) >= MathF.Abs(wheel.X) ? wheel.Y : wheel.X;
		if (wheelAmount == 0)
			return;

		if (wheel.X != 0)
		{
			brushSize = Math.Clamp(brushSize + Math.Sign(wheelAmount), 1, 10);
			return;
		}

		var before = ScreenToEditorWorld(Cursor, inner);
		var zoomFactor = MathF.Pow(1.15f, wheelAmount);
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
			var line = MathF.Max(1.0f / editorZoom, LineWeight / editorZoom);
			if (CurrentRoom == room)
				continue;

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

	private static Point2? GetEditorEntryDirection(Room from, Room to)
	{
		if (to.Position.X >= from.Position.X + from.Size.X)
			return Point2.Right;
		if (to.Position.X + to.Size.X <= from.Position.X)
			return Point2.Left;
		if (to.Position.Y >= from.Position.Y + from.Size.Y)
			return Point2.Down;
		if (to.Position.Y + to.Size.Y <= from.Position.Y)
			return Point2.Up;
		return null;
	}

	private void Editor()
	{
		var bounds = WorkspaceRect;
		var inner = GameViewRect;
		var arrow = new Rect().Inflate(ButtonSize / 2);
		var lastCell = cell;
		var worldMouseBlocked = imgui.WantsMouseInput;
		UpdateEditorUndoRedo();
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

		if (!worldMouseBlocked && inner.Contains(Cursor) && Input.Mouse.LeftPressed && !eyedropper)
		{
			var worldMouse = ScreenToEditorWorld(Cursor, inner);
			if (GetRoomAtWorld(worldMouse) is { } selected && selected != CurrentRoom)
			{
				using (CaptureSelectionUndo("Select Room"))
				{
					if (CurrentRoom is { } previous)
						editorRoomEntryDirection = GetEditorEntryDirection(previous, selected);
					CurrentRoom?.Save();
					SelectRoom(selected);
					SyncEditorGame();
				}
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
			proposedRoomTiles = worldMouseBlocked || overGame ? null : GetProposedRoomTiles(editing, worldMouse);
			if (selectedRoomActorIndex >= editing.Actors.Count)
				selectedRoomActorIndex = -1;
			var hoveredActorIndex = !worldMouseBlocked && overGame ? HitTestRoomActor(editing, worldMouse) : -1;

			for (int x = 1; x < editing.Size.X; x ++)
				batcher.Line(roomTopLeft + new Vector2(x * Game.TileSize, 0),  roomTopLeft + new Vector2(x * Game.TileSize, height), gridWeight, gridColor);
			for (int y = 1; y < editing.Size.Y; y ++)
				batcher.Line(roomTopLeft + new Vector2(0, y * Game.TileSize),  roomTopLeft + new Vector2(width, y * Game.TileSize), gridWeight, gridColor);

			var brushEntry = Factory.Find(brush);
			Factory.ActorPaletteEntry? actorBrush = selectedActorPaletteIndex >= 0 && selectedActorPaletteIndex < Factory.ActorPalette.Count
				? Factory.ActorPalette[selectedActorPaletteIndex]
				: null;
			var brushIsActor = actorBrush != null;

			if (eyedropper)
				tilePlacing = false;

			if (worldMouseBlocked)
				tilePlacing = false;

			if (!worldMouseBlocked && actorSelectTool && overGame && Input.Mouse.LeftPressed)
			{
				using (CaptureSelectionUndo("Select Actor"))
				{
					selectedRoomActorIndex = hoveredActorIndex;
					draggingRoomActor = selectedRoomActorIndex >= 0;
					tilePlacing = false;
				}
			}
			else if (!worldMouseBlocked && overGame && brushIsActor && Input.Mouse.RightPressed && hoveredActorIndex >= 0)
			{
				using (CaptureRoomUndo("Delete Actor", editing))
				{
					editing.Actors.RemoveAt(hoveredActorIndex);
					if (selectedRoomActorIndex == hoveredActorIndex)
						selectedRoomActorIndex = -1;
					else if (selectedRoomActorIndex > hoveredActorIndex)
						selectedRoomActorIndex--;
					draggingRoomActor = false;
					tilePlacing = false;
					editorGameDirty = true;
				}
			}
			else if (!worldMouseBlocked && eyedropper && overGame && Input.Mouse.LeftPressed)
			{
				brush = editing.Tiles[tileOver.X, tileOver.Y];
				tileTypePlacing = brush;
			}
			else if (!worldMouseBlocked && overGame && Input.Mouse.LeftPressed && actorBrush is { } actorEntry)
			{
				using (CaptureRoomUndo("Place Actor", editing))
				{
					var position = (Point2)(roomTopLeft + tileOver * Game.TileSize + actorEntry.Offset);
					if (actorEntry.ActorSpecId != null)
						editing.AddActorSpec(actorEntry.ActorSpecId, position);
					else if (actorEntry.ActorTypeName != null)
						editing.AddActorType(actorEntry.ActorTypeName, position);
					tilePlacing = false;
					editorGameDirty = true;
				}
			}
			else if (!worldMouseBlocked && lastCell == cell && overGame && !brushIsActor && (Input.Mouse.LeftPressed || Input.Mouse.RightPressed))
			{
				tilePaintUndo?.Commit();
				tilePaintUndo = CaptureRoomUndo("Paint Tiles", editing);
				tilePlaceFrom = tileOver;
				tileTypePlacing = Input.Mouse.LeftPressed ? brush : '0';
				tilePlacing = true;
			}

			if (!worldMouseBlocked && actorSelectTool && draggingRoomActor && selectedRoomActorIndex >= 0 && Input.Mouse.LeftDown)
			{
				actorMoveUndo ??= CaptureRoomUndo("Move Actor", editing);
				MoveRoomActorTo(editing, selectedRoomActorIndex, worldMouse, tileOver, !Input.Keyboard.Shift);
			}
			if (!Input.Mouse.LeftDown)
			{
				draggingRoomActor = false;
				actorMoveUndo?.Commit();
				actorMoveUndo = null;
			}

			if (actorSelectTool && selectedRoomActorIndex >= 0 && (Input.Keyboard.Pressed(Keys.Delete) || Input.Keyboard.Pressed(Keys.Backspace)))
			{
				using (CaptureRoomUndo("Delete Actor", editing))
				{
					editing.Actors.RemoveAt(selectedRoomActorIndex);
					selectedRoomActorIndex = -1;
					draggingRoomActor = false;
					editorGameDirty = true;
				}
			}

			if (!Input.Mouse.RightDown)
				tileTypePlacing = brush;

			var shift = -(brushSize - 1) / 2;
			var size = brushSize;

			if (!worldMouseBlocked && !actorSelectTool && overGame)
			{
				if (eyedropper)
				{
					var sampleRect = ((new Rect(tileOver.X, tileOver.Y, 1, 1) * Game.TileSize) + roomTopLeft).Inflate(gridWeight * 2);
					DrawEditorEyedropperCursor(sampleRect, gridWeight);
				}
				else if (actorBrush is { } actorPreview)
				{
					var position = roomTopLeft + tileOver * Game.TileSize + actorPreview.Offset;
					batcher.Image(actorPreview.Image, position, actorPreview.Origin, Vector2.One, 0, Color.White);
				}
				else if (brushEntry is {} placing)
				{
					var cursorRect = ((new Rect(tileOver.X + shift, tileOver.Y + shift, size, size) * Game.TileSize) + roomTopLeft).Inflate(gridWeight * 2);
					var cursorColor = tileTypePlacing == '0' ? Color.Red : highlight;
					batcher.RectLine(cursorRect, gridWeight * 2, cursorColor);
					var position = roomTopLeft + tileOver * Game.TileSize + placing.Offset;

					for (int x = shift; x < shift + size; x ++)
						for (int y = shift; y < shift + size; y ++)
							batcher.Image(placing.Image, position + new Point2(x, y) * Game.TileSize, placing.Origin, Vector2.One, 0, Color.White);
				}
			}

			if (!worldMouseBlocked && !eyedropper && !brushIsActor && tilePlacing && (Input.Mouse.LeftDown || Input.Mouse.RightDown))
			{
				StampTiles(editing, tilePlaceFrom, shift, size, tileTypePlacing);

				while (tilePlaceFrom != tileOver)
				{
					var diff = tileOver - tilePlaceFrom;
					if (Math.Abs(diff.X) > Math.Abs(diff.Y))
						diff.Y = 0;
					else
						diff.X = 0;
					tilePlaceFrom.X += Math.Sign(diff.X);
					tilePlaceFrom.Y += Math.Sign(diff.Y);

					StampTiles(editing, tilePlaceFrom, shift, size, tileTypePlacing);
				}
			}

			batcher.RectLine(roomBounds, gridWeight * 2, Color.Black);
			DrawPlaytestPlayerMarker(editing);
			if (actorSelectTool)
				DrawRoomActorOverlays(editing, gridWeight, hoveredActorIndex, worldMouse);

			if (proposedRoomTiles is { } proposal)
			{
				var proposalBounds = TileRectToWorldRect(proposal);
				batcher.Rect(proposalBounds, Color.Gray * 0.35f);
				batcher.RectLine(proposalBounds, gridWeight * 2, Color.Gray);

				if (!worldMouseBlocked && inner.Contains(Cursor) && Input.Mouse.LeftPressed && !eyedropper)
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
			tilePaintUndo?.Commit();
			tilePaintUndo = null;
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
