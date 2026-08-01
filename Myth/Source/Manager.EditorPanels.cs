#if EDITOR
using System.Numerics;
using ImGuiNET;

namespace Myth;

// The palette split into separate dockable panels. It used to be one window stacking tiles,
// actors, the actor list and the room/lighting settings into a single ~3000px scroll, so
// reaching room settings meant scrolling past sixty actor icons. Each section is now its own
// window: they tab together in the left dock region by default, and any of them can be torn
// off, moved to another region, or closed.
public partial class Manager
{
	bool tilesPanelOpen = true;
	bool actorsPanelOpen = true;
	bool actorListPanelOpen = true;
	bool roomPanelOpen = true;
	string actorPaletteSearch = "";

	void PaletteImGui()
	{
		EditorPanelImGui("Tiles", ref tilesPanelOpen, () =>
		{
			if (ImGuiSubtextureButton("##Select", Assets.GetSubtexture("buttons/16"), null, actorSelectTool, ButtonSizeV, true))
				SelectEditorActorTool();
			ImGui.Spacing();
			DrawTilePaletteSection();
		});

		EditorPanelImGui("Actors", ref actorsPanelOpen, DrawActorPaletteSection);
		EditorPanelImGui("Actor List", ref actorListPanelOpen, DrawActorListSection);
		EditorPanelImGui("Room", ref roomPanelOpen, () =>
		{
			DrawRoomPaletteSection();
			DrawAmbientOcclusionSection();
			DrawSoftShadowSection();
		});

		// These rode along at the end of the old single palette window and are not panels
		// themselves: the two inspectors plus the floating tile/selection option popups.
		ActorSpecInspectorImGui();
		ActorInstanceInspectorImGui();
		DrawEditorSelectionOptions();
		DrawSlopePaintOption();
	}

	/// One palette panel: the shared style the old single window used, plus the click-empty-
	/// space-to-pick-the-select-tool behaviour it had.
	void EditorPanelImGui(string name, ref bool open, Action body)
	{
		if (!open)
			return;

		DockNextWindow(name, editorPaletteDockId);
		// FramePadding is deliberately NOT pushed around Begin: a docked window reserves
		// GetFrameHeight() for its tab bar, so zeroing it here makes the window reserve less
		// than the host actually draws and the content spills up over the tabs. It is pushed
		// inside the window instead, where the icon grids still get their tight buttons.
		ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(BoxPadding, BoxPadding));
		ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(ButtonSpacing, ButtonSpacing));
		ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, LineWeight);
		ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.0f, 0.0f, 0.0f, 1.0f));

		// Reported whether or not the body draws: a window tabbed behind another is still
		// docked, and the region's visibility must not depend on which tab is active.
		var visible = ImGui.Begin(name, ref open, ImGuiWindowFlags.NoCollapse);
		NoteDockedWindow();
		if (visible)
		{
			ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, Vector2.Zero);
			body();
			ImGui.PopStyleVar();
			if (ImGui.IsWindowHovered() && !ImGui.IsAnyItemHovered() && Input.Mouse.LeftPressed)
				SelectEditorActorTool();
		}
		ImGui.End();

		ImGui.PopStyleColor();
		ImGui.PopStyleVar(3);
	}

	/// Type-to-filter over the actor grid. Sixty-odd icons, many of them near-identical
	/// placeholders, are not findable by eye; matching runs on the same name the icon's
	/// tooltip shows.
	bool ActorPaletteSearchMatches(Factory.ActorPaletteEntry entry)
		=> actorPaletteSearch.Length == 0 ||
			(entry.Name?.Contains(actorPaletteSearch, StringComparison.OrdinalIgnoreCase) ?? false);

	void DrawActorPaletteSearch()
	{
		ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(6, 3));
		ImGui.SetNextItemWidth(-1);
		ImGui.InputTextWithHint("##ActorSearch", "search actors", ref actorPaletteSearch, 64);
		ImGui.PopStyleVar();
	}

	/// Which panels exist, for the layout reset to re-home them all.
	void OpenAllEditorPanels()
	{
		tilesPanelOpen = true;
		actorsPanelOpen = true;
		actorListPanelOpen = true;
		roomPanelOpen = true;
	}
}
#endif
