#if EDITOR
using LinkDotNet.StringBuilder;
using System.Numerics;
using System.Text.Json.Nodes;
using Foster.Framework;
using ImGuiNET;

namespace Myth;

public partial class Manager
{
	const string LiveActorMoveUndoName = "Move Actor While Playing";
	const string LiveActorResizeUndoName = "Resize Actor While Playing";
	const string LiveActorDeleteUndoName = "Delete Actor While Playing";
	const float LiveActorDragThreshold = 4;
	const float LiveActorHandleRadius = 6;
	static float LiveActorDoubleClickSeconds => 0.35f;
	static float LiveActorDoubleClickRadius => 6;

	enum LiveActorDragKind
	{
		None,
		Move,
		Resize
	}

	static readonly ResizeHandle[] LiveActorResizeHandles =
	[
		ResizeHandle.TopLeft,
		ResizeHandle.TopRight,
		ResizeHandle.BottomLeft,
		ResizeHandle.BottomRight,
		ResizeHandle.Left,
		ResizeHandle.Right,
		ResizeHandle.Top,
		ResizeHandle.Bottom
	];

	string? liveSelectedActorId;
	string? liveHoveredActorId;
	string? liveInspectedActorId;
	bool liveActorInspectorOpen;
	Actor? livePropertyTarget;
	readonly Dictionary<string, JsonNode?> liveInspectorOriginalValues = [];
	readonly Dictionary<string, JsonNode?> liveInspectorEdits = [];
	string? liveActorLastClickId;
	TimeSpan liveActorLastClickTime;
	Vector2 liveActorLastClickCursor;
	LiveActorDragKind liveActorDragKind;
	ResizeHandle liveActorResizeHandle;
	Point2 liveActorStartPosition;
	Vector2 liveActorPressWorld;
	Vector2 liveActorPressCursor;
	bool liveActorDragStarted;
	bool liveActorSnapsByDefault;
	LiveActorUndoTarget? liveActorUndoTarget;
	JsonUndoScope? liveActorUndo;
	string? liveActorUndoBefore;

	bool LiveActorEditingWantsEscape => liveSelectedActorId != null || liveActorDragKind != LiveActorDragKind.None;

