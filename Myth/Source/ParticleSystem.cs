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
	const int LutWidth = 64;
	const int ThreadGroupSize = 64;

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
		public float Pad; // pads 36 -> 40 bytes to match the MSL struct stride
	}

	[StructLayout(LayoutKind.Sequential)]
	struct EffectParams
	{
		public float Gravity, Drag, NoiseAmount, NoiseScale;
		public float SizeStart, SizeEnd, CollisionMode, Bounciness;
		public float Style, Pad0, Pad1, Pad2;
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
		public float Pad0, Pad1, Pad2;
	}

	[StructLayout(LayoutKind.Sequential)]
	struct VertexParams
	{
		public Vector2 TargetSize;
		public Vector2 Camera;
		public float Zoom;
		public float MaxEffectsF;
		public float Pad0, Pad1;
	}

	readonly GraphicsDevice graphicsDevice;
	readonly bool supported;
	Shader? updateShader;
	Shader? vertexShader;
	Shader? fragmentShader;
	Material? material;
	Mesh<PosTexColVertex, ushort>? mesh;
	ComputeStorageBuffer<Particle>? bufferA;
	ComputeStorageBuffer<Particle>? bufferB;
	bool currentIsB;
	StorageBuffer<EffectParams>? effectsBuffer;
	StorageBuffer<EmitParams>? emitsBuffer;
	Texture? colorLut;
	int colorLutHash = -1;

	readonly string?[] effectNames = new string?[MaxEffects];
	readonly EffectParams[] effectData = new EffectParams[MaxEffects];
	readonly Dictionary<string, int> slotByName = new(StringComparer.OrdinalIgnoreCase);
	readonly int[] slotRefCount = new int[MaxEffects];
	readonly List<EmitParams> pendingEmits = [];
	int emitCursor;
	float time;
	readonly Rng rng = new(1234);

	public bool AutoBurst;

	ComputeStorageBuffer<Particle> Current => currentIsB ? bufferB! : bufferA!;

	public bool Supported => supported;

	public ParticleSystem(GraphicsDevice graphicsDevice)
	{
		this.graphicsDevice = graphicsDevice;
		try
		{
			var ext = graphicsDevice.Driver.GetShaderExtension();
			var dir = Path.Join(Assets.AssetsPath, "Shaders", "Compiled");
			var updatePath = Path.Join(dir, $"ParticleUpdate.compute.{ext}");
			var vertexPath = Path.Join(dir, $"Particle.vertex.{ext}");
			var fragmentPath = Path.Join(dir, $"Particle.fragment.{ext}");
			if (!File.Exists(updatePath) || !File.Exists(vertexPath) || !File.Exists(fragmentPath))
				return;

			updateShader = new Shader(
				graphicsDevice, ShaderStage.Compute, File.ReadAllBytes(updatePath),
				samplerCount: 1,
				uniformBufferCount: 1,
				readOnlyStorageBufferCount: 3,
				readWriteStorageBufferCount: 1,
				threadCountX: ThreadGroupSize, threadCountY: 1, threadCountZ: 1,
				entryPoint: "compute_main", name: "ParticleUpdate");

			vertexShader = new Shader(
				graphicsDevice, ShaderStage.Vertex, File.ReadAllBytes(vertexPath),
				samplerCount: 1, uniformBufferCount: 1, storageBufferCount: 2,
				entryPoint: "vertex_main", name: "ParticleVertex");

			fragmentShader = new Shader(
				graphicsDevice, ShaderStage.Fragment, File.ReadAllBytes(fragmentPath),
				entryPoint: "fragment_main", name: "ParticleFragment");

			material = new Material(vertexShader, fragmentShader);

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

			effectsBuffer = new StorageBuffer<EffectParams>(graphicsDevice, "ParticleEffects");
			effectsBuffer.Upload(new EffectParams[MaxEffects]);
			emitsBuffer = new StorageBuffer<EmitParams>(graphicsDevice, "ParticleEmits");
			emitsBuffer.Upload(new EmitParams[MaxEmitsPerFrame]);

			colorLut = new Texture(graphicsDevice, LutWidth, MaxEffects, TextureFormat.Color, TextureFlags.None, "ParticleColorLut");

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
		return -1;
	}

	public void ReleaseEffect(int slot)
	{
		if (slot < 0 || slot >= MaxEffects || slotRefCount[slot] <= 0)
			return;
		if (--slotRefCount[slot] > 0)
			return;
		if (effectNames[slot] is { } name)
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

	public void EmitLine(int effectIndex, Vector2 origin, Vector2 direction, float lineLength = -1f, Vector2 baseVelocity = default)
	{
		if (GetEffect(effectIndex, 1) is not { } effect)
			return;
		QueueEmit(effectIndex, effect, origin, effect.BurstCount,
			(uint)ParticleEmitterShape.Line,
			MathF.Atan2(direction.Y, direction.X),
			effect.SpeedMin, effect.SpeedMax,
			radius: lineLength == -1f ? effect.EmitterRadius : lineLength, spreadRadians: effect.SpreadDegrees, baseVelocity: baseVelocity);
	}

	public void EmitLineBetween(int effectIndex, Vector2 from, Vector2 to)
	{
		if (GetEffect(effectIndex, 1) is not { } effect)
			return;
		var delta = to - from;
		var count = Math.Max(1, (int)MathF.Round(effect.BurstCount * delta.Length() / MathF.Max(1, effect.EmitterRadius)));
		QueueEmit(effectIndex, effect, from, count,
			(uint)ParticleEmitterShape.Line,
			MathF.Atan2(delta.Y, delta.X),
			effect.SpeedMin, effect.SpeedMax,
			radius: delta.Length(), spreadRadians: effect.SpreadDegrees);
	}

	ParticleEffect? GetEffect(int effectIndex, int count)
	{
		if (!supported || count <= 0 || effectIndex < 0 || effectIndex >= MaxEffects)
			return null;
		if (pendingEmits.Count >= MaxEmitsPerFrame)
			return null;
		return effectNames[effectIndex] is { } name ? ParticleEffectAssets.Get(name) : null;
	}

	void QueueEmit(int effectIndex, ParticleEffect effect, Vector2 worldPosition, int count, uint shape, float dirRadians, float speedMin, float speedMax, float radius = float.NaN, float spreadRadians = float.NaN, Vector2 baseVelocity = default)
	{
		count = Math.Min(count, Capacity);
		pendingEmits.Add(new EmitParams
		{
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

		var src = Current;
		var dst = currentIsB ? bufferA! : bufferB!;

		var uniforms = new UniformBuffer();
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
		});

		new ComputeCommand(updateShader)
		{
			Samplers = [new(solidMask, new(TextureFilter.Nearest, TextureWrap.Clamp, TextureWrap.Clamp))],
			ReadOnlyStorageBuffers = [src, effectsBuffer, emitsBuffer],
			ReadWriteStorageBuffers = [dst],
			UniformBuffers = [uniforms],
			GroupCountX = (Capacity + ThreadGroupSize - 1) / ThreadGroupSize,
			GroupCountY = 1,
			GroupCountZ = 1,
		}.Submit(graphicsDevice);

		currentIsB = !currentIsB;
		pendingEmits.Clear();
	}

	public void Render(Target target, Vector2 camera, float zoom)
	{
		if (!supported)
			return;

		material!.Vertex.SetUniformBuffer(new VertexParams
		{
			TargetSize = new Vector2(target.Width, target.Height),
			Camera = camera,
			Zoom = zoom,
			MaxEffectsF = MaxEffects,
		});
		material.Vertex.Samplers[0] = new(colorLut, new TextureSampler(TextureFilter.Linear, TextureWrap.Clamp, TextureWrap.Clamp));

		new DrawCommand(target, mesh!, material)
		{
			InstanceCount = Capacity,
			BlendMode = new BlendMode(BlendOp.Add, BlendFactor.SrcAlpha, BlendFactor.One),
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

		Span<Color> texels = stackalloc Color[LutWidth * MaxEffects];
		for (var i = 0; i < MaxEffects; i++)
		{
			if (effectNames[i] is not { } name)
				continue;
			var e = ParticleEffectAssets.Get(name);
			for (var x = 0; x < LutWidth; x++)
				texels[i * LutWidth + x] = e.EvaluateColor(x / (float)(LutWidth - 1));
		}
		colorLut!.SetData<Color>(texels);
	}

	public void Dispose()
	{
		bufferA?.Dispose();
		bufferB?.Dispose();
		effectsBuffer?.Dispose();
		emitsBuffer?.Dispose();
		colorLut?.Dispose();
		mesh?.Dispose();
		updateShader?.Dispose();
		vertexShader?.Dispose();
		fragmentShader?.Dispose();
	}
}
