using System.Numerics;
using System.Runtime.InteropServices;
using Foster.Framework;

namespace Myth;

sealed class GpuLighting : IDisposable
{
	public const int MaxLights = 64;
	const int ShadowSamples = 512;
	const int ShadowRows = MaxLights + 1;

	[StructLayout(LayoutKind.Sequential)]
	readonly struct GpuLight(Vector2 position, float radius, float height, Vector4 colorIntensity)
	{
		public readonly Vector2 Position = position;
		public readonly float Radius = radius;
		public readonly float Height = height;
		public readonly Vector4 ColorIntensity = colorIntensity;
	}

	[StructLayout(LayoutKind.Sequential)]
	readonly struct GpuSegment(Vector4 coordinates, Vector4 properties)
	{
		public readonly Vector4 Coordinates = coordinates;
		public readonly Vector4 Properties = properties;
	}

	[StructLayout(LayoutKind.Sequential)]
	struct DepthParams
	{
		public uint SegmentCount;
		public uint LightCount;
		public uint SunEnabled;
		public uint ShadowSamples;
		public Vector2 SunDirection;
		public Vector2 SunPerpendicular;
		public float SunPerpendicularMin;
		public float SunPerpendicularMax;
		public float SunAlongMin;
		public float SunAlongMax;
		public Vector2 Camera;
		public Vector2 TargetSize;
		public Vector4 ZoomPadding;
	}

	[StructLayout(LayoutKind.Sequential)]
	struct MaskParams
	{
		public uint Width;
		public uint Height;
		public uint LightCount;
		public uint SunEnabled;
		public Vector2 Camera;
		public float Zoom;
		public float ShadowBias;
		public Vector4 Ambient;
		public Vector2 SunDirection;
		public float SunStrength;
		public float SoftShadowSamples;
		public Vector4 SunColor;
		public Vector2 SunPerpendicular;
		public float SunPerpendicularMin;
		public float SunPerpendicularMax;
		public float SunAlongMin;
		public float SunAlongMax;
		public float SunSoftness;
		public float MinimumSoftness;
	}

	readonly GraphicsDevice graphicsDevice;
	readonly StorageBuffer<GpuLight> lightBuffer;
	readonly StorageBuffer<GpuSegment> segmentBuffer;
	readonly ComputeStorageBuffer<float> shadowDepthBuffer;
	readonly UniformBuffer depthUniforms = new();
	readonly UniformBuffer maskUniforms = new();
	readonly GpuLight[] lights = new GpuLight[MaxLights];
	GpuSegment[] segments = new GpuSegment[256];
	Shader? depthShader;
	Shader? maskShader;
	int lightCount;
	int segmentCount;

	public bool Supported => depthShader != null && maskShader != null;

	public GpuLighting(GraphicsDevice graphicsDevice)
	{
		this.graphicsDevice = graphicsDevice;
		lightBuffer = new(graphicsDevice, "LightData");
		segmentBuffer = new(graphicsDevice, "LightOccluderSegments");
		shadowDepthBuffer = new(graphicsDevice, "LightShadowDepth");
		lightBuffer.Upload(lights);
		segmentBuffer.Upload(segments.AsSpan(0, 1));
		shadowDepthBuffer.Upload(new float[ShadowRows * ShadowSamples]);
		LoadShaders();
	}

	public void Begin()
	{
		lightCount = 0;
		segmentCount = 0;
	}

	public void AddLight(Vector2 position, float radius, float intensity, float height, Color color)
	{
		if (lightCount >= lights.Length || radius <= 0 || intensity <= 0)
			return;

		var value = (Vector4)color;
		lights[lightCount++] = new(position, radius, height, new(value.X, value.Y, value.Z, intensity));
	}

