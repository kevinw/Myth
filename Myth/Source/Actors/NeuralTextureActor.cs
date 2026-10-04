using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Foster.Framework;

namespace Myth;

public enum DynamismMode
{
	PaintedMask,
	ProceduralCurlAdvection,
}

public enum NeuralTextureOpacityMode
{
	Mask,
	LatentActivity,
}

public enum NeuralTextureNormalMode
{
	Decoded,
	Flat,
	DensityGradient,
}

public enum NeuralTextureSupersampling
{
	X1 = 1,
	X2 = 2,
}

[GameActor(Sprite = "Editor/neural_texture_actor", BoundsW = Game.TileSize * 4, BoundsH = Game.TileSize * 3,
	Anchor = SpriteAnchor.TopLeft, LivePositionEditable = true, ShowFacing = false)]
public class NeuralTextureActor : Actor, IResizable, IShapeOccluder, IPaintedMask
{
	readonly static bool doLog = false;

	public const int StateWidth = 128;
	public const int StateHeight = 128;
	const int ColorWeightCount = 3441;
	const int PairedWeightCount = 3466;
	const int MaxSlots = 4;
	// Concatenated models all use the six-channel stride; the final weight rows
	// for channels 0-2 end here in both source layouts.
	const int FinalWeightRowsEnd = 3440;
	const int FinalBiasOffset = 3464;
	const int MaxPerturbations = 64;
	public const string WeightsFolder = "NCA/DynamicTexture";
	static readonly ConditionalWeakTable<GraphicsDevice, SharedGpuResources> SharedResources = new();
	static readonly ConditionalWeakTable<GraphicsDevice, Shader> CurlAdvectionShaders = new();
	static readonly ConditionalWeakTable<GraphicsDevice, Shader> DensityQueryShaders = new();

	// The three NCT compute shaders are weight-independent and shared across every
	// NeuralTextureActor on a device. Creating a compute Shader eagerly compiles its GPU
	// pipeline (SDL_CreateGPUComputePipeline), which is the ~seconds-long stall the first
	// time a neural room (e.g. neural_duck) loads. Building the shared set up front — at the
	// loading screen — makes the first actor's Added() reuse it instantly. Returns null if
	// the compiled shaders are missing (unsupported driver / stripped build).
	static SharedGpuResources? GetSharedResources(GraphicsDevice graphicsDevice)
	{
		if (graphicsDevice.Driver == GraphicsDriver.Headless)
			return null;
		if (SharedResources.TryGetValue(graphicsDevice, out var existing))
			return existing;

		var ext = graphicsDevice.Driver.GetShaderExtension();
		var updateCode = Assets.TryReadCompiledShader($"NeuralTextureUpdate.compute.{ext}");
		var decodeCode = Assets.TryReadCompiledShader($"NeuralTextureDecode.compute.{ext}");
		var perturbCode = Assets.TryReadCompiledShader($"NeuralTexturePerturb.compute.{ext}");
		var resizeCode = Assets.TryReadCompiledShader($"NeuralTextureResize.compute.{ext}");
		if (updateCode == null || decodeCode == null || perturbCode == null || resizeCode == null)
			return null;

		return SharedResources.GetValue(graphicsDevice, _ => new(graphicsDevice, updateCode, decodeCode, perturbCode, resizeCode));
	}

	// Pre-builds the shared compute pipelines so the first neural room doesn't stall. Safe to
	// call before any NeuralTextureActor exists; a no-op if already warmed. See ShaderWarmup.
	public static void WarmPipelines(GraphicsDevice graphicsDevice) => GetSharedResources(graphicsDevice);

	// Procedural mask actors are uncommon, so don't add this pipeline's compilation cost to the
	// boot warmup or to rooms containing only the classic painted-mask actor.
	static Shader? GetCurlAdvectionShader(GraphicsDevice graphicsDevice)
	{
		if (graphicsDevice.Driver == GraphicsDriver.Headless)
			return null;
		if (CurlAdvectionShaders.TryGetValue(graphicsDevice, out var existing))
			return existing;

		var ext = graphicsDevice.Driver.GetShaderExtension();
		var code = Assets.TryReadCompiledShader($"NeuralTextureCurlAdvection.compute.{ext}");
		return code == null ? null : CurlAdvectionShaders.GetValue(graphicsDevice, _ => new(
			graphicsDevice, ShaderStage.Compute, code,
			uniformBufferCount: 1, readOnlyStorageBufferCount: 3, readWriteStorageBufferCount: 3,
			threadCountX: 8, threadCountY: 8, threadCountZ: 1,
			entryPoint: "compute_main", name: "NeuralTextureCurlAdvection"));
	}

	static Shader? GetDensityQueryShader(GraphicsDevice graphicsDevice)
	{
		if (graphicsDevice.Driver == GraphicsDriver.Headless)
			return null;
		if (DensityQueryShaders.TryGetValue(graphicsDevice, out var existing))
			return existing;

		var ext = graphicsDevice.Driver.GetShaderExtension();
		var code = Assets.TryReadCompiledShader($"NeuralTextureDensityQuery.compute.{ext}");
		return code == null ? null : DensityQueryShaders.GetValue(graphicsDevice, _ => new(
			graphicsDevice, ShaderStage.Compute, code,
			uniformBufferCount: 1, readOnlyStorageBufferCount: 2, readWriteStorageBufferCount: 1,
			threadCountX: 1, threadCountY: 1, threadCountZ: 1,
			entryPoint: "compute_main", name: "NeuralTextureDensityQuery"));
	}

	sealed class SharedGpuResources
	{
		public readonly Shader UpdateShader;
		public readonly Shader DecodeShader;
		public readonly Shader PerturbShader;
		public readonly Shader ResizeShader;
		readonly GraphicsDevice graphicsDevice;
		readonly Dictionary<string, (StorageBuffer<Vector4> Buffer, uint NormalSlots)> weights = [];

		public SharedGpuResources(GraphicsDevice graphicsDevice, byte[] updateCode, byte[] decodeCode, byte[] perturbCode, byte[] resizeCode)
		{
			this.graphicsDevice = graphicsDevice;
			UpdateShader = new(
				graphicsDevice, ShaderStage.Compute, updateCode,
				uniformBufferCount: 1, readOnlyStorageBufferCount: 3, readWriteStorageBufferCount: 1,
				threadCountX: 8, threadCountY: 8, threadCountZ: 1,
				entryPoint: "compute_main", name: "NeuralTextureUpdate");
			DecodeShader = new(
				graphicsDevice, ShaderStage.Compute, decodeCode,
				uniformBufferCount: 1, readOnlyStorageBufferCount: 4, readWriteStorageTextureCount: 2,
				threadCountX: 8, threadCountY: 8, threadCountZ: 1,
				entryPoint: "compute_main", name: "NeuralTextureDecode");
			PerturbShader = new(
				graphicsDevice, ShaderStage.Compute, perturbCode,
				uniformBufferCount: 1, readOnlyStorageBufferCount: 2, readWriteStorageBufferCount: 1,
				threadCountX: 8, threadCountY: 8, threadCountZ: 1,
				entryPoint: "compute_main", name: "NeuralTexturePerturb");
			ResizeShader = new(
				graphicsDevice, ShaderStage.Compute, resizeCode,
				uniformBufferCount: 1, readOnlyStorageBufferCount: 1, readWriteStorageBufferCount: 1,
				threadCountX: 8, threadCountY: 8, threadCountZ: 1,
				entryPoint: "compute_main", name: "NeuralTextureResize");
		}

		public (StorageBuffer<Vector4> Buffer, uint NormalSlots) GetWeights(string[] files)
		{
			var key = string.Join('\n', files);
			if (weights.TryGetValue(key, out var cached))
				return cached;

			var packed = new Vector4[MaxSlots * PairedWeightCount];
			var normalSlots = PackWeights(files, packed);
			var buffer = new StorageBuffer<Vector4>(graphicsDevice, $"NeuralTextureWeights:{key}");
			buffer.Upload(packed);
			return weights[key] = (buffer, normalSlots);
		}

		// Packs each slot's model into the shared weight layout, returning the bitmask of slots whose
		// model carries normal channels. Split out so callers that supply their own buffer (the
		// editor's thumbnail rig) don't add a 217KB entry to the cache for every model browsed.
		public static uint PackWeights(string[] files, Span<Vector4> packed)
		{
			var normalSlots = 0u;
			for (var slot = 0; slot < MaxSlots; slot++)
			{
				if (string.IsNullOrEmpty(files[slot]))
					continue;
				var bytes = Assets.Content.ReadAllBytes($"{WeightsFolder}/{files[slot]}");
				var values = MemoryMarshal.Cast<byte, Vector4>(bytes.AsSpan());
				var dest = packed.Slice(slot * PairedWeightCount, PairedWeightCount);
				if (values.Length == PairedWeightCount)
				{
					values.CopyTo(dest);
					normalSlots |= 1u << slot;
				}
				else if (values.Length == ColorWeightCount)
				{
					values[..FinalWeightRowsEnd].CopyTo(dest);
					var bias = values[FinalWeightRowsEnd];
					dest[FinalBiasOffset] = new Vector4(bias.X, bias.Y, bias.Z, 0.5f);
					dest[FinalBiasOffset + 1] = new Vector4(0.5f, 1.0f, 0.0f, 0.0f);
				}
				else
					throw new InvalidDataException($"Neural texture model '{files[slot]}' has {values.Length} weight vectors; expected {ColorWeightCount} (color) or {PairedWeightCount} (color+normal).");
			}
			return normalSlots;
		}
	}

