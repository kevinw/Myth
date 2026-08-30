#if BROWSER_JS_FMOD
using System.Runtime.InteropServices.JavaScript;

namespace Myth;

internal static partial class BrowserFmod
{
	[JSImport("isInitialized", "MythBrowserFmod")]
	internal static partial bool IsInitialized();

	[JSImport("reloadBanks", "MythBrowserFmod")]
	internal static partial void ReloadBanks(string[] paths);

	[JSImport("getEventParameterLabels", "MythBrowserFmod")]
	internal static partial string GetEventParameterLabels(string eventPath, string parameterName);

	[JSImport("createEvent", "MythBrowserFmod")]
	internal static partial int CreateEvent(string path);

	[JSImport("startEvent", "MythBrowserFmod")]
	internal static partial void StartEvent(int id);

	[JSImport("stopEvent", "MythBrowserFmod")]
	internal static partial void StopEvent(int id, bool immediate);

	[JSImport("setEventPaused", "MythBrowserFmod")]
	internal static partial void SetEventPaused(int id, bool paused);

	[JSImport("setEventVolume", "MythBrowserFmod")]
	internal static partial void SetEventVolume(int id, float volume);

	[JSImport("getTimelinePosition", "MythBrowserFmod")]
	internal static partial int GetTimelinePosition(int id);

	[JSImport("setTimelinePosition", "MythBrowserFmod")]
	internal static partial void SetTimelinePosition(int id, int position);

	[JSImport("setEventParameter", "MythBrowserFmod")]
	internal static partial void SetEventParameter(int id, string name, float value, bool ignoreSeekSpeed);

	[JSImport("setEventParameterLabel", "MythBrowserFmod")]
	internal static partial void SetEventParameterLabel(int id, string name, string label, bool ignoreSeekSpeed);

	[JSImport("setEvent3DAttributes", "MythBrowserFmod")]
	internal static partial void SetEvent3DAttributes(int id, float px, float py, float pz, float vx, float vy, float vz);

	[JSImport("releaseEvent", "MythBrowserFmod")]
	internal static partial void ReleaseEvent(int id);

	[JSImport("setParameter", "MythBrowserFmod")]
	internal static partial void SetParameter(string name, float value, bool ignoreSeekSpeed);

	[JSImport("setParameterLabel", "MythBrowserFmod")]
	internal static partial void SetParameterLabel(string name, string label, bool ignoreSeekSpeed);

	[JSImport("setListenerAttributes", "MythBrowserFmod")]
	internal static partial void SetListenerAttributes(float px, float py, float pz, float vx, float vy, float vz);

	[JSImport("setBusPaused", "MythBrowserFmod")]
	internal static partial void SetBusPaused(string path, bool paused);

	[JSImport("update", "MythBrowserFmod")]
	internal static partial void Update();

	[JSImport("reinitialize", "MythBrowserFmod")]
	internal static partial bool Reinitialize();

	[JSImport("consumeReinitRequest", "MythBrowserFmod")]
	internal static partial bool ConsumeReinitRequest();

	[JSImport("shutdown", "MythBrowserFmod")]
	internal static partial void Shutdown();
}
#endif
