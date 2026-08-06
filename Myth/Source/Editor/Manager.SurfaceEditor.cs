#if EDITOR
using System.Numerics;
using Foster.Framework;
using ImGuiNET;
using static ImGuiNET.ImGui;

namespace Myth;

public partial class Manager
{
	bool surfaceEditorOpen;
	bool surfaceEditorFocusRequested;
	SurfaceSpec? surfaceDraft;
	string newSurfaceName = "";
	bool randomSurfaceColorsFromPalettes = true;
	readonly HashSet<SurfaceLayerSpec> unlockedSurfaceTiling = [];
	readonly Dictionary<string, Target> surfacePreviewCache = new(StringComparer.OrdinalIgnoreCase);
	Batcher? surfacePreviewBatcher;
	static readonly string[] SurfaceBlendNames = Enum.GetNames<SurfaceBlend>();
	static readonly string[] SurfaceRotationNames = Enum.GetNames<SurfaceRotation>();
	const string SurfaceUndoSlot = "surface";
	const int SurfacePreviewSize = 48;
	// Palette hex values from Lospec. Keep these local so randomization stays instant/offline.
	static readonly SurfacePalette[] SurfacePalettes =
	[
		new("Neon Night Sky", [0x131626, 0x4d4d80, 0xe6a1cf, 0xffe6ea]),
		new("Red-Blood Pain", [0x7e1f23, 0xc4181f, 0x120a19, 0x5e4069]),
		new("2 Bit Matrix", [0xf2fff2, 0xadd9bc, 0x5b8c7c, 0x0d1a1a]),
		new("Rust Gold 8", [0xf6cd26, 0xac6b26, 0x563226, 0x331c17, 0xbb7f57, 0x725956, 0x393939, 0x202020]),
		new("Retro 8", [0x041026, 0x051a44, 0x1f0398, 0x5000c3, 0x7521ee, 0x0099c3, 0x00c38f, 0x3ee3b7]),
		new("Sweetie 16", [0x1a1c2c, 0x5d275d, 0xb13e53, 0xef7d57, 0xffcd75, 0xa7f070, 0x38b764, 0x257179, 0x29366f, 0x3b5dc9, 0x41a6f6, 0x73eff7, 0xf4f4f4, 0x94b0c2, 0x566c86, 0x333c57]),
		new("PICO-8", [0x000000, 0x1d2b53, 0x7e2553, 0x008751, 0xab5236, 0x5f574f, 0xc2c3c7, 0xfff1e8, 0xff004d, 0xffa300, 0xffec27, 0x00e436, 0x29adff, 0x83769c, 0xff77a8, 0xffccaa]),
		new("Mutant16", [0x000000, 0x2d303e, 0x4f575b, 0x828880, 0xb9b9a8, 0xab8e5e, 0x855e43, 0x4b2b29, 0x75629b, 0x454969, 0x586b4e, 0x958a4a, 0xd2a74d, 0xd17c3d, 0xb04731, 0x8a2f2d]),
		new("Greyt-bit", [0x574368, 0x8488d3, 0xcfd3c1, 0xf8c868, 0x8ddb34, 0x69cfef, 0xd1b3ff, 0xff8e65]),
		new("Velvet Cherry GB", [0x2d162c, 0x412752, 0x683a68, 0x9775a6]),
		new("Fiery Plague GB", [0x1a2129, 0x312137, 0x512839, 0x713141]),
		new("Moon Crystal", [0xffe2db, 0xd9a7c6, 0x8d89c7, 0x755f9c]),
		new("Cosmic cream - GB", [0x220780, 0x615fed, 0xffba5a, 0xfffab2]),
		new("pong4", [0x5c4bff, 0xfdfff2, 0x14e1ff, 0xff7ad6]),
		new("Hexpress4", [0x553840, 0x9b6859, 0xbebc6a, 0xedf8c8]),
		new("1-BIT ERROR 4", [0x060010, 0xd2b7ff, 0x556fd7, 0xd75555]),
		new("Gooseberry Thistle", [0x48376c, 0x6c56ab, 0xb883b9, 0xbfdce1]),
		new("Citrink", [0xffffff, 0xfcf660, 0xb2d942, 0x52c33f, 0x166e7a, 0x254d70, 0x252446, 0x201533]),
		new("Dream Haze 8", [0x3c42c4, 0x6e51c8, 0xa065cd, 0xce79d2, 0xd68fb8, 0xdda2a3, 0xeac4ae, 0xf4dfbe]),
		new("Lord of the Maps", [0xc0c188, 0xa09878, 0x526143, 0x272910, 0x866350, 0x823f3a, 0x432015, 0x1c1009]),
		new("Octoyuu Pretendified 2022", [0x7f95b1, 0x6d5089, 0x492045, 0x713547, 0xb14b54, 0xf08477, 0xf9a9ab, 0xf9ddab]),
		new("Fantshat8", [0x91f2df, 0x4cbdd9, 0x53708c, 0x5b662c, 0x2e5342, 0x253140, 0x13243d, 0x1b1721]),
		new("18 Bytes", [0xc8d0d8, 0xc8c8a0, 0xb89888, 0xa07098, 0x302828, 0x283058, 0x506878, 0x609068]),
		new("Neon Pulse 8", [0x1a0f2e, 0x241649, 0x391f59, 0x6e348c, 0xdf35c1, 0xc9ff00, 0x00e5e5, 0xf4d0d0]),
		new("Pastry Gradient", [0x533962, 0x72446a, 0x9c5376, 0xd06b80, 0xea8694, 0xf6a2a8, 0xfdd8d1, 0xffffff]),
		new("BREEZE8", [0x020226, 0x161860, 0x0c18c0, 0x6b5cf5, 0x63abff, 0xffba63, 0xf96280, 0xfffaf5]),
		new("SilverLining", [0x2e2c3e, 0x3d3141, 0x7a616a, 0xc49483, 0xddbea0, 0xfff8ca, 0xdcebe2, 0xb7ddfd]),
		new("Summers Past-16", [0x320011, 0x5f3a60, 0x876672, 0xb7a39d, 0xece8c2, 0x6db7c3, 0x5e80b2, 0x627057, 0x8da24e, 0xd2cb3e, 0xf7d554, 0xe8bf92, 0xe78c5b, 0xc66f5e, 0xc33846, 0x933942]),
		new("adl16", [0x010d1a, 0x482a37, 0x162f62, 0x5d285a, 0x195740, 0xaf2841, 0x9a5854, 0x357bd8, 0x888283, 0x43ab72, 0xf78057, 0x83c3d4, 0xfdb193, 0xc2e059, 0xffda57, 0xfff3ee]),
		new("AAP-DGA16", [0x010101, 0x031b75, 0x108c00, 0x17bbd3, 0x720c0a, 0x6c1c9e, 0xb25116, 0xb8b0a8, 0x4a4842, 0x0b63c4, 0x9bce00, 0x73f5d5, 0xe89e00, 0xff7bdb, 0xfef255, 0xfffffe]),
		new("Birdguy's 2x2 Lite", [0x1d1227, 0xfbfbfc, 0xbdc6e3, 0x685d8d, 0x848b92, 0x444556, 0xe3c59d, 0x9f686a, 0xec4942, 0x921c3b, 0xa566c3, 0x70295f, 0xf6c536, 0xd0631c, 0xafc144, 0x298147]),
		new("Breaking Point", [0xf5f5fa, 0x050505, 0xe2e7cb, 0xd1cead, 0xc4b5a0, 0xad9488, 0xcc94a3, 0xf66af3, 0xbc56e9, 0x7441d5, 0x0e0e26, 0x363c78, 0x4a6e8d, 0x6a9d81, 0x89b386, 0xb5d0aa]),
		new("Bit-Dream", [0x000000, 0x490000, 0x004900, 0x22123b, 0xffa044, 0xc84c0c, 0x6f8b6f, 0x5c70ff, 0xfcd8a8, 0xf01851, 0x00a830, 0x5eccf7, 0xfffff1, 0xf79c9c, 0xb6ff49, 0xdb6dff]),
		new("Leflautt0", [0x121733, 0x6a4173, 0xc28d98, 0xd9ce9e, 0x3c7368, 0x3f4873, 0x5f6373, 0x5e8853, 0xa1cc95, 0x8ebdb2, 0xe1ebdf, 0x13332d, 0x8f7d45, 0x664047, 0x30142e, 0xb8354f]),
		new("Xarss16", [0x111013, 0x2f2442, 0x455263, 0x57294d, 0x9d502f, 0xd67f4c, 0xd7d06a, 0xe4e5e1, 0xa73540, 0xd27370, 0x84c760, 0x9fa8ad, 0x466baf, 0x78c4d9, 0x348964, 0x68787d]),
		new("Dotoppenal16", [0x442831, 0x623a46, 0x755151, 0xa17a60, 0x2e432f, 0x375c51, 0x566f95, 0x678cae, 0xdc7d7d, 0xf5b0ac, 0xf6e4c1, 0xb1a29d, 0x6aa8bd, 0xabf1f5, 0xc1d3f7, 0xa8b7c6]),
		new("DarkAutumn", [0xd9543d, 0xcc2b36, 0xbd173c, 0xaa004a, 0x8c0062, 0x5e005f, 0x2d0045, 0x0b0124, 0x002c60, 0x005678, 0x008275, 0x00945b, 0x01ab3c, 0x24c927, 0x70de40, 0xb6eb67]),
		new("Gothic Bit", [0x0e0e12, 0x1a1a24, 0x333346, 0x535373, 0x8080a4, 0xa6a6bf, 0xc1c1d2, 0xe6e6ec]),
	];

