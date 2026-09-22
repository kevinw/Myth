#include "Common.hlsl"

#define MAX_DITHER_ANCHORS 32

float tile_field_family(float4 field)
{
    return floor(floor(field.g * 255.0 + 0.5) / 32.0);
}

int tile_field_anchor(float4 field)
{
    return int(fmod(floor(field.g * 255.0 + 0.5), 32.0));
}

cbuffer VertexUniformBlock : register(b0, space1)
{
    float4x4 Matrix;
};

cbuffer FragmentUniformBlock : register(b0, space3)
{
    float4 TextureSizeCamera;
    float4 DitherSettings;
    float4 DitherGlitch;
    float4 DitherGlitchAxis;
    float4 AoSettings; // x = strength, yz = sample offset in pixels, w = min visibility
    float4 NormalDitherSettings; // x = slope boost, y = quantization levels, z = actor tile field populated, w = rim strength
    float4 IndirectSettings; // x = background strength, y = foreground strength, z = actor gain, w = hero fill
    float4 DitherAnchorOffsets[MAX_DITHER_ANCHORS];
};

Texture2D Texture : register(t0, space2);
SamplerState Sampler : register(s0, space2);

Texture2D LightTexture : register(t1, space2);
SamplerState LightSampler : register(s1, space2);

Texture2D TileFieldTexture : register(t2, space2);
SamplerState TileFieldSampler : register(s2, space2);

Texture2D EmissiveTexture : register(t3, space2);
SamplerState EmissiveSampler : register(s3, space2);

Texture2D AoTexture : register(t4, space2);
SamplerState AoSampler : register(s4, space2);

Texture2D BackgroundCoverageTexture : register(t5, space2);
SamplerState BackgroundCoverageSampler : register(s5, space2);

Texture2D ForegroundTexture : register(t6, space2);
SamplerState ForegroundSampler : register(s6, space2);

Texture2D ForegroundLightTexture : register(t7, space2);
SamplerState ForegroundLightSampler : register(s7, space2);

Texture2D ForegroundEmissiveTexture : register(t8, space2);
SamplerState ForegroundEmissiveSampler : register(s8, space2);

Texture2D NormalTexture : register(t9, space2);
SamplerState NormalSampler : register(s9, space2);

Texture2D SporeFogTexture : register(t10, space2);
SamplerState SporeFogSampler : register(s10, space2);

Texture2D ActorTileFieldTexture : register(t11, space2);
SamplerState ActorTileFieldSampler : register(s11, space2);

Texture2D IndirectLightTexture : register(t12, space2);
SamplerState IndirectLightSampler : register(s12, space2);

struct VsInput
{
    float2 position : TEXCOORD0;
    float2 tex_coord : TEXCOORD1;
    float4 color : TEXCOORD2;
    float4 type : TEXCOORD4;
};

struct VsOutput
{
    float2 tex_coord : TEXCOORD0;
    float4 color : TEXCOORD1;
    float4 type : TEXCOORD4;
    float4 position : SV_Position;
};

VsOutput vertex_main(VsInput input)
{
    VsOutput output;
    output.tex_coord = input.tex_coord;
    output.color = input.color;
    output.type = input.type;
    output.position = mul(Matrix, float4(input.position, 0.0, 1.0));
    return output;
}

float bayer4_threshold(float2 position)
{
    int x = int(fmod(position.x, 4.0));
    int y = int(fmod(position.y, 4.0));
    float rank = 0.0;

    if (x == 0)
    {
        if (y == 0) rank = 16.0;
        else if (y == 1) rank = 5.0;
        else if (y == 2) rank = 13.0;
        else rank = 1.0;
    }
    else if (x == 1)
    {
        if (y == 0) rank = 8.0;
        else if (y == 1) rank = 12.0;
        else if (y == 2) rank = 4.0;
        else rank = 9.0;
    }
    else if (x == 2)
    {
        if (y == 0) rank = 14.0;
        else if (y == 1) rank = 2.0;
        else if (y == 2) rank = 15.0;
        else rank = 3.0;
    }
    else
    {
        if (y == 0) rank = 6.0;
        else if (y == 1) rank = 10.0;
        else if (y == 2) rank = 7.0;
        else rank = 11.0;
    }

    return rank / 17.0;
}

float hash_threshold(float2 position)
{
    float2 p = floor(position);
    return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
}

float hash_threshold(float value)
{
    return frac(sin(value) * 43758.5453);
}

float stripe_threshold(float2 position)
{
    float x = fmod(position.x, 4.0);
    return (x + 1.0) / 5.0;
}

