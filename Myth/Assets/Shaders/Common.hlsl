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

// --- tonemapping -------------------------------------------------------------------
// The lighting is HDR (float16 masks, float16 emissive) and the frame is 8 bit, so the lit
// scene is rolled off rather than clipped. Shared here because the composite needs the curve
// for its dither probe and the resolve pass needs it for the pixel it finally writes.

// Soft knee: exactly identity below `knee`, asymptotic to 1 above it, so mid-tones that are
// already dialed in stay untouched. A knee of 1 degenerates to a plain clamp.
float tonemap_scalar(float value, float knee)
{
    if (value <= knee)
        return value;
    float headroom = max(1.0 - knee, 0.0001);
    return knee + headroom * (1.0 - exp(-(value - knee) / headroom));
}

// Rescales all three channels by one factor, so channel ratios -- and with them hue and
// saturation -- survive the rolloff: a fully lit pink brightens toward pink, never toward
// white. `desaturate` dials back toward a per-channel clip when whiteout is wanted.
float3 tonemap_color(float3 color, float knee, float desaturate)
{
    float peak = max(max(color.r, color.g), color.b);
    float3 preserved = peak > 0.0001 ? color * (tonemap_scalar(peak, knee) / peak) : color;
    return lerp(preserved, saturate(color), desaturate);
}

// The energy the rolloff threw away, kept in the colour it arrived as. This is what becomes
// glow: light too bright for the display scatters instead of bleaching the surface.
float3 tonemap_overflow(float3 color, float knee)
{
    float peak = max(max(color.r, color.g), color.b);
    if (peak <= 0.0001)
        return 0.0;
    return color * ((peak - tonemap_scalar(peak, knee)) / peak);
}
