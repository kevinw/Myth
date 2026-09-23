using System.Numerics;
using System.Runtime.InteropServices;
using Foster.Framework;

namespace Myth;

sealed class GpuGlobalIllumination : IDisposable
{
	const int GridSpacing = 2;

	[StructLayout(LayoutKind.Sequential)]
	struct InjectParams
	{
		public uint Width;
		public uint Height;
		public uint GridWidth;
		public uint GridHeight;
		public float GridSpacing;
		public float BounceGain;
		public float BounceSaturation;
		public float ForegroundReflectivity;
		public float AlbedoFloor;
		public float Warmth;
		public float ColorBleed;
		public float Padding;
		public Vector4 Ambient;
		public Vector4 MaterialResponse;
		public Vector4 RoomBounds;
		public Vector4 NextRoomBounds;
	}

	[StructLayout(LayoutKind.Sequential)]
	struct PropagateParams
	{
		public uint GridWidth;
		public uint GridHeight;
		public float CardinalDecay;
		public float DiagonalDecay;
	}

	[StructLayout(LayoutKind.Sequential)]
	struct WrapParams
	{
		public uint GridWidth, GridHeight;
		public float GridSpacing, Depth;
	}

	[StructLayout(LayoutKind.Sequential)]
	struct ResolveParams
	{
		public uint Width;
		public uint Height;
		public uint GridWidth;
		public uint GridHeight;
		public float GridSpacing;
		public float Directionality;
		public float NormalWrap;
		public float ResponseCurve;
		public Vector2 ViewOrigin;
		public Vector2 ViewScale;
		public Vector4 ShadingSettings;
	}

	static readonly BlendMode CopyBlend = new(BlendOp.Add, BlendFactor.One, BlendFactor.Zero);

	readonly GraphicsDevice graphicsDevice;
	readonly ComputeStorageBuffer<Vector4> emission;
	readonly ComputeStorageBuffer<Vector4> surface;
	readonly ComputeStorageBuffer<Vector4> fieldA;
	readonly ComputeStorageBuffer<Vector4> fieldB;
	readonly UniformBuffer injectUniforms = new();
	readonly UniformBuffer propagateUniforms = new();
	readonly UniformBuffer wrapUniforms = new();
	readonly UniformBuffer resolveUniforms = new();
	Shader? injectShader;
	Shader? propagateShader;
	Shader? resolveShader;
	Shader? wrapShader;
	int fieldCapacity;
	Room? atlasRoom;
	Room? atlasNextRoom;
	RectInt atlasBounds;
	Target? atlasBackgroundScene;
	Target? atlasForegroundScene;
	Target? atlasBackgroundLight;
	Target? atlasForegroundLight;
	Target? atlasBackgroundLightEmission;
	Target? atlasForegroundLightEmission;
	Target? atlasTileField;
	Target? atlasBackgroundCoverage;
	Target? atlasNormal;
	Target? atlasSpriteShadow;
	Target? atlasSporeFog;

	public bool Supported => injectShader != null && propagateShader != null && resolveShader != null && wrapShader != null;
	public Target AtlasBackgroundScene => atlasBackgroundScene!;
	public Target AtlasForegroundScene => atlasForegroundScene!;
	public Target AtlasBackgroundLight => atlasBackgroundLight!;
	public Target AtlasForegroundLight => atlasForegroundLight!;
	public Target AtlasBackgroundLightEmission => atlasBackgroundLightEmission!;
	public Target AtlasForegroundLightEmission => atlasForegroundLightEmission!;
	public Target AtlasTileField => atlasTileField!;
	public Target AtlasBackgroundCoverage => atlasBackgroundCoverage!;
	public Target AtlasNormal => atlasNormal!;
	public Target AtlasSpriteShadow => atlasSpriteShadow!;
	public Target AtlasSporeFog => atlasSporeFog!;
	public RectInt AtlasBounds => atlasBounds;