	bool UpdateLiveActorEditing(Game liveGame, bool inputBlocked)
	{
		if (AttractMode) // the attract demo isn't an editable session
			return false;

		if (liveTileBrushArmed)
			return true;

		if (!liveGame.AcceptsLiveInput || liveGame.CurrentRoom is not { } runtimeRoom)
		{
			EndLiveActorDrag(restore: false);
			liveHoveredActorId = null;
			liveSelectedActorId = null;
			return false;
		}

		if (!inputBlocked && Input.Keyboard.Pressed(Keys.Escape) && LiveActorEditingWantsEscape)
		{
			controls.Map.ConsumePress();
			if (liveActorDragKind != LiveActorDragKind.None)
				EndLiveActorDrag(restore: true);
			else
				liveSelectedActorId = null;
			liveHoveredActorId = null;
			return true;
		}

		if (!inputBlocked && UpdateLiveActorUndoShortcut())
			return true;

		if (!inputBlocked && liveActorDragKind == LiveActorDragKind.None && liveSelectedActorId != null &&
			(Input.Keyboard.Pressed(Keys.Backspace) || Input.Keyboard.Pressed(Keys.Delete)) &&
			TryDeleteSelectedLiveActor(liveGame, runtimeRoom))
			return true;

		// Once the player owns a press, keep routing subsequent frames to its drag path so a
		// release over an ImGui window cannot leave the camera stuck in dragging-player mode.
		if (liveGame.DraggingPlayer)
		{
			liveHoveredActorId = null;
			return false;
		}

		if (liveActorDragKind != LiveActorDragKind.None)
		{
			if (!Input.Mouse.LeftDown)
				EndLiveActorDrag(restore: false);
			else if (!inputBlocked)
				UpdateLiveActorDrag(liveGame, runtimeRoom);
			return true;
		}

		if (inputBlocked)
		{
			liveHoveredActorId = null;
			return true;
		}

		var viewport = GameViewRect;
		var overGame = viewport.Contains(Cursor) && !TweakRegistry.Hovered && (!imgui.WantsMouseInput || gameViewHovered);
		if (!overGame)
		{
			liveHoveredActorId = null;
			return false;
		}

		var worldMouse = GameMouseWorld(liveGame);
		var hovered = HitTestLiveActor(liveGame, runtimeRoom, worldMouse);
		liveHoveredActorId = hovered?.ActorId;
		var hoveredHandle = HitTestLiveActorResizeHandle(liveGame, runtimeRoom);
		SetHiddenWallResizeCursor(hoveredHandle);

		if (!Input.Mouse.LeftPressed)
			return false;

		// The player gets its own direct-grab path even when standing over an editable actor.
		if (liveGame.GetFirst<Player>() is { } player && player.GetVisualBounds().Contains(worldMouse))
		{
			liveHoveredActorId = null;
			return false;
		}

		var inspected = HitTestLiveActor(liveGame, runtimeRoom, worldMouse, positionEditableOnly: false);
		var clickTime = Input.Mouse.PressedTimestamp(MouseButtons.Left);
		var doubleClick = inspected?.ActorId is { } inspectedId && inspectedId == liveActorLastClickId &&
			(clickTime - liveActorLastClickTime).TotalSeconds <= LiveActorDoubleClickSeconds &&
			Vector2.DistanceSquared(Cursor, liveActorLastClickCursor) <= LiveActorDoubleClickRadius * LiveActorDoubleClickRadius;
		liveActorLastClickId = inspected?.ActorId;
		liveActorLastClickTime = clickTime;
		liveActorLastClickCursor = Cursor;
		if (doubleClick)
		{
			var inspectedActor = inspected!;
			if (Factory.LivePositionEditable(inspectedActor.GetType()))
				liveSelectedActorId = inspectedActor.ActorId;
			OpenLiveActorInspector(inspectedActor);
			return true;
		}

		if (hoveredHandle != ResizeHandle.None && TryGetSelectedLiveActor(liveGame, runtimeRoom, out var selected, out var selectedRoom, out var selectedJson))
		{
			BeginLiveActorDrag(selected, selectedRoom, selectedJson, worldMouse, LiveActorDragKind.Resize, hoveredHandle);
			return true;
		}

		if (hovered != null && TryGetLiveActorSource(liveGame, runtimeRoom, hovered, out var authoredRoom, out var authored))
		{
			liveSelectedActorId = hovered.ActorId;
			BeginLiveActorDrag(hovered, authoredRoom, authored, worldMouse, LiveActorDragKind.Move, ResizeHandle.None);
			return true;
		}

		liveSelectedActorId = null;
		return false;
	}

	void BeginLiveActorDrag(Actor runtime, Room authoredRoom, JsonObject authored, Vector2 worldMouse,
		LiveActorDragKind kind, ResizeHandle resizeHandle)
	{
		liveSelectedActorId = runtime.ActorId;
		liveActorDragKind = kind;
		liveActorResizeHandle = resizeHandle;
		liveActorStartPosition = ActorSpecs.ReadPoint(authored, "Position") ?? Point2.Zero;
		liveActorPressWorld = worldMouse;
		liveActorPressCursor = Cursor;
		liveActorDragStarted = false;
		liveActorSnapsByDefault = RoomActorPositionIsSnapped(authored);
		liveActorUndoTarget = new(this, authoredRoom.Id, runtime.ActorId!);
		liveActorUndoBefore = null;
		liveActorUndo = null;
	}

