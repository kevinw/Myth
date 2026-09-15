# Catch native SDL calls during browser compilation

## Problem

The demo-world browser build crashed with `DllNotFoundException: SDL3` after
commit `3be6664d` added gameplay mouse cursor hiding. The call path was
`Manager.UpdateGame()` → `UpdateMouseVisibility()` → `App.SetMouseVisible()` →
`SDL_CursorVisible()`. Foster's visibility API called SDL without a browser guard.

The fix routes browser visibility changes through the JavaScript bridge to the
canvas's CSS `cursor` property. We also want future unsupported native calls to
fail during C# compilation. Native import declarations alone don't tell the
compiler that a library is unavailable on a target platform.

## Plan

Use the SDK's existing platform compatibility analyzer, `CA1416`:

1. Annotate Foster's SDL bindings with `[UnsupportedOSPlatform("browser")]`.
   Start with the binding class; check whether shared nested SDL types require
   narrower annotations on callable APIs. The bindings are generated, so keep
   the annotation maintainable when updating them.
2. Exclude or conditionally compile desktop-only implementations such as
   `GraphicsDeviceSDL` and `InputProviderSDL` from browser builds where practical.
   Guard remaining native paths with `#if !BROWSER` or platform checks recognized
   by the analyzer. Retain SDL data types needed by shared code.
3. Enable the analyzer and make `CA1416` an error for browser builds in Foster
   and Myth. Verify the analyzer's platform context explicitly: the
   `browser-wasm` runtime identifier and `BROWSER` preprocessor symbol should not
   be assumed to establish that context by themselves.
4. Fix the exposed platform boundaries instead of broadly suppressing warnings.
   Keep cross-platform APIs such as `SetMouseVisible()` available, with the
   appropriate implementation per platform.

This should add modest analysis cost to normal C# compilation, without requiring
a browser launch, WASM AOT compilation, or a custom analysis tool. Measure the
incremental build impact rather than assuming it is negligible.

## Validation

- Temporarily restore the unguarded SDL call in `SetMouseVisible()` and verify
  that a browser C# build fails with `CA1416` at that call. Remove the temporary
  regression afterward.
- Verify that the corrected browser build and the desktop build pass.
- Compare incremental build times before and after enabling the check.
- Run the demo-world browser build and check cursor hiding and showing.

The current local SDK lacked the `wasm-tools` workload when validating the cursor
fix; use an SDK with that workload for browser build verification.

Reference: [Microsoft's CA1416 documentation](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/quality-rules/ca1416).
