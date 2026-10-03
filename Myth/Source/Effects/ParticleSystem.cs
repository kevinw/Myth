using System.Numerics;
using System.Runtime.InteropServices;
using Foster.Framework;

namespace Myth;

/// GPU-driven particle system. Particles live in a ping-pong
/// <see cref="ComputeStorageBuffer{T}"/>, are advanced by a compute shader, and
/// drawn with one instanced quad per particle reading the same buffer in the
/// vertex stage (no CPU readback). Many independent effects coexist: each
/// particle carries an effect index into a per-effect params buffer + color LUT,
/// and emission is GPU-side via a per-frame emit queue.
public sealed class ParticleSystem : IDisposable
{
	public const int Capacity = 16384;
	public const int MaxEffects = 16;
	const int MaxEmitsPerFrame = 64;
	const int MaxEmitPointsPerFrame = 512;
	const int LutWidth = 64;
	// LUT rows 0..MaxEffects-1 are particle gradients, MaxEffects.. are trail gradients.
	const int LutHeight = MaxEffects * 2;
	const int ThreadGroupSize = 64;
	// Recorded positions per particle; keep in sync with TRAIL_SAMPLES in the shaders.
	const int TrailSamples = 16;
	const int TrailSegs = TrailSamples - 1;

	// NOTE: keep this a multiple of 8 bytes. The matching HLSL/MSL struct has
	// float2 members, so Metal rounds its StructuredBuffer stride up to 8-byte
	// alignment; the C# size must match or the GPU reads past the buffer.
	[StructLayout(LayoutKind.Sequential)]
	struct Particle
	{
		public float PosX, PosY;
		public float VelX, VelY;
		public float Age;
		public float Lifetime;
		public float Seed;
		public float Effect;
		public float Flags;
		public float TrailHead;
		public float TrailTimer;
		public float Pad; // pads 44 -> 48 bytes to match the MSL struct stride
	}

	[StructLayout(LayoutKind.Sequential)]
	struct EffectParams
	{
		public float Gravity, Drag, NoiseAmount, NoiseScale;
		public float SizeStart, SizeEnd, CollisionMode, Bounciness;
		public float Style, TrailSeconds, TrailWidthStart, TrailWidthEnd;
		public float LightResponse, CollideWithActors, Pad1, Pad2;
	}

	[StructLayout(LayoutKind.Sequential)]
	struct EmitParams
	{
		public uint Start, Count, Seed, Effect;
		public float OriginX, OriginY;
		public uint Shape;
		public float Radius, DirAngle, Spread;
		public float SpeedMin, SpeedMax, LifeMin, LifeMax;
		public float BaseVelocityX, BaseVelocityY;
		public uint PointStart, Pad0, Pad1, Pad2;
	}

	// Per-effect silhouette force field (see EdgeFieldParams in ParticleUpdate.hlsl).
	[StructLayout(LayoutKind.Sequential)]
	struct EdgeFieldParams
	{
		public float OriginX, OriginY, RectLocalX, RectLocalY;
		public float RectW, RectH, SrcX, SrcY;
		public float Flip, Attraction, Flow, Enable;
	}

	[StructLayout(LayoutKind.Sequential)]
	struct UpdateParams
	{
		public uint Count;
		public float Dt;
		public float Time;
		public uint EmitCount;

		public float CameraX;
		public float CameraY;
		public float Zoom;
		public float TargetW;

		public float TargetH;
		public float SdfAtlasW, SdfAtlasH, Pad2;
	}

	// Shared by the particle and trail vertex shaders (the particle shader
	// ignores MaxEffectsF).
	[StructLayout(LayoutKind.Sequential)]
	struct VertexParams
	{
		public Matrix4x4 Matrix;
		public float LutHeightF;
		public float MaxEffectsF;
		public float Pad0, Pad1;
	}

	[StructLayout(LayoutKind.Sequential)]
	struct FragmentParams
	{
		public float TargetWidth, TargetHeight, LightingEnabled, Pad0;
		public float AmbientR, AmbientG, AmbientB, Pad1;
	}

