#if EDITOR
using System.Numerics;
using Foster.Framework;
using ImGuiNET;

namespace Myth;

public partial class Manager
{
	bool surfaceEditorOpen;
	bool surfaceEditorFocusRequested;
	SurfaceSpec? surfaceDraft;
	string newSurfaceName = "";
	readonly HashSet<SurfaceLayerSpec> unlockedSurfaceTiling = [];
	readonly Dictionary<string, Target> surfacePreviewCache = new(StringComparer.OrdinalIgnoreCase);
	Batcher? surfacePreviewBatcher;
	static readonly string[] SurfaceBlendNames = Enum.GetNames<SurfaceBlend>();
	const string SurfaceUndoSlot = "surface";
	const int SurfacePreviewSize = 48;

	static string SurfaceSpecPath(string id)
		=> Path.Join(Assets.SourcesPath, "Surfaces", $"{id}.json");

	void SurfaceEditorImGui()
	{
		if (!surfaceEditorOpen)
			return;

		if (surfaceEditorFocusRequested)
		{
			surfaceEditorFocusRequested = false;
			ImGui.SetNextWindowFocus();
		}

		ImGui.SetNextWindowSize(new Vector2(460, 700), ImGuiCond.FirstUseEver);
		DockNextToolWindow("Surfaces");
		if (!ImGui.Begin("Surfaces", ref surfaceEditorOpen))
		{
			NoteDockedWindow();
			ImGui.End();
			return;
		}
		NoteDockedWindow();

		if (surfaceDraft == null && Assets.Surfaces.Count > 0)
			SelectSurface(Assets.Surfaces.Keys.First());

		DrawSurfaceHeader();
		ImGui.Separator();
		if (surfaceDraft is { } draft)
		{
			ImGui.BeginChild("SurfaceBody");
			DrawSurfaceLayers(draft);
			DrawSurfaceNormals(draft);
			ImGui.EndChild();
		}
		else
			ImGui.TextDisabled("Create a Surface to get started.");

		DrawCreateSurfacePopup();
		ImGui.End();
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
		ImGui.SetNextItemWidth(-90);
		if (ImGui.BeginCombo("##Surface", surfaceDraft?.Id ?? "select surface"))
		{
			foreach (var id in Assets.Surfaces.Keys)
				if (ImGui.Selectable(id, id == surfaceDraft?.Id))
					SelectSurface(id);
			ImGui.EndCombo();
		}
		ImGui.SameLine();
		if (ImGui.Button("New", new Vector2(-1, 0)))
		{
			newSurfaceName = "";
			ImGui.OpenPopup("New Surface");
		}
	}

	void DrawCreateSurfacePopup()
	{
		var open = true;
		if (!BeginPaddedPopupModal("New Surface", ref open, ImGuiWindowFlags.AlwaysAutoResize))
			return;

		ImGui.TextUnformatted("Create a new Surface:");
		ImGui.SetNextItemWidth(240);
		if (ImGui.IsWindowAppearing())
			ImGui.SetKeyboardFocusHere();
		var submit = ImGui.InputTextWithHint("##NewSurfaceName", "surface name", ref newSurfaceName, 64,
			ImGuiInputTextFlags.EnterReturnsTrue);
		var id = newSurfaceName.Trim().ToLowerInvariant();
		var valid = CanCreateSurface(id);
		ImGui.BeginDisabled(!valid);
		var create = ImGui.Button("Create", new Vector2(90, 0)) || submit;
		ImGui.EndDisabled();
		ImGui.SameLine();
		var cancel = ImGui.Button("Cancel", new Vector2(90, 0)) || !open;

		if (create && valid)
		{
			using (CaptureSurfaceUndo(id, "Create Surface"))
			{
				unlockedSurfaceTiling.Clear();
				surfaceDraft = new SurfaceSpec { Id = id };
				if (Assets.Textures2D?.Order.Count > 0)
					surfaceDraft.Layers.Add(new SurfaceLayerSpec { Texture = Assets.Textures2D.Order[0] });
				ApplySurfaceEdit(surfaceDraft);
			}
			newSurfaceName = "";
			ImGui.CloseCurrentPopup();
		}
		else if (cancel)
		{
			newSurfaceName = "";
			ImGui.CloseCurrentPopup();
		}

		EndPaddedPopup();
	}

	static bool CanCreateSurface(string id)
		=> id.Length > 0 && char.IsAsciiLetter(id[0])
			&& id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_')
			&& !File.Exists(SurfaceSpecPath(id));

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
		if (!ImGui.CollapsingHeader($"Layers ({draft.Layers.Count}/{SurfaceSpec.MaxLayers})", ImGuiTreeNodeFlags.DefaultOpen))
			return;

