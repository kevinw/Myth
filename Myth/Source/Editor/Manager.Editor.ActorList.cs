#if EDITOR
using LinkDotNet.StringBuilder;
using Foster.Framework;
using ImGuiNET;
using System.Numerics;
using System.Text.Json.Nodes;

namespace Myth;

// The Actor Instances tool window: searches every actor instance across all rooms and jumps the
// editor to the one you pick.
public partial class Manager
{
	const string ActorListDragPayload = "MYTH_ACTOR_LIST";
	JsonObject? actorListDraggingActor;

	readonly record struct ActorInstanceSearchResult(string RoomId, string ActorId, string Position, string Modified);

	readonly List<ActorInstanceSearchResult> actorInstanceSearchResults = [];
	Factory.ActorPaletteEntry? actorInstanceSearchEntry;
	string actorInstanceSearchWindowTitle = "Actor Instances###ActorInstances";
	string actorInstanceSearchSummary = "0 instances";
	string actorInstanceSearchWorldId = "";
	bool actorInstanceSearchOpen;

	unsafe void DrawActorListSection()
	{
		if (CurrentRoom is not { } room)
			return;

		if (room.Actors.Count == 0)
		{
			ImGui.TextDisabled("No actors");
			return;
		}

		var label = new ValueStringBuilder(stackalloc char[128]);
		var reorderAt = -1;
		for (var i = 0; i < room.Actors.Count; i++)
		{
			var actor = room.Actors[i];
			label.Clear();
			label.Append(ActorSpecs.ReadString(actor, "From") ?? ActorSpecs.GetTypeName(actor) ?? "Actor");

			ImGui.PushID(i);
			if (ImGui.Selectable(label.AsSpan(), selectedRoomActorIndex == i))
			{
				using (CaptureSelectionUndo("Select Actor"))
				{
					SelectEditorActorTool();
					selectedRoomActorIndex = i;
					ClearEditorDragState();
					editorSelectionRect = null;
					CancelTilePaint();
				}
			}

			if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
				TweenEditorCameraToActor(room, i);

			var rowMin = ImGui.GetItemRectMin();
			var rowMax = ImGui.GetItemRectMax();
			if (ImGui.BeginDragDropSource())
			{
				actorListDraggingActor = actor;
				ImGui.SetDragDropPayload(ActorListDragPayload, IntPtr.Zero, 0);
				ImGui.TextUnformatted(label.AsSpan());
				ImGui.EndDragDropSource();
			}
			if (ImGui.BeginDragDropTarget())
			{
				var payload = ImGui.AcceptDragDropPayload(ActorListDragPayload);
				if (payload.NativePtr != null && actorListDraggingActor != null)
				{
					var after = ImGui.GetMousePos().Y >= (rowMin.Y + rowMax.Y) * 0.5f;
					var y = after ? rowMax.Y : rowMin.Y;
					ImGui.GetWindowDrawList().AddLine(new(rowMin.X, y), new(rowMax.X, y),
						ImGui.GetColorU32(ImGuiCol.DragDropTarget), 2);
					if (payload.IsDelivery())
						reorderAt = i + (after ? 1 : 0);
				}
				ImGui.EndDragDropTarget();
			}
			ImGui.PopID();
		}
		label.Dispose();

		if (reorderAt >= 0 && actorListDraggingActor is { } dragged)
		{
			var from = room.Actors.IndexOf(dragged);
			if (from >= 0)
			{
				if (from < reorderAt)
					reorderAt--;
				if (from != reorderAt)
				{
					using (CaptureRoomUndo("Reorder Actors", room))
					{
						room.Actors.RemoveAt(from);
						room.Actors.Insert(reorderAt, dragged);
						SelectEditorActorTool();
						selectedRoomActorIndex = reorderAt;
						ClearEditorDragState();
						editorSelectionRect = null;
						CancelTilePaint();
						editorGameDirty = true;
					}
				}
			}
		}
		if (!ImGui.IsMouseDown(ImGuiMouseButton.Left))
			actorListDraggingActor = null;
	}

	void OpenActorInstanceSearch(Factory.ActorPaletteEntry entry)
	{
		actorInstanceSearchEntry = entry;
		actorInstanceSearchWindowTitle = $"{entry.Name} Instances###ActorInstances";
		actorInstanceSearchOpen = true;
		RefreshActorInstanceSearch();
	}

	void RefreshActorInstanceSearch()
	{
		actorInstanceSearchResults.Clear();
		actorInstanceSearchWorldId = Assets.CurrentWorldId;
		if (actorInstanceSearchEntry is not { } entry)
			return;

		foreach (var room in Assets.RoomsById.Values)
		{
			var modified = RoomJsonModified(room);
			foreach (var actor in room.Actors)
			{
				if (!ActorMatchesPaletteEntry(actor, entry))
					continue;

				var position = ActorSpecs.ReadPoint(actor, "Position") ?? Point2.Zero;
				actorInstanceSearchResults.Add(new(
					room.Id,
					ActorSpecs.GetActorId(actor) ?? "-",
					$"({position.X}, {position.Y})",
					modified));
			}
		}

		actorInstanceSearchResults.Sort(static (a, b) =>
		{
			var room = StringComparer.OrdinalIgnoreCase.Compare(a.RoomId, b.RoomId);
			return room != 0 ? room : StringComparer.OrdinalIgnoreCase.Compare(a.ActorId, b.ActorId);
		});
		actorInstanceSearchSummary = $"{actorInstanceSearchResults.Count} instances in {actorInstanceSearchWorldId}";
	}

