cbuffer VertexUniformBlock : register(b0, space1)
{
    float4x4 Matrix;
};

cbuffer FragmentUniformBlock : register(b0, space3)
{
    float4 TextureSize;
    float4 LightPositionRadiusHeight;
    float4 LightColorIntensity;
};

Texture2D Texture : register(t0, space2);
SamplerState Sampler : register(s0, space2);

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

float4 fragment_main(VsOutput input) : SV_Target0
{
    float2 pixel = input.TexCoord * TextureSize.xy;
    float2 lightPosition = LightPositionRadiusHeight.xy;
    float radius = LightPositionRadiusHeight.z;
    float height = LightPositionRadiusHeight.w;

    float2 toLight2D = lightPosition - pixel;
    float distanceToLight = length(toLight2D);
    float directional = radius < 0.0 ? 1.0 : 0.0;
    float attenuation = directional > 0.5 ? 1.0 : saturate(1.0 - distanceToLight / radius);
    attenuation *= attenuation;

    float4 normalData = Texture.Sample(Sampler, input.TexCoord);
    float normalStrength = normalData.a;
    float3 normal = normalize(normalData.rgb * 2.0 - 1.0);
    float3 pointLightDirection = normalize(float3(toLight2D, height));
    float3 sunLightDirection = normalize(float3(-lightPosition, height));
    float3 lightDirection = directional > 0.5 ? sunLightDirection : pointLightDirection;
    float diffuse = saturate(dot(normal, lightDirection));
    float normalResponse = lerp(1.0, 0.12 + diffuse * 2.15, normalStrength);

    float3 contribution = LightColorIntensity.rgb * LightColorIntensity.a * attenuation * normalResponse;
    return float4(saturate(contribution), attenuation);
}
