#if EDITOR
using ImGuiNET;

namespace Foster.Editor;

public static class EditorGui
{
	public static bool DrawEnumCombo(Type type, ref string value)
	{
		if (type.IsDefined(typeof(FlagsAttribute), inherit: false))
			return DrawFlagsEnumCombo(type, ref value);

		var changed = false;
		if (ImGui.BeginCombo("##Value", value))
		{
			foreach (var name in Enum.GetNames(type))
			{
				var selected = string.Equals(value, name, StringComparison.OrdinalIgnoreCase);
				if (ImGui.Selectable(name, selected))
				{
					value = name;
					changed = true;
				}
				if (selected)
					ImGui.SetItemDefaultFocus();
			}
			ImGui.EndCombo();
		}
		return changed;
	}

	public static bool CheckboxFlags<T>(ReadOnlySpan<char> label, ref T flags, T flag) where T : Enum
	{
		var intFlags = (int)(object)flags;
		var changed = ImGui.CheckboxFlags(label, ref intFlags, (int)(object)flag);
		if (changed)
			flags = (T)Enum.ToObject(typeof(T), intFlags);
		return changed;
	}

	static bool DrawFlagsEnumCombo(Type type, ref string value)
	{
		var flags = Convert.ToInt64(Enum.Parse(type, value, ignoreCase: true));
		var changed = false;
		if (ImGui.BeginCombo("##Value", value))
		{
			foreach (var flag in Enum.GetValues(type))
			{
				var bits = Convert.ToInt64(flag);
				if (bits == 0 || (bits & (bits - 1)) != 0)
					continue;

				var selected = (flags & bits) == bits;
				if (ImGui.Checkbox(Enum.GetName(type, flag)!, ref selected))
				{
					flags = selected ? flags | bits : flags & ~bits;
					value = Enum.ToObject(type, flags).ToString()!;
					changed = true;
				}
			}
			ImGui.EndCombo();
		}
		return changed;
	}
}
#endif
