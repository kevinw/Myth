# Backbuffer resize lag (Foster)

## Current state

Foster's `GraphicsDeviceSDL` only learns a window's new pixel size at present
time, when the swapchain is acquired (`PresentWindow` updates
`state.BackbufferSize` and recreates `state.Backbuffer`). Anything that renders
during the frame of a resize draws against the *old* backbuffer texture, while
code that queries the live window size (e.g. `ImGuiRenderer` computing clip
scissors from `window.WidthInPixels`) sees the *new* size — one frame of
disagreement.

This used to abort under `MTL_DEBUG_LAYER=1` (oversized scissor into the stale
render pass, e.g. 6016x3320 into 2560x1440 when startup restores the saved
window size). Fixed by clamping scissors to the render pass texture via
`RectInt.GetIntersection` in `GraphicsDeviceSDL` (July 2026), so it's
crash-free now — but the first frame after a resize still renders at the old
size and gets stretched by the present blit.

## Future fix

Update `BackbufferSize` (and recreate `Backbuffer` if needed) from window
resize events instead of only at present, so the frame after a resize renders
pixel-correct immediately.

- The resize event path needs to reach `GraphicsDeviceSDL` per-window state
  (`SDL_EVENT_WINDOW_PIXEL_SIZE_CHANGED` is the right signal; it fires with the
  new pixel size, covering DPI changes from monitor moves too).
- Keep the existing present-time update as the fallback/truth — the swapchain's
  acquired size is what actually gets blitted.
- The +64/+128 slack sizing in `PresentWindow` already avoids recreation churn
  during live drags; reuse it in the event path.

Cosmetic only (single stretched frame per resize), so it wasn't worth the
blast radius when the scissor crash was fixed.
