#if EDITOR
using System.Numerics;
using Foster.Framework;
using ImGuiNET;

namespace Myth;

// Everything the top toolbar used to hold, behind one hamburger button overlaid on the corner of
// the game view. The toolbar was a permanent strip for a handful of rarely-pressed things -- five
// window toggles, two preferences, two rare actions -- so it now costs no layout at all, and the
// room name lives in the Room panel where the rest of the room settings are.
public partial class Manager
{
	const string EditorMenuPopup = "##EditorMenuPopup";

	static float EditorMenuButtonSize => ButtonSize * 0.8f;
	static bool BeginPaddedPopup(string id)
	{
		ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(8, 6));
		var open = ImGui.BeginPopup(id);
		if (!open) ImGui.PopStyleVar();
		return open;
	}
	static bool BeginPaddedPopupModal(string id, ref bool open, ImGuiWindowFlags flags)
	{
		ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(8, 6));
		var begun = ImGui.BeginPopupModal(id, ref open, flags);
		if (!begun) ImGui.PopStyleVar();
		return begun;
	}
	static void EndPaddedPopup() { ImGui.EndPopup(); ImGui.PopStyleVar(); }

	/// Where the hamburger sits, so the game view can exclude it from its own hover test.
	Rect EditorMenuButtonRect => new(EditorOpenRect.TopLeft + new Vector2(6, 6), new Vector2(EditorMenuButtonSize, EditorMenuButtonSize));

	void EditorMenuImGui()
	{
		if (startingPlay || gameEase >= 1.0f)
			return;

		var flags =
			ImGuiWindowFlags.NoDecoration |
			ImGuiWindowFlags.NoMove |
			ImGuiWindowFlags.NoSavedSettings |
			ImGuiWindowFlags.NoDocking |
			ImGuiWindowFlags.AlwaysAutoResize |
			ImGuiWindowFlags.NoFocusOnAppearing |
			ImGuiWindowFlags.NoBackground;

		ImGui.SetNextWindowPos(EditorOpenRect.TopLeft + new Vector2(6, 6), ImGuiCond.Always);
		ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
		if (ImGui.Begin("##EditorMenuOverlay", flags))
		{
			if (DrawHamburgerButton())
				ImGui.OpenPopup(EditorMenuPopup);
			DrawEditorSublayerButtons();
			DrawEditorMenuPopup();
			// Root-scope popups: opened from a flag (world switch, create room), so they need
			// somewhere stable to be submitted now that the toolbar is gone.
			DrawCreateRoomPopup();
		}
		ImGui.End();
		ImGui.PopStyleVar();
	}

	void DrawEditorSublayerButtons()
	{
		var max = 0;
		foreach (var room in Assets.RoomsById.Values)
			max = Math.Max(max, room.Sublayer);
		if (max == 0)
			return;

		ImGui.SameLine();
		ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(7, 3));
		for (var layer = 0; layer <= max; layer++)
		{
			var exists = false;
			foreach (var room in Assets.RoomsById.Values)
				if (room.Sublayer == layer)
				{
					exists = true;
					break;
				}
			if (!exists)
				continue;
			if (layer > 0)
				ImGui.SameLine();
			var selected = layer == editorSublayer;
			if (selected)
				ImGui.PushStyleColor(ImGuiCol.Button, ImGui.GetStyle().Colors[(int)ImGuiCol.ButtonActive]);
			if (ImGui.SmallButton(layer == 0 ? "Main" : $"Sub {layer}"))
				SelectEditorSublayer(layer);
			if (selected)
				ImGui.PopStyleColor();
		}
		ImGui.PopStyleVar();
	}

	/// Drawn rather than glyphed: the editor font has no hamburger character.
	bool DrawHamburgerButton()
	{
		var size = new Vector2(EditorMenuButtonSize, EditorMenuButtonSize);
		var origin = ImGui.GetCursorScreenPos();
		var pressed = ImGui.InvisibleButton("##EditorMenuButton", size);
		var hovered = ImGui.IsItemHovered();

		var draw = ImGui.GetWindowDrawList();
		draw.AddRectFilled(origin, origin + size, ImGui.GetColorU32(hovered ? ImGuiCol.ButtonHovered : ImGuiCol.WindowBg), 3f);
		var bar = new Vector2(size.X * 0.56f, MathF.Max(1f, size.Y * 0.08f));
		var left = origin.X + (size.X - bar.X) * 0.5f;
		for (var i = 0; i < 3; i++)
		{
			var y = origin.Y + size.Y * (0.32f + i * 0.18f);
			draw.AddRectFilled(new Vector2(left, y), new Vector2(left + bar.X, y + bar.Y), ImGui.GetColorU32(ImGuiCol.Text));
		}
		return pressed;
	}

	void DrawEditorMenuPopup()
	{
		if (!BeginPaddedPopup(EditorMenuPopup))
			return;

		if (ImGui.MenuItem("Play", "Tab"))
			StartGame();
		ImGui.Separator();
		var commandKey = OperatingSystem.IsMacOS() ? "Cmd" : "Ctrl";
		var undoLabel = undo.UndoName is { } undoName ? $"Undo {undoName}" : "Undo";
		if (ImGui.MenuItem(undoLabel, $"{commandKey}+Z", false, undo.CanUndo))
			UndoEditor();
		var redoLabel = undo.RedoName is { } redoName ? $"Redo {redoName}" : "Redo";
		var redoShortcut = OperatingSystem.IsMacOS() ? "Cmd+Shift+Z" : "Ctrl+Y";
		if (ImGui.MenuItem(redoLabel, redoShortcut, false, undo.CanRedo))
			RedoEditor();
		var canSavePrefab = actorSpecInspectorOpen && selectedActorSpecId is { } specId &&
			ActorSpecs.IsSpecDirty(specId) && ActorSpecs.CanSaveSpec(specId);
		if (ImGui.MenuItem("Save Prefab", $"{commandKey}+S", false, canSavePrefab))
			SaveSelectedActorSpec();
		ImGui.Separator();

		if (ImGui.BeginMenu("Windows"))
		{
			ImGui.MenuItem("Tiles", null, ref tilesPanelOpen);
			ImGui.MenuItem("Actors", null, ref actorsPanelOpen);
			ImGui.MenuItem("Actor List", null, ref actorListPanelOpen);
			ImGui.MenuItem("Room", null, ref roomPanelOpen);
			ImGui.Separator();
			ImGui.MenuItem("Tile Kits", null, ref tileKitEditorOpen);
			ImGui.MenuItem("NCT Tiles", null, ref nctEditorOpen);
			if (ImGui.MenuItem("Particles", null, particleEditor.PinnedWindowOpen))
				particleEditor.TogglePinnedWindow();
			ImGui.MenuItem("Music Regions", null, ref musicRegionsOpen);
#if !BROWSER
			ImGui.MenuItem("SFX", null, ref proceduralSoundEditor.Open);
#endif
			ImGui.MenuItem("Preferences", null, ref preferencesOpen);
			ImGui.Separator();
			ImGui.MenuItem("Dear ImGui Demo", null, ref imguiDemoOpen);
			if (ImGui.MenuItem("Reset Layout"))
				ResetEditorToolWindowLayout();
			ImGui.EndMenu();
		}

		if (ImGui.BeginMenu("World"))
		{
			foreach (var world in Assets.GetWorldIds())
				if (ImGui.MenuItem(world, null, string.Equals(world, Assets.CurrentWorldId, StringComparison.OrdinalIgnoreCase)))
					SwitchWorld(world);
			ImGui.EndMenu();
		}
		ImGui.Separator();

		if (ImGui.MenuItem("Bake World Map"))
			BakeWorldMap();
#if !IOS
		if (ImGui.MenuItem("Open FMOD Studio"))
			FmodStudio.Open();
#endif
		ImGui.Separator();

		if (ImGui.MenuItem("Music", null, musicEnabled))
		{
			musicEnabled = !musicEnabled;
			game?.RefreshMusic();
		}
#if !IOS
		if (ImGui.MenuItem("Always on Top", null, alwaysOnTop))
		{
			alwaysOnTop = !alwaysOnTop;
			RequiredWindow.AlwaysOnTop = alwaysOnTop;
		}
#endif

		EndPaddedPopup();
	}

	/// Room identity, moved out of the toolbar: the name (double-click or Rename to edit),
	/// plus deleting the room. Drawn at the top of the Room panel.
	void DrawRoomIdentitySection(Room room)
	{
		ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(6, 3));
		if (editingRoomName && roomNameEditingRoomId == room.Id)
		{
			ImGui.SetNextItemWidth(-1);
			if (focusRoomNameEditor)
			{
				ImGui.SetKeyboardFocusHere();
				focusRoomNameEditor = false;
			}
			var commit = ImGui.InputText("##RoomNameEditor", ref roomNameEditor, 128, ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.AutoSelectAll);
			if (ImGui.IsKeyPressed(ImGuiKey.Escape))
				CancelRoomRename();
			else if (commit || ImGui.IsItemDeactivated())
				CommitRoomRename(room);
		}
		else
		{
			ImGui.TextUnformatted(room.Id);
			if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
				BeginRoomRename(room);
			if (ImGui.SmallButton("Rename"))
				BeginRoomRename(room);
			ImGui.SameLine();
			if (ImGui.SmallButton("Delete"))
			{
				pendingDeleteRoomId = room.Id;
				ImGui.OpenPopup("Delete Room?");
			}
		}
		DrawDeleteRoomPopup();
		ImGui.PopStyleVar();
		ImGui.Separator();
	}
}
#endif
