#if EDITOR
using LinkDotNet.StringBuilder; // Namespace of the package
using System.Numerics;
using System.Reflection;
using System.Text.Json.Nodes;
using Foster.Editor;
using Foster.Framework;
using ImGuiNET;

namespace Myth;

public partial class Manager
{

	enum EditorScrollMode
	{
		Auto,
		Mouse,
		Trackpad
	}

	static Vector2 ButtonSizeV = new(ButtonSize, ButtonSize);
	static float BoxSpacing => 0;
	static float BoxPadding => 4;
	static int PaletteColumns => 5;
	static int ToolbarSpacing => 4;
	static float ToolbarHeight => ButtonSize + 4;
	static float ToolbarButtonImagePadding => 4;
	static float PaletteWidth => BoxPadding * 2 + PaletteColumns * ButtonSize + (PaletteColumns - 1) * ButtonSpacing;
	static Color highlight => 0x5fcde4;


	bool editorGameDirty;
	bool editorViewInitialized;
	bool showFungalMarkers;
	Vector2 editorCamera;
	float editorZoom = 1.0f;
	float editorZoomTarget = 1.0f;
	const bool AutoSelectRoomByCamera = true;
	Vector2 editorCameraTweenFrom;
	Vector2 editorCameraTweenTo;
	float editorZoomTweenFrom;
	float editorZoomTweenTo;
	float editorCameraTween = 1.0f;

	// Seconds left where wheel events are treated as a trackpad two-finger swipe (pan) rather than a
	// mouse wheel (zoom). A trackpad sends precise/fractional deltas; we latch on those so the stray
	// whole-number crossing mid-swipe doesn't blip a zoom. See UpdateEditorWorldView.
	float trackpadScrollLatch;
	EditorScrollMode editorScrollMode;
	bool preferencesOpen;
	static float TrackpadScrollLatchTime => 0.5f;
	// World pixels panned per unit of (small, ~0.1-0.4) trackpad wheel delta, before the zoom divide.
	static float EditorTrackpadPanSpeed => 20f;

	char brush = '0'; // The current Type we're painting in the Editor
	Factory.TileLayer activeTileLayer = Factory.TileLayer.Foreground;
	bool editorShowForegroundLayer = true;
	bool editorShowBackgroundLayer = true;

	char tileTypePlacing;
	bool editingRoomGeometry;
	bool slopePaint; // when set, a tile-rect drag lays a slope staircase instead of a solid block
	Point2 tileRectPaintFrom;
	Point2 tileRectPaintTo;
	RectInt? proposedRoomTiles;
	// Snapshot of the proposal the context menu was opened on: proposedRoomTiles follows the
	// cursor and goes null once ImGui takes the mouse, so the menu needs its own copy.
	RectInt? pendingCreateRoomTiles;
	bool createRoomPopupRequested;
	string? selectedActorSpecId;
	int selectedActorPaletteIndex = -1;
	ActorTag actorPaletteFilter = ActorTag.None; // None = show all
	bool actorSpecInspectorOpen;
	bool actorSelectTool = true;
	int selectedRoomActorIndex = -1;
	// Click-to-cycle picking: a click that doesn't drag walks down the stack of overlapping actors
	// so hidden ones are reachable. Scratch + press state drive it.
	readonly List<int> actorPickScratch = [];
	Vector2 actorPressCursor;
	bool actorPressCanCycle;
	const float ActorPickCycleThreshold = 4f; // UI pixels: a release within this of the press is a click, not a drag
	bool selectionIncludeBackgroundTiles = true;
	bool selectionIncludeForegroundTiles = true;
	SnapshotFlags selectionSnapshotFlags = SnapshotFlags.None;
	bool selectionIncludeActors = true;
	RectInt? editorSelectionRect;
	Point2 editorSelectionFrom;
	Point2 editorSelectionTo;
	EditorDragState editorDrag = EditorDragState.None;
	string? pickingLinkedDoorTargetForActorId;
	// Neural texture mask painting: which room actor is being painted, and the brush.
	string? maskPaintActorId;
	int maskPaintSlot;
	float maskPaintRadius = 24.0f;
	float maskPaintSoftness = 0.4f;
	float maskPaintStrength = 1.0f;
	int maskPaintPreviewSteps = 128;
	bool maskPaintStroke;
	readonly JsonUndoManager undo = new();
	// Mutable struct on purpose: methods called through this field update its state in place.
	// Assigned in the Manager constructor (it needs `undo`).
	ParticleEffectEditor particleEditor;
	readonly Dictionary<Cursor.SystemTypes, Cursor> editorCursors = [];
	RoomSettings? copiedRoomSettings;
	JsonObject? actorClipboard;
	bool actorClipboardPreservesId;
	// Whole-room clipboard (serialized room state), distinct from copiedRoomSettings which is settings-only.
	string? roomClipboardJson;


	Point2 roomCreatePosition;
	Point2? editorRoomEntryDirection;
	bool editingRoomName;
	bool focusRoomNameEditor;
	string roomNameEditor = string.Empty;
	string? roomNameEditingRoomId;
	string? pendingDeleteRoomId;

	/// Toolbar Space
	Rect ToolbarRect => new(View.X, View.Y, View.Width, ToolbarHeight);

	/// Workspace, which is the full space below the Toolbar
	Rect WorkRect => new(View.X, View.Y + ToolbarSpacing + ToolbarHeight, View.Width, View.Height - ToolbarHeight - ToolbarSpacing);

	/// Palette View when the Editor is Open
	Rect PaletteOpenRect => new(WorkRect.X, WorkRect.Y, PaletteWidth, WorkRect.Height);

	/// Edit Workspace when the Editor is Open
	Rect EditorOpenRect
		=> new Rect(WorkRect.X + PaletteWidth + BoxSpacing, WorkRect.Y, WorkRect.Width - PaletteWidth - BoxSpacing, WorkRect.Height);

	/// Eases between the EditView and the PlayView as the Game opens/closes
	Rect WorkspaceRect
	{
		get
		{
			var a = Vector2.Lerp(EditorOpenRect.TopLeft, GameOpenRect.TopLeft, Ease.Cube.InOut(gameEase));
			var b = Vector2.Lerp(EditorOpenRect.BottomRight, GameOpenRect.BottomRight, Ease.Cube.InOut(gameEase));
			return Rect.Between(a, b);
		}
	}

	Rect GameViewRect => WorkspaceRect;

	IJsonUndoTarget CurrentRoomUndoTarget(Room room)
		=> new RoomUndoTarget(room);

	IJsonUndoTarget EditorSelectionUndoTarget()
		=> new EditorSelectionTarget(this);

	JsonUndoScope CaptureRoomUndo(string name, Room room)
		=> undo.Capture(name, CurrentRoomUndoTarget(room), EditorSelectionUndoTarget());

	JsonUndoScope CaptureSelectionUndo(string name)
		=> undo.Capture(name, EditorSelectionUndoTarget());

	// Pairs room existence with the editor selection so undo restores both the room and what was selected.
	JsonUndoScope CaptureRoomExistenceUndo(string name, Room room)
		=> undo.Capture(name, new RoomExistenceUndoTarget(this, room), EditorSelectionUndoTarget());

	JsonUndoScope CapturePrefabUndo(string name, string specId)
		=> undo.Capture(name, new ActorSpecUndoTarget(specId));

	// Pending-scope slots (see JsonUndoManager.OpenPending): each multi-frame editor gesture
	// opens its scope under one of these on its first change and commits it when the gesture ends.
	const string RoomGeometryUndoSlot = "room-geometry";
	const string ActorMoveUndoSlot = "actor-move";
	const string TilePaintUndoSlot = "tile-paint";
	const string PrefabUndoSlot = "prefab";
	const string RoomSettingsUndoSlot = "room-settings";
	const string ActorPropertyUndoSlot = "actor-property";

	// The HasPending guard keeps repeat calls during a drag from re-allocating the targets.
	void OpenPendingRoomUndo(string slot, string name, Room room)
	{
		if (!undo.HasPending(slot))
			undo.OpenPending(slot, name, CurrentRoomUndoTarget(room), EditorSelectionUndoTarget());
	}

	// Inspector gestures (room-settings sliders, actor-property drags, text fields) each hold
	// one pending scope so a whole drag or typing session becomes a single undo entry. They
	// have no single deactivation point we can observe (popup color pickers, sections that can
	// collapse mid-gesture), so EditorUI commits them once no ImGui item is active anywhere.
	void OpenRoomSettingsUndo(string name, Room room)
	{
		if (!undo.HasPending(RoomSettingsUndoSlot, room.Id))
			undo.OpenPending(RoomSettingsUndoSlot, room.Id, name, CurrentRoomUndoTarget(room), EditorSelectionUndoTarget());
	}

	void OpenActorPropertyUndo(Room room, JsonObject actor)
	{
		var id = ActorSpecs.GetActorId(actor) ?? "";
		if (!undo.HasPending(ActorPropertyUndoSlot, id))
			undo.OpenPending(ActorPropertyUndoSlot, id, "Edit Actor Property", CurrentRoomUndoTarget(room), EditorSelectionUndoTarget());
	}

	internal void EditorUI()
	{
		// End-of-gesture commit for the inspector undo scopes (see OpenRoomSettingsUndo).
		if (!ImGui.IsAnyItemActive())
		{
			undo.CommitPending(RoomSettingsUndoSlot);
			undo.CommitPending(ActorPropertyUndoSlot);
		}

		if (gameEase < 1.0f && !startingPlay)
		{
			using (PerfStats.Time(PerfDetail.PaletteImGui))
				PaletteImGui();
		}
		if (!playing && !startingPlay)
		{
			using (PerfStats.Time(PerfDetail.ToolbarImGui))
				ToolbarImGui();
		}
		PlatformNavDebugOverlayImGui();
		BudDebugOverlayImGui();
		using (PerfStats.Time(PerfDetail.FpsOverlayImGui))
			FpsOverlayImGui();
		TransitionViewerImGui();
		PreferencesImGui();
		particleEditor.PinnedWindowImGui(game, Scale);
		if (!playing && !startingPlay)
		{
			TileKitEditorImGui();
			NctTileEditorImGui();
			MusicRegionsImGui();
		}
#if DEBUG
		TweakRegistry.DrawPanel();
#endif
	}

#if EDITOR
	void EnteringEditor() {
		// it was annoying that session destroyed enemies would stay disappeared while editing. but maybe this logic is better on loading the room?
		if (game != null)
		{
			game.Save.Session.DestroyedActors.Clear();
		}
		MarkSaveDirty();
		FlushSaveState();
	}
#endif


	/// Eases the PaletteView out/in as the Game opens/closes
	Rect PaletteSlideRect =>
		PaletteOpenRect - Vector2.UnitX * Ease.Cube.InOut(gameEase) * PaletteOpenRect.Right;

	void ClearEditorStateForEnteringPlayMode()
	{
		editingRoomGeometry = false;
	}

	void UndoEditor()
	{
		undo.Undo();
		AfterUndoRedo();
	}

	void RedoEditor()
	{
		undo.Redo();
		AfterUndoRedo();
	}

	void DuplicateSelectedActor()
	{
		if (CurrentRoom is not { } room || selectedRoomActorIndex < 0 || selectedRoomActorIndex >= room.Actors.Count)
			return;

		using (CaptureRoomUndo("Duplicate Actor", room))
		{
			var copy = ActorSpecs.CloneObject(room.Actors[selectedRoomActorIndex]);
			var position = ActorSpecs.ReadPoint(copy, "Position") ?? Point2.Zero;
			copy["Position"] = new JsonArray(position.X + Game.TileSize, position.Y);
			ActorSpecs.AssignNewActorId(copy);
			room.Actors.Add(copy);
			selectedRoomActorIndex = room.Actors.Count - 1;
			ClearEditorDragState();
			editorGameDirty = true;
		}
	}

	void CopySelectedActor(bool cut)
	{
		if (CurrentRoom is not { } room || selectedRoomActorIndex < 0 || selectedRoomActorIndex >= room.Actors.Count)
			return;

		actorClipboard = ActorSpecs.CloneObject(room.Actors[selectedRoomActorIndex]);
		actorClipboardPreservesId = cut;
		if (!cut)
			return;

		using (CaptureRoomUndo("Cut Actor", room))
		{
			room.Actors.RemoveAt(selectedRoomActorIndex);
			selectedRoomActorIndex = -1;
			ClearEditorDragState();
			editorSelectionRect = null;
			editorGameDirty = true;
		}
	}

	void PasteActor()
	{
		if (CurrentRoom is not { } room || actorClipboard == null)
			return;

		using (CaptureRoomUndo("Paste Actor", room))
		{
			var actor = ActorSpecs.CloneObject(actorClipboard);
			var id = ActorSpecs.GetActorId(actor);
			if (!actorClipboardPreservesId || string.IsNullOrWhiteSpace(id) || Assets.FindActorById(id) != null)
				ActorSpecs.AssignNewActorId(actor);
			room.Actors.Add(actor);
			selectedRoomActorIndex = room.Actors.Count - 1;
			actorSelectTool = true;
			selectedActorPaletteIndex = -1;
			ClearEditorDragState();
			editorSelectionRect = null;
			editorGameDirty = true;
		}
	}

	void AfterUndoRedo()
	{
		if (CurrentRoom is { } room)
		{
			if (selectedRoomActorIndex >= room.Actors.Count)
				selectedRoomActorIndex = -1;
			room.Save();
			MarkRoomPreviewDirty(room);
		}

		// Pending undo scopes were abandoned by JsonUndoManager.Undo/Redo; only UI state is ours.
		ClearEditorDragState();
		CancelTilePaint();
		editorSelectionRect = null;
		SyncEditorGame();
	}