	void UpdateLiveActorDrag(Game liveGame, Room runtimeRoom)
	{
		if (!TryGetSelectedLiveActor(liveGame, runtimeRoom, out var runtime, out var authoredRoom, out var authored))
		{
			EndLiveActorDrag(restore: false);
			return;
		}

		if (!liveActorDragStarted)
		{
			if (Vector2.DistanceSquared(Cursor, liveActorPressCursor) <= LiveActorDragThreshold * LiveActorDragThreshold)
				return;

			liveActorDragStarted = true;
			liveActorUndoBefore = liveActorUndoTarget!.CaptureJson();
			liveActorUndo = undo.Capture(
				liveActorDragKind == LiveActorDragKind.Move ? LiveActorMoveUndoName : LiveActorResizeUndoName,
				liveActorUndoTarget);
			if (liveActorDragKind == LiveActorDragKind.Move)
				EditorFor(authored)?.BeforeMove(new EditorActor(authoredRoom, authored));
		}

		var worldMouse = GameMouseWorld(liveGame);
		if (liveActorDragKind == LiveActorDragKind.Move)
		{
			var targetLocal = (Vector2)liveActorStartPosition + worldMouse - liveActorPressWorld;
			var shiftDown = Input.Keyboard.Shift;
			var snap = liveActorSnapsByDefault ? !shiftDown : shiftDown;
			var local = snap
				? SnapRoomActorLocalPosition(targetLocal, GetRoomActorPaletteOffset(authored))
				: (Point2)targetLocal;
			ActorSpecs.SetProperty(authored, "Position", ActorSpecs.ToJsonNode(local));
		}
		else
		{
			var tile = ((worldMouse - (Vector2)authoredRoom.WorldBounds.TopLeft) / Game.TileSize).FloorToPoint2();
			ResizeRoomActor(authoredRoom, authored, liveActorResizeHandle, worldMouse, tile, !Input.Keyboard.Shift);
		}

		SyncLiveActorFromAuthored(runtime, authoredRoom, authored);
		editorGameDirty = true;
	}

	void EndLiveActorDrag(bool restore)
	{
		if (liveActorDragStarted && liveActorUndoTarget != null)
		{
			if (restore && liveActorUndoBefore != null)
				liveActorUndoTarget.ApplyJson(liveActorUndoBefore);
			liveActorUndo?.Dispose();

			if (!restore && Assets.GetRoom(liveActorUndoTarget.RoomId) is { } room)
			{
				room.Save();
				MarkRoomPreviewDirty(room);
			}
		}

		liveActorDragKind = LiveActorDragKind.None;
		liveActorResizeHandle = ResizeHandle.None;
		liveActorDragStarted = false;
		liveActorUndoTarget = null;
		liveActorUndo = null;
		liveActorUndoBefore = null;
	}

	// Removes the selected actor from the authored room and from the running game. Undo
	// restores both (see LiveActorDeleteUndoTarget), so the actor comes back alive.
	bool TryDeleteSelectedLiveActor(Game liveGame, Room runtimeRoom)
	{
		if (!TryGetSelectedLiveActor(liveGame, runtimeRoom, out var runtime, out var authoredRoom, out _) ||
			runtime.ActorId is not { } actorId)
			return false;

		var name = runtime.GetType().Name;
		using (undo.Capture(LiveActorDeleteUndoName, new LiveActorDeleteUndoTarget(this, authoredRoom.Id, actorId)))
		{
			var index = FindAuthoredActorIndex(authoredRoom, actorId);
			if (index < 0)
				return false;

			authoredRoom.Actors.RemoveAt(index);
			liveGame.Destroy(runtime);
			authoredRoom.Save();
			MarkRoomPreviewDirty(authoredRoom);
			editorGameDirty = true;
		}

		liveSelectedActorId = null;
		liveHoveredActorId = null;
		DisplayMessage($"Deleted {name}");
		return true;
	}

	// Re-creates an authored actor in the running game, for delete-undo. No-op unless the
	// room it belongs to is the one being played.
	void RespawnLiveActor(Room room, JsonObject authored)
	{
		if (game?.CurrentRoom is not { } current || current.Id != room.Id)
			return;

		var local = ActorSpecs.ReadResolvedPosition(authored) ?? Point2.Zero;
		ActorSpecs.Create(game, authored, current.WorldBounds.TopLeft + local);
	}

	void DestroyLiveActor(string actorId)
	{
		if (game?.GetActorById(actorId) is { } runtime)
			game.Destroy(runtime);
	}

	void CancelLiveActorEditing(bool restoreDrag = true)
	{
		EndLiveActorDrag(restoreDrag);
		liveSelectedActorId = null;
		liveHoveredActorId = null;
		liveInspectedActorId = null;
		liveActorInspectorOpen = false;
		livePropertyTarget = null;
		liveInspectorOriginalValues.Clear();
		liveInspectorEdits.Clear();
		liveEditLedger.Clear();
		liveActorLastClickId = null;
		liveActorLastClickTime = default;
		liveActorLastClickCursor = default;
	}