	readonly GraphicsDevice graphicsDevice;
	readonly bool supported;
	Shader? updateShader;
	Shader? vertexShader;
	Shader? fragmentShader;
	Shader? trailVertexShader;
	Shader? trailFragmentShader;
	Material? material;
	Material? trailMaterial;
	Mesh<PosTexColVertex, ushort>? mesh;
	ComputeStorageBuffer<Particle>? bufferA;
	ComputeStorageBuffer<Particle>? bufferB;
	ComputeStorageBuffer<Vector2>? trailBufferA;
	ComputeStorageBuffer<Vector2>? trailBufferB;
	bool currentIsB;
	StorageBuffer<EffectParams>? effectsBuffer;
	StorageBuffer<EmitParams>? emitsBuffer;
	StorageBuffer<Vector4>? emitPointsBuffer;
	StorageBuffer<EdgeFieldParams>? fieldsBuffer;
	Texture? colorLut;
	int colorLutHash = -1;

	readonly string?[] effectNames = new string?[MaxEffects];
	readonly EffectParams[] effectData = new EffectParams[MaxEffects];
	readonly EdgeFieldParams[] fieldData = new EdgeFieldParams[MaxEffects];
	readonly Dictionary<string, int> slotByName = new(StringComparer.OrdinalIgnoreCase);
	readonly int[] slotRefCount = new int[MaxEffects];
	readonly List<EmitParams> pendingEmits = [];
	readonly List<Vector4> pendingEmitPoints = [];
	int emitCursor;
	float time;
	readonly Rng rng = new(1234);

	public bool AutoBurst;

	ComputeStorageBuffer<Particle> Current => currentIsB ? bufferB! : bufferA!;
	ComputeStorageBuffer<Vector2> CurrentTrail => currentIsB ? trailBufferB! : trailBufferA!;

	public bool Supported => supported;

	public ParticleSystem(GraphicsDevice graphicsDevice)
	{
		this.graphicsDevice = graphicsDevice;
		try
		{
			var ext = graphicsDevice.Driver.GetShaderExtension();
			var updateCode = Assets.TryReadCompiledShader($"ParticleUpdate.compute.{ext}");
			var vertexCode = Assets.TryReadCompiledShader($"Particle.vertex.{ext}");
			var fragmentCode = Assets.TryReadCompiledShader($"Particle.fragment.{ext}");
			var trailVertexCode = Assets.TryReadCompiledShader($"ParticleTrail.vertex.{ext}");
			var trailFragmentCode = Assets.TryReadCompiledShader($"ParticleTrail.fragment.{ext}");
			if (updateCode == null || vertexCode == null || fragmentCode == null
				|| trailVertexCode == null || trailFragmentCode == null)
				return;

			updateShader = new Shader(
				graphicsDevice, ShaderStage.Compute, updateCode,
				samplerCount: 2,
				uniformBufferCount: 1,
				readOnlyStorageBufferCount: 6,
				readWriteStorageBufferCount: 2,
				threadCountX: ThreadGroupSize, threadCountY: 1, threadCountZ: 1,
				entryPoint: "compute_main", name: "ParticleUpdate");

			vertexShader = new Shader(
				graphicsDevice, ShaderStage.Vertex, vertexCode,
				samplerCount: 1, uniformBufferCount: 1, storageBufferCount: 2,
				entryPoint: "vertex_main", name: "ParticleVertex");

			fragmentShader = new Shader(
				graphicsDevice, ShaderStage.Fragment, fragmentCode,
				samplerCount: 1, uniformBufferCount: 1,
				entryPoint: "fragment_main", name: "ParticleFragment");

			material = new Material(vertexShader, fragmentShader);

			trailVertexShader = new Shader(
				graphicsDevice, ShaderStage.Vertex, trailVertexCode,
				samplerCount: 1, uniformBufferCount: 1, storageBufferCount: 3,
				entryPoint: "vertex_main", name: "ParticleTrailVertex");

			trailFragmentShader = new Shader(
				graphicsDevice, ShaderStage.Fragment, trailFragmentCode,
				samplerCount: 1, uniformBufferCount: 1,
				entryPoint: "fragment_main", name: "ParticleTrailFragment");

			trailMaterial = new Material(trailVertexShader, trailFragmentShader);

			mesh = new Mesh<PosTexColVertex, ushort>(graphicsDevice);
			Span<PosTexColVertex> vertices =
			[
				new(new(-0.5f, -0.5f), new(0, 0), Color.White),
				new(new( 0.5f, -0.5f), new(1, 0), Color.White),
				new(new( 0.5f,  0.5f), new(1, 1), Color.White),
				new(new(-0.5f,  0.5f), new(0, 1), Color.White),
			];
			Span<ushort> indices = [0, 1, 2, 0, 2, 3];
			mesh.SetVertices(vertices);
			mesh.SetIndices(indices);

			var empty = new Particle[Capacity];
			bufferA = new ComputeStorageBuffer<Particle>(graphicsDevice, "ParticlesA");
			bufferB = new ComputeStorageBuffer<Particle>(graphicsDevice, "ParticlesB");
			bufferA.Upload(empty);
			bufferB.Upload(empty);

			var emptyTrail = new Vector2[Capacity * TrailSamples];
			trailBufferA = new ComputeStorageBuffer<Vector2>(graphicsDevice, "ParticleTrailsA");
			trailBufferB = new ComputeStorageBuffer<Vector2>(graphicsDevice, "ParticleTrailsB");
			trailBufferA.Upload(emptyTrail);
			trailBufferB.Upload(emptyTrail);

			effectsBuffer = new StorageBuffer<EffectParams>(graphicsDevice, "ParticleEffects");
			effectsBuffer.Upload(new EffectParams[MaxEffects]);
			emitsBuffer = new StorageBuffer<EmitParams>(graphicsDevice, "ParticleEmits");
			emitsBuffer.Upload(new EmitParams[MaxEmitsPerFrame]);
			emitPointsBuffer = new StorageBuffer<Vector4>(graphicsDevice, "ParticleEmitPoints");
			emitPointsBuffer.Upload(new Vector4[MaxEmitPointsPerFrame]);
			fieldsBuffer = new StorageBuffer<EdgeFieldParams>(graphicsDevice, "ParticleEdgeFields");
			fieldsBuffer.Upload(new EdgeFieldParams[MaxEffects]);

			colorLut = new Texture(graphicsDevice, LutWidth, LutHeight, TextureFormat.Color, TextureFlags.None, "ParticleColorLut");

			supported = true;
		}
		catch (Exception e)
		{
			Console.Error.WriteLine($"ParticleSystem init failed: {e}");
		}
	}

