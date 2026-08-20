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

// Editor undo/redo plumbing: the IJsonUndoTarget implementations (room state, editor selection,
// actor-spec, room existence) plus the scope-capture helpers that group a gesture into one entry.
public partial class Manager
{

	IJsonUndoTarget CurrentRoomUndoTarget(Room room)
		=> new RoomUndoTarget(this, room);

	IJsonUndoTarget EditorSelectionUndoTarget()
		=> new EditorSelectionTarget(this);

	JsonUndoScope CaptureRoomUndo(string name, Room room)
		=> undo.Capture(name, CurrentRoomUndoTarget(room), EditorSelectionUndoTarget());

	JsonUndoScope CaptureRoomUndo(string name, Room first, Room second)
		=> first == second
			? CaptureRoomUndo(name, first)
			: undo.Capture(name, CurrentRoomUndoTarget(first), CurrentRoomUndoTarget(second), EditorSelectionUndoTarget());

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
	const string RoomColorFieldUndoSlot = "room-color-field";

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

	sealed class RoomUndoTarget(Manager manager, Room room) : IJsonUndoTarget
	{
		public string UndoId => $"room:{room.Id}";
		public string CaptureJson() => room.ToJsonString();
		public void ApplyJson(string json)
		{
			var oldCell = room.Cell;
			room.LoadJsonState(json);
			UpdateRoomCellIndex(room, oldCell);
			room.Save();
			manager.MarkRoomPreviewDirty(room);
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

	sealed class ActorSpecExistenceUndoTarget(string specId, string path) : IJsonUndoTarget
	{
		public string UndoId => $"actor-spec-exists:{specId}";
		public string CaptureJson() => ActorSpecs.Get(specId) is null ? "" : ActorSpecs.CaptureSpecJson(specId);
		public void ApplyJson(string json)
		{
			if (json.Length == 0)
				ActorSpecs.DeleteSpec(specId);
			else
				ActorSpecs.CreateSpec(specId, path, JsonNode.Parse(json)?.AsObject() ?? []);
		}
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

	void BeginPrefabUndo(string specId)
	{
		if (!undo.HasPending(PrefabUndoSlot, specId))
			undo.OpenPending(PrefabUndoSlot, specId, "Edit Prefab", new ActorSpecUndoTarget(specId));
	}

	void CommitPrefabUndo()
		=> undo.CommitPending(PrefabUndoSlot);
}
#endif