	public void AddSegment(Vector2 a, Vector2 b, bool castsSunShadow = true)
	{
		if (a == b)
			return;
		if (segmentCount >= segments.Length)
			Array.Resize(ref segments, segments.Length * 2);
		segments[segmentCount++] = new(new(a.X, a.Y, b.X, b.Y), new(castsSunShadow ? 1 : 0, 0, 0, 0));
	}

	public void AddRect(RectInt rect)
	{
		AddSegment(rect.TopLeft, rect.TopRight);
		AddSegment(rect.TopRight, rect.BottomRight);
		AddSegment(rect.BottomRight, rect.BottomLeft);
		AddSegment(rect.BottomLeft, rect.TopLeft);
	}

	public void Render(
		Target normalBuffer,
		Target spriteShadowMask,
		Target backgroundMask,
		Target foregroundMask,
		Vector2 camera,
		float zoom,
		Color ambientColor,
		float ambientIntensity,
		Vector2 sunDirection,
		Color sunColor,
		float sunStrength,
		bool softenShadows,
		float softness,
		float minimumSoftness)
	{
		if (!Supported)
			return;

		lightBuffer.Upload(lights.AsSpan(0, Math.Max(1, lightCount)));
		segmentBuffer.Upload(segments.AsSpan(0, Math.Max(1, segmentCount)));

		var perpendicular = new Vector2(-sunDirection.Y, sunDirection.X);
		GetSunBounds(camera, new(normalBuffer.Width, normalBuffer.Height), zoom, sunDirection, perpendicular,
			out var perpendicularMin, out var perpendicularMax, out var alongMin, out var alongMax);
		var sunEnabled = sunStrength > 0.001f;

		depthUniforms.Set(new DepthParams
		{
			SegmentCount = (uint)segmentCount,
			LightCount = (uint)lightCount,
			SunEnabled = sunEnabled ? 1u : 0u,
			ShadowSamples = ShadowSamples,
			SunDirection = sunDirection,
			SunPerpendicular = perpendicular,
			SunPerpendicularMin = perpendicularMin,
			SunPerpendicularMax = perpendicularMax,
			SunAlongMin = alongMin,
			SunAlongMax = alongMax,
			Camera = camera,
			TargetSize = new(normalBuffer.Width, normalBuffer.Height),
			ZoomPadding = new(zoom, 0, 0, 0),
		});
		new ComputeCommand(depthShader!)
		{
			Samplers = [new(spriteShadowMask, new(TextureFilter.Nearest, TextureWrap.Clamp, TextureWrap.Clamp))],
			ReadOnlyStorageBuffers = [segmentBuffer, lightBuffer],
			ReadWriteStorageBuffers = [shadowDepthBuffer],
			UniformBuffers = [depthUniforms],
			GroupCountX = ShadowSamples / 64,
			GroupCountY = ShadowRows,
		}.Submit(graphicsDevice);

		var ambient = (Vector4)ambientColor * ambientIntensity;
		maskUniforms.Set(new MaskParams
		{
			Width = (uint)normalBuffer.Width,
			Height = (uint)normalBuffer.Height,
			LightCount = (uint)lightCount,
			SunEnabled = sunEnabled ? 1u : 0u,
			Camera = camera,
			Zoom = zoom,
			ShadowBias = 0.5f,
			Ambient = ambient,
			SunDirection = sunDirection,
			SunStrength = sunStrength,
			SoftShadowSamples = softenShadows ? softness : 0,
			SunColor = (Vector4)sunColor,
			SunPerpendicular = perpendicular,
			SunPerpendicularMin = perpendicularMin,
			SunPerpendicularMax = perpendicularMax,
			SunAlongMin = alongMin,
			SunAlongMax = alongMax,
			SunSoftness = softenShadows ? softness : 0,
			MinimumSoftness = minimumSoftness,
		});
		new ComputeCommand(maskShader!)
		{
			Samplers = [new(normalBuffer, new(TextureFilter.Nearest, TextureWrap.Clamp, TextureWrap.Clamp))],
			ReadOnlyStorageBuffers = [lightBuffer, shadowDepthBuffer],
			ReadWriteStorageTextures = [backgroundMask.Attachments[0], foregroundMask.Attachments[0]],
			UniformBuffers = [maskUniforms],
			GroupCountX = (normalBuffer.Width + 7) / 8,
			GroupCountY = (normalBuffer.Height + 7) / 8,
		}.Submit(graphicsDevice);
	}

