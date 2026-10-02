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

		OpenFile(filename);
	}

	/// Opens any file in Aseprite -- the tile kit round trip hands it a scratch .ase.
	public static void OpenFile(string filename)
	{
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

	const string MacOpen = "/usr/bin/open";

	static ProcessStartInfo CreateStartInfo(string filename)
	{
		// Always via `open` on macOS: running the binary inside the bundle starts a second copy every
		// time, while `open` hands the file to the instance already running and brings it forward.
		// The bundle is named by path first so a Steam install works whether or not LaunchServices
		// has registered it.
		if (OperatingSystem.IsMacOS() && File.Exists(MacOpen))
		{
			var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
			var steamBundle = Path.Combine(homeDir, "Library/Application Support/Steam/steamapps/common/Aseprite/Aseprite.app");
			return Directory.Exists(steamBundle)
				? CreateStartInfo(MacOpen, "-a", steamBundle, filename)
				: CreateStartInfo(MacOpen, "-a", "Aseprite", filename);
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
