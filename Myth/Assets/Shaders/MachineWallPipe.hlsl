#include "Common.hlsl"

struct PipeInstance
{
    float4 OriginScale;
    float4 Basis;
    float4 DepthUvRoughness;
    float4 Color;
};

StructuredBuffer<PipeInstance> Instances : register(t0, space0);

cbuffer VertexParams : register(b0, space1)
{
    float2 Camera;
    float2 TargetSize;
    float2 ProjectionSkew;
    float Zoom;
    float Time;
};

Texture2D DetailTexture : register(t0, space2);
SamplerState DetailSampler : register(s0, space2);
Texture2D DetailNormalTexture : register(t1, space2);
SamplerState DetailNormalSampler : register(s1, space2);

cbuffer FragmentParams : register(b0, space3)
{
    float4 LightSpecular;
    float4 ViewNormalPass;
    float4 NormalSettings;
    float4 ActorFieldSettings;
};

struct VsInput
{
    float2 Centerline : TEXCOORD0;
    float3 Radial : TEXCOORD1;
    float2 Tangent : TEXCOORD2;
    float2 TexCoord : TEXCOORD3;
};

struct VsOutput
{
    float3 Normal : TEXCOORD0;
    float3 Tangent : TEXCOORD1;
    float2 TexCoord : TEXCOORD2;
    float4 ColorRoughness : TEXCOORD3;
    float FieldVariant : TEXCOORD4;
    float4 Position : SV_Position;
};

float2 transform_basis(float2 value, float4 basis)
{
    return basis.xy * value.x + basis.zw * value.y;
}

VsOutput vertex_main(VsInput input, uint instanceId : SV_InstanceID)
{
    PipeInstance instance = Instances[instanceId];
    float radius = lerp(instance.OriginScale.w, instance.Color.w, input.TexCoord.x);
    float2 local = input.Centerline * instance.OriginScale.z + input.Radial.xy * radius;
    float3 world = float3(
        instance.OriginScale.xy + transform_basis(local, instance.Basis),
        instance.DepthUvRoughness.x + input.Radial.z * radius);
    float2 screen = world.xy + world.z * ProjectionSkew;
    float2 clip = (screen - Camera) * Zoom / TargetSize * 2.0 - 1.0;
    clip.y = -clip.y;

    VsOutput output;
    output.Normal = normalize(float3(transform_basis(input.Radial.xy, instance.Basis), input.Radial.z));
    output.Tangent = normalize(float3(transform_basis(input.Tangent, instance.Basis), 0.0));
    output.TexCoord = float2(
        instance.DepthUvRoughness.y + input.TexCoord.x * instance.DepthUvRoughness.z,
        input.TexCoord.y);
    output.ColorRoughness = float4(instance.Color.rgb, instance.DepthUvRoughness.w);
    output.FieldVariant = frac(instance.DepthUvRoughness.y);
    output.Position = float4(clip, 0.0, 1.0);
    return output;
}

float4 fragment_main(VsOutput input) : SV_Target0
{
    float3 geometricNormal = normalize(input.Normal);
    float3 viewDirection = normalize(ViewNormalPass.xyz);
    if (dot(geometricNormal, viewDirection) <= 0.001)
        discard;

    float2 detailUv = input.TexCoord * float2(1.4, 3.0) * NormalSettings.y;
    float grain = DetailTexture.Sample(DetailSampler, detailUv).r;
    float3 sampledNormal = DetailNormalTexture.Sample(DetailNormalSampler, detailUv).xyz * 2.0 - 1.0;
    float3 tangent = normalize(input.Tangent);
    float3 bitangent = normalize(cross(geometricNormal, tangent));
    float3 normal = normalize(
        tangent * sampledNormal.x * NormalSettings.x +
        bitangent * sampledNormal.y * NormalSettings.x +
        geometricNormal * max(sampledNormal.z, 0.3));

    if (ViewNormalPass.w > 2.5)
    {
        float ringDetail = pow(abs(sin(input.TexCoord.x * 15.707963)), 20.0);
        float ribDetail = abs(cos(input.TexCoord.y * 25.132741));
        float detail = saturate(length(sampledNormal.xy) * 0.55 + ringDetail * 0.30 + ribDetail * 0.10 + abs(grain - 0.5) * 0.25);
        float priority = 0.5 + (detail - 0.5) * ActorFieldSettings.z * 0.7;
        float familyScale = (ActorFieldSettings.x * 32.0 + ActorFieldSettings.y) / 255.0;
        return float4(input.FieldVariant, familyScale, priority, 1.0);
    }

    if (ViewNormalPass.w > 0.5)
        return float4(normal * 0.5 + 0.5, 1.0);

    float roughness = saturate(input.ColorRoughness.w + (grain - 0.5) * 0.12);
    float3 lightDirection = normalize(LightSpecular.xyz);
    float diffuse = 0.42 + saturate(dot(normal, lightDirection)) * 0.58;
    float3 halfDirection = normalize(lightDirection + viewDirection);
    float specular = pow(saturate(dot(normal, halfDirection)), lerp(88.0, 24.0, roughness));
    specular *= LightSpecular.w * 1.25 * (1.0 - roughness * 0.55);
    float rings = 0.94 + pow(abs(sin(input.TexCoord.x * 15.707963)), 20.0) * 0.08;
    float ribs = 0.97 + cos(input.TexCoord.y * 25.132741) * 0.03;
    float tone = rings * ribs * lerp(0.88, 1.1, grain);
    float3 color = input.ColorRoughness.rgb * tone * diffuse + specular;
    return float4(saturate(color), 1.0);
}