	bool UpdateEditorShortcuts()
	{
		if (OperatingSystem.IsMacOS() && Input.Keyboard.CtrlOrCommand && Input.Keyboard.Pressed(Keys.Comma))
		{
			preferencesOpen = true;
			return true;
		}
		if (ImGuiWantsKeyboard)
			return false;

		if (Input.Keyboard.Pressed(Keys.Escape))
		{
			SelectEditorActorTool();
		}

		var modifier =
			Input.Keyboard.Down(Keys.LeftControl) ||
			Input.Keyboard.Down(Keys.RightControl) ||
			Input.Keyboard.Down(Keys.LeftOS) ||
			Input.Keyboard.Down(Keys.RightOS);
		if (!modifier)
			return false;

		// In room-edit mode the clipboard operates on whole rooms; otherwise on the selected actor.
		if (editingRoomGeometry)
		{
			if (Input.Keyboard.Pressed(Keys.C))
			{
				CopyCurrentRoom();
				return true;
			}

			if (Input.Keyboard.Pressed(Keys.X))
			{
				CutCurrentRoom();
				return true;
			}

			if (Input.Keyboard.Pressed(Keys.V))
			{
				PasteRoomAtCursor();
				return true;
			}

			if (Input.Keyboard.Pressed(Keys.D))
			{
				DuplicateCurrentRoom();
				return true;
			}
		}
		else
		{
			if (Input.Keyboard.Pressed(Keys.C))
			{
				CopySelectedActor(cut: false);
				return true;
			}

			if (Input.Keyboard.Pressed(Keys.X))
			{
				CopySelectedActor(cut: true);
				return true;
			}

			if (Input.Keyboard.Pressed(Keys.V))
			{
				PasteActor();
				return true;
			}

			if (Input.Keyboard.Pressed(Keys.D))
			{
				DuplicateSelectedActor();
				return true;
			}
		}

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

	sealed class RoomUndoTarget(Room room) : IJsonUndoTarget
	{
		public string UndoId => $"room:{room.Id}";
		public string CaptureJson() => room.ToJsonString();
		public void ApplyJson(string json)
		{
			var oldCell = room.Cell;
			room.LoadJsonState(json);
			UpdateRoomCellIndex(room, oldCell);
		}
	}

	sealed class EditorSelectionTarget(Manager manager) : IJsonUndoTarget
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
				["tileLayer"] = manager.activeTileLayer.ToString(),
				["showForegroundLayer"] = manager.editorShowForegroundLayer,
				["showBackgroundLayer"] = manager.editorShowBackgroundLayer,
				["actorPaletteIndex"] = manager.selectedActorPaletteIndex,
				["actorPaletteFilter"] = (int)manager.actorPaletteFilter,
				["prefabId"] = manager.selectedActorSpecId,
				["prefabInspectorOpen"] = manager.actorSpecInspectorOpen,
				["entryDirection"] = manager.editorRoomEntryDirection is { } direction ? new JsonArray(direction.X, direction.Y) : null
			};
			return ActorSpecs.ToJsonString(json);
		}

		public void ApplyJson(string jsonText)
		{
			var json = JsonNode.Parse(jsonText)?.AsObject() ?? [];
			var roomId = JsonUtil.ReadString(json, "roomId");
			if (roomId != null && Assets.GetRoom(roomId) is { } room)
				manager.SelectRoom(room);

			manager.selectedRoomActorIndex = JsonUtil.ReadInt(json, "actorIndex", -1);
			manager.actorSelectTool = JsonUtil.ReadBool(json, "actorSelectTool");
			if (JsonUtil.ReadString(json, "brush") is { Length: > 0 } brush)
				manager.brush = brush[0];
			if (Enum.TryParse<Factory.TileLayer>(JsonUtil.ReadString(json, "tileLayer"), out var layer))
				manager.activeTileLayer = layer;
			manager.editorShowForegroundLayer = JsonUtil.ReadBool(json, "showForegroundLayer", true);
			manager.editorShowBackgroundLayer = JsonUtil.ReadBool(json, "showBackgroundLayer", true);
			manager.selectedActorPaletteIndex = JsonUtil.ReadInt(json, "actorPaletteIndex", -1);
			manager.actorPaletteFilter = (ActorTag)JsonUtil.ReadInt(json, "actorPaletteFilter", 0);
			manager.selectedActorSpecId = JsonUtil.ReadString(json, "prefabId");
			manager.actorSpecInspectorOpen = JsonUtil.ReadBool(json, "prefabInspectorOpen");
			manager.editorRoomEntryDirection = JsonUtil.ReadPoint(json, "entryDirection");
		}
	}

	sealed class ActorSpecUndoTarget(string specId) : IJsonUndoTarget
	{
		public string UndoId => $"actor-spec:{specId}";
		public string CaptureJson() => ActorSpecs.CaptureSpecJson(specId);
		public void ApplyJson(string json) => ActorSpecs.ApplySpecJson(specId, json);
	}

	// Undo target for a whole room's *existence*, so create/paste/duplicate/delete are all undoable
	// through one mechanism. CaptureJson returns the room's serialized state while it's in the world,
	// or "" once removed; ApplyJson recreates the room (restoring tiles/actors/settings from that
	// state) or removes it. The Room object is kept alive by this target across the undo/redo cycle,
	// so an undone deletion re-adds the very same instance.
	sealed class RoomExistenceUndoTarget(Manager manager, Room room) : IJsonUndoTarget
	{
		public string UndoId => $"room-exists:{room.Id}";

		public string CaptureJson()
			=> Assets.RoomsById.TryGetValue(room.Id, out var existing) && existing == room
				? room.ToJsonString()
				: "";

		public void ApplyJson(string json)
		{
			if (json.Length == 0)
				manager.RemoveRoomFromWorld(room);
			else
			{
				room.LoadJsonState(json);
				manager.AddRoomToWorld(room);
			}
		}
	}

	enum ResizeHandle
	{
		None,
		Left, Right, Top, Bottom,
		TopLeft, TopRight, BottomLeft, BottomRight
	}

	void SetHiddenWallResizeCursor(ResizeHandle handle)
	{
		if (handle == ResizeHandle.None)
		{
			SetMouseCursor(null);
			return;
		}

		var type = handle switch
		{
			ResizeHandle.Left or ResizeHandle.Right => Foster.Framework.Cursor.SystemTypes.ResizeHorizontal,
			ResizeHandle.Top or ResizeHandle.Bottom => Foster.Framework.Cursor.SystemTypes.ResizeVertical,
			ResizeHandle.TopLeft or ResizeHandle.BottomRight => Foster.Framework.Cursor.SystemTypes.ResizeNWSE,
			ResizeHandle.TopRight or ResizeHandle.BottomLeft => Foster.Framework.Cursor.SystemTypes.ResizeNESW,
			_ => Foster.Framework.Cursor.SystemTypes.Default
		};

		if (!editorCursors.TryGetValue(type, out var cursor))
		{
			cursor = new Cursor(type);
			editorCursors[type] = cursor;
		}
		SetMouseCursor(cursor);
	}

	void DisposeEditorCursors()
	{
		foreach (var cursor in editorCursors.Values)
			cursor.Dispose();
		editorCursors.Clear();
	}

	void FocusEditorOnCurrentRoom()
	{
		if (CurrentRoom is not { } room)
			return;

		GetEditorRoomFocus(room, out editorCamera, out editorZoom);
		editorZoomTarget = editorZoom;
		editorCameraTween = 1.0f;
		editorViewInitialized = true;
	}

	void TweenEditorCameraToRoom(Room room)
	{
		GetEditorRoomFocus(room, out editorCameraTweenTo, out editorZoomTweenTo);
		editorCameraTweenFrom = editorCamera;
		editorZoomTweenFrom = editorZoom;
		editorCameraTween = 0.0f;
		editorViewInitialized = true;
	}

	void GetEditorRoomFocus(Room room, out Vector2 camera, out float zoom)
	{
		var bounds = room.WorldBounds;
		var view = GameViewRect;
		camera = bounds.Center;
		zoom = Math.Clamp(Math.Min(view.Width / bounds.Width, view.Height / bounds.Height), 0.15f, 8.0f);
	}

	void UpdateEditorCameraTween()
	{
		// Don't let the editor preview build its nav graph mid-slide (see GetPlatformNavGraph).
		if (game != null)
			game.navBuildSuppressedByHost = editorCameraTween < 1.0f;

		if (editorCameraTween >= 1.0f)
			return;

		editorCameraTween = Calc.Approach(editorCameraTween, 1.0f, Time.Delta * 4.0f);
		var t = Ease.Cube.InOut(editorCameraTween);
		editorCamera = Vector2.Lerp(editorCameraTweenFrom, editorCameraTweenTo, t);
		editorZoom = float.Lerp(editorZoomTweenFrom, editorZoomTweenTo, t);
		editorZoomTarget = editorZoom;

#if DEBUG
#if SHOW_EDITOR_ALLOCS
		if (editorCameraTween >= 1.0f)
		{
			var bytesAfterTween = GC.GetAllocatedBytesForCurrentThread();
			var bytesAllocated = bytesAfterTween - gcBytesBeforeEditorTween;
			if (bytesAllocated > 0)
				Console.WriteLine($"Editor tween: GC allocated {bytesAllocated} bytes");
		}
#endif
#endif
	}

	void MatchEditorCameraToGame()
	{
		if (game == null)
			return;

		editorCamera = game.Camera + new Vector2(Game.Width, Game.Height) / 2;
		var view = EditorOpenRect;
		editorZoom = Math.Clamp(Math.Min(view.Width / Game.Width, view.Height / Game.Height), 0.15f, 8.0f);
		editorZoomTarget = editorZoom;
		editorCameraTween = 1.0f;
		editorViewInitialized = true;
	}

	const float BASE_EDITOR_CAM_MOVE_MULTIPLIER = 0.7f;
	const float EDITOR_CAM_MOVE_MULTIPLIER_SPEED_UP = 1.2f;
	float editorCameraMoveMultiplier = BASE_EDITOR_CAM_MOVE_MULTIPLIER;

	void UpdateEditorNavigation()
	{
		// Keyboard Shift + WASD moves the camera around incrementally.
		if (Input.Keyboard.Shift && controls.Move.Value != Point2.Zero)
		{
			editorCameraMoveMultiplier += Time.Delta * EDITOR_CAM_MOVE_MULTIPLIER_SPEED_UP;
			editorCamera += controls.Move.Value * Time.Delta * 700.0f / editorZoom * (editorCameraMoveMultiplier);
			return;
		}

		// Otherwise, we jump to adjacent rooms.
		editorCameraMoveMultiplier = BASE_EDITOR_CAM_MOVE_MULTIPLIER;
		var direction = Point2.Zero;
		if (controls.Move.PressedUp) direction += Point2.Up;
		if (controls.Move.PressedDown) direction += Point2.Down;
		if (controls.Move.PressedLeft) direction += Point2.Left;
		if (controls.Move.PressedRight) direction += Point2.Right;

		if (direction != Point2.Zero)
		{
			using (CaptureSelectionUndo("Select Room"))
				ShiftRoom(direction);
			controls.Move.Clear();
		}
	}

	void PaletteImGui()
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
		//ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(1.0f, 1.0f, 1.0f, 1.0f));

		if (ImGui.Begin("Palette", flags))
		{
			if (ImGuiSubtextureButton("##Select", Assets.GetSubtexture("buttons/16"), null, actorSelectTool, ButtonSizeV, true))
				SelectEditorActorTool();
			ImGui.Spacing();
			DrawTilePaletteSection();
			ImGui.Spacing();
			DrawActorPaletteSection();
			ImGui.Spacing();
			DrawRoomPaletteSection();
			ImGui.Spacing();
			DrawAmbientOcclusionSection();
			DrawSoftShadowSection();
			ImGui.Spacing();
			//DrawPreviewPaletteSection();

			if (ImGui.IsWindowHovered() && !ImGui.IsAnyItemHovered() && Input.Mouse.LeftPressed)
				SelectEditorActorTool();
		}
		ImGui.End();

		ImGui.PopStyleColor(1);
		ImGui.PopStyleVar(4);

		ActorSpecInspectorImGui();
		ActorInstanceInspectorImGui();
		DrawEditorSelectionOptions();
		DrawSlopePaintOption();
	}

	void DrawSlopePaintOption()
	{
		if (!SlopePaintAvailable)
		{
			slopePaint = false;
			return;
		}

		var flags =
			ImGuiWindowFlags.NoDecoration |
			ImGuiWindowFlags.AlwaysAutoResize |
			ImGuiWindowFlags.NoSavedSettings |
			ImGuiWindowFlags.NoMove |
			ImGuiWindowFlags.NoFocusOnAppearing;

		var pos = new Vector2(PaletteSlideRect.Right + 10, ToolbarHeight + 10);
		ImGui.SetNextWindowPos(pos, ImGuiCond.Always);
		ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(8, 6));
		ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.04f, 0.04f, 0.05f, 0.82f));
		ImGui.Begin("Tile Paint Options", flags);
		ImGui.Checkbox("Slopes", ref slopePaint);
		if (ImGui.IsItemHovered())
			ImGui.SetTooltip("Drag a rectangle to lay a slope staircase.\nDirection follows the drag (down = floor, up = underhang).");
		ImGui.End();
		ImGui.PopStyleColor();
		ImGui.PopStyleVar();
	}

	void SelectEditorActorTool()
	{
		actorSelectTool = true;
		brush = '0';
	}

	void DrawTilePaletteSection()
	{
		ImGui.TextUnformatted("Tiles");
		DrawTileLayerSelector();
		var index = 0;
		ValueStringBuilder vsb = new(stackalloc char[30]);
		foreach (var (id, entry) in Factory.Entries)
		{
			vsb.Clear();
			vsb.Append("Palette_Tiles_");
			vsb.Append(id);

			if (entry.Layer != activeTileLayer) continue;

			if (index > 0 && index % PaletteColumns != 0)
				ImGui.SameLine();

			var dualPreview = GetDualTilesetPreview(entry);

			if (ImGuiSubtextureButton(vsb.AsSpan(), entry.Image, entry.Name, dualPreview,
				selectedActorPaletteIndex < 0 && brush == id, new Vector2(ButtonSize, ButtonSize)))
			{
				brush = id;
				selectedActorPaletteIndex = -1;
				actorSelectTool = false;
			}

			if (ImGui.BeginPopupContextItem(vsb.AsSpan()))
			{
				if (ImGui.Selectable("Open in Sprite Editor"))
				{
					AsepriteLauncher.Open(entry);
				}
				ImGui.EndPopup();
			}

			index++;
		}
	}

	void DrawTileLayerSelector()
	{
		if (ImGui.SmallButton("O ##ShowBG"))
			editorShowBackgroundLayer = !editorShowBackgroundLayer;
		ImGui.SameLine();
		if (ImGui.Button(activeTileLayer == Factory.TileLayer.Background ? "Background *" : "Background"))
			SelectTileLayer(Factory.TileLayer.Background);

		if (ImGui.SmallButton("O ##ShowFG"))
			editorShowForegroundLayer = !editorShowForegroundLayer;
		ImGui.SameLine();
		if (ImGui.Button(activeTileLayer == Factory.TileLayer.Foreground ? "Foreground *" : "Foreground"))
			SelectTileLayer(Factory.TileLayer.Foreground);
	}

	void SelectTileLayer(Factory.TileLayer layer)
	{
		activeTileLayer = layer;
		if (Factory.Find(brush) is { } entry && entry.Layer == activeTileLayer)
			return;

		brush = Factory.Entries.FirstOrDefault(it => it.Value.Layer == activeTileLayer).Key;
		selectedActorPaletteIndex = -1;
		actorSelectTool = false;
	}

	static Tileset? GetDualTilesetPreview(Factory.Entry entry)
		=> entry.DualTilesetName != null ? Assets.GetTileset(entry.DualTilesetName) : null;

	void DrawActorPaletteSection()
	{
		ImGui.TextUnformatted("Actors");
		DrawActorTagFilter();
		var index = 0;
		var vsb = new ValueStringBuilder(stackalloc char[30]);
		for (var i = 0; i < Factory.ActorPalette.Count; i++)
		{
			var entry = Factory.ActorPalette[i];
			if (entry.Image.Texture == null)
				continue;
			if (actorPaletteFilter != ActorTag.None && (entry.Tags & actorPaletteFilter) == 0)
				continue;

			if (index > 0 && index % PaletteColumns != 0)
				ImGui.SameLine();

			vsb.Clear();
			vsb.Append("Palette_Actors_");
			vsb.Append(i);
			if (ImGuiSubtextureButton(vsb.AsSpan(), entry.Image, entry.Name, selectedActorPaletteIndex == i, new Vector2(ButtonSize, ButtonSize), true))
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

	void DrawActorTagFilter()
	{
		// Only offer filters for tags some palette entry actually uses.
		var available = ActorTag.None;
		foreach (var entry in Factory.ActorPalette)
			available |= entry.Tags;
		if (available == ActorTag.None)
			return;

		var visibleMaxX = ImGui.GetCursorScreenPos().X + ImGui.GetContentRegionAvail().X;
		ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(6, 3)); // the palette zeroes FramePadding; give tags a bit back
		ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 4f);

		DrawTagButton("All", actorPaletteFilter == ActorTag.None, () => actorPaletteFilter = ActorTag.None, visibleMaxX, firstInRow: true);
		foreach (ActorTag tag in Enum.GetValues<ActorTag>())
		{
			if (tag == ActorTag.None || (available & tag) == 0)
				continue;
			var t = tag;
			DrawTagButton(tag.ToString(), (actorPaletteFilter & t) != 0, () => ToggleActorTagFilter(t), visibleMaxX, firstInRow: false);
		}

		ImGui.PopStyleVar(2);
	}

	void ToggleActorTagFilter(ActorTag tag)
	{
		var io = ImGui.GetIO();
		if (io.KeyShift || io.KeyCtrl || io.KeySuper) // additive: add/remove this tag
			actorPaletteFilter ^= tag;
		else // exclusive: this tag only; click again to clear
			actorPaletteFilter = actorPaletteFilter == tag ? ActorTag.None : tag;
	}

	static void DrawTagButton(string label, bool active, Action onClick, float visibleMaxX, bool firstInRow)
	{
		var style = ImGui.GetStyle();
		if (!firstInRow)
		{
			// Canonical ImGui button-wrap: stay on the row only while the next button fits.
			var width = ImGui.CalcTextSize(label).X + style.FramePadding.X * 2;
			if (ImGui.GetItemRectMax().X + style.ItemSpacing.X + width < visibleMaxX)
				ImGui.SameLine();
		}

		if (active)
			ImGui.PushStyleColor(ImGuiCol.Button, style.Colors[(int)ImGuiCol.ButtonActive]);

		var vsb = new ValueStringBuilder(stackalloc char[30]);
		vsb.Append(label);
		vsb.Append("##ActorTag");

		if (ImGui.Button(vsb.AsSpan()))
			onClick();
		if (active)
			ImGui.PopStyleColor();
	}

	void DrawRoomPaletteSection()
	{
		if (CurrentRoom is not { } room)
			return;

		if (!ImGui.CollapsingHeader("Room"))
			return;

		var changed = false;
		// Room geometry is edited directly in the world now: zoom out until the room is small on
		// screen to enter room-edit mode (move/resize handles, drag, copy/paste). No button needed.
		{
			var vsb = new ValueStringBuilder(stackalloc char[40]);
			vsb.Append("Size ");
			vsb.Append(room.Size.X);
			vsb.Append(" x ");
			vsb.Append(room.Size.Y);
			ImGui.TextUnformatted(vsb.AsSpan());
		}

		// These copy/paste only the room's *settings* (backdrop/ambient/etc.) onto the current room.
		// Whole-room copy/paste is the world-level Ctrl+C/V in room-edit mode.
		if (ImGui.Button("Copy Settings"))
			copiedRoomSettings = CaptureRoomSettings(room);
		if (copiedRoomSettings is { } settings)
		{
			ImGui.SameLine();
			if (ImGui.Button("Paste Settings"))
			{
				using (CaptureRoomUndo("Paste Room Settings", room))
					ApplyRoomSettings(room, settings);
				changed = true;
			}
		}

		ImGui.TextUnformatted("Notes");
		ImGui.SetNextItemWidth(-1);
		// Local copy so the undo scope captures the room *before* the first keystroke lands.
		var notes = room.Notes;
		if (ImGui.InputTextMultiline("##RoomNotesEditor", ref notes, 3000, new(-1, 80)))
		{
			OpenRoomSettingsUndo("Edit Room Notes", room);
			room.Notes = notes;
		}

		ImGui.TextUnformatted("Backdrop");
		var backdrop = room.BackdropColor.ToVector3();
		ImGui.SetNextItemWidth(-1);
		if (ImGui.ColorEdit3("##RoomBackdropColor", ref backdrop, ImGuiColorEditFlags.NoInputs))
		{
			OpenRoomSettingsUndo("Edit Room Backdrop", room);
			room.BackdropColor = new Color(Vector3.Clamp(backdrop, Vector3.Zero, Vector3.One));
			changed = true;
		}

		var gradient = room.BackdropColor2.HasValue;
		if (ImGui.Checkbox("Gradient", ref gradient))
		{
			using (CaptureRoomUndo("Edit Room Backdrop", room))
				room.BackdropColor2 = gradient ? room.BackdropColor : null;
			changed = true;
		}

		if (room.BackdropColor2 is { } backdrop2Color)
		{
			ImGui.TextUnformatted("Backdrop Bottom");
			var backdrop2 = backdrop2Color.ToVector3();
			ImGui.SetNextItemWidth(-1);
			if (ImGui.ColorEdit3("##RoomBackdropColor2", ref backdrop2, ImGuiColorEditFlags.NoInputs))
			{
				OpenRoomSettingsUndo("Edit Room Backdrop", room);
				room.BackdropColor2 = new Color(Vector3.Clamp(backdrop2, Vector3.Zero, Vector3.One));
				changed = true;
			}
		}

		ImGui.TextUnformatted("Ambient");
		var ambient = room.AmbientColor.ToVector3();
		ImGui.SetNextItemWidth(-1);
		if (ImGui.ColorEdit3("##RoomAmbientColor", ref ambient, ImGuiColorEditFlags.NoInputs))
		{
			OpenRoomSettingsUndo("Edit Room Lighting", room);
			room.AmbientColor = new Color(Vector3.Clamp(ambient, Vector3.Zero, Vector3.One));
			changed = true;
		}

		ImGui.TextUnformatted("Intensity");
		var ambientIntensity = room.AmbientIntensity;
		ImGui.SetNextItemWidth(-1);
		if (ImGui.SliderFloat("##RoomAmbientIntensity", ref ambientIntensity, 0.0f, 2.0f))
		{
			OpenRoomSettingsUndo("Edit Room Lighting", room);
			room.AmbientIntensity = ambientIntensity;
			changed = true;
		}

		ImGui.TextUnformatted("Noise");
		var lightNoise = room.LightNoise;
		ImGui.SetNextItemWidth(-1);
		if (ImGui.SliderFloat("##RoomLightNoise", ref lightNoise, 0.0f, 1.0f))
		{
			OpenRoomSettingsUndo("Edit Room Lighting", room);
			room.LightNoise = lightNoise;
			changed = true;
		}

		ImGui.TextUnformatted("Reverb");
		var reverbAuto = room.ReverbAuto;
		if (ImGui.Checkbox("Auto Reverb", ref reverbAuto))
		{
			using (CaptureRoomUndo("Edit Room Reverb", room))
			{
				room.ReverbAuto = reverbAuto;
				if (room.ReverbAuto)
					room.RebuildGeometry();
			}
			changed = true;
		}

		var reverb = room.Reverb;
		ImGui.SetNextItemWidth(-1);
		if (ImGui.SliderFloat("##RoomReverb", ref reverb, 0.0f, 1.0f))
		{
			OpenRoomSettingsUndo("Edit Room Reverb", room);
			room.ReverbAuto = false;
			room.Reverb = Math.Clamp(reverb, 0.0f, 1.0f);
			changed = true;
		}

		ImGui.TextUnformatted("Sun Strength");
		var sunStrength = room.SunStrength;
		ImGui.SetNextItemWidth(-1);
		if (ImGui.SliderFloat("##RoomSunStrength", ref sunStrength, 0.0f, 2.0f))
		{
			OpenRoomSettingsUndo("Edit Room Sun", room);
			room.SunStrength = sunStrength;
			changed = true;
		}

		if (sunStrength > 0)
		{
			ImGui.TextUnformatted("Sun Color");
			var sun = room.SunColor.ToVector3();
			ImGui.SetNextItemWidth(-1);
			if (ImGui.ColorEdit3("##RoomSunColor", ref sun, ImGuiColorEditFlags.NoInputs))
			{
				OpenRoomSettingsUndo("Edit Room Sun", room);
				room.SunColor = new Color(Vector3.Clamp(sun, Vector3.Zero, Vector3.One));
				changed = true;
			}

			ImGui.TextUnformatted("Sun Direction");
			var sunDirection = room.SunDirection;
			ImGui.SetNextItemWidth(-1);
			if (ImGui.DragFloat2("##RoomSunDirection", ref sunDirection, 0.01f, -1.0f, 1.0f))
			{
				OpenRoomSettingsUndo("Edit Room Sun", room);
				room.SunDirection = Room.NormalizeOrDefault(sunDirection, room.SunDirection);
				changed = true;
			}
		}

		if (musicAreas.Count > 0)
		{
			ImGui.TextUnformatted("Music");
			ImGui.PushID("Music");
			// null = auto: inherit from the containing music region, or the default. Any
			// selected area (including the default) is an explicit per-room override.
			var autoLabel = $"Auto ({MusicRegions.ResolveRegion(room)?.Area ?? DefaultMusicArea})";
			ImGui.SetNextItemWidth(-1);
			if (ImGui.BeginCombo("##RoomMusicArea", room.MusicArea ?? autoLabel))
			{
				if (ImGui.Selectable(autoLabel, room.MusicArea == null))
				{
					using (CaptureRoomUndo("Edit Room Music", room))
						room.MusicArea = null;
					changed = true;
				}
				foreach (var musicArea in musicAreas)
				{
					var selected = room.MusicArea == musicArea;
					ImGui.PushID(musicArea ?? "");
					if (ImGui.Selectable(musicArea, selected))
					{
						using (CaptureRoomUndo("Edit Room Music", room))
							room.MusicArea = musicArea;
						changed = true;
					}
					if (selected)
						ImGui.SetItemDefaultFocus();
					ImGui.PopID();
				}
				ImGui.EndCombo();
			}
			ImGui.PopID();
		}

		ImGui.SeparatorText("Dither");
		var ditherEnabled = room.LightingDitherEnabled;
		if (ImGui.Checkbox("Dither Enabled", ref ditherEnabled))
		{
			using (CaptureRoomUndo("Edit Room Dither", room))
				room.LightingDitherEnabled = ditherEnabled;
			changed = true;
		}

		ImGui.TextUnformatted("Dither Scale");
		var ditherScale = room.LightingDitherScale;
		ImGui.SetNextItemWidth(-1);
		if (ImGui.SliderFloat("##RoomDitherScale", ref ditherScale, 1.0f, 8.0f))
		{
			OpenRoomSettingsUndo("Edit Room Dither", room);
			room.LightingDitherScale = ditherScale;
			changed = true;
		}

		ImGui.TextUnformatted("Dither Strength");
		var ditherStrength = room.LightingDitherStrength;
		ImGui.SetNextItemWidth(-1);
		if (ImGui.SliderFloat("##RoomDitherStrength", ref ditherStrength, 0.0f, 1.0f))
		{
			OpenRoomSettingsUndo("Edit Room Dither", room);
			room.LightingDitherStrength = ditherStrength;
			changed = true;
		}

		ImGui.TextUnformatted("Dither Slope Boost");
		var ditherSlopeBoost = room.NormalDitherSlopeBoost;
		ImGui.SetNextItemWidth(-1);
		if (ImGui.SliderFloat("##RoomDitherSlopeBoost", ref ditherSlopeBoost, 0.0f, 1.0f))
		{
			OpenRoomSettingsUndo("Edit Room Dither", room);
			room.NormalDitherSlopeBoost = ditherSlopeBoost;
			changed = true;
		}

		var ditherQuantization = room.NormalDitherQuantization;
		if (ImGui.Checkbox("Dither Level Quantization", ref ditherQuantization))
		{
			using (CaptureRoomUndo("Edit Room Dither", room))
				room.NormalDitherQuantization = ditherQuantization;
			changed = true;
		}

		if (changed)
		{
			editorGameDirty = true;
			SyncEditorGame();
		}
	}

	void DrawAmbientOcclusionSection()
	{
		if (!ImGui.CollapsingHeader("Ambient Occlusion"))
			return;

		editorGameDirty |= ImGui.Checkbox("Enabled##AO", ref PerfToggles.AmbientOcclusion);

		ImGui.TextUnformatted("Strength");
		ImGui.SetNextItemWidth(-1);
		editorGameDirty |= ImGui.SliderFloat("##AmbientOcclusionStrength", ref AmbientOcclusionStrength, 0.0f, 4.0f);

		ImGui.TextUnformatted("Blur");
		ImGui.SetNextItemWidth(-1);
		editorGameDirty |= ImGui.SliderFloat("##AmbientOcclusionBlur", ref AmbientOcclusionBlur, 0.0f, 32.0f);

		ImGui.TextUnformatted("Offset (px)");
		ImGui.SetNextItemWidth(-1);
		editorGameDirty |= ImGui.SliderFloat2("##AmbientOcclusionOffset", ref AmbientOcclusionOffset, -16.0f, 16.0f);

		ImGui.TextUnformatted("Min Visibility");
		ImGui.SetNextItemWidth(-1);
		editorGameDirty |= ImGui.SliderFloat("##AmbientOcclusionMinVisibility", ref AmbientOcclusionMinVisibility, 0.0f, 1.0f);
	}

	void DrawSoftShadowSection()
	{
		if (!ImGui.CollapsingHeader("Soft Shadows"))
			return;

		editorGameDirty |= ImGui.Checkbox("Enabled##SS", ref PerfToggles.SoftShadows);

		ImGui.TextUnformatted("Penumbra (px)");
		ImGui.SetNextItemWidth(-1);
		editorGameDirty |= ImGui.SliderFloat("##SoftShadowPenumbra", ref SoftShadowPenumbra, 0.0f, 24.0f);

		ImGui.TextUnformatted("Min Softness");
		ImGui.SetNextItemWidth(-1);
		editorGameDirty |= ImGui.SliderFloat("##SoftShadowMinSoftness", ref SoftShadowMinSoftness, 0.0f, 1.0f);
	}

#if false
	void DrawPreviewPaletteSection()
	{
		if (!ImGui.CollapsingHeader("Previews"))
			return;

		ImGui.TextUnformatted("Resolution");
		ImGui.SetNextItemWidth(-1);
		var scale = roomPreviewResolutionScale;
		if (ImGui.SliderFloat("##RoomPreviewResolutionScale", ref scale, 0.05f, 1.0f))
		{
			roomPreviewResolutionScale = scale;
			ClearRoomPreviewCache();
		}
	}