float vertical_stripe_threshold(float2 position)
{
    float y = fmod(position.y, 4.0);
    return (y + 1.0) / 5.0;
}

float diagonal_threshold(float2 position)
{
    float diagonal = fmod(position.x + position.y * 2.0, 6.0);
    return (diagonal + 1.0) / 7.0;
}

float crosshatch_threshold(float2 position)
{
    float a = fmod(position.x + position.y, 4.0);
    float b = fmod(position.x - position.y + 8.0, 4.0);
    return (min(a, b) + 1.0) / 5.0;
}

float cluster_threshold(float2 position)
{
    float2 p = fmod(position, 4.0) - 1.5;
    float distanceRank = dot(p, p);
    return saturate((distanceRank + 0.5) / 5.0);
}

float checker_threshold(float2 position)
{
    float2 p = fmod(position, 4.0);
    float checker = fmod(floor(p.x) + floor(p.y), 2.0);
    float bayer = bayer4_threshold(position);
    return checker == 0.0 ? bayer * 0.75 : 0.35 + bayer * 0.65;
}

float brick_threshold(float2 position)
{
    float2 p = position;
    p.x += fmod(floor(p.y / 2.0), 2.0) * 2.0;
    float mortar = min(fmod(p.x, 4.0), fmod(p.y, 4.0));
    return saturate((mortar + 1.0) / 5.0);
}

float saw_threshold(float2 position)
{
    float saw = fmod(position.x * 2.0 + position.y, 8.0);
    return (saw + 1.0) / 9.0;
}

float edge_threshold(float2 position)
{
    float2 p = abs(fmod(position, 8.0) - 3.5);
    float edge = max(p.x, p.y);
    return saturate((edge - 0.5) / 3.5);
}

float threshold_for_pattern(float2 position, float pattern)
{
    if (pattern == 1.0)
        return diagonal_threshold(position);
    if (pattern == 2.0)
        return cluster_threshold(position);
    if (pattern == 3.0)
        return stripe_threshold(position);
    if (pattern == 4.0)
        return hash_threshold(position);
    if (pattern == 5.0)
        return vertical_stripe_threshold(position);
    if (pattern == 6.0)
        return crosshatch_threshold(position);
    if (pattern == 7.0)
        return checker_threshold(position);
    if (pattern == 8.0)
        return brick_threshold(position);
    if (pattern == 9.0)
        return saw_threshold(position);
    if (pattern >= 10.0)
        return edge_threshold(position);

    return bayer4_threshold(position);
}

float pattern_for_family(float family, float variant)
{
    float choice = fmod(variant, 3.0);
    if (family == 1.0)
        return choice == 0.0 ? 8.0 : choice == 1.0 ? 6.0 : 5.0;
    if (family == 2.0)
        return choice == 0.0 ? 2.0 : choice == 1.0 ? 9.0 : 10.0;
    if (family == 3.0)
        return choice == 0.0 ? 3.0 : choice == 1.0 ? 5.0 : 1.0;
    if (family == 5.0)
        return 0.0;
    if (family == 6.0)
        return 6.0;
    if (family >= 7.0)
        return 4.0;
    if (family >= 4.0)
        return choice == 0.0 ? 4.0 : choice == 1.0 ? 7.0 : 10.0;

    return choice == 0.0 ? 0.0 : choice == 1.0 ? 4.0 : 7.0;
}

float2 glitch_dither_position(float2 position, float4 tile_field)
{
    float family = tile_field_family(tile_field);
    float variant = floor(tile_field.r * 255.0 + 0.5);
    float family_scale = family == 2.0 ? 2.0 : family == 3.0 ? 3.0 : 1.0;
    float variant_scale = 1.0 + fmod(variant, 3.0) * 0.08;
    float2 cell = floor(position / max(DitherSettings.y * family_scale * variant_scale, 1.0));

    if (family == 1.0)
        cell.x = -cell.x;
    else if (family == 2.0)
        cell = cell.yx;
    else if (family == 3.0)
        cell.y = -cell.y;
    else if (family >= 4.0)
        cell = -cell.yx;

    float2 hash_offset = float2(fmod(variant, 4.0), fmod(floor(variant / 4.0), 4.0));
    float2 offset = float2(fmod(family * 2.0, 4.0), fmod(family * 3.0, 4.0)) + hash_offset * 0.35;
    return cell + offset;
}

float ellipse_distance(float2 position, float2 origin, float2 axis, float long_radius, float short_radius)
{
    axis = normalize(axis);
    float2 perpendicular = float2(-axis.y, axis.x);
    float2 delta = position - origin;
    float along = dot(delta, axis) / max(long_radius, 1.0);
    float across = dot(delta, perpendicular) / max(short_radius, 1.0);
    return sqrt(along * along + across * across);
}

