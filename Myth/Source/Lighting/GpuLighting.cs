using System.Numerics;
using System.Runtime.InteropServices;
using Foster.Framework;

namespace Myth;

sealed class GpuLighting : IDisposable
{
	public const int MaxLights = 64;
	const int MaxSporeFogVolumes = 64;
	const int ShadowSamples = 512;
	const int ShadowRows = MaxLights + 1;

	[StructLayout(LayoutKind.Sequential)]
	internal readonly struct GpuLight(Vector2 position, float radius, float height, Vector4 colorIntensity, Vector4 cookie, Vector4 cookieShape, Vector4 shadow)
	{
		public readonly Vector2 Position = position;
		public readonly float Radius = radius;
		public readonly float Height = height;
		public readonly Vector4 ColorIntensity = colorIntensity;
		public readonly Vector4 Cookie = cookie;
		public readonly Vector4 CookieShape = cookieShape;
		public readonly Vector4 Shadow = shadow;
	}

	[StructLayout(LayoutKind.Sequential)]
	readonly struct GpuSegment(Vector4 coordinates, Vector4 properties)
	{
		public readonly Vector4 Coordinates = coordinates;
		public readonly Vector4 Properties = properties;
	}

	[StructLayout(LayoutKind.Sequential)]
	readonly struct GpuSporeFogVolume(RectInt rect, float strength, float feather)
	{
		public readonly Vector4 Rect = new(rect.X, rect.Y, rect.Width, rect.Height);
		public readonly Vector4 Settings = new(strength, feather, 0, 0);
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
		public float NormalLightFloor;
		public float SpriteShadowStrength;
		public float SpriteShadowMinLength;
		public float SpriteShadowMaxLength;
		public float NormalLightGain;
		public Vector4 SporeFogTint;
		public Vector2 SporeFogDrift;
		public float Time;
		public float SporeFogStrength;
		public float SporeFogScale;
		public float SporeFogContrast;
		public float SporeFogPointStrength;
		public float SporeFogSunStrength;
		public Vector4 SporeFogVolumeData;
	}

	readonly GraphicsDevice graphicsDevice;
	readonly StorageBuffer<GpuLight> lightBuffer;
	readonly StorageBuffer<GpuSegment> segmentBuffer;
	readonly StorageBuffer<GpuSporeFogVolume> sporeFogVolumeBuffer;
	readonly ComputeStorageBuffer<Vector2> shadowDepthBuffer;
	readonly UniformBuffer depthUniforms = new();
	readonly UniformBuffer maskUniforms = new();
	readonly GpuLight[] lights = new GpuLight[MaxLights];
	readonly float[] flickers = new float[MaxLights]; // each light's breathing, scaled into its radius and intensity
	readonly GpuSporeFogVolume[] sporeFogVolumes = new GpuSporeFogVolume[MaxSporeFogVolumes];
	GpuSegment[] segments = new GpuSegment[256];
	Shader? depthShader;
	Shader? maskShader;
	int lightCount;
	int segmentCount;
	int sporeFogVolumeCount;

	public bool Supported => depthShader != null && maskShader != null;

	public GpuLighting(GraphicsDevice graphicsDevice)
	{
		this.graphicsDevice = graphicsDevice;
		lightBuffer = new(graphicsDevice, "LightData");
		segmentBuffer = new(graphicsDevice, "LightOccluderSegments");
		sporeFogVolumeBuffer = new(graphicsDevice, "SporeFogVolumes");
		shadowDepthBuffer = new(graphicsDevice, "LightShadowDepth");
		lightBuffer.Upload(lights);
		segmentBuffer.Upload(segments.AsSpan(0, 1));
		sporeFogVolumeBuffer.Upload(sporeFogVolumes.AsSpan(0, 1));
		shadowDepthBuffer.Upload(new Vector2[ShadowRows * ShadowSamples]);
		LoadShaders();
	}

	// World rect the masks cover. Lights that can't reach it are dropped in AddLight.
	Rect visibleWorld;