#endif

	static RoomSettings CaptureRoomSettings(Room room)
		=> new(room.MusicArea, room.BackdropColor, room.BackdropColor2, room.AmbientColor, room.AmbientIntensity, room.LightNoise, room.ReverbAuto, room.Reverb, room.SunColor, room.SunStrength, room.SunDirection, room.LightingDitherEnabled, room.LightingDitherScale, room.LightingDitherStrength, room.NormalDitherSlopeBoost, room.NormalDitherQuantization);

	static void ApplyRoomSettings(Room room, RoomSettings settings)
	{
		room.MusicArea = settings.MusicArea;
		room.BackdropColor = settings.BackdropColor;
		room.BackdropColor2 = settings.BackdropColor2;
		room.AmbientColor = settings.AmbientColor;
		room.AmbientIntensity = settings.AmbientIntensity;
		room.LightNoise = settings.LightNoise;
		room.ReverbAuto = settings.ReverbAuto;
		room.Reverb = settings.Reverb;
		room.SunColor = settings.SunColor;
		room.SunStrength = settings.SunStrength;
		room.SunDirection = settings.SunDirection;
		room.LightingDitherEnabled = settings.LightingDitherEnabled;
		room.LightingDitherScale = settings.LightingDitherScale;
		room.LightingDitherStrength = settings.LightingDitherStrength;
		room.NormalDitherSlopeBoost = settings.NormalDitherSlopeBoost;
		room.NormalDitherQuantization = settings.NormalDitherQuantization;
		if (room.ReverbAuto)
			room.RebuildGeometry();
	}

	void ActorSpecInspectorImGui()
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
		{
			SyncEditorGame();
		}

		if (ImGui.Button("Save"))
			ActorSpecs.SaveSpec(selectedActorSpecId);

		ImGui.End();
	}

	bool DrawActorSpecProperty(string specId, ActorSpecs.EditableProperty property)
	{
		var changed = false;
		var controlWidth = BeginInspectorPropertyRow(property.Name, property.HasLocalValue);
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
		else if (IsHiddenWallTileTypeProperty(property))
		{
			var value = property.Value?.GetValue<string>() ?? "castle";
			if (DrawHiddenWallTileTypeButtons(value, next =>
			{
				BeginPrefabUndo(specId);
				ActorSpecs.SetProperty(specId, property.JsonName, JsonValue.Create(next));
			}))
				changed = true;
		}
		else if (IsMovingPlatformMaterialProperty(property))
		{
			var value = property.Value?.GetValue<string>() ?? "metal-dual";
			if (DrawMovingPlatformMaterialButtons(value, next =>
			{
				BeginPrefabUndo(specId);
				ActorSpecs.SetProperty(specId, property.JsonName, JsonValue.Create(next));
			}))
				changed = true;
		}
		else if (property.Name == nameof(Prop.SpriteName) && ActorSpecs.Get(specId) is { } spec && IsPropActor(spec))
		{
			var value = property.Value?.GetValue<string>() ?? string.Empty;
			if (DrawSpriteCombo(ref value))
			{
				BeginPrefabUndo(specId);
				ActorSpecs.SetProperty(specId, property.JsonName, JsonValue.Create(value));
				changed = true;
			}
		}
		else if (NeuralTextureWeightsSlot(property.Name) is >= 0 and var weightsSlot && ActorSpecs.Get(specId) is { } textureSpec && IsNeuralTextureActor(textureSpec))
		{
			var value = property.Value?.GetValue<string>() ?? string.Empty;
			if (NeuralTextureCatalog.DrawCombo(ref value, allowNone: weightsSlot > 0))
			{
				BeginPrefabUndo(specId);
				ActorSpecs.SetProperty(specId, property.JsonName, JsonValue.Create(value));
				changed = true;
			}
		}
		else if (property.Name == nameof(NeuralTextureActor.PaintedMask) && ActorSpecs.Get(specId) is { } maskSpec && IsNeuralTextureActor(maskSpec))
		{
			ImGui.TextDisabled("(painted per room instance)");
		}
		else if (property.Type == typeof(string))
		{
			var value = property.Value?.GetValue<string>() ?? string.Empty;
			if (ImGui.InputTextMultiline("##Value", ref value, 2048, new Vector2(controlWidth, 64)))
			{
				BeginPrefabUndo(specId);
				ActorSpecs.SetProperty(specId, property.JsonName, JsonValue.Create(value));
				changed = true;
			}
		}
		else if (property.Type == typeof(Point2) || property.Type == typeof(Point2?))
		{
			var point = ActorSpecs.ReadPoint(property.Value) ?? Point2.Zero;
			var values = new[] { point.X, point.Y };
			if (ImGui.DragInt2("##Value", ref values[0]))
			{
				BeginPrefabUndo(specId);
				ActorSpecs.SetProperty(specId, property.JsonName, new JsonArray(values[0], values[1]));
				changed = true;
			}
		}
		else if (property.Type == typeof(Point2[]))
		{
			var points = ActorSpecs.ReadPointArray(property.Value) ?? [];
			ImGui.TextUnformatted($"{points.Length} points");
			ImGui.SameLine();
			if (ImGui.SmallButton("Add"))
			{
				var next = points.Length > 0 ? points[^1] + new Point2(Game.TileSize * 4, 0) : Point2.Zero;
				using (CapturePrefabUndo("Add Path Point", specId))
					ActorSpecs.SetProperty(specId, property.JsonName, ActorSpecs.ToJsonNode((Point2[])[.. points, next]));
				changed = true;
			}
			if (points.Length > 2)
			{
				ImGui.SameLine();
				if (ImGui.SmallButton("Remove Last"))
				{
					using (CapturePrefabUndo("Remove Path Point", specId))
						ActorSpecs.SetProperty(specId, property.JsonName, ActorSpecs.ToJsonNode(points[..^1]));
					changed = true;
				}
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
			var rgb = JsonUtil.ReadColor(property.Value)?.ToVector3() ?? Vector3.One;
			if (ImGui.ColorEdit3("##Value", ref rgb, ImGuiColorEditFlags.NoInputs))
			{
				BeginPrefabUndo(specId);
				ActorSpecs.SetProperty(specId, property.JsonName, JsonValue.Create(new Color(Vector3.Clamp(rgb, Vector3.Zero, Vector3.One)).ToHexStringRGB()));
				changed = true;
			}
		}
		else if (property.Type == typeof(Signs))
		{
			var value = ActorSpecs.ReadSigns(property.Value);
			if (ImGuiSubtextureButton("##Value", Assets.GetSubtexture("buttons/15"), FacingTooltip(value), false, new Vector2(ButtonSize, ButtonSize), true))
			{
				BeginPrefabUndo(specId);
				ActorSpecs.SetProperty(specId, property.JsonName, JsonValue.Create((-value).ToString()));
				changed = true;
			}
		}
		else if (property.Type.IsEnum)
		{
			var value = property.Value?.GetValue<string>() ?? Enum.GetNames(property.Type).FirstOrDefault() ?? string.Empty;
			if (EditorGui.DrawEnumCombo(property.Type, ref value))
			{
				BeginPrefabUndo(specId);
				ActorSpecs.SetProperty(specId, property.JsonName, JsonValue.Create(value));
				changed = true;
			}
		}
		else
		{
			ImGui.TextUnformatted("-");
		}
		ImGui.PopID();
		if (undo.HasPending(PrefabUndoSlot, specId) && ImGui.IsItemDeactivatedAfterEdit())
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

	void ActorInstanceInspectorImGui()
	{
		if (CurrentRoom is not { } room || selectedRoomActorIndex < 0 || selectedRoomActorIndex >= room.Actors.Count)
			return;

		var actor = room.Actors[selectedRoomActorIndex];
		var title = ActorSpecs.ReadString(actor, "From") ?? ActorSpecs.ReadString(actor, "Type") ?? "Actor";
		var inspectorPadding = Vector2.One * 16;
		var window = RequiredWindow;
		var inspectorPos = new Vector2(window.WidthInPixels / imgui.Scale, window.HeightInPixels / imgui.Scale) - inspectorPadding;
		ImGui.SetNextWindowPos(inspectorPos, ImGuiCond.Appearing, Vector2.One);
		ImGui.SetNextWindowSize(new Vector2(260, 0), ImGuiCond.FirstUseEver);
		var open = true;
		if (!ImGui.Begin($"Actor: {title}##ActorInstanceInspector", ref open, ImGuiWindowFlags.NoSavedSettings))
		{
			if (!open)
				selectedRoomActorIndex = -1;
			ImGui.End();
			return;
		}

		if (!open)
		{
			selectedRoomActorIndex = -1;
			ImGui.End();
			return;
		}

		if (ActorSpecs.ReadString(actor, "From") is { } specId && ActorSpecs.Get(specId) != null)
		{
			if (ImGui.Button("Jump to Prefab"))
			{
				selectedActorSpecId = specId;
				actorSpecInspectorOpen = true;
			}
			ImGui.Separator();
		}

		var changed = false;
		foreach (var property in ActorSpecs.GetEditableProperties(actor))
		{
			ImGui.PushID(property.JsonName);
			changed |= DrawActorInstanceProperty(room, actor, property);
			ImGui.PopID();
		}

		if (changed)
		{
			editorGameDirty = true;
			SyncEditorGame();
		}

		// Edit the shared effect asset directly. This mutates the live asset (not
		// the room), so it doesn't trigger SyncEditorGame - the selected emitter
		// reflects changes immediately, and edits autosave + are undoable.
		if (IsParticleEmitterActor(actor))
			particleEditor.DrawActorInspectorBox(game, actor);

		ImGui.End();
	}

	bool DrawActorInstanceProperty(Room room, JsonObject actor, ActorSpecs.EditableProperty property)
	{
		if (property.Name == nameof(LinkedDoor.TargetActorId) && IsLinkedDoorActor(actor))
			return DrawLinkedDoorTargetProperty(room, actor, property);
		if (property.Name == nameof(Switch.TargetActorId) && IsSwitchActor(actor))
			return DrawSwitchTargetProperty(room, actor, property);

		if (property.Name == nameof(ParticleEmitter.EffectName) && IsParticleEmitterActor(actor))
			return DrawParticleEffectNameProperty(room, actor, property);

		var changed = false;
		var controlWidth = BeginInspectorPropertyRow(property.Name, property.HasLocalValue);
		ImGui.SetNextItemWidth(controlWidth);
		ImGui.PushID("Value");

		if (property.Type == typeof(float))
		{
			var value = property.Value?.GetValue<float>() ?? 0;
			if (ImGui.DragFloat("##Value", ref value, 0.05f))
			{
				OpenActorPropertyUndo(room, actor);
				ActorSpecs.SetProperty(actor, property.JsonName, JsonValue.Create(value));
				changed = true;
			}
		}
		else if (property.Type == typeof(int))
		{
			var value = property.Value?.GetValue<int>() ?? 0;
			if (ImGui.DragInt("##Value", ref value))
			{
				OpenActorPropertyUndo(room, actor);
				ActorSpecs.SetProperty(actor, property.JsonName, JsonValue.Create(value));
				changed = true;
			}
		}
		else if (IsHiddenWallTileTypeProperty(property))
		{
			var value = property.Value?.GetValue<string>() ?? "castle";
			if (DrawHiddenWallTileTypeButtons(value, next =>
			{
				using (CaptureRoomUndo("Edit Actor Property", room))
					ActorSpecs.SetProperty(actor, property.JsonName, JsonValue.Create(next));
			}))
				changed = true;
		}
		else if (IsMovingPlatformMaterialProperty(property))
		{
			var value = property.Value?.GetValue<string>() ?? "metal-dual";
			if (DrawMovingPlatformMaterialButtons(value, next =>
			{
				using (CaptureRoomUndo("Edit Actor Property", room))
					ActorSpecs.SetProperty(actor, property.JsonName, JsonValue.Create(next));
			}))
				changed = true;
		}
		else if (property.Name == nameof(Prop.SpriteName) && IsPropActor(actor))
		{
			var value = property.Value?.GetValue<string>() ?? string.Empty;
			if (DrawSpriteCombo(ref value))
			{
				using (CaptureRoomUndo("Edit Actor Property", room))
					ActorSpecs.SetProperty(actor, property.JsonName, JsonValue.Create(value));
				changed = true;
			}
		}
		else if (NeuralTextureWeightsSlot(property.Name) is >= 0 and var weightsSlot && IsNeuralTextureActor(actor))
		{
			var value = property.Value?.GetValue<string>() ?? string.Empty;
			if (NeuralTextureCatalog.DrawCombo(ref value, allowNone: weightsSlot > 0))
			{
				using (CaptureRoomUndo("Edit Actor Property", room))
					ActorSpecs.SetProperty(actor, property.JsonName, JsonValue.Create(value));
				changed = true;
			}
		}
		else if (property.Name == nameof(NeuralTextureActor.PaintedMask) && IsNeuralTextureActor(actor))
		{
			DrawMaskPaintControls(room, actor);
		}
		else if (property.Type == typeof(string))
		{
			var value = property.Value?.GetValue<string>() ?? string.Empty;
			if (ImGui.InputTextMultiline("##Value", ref value, 2048, new Vector2(controlWidth, 64)))
			{
				OpenActorPropertyUndo(room, actor);
				ActorSpecs.SetProperty(actor, property.JsonName, JsonValue.Create(value));
				changed = true;
			}
		}
		else if (property.Type == typeof(Point2) || property.Type == typeof(Point2?))
		{
			var point = ActorSpecs.ReadPoint(property.Value) ?? Point2.Zero;
			var values = new[] { point.X, point.Y };
			if (ImGui.DragInt2("##Value", ref values[0]))
			{
				OpenActorPropertyUndo(room, actor);
				ActorSpecs.SetProperty(actor, property.JsonName, new JsonArray(values[0], values[1]));
				changed = true;
			}
		}
		else if (property.Type == typeof(Point2[]))
		{
			var points = ActorSpecs.ReadPointArray(property.Value) ?? [];
			ImGui.TextUnformatted($"{points.Length} points");
			ImGui.SameLine();
			if (ImGui.SmallButton("Add"))
			{
				var next = points.Length > 0 ? points[^1] + new Point2(Game.TileSize * 4, 0) : Point2.Zero;
				using (CaptureRoomUndo("Add Path Point", room))
					ActorSpecs.SetProperty(actor, property.JsonName, ActorSpecs.ToJsonNode((Point2[])[.. points, next]));
				changed = true;
			}
			if (points.Length > 2)
			{
				ImGui.SameLine();
				if (ImGui.SmallButton("Remove Last"))
				{
					using (CaptureRoomUndo("Remove Path Point", room))
						ActorSpecs.SetProperty(actor, property.JsonName, ActorSpecs.ToJsonNode(points[..^1]));
					changed = true;
				}
			}
		}
		else if (property.Type == typeof(bool))
		{
			var value = property.Value?.GetValue<bool>() ?? false;
			if (ImGui.Checkbox("##Value", ref value))
			{
				using (CaptureRoomUndo("Edit Actor Property", room))
					ActorSpecs.SetProperty(actor, property.JsonName, JsonValue.Create(value));
				changed = true;
			}
		}
		else if (property.Type == typeof(Color))
		{
			var rgb = JsonUtil.ReadColor(property.Value)?.ToVector3() ?? Vector3.One;
			if (ImGui.ColorEdit3("##Value", ref rgb, ImGuiColorEditFlags.NoInputs))
			{
				OpenActorPropertyUndo(room, actor);
				ActorSpecs.SetProperty(actor, property.JsonName, JsonValue.Create(new Color(Vector3.Clamp(rgb, Vector3.Zero, Vector3.One)).ToHexStringRGB()));
				changed = true;
			}
		}
		else if (property.Type == typeof(Signs))
		{
			var value = ActorSpecs.ReadSigns(property.Value);
			if (ImGuiSubtextureButton("##Value", Assets.GetSubtexture("buttons/15"), FacingTooltip(value), false, new Vector2(ButtonSize, ButtonSize), true))
			{
				using (CaptureRoomUndo("Edit Actor Property", room))
					ActorSpecs.SetProperty(actor, property.JsonName, JsonValue.Create((-value).ToString()));
				changed = true;
			}
		}
		else if (property.Type.IsEnum)
		{
			var value = property.Value?.GetValue<string>() ?? Enum.GetNames(property.Type).FirstOrDefault() ?? string.Empty;
			if (EditorGui.DrawEnumCombo(property.Type, ref value))
			{
				using (CaptureRoomUndo("Edit Actor Property", room))
				{
					ActorSpecs.SetProperty(actor, property.JsonName, JsonValue.Create(value));
					EditorFor(actor)?.PropertyChanged(new EditorActor(room, actor), property.Name);
				}
				changed = true;
			}
		}
		else
		{
			ImGui.TextUnformatted("-");
		}
		ImGui.PopID();

		if (property.HasLocalValue)
		{
			ImGui.SameLine();
			if (ImGui.SmallButton("Reset"))
			{
				using (CaptureRoomUndo("Reset Actor Property", room))
					ActorSpecs.ResetProperty(actor, property.JsonName);
				changed = true;
			}
		}

		return changed;
	}

	static string FacingTooltip(Signs value) => "Click to face the other direction.";

	static bool IsParticleEmitterActor(JsonObject actor)
		=> string.Equals(ActorSpecs.ReadString(actor, "Type"), "ParticleEmitter", StringComparison.OrdinalIgnoreCase);

	bool DrawParticleEffectNameProperty(Room room, JsonObject actor, ActorSpecs.EditableProperty property)
	{
		var changed = false;
		var controlWidth = BeginInspectorPropertyRow(property.Name, property.HasLocalValue);
		ImGui.PushID("Value");

		var current = property.Value?.GetValue<string>() ?? string.Empty;
		ImGui.SetNextItemWidth(controlWidth);
		if (ImGui.BeginCombo("##Value", current))
		{
			foreach (var name in ParticleEffectAssets.Names)
				if (ImGui.Selectable(name, name == current))
				{
					using (CaptureRoomUndo("Edit Actor Property", room))
						ActorSpecs.SetProperty(actor, property.JsonName, JsonValue.Create(name));
					changed = true;
				}
			ImGui.EndCombo();
		}

		ImGui.PopID();
		return changed;
	}

	bool DrawLinkedDoorTargetProperty(Room room, JsonObject actor, ActorSpecs.EditableProperty property)
	{
		var changed = false;
		var controlWidth = BeginInspectorPropertyRow(property.Name, property.HasLocalValue);
		var value = property.Value?.GetValue<string>() ?? string.Empty;
		var target = Assets.FindActorById(value);
		var sourceId = ActorSpecs.ReadString(actor, "Id") ?? "";
		var picking = !string.IsNullOrWhiteSpace(sourceId) &&
					  string.Equals(pickingLinkedDoorTargetForActorId, sourceId, StringComparison.OrdinalIgnoreCase);

		ImGui.BeginGroup();
		ImGui.SetNextItemWidth(controlWidth);
		ImGui.InputText("##Value", ref value, 128, ImGuiInputTextFlags.ReadOnly);

		if (target is { } found)
			ImGui.TextDisabled($"{found.Room.Id}");
		else if (!string.IsNullOrWhiteSpace(value))
			ImGui.TextColored(new Vector4(1.0f, 0.35f, 0.25f, 1.0f), "Missing target");
		else
			ImGui.TextDisabled("No target");

		if (ImGui.SmallButton(picking ? "Cancel Pick" : "Pick"))
			pickingLinkedDoorTargetForActorId = picking ? null : sourceId;

		ImGui.SameLine();
		if (ImGui.SmallButton("Clear"))
		{
			using (CaptureRoomUndo("Clear Linked Door Target", room))
				ActorSpecs.SetProperty(actor, property.JsonName, JsonValue.Create(""));
			if (picking)
				pickingLinkedDoorTargetForActorId = null;
			changed = true;
		}

		if (target is { } jumpTarget)
		{
			ImGui.SameLine();
			if (ImGui.SmallButton("Jump"))
			{
				CurrentRoom?.Save();
				SelectRoom(jumpTarget.Room);
				selectedRoomActorIndex = jumpTarget.Room.Actors.IndexOf(jumpTarget.Actor);
				SyncEditorGame();
				TweenEditorCameraToRoom(jumpTarget.Room);
			}
		}
		ImGui.EndGroup();

		if (property.HasLocalValue)
		{
			ImGui.SameLine();
			if (ImGui.SmallButton("Reset"))
			{
				using (CaptureRoomUndo("Reset Actor Property", room))
					ActorSpecs.ResetProperty(actor, property.JsonName);
				changed = true;
			}
		}

		return changed;
	}

	bool DrawSwitchTargetProperty(Room room, JsonObject actor, ActorSpecs.EditableProperty property)
	{
		var changed = false;
		var controlWidth = BeginInspectorPropertyRow(property.Name, property.HasLocalValue);
		var value = property.Value?.GetValue<string>() ?? string.Empty;
		var target = Assets.FindActorById(value);
		var label = target is { } found
			? $"{ActorSpecs.GetTypeName(found.Actor)} ({value[..Math.Min(8, value.Length)]})"
			: string.IsNullOrWhiteSpace(value) ? "No target" : $"Missing ({value})";

		ImGui.SetNextItemWidth(controlWidth);
		if (ImGui.BeginCombo("##Value", label))
		{
			foreach (var candidate in room.Actors)
			{
				if (!IsActivatableActor(candidate) || ActorSpecs.GetActorId(candidate) is not { } candidateId)
					continue;
				var typeName = ActorSpecs.GetTypeName(candidate) ?? "Actor";
				var selected = string.Equals(value, candidateId, StringComparison.OrdinalIgnoreCase);
				if (ImGui.Selectable($"{typeName} ({candidateId[..Math.Min(8, candidateId.Length)]})", selected))
				{
					using (CaptureRoomUndo("Set Switch Target", room))
						ActorSpecs.SetProperty(actor, property.JsonName, JsonValue.Create(candidateId));
					changed = true;
				}
			}
			ImGui.EndCombo();
		}

		if (!string.IsNullOrWhiteSpace(value))
		{
			ImGui.SameLine();
			if (ImGui.SmallButton("Clear"))
			{
				using (CaptureRoomUndo("Clear Switch Target", room))
					ActorSpecs.SetProperty(actor, property.JsonName, JsonValue.Create(""));
				changed = true;
			}
		}

		if (property.HasLocalValue)
		{
			ImGui.SameLine();
			if (ImGui.SmallButton("Reset"))
			{
				using (CaptureRoomUndo("Reset Actor Property", room))
					ActorSpecs.ResetProperty(actor, property.JsonName);
				changed = true;
			}
		}

		return changed;
	}

	static bool DrawSpriteCombo(ref string value)
	{
		var changed = false;
		if (ImGui.BeginCombo("##Value", value))
		{
			foreach (var name in Assets.Sprites.Keys)
			{
				var selected = string.Equals(value, name, StringComparison.OrdinalIgnoreCase);
				if (ImGui.Selectable(name, selected))
				{
					value = name;
					changed = true;
				}
				if (selected)
					ImGui.SetItemDefaultFocus();
			}
			ImGui.EndCombo();
		}
		return changed;
	}

	void DrawMaskPaintControls(Room room, JsonObject actor)
	{
		if (ActorSpecs.ReadString(actor, "Id") is not { } actorId)
		{
			ImGui.TextDisabled("(actor has no id)");
			return;
		}

		var painting = maskPaintActorId == actorId;
		if (ImGui.Button(painting ? "Stop Painting" : "Paint Mask"))
		{
			EndMaskPainting();
			if (!painting)
				maskPaintActorId = actorId;
		}

		if (!painting)
			return;

		string?[] slotFiles =
		[
			ActorSpecs.ReadString(actor, nameof(NeuralTextureActor.WeightsFile)),
			ActorSpecs.ReadString(actor, nameof(NeuralTextureActor.WeightsFile1)),
			ActorSpecs.ReadString(actor, nameof(NeuralTextureActor.WeightsFile2)),
			ActorSpecs.ReadString(actor, nameof(NeuralTextureActor.WeightsFile3)),
		];
		for (var slot = 0; slot < slotFiles.Length; slot++)
		{
			if (slot > 0 && string.IsNullOrEmpty(slotFiles[slot]))
				continue;
			var name = string.IsNullOrEmpty(slotFiles[slot])
				? "(default)"
				: Path.GetFileNameWithoutExtension(slotFiles[slot])!.Replace('_', ' ');
			if (ImGui.RadioButton($"{name}##MaskSlot{slot}", maskPaintSlot == slot))
				maskPaintSlot = slot;
		}
		if (ImGui.RadioButton("Eraser##MaskSlot", maskPaintSlot < 0))
			maskPaintSlot = -1;

		ImGui.SliderFloat("Radius", ref maskPaintRadius, 2.0f, 96.0f);
		ImGui.SliderFloat("Softness", ref maskPaintSoftness, 0.05f, 1.0f);
		ImGui.SliderFloat("Strength", ref maskPaintStrength, 0.05f, 1.0f);
		ImGui.SliderInt("Preview Steps", ref maskPaintPreviewSteps, 0, 256);
		if (ImGui.Button("Resimulate") && FindLiveNeuralTextureActor(actorId) is { } preview)
		{
			preview.SimulateFromSeed(maskPaintPreviewSteps);
			MarkRoomPreviewDirty(room);
		}

		if (ImGui.Button("Clear Mask"))
		{
			using (CaptureRoomUndo("Clear Neural Texture Mask", room))
				ActorSpecs.SetProperty(actor, nameof(NeuralTextureActor.PaintedMask), JsonValue.Create(""));
			if (FindLiveNeuralTextureActor(actorId) is { } live)
			{
				live.PaintedMask = "";
				live.SimulateFromSeed(maskPaintPreviewSteps);
			}
			MarkRoomPreviewDirty(room);
		}
		ImGui.TextDisabled("Drag in the room to paint. Release to resimulate.\nRight-drag erases. Esc stops.");
	}

	static int NeuralTextureWeightsSlot(string propertyName) => propertyName switch
	{
		nameof(NeuralTextureActor.WeightsFile) => 0,
		nameof(NeuralTextureActor.WeightsFile1) => 1,
		nameof(NeuralTextureActor.WeightsFile2) => 2,
		nameof(NeuralTextureActor.WeightsFile3) => 3,
		_ => -1,
	};

	static bool IsHiddenWallTileTypeProperty(ActorSpecs.EditableProperty property)
		=> property.Type == typeof(string) && string.Equals(property.JsonName, "TileType", StringComparison.OrdinalIgnoreCase);

	bool DrawHiddenWallTileTypeButtons(string value, Action<string> setValue)
	{
		var current = NormalizeHiddenWallTileType(value);
		var changed = false;

		changed |= DrawHiddenWallTileTypeButton("castle", "Castle", current, setValue);
		ImGui.SameLine();
		changed |= DrawHiddenWallTileTypeButton("grass", "Grass", current, setValue);

		return changed;
	}

	bool DrawHiddenWallTileTypeButton(string tileType, string label, string current, Action<string> setValue)
	{
		var image = Assets.GetTileset(tileType)?.Tiles.FirstOrDefault() ?? new Subtexture();
		if (!ImGuiSubtextureButton(tileType, image, label, current == tileType, new Vector2(ButtonSize, ButtonSize)))
			return false;

		if (current != tileType)
			setValue(tileType);
		return current != tileType;
	}

	static string NormalizeHiddenWallTileType(string value)
		=> value == "grass" ? "grass" : "castle";

	// Dual tilesets a MovingPlatform can be skinned with (each has generated color + normal tiles).
	static readonly (string Tileset, string Label)[] PlatformMaterials =
	[
		("metal-dual", "Metal"),
		("castle-dual", "Castle"),
		("stone-dual", "Stone"),
	];

	static bool IsMovingPlatformMaterialProperty(ActorSpecs.EditableProperty property)
		=> property.Type == typeof(string) && string.Equals(property.JsonName, "Material", StringComparison.OrdinalIgnoreCase);

	bool DrawMovingPlatformMaterialButtons(string value, Action<string> setValue)
	{
		var changed = false;
		for (var i = 0; i < PlatformMaterials.Length; i++)
		{
			var (tileset, label) = PlatformMaterials[i];
			if (i > 0)
				ImGui.SameLine();

			var image = Assets.GetTileset(tileset)?.Tiles.FirstOrDefault() ?? new Subtexture();
			if (ImGuiSubtextureButton(tileset, image, label, value == tileset, new Vector2(ButtonSize, ButtonSize)) && value != tileset)
			{
				setValue(tileset);
				changed = true;
			}
		}
		return changed;
	}

	static float BeginInspectorPropertyRow(string label, bool hasResetButton)
	{
		var available = ImGui.GetContentRegionAvail().X;
		var labelWidth = MathF.Min(92, MathF.Max(56, available * 0.38f));
		var resetWidth = hasResetButton ? ImGui.CalcTextSize("Reset").X + ImGui.GetStyle().FramePadding.X * 2 + ImGui.GetStyle().ItemSpacing.X : 0;
		var controlWidth = MathF.Max(72, available - labelWidth - resetWidth - ImGui.GetStyle().ItemSpacing.X);

		ImGui.AlignTextToFramePadding();
		ImGui.TextUnformatted(label);
		if (ImGui.IsItemHovered() && ImGui.CalcTextSize(label).X > labelWidth)
			ImGui.SetTooltip(label);
		ImGui.SameLine(labelWidth);

		return controlWidth;
	}

	void BeginPrefabUndo(string specId)
	{
		if (!undo.HasPending(PrefabUndoSlot, specId))
			undo.OpenPending(PrefabUndoSlot, specId, "Edit Prefab", new ActorSpecUndoTarget(specId));
	}

	void CommitPrefabUndo()
		=> undo.CommitPending(PrefabUndoSlot);

	static RectInt RoomActorBounds(Room room, JsonObject actor)
	{
		var local = ActorSpecs.ReadPoint(actor, "Position") ?? Point2.Zero;
		var position = room.WorldBounds.TopLeft + local;
		var resolved = ActorSpecs.Resolve(actor);
		if (ResizableRoomRectTypes.TryGetValue(ActorSpecs.ReadString(resolved, "Type") ?? "", out var info))
		{
			var size = ActorSpecs.ReadPoint(resolved, "Size") ?? info.DefaultSize;
			return new RectInt(position.X, position.Y, size.X, size.Y);
		}
		if (IsPropActor(actor) && Assets.GetSprite(ActorSpecs.ReadString(resolved, nameof(Prop.SpriteName)) ?? "") is { } sprite)
			return Factory.ActorPaletteEntry.GetDefaultEditorBounds(sprite, sprite.ResolveOrigin(SpriteAnchor.TopLeft)) + position;

		if (FindRoomActorEntry(actor) is { } entry)
			return (entry.EditorBounds ?? entry.DefaultEditorBounds) + position;

		return new RectInt(position.X - 4, position.Y - 4, 8, 8);
	}

	static bool IsHiddenWallActor(JsonObject actor)
		=> ActorSpecs.ReadString(ActorSpecs.Resolve(actor), "Type") == "HiddenWall";

	static bool IsRevealDoorActor(JsonObject actor)
		=> ActorSpecs.ReadString(ActorSpecs.Resolve(actor), "Type") == "RevealDoor";

	static bool IsPropActor(JsonObject actor)
		=> string.Equals(ActorSpecs.ReadString(ActorSpecs.Resolve(actor), "Type"), nameof(Prop), StringComparison.OrdinalIgnoreCase);

	static bool IsNeuralTextureActor(JsonObject actor)
		=> string.Equals(ActorSpecs.ReadString(ActorSpecs.Resolve(actor), "Type"), nameof(NeuralTextureActor), StringComparison.OrdinalIgnoreCase);

	static bool IsLinkedDoorActor(JsonObject actor)
		=> ActorSpecs.ReadString(ActorSpecs.Resolve(actor), "Type") == "LinkedDoor";

	static bool IsSwitchActor(JsonObject actor)
		=> string.Equals(ActorSpecs.GetTypeName(actor), nameof(Switch), StringComparison.OrdinalIgnoreCase);

	static Dictionary<string, ActorEditor>? actorEditors;
	static ActorEditor? EditorFor(JsonObject actor)
	{
		actorEditors ??= BuildActorEditors();
		return actorEditors.GetValueOrDefault(ActorSpecs.GetTypeName(actor) ?? "");
	}

	static Dictionary<string, ActorEditor> BuildActorEditors()
	{
		var result = new Dictionary<string, ActorEditor>(StringComparer.OrdinalIgnoreCase);
		foreach (var type in typeof(Manager).Assembly.GetTypes())
		{
			if (type.IsAbstract || !typeof(ActorEditor).IsAssignableFrom(type))
				continue;
			if (Activator.CreateInstance(type, nonPublic: true) is ActorEditor editor)
				result[editor.ActorType.Name] = editor;
		}
		return result;
	}

	bool RunActorGizmos(Room room, int actorIndex, Vector2 worldMouse, float lineWeight,
		bool draw, bool canInteract, bool keyboardBlocked)
	{
		if (actorIndex < 0 || actorIndex >= room.Actors.Count || EditorFor(room.Actors[actorIndex]) is not { } editor)
			return false;

		var actor = new EditorActor(room, room.Actors[actorIndex]);
		var gizmos = new EditorGizmos(this, actor, worldMouse, lineWeight, draw, canInteract, keyboardBlocked);
		editor.Gizmos(actor, ref gizmos);
		return gizmos.ConsumedPress;
	}

	static HashSet<string>? pointPathActorTypes;
	static HashSet<string> PointPathActorTypes
		=> pointPathActorTypes ??= BuildActorCapabilityTypes<IPointPath>();

	static HashSet<string>? activatableActorTypes;
	static HashSet<string> ActivatableActorTypes
		=> activatableActorTypes ??= BuildActorCapabilityTypes<IActivatable>();

	static HashSet<string> BuildActorCapabilityTypes<T>()
	{
		var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (var (name, type) in ActorSpecs.ActorTypes)
			if (typeof(T).IsAssignableFrom(type))
				result.Add(name);
		return result;
	}

	static bool IsPointPathActor(JsonObject actor)
		=> PointPathActorTypes.Contains(ActorSpecs.GetTypeName(actor) ?? "");

	static bool IsActivatableActor(JsonObject actor)
		=> ActivatableActorTypes.Contains(ActorSpecs.GetTypeName(actor) ?? "");

	static Point2[] PointPathPoints(JsonObject actor)
	{
		if (PointPathNodes(actor) is not { } nodes)
			return [Point2.Zero, new Point2(Game.TileSize * 8, 0)];

		var points = new Point2[nodes.Count];
		for (var i = 0; i < points.Length; i++)
			points[i] = PointPathPoint(nodes, i);
		return points;
	}

	static JsonArray? PointPathNodes(JsonObject actor)
		=> ActorSpecs.GetResolvedProperty(actor, nameof(IPointPath.Points)) as JsonArray;

	static int PointPathPointCount(JsonArray? nodes) => nodes?.Count ?? 2;

	static Point2 PointPathPoint(JsonArray? nodes, int index)
		=> nodes != null
			? ActorSpecs.ReadPoint(nodes[index]) ?? Point2.Zero
			: index == 0 ? Point2.Zero : new Point2(Game.TileSize * 8, 0);

	// Actor types that author as a tile-snapped room rect, sharing the resize-handle UI: every
	// actor implementing IResizable, mapped to the default size of a freshly placed instance (its
	// public static DefaultSize). Built once from the actor type registry, so a new resizable
	// actor just implements IResizable -- no edits here.
	readonly record struct ResizableInfo(Point2 DefaultSize, ResizeAxes Axes);

	static Dictionary<string, ResizableInfo>? resizableRoomRectTypes;
	static Dictionary<string, ResizableInfo> ResizableRoomRectTypes
		=> resizableRoomRectTypes ??= BuildResizableRoomRectTypes();

	static Dictionary<string, ResizableInfo> BuildResizableRoomRectTypes()
	{
		var result = new Dictionary<string, ResizableInfo>(StringComparer.OrdinalIgnoreCase);
		foreach (var (name, type) in ActorSpecs.ActorTypes)
		{
			if (!typeof(IResizable).IsAssignableFrom(type))
				continue;

			var sizeField = type.GetField("DefaultSize", BindingFlags.Public | BindingFlags.Static)
				?? throw new InvalidOperationException($"Resizable actor '{type.Name}' is missing a public static \"DefaultSize\" field.");
			var axesField = type.GetField(nameof(Jumpthru.ResizableAxes), BindingFlags.Public | BindingFlags.Static);
			var axes = axesField != null ? (ResizeAxes)axesField.GetValue(null)! : ResizeAxes.Both;
			result[name] = new ResizableInfo((Point2)sizeField.GetValue(null)!, axes);
		}
		return result;
	}

	static bool IsResizableRoomRectActor(JsonObject actor)
		=> ResizableRoomRectTypes.ContainsKey(ActorSpecs.ReadString(ActorSpecs.Resolve(actor), "Type") ?? "");

	static ResizableInfo ResizableRoomRectInfo(JsonObject actor)
		=> ResizableRoomRectTypes.GetValueOrDefault(
			ActorSpecs.ReadString(ActorSpecs.Resolve(actor), "Type") ?? "", new ResizableInfo(new(Game.TileSize * 2, Game.TileSize * 2), ResizeAxes.Both));

	static Point2 ResizableRoomRectDefaultSize(JsonObject actor)
		=> ResizableRoomRectInfo(actor).DefaultSize;

	static bool ResizeHandleAllowed(ResizeHandle handle, ResizeAxes axes)
		=> axes switch
		{
			ResizeAxes.X => handle is ResizeHandle.Left or ResizeHandle.Right,
			ResizeAxes.Y => handle is ResizeHandle.Top or ResizeHandle.Bottom,
			_ => true,
		};

	static Factory.ActorPaletteEntry? FindRoomActorEntry(JsonObject actor)
	{
		var specId = ActorSpecs.ReadString(actor, "From");
		var typeName = ActorSpecs.ReadString(actor, "Type");

		foreach (var entry in Factory.ActorPalette)
		{
			if (specId != null && entry.ActorSpecId == specId)
				return entry;
			if (typeName != null && entry.ActorTypeName == typeName && entry.ActorSpecId == null)
				return entry;
		}

		return null;
	}

	int HitTestRoomActor(Room room, Vector2 world)
	{
		for (var i = room.Actors.Count - 1; i >= 0; i--)
			if (RoomActorContains(room, room.Actors[i], world))
				return i;

		return -1;
	}

	// Fills `into` with every actor whose bounds contain `world`, topmost first (higher indices
	// render on top), so click-to-cycle can walk the whole overlapping stack.
	void HitTestRoomActors(Room room, Vector2 world, List<int> into)
	{
		into.Clear();
		for (var i = room.Actors.Count - 1; i >= 0; i--)
			if (RoomActorContains(room, room.Actors[i], world))
				into.Add(i);
	}

	bool RoomActorContains(Room room, JsonObject actor, Vector2 world)
	{
		if (RoomActorBounds(room, actor).Contains(world))
			return true;
		if (!IsPointPathActor(actor))
			return false;

		var points = PointPathNodes(actor);
		var count = PointPathPointCount(points);
		var origin = PointPathOrigin(room, actor);
		var tolerance = MathF.Max(3, 6 / editorZoom);
		for (var i = 1; i < count; i++)
			if (new Line(origin + PointPathPoint(points, i - 1), origin + PointPathPoint(points, i)).DistanceSquared(world) <= tolerance * tolerance)
				return true;
		return false;
	}

	// Which actor a left-press acts on. A press holds onto the current selection whenever it is
	// under the cursor, so handles and drags always affect the actor the inspector is showing;
	// otherwise it takes the topmost. The selection is only given up for something more specific
	// than the topmost hit (a small actor sitting on a room-sized one), so clicking a light on top
	// of a selected backdrop actor still grabs the light. Reaching an actor underneath is a click
	// that doesn't drag; see CycleRoomActorSelection.
	int PickRoomActorForPress(Room room, Vector2 world)
	{
		HitTestRoomActors(room, world, actorPickScratch);
		actorPressCursor = Cursor;
		actorPressCanCycle = false;
		if (actorPickScratch.Count == 0)
			return -1;

		var topmost = actorPickScratch[0];
		if (!actorPickScratch.Contains(selectedRoomActorIndex))
			return topmost;

		if (RoomActorPickArea(room, selectedRoomActorIndex) > RoomActorPickArea(room, topmost))
			return topmost;

		actorPressCanCycle = actorPickScratch.Count > 1;
		return selectedRoomActorIndex;
	}

	static long RoomActorPickArea(Room room, int index)
	{
		var bounds = RoomActorBounds(room, room.Actors[index]);
		return (long)bounds.Width * bounds.Height;
	}

	// A left-click that never moved is a request for the actor underneath the one already selected.
	void CycleRoomActorSelection(Room room, Vector2 world)
	{
		HitTestRoomActors(room, world, actorPickScratch);
		var current = actorPickScratch.IndexOf(selectedRoomActorIndex);
		if (current < 0)
			return;

		using (CaptureSelectionUndo("Select Actor"))
			selectedRoomActorIndex = actorPickScratch[(current + 1) % actorPickScratch.Count];
	}

	// The lattice rooms tile on: one gameplay screen wide, one tile taller (Rows = Height/TileSize + 1).
	// New rooms snap to this, the orientation grid is spaced by it, and room moves snap to it by default.
	static Point2 RoomLatticeTiles => new(Game.Columns, Game.Rows);
	static Point2 RoomLatticeSize => new(Game.Columns * Game.TileSize, Game.Rows * Game.TileSize);

	// A subtle screen-sized grid for orientation when zoomed far enough out to see several
	// gameplay screens at once. It's a fixed world grid anchored at the origin (a line passes
	// through 0,0), spaced one room-cell apart -- independent of any room's size or position.
	// We space by the room lattice (Columns x Rows tiles), not raw screen pixels: Rows is
	// Height/TileSize + 1, so a default room is one tile taller than the screen and tiles on a
	// 320x184 lattice. Spacing the grid by 320x180 would drift 4px/row out of alignment.
	void DrawEditorScreenGrid(Rect visibleWorld)
	{
		const float minScreensVisible = 1f;
		float screenW = RoomLatticeSize.X;
		float screenH = RoomLatticeSize.Y;
		if (MathF.Max(visibleWorld.Width / screenW, visibleWorld.Height / screenH) < minScreensVisible)
			return;

		var weight = 1.0f / editorZoom;
		var color = Color.Gray * 0.22f;

		var left = visibleWorld.X;
		var top = visibleWorld.Y;
		var right = visibleWorld.X + visibleWorld.Width;
		var bottom = visibleWorld.Y + visibleWorld.Height;

		for (var x = MathF.Floor(left / screenW) * screenW; x <= right; x += screenW)
			batcher.Line(new Vector2(x, top), new Vector2(x, bottom), weight, color);
		for (var y = MathF.Floor(top / screenH) * screenH; y <= bottom; y += screenH)
			batcher.Line(new Vector2(left, y), new Vector2(right, y), weight, color);
	}

	void DrawRoomActorOverlays(Room room, float lineWeight, int hoveredIndex, Vector2 worldMouse)
	{
		for (var i = 0; i < room.Actors.Count; i++)
		{
			var selected = i == selectedRoomActorIndex;
			if ((DragKind is EditorDragKind.RoomActor or EditorDragKind.ActorPathPoint or EditorDragKind.ActorGizmo or EditorDragKind.ActorResize) && !selected)
				continue;

			var hovered = i == hoveredIndex;
			var bounds = RoomActorBounds(room, room.Actors[i]);
			if (!selected && !hovered && !bounds.Inflate(bounds.Width * 1.5f, bounds.Height * 1.5f).Contains(worldMouse))
				continue;

			var color = selected ? highlight : hovered ? Color.White : Color.Gray;
			batcher.RectLine(bounds.Inflate(lineWeight * 2), lineWeight * 2, color);

			if (selected && IsResizableRoomRectActor(room.Actors[i]))
				DrawHiddenWallResizeHandles(bounds, lineWeight, ResizableRoomRectInfo(room.Actors[i]).Axes);
		}
	}

	void DrawPointPaths(Room room, float lineWeight)
	{
		for (var actorIndex = 0; actorIndex < room.Actors.Count; actorIndex++)
		{
			var actor = room.Actors[actorIndex];
			if (!IsPointPathActor(actor))
				continue;

			var points = PointPathNodes(actor);
			var count = PointPathPointCount(points);
			var origin = PointPathOrigin(room, actor);
			var selected = actorIndex == selectedRoomActorIndex;
			var typeName = ActorSpecs.GetTypeName(actor);
			var color = typeName == nameof(BurrowLine) ? new Color(0x54e6c1) : new Color(0xffb454);
			var weight = lineWeight * (selected ? 3 : 2);
			for (var i = 1; i < count; i++)
				batcher.Line(origin + PointPathPoint(points, i - 1), origin + PointPathPoint(points, i), weight, color * (selected ? 1 : 0.65f));

			if (!selected)
				continue;
			var radius = PointPathHandleRadius(lineWeight);
			for (var i = 0; i < count; i++)
			{
				var point = origin + PointPathPoint(points, i);
				if (typeName == nameof(BurrowLine) && BurrowLinePointConnected(room, actorIndex, i, point))
					batcher.CircleLine(point, radius * 1.65f, lineWeight * 2, 16, color);
				batcher.Circle(point, radius, 12, color);
				batcher.CircleLine(point, radius, lineWeight, 12, Color.White);
			}
		}
	}

	bool BurrowLinePointConnected(Room room, int sourceIndex, int sourcePoint, Vector2 point)
	{
		var maxDistanceSquared = BurrowLine.JunctionDistance * BurrowLine.JunctionDistance;
		for (var actorIndex = 0; actorIndex < room.Actors.Count; actorIndex++)
		{
			var actor = room.Actors[actorIndex];
			if (actorIndex == sourceIndex || !string.Equals(ActorSpecs.GetTypeName(actor), nameof(BurrowLine), StringComparison.OrdinalIgnoreCase))
				continue;

			var points = PointPathNodes(actor);
			var count = PointPathPointCount(points);
			var origin = PointPathOrigin(room, actor);
			for (var i = 0; i < count; i++)
				if ((actorIndex != sourceIndex || i != sourcePoint) &&
					Vector2.DistanceSquared(point, origin + PointPathPoint(points, i)) <= maxDistanceSquared)
					return true;
		}

		return false;
	}

	int HitTestPointPathHandle(Room room, int actorIndex, Vector2 world, float lineWeight)
	{
		if (actorIndex < 0 || actorIndex >= room.Actors.Count || !IsPointPathActor(room.Actors[actorIndex]))
			return -1;

		var actor = room.Actors[actorIndex];
		var points = PointPathNodes(actor);
		var origin = PointPathOrigin(room, actor);
		var radius = PointPathHandleRadius(lineWeight) * 1.5f;
		for (var i = PointPathPointCount(points) - 1; i >= 0; i--)
			if (Vector2.DistanceSquared(world, origin + PointPathPoint(points, i)) <= radius * radius)
				return i;
		return -1;
	}

	static Vector2 PointPathOrigin(Room room, JsonObject actor)
		=> room.WorldBounds.TopLeft + (ActorSpecs.ReadPoint(actor, "Position") ?? Point2.Zero);

	static float PointPathHandleRadius(float lineWeight) => MathF.Max(3, lineWeight * 5);

	void DrawRevealDoorMarkers(Room room, float lineWeight)
	{
		foreach (var actor in room.Actors)
			if (IsRevealDoorActor(actor))
				DrawRevealDoorMarker(room, actor, lineWeight);
	}

	void DrawRevealDoorMarker(Room room, JsonObject actor, float lineWeight)
	{
		var bounds = RoomActorBounds(room, actor);
		var direction = Enum.TryParse<RevealDoor.RevealDirection>(ActorSpecs.ReadString(ActorSpecs.Resolve(actor), "Direction"), true, out var parsed)
			? parsed
			: RevealDoor.RevealDirection.Right;
		var arrow = direction switch
		{
			RevealDoor.RevealDirection.Left => -Vector2.UnitX,
			RevealDoor.RevealDirection.Up => -Vector2.UnitY,
			RevealDoor.RevealDirection.Down => Vector2.UnitY,
			_ => Vector2.UnitX,
		};
		var perpendicular = new Vector2(-arrow.Y, arrow.X);
		var center = (Vector2)bounds.Center;
		var length = MathF.Min(bounds.Width, bounds.Height) * 0.35f;
		var tip = center + arrow * length;
		var baseCenter = center + arrow * length * 0.15f;
		var color = new Color(0xff5ca8);

		batcher.Rect(bounds, color * 0.35f);
		batcher.RectLine(bounds, lineWeight * 2, color);
		batcher.Line(center - arrow * length * 0.65f, baseCenter, lineWeight * 2, Color.White);
		batcher.Triangle(tip, baseCenter + perpendicular * length * 0.45f, baseCenter - perpendicular * length * 0.45f, Color.White);
	}

	void DrawHiddenWallResizeHandles(RectInt bounds, float lineWeight, ResizeAxes axes)
	{
		var size = HiddenWallHandleSize(lineWeight);
		foreach (var handle in Enum.GetValues<ResizeHandle>())
		{
			if (handle == ResizeHandle.None || !ResizeHandleAllowed(handle, axes))
				continue;

			var rect = HiddenWallHandleRect(bounds, handle, size);
			var color = DragKind == EditorDragKind.ActorResize && editorDrag.ResizeHandle == handle ? Color.White : highlight;
			batcher.Rect(rect, Color.Black * 0.75f);
			batcher.RectLine(rect, lineWeight * 2, color);
		}
	}

	void DrawRoomGeometryHandles(Room room, float lineWeight)
	{
		var bounds = room.WorldBounds;
		var color = DragKind == EditorDragKind.RoomGeometry ? Color.White : highlight;
		batcher.RectLine(bounds.Inflate(lineWeight * 3), lineWeight * 2, color);

		var size = HiddenWallHandleSize(lineWeight);
		foreach (var handle in Enum.GetValues<ResizeHandle>())
		{
			if (handle == ResizeHandle.None)
				continue;

			var rect = HiddenWallHandleRect(bounds, handle, size);
			var handleColor = DragKind == EditorDragKind.RoomGeometry && editorDrag.RoomGeometryHandle == handle ? Color.White : highlight;
			batcher.Rect(rect, Color.Black * 0.75f);
			batcher.RectLine(rect, lineWeight * 2, handleColor);
		}
	}

	ResizeHandle HitTestHiddenWallResizeHandle(Room room, Vector2 world, float lineWeight)
	{
		if (selectedRoomActorIndex < 0 || selectedRoomActorIndex >= room.Actors.Count)
			return ResizeHandle.None;

		var actor = room.Actors[selectedRoomActorIndex];
		if (!IsResizableRoomRectActor(actor))
			return ResizeHandle.None;

		var axes = ResizableRoomRectInfo(actor).Axes;
		var bounds = RoomActorBounds(room, actor);
		var size = HiddenWallHandleSize(lineWeight);
		foreach (var handle in new[]
				 {
					 ResizeHandle.TopLeft,
					 ResizeHandle.TopRight,
					 ResizeHandle.BottomLeft,
					 ResizeHandle.BottomRight,
					 ResizeHandle.Left,
					 ResizeHandle.Right,
					 ResizeHandle.Top,
					 ResizeHandle.Bottom
				 })
		{
			if (ResizeHandleAllowed(handle, axes) && HiddenWallHandleRect(bounds, handle, size).Contains(world))
				return handle;
		}

		return ResizeHandle.None;
	}

	ResizeHandle HitTestRoomGeometryHandle(Room room, Vector2 world, float lineWeight)
	{
		var bounds = room.WorldBounds;
		var size = HiddenWallHandleSize(lineWeight);
		foreach (var handle in new[]
				 {
					 ResizeHandle.TopLeft,
					 ResizeHandle.TopRight,
					 ResizeHandle.BottomLeft,
					 ResizeHandle.BottomRight,
					 ResizeHandle.Left,
					 ResizeHandle.Right,
					 ResizeHandle.Top,
					 ResizeHandle.Bottom
				 })
		{
			if (HiddenWallHandleRect(bounds, handle, size).Contains(world))
				return handle;
		}

		return ResizeHandle.None;
	}

	static float HiddenWallHandleSize(float lineWeight) => MathF.Max(3, lineWeight * 8);

	static Rect HiddenWallHandleRect(RectInt bounds, ResizeHandle handle, float size)
	{
		var half = size * 0.5f;
		var x = handle switch
		{
			ResizeHandle.Left or ResizeHandle.TopLeft or ResizeHandle.BottomLeft => bounds.X,
			ResizeHandle.Right or ResizeHandle.TopRight or ResizeHandle.BottomRight => bounds.X + bounds.Width,
			_ => bounds.X + bounds.Width * 0.5f
		};
		var y = handle switch
		{
			ResizeHandle.Top or ResizeHandle.TopLeft or ResizeHandle.TopRight => bounds.Y,
			ResizeHandle.Bottom or ResizeHandle.BottomLeft or ResizeHandle.BottomRight => bounds.Y + bounds.Height,
			_ => bounds.Y + bounds.Height * 0.5f
		};
		return new Rect(x - half, y - half, size, size);
	}

	void DrawPlaytestPlayerMarker(Room room)
	{
		if (playtestPlayerPosition is not { } remembered || remembered.RoomId != room.Id)
			return;

		var sprite = Assets.GetSprite("player");
		if (sprite == null || sprite.Frames.Count <= 0)
			return;

		batcher.Image(sprite.Frames[0].Subtexture, remembered.Position, sprite.Origin, Vector2.One, 0, Color.White * 0.25f);
	}

	void MoveRoomActorTo(Room room, int actorIndex, Vector2 world, bool snap)
	{
		if (actorIndex < 0 || actorIndex >= room.Actors.Count)
			return;

		var actor = room.Actors[actorIndex];
		var offset = GetRoomActorPaletteOffset(actor);
		EditorFor(actor)?.BeforeMove(new EditorActor(room, actor));

		var targetLocal = world - editorDrag.ActorOffset - (Vector2)room.WorldBounds.TopLeft;
		var local = snap
			? SnapRoomActorLocalPosition(targetLocal, offset)
			: (Point2)targetLocal;
		actor["Position"] = new JsonArray(local.X, local.Y);
		editorGameDirty = true;
	}

	void MovePointPathPoint(Room room, int actorIndex, int pointIndex, Vector2 world, bool snap)
	{
		if (actorIndex < 0 || actorIndex >= room.Actors.Count)
			return;

		var actor = room.Actors[actorIndex];
		var points = PointPathPoints(actor);
		if (pointIndex < 0 || pointIndex >= points.Length)
			return;

		var local = world - PointPathOrigin(room, actor);
		points[pointIndex] = snap
			? (local / Game.TileSize).RoundToPoint2() * Game.TileSize
			: local.RoundToPoint2();
		ActorSpecs.SetProperty(actor, nameof(IPointPath.Points), ActorSpecs.ToJsonNode(points));
		editorGameDirty = true;
	}

	static Point2 GetRoomActorPaletteOffset(JsonObject actor)
	{
		var specId = ActorSpecs.ReadString(actor, "From");
		var typeName = ActorSpecs.ReadString(actor, "Type");
		var propSpriteName = typeName == nameof(Prop) ? ActorSpecs.ReadString(actor, nameof(Prop.SpriteName)) : null;
		if (specId == null && typeName == null)
			return Point2.Zero;

		foreach (var entry in Factory.ActorPalette)
		{
			if (propSpriteName != null && string.Equals(entry.PropSpriteName, propSpriteName, StringComparison.OrdinalIgnoreCase))
				return entry.Offset;
			if ((specId != null && entry.ActorSpecId == specId) ||
				(typeName != null && entry.ActorTypeName == typeName && entry.ActorSpecId == null))
				return entry.Offset;
		}

		return Point2.Zero;
	}

	static bool RoomActorPositionIsSnapped(JsonObject actor)
	{
		var position = ActorSpecs.ReadPoint(actor, "Position") ?? Point2.Zero;
		return SnapRoomActorLocalPosition(position, GetRoomActorPaletteOffset(actor)) == position;
	}

	bool RoomActorDragShouldSnap(bool keyboardBlocked)
	{
		var shiftDown = !keyboardBlocked && Input.Keyboard.Shift;
		return editorDrag.ActorSnapsByDefault ? !shiftDown : shiftDown;
	}

	EditorDragKind DragKind => editorDrag.Kind;
	bool TilePlacing => DragKind == EditorDragKind.TilePaint;

	void ClearEditorDragState()
	{
		editorDrag = EditorDragState.None;
	}

	void CancelTilePaint()
	{
		if (DragKind == EditorDragKind.TilePaint)
			ClearEditorDragState();
		tileTypePlacing = brush;
	}

	void CommitEditorDragState()
	{
		ClearEditorDragState();
		undo.CommitPending(ActorMoveUndoSlot);
		undo.CommitPending(RoomGeometryUndoSlot);
	}

	static Point2 SnapRoomActorLocalPosition(Vector2 local, Point2 offset)
	{
		var adjusted = local - (Vector2)offset;
		return new Point2(
			(int)MathF.Floor(adjusted.X / Game.TileSize) * Game.TileSize + offset.X,
			(int)MathF.Floor(adjusted.Y / Game.TileSize) * Game.TileSize + offset.Y);
	}

	void ResizeHiddenWallActor(Room room, int actorIndex, ResizeHandle handle, Vector2 world, Point2 tile, bool snap)
	{
		if (actorIndex < 0 || actorIndex >= room.Actors.Count || handle == ResizeHandle.None)
			return;

		var actor = room.Actors[actorIndex];
		if (!IsResizableRoomRectActor(actor))
			return;

		var resolved = ActorSpecs.Resolve(actor);
		var position = ActorSpecs.ReadPoint(actor, "Position") ?? Point2.Zero;
		var size = ActorSpecs.ReadPoint(resolved, "Size") ?? ResizableRoomRectDefaultSize(actor);
		var cursor = snap ? tile * Game.TileSize : (Point2)(world - (Vector2)room.WorldBounds.TopLeft);

		var left = position.X;
		var top = position.Y;
		var right = position.X + size.X;
		var bottom = position.Y + size.Y;

		switch (handle)
		{
			case ResizeHandle.Left:
			case ResizeHandle.TopLeft:
			case ResizeHandle.BottomLeft:
				left = cursor.X;
				break;
			case ResizeHandle.Right:
			case ResizeHandle.TopRight:
			case ResizeHandle.BottomRight:
				right = cursor.X;
				break;
		}

		switch (handle)
		{
			case ResizeHandle.Top:
			case ResizeHandle.TopLeft:
			case ResizeHandle.TopRight:
				top = cursor.Y;
				break;
			case ResizeHandle.Bottom:
			case ResizeHandle.BottomLeft:
			case ResizeHandle.BottomRight:
				bottom = cursor.Y;
				break;
		}

		var roomWidth = room.Size.X * Game.TileSize;
		var roomHeight = room.Size.Y * Game.TileSize;
		left = Math.Clamp(left, 0, roomWidth - Game.TileSize);
		top = Math.Clamp(top, 0, roomHeight - Game.TileSize);
		right = Math.Clamp(right, Game.TileSize, roomWidth);
		bottom = Math.Clamp(bottom, Game.TileSize, roomHeight);

		if (right - left < Game.TileSize)
		{
			if (handle is ResizeHandle.Left or ResizeHandle.TopLeft or ResizeHandle.BottomLeft)
				left = right - Game.TileSize;
			else
				right = left + Game.TileSize;
		}

		if (bottom - top < Game.TileSize)
		{
			if (handle is ResizeHandle.Top or ResizeHandle.TopLeft or ResizeHandle.TopRight)
				top = bottom - Game.TileSize;
			else
				bottom = top + Game.TileSize;
		}

		left = Math.Clamp(left, 0, roomWidth - Game.TileSize);
		top = Math.Clamp(top, 0, roomHeight - Game.TileSize);
		right = Math.Clamp(right, left + Game.TileSize, roomWidth);
		bottom = Math.Clamp(bottom, top + Game.TileSize, roomHeight);

		actor["Position"] = new JsonArray(left, top);
		actor["Size"] = new JsonArray(right - left, bottom - top);
		editorGameDirty = true;
	}

	void ApplyRoomGeometryDrag(Room room, Vector2 world)
	{
		var mouseTile = WorldToRoomGeometryTile(world);
		var current = new RectInt(room.TileSpacePosition, room.Size);
		RectInt candidate;
		if (editorDrag.RoomGeometryHandle == ResizeHandle.None)
		{
			// Moving: snap the top-left to the room lattice (whole-screen steps) by default, since
			// most rooms are screen-sized and want to line up; hold Shift for free per-tile dragging.
			var position = mouseTile - editorDrag.RoomGeometryOffset;
			if (!Input.Keyboard.Shift)
				position = SnapToRoomLattice(position);
			candidate = new RectInt(position, room.Size);
		}
		else
			candidate = ResizeRoomGeometryBounds(current, editorDrag.RoomGeometryHandle, mouseTile);

		if (candidate.Width <= 0 || candidate.Height <= 0 || RoomGeometryOverlaps(room, candidate))
			return;

		if (candidate.Position == room.TileSpacePosition && candidate.Size == room.Size)
			return;

		if (candidate.Size == room.Size)
			MoveRoomGeometry(room, candidate.Position);
		else
			ResizeRoomGeometry(room, candidate);

		selectedRoomId = room.Id;
		roomCreatePosition = room.TileSpacePosition;
		editorGameDirty = true;
		InvalidateRoomPreview(room);
	}

	static Point2 WorldToRoomGeometryTile(Vector2 world)
		=> new(
			(int)MathF.Floor(world.X / Game.TileSize),
			(int)MathF.Floor(world.Y / Game.TileSize));

	// Round a tile position to the nearest room-lattice cell, so screen-sized rooms line up edge-to-edge.
	static Point2 SnapToRoomLattice(Point2 tile)
	{
		var lattice = RoomLatticeTiles;
		return new(
			(int)MathF.Round((float)tile.X / lattice.X) * lattice.X,
			(int)MathF.Round((float)tile.Y / lattice.Y) * lattice.Y);
	}

	static RectInt ResizeRoomGeometryBounds(RectInt bounds, ResizeHandle handle, Point2 mouseTile)
	{
		var left = bounds.Left;
		var top = bounds.Top;
		var right = bounds.Right;
		var bottom = bounds.Bottom;

		if (handle is ResizeHandle.Left or ResizeHandle.TopLeft or ResizeHandle.BottomLeft)
			left = Math.Min(mouseTile.X, right - 1);
		if (handle is ResizeHandle.Right or ResizeHandle.TopRight or ResizeHandle.BottomRight)
			right = Math.Max(mouseTile.X + 1, left + 1);
		if (handle is ResizeHandle.Top or ResizeHandle.TopLeft or ResizeHandle.TopRight)
			top = Math.Min(mouseTile.Y, bottom - 1);
		if (handle is ResizeHandle.Bottom or ResizeHandle.BottomLeft or ResizeHandle.BottomRight)
			bottom = Math.Max(mouseTile.Y + 1, top + 1);

		return new RectInt(left, top, right - left, bottom - top);
	}

	static bool RoomGeometryOverlaps(Room editing, RectInt candidate)
	{
		foreach (var room in Assets.RoomsById.Values)
		{
			if (room == editing)
				continue;
			if (candidate.Overlaps(new RectInt(room.TileSpacePosition, room.Size)))
				return true;
		}

		return false;
	}

	static void MoveRoomGeometry(Room room, Point2 position)
	{
		var oldCell = room.Cell;
		room.MoveTo(position);
		UpdateRoomCellIndex(room, oldCell);
	}

	static void ResizeRoomGeometry(Room room, RectInt bounds)
	{
		var oldCell = room.Cell;
		room.ResizeTo(bounds);
		UpdateRoomCellIndex(room, oldCell);
	}

	static void UpdateRoomCellIndex(Room room, Point2 oldCell)
	{
		if (Assets.Rooms.TryGetValue(oldCell, out var indexed) && indexed == room)
			Assets.Rooms.Remove(oldCell);
		Assets.Rooms[room.Cell] = room;
	}

	static bool RoomGeometryOverlaps(Point2 position, Point2 size, Room? exclude)
	{
		var candidate = new RectInt(position, size);
		foreach (var room in Assets.RoomsById.Values)
		{
			if (room == exclude)
				continue;
			if (candidate.Overlaps(new RectInt(room.TileSpacePosition, room.Size)))
				return true;
		}

		return false;
	}

	// Nearest free room-lattice cell to a desired tile, spiralling outward a lattice step at a time so
	// a pasted/duplicated room lands next to where it was asked for without overlapping existing rooms.
	Point2 FindFreeRoomCell(Point2 desiredTile, Point2 size)
	{
		var start = SnapToRoomLattice(desiredTile);
		var lattice = RoomLatticeTiles;
		for (var radius = 0; radius < 64; radius++)
			for (var dy = -radius; dy <= radius; dy++)
				for (var dx = -radius; dx <= radius; dx++)
				{
					if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != radius)
						continue;
					var pos = start + new Point2(dx * lattice.X, dy * lattice.Y);
					if (!RoomGeometryOverlaps(pos, size, null))
						return pos;
				}

		return start;
	}

	// Insert a room into the world (id + cell indexes), persist it, and refresh its preview/editor game.
	// The counterpart to RemoveRoomFromWorld; both are driven by RoomExistenceUndoTarget for undo/redo.
	void AddRoomToWorld(Room room)
	{
		Assets.RoomsById[room.Id] = room;
		UpdateRoomCellIndex(room, room.Cell);
		room.Save();
		InvalidateRoomPreview(room);
		editorGameDirty = true;
		SyncEditorGame();
	}

	void RemoveRoomFromWorld(Room room)
	{
		if (CurrentRoom == room)
			room.Save();

		Assets.DeleteRoom(room);
		if (selectedRoomId == room.Id)
			selectedRoomId = null;
		if (playtestPlayerPosition is { } remembered && remembered.RoomId == room.Id)
			playtestPlayerPosition = null;
		InvalidateRoomPreview(room);
		ResolveSelectedRoom();
		editorGameDirty = true;
		SyncEditorGame();
	}

	void CopyCurrentRoom()
	{
		if (CurrentRoom is { } room)
			roomClipboardJson = room.ToJsonString();
	}

	void CutCurrentRoom()
	{
		if (CurrentRoom is { } room)
		{
			roomClipboardJson = room.ToJsonString();
			DeleteRoom(room);
		}
	}

	void PasteRoomAtCursor()
	{
		if (roomClipboardJson is { } json)
			PasteRoomFromJson(json, WorldToRoomGeometryTile(ScreenToEditorWorld(Cursor, GameViewRect)));
	}

	void DuplicateCurrentRoom()
	{
		if (CurrentRoom is { } room)
			PasteRoomFromJson(room.ToJsonString(), room.TileSpacePosition + new Point2(room.Size.X, 0));
	}

	// Create a new room from serialized state and drop it at the nearest free lattice cell to desiredTile.
	void PasteRoomFromJson(string json, Point2 desiredTile)
	{
		CurrentRoom?.Save();

		var it = new Room(Assets.GenerateRandomRoomId(), desiredTile, Room.DefaultSize);
		it.LoadJsonState(json);                       // pulls in size/tiles/actors/settings (and the source position)
		it.MoveTo(FindFreeRoomCell(desiredTile, it.Size));

		using (CaptureRoomExistenceUndo("Paste Room", it))
		{
			AddRoomToWorld(it);
			SelectRoom(it);
			selectedRoomActorIndex = -1;
			editorRoomEntryDirection = null;
			SyncEditorGame();
		}
		// No camera tween: paste/duplicate happen while zoomed out arranging rooms, so we keep the view put.
	}

	void StampTiles(Room room, Point2 origin, int shift, int size, char tile)
	{
		for (var x = shift; x < shift + size; x++)
			for (var y = shift; y < shift + size; y++)
			{
				if (activeTileLayer == Factory.TileLayer.Background)
					room.SetBackground(origin + new Point2(x, y), tile, rebuildGeometry: false);
				else
					room.Set(origin + new Point2(x, y), tile, rebuildGeometry: false);
			}
		room.RebuildGeometry();
		editorGameDirty = true;
	}

	static RectInt NormalizeTileRect(Point2 a, Point2 b)
	{
		var left = Math.Min(a.X, b.X);
		var top = Math.Min(a.Y, b.Y);
		var right = Math.Max(a.X, b.X);
		var bottom = Math.Max(a.Y, b.Y);
		return new RectInt(left, top, right - left + 1, bottom - top + 1);
	}

	static RectInt ClampTileRect(RectInt rect, Room room)
	{
		var left = Math.Clamp(rect.Left, 0, room.Size.X);
		var top = Math.Clamp(rect.Top, 0, room.Size.Y);
		var right = Math.Clamp(rect.Right, 0, room.Size.X);
		var bottom = Math.Clamp(rect.Bottom, 0, room.Size.Y);
		return new RectInt(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
	}

	static Point2 ClampTile(Room room, Point2 tile)
		=> new(Math.Clamp(tile.X, 0, Math.Max(0, room.Size.X - 1)), Math.Clamp(tile.Y, 0, Math.Max(0, room.Size.Y - 1)));

	static bool TileRectContains(RectInt rect, Point2 tile)
		=> tile.X >= rect.Left && tile.X < rect.Right && tile.Y >= rect.Top && tile.Y < rect.Bottom;

	static bool TileRectOverlaps(RectInt a, RectInt b)
		=> a.Width > 0 && a.Height > 0 && b.Width > 0 && b.Height > 0 &&
		   a.Left < b.Right && a.Right > b.Left && a.Top < b.Bottom && a.Bottom > b.Top;

	static Rect TileRectToRoomWorldRect(Room room, RectInt rect)
		=> (new Rect(rect.X, rect.Y, rect.Width, rect.Height) * Game.TileSize) + (Vector2)room.WorldBounds.TopLeft;

	static RectInt WorldRectToRoomTileRect(Room room, Rect world)
	{
		var roomTopLeft = (Vector2)room.WorldBounds.TopLeft;
		var local = new Rect(world.X - roomTopLeft.X, world.Y - roomTopLeft.Y, world.Width, world.Height);
		var left = (int)MathF.Floor(local.Left / Game.TileSize);
		var top = (int)MathF.Floor(local.Top / Game.TileSize);
		var right = (int)MathF.Ceiling(local.Right / Game.TileSize);
		var bottom = (int)MathF.Ceiling(local.Bottom / Game.TileSize);
		return ClampTileRect(new RectInt(left, top, right - left, bottom - top), room);
	}

	void ApplyTileRect(Room room, RectInt rect, Factory.TileLayer layer, char tile)
	{
		rect = ClampTileRect(rect, room);
		for (var y = rect.Top; y < rect.Bottom; y++)
			for (var x = rect.Left; x < rect.Right; x++)
			{
				room.SetTile(layer, new Point2(x, y), tile, rebuildGeometry: false);
			}

		room.RebuildGeometry();
		editorGameDirty = true;
	}

	// Slope painting is a foreground-terrain affordance: only offered when the active brush
	// supports slopes and we're editing the foreground layer.
	bool SlopePaintAvailable
		=> activeTileLayer == Factory.TileLayer.Foreground && !actorSelectTool && selectedActorPaletteIndex < 0
			&& Factory.Find(brush) is { BlocksMovement: true, SupportsSlope: true };

	// Fill the dragged rectangle with a slope staircase whose descent matches the drag direction:
	// down-right/left = a floor ramp (solid below), up-right/left = an underhang (solid above). The
	// run length per step follows the rectangle's width:height, and the engine infers each run's
	// facing from the surroundings we lay down (see Room.InferSlopeShape). `from`/`to` are the raw
	// (direction-preserving) drag endpoints.
	void ApplySlopeRect(Room room, Point2 from, Point2 to, char tile)
	{
		var rect = ClampTileRect(NormalizeTileRect(from, to), room);
		var w = rect.Width;
		var h = rect.Height;
		if (w <= 0 || h <= 0)
			return;

		var toRight = to.X >= from.X;
		var underhang = to.Y < from.Y;
		var layer = Factory.TileLayer.Foreground;

		for (var x = 0; x < w; x++)
		{
			// 0 at the high (uphill) end, growing toward the low end; the run length per 1-tile
			// step is w/h, so the ramp spans the rectangle corner to corner.
			var fromHigh = toRight ? x : w - 1 - x;
			var step = Math.Min(h - 1, fromHigh * h / w);
			var surfaceRow = underhang ? h - 1 - step : step;
			var col = rect.Left + x;

			var surfaceTile = new Point2(col, rect.Top + surfaceRow);
			room.SetTile(layer, surfaceTile, tile, rebuildGeometry: false);
			room.SetSlope(surfaceTile, true, rebuildGeometry: false);

			// Body fill: solid below the surface for a floor, above it for an underhang.
			if (underhang)
				for (var y = 0; y < surfaceRow; y++)
					room.SetTile(layer, new Point2(col, rect.Top + y), tile, rebuildGeometry: false);
			else
				for (var y = surfaceRow + 1; y < h; y++)
					room.SetTile(layer, new Point2(col, rect.Top + y), tile, rebuildGeometry: false);
		}

		// Cap the high corner with solid so the topmost run has a solid neighbour on its high side --
		// otherwise InferSlopeShape's DownRight fallback mis-faces an isolated leftward/underhang ramp.
		var highCol = rect.Left + (toRight ? 0 : w - 1);
		var highRow = rect.Top + (underhang ? h - 1 : 0);
		room.SetTile(layer, new Point2(highCol, highRow), tile, rebuildGeometry: false);

		room.RebuildGeometry();
		editorGameDirty = true;
	}

	EditorSelectionSnapshot CaptureEditorSelection(Room room)
	{
		var rect = ClampTileRect(editorSelectionRect ?? new RectInt(), room);
		var snapshot = new EditorSelectionSnapshot
		{
			Rect = rect,
			IncludeBackgroundTiles = selectionIncludeBackgroundTiles,
			IncludeForegroundTiles = selectionIncludeForegroundTiles,
			IncludeActors = selectionIncludeActors,
			RoomJson = room.ToJsonString()
		};

		if (snapshot.IncludeBackgroundTiles)
			snapshot.BackgroundTiles = CaptureTiles(room.BackgroundTiles, rect);
		if (snapshot.IncludeForegroundTiles)
			snapshot.ForegroundTiles = CaptureTiles(room.Tiles, rect);
		if (snapshot.IncludeActors)
		{
			var worldRect = TileRectToRoomWorldRect(room, rect);
			for (var i = 0; i < room.Actors.Count; i++)
			{
				if (!RoomActorBounds(room, room.Actors[i]).Overlaps(worldRect))
					continue;

				var actor = ActorSpecs.CloneObject(room.Actors[i]);
				var position = ActorSpecs.ReadPoint(actor, "Position") ?? Point2.Zero;
				snapshot.Actors.Add((i, actor, position));
			}
		}

		return snapshot;
	}

	static char[,] CaptureTiles(char[,] source, RectInt rect)
	{
		var result = new char[rect.Width, rect.Height];
		for (var y = 0; y < rect.Height; y++)
			for (var x = 0; x < rect.Width; x++)
				result[x, y] = source[rect.Left + x, rect.Top + y];
		return result;
	}

	[Flags]
	enum SnapshotFlags
	{
		None = 0,
		IncludeEmptyTiles = 1 << 0,
	}

	void ApplyEditorSelectionSnapshot(Room room, EditorSelectionSnapshot snapshot, Point2 offset, bool duplicate)
	{
		room.LoadJsonState(snapshot.RoomJson);
		var destination = ClampSelectionDestination(room, snapshot.Rect, offset);
		offset = destination.Position - snapshot.Rect.Position;

		if (!duplicate)
			ClearEditorSelectionSource(room, snapshot);

		// Write tiles
		bool writeEmpty = (selectionSnapshotFlags & SnapshotFlags.IncludeEmptyTiles) != 0;
		if (snapshot.BackgroundTiles != null) WriteTiles(room, snapshot.BackgroundTiles, destination.Position, Factory.TileLayer.Background, writeEmpty: writeEmpty);
		if (snapshot.ForegroundTiles != null) WriteTiles(room, snapshot.ForegroundTiles, destination.Position, Factory.TileLayer.Foreground, writeEmpty: writeEmpty);

		if (snapshot.IncludeActors)
		{
			if (duplicate)
			{
				foreach (var (_, actor, position) in snapshot.Actors)
				{
					var copy = ActorSpecs.CloneObject(actor);
					copy["Position"] = new JsonArray(position.X + offset.X * Game.TileSize, position.Y + offset.Y * Game.TileSize);
					ActorSpecs.AssignNewActorId(copy);
					room.Actors.Add(copy);
				}
			}
			else
			{
				foreach (var (index, _, position) in snapshot.Actors)
					if (index >= 0 && index < room.Actors.Count)
						room.Actors[index]["Position"] = new JsonArray(position.X + offset.X * Game.TileSize, position.Y + offset.Y * Game.TileSize);
			}
		}

		room.RebuildGeometry();
		editorSelectionRect = destination;
		selectedRoomActorIndex = -1;
		editorGameDirty = true;
	}

	void ClearEditorSelectionSource(Room room, EditorSelectionSnapshot snapshot)
	{
		if (snapshot.BackgroundTiles != null)
			ClearTiles(room, snapshot.Rect, Factory.TileLayer.Background);
		if (snapshot.ForegroundTiles != null)
			ClearTiles(room, snapshot.Rect, Factory.TileLayer.Foreground);
	}

	static RectInt ClampSelectionDestination(Room room, RectInt source, Point2 offset)
	{
		var maxX = Math.Max(0, room.Size.X - source.Width);
		var maxY = Math.Max(0, room.Size.Y - source.Height);
		var x = Math.Clamp(source.X + offset.X, 0, maxX);
		var y = Math.Clamp(source.Y + offset.Y, 0, maxY);
		return new RectInt(x, y, source.Width, source.Height);
	}

	void WriteTiles(Room room, char[,] tiles, Point2 destination, Factory.TileLayer layer, bool writeEmpty = true)
	{
		var width = tiles.GetLength(0);
		var height = tiles.GetLength(1);
		for (var y = 0; y < height; y++)
			for (var x = 0; x < width; x++)
			{
				var tile = destination + new Point2(x, y);
				if (!writeEmpty && tiles[x, y] == '0')
					continue;
				room.SetTile(layer, tile, tiles[x, y], rebuildGeometry: false);
			}
	}

	void ClearTiles(Room room, RectInt rect, Factory.TileLayer layer)
	{
		for (var y = rect.Top; y < rect.Bottom; y++)
			for (var x = rect.Left; x < rect.Right; x++)
			{
				room.SetTile(layer, new Point2(x, y), '0', rebuildGeometry: false);
			}
	}

	void UpdateEditorWorldView(Rect inner)
	{
		var cursor = Cursor;
		if (UpdateEditorTouchView(inner))
			return;

		var canInteract = !imgui.WantsMouseInput && inner.Contains(cursor);

		// Music-region drawing/resizing owns the mouse while the regions window is engaged.
		if (UpdateMusicRegionInteraction(canInteract, cursor, inner))
			return;

		// Middle-mouse drag pans.
		if (canInteract && Input.Mouse.MiddleDown)
		{
			editorCameraTween = 1.0f;
			editorZoomTarget = editorZoom;
			editorCamera -= Input.Mouse.Delta / Scale / editorZoom;
		}

		// Trackpad pinch zooms, anchored at the cursor. Pinch events fire independent of finger count
		// (no FINGER events on the macOS trackpad), so the multi-finger touch path above never handles
		// them on desktop -- we apply them here. PinchScale is the per-event incremental factor (~1.0x),
		// reset to 1.0 each frame; begin/end carry 0, which the guard skips.
		if (canInteract && Input.Touch.DownCount < 2 && Input.Touch.PinchDown)
		{
			var scale = Input.Touch.PinchScale;
			if (scale > 0 && MathF.Abs(scale - 1.0f) > 0.0001f)
			{
				editorCameraTween = 1.0f;
				var before = ScreenToEditorWorld(cursor, inner);
				editorZoomTarget = Math.Clamp(editorZoomTarget * scale, 0.15f, 8.0f);
				editorZoom = editorZoomTarget;
				editorCamera = before - (cursor - inner.Center) / editorZoom;
			}
		}

		// Wheel: a trackpad two-finger swipe arrives as precise (fractional / horizontal) deltas -> pan;
		// a notched mouse wheel arrives as whole-number vertical ticks -> zoom. Latch trackpad mode so an
		// integer crossing mid-swipe stays a pan. Skip while pinching so an incidental scroll doesn't jitter.
		if (canInteract && !Input.Touch.PinchDown)
		{
			var wheel = Input.Mouse.Wheel;
			if (wheel != Vector2.Zero)
			{
				if (editorScrollMode == EditorScrollMode.Auto && IsPreciseScroll(wheel))
					trackpadScrollLatch = TrackpadScrollLatchTime;

				if (editorScrollMode == EditorScrollMode.Trackpad ||
					editorScrollMode == EditorScrollMode.Auto && trackpadScrollLatch > 0)
				{
					wheel.X *= -1f;

					// SDL reports natural (flipped) direction, so move the camera opposite the swipe to
					// keep content under the fingers. Divide by zoom for consistent screen-space speed.
					editorCameraTween = 1.0f;
					editorZoomTarget = editorZoom;
					editorCamera -= wheel * EditorTrackpadPanSpeed / editorZoom;
				}
				else if (wheel.Y != 0)
				{
					editorCameraTween = 1.0f;
					editorZoomTarget = Math.Clamp(editorZoomTarget * MathF.Pow(1.15f, wheel.Y), 0.15f, 8.0f);
				}
			}
		}

		if (trackpadScrollLatch > 0)
			trackpadScrollLatch -= Time.Delta;

		UpdateEditorZoomSmoothing(canInteract ? cursor : inner.Center, inner);
	}

	// A trackpad swipe sends precise deltas: fractional vertical, or any horizontal component (a notched
	// mouse wheel only ever sends whole-number vertical ticks).
	static bool IsPreciseScroll(Vector2 wheel)
		=> wheel.X != 0 || wheel.Y != MathF.Truncate(wheel.Y);

	bool UpdateEditorTouchView(Rect inner)
	{
		var touch = Input.Touch;
		if (touch.DownCount < 2)
			return false;

		var center = touch.Center / Scale;
		if (!inner.Contains(center))
			return false;

		editorCameraTween = 1.0f;
		var centerDelta = touch.CenterDelta / Scale;
		editorCamera -= centerDelta / editorZoom;

		var zoomScale = touch.PinchScale;
		if (zoomScale <= 0 || MathF.Abs(zoomScale - 1.0f) < 0.0001f)
			zoomScale = TouchDistanceScale(touch);

		if (zoomScale > 0 && MathF.Abs(zoomScale - 1.0f) > 0.0001f)
		{
			var before = ScreenToEditorWorld(center, inner);
			editorZoomTarget = Math.Clamp(editorZoomTarget * zoomScale, 0.15f, 8.0f);
			editorZoom = editorZoomTarget;
			editorCamera = before - (center - inner.Center) / editorZoom;
		}

		return true;
	}

	bool SingleTouchInEditorWorld(Rect inner)
	{
		var touch = Input.Touch;
		return touch.DownCount == 1 && inner.Contains(touch.Center / Scale);
	}

	static float TouchDistanceScale(TouchState touch)
	{
		TouchState.Finger? first = null;
		TouchState.Finger? second = null;
		for (var i = 0; i < touch.Count; i++)
		{
			var finger = touch[i];
			if (!finger.Down)
				continue;
			if (first == null)
				first = finger;
			else
			{
				second = finger;
				break;
			}
		}

		if (first is not { } a || second is not { } b)
			return 1.0f;

		var current = Vector2.Distance(a.Position, b.Position);
		var previous = Vector2.Distance(a.Position - a.Delta, b.Position - b.Delta);
		return previous > 0.001f ? current / previous : 1.0f;
	}

	void UpdateEditorZoomSmoothing(Vector2 screenAnchor, Rect inner)
	{
		if (MathF.Abs(editorZoomTarget - editorZoom) <= 0.0001f)
		{
			editorZoom = editorZoomTarget;
			return;
		}

		var before = ScreenToEditorWorld(screenAnchor, inner);
		editorZoom = Calc.Approach(editorZoom, editorZoomTarget, Time.Delta * MathF.Max(editorZoom, editorZoomTarget) * 18.0f);
		editorCamera = before - (screenAnchor - inner.Center) / editorZoom;
	}

	Vector2 ScreenToEditorWorld(Vector2 screen, Rect inner)
		=> editorCamera + (screen - inner.Center) / editorZoom;

	void UpdateAutoSelectedRoomFromEditorCamera(Rect inner)
	{
		if (!AutoSelectRoomByCamera || editorCameraTween < 1.0f)
			return;

		var worldView = new Rect(
			editorCamera - inner.Size / (editorZoom * 2),
			inner.Size / editorZoom);
		var viewArea = worldView.Area;
		if (viewArea <= 0)
			return;

		Room? bestRoom = null;
		var bestCoverage = 0.0f;
		foreach (var room in Assets.RoomsById.Values)
		{
			var bounds = room.WorldBounds;
			var roomRect = new Rect(bounds.X, bounds.Y, bounds.Width, bounds.Height);
			var coverage = worldView.GetIntersection(roomRect).Area / viewArea;
			if (coverage > bestCoverage)
			{
				bestCoverage = coverage;
				bestRoom = room;
			}
		}

		if (bestRoom == null || bestRoom == CurrentRoom || bestCoverage < 0.65f)
			return;

		CurrentRoom?.Save();
		SelectRoom(bestRoom);
		selectedRoomActorIndex = -1;
		editorRoomEntryDirection = null;
		SyncEditorGame();
	}

	// Room-edit mode is implicit: it turns on once the current room shrinks below ~60% of the editor
	// view (you've zoomed out to arrange rooms) and off once it grows back past ~66% (you've zoomed
	// back in to edit tiles). The hysteresis band keeps it from flickering right at the threshold.
	static float RoomEditEnterFraction => 0.6f;
	static float RoomEditExitFraction => 0.66f;

	void UpdateRoomEditModeFromZoom(Rect inner)
	{
		if (CurrentRoom is not { } room || inner.Width <= 0 || inner.Height <= 0)
		{
			editingRoomGeometry = false;
			return;
		}

		var bounds = room.WorldBounds;
		var fraction = MathF.Max(bounds.Width * editorZoom / inner.Width, bounds.Height * editorZoom / inner.Height);

		if (editingRoomGeometry)
		{
			if (fraction > RoomEditExitFraction)
			{
				editingRoomGeometry = false;
				CommitEditorDragState();
			}
		}
		else if (fraction < RoomEditEnterFraction)
		{
			editingRoomGeometry = true;
			ClearEditorDragState();
		}
	}

	void DrawEditorRooms(Rect visibleWorld)
	{
		using var perf = PerfStats.Time(PerfDetail.DrawEditorRooms);
		BuildQueuedRoomPreview();
		foreach (var room in Assets.RoomsById.Values)
		{
			var bounds = room.WorldBounds;
			if (visibleWorld.GetIntersection(new Rect(bounds.X, bounds.Y, bounds.Width, bounds.Height)).Area <= 0)
				continue;

			var line = MathF.Max(1.0f / editorZoom, LineWeight / editorZoom);

			if (GetEditorRoomTexture(room) is { } preview)
				DrawEditorRoomPreview(preview, bounds);
			else
				DrawEditorRoomBackdrop(room, bounds);
			batcher.RectLine(bounds, line, Color.Gray);
		}
	}

	void DrawEditorRoomBackdrop(Room room, RectInt bounds)
	{
		var top = room.BackdropColor;
		var bottom = room.BackdropColor2 ?? top;
		batcher.Rect(bounds, top, top, bottom, bottom);
	}

	void DrawEditorRoomPreview(Texture preview, RectInt bounds)
	{
		var scale = new Vector2(
			bounds.Width / (float)Math.Max(1, preview.Width),
			bounds.Height / (float)Math.Max(1, preview.Height));
		batcher.Image(preview, bounds.TopLeft, Vector2.Zero, scale, 0, Color.White);
	}

	Room? GetRoomAtWorld(Vector2 world)
	{
		foreach (var room in Assets.RoomsById.Values)
			if (room.WorldBounds.Contains(world))
				return room;

		return null;
	}

	NeuralTextureActor? FindLiveNeuralTextureActor(string actorId)
	{
		if (game == null)
			return null;
		foreach (var actor in game.Actors)
			if (actor is NeuralTextureActor texture && texture.ActorId == actorId)
				return texture;
		return null;
	}

	bool UpdateMaskPainting(Rect inner, bool worldMouseBlocked, bool keyboardBlocked)
	{
		if (maskPaintActorId == null)
			return false;

		var live = FindLiveNeuralTextureActor(maskPaintActorId);
		if (live == null || (!keyboardBlocked && Input.Keyboard.Pressed(Keys.Escape)))
		{
			EndMaskPainting();
			return live != null;
		}

		var canInteract = !worldMouseBlocked && inner.Contains(Cursor);
		if (canInteract && (Input.Mouse.LeftDown || Input.Mouse.RightDown))
		{
			var slot = Input.Mouse.RightDown ? -1 : maskPaintSlot;
			var flow = Math.Clamp(maskPaintStrength * Time.Delta * 10.0f, 0.0f, 1.0f);
			live.PaintMask(ScreenToEditorWorld(Cursor, inner), maskPaintRadius, maskPaintSoftness, flow, slot);
			if (CurrentRoom is { } room)
				MarkRoomPreviewDirty(room);
			maskPaintStroke = true;
		}
		else if (maskPaintStroke)
		{
			CommitMaskStroke(live);
		}

		return true;
	}

	void EndMaskPainting()
	{
		if (maskPaintActorId != null && maskPaintStroke && FindLiveNeuralTextureActor(maskPaintActorId) is { } live)
			CommitMaskStroke(live);
		maskPaintActorId = null;
		maskPaintStroke = false;
	}

	void CommitMaskStroke(NeuralTextureActor live)
	{
		maskPaintStroke = false;
		if (CurrentRoom is not { } room)
			return;
		foreach (var actor in room.Actors)
			if (ActorSpecs.ReadString(actor, "Id") == maskPaintActorId)
			{
				using (CaptureRoomUndo("Paint Neural Texture Mask", room))
					ActorSpecs.SetProperty(actor, nameof(NeuralTextureActor.PaintedMask), JsonValue.Create(live.SerializeMask()));
				live.SimulateFromSeed(maskPaintPreviewSteps);
				MarkRoomPreviewDirty(room);
				return;
			}
	}

	bool UpdateLinkedDoorTargetPicker(Rect inner, Rect visibleWorld, bool worldMouseBlocked, bool keyboardBlocked)
	{
		if (pickingLinkedDoorTargetForActorId == null)
			return false;

		if (!keyboardBlocked && Input.Keyboard.Pressed(Keys.Escape))
		{
			pickingLinkedDoorTargetForActorId = null;
			return true;
		}

		DrawLinkedDoorTargetPickerOverlay(visibleWorld);

		var canInteract = !worldMouseBlocked && inner.Contains(Cursor);
		if (!canInteract || !Input.Mouse.LeftPressed)
			return true;

		var world = ScreenToEditorWorld(Cursor, inner);
		if (FindLinkedDoorAtWorld(world, pickingLinkedDoorTargetForActorId) is { } target &&
			Assets.FindActorById(pickingLinkedDoorTargetForActorId) is { } source)
		{
			using (CaptureRoomUndo("Set Linked Door Target", source.Room))
				ActorSpecs.SetProperty(source.Actor, nameof(LinkedDoor.TargetActorId), JsonValue.Create(ActorSpecs.ReadString(target.Actor, "Id") ?? ""));
			if (source.Room == CurrentRoom)
				editorGameDirty = true;
			else
				source.Room.Save();
			pickingLinkedDoorTargetForActorId = null;
		}

		return true;
	}

	void DrawLinkedDoorTargetPickerOverlay(Rect visibleWorld)
	{
		var lineWeight = MathF.Max(1.0f / editorZoom, LineWeight / editorZoom);
		foreach (var room in Assets.RoomsById.Values)
		{
			if (visibleWorld.GetIntersection(new Rect(room.WorldBounds.X, room.WorldBounds.Y, room.WorldBounds.Width, room.WorldBounds.Height)).Area <= 0)
				continue;

			foreach (var actor in room.Actors)
			{
				if (!IsLinkedDoorActor(actor))
					continue;
				if (string.Equals(ActorSpecs.ReadString(actor, "Id"), pickingLinkedDoorTargetForActorId, StringComparison.OrdinalIgnoreCase))
					continue;

				var bounds = RoomActorBounds(room, actor).Inflate(lineWeight * 3);
				batcher.Rect(bounds, highlight * 0.18f);
				batcher.RectLine(bounds, lineWeight * 2, highlight);
			}
		}
	}

	static (Room Room, JsonObject Actor)? FindLinkedDoorAtWorld(Vector2 world, string excludeId)
	{
		foreach (var room in Assets.RoomsById.Values)
		{
			if (!room.WorldBounds.Contains(world))
				continue;

			for (var i = room.Actors.Count - 1; i >= 0; i--)
			{
				var actor = room.Actors[i];
				if (!IsLinkedDoorActor(actor))
					continue;
				if (string.Equals(ActorSpecs.ReadString(actor, "Id"), excludeId, StringComparison.OrdinalIgnoreCase))
					continue;
				if (RoomActorBounds(room, actor).Contains(world))
					return (room, actor);
			}
		}

		return null;
	}

	void DrawEditorEyedropperCursor(Rect tileRect, float lineWeight)
	{
		var width = lineWeight * 2;
		batcher.RectLine(tileRect, width, highlight);

		var start = tileRect.TopLeft + tileRect.Size * 0.25f;
		var end = tileRect.BottomRight - tileRect.Size * 0.20f;
		batcher.Line(start, end, width, Color.White);
		batcher.Rect(new Rect(start - Vector2.One * width, Vector2.One * width * 2), highlight);
		batcher.Rect(new Rect(end - Vector2.One * width * 0.75f, Vector2.One * width * 1.5f), Color.White);
	}

	void DrawBreakableTileMarkers(Room room, Vector2 roomTopLeft, float lineWeight)
		=> DrawFlaggedTileMarkers(room, roomTopLeft, lineWeight, Room.TileFlags.Breakable, new Color(1.0f, 0.82f, 0.24f, 0.2f));

	void DrawFungalTileMarkers(Room room, Vector2 roomTopLeft, float lineWeight)
		=> DrawFlaggedTileMarkers(room, roomTopLeft, lineWeight, Room.TileFlags.Fungal, new Color(0.40f, 0.90f, 0.42f, 0.35f));

	void DrawCrumbleTileMarkers(Room room, Vector2 roomTopLeft, float lineWeight)
		=> DrawFlaggedTileMarkers(room, roomTopLeft, lineWeight, Room.TileFlags.Crumble, new Color(0.72f, 0.72f, 0.76f, 0.35f));

	void DrawFlaggedTileMarkers(Room room, Vector2 roomTopLeft, float lineWeight, Room.TileFlags flag, Color color)
	{
		var tileSize = Game.TileSize - lineWeight * 4;
		for (var x = 0; x < room.Size.X; x++)
			for (var y = 0; y < room.Size.Y; y++)
			{
				if (!room.HasTileFlag(new Point2(x, y), flag))
					continue;

				var rect = new Rect(
					roomTopLeft.X + x * Game.TileSize + lineWeight * 2,
					roomTopLeft.Y + y * Game.TileSize + lineWeight * 2, tileSize, tileSize);
				batcher.RectDashed(rect, lineWeight, color, 1, 5.5f);
			}
	}

	static Point2? GetEditorEntryDirection(Room from, Room to)
	{
		if (to.TileSpacePosition.X >= from.TileSpacePosition.X + from.Size.X) return Point2.Right;
		if (to.TileSpacePosition.X + to.Size.X <= from.TileSpacePosition.X) return Point2.Left;
		if (to.TileSpacePosition.Y >= from.TileSpacePosition.Y + from.Size.Y) return Point2.Down;
		if (to.TileSpacePosition.Y + to.Size.Y <= from.TileSpacePosition.Y) return Point2.Up;
		return null;
	}


	void Editor()
	{
		using var perf = PerfStats.Time(PerfDetail.EditorWorld);
		var inner = GameViewRect;
		var worldMouseBlocked = imgui.WantsMouseInput && !SingleTouchInEditorWorld(inner);
		var keyboardBlocked = ImGuiWantsKeyboard;
		var roomActivationClickConsumed = false;
		UpdateEditorShortcuts();
		// Shoulder buttons cycle worlds in edit mode (same as time speed in play mode)
		if (!worldMouseBlocked)
		{
			int worldSwitch = 0;
			foreach (var controller in Input.Controllers) if (controller.Connected)
			{
				if (controller.Pressed(Buttons.LeftShoulder)) worldSwitch -= 1;
				if (controller.Pressed(Buttons.RightShoulder)) worldSwitch += 1;
			}

			if (worldSwitch != 0)
				SwitchWorld(worldSwitch);
		}

		if (!editorViewInitialized)
			FocusEditorOnCurrentRoom();
		UpdateEditorCameraTween();
		UpdateEditorWorldView(inner);
		UpdateRoomEditModeFromZoom(inner);
		if (!editingRoomGeometry)
			UpdateAutoSelectedRoomFromEditorCamera(inner);

		batcher.PushScissor((RectInt)(inner * Scale));
		var worldMatrix =
			Matrix3x2.CreateTranslation(-editorCamera) *
			Matrix3x2.CreateScale(editorZoom) *
			Matrix3x2.CreateTranslation(inner.Center);
		var visibleWorld = new Rect(
			editorCamera - inner.Size / (editorZoom * 2),
			inner.Size / editorZoom);
		batcher.PushMatrix(worldMatrix);
		DrawEditorRooms(visibleWorld);
		DrawMusicRegionsOverlay(visibleWorld);
		DrawEditorScreenGrid(visibleWorld);

		var eyedropper = !keyboardBlocked && Input.Keyboard.Alt;
		if (UpdateLinkedDoorTargetPicker(inner, visibleWorld, worldMouseBlocked, keyboardBlocked))
			worldMouseBlocked = true;
		if (UpdateMaskPainting(inner, worldMouseBlocked, keyboardBlocked))
			worldMouseBlocked = true;

		// Room picking. A single click selects the room under the cursor (room-edit mode only, so it
		// doesn't fight tile painting when zoomed in); a double click anywhere on a room zooms to it
		// (which zooms in past the threshold and leaves room-edit mode). The old single-click-to-zoom
		// is gone -- selecting and zooming are now distinct gestures.
		if (!worldMouseBlocked && inner.Contains(Cursor) && !eyedropper)
		{
			var doubleClick = ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left);
			if ((editingRoomGeometry && Input.Mouse.LeftPressed) || doubleClick)
			{
				var worldMouse = ScreenToEditorWorld(Cursor, inner);
				if (GetRoomAtWorld(worldMouse) is { } selected)
				{
					var switching = selected != CurrentRoom;
					// Double-click zooms when switching rooms or arranging (room-edit mode); double-clicking
					// the current room while zoomed in does nothing, so it can't disrupt tile editing.
					var zoomTo = doubleClick && (switching || editingRoomGeometry);
					if (switching || zoomTo)
					{
						using (CaptureSelectionUndo("Select Room"))
						{
							if (switching)
							{
								if (CurrentRoom is { } previous)
									editorRoomEntryDirection = GetEditorEntryDirection(previous, selected);
								CurrentRoom?.Save();
								SelectRoom(selected);
								selectedRoomActorIndex = -1;
								ClearEditorDragState();
								SyncEditorGame();
							}
							if (zoomTo)
								TweenEditorCameraToRoom(selected);
							roomActivationClickConsumed = true;
						}
					}
				}
			}
		}

		if (roomActivationClickConsumed)
		{
			worldMouseBlocked = true;
			CancelTilePaint();
		}

		if (CurrentRoom is { } editing)
		{
			using var editorInteractionPerf = PerfStats.Time(PerfDetail.EditorInteraction);
			var gridColor = Color.White * 0.07f;
			var gridColorHeavy = Color.White * 0.115f;
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
			proposedRoomTiles = editorDrag.Kind != EditorDragKind.None || editingRoomGeometry || worldMouseBlocked || overGame ? null : GetProposedRoomTiles(editing, worldMouse);
			if (selectedRoomActorIndex >= editing.Actors.Count)
				selectedRoomActorIndex = -1;
			var actorSelectActive = actorSelectTool && !eyedropper;
			if (!editingRoomGeometry && actorSelectActive)
				worldMouseBlocked |= RunActorGizmos(editing, selectedRoomActorIndex, worldMouse, gridWeight,
					draw: false, canInteract: !worldMouseBlocked && inner.Contains(Cursor), keyboardBlocked);
			var hoveredActorIndex = !worldMouseBlocked && overGame ? HitTestRoomActor(editing, worldMouse) : -1;
			var hoveredPathPoint = !worldMouseBlocked && actorSelectActive && overGame
				? HitTestPointPathHandle(editing, selectedRoomActorIndex, worldMouse, gridWeight)
				: -1;
			var revealDoorFocused =
				hoveredActorIndex >= 0 && hoveredActorIndex < editing.Actors.Count && IsRevealDoorActor(editing.Actors[hoveredActorIndex]) ||
				selectedRoomActorIndex >= 0 && selectedRoomActorIndex < editing.Actors.Count && IsRevealDoorActor(editing.Actors[selectedRoomActorIndex]);
			game?.SetHiddenCoverEditorOpacity(revealDoorFocused);
			var hoveredHiddenWallHandle = !worldMouseBlocked && actorSelectActive && overGame
				? HitTestHiddenWallResizeHandle(editing, worldMouse, gridWeight)
				: ResizeHandle.None;
			var hoveredRoomGeometryHandle = editingRoomGeometry && !worldMouseBlocked
				? HitTestRoomGeometryHandle(editing, worldMouse, gridWeight)
				: ResizeHandle.None;
			SetHiddenWallResizeCursor(
				DragKind == EditorDragKind.RoomGeometry ? editorDrag.RoomGeometryHandle :
				hoveredRoomGeometryHandle != ResizeHandle.None ? hoveredRoomGeometryHandle :
				DragKind == EditorDragKind.ActorResize ? editorDrag.ResizeHandle :
				hoveredHiddenWallHandle);

			for (int x = 1; x < editing.Size.X; x++)
				batcher.Line(roomTopLeft + new Vector2(x * Game.TileSize, 0), roomTopLeft + new Vector2(x * Game.TileSize, height), gridWeight, (x - 1) % 4 == 0 ? gridColorHeavy : gridColor);
			for (int y = 1; y < editing.Size.Y; y++)
				batcher.Line(roomTopLeft + new Vector2(0, y * Game.TileSize), roomTopLeft + new Vector2(width, y * Game.TileSize), gridWeight, (y - 1) % 4 == 0 ? gridColorHeavy : gridColor);
			DrawBreakableTileMarkers(editing, roomTopLeft, gridWeight);
			DrawCrumbleTileMarkers(editing, roomTopLeft, gridWeight);
			if (showFungalMarkers)
				DrawFungalTileMarkers(editing, roomTopLeft, gridWeight);

			var brushEntry = Factory.Find(brush);
			if (brushEntry is { } foundBrushEntry && foundBrushEntry.Layer != activeTileLayer)
				brushEntry = null;
			Factory.ActorPaletteEntry? actorBrush = selectedActorPaletteIndex >= 0 && selectedActorPaletteIndex < Factory.ActorPalette.Count
				? Factory.ActorPalette[selectedActorPaletteIndex]
				: null;
			var brushIsActor = actorBrush != null;
			var actorBrushPosition = brushIsActor && !keyboardBlocked && Input.Keyboard.Shift
				? (Point2)worldMouse
				: (Point2)(roomTopLeft + tileOver * Game.TileSize + (actorBrush?.Offset ?? Point2.Zero));
			var ctrlClick =
				Input.Mouse.LeftPressed &&
				(Input.Keyboard.Down(Keys.LeftControl) || Input.Keyboard.Down(Keys.RightControl));

			if (eyedropper)
				CancelTilePaint();

			if (worldMouseBlocked)
				CancelTilePaint();

			if (editingRoomGeometry && !worldMouseBlocked && Input.Mouse.LeftPressed && (overGame || hoveredRoomGeometryHandle != ResizeHandle.None))
			{
				ClearEditorDragState();
				editorDrag.Kind = EditorDragKind.RoomGeometry;
				OpenPendingRoomUndo(RoomGeometryUndoSlot, "Edit Room Geometry", editing);
				editorDrag.RoomGeometryHandle = hoveredRoomGeometryHandle;
				var mouseTile = WorldToRoomGeometryTile(worldMouse);
				editorDrag.RoomGeometryOffset = mouseTile - editing.TileSpacePosition;
				CancelTilePaint();
			}
			else if (!editingRoomGeometry && !worldMouseBlocked && actorSelectActive && overGame && Input.Mouse.LeftPressed && hoveredPathPoint >= 0)
			{
				ClearEditorDragState();
				editorDrag.Kind = EditorDragKind.ActorPathPoint;
				editorDrag.PathPointIndex = hoveredPathPoint;
				editorDrag.ActorSnapsByDefault = true;
				editorSelectionRect = null;
				CancelTilePaint();
			}
			else if (!editingRoomGeometry && !worldMouseBlocked && actorSelectActive && overGame && Input.Mouse.LeftPressed && hoveredHiddenWallHandle != ResizeHandle.None)
			{
				ClearEditorDragState();
				editorDrag.Kind = EditorDragKind.ActorResize;
				editorDrag.ResizeHandle = hoveredHiddenWallHandle;
				editorSelectionRect = null;
				CancelTilePaint();
			}
			else if (!editingRoomGeometry && !worldMouseBlocked && actorSelectActive && overGame && Input.Mouse.LeftPressed && editorSelectionRect is { } selection && TileRectContains(selection, tileOver))
			{
				var duplicateSelection = Input.Keyboard.Down(Keys.LeftControl) || Input.Keyboard.Down(Keys.RightControl);
				OpenPendingRoomUndo(ActorMoveUndoSlot, duplicateSelection ? "Duplicate Selection" : "Move Selection", editing);
				ClearEditorDragState();
				editorDrag.Kind = EditorDragKind.SelectionMove;
				editorDrag.DuplicateSelection = duplicateSelection;
				editorDrag.SelectionAnchor = tileOver;
				editorDrag.SelectionOffset = Point2.Zero;
				editorDrag.SelectionSnapshot = CaptureEditorSelection(editing);
				CancelTilePaint();
			}
			else if (!editingRoomGeometry && !worldMouseBlocked && actorSelectActive && overGame && Input.Mouse.LeftPressed)
			{
				if (hoveredActorIndex >= 0)
				{
					using (CaptureSelectionUndo("Select Actor"))
					{
						selectedRoomActorIndex = PickRoomActorForPress(editing, worldMouse);
						ClearEditorDragState();
						editorSelectionRect = null;
						editorDrag.Kind = EditorDragKind.RoomActor;
						editorDrag.ActorOffset = worldMouse - (roomTopLeft + (Vector2)(ActorSpecs.ReadPoint(editing.Actors[selectedRoomActorIndex], "Position") ?? Point2.Zero));
						editorDrag.ActorSnapsByDefault = RoomActorPositionIsSnapped(editing.Actors[selectedRoomActorIndex]);
						CancelTilePaint();
					}
				}
				else if (selectedRoomActorIndex < 0)
				{
					using (CaptureSelectionUndo("Select Area"))
					{
						ClearEditorDragState();
						editorDrag.Kind = EditorDragKind.SelectionRect;
						editorSelectionFrom = ClampTile(editing, tileOver);
						editorSelectionTo = editorSelectionFrom;
						editorSelectionRect = NormalizeTileRect(editorSelectionFrom, editorSelectionTo);
						CancelTilePaint();
					}
				}
				else
				{
					using (CaptureSelectionUndo("Clear Selection"))
					{
						selectedRoomActorIndex = -1;
						ClearEditorDragState();
						CancelTilePaint();
					}
				}
			}
			else if (!editingRoomGeometry && !worldMouseBlocked && overGame && brushIsActor && Input.Mouse.RightPressed && hoveredActorIndex >= 0)
			{
				using (CaptureRoomUndo("Delete Actor", editing))
				{
					var deletedActor = hoveredActorIndex < editing.Actors.Count ? editing.Actors[hoveredActorIndex] : null;

					editing.Actors.RemoveAt(hoveredActorIndex);
					if (selectedRoomActorIndex == hoveredActorIndex)
						selectedRoomActorIndex = -1;
					else if (selectedRoomActorIndex > hoveredActorIndex)
						selectedRoomActorIndex--;
					ClearEditorDragState();
					editorSelectionRect = null;
					CancelTilePaint();
					editorGameDirty = true;

					if (deletedActor is { } actor) {
						DisplayMessage($"Deleted {actor["Type"]}");
					}
				}
			}
			else if (!editingRoomGeometry && !worldMouseBlocked && eyedropper && overGame && Input.Mouse.LeftPressed)
			{
				brush = activeTileLayer == Factory.TileLayer.Background
					? editing.BackgroundTiles[tileOver.X, tileOver.Y]
					: editing.Tiles[tileOver.X, tileOver.Y];
				tileTypePlacing = brush;
				selectedActorPaletteIndex = -1;
				actorSelectTool = false;
				editorSelectionRect = null;
			}
			else if (!editingRoomGeometry && !worldMouseBlocked && activeTileLayer == Factory.TileLayer.Foreground && !actorSelectTool && !brushIsActor && overGame && ctrlClick && editing.GetEntry(tileOver) is { BlocksMovement: true, SupportsSlope: true })
			{
				using (CaptureRoomUndo("Toggle Slope Tile", editing))
				{
					editing.SetSlope(tileOver, !editing.IsSlope(tileOver));
					selectedActorPaletteIndex = -1;
					CancelTilePaint();
					editorGameDirty = true;
				}
			}
			else if (!editingRoomGeometry && !worldMouseBlocked && activeTileLayer == Factory.TileLayer.Foreground && overGame && !keyboardBlocked && Input.Keyboard.Pressed(Keys.B))
			{
				if (editing.GetEntry(tileOver) is { BlocksMovement: true })
				{
					using (CaptureRoomUndo("Toggle Breakable Tile", editing))
					{
						editing.SetBreakable(tileOver, !editing.IsBreakable(tileOver));
						editorGameDirty = true;
					}
				}
			}
			else if (!editingRoomGeometry && !worldMouseBlocked && activeTileLayer == Factory.TileLayer.Foreground && overGame && !keyboardBlocked && Input.Keyboard.Pressed(Keys.C))
			{
				if (editing.GetEntry(tileOver) is { BlocksMovement: true })
				{
					using (CaptureRoomUndo("Toggle Crumble Tile", editing))
					{
						editing.SetTileFlag(tileOver, Room.TileFlags.Crumble, !editing.HasTileFlag(tileOver, Room.TileFlags.Crumble));
						editorGameDirty = true;
					}
				}
			}
			else if (!keyboardBlocked && Input.Keyboard.Shift && Input.Keyboard.Pressed(Keys.G))
			{
				showFungalMarkers = !showFungalMarkers;
			}
			else if (!editingRoomGeometry && !worldMouseBlocked && overGame && !keyboardBlocked && !Input.Keyboard.Shift && Input.Keyboard.Pressed(Keys.G))
			{
				using (CaptureRoomUndo("Toggle Fungal Tile", editing))
				{
					editing.SetFungal(tileOver, !editing.IsFungal(tileOver));
					editorGameDirty = true;
				}
			}
			else if (!editingRoomGeometry && !worldMouseBlocked && overGame && Input.Mouse.LeftPressed && actorBrush is { } actorEntry)
			{
				using (CaptureRoomUndo("Place Actor", editing))
				{
					var position = actorBrushPosition;
					var before = editing.Actors.Count;
					if (actorEntry.PropSpriteName is { } propSpriteName)
					{
						editing.AddActorType(nameof(Prop), position);
						editing.Actors[^1][nameof(Prop.SpriteName)] = propSpriteName;
					}
					else if (actorEntry.ActorSpecId != null)
						editing.AddActorSpec(actorEntry.ActorSpecId, position);
					else if (actorEntry.ActorTypeName != null)
						editing.AddActorType(actorEntry.ActorTypeName, position);
					// Select the just-placed actor so its inspector opens immediately.
					if (editing.Actors.Count == before + 1)
						selectedRoomActorIndex = editing.Actors.Count - 1;
					CancelTilePaint();
					editorGameDirty = true;
				}
			}
			else if (!editingRoomGeometry && !worldMouseBlocked && overGame && !brushIsActor && brushEntry != null && (Input.Mouse.LeftPressed || Input.Mouse.RightPressed))
			{
				undo.CommitPending(TilePaintUndoSlot);
				OpenPendingRoomUndo(TilePaintUndoSlot, "Paint Tiles", editing);
				tileRectPaintFrom = ClampTile(editing, tileOver);
				tileRectPaintTo = tileRectPaintFrom;
				tileTypePlacing = Input.Mouse.LeftPressed ? brush : '0';
				ClearEditorDragState();
				editorDrag.Kind = EditorDragKind.TilePaint;
				editorSelectionRect = null;
			}

			if (!worldMouseBlocked && DragKind == EditorDragKind.RoomGeometry && Input.Mouse.LeftDown)
			{
				ApplyRoomGeometryDrag(editing, worldMouse);
			}
			else if (!worldMouseBlocked && actorSelectActive && DragKind == EditorDragKind.ActorPathPoint && selectedRoomActorIndex >= 0 && Input.Mouse.LeftDown)
			{
				OpenPendingRoomUndo(ActorMoveUndoSlot, "Move Path Point", editing);
				MovePointPathPoint(editing, selectedRoomActorIndex, editorDrag.PathPointIndex, worldMouse, RoomActorDragShouldSnap(keyboardBlocked));
			}
			else if (!worldMouseBlocked && actorSelectActive && DragKind == EditorDragKind.ActorResize && selectedRoomActorIndex >= 0 && Input.Mouse.LeftDown)
			{
				OpenPendingRoomUndo(ActorMoveUndoSlot, "Resize Actor", editing);
				ResizeHiddenWallActor(editing, selectedRoomActorIndex, editorDrag.ResizeHandle, worldMouse, tileOver, keyboardBlocked || !Input.Keyboard.Shift);
			}
			else if (!worldMouseBlocked && actorSelectActive && DragKind == EditorDragKind.RoomActor && selectedRoomActorIndex >= 0 && Input.Mouse.LeftDown)
			{
				OpenPendingRoomUndo(ActorMoveUndoSlot, "Move Actor", editing);
				MoveRoomActorTo(editing, selectedRoomActorIndex, worldMouse, RoomActorDragShouldSnap(keyboardBlocked));
			}
			else if (!worldMouseBlocked && actorSelectActive && DragKind == EditorDragKind.SelectionMove && editorDrag.SelectionSnapshot != null && Input.Mouse.LeftDown)
			{
				editorDrag.SelectionOffset = tileOver - editorDrag.SelectionAnchor;
				ApplyEditorSelectionSnapshot(editing, editorDrag.SelectionSnapshot, editorDrag.SelectionOffset, editorDrag.DuplicateSelection);
			}
			else if (!worldMouseBlocked && actorSelectActive && DragKind == EditorDragKind.SelectionRect && Input.Mouse.LeftDown)
			{
				editorSelectionTo = ClampTile(editing, tileOver);
				editorSelectionRect = NormalizeTileRect(editorSelectionFrom, editorSelectionTo);
			}

			if (!Input.Mouse.LeftDown && DragKind != EditorDragKind.TilePaint)
			{
				if (DragKind == EditorDragKind.RoomActor && actorPressCanCycle && !worldMouseBlocked && actorSelectActive &&
					Vector2.DistanceSquared(Cursor, actorPressCursor) <= ActorPickCycleThreshold * ActorPickCycleThreshold)
					CycleRoomActorSelection(editing, worldMouse);
				actorPressCanCycle = false;
				CommitEditorDragState();
			}

			if (!keyboardBlocked && actorSelectTool && selectedRoomActorIndex >= 0 && (Input.Keyboard.Pressed(Keys.Delete) || Input.Keyboard.Pressed(Keys.Backspace)))
			{
				using (CaptureRoomUndo("Delete Actor", editing))
				{
					editing.Actors.RemoveAt(selectedRoomActorIndex);
					selectedRoomActorIndex = -1;
					ClearEditorDragState();
					editorGameDirty = true;
				}
			}

			if (!Input.Mouse.RightDown && !TilePlacing)
				tileTypePlacing = brush;

			if (!editingRoomGeometry && !worldMouseBlocked && (!actorSelectTool || eyedropper) && overGame)
			{
				if (eyedropper)
				{
					var sampleRect = ((new Rect(tileOver.X, tileOver.Y, 1, 1) * Game.TileSize) + roomTopLeft).Inflate(gridWeight * 2);
					DrawEditorEyedropperCursor(sampleRect, gridWeight);
				}
				else if (actorBrush is { } actorPreview)
				{
					batcher.Image(actorPreview.PreviewImage, actorBrushPosition, actorPreview.Origin, Vector2.One, 0, Color.White);
				}
				else if (brushEntry is { } placing)
				{
					var previewTileRect = TilePlacing ? NormalizeTileRect(tileRectPaintFrom, ClampTile(editing, tileOver)) : new RectInt(tileOver.X, tileOver.Y, 1, 1);
					var cursorRect = TileRectToRoomWorldRect(editing, previewTileRect).Inflate(gridWeight * 2);
					var cursorColor = tileTypePlacing == '0' ? Color.Red : highlight;
					batcher.RectLine(cursorRect, gridWeight * 2, cursorColor);
					if (tileTypePlacing != '0')
					{
						if (slopePaint && SlopePaintAvailable && TilePlacing)
						{
							// The inferred slope subtextures need a geometry rebuild per cell, so just
							// outline the triangle we'll fill -- its hypotenuse is the ramp surface.
							var clampedEnd = ClampTile(editing, tileOver);
							var toRight = clampedEnd.X >= tileRectPaintFrom.X;
							var underhang = clampedEnd.Y < tileRectPaintFrom.Y;

							Vector2 Corner(int tx, int ty) => roomTopLeft + new Vector2(tx * Game.TileSize, ty * Game.TileSize);
							var tl = Corner(previewTileRect.Left, previewTileRect.Top);
							var tr = Corner(previewTileRect.Right, previewTileRect.Top);
							var bl = Corner(previewTileRect.Left, previewTileRect.Bottom);
							var br = Corner(previewTileRect.Right, previewTileRect.Bottom);
							var (a, b, c) = (toRight, underhang) switch
							{
								(true, false) => (tl, bl, br),  // down-right floor
								(false, false) => (tr, br, bl), // down-left floor
								(true, true) => (tl, tr, bl),   // up-right underhang
								(false, true) => (tl, tr, br),  // up-left underhang
							};
							batcher.Triangle(a, b, c, highlight * 0.45f);
						}
						else
							for (var y = previewTileRect.Top; y < previewTileRect.Bottom; y++)
								for (var x = previewTileRect.Left; x < previewTileRect.Right; x++)
								{
									var tile = new Point2(x, y);
									var image = placing.Image;
									batcher.Image(image, roomTopLeft + tile * Game.TileSize + placing.Offset, placing.Origin, Vector2.One, 0, Color.White);
								}
					}
				}
			}

			if (!editingRoomGeometry && !worldMouseBlocked && !eyedropper && !brushIsActor && brushEntry != null && TilePlacing && (Input.Mouse.LeftDown || Input.Mouse.RightDown))
			{
				tileRectPaintTo = ClampTile(editing, tileOver);
			}

			batcher.RectLine(roomBounds, gridWeight * 2, Color.Black);
			if (editorSelectionRect is { } selectedRect)
			{
				var selectedBounds = TileRectToRoomWorldRect(editing, selectedRect).Inflate(gridWeight * 2);
				var lineWidth = MathF.Max(1, gridWeight);
				var dash = MathF.Max(2, Game.TileSize * 0.25f);
				var offset = (Time.SecondsF * 0.45f) % (dash * 2);
				var tl = selectedBounds.TopLeft;
				var tr = selectedBounds.TopRight;
				var bl = selectedBounds.BottomLeft;
				var br = selectedBounds.BottomRight;
				batcher.LineDashed(tl, tr, lineWidth, highlight, dash, offset);
				batcher.LineDashed(tr, br, lineWidth, highlight, dash, offset);
				batcher.LineDashed(br, bl, lineWidth, highlight, dash, offset);
				batcher.LineDashed(bl, tl, lineWidth, highlight, dash, offset);
			}
			if (editingRoomGeometry)
				DrawRoomGeometryHandles(editing, gridWeight);
			DrawPlaytestPlayerMarker(editing);
			DrawRevealDoorMarkers(editing, gridWeight);
			DrawPointPaths(editing, gridWeight);
			RunActorGizmos(editing, selectedRoomActorIndex, worldMouse, gridWeight,
				draw: true, canInteract: false, keyboardBlocked);
			if (!editingRoomGeometry && actorSelectActive)
				DrawRoomActorOverlays(editing, gridWeight, hoveredActorIndex, worldMouse);
			if (maskPaintActorId != null)
			{
				var brushColor = maskPaintSlot < 0 ? new Color(255, 90, 90, 255) : Color.White;
				batcher.CircleLine(worldMouse, maskPaintRadius, gridWeight, 48, brushColor);
				var core = maskPaintRadius * (1.0f - maskPaintSoftness);
				if (core > 1.0f)
					batcher.CircleLine(worldMouse, core, gridWeight, 48, brushColor * 0.4f);
			}

			// Right-click the "proposed" adjacent room to open the menu that adds a room there.
			if (proposedRoomTiles is { } proposal && !worldMouseBlocked && inner.Contains(Cursor) && Input.Mouse.RightPressed && !eyedropper)
			{
				pendingCreateRoomTiles = proposal;
				createRoomPopupRequested = true;
			}

			// Keep the pending proposal on screen while the menu is up: ImGui owns the mouse then,
			// so proposedRoomTiles is null and the rect would otherwise vanish under the menu.
			if ((pendingCreateRoomTiles ?? proposedRoomTiles) is { } showProposal)
			{
				var proposalBounds = TileRectToWorldRect(showProposal);
				batcher.Rect(proposalBounds, Color.Gray * 0.35f);
				batcher.RectLine(proposalBounds, gridWeight * 2, Color.Gray);
			}
		}

		batcher.PopMatrix();
		batcher.PopScissor();

		if (!Input.Mouse.LeftDown && !Input.Mouse.RightDown)
		{
			if (TilePlacing && CurrentRoom is { } room)
			{
				if (slopePaint && tileTypePlacing != '0' && SlopePaintAvailable)
					ApplySlopeRect(room, tileRectPaintFrom, tileRectPaintTo, tileTypePlacing);
				else
					ApplyTileRect(room, NormalizeTileRect(tileRectPaintFrom, tileRectPaintTo), activeTileLayer, tileTypePlacing);
			}
			tileTypePlacing = brush;
			if (TilePlacing)
				ClearEditorDragState();
			undo.CommitPending(TilePaintUndoSlot);
		}

		using (PerfStats.Time(PerfDetail.EditorSync))
		{
			if (editorGameDirty && (TilePlacing || DragKind == EditorDragKind.RoomGeometry))
			{
				if (CurrentRoom is { } room)
					MarkRoomPreviewDirty(room);
				SyncEditorGame();
			}
			else if (editorGameDirty)
			{
				editorGameDirty = false;
				if (CurrentRoom is { } room)
				{
					MarkRoomPreviewDirty(room);
					room.Save();
				}
				SyncEditorGame();
			}
		}
	}

	bool HasAdjacentEditorRoom(Point2 direction)
	{
		if (CurrentRoom is not { } room)
			return false;

		return Assets.FindAdjacentRoom(room, direction, EditorExitPosition(room, direction)) != null;
	}

	void DrawEditorSelectionOptions()
	{
		if (DragKind != EditorDragKind.SelectionRect && editorSelectionRect == null)
			return;

		var flags =
			ImGuiWindowFlags.NoDecoration |
			ImGuiWindowFlags.AlwaysAutoResize |
			ImGuiWindowFlags.NoSavedSettings |
			ImGuiWindowFlags.NoMove |
			ImGuiWindowFlags.NoFocusOnAppearing;

		var pos = new Vector2(PaletteSlideRect.Right + 10, ToolbarHeight + 10);
		ImGui.SetNextWindowPos(pos, ImGuiCond.Always);
		ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(8, 6));
		ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(6, 3));
		ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.04f, 0.04f, 0.05f, 0.82f));
		ImGui.Begin("Selection Options", flags);
		EditorGui.CheckboxFlags("Empty Tiles", ref selectionSnapshotFlags, SnapshotFlags.IncludeEmptyTiles);
		ImGui.Checkbox("BG Tiles", ref selectionIncludeBackgroundTiles);
		ImGui.Checkbox("FG Tiles", ref selectionIncludeForegroundTiles);
		ImGui.Checkbox("Actors", ref selectionIncludeActors);

		if (editorSelectionRect != null)
		{
			ImGui.Separator();
			if (ImGui.Button("Flip X"))
				FlipSelection(horizontal: true);
			ImGui.SameLine();
			if (ImGui.Button("Flip Y"))
				FlipSelection(horizontal: false);
		}

		ImGui.End();
		ImGui.PopStyleColor();
		ImGui.PopStyleVar(2);
	}

	// Mirror the current selection in place (respecting the BG/FG/Actors checkboxes), reusing the
	// move/duplicate snapshot path: capture, flip the captured tiles + actor positions, write back at
	// the same rect. Slope/dual tiles re-infer their facing from the mirrored surroundings on rebuild.
	void FlipSelection(bool horizontal)
	{
		if (CurrentRoom is not { } room || editorSelectionRect == null)
			return;

		using (CaptureRoomUndo(horizontal ? "Flip Selection X" : "Flip Selection Y", room))
		{
			var snapshot = CaptureEditorSelection(room);
			MirrorSnapshot(room, snapshot, horizontal);
			ApplyEditorSelectionSnapshot(room, snapshot, Point2.Zero, duplicate: false);
		}

		MarkRoomPreviewDirty(room);
		SyncEditorGame();
	}

	void MirrorSnapshot(Room room, EditorSelectionSnapshot snapshot, bool horizontal)
	{
		if (snapshot.ForegroundTiles != null)
			MirrorTiles(snapshot.ForegroundTiles, horizontal);
		if (snapshot.BackgroundTiles != null)
			MirrorTiles(snapshot.BackgroundTiles, horizontal);

		if (!snapshot.IncludeActors)
			return;

		// Mirror each actor's bounding box across the selection's centre line, preserving the actor's
		// own position-to-bounds offset so off-centre anchors land correctly.
		var rect = snapshot.Rect;
		var minPx = (horizontal ? rect.Left : rect.Top) * Game.TileSize;
		var maxPx = (horizontal ? rect.Right : rect.Bottom) * Game.TileSize;
		var origin = horizontal ? room.WorldBounds.X : room.WorldBounds.Y;

		for (var i = 0; i < snapshot.Actors.Count; i++)
		{
			var (index, actor, position) = snapshot.Actors[i];
			var bounds = RoomActorBounds(room, actor);
			var lo = (horizontal ? bounds.Left : bounds.Top) - origin;
			var hi = (horizontal ? bounds.Right : bounds.Bottom) - origin;
			var coord = horizontal ? position.X : position.Y;
			var mirrored = minPx + maxPx - lo - hi + coord;
			snapshot.Actors[i] = (index, actor, horizontal ? new Point2(mirrored, position.Y) : new Point2(position.X, mirrored));
		}
	}

	static void MirrorTiles(char[,] tiles, bool horizontal)
	{
		var w = tiles.GetLength(0);
		var h = tiles.GetLength(1);
		if (horizontal)
		{
			for (var y = 0; y < h; y++)
				for (var x = 0; x < w / 2; x++)
					(tiles[x, y], tiles[w - 1 - x, y]) = (tiles[w - 1 - x, y], tiles[x, y]);
		}
		else
		{
			for (var x = 0; x < w; x++)
				for (var y = 0; y < h / 2; y++)
					(tiles[x, y], tiles[x, h - 1 - y]) = (tiles[x, h - 1 - y], tiles[x, y]);
		}
	}

	RectInt? GetProposedRoomTiles(Room source, Vector2 worldMouse)
	{
		var sourceTiles = new RectInt(source.TileSpacePosition, source.Size);
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

	RectInt? FitProposedRoom(RectInt source, Point2 direction, int mouseAxis)
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
			var roomTiles = new RectInt(room.TileSpacePosition, room.Size);
			if (!candidate.Overlaps(roomTiles))
				continue;

			var blockStart = Math.Max(start, vertical ? roomTiles.Top : roomTiles.Left);
			var blockEnd = Math.Min(end, vertical ? roomTiles.Bottom : roomTiles.Right);
			for (var i = blockStart; i < blockEnd; i++)
				blocked[i - start] = true;
		}

		var index = mouseAxis - start;
		if (index < 0 || index >= blocked.Length || blocked[index])
			return null;

		var freeStart = index;
		while (freeStart > 0 && !blocked[freeStart - 1])
			freeStart--;

		var freeEnd = index + 1;
		while (freeEnd < blocked.Length && !blocked[freeEnd])
			freeEnd++;

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

	static Rect TileRectToWorldRect(RectInt tiles)
		=> new(tiles.X * Game.TileSize, tiles.Y * Game.TileSize, tiles.Width * Game.TileSize, tiles.Height * Game.TileSize);

	void CreateRoom(Point2 position, Point2 size)
	{
		var sourceSettings = CurrentRoom is { } source ? CaptureRoomSettings(source) : (RoomSettings?)null;
		CurrentRoom?.Save();

		var it = new Room(position, size);
		if (sourceSettings is { } settings)
			ApplyRoomSettings(it, settings);
		MirrorAdjacentRoomBorders(it);

		using (CaptureRoomExistenceUndo("Create Room", it))
		{
			AddRoomToWorld(it);
			SelectRoom(it);
			SyncEditorGame();
		}

		TweenEditorCameraToRoom(it);
	}

	static void MirrorAdjacentRoomBorders(Room room)
	{
		for (var x = 0; x < room.Size.X; x++)
		{
			room.Tiles[x, 0] = SampleAdjacentRoomTile(room, new Point2(room.TileSpacePosition.X + x, room.TileSpacePosition.Y - 1));
			room.Tiles[x, room.Size.Y - 1] = SampleAdjacentRoomTile(room, new Point2(room.TileSpacePosition.X + x, room.TileSpacePosition.Y + room.Size.Y));
		}

		for (var y = 0; y < room.Size.Y; y++)
		{
			room.Tiles[0, y] = SampleAdjacentRoomTile(room, new Point2(room.TileSpacePosition.X - 1, room.TileSpacePosition.Y + y));
			room.Tiles[room.Size.X - 1, y] = SampleAdjacentRoomTile(room, new Point2(room.TileSpacePosition.X + room.Size.X, room.TileSpacePosition.Y + y));
		}

		room.RebuildGeometry();
	}

	static char SampleAdjacentRoomTile(Room exclude, Point2 worldTile)
	{
		foreach (var room in Assets.RoomsById.Values)
		{
			if (room == exclude)
				continue;
			if (worldTile.X < room.TileSpacePosition.X || worldTile.Y < room.TileSpacePosition.Y ||
				worldTile.X >= room.TileSpacePosition.X + room.Size.X || worldTile.Y >= room.TileSpacePosition.Y + room.Size.Y)
				continue;

			return room.Tiles[worldTile.X - room.TileSpacePosition.X, worldTile.Y - room.TileSpacePosition.Y];
		}

		return '0';
	}

	#if SHOW_EDITOR_ALLOCS
	long gcBytesBeforeEditorTween;
	#endif

	/// Shifts to display a new room
	void ShiftRoom(Point2 direction)
	{
		var current = CurrentRoom;
		if (current == null) return;

#if DEBUG
#if SHOW_EDITOR_ALLOCS
		gcBytesBeforeEditorTween = GC.GetAllocatedBytesForCurrentThread();
#endif
#endif

		var exitPosition = EditorExitPosition(current, direction);
		if (Assets.FindAdjacentRoom(current, direction, exitPosition) is not { } adjacent)
			return;

		current.Save();
		SelectRoom(adjacent);
		editorRoomEntryDirection = direction;
		SyncEditorGame();
		TweenEditorCameraToRoom(adjacent);
	}

	static Vector2 EditorExitPosition(Room room, Point2 direction)
	{
		var bounds = room.WorldBounds;
		if (direction.X > 0) return new(bounds.Right, bounds.CenterY);
		if (direction.X < 0) return new(bounds.Left - 1, bounds.CenterY);
		if (direction.Y > 0) return new(bounds.CenterX, bounds.Bottom);
		if (direction.Y < 0) return new(bounds.CenterX, bounds.Top - 1);
		return bounds.Center;
	}

	void DrawCenteredRoomName(Room? room)
	{
		if (room == null)
		{
			DrawCenteredToolbarText($"null Room");
			return;
		}

		if (editingRoomName && roomNameEditingRoomId != room.Id)
			CancelRoomRename();

		if (editingRoomName)
		{
			var width = Math.Max(160, ImGui.CalcTextSize(roomNameEditor).X + 24);
			var windowPos = ImGui.GetWindowPos();
			var windowSize = ImGui.GetWindowSize();
			var pos = new Vector2(
				windowPos.X + windowSize.X * 0.5f - width * 0.5f,
				windowPos.Y + windowSize.Y * 0.5f - ImGui.GetFrameHeight() * 0.5f);

			ImGui.SetCursorScreenPos(pos);
			ImGui.SetNextItemWidth(width);
			if (focusRoomNameEditor)
			{
				ImGui.SetKeyboardFocusHere();
				focusRoomNameEditor = false;
			}

			var commit = ImGui.InputText("##RoomNameEditor", ref roomNameEditor, 128, ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.AutoSelectAll);
			var cancel = ImGui.IsKeyPressed(ImGuiKey.Escape);
			if (cancel)
				CancelRoomRename();
			else if (commit || ImGui.IsItemDeactivated())
				CommitRoomRename(room);
			return;
		}

		DrawCenteredToolbarText(room.Id, canDoubleClick: true);
		var textHovered = ImGui.IsItemHovered();
		if (textHovered && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
			BeginRoomRename(room);

		var deleteSize = new Vector2(ButtonSize, ButtonSize);
		var deletePos = ImGui.GetItemRectMax() + new Vector2(ToolbarSpacing, -ButtonSize * 0.5f - ImGui.GetItemRectSize().Y * 0.5f);
		var deleteRect = new Rect(deletePos, deleteSize);
		var mouse = ImGui.GetMousePos();
		var showDelete = textHovered || deleteRect.Contains(mouse);
		if (showDelete)
		{
			ImGui.SetCursorScreenPos(deletePos);
			if (ImGuiSubtextureButton("DeleteRoom", Assets.GetSubtexture("buttons/14"), "Delete room", false, deleteSize, imagePadding: ToolbarButtonImagePadding))
			{
				pendingDeleteRoomId = room.Id;
				ImGui.OpenPopup("Delete Room?");
			}
		}
	}

	void BeginRoomRename(Room room)
	{
		editingRoomName = true;
		focusRoomNameEditor = true;
		roomNameEditor = room.Id;
		roomNameEditingRoomId = room.Id;
	}

	void CancelRoomRename()
	{
		editingRoomName = false;
		focusRoomNameEditor = false;
		roomNameEditor = string.Empty;
		roomNameEditingRoomId = null;
	}

	void CommitRoomRename(Room room)
	{
		var next = roomNameEditor.Trim();
		if (!Assets.CanRenameRoom(room, next))
		{
			roomNameEditor = room.Id;
			CancelRoomRename();
			return;
		}

		if (Assets.RenameRoom(room, next))
			selectedRoomId = room.Id;

		CancelRoomRename();
	}

	// Opening is requested from Editor(), which runs earlier in the frame at a different ImGui ID
	// stack level; OpenPopup happens here so it pairs with BeginPopup below.
	void DrawCreateRoomPopup()
	{
		if (createRoomPopupRequested)
		{
			createRoomPopupRequested = false;
			ImGui.OpenPopup("Create Room");
		}

		if (!ImGui.BeginPopup("Create Room"))
		{
			pendingCreateRoomTiles = null;
			return;
		}

		if (ImGui.Selectable("Create New Room") && pendingCreateRoomTiles is { } proposal)
		{
			CreateRoom(proposal.Position, proposal.Size);
			pendingCreateRoomTiles = null;
			proposedRoomTiles = null;
			ImGui.CloseCurrentPopup();
		}

		ImGui.EndPopup();
	}

	void DrawDeleteRoomPopup()
	{
		var open = true;
		if (!ImGui.BeginPopupModal("Delete Room?", ref open, ImGuiWindowFlags.AlwaysAutoResize))
			return;

		var room = pendingDeleteRoomId != null ? Assets.GetRoom(pendingDeleteRoomId) : null;
		ImGui.Text(room != null ? $"Delete room '{room.Id}'?" : "Room no longer exists.");
		ImGui.Spacing();

		if (ImGui.Button("Delete", new Vector2(90, 0)))
		{
			if (room != null)
				DeleteRoom(room);
			pendingDeleteRoomId = null;
			ImGui.CloseCurrentPopup();
		}
		ImGui.SameLine();
		if (ImGui.Button("Cancel", new Vector2(90, 0)) || !open)
		{
			pendingDeleteRoomId = null;
			ImGui.CloseCurrentPopup();
		}

		ImGui.EndPopup();
	}

	void DeleteRoom(Room room)
	{
		using (CaptureRoomExistenceUndo("Delete Room", room))
			RemoveRoomFromWorld(room);
	}

	static void DrawCenteredToolbarText(ReadOnlySpan<char> text, bool canDoubleClick = false)
	{
		var size = ImGui.CalcTextSize(text);
		var windowPos = ImGui.GetWindowPos();
		var windowSize = ImGui.GetWindowSize();
		var pos = new Vector2(
			windowPos.X + windowSize.X * 0.5f - size.X * 0.5f,
			windowPos.Y + windowSize.Y * 0.5f - size.Y * 0.5f);
		if (canDoubleClick)
		{
			ImGui.SetCursorScreenPos(pos);
			ImGui.InvisibleButton("##CenteredToolbarText", size);
		}
		ImGui.GetWindowDrawList().AddText(pos, ImGui.GetColorU32(ImGuiCol.Text), text);
	}

	bool ToggleEditorPressed() => Input.Keyboard.Pressed(Keys.Tab) || Input.AnyControllerButtonPressed(Buttons.RightStick);

	void ToolbarImGui()
	{
		var flags =
			ImGuiWindowFlags.NoDecoration |
			ImGuiWindowFlags.NoMove |
			ImGuiWindowFlags.NoSavedSettings |
			ImGuiWindowFlags.NoScrollbar |
			ImGuiWindowFlags.NoScrollWithMouse |
			ImGuiWindowFlags.NoBringToFrontOnFocus;

		ImGui.SetNextWindowPos(Vector2.Zero, ImGuiCond.Always);
		var window = RequiredWindow;
		ImGui.SetNextWindowSize(new Vector2(window.WidthInPixels / imgui.Scale, ToolbarHeight), ImGuiCond.Always);

		ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(2, 2));
		ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(ButtonSpacing, 0));
		ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, Vector2.Zero);
		ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.18f, 0.08f, 0.15f, 1.0f));
		ImGui.Begin("Toolbar", flags);

		if (ImGuiIconButton("Play", Assets.GetSubtexture("buttons/0"), "Play [Tab/R3]", imagePadding: ToolbarButtonImagePadding))
			StartGame();
		ImGui.SameLine();

		if (ImGuiIconButton("Music Areas", Assets.GetSubtexture(musicEnabled ? "buttons/9" : "buttons/10"), "Music", musicEnabled, ToolbarButtonImagePadding))
		{
			musicEnabled = !musicEnabled;
			game?.RefreshMusic();
		}
		ImGui.SameLine();
		if (ImGuiIconButton("Preferences", Assets.GetSubtexture("buttons/17"), OperatingSystem.IsMacOS() ? "Preferences [Command+,]" : "Preferences", imagePadding: ToolbarButtonImagePadding))
			preferencesOpen = true;
		ImGui.SameLine();

