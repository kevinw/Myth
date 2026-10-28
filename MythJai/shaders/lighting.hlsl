// Lights the world, drawn unlit into a render target, onto the window: the parts of Myth's
// LightMasks (compute) and LightingComposite that the slice needs, in one pass.
// Per art pixel: ambient plus each light's (1 - d/r)^2 falloff, drifting light noise, the
// shoulder that rolls off bright light, the rim that lifts dark scenery near lights, and the
// ordered dither that breaks light levels into patterns.
// Background (Texture) and foreground (FrontTexture) are lit apart and stacked like Myth's: only
// the background gets exposure and noise, and the foreground's rim is added even where it's
// transparent, so a faint glow of the light hangs over the backdrop.
// Lights bend by the tiles' baked normals (LightMasks' normal_response).
// Lights are shadowed by the occluders (shadows.jai), softened like LightMasks' point_visibility,
// and actors cast short soft shadows behind them (sprite_visibility).
// Background tiles darken under the foreground that overhangs them (ambient_occlusion).
// Not ported yet: emissive, spore fog, the player's dither glitch.

cbuffer VertexUniforms : register(b0, space1)
{
    float4x4 Matrix;
};

#define MAX_LIGHTS 16

cbuffer FragmentUniforms : register(b0, space3)
{
    float4 View;        // xy = view size in art pixels, zw = camera (world pixel at the view's top-left)
    float4 Ambient;     // rgb = ambient light, a = its strength
    float4 Noise;       // x = strength, y = pivot, zw = drift offset in noise uv
    float4 NoiseScale;  // xy = noise uv per world pixel
    float4 Response;    // x = background exposure, y = rim strength, z = shoulder knee, w = shoulder ceiling
    float4 Dither;      // x = strength, y = pattern scale
    float4 Counts;      // x = light count, y = shadow bias, z = 1 to show the background's light instead
    float4 NormalLight; // x = floor, y = gain
    float4 SpriteShadow; // x = strength, y = shortest, z = longest
    float4 Lights[MAX_LIGHTS * 2];  // [2i] = xy position, z radius, w height; [2i+1] = rgb color * intensity, a shadow source radius
};

Texture2D Texture : register(t0, space2);
SamplerState Sampler : register(s0, space2);
Texture2D NoiseTexture : register(t1, space2);
SamplerState NoiseSampler : register(s1, space2);
Texture2D NormalTexture : register(t2, space2);
SamplerState NormalSampler : register(s2, space2);
Texture2D FrontTexture : register(t3, space2);
SamplerState FrontSampler : register(s3, space2);
Texture2D TileFieldTexture : register(t4, space2);  // per tile: r = variant, g = dither family << 5
SamplerState TileFieldSampler : register(s4, space2);
Texture2D ShadowDepth : register(t5, space2);  // row per light: r = how far each of its rays gets
SamplerState ShadowSampler : register(s5, space2);
#define SHADOW_SAMPLES 512
Texture2D ActorMask : register(t6, space2);  // r = an actor that casts a sprite shadow, g = a background tile, b = the player or an enemy
SamplerState ActorMaskSampler : register(s6, space2);
#define ACTOR_DITHER 0.2
// Manager.Visuals' ambient occlusion: blur, offset, and the light it never drops below.
#define AO_BLUR 8.0
#define AO_OFFSET float2(0.0, -3.0)
#define AO_MIN_VISIBILITY 0.35

struct VsInput
{
    float2 Position : TEXCOORD0;
    float2 TexCoord : TEXCOORD1;
    float4 Color : TEXCOORD2;
    float4 Type : TEXCOORD4;
};

struct VsOutput
{
    float2 TexCoord : TEXCOORD0;
    float4 Color : TEXCOORD1;
    float4 Type : TEXCOORD4;
    float4 Position : SV_Position;
};

