#if EDITOR
using LinkDotNet.StringBuilder;
using Foster.Framework;
using ImGuiNET;

namespace Myth;

public partial class Manager
{
	void DrawActorListSection()
	{
		if (CurrentRoom is not { } room)
			return;

		if (room.Actors.Count == 0)
		{
			ImGui.TextDisabled("No actors");
			return;
		}

		var label = new ValueStringBuilder(stackalloc char[128]);
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
			ImGui.PopID();
		}
		label.Dispose();
	}

	void TweenEditorCameraToActor(Room room, int actorIndex)
	{
		editorCameraTweenFrom = editorCamera;
		editorCameraTweenTo = RoomActorBounds(room, room.Actors[actorIndex]).Center;
		editorZoomTweenFrom = editorZoom;
		editorZoomTweenTo = editorZoom;
		editorCameraTween = 0.0f;
		editorViewInitialized = true;
	}
}
#endif