	public GpuGlobalIllumination(GraphicsDevice graphicsDevice)
	{
		this.graphicsDevice = graphicsDevice;
		emission = new(graphicsDevice, "IndirectEmission");
		surface = new(graphicsDevice, "IndirectSurface");
		fieldA = new(graphicsDevice, "IndirectFieldA");
		fieldB = new(graphicsDevice, "IndirectFieldB");
		LoadShaders();
	}

	public void Render(
		Batcher batcher,
		RectInt bounds,
		Target backgroundScene,
		Target foregroundScene,
		Target backgroundLight,
		Target foregroundLight,
		Target backgroundLightEmission,
		Target foregroundLightEmission,
		Target tileField,
		Target backgroundCoverage,
		Target normalBuffer,
		Target output,
		Target wrapOutput,
		Vector2 camera,
		float zoom,
		Color ambientColor,
		float ambientIntensity,
		VisualTuning visuals)
	{
		if (!Supported)
		{
			output.Clear(Color.Transparent);
			wrapOutput.Clear(Color.Transparent);
			return;
		}

		StampView(batcher, bounds, camera, zoom,
			backgroundScene, foregroundScene, backgroundLight, foregroundLight,
			backgroundLightEmission, foregroundLightEmission, tileField, backgroundCoverage);
		backgroundScene = atlasBackgroundScene!;
		foregroundScene = atlasForegroundScene!;
		backgroundLight = atlasBackgroundLight!;
		foregroundLight = atlasForegroundLight!;
		backgroundLightEmission = atlasBackgroundLightEmission!;
		foregroundLightEmission = atlasForegroundLightEmission!;
		tileField = atlasTileField!;
		backgroundCoverage = atlasBackgroundCoverage!;

		var gridWidth = (backgroundScene.Width + GridSpacing - 1) / GridSpacing;
		var gridHeight = (backgroundScene.Height + GridSpacing - 1) / GridSpacing;
		var cellCount = gridWidth * gridHeight;
		EnsureCapacity(cellCount);
		var roomRect = atlasRoom!.WorldBounds - bounds.TopLeft;
		var nextRect = atlasNextRoom != null ? atlasNextRoom.WorldBounds - bounds.TopLeft : default;
		injectUniforms.Set(new InjectParams
		{
			Width = (uint)backgroundScene.Width,
			Height = (uint)backgroundScene.Height,
			GridWidth = (uint)gridWidth,
			GridHeight = (uint)gridHeight,
			GridSpacing = GridSpacing,
			BounceGain = visuals.IndirectBounceGain,
			BounceSaturation = visuals.IndirectBounceSaturation,
			ForegroundReflectivity = visuals.IndirectForegroundReflectivity,
			AlbedoFloor = visuals.IndirectBounceAlbedoFloor,
			Warmth = visuals.IndirectBounceWarmth,
			ColorBleed = visuals.IndirectColorBleed,
			Ambient = (Vector4)ambientColor * ambientIntensity,
			RoomBounds = new(roomRect.X, roomRect.Y, roomRect.Width, roomRect.Height),
			NextRoomBounds = new(nextRect.X, nextRect.Y, nextRect.Width, nextRect.Height),
			MaterialResponse = new(
				visuals.IndirectStoneReflectivity,
				visuals.IndirectOrganicReflectivity,
				visuals.IndirectWaterReflectivity,
				visuals.IndirectOtherReflectivity),
		});
		new ComputeCommand(injectShader!)
		{
			Samplers =
			[
				new(backgroundScene, new(TextureFilter.Nearest, TextureWrap.Clamp, TextureWrap.Clamp)),
				new(foregroundScene, new(TextureFilter.Nearest, TextureWrap.Clamp, TextureWrap.Clamp)),
				new(backgroundLight, new(TextureFilter.Linear, TextureWrap.Clamp, TextureWrap.Clamp)),
				new(foregroundLight, new(TextureFilter.Linear, TextureWrap.Clamp, TextureWrap.Clamp)),
				new(backgroundLightEmission, new(TextureFilter.Nearest, TextureWrap.Clamp, TextureWrap.Clamp)),
				new(foregroundLightEmission, new(TextureFilter.Nearest, TextureWrap.Clamp, TextureWrap.Clamp)),
				new(tileField, new(TextureFilter.Nearest, TextureWrap.Clamp, TextureWrap.Clamp)),
				new(backgroundCoverage, new(TextureFilter.Nearest, TextureWrap.Clamp, TextureWrap.Clamp)),
			],
			ReadWriteStorageBuffers = [emission, surface, fieldA, fieldB],
			UniformBuffers = [injectUniforms],
			GroupCountX = (cellCount + 63) / 64,
		}.Submit(graphicsDevice);

		var cardinalDecay = MathF.Pow(2, -GridSpacing * visuals.IndirectLightFalloff);
		propagateUniforms.Set(new PropagateParams
		{
			GridWidth = (uint)gridWidth,
			GridHeight = (uint)gridHeight,
			CardinalDecay = cardinalDecay,
			DiagonalDecay = MathF.Pow(cardinalDecay, MathF.Sqrt(2)),
		});
		var previous = fieldA;
		var current = fieldB;
		var propagationSteps = Math.Max((int)MathF.Ceiling(visuals.IndirectLightRange / GridSpacing), 1);
		for (var step = 0; step < propagationSteps; step++)
		{
			new ComputeCommand(propagateShader!)
			{
				ReadOnlyStorageBuffers = [emission, surface, previous],
				ReadWriteStorageBuffers = [current],
				UniformBuffers = [propagateUniforms],
				GroupCountX = (cellCount + 63) / 64,
			}.Submit(graphicsDevice);
			(previous, current) = (current, previous);
		}

		wrapUniforms.Set(new WrapParams
		{
			GridWidth = (uint)gridWidth,
			GridHeight = (uint)gridHeight,
			GridSpacing = GridSpacing,
			Depth = visuals.IndirectForegroundWrapStrength > 0 ? visuals.IndirectForegroundWrapDepth : 0,
		});
		// Reuse the idle propagation buffer; the original GI field stays untouched.
		new ComputeCommand(wrapShader!)
		{
			ReadOnlyStorageBuffers = [previous, surface, emission],
			ReadWriteStorageBuffers = [current],
			UniformBuffers = [wrapUniforms],
			GroupCountX = (cellCount + 63) / 64,
		}.Submit(graphicsDevice);

		resolveUniforms.Set(new ResolveParams
		{
			Width = (uint)output.Width,
			Height = (uint)output.Height,
			GridWidth = (uint)gridWidth,
			GridHeight = (uint)gridHeight,
			GridSpacing = GridSpacing,
			Directionality = visuals.IndirectDirectionality,
			NormalWrap = visuals.IndirectNormalWrap,
			ResponseCurve = visuals.IndirectResponseCurve,
			ViewOrigin = camera - (Vector2)bounds.TopLeft,
			ViewScale = Vector2.One / zoom,
			ShadingSettings = new(visuals.IndirectForegroundWrapStrength, visuals.IndirectHighlightColorRetention, 0, 0),
		});
		new ComputeCommand(resolveShader!)
		{
			Samplers =
			[
				new(normalBuffer, new(TextureFilter.Nearest, TextureWrap.Clamp, TextureWrap.Clamp)),
			],
			ReadOnlyStorageBuffers = [previous, current],
			ReadWriteStorageTextures = [output.Attachments[0], wrapOutput.Attachments[0]],
			UniformBuffers = [resolveUniforms],
			GroupCountX = (output.Width + 7) / 8,
			GroupCountY = (output.Height + 7) / 8,
		}.Submit(graphicsDevice);
	}