	void OpenLiveActorInspector(Actor actor)
	{
		if (liveInspectedActorId != actor.ActorId)
		{
			// Keep the previous actor's unsaved edits (live on the runtime and in the ledger) instead
			// of reverting -- the save-review modal is where they get kept or dropped.
			StashInspectedLiveEdits();
			liveInspectedActorId = actor.ActorId;
			CaptureLiveInspectorValues(actor);
			RestoreInspectedLiveEdits(actor);
		}
		liveActorInspectorOpen = true;
	}

	void CaptureLiveInspectorValues(Actor actor)
	{
		liveInspectorOriginalValues.Clear();
		liveInspectorEdits.Clear();
		foreach (var property in ActorSpecs.GetEditableProperties(actor))
			liveInspectorOriginalValues[property.Name] = property.Value?.DeepClone();
	}

	bool UpdateLiveActorUndoShortcut()
	{
		if (!Input.Keyboard.CtrlOrCommand)
			return false;

		var redo = Input.Keyboard.Shift;
		if (Input.Keyboard.Pressed(Keys.Z))
		{
			var name = redo ? undo.RedoName : undo.UndoName;
			if (name is not LiveActorMoveUndoName and not LiveActorResizeUndoName and not LiveActorDeleteUndoName)
				return false;
			if (redo) undo.Redo(); else undo.Undo();
			DisplayMessage(redo ? "Redid live actor edit" : "Undid live actor edit");
			return true;
		}

		if (Input.Keyboard.Pressed(Keys.Y) && undo.RedoName is LiveActorMoveUndoName or LiveActorResizeUndoName or LiveActorDeleteUndoName)
		{
			undo.Redo();
			DisplayMessage("Redid live actor edit");
			return true;
		}

		return false;
	}

	Actor? HitTestLiveActor(Game liveGame, Room runtimeRoom, Vector2 world, bool positionEditableOnly = true)
	{
		Actor? best = null;
		long bestArea = long.MaxValue;
		for (var i = liveGame.Actors.Count - 1; i >= 0; i--)
		{
			var candidate = liveGame.Actors[i];
			if (!TryGetLiveActorSource(liveGame, runtimeRoom, candidate, positionEditableOnly, out var room, out var actor))
				continue;
			var bounds = LiveActorBounds(candidate, room, actor);
			if (!bounds.Contains(world))
				continue;
			var area = (long)bounds.Width * bounds.Height;
			if (area < bestArea || area == bestArea && (best == null || candidate.Depth < best.Depth))
			{
				best = candidate;
				bestArea = area;
			}
		}

		// Keep the current selection when it is at least as specific as the best hit. A light
		// inside a room-sized Wind/CameraBlocker still wins over that large selected region.
		if (liveSelectedActorId is { } selectedId && liveGame.GetActorById(selectedId) is { } selected &&
			TryGetLiveActorSource(liveGame, runtimeRoom, selected, positionEditableOnly, out var selectedRoom, out var selectedJson))
		{
			var bounds = LiveActorBounds(selected, selectedRoom, selectedJson);
			if (bounds.Contains(world) && (long)bounds.Width * bounds.Height <= bestArea)
				return selected;
		}
		return best;
	}

	bool TryGetSelectedLiveActor(Game liveGame, Room runtimeRoom, out Actor runtime, out Room authoredRoom, out JsonObject authored)
	{
		runtime = liveSelectedActorId is { } id ? liveGame.GetActorById(id)! : null!;
		authoredRoom = null!;
		authored = null!;
		return runtime != null && TryGetLiveActorSource(liveGame, runtimeRoom, runtime, out authoredRoom, out authored);
	}

	static bool TryGetLiveActorSource(Game liveGame, Room runtimeRoom, Actor runtime,
		out Room authoredRoom, out JsonObject authored)
		=> TryGetLiveActorSource(liveGame, runtimeRoom, runtime, positionEditableOnly: true, out authoredRoom, out authored);

