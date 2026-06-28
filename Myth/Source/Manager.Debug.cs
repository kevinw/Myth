using ImGuiNET;
using System.Numerics;
using Foster.Framework;
namespace Myth;

public partial class Manager
{
	float renderTargetPreviewWidth = 200f;
	float atlasZoom = 1f;
	bool atlasFit = true;
	int selectedAtlas;

	// Grid of every render target (toggled in-game with G), drawn at full window
	// resolution. Hover a tile for its name, color format, and resolution.
	private void RenderTargetOverlayImGui()
	{
		if (game is not { ShowRenderTargetOverlay: true } g || imgui == null)
			return;

		ImGui.SetNextWindowSize(new Vector2(760, 560), ImGuiCond.FirstUseEver);
		var open = true;
		if (ImGui.Begin("Graphics", ref open, ImGuiWindowFlags.NoSavedSettings) && ImGui.BeginTabBar("GraphicsTabs"))
		{
			if (ImGui.BeginTabItem("Render Targets"))
			{
				DrawRenderTargetsTab(g.DebugTargets);
				ImGui.EndTabItem();
			}
			if (ImGui.BeginTabItem("Atlases"))
			{
				DrawAtlasesTab();
				ImGui.EndTabItem();
			}
			ImGui.EndTabBar();
		}
		ImGui.End();

		if (!open)
			g.ShowRenderTargetOverlay = false;
	}

	void DrawRenderTargetsTab(IReadOnlyList<Target> targets)
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
			var renderTargetSubtexture = new Subtexture(target);
#if BROWSER
			const bool flipY = true;
#else
			const bool flipY = false;
#endif
			ImGui.BeginGroup();
			imgui!.Image(renderTargetSubtexture, new Vector2(renderTargetPreviewWidth, cellHeight), flipY: flipY);
#if !BROWSER
			if (ImGui.IsItemHovered())
				ImGui.SetTooltip(RenderTargetInfo(target));
#endif
			ImGui.TextUnformatted(target.Name);
			ImGui.EndGroup();

			visible++;
			if (visible % columns != 0)
				ImGui.SameLine(0, Spacing);
		}
	}

	void DrawAtlasesTab()
	{
		var currentName = selectedAtlas == 1 ? "Sprite SDF Atlas" : "Main Atlas";
		ImGui.SetNextItemWidth(180);
		if (ImGui.BeginCombo("##Atlas", currentName))
		{
			if (ImGui.Selectable("Main Atlas", selectedAtlas == 0))
				selectedAtlas = 0;
			if (ImGui.Selectable("Sprite SDF Atlas", selectedAtlas == 1))
				selectedAtlas = 1;
			ImGui.EndCombo();
		}

		var atlas = selectedAtlas == 1 ? SpriteSdf.Atlas : Assets.Atlas;
		if (atlas == null)
		{
			ImGui.SameLine();
			ImGui.TextDisabled("Not ready");
			return;
		}

		ImGui.SameLine();
		if (ImGui.Button("Fit"))
			atlasFit = true;
		ImGui.SameLine();
		if (ImGui.Button("1:1"))
		{
			atlasFit = false;
			atlasZoom = 1;
		}
		ImGui.SameLine();
		if (ImGui.Button("-", new Vector2(24, 0)))
		{
			atlasFit = false;
			atlasZoom = Math.Max(0.0625f, atlasZoom * 0.5f);
		}
		ImGui.SameLine();
		if (ImGui.Button("+", new Vector2(24, 0)))
		{
			atlasFit = false;
			atlasZoom = Math.Min(16, atlasZoom * 2);
		}
		ImGui.SameLine();
		ImGui.TextUnformatted(atlasFit ? "Fit" : $"{atlasZoom * 100:0}%");
		ImGui.TextUnformatted(AtlasInfo(atlas));
		ImGui.Separator();

		var canvasSize = ImGui.GetContentRegionAvail();
		if (!ImGui.BeginChild("AtlasCanvas", Vector2.Zero, ImGuiChildFlags.Borders, ImGuiWindowFlags.HorizontalScrollbar))
		{
			ImGui.EndChild();
			return;
		}

		var canvas = ImGui.GetContentRegionAvail();
		var fit = MathF.Min(canvas.X / atlas.Width, canvas.Y / atlas.Height);
		var scale = atlasFit ? Math.Max(0.01f, fit) : atlasZoom;
		var imageSize = new Vector2(atlas.Width, atlas.Height) * scale;
		imgui!.Image(new Subtexture(atlas), imageSize);
		if (selectedAtlas == 0 && ImGui.IsItemHovered())
			DrawAtlasHover(atlas, scale);

		ImGui.EndChild();
	}

	static void DrawAtlasHover(Texture atlas, float scale)
	{
		var imageMin = ImGui.GetItemRectMin();
		var imageMax = ImGui.GetItemRectMax();
		var mouse = ImGui.GetMousePos();
		var pixel = (mouse - imageMin) / scale;
		Rect? hovered = null;
		var matches = 0;

		ImGui.BeginTooltip();
		ImGui.TextUnformatted($"{(int)pixel.X}, {(int)pixel.Y}");
		ImGui.Separator();
		foreach (var (name, subtexture) in Assets.Subtextures)
		{
			if (subtexture.Texture != atlas || pixel.X < subtexture.Source.Left || pixel.X >= subtexture.Source.Right || pixel.Y < subtexture.Source.Top || pixel.Y >= subtexture.Source.Bottom)
				continue;
			hovered ??= subtexture.Source;
			if (matches++ < 20)
				ImGui.TextUnformatted(name);
		}
		if (matches == 0)
			ImGui.TextDisabled("No packed subtexture");
		else if (matches > 20)
			ImGui.TextDisabled($"+{matches - 20} more");
		ImGui.EndTooltip();

		if (hovered is not { } source)
			return;
		var min = imageMin + source.TopLeft * scale;
		var max = imageMin + source.BottomRight * scale;
		var clippedMin = Vector2.Max(min, imageMin);
		var clippedMax = Vector2.Min(max, imageMax);
		ImGui.GetWindowDrawList().AddRect(clippedMin, clippedMax, ImGui.ColorConvertFloat4ToU32(new Vector4(1, 0.35f, 0.7f, 1)), 0, ImDrawFlags.None, 2);
	}

	static string AtlasInfo(Texture atlas)
	{
		var memory = atlas.MemorySize >= 1024 * 1024
			? $"{atlas.MemorySize / (1024f * 1024f):0.0} MiB"
			: $"{atlas.MemorySize / 1024f:0.0} KiB";
		return $"{atlas.Name} — {atlas.Width}×{atlas.Height}, {atlas.Format}, {memory}";
	}

	private static string RenderTargetInfo(Target target)
	{
		var formats = "";
		for (var i = 0; i < target.Attachments.Length; i++)
			formats += (i > 0 ? ", " : "") + target.Attachments[i].Format;
		return $"{target.Name}\n{formats}\n{target.Width}×{target.Height}";
	}

}