	static string SurfaceSpecPath(string id)
		=> Path.Join(Assets.SourcesPath, "Surfaces", $"{id}.json");

	void SurfaceEditorImGui()
	{
		if (!surfaceEditorOpen)
			return;

		if (surfaceEditorFocusRequested)
		{
			surfaceEditorFocusRequested = false;
			SetNextWindowFocus();
		}

		SetNextWindowSize(new Vector2(460, 700), ImGuiCond.FirstUseEver);
		DockNextToolWindow("Surfaces");
		if (!Begin("Surfaces", ref surfaceEditorOpen))
		{
			NoteDockedWindow();
			End();
			return;
		}
		NoteDockedWindow();

		if (surfaceDraft == null && Assets.Surfaces.Count > 0)
			SelectSurface(Assets.Surfaces.Keys.First());

		DrawSurfaceHeader();
		Separator();
		if (surfaceDraft is { } draft)
		{
			BeginChild("SurfaceBody");
			DrawSurfaceLayers(draft);
			DrawSurfaceNormals(draft);
			EndChild();
		}
		else
			TextDisabled("Create a Surface to get started.");

		DrawCreateSurfacePopup();
		End();
	}

	void OpenSurfaceEditor(string id)
	{
		surfaceEditorOpen = true;
		surfaceEditorFocusRequested = true;
		if (surfaceDraft?.Id != id)
			SelectSurface(id);
	}

