#if EDITOR
using ImGuiNET;

namespace Myth;

/// The set of trained neural-texture weight files on disk, parsed out of their filenames into an
/// image (the material) and a motion, plus the two-combo picker the inspectors share. Filenames
/// look like `<image>[_pair][_<motion>][_screen].bin`.
static class NeuralTextureCatalog
{
	readonly record struct Model(string FileName, string Image, string ImageLabel, string Motion, string MotionLabel, int MotionOrder, bool Paired, bool Screen);

	static readonly List<Model> models = [];
	static readonly List<string> images = [];
	static readonly string[] motionSuffixes = ["right_acc_down", "right_acc_right", "2block_x", "2block_y", "3block", "4block", "circular", "concentrate", "converge", "diverge", "hyperbolic", "right", "up"];

	public static bool DrawCombo(ref string value, bool allowNone = false)
	{
		EnsureCatalog();
		var changed = false;
		var width = ImGui.CalcItemWidth();
		var currentIndex = FindModel(value);
		var image = currentIndex >= 0 ? models[currentIndex].Image : "";
		var imageLabel = currentIndex >= 0 ? models[currentIndex].ImageLabel : value.Length == 0 ? "(none)" : Path.GetFileNameWithoutExtension(value).Replace('_', ' ');
		if (ImGui.BeginCombo("##Image", $"Image: {imageLabel}"))
		{
			if (allowNone && ImGui.Selectable("(none)", value.Length == 0))
			{
				value = "";
				changed = true;
			}
			for (var i = 0; i < images.Count; i++)
			{
				var nextImage = images[i];
				var selected = string.Equals(image, nextImage, StringComparison.OrdinalIgnoreCase);
				var first = FirstModel(nextImage);
				if (ImGui.Selectable(models[first].ImageLabel, selected))
				{
					value = BestModel(nextImage, currentIndex).FileName;
					changed = true;
				}
				if (selected)
					ImGui.SetItemDefaultFocus();
			}
			ImGui.EndCombo();
		}

		currentIndex = FindModel(value);
		if (currentIndex < 0)
			return changed;
		var current = models[currentIndex];
		ImGui.SetNextItemWidth(width);
		if (ImGui.BeginCombo("##Motion", $"Motion: {current.MotionLabel}"))
		{
			for (var i = 0; i < models.Count; i++)
			{
				var model = models[i];
				if (!string.Equals(model.Image, current.Image, StringComparison.OrdinalIgnoreCase))
					continue;
				var selected = i == currentIndex;
				if (ImGui.Selectable(model.MotionLabel, selected))
				{
					value = model.FileName;
					changed = true;
				}
				if (selected)
					ImGui.SetItemDefaultFocus();
			}
			ImGui.EndCombo();
		}
		return changed;
	}

	static void EnsureCatalog()
	{
		if (models.Count > 0)
			return;

		foreach (var key in Assets.Content.EnumerateDirectory(NeuralTextureActor.WeightsFolder, "*.bin"))
			models.Add(ParseModel(Path.GetFileName(key)));
		models.Sort(static (a, b) =>
		{
			var image = StringComparer.OrdinalIgnoreCase.Compare(a.Image, b.Image);
			if (image != 0)
				return image;
			var motion = a.MotionOrder.CompareTo(b.MotionOrder);
			return motion != 0 ? motion : StringComparer.OrdinalIgnoreCase.Compare(a.MotionLabel, b.MotionLabel);
		});

		for (var i = 0; i < models.Count; i++)
			if (i == 0 || !string.Equals(models[i].Image, models[i - 1].Image, StringComparison.OrdinalIgnoreCase))
				images.Add(models[i].Image);
	}

	static Model ParseModel(string fileName)
	{
		var stem = Path.GetFileNameWithoutExtension(fileName);
		var screen = stem.EndsWith("_screen", StringComparison.OrdinalIgnoreCase);
		if (screen)
			stem = stem[..^"_screen".Length];

		var motion = "default";
		for (var i = 0; i < motionSuffixes.Length; i++)
			if (stem.EndsWith($"_{motionSuffixes[i]}", StringComparison.OrdinalIgnoreCase))
			{
				motion = motionSuffixes[i];
				stem = stem[..^(motion.Length + 1)];
				break;
			}

		var paired = stem.EndsWith("_pair", StringComparison.OrdinalIgnoreCase);
		if (paired)
			stem = stem[..^"_pair".Length];
		var motionLabel = motion.Replace('_', ' ');
		if (paired)
			motionLabel += " · normals";
		if (screen)
			motionLabel += " · screen";
		return new(fileName, stem, stem.Replace('_', ' '), motion, motionLabel, MotionOrder(motion), paired, screen);
	}

	static int MotionOrder(string motion) => motion switch
	{
		"2block_x" => 0,
		"2block_y" => 1,
		"3block" => 2,
		"4block" => 3,
		"circular" => 4,
		"concentrate" => 5,
		"converge" => 6,
		"diverge" => 7,
		"hyperbolic" => 8,
		"right" => 9,
		"right_acc_down" => 10,
		"right_acc_right" => 11,
		"up" => 12,
		_ => 13,
	};

	static int FindModel(string fileName)
	{
		for (var i = 0; i < models.Count; i++)
			if (string.Equals(models[i].FileName, fileName, StringComparison.OrdinalIgnoreCase))
				return i;
		return -1;
	}

	static int FirstModel(string image)
	{
		for (var i = 0; i < models.Count; i++)
			if (string.Equals(models[i].Image, image, StringComparison.OrdinalIgnoreCase))
				return i;
		return -1;
	}

	// Switching image keeps as much of the current motion as it can, falling back through
	// progressively looser matches to "circular" and finally to whatever is first.
	static Model BestModel(string image, int currentIndex)
	{
		var first = FirstModel(image);
		if (currentIndex < 0)
		{
			for (var i = first; i < models.Count && string.Equals(models[i].Image, image, StringComparison.OrdinalIgnoreCase); i++)
				if (models[i].Motion == "circular")
					return models[i];
			return models[first];
		}

		var current = models[currentIndex];
		for (var pass = 0; pass < 4; pass++)
			for (var i = first; i < models.Count && string.Equals(models[i].Image, image, StringComparison.OrdinalIgnoreCase); i++)
			{
				var candidate = models[i];
				if (pass == 0 && candidate.Motion == current.Motion && candidate.Paired == current.Paired && candidate.Screen == current.Screen ||
					pass == 1 && candidate.Motion == current.Motion && candidate.Screen == current.Screen ||
					pass == 2 && candidate.Motion == current.Motion ||
					pass == 3 && candidate.Motion == "circular")
					return candidate;
			}
		return models[first];
	}
}
#endif