	[StructLayout(LayoutKind.Sequential)]
	struct CellState
	{
		public Vector4 C0, C1, C2, C3;
	}

	struct UpdateParams
	{
		public uint Width;
		public uint Height;
		public uint Seed;
		public float FireRate;
		public uint OriginX;
		public uint OriginY;
		public uint DispatchWidth;
		public uint DispatchHeight;
		public Vector2 FlowOrigin;
		public float SimulationScale;
		public float SmokeBlend;
		public uint SmokeSlot;
		public float Pad0;
		public Vector2 FlowSize;
	}

	struct ResizeParams
	{
		public Point2 SourceSize;
		public Point2 DestinationSize;
		public Vector2 Offset;
		public float Scale;
		public float Padding;
	}

	struct DecodeParams
	{
		public uint Width;
		public uint Height;
		public uint OutputWidth;
		public uint OutputHeight;
		public uint NormalSlots;
		public float Contrast;
		public float Saturation;
		public float Brightness;
		public Vector4 Tint;
		public float NormalStrength;
		public float DetailScale;
		public float PatchFrequency;
		public float SirenFrequency;
		public Vector2 DetailPhase;
		public float LatentGain;
		public float LatentBias;
		public uint NearestStateSampling;
		public float SimulationScale;
		public float SmokeBlend;
		public uint SmokeSlot;
		public uint OpacityMode;
		public float Opacity;
		public float DensityLow;
		public float DensityHigh;
		public uint NormalMode;
		public uint ProceduralMask;
		public Vector2 DetailOrigin;
		public uint DecodeSupersampling;
		public uint Padding0;
		public uint Padding1;
		public uint Padding2;
	}

	[StructLayout(LayoutKind.Sequential)]
	struct PerturbStamp
	{
		public Vector2 From;
		public Vector2 To;
		public float Radius;
		public float Strength;
		public Vector2 Push;
	}

	struct PerturbParams
	{
		public uint Width;
		public uint Height;
		public uint StampCount;
		public uint Padding;
	}

	struct CurlAdvectionParams
	{
		public uint Width;
		public uint Height;
		public float Time;
		public float DeltaTime;
		public float CurlScale;
		public float CurlSpeed;
		public float BoundarySharpness;
		public float Padding;
	}

	struct DensityQueryParams
	{
		public uint Width;
		public uint Height;
		public float DensityLow;
		public float DensityHigh;
		public float SmokeBlend;
		public uint Padding0;
		public uint Padding1;
		public uint Padding2;
		public Vector2 Head;
		public Vector2 Torso;
		public Vector2 Feet;
		public Vector2 BelowFeet;
	}

	readonly record struct PawnSample(Point2 Position, float Time);
	readonly record struct FluidCrossing(Vector2 Position, Vector2 Velocity, bool Entering);

	// Handed to INeuralTextureAgent implementers during a step so they can disturb this texture
	// in world space; it owns the world->state transform and the shared per-step stamp buffer.
	public sealed class PerturbContext
	{
		readonly NeuralTextureActor texture;

		internal PerturbContext(NeuralTextureActor texture) => this.texture = texture;

		// The texture being disturbed, so agents can cull against its bounds before stamping.
		public NeuralTextureActor Texture => texture;
		public float PreviousStepTime { get; internal set; } = float.NegativeInfinity;

		// True once the per-step stamp buffer is full; stop adding.
		public bool IsFull => texture.perturbStampCount >= texture.perturbStamps.Length;

		// Stamps a disturbance sweeping from one world point to another, optionally pushing state
		// by a world-space displacement. Returns false once the buffer is full. The stamp type stays
		// private to the actor: agents only ever speak world coordinates.
		public bool AddStamp(Vector2 fromWorld, Vector2 toWorld, float worldRadius, float strength, Vector2 push = default)
		{
			var stamps = texture.perturbStamps;
			if (texture.perturbStampCount >= stamps.Length)
				return false;
			if ((strength > 0 || push != Vector2.Zero) && worldRadius > 0)
				stamps[texture.perturbStampCount++] = new PerturbStamp
				{
					From = texture.WorldToState(fromWorld),
					To = texture.WorldToState(toWorld),
					Radius = MathF.Max(worldRadius * texture.StateScale, 0.5f),
					Strength = strength,
					Push = push * texture.StateScale,
				};
			return texture.perturbStampCount < stamps.Length;
		}
	}

	public static readonly Point2 DefaultSize = new(Game.Width, Game.Height);

	Point2 size = DefaultSize;
	public Point2 Size
	{
		get => size;
		set => ResizeWindow(Point2.Max(value, Point2.One), Point2.Zero);
	}

	[InspectorGroup("Simulation")]
	float simulationScale = 1;
	// Cells per world pixel. Painting is stored independently at one pixel per world pixel.
	[I(0.1f, 4)] public float SimulationScale
	{
		get => simulationScale;
		set
		{
			if (!float.IsFinite(value) || value <= 0)
				throw new ArgumentOutOfRangeException(nameof(value));
			if (simulationScale == value)
				return;
			var oldSize = SimulationSize;
			var oldScale = simulationScale;
			simulationScale = value;
			if (supported)
				ResizeSimulation(oldSize, oldScale, Point2.Zero);
		}
	}
	public Point2 SimulationSize => Point2.Max(Point2.One, (Size * SimulationScale).CeilingToPoint2());
	// Actor-local bounds of one [-1, 1] flow domain; repeated with mirrored edges.
	public Vector2 FlowOrigin;
	public Vector2 FlowSize = new(StateWidth, StateHeight);
	[I(0, 1)] public float FireRate = 0.5f;
	[I(1, 120)] public float StepsPerSecond = 30.0f;
	[I(0, 256)] public int PrewarmSteps = 20;
	public DynamismMode Dynamism = DynamismMode.PaintedMask;
	[I(0.1f, 8)] public float CurlScale = 2f;
	[I(0, 32)] public float CurlSpeed = 6f;
	[I(1, 8)] public float BoundarySharpness = 2f;

	[InspectorGroup("Models & Mask")]
	public ContentRef<NeuralWeightsContent> WeightsFile = NeuralWeightsContent.NeuralTextureDefault;
	public ContentRef<NeuralWeightsContent> WeightsFile1 = new("");
	public ContentRef<NeuralWeightsContent> WeightsFile2 = new("");
	public ContentRef<NeuralWeightsContent> WeightsFile3 = new("");
	// Hides the outermost painted cells, which simulate against partly-empty neighborhoods and
	// decode to an off-palette rim. Measured in simulation cells; see RebuildDecodeMask.
	[I(0, 4)] public int EdgeTrim;

	[InspectorGroup("Appearance")]
	[I(0, 3)] public float Contrast = 1f;
	[I(0, 3)] public float Saturation = 1f;
	[I(-1, 1)] public float Brightness;
	public Color Tint = Color.White;
	public NeuralTextureOpacityMode OpacityMode;
	[I(0, 1)] public float Opacity = 1f;
	[I(0, 4)] public float DensityLow = 0.1f;
	[I(0, 4)] public float DensityHigh = 0.6f;
	public NeuralTextureNormalMode NormalMode;
	[I(0, 4)] public float NormalStrength = 1f;
	[I(0, 4)] public float SilhouetteNormalStrength = 1f;

	[InspectorGroup("Decoder")]
	public NeuralTextureSupersampling DecodeSupersampling = NeuralTextureSupersampling.X1;
	[I(0.1f, 4)] public float DetailScale = 1f;
	public Vector2 DetailPhase;
	public Vector2 DetailOrigin;
	[I(0, 4)] public float PatchFrequency = 1f;
	[I(0, 30)] public float SirenFrequency = 10f;
	[I(0, 3)] public float LatentGain = 1f;
	[I(-2, 2)] public float LatentBias;
	public bool NearestStateSampling;

	[InspectorGroup("Interaction")]
	public bool AutoPerturb = true;
	public NeuralTexturePerturbEffect AutoPerturbEffect;
	public bool ActsAsFluid;
	public bool BuoyantSurface;
	public bool Suffocates;
	public bool PerturbOnFluidCrossing = true;
	[I(0, 1)] public float GameplayDensityThreshold = 0.5f;
	[I(0, 0.5f)] public float GameplayDensityHysteresis = 0.08f;
	[I(0, 1000)] public float Buoyancy = 500f;
	[I(0.1f, 30)] public float SuffocationSeconds = 4f;
	[I(0.1f, 10)] public float SuffocationRecoverySeconds = 1.5f;
	[I(1, 32)] public float FluidCrossingRadius = 12f;
	[I(0, 1)] public float FluidCrossingEraseStrength = 0.02f;
	[I(0, 32)] public float FluidEnterPush = 6f;
	[I(0, 32)] public float FluidExitPush = 3f;

