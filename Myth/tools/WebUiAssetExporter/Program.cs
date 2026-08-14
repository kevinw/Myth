using System.Diagnostics;
using Foster.Framework;

var root = FindMythRoot();
var source = Resolve(args.ElementAtOrDefault(0) ?? "Production/WebUI/gamepad.ase");
var output = Resolve(args.ElementAtOrDefault(1) ?? "Platforms/Web/wwwroot/loading-gamepad.webp");

if (!File.Exists(source))
	throw new FileNotFoundException($"Missing source image: {source}");

Directory.CreateDirectory(Path.GetDirectoryName(output)!);

using var frame = new Aseprite(source).RenderAllFrames().FirstOrDefault()
	?? throw new InvalidOperationException($"No frames found in {source}");
using var trimmed = Trim(frame);

var png = Path.ChangeExtension(Path.GetTempFileName(), ".png");
try
{
	trimmed.WritePng(png);
	Run("cwebp", "-quiet", "-lossless", "-exact", "-q", "100", "-m", "6", png, "-o", output);
	Console.WriteLine($"Wrote {Path.GetRelativePath(root, output)} from {Path.GetRelativePath(root, source)} ({trimmed.Width}x{trimmed.Height})");
}
finally
{
	if (File.Exists(png))
		File.Delete(png);
}

string Resolve(string path) => Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(root, path));

static string FindMythRoot()
{
	var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
	while (dir != null)
	{
		if (File.Exists(Path.Combine(dir.FullName, "Myth.csproj")))
			return dir.FullName;
		if (dir.Name == "FosterGames" && File.Exists(Path.Combine(dir.FullName, "Myth", "Myth.csproj")))
			return Path.Combine(dir.FullName, "Myth");
		dir = dir.Parent;
	}

	throw new InvalidOperationException("Run from the Myth project or FosterGames repo.");
}

static Image Trim(Image image)
{
	var bounds = ContentBounds(image);
	if (bounds.Width == image.Width && bounds.Height == image.Height)
		return Copy(image, image.Bounds);

	var result = new Image(bounds.Width, bounds.Height);
	result.CopyPixels(image, bounds, Point2.Zero);
	return result;
}

static Image Copy(Image image, RectInt bounds)
{
	var result = new Image(bounds.Width, bounds.Height);
	result.CopyPixels(image, bounds, Point2.Zero);
	return result;
}

static RectInt ContentBounds(Image image)
{
	var min = new Point2(image.Width, image.Height);
	var max = Point2.Zero;
	var found = false;

	for (var y = 0; y < image.Height; y++)
	for (var x = 0; x < image.Width; x++)
	{
		if (image[x, y].A == 0)
			continue;

		found = true;
		min = new Point2(Math.Min(min.X, x), Math.Min(min.Y, y));
		max = new Point2(Math.Max(max.X, x + 1), Math.Max(max.Y, y + 1));
	}

	return found ? RectInt.Between(min, max) : new RectInt(0, 0, 1, 1);
}

static void Run(string file, params string[] args)
{
	var start = new ProcessStartInfo(file)
	{
		UseShellExecute = false,
		RedirectStandardError = true,
		RedirectStandardOutput = true,
	};

	foreach (var arg in args)
		start.ArgumentList.Add(arg);

	using var process = Process.Start(start) ?? throw new InvalidOperationException($"Failed to start {file}");
	var stdout = process.StandardOutput.ReadToEnd();
	var stderr = process.StandardError.ReadToEnd();
	process.WaitForExit();

	if (process.ExitCode == 0)
		return;

	throw new InvalidOperationException($"{file} failed with exit code {process.ExitCode}\n{stdout}{stderr}");
}
