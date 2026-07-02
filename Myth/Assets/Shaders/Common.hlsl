// Content-space fragment pixel. WebGPU stores render targets y-flipped vs native, so the flip
// happens here once — callers pass the target height and never reason about orientation (this
// mirrors target_uv, which owns the same flip for texture sampling).
float2 screen_pixel(float4 position, float target_height)
{
#if BROWSER
    return float2(position.x, target_height - position.y);
#else
    return position.xy;
#endif
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
#if BROWSER
    uv.y = 1.0 - uv.y;
#endif
    return uv;
}
