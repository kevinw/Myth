#if EDITOR
using LinkDotNet.StringBuilder;
using System.Numerics;
using System.Reflection;
using System.Text.Json.Nodes;
using Foster.Editor;
using Foster.Framework;
using ImGuiNET;
using static ImGuiNET.ImGui;

namespace Myth;

// Core editor: the zoomed-out room-arrangement world view, tile/actor tools, camera and zoom, and
// keyboard shortcuts -- the hub that the sibling Manager.Editor.* partials are peeled out of.
public partial class Manager
{
	enum EditorScrollMode
	{
		Auto,
		Mouse,
		Trackpad
	}

	static Vector2 ButtonSizeV = new(ButtonSize, ButtonSize);
	const float TileButtonSpacing = 1;
	static float BoxSpacing => 0;
	static float BoxPadding => 4;
	static int PaletteColumns => 5;
	static float PaletteWidth => BoxPadding * 2 + PaletteColumns * ButtonSize + (PaletteColumns - 1) * ButtonSpacing;
	static Color highlight => 0x5fcde4;

	bool editorGameDirty;
	bool editorViewInitialized;
	bool showFungalMarkers;
	Vector2 editorCamera;
	float editorZoom = 1.0f;
	float editorZoomTarget = 1.0f;
	int editorSublayer;
	const bool AutoSelectRoomByCamera = true;
	Vector2 editorCameraTweenFrom;
	Vector2 editorCameraTweenTo;
	float editorZoomTweenFrom;
	float editorZoomTweenTo;
	float editorCameraTween = 1.0f;
	Vector2? previousStableGameViewCenter;

	// Seconds left where wheel events are treated as a trackpad two-finger swipe (pan) rather than a
	// mouse wheel (zoom). A trackpad sends precise/fractional deltas; we latch on those so the stray
	// whole-number crossing mid-swipe doesn't blip a zoom. See UpdateEditorWorldView.
	float trackpadScrollLatch;
	EditorScrollMode editorScrollMode;
	bool preferencesOpen;
	bool imguiDemoOpen;
	bool editorGameRunInBackground = true;
	// Paste drops the actor under the middle of the editor view instead of at the position it
	// was copied from. Either way the paste is clamped into the room (see PasteActor).
	bool pasteActorAtViewCenter = true;
	static float TrackpadScrollLatchTime => 0.5f;
	// World pixels panned per unit of (small, ~0.1-0.4) trackpad wheel delta, before the zoom divide.
	static float EditorTrackpadPanSpeed => 20f;

	char brush = '0'; // The current Type we're painting in the Editor
	Factory.TileLayer activeTileLayer = Factory.TileLayer.Foreground;
	bool editorShowForegroundLayer = true;
	bool editorShowBackgroundLayer = true;

	bool editingRoomGeometry;
	// What the room held when a resize drag began, in room-local tiles, plus the top-left it began
	// at. Room.ResizeTo rebuilds the tile grids on every step of the drag, so what a shrink left
	// behind can only be reported by measuring this against the bounds the drag ended on.
	const string RoomResizeUndoName = "Resize Room";
	readonly List<Point2> roomResizeForegroundTiles = [];
	readonly List<Point2> roomResizeBackgroundTiles = [];
	readonly List<Point2> roomResizeActorTiles = [];
	string? roomResizeRoomId;
	Point2 roomResizeOrigin;
	Point2 roomResizeSize;
	bool roomResizePopupRequested;
	int roomResizeDiscardedForeground;
	int roomResizeDiscardedBackground;
	int roomResizeDiscardedActors;
	bool slopePaint; // when set, a tile-rect drag lays a slope staircase instead of a solid block
	RectInt? proposedRoomTiles;
	// Snapshot of the proposal the context menu was opened on: proposedRoomTiles follows the
	// cursor and goes null once ImGui takes the mouse, so the menu needs its own copy.
	RectInt? pendingCreateRoomTiles;
	bool createRoomPopupRequested;
	bool addTilePopupRequested;
	string newTileName = "";
	Factory.TileLayer newTileLayer;
	string? selectedActorSpecId;
	int selectedActorPaletteIndex = -1;
	ActorTag actorPaletteFilter = ActorTag.None; // None = show all
	bool actorSpecInspectorOpen;
	bool actorSelectTool = true;
	int selectedRoomActorIndex = -1;
	// The selected point of the selected path actor, plus the actor it was selected on. Actor
	// selection is reassigned from a couple dozen places, so rather than clear this at each of them,
	// SelectedPathPoint validates it on read and a stale selection simply reads as "none".
	int selectedPathPointActorIndex = -1;
	int selectedPathPointIndex = -1;
	const string ActorContextMenuPopup = "##ActorContextMenu";
	string? actorContextMenuRoomId;
	int actorContextMenuIndex = -1;
	int actorContextMenuPathPoint = -1;
	bool actorContextMenuRequested;
	bool createPrefabPopupRequested;
	string? createPrefabRoomId;
	string? createPrefabActorId;
	int createPrefabActorIndex = -1;
	string newPrefabName = "";
	// Click-to-cycle picking: a click that doesn't drag walks down the stack of overlapping actors
	// so hidden ones are reachable. Scratch + press state drive it.
	readonly List<int> actorPickScratch = [];
	readonly List<ActorSpecs.EditableProperty> actorPropertyScratch = [];
	Vector2 actorPressCursor;
	bool actorPressCanCycle;
	const float ActorPickCycleThreshold = 4f; // UI pixels: a release within this of the press is a click, not a drag
	bool ActorPressPassedDragThreshold
		=> Vector2.DistanceSquared(Cursor, actorPressCursor) > ActorPickCycleThreshold * ActorPickCycleThreshold;
	bool selectionIncludeBackgroundTiles = true;
	bool selectionIncludeForegroundTiles = true;
	SnapshotFlags selectionSnapshotFlags = SnapshotFlags.None;
	bool selectionIncludeActors = true;
	RectInt? editorSelectionRect;
	Point2 editorSelectionFrom;
	Point2 editorSelectionTo;
	EditorDragState editorDrag = EditorDragState.None;
	// Multi-room selection, meaningful only in room-select (zoomed-out) mode. The "primary" room
	// stays selectedRoomId/CurrentRoom (inspector + tile editing); this is the set moved together.
	readonly HashSet<string> selectedRoomIds = [];
	Vector2 roomPressCursor;              // screen-space press point, for the drag deadzone
	bool roomDragStarted;                 // a room press only becomes a move once past the deadzone
	Point2 roomMoveAnchorTile;            // world tile under the cursor at press
	readonly Dictionary<string, Point2> roomMoveOrigins = []; // per-room tile position at press
	Vector2 roomMarqueeFromWorld;
	Vector2 roomMarqueeToWorld;
	const float RoomDragDeadzone = 4f;    // UI pixels a room press must travel before it moves rooms
	const float RoomSnapScreenPx = 6f;    // magnetic snap threshold, screen pixels
	string? pickingLinkedDoorTargetForActorId;
	// Neural texture mask painting: which room actor is being painted, and the brush.
	string? maskPaintActorId;
	int maskPaintSlot;
	float maskPaintRadius = 24.0f;
	float maskPaintSoftness = 0.4f;
	float maskPaintStrength = 1.0f;
	int maskPaintPreviewSteps = 128;
	bool maskPaintStroke;
	readonly RoomColorFieldPainter roomColorFieldPainter = new();
	readonly JsonUndoManager undo = new();
	// Mutable struct on purpose: methods called through this field update its state in place.
	// Assigned in the Manager constructor (it needs `undo`).
	ParticleEffectEditor particleEditor;
#if !IOS && !BROWSER
	readonly ProceduralSoundEditor proceduralSoundEditor;
#endif
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

	/// Workspace: the whole view. The old toolbar strip is gone, replaced by the hamburger
	/// overlay (see Manager.EditorMenu.cs), which floats over the game view and costs no layout.
	Rect WorkRect => View;

	/// Palette View when the Editor is Open
	Rect PaletteOpenRect => new(WorkRect.X, WorkRect.Y, PaletteWidth, WorkRect.Height);

	/// Edit Workspace when the Editor is Open: the Game dock window's content rect, which the
	/// dock layout decides. Falls back to the plain centre region before the window exists.
	Rect EditorOpenRect
		=> gameViewWindowRect.Width > 1 && gameViewWindowRect.Height > 1
			? gameViewWindowRect
			: EditorCenterRect;

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

