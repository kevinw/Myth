float2 screen_pixel(float4 position)
{
    return position.xy;
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