float3 dither_light_intensity(float3 light, float2 world_position, float2 pattern_position, float4 tile_field,
    float dither_mask, float normal_slope, float detail_priority)
{
    if (DitherSettings.x <= 0.0 && NormalDitherSettings.x <= 0.0 && NormalDitherSettings.y <= 1.0)
        return light;

    float intensity = max(max(light.r, light.g), light.b);
    if (intensity <= 0.0001)
        return light;

    float family = tile_field_family(tile_field);
    float variant = floor(tile_field.r * 255.0 + 0.5);
    float amount = DitherSettings.w * DitherSettings.w * (3.0 - 2.0 * DitherSettings.w);
    float long_radius = lerp(24.0, DitherGlitch.z * 1.45, amount);
    float short_radius = long_radius * 0.38;
    float ellipse = ellipse_distance(world_position, DitherGlitch.xy, DitherGlitchAxis.xy, long_radius, short_radius);
    float proximity = smoothstep(1.0, 0.25, ellipse);
    float global_glitch = amount * smoothstep(0.60, 1.0, amount) * 0.18;
    float local_glitch = amount * proximity * 0.82 + global_glitch;

    float dirty_frame = floor(DitherGlitch.w * 9.0);
    float2 dirty_block = floor(pattern_position / 16.0);
    float dirty = hash_threshold(dirty_block.x * 17.0 + dirty_block.y * 31.0 + dirty_frame * 13.0 + variant * 0.23);
    float glitch_hash = hash_threshold(variant * 19.19 + dirty_block.x * 7.13 + dirty_block.y * 3.71 + dirty_frame * 5.17);
    float2 glitch_offset = float2(dirty - 0.5, glitch_hash - 0.5) * local_glitch * 9.0;
    float2 dither_position = glitch_dither_position(pattern_position + glitch_offset, tile_field);
    float pattern = local_glitch > 0.65 ? floor(glitch_hash * 11.0) : pattern_for_family(family, variant);
    float threshold = threshold_for_pattern(dither_position, pattern);
    threshold = saturate(threshold + (frac(variant * 0.61803398875) - 0.5) * 0.10);
    threshold = saturate(threshold + (dirty - 0.5) * local_glitch * 0.18);
    threshold = saturate(threshold + 0.5 - detail_priority);
    float dithered_intensity = saturate(intensity) < threshold ? 0.0 : 1.0;

#if 1
    // This looks better on average, but makes things fullbright when dither strength is up (dither strength becomes a sort of ambient light)
    float scale = dithered_intensity / intensity;
    float strength = saturate((DitherSettings.x > 0.0 ? DitherSettings.z * dither_mask : 0.0) + normal_slope * NormalDitherSettings.x);
    float3 result = light * lerp(1.0, scale, strength);

    if (NormalDitherSettings.y > 1.0 && normal_slope > 0.0)
    {
        float result_intensity = max(max(result.r, result.g), result.b);
        float steps = NormalDitherSettings.y - 1.0;
        float quantized = floor(saturate(result_intensity) * steps + threshold) / steps;
        result *= lerp(1.0, quantized / max(result_intensity, 0.0001), normal_slope);
    }

    return result;
#else
    // This looks worse because it has less interesting behaviour in dark solid areas, but is more "physically plausible" lighting wise...
    float strength = saturate(DitherSettings.z * dither_mask);
    float shadow = lerp(0.18, 1.0, dithered_intensity);
    return light * lerp(1.0, shadow, strength);
#endif
}

float4 light_scene(float4 scene, float4 light, float3 indirect, float4 emissive, float2 world_pixel, float2 pattern_pixel,
    float4 tile_field, float dither_mask, float normal_slope, float detail_priority)
{
    light.rgb += indirect;
    light.rgb = dither_light_intensity(light.rgb, world_pixel, pattern_pixel, tile_field, dither_mask, normal_slope, detail_priority);
    float brightness = max(max(scene.r, scene.g), scene.b);
    float rim_light = light.a * saturate(1.0 - brightness * 2.2) * NormalDitherSettings.w;
    float3 color = scene.rgb * light.rgb + light.rgb * rim_light;
    color = lerp(color, emissive.rgb, emissive.a);
    return float4(saturate(color), scene.a);
}

