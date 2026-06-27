namespace Myth;
using ImGuiNET;

public static class ImGuiExt {
	public static bool CheckboxFlags<T>(ReadOnlySpan<char> label, ref T flags, T flag) where T : Enum
	{
		int int_flags = (int)(object)flags;
		int int_flag = (int)(object)flag;
		var change = ImGui.CheckboxFlags(label, ref int_flags, int_flag);
		if (change)
		{
			flags = (T)Enum.ToObject(typeof(T), int_flags);
		}
		return change;
	}
}
