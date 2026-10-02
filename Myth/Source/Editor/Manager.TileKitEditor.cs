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
	Texture? tileKitPreviewTexture;

	readonly TileKitSheetBrowser tileKitSheet = new();

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
		{
			ImGui.TextUnformatted(activeDraft.Name);
			if (activeDraft.Source is { } source)
			{
				ImGui.SameLine();
				if (ImGui.Button("Open art in Aseprite"))
					AsepriteLauncher.OpenFile(Path.Join(Assets.SourcesPath, "Tilesets", Assets.KitSourceFolder, $"{source}.ase"));
				if (ImGui.IsItemHovered())
					ImGui.SetTooltip($"{source}.ase -- this kit is the slice named '{activeDraft.Name}',\nits rim is '{activeDraft.Name}-rim' (and -rimtop / -rimbottom).\nSaving there reloads every kit in the file.");
			}
		}
		ImGui.Separator();

		if (tileKitDraft is { } draft)
		{
			var footer = ImGui.GetFrameHeightWithSpacing() + ImGui.GetStyle().ItemSpacing.Y;
			ImGui.BeginChild("KitBody", new Vector2(0, -footer));
			DrawTileKitPreview(draft);
			ImGui.Separator();
			DrawTileKitParams(draft);
			DrawTileKitLighting(draft);
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

	TileKitSpec CreateTileKitDraft(string name, string source, int fillTiles, Factory.TileLayer layer)
	{
		var draft = TileKits.CreateDefault(name, source);
		draft.FillTiles = fillTiles;
		draft.Layer = layer;
		draft.PreferredChar = Factory.PickCharFor(name);
		tileKitDraft = draft;
		tileKitSelected = name;
		tileKitDirty = true;
		tileKitPreviewStale = true;
		return draft;
	}

	void SelectTileKit(TileKitSpec spec)
	{
		tileKitSelected = spec.Name;
		tileKitDraft = TileKits.Clone(spec);
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

	static readonly string[] TileKitNormalKernels = ["sobel (smooth)", "crisp", "facet (pixel-art)"];

	void DrawTileKitLighting(TileKitSpec draft)
	{
		if (!ImGui.CollapsingHeader("Lighting"))
			return;

		ImGui.TextDisabled("relief is the 'height' layer in the source .ase; this is how it is read");
		var kernel = (int)draft.NormalKernel;
		ImGui.SetNextItemWidth(240);
		if (ImGui.Combo("normal kernel", ref kernel, TileKitNormalKernels, TileKitNormalKernels.Length))
		{
			draft.NormalKernel = (Assets.NormalKernel)kernel;
			MarkTileKitEdited();
		}
		if (ImGui.IsItemHovered())
			ImGui.SetTooltip("sobel: smooth, and a 1px ridge comes out flat with its shading on the neighbours\ncrisp: same placement, without the sideways bleed\nfacet: the raised pixel is the lit one -- hand-painted shading");
		var normalStrength = draft.NormalStrength;
		ImGui.SetNextItemWidth(240);
		if (ImGui.SliderFloat("normal strength", ref normalStrength, 0f, 4f))
		{
			draft.NormalStrength = normalStrength;
			MarkTileKitEdited();
		}
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
/// The Tile Creator's sheet picker: which source .png is loaded, which cell of it is selected, and
/// the settings the import reads. Owns the loaded pixels (what the import quantizes) and the preview
/// texture (what the picker draws), and swaps both together when the sheet changes.
sealed class TileKitSheetBrowser
{
	static Point2 NoCell => new(-1, -1);

	string[]? names;

	public string? Name { get; private set; }
	public Image? Pixels { get; private set; }
	public Texture? Preview { get; private set; }

	// Plain fields: ImGui takes these by ref.
	public Point2 Cell = NoCell;
	public int CellSize = Game.TileSize * 2;
	public int Colors = 6;
	public bool Native = true;

	public bool HasCell => Cell.X >= 0;

	/// Found once and kept -- a sheet appearing mid-session is rare enough to not be worth a watcher,
	/// and reopening the Tile Creator picks it up.
	public string[] Names => names ??= [.. Directory.EnumerateFiles(Path.Join(Assets.SourcesPath, "Tilesets"), "*.png")
		.Select(it => Path.GetFileNameWithoutExtension(it)!).Order(StringComparer.OrdinalIgnoreCase)];

	public void Select(GraphicsDevice graphicsDevice, string name)
	{
		Name = name;
		ClearCell();
		Pixels?.Dispose();
		Preview?.Dispose();
		Pixels = new Image(Path.Join(Assets.SourcesPath, "Tilesets", $"{name}.png"));
		Preview = new Texture(graphicsDevice, Pixels, name: "TileKitSheet");
	}

	public void ClearCell() => Cell = NoCell;
}
#endif