	public bool EnsureRooms(Room room, Room? nextRoom)
	{
		if (ReferenceEquals(atlasRoom, room) && ReferenceEquals(atlasNextRoom, nextRoom))
			return false;

		DisposeAtlas();
		atlasRoom = room;
		atlasNextRoom = nextRoom;
		atlasBounds = nextRoom == null ? room.WorldBounds : room.WorldBounds.Conflate(nextRoom.WorldBounds);
		var bounds = atlasBounds;
		atlasBackgroundScene = NewAtlas(bounds, "BackgroundScene");
		atlasForegroundScene = NewAtlas(bounds, "ForegroundScene");
		atlasBackgroundLight = NewComputeAtlas(bounds, "BackgroundLight", TextureFormat.R16G16B16A16Float);
		atlasForegroundLight = NewComputeAtlas(bounds, "ForegroundLight", TextureFormat.R16G16B16A16Float);
		atlasBackgroundLightEmission = NewAtlas(bounds, "BackgroundLightEmission", TextureFormat.R16G16B16A16Float);
		atlasForegroundLightEmission = NewAtlas(bounds, "ForegroundLightEmission", TextureFormat.R16G16B16A16Float);
		atlasTileField = NewAtlas(bounds, "TileField");
		atlasBackgroundCoverage = NewAtlas(bounds, "BackgroundCoverage");
		atlasNormal = NewAtlas(bounds, "Normal");
		atlasSpriteShadow = NewAtlas(bounds, "SpriteShadow");
		atlasSporeFog = NewComputeAtlas(bounds, "SporeFog");
		return true;
	}