	void DrawSurfaceHeader()
	{
		SetNextItemWidth(-90);
		if (BeginCombo("##Surface", surfaceDraft?.Id ?? "select surface"))
		{
			foreach (var id in Assets.Surfaces.Keys)
				if (Selectable(id, id == surfaceDraft?.Id))
					SelectSurface(id);
			EndCombo();
		}
		SameLine();
		if (Button("New", new Vector2(-1, 0)))
		{
			newSurfaceName = "";
			OpenPopup("New Surface");
		}
	}

	void DrawCreateSurfacePopup()
	{
		var open = true;
		if (!BeginPaddedPopupModal("New Surface", ref open, ImGuiWindowFlags.AlwaysAutoResize))
			return;

		TextUnformatted("Create a new Surface:");
		SetNextItemWidth(240);
		if (IsWindowAppearing())
			SetKeyboardFocusHere();
		var submit = InputTextWithHint("##NewSurfaceName", "surface name", ref newSurfaceName, 64,
			ImGuiInputTextFlags.EnterReturnsTrue);
		var id = newSurfaceName.Trim().ToLowerInvariant();
		var valid = CanCreateSurface(id);
		BeginDisabled(!valid);
		var create = Button("Create", new Vector2(90, 0)) || submit;
		EndDisabled();
		SameLine();
		var cancel = Button("Cancel", new Vector2(90, 0)) || !open;

		if (create && valid)
		{
			CreateSurface(id, randomize: false);
			newSurfaceName = "";
			CloseCurrentPopup();
		}
		else if (cancel)
		{
			newSurfaceName = "";
			CloseCurrentPopup();
		}

		EndPaddedPopup();
	}

	static bool CanCreateSurface(string id)
		=> id.Length > 0 && char.IsAsciiLetter(id[0])
			&& id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_')
			&& !File.Exists(SurfaceSpecPath(id));

	string CreateRandomSurface()
	{
		for (var i = 0; i < 128; i++)
		{
			var id = Assets.GenerateRandomRoomId();
			if (CanCreateSurface(id))
				return CreateSurface(id, randomize: true);
		}
		return CreateSurface($"s_{Guid.NewGuid():N}", randomize: true);
	}

	string CreateSurface(string id, bool randomize)
	{
		using (CaptureSurfaceUndo(id, "Create Surface"))
		{
			unlockedSurfaceTiling.Clear();
			surfaceDraft = new SurfaceSpec { Id = id };
			if (Assets.Textures2D?.Order.Count > 0)
			{
				var layers = randomize ? Random.Shared.Next(2, 4) : 1;
				SurfacePalette? palette = randomize && randomSurfaceColorsFromPalettes ? RandomSurfacePalette() : null;
				for (var i = 0; i < layers; i++)
				{
					var layer = new SurfaceLayerSpec { Texture = Assets.Textures2D.Order[0] };
					if (randomize)
						RandomizeSurfaceLayer(layer, i, tilingLocked: true, palette);
					surfaceDraft.Layers.Add(layer);
				}
			}
			ApplySurfaceEdit(surfaceDraft);
		}
		return id;
	}