	/// Acquires a GPU slot for the named effect asset, sharing one slot across
	/// all users of the same name (ref-counted). Returns -1 if the pool is full.
	public int AcquireEffect(string name)
	{
		if (!supported)
			return -1;
		if (slotByName.TryGetValue(name, out var existing))
		{
			slotRefCount[existing]++;
			return existing;
		}
		for (var i = 0; i < MaxEffects; i++)
		{
			if (effectNames[i] == null)
			{
				effectNames[i] = name;
				slotByName[name] = i;
				slotRefCount[i] = 1;
				return i;
			}
		}
		Console.Error.WriteLine("ParticleSystem.AcquireEffect error: out of slots");
		return -1;
	}

	public void ReleaseEffect(ref int slot) {
		ReleaseEffect(slot);
		slot = -1;
	}

	/// Acquires a dedicated (unshared) slot for the named effect. Required for
	/// effects with an edge field (EdgeAttraction > 0), which bind one sprite
	/// instance per slot; shed-only effects should use AcquireEffect instead.
	public int AcquireEffectInstance(string name)
	{
		if (!supported)
			return -1;
		for (var i = 0; i < MaxEffects; i++)
		{
			if (effectNames[i] == null)
			{
				effectNames[i] = name;
				slotRefCount[i] = 1;
				return i;
			}
		}
		Console.Error.WriteLine("ParticleSystem.AcquireEffectInstance error: out of slots");
		return -1;
	}

	public void ReleaseEffect(int slot)
	{
		if (slot < 0 || slot >= MaxEffects || slotRefCount[slot] <= 0)
			return;
		if (--slotRefCount[slot] > 0)
			return;
		// Instance slots share a name with a potential shared slot; only drop
		// the name mapping if it points at this slot.
		if (effectNames[slot] is { } name && slotByName.TryGetValue(name, out var mapped) && mapped == slot)
			slotByName.Remove(name);
		effectNames[slot] = null;
	}

