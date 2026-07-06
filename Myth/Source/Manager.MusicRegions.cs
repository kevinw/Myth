#if EDITOR
using System.Numerics;
using Foster.Framework;
using ImGuiNET;

namespace Myth;

// Music Regions editing: a window listing the world's regions, a one-shot drag-draw mode
// for painting new rects in the world view, and an overlay tinting rooms by their resolved
// music area while the window is open. Edits apply live (music retargets immediately);
// Save persists to the per-world regions file.
public partial class Manager
{
	bool musicRegionsOpen;
	bool musicRegionsDirty;
	bool musicRegionDrawArm;
	int musicRegionSelected = -1;
	Point2? musicRegionDragStart;
	RectInt musicRegionDragRect;
	string musicRegionLastArea = DefaultMusicArea;

	static readonly Color[] MusicRegionPalette =
	[
		new(0x5FCDE4), new(0xDF7126), new(0x99E550), new(0xD77BBA),
		new(0xFBF236), new(0x5B6EE1), new(0x37946E), new(0xAC3232),
	];

	static Color MusicRegionAreaColor(string area)
	{
		var hash = 0;
		foreach (var c in area)
			hash = hash * 31 + c;
		return MusicRegionPalette[(hash & 0x7FFFFFFF) % MusicRegionPalette.Length];
	}

	void MarkMusicRegionsEdited()
	{
		musicRegionsDirty = true;
		game?.RefreshMusic();
	}

	void MusicRegionsImGui()
	{
		if (!musicRegionsOpen)
			return;

		ImGui.SetNextWindowSize(new Vector2(380, 420), ImGuiCond.FirstUseEver);
		if (!ImGui.Begin("Music Regions", ref musicRegionsOpen))
		{
			ImGui.End();
			return;
		}

		if (ImGuiTextButton(musicRegionDrawArm ? "Drag a rect in the world view (Esc cancels)" : "Draw New Region", new Vector2(0, 0), musicRegionDrawArm))
			musicRegionDrawArm = !musicRegionDrawArm;
		ImGui.SameLine();
		ImGui.TextDisabled("smallest region wins; rooms can still override");
		ImGui.Separator();

		for (var i = 0; i < MusicRegions.Regions.Count; i++)
		{
			var region = MusicRegions.Regions[i];
			ImGui.PushID(i);

			ImGui.ColorButton("##Swatch", MusicRegionAreaColor(region.Area).ToVector4(), ImGuiColorEditFlags.NoTooltip, new Vector2(18, 18));
			ImGui.SameLine();
			if (ImGui.Selectable($"{region.Area}##Row", musicRegionSelected == i, ImGuiSelectableFlags.None, new Vector2(120, 0)))
				musicRegionSelected = i;
			if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
			{
				// double-click focuses the editor camera on the region
				var rect = region.Rect * Game.TileSize;
				editorCamera = new Vector2(rect.X + rect.Width / 2f, rect.Y + rect.Height / 2f);
				editorCameraTween = 1.0f;
			}

			ImGui.SameLine();
			ImGui.SetNextItemWidth(110);
			if (ImGui.BeginCombo("##Area", region.Area))
			{
				foreach (var area in musicAreas)
				{
					if (ImGui.Selectable(area, region.Area == area))
					{
						region.Area = area;
						musicRegionLastArea = area;
						MarkMusicRegionsEdited();
					}
				}
				ImGui.EndCombo();
			}

			Span<int> pos = [region.Rect.X, region.Rect.Y];
			Span<int> size = [region.Rect.Width, region.Rect.Height];
			var edited = false;
			ImGui.SameLine();
			ImGui.SetNextItemWidth(100);
			edited |= ImGui.DragInt2("##Pos", ref pos[0]);
			ImGui.SameLine();
			ImGui.SetNextItemWidth(100);
			edited |= ImGui.DragInt2("##Size", ref size[0]);
			if (edited)
			{
				region.Rect = new RectInt(pos[0], pos[1], Math.Max(1, size[0]), Math.Max(1, size[1]));
				MarkMusicRegionsEdited();
			}

			ImGui.SameLine();
			if (ImGui.SmallButton("x"))
			{
				MusicRegions.Regions.RemoveAt(i--);
				musicRegionSelected = -1;
				MarkMusicRegionsEdited();
			}
			ImGui.PopID();
		}

		if (MusicRegions.Regions.Count == 0)
			ImGui.TextDisabled("No regions yet. Draw one, then pick its area.");

		ImGui.Separator();
		ImGui.BeginDisabled(!musicRegionsDirty);
		if (ImGui.Button("Save"))
		{
			MusicRegions.Save();
			musicRegionsDirty = false;
		}
		ImGui.SameLine();
		if (ImGui.Button("Revert"))
		{
			MusicRegions.Load();
			musicRegionSelected = -1;
			musicRegionsDirty = false;
			game?.RefreshMusic();
		}
		ImGui.EndDisabled();
		if (musicRegionsDirty)
		{
			ImGui.SameLine();
			ImGui.TextDisabled("unsaved changes");
		}

		ImGui.End();
	}