	static bool TryGetLiveActorSource(Game liveGame, Room runtimeRoom, Actor runtime, bool positionEditableOnly,
		out Room authoredRoom, out JsonObject authored)
	{
		authoredRoom = null!;
		authored = null!;

		if (runtime.editorFlags.Has(Actor.EditorFlags.Locked))
			return false;

		if (runtime.ActorId is not { } id || runtime.Room?.Id != runtimeRoom.Id ||
			liveGame.destroying.Contains(runtime) || positionEditableOnly && !Factory.LivePositionEditable(runtime.GetType()) ||
			Assets.GetRoom(runtimeRoom.Id) is not { } room)
			return false;

		var json = runtime.AuthoredSpec;
		if (json == null || !room.Actors.Contains(json))
		{
			json = FindAuthoredActor(room, id);
			if (json == null)
				return false;
			runtime.AuthoredSpec = json;
		}

		authoredRoom = room;
		authored = json;
		return true;
	}

	static JsonObject? FindAuthoredActor(Room room, string id)
	{
		var index = FindAuthoredActorIndex(room, id);
		return index >= 0 ? room.Actors[index] : null;
	}

	static int FindAuthoredActorIndex(Room room, string id)
	{
		for (var i = 0; i < room.Actors.Count; i++)
			if (string.Equals(ActorSpecs.GetActorId(room.Actors[i]), id, StringComparison.OrdinalIgnoreCase))
				return i;
		return -1;
	}

	static RectInt LiveActorBounds(Actor runtime, Room authoredRoom, JsonObject authored)
	{
		if (runtime is RigPuppet)
		{
			var bounds = runtime.GetVisualBounds();
			return RectInt.Between(bounds.TopLeft.FloorToPoint2(), -(-bounds.BottomRight).FloorToPoint2());
		}
		var authoredPosition = authoredRoom.WorldBounds.TopLeft + (ActorSpecs.ReadPoint(authored, "Position") ?? Point2.Zero);
		return RoomActorBounds(authoredRoom, authored) + runtime.Position - authoredPosition;
	}

	static void SyncLiveActorFromAuthored(Actor runtime, Room authoredRoom, JsonObject authored)
	{
		var local = ActorSpecs.ReadPoint(authored, "Position") ?? Point2.Zero;
		runtime.Position = (runtime.Room?.WorldBounds.TopLeft ?? authoredRoom.WorldBounds.TopLeft) + local;
		runtime.Velocity = Vector2.Zero;
		if (runtime is IResizable resizable && ActorSpecs.ReadResolvedPoint(authored, nameof(IResizable.Size)) is { } size)
			resizable.Size = size;
	}

	ResizeHandle HitTestLiveActorResizeHandle(Game liveGame, Room runtimeRoom)
	{
		if (!TryGetSelectedLiveActor(liveGame, runtimeRoom, out var runtime, out var room, out var authored) ||
			!Factory.LiveResizable(runtime.GetType()))
			return ResizeHandle.None;

		var bounds = LiveActorBounds(runtime, room, authored);
		var axes = ResizableRoomRectInfo(authored).Axes;
		foreach (var handle in LiveActorResizeHandles)
			if (ResizeHandleAllowed(handle, axes) && LiveActorHandleScreenRect(bounds, handle).Contains(Cursor))
				return handle;
		return ResizeHandle.None;
	}

	Rect LiveActorHandleScreenRect(RectInt bounds, ResizeHandle handle)
	{
		var center = GameWorldToImGui(GameViewRect, game!, LiveActorHandleWorldPosition(bounds, handle));
		return new(center - Vector2.One * LiveActorHandleRadius, Vector2.One * LiveActorHandleRadius * 2);
	}

	static Vector2 LiveActorHandleWorldPosition(RectInt bounds, ResizeHandle handle)
		=> new(
			handle is ResizeHandle.Left or ResizeHandle.TopLeft or ResizeHandle.BottomLeft ? bounds.Left :
			handle is ResizeHandle.Right or ResizeHandle.TopRight or ResizeHandle.BottomRight ? bounds.Right : bounds.Center.X,
			handle is ResizeHandle.Top or ResizeHandle.TopLeft or ResizeHandle.TopRight ? bounds.Top :
			handle is ResizeHandle.Bottom or ResizeHandle.BottomLeft or ResizeHandle.BottomRight ? bounds.Bottom : bounds.Center.Y);

