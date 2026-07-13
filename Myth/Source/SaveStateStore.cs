using System.Diagnostics;

namespace Myth;

static partial class SaveStateStore
{
#if EDITOR
	const string FileName = "save-editor.json";
#else
	const string FileName = "save.json";
#endif

	public static SaveState Load(Manager manager)
	{
		var json = LoadJson(manager);
		if (string.IsNullOrWhiteSpace(json))
			return new();

		try
		{
			return SaveState.FromJsonString(json);
		}
		catch (Exception e)
		{
			Debug.WriteLine($"Failed to load save state: {e}");
			return new();
		}
	}

	public static void Save(Manager manager, SaveState state)
	{
		try
		{
			SaveJson(manager, state.ToJsonString());
		}
		catch (Exception e)
		{
			Debug.WriteLine($"Failed to save state: {e}");
		}
	}

	public static void Delete(Manager manager)
	{
		try
		{
			DeleteJson(manager);
		}
		catch (Exception e)
		{
			Debug.WriteLine($"Failed to delete save state: {e}");
		}
	}
}