	/// Queues a burst of the given effect at a world position.
	public void Emit(int effectIndex, Vector2 worldPosition, int count)
	{
		if (GetEffect(effectIndex, count) is not { } effect)
			return;
		QueueEmit(effectIndex, effect, worldPosition, count,
			(uint)effect.Shape,
			effect.DirectionDegrees * (MathF.PI / 180.0f),
			effect.SpeedMin, effect.SpeedMax);
	}

	/// Queues a directional cone burst, overriding the effect's aim and scaling
	/// its speed (e.g. emitting along motion). Used by code-driven emitters.
	public void Emit(int effectIndex, Vector2 worldPosition, int count, float directionRadians, float speedScale = 1.0f, float spreadRadians = float.NaN)
	{
		if (GetEffect(effectIndex, count) is not { } effect)
			return;
		QueueEmit(effectIndex, effect, worldPosition, count,
			(uint)ParticleEmitterShape.Cone,
			directionRadians,
			effect.SpeedMin * speedScale, effect.SpeedMax * speedScale,
			spreadRadians: spreadRadians);
	}

	// A Line emitter's `spread` is a cross-line width in world pixels rather than an angle (ParticleUpdate.hlsl,
	// shape 3), which is what a Line-authored effect's SpreadDegrees holds. Anything else authored SpreadDegrees
	// as a cone angle, so reading it as a width smears the burst that many pixels off the line -- a 115-degree
	// dust cone became a 115px band straddling the crusher's face. Those fall back to the emitter's own radius.
	static float LineCrossWidth(ParticleEffect effect)
		=> effect.Shape == ParticleEmitterShape.Line ? effect.SpreadDegrees : effect.EmitterRadius;

	// Particles per unit of line. A Line-authored effect keeps its authored length in EmitterRadius, so its
	// density comes out as drawn; anything else has no length to speak of, and spends its burst over a tile.
	static float LineDensityUnit(ParticleEffect effect)
		=> MathF.Max(1, effect.Shape == ParticleEmitterShape.Line ? effect.EmitterRadius : Game.TileSize);

	public void EmitLine(int effectIndex, Vector2 origin, Vector2 direction, float lineLength = -1f, Vector2 baseVelocity = default)
	{
		if (GetEffect(effectIndex, 1) is not { } effect)
			return;
		QueueEmit(effectIndex, effect, origin, effect.BurstCount,
			(uint)ParticleEmitterShape.Line,
			MathF.Atan2(direction.Y, direction.X),
			effect.SpeedMin, effect.SpeedMax,
			radius: lineLength == -1f ? effect.EmitterRadius : lineLength, spreadRadians: LineCrossWidth(effect), baseVelocity: baseVelocity);
	}

	public void EmitLineBetween(int effectIndex, Vector2 from, Vector2 to)
	{
		if (GetEffect(effectIndex, 1) is not { } effect)
			return;
		var delta = to - from;
		var count = Math.Max(1, (int)MathF.Round(effect.BurstCount * delta.Length() / LineDensityUnit(effect)));
		QueueEmit(effectIndex, effect, from, count,
			(uint)ParticleEmitterShape.Line,
			MathF.Atan2(delta.Y, delta.X),
			effect.SpeedMin, effect.SpeedMax,
			radius: delta.Length(), spreadRadians: LineCrossWidth(effect));
	}

	static readonly List<int> edgeScratch = [];

