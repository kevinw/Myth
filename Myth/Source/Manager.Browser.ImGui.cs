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
	bool browserImguiShowDemo = true;

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

		DrawDebugImGuiTriangle();

		if (ImGui.Begin("Debug"))
		{
			// igText is variadic (traps the wasm P/Invoke interp); use TextUnformatted.
			ImGui.TextUnformatted($"{Time.Delta * 1000f:0.0} ms  ({1f / MathF.Max(Time.Delta, 1e-6f):0} fps)");
			ImGui.Checkbox("ImGui Demo", ref browserImguiShowDemo);
		}
		ImGui.End();
		if (browserImguiShowDemo)
			ImGui.ShowDemoWindow(ref browserImguiShowDemo);
		gui.EndLayout();
	}

	void DrawDebugImGuiTriangle()
	{
		var drawList = ImGui.GetForegroundDrawList();
		var io = ImGui.GetIO();
		var center = io.DisplaySize * 0.5f;
		drawList.AddTriangleFilled(
			center + new Vector2(0, -150),
			center + new Vector2(150, 120),
			center + new Vector2(-150, 120),
			0xff0000ff);
	}

	void RenderBrowserImGui()
	{
		if (browserImguiOpen)
			imgui?.Render();
	}
}
#endif