	[InspectorGroup("Shutdown"), ActorRef]
	public string ShutdownSourceActorId = "";
	// A/B toggle for what happens on shutdown. Passive (default): sim rate eases to zero and the
	// field greys out. Active: a strong ripple sweeps out, the painted mask dissolves toward
	// SmokeSlot across the whole rect, and the sim keeps running slow and dim ("just smoke").
	public bool ActiveShutdown;
	// Which model slot the active shutdown dissolves toward. Slot 1 here is the smoke plume.
	[I(0, 3)] public int SmokeSlot = 1;
	// Peak erase strength of the one-shot active-shutdown wave (clamped to 1 in the shader).
	[I(0, 3)] public float ShutdownWaveStrength = 1.5f;

	[InspectorGroup("World")]
	public Factory.TileLayer Layer = Factory.TileLayer.Background;
	public int RenderDepth
	{
		get => Depth;
		set => Depth = value;
	}
	public char MaterialTile;
	public bool CastsMaskShadow;

	// Set before Added() to drive the per-slot blend mask directly (region tiles build a
	// feathered two-model mask from tile geometry) instead of decoding PaintedMask. Length must
	// be StateWidth*StateHeight; each uint packs one byte per slot (R=slot0 … A=slot3).
	public uint[]? InitialMaskCells;

	public override bool CastsSpriteShadow => false;
	public override bool CastsReflection => false;
	public Texture? OutputTexture => supported ? output : null;
	public Texture? OutputNormalTexture => supported && HasNormalOutput ? normalOutput : null;

	readonly UniformBuffer updateUniforms = new();
	readonly UniformBuffer decodeUniforms = new();
	readonly UniformBuffer perturbUniforms = new();
	readonly UniformBuffer curlAdvectionUniforms = new();
	readonly UniformBuffer densityQueryUniforms = new();
	readonly Vector4[] densityReadback = new Vector4[1];
	readonly PerturbStamp[] perturbStamps = new PerturbStamp[MaxPerturbations];
	readonly PerturbContext perturbContext;
	int perturbStampCount;
	readonly Dictionary<Pawn, PawnSample> pawnSamples = [];
	readonly List<Pawn> stalePawnSamples = [];
	NeuralTexturePerturbActor? autoPerturb;
	GraphicsDevice graphicsDevice = null!;
	ComputeStorageBuffer<CellState>? bufferA;
	ComputeStorageBuffer<CellState>? bufferB;
	StorageBuffer<Vector4>? weights;
	StorageBuffer<PerturbStamp>? perturbations;
	Texture? output;
	Texture? normalOutput;
	Texture? silhouetteNormalOutput;
	Shader? updateShader;
	Shader? decodeShader;
	Shader? perturbShader;
	Shader? curlAdvectionShader;
	Shader? densityQueryShader;
	StorageBuffer<uint>? maskBuffer;
	StorageBuffer<uint>? decodeMaskBuffer;
	StorageBuffer<uint>? outputMaskBuffer;
	ComputeStorageBuffer<Vector4>? ownershipA;
	ComputeStorageBuffer<Vector4>? ownershipB;
	ComputeStorageBuffer<uint>? proceduralUpdateMask;
	ComputeStorageBuffer<uint>? proceduralDecodeMask;
	ComputeStorageBuffer<Vector4>? densityProbe;
	BufferDownload<Vector4> densityDownload;
	uint[] maskCells = [];
	uint[] decodeMaskCells = [];
	Vector4[] ownershipCells = [];
	CellState[] emptyState = [];
	Point2 updateOrigin;
	Point2 updateSize;
	readonly List<ShadowSegment> occluderSegments = [];
	ContentRef<BlobContent> maskData = new("");
	Image? paintedImage;
	uint normalSlots;
	bool HasNormalOutput => normalSlots != 0 || NormalMode != NeuralTextureNormalMode.Decoded;
	bool currentIsB;
	bool ownershipCurrentIsB;
	bool supported;
	bool curlAdvectionUnavailable;
	bool settledFromSeed;
	float stepAccumulator;
	int deferredFrames;
	bool decodePending;
	float shutdownTime = -1;
	ActorDestructionTrigger? shutdownTrigger;
	float shutdownStartStepsPerSecond;
	float shutdownStartSaturation;
	float shutdownStartBrightness;
	float smokeBlend;
	float curlTime;
	float densityProbeTimer;
	float suffocation;
	Vector4 latestDensity;
	Player? probedPlayer;
	bool densityReady;
	bool playerSubmerged;
	FluidCrossing? pendingFluidCrossing;
	Vector2 shutdownSourceWorld;
	bool haveShutdownSourceWorld;
	uint seed = 1;
	static float ShutdownDuration => 5;
	static float ShutdownBrightnessDrop => 0.2f;
	// Active shutdown slows the sim to this instead of stopping it dead.
	static float ShutdownSlowStepsPerSecond => 30;
	// Fullscreen smoke fires asynchronously (rate < 1); synchronous updates amplify the sharp
	// edges the player's erase carves into a diverging checkerboard. See EMPTY_DECAY notes.
	static float ShutdownFireRate => 0.5f;
	// How long the active-shutdown wave takes to cross the field.
	static float ShutdownWaveDuration => 3.6f;
	static float DensityProbeInterval => 1f / 20f;
	// True from the moment an active shutdown starts: the smoke fills the whole rect, so the
	// update pass has to dispatch every cell, not just the painted footprint.
	bool SmokeActive => ActiveShutdown && shutdownTime >= 0;

	// Actor-local RGBA slot weights at one pixel per world pixel. Empty fills the window.
	public ContentRef<BlobContent> PaintedMask
	{
		get => maskData;
		set
		{
			maskData = value;
			paintedImage?.Dispose();
			paintedImage = null;
			if (supported)
			{
				DecodeMaskData();
				maskBuffer!.Upload(maskCells);
				RebuildUpdateBounds();
				RebuildDecodeMask();
				ResetProceduralOwnership();
				RebuildSilhouette();
				Decode();
			}
		}
	}

	public NeuralTextureActor()
	{
		Depth = 1000;
		CollidesWithSolids = false;
		Hitbox = new(new RectInt(0, 0, 0, 0));
		perturbContext = new(this);
	}

	public override void Added()
	{
		base.Added();
		if (!string.IsNullOrWhiteSpace(ShutdownSourceActorId))
		{
			shutdownTrigger = new(this, ShutdownSourceActorId);
			if (shutdownTrigger.Triggered)
				BeginShutdown(immediate: true);
		}
		graphicsDevice = Game.Manager.GraphicsDevice;

		var shared = GetSharedResources(graphicsDevice);
		if (shared == null)
			return;

		updateShader = shared.UpdateShader;
		decodeShader = shared.DecodeShader;
		perturbShader = shared.PerturbShader;

		bufferA = new(graphicsDevice, "NeuralTextureStateA");
		bufferB = new(graphicsDevice, "NeuralTextureStateB");
		perturbations = new(graphicsDevice, "NeuralTexturePerturbations");
		maskBuffer = new(graphicsDevice, "NeuralTextureMask");

		(weights, normalSlots) = shared.GetWeights(SlotWeightsFiles);
		emptyState = new CellState[StateCellCount];
		if (InitialMaskCells is { Length: var length } injected && length == StateCellCount)
		{
			maskCells = injected;
			paintedImage = new Image(Size.X, Size.Y);
			for (var y = 0; y < Size.Y; y++)
				for (var x = 0; x < Size.X; x++)
					paintedImage[x, y] = SampleMask(maskCells, OutputToState(new Vector2(x + 0.5f, y + 0.5f)));
		}
		else
			DecodeMaskData();
		RebuildUpdateBounds();
		maskBuffer.Upload(maskCells);
		RebuildDecodeMask();
		if (Dynamism == DynamismMode.ProceduralCurlAdvection)
			EnsureProceduralDynamism();
		output = new(
			graphicsDevice,
			Math.Max(1, Size.X),
			Math.Max(1, Size.Y),
			TextureFormat.Color,
			TextureFlags.ComputeWrite,
			"NeuralTextureOutput");
		// The decode shader statically binds a normal target, so actors without any normal mode get a 1x1 stub.
		normalOutput = new(
			graphicsDevice,
			HasNormalOutput ? Math.Max(1, Size.X) : 1,
			HasNormalOutput ? Math.Max(1, Size.Y) : 1,
			TextureFormat.Color,
			TextureFlags.ComputeWrite,
			"NeuralTextureNormalOutput");
		bufferA.Upload(emptyState);
		bufferB.Upload(emptyState);
		perturbations.Upload(perturbStamps);
		RebuildSilhouette();

		supported = true;
		if (PrewarmSteps > 0)
			SimulateFromSeed(PrewarmSteps);
		else
			Decode();
		if (AutoPerturb)
			autoPerturb = Game.Create<NeuralTexturePerturbActor>(Position, it =>
			{
				it.Size = Size;
				it.Effect = AutoPerturbEffect;
				it.KeepOnRoomTransition = KeepOnRoomTransition;
			});
		Game.EditorPreviewSync += SettleForEditorPreview;
	}