		for (var i = 0; i < draft.Layers.Count; i++)
		{
			var layer = draft.Layers[i];
			ImGui.PushID(i);
			if (i > 0)
				ImGui.Separator();
			var open = ImGui.TreeNodeEx("##Layer", ImGuiTreeNodeFlags.DefaultOpen);
			ImGui.SameLine();
			ImGui.BeginDisabled(i == 0);
			if (ImGui.SmallButton($"{EditorIcons.Up}##Up"))
			{
				using (CaptureSurfaceUndo(draft.Id))
				{
					(draft.Layers[i - 1], draft.Layers[i]) = (draft.Layers[i], draft.Layers[i - 1]);
					ApplySurfaceEdit(draft);
				}
			}
			ImGui.EndDisabled();
			if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
				ImGui.SetTooltip("Move layer up");
			ImGui.SameLine();
			ImGui.BeginDisabled(i + 1 == draft.Layers.Count);
			if (ImGui.SmallButton($"{EditorIcons.Down}##Down"))
			{
				using (CaptureSurfaceUndo(draft.Id))
				{
					(draft.Layers[i + 1], draft.Layers[i]) = (draft.Layers[i], draft.Layers[i + 1]);
					ApplySurfaceEdit(draft);
				}
			}
			ImGui.EndDisabled();
			if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
				ImGui.SetTooltip("Move layer down");
			ImGui.SameLine();
			if (ImGui.SmallButton($"{EditorIcons.Randomize}##RandomizeLayer"))
			{
				using (CaptureSurfaceUndo(draft.Id))
				{
					RandomizeSurfaceLayer(layer, !unlockedSurfaceTiling.Contains(layer));
					ApplySurfaceEdit(draft);
				}
			}
			if (ImGui.IsItemHovered())
				ImGui.SetTooltip("Randomize layer");
			ImGui.SameLine();
			if (ImGui.SmallButton("x"))
			{
				using (CaptureSurfaceUndo(draft.Id))
				{
					unlockedSurfaceTiling.Remove(layer);
					draft.Layers.RemoveAt(i--);
					ApplySurfaceEdit(draft);
				}
				if (open)
					ImGui.TreePop();
				ImGui.PopID();
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
				if (ImGui.Combo("##Blend", ref blend, SurfaceBlendNames, SurfaceBlendNames.Length))
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
				if (ImGui.ColorEdit4("##WhiteColor", ref tint, ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.AlphaBar))
				{
					BeginSurfaceUndo(draft.Id);
					layer.Tint = TintHex(new Color(tint));
					ApplySurfaceEdit(draft);
				}
				CommitSurfaceUndoAfterEdit();
				ImGui.SameLine();
				if (ImGui.SmallButton($"{EditorIcons.Randomize}##RandomizeWhite"))
				{
					using (CaptureSurfaceUndo(draft.Id))
					{
						layer.Tint = TintHex(RandomSurfaceColor());
						ApplySurfaceEdit(draft);
					}
				}
				if (ImGui.IsItemHovered())
					ImGui.SetTooltip(luminance ? "Randomize white color" : "Randomize tint");
				if (luminance)
				{
					var blackColor = layer.BlackColor != null
						? SurfaceSpec.ParseTint(layer.BlackColor)
						: new Color(0, 0, 0, new Color(tint).A);
					var black = (Vector4)blackColor;
					BeginPropertyRow("Black color", hasTrailingButton: true);
					if (ImGui.ColorEdit4("##BlackColor", ref black, ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.AlphaBar))
					{
						BeginSurfaceUndo(draft.Id);
						layer.BlackColor = TintHex(new Color(black));
						ApplySurfaceEdit(draft);
					}
					CommitSurfaceUndoAfterEdit();
					ImGui.SameLine();
					if (ImGui.SmallButton($"{EditorIcons.Randomize}##RandomizeBlack"))
					{
						using (CaptureSurfaceUndo(draft.Id))
						{
							layer.BlackColor = TintHex(RandomSurfaceColor());
							ApplySurfaceEdit(draft);
						}
					}
					if (ImGui.IsItemHovered())
						ImGui.SetTooltip("Randomize black color");
				}

				var tiling = layer.Tiling is { Length: >= 2 }
					? new Vector2(layer.Tiling[0], layer.Tiling[1])
					: new Vector2(48);
				var oldTiling = tiling;
				var tilingLocked = !unlockedSurfaceTiling.Contains(layer);
				BeginPropertyRow("Tile size", hasTrailingButton: true);
				if (ImGui.DragFloat2("##TileSize", ref tiling, 1, 1, 2048, "%.0f px"))
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
				ImGui.SameLine();
				if (ImGui.SmallButton($"{(tilingLocked ? EditorIcons.Lock : EditorIcons.Unlock)}##TileSizeLock"))
				{
					if (tilingLocked)
						unlockedSurfaceTiling.Add(layer);
					else
						unlockedSurfaceTiling.Remove(layer);
				}
				if (ImGui.IsItemHovered())
					ImGui.SetTooltip(tilingLocked ? "Unlock tile width and height" : "Lock tile width and height");

				var scroll = layer.Scroll is { Length: >= 2 }
					? new Vector2(layer.Scroll[0], layer.Scroll[1])
					: Vector2.Zero;
				BeginPropertyRow("Motion");
				if (ImGui.DragFloat2("##Motion", ref scroll, 0.25f, -1024, 1024, "%.1f px/s"))
				{
					BeginSurfaceUndo(draft.Id);
					layer.Scroll = [scroll.X, scroll.Y];
					ApplySurfaceEdit(draft);
				}
				CommitSurfaceUndoAfterEdit();
				ImGui.TreePop();
			}
			ImGui.PopID();
		}

