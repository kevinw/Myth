#if BROWSER
using System.Runtime.InteropServices.JavaScript;

namespace Myth;

internal static partial class BrowserDebugOverlay
{
	[JSImport("setDebugOverlayStats", "FosterWebGPU")]
	internal static partial void SetStats(string text);
}
#endif