	public void InvalidateRoom(Room room)
	{
		if (ReferenceEquals(atlasRoom, room) || ReferenceEquals(atlasNextRoom, room))
			DisposeAtlas();
	}

	Target NewAtlas(RectInt bounds, string name, TextureFormat format = TextureFormat.Color)
	{
		ReadOnlySpan<TextureFormat> attachments = [format];
		var target = new Target(graphicsDevice, bounds.Width, bounds.Height, attachments, name: $"IndirectAtlas/{name}");
		target.Clear(Color.Transparent);
		return target;
	}

	Target NewComputeAtlas(RectInt bounds, string name, TextureFormat format = TextureFormat.Color)
	{
		ReadOnlySpan<TextureFormat> attachments = [format];
		var target = new Target(graphicsDevice, bounds.Width, bounds.Height, attachments, TextureFlags.ComputeWrite, $"IndirectAtlas/{name}");
		target.Clear(Color.Transparent);
		return target;
	}

	void StampView(
		Batcher batcher,
		RectInt bounds,
		Vector2 camera,
		float zoom,
		Target backgroundScene,
		Target foregroundScene,
		Target backgroundLight,
		Target foregroundLight,
		Target backgroundLightEmission,
		Target foregroundLightEmission,
		Target tileField,
		Target backgroundCoverage)
	{
		var previousMatrix = batcher.Matrix;
		var position = camera - (Vector2)bounds.TopLeft;
		var scale = Vector2.One / zoom;
		batcher.Matrix = Matrix3x2.Identity;
		Stamp(atlasBackgroundScene!, backgroundScene);
		Stamp(atlasForegroundScene!, foregroundScene);
		Stamp(atlasBackgroundLight!, backgroundLight);
		Stamp(atlasForegroundLight!, foregroundLight);
		Stamp(atlasBackgroundLightEmission!, backgroundLightEmission);
		Stamp(atlasForegroundLightEmission!, foregroundLightEmission);
		Stamp(atlasTileField!, tileField);
		Stamp(atlasBackgroundCoverage!, backgroundCoverage);
		batcher.Matrix = previousMatrix;

		void Stamp(Target destination, Target source)
		{
			batcher.PushBlend(CopyBlend);
			batcher.PushSampler(new(TextureFilter.Nearest, TextureWrap.Clamp, TextureWrap.Clamp));
			batcher.Image(source.Attachments[0], position, Vector2.Zero, scale, 0, Color.White);
			batcher.PopSampler();
			batcher.PopBlend();
			batcher.Render(destination);
			batcher.Clear();
		}
	}

