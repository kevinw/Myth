// Content-space fragment pixel. The target_height param is retained for call-site compatibility
// but no longer used: the browser flip was removed (there is no hardware target inversion; the
// old flips were self-inflicted — see Notes/BROWSER_TARGET_FLIP.md).
float2 screen_pixel(float4 position, float target_height)
{
    return position.xy;
}

// Screen fragment -> world pixel: camera top-left + content-space pixel scaled by inverse zoom.
// The single definition of "screen to world" for the renderer, so the browser flip stays in one
// place instead of being re-derived per shader.
float2 screen_to_world(float4 position, float2 target_size, float2 camera_top_left, float zoom)
{
    return camera_top_left + screen_pixel(position, target_size.y) / zoom;
}

float2 texture_uv(float2 uv)
{
    return uv;
}

float2 target_uv(float2 uv)
{
    // Flip removed: render targets are no longer sampled y-flipped on browser.
    // See Notes/BROWSER_TARGET_FLIP.md.
    return uv;
}
