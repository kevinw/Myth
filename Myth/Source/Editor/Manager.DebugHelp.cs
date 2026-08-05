#if EDITOR
using Foster.Framework;
using ImGuiNET;

namespace Myth;

public partial class Manager
{
	// Cheat sheet for the debug/dev shortcuts, toggled with '?'. Hand-maintained: when a
	// shortcut is added or changed, update the matching section here.
	bool debugHelpOpen;

	static readonly (string Keys, string Action)[] DebugHelpAnywhere =
	[
		("Tab", "Toggle editor / play"),
		("~", "Command console"),
		("T", "Tweaks panel"),
		("F5", "FPS overlay"),
		("F4", "Fullscreen"),
		("B", "Toggle Bud AI"),
		("M / Shift+M", "Movie recording"),
		("?", "This window"),
	];

	static readonly (string Keys, string Action)[] DebugHelpPlaying =
	[
		("R", "Reload room"),
		("C", "Collision rects view"),
		("N", "Normal buffer view"),
		("G", "Render-target overlay"),
		("Shift+G", "God mode"),
		("P", "Platform-nav view"),
		("J", "Atlas wander"),
		("L", "Center camera on player"),
		("U / Shift+U", "Perf toggles: reveal / hide one"),
		("V", "Bud voice test line"),
		("- / =", "Time scale down / up (also LB / RB)"),
		("Enter", "Advance gym (gym loop)"),
		("Left drag", "Drag the player"),
		("Click / drag", "Select / move live-editable actor"),
		("Handles", "Resize live-editable actor"),
		("Shift (dragging)", "Toggle position snap"),
		("Esc", "Cancel live drag / deselect"),
		("Ctrl+Z / Ctrl+Y", "Undo / redo live actor edit"),
	];

	static readonly (string Keys, string Action)[] DebugHelpEditing =
	[
		("Move keys", "Jump to adjacent room"),
		("Shift+Move", "Pan camera"),
		("- / =", "Editor UI scale"),
		("Cmd+Z / Shift+Z / Y", "Undo / redo"),
		("Cmd+C / X / V / D", "Copy / cut / paste / duplicate actor (room while room-editing)"),
		("Delete", "Delete selected actor"),
		("Esc", "Actor select tool"),
		("P", "Paint room color field"),
		("B", "Toggle breakable on hovered tile"),
		("C", "Toggle crumble on hovered tile"),
		("G", "Toggle fungal on hovered tile"),
		("Shift+G", "Show fungal markers"),
		("Cmd+S", "Save selected actor spec"),
		("Cmd+,", "Preferences (macOS)"),
	];

	void UpdateDebugHelpToggle()
	{
		if (imgui is { WantsTextInput: false } && Input.Keyboard.Shift && Input.Keyboard.Pressed(Keys.Slash))
			debugHelpOpen = !debugHelpOpen;
	}

	void DrawDebugHelp()
	{
		if (!debugHelpOpen)
			return;

		ImGui.SetNextWindowSize(new System.Numerics.Vector2(440, 560), ImGuiCond.FirstUseEver);
		if (ImGui.Begin("Debug Controls", ref debugHelpOpen))
		{
			DebugHelpSection("Anywhere", DebugHelpAnywhere);
			DebugHelpSection("While Playing", DebugHelpPlaying);
			DebugHelpSection("While Editing", DebugHelpEditing);
		}
		ImGui.End();
	}

	static void DebugHelpSection(string title, (string Keys, string Action)[] rows)
	{
		ImGui.SeparatorText(title);
		if (!ImGui.BeginTable(title, 2, ImGuiTableFlags.SizingFixedFit))
			return;
		foreach (var (keys, action) in rows)
		{
			ImGui.TableNextRow();
			ImGui.TableNextColumn();
			ImGui.TextDisabled(keys);
			ImGui.TableNextColumn();
			ImGui.TextUnformatted(action);
		}
		ImGui.EndTable();
	}
}
#endif