VsOutput vertex_main(VsInput input)
{
    VsOutput output;
    output.TexCoord = input.TexCoord;
    output.Color = input.Color;
    output.Type = input.Type;
    output.Position = mul(Matrix, float4(input.Position, 0.0, 1.0));
    return output;
}

// Myth's dither patterns (LightingComposite.hlsl), verbatim: the tile field picks one per tile.
float tile_field_family(float4 field)
{
    return floor(floor(field.g * 255.0 + 0.5) / 32.0);
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
    float2 cell = floor(position / max(Dither.y * family_scale * variant_scale, 1.0));

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

float3 light_shoulder(float3 light)
{
    float knee = Response.z;
    float ceiling = max(Response.w, knee + 0.001);
    float peak = max(light.r, max(light.g, light.b));
    if (peak <= knee)
        return light;
    float over = (peak - knee) / (ceiling - knee);
    return light * (knee + (ceiling - knee) * over / (1.0 + over)) / peak;
}

float shadow_sample(int light, int index, float distance)
{
    index = (index % SHADOW_SAMPLES + SHADOW_SAMPLES) % SHADOW_SAMPLES;
    // Sampled at the texel's centre (not Load, which shadercross gives a texture slot of its own).
    float2 texel = (float2(index, light) + 0.5) / float2(SHADOW_SAMPLES, MAX_LIGHTS);
    return distance > ShadowDepth.SampleLevel(ShadowSampler, texel, 0).r + Counts.y ? 0.0 : 1.0;
}

// Five rays around the pixel's, spread wider with distance and the light's size.
float shadow_visibility(int light, float2 delta, float distance, float radius, float source_radius)
{
    float angle = atan2(delta.y, delta.x);
    if (angle < 0.0)
        angle += 6.28318530718;
    int center = (int)round(angle / 6.28318530718 * SHADOW_SAMPLES - 0.5);
    int step = max(1, (int)round(source_radius * saturate(distance / radius)));
    static const float weights[5] = { 0.1, 0.2, 0.4, 0.2, 0.1 };
    float visibility = 0.0;
    for (int i = 0; i < 5; i++)
        visibility += weights[i] * shadow_sample(light, center + (i - 2) * step, distance);
    return visibility;
}

// LightShadowDepth's sprite exit and point_sample_visibility's fade, marched per pixel: past the
// first actor between it and the light, the light dims and recovers over a length that grows the
// nearer the actor stands to the light.
float sprite_visibility(float2 light, float2 world, float distance, float radius)
{
    float2 direction = (world - light) / max(distance, 0.0001);
    bool inside = false;
    for (float t = 0.5; t < distance; t += 0.5)
    {
        float2 uv = (light + direction * t - View.zw) / View.xy;
        bool occluded = all(uv >= 0.0) && all(uv < 1.0) && ActorMask.SampleLevel(ActorMaskSampler, uv, 0).r > 0.1;
        if (!inside)
        {
            inside = occluded;
            continue;
        }
        if (!occluded)
        {
            float reach = saturate(1.0 - t / radius);
            float length = lerp(SpriteShadow.y, SpriteShadow.z, reach);
            return 1.0 - SpriteShadow.x * saturate(1.0 - (distance - t) / length);
        }
    }
    return 1.0;
}

// How much foreground hangs over a pixel: its coverage blurred like Blur.hlsl's two passes (one
// 2D kernel here, every other pixel), looked up a little above.
float ambient_occlusion(float2 uv)
{
    float2 texel = 1.0 / View.xy;
    float2 center = uv + AO_OFFSET * texel;
    float sigma = AO_BLUR * 0.5;
    float sum = 0.0;
    float weights = 0.0;
    for (int y = -4; y <= 4; y++)
    {
        for (int x = -4; x <= 4; x++)
        {
            float2 offset = float2(x, y) * AO_BLUR / 4.0;
            float weight = exp(-dot(offset, offset) / (2.0 * sigma * sigma));
            float2 at = clamp(center + offset * texel, texel * 0.5, 1.0 - texel * 0.5);
            sum += FrontTexture.SampleLevel(FrontSampler, at, 0).a * weight;
            weights += weight;
        }
    }
    return saturate(sum / weights);
}

// LightingComposite's dither_light_intensity without the player's dither glitch.
float4 light_scene(float4 scene, float4 light, float2 world, float4 tile_field, float dither_mask)
{
    float intensity = max(max(light.r, light.g), light.b);
    if (Dither.x > 0.0 && intensity > 0.0001)
    {
        float family = tile_field_family(tile_field);
        float variant = floor(tile_field.r * 255.0 + 0.5);
        float threshold = threshold_for_pattern(glitch_dither_position(world, tile_field), pattern_for_family(family, variant));
        threshold = saturate(threshold + (frac(variant * 0.61803398875) - 0.5) * 0.10);
        float lit = saturate(intensity) < threshold ? 0.0 : 1.0;
        light.rgb *= lerp(1.0, lit / intensity, saturate(Dither.x * dither_mask));
    }
    float brightness = max(max(scene.r, scene.g), scene.b);
    float rim = light.a * saturate(1.0 - brightness * 2.2) * Response.y;
    return float4(saturate(scene.rgb * light.rgb + light.rgb * rim), scene.a);
}

float4 fragment_main(VsOutput input) : SV_Target0
{
    // Every input is sampled at the art pixel's centre, so the light can't vary inside one.
    float2 art_pixel = min(floor(input.TexCoord * View.xy), View.xy - 1.0);
    float2 uv = (art_pixel + 0.5) / View.xy;
    float2 world = View.zw + art_pixel + 0.5;

    float4 normal_data = NormalTexture.Sample(NormalSampler, uv);
    float3 normal = normalize(normal_data.rgb * 2.0 - 1.0);
    float4 light = Ambient;
    float4 back_light = Ambient;
    for (int i = 0; i < int(Counts.x); i++)
    {
        float4 position = Lights[i * 2];
        float distance = length(position.xy - world);
        float attenuation = saturate(1.0 - distance / position.z);
        if (attenuation <= 0.0)
            continue;
        float3 to_light = float3(position.xy - world, position.w);
        float response = lerp(1.0, NormalLight.x + saturate(dot(normal, normalize(to_light))) * NormalLight.y, normal_data.a);
        float4 contribution = float4(Lights[i * 2 + 1].rgb * attenuation * attenuation * response, attenuation);
        light += contribution;
        back_light += contribution * shadow_visibility(i, world - position.xy, distance, position.z, Lights[i * 2 + 1].a)
            * sprite_visibility(position.xy, world, distance, position.z);
    }
    light.a = saturate(light.a);
    back_light.a = saturate(back_light.a);
    if (Counts.z > 0.0)
        return float4(back_light.rgb, 1.0);

    back_light.rgb *= Response.x;
    float4 actor_mask = ActorMask.Sample(ActorMaskSampler, uv);
    if (actor_mask.g > 0.0)
        back_light.rgb *= lerp(1.0, AO_MIN_VISIBILITY, ambient_occlusion(uv) * actor_mask.g);
    if (Noise.x > 0.0)
    {
        float noise = NoiseTexture.Sample(NoiseSampler, world * NoiseScale.xy - Noise.zw).r;
        back_light.rgb *= max(0.0, 1.0 + Noise.x * (noise - Noise.y));
    }
    back_light.rgb = light_shoulder(back_light.rgb);
    light.rgb = light_shoulder(light.rgb);

    float4 tile_field = TileFieldTexture.Sample(TileFieldSampler, uv);
    float dither_mask = lerp(normal_data.b, ACTOR_DITHER, actor_mask.b);
    float4 back = light_scene(Texture.Sample(Sampler, uv), back_light, world, tile_field, dither_mask);
    float4 front = light_scene(FrontTexture.Sample(FrontSampler, uv), light, world, tile_field, dither_mask);
    return saturate(front + back * (1.0 - front.a)) * input.Color;
}
