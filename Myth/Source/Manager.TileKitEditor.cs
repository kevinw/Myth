#if EDITOR
using System.Numerics;
using Foster.Framework;
using ImGuiNET;

namespace Myth;

// The Tile Creator: edits JSON tile kit specs (Assets/Tilesets/*.kit.json) with a live
// preview baked in memory. Saving writes the spec and hot-reloads assets, so every room
// using the kit updates immediately.
public partial class Manager
{
	bool tileKitEditorOpen;
	string? tileKitSelected;
	TileKitSpec? tileKitDraft;
	bool tileKitDirty;
	bool tileKitPreviewStale;
	bool tileKitFocusRequested;
	char tileKitPaintKey;
	string tileKitNewName = "";
	Texture? tileKitPreviewTexture;

	// Demo terrain for the preview: a floating island, a bumpy floor, an overhang and a
	// wall, so top/side/bottom rims, corners and wobble are all visible at once.
	static readonly string[] TileKitPreviewMap =
	[
		"...........#",
		"..####.....#",
		".######....#",
		".######...##",
		"..####.....#",
		"...........#",
		"......###..#",
		"#..######..#",
		"############",
	];

	/// The kit spec a palette tile was baked from, or null for tiles that come from a .ase
	/// tileset (those open in Aseprite instead).
	static TileKitSpec? FindTileKitSpec(Factory.Entry entry)
	{
		foreach (var spec in TileKits.Specs)
			if (NameMatches(spec.Name, entry.TilesetName) ||
				NameMatches(spec.Name, entry.SlopeTilesetName) ||
				NameMatches(spec.DualName, entry.DualTilesetName))
				return spec;

		return null;

		static bool NameMatches(string specName, string? tilesetName)
			=> string.Equals(specName, tilesetName, StringComparison.OrdinalIgnoreCase);
	}

	void OpenTileKitEditor(TileKitSpec spec)
	{
		tileKitEditorOpen = true;
		tileKitFocusRequested = true;
		if (tileKitSelected != spec.Name)
			SelectTileKit(spec);
	}

	void TileKitEditorImGui()
	{
		if (!tileKitEditorOpen)
			return;

		if (tileKitFocusRequested)
		{
			tileKitFocusRequested = false;
			ImGui.SetNextWindowFocus();
		}

		ImGui.SetNextWindowSize(new Vector2(480, 720), ImGuiCond.FirstUseEver);
		DockNextToolWindow("Tile Creator");
		if (!ImGui.Begin("Tile Creator", ref tileKitEditorOpen))
		{
			NoteDockedWindow();
			ImGui.End();
			return;
		}
		NoteDockedWindow();

		if (tileKitDraft == null && TileKits.Specs.Count > 0)
			SelectTileKit(TileKits.Specs[0]);

		// The kit list and save row stay pinned; only the editing sections scroll.
		DrawTileKitList();
		ImGui.Separator();

		if (tileKitDraft is { } draft)
		{
			var footer = ImGui.GetFrameHeightWithSpacing() + ImGui.GetStyle().ItemSpacing.Y;
			ImGui.BeginChild("KitBody", new Vector2(0, -footer));
			DrawTileKitPreview(draft);
			ImGui.Separator();
			DrawTileKitParams(draft);
			DrawTileKitPalette(draft);
			DrawTileKitFills(draft);
			DrawTileKitEdgeWeights(draft, "Edge (sides + slopes)", draft.Edge);
			DrawTileKitEdgeWeights(draft, "Edge Top", draft.EdgeTop);
			DrawTileKitEdgeWeights(draft, "Edge Bottom", draft.EdgeBottom);
			ImGui.EndChild();
			DrawTileKitSaveRow(draft);
		}
		else
			ImGui.TextDisabled("Select a kit above, or create a new one.");

		ImGui.End();
	}

