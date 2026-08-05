#if EDITOR
using Foster.Framework;
using ImGuiNET;

namespace Myth;

public partial class Manager
{
	// Cheat sheet for the debug/dev shortcuts, toggled with '?'. Rows come straight from
	// DebugControls, so a binding added there shows up here automatically.
	bool debugHelpOpen;

	void UpdateDebugHelpToggle()
	{
		if (imgui is { WantsTextInput: false } && Input.Keyboard.Shift && debugControls.Help.Pressed)
			debugHelpOpen = !debugHelpOpen;
	}

	void DrawDebugHelp()
	{
		if (!debugHelpOpen)
			return;

		ImGui.SetNextWindowSize(new System.Numerics.Vector2(440, 560), ImGuiCond.FirstUseEver);
		if (ImGui.Begin("Debug Controls", ref debugHelpOpen))
		{
			DebugHelpSection("Anywhere", DebugControls.Context.Anywhere);
			DebugHelpSection("While Playing", DebugControls.Context.Playing);
			DebugHelpSection("While Editing", DebugControls.Context.Editing);
		}
		ImGui.End();
	}

	void DebugHelpSection(string title, DebugControls.Context context)
	{
		ImGui.SeparatorText(title);
		if (!ImGui.BeginTable(title, 2, ImGuiTableFlags.SizingFixedFit))
			return;
		foreach (var entry in debugControls.Entries)
		{
			if (entry.Where != context)
				continue;
			ImGui.TableNextRow();
			ImGui.TableNextColumn();
			ImGui.TextDisabled(entry.Keys);
			ImGui.TableNextColumn();
			ImGui.TextUnformatted(entry.Description);
		}
		ImGui.EndTable();
	}
}
#endif
