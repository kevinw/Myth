using System.Runtime.CompilerServices;

namespace Myth;

// Live-tunable numeric literals. Suffix any float/int literal with `.T()` and — in DEBUG — it becomes
// a slider in the Tweaks ImGui panel: searchable, watchable (pin it to the top), and auto-surfaced
// when the literal sits inside the current `git diff`. In release the call inlines straight back to
// the literal, so shipping code pays nothing and the marker is a harmless no-op you can leave in.
//
// Identity is the source location (file + line), so it relies on `dotnet watch`: editing + saving
// recompiles and keeps the key aligned with the file on disk (and with git-diff line numbers).
public static class Tweak
{
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static float T(this float value,
		[CallerFilePath] string file = "",
		[CallerLineNumber] int line = 0,
		[CallerArgumentExpression(nameof(value))] string expr = "")
#if DEBUG
		=> TweakRegistry.ResolveFloat(file, line, expr, value, null, null);
#else
		=> value;
#endif

	// Explicit slider range: 16f.T(0, 64).
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static float T(this float value, float min, float max,
		[CallerFilePath] string file = "",
		[CallerLineNumber] int line = 0,
		[CallerArgumentExpression(nameof(value))] string expr = "")
#if DEBUG
		=> TweakRegistry.ResolveFloat(file, line, expr, value, min, max);
#else
		=> value;
#endif

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static int T(this int value,
		[CallerFilePath] string file = "",
		[CallerLineNumber] int line = 0,
		[CallerArgumentExpression(nameof(value))] string expr = "")
#if DEBUG
		=> TweakRegistry.ResolveInt(file, line, expr, value, null, null);
#else
		=> value;
#endif

	// Explicit slider range: 16.T(0, 64).
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static int T(this int value, int min, int max,
		[CallerFilePath] string file = "",
		[CallerLineNumber] int line = 0,
		[CallerArgumentExpression(nameof(value))] string expr = "")
#if DEBUG
		=> TweakRegistry.ResolveInt(file, line, expr, value, min, max);
#else
		=> value;
#endif
}