	// Steps run so the editor preview shows a settled texture instead of the near-blank seed
	// decode; gameplay reaches this look on its own by stepping every frame.
	const int EditorPreviewSteps = 95;

#if EDITOR
	// The decoded colour image, before any lighting or normal pass -- what the editor's model
	// thumbnails show.
	internal Texture? PreviewOutput => output;

	StorageBuffer<Vector4>? previewWeights;
	Vector4[]? previewPacked;

	// Re-points slot 0 at another model and settles it, without rebuilding the actor. Packs into a
	// buffer this actor owns so baking the whole catalogue doesn't grow the shared weights cache.
	internal void SetPreviewModel(string fileName, int settleSteps)
	{
		if (!supported)
			return;
		WeightsFile = new(fileName);
		PackPreviewWeights();
		normalSlots = 0; // thumbnails are raw decode colour, so nothing reads the normal channels
		SimulateFromSeed(settleSteps);
	}

	internal void SetEditorPreviewModel(int slot, string fileName)
	{
		if (!supported || slot is < 0 or >= MaxSlots)
			return;
		var current = slot switch
		{
			0 => WeightsFile.Name,
			1 => WeightsFile1.Name,
			2 => WeightsFile2.Name,
			_ => WeightsFile3.Name,
		};
		if (current == fileName)
			return;
		switch (slot)
		{
			case 0: WeightsFile = fileName; break;
			case 1: WeightsFile1 = fileName; break;
			case 2: WeightsFile2 = fileName; break;
			case 3: WeightsFile3 = fileName; break;
		}
		normalSlots = PackPreviewWeights();
		if (HasNormalOutput && normalOutput is { } normal && (normal.Width != Size.X || normal.Height != Size.Y))
		{
			normal.Dispose();
			normalOutput = new(graphicsDevice, Size.X, Size.Y, TextureFormat.Color, TextureFlags.ComputeWrite,
				"NeuralTextureNormalOutput");
		}
		SimulateFromSeed(EditorPreviewSteps);
	}

	uint PackPreviewWeights()
	{
		previewWeights ??= new StorageBuffer<Vector4>(graphicsDevice, "NeuralTexturePreviewWeights");
		previewPacked ??= new Vector4[MaxSlots * PairedWeightCount];
		Array.Clear(previewPacked);
		var packedNormalSlots = SharedGpuResources.PackWeights(SlotWeightsFiles, previewPacked);
		previewWeights.Upload(previewPacked);
		weights = previewWeights;
		return packedNormalSlots;
	}
#endif

	// The editor doesn't tick this actor toward its attractor before rendering the preview, so
	// fast-forward on the sync signal. See Game.EditorPreviewSync / Manager.SyncEditorGame.
	void SettleForEditorPreview()
	{
		if (supported && !settledFromSeed)
			SimulateFromSeed(EditorPreviewSteps);
	}

	public string[] SlotWeightsFiles => [WeightsFile.Name, WeightsFile1.Name, WeightsFile2.Name, WeightsFile3.Name];

	void DecodeMaskData()
	{
		maskCells = new uint[StateCellCount];
		if (paintedImage == null && string.IsNullOrEmpty(maskData.Name))
		{
			Array.Fill(maskCells, 0xFFu); // slot 0 fully painted
			return;
		}
		paintedImage ??= new Image(BlobContent.Read(maskData));
		for (var y = 0; y < SimulationSize.Y; y++)
			for (var x = 0; x < SimulationSize.X; x++)
				maskCells[y * SimulationSize.X + x] = PackMask(SamplePainting((new Vector2(x, y) + new Vector2(0.5f)) / SimulationScale - new Vector2(0.5f)));
	}

	static uint PackMask(Color color) => color.R | ((uint)color.G << 8) | ((uint)color.B << 16) | ((uint)color.A << 24);

	Color SamplePainting(Vector2 point)
	{
		var cell = point.FloorToPoint2();
		var blend = point - cell;
		return Color.Lerp(
			Color.Lerp(Pixel(cell), Pixel(cell + Point2.UnitX), blend.X),
			Color.Lerp(Pixel(cell + Point2.UnitY), Pixel(cell + Point2.One), blend.X), blend.Y);

		Color Pixel(Point2 at) => paintedImage!.Bounds.Contains(at) ? paintedImage[at.X, at.Y] : default;
	}

	Color SampleMask(uint[] cells, Vector2 point)
	{
		var cell = point.FloorToPoint2();
		var blend = point - cell;
		return Color.Lerp(
			Color.Lerp(Pixel(cell), Pixel(cell + Point2.UnitX), blend.X),
			Color.Lerp(Pixel(cell + Point2.UnitY), Pixel(cell + Point2.One), blend.X), blend.Y);

		Color Pixel(Point2 at)
		{
			at = Point2.Min(Point2.Max(at, Point2.Zero), SimulationSize - Point2.One);
			var packed = cells[at.Y * SimulationSize.X + at.X];
			return new Color((byte)packed, (byte)(packed >> 8), (byte)(packed >> 16), (byte)(packed >> 24));
		}
	}

	public string SerializeMask()
	{
		if (paintedImage != null)
			maskData = BlobContent.Write(paintedImage.WritePng(), "png");
		return maskData.Name;
	}

	public Image CreatePaintedMaskPreview()
	{
		if (paintedImage == null && !string.IsNullOrEmpty(maskData.Name))
			paintedImage = new Image(BlobContent.Read(maskData));
		var preview = new Image(Size.X, Size.Y);
		var pixels = preview.Data;
		for (var y = 0; y < Size.Y; y++)
			for (var x = 0; x < Size.X; x++)
			{
				var mask = paintedImage != null && paintedImage.Bounds.Contains(new Point2(x, y))
					? paintedImage[x, y] : new Color(255, 0, 0, 0);
				pixels[y * Size.X + x] = new Color(
					(byte)Math.Min(255, mask.R + mask.A),
					(byte)Math.Min(255, mask.G + mask.A), mask.B, 255);
			}
		return preview;
	}

	public static string ReframeMask(string blob, Point2 newSize, Point2 offset)
	{
		if (string.IsNullOrEmpty(blob))
			return blob;
		using var original = new Image(BlobContent.Read(blob));
		using var cropped = new Image(newSize.X, newSize.Y);
		cropped.CopyPixels(original, original.Bounds, -offset);
		return BlobContent.Write(cropped.WritePng(), "png").Name;
	}

	public void ResizeWindow(Point2 newSize, Point2 offset)
	{
		newSize = Point2.Max(newSize, Point2.One);
		if (newSize == size && offset == Point2.Zero)
			return;
		var oldSize = SimulationSize;
		size = newSize;
		FlowOrigin -= (Vector2)offset;
		DetailOrigin -= (Vector2)offset;
		if (!supported)
			return;
		maskData = ReframeMask(SerializeMask(), newSize, offset);
		paintedImage?.Dispose();
		paintedImage = null;
		ResizeSimulation(oldSize, SimulationScale, offset);
	}

	void ResizeSimulation(Point2 oldSize, float oldScale, Point2 offset)
	{
		var oldA = bufferA!;
		var oldB = bufferB!;
		bufferA = new(graphicsDevice, "NeuralTextureStateA");
		bufferB = new(graphicsDevice, "NeuralTextureStateB");
		emptyState = new CellState[StateCellCount];
		bufferA.Upload(emptyState);
		bufferB.Upload(emptyState);
		var uniforms = new UniformBuffer();
		uniforms.Set(new ResizeParams
		{
			SourceSize = oldSize,
			DestinationSize = SimulationSize,
			Offset = offset * oldScale,
			Scale = oldScale / SimulationScale,
			Padding = 0,
		});
		new ComputeCommand(GetSharedResources(graphicsDevice)!.ResizeShader)
		{
			ReadOnlyStorageBuffers = [currentIsB ? oldB : oldA],
			ReadWriteStorageBuffers = [bufferA],
			UniformBuffers = [uniforms],
			GroupCountX = (SimulationSize.X + 7) / 8,
			GroupCountY = (SimulationSize.Y + 7) / 8,
		}.Submit(graphicsDevice);
		currentIsB = false;
		oldA.Dispose();
		oldB.Dispose();
		DecodeMaskData();
		maskBuffer!.Upload(maskCells);
		RebuildUpdateBounds();
		RebuildDecodeMask();
		ResetProceduralOwnership();
		output?.Dispose();
		normalOutput?.Dispose();
		silhouetteNormalOutput?.Dispose();
		silhouetteNormalOutput = null;
		output = new(graphicsDevice, Size.X, Size.Y, TextureFormat.Color, TextureFlags.ComputeWrite, "NeuralTextureOutput");
		normalOutput = new(graphicsDevice, HasNormalOutput ? Size.X : 1, HasNormalOutput ? Size.Y : 1,
			TextureFormat.Color, TextureFlags.ComputeWrite, "NeuralTextureNormalOutput");
		RebuildSilhouette();
		Decode();
	}

