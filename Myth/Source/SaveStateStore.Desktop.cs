#if !BROWSER
namespace Myth;

static partial class SaveStateStore
{
	static string SavePath(Manager manager) => Path.Join(manager.UserPath, FileName);

	static string? LoadJson(Manager manager)
	{
		var path = SavePath(manager);
		return File.Exists(path) ? File.ReadAllText(path) : null;
	}

	static void SaveJson(Manager manager, string json)
	{
		Directory.CreateDirectory(manager.UserPath);
		File.WriteAllText(SavePath(manager), json);
	}

	static void DeleteJson(Manager manager)
	{
		var path = SavePath(manager);
		if (File.Exists(path))
			File.Delete(path);
	}
}
#endif