	void LiveActorEditingImGui()
	{
		if (!playing || AttractMode || liveTileBrushArmed || Input.Keyboard.Alt || game?.CurrentRoom is not { } runtimeRoom)
			return;

		var selected = liveSelectedActorId is { } selectedId ? game.GetActorById(selectedId) : null;
		Room selectedRoom = null!;
		JsonObject selectedJson = null!;
		var hasSelected = selected != null && TryGetLiveActorSource(game, runtimeRoom, selected,
			positionEditableOnly: false, out selectedRoom, out selectedJson);
		var hovered = liveHoveredActorId is { } hoveredId ? game.GetActorById(hoveredId) : null;
		Room hoveredRoom = null!;
		JsonObject hoveredJson = null!;
		var hasHovered = hovered != null && TryGetLiveActorSource(game, runtimeRoom, hovered, out hoveredRoom, out hoveredJson);
		if (!hasSelected && !hasHovered)
			return;

		var viewport = GameViewRect;
		var draw = ImGui.GetForegroundDrawList();
		draw.PushClipRect(viewport.TopLeft, viewport.BottomRight, true);

		if (hasHovered && hovered != selected)
			DrawLiveActorBounds(draw, viewport, game, LiveActorBounds(hovered!, hoveredRoom, hoveredJson), selected: false);

		if (hasSelected)
		{
			var bounds = LiveActorBounds(selected!, selectedRoom, selectedJson);
			DrawLiveActorBounds(draw, viewport, game, bounds, selected: true);
			if (Factory.LiveResizable(selected!.GetType()))
			{
				var axes = ResizableRoomRectInfo(selectedJson).Axes;
				foreach (var handle in LiveActorResizeHandles)
				{
					if (!ResizeHandleAllowed(handle, axes))
						continue;
					var rect = LiveActorHandleScreenRect(bounds, handle);
					var active = liveActorDragKind == LiveActorDragKind.Resize && liveActorResizeHandle == handle;
					draw.AddRectFilled(rect.TopLeft, rect.BottomRight,
						active ? ImGuiColor(1, 1, 1, 0.95f) : ImGuiColor(0.08f, 0.12f, 0.10f, 0.9f));
					draw.AddRect(rect.TopLeft, rect.BottomRight, ImGuiColor(0.45f, 1, 0.72f, 0.95f), 1, ImDrawFlags.None, 1.5f);
				}
			}
		}

		draw.PopClipRect();

		var status = hasHovered ? hovered! : selected!;
		var statusRoom = hasHovered ? hoveredRoom : selectedRoom;
		var local = status.Position - statusRoom.WorldBounds.TopLeft;
		var label = new ValueStringBuilder(stackalloc char[128]);
		label.Append(status.GetType().Name);
		label.Append("  ·  ");
		label.Append(status.ActorId);
		label.Append("  ·  (");
		label.Append(local.X);
		label.Append(", ");
		label.Append(local.Y);
		label.Append(')');
		DebugOverlayLabelImGui(
			"LiveActorEditingStatus",
			viewport.TopLeft + new Vector2(viewport.Width * 0.5f, viewport.Height - 6),
			label.AsSpan(),
			viewport);
		label.Dispose();
	}

	void LiveStatusImGui()
	{
		if (!playing || liveSelectedActorId is not { } id || game?.GetActorById(id) is not ILiveStatus status)
			return;

		DockNextToolWindow("Live Status");
		ImGui.SetNextWindowSize(new Vector2(360, 420), ImGuiCond.FirstUseEver);
		if (ImGui.Begin("Live Status"))
		{
			NoteDockedWindow();
			status.LiveStatusImGui();
		}
		ImGui.End();
	}

	static void DrawLiveActorBounds(ImDrawListPtr draw, Rect viewport, Game game, RectInt bounds, bool selected)
	{
		var topLeft = GameWorldToImGui(viewport, game, bounds.TopLeft);
		var bottomRight = GameWorldToImGui(viewport, game, bounds.BottomRight);
		var color = selected ? ImGuiColor(0.45f, 1, 0.72f, 0.45f) : ImGuiColor(1, 1, 1, 0.22f);
		//if (selected)
			//draw.AddRectFilled(topLeft, bottomRight, ImGuiColor(0.35f, 1, 0.62f, 0.05f));
		draw.AddRect(topLeft, bottomRight, color, 0, ImDrawFlags.None, selected ? 2 : 1.25f);
	}

