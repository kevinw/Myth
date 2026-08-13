#include "Common.hlsl"

struct Instance
{
    float4 Rect;
    float4 DepthMotion;
    float4 AxisAmount;
    float4 TintRoughness;
    float4 Uv;
    float4 Flags;
};

StructuredBuffer<Instance> Instances : register(t0, space0);

cbuffer VertexParams : register(b0, space1)
{
    float2 Camera;
    float2 TargetSize;
    float2 ProjectionSkew;
    float Zoom;
    float Time;
};

Texture2D PatternTexture : register(t0, space2);
SamplerState PatternSampler : register(s0, space2);
Texture2D DetailTexture : register(t1, space2);
SamplerState DetailSampler : register(s1, space2);

cbuffer FragmentParams : register(b0, space3)
{
    float4 LightSpecular;
    float4 ViewNormalPass;
    float4 NormalSettings;
    float4 ActorFieldSettings;
};

struct VsInput
{
    float3 Position : TEXCOORD0;
    float3 Normal : TEXCOORD1;
    float2 TexCoord : TEXCOORD2;
    float3 Tangent : TEXCOORD3;
};

struct VsOutput
{
    float3 Normal : TEXCOORD0;
    float3 Tangent : TEXCOORD1;
    float2 TexCoord : TEXCOORD2;
    float4 TintRoughness : TEXCOORD3;
    float4 Flags : TEXCOORD4;
    float4 Position : SV_Position;
};

float3 rotate_axis(float3 value, float3 axis, float angle)
{
    float s = sin(angle);
    float c = cos(angle);
    return value * c + cross(axis, value) * s + axis * dot(axis, value) * (1.0 - c);
}

VsOutput vertex_main(VsInput input, uint instanceId : SV_InstanceID)
{
    Instance instance = Instances[instanceId];
    float phase = Time * instance.DepthMotion.w * 6.2831853 + instance.DepthMotion.z;
    float wave = sin(phase);
    float motionType = instance.DepthMotion.y;
    float amount = instance.AxisAmount.w;
    float3 translation = float3(0.0, 0.0, 0.0);
    float angle = 0.0;
    if (motionType == 1.0)
        translation.z = wave * amount;
    else if (motionType == 2.0)
        translation.x = wave * amount;
    else if (motionType == 3.0)
        translation.y = wave * amount;
    else if (motionType == 4.0)
        angle = wave * amount;

    float3 size = float3(instance.Rect.zw, instance.DepthMotion.x);
    float3 local = input.Position * size;
    float3 normal = input.Normal;
    float3 tangent = input.Tangent;
    if (angle != 0.0)
    {
        float3 axis = normalize(instance.AxisAmount.xyz);
        local = rotate_axis(local, axis, angle);
        normal = rotate_axis(normal, axis, angle);
        tangent = rotate_axis(tangent, axis, angle);
    }

    float3 world = local + float3(instance.Rect.xy, instance.DepthMotion.x * 0.5) + translation;
    float2 screen = world.xy + world.z * ProjectionSkew;
    float2 clip = (screen - Camera) * Zoom / TargetSize * 2.0 - 1.0;
    clip.y = -clip.y;

    VsOutput output;
    output.Normal = normal;
    output.Tangent = tangent;
    output.TexCoord = instance.Uv.xy + input.TexCoord * instance.Uv.zw;
    output.TintRoughness = instance.TintRoughness;
    output.Flags = instance.Flags;
    output.Position = float4(clip, 0.0, 1.0);
    return output;
}

float3 unpack_normal(float3 value)
{
    return value * 2.0 - 1.0;
}

float4 fragment_main(VsOutput input) : SV_Target0
{
    float3 geometricNormal = normalize(input.Normal);
    float3 viewDirection = normalize(ViewNormalPass.xyz);
    if (dot(geometricNormal, viewDirection) <= 0.001)
        discard;

    float3 tangent = normalize(input.Tangent);
    float3 bitangent = normalize(cross(geometricNormal, tangent));
    float4 patternSample = PatternTexture.Sample(PatternSampler, input.TexCoord);
    float4 detailSample = DetailTexture.Sample(DetailSampler, input.TexCoord * NormalSettings.z);
    float3 coarse = unpack_normal(patternSample.gba);
    float3 detail = unpack_normal(detailSample.gba);
    float3 tangentNormal = normalize(float3(
        (coarse.xy * NormalSettings.x + detail.xy * NormalSettings.y) * (1.0 - input.Flags.y),
        max(coarse.z * detail.z, 0.2)));
    float3 normal = normalize(
        tangent * tangentNormal.x +
        bitangent * tangentNormal.y +
        geometricNormal * tangentNormal.z);

    if (ViewNormalPass.w > 2.5)
    {
        float pattern = patternSample.r;
        float grain = detailSample.r;
        float priorityDetail = saturate(length(coarse.xy) * 0.55 + length(detail.xy) * 0.30 + abs(pattern - grain) * 0.30);
        float priority = 0.5 + (priorityDetail - 0.5) * ActorFieldSettings.z * 0.7;
        float variant = fmod(floor(saturate(input.Flags.z) * 255.0 + 0.5), 30.0);
        float familyVariant = (ActorFieldSettings.x * 32.0 + variant) / 255.0;
        float scalePriority = (ActorFieldSettings.y * 8.0 + floor(saturate(priority) * 7.0 + 0.5)) / 255.0;
        return float4(familyVariant, scalePriority, 0.0, 1.0);
    }

    if (ViewNormalPass.w > 0.5)
    {
        if (ViewNormalPass.w > 1.5)
            return float4(input.TintRoughness.rgb * input.Flags.x, saturate(input.Flags.x));
        return float4(normal * 0.5 + 0.5, 1.0);
    }

    float pattern = patternSample.r;
    float grain = detailSample.r;
    pattern = lerp(pattern, 0.72, input.Flags.y);
    grain = lerp(grain, 0.5, input.Flags.y);
    float roughness = saturate(input.TintRoughness.w + (grain - 0.5) * 0.16);
    float3 lightDirection = normalize(LightSpecular.xyz);
    float diffuse = 0.48 + saturate(dot(normal, lightDirection)) * 0.52;
    float3 halfDirection = normalize(lightDirection + viewDirection);
    float specular = pow(saturate(dot(normal, halfDirection)), lerp(72.0, 18.0, roughness));
    specular *= LightSpecular.w * (1.0 - roughness * 0.65);
    float tone = lerp(0.76, 1.12, pattern * 0.68 + grain * 0.32);
    float3 color = input.TintRoughness.rgb * tone * diffuse + specular;
    return float4(saturate(color), 1.0);
}
