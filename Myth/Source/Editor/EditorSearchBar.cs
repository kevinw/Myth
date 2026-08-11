#if EDITOR
using System.Numerics;
using ImGuiNET;

namespace Myth;

static class EditorSearchBar
{
	public static bool Draw(string id, ref string text, uint maxLength, string hint, float width = -1)
	{
		ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(6, 3));
		ImGui.SetNextItemWidth(width);
		var changed = ImGui.InputTextWithHint(id, EditorIcons.Search + "  " + hint, ref text, maxLength, ImGuiInputTextFlags.AutoSelectAll);
		ImGui.PopStyleVar();
		return changed;
	}
}
#endif
