#if EDITOR
namespace Myth;

/// The set of trained neural-texture weight files on disk, parsed out of their filenames into an
/// image (the material) and a motion. Filenames look like `<image>[_pair][_<motion>][_screen].bin`.
/// The picker UI that reads this lives in Manager.NeuralModelPicker.
static class NeuralTextureCatalog
{
	internal readonly record struct Model(string FileName, string Image, string ImageLabel, string Motion, string MotionLabel, int MotionOrder, bool Paired, bool Screen);

	static readonly List<Model> models = [];
	static readonly List<string> images = [];

	internal static IReadOnlyList<Model> AllModels { get { EnsureCatalog(); return models; } }
	// Distinct materials; each has one model per motion. The picker grids over these, not over all
	// ~790 files, and shows one canonical motion per material.
	internal static IReadOnlyList<string> Images { get { EnsureCatalog(); return images; } }
	static readonly string[] motionSuffixes = ["right_acc_down", "right_acc_right", "2block_x", "2block_y", "3block", "4block", "circular", "concentrate", "converge", "diverge", "hyperbolic", "right", "up"];

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

	// Display name for a material (the label its models share).
	internal static string ImageLabel(string image)
	{
		var first = FirstModel(image);
		return first >= 0 ? models[first].ImageLabel : image.Replace('_', ' ');
	}

	// The model a material's thumbnail is baked from -- its canonical motion.
	internal static string ThumbnailModel(string image) => BestModel(image, -1).FileName;

	internal static int FindModel(string fileName)
	{
		for (var i = 0; i < models.Count; i++)
			if (string.Equals(models[i].FileName, fileName, StringComparison.OrdinalIgnoreCase))
				return i;
		return -1;
	}

	internal static int FirstModel(string image)
	{
		for (var i = 0; i < models.Count; i++)
			if (string.Equals(models[i].Image, image, StringComparison.OrdinalIgnoreCase))
				return i;
		return -1;
	}

	// Switching image keeps as much of the current motion as it can, falling back through
	// progressively looser matches to "circular" and finally to whatever is first.
	internal static Model BestModel(string image, int currentIndex)
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