	public void PaintMask(Vector2 worldCenter, float worldRadius, float softness, float strength, int slot)
	{
		if (!supported)
			return;

		if (paintedImage == null || paintedImage.Size != Size)
		{
			var canvas = new Image(Size.X, Size.Y);
			if (paintedImage != null)
				canvas.CopyPixels(paintedImage, paintedImage.Bounds, Point2.Zero);
			else
				canvas.Data.Fill(new Color(255, 0, 0, 0));
			paintedImage?.Dispose();
			paintedImage = canvas;
		}
		var center = worldCenter - Position - new Vector2(0.5f);
		var radius = MathF.Max(worldRadius, 0.5f);
		var falloff = MathF.Max(softness, 0.01f);
		var minX = Math.Max((int)(center.X - radius * 2), 0);
		var maxX = Math.Min((int)(center.X + radius * 2) + 1, Size.X - 1);
		var minY = Math.Max((int)(center.Y - radius * 2), 0);
		var maxY = Math.Min((int)(center.Y + radius * 2) + 1, Size.Y - 1);

		for (var y = minY; y <= maxY; y++)
			for (var x = minX; x <= maxX; x++)
			{
				var distance = (new Vector2(x, y) - center).Length() / radius;
				if (distance >= 1.0f + falloff * 2)
					continue;
				// Flat core with a gaussian skirt whose width is the softness.
				var weight = strength * MathF.Exp(-MathF.Max(distance - (1.0f - falloff), 0.0f) * MathF.Max(distance - (1.0f - falloff), 0.0f) / (2.0f * falloff * falloff));
				if (weight <= 0.001f)
					continue;

				var index = y * Size.X + x;
				var cell = PackMask(paintedImage[index]);
				var next = 0u;
				for (var s = 0; s < MaxSlots; s++)
				{
					var value = (cell >> (s * 8)) & 0xFF;
					var target = s == slot ? 255.0f : 0.0f;
					var blended = (uint)Math.Clamp(value + (target - value) * weight, 0.0f, 255.0f);
					next |= blended << (s * 8);
				}
				paintedImage[index] = new Color((byte)next, (byte)(next >> 8), (byte)(next >> 16), (byte)(next >> 24));
			}

		DecodeMaskData();
		maskBuffer!.Upload(maskCells);
		RebuildUpdateBounds();
		RebuildDecodeMask();
		ResetProceduralOwnership();
		RebuildSilhouette();
		Decode();
	}

	bool IShapeOccluder.CastsShadow => CastsMaskShadow && supported;

	public void AppendOccluderSegments(List<ShadowSegment> segments)
	{
		var offset = (Vector2)Position;
		foreach (var segment in occluderSegments)
			segments.Add(new(segment.A + offset, segment.B + offset));
	}

	// Trace the exact non-zero alpha footprint produced by NeuralTextureDecode. Like DualGrid's
	// mask tracer, neighboring pixel edges are merged into the segment currency used by both
	// point-light and sun shadows.
	void RebuildSilhouette()
	{
		occluderSegments.Clear();
		var width = Math.Max(1, Size.X);
		var height = Math.Max(1, Size.Y);
		var outputMask = new uint[width * height];
		var solid = Trace(true);
		outputMaskBuffer ??= new(graphicsDevice, "NeuralTextureOutputMask");
		outputMaskBuffer.Upload(outputMask);
		// What the painter drew, before EdgeTrim shaved the visible edge back: the bevel below
		// follows this, so trimming hides cells without carving a fresh lit edge into the shape.
		var painted = EdgeTrim <= 0 ? solid : Trace(false);

		for (var y = 0; y < height; y++)
		{
			MergeHorizontal(y, -1, y);
			MergeHorizontal(y, 1, y + 1);
		}
		for (var x = 0; x < width; x++)
		{
			MergeVertical(x, -1, x);
			MergeVertical(x, 1, x + 1);
		}

		var normalPixels = new Color[solid.Length];
		for (var y = 0; y < height; y++)
			for (var x = 0; x < width; x++)
			{
				if (!IsSolid(x, y))
					continue;
				var sx = SolidValue(x - 1, y) - SolidValue(x + 1, y);
				var sy = SolidValue(x, y - 1) - SolidValue(x, y + 1);
				if (sx == 0 && sy == 0)
					continue;
				var normal = Vector3.Normalize(new Vector3(sx, sy, 1) * new Vector3(SilhouetteNormalStrength, SilhouetteNormalStrength, 1)) * 0.5f + Vector3.One * 0.5f;
				normalPixels[y * width + x] = new Color(normal.X, normal.Y, normal.Z, 1);
			}
		if (silhouetteNormalOutput == null)
			silhouetteNormalOutput = new(graphicsDevice, width, height, normalPixels, name: "NeuralTextureSilhouetteNormals");
		else
			silhouetteNormalOutput.SetData<Color>(normalPixels);

		bool[] Trace(bool trim)
		{
			var map = new bool[width * height];
			for (var y = 0; y < height; y++)
				for (var x = 0; x < width; x++)
				{
					var state = OutputToState(new Vector2(x + 0.5f, y + 0.5f));
					var color = paintedImage != null
						? (paintedImage.Bounds.Contains(new Point2(x, y)) ? paintedImage[x, y] : default)
						: SampleMask(maskCells, state);
					if (trim && EdgeTrim > 0)
					{
						var coverage = Coverage(color);
						color *= coverage > 0 ? Math.Min(1, Coverage(SampleMask(decodeMaskCells, state)) / coverage) : 0;
					}
					if (trim)
						outputMask[y * width + x] = PackMask(color);
					map[y * width + x] = Coverage(color) >= 1f / 512f;
				}
			return map;
		}

		static float Coverage(Color color) => (color.R + color.G + color.B + color.A) / 255f;

		bool IsSolid(int x, int y) => x >= 0 && y >= 0 && x < width && y < height && solid[y * width + x];
		// Off-image neighbors count as solid: the image border is where the texture stops, not a
		// silhouette edge, and beveling it stamps a one-pixel rim of normals over whatever the
		// actor is layered against. Only painted silhouettes inside the image get a bevel.
		int SolidValue(int x, int y) => (uint)x >= (uint)width || (uint)y >= (uint)height || painted[y * width + x] ? 1 : 0;

		void MergeHorizontal(int y, int neighborOffset, int edgeY)
		{
			var start = -1;
			for (var x = 0; x <= width; x++)
			{
				var edge = x < width && IsSolid(x, y) && !IsSolid(x, y + neighborOffset);
				if (edge && start < 0)
					start = x;
				else if (!edge && start >= 0)
				{
					occluderSegments.Add(new(new Vector2(start, edgeY), new Vector2(x, edgeY)));
					start = -1;
				}
			}
		}

		void MergeVertical(int x, int neighborOffset, int edgeX)
		{
			var start = -1;
			for (var y = 0; y <= height; y++)
			{
				var edge = y < height && IsSolid(x, y) && !IsSolid(x + neighborOffset, y);
				if (edge && start < 0)
					start = y;
				else if (!edge && start >= 0)
				{
					occluderSegments.Add(new(new Vector2(edgeX, start), new Vector2(edgeX, y)));
					start = -1;
				}
			}
		}
	}

	internal void RenderTileField(Batcher batcher, Func<Point2, Color> colorForTile)
	{
		if (!supported || output == null)
			return;

		var worldBounds = GetVisualBounds();
		var first = (worldBounds.TopLeft / Game.TileSize).FloorToPoint2();
		var last = (worldBounds.BottomRight / Game.TileSize).CeilingToPoint2();
		for (var y = first.Y; y < last.Y; y++)
			for (var x = first.X; x < last.X; x++)
			{
				var worldTile = new Point2(x, y);
				var tileBounds = new Rect((Vector2)worldTile * Game.TileSize, new Vector2(Game.TileSize));
				var intersection = worldBounds.GetIntersection(tileBounds);
				if (intersection.Width <= 0 || intersection.Height <= 0)
					continue;
				var clip = intersection - (Vector2)Position;
				batcher.Image(output, clip, clip.Position, colorForTile(worldTile));
			}
	}

	internal void SimulateFromSeed(int steps)
	{
		if (!supported)
			return;

		bufferA!.Upload(emptyState);
		bufferB!.Upload(emptyState);
		currentIsB = false;
		stepAccumulator = 0;
		seed = 1;
		pawnSamples.Clear();
		long started = 0;
		if (doLog)
			started = Stopwatch.GetTimestamp();
		for (var i = 0; i < steps; i++)
			StepState();
		Decode();
		settledFromSeed = true;
		if (doLog)
			Log.Info($"NCT settle: {steps} steps, {updateSize.X}x{updateSize.Y} of {SimulationSize.X}x{SimulationSize.Y}, {Stopwatch.GetElapsedTime(started).TotalMilliseconds:0.00} ms CPU submit");
	}

