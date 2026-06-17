#if BROWSER && IMGUI
using System.Numerics;
using Foster.Framework;
using ImGuiNET;

namespace Myth;

// Minimal Dear ImGui overlay for the browser build. The full editor is desktop-only;
// this exists to drive (and verify) the cimgui wasm link and to give web builds a
// lightweight debug overlay. Toggle with the tilde key.
public partial class Manager
{
	// Open with the demo window up by default so the imgui-on-web integration is visible.
	bool browserImguiOpen = true;
	bool browserImguiShowDemo = false;
	static float BrowserToolbarButtonSize => 24;
	static float BrowserToolbarPadding => 4;

	void InitBrowserImGui()
	{
	}

	void UpdateBrowserImGui()
	{
		if (imgui is not { } gui) {
			Console.WriteLine("browserImgui is null");
			return;
		}

		if (Input.Keyboard.Pressed(Keys.Tilde))
			browserImguiOpen = !browserImguiOpen;

		if (!browserImguiOpen)
			return;

		gui.BeginLayout();

		DrawBrowserToolbar(gui);

		/*
		if (ImGui.Begin("Debug"))
		{
			// igText is variadic (traps the wasm P/Invoke interp); use TextUnformatted.
			ImGui.TextUnformatted($"{Time.Delta * 1000f:0.0} ms  ({1f / MathF.Max(Time.Delta, 1e-6f):0} fps)");
			ImGui.Checkbox("ImGui Demo", ref browserImguiShowDemo);
		}
		ImGui.End();
		*/

		if (browserImguiShowDemo)
			ImGui.ShowDemoWindow(ref browserImguiShowDemo);
		gui.EndLayout();
	}

	void DrawBrowserToolbar(ImGuiRenderer gui)
	{
		//
		// disabled for now, toggling music in browser causes a crash.
		//

		/*
		var window = RequiredWindow;
		var size = new Vector2(BrowserToolbarButtonSize + BrowserToolbarPadding * 2);
		var pos = new Vector2(window.WidthInPixels / gui.Scale - size.X - BrowserToolbarPadding, BrowserToolbarPadding);
		var flags =
			ImGuiWindowFlags.NoDecoration |
			ImGuiWindowFlags.NoMove |
			ImGuiWindowFlags.NoSavedSettings |
			ImGuiWindowFlags.NoScrollbar |
			ImGuiWindowFlags.NoScrollWithMouse |
			ImGuiWindowFlags.NoBringToFrontOnFocus;

		ImGui.SetNextWindowPos(pos, ImGuiCond.Always);
		ImGui.SetNextWindowSize(size, ImGuiCond.Always);
		ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(BrowserToolbarPadding));
		ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, Vector2.Zero);
		ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, Vector2.Zero);
		ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0, 0, 0, 0));

		ImGui.Begin("BrowserToolbar", flags);
		ImGui.BeginDisabled();
		var icon = Assets.GetSubtexture("buttons/10");
		gui.ImageButton("BrowserMusic", icon, new Vector2(BrowserToolbarButtonSize), false, imagePadding: new Vector2(3));
		ImGui.EndDisabled();
		if (ImGui.IsItemHovered())
			ImGui.SetTooltip("Browser music toggle is disabled for now");
		ImGui.End();

		ImGui.PopStyleColor();
		ImGui.PopStyleVar(3);
		*/
	}

	void RenderBrowserImGui()
	{
		if (browserImguiOpen)
			imgui?.Render();
	}
}
#endif
