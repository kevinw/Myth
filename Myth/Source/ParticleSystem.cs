using System.Numerics;
using System.Runtime.InteropServices;
using Foster.Framework;

namespace Myth;

/// <summary>
/// GPU-driven particle system. Particles live in a ping-pong
/// <see cref="ComputeStorageBuffer{T}"/>, are advanced by a compute shader, and
/// drawn with one instanced quad per particle reading the same buffer in the
/// vertex stage (no CPU readback). Many independent effects coexist: each
/// particle carries an effect index into a per-effect params buffer + color LUT,
/// and emission is GPU-side via a per-frame emit queue.
/// </summary>
public sealed class ParticleSystem : IDisposable
{
	public const int Capacity = 16384;
	public const int MaxEffects = 16;
	private const int MaxEmitsPerFrame = 64;
	private const int LutWidth = 64;
	private const int ThreadGroupSize = 64;

	// NOTE: keep this a multiple of 8 bytes. The matching HLSL/MSL struct has
	// float2 members, so Metal rounds its StructuredBuffer stride up to 8-byte
	// alignment; the C# size must match or the GPU reads past the buffer.
	[StructLayout(LayoutKind.Sequential)]
	private struct Particle
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
	private struct EffectParams
	{
		public float Gravity, Drag, NoiseAmount, NoiseScale;
		public float SizeStart, SizeEnd, CollisionMode, Bounciness;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct EmitParams
	{
		public uint Start, Count, Seed, Effect;
		public float OriginX, OriginY;
		public uint Shape;
		public float Radius, DirAngle, Spread;
		public float SpeedMin, SpeedMax, LifeMin, LifeMax;
		public float Pad0, Pad1;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct UpdateParams
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
	private struct VertexParams
	{
		public Vector2 TargetSize;
		public Vector2 Camera;
		public float Zoom;
		public float MaxEffectsF;
		public float Pad0, Pad1;
	}

	private readonly GraphicsDevice graphicsDevice;
	private readonly bool supported;
	private Shader? updateShader;
	private Shader? vertexShader;
	private Shader? fragmentShader;
	private Material? material;
	private Mesh<PosTexColVertex, ushort>? mesh;
	private ComputeStorageBuffer<Particle>? bufferA;
	private ComputeStorageBuffer<Particle>? bufferB;
	private bool currentIsB;
	private StorageBuffer<EffectParams>? effectsBuffer;
	private StorageBuffer<EmitParams>? emitsBuffer;
	private Texture? colorLut;
	private int colorLutHash = -1;

	private readonly ParticleEffect?[] effects = new ParticleEffect?[MaxEffects];
	private readonly EffectParams[] effectData = new EffectParams[MaxEffects];
	private readonly Dictionary<string, int> slotByName = new(StringComparer.OrdinalIgnoreCase);
	private readonly int[] slotRefCount = new int[MaxEffects];
	private readonly List<EmitParams> pendingEmits = [];
	private int emitCursor;
	private float time;
	private readonly Rng rng = new(1234);

	public bool AutoBurst;

	private ComputeStorageBuffer<Particle> Current => currentIsB ? bufferB! : bufferA!;

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

	/// <summary>
	/// Acquires a GPU slot for the named effect asset, sharing one slot across
	/// all users of the same name (ref-counted). Returns -1 if the pool is full.
	/// </summary>
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
			if (effects[i] == null)
			{
				effects[i] = ParticleEffectAssets.Get(name);
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
		effects[slot] = null;
		foreach (var (name, index) in slotByName)
		{
			if (index == slot)
			{
				slotByName.Remove(name);
				break;
			}
		}
	}

	/// <summary>Queues a burst of the given effect at a world position.</summary>
	public void Emit(int effectIndex, Vector2 worldPosition, int count)
	{
		if (GetEffect(effectIndex, count) is not { } effect)
			return;
		QueueEmit(effectIndex, effect, worldPosition, count,
			(uint)effect.Shape,
			effect.DirectionDegrees * (MathF.PI / 180.0f),
			effect.SpeedMin, effect.SpeedMax);
	}

	/// <summary>
	/// Queues a directional cone burst, overriding the effect's aim and scaling
	/// its speed (e.g. emitting along motion). Used by code-driven emitters.
	/// </summary>
	public void Emit(int effectIndex, Vector2 worldPosition, int count, float directionRadians, float speedScale = 1.0f)
	{
		if (GetEffect(effectIndex, count) is not { } effect)
			return;
		QueueEmit(effectIndex, effect, worldPosition, count,
			(uint)ParticleEmitterShape.Cone,
			directionRadians,
			effect.SpeedMin * speedScale, effect.SpeedMax * speedScale);
	}

	private ParticleEffect? GetEffect(int effectIndex, int count)
	{
		if (!supported || count <= 0 || effectIndex < 0 || effectIndex >= MaxEffects)
			return null;
		if (pendingEmits.Count >= MaxEmitsPerFrame)
			return null;
		return effects[effectIndex];
	}

	private void QueueEmit(int effectIndex, ParticleEffect effect, Vector2 worldPosition, int count, uint shape, float dirRadians, float speedMin, float speedMax)
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
			Radius = effect.EmitterRadius,
			DirAngle = dirRadians,
			Spread = effect.SpreadDegrees * (MathF.PI / 180.0f),
			SpeedMin = speedMin,
			SpeedMax = speedMax,
			LifeMin = effect.LifeMin,
			LifeMax = effect.LifeMax,
		});
		emitCursor = (emitCursor + count) % Capacity;
	}

	/// <summary>
	/// Advances the sim. <paramref name="solidMask"/> is the world-collision
	/// texture (foreground coverage in its alpha); <paramref name="camera"/> and
	/// <paramref name="zoom"/> map world positions into it.
	/// </summary>
	public void Update(float dt, Texture? solidMask, Vector2 camera, float zoom)
	{
		if (!supported)
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

		new ComputeCommand(updateShader!)
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
	private void BuildEffectData()
	{
		for (var i = 0; i < MaxEffects; i++)
		{
			if (effects[i] is { } e)
			{
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
			hash = HashCode.Combine(hash, effects[i]?.ColorHash() ?? 0);
		if (hash == colorLutHash)
			return;
		colorLutHash = hash;

		Span<Color> texels = stackalloc Color[LutWidth * MaxEffects];
		for (var i = 0; i < MaxEffects; i++)
		{
			if (effects[i] is not { } e)
				continue;
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
