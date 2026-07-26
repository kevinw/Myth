#if !MYTHSHIP && !BROWSER && !IOS
using Foster.Framework;
using System.Runtime.InteropServices;

namespace Myth;

// Thin P/Invoke wrapper over native/MythCapture (ScreenCaptureKit window recorder).
// The dylib is macOS-only; every entry point is guarded so non-mac or missing-dylib
// builds degrade to a friendly message rather than throwing at the call site.
static partial class MythCapture
{
	const string Lib = "MythCapture";

	// Capture/quality settings — tweak here.
	// Fps 0 = auto (match the display's refresh rate, capped at 120) for a 1:1 frame mapping.
	// Bitrate 0 = auto (high-quality, scales with resolution).
	// MaxHeight 0 = native retina size; set e.g. 1080 to cap/downscale (lightens the encoder).
	public static int Fps = 60;
	public static int BitrateMbps = 0;
	public static int MaxHeight = 1080;

	// must match myth_capture_start
	enum Codec: int
	{
		H264 = 0,
		HEVC = 1,
		ProRes_422_LT = 2,
		ProRes_422 = 3,
		ProRes_422_HQ = 4,
	}

	readonly static Codec DefaultCodec = Codec.ProRes_422_LT;

	[LibraryImport(Lib, EntryPoint = "myth_capture_start", StringMarshalling = StringMarshalling.Utf8)]
	private static partial int NativeStart(string path, int fps, int bitrateBps, int maxHeight, int hevc);

	[LibraryImport(Lib, EntryPoint = "myth_capture_stop")]
	private static partial int NativeStop();

	[LibraryImport(Lib, EntryPoint = "myth_capture_is_recording")]
	private static partial int NativeIsRecording();

	[LibraryImport(Lib, EntryPoint = "myth_capture_fps")]
	private static partial int NativeFps();

	[LibraryImport(Lib, EntryPoint = "myth_capture_width")]
	private static partial int NativeWidth();

	[LibraryImport(Lib, EntryPoint = "myth_capture_height")]
	private static partial int NativeHeight();

	[LibraryImport(Lib, EntryPoint = "myth_capture_last_error")]
	private static partial void NativeLastError([Out] byte[] buffer, int capacity);

	public static bool Available => OperatingSystem.IsMacOS();
	public static string? OutputPath { get; private set; }

	public static bool IsRecording
	{
		get
		{
			if (!Available)
				return false;
			try { return NativeIsRecording() != 0; }
			catch (DllNotFoundException) { return false; }
		}
	}

	public static bool Start(string path)
	{
		OutputPath = path;
		var bitrateBps = BitrateMbps > 0 ? BitrateMbps * 1_000_000 : 0;
		return NativeStart(path, Fps, bitrateBps, MaxHeight, (int)DefaultCodec) == 0;
	}

	public static bool Stop() => NativeStop() == 0;
	public static int EffectiveFps => NativeFps();
	public static Point2 OutputSize => new(NativeWidth(), NativeHeight());

	public static string LastError()
	{
		try
		{
			var buffer = new byte[256];
			NativeLastError(buffer, buffer.Length);
			var len = Array.IndexOf(buffer, (byte)0);
			if (len < 0)
				len = buffer.Length;
			return System.Text.Encoding.UTF8.GetString(buffer, 0, len);
		}
		catch (DllNotFoundException) { return "libMythCapture.dylib not found"; }
	}
}
#endif
