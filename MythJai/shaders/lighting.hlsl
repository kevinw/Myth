// Lights the world, drawn unlit into a render target, onto the window: the parts of Myth's
// LightMasks (compute) and LightingComposite that the slice needs, in one pass.
// Per art pixel: ambient plus each light's (1 - d/r)^2 falloff, drifting light noise, the
// shoulder that rolls off bright light, the rim that lifts dark scenery near lights, and the
// ordered dither that breaks light levels into patterns.
// Not ported yet: shadows, normals, AO, emissive, tile-field dither families, spore fog.

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
    float4 Dither;      // x = strength
    float4 Counts;      // x = light count
    float4 Lights[MAX_LIGHTS * 2];  // [2i] = xy position, z radius; [2i+1] = rgb color * intensity
};

Texture2D Texture : register(t0, space2);
SamplerState Sampler : register(s0, space2);
Texture2D NoiseTexture : register(t1, space2);
SamplerState NoiseSampler : register(s1, space2);

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

float bayer4_threshold(float2 position)
{
    static const float ranks[16] = { 16, 8, 14, 6,  5, 12, 2, 10,  13, 4, 15, 7,  1, 9, 3, 11 };
    int x = int(fmod(position.x, 4.0));
    int y = int(fmod(position.y, 4.0));
    return ranks[y * 4 + x] / 17.0;
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

float4 fragment_main(VsOutput input) : SV_Target0
{
    // Every input is sampled at the art pixel's centre, so the light can't vary inside one.
    float2 art_pixel = min(floor(input.TexCoord * View.xy), View.xy - 1.0);
    float2 uv = (art_pixel + 0.5) / View.xy;
    float4 scene = Texture.Sample(Sampler, uv);
    float2 world = View.zw + art_pixel + 0.5;

    float4 light = Ambient;
    for (int i = 0; i < int(Counts.x); i++)
    {
        float4 position = Lights[i * 2];
        float attenuation = saturate(1.0 - length(position.xy - world) / position.z);
        if (attenuation <= 0.0)
            continue;
        light += float4(Lights[i * 2 + 1].rgb * attenuation * attenuation, attenuation);
    }
    light.a = saturate(light.a);
    light.rgb *= Response.x;

    if (Noise.x > 0.0)
    {
        float noise = NoiseTexture.Sample(NoiseSampler, world * NoiseScale.xy - Noise.zw).r;
        light.rgb *= max(0.0, 1.0 + Noise.x * (noise - Noise.y));
    }
    light.rgb = light_shoulder(light.rgb);

    float intensity = max(max(light.r, light.g), light.b);
    if (Dither.x > 0.0 && intensity > 0.0001)
    {
        float lit = saturate(intensity) < bayer4_threshold(world) ? 0.0 : 1.0;
        light.rgb *= lerp(1.0, lit / intensity, saturate(Dither.x));
    }

    float brightness = max(max(scene.r, scene.g), scene.b);
    float rim = light.a * saturate(1.0 - brightness * 2.2) * Response.y;
    return float4(saturate(scene.rgb * light.rgb + light.rgb * rim), scene.a) * input.Color;
}