		ImGui.BeginDisabled(draft.Layers.Count >= SurfaceSpec.MaxLayers || Assets.Textures2D?.Order.Count == 0);
		if (ImGui.SmallButton("+ layer") && Assets.Textures2D is { Order.Count: > 0 } bucket)
		{
			using (CaptureSurfaceUndo(draft.Id))
			{
				draft.Layers.Add(new SurfaceLayerSpec { Texture = bucket.Order[0] });
				ApplySurfaceEdit(draft);
			}
		}
		ImGui.EndDisabled();
	}

	bool DrawSurfaceTexturePicker(ref string value, float controlWidth, bool normalOnly = false)
	{
		if (Assets.Textures2D is not { } bucket)
		{
			ImGui.TextDisabled("No Surface textures loaded");
			return false;
		}

		var changed = false;
		if (bucket.Get(value) is { } current)
		{
			if (ImGuiSubtextureButton("##Texture", current.Thumbnail, current.Key, false,
				new Vector2(MathF.Min(ButtonSize, controlWidth), MathF.Min(ButtonSize, controlWidth)), true))
				ImGui.OpenPopup("##TextureGrid");
		}
		else if (ImGui.Button("Missing##Texture", new Vector2(MathF.Min(ButtonSize, controlWidth), MathF.Min(ButtonSize, controlWidth))))
			ImGui.OpenPopup("##TextureGrid");
		ImGui.SameLine();
		if (ImGui.SmallButton($"{EditorIcons.Randomize}##RandomizeTexture") && RandomSurfaceTexture(bucket, normalOnly, value) is { } random)
		{
			value = random;
			changed = true;
		}
		if (ImGui.IsItemHovered())
			ImGui.SetTooltip("Randomize texture");

		if (ImGui.BeginPopup("##TextureGrid"))
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
					ImGui.SameLine();
				if (ImGuiSubtextureButton($"##texture{i}", texture.Thumbnail, key, value == key,
					new Vector2(ButtonSize, ButtonSize), true))
				{
					value = key;
					changed = true;
					ImGui.CloseCurrentPopup();
				}
			}
			ImGui.EndPopup();
		}
		return changed;
	}

	void RandomizeSurfaceLayer(SurfaceLayerSpec layer, bool tilingLocked)
	{
		if (Assets.Textures2D is { } bucket)
			layer.Texture = RandomSurfaceTexture(bucket, normalOnly: false) ?? layer.Texture;
		layer.Blend = (SurfaceBlend)Random.Shared.Next(SurfaceBlendNames.Length);
		layer.Tint = TintHex(RandomSurfaceColor());
		layer.BlackColor = TintHex(RandomSurfaceColor());
		var width = Random.Shared.Next(8, 129);
		layer.Tiling = [width, tilingLocked ? width : Random.Shared.Next(8, 129)];
		layer.Scroll = [Random.Shared.Next(-7, 8), Random.Shared.Next(-7, 8)];
	}

	static Color RandomSurfaceColor()
		=> new((byte)Random.Shared.Next(256), (byte)Random.Shared.Next(256), (byte)Random.Shared.Next(256), 255);

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
		if (!ImGui.CollapsingHeader("Normals", ImGuiTreeNodeFlags.DefaultOpen))
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
			if (ImGui.BeginCombo("##HeightLayer", $"{layer + 1}: {Path.GetFileName(draft.Layers[layer].Texture)}"))
			{
				for (var i = 0; i < draft.Layers.Count; i++)
					if (ImGui.Selectable($"{i + 1}: {Path.GetFileName(draft.Layers[i].Texture)}", layer == i))
					{
						using (CaptureSurfaceUndo(draft.Id))
						{
							draft.HeightFromLayer = i;
							ApplySurfaceEdit(draft);
						}
					}
				ImGui.EndCombo();
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
			if (ImGui.SliderFloat("##Strength", ref strength, 0, 4))
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
		if (ImGui.BeginCombo("##Source", names[mode]))
		{
			for (var i = 0; i < names.Length; i++)
			{
				ImGui.BeginDisabled(i == 2 && !hasNormalTexture);
				if (ImGui.Selectable(names[i], mode == i))
				{
					mode = i;
					changed = true;
				}
				ImGui.EndDisabled();
			}
			ImGui.EndCombo();
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
		if (ImGui.IsItemDeactivatedAfterEdit())
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
				usesSurface = string.Equals(ActorSpecs.ReadString(room.Actors[i], nameof(Bouncer.SurfaceId)), id, StringComparison.OrdinalIgnoreCase);
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

	static string TintHex(Color color)
		=> $"{color.R:x2}{color.G:x2}{color.B:x2}{color.A:x2}";
}
#endif