	static bool ActorMatchesPaletteEntry(JsonObject actor, Factory.ActorPaletteEntry entry)
	{
		if (entry.ActorSpecId is { } specId)
			return string.Equals(ActorSpecs.ReadString(actor, "From"), specId, StringComparison.OrdinalIgnoreCase);
		if (entry.PropSpriteName is { } propSpriteName)
			return string.Equals(ActorSpecs.GetTypeName(actor), nameof(Prop), StringComparison.OrdinalIgnoreCase) &&
				string.Equals(ActorSpecs.ReadResolvedString(actor, nameof(Prop.SpriteName)), propSpriteName, StringComparison.OrdinalIgnoreCase);
		return entry.ActorTypeName is { } typeName &&
			string.Equals(ActorSpecs.GetTypeName(actor), typeName, StringComparison.OrdinalIgnoreCase);
	}

	static string RoomJsonModified(Room room)
	{
		try
		{
			return room.SourcePath is { } path && File.Exists(path)
				? File.GetLastWriteTime(path).ToString("yyyy-MM-dd HH:mm")
				: "-";
		}
		catch (IOException)
		{
			return "-";
		}
		catch (UnauthorizedAccessException)
		{
			return "-";
		}
	}

	void ActorInstancesImGui()
	{
		if (!actorInstanceSearchOpen)
			return;
		if (!string.Equals(actorInstanceSearchWorldId, Assets.CurrentWorldId, StringComparison.OrdinalIgnoreCase))
			RefreshActorInstanceSearch();

		ImGui.SetNextWindowSize(new Vector2(680, 420), ImGuiCond.FirstUseEver);
		if (!ImGui.Begin(actorInstanceSearchWindowTitle, ref actorInstanceSearchOpen))
		{
			ImGui.End();
			return;
		}

		ImGui.TextDisabled(actorInstanceSearchSummary);
		ImGui.SameLine();
		if (ImGui.SmallButton("Refresh"))
			RefreshActorInstanceSearch();

		if (ImGui.BeginTable("ActorInstances", 4,
			ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable | ImGuiTableFlags.ScrollY,
			ImGui.GetContentRegionAvail()))
		{
			ImGui.TableSetupScrollFreeze(0, 1);
			ImGui.TableSetupColumn("Actor ID", ImGuiTableColumnFlags.WidthStretch, 1.2f);
			ImGui.TableSetupColumn("Room", ImGuiTableColumnFlags.WidthStretch, 1.0f);
			ImGui.TableSetupColumn("Position", ImGuiTableColumnFlags.WidthFixed, 110);
			ImGui.TableSetupColumn("Room JSON Modified", ImGuiTableColumnFlags.WidthFixed, 150);
			ImGui.TableHeadersRow();

			for (var i = 0; i < actorInstanceSearchResults.Count; i++)
			{
				var result = actorInstanceSearchResults[i];
				ImGui.PushID(i);
				ImGui.TableNextRow();
				ImGui.TableNextColumn();
				if (ImGui.Selectable(result.ActorId, false, ImGuiSelectableFlags.SpanAllColumns | ImGuiSelectableFlags.AllowDoubleClick) &&
					ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
					FocusActorInstance(result);
				ImGui.TableNextColumn();
				ImGui.TextUnformatted(result.RoomId);
				ImGui.TableNextColumn();
				ImGui.TextUnformatted(result.Position);
				ImGui.TableNextColumn();
				ImGui.TextUnformatted(result.Modified);
				ImGui.PopID();
			}

			ImGui.EndTable();
		}

		ImGui.End();
	}

	void FocusActorInstance(ActorInstanceSearchResult result)
	{
		if (Assets.GetRoom(result.RoomId) is not { } room)
			return;

		var index = -1;
		for (var i = 0; i < room.Actors.Count; i++)
			if (string.Equals(ActorSpecs.GetActorId(room.Actors[i]), result.ActorId, StringComparison.OrdinalIgnoreCase))
			{
				index = i;
				break;
			}
		if (index < 0)
			return;

		using (CaptureSelectionUndo("Select Actor Instance"))
		{
			var switchingRooms = CurrentRoom != room;
			if (switchingRooms)
			{
				CurrentRoom?.Save();
				SelectRoom(room);
			}
			SelectEditorActorTool();
			selectedRoomActorIndex = index;
			ClearEditorDragState();
			editorSelectionRect = null;
			CancelTilePaint();
			if (switchingRooms)
				SyncEditorGame();
			TweenEditorCameraToActor(room, index);
		}
	}

	void TweenEditorCameraToActor(Room room, int actorIndex)
	{
		editorCameraTweenFrom = editorCamera;
		editorCameraTweenTo = RoomActorBounds(room, room.Actors[actorIndex]).Center;
		editorZoomTweenFrom = editorZoom;
		editorZoomTweenTo = editorZoom;
		editorCameraTween = 0.0f;
		editorCameraTweenSpeed = 4;
		editorViewInitialized = true;
	}
}
#endif
