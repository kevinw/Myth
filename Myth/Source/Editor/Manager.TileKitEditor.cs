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
	Texture? tileKitPreviewTexture;

	// 0 leaves the authored heights alone, 1 clears them, 2+ is TileKits.HeightMode. Editor state
	// only -- deriving writes concrete numbers into the spec, so nothing re-derives behind your back.
	int tileKitHeightMode;
	float tileKitHeightStrength = 1f;

	string[]? tileKitSheetNames;
	string? tileKitSheetName;
	Image? tileKitSheetImage;
	Texture? tileKitSheetTexture;
	Point2 tileKitSheetCell = new(-1, -1);
	int tileKitSheetCellSize = 16;
	int tileKitSheetColors = 6;

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

		if (tileKitDraft is { } activeDraft)
			ImGui.TextUnformatted(activeDraft.Name);
		ImGui.Separator();

		if (tileKitDraft is { } draft)
		{
			var footer = ImGui.GetFrameHeightWithSpacing() + ImGui.GetStyle().ItemSpacing.Y;
			ImGui.BeginChild("KitBody", new Vector2(0, -footer));
			DrawTileKitPreview(draft);
			ImGui.Separator();
			DrawTileKitParams(draft);
			DrawTileKitPalette(draft);
			DrawTileKitLighting(draft);
			DrawTileKitFills(draft);
			DrawTileKitSheetImport(draft);
			DrawTileKitEdgeWeights(draft, "Edge (sides + slopes)", draft.Edge);
			DrawTileKitEdgeWeights(draft, "Edge Top", draft.EdgeTop);
			DrawTileKitEdgeWeights(draft, "Edge Bottom", draft.EdgeBottom);
			ImGui.EndChild();
			DrawTileKitSaveRow(draft);
		}
		else
			ImGui.TextDisabled("Open a tile from the palette to edit it.");

		ImGui.End();
	}

	static bool CanCreateTileKit(string name)
		=> name.Length > 0 && char.IsAsciiLetter(name[0])
			&& name.All(c => char.IsAsciiLetterOrDigit(c) || c == '-')
			&& TileKits.Specs.All(it => !string.Equals(it.Name, name, StringComparison.OrdinalIgnoreCase))
			&& Assets.GetTileset(name) == null;

	TileKitSpec CreateTileKitDraft(string name, Factory.TileLayer layer)
	{
		var draft = TileKits.CreateDefault(name);
		draft.Layer = layer;
		draft.PreferredChar = Factory.PickCharFor(name);
		tileKitDraft = draft;
		tileKitSelected = name;
		tileKitPaintKey = 'a';
		tileKitHeightMode = 0;
		tileKitDirty = true;
		tileKitPreviewStale = true;
		return draft;
	}

	void SelectTileKit(TileKitSpec spec)
	{
		tileKitSelected = spec.Name;
		tileKitDraft = TileKits.Clone(spec);
		tileKitPaintKey = tileKitDraft.Palette.Keys.OrderBy(it => it).FirstOrDefault('a');
		tileKitHeightMode = 0;
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

		if (!background)
		{
			var substances = (string[])[Factory.DefaultSubstance, "Brick", Factory.MetalSubstance];
			var substanceIndex = Math.Max(0, Array.IndexOf(substances, draft.Substance));
			if (ImGui.Combo("substance", ref substanceIndex, substances, substances.Length))
			{
				draft.Substance = substances[substanceIndex];
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
			var key = TileKits.PaletteKeys.FirstOrDefault(c => !draft.Palette.ContainsKey(c), '\0');
			if (key != '\0')
			{
				draft.Palette[key] = new Color(128, 128, 128, 255);
				if (hasHeights)
					draft.Heights[key] = TileKits.DefaultHeight;
				MarkTileKitEdited();
			}
		}
	}

	// "as authored" and "none" bracket the derive modes so opening a kit never re-derives it, and
	// clearing back to flat lighting stays one click away.
	static readonly string[] TileKitHeightModes =
		["as authored", "none (flat lighting)", "flat", "luminance", "local contrast", "seams", "top-lit", "relief (per-pixel)"];

	void DrawTileKitLighting(TileKitSpec draft)
	{
		if (!ImGui.CollapsingHeader("Lighting"))
			return;

		ImGui.TextDisabled("heights bevel the normal map; deriving writes numbers into the spec");
		ImGui.SetNextItemWidth(240);
		var changed = ImGui.Combo("derive heights", ref tileKitHeightMode, TileKitHeightModes, TileKitHeightModes.Length);
		if (tileKitHeightMode >= 2)
		{
			ImGui.SetNextItemWidth(240);
			changed |= ImGui.SliderFloat("relief strength", ref tileKitHeightStrength, 0f, 2f);
		}
		if (changed)
		{
			ApplyTileKitHeightMode(draft);
			MarkTileKitEdited();
		}

		if (draft.HeightMaps.Count == 0)
			return;

		ImGui.TextDisabled($"per-pixel relief, {draft.HeightMaps.Count} map(s) -- replaces the per-color heights");
		for (var i = 0; i < draft.HeightMaps.Count; i++)
		{
			ImGui.PushID(i);
			DrawHeightMapGrid(draft.HeightMaps[i]);
			ImGui.PopID();
			if ((i + 1) % 4 != 0 && i + 1 < draft.HeightMaps.Count)
				ImGui.SameLine(0, 10);
		}
	}

	static void DrawHeightMapGrid(string[] map)
	{
		const float PixelSize = 8f;
		ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, Vector2.One);
		ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 0);
		ImGui.BeginGroup();
		for (var y = 0; y < Game.TileSize; y++)
			for (var x = 0; x < Game.TileSize; x++)
			{
				if (x > 0)
					ImGui.SameLine(0, 1);
				var level = (map[y][x] - '0') / 9f;
				ImGui.PushID(y * Game.TileSize + x);
				ImGui.ColorButton("##h", new Vector4(level, level, level, 1),
					ImGuiColorEditFlags.NoTooltip | ImGuiColorEditFlags.NoBorder, new Vector2(PixelSize, PixelSize));
				ImGui.PopID();
			}
		ImGui.EndGroup();
		ImGui.PopStyleVar(2);
	}

	void ApplyTileKitHeightMode(TileKitSpec draft)
	{
		if (tileKitHeightMode == 0)
			return;
		if (tileKitHeightMode == 1)
		{
			draft.Heights.Clear();
			draft.HeightMaps.Clear();
			return;
		}
		TileKits.DeriveHeights(draft, (TileKits.HeightMode)(tileKitHeightMode - 2), tileKitHeightStrength);
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

	// Pulls fills out of an authored pixel-art sheet. The sheet stays the source of truth on disk;
	// what lands in the spec is a quantized, hand-editable copy of one cell.
	void DrawTileKitSheetImport(TileKitSpec draft)
	{
		if (!ImGui.CollapsingHeader("Import from sheet"))
			return;

		tileKitSheetNames ??= [.. Directory.EnumerateFiles(Path.Join(Assets.SourcesPath, "Tilesets"), "*.png")
			.Select(it => Path.GetFileNameWithoutExtension(it)!).Order(StringComparer.OrdinalIgnoreCase)];
		if (tileKitSheetNames.Length == 0)
		{
			ImGui.TextDisabled("no .png sheets in Assets/Sources/Tilesets");
			return;
		}

		var index = Math.Max(0, Array.IndexOf(tileKitSheetNames, tileKitSheetName));
		ImGui.SetNextItemWidth(200);
		if (ImGui.Combo("sheet", ref index, tileKitSheetNames, tileKitSheetNames.Length) || tileKitSheetName == null)
			SelectTileKitSheet(tileKitSheetNames[index]);

		var cellBlocks = tileKitSheetCellSize / Game.TileSize;
		ImGui.SetNextItemWidth(160);
		if (ImGui.SliderInt("cell size", ref cellBlocks, 1, 4, $"{tileKitSheetCellSize}px"))
		{
			tileKitSheetCellSize = cellBlocks * Game.TileSize;
			tileKitSheetCell = new(-1, -1);
		}
		// The ceiling is the palette key alphabet: 52 single-char keys, against at most 64 distinct
		// pixels in a tile. Most cells of a pixel-art sheet land well under it.
		ImGui.SetNextItemWidth(160);
		ImGui.SliderInt("colors", ref tileKitSheetColors, 2, TileKits.PaletteKeys.Length);

		if (tileKitSheetTexture is not { } sheet)
			return;

		var cells = new Point2(sheet.Width, sheet.Height) / tileKitSheetCellSize;
		ImGui.BeginChild("Sheet", new Vector2(0, 280), ImGuiChildFlags.Borders, ImGuiWindowFlags.HorizontalScrollbar);
		var origin = ImGui.GetCursorScreenPos();
		imgui.Image(new Subtexture(sheet), new Vector2(sheet.Width, sheet.Height));
		var draw = ImGui.GetWindowDrawList();
		if (ImGui.IsWindowHovered() && Rect.Between(ImGui.GetItemRectMin(), ImGui.GetItemRectMax()).Contains(ImGui.GetMousePos()))
		{
			var hovered = ((ImGui.GetMousePos() - origin) / tileKitSheetCellSize).FloorToPoint2();
			if (hovered.X < cells.X && hovered.Y < cells.Y)
			{
				OutlineSheetCell(draw, origin, hovered, ImGuiColor(1, 1, 1, 0.45f));
				if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
					tileKitSheetCell = hovered;
			}
		}
		if (tileKitSheetCell.X >= 0)
			OutlineSheetCell(draw, origin, tileKitSheetCell, ImGuiColor(1, 0.85f, 0.3f, 1));
		ImGui.EndChild();

		if (tileKitSheetCell.X < 0)
		{
			ImGui.TextDisabled("click a cell to pick it");
			return;
		}

		imgui.Image(new Subtexture(sheet, new Rect(tileKitSheetCell * tileKitSheetCellSize, new Vector2(tileKitSheetCellSize))), new Vector2(56));
		ImGui.SameLine();
		ImGui.BeginGroup();
		ImGui.TextDisabled($"cell {tileKitSheetCell.X},{tileKitSheetCell.Y}");
		if (ImGui.Button("Replace fill"))
			ImportTileKitSheetCell(draft, addVariant: false);
		ImGui.SameLine();
		if (ImGui.Button("Add as variant"))
			ImportTileKitSheetCell(draft, addVariant: true);
		ImGui.SameLine();
		if (ImGui.Button("Edge from palette"))
		{
			TileKits.DeriveEdges(draft);
			MarkTileKitEdited();
		}
		ImGui.EndGroup();
	}

	void OutlineSheetCell(ImDrawListPtr draw, Vector2 origin, Point2 cell, uint color)
		=> draw.AddRect(origin + cell * tileKitSheetCellSize,
			origin + (cell + Point2.One) * tileKitSheetCellSize, color, 0, ImDrawFlags.None, 1.5f);

	void SelectTileKitSheet(string name)
	{
		tileKitSheetName = name;
		tileKitSheetCell = new(-1, -1);
		tileKitSheetImage?.Dispose();
		tileKitSheetTexture?.Dispose();
		tileKitSheetImage = new Image(Path.Join(Assets.SourcesPath, "Tilesets", $"{name}.png"));
		tileKitSheetTexture = new Texture(GraphicsDevice, tileKitSheetImage, name: "TileKitSheet");
	}

	void ImportTileKitSheetCell(TileKitSpec draft, bool addVariant)
	{
		if (tileKitSheetImage is not { } sheet)
			return;

		TileKits.ImportSheetCell(draft, sheet, tileKitSheetCell, tileKitSheetCellSize, tileKitSheetColors, addVariant);
		ApplyTileKitHeightMode(draft);
		tileKitPaintKey = draft.Palette.Keys.OrderBy(it => it).FirstOrDefault('a');
		MarkTileKitEdited();
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
			// Freeze the char the kit is holding right now into the spec, so adding or removing
			// other tile types later can't shuffle it and churn every room legend that names it.
			// Kits authored before this stamp themselves the first time they are saved.
			if (draft.PreferredChar == '\0')
				draft.PreferredChar = Factory.CharForName(draft.Name) ?? Factory.PickCharFor(draft.Name);
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