	/// Emits from a baked sprite frame's border (see SpriteSdf), filtered to
	/// the side chosen by the effect's Edge settings. <paramref name="canvasOffset"/>
	/// is the sprite canvas top-left in unflipped local space (Shift - Origin
	/// for actors); <paramref name="facing"/> is +1/-1. <paramref name="smearDt"/>
	/// spreads spawns back along <paramref name="velocity"/> to cover fast motion.
	public void EmitSpriteEdge(int effectIndex, string spriteName, int frameIndex, Vector2 position, Vector2 canvasOffset, int facing, Vector2 velocity, int count, float smearDt = 0f)
	{
		if (GetEffect(effectIndex, count) is not { } effect)
			return;
		var edge = SpriteSdf.EdgePoints(spriteName, frameIndex);
		if (edge.Length == 0)
			return;

		// Side filter in unflipped sprite-local space.
		var filter = effect.EdgeSide != ParticleEdgeSide.All;
		var sideDir = Vector2.Zero;
		if (effect.EdgeSide == ParticleEdgeSide.FacingMotion)
		{
			if (velocity.LengthSquared() < 1e-4f)
				return;
			sideDir = Vector2.Normalize(velocity with { X = velocity.X * facing });
		}
		else if (effect.EdgeSide == ParticleEdgeSide.FixedDirection)
		{
			var rad = effect.EdgeSideDegrees * (MathF.PI / 180.0f);
			sideDir = new Vector2(MathF.Cos(rad), MathF.Sin(rad));
		}
		var cosThreshold = MathF.Cos(effect.EdgeArcDegrees * 0.5f * (MathF.PI / 180.0f));

		edgeScratch.Clear();
		for (var i = 0; i < edge.Length; i++)
			if (!filter || Vector2.Dot(edge[i].Normal, sideDir) >= cosThreshold)
				edgeScratch.Add(i);
		if (edgeScratch.Count == 0)
			return;

		count = Math.Min(count, MaxEmitPointsPerFrame - pendingEmitPoints.Count);
		if (count <= 0)
			return;
		var pointStart = (uint)pendingEmitPoints.Count;

		// Evenly spaced picks with jitter: coherent coverage without clumping.
		var step = edgeScratch.Count / (float)count;
		for (var k = 0; k < count; k++)
		{
			var pick = edgeScratch[Math.Min((int)((k + rng.Float()) * step), edgeScratch.Count - 1)];
			var (canvasPos, normal) = edge[pick];
			var local = canvasOffset + canvasPos;
			var world = position + new Vector2(facing * local.X, local.Y);
			world -= velocity * (rng.Float() * smearDt);
			var angle = MathF.Atan2(normal.Y, facing * normal.X);
			pendingEmitPoints.Add(new Vector4(world.X, world.Y, angle, 0));
		}

		QueueEmit(effectIndex, effect, position, count,
			(uint)ParticleEmitterShape.Points,
			0f, effect.SpeedMin, effect.SpeedMax,
			spreadRadians: effect.SpreadDegrees * (MathF.PI / 180.0f),
			baseVelocity: velocity, pointStart: pointStart);
	}

	/// Binds the named sprite frame's silhouette SDF to the effect slot for
	/// this frame, enabling the effect's EdgeAttraction/EdgeFlow forces. Call
	/// every frame before the sim updates; the binding expires each frame.
	public void SetEffectField(int effectIndex, string spriteName, int frameIndex, Vector2 position, Vector2 canvasOffset, int facing)
	{
		if (!supported || effectIndex < 0 || effectIndex >= MaxEffects || effectNames[effectIndex] is not { } name)
			return;
		var effect = ParticleEffectAssets.Get(name);
		if (effect.EdgeAttraction <= 0f || SpriteSdf.Frame(spriteName, frameIndex) is not { } sdf)
			return;
		fieldData[effectIndex] = new EdgeFieldParams
		{
			OriginX = position.X,
			OriginY = position.Y,
			RectLocalX = canvasOffset.X - SpriteSdf.Margin,
			RectLocalY = canvasOffset.Y - SpriteSdf.Margin,
			RectW = sdf.Source.Width,
			RectH = sdf.Source.Height,
			SrcX = sdf.Source.X,
			SrcY = sdf.Source.Y,
			Flip = facing,
			Attraction = effect.EdgeAttraction,
			Flow = effect.EdgeFlow,
			Enable = 1f,
		};
	}

	ParticleEffect? GetEffect(int effectIndex, int count)
	{
		if (!supported || count <= 0 || effectIndex < 0 || effectIndex >= MaxEffects)
			return null;
		if (pendingEmits.Count >= MaxEmitsPerFrame)
			return null;
		return effectNames[effectIndex] is { } name ? ParticleEffectAssets.Get(name) : null;
	}