	// The lights gathered for the frame being lit; actors drawing before the next Begin see last frame's.
	internal ReadOnlySpan<GpuLight> Lights => lights.AsSpan(0, lightCount);
	internal ReadOnlySpan<float> Flickers => flickers.AsSpan(0, lightCount);

	public void Begin(in Rect visibleWorld)
	{
		this.visibleWorld = visibleWorld;
		lightCount = 0;
		segmentCount = 0;
		sporeFogVolumeCount = 0;
	}

	public void AddSporeFogVolume(RectInt rect, float strength, float feather)
	{
		if (sporeFogVolumeCount >= sporeFogVolumes.Length || rect.Width <= 0 || rect.Height <= 0 || strength <= 0)
			return;
		sporeFogVolumes[sporeFogVolumeCount++] = new(rect, Math.Clamp(strength, 0, 4), MathF.Max(0, feather));
	}

	public bool AddLight(Vector2 position, float radius, float intensity, float height, Color color)
		=> AddLight(position, radius, intensity, height, color, LightCookie.None, 0, 0, 1, 1, 0.12f, 0, Light.DefaultShadowSourceRadius, 1, 0, 0);

	public bool AddLight(
		Vector2 position,
		float radius,
		float intensity,
		float height,
		Color color,
		LightCookie cookie,
		float cookieAngleDegrees,
		float cookieSpeedDegrees,
		float cookieScale,
		float cookieStrength,
		float cookieFeather,
		float cookieCutoff,
		float shadowSourceRadius,
		float shadowSoftness,
		float shadowNoise,
		float shadowNoiseSpeed,
		float flicker = 1)
	{
		if (lightCount >= lights.Length || radius <= 0 || intensity <= 0)
			return false;

		// Exact, not an approximation: LightMasks.hlsl attenuates by saturate(1 - distance/radius),
		// which is zero at and beyond the radius, and the cookie only ever multiplies that down.
		// So a light whose circle misses the view contributes nothing to any pixel. Culling here
		// rather than at the call sites covers both gather paths (Light actors and ForEachLight)
		// and leaves their own bookkeeping alone, and it makes the MaxLights cap mean "64 lights
		// you can see" instead of "the first 64 actors".
		if ((position - Vector2.Clamp(position, visibleWorld.TopLeft, visibleWorld.BottomRight)).LengthSquared() >= radius * radius)
			return false;

		var value = (Vector4)color;
		flickers[lightCount] = flicker;
		lights[lightCount++] = new(
			position,
			radius,
			height,
			new(value.X, value.Y, value.Z, intensity),
			new((float)cookie, cookieAngleDegrees * MathF.PI / 180, cookieSpeedDegrees * MathF.PI / 180, cookieScale),
			new(cookieStrength, cookieFeather, cookieCutoff, 0),
			new(shadowSourceRadius, shadowSoftness, shadowNoise, shadowNoiseSpeed));
		return true;
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
		Texture? sporeNoise,
		Target backgroundMask,
		Target foregroundMask,
		Target sporeFogMask,
		Vector2 camera,
		float zoom,
		float time,
		float sporeFogStrength,
		Color sporeFogColor,
		Color ambientColor,
		float ambientIntensity,
		Vector2 sunDirection,
		Color sunColor,
		float sunStrength,
		bool softenShadows,
		VisualTuning visuals)
	{
		if (!Supported)
			return;

		lightBuffer.Upload(lights.AsSpan(0, Math.Max(1, lightCount)));
		segmentBuffer.Upload(segments.AsSpan(0, Math.Max(1, segmentCount)));
		sporeFogVolumeBuffer.Upload(sporeFogVolumes.AsSpan(0, Math.Max(1, sporeFogVolumeCount)));

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
			ShadowBias = visuals.ShadowBias,
			Ambient = ambient,
			SunDirection = sunDirection,
			SunStrength = sunStrength,
			SoftShadowSamples = softenShadows ? 1 : 0,
			SunColor = (Vector4)sunColor,
			SunPerpendicular = perpendicular,
			SunPerpendicularMin = perpendicularMin,
			SunPerpendicularMax = perpendicularMax,
			SunAlongMin = alongMin,
			SunAlongMax = alongMax,
			SunSoftness = softenShadows ? visuals.SunShadowSoftness : 0,
			NormalLightFloor = visuals.NormalLightFloor,
			SpriteShadowStrength = visuals.SpriteShadowStrength,
			SpriteShadowMinLength = visuals.SpriteShadowMinLength,
			SpriteShadowMaxLength = visuals.SpriteShadowMaxLength,
			NormalLightGain = visuals.NormalLightGain,
			SporeFogTint = (Vector4)sporeFogColor,
			SporeFogDrift = visuals.SporeFogDrift,
			Time = time,
			SporeFogStrength = sporeNoise != null ? sporeFogStrength : 0,
			SporeFogScale = visuals.SporeFogScale,
			SporeFogContrast = visuals.SporeFogContrast,
			SporeFogPointStrength = visuals.SporeFogPointStrength,
			SporeFogSunStrength = visuals.SporeFogSunStrength,
			SporeFogVolumeData = new(sporeNoise != null ? sporeFogVolumeCount : 0, 0, 0, 0),
		});
		new ComputeCommand(maskShader!)
		{
			Samplers =
			[
				new(normalBuffer, new(TextureFilter.Nearest, TextureWrap.Clamp, TextureWrap.Clamp)),
				new(sporeNoise ?? normalBuffer.Attachments[0], new(TextureFilter.Linear, TextureWrap.Repeat, TextureWrap.Repeat)),
			],
			ReadOnlyStorageBuffers = [lightBuffer, shadowDepthBuffer, sporeFogVolumeBuffer],
			ReadWriteStorageTextures =
			[
				backgroundMask.Attachments[0],
				foregroundMask.Attachments[0],
				sporeFogMask.Attachments[0],
			],
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
		const float PerpendicularPaddingPixels = 8;
		const float AlongPadding = 2;

		var viewSize = (Vector2)targetSize / zoom;
		var topLeft = camera;
		var bottomRight = camera + viewSize;
		var perpendicularMinimum = float.MaxValue;
		var perpendicularMaximum = float.MinValue;
		var alongMinimum = float.MaxValue;
		var alongMaximum = float.MinValue;
		IncludeViewPoint(topLeft);
		IncludeViewPoint(new(bottomRight.X, topLeft.Y));
		IncludeViewPoint(bottomRight);
		IncludeViewPoint(new(topLeft.X, bottomRight.Y));
		for (var i = 0; i < segmentCount; i++)
		{
			var segment = segments[i];
			if (segment.Properties.X <= 0)
				continue;
			IncludeAlong(new(segment.Coordinates.X, segment.Coordinates.Y));
			IncludeAlong(new(segment.Coordinates.Z, segment.Coordinates.W));
		}

		// Keep the lateral ray grid at viewport resolution and anchored to world-space cells.
		// Including moving occluders in these bounds slides every sun shadow when any one moves.
		var perpendicularPadding = PerpendicularPaddingPixels / zoom;
		var sampleSpacing = (perpendicularMaximum - perpendicularMinimum + perpendicularPadding * 2) / ShadowSamples;
		perpendicularMin = MathF.Floor((perpendicularMinimum - perpendicularPadding) / sampleSpacing) * sampleSpacing;
		perpendicularMax = perpendicularMin + sampleSpacing * ShadowSamples;
		alongMin = alongMinimum - AlongPadding;
		alongMax = alongMaximum + AlongPadding;

		void IncludeViewPoint(Vector2 point)
		{
			var across = Vector2.Dot(point, perpendicular);
			perpendicularMinimum = MathF.Min(perpendicularMinimum, across);
			perpendicularMaximum = MathF.Max(perpendicularMaximum, across);
			IncludeAlong(point);
		}

		void IncludeAlong(Vector2 point)
		{
			var along = Vector2.Dot(point, direction);
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
				samplerCount: 2,
				uniformBufferCount: 1,
				readOnlyStorageBufferCount: 3,
				readWriteStorageTextureCount: 3,
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
		sporeFogVolumeBuffer.Dispose();
		shadowDepthBuffer.Dispose();
	}
}
