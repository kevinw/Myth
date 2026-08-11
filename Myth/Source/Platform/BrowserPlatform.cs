#if BROWSER
using System.Runtime.InteropServices.JavaScript;

namespace Myth;

internal static partial class BrowserPlatform
{
	[JSImport("isMobileBrowser", "MythOverlay")]
	internal static partial bool IsMobileBrowser();
}
#endif