	void QueueEmit(int effectIndex, ParticleEffect effect, Vector2 worldPosition, int count, uint shape, float dirRadians, float speedMin, float speedMax, float radius = float.NaN, float spreadRadians = float.NaN, Vector2 baseVelocity = default, uint pointStart = 0)
	{
		count = Math.Min(count, Capacity);
		pendingEmits.Add(new EmitParams
		{
			PointStart = pointStart,
			Start = (uint)emitCursor,
			Count = (uint)count,
			Seed = (uint)rng.Int(),
			Effect = (uint)effectIndex,
			OriginX = worldPosition.X,
			OriginY = worldPosition.Y,
			Shape = shape,
			Radius = float.IsNaN(radius) ? effect.EmitterRadius : radius,
			DirAngle = dirRadians,
			Spread = float.IsNaN(spreadRadians)
				? effect.Shape == ParticleEmitterShape.Line ? effect.SpreadDegrees : effect.SpreadDegrees * (MathF.PI / 180.0f)
				: spreadRadians,
			SpeedMin = speedMin,
			SpeedMax = speedMax,
			LifeMin = effect.LifeMin,
			LifeMax = effect.LifeMax,
			BaseVelocityX = baseVelocity.X,
			BaseVelocityY = baseVelocity.Y,
		});
		emitCursor = (emitCursor + count) % Capacity;
	}

	readonly UniformBuffer uniforms = new();

	/// Advances the sim. <paramref name="solidMask"/> is the world-collision
	/// texture (foreground coverage in its alpha); <paramref name="camera"/> and
	/// <paramref name="zoom"/> map world positions into it.
	public void Update(float dt, Texture? solidMask, Vector2 camera, float zoom)
	{
		if (!supported || updateShader == null)
			return;

		BuildEffectData();

		dt = Math.Min(dt, 1.0f / 30.0f); // avoid blowups on a hitch
		time += dt;

		var emitCount = Math.Min(pendingEmits.Count, MaxEmitsPerFrame);
		if (emitCount > 0)
			emitsBuffer!.Upload(CollectionsMarshal.AsSpan(pendingEmits)[..emitCount]);
		if (pendingEmitPoints.Count > 0)
			emitPointsBuffer!.Upload(CollectionsMarshal.AsSpan(pendingEmitPoints));
		fieldsBuffer!.Upload(fieldData);

		var src = Current;
		var dst = currentIsB ? bufferA! : bufferB!;
		var trailSrc = CurrentTrail;
		var trailDst = currentIsB ? trailBufferA! : trailBufferB!;
		var sdfAtlas = SpriteSdf.Atlas;

		uniforms.Set(new UpdateParams
		{
			Count = Capacity,
			Dt = dt,
			Time = time,
			EmitCount = (uint)emitCount,
			CameraX = camera.X,
			CameraY = camera.Y,
			Zoom = zoom,
			TargetW = solidMask?.Width ?? 1,
			TargetH = solidMask?.Height ?? 1,
			SdfAtlasW = sdfAtlas?.Width ?? 1,
			SdfAtlasH = sdfAtlas?.Height ?? 1,
		});

		new ComputeCommand(updateShader)
		{
			Samplers =
			[
				new(solidMask, new(TextureFilter.Nearest, TextureWrap.Clamp, TextureWrap.Clamp)),
				new(sdfAtlas, new(TextureFilter.Linear, TextureWrap.Clamp, TextureWrap.Clamp)),
			],
			ReadOnlyStorageBuffers = [src, effectsBuffer, emitsBuffer, trailSrc, emitPointsBuffer, fieldsBuffer],
			ReadWriteStorageBuffers = [dst, trailDst],
			UniformBuffers = [uniforms],
			GroupCountX = (Capacity + ThreadGroupSize - 1) / ThreadGroupSize,
			GroupCountY = 1,
			GroupCountZ = 1,
		}.Submit(graphicsDevice);

		currentIsB = !currentIsB;
		pendingEmits.Clear();
		pendingEmitPoints.Clear();
		// Fields are one-frame bindings to a moving sprite; owners re-set them
		// every frame before Update or the force switches off.
		Array.Clear(fieldData);
	}

