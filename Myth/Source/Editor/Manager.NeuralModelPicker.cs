#if EDITOR
using System.Numerics;
using Foster.Framework;
using ImGuiNET;
using static ImGuiNET.ImGui;

namespace Myth;

// The neural-texture model picker: a thumbnail grid over the ~80 materials (rather than a text
// combo over all ~790 model files), plus the motion combo beside it. Thumbnails are baked by one
// offscreen rig actor -- see GetNeuralModelPreview.
public partial class Manager
{
	const int NeuralModelPreviewSize = 64;
	// Matches NeuralTextureActor.EditorPreviewSteps: enough for the texture to reach its attractor.
	const int NeuralModelPreviewSteps = 95;
	// Baking is ~95 tiny dispatches plus a weights upload, so spread a grid's worth over several
	// frames: thumbnails stream in instead of hitching the first time the popup opens.
	const int NeuralModelPreviewBakesPerFrame = 4;
	const int NeuralModelPickerColumns = 8;

	readonly Dictionary<string, Target> neuralModelPreviews = new(StringComparer.OrdinalIgnoreCase);
	Game? neuralPreviewRig;
	NeuralTextureActor? neuralPreviewActor;
	Batcher? neuralPreviewBatcher;
	int neuralPreviewBakeFrame = -1;
	int neuralPreviewBakes;

	// A model's settled decode, or null while it's still queued behind this frame's bake budget.
	Subtexture? GetNeuralModelPreview(string modelFile)
	{
		if (string.IsNullOrEmpty(modelFile))
			return null;
		if (neuralModelPreviews.TryGetValue(modelFile, out var cached))
			return new Subtexture(cached.Attachments[0]);

		var frame = GetFrameCount();
		if (frame != neuralPreviewBakeFrame)
		{
			neuralPreviewBakeFrame = frame;
			neuralPreviewBakes = 0;
		}
		if (neuralPreviewBakes >= NeuralModelPreviewBakesPerFrame)
			return null;
		neuralPreviewBakes++;

		if (NeuralPreviewRig() is not { } rig)
			return null;
		rig.SetPreviewModel(modelFile, NeuralModelPreviewSteps);
		if (rig.PreviewOutput is not { } decoded)
			return null;

		// The rig reuses one output texture, so copy it out before the next model overwrites it.
		var target = new Target(GraphicsDevice, NeuralModelPreviewSize, NeuralModelPreviewSize,
			name: $"NeuralModelPreview/{modelFile}");
		neuralPreviewBatcher ??= new Batcher(GraphicsDevice, "NeuralModelPreviews");
		neuralPreviewBatcher.Image(decoded, Vector2.Zero, Color.White);
		neuralPreviewBatcher.Render(target);
		neuralPreviewBatcher.Clear();
		neuralModelPreviews[modelFile] = target;
		return new Subtexture(target.Attachments[0]);
	}

	// One small square neural texture in its own offscreen Game, kept for the session. Reading its
	// decode output directly means no room render, no lighting pass, and no second editor preview.
	NeuralTextureActor? NeuralPreviewRig()
	{
		if (neuralPreviewActor != null)
			return neuralPreviewActor;

		neuralPreviewRig = new Game(this, new Room(Point2.Zero, new Point2(Game.Columns, Game.Rows)),
			audioEventsEnabled: false, spawnBud: false, enableParticles: false);
		// No painted mask means slot 0 covers the whole square, which is exactly the swatch we want.
		neuralPreviewActor = neuralPreviewRig.Create<NeuralTextureActor>(Point2.Zero, it =>
		{
			it.Size = new Point2(NeuralModelPreviewSize, NeuralModelPreviewSize);
			it.PrewarmSteps = 0;
			it.AutoPerturb = false;
		});
		return neuralPreviewActor;
	}

	// The model picker shared by the inspector's four weight slots: material thumbnail (click for
	// the grid) and, beside it, the motion this material was trained to drift in.
	bool DrawNeuralModelPicker(ref string value, bool allowNone)
	{
		var changed = false;
		var cell = new Vector2(ButtonSize, ButtonSize);
		var index = NeuralTextureCatalog.FindModel(value);
		var current = index >= 0 ? NeuralTextureCatalog.AllModels[index] : default;
		var label = index >= 0 ? current.ImageLabel
			: value.Length == 0 ? "(none)"
			: Path.GetFileNameWithoutExtension(value).Replace('_', ' ');

		if (GetNeuralModelPreview(value) is { } preview)
		{
			if (ImGuiSubtextureButton("##NeuralModel", preview, $"{label} — click to change", false, cell, true, 0f))
				OpenPopup("##NeuralModelGrid");
		}
		else if (Button($"{label}##NeuralModel", cell))
		{
			OpenPopup("##NeuralModelGrid");
		}

		if (index >= 0)
		{
			SameLine();
			SetNextItemWidth(-1);
			if (BeginCombo("##Motion", current.MotionLabel))
			{
				var models = NeuralTextureCatalog.AllModels;
				for (var i = 0; i < models.Count; i++)
				{
					if (!string.Equals(models[i].Image, current.Image, StringComparison.OrdinalIgnoreCase))
						continue;
					var selected = i == index;
					if (Selectable(models[i].MotionLabel, selected))
					{
						value = models[i].FileName;
						changed = true;
					}
					if (selected)
						SetItemDefaultFocus();
				}
				EndCombo();
			}
		}

		PushSwatchGridStyle();
		if (BeginPopup("##NeuralModelGrid"))
		{
			var swatch = new Vector2(SwatchGridCell);
			var images = NeuralTextureCatalog.Images;
			if (images.Count == 0)
			{
				TextDisabled($"No models found ({NeuralTextureActor.WeightsFolder}/*.bin)");
			}
			else
			{
				var column = 0;
				if (allowNone)
				{
					if (Button("None", swatch))
					{
						value = string.Empty;
						changed = true;
						CloseCurrentPopup();
					}
					column = 1;
				}
				// Switching material keeps the current motion where it exists; the thumbnail always
				// shows the canonical one so the grid reads as a set of materials.
				foreach (var image in images)
				{
					SwatchGridWrap(ref column, NeuralModelPickerColumns);
					var selected = index >= 0 && string.Equals(image, current.Image, StringComparison.OrdinalIgnoreCase);
					var name = NeuralTextureCatalog.ImageLabel(image);
					var clicked = GetNeuralModelPreview(NeuralTextureCatalog.ThumbnailModel(image)) is { } thumb
						? SwatchGridButton($"##nca{image}", thumb, name, selected)
						: Button($"...##nca{image}", swatch);
					if (clicked)
					{
						value = NeuralTextureCatalog.BestModel(image, index).FileName;
						changed = true;
						CloseCurrentPopup();
					}
				}
			}
			EndPopup();
		}
		PopSwatchGridStyle();

		return changed;
	}

	void DisposeNeuralModelPreviews()
	{
		foreach (var (_, target) in neuralModelPreviews)
			target.Dispose();
		neuralModelPreviews.Clear();
		neuralPreviewBatcher?.Dispose();
		neuralPreviewBatcher = null;
		neuralPreviewActor = null;
		neuralPreviewRig?.Dispose();
		neuralPreviewRig = null;
	}
}
#endif