	public override void Update()
	{
		base.Update();

		if (shutdownTrigger != null)
		{
			// Cache the source's position while it still exists; it's gone by the time the
			// destruction trigger fires, and the active-shutdown wave originates from it.
			if (shutdownTime < 0 && Game.GetActorById(ShutdownSourceActorId) is { } source)
			{
				shutdownSourceWorld = source.GetVisualBounds().Center;
				haveShutdownSourceWorld = true;
			}
			if (shutdownTrigger.Update())
				BeginShutdown();
		}

		if (autoPerturb != null)
		{
			autoPerturb.Position = Position;
			autoPerturb.Size = Size;
			autoPerturb.KeepOnRoomTransition = KeepOnRoomTransition;
		}
		decodePending |= UpdateShutdown();
		if (supported)
			UpdateDensityProbe();
	}

	static readonly List<NeuralTextureActor> dueTextures = [];
	static readonly List<Pawn> perturbingPawns = [];
	static readonly List<NeuralTexturePerturbActor> pawnPerturbers = [];
	static readonly List<INeuralTextureAgent> perturbingAgents = [];
	static float DecodeViewMargin => Game.TileSize * 4;

	// Runs before actor updates, on last frame's actor state. A cell budget spreads due steps
	// across frames so textures sharing a rate don't all land on one frame, and the recorded work
	// is submitted right away so the GPU simulates while the CPU updates actors.
	internal static void StepAll(Game game)
	{
		var delta = game.Time.Delta;
		var expectedCost = 0f;
		var largestCost = 0;
		dueTextures.Clear();
		foreach (var actor in game.Actors)
		{
			if (actor is not NeuralTextureActor { supported: true, StepsPerSecond: > 0 } texture)
				continue;
			var interval = 1 / texture.StepsPerSecond;
			texture.stepAccumulator = MathF.Min(texture.stepAccumulator + delta, interval);
			expectedCost += texture.StepCost * MathF.Min(1, delta / interval);
			largestCost = Math.Max(largestCost, texture.StepCost);
			if (texture.stepAccumulator >= interval)
				dueTextures.Add(texture);
		}

		if (dueTextures.Count > 0)
		{
			GatherPerturbSources(game);
			dueTextures.Sort(static (a, b) => b.deferredFrames - a.deferredFrames);
			var budget = expectedCost + largestCost / 2f;
			var spent = 0f;
			foreach (var texture in dueTextures)
			{
				if (spent > 0 && spent + texture.StepCost > budget)
				{
					texture.deferredFrames++;
					continue;
				}
				spent += texture.StepCost;
				texture.deferredFrames = 0;
				texture.stepAccumulator -= 1 / texture.StepsPerSecond;
				texture.Step();
			}
		}

		if (DecodeVisible(game.Actors, new Rect(game.Camera, new Vector2(Game.Width, Game.Height) / game.zoomLevel)) || dueTextures.Count > 0)
			game.Manager.GraphicsDevice.SubmitPendingCommands();
	}

	// Offscreen textures keep simulating but only decode once a view overlaps them. Rendering calls
	// this with its own view too, so a texture the camera reaches mid-frame still draws current state.
	internal static bool DecodeVisible(List<Actor> actors, Rect view)
	{
		view = view.Inflate(DecodeViewMargin);
		var decoded = false;
		foreach (var actor in actors)
			if (actor is NeuralTextureActor { supported: true, decodePending: true } texture && texture.GetVisualBounds().Overlaps(view))
			{
				texture.Decode();
				decoded = true;
			}
		return decoded;
	}

	static void GatherPerturbSources(Game game)
	{
		perturbingPawns.Clear();
		pawnPerturbers.Clear();
		perturbingAgents.Clear();
		var players = 0;
		foreach (var actor in game.Actors)
		{
			if (actor is NeuralTexturePerturbActor { Mode: NeuralTexturePerturbMode.Pawns } perturber)
				pawnPerturbers.Add(perturber);
			if (game.destroying.Contains(actor))
				continue;
			if (actor is Pawn pawn)
				perturbingPawns.Add(pawn);
			// Player attacks take priority over ambient agents when a texture's stamp buffer fills.
			if (actor is Player player)
				perturbingAgents.Insert(players++, player);
			else if (actor is INeuralTextureAgent agent)
				perturbingAgents.Add(agent);
		}
	}

	int StepCost
	{
		get
		{
			var cells = SmokeActive ? SimulationSize : updateSize;
			return cells.X * cells.Y + Size.X * Size.Y;
		}
	}

	void UpdateDensityProbe()
	{
		if (!ActsAsFluid && !BuoyantSurface && !Suffocates)
			return;

		if (densityDownload.TryRead(densityReadback))
		{
			latestDensity = densityReadback[0];
			densityReady = true;
		}

		densityProbeTimer -= Time.Delta;
		if (densityProbeTimer > 0)
			return;
		densityProbeTimer = DensityProbeInterval;

		probedPlayer = null;
		foreach (var actor in Game.Actors)
			if (actor is Player player && GetVisualBounds().Overlaps(player.Hitbox.Rect + player.Position))
			{
				probedPlayer = player;
				break;
			}
		if (probedPlayer == null)
		{
			latestDensity = Vector4.Zero;
			densityReady = false;
			return;
		}

		densityQueryShader ??= GetDensityQueryShader(graphicsDevice);
		if (densityQueryShader == null)
			return;
		if (densityProbe == null)
		{
			densityProbe = new(graphicsDevice, "NeuralTextureDensityProbe");
			densityProbe.Upload([Vector4.Zero]);
		}

		var bounds = probedPlayer.Hitbox.Rect + probedPlayer.Position;
		var x = bounds.CenterX;
		densityQueryUniforms.Set(new DensityQueryParams
		{
			Width = (uint)SimulationSize.X,
			Height = (uint)SimulationSize.Y,
			DensityLow = DensityLow,
			DensityHigh = DensityHigh,
			SmokeBlend = smokeBlend,
			Padding0 = 0,
			Padding1 = 0,
			Padding2 = 0,
			Head = WorldToState(new Vector2(x, bounds.Top + 1)),
			Torso = WorldToState(bounds.Center),
			Feet = WorldToState(new Vector2(x, bounds.Bottom - 1)),
			BelowFeet = WorldToState(new Vector2(x, bounds.Bottom + 3)),
		});

		var source = currentIsB ? bufferB! : bufferA!;
		new ComputeCommand(densityQueryShader)
		{
			ReadOnlyStorageBuffers = [source, ActiveDecodeMask],
			ReadWriteStorageBuffers = [densityProbe],
			UniformBuffers = [densityQueryUniforms],
			GroupCountX = 1,
			GroupCountY = 1,
		}.Submit(graphicsDevice);
		densityDownload = densityProbe.RequestDownload(0, 1, densityDownload);
	}

	internal void ApplyGameplay(Player player, ref FluidContact contact, ref bool headSubmerged)
	{
		var overlaps = probedPlayer == player && GetVisualBounds().Overlaps(player.Hitbox.Rect + player.Position);
		var threshold = Calc.Clamp(GameplayDensityThreshold, 0, 0.999f);
		var fluidThreshold = MathF.Max(0, threshold - (playerSubmerged ? GameplayDensityHysteresis : 0));
		var wasSubmerged = playerSubmerged;
		playerSubmerged = ActsAsFluid && densityReady && overlaps && latestDensity.Y >= fluidThreshold;
		if (playerSubmerged)
		{
			contact |= FluidContact.NeuralTexture;
			headSubmerged |= latestDensity.X >= fluidThreshold;
		}
		if (PerturbOnFluidCrossing && playerSubmerged != wasSubmerged)
			pendingFluidCrossing = new((player.Hitbox.Rect + player.Position).Center, player.Velocity, playerSubmerged);
		if (BuoyantSurface && densityReady && overlaps && latestDensity.W >= threshold)
			player.ApplyNeuralBuoyancy(Buoyancy * Calc.ClampedMap(latestDensity.W, threshold, 1, 0, 1));

		var suffocating = Suffocates && densityReady && overlaps && latestDensity.X >= threshold;
		var rate = suffocating ? 1f / MathF.Max(SuffocationSeconds, 0.1f) : -1f / MathF.Max(SuffocationRecoverySeconds, 0.1f);
		suffocation = Calc.Clamp(suffocation + rate * Time.Delta, 0, 1);
		if (suffocation >= 1 && player.HurtFromEnvironment(this))
			suffocation = 0;
	}

	internal void BeginShutdown(bool immediate = false)
	{
		if (shutdownTime >= 0)
			return;
		shutdownStartStepsPerSecond = StepsPerSecond;
		shutdownStartSaturation = Saturation;
		shutdownStartBrightness = Brightness;
		shutdownTime = immediate ? ShutdownDuration : 0;
		ApplyShutdown();
	}