	public void Render(Target target, Target lightMask, Vector2 camera, float zoom, bool lightingEnabled, Color ambientLight)
	{
		if (!supported)
			return;

		var vertexParams = new VertexParams
		{
			Matrix = Matrix4x4.CreateTranslation(-camera.X, -camera.Y, 0) *
				Matrix4x4.CreateScale(zoom, zoom, 1) *
				Batcher.CreateProjectionMatrix(target.Bounds.Size),
			LutHeightF = LutHeight,
			MaxEffectsF = MaxEffects,
		};
		var ambient = ambientLight.ToVector3();
		var fragmentParams = new FragmentParams
		{
			TargetWidth = target.Width,
			TargetHeight = target.Height,
			LightingEnabled = lightingEnabled ? 1 : 0,
			AmbientR = ambient.X,
			AmbientG = ambient.Y,
			AmbientB = ambient.Z,
		};
		var lutSampler = new TextureSampler(TextureFilter.Linear, TextureWrap.Clamp, TextureWrap.Clamp);
		var blend = new BlendMode(BlendOp.Add, BlendFactor.SrcAlpha, BlendFactor.One);

		// Trails first so particle heads draw on top of their own ribbons.
		trailMaterial!.Vertex.SetUniformBuffer(vertexParams);
		trailMaterial.Vertex.Samplers[0] = new(colorLut, lutSampler);
		trailMaterial.Fragment.SetUniformBuffer(fragmentParams);
		trailMaterial.Fragment.Samplers[0] = new(lightMask, lutSampler);

		new DrawCommand(target, mesh!, trailMaterial)
		{
			InstanceCount = Capacity * TrailSegs,
			BlendMode = blend,
			VertexStorageBuffers = [Current, effectsBuffer, CurrentTrail],
		}.Submit(graphicsDevice);

		material!.Vertex.SetUniformBuffer(vertexParams);
		material.Vertex.Samplers[0] = new(colorLut, lutSampler);
		material.Fragment.SetUniformBuffer(fragmentParams);
		material.Fragment.Samplers[0] = new(lightMask, lutSampler);

		new DrawCommand(target, mesh!, material)
		{
			InstanceCount = Capacity,
			BlendMode = blend,
			VertexStorageBuffers = [Current, effectsBuffer],
		}.Submit(graphicsDevice);
	}

	// Refreshes the per-effect GPU params each frame (cheap) and re-bakes the
	// color LUT only when a gradient changes.
	void BuildEffectData()
	{
		for (var i = 0; i < MaxEffects; i++)
		{
			if (effectNames[i] is { } name)
			{
				var e = ParticleEffectAssets.Get(name);
				effectData[i] = new EffectParams
				{
					Gravity = e.Gravity,
					Drag = e.Drag,
					NoiseAmount = e.NoiseAmount,
					NoiseScale = e.NoiseScale,
					SizeStart = e.SizeStart,
					SizeEnd = e.SizeEnd,
					CollisionMode = (float)e.Collision,
					Bounciness = e.Bounciness,
					Style = (float)e.Style,
					TrailSeconds = e.TrailSeconds,
					TrailWidthStart = e.TrailWidthStart,
					TrailWidthEnd = e.TrailWidthEnd,
					LightResponse = e.LightResponse,
					CollideWithActors = e.CollideWithActors ? 1 : 0,
				};
			}
			else
			{
				effectData[i] = default;
			}
		}
		effectsBuffer!.Upload(effectData);

		var hash = 17;
		for (var i = 0; i < MaxEffects; i++)
			hash = HashCode.Combine(hash, effectNames[i] is { } name ? ParticleEffectAssets.Get(name).ColorHash() : 0);
		if (hash == colorLutHash)
			return;
		colorLutHash = hash;

		Span<Color> texels = stackalloc Color[LutWidth * LutHeight];
		for (var i = 0; i < MaxEffects; i++)
		{
			if (effectNames[i] is not { } name)
				continue;
			var e = ParticleEffectAssets.Get(name);
			for (var x = 0; x < LutWidth; x++)
			{
				var t = x / (float)(LutWidth - 1);
				texels[i * LutWidth + x] = e.EvaluateColor(t);
				texels[(MaxEffects + i) * LutWidth + x] = e.EvaluateTrailColor(t);
			}
		}
		colorLut!.SetData<Color>(texels);
	}

	public void Dispose()
	{
		bufferA?.Dispose();
		bufferB?.Dispose();
		trailBufferA?.Dispose();
		trailBufferB?.Dispose();
		effectsBuffer?.Dispose();
		emitsBuffer?.Dispose();
		emitPointsBuffer?.Dispose();
		fieldsBuffer?.Dispose();
		colorLut?.Dispose();
		mesh?.Dispose();
		updateShader?.Dispose();
		vertexShader?.Dispose();
		fragmentShader?.Dispose();
		trailVertexShader?.Dispose();
		trailFragmentShader?.Dispose();
	}
}
