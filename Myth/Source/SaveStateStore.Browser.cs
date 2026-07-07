#if BROWSER
using System.Runtime.InteropServices.JavaScript;

namespace Myth;

static partial class SaveStateStore
{
	static string? LoadJson(Manager manager) => LoadBrowserSave();
	static void SaveJson(Manager manager, string json) => SaveBrowserSave(json);
	static void DeleteJson(Manager manager) => DeleteBrowserSave();

	[JSImport("loadSaveState", "MythOverlay")]
	internal static partial string? LoadBrowserSave();

	[JSImport("saveSaveState", "MythOverlay")]
	internal static partial void SaveBrowserSave(string json);

	[JSImport("deleteSaveState", "MythOverlay")]
	internal static partial void DeleteBrowserSave();
}
#endif