	void GetSunBounds(
		Vector2 camera,
		Point2 targetSize,
		float zoom,
		Vector2 direction,
		Vector2 perpendicular,
		out float perpendicularMin,
		out float perpendicularMax,
		out float alongMin,
		out float alongMax)
	{
		var viewSize = (Vector2)targetSize / zoom;
		var topLeft = camera;
		var bottomRight = camera + viewSize;
		var perpendicularMinimum = float.MaxValue;
		var perpendicularMaximum = float.MinValue;
		var alongMinimum = float.MaxValue;
		var alongMaximum = float.MinValue;
		Include(topLeft);
		Include(new(bottomRight.X, topLeft.Y));
		Include(bottomRight);
		Include(new(topLeft.X, bottomRight.Y));
		for (var i = 0; i < segmentCount; i++)
		{
			var segment = segments[i];
			if (segment.Properties.X <= 0)
				continue;
			Include(new(segment.Coordinates.X, segment.Coordinates.Y));
			Include(new(segment.Coordinates.Z, segment.Coordinates.W));
		}

		var padding = 2.0f;
		perpendicularMin = perpendicularMinimum - padding;
		perpendicularMax = perpendicularMaximum + padding;
		alongMin = alongMinimum - padding;
		alongMax = alongMaximum + padding;

		void Include(Vector2 point)
		{
			var across = Vector2.Dot(point, perpendicular);
			var along = Vector2.Dot(point, direction);
			perpendicularMinimum = MathF.Min(perpendicularMinimum, across);
			perpendicularMaximum = MathF.Max(perpendicularMaximum, across);
			alongMinimum = MathF.Min(alongMinimum, along);
			alongMaximum = MathF.Max(alongMaximum, along);
		}
	}

	void LoadShaders()
	{
		try
		{
			var ext = graphicsDevice.Driver.GetShaderExtension();
			var depthCode = Assets.TryReadCompiledShader($"LightShadowDepth.compute.{ext}");
			var maskCode = Assets.TryReadCompiledShader($"LightMasks.compute.{ext}");
			if (depthCode == null || maskCode == null)
				return;

			depthShader = new(
				graphicsDevice, ShaderStage.Compute, depthCode,
				samplerCount: 1,
				uniformBufferCount: 1,
				readOnlyStorageBufferCount: 2,
				readWriteStorageBufferCount: 1,
				threadCountX: 64, threadCountY: 1, threadCountZ: 1,
				entryPoint: "compute_main", name: "LightShadowDepth");
			maskShader = new(
				graphicsDevice, ShaderStage.Compute, maskCode,
				samplerCount: 1,
				uniformBufferCount: 1,
				readOnlyStorageBufferCount: 2,
				readWriteStorageTextureCount: 2,
				threadCountX: 8, threadCountY: 8, threadCountZ: 1,
				entryPoint: "compute_main", name: "LightMasks");
		}
		catch (Exception e)
		{
			Console.Error.WriteLine($"GPU lighting init failed: {e}");
			depthShader?.Dispose();
			maskShader?.Dispose();
			depthShader = null;
			maskShader = null;
		}
	}

#if DEBUG
	public void ReloadShaders()
	{
		depthShader?.Dispose();
		maskShader?.Dispose();
		depthShader = null;
		maskShader = null;
		LoadShaders();
	}
#endif

	public void Dispose()
	{
		depthShader?.Dispose();
		maskShader?.Dispose();
		lightBuffer.Dispose();
		segmentBuffer.Dispose();
		shadowDepthBuffer.Dispose();
	}
}