	void SelectSurface(string id)
	{
		undo.CommitPending(SurfaceUndoSlot);
		try
		{
			unlockedSurfaceTiling.Clear();
			surfaceDraft = SurfaceSpec.Load(SurfaceSpecPath(id));
		}
		catch (Exception e)
		{
			Console.Error.WriteLine($"Failed to edit surface '{id}': {e.Message}");
		}
	}

	void DrawSurfaceLayers(SurfaceSpec draft)
	{
		if (!CollapsingHeader($"Layers ({draft.Layers.Count}/{SurfaceSpec.MaxLayers})", ImGuiTreeNodeFlags.DefaultOpen))
			return;

		BeginDisabled(draft.Layers.Count == 0);
		if (SmallButton($"{EditorIcons.Randomize}##RandomizeLayers"))
		{
			using (CaptureSurfaceUndo(draft.Id))
			{
				SurfacePalette? palette = randomSurfaceColorsFromPalettes ? RandomSurfacePalette() : null;
				for (var i = 0; i < draft.Layers.Count; i++)
					RandomizeSurfaceLayer(draft.Layers[i], i, !unlockedSurfaceTiling.Contains(draft.Layers[i]), palette);
				ApplySurfaceEdit(draft);
			}
		}
		EndDisabled();
		if (IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
			SetTooltip("Randomize all layers");

		SameLine();
		Checkbox("Palettes", ref randomSurfaceColorsFromPalettes);


		for (var i = 0; i < draft.Layers.Count; i++)
		{
			var layer = draft.Layers[i];
			PushID(i);
			if (i > 0)
				Separator();
			var open = TreeNodeEx("##Layer", ImGuiTreeNodeFlags.DefaultOpen);
			SameLine();
			BeginDisabled(i == 0);
			if (SmallButton($"{EditorIcons.Up}##Up"))
			{
				using (CaptureSurfaceUndo(draft.Id))
				{
					(draft.Layers[i - 1], draft.Layers[i]) = (draft.Layers[i], draft.Layers[i - 1]);
					ApplySurfaceEdit(draft);
				}
			}
			EndDisabled();
			if (IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
				SetTooltip("Move layer up");
			SameLine();
			BeginDisabled(i + 1 == draft.Layers.Count);
			if (SmallButton($"{EditorIcons.Down}##Down"))
			{
				using (CaptureSurfaceUndo(draft.Id))
				{
					(draft.Layers[i + 1], draft.Layers[i]) = (draft.Layers[i], draft.Layers[i + 1]);
					ApplySurfaceEdit(draft);
				}
			}
			EndDisabled();
			if (IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
				SetTooltip("Move layer down");
			SameLine();
			if (SmallButton($"{EditorIcons.Randomize}##RandomizeLayer"))
			{
				using (CaptureSurfaceUndo(draft.Id))
				{
					RandomizeSurfaceLayer(layer, i, !unlockedSurfaceTiling.Contains(layer), randomSurfaceColorsFromPalettes ? RandomSurfacePalette() : null);
					ApplySurfaceEdit(draft);
				}
			}
			if (IsItemHovered())
				SetTooltip("Randomize layer");
			SameLine();
			if (SmallButton("x"))
			{
				using (CaptureSurfaceUndo(draft.Id))
				{
					unlockedSurfaceTiling.Remove(layer);
					draft.Layers.RemoveAt(i--);
					ApplySurfaceEdit(draft);
				}
				if (open)
					TreePop();
				PopID();
				continue;
			}

			if (open)
			{
				var texture = layer.Texture;
				var textureRow = BeginPropertyRow("Texture", hasTrailingButton: true);
				if (DrawSurfaceTexturePicker(ref texture, textureRow.ControlWidth))
				{
					using (CaptureSurfaceUndo(draft.Id))
					{
						layer.Texture = texture;
						ApplySurfaceEdit(draft);
					}
				}

				var blend = (int)layer.Blend;
				BeginPropertyRow("Blend");
				if (Combo("##Blend", ref blend, SurfaceBlendNames, SurfaceBlendNames.Length))
				{
					using (CaptureSurfaceUndo(draft.Id))
					{
						layer.Blend = (SurfaceBlend)blend;
						ApplySurfaceEdit(draft);
					}
				}

				var luminance = Assets.Textures2D?.Get(layer.Texture)?.Swizzle == Assets.TextureSwizzle.Luminance;
				var tint = (Vector4)SurfaceSpec.ParseTint(layer.Tint);
				BeginPropertyRow(luminance ? "White color" : "Tint", hasTrailingButton: true);
				if (ColorEdit4("##WhiteColor", ref tint, ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.AlphaBar))
				{
					BeginSurfaceUndo(draft.Id);
					layer.Tint = TintHex(new Color(tint));
					ApplySurfaceEdit(draft);
				}
				CommitSurfaceUndoAfterEdit();
				SameLine();
				if (SmallButton($"{EditorIcons.Randomize}##RandomizeWhite"))
				{
					using (CaptureSurfaceUndo(draft.Id))
					{
						layer.Tint = TintHex(RandomSurfaceColor());
						ApplySurfaceEdit(draft);
					}
				}
				if (IsItemHovered())
					SetTooltip(luminance ? "Randomize white color" : "Randomize tint");
				if (luminance)
				{
					var blackColor = layer.BlackColor != null
						? SurfaceSpec.ParseTint(layer.BlackColor)
						: new Color(0, 0, 0, new Color(tint).A);
					var black = (Vector4)blackColor;
					BeginPropertyRow("Black color", hasTrailingButton: true);
					if (ColorEdit4("##BlackColor", ref black, ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.AlphaBar))
					{
						BeginSurfaceUndo(draft.Id);
						layer.BlackColor = TintHex(new Color(black));
						ApplySurfaceEdit(draft);
					}
					CommitSurfaceUndoAfterEdit();
					SameLine();
					if (SmallButton($"{EditorIcons.Randomize}##RandomizeBlack"))
					{
						using (CaptureSurfaceUndo(draft.Id))
						{
							layer.BlackColor = TintHex(RandomSurfaceColor());
							ApplySurfaceEdit(draft);
						}
					}
					if (IsItemHovered())
						SetTooltip("Randomize black color");
				}

				var opacity = layer.Opacity;
				BeginPropertyRow("Opacity");
				if (DragFloat("##Opacity", ref opacity, 0.01f, 0, 1, "%.2f"))
				{
					BeginSurfaceUndo(draft.Id);
					layer.Opacity = Math.Clamp(opacity, 0, 1);
					ApplySurfaceEdit(draft);
				}
				CommitSurfaceUndoAfterEdit();

				var tiling = layer.Tiling is { Length: >= 2 }
					? new Vector2(layer.Tiling[0], layer.Tiling[1])
					: new Vector2(48);
				var oldTiling = tiling;
				var tilingLocked = !unlockedSurfaceTiling.Contains(layer);
				BeginPropertyRow("Tile size", hasTrailingButton: true);
				if (DragFloat2("##TileSize", ref tiling, 1, 1, 2048, "%.0f px"))
				{
					BeginSurfaceUndo(draft.Id);
					if (tilingLocked)
					{
						if (tiling.X != oldTiling.X && tiling.Y == oldTiling.Y)
							tiling.Y = tiling.X;
						else if (tiling.Y != oldTiling.Y)
							tiling.X = tiling.Y;
					}
					tiling = Vector2.Max(tiling, Vector2.One);
					layer.Tiling = [tiling.X, tiling.Y];
					ApplySurfaceEdit(draft);
				}
				CommitSurfaceUndoAfterEdit();
				SameLine();
				if (SmallButton($"{(tilingLocked ? EditorIcons.Lock : EditorIcons.Unlock)}##TileSizeLock"))
				{
					if (tilingLocked)
						unlockedSurfaceTiling.Add(layer);
					else
						unlockedSurfaceTiling.Remove(layer);
				}
				if (IsItemHovered())
					SetTooltip(tilingLocked ? "Unlock tile width and height" : "Lock tile width and height");

				var offset = layer.Offset is { Length: >= 2 }
					? new Vector2(layer.Offset[0], layer.Offset[1])
					: Vector2.Zero;
				BeginPropertyRow("Offset");
				if (DragFloat2("##Offset", ref offset, 1, -4096, 4096, "%.0f px"))
				{
					BeginSurfaceUndo(draft.Id);
					layer.Offset = [offset.X, offset.Y];
					ApplySurfaceEdit(draft);
				}
				CommitSurfaceUndoAfterEdit();

				var scroll = layer.Scroll is { Length: >= 2 }
					? new Vector2(layer.Scroll[0], layer.Scroll[1])
					: Vector2.Zero;
				BeginPropertyRow("Motion");
				if (DragFloat2("##Motion", ref scroll, 0.25f, -1024, 1024, "%.1f px/s"))
				{
					BeginSurfaceUndo(draft.Id);
					layer.Scroll = [scroll.X, scroll.Y];
					ApplySurfaceEdit(draft);
				}
				CommitSurfaceUndoAfterEdit();

				var rotation = (int)layer.Rotation;
				BeginPropertyRow("Rotation");
				if (Combo("##Rotation", ref rotation, SurfaceRotationNames, SurfaceRotationNames.Length))
				{
					using (CaptureSurfaceUndo(draft.Id))
					{
						layer.Rotation = (SurfaceRotation)rotation;
						ApplySurfaceEdit(draft);
					}
				}

				BeginPropertyRow("Flip");
				var flipX = layer.FlipX;
				var flipY = layer.FlipY;
				if (Checkbox("X##FlipX", ref flipX))
				{
					using (CaptureSurfaceUndo(draft.Id))
					{
						layer.FlipX = flipX;
						ApplySurfaceEdit(draft);
					}
				}
				SameLine();
				if (Checkbox("Y##FlipY", ref flipY))
				{
					using (CaptureSurfaceUndo(draft.Id))
					{
						layer.FlipY = flipY;
						ApplySurfaceEdit(draft);
					}
				}

				var contrast = layer.Contrast;
				BeginPropertyRow("Contrast");
				if (DragFloat("##Contrast", ref contrast, 0.01f, 0, 4, "%.2f"))
				{
					BeginSurfaceUndo(draft.Id);
					layer.Contrast = Math.Clamp(contrast, 0, 4);
					ApplySurfaceEdit(draft);
				}
				CommitSurfaceUndoAfterEdit();

				var bias = layer.Bias;
				BeginPropertyRow("Bias");
				if (DragFloat("##Bias", ref bias, 0.01f, -1, 1, "%.2f"))
				{
					BeginSurfaceUndo(draft.Id);
					layer.Bias = Math.Clamp(bias, -1, 1);
					ApplySurfaceEdit(draft);
				}
				CommitSurfaceUndoAfterEdit();

				var quantize = layer.Quantize;
				BeginPropertyRow("Quantize");
				if (DragFloat("##Quantize", ref quantize, 1, 0, 32, "%.0f levels"))
				{
					BeginSurfaceUndo(draft.Id);
					layer.Quantize = Math.Clamp(MathF.Round(quantize), 0, 32);
					ApplySurfaceEdit(draft);
				}
				CommitSurfaceUndoAfterEdit();
				TreePop();
			}
			PopID();
		}

		BeginDisabled(draft.Layers.Count >= SurfaceSpec.MaxLayers || Assets.Textures2D?.Order.Count == 0);
		if (SmallButton("+ layer") && Assets.Textures2D is { Order.Count: > 0 } bucket)
		{
			using (CaptureSurfaceUndo(draft.Id))
			{
				draft.Layers.Add(new SurfaceLayerSpec { Texture = bucket.Order[0] });
				ApplySurfaceEdit(draft);
			}
		}
		EndDisabled();
	}

	bool DrawSurfaceTexturePicker(ref string value, float controlWidth, bool normalOnly = false)
	{
		if (Assets.Textures2D is not { } bucket)
		{
			TextDisabled("No Surface textures loaded");
			return false;
		}

		var changed = false;
		if (bucket.Get(value) is { } current)
		{
			if (ImGuiSubtextureButton("##Texture", current.Thumbnail, current.Key, false,
				new Vector2(MathF.Min(ButtonSize, controlWidth), MathF.Min(ButtonSize, controlWidth)), true))
				OpenPopup("##TextureGrid");
		}
		else if (Button("Missing##Texture", new Vector2(MathF.Min(ButtonSize, controlWidth), MathF.Min(ButtonSize, controlWidth))))
			OpenPopup("##TextureGrid");
		SameLine();
		if (SmallButton($"{EditorIcons.Randomize}##RandomizeTexture") && RandomSurfaceTexture(bucket, normalOnly, value) is { } random)
		{
			value = random;
			changed = true;
		}
		if (IsItemHovered())
			SetTooltip("Randomize texture");

		if (BeginPopup("##TextureGrid"))
		{
			const int Columns = 6;
			var shown = 0;
			for (var i = 0; i < bucket.Order.Count; i++)
			{
				var key = bucket.Order[i];
				var texture = bucket.Textures[key];
				if (normalOnly && texture.Swizzle != Assets.TextureSwizzle.Normal)
					continue;
				if (shown++ % Columns != 0)
					SameLine();
				if (ImGuiSubtextureButton($"##texture{i}", texture.Thumbnail, key, value == key,
					new Vector2(ButtonSize, ButtonSize), true))
				{
					value = key;
					changed = true;
					CloseCurrentPopup();
				}
			}
			EndPopup();
		}
		return changed;
	}

	void RandomizeSurfaceLayer(SurfaceLayerSpec layer, int index, bool tilingLocked, SurfacePalette? palette = null)
	{
		if (Assets.Textures2D is { } bucket)
			layer.Texture = RandomSurfaceTexture(bucket, normalOnly: false) ?? layer.Texture;
		layer.Blend = index == 0 ? SurfaceBlend.Over : (SurfaceBlend)Random.Shared.Next(SurfaceBlendNames.Length);
		layer.Tint = TintHex(RandomSurfaceColor(palette));
		layer.BlackColor = TintHex(RandomSurfaceColor(palette));
		layer.Opacity = Random.Shared.NextSingle() * 0.7f + 0.3f;
		var width = Random.Shared.Next(8, 129);
		layer.Tiling = [width, tilingLocked ? width : Random.Shared.Next(8, 129)];
		layer.Offset = [Random.Shared.Next(-64, 65), Random.Shared.Next(-64, 65)];
		layer.Scroll = [Random.Shared.Next(-7, 8), Random.Shared.Next(-7, 8)];
		layer.Rotation = (SurfaceRotation)Random.Shared.Next(SurfaceRotationNames.Length);
		layer.FlipX = Random.Shared.Next(2) == 0;
		layer.FlipY = Random.Shared.Next(2) == 0;
		layer.Contrast = MathF.Round((Random.Shared.NextSingle() * 2.5f + 0.5f) * 100f) / 100f;
		layer.Bias = MathF.Round((Random.Shared.NextSingle() * 0.8f - 0.4f) * 100f) / 100f;
		layer.Quantize = Random.Shared.Next(4) == 0 ? Random.Shared.Next(2, 9) : 0;
	}

	static SurfacePalette RandomSurfacePalette() => SurfacePalettes[Random.Shared.Next(SurfacePalettes.Length)];

	static Color RandomSurfaceColor(SurfacePalette? palette = null)
	{
		if (palette is { } p)
			return new Color((int)p.Colors[Random.Shared.Next(p.Colors.Length)]);
		return new((byte)Random.Shared.Next(256), (byte)Random.Shared.Next(256), (byte)Random.Shared.Next(256), 255);
	}

	static string? RandomSurfaceTexture(Assets.TextureBucket bucket, bool normalOnly, string? except = null)
	{
		var count = 0;
		foreach (var key in bucket.Order)
			if ((!normalOnly || bucket.Textures[key].Swizzle == Assets.TextureSwizzle.Normal)
				&& !string.Equals(key, except, StringComparison.OrdinalIgnoreCase))
				count++;
		if (count == 0)
			return except;

		var selected = Random.Shared.Next(count);
		foreach (var key in bucket.Order)
			if ((!normalOnly || bucket.Textures[key].Swizzle == Assets.TextureSwizzle.Normal)
				&& !string.Equals(key, except, StringComparison.OrdinalIgnoreCase) && selected-- == 0)
				return key;
		return null;
	}

	void DrawSurfaceNormals(SurfaceSpec draft)
	{
		if (!CollapsingHeader("Normals", ImGuiTreeNodeFlags.DefaultOpen))
			return;

		var mode = !string.IsNullOrEmpty(draft.NormalTexture) ? 2 : draft.NormalFromHeight ? 1 : 0;
		string? normalTexture = null;
		if (Assets.Textures2D is { } bucket)
			foreach (var key in bucket.Order)
				if (bucket.Textures[key].Swizzle == Assets.TextureSwizzle.Normal)
				{
					normalTexture = key;
					break;
				}
		BeginPropertyRow("Source");
		if (DrawSurfaceNormalSource(ref mode, normalTexture != null))
		{
			using (CaptureSurfaceUndo(draft.Id))
			{
				draft.NormalFromHeight = mode == 1;
				draft.NormalTexture = mode == 2 ? normalTexture : null;
				ApplySurfaceEdit(draft);
			}
		}

		if (mode == 1 && draft.Layers.Count > 0)
		{
			var layer = Math.Clamp(draft.HeightFromLayer, 0, draft.Layers.Count - 1);
			BeginPropertyRow("Height layer");
			if (BeginCombo("##HeightLayer", $"{layer + 1}: {Path.GetFileName(draft.Layers[layer].Texture)}"))
			{
				for (var i = 0; i < draft.Layers.Count; i++)
					if (Selectable($"{i + 1}: {Path.GetFileName(draft.Layers[i].Texture)}", layer == i))
					{
						using (CaptureSurfaceUndo(draft.Id))
						{
							draft.HeightFromLayer = i;
							ApplySurfaceEdit(draft);
						}
					}
				EndCombo();
			}
		}
		else if (mode == 2)
		{
			var texture = draft.NormalTexture ?? "";
			var textureRow = BeginPropertyRow("Normal texture", hasTrailingButton: true);
			if (DrawSurfaceTexturePicker(ref texture, textureRow.ControlWidth, normalOnly: true))
			{
				using (CaptureSurfaceUndo(draft.Id))
				{
					draft.NormalTexture = texture;
					ApplySurfaceEdit(draft);
				}
			}
		}

		if (mode != 0)
		{
			var strength = draft.NormalStrength;
			BeginPropertyRow("Strength");
			if (SliderFloat("##Strength", ref strength, 0, 4))
			{
				BeginSurfaceUndo(draft.Id);
				draft.NormalStrength = strength;
				ApplySurfaceEdit(draft);
			}
			CommitSurfaceUndoAfterEdit();
		}
	}

	static bool DrawSurfaceNormalSource(ref int mode, bool hasNormalTexture)
	{
		var changed = false;
		var names = (ReadOnlySpan<string>)["None", "Height from layer", "Texture"];
		if (BeginCombo("##Source", names[mode]))
		{
			for (var i = 0; i < names.Length; i++)
			{
				BeginDisabled(i == 2 && !hasNormalTexture);
				if (Selectable(names[i], mode == i))
				{
					mode = i;
					changed = true;
				}
				EndDisabled();
			}
			EndCombo();
		}
		return changed;
	}

	JsonUndoScope CaptureSurfaceUndo(string id, string name = "Edit Surface")
		=> undo.Capture(name, new SurfaceUndoTarget(this, id));

	void BeginSurfaceUndo(string id)
	{
		if (!undo.HasPending(SurfaceUndoSlot, id))
			undo.OpenPending(SurfaceUndoSlot, id, "Edit Surface", new SurfaceUndoTarget(this, id));
	}

	void CommitSurfaceUndoAfterEdit()
	{
		if (IsItemDeactivatedAfterEdit())
			undo.CommitPending(SurfaceUndoSlot);
	}

	void ApplySurfaceEdit(SurfaceSpec spec)
	{
		spec.Save(SurfaceSpecPath(spec.Id));
		Assets.SetSurface(GraphicsDevice, spec);
		InvalidateSurfacePreview(spec.Id);
		InvalidateSurfaceUsers(spec.Id);
	}

	Subtexture GetSurfacePreview(Surface surface)
	{
		if (!surfacePreviewCache.TryGetValue(surface.Id, out var target))
		{
			target = new Target(GraphicsDevice, SurfacePreviewSize, SurfacePreviewSize, name: $"SurfacePreview/{surface.Id}");
			target.Clear(new Color(0x24222a));
			surfacePreviewBatcher ??= new Batcher(GraphicsDevice, "SurfacePreviews");
			for (var y = 0; y < SurfacePreviewSize; y += 8)
				for (var x = 0; x < SurfacePreviewSize; x += 8)
					if ((x + y) % 16 == 0)
						surfacePreviewBatcher.Rect(new Rect(x, y, 8, 8), new Color(0x36333e));
			surface.Render(surfacePreviewBatcher, new Rect(0, 0, SurfacePreviewSize, SurfacePreviewSize), 0);
			surfacePreviewBatcher.Render(target);
			surfacePreviewBatcher.Clear();
			surfacePreviewCache[surface.Id] = target;
		}
		return new Subtexture(target.Attachments[0]);
	}

	void InvalidateSurfacePreview(string id)
	{
		if (surfacePreviewCache.Remove(id, out var target))
			deferredRoomPreviewDisposals.Add(target);
	}

	void InvalidateSurfaceUsers(string id)
	{
		foreach (var room in Assets.RoomsById.Values)
		{
			var usesSurface = string.Equals(room.BackdropSurfaceId, id, StringComparison.OrdinalIgnoreCase);
			for (var i = 0; !usesSurface && i < room.Actors.Count; i++)
				usesSurface = string.Equals(ActorSpecs.ReadString(room.Actors[i], "SurfaceId"), id, StringComparison.OrdinalIgnoreCase);
			if (usesSurface)
				MarkRoomPreviewDirty(room);
		}
	}

	void ClearSurfacePreviewCache(bool deferDisposal = true)
	{
		foreach (var target in surfacePreviewCache.Values)
			if (deferDisposal)
				deferredRoomPreviewDisposals.Add(target);
			else
				target.Dispose();
		surfacePreviewCache.Clear();
	}

	sealed class SurfaceUndoTarget(Manager manager, string id) : IJsonUndoTarget
	{
		public string UndoId => $"surface:{id}";

		public string CaptureJson()
		{
			if (manager.surfaceDraft?.Id == id)
				return manager.surfaceDraft.ToJson();
			var path = SurfaceSpecPath(id);
			return File.Exists(path) ? File.ReadAllText(path) : "";
		}

		public void ApplyJson(string json)
		{
			var path = SurfaceSpecPath(id);
			if (json.Length == 0)
			{
				if (File.Exists(path))
					File.Delete(path);
				Assets.RemoveSurface(id);
				manager.InvalidateSurfacePreview(id);
				manager.InvalidateSurfaceUsers(id);
				if (manager.surfaceDraft?.Id == id)
					manager.surfaceDraft = null;
				return;
			}

			var spec = SurfaceSpec.Parse(json, id);
			manager.unlockedSurfaceTiling.Clear();
			manager.surfaceDraft = spec;
			manager.ApplySurfaceEdit(spec);
		}
	}

	readonly record struct SurfacePalette(string Name, uint[] Colors);

	static string TintHex(Color color)
		=> $"{color.R:x2}{color.G:x2}{color.B:x2}{color.A:x2}";
}
#endif