	bool UpdateShutdown()
	{
		if (shutdownTime < 0 || shutdownTime >= ShutdownDuration)
			return false;
		shutdownTime = MathF.Min(shutdownTime + Time.Delta, ShutdownDuration);
		ApplyShutdown();
		return true;
	}

	void ApplyShutdown()
	{
		var progress = Ease.Cube.InOut(shutdownTime / ShutdownDuration);
		var dimmed = float.Lerp(shutdownStartBrightness,
			MathF.Max(-1, shutdownStartBrightness - ShutdownBrightnessDrop), progress);
		if (ActiveShutdown)
		{
			// Slow but alive; dissolve to smoke everywhere; keep the color, just dim it.
			StepsPerSecond = float.Lerp(shutdownStartStepsPerSecond,
				MathF.Min(shutdownStartStepsPerSecond, ShutdownSlowStepsPerSecond), progress);
			smokeBlend = progress;
			Brightness = dimmed;
			return;
		}
		StepsPerSecond = float.Lerp(shutdownStartStepsPerSecond, 0, progress);
		Saturation = float.Lerp(shutdownStartSaturation, 0, progress);
		Brightness = dimmed;
	}

	void Step()
	{
		ApplyPerturbations();
		AdvanceDynamism();
		StepState();
		decodePending = true;
	}

	void StepState()
	{
		// Smoke fills the rect, so the painted footprint no longer bounds the update.
		var origin = SmokeActive ? Point2.Zero : updateOrigin;
		var size = SmokeActive ? SimulationSize : updateSize;
		if (size.X <= 0 || size.Y <= 0)
			return;

		var source = currentIsB ? bufferB! : bufferA!;
		var destination = currentIsB ? bufferA! : bufferB!;
		updateUniforms.Set(new UpdateParams
		{
			Width = (uint)SimulationSize.X,
			Height = (uint)SimulationSize.Y,
			Seed = seed++,
			FireRate = float.Lerp(FireRate, ShutdownFireRate, smokeBlend),
			OriginX = (uint)origin.X,
			OriginY = (uint)origin.Y,
			DispatchWidth = (uint)size.X,
			DispatchHeight = (uint)size.Y,
			SimulationScale = SimulationScale,
			FlowOrigin = FlowOrigin,
			FlowSize = Vector2.Max(FlowSize, Vector2.One),
			SmokeBlend = smokeBlend,
			SmokeSlot = (uint)SmokeSlot,
			Pad0 = 0,
		});

		new ComputeCommand(updateShader!)
		{
			ReadOnlyStorageBuffers = [source, weights!, ActiveUpdateMask],
			ReadWriteStorageBuffers = [destination],
			UniformBuffers = [updateUniforms],
			GroupCountX = (size.X + 7) / 8,
			GroupCountY = (size.Y + 7) / 8,
		}.Submit(graphicsDevice);

		currentIsB = !currentIsB;
	}

	void RebuildUpdateBounds()
	{
		var min = SimulationSize;
		var max = new Point2(-1, -1);
		for (var y = 0; y < SimulationSize.Y; y++)
			for (var x = 0; x < SimulationSize.X; x++)
				if (maskCells[y * SimulationSize.X + x] != 0)
				{
					min = Point2.Min(min, new(x, y));
					max = Point2.Max(max, new(x, y));
				}

		updateOrigin = min;
		updateSize = max.X >= min.X && max.Y >= min.Y ? max - min + Point2.One : Point2.Zero;
	}

	// A cell painted next to empty space steps against a partly-empty neighborhood, so it settles
	// somewhere the model never trained and decodes as a rim one cell wide. EdgeTrim hides those
	// cells from the decode and the silhouette while the simulation keeps running on the authored
	// mask, so the newly outermost visible cell is one that settled with full neighbors.
	void RebuildDecodeMask()
	{
		decodeMaskCells = maskCells;
		if (EdgeTrim <= 0)
		{
			decodeMaskBuffer?.Dispose();
			decodeMaskBuffer = null;
			return;
		}

		var (width, height) = (SimulationSize.X, SimulationSize.Y);
		// Separable erosion: a cell survives only if every cell within EdgeTrim of it is painted,
		// and off-grid counts as unpainted so the trim eats inward from the image border too.
		var rowKept = new bool[maskCells.Length];
		for (var y = 0; y < height; y++)
			for (var x = 0; x < width; x++)
			{
				var kept = true;
				for (var dx = -EdgeTrim; dx <= EdgeTrim && kept; dx++)
					kept = (uint)(x + dx) < (uint)width && maskCells[y * width + x + dx] != 0;
				rowKept[y * width + x] = kept;
			}

		decodeMaskCells = new uint[maskCells.Length];
		for (var y = 0; y < height; y++)
			for (var x = 0; x < width; x++)
			{
				var kept = true;
				for (var dy = -EdgeTrim; dy <= EdgeTrim && kept; dy++)
					kept = (uint)(y + dy) < (uint)height && rowKept[(y + dy) * width + x];
				if (kept)
					decodeMaskCells[y * width + x] = maskCells[y * width + x];
			}

		decodeMaskBuffer ??= new(graphicsDevice, "NeuralTextureDecodeMask");
		decodeMaskBuffer.Upload(decodeMaskCells);
	}

	StorageBuffer ActiveUpdateMask
		=> Dynamism == DynamismMode.ProceduralCurlAdvection && proceduralUpdateMask != null
			? proceduralUpdateMask
			: maskBuffer!;

	StorageBuffer ActiveDecodeMask
		=> Dynamism == DynamismMode.ProceduralCurlAdvection && proceduralDecodeMask != null
			? proceduralDecodeMask
			: decodeMaskBuffer ?? maskBuffer!;

	bool EnsureProceduralDynamism()
	{
		if (ownershipA != null)
			return true;
		if (curlAdvectionUnavailable)
			return false;

		curlAdvectionShader = GetCurlAdvectionShader(graphicsDevice);
		if (curlAdvectionShader == null)
		{
			curlAdvectionUnavailable = true;
			Log.Warning("Procedural neural-texture mask unavailable; using the painted mask.");
			return false;
		}

		ownershipA = new(graphicsDevice, "NeuralTextureOwnershipA");
		ownershipB = new(graphicsDevice, "NeuralTextureOwnershipB");
		proceduralUpdateMask = new(graphicsDevice, "NeuralTextureProceduralUpdateMask");
		proceduralDecodeMask = new(graphicsDevice, "NeuralTextureProceduralDecodeMask");
		ResetProceduralOwnership();
		return true;
	}

	void ResetProceduralOwnership()
	{
		if (ownershipA == null)
			return;
		if (ownershipCells.Length != maskCells.Length)
			ownershipCells = new Vector4[maskCells.Length];

		for (var i = 0; i < maskCells.Length; i++)
		{
			var packed = maskCells[i];
			var ownership = new Vector4(
				packed & 0xFF,
				(packed >> 8) & 0xFF,
				(packed >> 16) & 0xFF,
				packed >> 24);
			var total = Vector4.Dot(ownership, Vector4.One);
			ownershipCells[i] = total > 0 ? ownership / total : Vector4.Zero;
		}

		ownershipA.Upload(ownershipCells);
		ownershipB!.Upload(ownershipCells);
		proceduralUpdateMask!.Upload(maskCells);
		proceduralDecodeMask!.Upload(decodeMaskCells);
		ownershipCurrentIsB = false;
		curlTime = 0;
	}

	void AdvanceDynamism()
	{
		if (Dynamism != DynamismMode.ProceduralCurlAdvection || !EnsureProceduralDynamism())
			return;

		var deltaTime = 1f / StepsPerSecond;
		curlAdvectionUniforms.Set(new CurlAdvectionParams
		{
			Width = (uint)SimulationSize.X,
			Height = (uint)SimulationSize.Y,
			Time = curlTime,
			DeltaTime = deltaTime,
			CurlScale = CurlScale,
			CurlSpeed = CurlSpeed,
			BoundarySharpness = BoundarySharpness,
			Padding = 0,
		});

		var source = ownershipCurrentIsB ? ownershipB! : ownershipA!;
		var destination = ownershipCurrentIsB ? ownershipA! : ownershipB!;
		new ComputeCommand(curlAdvectionShader!)
		{
			ReadOnlyStorageBuffers = [source, maskBuffer!, decodeMaskBuffer ?? maskBuffer!],
			ReadWriteStorageBuffers = [destination, proceduralUpdateMask!, proceduralDecodeMask!],
			UniformBuffers = [curlAdvectionUniforms],
			GroupCountX = (SimulationSize.X + 7) / 8,
			GroupCountY = (SimulationSize.Y + 7) / 8,
		}.Submit(graphicsDevice);
		ownershipCurrentIsB = !ownershipCurrentIsB;
		curlTime += deltaTime;
	}

