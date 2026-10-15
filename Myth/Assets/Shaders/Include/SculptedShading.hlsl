// Shading shared by the ray-marched sculpted shapes (SculptedBody, SculptedHead). Space is pixels: x right, y down,
// z toward the viewer. The including shader declares, before including this:
//   float4 Shading;  // x = 0 banded, 1 smooth, 2 dithered; yz = the texture's world position; w = light count
//   float4 Lights[MAX_LIGHTS * 2]; // (texture-space position, height, radius), (color * intensity, 0)

// World-anchored, like the lighting's own dither, so the pattern matches the scene around it.
float sculpted_threshold(float2 pixel)
{
    static const float matrix4[16] = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };
    float2 world = pixel + Shading.yz;
    float2 cell = world - 4.0 * floor(world / 4.0);
    return (matrix4[(int)cell.y * 4 + (int)cell.x] + 0.5) / 16.0;
}

// Turns a 0..1 amount into a quantized, dithered or smooth step of `levels`.
float sculpted_steps(float amount, float levels, float threshold)
{
    float scaled = amount * levels;
    return Shading.x > 1.5 ? min(floor(scaled + threshold), levels) : Shading.x > 0.5 ? scaled : floor(scaled + 0.5);
}

// Matte: a fixed key light plus cavity. Metal: what it reflects -- bright sky, a hard dark horizon, warm ground.
// Cavities and `darken` (0..1) pull it down.
float sculpted_light(float3 normal, float occlusion, float metallic, float darken)
{
    float3 keyLight = normalize(float3(0.0, -0.75, 0.65));
    float matte = saturate(dot(normal, keyLight)) * 0.65 + occlusion * 0.35;
    float3 reflected = reflect(float3(0.0, 0.0, -1.0), normal);
    float sky = reflected.y < 0.0 ? 0.62 + 0.38 * sqrt(-reflected.y) : 0.15 + 0.4 * reflected.y;
    return lerp(matte, sky, metallic) * lerp(0.5, 1.0, saturate((occlusion - 0.25) / 0.3)) * (1.0 - darken);
}

// Far below 0.4, near above 0.62, core between: banded rounds, smooth blends, dithered thresholds.
float3 sculpted_color(float light, float3 near, float3 core, float3 far, float threshold)
{
    float band = sculpted_steps(saturate(((light - 0.4) / 0.22 + 0.5) / 2.0), 2.0, threshold);
    return band >= 1.0 ? lerp(core, near, band - 1.0) : lerp(far, core, band);
}

// What the nearest scene lights glint off the surface, premultiplied for the emissive pass. surface: metallic,
// roughness, specular. Rough highlights spread wider and dimmer; metal tints them its own color.
float4 sculpted_specular(float3 position, float3 normal, float3 tint, float4 surface, float threshold)
{
    float shininess = lerp(64.0, 4.0, surface.y);
    float3 specular = float3(0.0, 0.0, 0.0);
    for (int i = 0; i < (int)Shading.w; i++)
    {
        float4 at = Lights[i * 2];
        float3 toLight = at.xyz - position;
        float reach = saturate(1.0 - length(toLight.xy) / at.w);
        if (reach <= 0.0)
            continue;
        float3 l = normalize(toLight);
        float3 h = normalize(l + float3(0.0, 0.0, 1.0));
        specular += Lights[i * 2 + 1].rgb * pow(saturate(dot(normal, h)), shininess) * saturate(dot(normal, l)) * reach * reach;
    }
    specular *= surface.z * (shininess + 2.0) / 16.0 * lerp(float3(1.0, 1.0, 1.0), tint, surface.x);
    float strength = max(specular.r, max(specular.g, specular.b));
    float glint = saturate(sculpted_steps(saturate(strength), 1.0, threshold));
    return glint > 0.0 ? float4(specular / max(strength, 0.0001) * glint, glint) : float4(0.0, 0.0, 0.0, 0.0);
}
