using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Foster.Framework;

namespace Myth;

[GameActor(Sprite = "Editor/neural_texture_actor", BoundsW = Game.TileSize * 4, BoundsH = Game.TileSize * 3,
	Anchor = SpriteAnchor.TopLeft, LivePositionEditable = true)]
public class NeuralTextureActor : Actor, IResizable, IShapeOccluder
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
	public const string DefaultWeightsFile = "chequered_0050_circular.bin";
	static readonly ConditionalWeakTable<GraphicsDevice, SharedGpuResources> SharedResources = new();

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
		if (updateCode == null || decodeCode == null || perturbCode == null)
			return null;

		return SharedResources.GetValue(graphicsDevice, _ => new(graphicsDevice, updateCode, decodeCode, perturbCode));
	}

	// Pre-builds the shared compute pipelines so the first neural room doesn't stall. Safe to
	// call before any NeuralTextureActor exists; a no-op if already warmed. See ShaderWarmup.
	public static void WarmPipelines(GraphicsDevice graphicsDevice) => GetSharedResources(graphicsDevice);

	sealed class SharedGpuResources
	{
		public readonly Shader UpdateShader;
		public readonly Shader DecodeShader;
		public readonly Shader PerturbShader;
		readonly GraphicsDevice graphicsDevice;
		readonly Dictionary<string, (StorageBuffer<Vector4> Buffer, uint NormalSlots)> weights = [];

		public SharedGpuResources(GraphicsDevice graphicsDevice, byte[] updateCode, byte[] decodeCode, byte[] perturbCode)
		{
			this.graphicsDevice = graphicsDevice;
			UpdateShader = new(
				graphicsDevice, ShaderStage.Compute, updateCode,
				uniformBufferCount: 1, readOnlyStorageBufferCount: 3, readWriteStorageBufferCount: 1,
				threadCountX: 8, threadCountY: 8, threadCountZ: 1,
				entryPoint: "compute_main", name: "NeuralTextureUpdate");
			DecodeShader = new(
				graphicsDevice, ShaderStage.Compute, decodeCode,
				uniformBufferCount: 1, readOnlyStorageBufferCount: 3, readWriteStorageTextureCount: 2,
				threadCountX: 8, threadCountY: 8, threadCountZ: 1,
				entryPoint: "compute_main", name: "NeuralTextureDecode");
			PerturbShader = new(
				graphicsDevice, ShaderStage.Compute, perturbCode,
				uniformBufferCount: 1, readOnlyStorageBufferCount: 2, readWriteStorageBufferCount: 1,
				threadCountX: 8, threadCountY: 8, threadCountZ: 1,
				entryPoint: "compute_main", name: "NeuralTexturePerturb");
		}

		public (StorageBuffer<Vector4> Buffer, uint NormalSlots) GetWeights(string[] files)
		{
			var key = string.Join('\n', files);
			if (weights.TryGetValue(key, out var cached))
				return cached;

			var packed = new Vector4[MaxSlots * PairedWeightCount];
			var normalSlots = 0u;
			for (var slot = 0; slot < MaxSlots; slot++)
			{
				if (string.IsNullOrEmpty(files[slot]))
					continue;
				var bytes = Assets.Content.ReadAllBytes($"{WeightsFolder}/{files[slot]}");
				var values = MemoryMarshal.Cast<byte, Vector4>(bytes.AsSpan());
				var dest = packed.AsSpan(slot * PairedWeightCount, PairedWeightCount);
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

			var buffer = new StorageBuffer<Vector4>(graphicsDevice, $"NeuralTextureWeights:{key}");
			buffer.Upload(packed);
			return weights[key] = (buffer, normalSlots);
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
		public Vector2 WorldStateOrigin;
		public float StatePixelsPerCell;
		public uint Padding;
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
		public float StatePixelsPerCell;
		public uint Pad0, Pad1;
	}

	[StructLayout(LayoutKind.Sequential)]
	struct PerturbStamp
	{
		public Vector2 From;
		public Vector2 To;
		public float Radius;
		public float Strength;
	}

	struct PerturbParams
	{
		public uint Width;
		public uint Height;
		public uint StampCount;
		public uint Padding;
	}

	readonly record struct PawnSample(Point2 Position, float Time);

	// Handed to INeuralTextureAgent implementers during a step so they can disturb this texture
	// in world space; it owns the world->state transform and the shared per-step stamp buffer.
	public sealed class PerturbContext
	{
		readonly NeuralTextureActor texture;

		internal PerturbContext(NeuralTextureActor texture) => this.texture = texture;

		// The texture being disturbed, so agents can cull against its bounds before stamping.
		public NeuralTextureActor Texture => texture;

		// True once the per-step stamp buffer is full; stop adding.
		public bool IsFull => texture.perturbStampCount >= texture.perturbStamps.Length;

		// Stamps a disturbance sweeping from one world point to another. A zero/negative strength
		// or radius is ignored. Returns false once the buffer is full. The stamp type stays
		// private to the actor: agents only ever speak world coordinates.
		public bool AddStamp(Vector2 fromWorld, Vector2 toWorld, float worldRadius, float strength)
		{
			var stamps = texture.perturbStamps;
			if (texture.perturbStampCount >= stamps.Length)
				return false;
			if (strength > 0 && worldRadius > 0)
				stamps[texture.perturbStampCount++] = new PerturbStamp
				{
					From = texture.WorldToState(fromWorld),
					To = texture.WorldToState(toWorld),
					Radius = MathF.Max(worldRadius * texture.StateScale, 0.5f),
					Strength = strength,
				};
			return texture.perturbStampCount < stamps.Length;
		}
	}

	public static readonly Point2 DefaultSize = new(Game.Width, Game.Height);

	public Point2 Size { get; set; } = DefaultSize;
	// Simulation size is independent from output Size. Standalone actors retain the trained
	// 128x128 state by default; generated tile regions choose dimensions from FeatureScale.
	public Point2 SimulationSize = new(StateWidth, StateHeight);
	// Zero preserves the classic fit-to-output mapping. Positive values use a fixed world scale.
	[I(0, 8)] public float StatePixelsPerCell;
	public string WeightsFile = DefaultWeightsFile;
	public string WeightsFile1 = "";
	public string WeightsFile2 = "";
	public string WeightsFile3 = "";
	[I(0, 1)] public float FireRate = 0.5f;
	[I(1, 120)] public float StepsPerSecond = 30.0f;
	[I(0, 3)] public float Contrast = 1f;
	[I(0, 3)] public float Saturation = 1f;
	[I(-1, 1)] public float Brightness;
	public Color Tint = Color.White;
	[I(0, 4)] public float NormalStrength = 1f;
	[I(0, 4)] public float SilhouetteNormalStrength = 1f;
	// Hides the outermost painted cells, which simulate against partly-empty neighborhoods and
	// decode to an off-palette rim. Measured in simulation cells; see RebuildDecodeMask.
	[I(0, 4)] public int EdgeTrim;
	[I(0.1f, 4)] public float DetailScale = 1f;
	public Vector2 DetailPhase;
	[I(0, 4)] public float PatchFrequency = 1f;
	[I(0, 30)] public float SirenFrequency = 10f;
	[I(0, 3)] public float LatentGain = 1f;
	[I(-2, 2)] public float LatentBias;
	public bool NearestStateSampling;
	public Factory.TileLayer Layer = Factory.TileLayer.Background;
	public char MaterialTile;
	public bool CastsMaskShadow;

	// Set before Added() to drive the per-slot blend mask directly (region tiles build a
	// feathered two-model mask from tile geometry) instead of decoding PaintedMask. Length must
	// be StateWidth*StateHeight; each uint packs one byte per slot (R=slot0 … A=slot3).
	public uint[]? InitialMaskCells;

	public override bool CastsSpriteShadow => false;
	public override bool CastsReflection => false;
	public Texture? OutputTexture => supported ? output : null;
	public Texture? OutputNormalTexture => supported && normalSlots != 0 ? normalOutput : null;

	readonly UniformBuffer updateUniforms = new();
	readonly UniformBuffer decodeUniforms = new();
	readonly UniformBuffer perturbUniforms = new();
	readonly PerturbStamp[] perturbStamps = new PerturbStamp[MaxPerturbations];
	readonly PerturbContext perturbContext;
	int perturbStampCount;
	readonly Dictionary<Pawn, PawnSample> pawnSamples = [];
	readonly List<Pawn> stalePawnSamples = [];
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
	StorageBuffer<uint>? maskBuffer;
	StorageBuffer<uint>? decodeMaskBuffer;
	uint[] maskCells = [];
	uint[] decodeMaskCells = [];
	CellState[] emptyState = [];
	Point2 updateOrigin;
	Point2 updateSize;
	readonly List<ShadowSegment> occluderSegments = [];
	string maskData = "";
	uint normalSlots;
	bool currentIsB;
	bool supported;
	bool settledFromSeed;
	float stepAccumulator;
	uint seed = 1;

	// Base64 of a PNG-compressed 128x128 RGBA8 image; each channel is one slot's
	// blend weight. Empty means slot 0 painted everywhere (the classic full-rect actor).
	public string PaintedMask
	{
		get => maskData;
		set
		{
			maskData = value ?? "";
			if (supported)
			{
				DecodeMaskData();
				maskBuffer!.Upload(maskCells);
				RebuildUpdateBounds();
				RebuildDecodeMask();
				RebuildSilhouette();
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
			maskCells = injected;
		else
			DecodeMaskData();
		RebuildUpdateBounds();
		maskBuffer.Upload(maskCells);
		RebuildDecodeMask();
		output = new(
			graphicsDevice,
			Math.Max(1, Size.X),
			Math.Max(1, Size.Y),
			TextureFormat.Color,
			TextureFlags.ComputeWrite,
			"NeuralTextureOutput");
		// The decode shader statically binds a normal target, so color-only actors get a 1x1 stub.
		normalOutput = new(
			graphicsDevice,
			normalSlots != 0 ? Math.Max(1, Size.X) : 1,
			normalSlots != 0 ? Math.Max(1, Size.Y) : 1,
			TextureFormat.Color,
			TextureFlags.ComputeWrite,
			"NeuralTextureNormalOutput");
		bufferA.Upload(emptyState);
		bufferB.Upload(emptyState);
		perturbations.Upload(perturbStamps);
		RebuildSilhouette();

		supported = true;
		Decode();
		Game.EditorPreviewSync += SettleForEditorPreview;
	}

	// Steps run so the editor preview shows a settled texture instead of the near-blank seed
	// decode; gameplay reaches this look on its own by stepping every frame.
	const int EditorPreviewSteps = 95;

	// The editor doesn't tick this actor toward its attractor before rendering the preview, so
	// fast-forward on the sync signal. See Game.EditorPreviewSync / Manager.SyncEditorGame.
	void SettleForEditorPreview()
	{
		if (supported && !settledFromSeed)
			SimulateFromSeed(EditorPreviewSteps);
	}

	public string[] SlotWeightsFiles => [WeightsFile, WeightsFile1, WeightsFile2, WeightsFile3];

	void DecodeMaskData()
	{
		maskCells = new uint[StateCellCount];
		if (string.IsNullOrEmpty(maskData))
		{
			Array.Fill(maskCells, 0xFFu); // slot 0 fully painted
			return;
		}

		try
		{
			using var stream = new MemoryStream(Convert.FromBase64String(maskData));
			var image = new Image(stream);
			if (image.Width != SimulationSize.X || image.Height != SimulationSize.Y)
				throw new InvalidDataException($"mask is {image.Width}x{image.Height}; expected {SimulationSize.X}x{SimulationSize.Y}");
			var pixels = image.Data;
			for (var i = 0; i < maskCells.Length; i++)
				maskCells[i] = pixels[i].R | ((uint)pixels[i].G << 8) | ((uint)pixels[i].B << 16) | ((uint)pixels[i].A << 24);
		}
		catch (Exception exception)
		{
			Log.Warning($"Neural texture mask could not be decoded ({exception.Message}); painting slot 0 everywhere.");
			Array.Fill(maskCells, 0xFFu);
		}
	}

	public string SerializeMask()
	{
		var image = new Image(SimulationSize.X, SimulationSize.Y);
		var pixels = image.Data;
		for (var i = 0; i < maskCells.Length; i++)
			pixels[i] = new Color(
				(byte)(maskCells[i] & 0xFF),
				(byte)((maskCells[i] >> 8) & 0xFF),
				(byte)((maskCells[i] >> 16) & 0xFF),
				(byte)((maskCells[i] >> 24) & 0xFF));
		maskData = Convert.ToBase64String(image.WritePng());
		return maskData;
	}

	public void PaintMask(Vector2 worldCenter, float worldRadius, float softness, float strength, int slot)
	{
		if (!supported)
			return;

		var center = WorldToState(worldCenter);
		var radius = MathF.Max(worldRadius * StateScale, 0.5f);
		var falloff = MathF.Max(softness, 0.01f);
		var minX = Math.Max((int)(center.X - radius * 2), 0);
		var maxX = Math.Min((int)(center.X + radius * 2) + 1, SimulationSize.X - 1);
		var minY = Math.Max((int)(center.Y - radius * 2), 0);
		var maxY = Math.Min((int)(center.Y + radius * 2) + 1, SimulationSize.Y - 1);

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

				var index = y * SimulationSize.X + x;
				var cell = maskCells[index];
				var next = 0u;
				for (var s = 0; s < MaxSlots; s++)
				{
					var value = (cell >> (s * 8)) & 0xFF;
					var target = s == slot ? 255.0f : 0.0f;
					var blended = (uint)Math.Clamp(value + (target - value) * weight, 0.0f, 255.0f);
					next |= blended << (s * 8);
				}
				maskCells[index] = next;
			}

		maskBuffer!.Upload(maskCells);
		RebuildUpdateBounds();
		RebuildDecodeMask();
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
		var solid = Trace(decodeMaskCells);
		// What the painter drew, before EdgeTrim shaved the visible edge back: the bevel below
		// follows this, so trimming hides cells without carving a fresh lit edge into the shape.
		var painted = ReferenceEquals(decodeMaskCells, maskCells) ? solid : Trace(maskCells);

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

		bool[] Trace(uint[] cells)
		{
			var map = new bool[width * height];
			for (var y = 0; y < height; y++)
				for (var x = 0; x < width; x++)
				{
					var state = OutputToState(new Vector2(x + 0.5f, y + 0.5f));
					var cell = state.FloorToPoint2();
					var blend = state - (Vector2)cell;
					var top = float.Lerp(MaskAlpha(cells, cell), MaskAlpha(cells, cell + Point2.UnitX), blend.X);
					var bottom = float.Lerp(MaskAlpha(cells, cell + Point2.UnitY), MaskAlpha(cells, cell + Point2.One), blend.X);
					map[y * width + x] = float.Lerp(top, bottom, blend.Y) >= 1f / 512f;
				}
			return map;
		}

		float MaskAlpha(uint[] cells, Point2 cell)
		{
			cell = new(Math.Clamp(cell.X, 0, SimulationSize.X - 1), Math.Clamp(cell.Y, 0, SimulationSize.Y - 1));
			var packed = cells[cell.Y * SimulationSize.X + cell.X];
			return ((packed & 0xFF) + ((packed >> 8) & 0xFF) + ((packed >> 16) & 0xFF) + (packed >> 24)) / 255f;
		}

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
		if (!supported || StepsPerSecond <= 0)
			return;

		var interval = 1.0f / StepsPerSecond;
		stepAccumulator = MathF.Min(stepAccumulator + Time.Delta, interval);
		if (stepAccumulator >= interval)
		{
			stepAccumulator -= interval;
			Step();
		}
	}

	void Step()
	{
		ApplyPerturbations();
		StepState();
		Decode();
	}

	void StepState()
	{
		if (updateSize.X <= 0 || updateSize.Y <= 0)
			return;

		var source = currentIsB ? bufferB! : bufferA!;
		var destination = currentIsB ? bufferA! : bufferB!;
		updateUniforms.Set(new UpdateParams
		{
			Width = (uint)SimulationSize.X,
			Height = (uint)SimulationSize.Y,
			Seed = seed++,
			FireRate = FireRate,
			OriginX = (uint)updateOrigin.X,
			OriginY = (uint)updateOrigin.Y,
			DispatchWidth = (uint)updateSize.X,
			DispatchHeight = (uint)updateSize.Y,
			StatePixelsPerCell = StatePixelsPerCell,
			WorldStateOrigin = StatePixelsPerCell > 0
				? ((Vector2)Position + (Vector2)Size * 0.5f) / StatePixelsPerCell - ((Vector2)SimulationSize - Vector2.One) * 0.5f
				: Vector2.Zero,
			Padding = 0,
		});

		new ComputeCommand(updateShader!)
		{
			ReadOnlyStorageBuffers = [source, weights!, maskBuffer!],
			ReadWriteStorageBuffers = [destination],
			UniformBuffers = [updateUniforms],
			GroupCountX = (updateSize.X + 7) / 8,
			GroupCountY = (updateSize.Y + 7) / 8,
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
			return;

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

	void ApplyPerturbations()
	{
		perturbStampCount = 0;
		var now = Time.SecondsF;
		var textureBounds = GetVisualBounds();

		foreach (var actor in Game.Actors)
		{
			if (actor is not Pawn pawn || Game.destroying.Contains(pawn))
				continue;

			var position = pawn.Position;
			var previous = pawnSamples.GetValueOrDefault(pawn, new(position, now));
			var movement = (Vector2)(position - previous.Position);
			var moved = movement.LengthSquared() > 0.001f;
			var elapsed = MathF.Max(now - previous.Time, 0.0001f);
			var speed = movement.Length() / elapsed;
			var currentBounds = pawn.Hitbox.Rect + position;
			var previousBounds = currentBounds + previous.Position - position;

			foreach (var other in Game.Actors)
			{
				if (perturbContext.IsFull)
					break;
				if (other is not NeuralTexturePerturbActor { Mode: NeuralTexturePerturbMode.Pawns } perturb ||
					!perturb.WorldRect.Overlaps(textureBounds) ||
					(!perturb.DisturbStationary && !moved))
					continue;

				var radius = MathF.Max(0, perturb.Radius);
				if (!currentBounds.Conflate(previousBounds).Inflate((int)MathF.Ceiling(radius)).Overlaps(perturb.WorldRect))
					continue;

				var strength = MathF.Max(0, perturb.Strength);
				if (perturb.ScaleStrengthWithSpeed && perturb.SpeedForDoubleStrength > 0)
					strength *= 1 + speed / perturb.SpeedForDoubleStrength;

				var centerOffset = (Vector2)pawn.Hitbox.Rect.Center;
				perturbContext.AddStamp(
					(Vector2)previous.Position + centerOffset,
					(Vector2)position + centerOffset,
					radius,
					strength);
			}

			pawnSamples[pawn] = new(position, now);
		}

		RemoveStalePawnSamples();

		// Any actor can disturb the field it overlaps by implementing INeuralTextureAgent; the
		// context maps its world-space stamps into this texture's state grid.
		foreach (var actor in Game.Actors)
		{
			if (perturbContext.IsFull)
				break;
			if (actor is INeuralTextureAgent agent && !Game.destroying.Contains(actor))
				agent.PerturbNeuralTexture(perturbContext);
		}

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

	int StateCellCount => SimulationSize.X * SimulationSize.Y;

	public float StateScale => StatePixelsPerCell > 0 ? 1f / StatePixelsPerCell : SimulationSize.X / (float)Math.Max(Size.X, Size.Y);

	Vector2 WorldToState(Vector2 world)
		=> OutputToState(world - (Vector2)Position);

	Vector2 OutputToState(Vector2 output)
	{
		var outputSize = (Vector2)Size;
		var stateSize = (Vector2)SimulationSize;
		if (StatePixelsPerCell > 0)
			return (output - outputSize * 0.5f) / StatePixelsPerCell + (stateSize - Vector2.One) * 0.5f;
		var extent = Math.Max(Size.X, Size.Y);
		return (output - outputSize * 0.5f) / extent * stateSize + (stateSize - Vector2.One) * 0.5f;
	}

	void RemoveStalePawnSamples()
	{
		stalePawnSamples.Clear();
		foreach (var pawn in pawnSamples.Keys)
			if (!Game.Actors.Contains(pawn))
				stalePawnSamples.Add(pawn);
		foreach (var pawn in stalePawnSamples)
			pawnSamples.Remove(pawn);
	}

	void Decode()
	{
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
			StatePixelsPerCell = StatePixelsPerCell,
			Pad0 = 0,
			Pad1 = 0,
		});

		new ComputeCommand(decodeShader!)
		{
			ReadOnlyStorageBuffers = [source, weights!, decodeMaskBuffer ?? maskBuffer!],
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
		if (supported && normalSlots != 0 && normalOutput != null)
			batcher.Image(normalOutput, Vector2.Zero, Color.White);
	}

	public override Rect GetVisualBounds()
		=> new(Position.X, Position.Y, Size.X, Size.Y);

	public override void Destroyed()
	{
		base.Destroyed();
		Game.EditorPreviewSync -= SettleForEditorPreview;
		bufferA?.Dispose();
		bufferB?.Dispose();
		perturbations?.Dispose();
		maskBuffer?.Dispose();
		decodeMaskBuffer?.Dispose();
		output?.Dispose();
		normalOutput?.Dispose();
		silhouetteNormalOutput?.Dispose();
	}
}
