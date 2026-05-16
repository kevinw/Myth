cbuffer VertexUniformBlock : register(b0, space1)
{
    float4x4 Matrix;
};

cbuffer FragmentUniformBlock : register(b0, space3)
{
    float4 TextureSizeCamera;
    float4 GlitchSettings;
    float4 TimePadding;
};

Texture2D Texture : register(t0, space2);
SamplerState Sampler : register(s0, space2);

Texture2D TileFieldTexture : register(t1, space2);
SamplerState TileFieldSampler : register(s1, space2);

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

float hash_float(float2 position)
{
    float2 p = floor(position);
    return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
}

float luminance(float3 color)
{
    return dot(color, float3(0.2126, 0.7152, 0.0722));
}

float2 family_direction(float family, float variant)
{
    if (family < 0.125)
        return normalize(float2(1.0, -1.0));
    if (family < 0.375)
        return float2(0.0, 1.0);
    if (family < 0.625)
        return normalize(float2(1.0, 1.0));
    if (family < 0.875)
        return float2(1.0, 0.0);

    return normalize(variant < 0.5 ? float2(-1.0, 1.0) : float2(1.0, -1.0));
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

float4 fragment_main(VsOutput input) : SV_Target0
{
    float2 texture_size = TextureSizeCamera.xy;
    float2 camera = TextureSizeCamera.zw;
    float amount = saturate(GlitchSettings.x);
    float2 origin = GlitchSettings.yz;
    float max_radius = GlitchSettings.w;
    float time = TimePadding.x;
    float2 hit_axis = normalize(TimePadding.yz);

    float4 original = Texture.Sample(Sampler, input.tex_coord) * input.color;
    if (amount <= 0.001)
        return original;

    float4 tile_field = TileFieldTexture.Sample(TileFieldSampler, input.tex_coord);
    float family = tile_field.g;
    float variant = tile_field.r;
    float2 screen_pixel = input.tex_coord * texture_size;
    float2 world_pixel = camera + screen_pixel;
    float2 tile = floor(world_pixel / 8.0);
    float frame = floor(time * 7.0);
    float dirty = hash_float(tile + frame + variant * 31.0);
    float block = hash_float(floor(world_pixel / 16.0) + frame * 3.0 + family * 19.0);
    float edge_noise = hash_float(floor(world_pixel / 28.0) + frame * 1.7 + family * 47.0);
    float fine_edge_noise = hash_float(tile + frame * 0.5 + family * 71.0);
    float active_radius = lerp(42.0, max_radius * 1.75, smoothstep(0.0, 0.75, amount));
    float long_radius = active_radius * lerp(0.70, 1.45, edge_noise) + (fine_edge_noise - 0.5) * 30.0;
    float short_radius = max(long_radius * lerp(0.28, 0.46, block), 10.0);
    float ellipse = ellipse_distance(world_pixel, origin, hit_axis, long_radius, short_radius);
    float radial = saturate(1.0 - ellipse);
    float proximity = pow(smoothstep(0.0, 0.95, radial), 0.58);
    proximity *= lerp(0.42, 1.0, hash_float(floor(world_pixel / 20.0) + frame * 2.0));
    float global = amount * smoothstep(0.22, 1.0, amount) * 0.30;
    float local = saturate(global + amount * proximity * lerp(0.28, 0.90, dirty) * lerp(0.45, 1.0, block));

    if (local <= 0.002)
        return original;

    float2 direction = family_direction(family, variant);
    float band = hash_float(float2(floor(world_pixel.y / 3.0), frame + floor(world_pixel.x / 40.0)));
    float span = floor(lerp(2.0, 18.0, saturate(local * lerp(0.75, 1.35, band))));
    float seek_high = step(0.5, hash_float(tile + family * 43.0 + frame * 0.25));
    float best_luma = luminance(original.rgb);
    float3 best_color = original.rgb;

    [unroll]
    for (int i = -18; i <= 18; i++)
    {
        float fi = float(i);
        if (abs(fi) > span || i == 0)
            continue;

        float jitter = (hash_float(tile + fi * 17.0 + frame) - 0.5) * local * 3.0;
        float2 uv = input.tex_coord + direction * (fi + jitter) / texture_size;
        float3 sample_color = Texture.Sample(Sampler, uv).rgb;
        float sample_luma = luminance(sample_color);

        bool take_high = seek_high > 0.5 && sample_luma > best_luma;
        bool take_low = seek_high <= 0.5 && sample_luma < best_luma;
        if (take_high || take_low)
        {
            best_luma = sample_luma;
            best_color = sample_color;
        }
    }

    float blend = saturate(local * 1.35);
    float scan = hash_float(float2(floor(world_pixel.y / 2.0), frame + family * 23.0));
    blend *= lerp(0.78, 1.0, scan);

    float2 tear_direction = direction.yx * float2(-1.0, 1.0);
    float tear_amount = (hash_float(floor(world_pixel / 24.0) + frame * 5.0) - 0.5) * local * 7.0;
    float3 torn_color = Texture.Sample(Sampler, input.tex_coord + tear_direction * tear_amount / texture_size).rgb;
    float3 sorted_color = lerp(best_color, torn_color, saturate(local * 0.38));
    return float4(lerp(original.rgb, sorted_color, blend), original.a);
}
