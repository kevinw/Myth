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
	bool nctAutoSave;
	bool nctAutoSaveRequested;
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
			nctAutoSaveRequested = false;
			ImGui.BeginChild("NctBody", new Vector2(0, -footer));
			DrawNctTileParams(draft);
			ImGui.EndChild();
			if (nctAutoSaveRequested && nctDirty)
				SaveNctTile(draft);
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
		var changed = ImGui.Combo("layer", ref layerIndex, layers, layers.Length);
		if (changed)
		{
			draft.Layer = (Factory.TileLayer)layerIndex;
			if (draft.Layer != Factory.TileLayer.Foreground)
				draft.HasSlope = false;
		}
		MarkNctEdited(changed, discrete: true);

		var solid = draft.Solid;
		changed = ImGui.Checkbox("solid", ref solid);
		if (changed)
		{
			draft.Solid = solid;
			if (!solid)
				draft.HasSlope = false;
		}
		MarkNctEdited(changed, discrete: true);

		if (draft.Solid)
		{
			var surfaces = (string[])[Factory.DefaultSurfaceType, "Brick"];
			var surfaceIndex = Math.Max(0, Array.IndexOf(surfaces, draft.Surface));
			changed = ImGui.Combo("surface", ref surfaceIndex, surfaces, surfaces.Length);
			if (changed)
				draft.Surface = surfaces[surfaceIndex];
			MarkNctEdited(changed, discrete: true);

			if (draft.Layer == Factory.TileLayer.Foreground)
			{
				var slopes = draft.HasSlope;
				changed = ImGui.Checkbox("slopes", ref slopes);
				if (changed)
					draft.HasSlope = slopes;
				MarkNctEdited(changed, discrete: true);
			}
		}

		// Reuse the actor inspector's image + motion model pickers so NCT tiles browse models the
		// same way (grouped by texture, then flow/motion variant).
		ImGui.TextUnformatted("interior");
		ImGui.PushID("interior");
		changed = NeuralTextureCatalog.DrawCombo(ref draft.Interior, allowNone: false);
		MarkNctEdited(changed, discrete: true);
		ImGui.PopID();

		ImGui.TextUnformatted("edge");
		ImGui.PushID("edge");
		changed = NeuralTextureCatalog.DrawCombo(ref draft.Edge, allowNone: true);
		MarkNctEdited(changed, discrete: true);
		ImGui.PopID();

		var band = draft.EdgeBandCells;
		changed = ImGui.SliderInt("edge band", ref band, 1, 48);
		if (changed)
			draft.EdgeBandCells = band;
		MarkNctEdited(changed);

		var blendCurve = draft.BlendCurve;
		changed = ImGui.SliderFloat("edge blend curve", ref blendCurve, 0.1f, 4f, "%.2f");
		if (changed)
			draft.BlendCurve = blendCurve;
		MarkNctEdited(changed);

		var edgeWeight = draft.EdgeWeight;
		changed = ImGui.SliderFloat("edge model weight", ref edgeWeight, 0f, 1f, "%.2f");
		if (changed)
			draft.EdgeWeight = edgeWeight;
		MarkNctEdited(changed);

		var featureScale = draft.FeatureScale;
		changed = ImGui.SliderFloat("feature scale", ref featureScale, 0.25f, 8f, "%.2f px/cell");
		if (changed)
			draft.FeatureScale = featureScale;
		MarkNctEdited(changed);
		if (ImGui.IsItemHovered())
			ImGui.SetTooltip("World pixels per simulation cell. Higher values make learned features larger; island size no longer changes them.");

		var rounding = draft.RoundingPixels;
		changed = ImGui.SliderFloat("rounding (px)", ref rounding, 0f, Game.TileSize * 0.5f);
		if (changed)
			draft.RoundingPixels = rounding;
		MarkNctEdited(changed);

		var outset = draft.OutsetPixels;
		changed = ImGui.SliderFloat("outset (px)", ref outset, -Game.TileSize, Game.TileSize);
		if (changed)
			draft.OutsetPixels = outset;
		MarkNctEdited(changed);
		if (ImGui.IsItemHovered())
			ImGui.SetTooltip("Positive grows beyond collision; negative insets inside it.");

		var freeze = draft.FreezeSteps;
		changed = ImGui.SliderInt("freeze steps (0 = live)", ref freeze, 0, 400);
		if (changed)
			draft.FreezeSteps = Math.Max(0, freeze);
		MarkNctEdited(changed);

		if (!draft.Frozen)
		{
			var sps = draft.StepsPerSecond;
			changed = ImGui.SliderFloat("steps/sec", ref sps, 1f, 60f);
			if (changed)
				draft.StepsPerSecond = sps;
			MarkNctEdited(changed);
		}

		var fire = draft.FireRate;
		changed = ImGui.SliderFloat("fire rate", ref fire, 0f, 1f);
		if (changed)
			draft.FireRate = fire;
		MarkNctEdited(changed);

		ImGui.SeparatorText("Decoder");
		var contrast = draft.Contrast;
		changed = ImGui.SliderFloat("contrast", ref contrast, 0f, 4f, "%.2f");
		if (changed) draft.Contrast = contrast;
		MarkNctEdited(changed);

		var saturation = draft.Saturation;
		changed = ImGui.SliderFloat("saturation", ref saturation, 0f, 4f, "%.2f");
		if (changed) draft.Saturation = saturation;
		MarkNctEdited(changed);

		var brightness = draft.Brightness;
		changed = ImGui.SliderFloat("brightness", ref brightness, -1f, 1f, "%.2f");
		if (changed) draft.Brightness = brightness;
		MarkNctEdited(changed);

		var tint = draft.Tint.ToVector4();
		var tintRgb = new Vector3(tint.X, tint.Y, tint.Z);
		changed = ImGui.ColorEdit3("tint", ref tintRgb);
		if (changed) draft.Tint = new(tintRgb.X, tintRgb.Y, tintRgb.Z, 1f);
		MarkNctEdited(changed);

		var normalStrength = draft.NormalStrength;
		changed = ImGui.SliderFloat("authored normal strength", ref normalStrength, 0f, 4f, "%.2f");
		if (changed) draft.NormalStrength = normalStrength;
		MarkNctEdited(changed);

		var silhouetteNormalStrength = draft.SilhouetteNormalStrength;
		changed = ImGui.SliderFloat("silhouette normal strength", ref silhouetteNormalStrength, 0f, 4f, "%.2f");
		if (changed) draft.SilhouetteNormalStrength = silhouetteNormalStrength;
		MarkNctEdited(changed);

		if (ImGui.CollapsingHeader("Experimental decoder"))
		{
			ImGui.TextWrapped("These controls push the decoder outside its training distribution; the tile silhouette stays fixed.");
			var detailScale = draft.DetailScale;
			changed = ImGui.SliderFloat("detail scale", ref detailScale, 0.25f, 4f, "%.2f");
			if (changed) draft.DetailScale = detailScale;
			MarkNctEdited(changed);

			var detailPhase = draft.DetailPhase;
			changed = ImGui.DragFloat2("detail phase", ref detailPhase, 0.25f, -64f, 64f, "%.2f");
			if (changed) draft.DetailPhase = detailPhase;
			MarkNctEdited(changed);

			var patchFrequency = draft.PatchFrequency;
			changed = ImGui.SliderFloat("patch frequency", ref patchFrequency, 0.1f, 4f, "%.2f");
			if (changed) draft.PatchFrequency = patchFrequency;
			MarkNctEdited(changed);

			var sirenFrequency = draft.SirenFrequency;
			changed = ImGui.SliderFloat("SIREN frequency", ref sirenFrequency, 1f, 20f, "%.2f");
			if (changed) draft.SirenFrequency = sirenFrequency;
			MarkNctEdited(changed);

			var latentGain = draft.LatentGain;
			changed = ImGui.SliderFloat("latent gain", ref latentGain, 0f, 4f, "%.2f");
			if (changed) draft.LatentGain = latentGain;
			MarkNctEdited(changed);

			var latentBias = draft.LatentBias;
			changed = ImGui.SliderFloat("latent bias", ref latentBias, -2f, 2f, "%.2f");
			if (changed) draft.LatentBias = latentBias;
			MarkNctEdited(changed);

			var nearest = draft.NearestStateSampling;
			changed = ImGui.Checkbox("nearest state sampling", ref nearest);
			if (changed) draft.NearestStateSampling = nearest;
			MarkNctEdited(changed, discrete: true);

			changed = ImGui.Button("Reset experimental");
			if (changed)
			{
				draft.DetailScale = 1f;
				draft.DetailPhase = Vector2.Zero;
				draft.PatchFrequency = 1f;
				draft.SirenFrequency = 10f;
				draft.LatentGain = 1f;
				draft.LatentBias = 0f;
				draft.NearestStateSampling = false;
			}
			MarkNctEdited(changed, discrete: true);
		}
	}

	void DrawNctTileSaveRow(NctTileSpec draft)
	{
		var exists = NctTiles.Specs.Any(it => it.Name == draft.Name);
		ImGui.BeginDisabled(!nctDirty);
		if (ImGui.Button(exists ? "Save + Reload" : "Create + Reload"))
			SaveNctTile(draft);
		ImGui.EndDisabled();
		ImGui.SameLine();
		ImGui.BeginDisabled(!nctDirty || !exists);
		if (ImGui.Button("Revert") && NctTiles.Specs.FirstOrDefault(it => it.Name == draft.Name) is { } original)
			SelectNctTile(original);
		ImGui.EndDisabled();
		ImGui.SameLine();
		ImGui.Checkbox("auto-save", ref nctAutoSave);
		if (nctDirty)
		{
			ImGui.SameLine();
			ImGui.TextDisabled("unsaved changes");
		}
	}

	void MarkNctEdited(bool changed, bool discrete = false)
	{
		if (changed)
			nctDirty = true;
		if (nctAutoSave && ((discrete && changed) || ImGui.IsItemDeactivatedAfterEdit()))
			nctAutoSaveRequested = true;
	}

	void SaveNctTile(NctTileSpec draft)
	{
		NctTiles.Save(draft);
		ReloadAssets(false);
		nctDirty = false;
	}
}
#endif
