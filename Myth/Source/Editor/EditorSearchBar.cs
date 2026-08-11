#if EDITOR
using System.Collections.Generic;
using System.Numerics;
using ImGuiNET;

namespace Myth;

static class EditorSearchBar
{
	static readonly Dictionary<string, int> generations = [];
	static readonly HashSet<string> focusRequests = [];
	static readonly HashSet<string> focused = [];

	public static bool Draw(string id, ref string text, uint maxLength, string hint, float width = -1)
	{
		var style = ImGui.GetStyle();
		var totalWidth = width > 0 ? width : ImGui.GetContentRegionAvail().X;
		if (!generations.TryGetValue(id, out var generation))
			generation = 0;

		var focusRequested = focusRequests.Remove(id);
		var showHint = !focusRequested && !focused.Contains(id);
		ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(6, 3));
		ImGui.SetNextItemWidth(totalWidth);
		ImGui.PushID(id);
		ImGui.PushID(generation);
		if (focusRequested)
			ImGui.SetKeyboardFocusHere();
		var changed = ImGui.InputTextWithHint("##SearchInput", showHint ? EditorIcons.Search + "  " + hint : "", ref text, maxLength, ImGuiInputTextFlags.AutoSelectAll);
		var isFocused = ImGui.IsItemActive();
		ImGui.PopID();
		ImGui.PopID();
		ImGui.PopStyleVar();
		if (isFocused)
			focused.Add(id);
		else
			focused.Remove(id);

		if (text.Length > 0)
		{
			var min = ImGui.GetItemRectMin();
			var max = ImGui.GetItemRectMax();
			var clearSize = new Vector2(ImGui.GetFrameHeight(), max.Y - min.Y);
			var clearMin = new Vector2(max.X - clearSize.X, min.Y);
			var clearMax = clearMin + clearSize;
			var hovered = ImGui.IsMouseHoveringRect(clearMin, clearMax);
			if (hovered)
			{
				ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
				if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
				{
					text = "";
					generations[id] = generation + 1;
					focusRequests.Add(id);
					changed = true;
				}
			}
			if (hovered)
				ImGui.SetTooltip("Clear");

			var textColor = style.Colors[(int)ImGuiCol.Text];
			if (!hovered)
				textColor *= 0.65f;
			var textSize = ImGui.CalcTextSize(EditorIcons.Close);
			ImGui.GetWindowDrawList().AddText(
				clearMin + (clearSize - textSize) * 0.5f,
				ImGui.GetColorU32(textColor),
				EditorIcons.Close);
		}
		return changed;
	}
}
#endif