	// Whole-actor presence undo: captures whether the actor is in the room, where in the list,
	// and its full spec, so undo/redo can put it back (in the room file and in the running
	// game) or take it away again.
	sealed class LiveActorDeleteUndoTarget(Manager manager, string roomId, string actorId) : IJsonUndoTarget
	{
		public string UndoId => $"live-actor-delete:{roomId}:{actorId}";

		public string CaptureJson()
		{
			var room = Assets.GetRoom(roomId);
			var index = room != null ? FindAuthoredActorIndex(room, actorId) : -1;
			return ActorSpecs.ToJsonString(new JsonObject
			{
				["present"] = index >= 0,
				["index"] = index,
				["actor"] = index >= 0 ? room!.Actors[index].DeepClone() : null
			});
		}

		public void ApplyJson(string json)
		{
			var state = JsonNode.Parse(json)?.AsObject();
			if (state == null || Assets.GetRoom(roomId) is not { } room)
				return;

			var existing = FindAuthoredActorIndex(room, actorId);
			if (JsonUtil.ReadBool(state, "present"))
			{
				if (existing >= 0 || state["actor"] is not JsonObject stored)
					return;

				var restored = stored.DeepClone().AsObject();
				room.Actors.Insert(Math.Clamp(JsonUtil.ReadInt(state, "index", room.Actors.Count), 0, room.Actors.Count), restored);
				manager.RespawnLiveActor(room, restored);
			}
			else
			{
				if (existing < 0)
					return;

				room.Actors.RemoveAt(existing);
				manager.DestroyLiveActor(actorId);
			}

			room.Save();
			manager.MarkRoomPreviewDirty(room);
			manager.editorGameDirty = true;
		}
	}

	sealed class LiveActorUndoTarget(Manager manager, string roomId, string actorId) : IJsonUndoTarget
	{
		public string RoomId => roomId;
		public string UndoId => $"live-actor:{roomId}:{actorId}";

		public string CaptureJson()
		{
			var authored = Assets.GetRoom(roomId) is { } room ? FindAuthoredActor(room, actorId) : null;
			var runtime = manager.game?.GetActorById(actorId);
			JsonNode? position = null;
			JsonNode? size = null;
			var hasPosition = authored != null && JsonUtil.TryGetProperty(authored, "Position", out position);
			var hasSize = authored != null && JsonUtil.TryGetProperty(authored, "Size", out size);
			return ActorSpecs.ToJsonString(new JsonObject
			{
				["hasPosition"] = hasPosition,
				["position"] = hasPosition ? position?.DeepClone() : null,
				["hasSize"] = hasSize,
				["size"] = hasSize ? size?.DeepClone() : null,
				["runtimePosition"] = runtime != null ? ActorSpecs.ToJsonNode(runtime.Position) : null,
				["runtimeSize"] = runtime is IResizable resizable ? ActorSpecs.ToJsonNode(resizable.Size) : null
			});
		}

		public void ApplyJson(string json)
		{
			var state = JsonNode.Parse(json)?.AsObject();
			if (state == null || Assets.GetRoom(roomId) is not { } room || FindAuthoredActor(room, actorId) is not { } authored)
				return;

			ApplyProperty(authored, "Position", JsonUtil.ReadBool(state, "hasPosition"), state["position"]);
			ApplyProperty(authored, "Size", JsonUtil.ReadBool(state, "hasSize"), state["size"]);
			room.Save();
			manager.MarkRoomPreviewDirty(room);
			manager.editorGameDirty = true;

			if (manager.game?.GetActorById(actorId) is { } runtime)
			{
				if (ActorSpecs.ReadPoint(state["runtimePosition"]) is { } position)
					runtime.Position = position;
				if (runtime is IResizable resizable && ActorSpecs.ReadPoint(state["runtimeSize"]) is { } size)
					resizable.Size = size;
				runtime.Velocity = Vector2.Zero;
			}
		}

		static void ApplyProperty(JsonObject actor, string name, bool present, JsonNode? value)
		{
			if (present)
				ActorSpecs.SetProperty(actor, name, value?.DeepClone());
			else
				JsonUtil.RemoveProperty(actor, name);
		}
	}
}
#endif