	void ApplyPerturbations()
	{
		perturbStampCount = 0;
		ApplyPendingFluidCrossing();
		var now = Time.SecondsF;
		if (float.IsNegativeInfinity(perturbContext.PreviousStepTime))
			perturbContext.PreviousStepTime = now - Time.Delta;
		var textureBounds = GetVisualBounds();

		foreach (var pawn in perturbingPawns)
		{
			var position = pawn.Position;
			var previous = pawnSamples.GetValueOrDefault(pawn, new(position, now));
			var movement = (Vector2)(position - previous.Position);
			var moved = movement.LengthSquared() > 0.001f;
			var elapsed = MathF.Max(now - previous.Time, 0.0001f);
			var speed = movement.Length() / elapsed;
			var currentBounds = pawn.Hitbox.Rect + position;
			var previousBounds = currentBounds + previous.Position - position;

			foreach (var perturb in pawnPerturbers)
			{
				if (perturbContext.IsFull)
					break;
				if (!perturb.WorldRect.Overlaps(textureBounds) ||
					((perturb.Effect == NeuralTexturePerturbEffect.Wake || !perturb.DisturbStationary) && !moved))
					continue;

				var radius = MathF.Max(0, perturb.Radius);
				if (!currentBounds.Conflate(previousBounds).Inflate((int)MathF.Ceiling(radius)).Overlaps(perturb.WorldRect))
					continue;

				var wake = perturb.Effect == NeuralTexturePerturbEffect.Wake;
				var strength = MathF.Max(0, wake ? perturb.WakeEraseStrength : perturb.Strength);
				if (!wake && perturb.ScaleStrengthWithSpeed && perturb.SpeedForDoubleStrength > 0)
					strength *= 1 + speed / perturb.SpeedForDoubleStrength;
				var push = wake ? Calc.Approach(Vector2.Zero, movement * perturb.WakePush, radius) : Vector2.Zero;

				var centerOffset = (Vector2)pawn.Hitbox.Rect.Center;
				perturbContext.AddStamp(
					(Vector2)previous.Position + centerOffset,
					(Vector2)position + centerOffset,
					radius,
					strength,
					push);
			}

			pawnSamples[pawn] = new(position, now);
		}

		RemoveStalePawnSamples();

		// Any actor can disturb the field it overlaps by implementing INeuralTextureAgent; the
		// context maps its world-space stamps into this texture's state grid.
		foreach (var agent in perturbingAgents)
		{
			if (perturbContext.IsFull)
				break;
			agent.PerturbNeuralTexture(perturbContext);
		}

		EmitShutdownWave();
		perturbContext.PreviousStepTime = now;

		var stampCount = perturbStampCount;
		if (stampCount == 0)
			return;

		perturbations!.Upload(perturbStamps.AsSpan(0, stampCount));
		perturbUniforms.Set(new PerturbParams
		{
			Width = (uint)SimulationSize.X,
			Height = (uint)SimulationSize.Y,
			StampCount = (uint)stampCount,
			Padding = 0,
		});

		var source = currentIsB ? bufferB! : bufferA!;
		var destination = currentIsB ? bufferA! : bufferB!;
		new ComputeCommand(perturbShader!)
		{
			ReadOnlyStorageBuffers = [source, perturbations],
			ReadWriteStorageBuffers = [destination],
			UniformBuffers = [perturbUniforms],
			GroupCountX = (SimulationSize.X + 7) / 8,
			GroupCountY = (SimulationSize.Y + 7) / 8,
		}.Submit(graphicsDevice);
		currentIsB = !currentIsB;
	}

	void ApplyPendingFluidCrossing()
	{
		if (pendingFluidCrossing is not { } crossing)
			return;
		pendingFluidCrossing = null;

		var speed = crossing.Velocity.Length();
		var direction = speed > 0.001f ? crossing.Velocity / speed : Vector2.UnitY;
		var halfSweep = direction * (FluidCrossingRadius * 0.5f);
		var push = direction * (crossing.Entering ? FluidEnterPush : FluidExitPush);
		perturbContext.AddStamp(crossing.Position - halfSweep, crossing.Position + halfSweep,
			FluidCrossingRadius, FluidCrossingEraseStrength, push);
	}

	// One expanding ring that sweeps outward from the destroyed source (the duck) as the active
	// shutdown begins, at ShutdownWaveStrength; the CA heals behind it so steps read as a wave.
	void EmitShutdownWave()
	{
		if (!SmokeActive || shutdownTime >= ShutdownWaveDuration)
			return;
		var bounds = GetVisualBounds();
		var center = haveShutdownSourceWorld ? shutdownSourceWorld : bounds.Center;
		// Reach the farthest corner so the wave crosses the whole field even off-center.
		var tl = bounds.TopLeft;
		var br = bounds.BottomRight;
		var maxRadius = MathF.Max(
			MathF.Max(Vector2.Distance(center, tl), Vector2.Distance(center, br)),
			MathF.Max(Vector2.Distance(center, new Vector2(br.X, tl.Y)), Vector2.Distance(center, new Vector2(tl.X, br.Y))));
		var radius = shutdownTime / ShutdownWaveDuration * maxRadius;
		NeuralTextureRippleActor.PerturbRing(perturbContext, center, radius, maxRadius,
			ShutdownWaveStrength, thickness: 6f, stampCount: 24);
	}

	int StateCellCount => SimulationSize.X * SimulationSize.Y;

	public float StateScale => SimulationScale;

	Vector2 WorldToState(Vector2 world)
		=> OutputToState(world - (Vector2)Position);

	Vector2 OutputToState(Vector2 output)
		=> output * SimulationScale - new Vector2(0.5f);

	void RemoveStalePawnSamples()
	{
		stalePawnSamples.Clear();
		foreach (var pawn in pawnSamples.Keys)
			if (!perturbingPawns.Contains(pawn))
				stalePawnSamples.Add(pawn);
		foreach (var pawn in stalePawnSamples)
			pawnSamples.Remove(pawn);
	}

	void Decode()
	{
		decodePending = false;
		var source = currentIsB ? bufferB! : bufferA!;
		decodeUniforms.Set(new DecodeParams
		{
			Width = (uint)SimulationSize.X,
			Height = (uint)SimulationSize.Y,
			OutputWidth = (uint)output!.Width,
			OutputHeight = (uint)output.Height,
			NormalSlots = normalSlots,
			Contrast = Contrast,
			Saturation = Saturation,
			Brightness = Brightness,
			Tint = Tint.ToVector4(),
			NormalStrength = NormalStrength,
			DetailScale = DetailScale,
			PatchFrequency = PatchFrequency,
			SirenFrequency = SirenFrequency,
			DetailPhase = DetailPhase,
			LatentGain = LatentGain,
			LatentBias = LatentBias,
			NearestStateSampling = NearestStateSampling ? 1u : 0u,
			SimulationScale = SimulationScale,
			SmokeBlend = smokeBlend,
			SmokeSlot = (uint)SmokeSlot,
			OpacityMode = (uint)OpacityMode,
			Opacity = Opacity,
			DensityLow = DensityLow,
			DensityHigh = DensityHigh,
			NormalMode = (uint)NormalMode,
			ProceduralMask = Dynamism == DynamismMode.ProceduralCurlAdvection && proceduralDecodeMask != null ? 1u : 0u,
			DetailOrigin = DetailOrigin,
			DecodeSupersampling = (uint)DecodeSupersampling,
			Padding0 = 0,
			Padding1 = 0,
			Padding2 = 0,
		});

		new ComputeCommand(decodeShader!)
		{
			ReadOnlyStorageBuffers = [source, weights!, ActiveDecodeMask, outputMaskBuffer!],
			ReadWriteStorageTextures = [output, normalOutput!],
			UniformBuffers = [decodeUniforms],
			GroupCountX = (output.Width + 7) / 8,
			GroupCountY = (output.Height + 7) / 8,
		}.Submit(graphicsDevice);
	}

	public override void Render(Batcher batcher)
	{
		if (supported && output != null)
			batcher.Image(output, Vector2.Zero, Color.White);
	}

	public override void RenderNormals(Batcher batcher)
	{
		if (supported && silhouetteNormalOutput != null)
			batcher.Image(silhouetteNormalOutput, Vector2.Zero, Color.White);
		if (supported && HasNormalOutput && normalOutput != null)
			batcher.Image(normalOutput, Vector2.Zero, Color.White);
	}

	public override Rect GetVisualBounds()
		=> new(Position.X, Position.Y, Size.X, Size.Y);

	public override void Destroyed()
	{
		base.Destroyed();
		paintedImage?.Dispose();
		if (autoPerturb != null && !Game.destroying.Contains(autoPerturb))
			Game.Destroy(autoPerturb);
		Game.EditorPreviewSync -= SettleForEditorPreview;
		bufferA?.Dispose();
		bufferB?.Dispose();
		perturbations?.Dispose();
		maskBuffer?.Dispose();
		decodeMaskBuffer?.Dispose();
		outputMaskBuffer?.Dispose();
		ownershipA?.Dispose();
		ownershipB?.Dispose();
		proceduralUpdateMask?.Dispose();
		proceduralDecodeMask?.Dispose();
		densityProbe?.Dispose();
#if EDITOR
		previewWeights?.Dispose();
#endif
		output?.Dispose();
		normalOutput?.Dispose();
		silhouetteNormalOutput?.Dispose();
	}
}