	internal void EditorUI()
	{
		// End-of-gesture commit for the inspector undo scopes (see OpenRoomSettingsUndo).
		if (!IsAnyItemActive())
		{
			undo.CommitPending(RoomSettingsUndoSlot);
			undo.CommitPending(ActorPropertyUndoSlot);
			undo.CommitPending(SurfaceUndoSlot);
		}

		if (gameEase < 1.0f && !startingPlay)
		{
			using (PerfStats.Time(PerfDetail.PaletteImGui))
				PaletteImGui();
			DrawPaintOverlay();
		}
		if (!playing && !startingPlay)
		{
			using (PerfStats.Time(PerfDetail.ToolbarImGui))
				EditorMenuImGui();
		}
		ResetDebugOverlayLayout();
		PlatformNavDebugOverlayImGui();
		ActorPositionDebugOverlayImGui();
		BudDebugOverlayImGui();
		LiveTileEditingImGui();
		LiveActorEditingImGui();
		LiveRoomEditingImGui();
		using (PerfStats.Time(PerfDetail.FpsOverlayImGui))
			FpsOverlayImGui();
		TransitionViewerImGui();
		RigAnimationViewerImGui();
		PreferencesImGui();
		if (imguiDemoOpen)
			ShowDemoWindow(ref imguiDemoOpen);
		if (particleEditor.PinnedWindowOpen)
			DockNextToolWindow("Particles##Pinned");
		particleEditor.PinnedWindowImGui(game, Scale, NoteDockedWindow);
		if (!playing && !startingPlay)
		{
			ActorInstancesImGui();
			TileKitEditorImGui();
			SurfaceEditorImGui();
			NctTileEditorImGui();
			MusicRegionsImGui();
		}
#if !IOS && !BROWSER
		if (proceduralSoundEditor.Open)
			DockNextToolWindow("Procedural Sounds");
		proceduralSoundEditor.Draw(NoteDockedWindow);
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
		CancelLiveTileEditing();
		CancelLiveActorEditing();
		EndRoomColorFieldPainting();
		EndMaskPainting();
		editingRoomGeometry = false;
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
			ClampRoomActorToRoom(room, copy);
			selectedRoomActorIndex = room.Actors.Count - 1;
			ClearEditorDragState();
			editorGameDirty = true;
		}
	}

	void CopySelectedActor(bool cut)
	{
		if (CurrentRoom is not { } room || selectedRoomActorIndex < 0 || selectedRoomActorIndex >= room.Actors.Count)
			return;
		if (cut && HasBurrowDoor(room.Actors[selectedRoomActorIndex]))
		{
			DisplayMessage("Remove burrow door links before cutting this line");
			return;
		}

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

	void DeleteSelectedActor()
	{
		if (CurrentRoom is not { } room || selectedRoomActorIndex < 0 || selectedRoomActorIndex >= room.Actors.Count)
			return;
		if (HasBurrowDoor(room.Actors[selectedRoomActorIndex]))
		{
			DisplayMessage("Remove burrow door links before deleting this line");
			return;
		}

		using (CaptureRoomUndo("Delete Actor", room))
		{
			room.Actors.RemoveAt(selectedRoomActorIndex);
			selectedRoomActorIndex = -1;
			ClearEditorDragState();
			editorSelectionRect = null;
			editorGameDirty = true;
		}
	}

	bool NudgeSelectedRoomActor(Point2 offset)
	{
		if (CurrentRoom is not { } room || selectedRoomActorIndex < 0 || selectedRoomActorIndex >= room.Actors.Count)
			return false;
		if (HasBurrowDoor(room.Actors[selectedRoomActorIndex]))
		{
			DisplayMessage("Remove burrow door links before moving this line");
			return false;
		}

		using (CaptureRoomUndo("Nudge Actor", room))
		{
			var actor = room.Actors[selectedRoomActorIndex];
			var position = ActorSpecs.ReadPoint(actor, "Position") ?? Point2.Zero;
			ActorSpecs.SetProperty(actor, "Position", ActorSpecs.ToJsonNode(position + offset));
			ClearEditorDragState();
			editorSelectionRect = null;
			editorGameDirty = true;
		}

		return true;
	}

	void OpenActorContextMenu(Room room, int actorIndex, int pathPoint = -1)
	{
		if (selectedRoomActorIndex != actorIndex)
			using (CaptureSelectionUndo("Select Actor"))
				selectedRoomActorIndex = actorIndex;
		if (pathPoint >= 0)
			SelectPathPoint(pathPoint);
		actorContextMenuRoomId = room.Id;
		actorContextMenuIndex = actorIndex;
		actorContextMenuPathPoint = pathPoint;
		ClearEditorDragState();
		editorSelectionRect = null;
		CancelTilePaint();
		actorContextMenuRequested = true;
	}

	void DrawActorContextMenu()
	{
		if (actorContextMenuRequested)
		{
			actorContextMenuRequested = false;
			OpenPopup(ActorContextMenuPopup);
		}
		if (!BeginPaddedPopup(ActorContextMenuPopup))
			return;

		if (CurrentRoom is { } room && room.Id == actorContextMenuRoomId &&
			actorContextMenuIndex >= 0 && actorContextMenuIndex < room.Actors.Count)
		{
			selectedRoomActorIndex = actorContextMenuIndex;
			var actor = room.Actors[actorContextMenuIndex];
			DrawBurrowDoorContextMenu(room, actor, actorContextMenuPathPoint);
			var sourceFilename = ActorZedLauncher.ActorSourceFilename(actor);
			var commandKey = OperatingSystem.IsMacOS() ? "Cmd" : "Ctrl";
			if (MenuItem(EditorIcons.DuplicateMenuItem, $"{commandKey}+D"))
				DuplicateSelectedActor();
			if (MenuItem(EditorIcons.CopyMenuItem, $"{commandKey}+C"))
				CopySelectedActor(cut: false);
			if (MenuItem(EditorIcons.CutMenuItem, $"{commandKey}+X"))
				CopySelectedActor(cut: true);
			if (MenuItem("Create Prefab…"))
				RequestCreatePrefab(room, actor);

			Separator();
			if (MenuItem(EditorIcons.DeleteMenuItem, "Delete"))
				DeleteSelectedActor();

			if (sourceFilename is { } filename)
			{
				Separator();
				if (MenuItem($"{EditorIcons.SourceFile}  Open {filename}"))
					ActorZedLauncher.OpenActor(actor);
			}
		}
		else
			CloseCurrentPopup();

		EndPaddedPopup();
	}

	void DrawBurrowDoorContextMenu(Room room, JsonObject actor, int pointIndex)
	{
		var count = PointPathPointCount(PointPathNodes(actor));
		if (!string.Equals(ActorSpecs.GetTypeName(actor), nameof(BurrowLine), StringComparison.OrdinalIgnoreCase) ||
			pointIndex != 0 && pointIndex != count - 1)
			return;

		var marked = IsBurrowDoorPoint(actor, pointIndex);
		if (MenuItem("Burrow Door", null, marked))
		{
			using (CaptureRoomUndo(marked ? "Unmark Burrow Door" : "Mark Burrow Door", room))
			{
				SetBurrowDoorPoint(actor, pointIndex, !marked);
				editorGameDirty = true;
				SyncEditorGame();
			}
			CloseCurrentPopup();
		}

		var worldPoint = (Point2)(PointPathOrigin(room, actor) + PointPathPoint(PointPathNodes(actor), pointIndex));
		EditorBurrowDoor match = default;
		var matches = marked ? FindBurrowDoorMatches(room, worldPoint, out match) : 0;
		if (!marked && MenuItem("Create Link to Sublayer"))
		{
			CreateBurrowDoorLink(room, actor, pointIndex, worldPoint);
			CloseCurrentPopup();
		}
		else if (marked)
		{
			if (MenuItem("Go to Linked Endpoint", null, false, matches == 1))
			{
				SelectRoom(match.Room);
				selectedRoomActorIndex = match.ActorIndex;
				SelectPathPoint(match.PointIndex);
				TweenEditorCameraToRoom(match.Room);
				CloseCurrentPopup();
			}
			if (MenuItem("Remove Sublayer Link"))
			{
				RemoveBurrowDoorLink(room, actor, pointIndex, matches == 1 ? match : null);
				CloseCurrentPopup();
			}
		}
		Separator();
	}

	void CreateBurrowDoorLink(Room sourceRoom, JsonObject sourceActor, int sourcePoint, Point2 worldPoint)
	{
		var targetRoom = FindNextSublayerRoom(sourceRoom, worldPoint);
		var createdRoom = targetRoom == null;
		targetRoom ??= NewSublayerRoom(sourceRoom);

		var targets = createdRoom
			? new IJsonUndoTarget[] { CurrentRoomUndoTarget(sourceRoom), new RoomExistenceUndoTarget(this, targetRoom), EditorSelectionUndoTarget() }
			: new IJsonUndoTarget[] { CurrentRoomUndoTarget(sourceRoom), CurrentRoomUndoTarget(targetRoom), EditorSelectionUndoTarget() };
		using (undo.Capture("Create Burrow Door Link", targets))
		{
			if (createdRoom)
				AddRoomToWorld(targetRoom);

			var sourcePoints = PointPathPoints(sourceActor);
			var neighbor = sourcePoint == 0 ? 1 : sourcePoints.Length - 2;
			var continuation = sourcePoints[sourcePoint] - sourcePoints[neighbor];
			if (continuation == Point2.Zero)
				continuation = Point2.Right * Game.TileSize;

			targetRoom.AddActorType(nameof(BurrowLine), worldPoint);
			var targetActor = targetRoom.Actors[^1];
			ActorSpecs.SetProperty(targetActor, nameof(IPointPath.Points),
				ActorSpecs.ToJsonNode(new Point2[] { Point2.Zero, continuation }));
			SetBurrowDoorPoint(sourceActor, sourcePoint, true);
			SetBurrowDoorPoint(targetActor, 0, true);
			SelectRoom(targetRoom);
			selectedRoomActorIndex = targetRoom.Actors.Count - 1;
			SelectPathPoint(0);
			editorGameDirty = true;
			SyncEditorGame();
		}
		TweenEditorCameraToRoom(targetRoom);
	}

	void RemoveBurrowDoorLink(Room room, JsonObject actor, int pointIndex, EditorBurrowDoor? match)
	{
		using var capture = match is { } linked
			? CaptureRoomUndo("Remove Burrow Door Link", room, linked.Room)
			: CaptureRoomUndo("Remove Burrow Door Link", room);
		SetBurrowDoorPoint(actor, pointIndex, false);
		if (match is { } target)
			SetBurrowDoorPoint(target.Actor, target.PointIndex, false);
		editorGameDirty = true;
		SyncEditorGame();
	}

	static Room? FindNextSublayerRoom(Room source, Point2 worldPoint)
	{
		Room? next = null;
		Room? wrapped = null;
		foreach (var room in Assets.RoomsById.Values)
		{
			if (room.Sublayer == source.Sublayer || !room.WorldBounds.Inflate(1).Contains(worldPoint))
				continue;
			if (room.Sublayer > source.Sublayer && (next == null || room.Sublayer < next.Sublayer))
				next = room;
			if (wrapped == null || room.Sublayer < wrapped.Sublayer)
				wrapped = room;
		}
		return next ?? wrapped;
	}

	void RequestCreatePrefab(Room room, JsonObject actor)
	{
		createPrefabRoomId = room.Id;
		createPrefabActorId = ActorSpecs.GetActorId(actor);
		createPrefabActorIndex = room.Actors.IndexOf(actor);
		var type = ActorSpecs.ReadString(actor, "From") ?? ActorSpecs.ReadString(actor, "Type") ?? "Actor";
		var baseName = PrefabNameSuggestion(type);
		newPrefabName = baseName;
		for (var suffix = 2; !CanCreatePrefab(newPrefabName); suffix++)
			newPrefabName = baseName + suffix;
		createPrefabPopupRequested = true;
	}

	void DrawCreatePrefabPopup()
	{
		if (createPrefabPopupRequested)
		{
			createPrefabPopupRequested = false;
			OpenPopup("Create Prefab");
		}

		var open = true;
		if (!BeginPaddedPopupModal("Create Prefab", ref open, ImGuiWindowFlags.AlwaysAutoResize))
			return;

		TextUnformatted("Create a prefab from this actor:");
		SetNextItemWidth(260);
		if (IsWindowAppearing())
			SetKeyboardFocusHere();
		var submit = InputTextWithHint("##PrefabName", "PrefabName", ref newPrefabName, 64,
			ImGuiInputTextFlags.EnterReturnsTrue);
		var name = newPrefabName.Trim();
		var valid = CanCreatePrefab(name) && FindCreatePrefabActor() != null;
		BeginDisabled(!valid);
		var create = Button("Create", new Vector2(90, 0)) || submit;
		EndDisabled();
		SameLine();
		var cancel = Button("Cancel", new Vector2(90, 0)) || !open;

		if (create && valid && FindCreatePrefabActor() is { } found)
		{
			CreatePrefabFromActor(found.Room, found.Index, name);
			ClearCreatePrefabRequest();
			CloseCurrentPopup();
		}
		else if (cancel)
		{
			ClearCreatePrefabRequest();
			CloseCurrentPopup();
		}

		EndPaddedPopup();
	}

	readonly record struct PrefabActor(Room Room, int Index);

	PrefabActor? FindCreatePrefabActor()
	{
		if (createPrefabRoomId == null || Assets.GetRoom(createPrefabRoomId) is not { } room)
			return null;
		if (createPrefabActorId != null)
			for (var i = 0; i < room.Actors.Count; i++)
				if (ActorSpecs.GetActorId(room.Actors[i]) == createPrefabActorId)
					return new(room, i);
		if (createPrefabActorIndex >= 0 && createPrefabActorIndex < room.Actors.Count)
			return new(room, createPrefabActorIndex);
		return null;
	}

	static string PrefabNameSuggestion(string type)
	{
		var name = new ValueStringBuilder(stackalloc char[type.Length + "ActorPrefab".Length]);
		foreach (var c in type)
			if (char.IsAsciiLetterOrDigit(c))
				name.Append(c);
		if (name.Length == 0 || !char.IsAsciiLetter(name[0]))
			name.Insert(0, "Actor");
		name.Append("Prefab");
		return name.ToString();
	}

	static bool CanCreatePrefab(string name)
		=> name.Length > 0 && char.IsAsciiLetter(name[0])
			&& name.All(char.IsAsciiLetterOrDigit)
			&& ActorSpecs.Get(name) == null
			&& !File.Exists(PrefabPath(name));

	static string PrefabPath(string name)
	{
		var filename = new ValueStringBuilder(stackalloc char[name.Length * 2]);
		for (var i = 0; i < name.Length; i++)
		{
			var c = name[i];
			if (i > 0 && char.IsAsciiLetterUpper(c) && char.IsAsciiLetterLower(name[i - 1]))
				filename.Append('-');
			filename.Append(char.ToLowerInvariant(c));
		}
		return Path.Join(Assets.AssetsPath, ActorSpecs.ContentFolder, filename.ToString() + ".json");
	}

	void CreatePrefabFromActor(Room room, int actorIndex, string name)
	{
		var actor = room.Actors[actorIndex];
		var path = PrefabPath(name);
		using (undo.Capture("Create Prefab", CurrentRoomUndoTarget(room),
			new ActorSpecExistenceUndoTarget(name, path), EditorSelectionUndoTarget()))
		{
			var spec = ActorSpecs.PruneForSave(actor);
			JsonUtil.RemoveProperty(spec, "Id");
			JsonUtil.RemoveProperty(spec, "Position");
			ActorSpecs.CreateSpec(name, path, spec);

			var instance = new JsonObject
			{
				["Id"] = ActorSpecs.GetActorId(actor),
				["From"] = name,
			};
			if (JsonUtil.TryGetProperty(actor, "Position", out var position))
				instance["Position"] = position?.DeepClone();
			room.Actors[actorIndex] = instance;
			selectedRoomActorIndex = actorIndex;
			selectedActorSpecId = name;
			actorSpecInspectorOpen = true;
		}

		room.Save();
		editorGameDirty = true;
		SyncEditorGame();
	}

	void ClearCreatePrefabRequest()
	{
		createPrefabRoomId = null;
		createPrefabActorId = null;
		createPrefabActorIndex = -1;
		newPrefabName = "";
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
			if (pasteActorAtViewCenter)
				CenterRoomActorOnEditorCamera(room, actor);
			ClampRoomActorToRoom(room, actor);
			selectedRoomActorIndex = room.Actors.Count - 1;
			SelectEditorActorTool();
			ClearEditorDragState();
			editorSelectionRect = null;
			editorGameDirty = true;
		}
	}

	bool UpdateEditorShortcuts()
	{
		if (OperatingSystem.IsMacOS() && Input.Keyboard.CtrlOrCommand && Input.Keyboard.Pressed(Keys.Comma))
		{
			preferencesOpen = true;
			return true;
		}
		var modifier =
			Input.Keyboard.Down(Keys.LeftControl) ||
			Input.Keyboard.Down(Keys.RightControl) ||
			Input.Keyboard.Down(Keys.LeftOS) ||
			Input.Keyboard.Down(Keys.RightOS);
		if (modifier && Input.Keyboard.Pressed(Keys.S) && SaveSelectedActorSpec())
			return true;
		if (ImGuiWantsKeyboard)
			return false;

		if (Input.Keyboard.Pressed(Keys.Escape))
		{
			SelectEditorActorTool();
		}

		if (!modifier)
		{
			if (Input.Keyboard.Pressed(Keys.P) && CurrentRoom is { } room)
			{
				if (roomColorFieldPainter.IsActive(room))
					EndRoomColorFieldPainting();
				else
				{
					EndMaskPainting();
					roomColorFieldPainter.Start(room, activeTileLayer);
				}
				return true;
			}
			return false;
		}

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

	void PreserveEditorViewAcrossLayoutChanges(Rect view)
	{
		if (gameEase > 0 || editorCameraTween < 1)
		{
			previousStableGameViewCenter = null;
			return;
		}

		var center = view.Center;
		if (previousStableGameViewCenter is { } previous)
			editorCamera += (center - previous) / editorZoom;
		previousStableGameViewCenter = center;
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
		var keyMove = WasdDirection(down: true);
		// Keyboard Shift + WASD moves the camera around incrementally.
		if (Input.Keyboard.Shift && keyMove != Point2.Zero)
		{
			editorCameraMoveMultiplier += Time.Delta * EDITOR_CAM_MOVE_MULTIPLIER_SPEED_UP;
			editorCamera += (Vector2)keyMove * Time.Delta * 700.0f / editorZoom * editorCameraMoveMultiplier;
			return;
		}

		if (ArrowDirection() is { } nudge && nudge != Point2.Zero)
		{
			editorCameraMoveMultiplier = BASE_EDITOR_CAM_MOVE_MULTIPLIER;
			if (NudgeSelectedRoomActor(nudge))
				controls.Move.Clear();
			return;
		}

		// Otherwise, we jump to adjacent rooms.
		editorCameraMoveMultiplier = BASE_EDITOR_CAM_MOVE_MULTIPLIER;
		var direction = WasdDirection(down: false);
		if (direction == Point2.Zero)
		{
			if (controls.Move.PressedUp) direction += Point2.Up;
			if (controls.Move.PressedDown) direction += Point2.Down;
			if (controls.Move.PressedLeft) direction += Point2.Left;
			if (controls.Move.PressedRight) direction += Point2.Right;
		}

		if (direction != Point2.Zero)
		{
			using (CaptureSelectionUndo("Select Room"))
				ShiftRoom(direction);
			controls.Move.Clear();
		}
	}

	Point2 WasdDirection(bool down)
	{
		var direction = Point2.Zero;
		if (down ? Input.Keyboard.Down(Keys.W) : Input.Keyboard.Pressed(Keys.W)) direction += Point2.Up;
		if (down ? Input.Keyboard.Down(Keys.S) : Input.Keyboard.Pressed(Keys.S)) direction += Point2.Down;
		if (down ? Input.Keyboard.Down(Keys.A) : Input.Keyboard.Pressed(Keys.A)) direction += Point2.Left;
		if (down ? Input.Keyboard.Down(Keys.D) : Input.Keyboard.Pressed(Keys.D)) direction += Point2.Right;
		return direction;
	}

	Point2 ArrowDirection()
	{
		var direction = Point2.Zero;
		if (Input.Keyboard.Pressed(Keys.Up)) direction += Point2.Up;
		if (Input.Keyboard.Pressed(Keys.Down)) direction += Point2.Down;
		if (Input.Keyboard.Pressed(Keys.Left)) direction += Point2.Left;
		if (Input.Keyboard.Pressed(Keys.Right)) direction += Point2.Right;
		return direction;
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

		var pos = new Vector2(EditorPaletteDockRect.Right + 10, WorkRect.Y + EditorMenuButtonSize + 12);
		SetNextWindowPos(pos, ImGuiCond.Always);
		PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(8, 6));
		PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.04f, 0.04f, 0.05f, 0.82f));
		Begin("Tile Paint Options", flags);
		Checkbox("Slopes", ref slopePaint);
		if (IsItemHovered())
			SetTooltip("Drag a rectangle to lay a slope staircase.\nDirection follows the drag (down = floor, up = underhang).");
		End();
		PopStyleColor();
		PopStyleVar();
	}

	void SelectEditorActorTool()
	{
		actorSelectTool = true;
		selectedActorPaletteIndex = -1;
		brush = '0';
	}

	void DrawTilePaletteSection()
	{
		PushStyleVar(ImGuiStyleVar.ItemSpacing, Vector2.One);
		PushStyleColor(ImGuiCol.Button, new Vector4(0, 0, 0, 1));
		var columns = PaletteColumnsForWidth(TileButtonSpacing);
		var index = 0;
		ValueStringBuilder vsb = new(stackalloc char[30]);
		foreach (var (id, entry) in Factory.Entries)
		{
			vsb.Clear();
			vsb.Append("Palette_Tiles_");
			vsb.Append(id);

			if (entry.Layer != activeTileLayer) continue;

			if (index > 0 && index % columns != 0)
				SameLine();

			var dualPreview = GetDualTilesetPreview(entry);

			if (ImGuiSubtextureButton(vsb.AsSpan(), entry.Image, entry.Name, dualPreview,
				selectedActorPaletteIndex < 0 && brush == id, new Vector2(ButtonSize, ButtonSize),
				imagePadding: 0, drawBorder: false))
			{
				brush = id;
				selectedActorPaletteIndex = -1;
				actorSelectTool = false;
			}
			if (IsItemHovered() && IsMouseDoubleClicked(ImGuiMouseButton.Left))
				OpenTileSourceEditor(entry);

			if (BeginPopupContextItem(vsb.AsSpan()))
			{
				if (entry.NeuralTexture != null)
				{
					if (Selectable("Open in NCT Tiles"))
						OpenTileSourceEditor(entry);
				}
				else if (FindTileKitSpec(entry) is not null)
				{
					if (Selectable("Open in Tile Kit Editor"))
						OpenTileSourceEditor(entry);
				}
				else if (Selectable("Open in Sprite Editor"))
					OpenTileSourceEditor(entry);
				EndPopup();
			}

			index++;
		}

		if (index > 0 && index % columns != 0)
			SameLine();
		if (ImGuiSubtextureButton("##AddTile", Assets.GetSubtexture(EditorButtonSubtexture_Plus), "Add a new tile",
			false, new Vector2(ButtonSize, ButtonSize), true, 4))
		{
			newTileLayer = activeTileLayer;
			addTilePopupRequested = true;
		}
		PopStyleColor();
		PopStyleVar();
		DrawAddTilePopup();
	}

	const string EditorButtonSubtexture_Plus = "buttons/18";

	void OpenTileSourceEditor(Factory.Entry entry)
	{
		if (entry.NeuralTexture is { } nct)
			OpenNctTileEditor(nct);
		else if (FindTileKitSpec(entry) is { } kit)
			OpenTileKitEditor(kit);
		else
			AsepriteLauncher.Open(entry);
	}

	void DrawTileLayerSelector()
	{
		var framePadding = GetStyle().FramePadding;
		PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(framePadding.X, 1));
		BeginGroup();
		var buttonWidth = TileLayerButtonWidth();
		if (DrawTileLayerVisibilityButton("Background", editorShowBackgroundLayer))
			editorShowBackgroundLayer = !editorShowBackgroundLayer;
		SameLine();
		if (DrawTileLayerButton("Background", Factory.TileLayer.Background, buttonWidth))
			SelectTileLayer(Factory.TileLayer.Background);

		if (DrawTileLayerVisibilityButton("Foreground", editorShowForegroundLayer))
			editorShowForegroundLayer = !editorShowForegroundLayer;
		SameLine();
		if (DrawTileLayerButton("Foreground", Factory.TileLayer.Foreground, buttonWidth))
			SelectTileLayer(Factory.TileLayer.Foreground);
		EndGroup();
		PopStyleVar();
	}

	static float TileLayerButtonWidth()
		=> CalcTextSize("Foreground").X + GetStyle().FramePadding.X * 2;

	static float TileLayerSelectorWidth()
	{
		var style = GetStyle();
		var iconWidth = MathF.Max(CalcTextSize(EditorIcons.Visible).X, CalcTextSize(EditorIcons.Hidden).X) + style.FramePadding.X * 2;
		return iconWidth + style.ItemSpacing.X + TileLayerButtonWidth();
	}

	static bool DrawTileLayerVisibilityButton(string id, bool visible)
	{
		PushID(id);
		PushStyleColor(ImGuiCol.Button, Vector4.Zero);
		PushStyleColor(ImGuiCol.ButtonHovered, Vector4.Zero);
		PushStyleColor(ImGuiCol.ButtonActive, Vector4.Zero);
		var pressed = Button(visible ? EditorIcons.Visible : EditorIcons.Hidden);
		PopStyleColor(3);
		if (IsItemHovered())
			SetTooltip(visible ? "Hide tile layer" : "Show tile layer");
		PopID();
		return pressed;
	}

	bool DrawTileLayerButton(string label, Factory.TileLayer layer, float width)
	{
		var selected = activeTileLayer == layer;
		if (selected)
			PushStyleColor(ImGuiCol.Button, GetStyle().Colors[(int)ImGuiCol.ButtonActive]);
		var pressed = Button(label, new Vector2(width, 0));
		if (selected)
			PopStyleColor();
		return pressed;
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

	void SelectTileBrush(Factory.TileLayer layer, char tile)
	{
		activeTileLayer = layer;
		brush = tile;
		selectedActorPaletteIndex = -1;
		actorSelectTool = false;
		editorSelectionRect = null;
	}

	/// Icons per row for the palette grids, from however wide the panel currently is.
	static int PaletteColumnsForWidth()
		=> PaletteColumnsForWidth(ButtonSpacing);

	static int PaletteColumnsForWidth(float spacing)
		=> Math.Max(1, (int)((GetContentRegionAvail().X + spacing) / (ButtonSize + spacing)));

	static Tileset? GetDualTilesetPreview(Factory.Entry entry)
		=> entry.DualTilesetName != null ? Assets.GetTileset(entry.DualTilesetName) : null;

	void DrawActorPaletteSection()
	{
		DrawActorPaletteSearch();
		DrawActorTagFilter();
		var columns = PaletteColumnsForWidth();
		var index = 0;
		var vsb = new ValueStringBuilder(stackalloc char[30]);
		for (var i = 0; i < Factory.ActorPalette.Count; i++)
		{
			var entry = Factory.ActorPalette[i];
			if (entry.Image.Texture == null)
				continue;
			if (actorPaletteFilter != ActorTag.None && (entry.Tags & actorPaletteFilter) == 0)
				continue;
			if (!ActorPaletteSearchMatches(entry))
				continue;

			if (index > 0 && index % columns != 0)
				SameLine();

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
			if (IsItemHovered() && IsMouseDoubleClicked(ImGuiMouseButton.Left))
				ActorZedLauncher.OpenActor(entry);
			if (BeginPopupContextItem(vsb.AsSpan()))
			{
				if (MenuItem("Find Instances..."))
					OpenActorInstanceSearch(entry);
				EndPopup();
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

		var visibleMaxX = GetCursorScreenPos().X + GetContentRegionAvail().X;
		PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(6, 3));
		PushStyleVar(ImGuiStyleVar.FrameRounding, 4f);

		DrawTagButton("All", actorPaletteFilter == ActorTag.None, () => actorPaletteFilter = ActorTag.None, visibleMaxX, firstInRow: true);
		foreach (ActorTag tag in Enum.GetValues<ActorTag>())
		{
			if (tag == ActorTag.None || (available & tag) == 0)
				continue;
			var t = tag;
			DrawTagButton(tag.ToString(), (actorPaletteFilter & t) != 0, () => ToggleActorTagFilter(t), visibleMaxX, firstInRow: false);
		}

		PopStyleVar(2);
	}

	void ToggleActorTagFilter(ActorTag tag)
	{
		var io = GetIO();
		if (io.KeyShift || io.KeyCtrl || io.KeySuper) // additive: add/remove this tag
			actorPaletteFilter ^= tag;
		else // exclusive: this tag only; click again to clear
			actorPaletteFilter = actorPaletteFilter == tag ? ActorTag.None : tag;
	}

	static void DrawTagButton(string label, bool active, Action onClick, float visibleMaxX, bool firstInRow)
	{
		var style = GetStyle();
		if (!firstInRow)
		{
			// Canonical ImGui button-wrap: stay on the row only while the next button fits.
			var width = CalcTextSize(label).X + style.FramePadding.X * 2;
			if (GetItemRectMax().X + style.ItemSpacing.X + width < visibleMaxX)
				SameLine();
		}

		if (active)
			PushStyleColor(ImGuiCol.Button, style.Colors[(int)ImGuiCol.ButtonActive]);

		var vsb = new ValueStringBuilder(stackalloc char[30]);
		vsb.Append(label);
		vsb.Append("##ActorTag");

		if (Button(vsb.AsSpan()))
			onClick();
		if (active)
			PopStyleColor();
	}

	void DrawRoomPaletteSection()
	{
		if (CurrentRoom is not { } room)
			return;

		var changed = false;
		TextDisabled(room.Sublayer == 0 ? "Main layer" : $"Sublayer {room.Sublayer}");
		SameLine();
		if (SmallButton("Add Sublayer"))
			AddSublayer(room);

		// These copy/paste only the room's *settings* (backdrop/ambient/etc.) onto the current room.
		// Whole-room copy/paste is the world-level Ctrl+C/V in room-edit mode.
		if (Button("Copy Settings"))
			copiedRoomSettings = CaptureRoomSettings(room);
		if (copiedRoomSettings is { } settings)
		{
			SameLine();
			if (Button("Paste Settings"))
			{
				using (CaptureRoomUndo("Paste Room Settings", room))
					ApplyRoomSettings(room, settings);
				changed = true;
			}
		}

		/*
		var controlWidth = BeginPropertyRow("Notes").ControlWidth;
		// Local copy so the undo scope captures the room *before* the first keystroke lands.
		var notes = room.Notes;
		if (InputTextMultiline("##RoomNotesEditor", ref notes, 3000, new(controlWidth, 80)))
		{
			OpenRoomSettingsUndo("Edit Room Notes", room);
			room.Notes = notes;
		}
		*/

		SeparatorText("Color Fields");
		var wasColorPainting = roomColorFieldPainter.IsActive(room);
		if (roomColorFieldPainter.DrawStartStop(room, activeTileLayer) && !wasColorPainting)
			EndMaskPainting();
		if (wasColorPainting && !roomColorFieldPainter.IsActive(room))
			CommitRoomColorFieldStroke(room);

		BeginPropertyRow("Backdrop");
		var backdrop = room.BackdropColor.ToVector3();
		if (ColorEdit3("##RoomBackdropColor", ref backdrop, ImGuiColorEditFlags.NoInputs))
		{
			OpenRoomSettingsUndo("Edit Room Backdrop", room);
			room.BackdropColor = new Color(Vector3.Clamp(backdrop, Vector3.Zero, Vector3.One));
			changed = true;
		}

		var gradient = room.BackdropColor2.HasValue;
		BeginPropertyRow("Gradient");
		if (Checkbox("##RoomBackdropGradient", ref gradient))
		{
			using (CaptureRoomUndo("Edit Room Backdrop", room))
				room.BackdropColor2 = gradient ? room.BackdropColor : null;
			changed = true;
		}

		if (room.BackdropColor2 is { } backdrop2Color)
		{
			BeginPropertyRow("Backdrop Bottom");
			var backdrop2 = backdrop2Color.ToVector3();
			if (ColorEdit3("##RoomBackdropColor2", ref backdrop2, ImGuiColorEditFlags.NoInputs))
			{
				OpenRoomSettingsUndo("Edit Room Backdrop", room);
				room.BackdropColor2 = new Color(Vector3.Clamp(backdrop2, Vector3.Zero, Vector3.One));
				changed = true;
			}
		}

		BeginPropertyRow("Surface");
		var backdropSurface = room.BackdropSurfaceId;
		if (DrawSurfacePicker(ref backdropSurface))
		{
			using (CaptureRoomUndo("Edit Room Backdrop", room))
				room.BackdropSurfaceId = backdropSurface;
			changed = true;
		}

		BeginPropertyRow("Ambient");
		var ambient = room.AmbientColor.ToVector3();
		if (ColorEdit3("##RoomAmbientColor", ref ambient, ImGuiColorEditFlags.NoInputs))
		{
			OpenRoomSettingsUndo("Edit Room Lighting", room);
			room.AmbientColor = new Color(Vector3.Clamp(ambient, Vector3.Zero, Vector3.One));
			changed = true;
		}

		BeginPropertyRow("Intensity");
		var ambientIntensity = room.AmbientIntensity;
		if (SliderFloat("##RoomAmbientIntensity", ref ambientIntensity, 0.0f, 2.0f))
		{
			OpenRoomSettingsUndo("Edit Room Lighting", room);
			room.AmbientIntensity = ambientIntensity;
			changed = true;
		}

		BeginPropertyRow("Noise");
		var lightNoise = room.LightNoise;
		if (SliderFloat("##RoomLightNoise", ref lightNoise, 0.0f, 1.0f))
		{
			OpenRoomSettingsUndo("Edit Room Lighting", room);
			room.LightNoise = lightNoise;
			changed = true;
		}

		var playerLightEnabled = room.PlayerLightEnabled;
		BeginPropertyRow("Player Light");
		if (Checkbox("##RoomPlayerLight", ref playerLightEnabled))
		{
			using (CaptureRoomUndo("Toggle Player Light", room))
				room.PlayerLightEnabled = playerLightEnabled;
			changed = true;
		}

		BeginPropertyRow("Spore Fog");
		var sporeFogStrength = room.SporeFogStrength;
		if (SliderFloat("##RoomSporeFogStrength", ref sporeFogStrength, 0.0f, 1.0f))
		{
			OpenRoomSettingsUndo("Edit Room Lighting", room);
			room.SporeFogStrength = sporeFogStrength;
			changed = true;
		}

		var reverbAuto = room.ReverbAuto;
		BeginPropertyRow("Auto Reverb");
		if (Checkbox("##RoomAutoReverb", ref reverbAuto))
		{
			using (CaptureRoomUndo("Edit Room Reverb", room))
			{
				room.ReverbAuto = reverbAuto;
				if (room.ReverbAuto)
					room.RebuildGeometry();
			}
			changed = true;
		}

		BeginPropertyRow("Reverb");
		var reverb = room.Reverb;
		if (SliderFloat("##RoomReverb", ref reverb, 0.0f, 1.0f))
		{
			OpenRoomSettingsUndo("Edit Room Reverb", room);
			room.ReverbAuto = false;
			room.Reverb = Math.Clamp(reverb, 0.0f, 1.0f);
			changed = true;
		}

		BeginPropertyRow("Music Muffle");
		var musicMuffle = room.MusicMuffle;
		if (SliderFloat("##RoomMusicMuffle", ref musicMuffle, 0.0f, 1.0f))
		{
			OpenRoomSettingsUndo("Edit Room Music Muffle", room);
			room.MusicMuffle = Math.Clamp(musicMuffle, 0.0f, 1.0f);
			changed = true;
		}

		BeginPropertyRow("Sun Strength");
		var sunStrength = room.SunStrength;
		if (SliderFloat("##RoomSunStrength", ref sunStrength, 0.0f, 2.0f))
		{
			OpenRoomSettingsUndo("Edit Room Sun", room);
			room.SunStrength = sunStrength;
			changed = true;
		}

		if (sunStrength > 0)
		{
			BeginPropertyRow("Sun Color");
			var sun = room.SunColor.ToVector3();
			if (ColorEdit3("##RoomSunColor", ref sun, ImGuiColorEditFlags.NoInputs))
			{
				OpenRoomSettingsUndo("Edit Room Sun", room);
				room.SunColor = new Color(Vector3.Clamp(sun, Vector3.Zero, Vector3.One));
				changed = true;
			}

			BeginPropertyRow("Sun Direction");
			var sunDirection = room.SunDirection;
			if (DragFloat2("##RoomSunDirection", ref sunDirection, 0.01f, -1.0f, 1.0f))
			{
				OpenRoomSettingsUndo("Edit Room Sun", room);
				room.SunDirection = Room.NormalizeOrDefault(sunDirection, room.SunDirection);
				changed = true;
			}
		}

		if (musicAreas.Count > 0)
		{
			BeginPropertyRow("Music");
			PushID("Music");
			// null = auto: inherit from the containing music region, or the default. Any
			// selected area (including the default) is an explicit per-room override.
			var autoLabel = $"Auto ({MusicRegions.ResolveRegion(room)?.Area ?? DefaultMusicArea})";
			if (BeginCombo("##RoomMusicArea", room.MusicArea ?? autoLabel))
			{
				if (Selectable(autoLabel, room.MusicArea == null))
				{
					using (CaptureRoomUndo("Edit Room Music", room))
						room.MusicArea = null;
					changed = true;
				}
				foreach (var musicArea in musicAreas)
				{
					var selected = room.MusicArea == musicArea;
					PushID(musicArea ?? "");
					if (Selectable(musicArea, selected))
					{
						using (CaptureRoomUndo("Edit Room Music", room))
							room.MusicArea = musicArea;
						changed = true;
					}
					if (selected)
						SetItemDefaultFocus();
					PopID();
				}
				EndCombo();
			}
			PopID();
		}

		SeparatorText("Dither");
		var ditherEnabled = room.LightingDitherEnabled;
		BeginPropertyRow("Enabled");
		if (Checkbox("##RoomDitherEnabled", ref ditherEnabled))
		{
			using (CaptureRoomUndo("Edit Room Dither", room))
				room.LightingDitherEnabled = ditherEnabled;
			changed = true;
		}

		BeginPropertyRow("Scale");
		var ditherScale = room.LightingDitherScale;
		if (SliderFloat("##RoomDitherScale", ref ditherScale, 1.0f, 8.0f))
		{
			OpenRoomSettingsUndo("Edit Room Dither", room);
			room.LightingDitherScale = ditherScale;
			changed = true;
		}

		BeginPropertyRow("Strength");
		var ditherStrength = room.LightingDitherStrength;
		if (SliderFloat("##RoomDitherStrength", ref ditherStrength, 0.0f, 1.0f))
		{
			OpenRoomSettingsUndo("Edit Room Dither", room);
			room.LightingDitherStrength = ditherStrength;
			changed = true;
		}

		BeginPropertyRow("Slope Boost");
		var ditherSlopeBoost = room.NormalDitherSlopeBoost;
		if (SliderFloat("##RoomDitherSlopeBoost", ref ditherSlopeBoost, 0.0f, 1.0f))
		{
			OpenRoomSettingsUndo("Edit Room Dither", room);
			room.NormalDitherSlopeBoost = ditherSlopeBoost;
			changed = true;
		}

		var ditherQuantization = room.NormalDitherQuantization;
		BeginPropertyRow("Quantization");
		if (Checkbox("##RoomDitherQuantization", ref ditherQuantization))
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
		SeparatorText("Ambient Occlusion");

		BeginPropertyRow("Enabled");
		editorGameDirty |= Checkbox("##AmbientOcclusionEnabled", ref PerfToggles.AmbientOcclusion);

		BeginPropertyRow("Strength");
		editorGameDirty |= SliderFloat("##AmbientOcclusionStrength", ref AmbientOcclusionStrength, 0.0f, 4.0f);

		BeginPropertyRow("Blur");
		editorGameDirty |= SliderFloat("##AmbientOcclusionBlur", ref AmbientOcclusionBlur, 0.0f, 32.0f);

		BeginPropertyRow("Offset (px)");
		editorGameDirty |= SliderFloat2("##AmbientOcclusionOffset", ref AmbientOcclusionOffset, -16.0f, 16.0f);

		BeginPropertyRow("Min Visibility");
		editorGameDirty |= SliderFloat("##AmbientOcclusionMinVisibility", ref AmbientOcclusionMinVisibility, 0.0f, 1.0f);
	}

	void DrawSoftShadowSection()
	{
		SeparatorText("Soft Shadows");
		BeginPropertyRow("Enabled");
		editorGameDirty |= Checkbox("##SoftShadowsEnabled", ref PerfToggles.SoftShadows);
	}

#if false
	void DrawPreviewPaletteSection()
	{
		if (!CollapsingHeader("Previews"))
			return;

		TextUnformatted("Resolution");
		SetNextItemWidth(-1);
		var scale = roomPreviewResolutionScale;
		if (SliderFloat("##RoomPreviewResolutionScale", ref scale, 0.05f, 1.0f))
		{
			roomPreviewResolutionScale = scale;
			ClearRoomPreviewCache();
		}
	}
#endif

	static RoomSettings CaptureRoomSettings(Room room)
		=> new(room.MusicArea, room.BackdropColor, room.BackdropColor2, room.BackdropSurfaceId, room.AmbientColor, room.AmbientIntensity, room.LightNoise, room.PlayerLightEnabled, room.SporeFogStrength, room.ReverbAuto, room.Reverb, room.SunColor, room.SunStrength, room.SunDirection, room.LightingDitherEnabled, room.LightingDitherScale, room.LightingDitherStrength, room.NormalDitherSlopeBoost, room.NormalDitherQuantization);

	static void ApplyRoomSettings(Room room, RoomSettings settings)
	{
		room.MusicArea = settings.MusicArea;
		room.BackdropColor = settings.BackdropColor;
		room.BackdropColor2 = settings.BackdropColor2;
		room.BackdropSurfaceId = settings.BackdropSurfaceId;
		room.AmbientColor = settings.AmbientColor;
		room.AmbientIntensity = settings.AmbientIntensity;
		room.LightNoise = settings.LightNoise;
		room.PlayerLightEnabled = settings.PlayerLightEnabled;
		room.SporeFogStrength = settings.SporeFogStrength;
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

	static RectInt RoomActorBounds(Room room, JsonObject actor)
	{
		var local = ActorSpecs.ReadPoint(actor, "Position") ?? Point2.Zero;
		var position = room.WorldBounds.TopLeft + local;
		var type = ActorSpecs.GetTypeName(actor) ?? "";
		if (string.Equals(type, nameof(TitleText), StringComparison.OrdinalIgnoreCase))
			return TitleText.TextBounds(position, ActorSpecs.ReadResolvedString(actor, nameof(TitleText.Text)),
				ActorSpecs.ReadResolvedFloat(actor, nameof(TitleText.LineHeightMultiplier), 1));
		if (ResizableRoomRectTypes.TryGetValue(type, out var info))
		{
			var size = ActorSpecs.ReadResolvedPoint(actor, nameof(IResizable.Size)) ?? info.DefaultSize;
			return new RectInt(position.X, position.Y, size.X, size.Y);
		}
		if (string.Equals(type, nameof(Prop), StringComparison.OrdinalIgnoreCase) &&
			Assets.GetSprite(ActorSpecs.ReadResolvedString(actor, nameof(Prop.SpriteName)) ?? "") is { } sprite)
			return Factory.ActorPaletteEntry.GetDefaultEditorBounds(sprite, sprite.ResolveOrigin(SpriteAnchor.TopLeft)) + position;

		if (FindRoomActorEntry(actor) is { } entry)
			return (entry.EditorBounds ?? entry.DefaultEditorBounds) + position;

		return new RectInt(position.X - 4, position.Y - 4, 8, 8);
	}

	/// Moves an actor so its whole footprint sits inside the room. The clamp is applied to the
	/// actor's bounds rather than its Position, since the two differ by the sprite/palette origin;
	/// an actor larger than the room pins to the top-left corner.
	static void ClampRoomActorToRoom(Room room, JsonObject actor)
	{
		var local = ActorSpecs.ReadPoint(actor, "Position") ?? Point2.Zero;
		var bounds = RoomActorBounds(room, actor);
		var boundsLocal = bounds.TopLeft - room.WorldBounds.TopLeft;
		var clamped = new Point2(
			Math.Clamp(boundsLocal.X, 0, Math.Max(0, room.WorldBounds.Width - bounds.Width)),
			Math.Clamp(boundsLocal.Y, 0, Math.Max(0, room.WorldBounds.Height - bounds.Height)));
		if (clamped == boundsLocal)
			return;

		actor["Position"] = new JsonArray(local.X + clamped.X - boundsLocal.X, local.Y + clamped.Y - boundsLocal.Y);
	}

	/// Puts the actor's bounds under the middle of the editor view (which is editorCamera in
	/// world space), so a paste lands where you are looking instead of where it was copied from.
	void CenterRoomActorOnEditorCamera(Room room, JsonObject actor)
	{
		var local = ActorSpecs.ReadPoint(actor, "Position") ?? Point2.Zero;
		var bounds = RoomActorBounds(room, actor);
		var target = (editorCamera - (Vector2)bounds.Size / 2).RoundToPoint2();
		actor["Position"] = new JsonArray(
			local.X + target.X - bounds.X,
			local.Y + target.Y - bounds.Y);
	}

	static bool IsHiddenWallActor(JsonObject actor)
		=> ActorSpecs.GetTypeName(actor) == "HiddenWall";

	static bool IsRevealDoorActor(JsonObject actor)
		=> ActorSpecs.GetTypeName(actor) == nameof(RevealDoor);

	static bool IsPropActor(JsonObject actor)
		=> string.Equals(ActorSpecs.GetTypeName(actor), nameof(Prop), StringComparison.OrdinalIgnoreCase);

	static bool IsNeuralTextureActor(JsonObject actor)
		=> string.Equals(ActorSpecs.GetTypeName(actor), nameof(NeuralTextureActor), StringComparison.OrdinalIgnoreCase);

	static bool IsLinkedDoorActor(JsonObject actor)
		=> ActorSpecs.GetTypeName(actor) == nameof(LinkedDoor);

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

	// Registered actor type names assignable to kind, for capability checks and [ActorRef]
	// candidate filtering. Registrations are fixed once the Factory has run, so cache them.
	static readonly Dictionary<Type, HashSet<string>> actorTypesAssignableTo = [];
	static HashSet<string> ActorTypesAssignableTo(Type kind)
	{
		if (actorTypesAssignableTo.TryGetValue(kind, out var names))
			return names;

		names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (var (name, type) in ActorSpecs.ActorTypes)
			if (kind.IsAssignableFrom(type))
				names.Add(name);
		actorTypesAssignableTo[kind] = names;
		return names;
	}

	static bool IsPointPathActor(JsonObject actor)
		=> ActorTypesAssignableTo(typeof(IPointPath)).Contains(ActorSpecs.GetTypeName(actor) ?? "");

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

	static BurrowLine.BurrowDoorPoints BurrowDoorFlags(JsonObject actor)
		=> Enum.TryParse<BurrowLine.BurrowDoorPoints>(
			ActorSpecs.ReadResolvedString(actor, nameof(BurrowLine.BurrowDoors)), true, out var flags) ? flags : 0;

	static bool IsBurrowDoorPoint(JsonObject actor, int pointIndex)
	{
		if (!string.Equals(ActorSpecs.GetTypeName(actor), nameof(BurrowLine), StringComparison.OrdinalIgnoreCase))
			return false;
		var count = PointPathPointCount(PointPathNodes(actor));
		var flag = pointIndex == 0 ? BurrowLine.BurrowDoorPoints.First
			: pointIndex == count - 1 ? BurrowLine.BurrowDoorPoints.Last
			: BurrowLine.BurrowDoorPoints.None;
		return flag != 0 && BurrowDoorFlags(actor).HasFlag(flag);
	}

	static bool HasBurrowDoor(JsonObject actor)
		=> string.Equals(ActorSpecs.GetTypeName(actor), nameof(BurrowLine), StringComparison.OrdinalIgnoreCase) &&
			BurrowDoorFlags(actor) != 0;

	static void SetBurrowDoorPoint(JsonObject actor, int pointIndex, bool enabled)
	{
		var count = PointPathPointCount(PointPathNodes(actor));
		var flag = pointIndex == 0 ? BurrowLine.BurrowDoorPoints.First
			: pointIndex == count - 1 ? BurrowLine.BurrowDoorPoints.Last
			: BurrowLine.BurrowDoorPoints.None;
		if (flag == 0)
			return;
		var flags = BurrowDoorFlags(actor);
		flags = enabled ? flags | flag : flags & ~flag;
		ActorSpecs.SetProperty(actor, nameof(BurrowLine.BurrowDoors), JsonValue.Create(flags.ToString()));
	}

	readonly record struct EditorBurrowDoor(Room Room, JsonObject Actor, int ActorIndex, int PointIndex, Point2 Position);

	int FindBurrowDoorMatches(Room sourceRoom, Point2 worldPoint, out EditorBurrowDoor match)
	{
		match = default;
		var count = 0;
		foreach (var room in Assets.RoomsById.Values)
		{
			if (room.Sublayer == sourceRoom.Sublayer)
				continue;
			for (var actorIndex = 0; actorIndex < room.Actors.Count; actorIndex++)
			{
				var actor = room.Actors[actorIndex];
				if (!string.Equals(ActorSpecs.GetTypeName(actor), nameof(BurrowLine), StringComparison.OrdinalIgnoreCase))
					continue;
				var points = PointPathNodes(actor);
				var pointCount = PointPathPointCount(points);
				var origin = PointPathOrigin(room, actor);
				if (IsBurrowDoorPoint(actor, 0) && (Point2)(origin + PointPathPoint(points, 0)) == worldPoint)
				{
					match = new(room, actor, actorIndex, 0, worldPoint);
					count++;
				}
				if (IsBurrowDoorPoint(actor, pointCount - 1) &&
					(Point2)(origin + PointPathPoint(points, pointCount - 1)) == worldPoint)
				{
					match = new(room, actor, actorIndex, pointCount - 1, worldPoint);
					count++;
				}
			}
		}
		return count;
	}

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

			var sizeField = type.GetField("DefaultSize", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
				?? throw new InvalidOperationException($"Resizable actor '{type.Name}' is missing a public static \"DefaultSize\" field.");
			var axesField = type.GetField(nameof(Jumpthru.ResizableAxes), BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
			var axes = axesField != null ? (ResizeAxes)axesField.GetValue(null)! : ResizeAxes.Both;
			result[name] = new ResizableInfo((Point2)sizeField.GetValue(null)!, axes);
		}
		return result;
	}

	static bool IsResizableRoomRectActor(JsonObject actor)
		=> ResizableRoomRectTypes.ContainsKey(ActorSpecs.GetTypeName(actor) ?? "");

	static ResizableInfo ResizableRoomRectInfo(JsonObject actor)
		=> ResizableRoomRectTypes.GetValueOrDefault(
			ActorSpecs.GetTypeName(actor) ?? "", new ResizableInfo(new(Game.TileSize * 2, Game.TileSize * 2), ResizeAxes.Both));

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
			var selectedPoint = SelectedPathPoint(room);
			for (var i = 0; i < count; i++)
			{
				var point = origin + PointPathPoint(points, i);
				if (typeName == nameof(BurrowLine) && BurrowLinePointConnected(room, actorIndex, i, point))
					batcher.CircleLine(point, radius * 1.65f, lineWeight * 2, 16, color);
				if (IsBurrowDoorPoint(actor, i))
				{
					var matches = FindBurrowDoorMatches(room, (Point2)point, out _);
					batcher.CircleLine(point, radius * 2.2f, lineWeight * 3, 16,
						matches == 1 ? new Color(0xb77cff) : Color.Red);
				}
				// The selected point wears the fill and ring the other way round: Backspace means
				// "delete this point" while one is selected, so which one that is has to be obvious.
				var pointSelected = i == selectedPoint;
				batcher.Circle(point, radius, 12, pointSelected ? Color.White : color);
				batcher.CircleLine(point, radius, lineWeight, 12, pointSelected ? color : Color.White);
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

	int SelectedPathPoint(Room room)
	{
		if (selectedPathPointIndex < 0 || selectedPathPointActorIndex != selectedRoomActorIndex ||
			selectedRoomActorIndex < 0 || selectedRoomActorIndex >= room.Actors.Count ||
			!IsPointPathActor(room.Actors[selectedRoomActorIndex]))
			return -1;

		var count = PointPathPointCount(PointPathNodes(room.Actors[selectedRoomActorIndex]));
		return selectedPathPointIndex < count ? selectedPathPointIndex : -1;
	}

	void SelectPathPoint(int pointIndex)
	{
		selectedPathPointActorIndex = selectedRoomActorIndex;
		selectedPathPointIndex = pointIndex;
	}

	void ClearSelectedPathPoint() => selectedPathPointIndex = -1;

	// Nearest path segment under the cursor: its index (a new point goes right after it) plus where
	// on the line the cursor landed, so inserting there leaves the path's shape untouched.
	int HitTestPointPathSegment(Room room, int actorIndex, Vector2 world, float lineWeight, out Vector2 onLine)
	{
		onLine = world;
		if (actorIndex < 0 || actorIndex >= room.Actors.Count || !IsPointPathActor(room.Actors[actorIndex]))
			return -1;

		var actor = room.Actors[actorIndex];
		var nodes = PointPathNodes(actor);
		var count = PointPathPointCount(nodes);
		var origin = PointPathOrigin(room, actor);
		var tolerance = PointPathHandleRadius(lineWeight) * 1.5f;
		var best = -1;
		var bestDistance = tolerance * tolerance;
		for (var i = 1; i < count; i++)
		{
			var line = new Line(origin + PointPathPoint(nodes, i - 1), origin + PointPathPoint(nodes, i));
			var distance = line.DistanceSquared(world);
			if (distance > bestDistance)
				continue;

			bestDistance = distance;
			best = i - 1;
			onLine = line.ClosestPoint(world);
		}
		return best;
	}

	// Double-clicking the selected path adds a point: on a segment it lands where you clicked (so
	// the path keeps its shape), and on an end handle the path grows one more segment continuing the
	// direction it already ends in. An interior handle has no free end to grow from, so it does
	// nothing and the click stays a plain selection.
	bool TryInsertPointPathPoint(Room room, int actorIndex, Vector2 world, float lineWeight)
	{
		if (actorIndex < 0 || actorIndex >= room.Actors.Count || !IsPointPathActor(room.Actors[actorIndex]))
			return false;

		var actor = room.Actors[actorIndex];
		var points = PointPathPoints(actor);
		if (points.Length < 2)
			return false;

		var handle = HitTestPointPathHandle(room, actorIndex, world, lineWeight);
		if (handle >= 0 && IsBurrowDoorPoint(actor, handle))
		{
			DisplayMessage("Unmark the burrow door before extending this endpoint");
			return true;
		}
		int insertAt;
		Point2 local;
		if (handle == 0 || handle == points.Length - 1)
		{
			var neighbor = handle == 0 ? 1 : points.Length - 2;
			var delta = points[handle] - points[neighbor];
			if (delta == Point2.Zero)
				delta = new Point2(Game.TileSize, 0);
			local = points[handle] + delta;
			insertAt = handle == 0 ? 0 : points.Length;
		}
		else if (handle >= 0)
			return false;
		else
		{
			var segment = HitTestPointPathSegment(room, actorIndex, world, lineWeight, out var onLine);
			if (segment < 0)
				return false;

			insertAt = segment + 1;
			local = (onLine - PointPathOrigin(room, actor)).RoundToPoint2();
		}

		using (CaptureRoomUndo("Insert Path Point", room))
		{
			var next = new Point2[points.Length + 1];
			Array.Copy(points, next, insertAt);
			next[insertAt] = local;
			Array.Copy(points, insertAt, next, insertAt + 1, points.Length - insertAt);
			ActorSpecs.SetProperty(actor, nameof(IPointPath.Points), ActorSpecs.ToJsonNode(next));
			SelectPathPoint(insertAt);
			editorGameDirty = true;
		}
		return true;
	}

	// Two points is the floor: every segment lookup is Points[i] -> Points[i + 1], so dropping below
	// that leaves a path that is no longer a line rather than a shorter one.
	void DeleteSelectedPathPoint(Room room, int pointIndex)
	{
		var actor = room.Actors[selectedRoomActorIndex];
		var points = PointPathPoints(actor);
		if (IsBurrowDoorPoint(actor, pointIndex))
		{
			DisplayMessage("Unmark the burrow door before deleting this endpoint");
			return;
		}
		if (points.Length <= 2)
		{
			DisplayMessage("Path needs at least two points");
			return;
		}

		using (CaptureRoomUndo("Delete Path Point", room))
		{
			var next = new Point2[points.Length - 1];
			Array.Copy(points, next, pointIndex);
			Array.Copy(points, pointIndex + 1, next, pointIndex, next.Length - pointIndex);
			ActorSpecs.SetProperty(actor, nameof(IPointPath.Points), ActorSpecs.ToJsonNode(next));
			SelectPathPoint(Math.Clamp(pointIndex - 1, 0, next.Length - 1));
			editorGameDirty = true;
		}
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
		var direction = Enum.TryParse<RevealDoor.RevealDirection>(ActorSpecs.ReadResolvedString(actor, nameof(RevealDoor.Direction)), true, out var parsed)
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

	void MoveRoomActorTo(Room room, int actorIndex, Vector2 world, Vector2 pressWorld, bool snap)
	{
		if (actorIndex < 0 || actorIndex >= room.Actors.Count)
			return;

		var actor = room.Actors[actorIndex];
		if (HasBurrowDoor(actor))
			return;
		var offset = GetRoomActorPaletteOffset(actor);
		EditorFor(actor)?.BeforeMove(new EditorActor(room, actor));

		var targetLocal = (Vector2)editorDrag.ActorStartPosition + world - pressWorld;
		var local = snap
			? SnapRoomActorLocalPosition(targetLocal, offset)
			: (Point2)targetLocal;
		actor["Position"] = new JsonArray(local.X, local.Y);
		if (game is { IsEditorPreview: true } && ActorSpecs.GetActorId(actor) is { } actorId && game.GetActorById(actorId) is { } live)
			live.Position = room.WorldBounds.TopLeft + local;
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
		if (IsBurrowDoorPoint(actor, pointIndex))
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

	bool RoomActorDragShouldSnap()
	{
		var shiftDown = Input.Keyboard.Shift;
		return editorDrag.ActorSnapsByDefault ? !shiftDown : shiftDown;
	}

	EditorDragKind DragKind => editorDrag.Kind;
	bool TilePlacing => DragKind == EditorDragKind.TilePaint && tilePaintStroke.Active;

	void ClearEditorDragState()
	{
		if (DragKind == EditorDragKind.TilePaint)
			tilePaintStroke.Cancel();
		editorDrag = EditorDragState.None;
	}

	void CancelTilePaint()
	{
		if (DragKind == EditorDragKind.TilePaint)
			ClearEditorDragState();
		else
			tilePaintStroke.Cancel();
	}

	void CommitEditorDragState()
	{
		var resizedRoom = DragKind == EditorDragKind.RoomGeometry;
		ClearEditorDragState();
		undo.CommitPending(ActorMoveUndoSlot);
		undo.CommitPending(RoomGeometryUndoSlot);
		if (resizedRoom)
			CheckRoomResizeDiscardedContent();
	}

	void CaptureRoomResizeContents(Room room)
	{
		roomResizeRoomId = room.Id;
		roomResizeOrigin = room.TileSpacePosition;
		roomResizeSize = room.Size;
		roomResizeForegroundTiles.Clear();
		roomResizeBackgroundTiles.Clear();
		roomResizeActorTiles.Clear();

		for (var x = 0; x < room.Size.X; x++)
			for (var y = 0; y < room.Size.Y; y++)
			{
				if (room.Tiles[x, y] != '0')
					roomResizeForegroundTiles.Add(new Point2(x, y));
				if (room.BackgroundTiles[x, y] != '0')
					roomResizeBackgroundTiles.Add(new Point2(x, y));
			}

		// Actors already outside the room aren't this resize's doing, so they don't get counted.
		var bounds = new RectInt(Point2.Zero, room.Size);
		foreach (var actor in room.Actors)
		{
			var tile = RoomActorTile(actor);
			if (bounds.Contains(tile))
				roomResizeActorTiles.Add(tile);
		}
	}

	static Point2 RoomActorTile(JsonObject actor)
	{
		var position = ActorSpecs.ReadPoint(actor, "Position") ?? Point2.Zero;
		return new Point2(
			(int)MathF.Floor(position.X / (float)Game.TileSize),
			(int)MathF.Floor(position.Y / (float)Game.TileSize));
	}

	// A shrink drops the tiles it reclaimed and leaves any actors there stranded outside the room,
	// so measure what the drag started with against where it ended and ask before it stands. The
	// gesture's undo entry was just committed, which is what Cancel rewinds.
	void CheckRoomResizeDiscardedContent()
	{
		if (roomResizeRoomId is not { } roomId || Assets.GetRoom(roomId) is not { } room)
			return;

		roomResizeRoomId = null;
		if (room.Size == roomResizeSize)
			return; // the room only moved, and its contents moved with it

		// Contents keep their world position across a resize, so their local coordinates shift by
		// however far the room's top-left travelled.
		var shift = roomResizeOrigin - room.TileSpacePosition;
		var bounds = new RectInt(Point2.Zero, room.Size);
		roomResizeDiscardedForeground = CountTilesOutside(roomResizeForegroundTiles, shift, bounds);
		roomResizeDiscardedBackground = CountTilesOutside(roomResizeBackgroundTiles, shift, bounds);
		roomResizeDiscardedActors = CountTilesOutside(roomResizeActorTiles, shift, bounds);
		if (roomResizeDiscardedForeground + roomResizeDiscardedBackground + roomResizeDiscardedActors > 0)
			roomResizePopupRequested = true;
	}

	static int CountTilesOutside(List<Point2> tiles, Point2 shift, RectInt bounds)
	{
		var count = 0;
		foreach (var tile in tiles)
			if (!bounds.Contains(tile + shift))
				count++;
		return count;
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

		ResizeRoomActor(room, room.Actors[actorIndex], handle, world, tile, snap);
	}

	void ResizeRoomActor(Room room, JsonObject actor, ResizeHandle handle, Vector2 world, Point2 tile, bool snap)
	{
		if (!IsResizableRoomRectActor(actor))
			return;

		var position = ActorSpecs.ReadPoint(actor, "Position") ?? Point2.Zero;
		var size = ActorSpecs.ReadResolvedPoint(actor, nameof(IResizable.Size)) ?? ResizableRoomRectDefaultSize(actor);
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

		// Clamp and enforce the one-tile minimum only on the axis the handle resizes: the other
		// axis must pass through untouched, or a sub-tile dimension (e.g. a 4px-tall conveyor
		// belt) gets silently inflated to a tile by a drag along the other axis.
		var roomWidth = room.Size.X * Game.TileSize;
		var roomHeight = room.Size.Y * Game.TileSize;

		if (handle is not (ResizeHandle.Top or ResizeHandle.Bottom))
		{
			left = Math.Clamp(left, 0, roomWidth - Game.TileSize);
			right = Math.Clamp(right, Game.TileSize, roomWidth);

			if (right - left < Game.TileSize)
			{
				if (handle is ResizeHandle.Left or ResizeHandle.TopLeft or ResizeHandle.BottomLeft)
					left = right - Game.TileSize;
				else
					right = left + Game.TileSize;
			}

			left = Math.Clamp(left, 0, roomWidth - Game.TileSize);
			right = Math.Clamp(right, left + Game.TileSize, roomWidth);
		}

		if (handle is not (ResizeHandle.Left or ResizeHandle.Right))
		{
			top = Math.Clamp(top, 0, roomHeight - Game.TileSize);
			bottom = Math.Clamp(bottom, Game.TileSize, roomHeight);

			if (bottom - top < Game.TileSize)
			{
				if (handle is ResizeHandle.Top or ResizeHandle.TopLeft or ResizeHandle.TopRight)
					top = bottom - Game.TileSize;
				else
					bottom = top + Game.TileSize;
			}

			top = Math.Clamp(top, 0, roomHeight - Game.TileSize);
			bottom = Math.Clamp(bottom, top + Game.TileSize, roomHeight);
		}

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

	static Room? RoomById(string id) => Assets.RoomsById.GetValueOrDefault(id);

	// ---- Multi-room selection (room-select / zoomed-out mode) ------------------------------------

	// Makes `room` the primary (inspector + editor game focus). Same save/select/sync dance the
	// old single-click room switch used; does not touch the multi-selection set.
	void SelectPrimaryRoom(Room room)
	{
		if (room.Id == selectedRoomId)
			return;
		using (CaptureSelectionUndo("Select Room"))
		{
			if (CurrentRoom is { } previous)
				editorRoomEntryDirection = GetEditorEntryDirection(previous, room);
			CurrentRoom?.Save();
			SelectRoom(room);
			selectedRoomActorIndex = -1;
			SyncEditorGame();
		}
	}

	void SetRoomSelection(Room room)
	{
		selectedRoomIds.Clear();
		selectedRoomIds.Add(room.Id);
		SelectPrimaryRoom(room);
	}

	void ToggleRoomSelection(Room room)
	{
		if (selectedRoomIds.Add(room.Id))
			SelectPrimaryRoom(room);
		else
		{
			selectedRoomIds.Remove(room.Id);
			// Removing the primary repoints it at any remaining selected room.
			if (room.Id == selectedRoomId)
				foreach (var id in selectedRoomIds)
					if (RoomById(id) is { } remaining)
					{
						SelectPrimaryRoom(remaining);
						break;
					}
		}
	}

	// ---- Room-select mode press dispatch ---------------------------------------------------------

	// One entry point for a left-press while zoomed out: resize handle -> resize the primary; over a
	// room -> select (Shift toggles) and arm a group move; empty space -> marquee (Shift adds).
	void BeginRoomSelectPress(Room primary, Vector2 worldMouse, ResizeHandle handle)
	{
		var shift = Input.Keyboard.Shift;
		ClearEditorDragState();
		CancelTilePaint();
		roomPressCursor = Cursor;

		if (handle != ResizeHandle.None)
		{
			editorDrag.Kind = EditorDragKind.RoomGeometry;
			OpenPendingRoomUndo(RoomGeometryUndoSlot, RoomResizeUndoName, primary);
			CaptureRoomResizeContents(primary);
			editorDrag.RoomGeometryHandle = handle;
			editorDrag.RoomGeometryOffset = WorldToRoomGeometryTile(worldMouse) - primary.TileSpacePosition;
			return;
		}

		var room = GetRoomAtWorld(worldMouse);
		if (room == null)
		{
			if (!shift)
				selectedRoomIds.Clear();
			editorDrag.Kind = EditorDragKind.RoomMarquee;
			roomMarqueeFromWorld = worldMouse;
			roomMarqueeToWorld = worldMouse;
			return;
		}

		if (shift)
		{
			ToggleRoomSelection(room);
			return;
		}

		if (!selectedRoomIds.Contains(room.Id))
			SetRoomSelection(room);
		else if (room.Id != selectedRoomId)
			SelectPrimaryRoom(room);

		BeginRoomGroupMove(worldMouse);
	}

	void BeginRoomGroupMove(Vector2 worldMouse)
	{
		editorDrag.Kind = EditorDragKind.RoomMove;
		roomDragStarted = false;
		roomMoveAnchorTile = WorldToRoomGeometryTile(worldMouse);
		roomMoveOrigins.Clear();
		foreach (var id in selectedRoomIds)
			if (RoomById(id) is { } r)
				roomMoveOrigins[id] = r.TileSpacePosition;
	}

	void OpenPendingRoomGroupUndo(string name)
	{
		if (undo.HasPending(RoomGeometryUndoSlot))
			return;
		var targets = new List<IJsonUndoTarget>();
		foreach (var id in selectedRoomIds)
			if (RoomById(id) is { } r)
				targets.Add(CurrentRoomUndoTarget(r));
		targets.Add(EditorSelectionUndoTarget());
		undo.OpenPending(RoomGeometryUndoSlot, name, targets.ToArray());
	}

	void UpdateRoomGroupMove(Vector2 worldMouse)
	{
		if (!roomDragStarted)
		{
			if (Vector2.DistanceSquared(Cursor, roomPressCursor) <= RoomDragDeadzone * RoomDragDeadzone)
				return;
			roomDragStarted = true;
			OpenPendingRoomGroupUndo("Move Rooms");
		}

		var desired = WorldToRoomGeometryTile(worldMouse) - roomMoveAnchorTile;
		if (!Input.Keyboard.Alt)
			desired = SnapGroupDelta(desired);
		ApplyGroupDelta(ClampGroupDelta(desired));
	}

	void FinishRoomGroupMove()
	{
		if (!roomDragStarted)
			return;
		var moved = 0;
		foreach (var (id, origin) in roomMoveOrigins)
			if (RoomById(id) is { } r && r.TileSpacePosition != origin)
			{
				r.Save();
				moved++;
			}
		if (moved > 0)
		{
			editorGameDirty = true;
			SyncEditorGame();
			DisplayMessage($"{moved} room{(moved == 1 ? "" : "s")} moved");
		}
	}

	void FinishRoomMarquee()
	{
		var rect = RectFromCorners(roomMarqueeFromWorld, roomMarqueeToWorld);
		Room? firstHit = null;
		foreach (var room in Assets.RoomsById.Values)
		{
			if (room.Sublayer != editorSublayer)
				continue;
			var b = room.WorldBounds;
			if (rect.GetIntersection(new Rect(b.X, b.Y, b.Width, b.Height)).Area > 0)
			{
				selectedRoomIds.Add(room.Id);
				firstHit ??= room;
			}
		}
		if (firstHit != null && (selectedRoomId == null || !selectedRoomIds.Contains(selectedRoomId)))
			SelectPrimaryRoom(firstHit);
	}

	static Rect RectFromCorners(Vector2 a, Vector2 b)
		=> new(MathF.Min(a.X, b.X), MathF.Min(a.Y, b.Y), MathF.Abs(a.X - b.X), MathF.Abs(a.Y - b.Y));

	// ---- Group snap + collision ------------------------------------------------------------------

	RectInt GroupOriginBounds()
	{
		var min = new Point2(int.MaxValue, int.MaxValue);
		var max = new Point2(int.MinValue, int.MinValue);
		foreach (var (id, origin) in roomMoveOrigins)
			if (RoomById(id) is { } r)
			{
				min = Point2.Min(min, origin);
				max = Point2.Max(max, origin + r.Size);
			}
		return max.X >= min.X ? new RectInt(min, max - min) : default;
	}

	// Collects the X (left/right) and Y (top/bottom) tile edges of every room not in the selection,
	// so the drag can snap the moving group flush against neighbors as well as the screen lattice.
	(List<int> X, List<int> Y) NonSelectedRoomEdges()
	{
		var xs = new List<int>();
		var ys = new List<int>();
		foreach (var room in Assets.RoomsById.Values)
		{
			if (room.Sublayer != editorSublayer || selectedRoomIds.Contains(room.Id))
				continue;
			var p = room.TileSpacePosition;
			xs.Add(p.X);
			xs.Add(p.X + room.Size.X);
			ys.Add(p.Y);
			ys.Add(p.Y + room.Size.Y);
		}
		return (xs, ys);
	}

	Point2 SnapGroupDelta(Point2 desired)
	{
		var bounds = GroupOriginBounds();
		if (bounds.Width <= 0)
			return desired;
		var lattice = RoomLatticeTiles;
		var threshold = Math.Max(1, (int)MathF.Round(RoomSnapScreenPx / (editorZoom * Game.TileSize)));
		var (edgesX, edgesY) = NonSelectedRoomEdges();
		return new(
			desired.X + SnapAxis(bounds.Left, bounds.Right, desired.X, lattice.X, edgesX, threshold),
			desired.Y + SnapAxis(bounds.Top, bounds.Bottom, desired.Y, lattice.Y, edgesY, threshold));
	}

	// Smallest nudge that lands the group's near or far edge on a snap line (nearest lattice
	// multiple, or a neighbor edge) within `threshold` tiles; 0 if nothing is close enough.
	static int SnapAxis(int lo, int hi, int desired, int latticeStep, List<int> roomEdges, int threshold)
	{
		var movedLo = lo + desired;
		var movedHi = hi + desired;
		var best = 0;
		var bestDist = threshold + 1;
		Consider(movedLo, (int)MathF.Round((float)movedLo / latticeStep) * latticeStep);
		Consider(movedHi, (int)MathF.Round((float)movedHi / latticeStep) * latticeStep);
		foreach (var edge in roomEdges)
		{
			Consider(movedLo, edge);
			Consider(movedHi, edge);
		}
		return best;

		void Consider(int edge, int line)
		{
			var dist = Math.Abs(line - edge);
			if (dist < bestDist)
			{
				bestDist = dist;
				best = line - edge;
			}
		}
	}

	// Furthest collision-free delta toward `desired`, resolved per axis so the group slides along a
	// blocking neighbor instead of stopping dead.
	Point2 ClampGroupDelta(Point2 desired)
	{
		var dx = ClampAxisMarch(Point2.Zero, desired.X, horizontal: true);
		var dy = ClampAxisMarch(new Point2(dx, 0), desired.Y, horizontal: false);
		return new(dx, dy);
	}

	int ClampAxisMarch(Point2 baseDelta, int target, bool horizontal)
	{
		if (target == 0)
			return 0;
		var step = Math.Sign(target);
		var last = 0;
		for (var v = step; Math.Abs(v) <= Math.Abs(target); v += step)
		{
			var delta = horizontal ? new Point2(v, baseDelta.Y) : new Point2(baseDelta.X, v);
			if (GroupCollides(delta))
				break;
			last = v;
		}
		return last;
	}

	bool GroupCollides(Point2 delta)
	{
		foreach (var (id, origin) in roomMoveOrigins)
		{
			if (RoomById(id) is not { } r)
				continue;
			var rect = new RectInt(origin + delta, r.Size);
			foreach (var room in Assets.RoomsById.Values)
			{
				if (room.Sublayer != editorSublayer || selectedRoomIds.Contains(room.Id))
					continue;
				if (rect.Overlaps(new RectInt(room.TileSpacePosition, room.Size)))
					return true;
			}
		}
		return false;
	}

	// Translation only: no InvalidateRoomPreview, since room contents are unchanged and previews are
	// drawn at room.WorldBounds — the cached (including NeuralTextureActor) textures are reused.
	void ApplyGroupDelta(Point2 delta)
	{
		foreach (var (id, origin) in roomMoveOrigins)
			if (RoomById(id) is { } r)
			{
				var target = origin + delta;
				if (r.TileSpacePosition != target)
					MoveRoomGeometry(r, target);
			}
		if (CurrentRoom is { } primary)
			roomCreatePosition = primary.TileSpacePosition;
		editorGameDirty = true;
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
			if (room == editing || room.Sublayer != editing.Sublayer)
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
		if (Assets.Rooms.TryGetValue((room.Sublayer, oldCell), out var indexed) && indexed == room)
			Assets.Rooms.Remove((room.Sublayer, oldCell));
		Assets.Rooms[(room.Sublayer, room.Cell)] = room;
	}

	static bool RoomGeometryOverlaps(Point2 position, Point2 size, int sublayer, Room? exclude)
	{
		var candidate = new RectInt(position, size);
		foreach (var room in Assets.RoomsById.Values)
		{
			if (room == exclude || room.Sublayer != sublayer)
				continue;
			if (candidate.Overlaps(new RectInt(room.TileSpacePosition, room.Size)))
				return true;
		}

		return false;
	}

	// Nearest free room-lattice cell to a desired tile, spiralling outward a lattice step at a time so
	// a pasted/duplicated room lands next to where it was asked for without overlapping existing rooms.
	Point2 FindFreeRoomCell(Point2 desiredTile, Point2 size, int sublayer)
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
					if (!RoomGeometryOverlaps(pos, size, sublayer, null))
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
		it.MoveTo(FindFreeRoomCell(desiredTile, it.Size, it.Sublayer));

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

	// Slope painting is offered for slope-capable foreground terrain and tile-kit backgrounds.
	bool SlopePaintAvailable
		=> !actorSelectTool && selectedActorPaletteIndex < 0
			&& Factory.Find(brush) is { SupportsSlope: true } entry
			&& entry.Layer == activeTileLayer
			&& (activeTileLayer == Factory.TileLayer.Background || entry.BlocksMovement);

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

		var canInteract = (!imgui.WantsMouseInput || gameViewHovered) && inner.Contains(cursor);

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
				var resizingColorBrush = wheel.Y != 0 && CurrentRoom is { } room && roomColorFieldPainter.IsActive(room);
				if (resizingColorBrush)
					roomColorFieldPainter.AdjustRadius(wheel.Y);
				else
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
			if (room.Sublayer != editorSublayer)
				continue;
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
				selectedRoomIds.Clear(); // multi-select is a zoomed-out concept; collapse to the primary
			}
		}
		else if (fraction < RoomEditEnterFraction)
		{
			editingRoomGeometry = true;
			ClearEditorDragState();
			selectedRoomIds.Clear();
			if (selectedRoomId is { } id)
				selectedRoomIds.Add(id);
		}
	}

	void DrawEditorRooms(Rect visibleWorld)
	{
		using var perf = PerfStats.Time(PerfDetail.DrawEditorRooms);
		BuildQueuedRoomPreview();
		if (editorSublayer > 0)
			foreach (var room in Assets.RoomsById.Values)
				if (room.Sublayer == 0)
					DrawEditorRoom(room, visibleWorld, Color.White * 0.16f, Color.Gray * 0.25f);
		foreach (var room in Assets.RoomsById.Values)
			if (room.Sublayer == editorSublayer)
				DrawEditorRoom(room, visibleWorld, Color.White, Color.Gray);
	}

	void DrawEditorRoom(Room room, Rect visibleWorld, Color tint, Color outline)
	{
		var bounds = room.WorldBounds;
		if (visibleWorld.GetIntersection(new Rect(bounds.X, bounds.Y, bounds.Width, bounds.Height)).Area <= 0)
			return;

		if (GetEditorRoomTexture(room) is { } preview)
			DrawEditorRoomPreview(preview, bounds, tint);
		else
			DrawEditorRoomBackdrop(room, bounds, tint);
		batcher.RectLine(bounds, MathF.Max(1.0f / editorZoom, LineWeight / editorZoom), outline);
		if (editingRoomGeometry && selectedRoomIds.Contains(room.Id))
		{
			batcher.Rect(bounds, RoomSelectionColor * 0.14f);
			batcher.RectLine(bounds, MathF.Max(1.0f / editorZoom, (LineWeight + 1f) / editorZoom), RoomSelectionColor);
		}
	}

	static Color RoomSelectionColor => new(0x5ac8fa);

	void DrawEditorRoomBackdrop(Room room, RectInt bounds, Color tint)
	{
		var opacity = tint.A / 255.0f;
		var top = room.BackdropColor * opacity;
		var bottom = (room.BackdropColor2 ?? room.BackdropColor) * opacity;
		batcher.Rect(bounds, top, top, bottom, bottom);
		if (Assets.GetSurface(room.BackdropSurfaceId) is { } surface)
			surface.Render(batcher, new Rect(bounds.X, bounds.Y, bounds.Width, bounds.Height), Time.SecondsF, opacity);
	}

	void DrawEditorRoomPreview(Texture preview, RectInt bounds, Color tint)
	{
		var scale = new Vector2(
			bounds.Width / (float)Math.Max(1, preview.Width),
			bounds.Height / (float)Math.Max(1, preview.Height));
		batcher.Image(preview, bounds.TopLeft, Vector2.Zero, scale, 0, tint);
	}

	Room? GetRoomAtWorld(Vector2 world)
	{
		foreach (var room in Assets.RoomsById.Values)
			if (room.Sublayer == editorSublayer && room.WorldBounds.Contains(world))
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

	bool UpdateRoomColorFieldPainting(Rect inner, bool worldMouseBlocked, bool keyboardBlocked)
	{
		if (!roomColorFieldPainter.Active)
			return false;
		if (CurrentRoom is not { } room || !roomColorFieldPainter.IsActive(room))
		{
			EndRoomColorFieldPainting();
			return false;
		}

		if (!keyboardBlocked && Input.Keyboard.Pressed(Keys.Escape))
		{
			EndRoomColorFieldPainting();
			return true;
		}

		if (maskPaintActorId != null)
			EndMaskPainting();

		var canInteract = !worldMouseBlocked && inner.Contains(Cursor);
		if (canInteract && (Input.Mouse.LeftDown || Input.Mouse.RightDown))
		{
			if (!roomColorFieldPainter.Stroke)
			{
				undo.OpenPending(RoomColorFieldUndoSlot, room.Id, $"Paint {roomColorFieldPainter.Layer} Color Field",
					CurrentRoomUndoTarget(room), EditorSelectionUndoTarget());
				roomColorFieldPainter.Stroke = true;
			}

			if (roomColorFieldPainter.Paint(room, ScreenToEditorWorld(Cursor, inner), Time.Delta, Input.Mouse.RightDown))
				SyncRoomColorFieldPreview(room);
		}
		else if (roomColorFieldPainter.Stroke)
			CommitRoomColorFieldStroke(room);

		return true;
	}

	void EndRoomColorFieldPainting()
	{
		if (CurrentRoom is { } room)
			CommitRoomColorFieldStroke(room);
		else
		{
			undo.CommitPending(RoomColorFieldUndoSlot);
			roomColorFieldPainter.Stroke = false;
		}
		roomColorFieldPainter.Stop();
	}

	void CommitRoomColorFieldStroke(Room room)
	{
		if (!roomColorFieldPainter.Stroke)
			return;
		roomColorFieldPainter.Stroke = false;
		undo.CommitPending(RoomColorFieldUndoSlot);
		room.Save();
		MarkRoomPreviewDirty(room);
	}

	void SyncRoomColorFieldPreview(Room room)
	{
		if (game?.CurrentRoom is { } preview && preview.Id == room.Id)
		{
			var source = roomColorFieldPainter.Field(room);
			var destination = roomColorFieldPainter.Layer == Factory.TileLayer.Foreground
				? preview.ForegroundColorField
				: preview.BackgroundColorField;
			destination.CopyFrom(source);
		}
		MarkRoomPreviewDirty(room);
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
			var targetId = ActorSpecs.ReadString(target.Actor, "Id") ?? "";
			var sourceId = ActorSpecs.ReadString(source.Actor, "Id") ?? "";
			using (CaptureRoomUndo("Link Doors", source.Room, target.Room))
			{
				ActorSpecs.SetProperty(source.Actor, nameof(LinkedDoor.TargetActorId), JsonValue.Create(targetId));
				ActorSpecs.SetProperty(target.Actor, nameof(LinkedDoor.TargetActorId), JsonValue.Create(sourceId));
			}
			MarkRoomPreviewDirty(source.Room);
			if (target.Room != source.Room)
				MarkRoomPreviewDirty(target.Room);
			if (source.Room == CurrentRoom)
				editorGameDirty = true;
			else
				source.Room.Save();
			if (target.Room == CurrentRoom)
				editorGameDirty = true;
			else if (target.Room != source.Room)
				target.Room.Save();
			pickingLinkedDoorTargetForActorId = null;
		}

		return true;
	}

	void DrawLinkedDoorTargetPickerOverlay(Rect visibleWorld)
	{
		var lineWeight = MathF.Max(1.0f / editorZoom, LineWeight / editorZoom);
		foreach (var room in Assets.RoomsById.Values)
		{
			if (room.Sublayer != editorSublayer)
				continue;
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

	(Room Room, JsonObject Actor)? FindLinkedDoorAtWorld(Vector2 world, string excludeId)
	{
		foreach (var room in Assets.RoomsById.Values)
		{
			if (room.Sublayer != editorSublayer || !room.WorldBounds.Contains(world))
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
		var worldMouseBlocked = imgui.WantsMouseInput && !gameViewHovered && !SingleTouchInEditorWorld(inner);
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
		PreserveEditorViewAcrossLayoutChanges(inner);
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
		if (UpdateRoomColorFieldPainting(inner, worldMouseBlocked, keyboardBlocked))
			worldMouseBlocked = true;
		if (UpdateMaskPainting(inner, worldMouseBlocked, keyboardBlocked))
			worldMouseBlocked = true;

		// Room picking. A single click selects the room under the cursor (room-edit mode only, so it
		// doesn't fight tile painting when zoomed in); a double click anywhere on a room zooms to it
		// (which zooms in past the threshold and leaves room-edit mode). The old single-click-to-zoom
		// is gone -- selecting and zooming are now distinct gestures.
		if (!worldMouseBlocked && inner.Contains(Cursor) && !eyedropper)
		{
			var doubleClick = IsMouseDoubleClicked(ImGuiMouseButton.Left);
			// Single-click selection + drag in room-select mode is handled by BeginRoomSelectPress
			// below (so it can select-and-drag in one gesture); here we only handle the zoom gesture.
			// A plain double-click outside room-select mode still switches+zooms to the clicked room.
			if (doubleClick)
			{
				var worldMouse = ScreenToEditorWorld(Cursor, inner);
				if (GetRoomAtWorld(worldMouse) is { } selected)
				{
					var switching = selected != CurrentRoom;
					var zoomTo = switching || editingRoomGeometry;
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
					// Keep the multi-selection consistent with the primary after a zoom-select.
					if (editingRoomGeometry)
					{
						selectedRoomIds.Clear();
						selectedRoomIds.Add(selected.Id);
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
			proposedRoomTiles = editorDrag.Kind != EditorDragKind.None || worldMouseBlocked || overGame
				? null
				: (!editingRoomGeometry ? GetProposedRoomTiles(editing, worldMouse) : null) ?? GetEmptyRoomCellProposal(worldMouse);
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
			if (!editingRoomGeometry && !worldMouseBlocked && overGame && Input.Mouse.RightPressed && hoveredActorIndex >= 0)
			{
				var contextActor = hoveredPathPoint >= 0 ? selectedRoomActorIndex : hoveredActorIndex;
				var contextPoint = hoveredPathPoint >= 0 ? hoveredPathPoint
					: HitTestPointPathHandle(editing, contextActor, worldMouse, gridWeight);
				OpenActorContextMenu(editing, contextActor, contextPoint);
				worldMouseBlocked = true;
			}

			if (eyedropper)
				CancelTilePaint();

			if (worldMouseBlocked)
				CancelTilePaint();

			// Double-clicking the selected path adds a point (on a segment, or off either end); any
			// other press in the room drops the point selection, so Backspace goes back to meaning
			// "delete the actor".
			if (!editingRoomGeometry && !worldMouseBlocked && actorSelectActive && overGame &&
				IsMouseDoubleClicked(ImGuiMouseButton.Left) &&
				TryInsertPointPathPoint(editing, selectedRoomActorIndex, worldMouse, gridWeight))
			{
				ClearEditorDragState();
				CancelTilePaint();
				worldMouseBlocked = true;
			}
			else if (!worldMouseBlocked && actorSelectActive && overGame && Input.Mouse.LeftPressed && hoveredPathPoint < 0)
				ClearSelectedPathPoint();

			if (editingRoomGeometry && !worldMouseBlocked && !roomActivationClickConsumed && Input.Mouse.LeftPressed)
			{
				BeginRoomSelectPress(editing, worldMouse, hoveredRoomGeometryHandle);
			}
			else if (!editingRoomGeometry && !worldMouseBlocked && actorSelectActive && overGame && Input.Mouse.LeftPressed && hoveredPathPoint >= 0)
			{
				ClearEditorDragState();
				SelectPathPoint(hoveredPathPoint);
				if (!IsBurrowDoorPoint(editing.Actors[selectedRoomActorIndex], hoveredPathPoint))
				{
					editorDrag.Kind = EditorDragKind.ActorPathPoint;
					editorDrag.PathPointIndex = hoveredPathPoint;
					editorDrag.ActorSnapsByDefault = true;
				}
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
						editorDrag.ActorStartPosition = ActorSpecs.ReadPoint(editing.Actors[selectedRoomActorIndex], "Position") ?? Point2.Zero;
						editorDrag.ActorPressWorld = worldMouse;
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
			else if (!editingRoomGeometry && !worldMouseBlocked && eyedropper && overGame && Input.Mouse.LeftPressed)
			{
				if (TrySampleVisibleTile(editing, tileOver, activeTileLayer, out var layer, out var sampled))
					SelectTileBrush(layer, sampled);
			}
			else if (!editingRoomGeometry && !worldMouseBlocked && !actorSelectTool && !brushIsActor && overGame && ctrlClick &&
				(activeTileLayer == Factory.TileLayer.Background ? editing.GetBackgroundEntry(tileOver) : editing.GetEntry(tileOver)) is { SupportsSlope: true } slopeEntry &&
				(activeTileLayer == Factory.TileLayer.Background || slopeEntry.BlocksMovement))
			{
				using (CaptureRoomUndo("Toggle Slope Tile", editing))
				{
					editing.SetSlope(tileOver, !editing.IsSlope(tileOver, activeTileLayer), activeTileLayer);
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
					{
						selectedRoomActorIndex = editing.Actors.Count - 1;
						SelectEditorActorTool();
						ClearEditorDragState();
						editorSelectionRect = null;
					}
					CancelTilePaint();
					editorGameDirty = true;
				}
			}
			else if (!editingRoomGeometry && !worldMouseBlocked && overGame && !brushIsActor && brushEntry != null && (Input.Mouse.LeftPressed || Input.Mouse.RightPressed))
			{
				undo.CommitPending(TilePaintUndoSlot);
				OpenPendingRoomUndo(TilePaintUndoSlot, "Paint Tiles", editing);
				ClearEditorDragState();
				tilePaintStroke.Begin(
					editing.Id,
					ClampTile(editing, tileOver),
					activeTileLayer,
					Input.Mouse.LeftPressed ? brush : '0',
					slopePaint && Input.Mouse.LeftPressed && SlopePaintAvailable);
				editorDrag.Kind = EditorDragKind.TilePaint;
				editorSelectionRect = null;
			}

			if (!worldMouseBlocked && DragKind == EditorDragKind.RoomGeometry && Input.Mouse.LeftDown)
			{
				ApplyRoomGeometryDrag(editing, worldMouse);
			}
			else if (!worldMouseBlocked && DragKind == EditorDragKind.RoomMove && Input.Mouse.LeftDown)
			{
				UpdateRoomGroupMove(worldMouse);
			}
			else if (DragKind == EditorDragKind.RoomMarquee && Input.Mouse.LeftDown)
			{
				roomMarqueeToWorld = worldMouse;
			}
			else if (!worldMouseBlocked && actorSelectActive && DragKind == EditorDragKind.ActorPathPoint && selectedRoomActorIndex >= 0 && Input.Mouse.LeftDown)
			{
				OpenPendingRoomUndo(ActorMoveUndoSlot, "Move Path Point", editing);
				MovePointPathPoint(editing, selectedRoomActorIndex, editorDrag.PathPointIndex, worldMouse, RoomActorDragShouldSnap());
			}
			else if (!worldMouseBlocked && actorSelectActive && DragKind == EditorDragKind.ActorResize && selectedRoomActorIndex >= 0 && Input.Mouse.LeftDown)
			{
				OpenPendingRoomUndo(ActorMoveUndoSlot, "Resize Actor", editing);
				ResizeHiddenWallActor(editing, selectedRoomActorIndex, editorDrag.ResizeHandle, worldMouse, tileOver, !Input.Keyboard.Shift);
			}
			else if (!worldMouseBlocked && actorSelectActive && DragKind == EditorDragKind.RoomActor && selectedRoomActorIndex >= 0 && Input.Mouse.LeftDown)
			{
				if (!editorDrag.ActorDragStarted && ActorPressPassedDragThreshold)
					editorDrag.ActorDragStarted = true;
				if (editorDrag.ActorDragStarted)
				{
					OpenPendingRoomUndo(ActorMoveUndoSlot, "Move Actor", editing);
					MoveRoomActorTo(editing, selectedRoomActorIndex, worldMouse,
						ScreenToEditorWorld(actorPressCursor, inner), RoomActorDragShouldSnap());
				}
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
				if (DragKind == EditorDragKind.RoomActor && actorPressCanCycle && !editorDrag.ActorDragStarted &&
					!ActorPressPassedDragThreshold && !worldMouseBlocked && actorSelectActive)
					CycleRoomActorSelection(editing, editorDrag.ActorPressWorld);
				if (DragKind == EditorDragKind.RoomMove)
					FinishRoomGroupMove();
				else if (DragKind == EditorDragKind.RoomMarquee)
					FinishRoomMarquee();
				actorPressCanCycle = false;
				CommitEditorDragState();
			}

			if (!keyboardBlocked && actorSelectTool && selectedRoomActorIndex >= 0 && (Input.Keyboard.Pressed(Keys.Delete) || Input.Keyboard.Pressed(Keys.Backspace)))
			{
				var selectedPathPoint = SelectedPathPoint(editing);
				if (selectedPathPoint >= 0)
					DeleteSelectedPathPoint(editing, selectedPathPoint);
				else
					DeleteSelectedActor();
			}

			if (!editingRoomGeometry && !worldMouseBlocked && (!actorSelectTool || eyedropper) && overGame)
			{
				if (eyedropper)
				{
					var sampleRect = ((new Rect(tileOver.X, tileOver.Y, 1, 1) * Game.TileSize) + roomTopLeft).Inflate(gridWeight * 2);
					DrawEditorEyedropperCursor(sampleRect, gridWeight);
				}
				else if (actorBrush is { } actorPreview)
				{
					batcher.Image(actorPreview.PreviewImage, actorBrushPosition, actorPreview.Origin, new Vector2(actorPreview.PreviewScale), 0, Color.White);
				}
				else if (brushEntry is { } placing)
				{
					var previewTileRect = TilePlacing ? tilePaintStroke.Rect : new RectInt(tileOver.X, tileOver.Y, 1, 1);
					var cursorRect = TileRectToRoomWorldRect(editing, previewTileRect).Inflate(gridWeight * 2);
					var placingTile = TilePlacing ? tilePaintStroke.Tile : brush;
					var cursorColor = placingTile == '0' ? Color.Red : highlight;
					batcher.RectLine(cursorRect, gridWeight * 2, cursorColor);
					if (placingTile != '0')
					{
						if (TilePlacing && tilePaintStroke.Slope)
						{
							// The inferred slope subtextures need a geometry rebuild per cell, so just
							// outline the triangle we'll fill -- its hypotenuse is the ramp surface.
							var toRight = tilePaintStroke.ToRight;
							var underhang = tilePaintStroke.Underhang;

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
				tilePaintStroke.Update(ClampTile(editing, tileOver));

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
			if (DragKind == EditorDragKind.RoomMarquee)
			{
				var marquee = RectFromCorners(roomMarqueeFromWorld, roomMarqueeToWorld);
				batcher.Rect(marquee, RoomSelectionColor * 0.15f);
				batcher.RectLine(marquee, MathF.Max(1f, gridWeight), RoomSelectionColor);
			}
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
			if (roomColorFieldPainter.IsActive(editing))
			{
				var brushColor = Input.Mouse.RightDown ? new Color(255, 90, 90, 255) : roomColorFieldPainter.BrushColor;
				batcher.CircleLine(worldMouse, roomColorFieldPainter.Radius, gridWeight, 48, brushColor);
				var core = roomColorFieldPainter.Radius * (1.0f - roomColorFieldPainter.Softness);
				if (core > 1.0f)
					batcher.CircleLine(worldMouse, core, gridWeight, 48, brushColor * 0.4f);
			}

			// Right-click an adjacent proposal or any empty world-grid cell to add a room there.
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
				var stroke = tilePaintStroke;
				if (stroke.RoomId == room.Id)
				{
					ApplyTileStroke(room, stroke);
					editorGameDirty = true;
				}
			}
			if (TilePlacing)
				ClearEditorDragState();
			undo.CommitPending(TilePaintUndoSlot);
		}

		using (PerfStats.Time(PerfDetail.EditorSync))
		{
			// A room-actor drag moves its live preview instance directly. Rebuilding the whole
			// preview here would recreate every actor for every pixel, including NCA prewarm.
			// Once the mouse is released CommitEditorDragState clears the drag and the normal
			// save/rebuild below runs once, restoring any actor-specific derived state.
			if (editorGameDirty && DragKind != EditorDragKind.RoomActor)
			{
				if (TilePlacing || DragKind == EditorDragKind.RoomGeometry)
				{
					if (CurrentRoom is { } room)
						MarkRoomPreviewDirty(room);
					SyncEditorGame();
				}
				else
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

		var pos = new Vector2(EditorPaletteDockRect.Right + 10, WorkRect.Y + EditorMenuButtonSize + 12);
		SetNextWindowPos(pos, ImGuiCond.Always);
		PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(8, 6));
		PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(6, 3));
		PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.04f, 0.04f, 0.05f, 0.82f));
		Begin("Selection Options", flags);
		EditorGui.CheckboxFlags("Empty Tiles", ref selectionSnapshotFlags, SnapshotFlags.IncludeEmptyTiles);
		Checkbox("BG Tiles", ref selectionIncludeBackgroundTiles);
		Checkbox("FG Tiles", ref selectionIncludeForegroundTiles);
		Checkbox("Actors", ref selectionIncludeActors);

		if (editorSelectionRect != null)
		{
			Separator();
			if (Button("Flip X"))
				FlipSelection(horizontal: true);
			SameLine();
			if (Button("Flip Y"))
				FlipSelection(horizontal: false);
		}

		End();
		PopStyleColor();
		PopStyleVar(2);
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

	RectInt? GetEmptyRoomCellProposal(Vector2 world)
	{
		if (GetRoomAtWorld(world) != null)
			return null;

		var position = (world / (Vector2)RoomLatticeSize).FloorToPoint2() * RoomLatticeTiles;
		return RoomGeometryOverlaps(position, Room.DefaultSize, editorSublayer, null)
			? null
			: new RectInt(position, Room.DefaultSize);
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
			if (room.Sublayer != editorSublayer)
				continue;
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
		=> CreateRoomFromSource(CurrentRoom, position, size, select: true, tween: true, undoName: "Create Room");

	Room CreateRoomFromSource(Room? source, Point2 position, Point2 size, bool select, bool tween, string undoName, Action<Room>? configure = null)
	{
		var sourceSettings = source != null ? CaptureRoomSettings(source) : (RoomSettings?)null;
		CurrentRoom?.Save();
		if (source != null && source != CurrentRoom)
			source.Save();

		var it = new Room(position, size);
		it.Sublayer = source?.Sublayer ?? editorSublayer;
		if (sourceSettings is { } settings)
			ApplyRoomSettings(it, settings);
		MirrorAdjacentRoomBorders(it);
		configure?.Invoke(it);

		using (undo.Capture(undoName, new RoomExistenceUndoTarget(this, it), EditorSelectionUndoTarget()))
		{
			AddRoomToWorld(it);
			if (select)
				SelectRoom(it);
			SyncEditorGame();
		}

		if (tween)
			TweenEditorCameraToRoom(it);
		return it;
	}

	void AddSublayer(Room source)
	{
		var room = NewSublayerRoom(source);
		CurrentRoom?.Save();
		using (CaptureRoomExistenceUndo("Add Sublayer", room))
		{
			AddRoomToWorld(room);
			SelectRoom(room);
			selectedRoomActorIndex = -1;
			editorRoomEntryDirection = null;
			SyncEditorGame();
		}
	}

	Room NewSublayerRoom(Room source)
	{
		var sublayer = 1;
		while (RoomGeometryOverlaps(source.TileSpacePosition, source.Size, sublayer, null))
			sublayer++;

		var baseRoom = Assets.RoomsById.Values.FirstOrDefault(it =>
			it.Sublayer == 0 && it.TileSpacePosition == source.TileSpacePosition && it.Size == source.Size) ?? source;
		var id = $"{baseRoom.Id}_sub{sublayer}";
		for (var suffix = 2; Assets.RoomsById.ContainsKey(id); suffix++)
			id = $"{baseRoom.Id}_sub{sublayer}_{suffix}";

		var room = new Room(id, source.TileSpacePosition, source.Size) { Sublayer = sublayer };
		ApplyRoomSettings(room, CaptureRoomSettings(source));
		return room;
	}

	void SelectEditorSublayer(int sublayer)
	{
		if (sublayer == editorSublayer)
			return;

		var focus = CurrentRoom?.WorldBounds.Center ?? editorCamera.RoundToPoint2();
		Room? nearest = null;
		var nearestDistance = float.MaxValue;
		foreach (var candidate in Assets.RoomsById.Values)
		{
			if (candidate.Sublayer != sublayer)
				continue;
			var distance = Vector2.DistanceSquared(candidate.WorldBounds.Center, focus);
			if (distance < nearestDistance)
			{
				nearest = candidate;
				nearestDistance = distance;
			}
		}
		if (nearest == null)
			return;

		CurrentRoom?.Save();
		SelectRoom(nearest);
		selectedRoomActorIndex = -1;
		editorRoomEntryDirection = null;
		SyncEditorGame();
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
			if (room == exclude || room.Sublayer != exclude.Sublayer)
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
			OpenPopup("Create Room");
		}

		if (!BeginPaddedPopup("Create Room"))
		{
			pendingCreateRoomTiles = null;
			return;
		}

		if (Selectable("Create New Room") && pendingCreateRoomTiles is { } proposal)
		{
			CreateRoom(proposal.Position, proposal.Size);
			pendingCreateRoomTiles = null;
			proposedRoomTiles = null;
			CloseCurrentPopup();
		}

		EndPaddedPopup();
	}

	void DrawAddTilePopup()
	{
		if (addTilePopupRequested)
		{
			addTilePopupRequested = false;
			newTileName = "";
			OpenPopup("Add Tile");
		}

		var open = true;
		if (!BeginPaddedPopupModal("Add Tile", ref open, ImGuiWindowFlags.AlwaysAutoResize))
			return;

		TextUnformatted(newTileLayer == Factory.TileLayer.Background
			? "Create a new background tile:"
			: "Create a new foreground tile:");
		SetNextItemWidth(240);
		if (IsWindowAppearing())
			SetKeyboardFocusHere();
		var submit = InputTextWithHint("##NewTileName", "tile name", ref newTileName, 32,
			ImGuiInputTextFlags.EnterReturnsTrue);
		var name = newTileName.Trim().ToLowerInvariant();
		var valid = CanCreateTileKit(name);
		BeginDisabled(!valid);
		var create = Button("Create", new Vector2(90, 0)) || submit;
		EndDisabled();
		SameLine();
		var cancel = Button("Cancel", new Vector2(90, 0)) || !open;

		if (create && valid)
		{
			var spec = CreateTileKitDraft(name, newTileLayer);
			newTileName = "";
			OpenTileKitEditor(spec);
			CloseCurrentPopup();
		}
		else if (cancel)
		{
			newTileName = "";
			CloseCurrentPopup();
		}

		EndPaddedPopup();
	}

	void DrawDeleteRoomPopup()
	{
		var open = true;
		if (!BeginPopupModal("Delete Room?", ref open, ImGuiWindowFlags.AlwaysAutoResize))
			return;

		var room = pendingDeleteRoomId != null ? Assets.GetRoom(pendingDeleteRoomId) : null;
		Text(room != null ? $"Delete room '{room.Id}'?" : "Room no longer exists.");
		Spacing();

		if (Button("Delete", new Vector2(90, 0)))
		{
			if (room != null)
				DeleteRoom(room);
			pendingDeleteRoomId = null;
			CloseCurrentPopup();
		}
		SameLine();
		if (Button("Cancel", new Vector2(90, 0)) || !open)
		{
			pendingDeleteRoomId = null;
			CloseCurrentPopup();
		}

		EndPopup();
	}

	void DrawResizeRoomPopup()
	{
		if (roomResizePopupRequested)
		{
			roomResizePopupRequested = false;
			OpenPopup("Keep Resize?");
		}

		var open = true;
		if (!BeginPopupModal("Keep Resize?", ref open, ImGuiWindowFlags.AlwaysAutoResize))
			return;

		Text("Resizing the room reclaimed space that held:");
		if (roomResizeDiscardedForeground > 0)
			Text($"    {roomResizeDiscardedForeground} foreground tiles (discarded)");
		if (roomResizeDiscardedBackground > 0)
			Text($"    {roomResizeDiscardedBackground} background tiles (discarded)");
		if (roomResizeDiscardedActors > 0)
			Text($"    {roomResizeDiscardedActors} actors (now outside the room)");
		Spacing();

		if (Button("Keep", new Vector2(90, 0)))
			CloseCurrentPopup();
		SameLine();
		if (Button("Cancel", new Vector2(90, 0)) || !open)
		{
			// The drag committed one entry as it ended, so cancelling is exactly an undo of it --
			// checked by name in case something else landed on the stack while the popup was up.
			if (undo.UndoName == RoomResizeUndoName)
				undo.Undo();
			CloseCurrentPopup();
		}

		EndPopup();
	}

	void DeleteRoom(Room room)
	{
		using (CaptureRoomExistenceUndo("Delete Room", room))
			RemoveRoomFromWorld(room);
	}

	bool ToggleEditorPressed(bool keyboardBlocked)
		=> (!keyboardBlocked && Input.Keyboard.Pressed(Keys.Tab)) || Input.AnyControllerButtonPressed(Buttons.RightStick);

	void PreferencesImGui()
	{
		if (!preferencesOpen)
			return;

		SetNextWindowSize(new Vector2(280, 0), ImGuiCond.FirstUseEver);
		DockNextToolWindow("Preferences");
		if (!Begin("Preferences", ref preferencesOpen, ImGuiWindowFlags.NoCollapse))
		{
			NoteDockedWindow();
			End();
			return;
		}
		NoteDockedWindow();

		SetNextItemWidth(150);
		if (BeginCombo("Theme", EditorThemeName(editorTheme)))
		{
			for (var i = (int)EditorTheme.Default; i <= (int)EditorTheme.SolarizedLight; i++)
			{
				var theme = (EditorTheme)i;
				if (Selectable(EditorThemeName(theme), theme == editorTheme))
					SelectEditorTheme(theme);
			}
			EndCombo();
		}

		SetNextItemWidth(150);
		if (BeginCombo("Scroll input", EditorScrollModeName(editorScrollMode)))
		{
			for (var i = 0; i <= (int)EditorScrollMode.Trackpad; i++)
			{
				var mode = (EditorScrollMode)i;
				if (Selectable(EditorScrollModeName(mode), mode == editorScrollMode))
				{
					editorScrollMode = mode;
					trackpadScrollLatch = 0;
					SaveWindowSettings();
				}
			}
			EndCombo();
		}
		if (Checkbox("Preserve playtest save state", ref preservePlaytestSaveState))
			SaveWindowSettings();
		if (Checkbox("Editor Game Run in Background", ref editorGameRunInBackground))
			SaveWindowSettings();
		if (Checkbox("Paste actors at view center", ref pasteActorAtViewCenter))
			SaveWindowSettings();
		if (IsItemHovered())
			SetTooltip("On: a pasted actor lands in the middle of the editor view.\nOff: it keeps the position it was copied from.\nEither way it is clamped inside the room.");
		if (Button(EditorIcons.ResetToolWindowLayout))
			ResetEditorToolWindowLayout();
		if (IsItemHovered())
			SetTooltip("Docks Tile Creator, NCT Tiles, Particles, Music, SFX and this window\nback into the panel on the right.");

		End();
	}

	static string EditorScrollModeName(EditorScrollMode mode) => mode switch
	{
		EditorScrollMode.Mouse => "Mouse",
		EditorScrollMode.Trackpad => "Trackpad",
		_ => "Auto"
	};

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
		editorSublayer = room.Sublayer;
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
		editorSublayer = 0;
	}

	void TransitionViewerImGui()
	{
		if (!showTransitionViewer)
			return;

		if (Begin("Transition Viewer", ref showTransitionViewer, ImGuiWindowFlags.NoSavedSettings))
		{
			if (BeginTable("transitions", 4, ImGuiTableFlags.BordersV | ImGuiTableFlags.BordersOuterH | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY,
				new Vector2(GetContentRegionAvail().X, 480)))
			{
				TableSetupScrollFreeze(0, 1);
				TableSetupColumn("Sprite", ImGuiTableColumnFlags.WidthFixed, 100);
				TableSetupColumn("Animation", ImGuiTableColumnFlags.WidthFixed, 160);
				TableSetupColumn("Frames", ImGuiTableColumnFlags.WidthStretch);
				TableSetupColumn("Count", ImGuiTableColumnFlags.WidthFixed, 60);
				TableHeadersRow();

				foreach (var (spriteName, sprite) in Assets.Sprites)
				{
					foreach (var anim in sprite.Animations)
					{
						if (!anim.Name.Contains("->"))
							continue;

						TableNextRow();
						TableNextColumn();
						TextUnformatted(spriteName);
						TableNextColumn();
						TextUnformatted(anim.Name);
						TableNextColumn();

						float previewSize = 48;
						for (int i = 0; i < anim.FrameCount; i++)
						{
							if (i > 0) SameLine(0, 2);
							int frameIdx = anim.FrameStart + i;
							if (frameIdx < sprite.Frames.Count)
							{
								var frame = sprite.Frames[frameIdx];
								var sub = frame.VisibleSubtexture;
								float scale = MathF.Min(previewSize / sub.Size.X, previewSize / sub.Size.Y);
								imgui.Image(sub, sub.Size * scale);
								if (IsItemHovered())
									SetTooltip($"Frame {i}\n{frame.Duration * 1000:F0}ms");
							}
						}

						TableNextColumn();
						TextUnformatted($"{anim.FrameCount}");
					}
				}

				EndTable();
			}
		}
		End();
	}

}
#endif
