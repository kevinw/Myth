using ImGuiNET;
using System.Numerics;
using Foster.Framework;
namespace Myth;

public partial class Manager
{
	float renderTargetPreviewWidth = 200f;

	// Grid of every render target (toggled in-game with G), drawn at full window
	// resolution. Hover a tile for its name, color format, and resolution.
	private void RenderTargetOverlayImGui()
	{
		if (game is not { ShowRenderTargetOverlay: true } g || imgui == null)
			return;

		var targets = g.DebugTargets;
		if (targets.Count == 0)
			return;

		ImGui.SetNextWindowSize(new Vector2(760, 560), ImGuiCond.FirstUseEver);
		var open = true;
		if (ImGui.Begin("Render Targets", ref open, ImGuiWindowFlags.NoSavedSettings))
		{
			const float Spacing = 8f;
			if (ImGui.Button("-", new Vector2(24, 0)))
				renderTargetPreviewWidth = Math.Max(80f, renderTargetPreviewWidth - 40f);
			ImGui.SameLine();
			if (ImGui.Button("+", new Vector2(24, 0)))
				renderTargetPreviewWidth = Math.Min(640f, renderTargetPreviewWidth + 40f);
			ImGui.SameLine();
			ImGui.TextUnformatted($"{renderTargetPreviewWidth:0}px");
			ImGui.Separator();

			var avail = ImGui.GetContentRegionAvail().X;
			var columns = Math.Max(1, (int)((avail + Spacing) / (renderTargetPreviewWidth + Spacing)));

			var visible = 0;
			for (var i = 0; i < targets.Count; i++)
			{
				var target = targets[i];
				if (target.Name == "GameScreen")
					continue;

				var aspect = target.Height > 0 ? target.Width / (float)target.Height : 1f;
				var cellHeight = aspect > 0 ? renderTargetPreviewWidth / aspect : renderTargetPreviewWidth;

				ImGui.BeginGroup();
				imgui.Image(new Subtexture(target), new Vector2(renderTargetPreviewWidth, cellHeight));
				if (ImGui.IsItemHovered())
					ImGui.SetTooltip(RenderTargetInfo(target));
				ImGui.TextUnformatted(target.Name);
				ImGui.EndGroup();

				visible++;
				if (visible % columns != 0)
					ImGui.SameLine(0, Spacing);
			}
		}
		ImGui.End();

		if (!open)
			g.ShowRenderTargetOverlay = false;
	}

	private static string RenderTargetInfo(Target target)
	{
		var formats = "";
		for (var i = 0; i < target.Attachments.Length; i++)
			formats += (i > 0 ? ", " : "") + target.Attachments[i].Format;
		return $"{target.Name}\n{formats}\n{target.Width}×{target.Height}";
	}

}