	void DrawTileKitList()
	{
		foreach (var spec in TileKits.Specs)
		{
			if (ImGuiTextButton(spec.Name, new Vector2(0, 0), tileKitSelected == spec.Name))
				SelectTileKit(spec);
			ImGui.SameLine();
		}
		ImGui.SetNextItemWidth(120);
		ImGui.InputTextWithHint("##NewKitName", "new kit name", ref tileKitNewName, 32);
		ImGui.SameLine();
		var name = tileKitNewName.Trim().ToLowerInvariant();
		var valid = name.Length > 0 && name.All(c => char.IsAsciiLetterOrDigit(c) || c == '-')
			&& char.IsAsciiLetter(name[0])
			&& TileKits.Specs.All(it => !string.Equals(it.Name, name, StringComparison.OrdinalIgnoreCase))
			&& Assets.GetTileset(name) == null;
		ImGui.BeginDisabled(!valid);
		if (ImGui.Button("New Kit"))
		{
			tileKitDraft = TileKits.CreateDefault(name);
			tileKitSelected = name;
			tileKitNewName = "";
			tileKitPaintKey = 'a';
			tileKitDirty = true;
			tileKitPreviewStale = true;
		}
		ImGui.EndDisabled();
	}

	void SelectTileKit(TileKitSpec spec)
	{
		tileKitSelected = spec.Name;
		tileKitDraft = TileKits.Clone(spec);
		tileKitPaintKey = tileKitDraft.Palette.Keys.OrderBy(it => it).FirstOrDefault('a');
		tileKitDirty = false;
		tileKitPreviewStale = true;
	}

	void DrawTileKitPreview(TileKitSpec draft)
	{
		if (tileKitPreviewStale)
		{
			tileKitPreviewStale = false;
			try
			{
				using var image = Assets.GenerateTileKitPreviewImage(draft, TileKitPreviewMap);
				tileKitPreviewTexture?.Dispose();
				tileKitPreviewTexture = new Texture(GraphicsDevice, image, name: "TileKitPreview");
			}
			catch (Exception e)
			{
				Console.WriteLine($"Tile kit preview failed: {e.Message}");
			}
		}

		if (tileKitPreviewTexture is { } texture)
			imgui.Image(new Subtexture(texture), new Vector2(texture.Width, texture.Height) * 3.5f);
	}

	void MarkTileKitEdited()
	{
		tileKitDirty = true;
		tileKitPreviewStale = true;
	}

	void DrawTileKitParams(TileKitSpec draft)
	{
		if (!ImGui.CollapsingHeader("Parameters", ImGuiTreeNodeFlags.DefaultOpen))
			return;

		var background = draft.Layer == Factory.TileLayer.Background;
		if (ImGui.Checkbox("background tile", ref background))
		{
			draft.Layer = background ? Factory.TileLayer.Background : Factory.TileLayer.Foreground;
			MarkTileKitEdited();
		}

		var wobble = draft.Wobble;
		if (ImGui.SliderFloat("wobble", ref wobble, 0f, 1f))
		{
			draft.Wobble = wobble;
			MarkTileKitEdited();
		}

		var widths = Assets.EdgeWidthRangeForEditor(draft.Name, draft.EdgeWidth);
		var min = widths.Min;
		var max = widths.Max;
		var changed = ImGui.SliderInt("edge width min", ref min, 0, 4);
		changed |= ImGui.SliderInt("edge width max", ref max, 0, 4);
		if (changed)
		{
			max = Math.Max(min, max);
			draft.EdgeWidth = min == max ? min.ToString() : $"{min}-{max}";
			MarkTileKitEdited();
		}

		var seed = (int)Math.Clamp(draft.Seed, 1, int.MaxValue);
		if (ImGui.InputInt("seed", ref seed))
		{
			draft.Seed = Math.Max(1, seed);
			MarkTileKitEdited();
		}
		ImGui.SameLine();
		if (ImGui.Button("Reroll"))
		{
			draft.Seed = Random.Shared.Next(1, int.MaxValue);
			MarkTileKitEdited();
		}

		var surfaces = (string[])[Factory.DefaultSurfaceType, "Brick"];
		var surfaceIndex = Math.Max(0, Array.IndexOf(surfaces, draft.Surface));
		if (ImGui.Combo("surface", ref surfaceIndex, surfaces, surfaces.Length))
		{
			draft.Surface = surfaces[surfaceIndex];
			MarkTileKitEdited();
		}

		var reflections = Enum.GetNames<Factory.ReflectionMode>();
		var reflectionIndex = (int)draft.Reflection;
		if (ImGui.Combo("reflection", ref reflectionIndex, reflections, reflections.Length))
		{
			draft.Reflection = (Factory.ReflectionMode)reflectionIndex;
			MarkTileKitEdited();
		}
		if (draft.Reflection != Factory.ReflectionMode.None)
		{
			var opacity = draft.ReflectionOpacity;
			if (ImGui.SliderFloat("reflection opacity", ref opacity, 0f, 1f))
			{
				draft.ReflectionOpacity = opacity;
				MarkTileKitEdited();
			}
		}

		var acoustic = draft.AcousticReflectivity;
		if (ImGui.SliderFloat("acoustic reflectivity", ref acoustic, 0f, 1f))
		{
			draft.AcousticReflectivity = acoustic;
			MarkTileKitEdited();
		}
	}

