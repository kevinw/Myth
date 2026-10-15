// Dilates a ray-marched SculptedHead or SculptedBody (its normal texture, alpha = hit) by one pixel: the mask
// (SV_Target0) that outlines and silhouettes hug, and the final normals (SV_Target1), where the dilated rim faces
// outward: away from the center (heads) or away from the covered neighbors (bodies).

cbuffer VertexUniformBlock : register(b0, space1)
{
    float4x4 Matrix;
};

cbuffer FragmentUniformBlock : register(b0, space3)
{
    float4 Frame; // xy = size (px), z = rim faces away from neighbors
};

Texture2D Texture : register(t0, space2);
SamplerState Sampler : register(s0, space2);

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

struct MaskOutput
{
    float4 Mask : SV_Target0;
    float4 Normal : SV_Target1;
};

MaskOutput fragment_main(VsOutput input)
{
    MaskOutput output;
    output.Mask = float4(0.0, 0.0, 0.0, 0.0);
    output.Normal = float4(0.0, 0.0, 0.0, 0.0);
    if (input.type.x < -1000.0)
        return output;

    float2 size = Frame.xy;
    float2 pixel = input.position.xy;
    float4 center = Texture.SampleLevel(Sampler, pixel / size, 0.0);
    bool covered = center.a > 0.5;
    float2 away = float2(0.0, 0.0);
    for (int dy = -1; dy <= 1; dy++)
        for (int dx = -1; dx <= 1; dx++)
        {
            float2 neighbor = pixel + float2(dx, dy);
            if (all(neighbor >= 0.0) && all(neighbor < size) && Texture.SampleLevel(Sampler, neighbor / size, 0.0).a > 0.5)
            {
                covered = true;
                away -= float2(dx, dy);
            }
        }
    if (!covered)
        return output;

    output.Mask = float4(1.0, 1.0, 1.0, 1.0);
    if (center.a > 0.5)
        output.Normal = center;
    else
    {
        float2 outward = Frame.z > 0.5 ? away : pixel - size * 0.5;
        outward = dot(outward, outward) > 0.0 ? normalize(outward) : float2(0.0, 1.0);
        output.Normal = float4(outward * 0.5 + 0.5, 0.5, 1.0);
    }
    return output;
}
