#include "Common.hlsl"

Texture2D NormalTexture : register(t0, space0);
SamplerState NormalSampler : register(s0, space0);
StructuredBuffer<float4> Field : register(t1, space0);

#if BROWSER
[[vk::image_format("rgba16f")]]
#endif
RWTexture2D<float4> IndirectLight : register(u0, space1);

cbuffer Params : register(b0, space2)
{
    uint Width;
    uint Height;
    uint GridWidth;
    uint GridHeight;
    float GridSpacing;
    float Directionality;
    float NormalWrap;
    float ResponseCurve;
    float2 ViewOrigin;
    float2 ViewScale;
};

float3 cell_radiance(int2 cell)
{
    cell = clamp(cell, int2(0, 0), int2(GridWidth - 1, GridHeight - 1));
    return Field[cell.y * GridWidth + cell.x].rgb;
}

float3 field_radiance(float2 cellPosition)
{
    int2 base = int2(floor(cellPosition));
    float2 blend = frac(cellPosition);
    float3 a = lerp(cell_radiance(base), cell_radiance(base + int2(1, 0)), blend.x);
    float3 b = lerp(cell_radiance(base + int2(0, 1)), cell_radiance(base + int2(1, 1)), blend.x);
    return lerp(a, b, blend.y);
}

[numthreads(8, 8, 1)]
void compute_main(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= Width || id.y >= Height)
        return;

    float2 uv = (float2(id.xy) + 0.5) / float2(Width, Height);
    float4 normalData = NormalTexture.SampleLevel(NormalSampler, target_uv(uv), 0.0);
    float2 normal = normalData.rg * 2.0 - 1.0;
    float normalStrength = normalData.a * saturate(length(normal));
    float2 roomPixel = ViewOrigin + (float2(id.xy) + 0.5) * ViewScale;
	if (any(roomPixel < 0.0) || any(roomPixel >= float2(GridWidth, GridHeight) * GridSpacing))
	{
		IndirectLight[id.xy] = 0.0;
		return;
	}
    float2 cellPosition = roomPixel / GridSpacing - 0.5;
	float2 surfacePosition = cellPosition + normal * normalStrength * 1.25;
	float3 irradiance = field_radiance(surfacePosition);
	float left = dot(field_radiance(surfacePosition - float2(1.0, 0.0)), float3(0.2126, 0.7152, 0.0722));
	float right = dot(field_radiance(surfacePosition + float2(1.0, 0.0)), float3(0.2126, 0.7152, 0.0722));
	float up = dot(field_radiance(surfacePosition - float2(0.0, 1.0)), float3(0.2126, 0.7152, 0.0722));
	float down = dot(field_radiance(surfacePosition + float2(0.0, 1.0)), float3(0.2126, 0.7152, 0.0722));
    float2 gradient = float2(right - left, down - up);
    float gradientStrength = saturate(length(gradient) * 4.0);
    float2 lightDirection = length(gradient) > 0.0001 ? normalize(gradient) : normal;
    float directionalWeight = saturate((dot(normal, lightDirection) + NormalWrap) / (1.0 + NormalWrap));
    irradiance *= lerp(1.0, directionalWeight, normalStrength * Directionality * gradientStrength);
    irradiance = pow(max(irradiance, 0.0), ResponseCurve);
    IndirectLight[id.xy] = float4(irradiance, saturate(max(max(irradiance.r, irradiance.g), irradiance.b)));
}