float4 fragment_main(VsOutput input) : SV_Target0
{
    // Prevent optimizer from removing input.type - D3D12 requires matching signatures
    if (input.type.x < -1000) discard;

    // The world is drawn at `zoom`, so several frame pixels can share one art pixel. Every
    // lighting input is sampled at that art pixel's centre: otherwise the light varies within a
    // single dither cell and the quantizer below splits it into fractions of a cell.
    float zoom = DitherGlitchAxis.z > 0.0 ? DitherGlitchAxis.z : 1.0;
    float2 art_pixel = floor(screen_pixel(input.position, TextureSizeCamera.y) / zoom);
    float2 sample = target_uv((art_pixel + 0.5) * zoom / TextureSizeCamera.xy);
    float4 background_scene = Texture.Sample(Sampler, sample) * input.color;
    float4 background_light = LightTexture.Sample(LightSampler, sample);
    float4 tile_field = TileFieldTexture.Sample(TileFieldSampler, sample);
    float4 background_emissive = EmissiveTexture.Sample(EmissiveSampler, sample);
    float3 indirect_light = IndirectLightTexture.Sample(IndirectLightSampler, sample).rgb;
    float4 normal_data = NormalTexture.Sample(NormalSampler, sample);
    float dither_mask = normal_data.b;
    float normal_slope = normal_data.a * saturate(length(normal_data.rg * 2.0 - 1.0));
    float2 world_pixel = TextureSizeCamera.zw + art_pixel + 0.5;
    float2 pattern_pixel = world_pixel + DitherAnchorOffsets[tile_field_anchor(tile_field)].xy;
    float4 background_tile_field = tile_field;
    float2 background_pattern_pixel = pattern_pixel;
    float background_detail_priority = 0.5;
    float actor_coverage = 0.0;
    if (NormalDitherSettings.z > 0.5)
    {
        float4 actor_tile_field = ActorTileFieldTexture.Sample(ActorTileFieldSampler, sample);
        float packed_actor_family_variant = floor(actor_tile_field.r * 255.0 + 0.5);
        float actor_family = floor(packed_actor_family_variant / 32.0);
        float actor_variant = fmod(packed_actor_family_variant, 32.0);
        actor_coverage = step(0.5, actor_family);
        float4 decoded_actor_tile_field = float4(actor_variant / 255.0, actor_family * 32.0 / 255.0, 0.0, 1.0);
        background_tile_field = lerp(tile_field, decoded_actor_tile_field, actor_coverage);
        float packed_actor_scale_priority = floor(actor_tile_field.g * 255.0 + 0.5);
        float actor_scale = exp2((floor(packed_actor_scale_priority / 8.0) - 16.0) / 8.0);
        float actor_detail_priority = fmod(packed_actor_scale_priority, 8.0) / 7.0;
        background_pattern_pixel = lerp(pattern_pixel, world_pixel / actor_scale, actor_coverage);
        background_detail_priority = lerp(0.5, actor_detail_priority, actor_coverage);
    }

    // Ambient occlusion: darken the light where blurred foreground coverage
    // overhangs background tiles. Applied to the light buffer before the dither
    // quantizer so the occlusion breaks up into the same dither patterns as the
    // rest of the lighting, with AoSettings.w as a floor so it never fully vanishes.
    if (AoSettings.x > 0.0)
    {
        float2 ao_uv = sample + AoSettings.yz / TextureSizeCamera.xy;
        float occlusion = saturate(AoTexture.Sample(AoSampler, ao_uv).r * AoSettings.x);
        // Gate by background tile coverage so the backdrop (which has no tiles)
        // is never darkened - AO only lands on actual background tiles.
        float coverage = BackgroundCoverageTexture.Sample(BackgroundCoverageSampler, sample).r;
        background_light.rgb *= lerp(1.0, AoSettings.w, occlusion * coverage);
    }

    float actor_indirect = lerp(1.0, IndirectSettings.z, actor_coverage);
    float hero_indirect = actor_coverage * IndirectSettings.w;
    float3 background_indirect = indirect_light * IndirectSettings.x * actor_indirect + indirect_light * hero_indirect;
    float4 background = light_scene(background_scene, background_light, background_indirect, background_emissive, world_pixel,
        background_pattern_pixel, background_tile_field, dither_mask, normal_slope, background_detail_priority);
    background.rgb = saturate(background.rgb + SporeFogTexture.Sample(SporeFogSampler, sample).rgb);
    float4 foreground = light_scene(
        ForegroundTexture.Sample(ForegroundSampler, sample) * input.color,
        ForegroundLightTexture.Sample(ForegroundLightSampler, sample),
        indirect_light * IndirectSettings.y,
        ForegroundEmissiveTexture.Sample(ForegroundEmissiveSampler, sample),
        world_pixel,
        pattern_pixel,
        tile_field,
        dither_mask,
        normal_slope,
        0.5);
    return saturate(foreground + background * (1.0 - foreground.a));
}
