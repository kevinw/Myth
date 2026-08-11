#if EDITOR
using System.Numerics;
using ImGuiNET;

namespace Myth;

public partial class Manager
{
	enum EditorTheme
	{
		Default,
		Mycelium,
		Gruvbox,
		CatppuccinMocha,
		Synthwave,
		SolarizedLight
	}

	readonly record struct EditorThemePalette(
		uint Text,
		uint TextDisabled,
		uint Window,
		uint Surface,
		uint SurfaceHovered,
		uint SurfaceActive,
		uint Border,
		uint Accent,
		uint AccentHovered,
		uint AccentActive,
		uint Secondary);

	EditorTheme editorTheme = EditorTheme.Mycelium;
	EditorTheme? appliedEditorTheme;

	void EnsureEditorTheme()
	{
		if (appliedEditorTheme == editorTheme)
			return;
		ApplyEditorTheme(editorTheme);
		appliedEditorTheme = editorTheme;
	}

	void SelectEditorTheme(EditorTheme theme)
	{
		editorTheme = theme;
		ApplyEditorTheme(theme);
		appliedEditorTheme = theme;
		SaveWindowSettings();
	}

	static void ApplyEditorTheme(EditorTheme theme)
	{
		ImGui.StyleColorsDark();
		ImGui.GetStyle().WindowRounding = 4;
		if (theme == EditorTheme.Default)
			return;

		// Gruvbox, Catppuccin and Solarized use their published palettes; Mycelium and
		// Synthwave are editor-specific palettes built for this switcher.
		var palette = theme switch
		{
			EditorTheme.Mycelium => new EditorThemePalette(
				0xdce8df, 0x7f9987, 0x101713, 0x1c2a22, 0x294033, 0x355a45,
				0x30483a, 0x61b67a, 0x89d49d, 0xa7e3b5, 0xd6b36a),
			EditorTheme.Gruvbox => new EditorThemePalette(
				0xebdbb2, 0x928374, 0x1d2021, 0x3c3836, 0x504945, 0x665c54,
				0x665c54, 0xd65d0e, 0xfe8019, 0xfabd2f, 0x98971a),
			EditorTheme.CatppuccinMocha => new EditorThemePalette(
				0xcdd6f4, 0x7f849c, 0x181825, 0x313244, 0x45475a, 0x585b70,
				0x45475a, 0xcba6f7, 0xb4befe, 0xf5c2e7, 0x94e2d5),
			EditorTheme.Synthwave => new EditorThemePalette(
				0xf6e9ff, 0xa58bb8, 0x100b1f, 0x21153d, 0x382060, 0x5b2a86,
				0x5b327a, 0xff4fd8, 0x38e8ff, 0xffee55, 0x43ff9a),
			EditorTheme.SolarizedLight => new EditorThemePalette(
				0x586e75, 0x93a1a1, 0xfdf6e3, 0xeee8d5, 0xdad3c1, 0xc9c2b0,
				0xc7c0ad, 0x268bd2, 0x2aa198, 0x859900, 0xb58900),
			_ => default
		};

		var colors = ImGui.GetStyle().Colors;
		colors[(int)ImGuiCol.Text] = ThemeColor(palette.Text);
		colors[(int)ImGuiCol.TextDisabled] = ThemeColor(palette.TextDisabled);
		colors[(int)ImGuiCol.WindowBg] = ThemeColor(palette.Window);
		colors[(int)ImGuiCol.ChildBg] = ThemeColor(palette.Window);
		colors[(int)ImGuiCol.PopupBg] = ThemeColor(palette.Window);
		colors[(int)ImGuiCol.Border] = ThemeColor(palette.Border);
		colors[(int)ImGuiCol.BorderShadow] = Vector4.Zero;
		colors[(int)ImGuiCol.FrameBg] = ThemeColor(palette.Surface);
		colors[(int)ImGuiCol.FrameBgHovered] = ThemeColor(palette.SurfaceHovered);
		colors[(int)ImGuiCol.FrameBgActive] = ThemeColor(palette.SurfaceActive);
		colors[(int)ImGuiCol.TitleBg] = ThemeColor(palette.Window);
		colors[(int)ImGuiCol.TitleBgActive] = ThemeColor(palette.Surface);
		colors[(int)ImGuiCol.TitleBgCollapsed] = ThemeColor(palette.Window, 0.8f);
		colors[(int)ImGuiCol.MenuBarBg] = ThemeColor(palette.Surface);
		colors[(int)ImGuiCol.ScrollbarBg] = ThemeColor(palette.Window, 0.7f);
		colors[(int)ImGuiCol.ScrollbarGrab] = ThemeColor(palette.Surface);
		colors[(int)ImGuiCol.ScrollbarGrabHovered] = ThemeColor(palette.SurfaceHovered);
		colors[(int)ImGuiCol.ScrollbarGrabActive] = ThemeColor(palette.SurfaceActive);
		colors[(int)ImGuiCol.CheckMark] = ThemeColor(palette.AccentHovered);
		colors[(int)ImGuiCol.SliderGrab] = ThemeColor(palette.Accent, 0.75f);
		colors[(int)ImGuiCol.SliderGrabActive] = ThemeColor(palette.AccentActive);
		colors[(int)ImGuiCol.Button] = ThemeColor(palette.Surface);
		colors[(int)ImGuiCol.ButtonHovered] = ThemeColor(palette.SurfaceHovered);
		colors[(int)ImGuiCol.ButtonActive] = ThemeColor(palette.SurfaceActive);
		colors[(int)ImGuiCol.Header] = ThemeColor(palette.Accent, 0.42f);
		colors[(int)ImGuiCol.HeaderHovered] = ThemeColor(palette.AccentHovered, 0.7f);
		colors[(int)ImGuiCol.HeaderActive] = ThemeColor(palette.AccentActive, 0.85f);
		colors[(int)ImGuiCol.Separator] = ThemeColor(palette.Border);
		colors[(int)ImGuiCol.SeparatorHovered] = ThemeColor(palette.AccentHovered);
		colors[(int)ImGuiCol.SeparatorActive] = ThemeColor(palette.AccentActive);
		colors[(int)ImGuiCol.ResizeGrip] = ThemeColor(palette.Accent, 0.25f);
		colors[(int)ImGuiCol.ResizeGripHovered] = ThemeColor(palette.AccentHovered, 0.7f);
		colors[(int)ImGuiCol.ResizeGripActive] = ThemeColor(palette.AccentActive);
		colors[(int)ImGuiCol.Tab] = ThemeColor(palette.Surface);
		colors[(int)ImGuiCol.TabHovered] = ThemeColor(palette.AccentHovered, 0.7f);
		colors[(int)ImGuiCol.TabSelected] = ThemeColor(palette.SurfaceActive);
		colors[(int)ImGuiCol.TabSelectedOverline] = ThemeColor(palette.AccentHovered);
		colors[(int)ImGuiCol.TabDimmed] = ThemeColor(palette.Window);
		colors[(int)ImGuiCol.TabDimmedSelected] = ThemeColor(palette.Surface);
		colors[(int)ImGuiCol.TabDimmedSelectedOverline] = ThemeColor(palette.Accent, 0.6f);
		colors[(int)ImGuiCol.DockingPreview] = ThemeColor(palette.AccentHovered, 0.7f);
		colors[(int)ImGuiCol.DockingEmptyBg] = ThemeColor(palette.Window);
		colors[(int)ImGuiCol.PlotLines] = ThemeColor(palette.Secondary);
		colors[(int)ImGuiCol.PlotLinesHovered] = ThemeColor(palette.AccentActive);
		colors[(int)ImGuiCol.PlotHistogram] = ThemeColor(palette.Secondary);
		colors[(int)ImGuiCol.PlotHistogramHovered] = ThemeColor(palette.AccentActive);
		colors[(int)ImGuiCol.TableHeaderBg] = ThemeColor(palette.Surface);
		colors[(int)ImGuiCol.TableBorderStrong] = ThemeColor(palette.Border);
		colors[(int)ImGuiCol.TableBorderLight] = ThemeColor(palette.Border, 0.6f);
		colors[(int)ImGuiCol.TableRowBg] = ThemeColor(palette.Window);
		colors[(int)ImGuiCol.TableRowBgAlt] = ThemeColor(palette.Surface, 0.5f);
		colors[(int)ImGuiCol.TextSelectedBg] = ThemeColor(palette.Accent, 0.4f);
		colors[(int)ImGuiCol.DragDropTarget] = ThemeColor(palette.AccentActive);
		colors[(int)ImGuiCol.NavCursor] = ThemeColor(palette.AccentHovered);
		colors[(int)ImGuiCol.NavWindowingHighlight] = ThemeColor(palette.Text, 0.7f);
		colors[(int)ImGuiCol.NavWindowingDimBg] = ThemeColor(palette.Window, 0.7f);
		colors[(int)ImGuiCol.ModalWindowDimBg] = new Vector4(0, 0, 0, 0.55f);
	}

	static Vector4 ThemeColor(uint rgb, float alpha = 1)
		=> new(
			((rgb >> 16) & 0xff) / 255f,
			((rgb >> 8) & 0xff) / 255f,
			(rgb & 0xff) / 255f,
			alpha);

	static string EditorThemeName(EditorTheme theme) => theme switch
	{
		EditorTheme.Mycelium => "Mycelium",
		EditorTheme.Gruvbox => "Gruvbox",
		EditorTheme.CatppuccinMocha => "Catppuccin Mocha",
		EditorTheme.Synthwave => "Synthwave",
		EditorTheme.SolarizedLight => "Solarized Light",
		_ => "Default"
	};
}
#endif