#if !IOS
		if (ImGuiIconButton("AlwaysOnTop", Assets.GetSubtexture(alwaysOnTop ? "buttons/11" : "buttons/12"), "Always on Top", alwaysOnTop, ToolbarButtonImagePadding))
		{
			alwaysOnTop = !alwaysOnTop;
			window.AlwaysOnTop = alwaysOnTop;
		}
		ImGui.SameLine();

		if (ImGuiIconButton("OpenFmodStudio", Assets.GetSubtexture("buttons/13"), "Open FMOD Studio", imagePadding: ToolbarButtonImagePadding))
			OpenFmodStudio();
		ImGui.SameLine();

		if (ImGuiTextButton("Tile Kits", new Vector2(70, ButtonSize), tileKitEditorOpen))
			tileKitEditorOpen = !tileKitEditorOpen;
		ImGui.SameLine();

		if (ImGuiTextButton("NCT Tiles", new Vector2(70, ButtonSize), nctEditorOpen))
			nctEditorOpen = !nctEditorOpen;
		ImGui.SameLine();

		if (ImGuiTextButton("Particles", new Vector2(70, ButtonSize), particleEditor.PinnedWindowOpen))
			particleEditor.TogglePinnedWindow();
		ImGui.SameLine();

		if (ImGuiTextButton("Music", new Vector2(55, ButtonSize), musicRegionsOpen))
			musicRegionsOpen = !musicRegionsOpen;
		ImGui.SameLine(0, ToolbarSpacing);

		//if (ImGuiTextButton("Transitions", new Vector2(70, ButtonSize), showTransitionViewer))
		//showTransitionViewer = !showTransitionViewer;
		ImGui.SameLine(0, ToolbarSpacing);