	// One-shot rect drawing in the world view. Returns true while it owns the mouse (armed
	// or dragging) so normal editor tools don't also react; pan/zoom above still ran.
	bool UpdateMusicRegionDrawing(bool canInteract, Vector2 cursor, Rect inner)
	{
		if (!musicRegionDrawArm && musicRegionDragStart == null)
			return false;

		if (Input.Keyboard.Pressed(Keys.Escape))
		{
			musicRegionDrawArm = false;
			musicRegionDragStart = null;
			return true;
		}

		var world = ScreenToEditorWorld(cursor, inner);
		var tile = new Point2(
			(int)MathF.Floor(world.X / Game.TileSize),
			(int)MathF.Floor(world.Y / Game.TileSize));

		if (musicRegionDragStart == null)
		{
			if (canInteract && Input.Mouse.LeftPressed)
				musicRegionDragStart = tile;
			return true;
		}

		var start = musicRegionDragStart.Value;
		musicRegionDragRect = new RectInt(
			Math.Min(start.X, tile.X),
			Math.Min(start.Y, tile.Y),
			Math.Abs(tile.X - start.X) + 1,
			Math.Abs(tile.Y - start.Y) + 1);

		if (!Input.Mouse.LeftDown)
		{
			MusicRegions.Regions.Add(new MusicRegion { Area = musicRegionLastArea, Rect = musicRegionDragRect });
			musicRegionSelected = MusicRegions.Regions.Count - 1;
			musicRegionDrawArm = false;
			musicRegionDragStart = null;
			musicRegionsOpen = true;
			MarkMusicRegionsEdited();
		}
		return true;
	}

	// Drawn in world space right after the rooms so the tints sit under the grid and
	// selection chrome. Only visible while the window (or draw mode) is active.
	void DrawMusicRegionsOverlay(Rect visibleWorld)
	{
		if (!musicRegionsOpen && !musicRegionDrawArm && musicRegionDragStart == null)
			return;

		var line = MathF.Max(1.0f / editorZoom, LineWeight / editorZoom);

		foreach (var room in Assets.RoomsById.Values)
		{
			var bounds = room.WorldBounds;
			if (visibleWorld.GetIntersection(new Rect(bounds.X, bounds.Y, bounds.Width, bounds.Height)).Area <= 0)
				continue;
			if (MusicRegions.Resolve(room) is { } area)
				batcher.Rect(bounds, MusicRegionAreaColor(area) * 0.22f);
		}

		for (var i = 0; i < MusicRegions.Regions.Count; i++)
		{
			var region = MusicRegions.Regions[i];
			var rect = region.Rect * Game.TileSize;
			var color = MusicRegionAreaColor(region.Area);
			batcher.Rect(rect, color * 0.08f);
			batcher.RectLine(rect, musicRegionSelected == i ? line * 2 : line, musicRegionSelected == i ? Color.White : color);
			if (Assets.Font != null)
				batcher.Text(Assets.Font, region.Area, new Vector2(rect.X, rect.Y) + new Vector2(4, 4) / editorZoom, Vector2.One / MathF.Max(editorZoom, 0.25f), color);
		}

		if (musicRegionDragStart != null)
			batcher.RectLine(musicRegionDragRect * Game.TileSize, line * 2, Color.White);
	}
}
#endif
