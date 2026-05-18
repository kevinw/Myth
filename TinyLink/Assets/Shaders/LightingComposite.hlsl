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
};

Texture2D Texture : register(t0, space2);
SamplerState Sampler : register(s0, space2);

Texture2D LightTexture : register(t1, space2);
SamplerState LightSampler : register(s1, space2);

Texture2D TileFieldTexture : register(t2, space2);
SamplerState TileFieldSampler : register(s2, space2);

Texture2D DitherMaskTexture : register(t3, space2);
SamplerState DitherMaskSampler : register(s3, space2);

Texture2D EmissiveTexture : register(t4, space2);
SamplerState EmissiveSampler : register(s4, space2);

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
    if (family >= 4.0)
        return choice == 0.0 ? 4.0 : choice == 1.0 ? 7.0 : 10.0;

    return choice == 0.0 ? 0.0 : choice == 1.0 ? 4.0 : 7.0;
}

float2 glitch_dither_position(float2 position, float4 tile_field)
{
    float family = floor(tile_field.g * 4.0 + 0.5);
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

float3 dither_light_intensity(float3 light, float2 position, float4 tile_field, float dither_mask)
{
    if (DitherSettings.x <= 0.0)
        return light;

    float intensity = max(max(light.r, light.g), light.b);
    if (intensity <= 0.0001)
        return light;

    float family = floor(tile_field.g * 4.0 + 0.5);
    float variant = floor(tile_field.r * 255.0 + 0.5);
    float amount = DitherSettings.w * DitherSettings.w * (3.0 - 2.0 * DitherSettings.w);
    float long_radius = lerp(24.0, DitherGlitch.z * 1.45, amount);
    float short_radius = long_radius * 0.38;
    float ellipse = ellipse_distance(position, DitherGlitch.xy, DitherGlitchAxis.xy, long_radius, short_radius);
    float proximity = smoothstep(1.0, 0.25, ellipse);
    float global_glitch = amount * smoothstep(0.60, 1.0, amount) * 0.18;
    float local_glitch = amount * proximity * 0.82 + global_glitch;

    float dirty_frame = floor(DitherGlitch.w * 9.0);
    float2 dirty_block = floor(position / 16.0);
    float dirty = hash_threshold(dirty_block.x * 17.0 + dirty_block.y * 31.0 + dirty_frame * 13.0 + variant * 0.23);
    float glitch_hash = hash_threshold(variant * 19.19 + dirty_block.x * 7.13 + dirty_block.y * 3.71 + dirty_frame * 5.17);
    float2 glitch_offset = float2(dirty - 0.5, glitch_hash - 0.5) * local_glitch * 9.0;
    float2 dither_position = glitch_dither_position(position + glitch_offset, tile_field);
    float pattern = local_glitch > 0.65 ? floor(glitch_hash * 11.0) : pattern_for_family(family, variant);
    float threshold = threshold_for_pattern(dither_position, pattern);
    threshold = saturate(threshold + (frac(variant * 0.61803398875) - 0.5) * 0.10);
    threshold = saturate(threshold + (dirty - 0.5) * local_glitch * 0.18);
    float dithered_intensity = saturate(intensity) < threshold ? 0.0 : 1.0;
    float scale = dithered_intensity / intensity;
    return light * lerp(1.0, scale, saturate(DitherSettings.z * dither_mask));
}

float4 fragment_main(VsOutput input) : SV_Target0
{
    float4 scene = Texture.Sample(Sampler, input.tex_coord) * input.color;
    float4 light = LightTexture.Sample(LightSampler, input.tex_coord);
    float4 tile_field = TileFieldTexture.Sample(TileFieldSampler, input.tex_coord);
    float dither_mask = DitherMaskTexture.Sample(DitherMaskSampler, input.tex_coord).r;
    float4 emissive = EmissiveTexture.Sample(EmissiveSampler, input.tex_coord);
    float2 screen_pixel = input.tex_coord * TextureSizeCamera.xy;
    float2 world_pixel = TextureSizeCamera.zw + screen_pixel;
    light.rgb = dither_light_intensity(light.rgb, world_pixel, tile_field, dither_mask);
    float brightness = max(max(scene.r, scene.g), scene.b);
    float rim_light = light.a * saturate(1.0 - brightness * 2.2) * 0.18;
    float3 color = scene.rgb * light.rgb + light.rgb * rim_light;
    color = lerp(color, emissive.rgb, emissive.a);
    return float4(saturate(color), scene.a);
}