	void DisposeAtlas()
	{
		atlasBackgroundScene?.Dispose();
		atlasForegroundScene?.Dispose();
		atlasBackgroundLight?.Dispose();
		atlasForegroundLight?.Dispose();
		atlasBackgroundLightEmission?.Dispose();
		atlasForegroundLightEmission?.Dispose();
		atlasTileField?.Dispose();
		atlasBackgroundCoverage?.Dispose();
		atlasNormal?.Dispose();
		atlasSpriteShadow?.Dispose();
		atlasSporeFog?.Dispose();
		atlasBackgroundScene = atlasForegroundScene = atlasBackgroundLight = atlasForegroundLight = null;
		atlasBackgroundLightEmission = atlasForegroundLightEmission = atlasTileField = atlasBackgroundCoverage = null;
		atlasNormal = atlasSpriteShadow = atlasSporeFog = null;
		atlasRoom = null;
		atlasNextRoom = null;
		atlasBounds = default;
	}

	void EnsureCapacity(int cellCount)
	{
		if (fieldCapacity >= cellCount)
			return;
		fieldCapacity = cellCount;
		var empty = new Vector4[cellCount];
		emission.Upload(empty);
		surface.Upload(empty);
		fieldA.Upload(empty);
		fieldB.Upload(empty);
	}

	void LoadShaders()
	{
		try
		{
			var ext = graphicsDevice.Driver.GetShaderExtension();
			var injectCode = Assets.TryReadCompiledShader($"IndirectInject.compute.{ext}");
			var propagateCode = Assets.TryReadCompiledShader($"IndirectPropagate.compute.{ext}");
			var wrapCode = Assets.TryReadCompiledShader($"IndirectWrap.compute.{ext}");
			var resolveCode = Assets.TryReadCompiledShader($"IndirectResolve.compute.{ext}");
			if (injectCode == null || propagateCode == null || resolveCode == null || wrapCode == null)
				return;
			injectShader = new(graphicsDevice, ShaderStage.Compute, injectCode, samplerCount: 8, uniformBufferCount: 1,
				readWriteStorageBufferCount: 4, threadCountX: 64, threadCountY: 1, threadCountZ: 1,
				entryPoint: "compute_main", name: "IndirectInject");
			propagateShader = new(graphicsDevice, ShaderStage.Compute, propagateCode, uniformBufferCount: 1,
				readOnlyStorageBufferCount: 3, readWriteStorageBufferCount: 1, threadCountX: 64, threadCountY: 1, threadCountZ: 1,
				entryPoint: "compute_main", name: "IndirectPropagate");
			wrapShader = new(graphicsDevice, ShaderStage.Compute, wrapCode, uniformBufferCount: 1,
				readOnlyStorageBufferCount: 3, readWriteStorageBufferCount: 1, threadCountX: 64,
				entryPoint: "compute_main", name: "IndirectWrap");
			resolveShader = new(graphicsDevice, ShaderStage.Compute, resolveCode, samplerCount: 1, uniformBufferCount: 1,
				readOnlyStorageBufferCount: 2, readWriteStorageTextureCount: 2, threadCountX: 8, threadCountY: 8, threadCountZ: 1,
				entryPoint: "compute_main", name: "IndirectResolve");
		}
		catch (Exception e)
		{
			Console.Error.WriteLine($"GPU global illumination init failed: {e}");
			injectShader?.Dispose();
			propagateShader?.Dispose();
			resolveShader?.Dispose();
			wrapShader?.Dispose();
			injectShader = propagateShader = resolveShader = wrapShader = null;
		}
	}

#if DEBUG
	public void ReloadShaders()
	{
		injectShader?.Dispose();
		propagateShader?.Dispose();
		resolveShader?.Dispose();
		wrapShader?.Dispose();
		injectShader = propagateShader = resolveShader = wrapShader = null;
		LoadShaders();
	}
#endif

	public void Dispose()
	{
		injectShader?.Dispose();
		propagateShader?.Dispose();
		resolveShader?.Dispose();
		wrapShader?.Dispose();
		emission.Dispose();
		surface.Dispose();
		fieldA.Dispose();
		fieldB.Dispose();
		DisposeAtlas();
	}
}
