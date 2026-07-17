#if EDITOR
using System.Diagnostics;

namespace Myth;

/// Opens a tile type's source .ase in Aseprite.
static class AsepriteLauncher
{
	public static void Open(Factory.Entry entry)
	{
		var filename = GetTileEntryFilename(entry);
		if (filename == null)
		{
			Console.WriteLine($"No sprite filename found for entry {entry.Name}");
			return;
		}

		var startInfo = CreateStartInfo(filename);

		try
		{
			Process.Start(startInfo);
		}
		catch (Exception e)
		{
			Console.Error.WriteLine($"Failed to open '{filename}' in Aseprite using '{startInfo.FileName}': {e.Message}");
		}
	}

	static ProcessStartInfo CreateStartInfo(string filename)
	{
		if (OperatingSystem.IsMacOS())
		{
			var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
			var steamAseprite = Path.Combine(homeDir, "Library/Application Support/Steam/steamapps/common/Aseprite/Aseprite.app/Contents/MacOS/aseprite");
			if (File.Exists(steamAseprite))
				return CreateStartInfo(steamAseprite, filename);

			var open = "/usr/bin/open";
			if (File.Exists(open))
			{
				var startInfo = CreateStartInfo(open, "-a", "Aseprite", filename);
				return startInfo;
			}
		}

		return CreateStartInfo("aseprite", filename);
	}

	static ProcessStartInfo CreateStartInfo(string executable, params string[] arguments)
	{
		var startInfo = new ProcessStartInfo
		{
			FileName = executable,
			UseShellExecute = false
		};

		foreach (var argument in arguments)
			startInfo.ArgumentList.Add(argument);

		return startInfo;
	}

	static string? GetTileEntryFilename(Factory.Entry entry)
	{
		foreach (var tilesetName in GetTileEntryCandidates(entry))
		{
			if (FindFile(Path.Join(Assets.SourcesPath, "Tilesets"), tilesetName) is { } filename)
				return filename;
		}

		return null;
	}

	static IEnumerable<string> GetTileEntryCandidates(Factory.Entry entry)
	{
		if (!string.IsNullOrWhiteSpace(entry.TilesetName))
		{
			yield return entry.TilesetName;
			yield return $"{entry.TilesetName}-kit";
		}

		if (!string.IsNullOrWhiteSpace(entry.SlopeTilesetName) && entry.SlopeTilesetName != entry.TilesetName)
		{
			yield return entry.SlopeTilesetName;
			yield return $"{entry.SlopeTilesetName}-kit";
		}

		if (!string.IsNullOrWhiteSpace(entry.DualTilesetName))
		{
			yield return entry.DualTilesetName;
			yield return $"{entry.DualTilesetName}-kit";
		}
	}

	static string? FindFile(string directory, string name)
	{
		foreach (var extension in new[] { ".ase", ".aseprite" })
		{
			var path = Path.GetFullPath(Path.Join(directory, name + extension));
			if (File.Exists(path))
				return path;
		}

		return null;
	}
}
#endif
