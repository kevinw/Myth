#if EDITOR
using System.Numerics;
using ImGuiNET;

namespace Myth;

// The NCT Tile editor: edits *.nct.json neural-texture tile specs. Save writes the JSON and
// hot-reloads assets, so every room using the tile rebuilds its regions with the new models and
// parameters immediately (same flow as the Tile Creator).
public partial class Manager
{
	bool nctEditorOpen;
	string? nctSelected;
	NctTileSpec? nctDraft;
	bool nctDirty;
	string nctNewName = "";

	void NctTileEditorImGui()
	{
		if (!nctEditorOpen)
			return;

		ImGui.SetNextWindowSize(new Vector2(420, 560), ImGuiCond.FirstUseEver);
		if (!ImGui.Begin("NCT Tiles", ref nctEditorOpen))
		{
			ImGui.End();
			return;
		}

		if (nctDraft == null && NctTiles.Specs.Count > 0)
			SelectNctTile(NctTiles.Specs[0]);

		DrawNctTileList();
		ImGui.Separator();

		if (nctDraft is { } draft)
		{
			var footer = ImGui.GetFrameHeightWithSpacing() + ImGui.GetStyle().ItemSpacing.Y;
			ImGui.BeginChild("NctBody", new Vector2(0, -footer));
			DrawNctTileParams(draft);
			ImGui.EndChild();
			DrawNctTileSaveRow(draft);
		}
		else
			ImGui.TextDisabled("Select a tile above, or create a new one.");

		ImGui.End();
	}

	void SelectNctTile(NctTileSpec spec)
	{
		nctSelected = spec.Name;
		nctDraft = NctTiles.Clone(spec);
		nctDirty = false;
	}

	void DrawNctTileList()
	{
		foreach (var spec in NctTiles.Specs)
		{
			if (ImGuiTextButton(spec.Name, new Vector2(0, 0), nctSelected == spec.Name))
				SelectNctTile(spec);
			ImGui.SameLine();
		}
		ImGui.SetNextItemWidth(120);
		ImGui.InputTextWithHint("##NewNctName", "new tile name", ref nctNewName, 32);
		ImGui.SameLine();
		var name = nctNewName.Trim().ToLowerInvariant();
		var valid = name.Length > 0 && name.All(c => char.IsAsciiLetterOrDigit(c) || c == '-')
			&& char.IsAsciiLetter(name[0])
			&& NctTiles.Specs.All(it => !string.Equals(it.Name, name, StringComparison.OrdinalIgnoreCase))
			&& Factory.CharForName(name) == null;
		ImGui.BeginDisabled(!valid);
		if (ImGui.Button("New"))
		{
			nctDraft = NctTiles.CreateDefault(name);
			nctSelected = name;
			nctNewName = "";
			nctDirty = true;
		}
		ImGui.EndDisabled();
	}

	void DrawNctTileParams(NctTileSpec draft)
	{
		var layers = Enum.GetNames<Factory.TileLayer>();
		var layerIndex = (int)draft.Layer;
		if (ImGui.Combo("layer", ref layerIndex, layers, layers.Length))
		{
			draft.Layer = (Factory.TileLayer)layerIndex;
			nctDirty = true;
		}

		var solid = draft.Solid;
		if (ImGui.Checkbox("solid", ref solid))
		{
			draft.Solid = solid;
			nctDirty = true;
		}

		if (draft.Solid)
		{
			var surfaces = (string[])[Factory.DefaultSurfaceType, "Brick"];
			var surfaceIndex = Math.Max(0, Array.IndexOf(surfaces, draft.Surface));
			if (ImGui.Combo("surface", ref surfaceIndex, surfaces, surfaces.Length))
			{
				draft.Surface = surfaces[surfaceIndex];
				nctDirty = true;
			}
		}

		// Reuse the actor inspector's image + motion model pickers so NCT tiles browse models the
		// same way (grouped by texture, then flow/motion variant).
		ImGui.TextUnformatted("interior");
		ImGui.PushID("interior");
		if (DrawNeuralTextureWeightsCombo(ref draft.Interior, allowNone: false))
			nctDirty = true;
		ImGui.PopID();

		ImGui.TextUnformatted("edge");
		ImGui.PushID("edge");
		if (DrawNeuralTextureWeightsCombo(ref draft.Edge, allowNone: true))
			nctDirty = true;
		ImGui.PopID();

		var band = draft.EdgeBandCells;
		if (ImGui.SliderInt("edge band", ref band, 1, 48))
		{
			draft.EdgeBandCells = band;
			nctDirty = true;
		}

		var freeze = draft.FreezeSteps;
		if (ImGui.SliderInt("freeze steps (0 = live)", ref freeze, 0, 400))
		{
			draft.FreezeSteps = Math.Max(0, freeze);
			nctDirty = true;
		}

		if (!draft.Frozen)
		{
			var sps = draft.StepsPerSecond;
			if (ImGui.SliderFloat("steps/sec", ref sps, 1f, 60f))
			{
				draft.StepsPerSecond = sps;
				nctDirty = true;
			}
		}

		var fire = draft.FireRate;
		if (ImGui.SliderFloat("fire rate", ref fire, 0f, 1f))
		{
			draft.FireRate = fire;
			nctDirty = true;
		}
	}

	void DrawNctTileSaveRow(NctTileSpec draft)
	{
		var exists = NctTiles.Specs.Any(it => it.Name == draft.Name);
		ImGui.BeginDisabled(!nctDirty);
		if (ImGui.Button(exists ? "Save + Reload" : "Create + Reload"))
		{
			NctTiles.Save(draft);
			ReloadAssets(false);
			nctDirty = false;
		}
		ImGui.EndDisabled();
		ImGui.SameLine();
		ImGui.BeginDisabled(!nctDirty || !exists);
		if (ImGui.Button("Revert") && NctTiles.Specs.FirstOrDefault(it => it.Name == draft.Name) is { } original)
			SelectNctTile(original);
		ImGui.EndDisabled();
		if (nctDirty)
		{
			ImGui.SameLine();
			ImGui.TextDisabled("unsaved changes");
		}
	}
}
#endif