#endif

		DrawWorldSelector();

		DrawCenteredRoomName(CurrentRoom);
		DrawDeleteRoomPopup();
		DrawCreateRoomPopup();

		ImGui.End();
		ImGui.PopStyleColor();
		ImGui.PopStyleVar(3);
	}

	void PreferencesImGui()
	{
		if (!preferencesOpen)
			return;

		ImGui.SetNextWindowSize(new Vector2(280, 0), ImGuiCond.FirstUseEver);
		if (!ImGui.Begin("Preferences", ref preferencesOpen, ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoCollapse))
		{
			ImGui.End();
			return;
		}

		ImGui.SetNextItemWidth(150);
		if (ImGui.BeginCombo("Scroll input", EditorScrollModeName(editorScrollMode)))
		{
			for (var i = 0; i <= (int)EditorScrollMode.Trackpad; i++)
			{
				var mode = (EditorScrollMode)i;
				if (ImGui.Selectable(EditorScrollModeName(mode), mode == editorScrollMode))
				{
					editorScrollMode = mode;
					trackpadScrollLatch = 0;
					SaveWindowSettings();
				}
			}
			ImGui.EndCombo();
		}
		if (ImGui.Checkbox("Preserve playtest save state", ref preservePlaytestSaveState))
			SaveWindowSettings();

		ImGui.End();
	}

	static string EditorScrollModeName(EditorScrollMode mode) => mode switch
	{
		EditorScrollMode.Mouse => "Mouse",
		EditorScrollMode.Trackpad => "Trackpad",
		_ => "Auto"
	};

	void DrawWorldSelector()
	{
		var worlds = Assets.GetWorldIds();
		if (worlds.Count == 0)
			return;

		var width = Math.Clamp(ImGui.CalcTextSize(Assets.CurrentWorldId).X + 32, 96, 180);
		ImGui.SetCursorPosY((ToolbarHeight - ImGui.GetFrameHeight()) * 0.5f);
		ImGui.SetNextItemWidth(width);
		if (ImGui.BeginCombo("##WorldSelector", Assets.CurrentWorldId))
		{
			foreach (var world in worlds)
			{
				var selected = string.Equals(world, Assets.CurrentWorldId, StringComparison.OrdinalIgnoreCase);
				if (ImGui.Selectable(world, selected))
					SwitchWorld(world);
				if (selected)
					ImGui.SetItemDefaultFocus();
			}

			ImGui.EndCombo();
		}
		ImGui.SameLine(0, ToolbarSpacing);
	}

	void SwitchWorld(int worldSwitch)
	{
		var worlds = Assets.GetWorldIds();
		if (worlds.Count == 0) return;

		var currentIdx = -1;
		for (var i = 0; i < worlds.Count; i++)
		{
			if (string.Equals(worlds[i], Assets.CurrentWorldId, StringComparison.OrdinalIgnoreCase))
			{
				currentIdx = i;
				break;
			}
		}

		if (currentIdx < 0)
			currentIdx = 0;

		var nextIdx = worldSwitch < 0
			? (currentIdx - 1 + worlds.Count) % worlds.Count
			: (currentIdx + 1) % worlds.Count;

		SwitchWorld(worlds[nextIdx]);
	}

	void SwitchWorld(string worldId)
	{
		if (string.Equals(worldId, Assets.CurrentWorldId, StringComparison.OrdinalIgnoreCase))
			return;

		if (playing || startingPlay)
			StopGame();

		CurrentRoom?.Save();
		Assets.SelectWorld(worldId);
		Assets.ReloadRooms();
		Assets.RebuildRoomGeometry();
		ClearRoomPreviewCache();
		undo.Clear();
		selectedRoomId = null;
		roomCreatePosition = Point2.Zero;
		editorSelectionRect = null;
		selectedRoomActorIndex = -1;
		pendingDeleteRoomId = null;
		ResolveSelectedRoom();
		FocusEditorOnCurrentRoom();
		SyncEditorGame();
	}




	void SelectRoom(Room room)
	{
		selectedRoomId = room.Id;
		roomCreatePosition = room.TileSpacePosition;
	}

	void ResolveSelectedRoom()
	{
		if (selectedRoomId != null && Assets.GetRoom(selectedRoomId) is { } byId)
		{
			SelectRoom(byId);
			return;
		}

		if (Assets.GetRoom(Point2.Zero) is { } origin)
		{
			SelectRoom(origin);
			return;
		}

		selectedRoomId = null;
		roomCreatePosition = Point2.Zero;
	}


	void TransitionViewerImGui()
	{
		if (!showTransitionViewer)
			return;

		if (ImGui.Begin("Transition Viewer", ref showTransitionViewer, ImGuiWindowFlags.NoSavedSettings))
		{
			if (ImGui.BeginTable("transitions", 4, ImGuiTableFlags.BordersV | ImGuiTableFlags.BordersOuterH | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY,
				new Vector2(ImGui.GetContentRegionAvail().X, 480)))
			{
				ImGui.TableSetupScrollFreeze(0, 1);
				ImGui.TableSetupColumn("Sprite", ImGuiTableColumnFlags.WidthFixed, 100);
				ImGui.TableSetupColumn("Animation", ImGuiTableColumnFlags.WidthFixed, 160);
				ImGui.TableSetupColumn("Frames", ImGuiTableColumnFlags.WidthStretch);
				ImGui.TableSetupColumn("Count", ImGuiTableColumnFlags.WidthFixed, 60);
				ImGui.TableHeadersRow();

				foreach (var (spriteName, sprite) in Assets.Sprites)
				{
					foreach (var anim in sprite.Animations)
					{
						if (!anim.Name.Contains("->"))
							continue;

						ImGui.TableNextRow();
						ImGui.TableNextColumn();
						ImGui.TextUnformatted(spriteName);
						ImGui.TableNextColumn();
						ImGui.TextUnformatted(anim.Name);
						ImGui.TableNextColumn();

						float previewSize = 48;
						for (int i = 0; i < anim.FrameCount; i++)
						{
							if (i > 0) ImGui.SameLine(0, 2);
							int frameIdx = anim.FrameStart + i;
							if (frameIdx < sprite.Frames.Count)
							{
								var frame = sprite.Frames[frameIdx];
								var sub = frame.VisibleSubtexture;
								float scale = MathF.Min(previewSize / sub.Size.X, previewSize / sub.Size.Y);
								imgui.Image(sub, sub.Size * scale);
								if (ImGui.IsItemHovered())
									ImGui.SetTooltip($"Frame {i}\n{frame.Duration * 1000:F0}ms");
							}
						}

						ImGui.TableNextColumn();
						ImGui.TextUnformatted($"{anim.FrameCount}");
					}
				}

				ImGui.EndTable();
			}
		}
		ImGui.End();
	}

}
#endif