	void DrawTileKitPalette(TileKitSpec draft)
	{
		if (!ImGui.CollapsingHeader("Palette", ImGuiTreeNodeFlags.DefaultOpen))
			return;

		var hasHeights = draft.Heights.Count > 0;
		foreach (var key in draft.Palette.Keys.OrderBy(it => it).ToArray())
		{
			ImGui.PushID(key);
			if (ImGui.RadioButton($"{key}", tileKitPaintKey == key))
				tileKitPaintKey = key;
			ImGui.SameLine();

			var color = draft.Palette[key].ToVector4();
			var rgb = new Vector3(color.X, color.Y, color.Z);
			if (ImGui.ColorEdit3("##Color", ref rgb, ImGuiColorEditFlags.NoInputs))
			{
				draft.Palette[key] = new Color(rgb.X, rgb.Y, rgb.Z, 1f);
				MarkTileKitEdited();
			}

			if (hasHeights)
			{
				ImGui.SameLine();
				ImGui.SetNextItemWidth(140);
				var height = draft.Heights.GetValueOrDefault(key, 0.67f);
				if (ImGui.SliderFloat("##Height", ref height, 0f, 1f, "height %.2f"))
				{
					draft.Heights[key] = height;
					MarkTileKitEdited();
				}
			}

			var used = draft.Fills.Any(fill => fill.Any(row => row.Contains(key)));
			ImGui.SameLine();
			ImGui.BeginDisabled(used || draft.Palette.Count <= 1);
			if (ImGui.SmallButton("x"))
			{
				draft.Palette.Remove(key);
				draft.Heights.Remove(key);
				if (tileKitPaintKey == key)
					tileKitPaintKey = draft.Palette.Keys.OrderBy(it => it).First();
				MarkTileKitEdited();
			}
			ImGui.EndDisabled();
			if (used && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
				ImGui.SetTooltip("in use by a fill tile");
			ImGui.PopID();
		}

		if (ImGui.SmallButton("+ color"))
		{
			var key = "abcdefghijklmnopqrstuvwxyz".FirstOrDefault(c => !draft.Palette.ContainsKey(c), '\0');
			if (key != '\0')
			{
				draft.Palette[key] = new Color(128, 128, 128, 255);
				if (hasHeights)
					draft.Heights[key] = 0.67f;
				MarkTileKitEdited();
			}
		}
		ImGui.SameLine();
		if (hasHeights)
		{
			if (ImGui.SmallButton("- heights (flat lighting)"))
			{
				draft.Heights.Clear();
				MarkTileKitEdited();
			}
		}
		else if (ImGui.SmallButton("+ heights (beveled lighting)"))
		{
			foreach (var key in draft.Palette.Keys.ToArray())
				draft.Heights[key] = 0.67f;
			MarkTileKitEdited();
		}
	}

	void DrawTileKitFills(TileKitSpec draft)
	{
		if (!ImGui.CollapsingHeader($"Fill variants ({draft.Fills.Count})", ImGuiTreeNodeFlags.DefaultOpen))
			return;
		ImGui.TextDisabled("left-drag paints the selected palette color");

		const float PixelSize = 15f;
		for (var v = 0; v < draft.Fills.Count; v++)
		{
			ImGui.PushID(v);
			var fill = draft.Fills[v];
			ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, Vector2.One);
			ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 0);
			ImGui.BeginGroup();
			for (var y = 0; y < Game.TileSize; y++)
			{
				for (var x = 0; x < Game.TileSize; x++)
				{
					if (x > 0)
						ImGui.SameLine(0, 1);
					var key = fill[y][x];
					var color = draft.Palette.GetValueOrDefault(key, Color.Magenta).ToVector4();
					ImGui.PushID(y * Game.TileSize + x);
					ImGui.ColorButton("##px", color, ImGuiColorEditFlags.NoTooltip | ImGuiColorEditFlags.NoBorder, new Vector2(PixelSize, PixelSize));
					if (ImGui.IsItemHovered() && ImGui.IsMouseDown(ImGuiMouseButton.Left) && key != tileKitPaintKey)
					{
						var row = fill[y].ToCharArray();
						row[x] = tileKitPaintKey;
						fill[y] = new string(row);
						MarkTileKitEdited();
					}
					ImGui.PopID();
				}
			}
			ImGui.EndGroup();
			ImGui.PopStyleVar(2);
			ImGui.SameLine();
			ImGui.BeginGroup();
			if (ImGui.SmallButton("copy"))
			{
				draft.Fills.Insert(v + 1, (string[])fill.Clone());
				MarkTileKitEdited();
			}
			ImGui.BeginDisabled(draft.Fills.Count <= 1);
			if (ImGui.SmallButton("x"))
			{
				draft.Fills.RemoveAt(v);
				MarkTileKitEdited();
			}
			ImGui.EndDisabled();
			ImGui.EndGroup();
			ImGui.PopID();
			if ((v + 1) % 2 != 0 && v + 1 < draft.Fills.Count)
				ImGui.SameLine(0, 18);
		}
	}

	void DrawTileKitEdgeWeights(TileKitSpec draft, string label, List<(Color Color, float Weight)> weights)
	{
		if (!ImGui.CollapsingHeader($"{label} ({(weights.Count == 0 ? "off" : weights.Count.ToString())})###{label}"))
			return;

		ImGui.PushID(label);
		for (var i = 0; i < weights.Count; i++)
		{
			ImGui.PushID(i);
			var color = weights[i].Color.ToVector4();
			var rgb = new Vector3(color.X, color.Y, color.Z);
			if (ImGui.ColorEdit3("##Color", ref rgb, ImGuiColorEditFlags.NoInputs))
			{
				weights[i] = (new Color(rgb.X, rgb.Y, rgb.Z, 1f), weights[i].Weight);
				MarkTileKitEdited();
			}
			ImGui.SameLine();
			ImGui.SetNextItemWidth(180);
			var weight = weights[i].Weight;
			if (ImGui.SliderFloat("##Weight", ref weight, 1f, 100f, "weight %.0f"))
			{
				weights[i] = (weights[i].Color, weight);
				MarkTileKitEdited();
			}
			ImGui.SameLine();
			if (ImGui.SmallButton("x"))
			{
				weights.RemoveAt(i--);
				MarkTileKitEdited();
			}
			ImGui.PopID();
		}
		if (ImGui.SmallButton("+ color"))
		{
			weights.Add((new Color(128, 128, 128, 255), 20));
			MarkTileKitEdited();
		}
		ImGui.PopID();
	}

	void DrawTileKitSaveRow(TileKitSpec draft)
	{
		var exists = TileKits.Specs.Any(it => it.Name == draft.Name);
		ImGui.BeginDisabled(!tileKitDirty);
		if (ImGui.Button(exists ? "Save + Reload" : "Create + Reload"))
		{
			TileKits.Save(draft);
			ReloadAssets(false);
			tileKitDirty = false;
		}
		ImGui.EndDisabled();
		ImGui.SameLine();
		ImGui.BeginDisabled(!tileKitDirty || !exists);
		if (ImGui.Button("Revert") && TileKits.Specs.FirstOrDefault(it => it.Name == draft.Name) is { } original)
			SelectTileKit(original);
		ImGui.EndDisabled();
		if (tileKitDirty)
		{
			ImGui.SameLine();
			ImGui.TextDisabled("unsaved changes");
		}
	}
}
#endif
