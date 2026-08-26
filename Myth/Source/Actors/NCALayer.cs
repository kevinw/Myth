using System.Numerics;
using System.Runtime.InteropServices;
using Foster.Framework;

namespace Myth;

/// <summary>
/// Renders a single persistent Neural Cellular Automata "fungus" texture
/// across a designer-painted background region (see Factory.FungusTile).
/// Unlike NeuralCellularActor (one creature, fixed grid), NCALayer manages
/// one buffer sized to the whole region and only grows where the region's
/// growth mask allows. The player walking over the region sporadically
/// seeds new growth and disturbs existing cells; attacks cut it back.
/// </summary>
public class NCALayer : Actor
{
	[StructLayout(LayoutKind.Sequential)]
	struct CellState
	{
		public float C0, C1, C2, C3, C4, C5, C6, C7, C8, C9, C10, C11, C12, C13, C14, C15;

		public static CellState Seed => new()
		{
			C3 = 1, C4 = 1, C5 = 1, C6 = 1, C7 = 1, C8 = 1, C9 = 1, C10 = 1,
			C11 = 1, C12 = 1, C13 = 1, C14 = 1, C15 = 1,
		};
	}

	struct UpdateParams
	{
		public uint Width;
		public uint Height;
		public uint Seed;
		public float FireRate;
		public uint OriginX;
		public uint OriginY;
	}

	struct PresentParams
	{
		public uint Width;
		public uint Height;
		public float RippleDisplacement;
		public float RippleTint;
	}

	// Layout-only: never written from C#, only used to size/zero-fill the
	// GPU-side ripple buffers, so the fields are never directly assigned.
#pragma warning disable CS0649
	struct RippleState
	{
		public float H, V;
	}
#pragma warning restore CS0649

	struct RippleUpdateParams
	{
		public uint Width;
		public uint Height;
		public float Speed;
		public float Damping;
		public uint OriginX;
		public uint OriginY;
	}

	struct RippleHitParams
	{
		public uint Width;
		public uint Height;
		public float HitX;
		public float HitY;
		public float HitRadius;
		public float HitStrength;
	}

	struct CutParams
	{
		public uint Width;
		public uint Height;
		public uint CutX;
		public uint CutY;
		public uint CutWidth;
		public uint CutHeight;
		public uint Seed;
	}

	public override bool CastsSpriteShadow => false;
	public override bool CastsReflection => false;

	/// <summary>Pixel size of the region buffer. Set by the caller before Added() runs.</summary>
	public int RegionWidth = 8;

	/// <summary>Pixel size of the region buffer. Set by the caller before Added() runs.</summary>
	public int RegionHeight = 8;

	/// <summary>
	/// Per-pixel growth mask (RegionWidth*RegionHeight, row-major). 1 = the
	/// simulation may grow into this cell, 0 = always forced dead. Set by
	/// the caller before Added() runs.
	/// </summary>
	public float[]? GrowthMask;

	public ContentRef<NcaWeightsContent> WeightsFile = NcaWeightsContent.LayerDefault;
	public float FireRate = 0.5f;
	public float StepsPerSecond = 30.0f;
	public float RippleWaveSpeed = 0.7f;
	// Fungus ripple is a subtle settling shimmer, not the creature's watery
	// wobble: ripples decay faster (lower damping), barely displace cells, and
	// only faintly brighten rather than flashing to white.
	public float RippleDamping = 0.90f;
	public float RippleDisplacement = 1.1f;
	public float RippleTintStrength = 0.22f;

	/// <summary>Chance per frame, while the player overlaps the region, of depositing a burst of growth.</summary>
	public float SeedChance = 0.12f;

	/// <summary>How many seed cells to attempt to plant each time a burst fires.</summary>
	public int SeedCount = 4;

	/// <summary>Min/max jitter radius (in pixels) around the player's foot position when seeding.</summary>
	public float SeedJitterMin = 2.0f;
	public float SeedJitterMax = 6.0f;

	private static Rng rng = new();

	private GraphicsDevice graphicsDevice = null!;
	private ComputeStorageBuffer<CellState>? bufferA;
	private ComputeStorageBuffer<CellState>? bufferB;
	private ComputeStorageBuffer<RippleState>? rippleA;
	private ComputeStorageBuffer<RippleState>? rippleB;
	private StorageBuffer<float>? weights;
	private StorageBuffer<float>? mask;
	private Texture? presentTexture;
	private Shader? updateShader;
	private Shader? presentShader;
	private Shader? cutShader;
	private Shader? rippleUpdateShader;
	private Shader? rippleHitShader;
	private bool currentIsB;
	private bool rippleIsB;
	private float stepAccumulator;
	private uint seed = 1;
	private bool supported;

	// CPU-side mirror of the cell grid, refreshed asynchronously (~1 step of
	// latency) via ComputeStorageBuffer.RequestDownload, used to decide
	// whether a candidate seed position is already alive without stalling
	// the GPU.
	private CellState[]? cellCache;
	private BufferDownload<CellState> cellDownload;

	public override void Added()
	{
		base.Added();

		graphicsDevice = Game.Manager.GraphicsDevice;

		var ext = graphicsDevice.Driver.GetShaderExtension();
		var updateCode = Assets.TryReadCompiledShader($"NCAUpdateMasked.compute.{ext}");
		var presentCode = Assets.TryReadCompiledShader($"NCAPresent.compute.{ext}");
		var cutCode = Assets.TryReadCompiledShader($"NCACut.compute.{ext}");
		var rippleUpdateCode = Assets.TryReadCompiledShader($"RippleUpdateMasked.compute.{ext}");
		var rippleHitCode = Assets.TryReadCompiledShader($"RippleHit.compute.{ext}");

		if (updateCode == null || presentCode == null || cutCode == null
			|| rippleUpdateCode == null || rippleHitCode == null)
			return;

		updateShader = new Shader(
			graphicsDevice,
			ShaderStage.Compute,
			updateCode,
			uniformBufferCount: 1,
			readOnlyStorageBufferCount: 3,
			readWriteStorageBufferCount: 1,
			threadCountX: 8,
			threadCountY: 8,
			threadCountZ: 1,
			entryPoint: "compute_main",
			name: "NCAUpdateMasked");

		presentShader = new Shader(
			graphicsDevice,
			ShaderStage.Compute,
			presentCode,
			uniformBufferCount: 1,
			readOnlyStorageBufferCount: 2,
			readWriteStorageTextureCount: 1,
			threadCountX: 8,
			threadCountY: 8,
			threadCountZ: 1,
			entryPoint: "compute_main",
			name: "NCAPresent");

		cutShader = new Shader(
			graphicsDevice,
			ShaderStage.Compute,
			cutCode,
			uniformBufferCount: 1,
			readOnlyStorageBufferCount: 1,
			readWriteStorageBufferCount: 1,
			threadCountX: 8,
			threadCountY: 8,
			threadCountZ: 1,
			entryPoint: "compute_main",
			name: "NCACut");

		rippleUpdateShader = new Shader(
			graphicsDevice,
			ShaderStage.Compute,
			rippleUpdateCode,
			uniformBufferCount: 1,
			readOnlyStorageBufferCount: 1,
			readWriteStorageBufferCount: 1,
			threadCountX: 8,
			threadCountY: 8,
			threadCountZ: 1,
			entryPoint: "compute_main",
			name: "RippleUpdateMasked");

		rippleHitShader = new Shader(
			graphicsDevice,
			ShaderStage.Compute,
			rippleHitCode,
			uniformBufferCount: 1,
			readOnlyStorageBufferCount: 1,
			readWriteStorageBufferCount: 1,
			threadCountX: 8,
			threadCountY: 8,
			threadCountZ: 1,
			entryPoint: "compute_main",
			name: "RippleHit");

		bufferA = new ComputeStorageBuffer<CellState>(graphicsDevice, "NCALayerStateA");
		bufferB = new ComputeStorageBuffer<CellState>(graphicsDevice, "NCALayerStateB");
		rippleA = new ComputeStorageBuffer<RippleState>(graphicsDevice, "NCALayerRippleA");
		rippleB = new ComputeStorageBuffer<RippleState>(graphicsDevice, "NCALayerRippleB");
		weights = new StorageBuffer<float>(graphicsDevice, "NCALayerWeights");
		mask = new StorageBuffer<float>(graphicsDevice, "NCALayerMask");
		presentTexture = new Texture(graphicsDevice, RegionWidth, RegionHeight, TextureFormat.Color, TextureFlags.ComputeWrite, "NCALayerPresent");

		cellCache = new CellState[RegionWidth * RegionHeight];

		LoadWeights();
		SeedBuffers();
		UploadMask();

		supported = true;

		Present();
	}

	private void LoadWeights()
	{
		var bytes = Assets.Content.ReadAllBytes(WeightsFile.ContentKey);
		var floats = MemoryMarshal.Cast<byte, float>(bytes.AsSpan());
		weights!.Upload(floats);
	}

	private void SeedBuffers()
	{
		var cells = new CellState[RegionWidth * RegionHeight];
		var ripple = new RippleState[RegionWidth * RegionHeight];

		bufferA!.Upload(cells);
		bufferB!.Upload(cells);
		rippleA!.Upload(ripple);
		rippleB!.Upload(ripple);
	}

	private void UploadMask()
	{
		var data = GrowthMask ?? new float[RegionWidth * RegionHeight];
		mask!.Upload(data);
	}

	public override void Update()
	{
		base.Update();

		if (!supported)
			return;

		var stepInterval = 1.0f / StepsPerSecond;
		stepAccumulator += Time.Delta;

		while (stepAccumulator >= stepInterval)
		{
			stepAccumulator -= stepInterval;
			Step();
		}
	}

	private void Step()
	{
		var src = currentIsB ? bufferB! : bufferA!;
		var dst = currentIsB ? bufferA! : bufferB!;

		var updateUniforms = new UniformBuffer();
		updateUniforms.Set(new UpdateParams
		{
			Width = (uint)RegionWidth,
			Height = (uint)RegionHeight,
			Seed = seed++,
			FireRate = FireRate,
			OriginX = 0,
			OriginY = 0,
		});

		new ComputeCommand(updateShader!)
		{
			ReadOnlyStorageBuffers = [src, weights!, mask!],
			ReadWriteStorageBuffers = [dst],
			UniformBuffers = [updateUniforms],
			GroupCountX = (RegionWidth + 7) / 8,
			GroupCountY = (RegionHeight + 7) / 8,
			GroupCountZ = 1,
		}.Submit(graphicsDevice);

		currentIsB = !currentIsB;

		StepRipple();

		Present();
		RefreshCellCache();
	}

	private void StepRipple()
	{
		var src = rippleIsB ? rippleB! : rippleA!;
		var dst = rippleIsB ? rippleA! : rippleB!;

		var rippleUniforms = new UniformBuffer();
		rippleUniforms.Set(new RippleUpdateParams
		{
			Width = (uint)RegionWidth,
			Height = (uint)RegionHeight,
			Speed = RippleWaveSpeed,
			Damping = RippleDamping,
			OriginX = 0,
			OriginY = 0,
		});

		new ComputeCommand(rippleUpdateShader!)
		{
			ReadOnlyStorageBuffers = [src],
			ReadWriteStorageBuffers = [dst],
			UniformBuffers = [rippleUniforms],
			GroupCountX = (RegionWidth + 7) / 8,
			GroupCountY = (RegionHeight + 7) / 8,
			GroupCountZ = 1,
		}.Submit(graphicsDevice);

		rippleIsB = !rippleIsB;
	}

	/// <summary>
	/// Injects an impulse into the ripple displacement field at the given
	/// world-space position, e.g. from a player walking by or attacking.
	/// The impulse propagates outward as a decaying ring, visually pushing
	/// and flashing the cells it passes through.
	/// </summary>
	public void Hit(Vector2 worldPos, float strength = 1.0f, float radius = 6.0f)
	{
		if (!supported)
			return;

		var src = rippleIsB ? rippleB! : rippleA!;
		var dst = rippleIsB ? rippleA! : rippleB!;

		var hitUniforms = new UniformBuffer();
		hitUniforms.Set(new RippleHitParams
		{
			Width = (uint)RegionWidth,
			Height = (uint)RegionHeight,
			HitX = worldPos.X - Position.X,
			HitY = worldPos.Y - Position.Y,
			HitRadius = radius,
			HitStrength = strength,
		});

		new ComputeCommand(rippleHitShader!)
		{
			ReadOnlyStorageBuffers = [src],
			ReadWriteStorageBuffers = [dst],
			UniformBuffers = [hitUniforms],
			GroupCountX = (RegionWidth + 7) / 8,
			GroupCountY = (RegionHeight + 7) / 8,
			GroupCountZ = 1,
		}.Submit(graphicsDevice);

		rippleIsB = !rippleIsB;
	}

	/// <summary>
	/// Kills all cells overlapping the given world-space rectangle, e.g. from
	/// a player attack. The simulation will regrow into the cut region
	/// (subject to the growth mask).
	/// </summary>
	public void Cut(RectInt worldRect)
	{
		if (!supported)
			return;

		var x0 = Math.Max(0, worldRect.X - Position.X);
		var y0 = Math.Max(0, worldRect.Y - Position.Y);
		var x1 = Math.Min(RegionWidth, worldRect.X + worldRect.Width - Position.X);
		var y1 = Math.Min(RegionHeight, worldRect.Y + worldRect.Height - Position.Y);

		if (x1 <= x0 || y1 <= y0)
			return;

		var src = currentIsB ? bufferB! : bufferA!;
		var dst = currentIsB ? bufferA! : bufferB!;

		var cutUniforms = new UniformBuffer();
		cutUniforms.Set(new CutParams
		{
			Width = (uint)RegionWidth,
			Height = (uint)RegionHeight,
			CutX = (uint)x0,
			CutY = (uint)y0,
			CutWidth = (uint)(x1 - x0),
			CutHeight = (uint)(y1 - y0),
			Seed = seed++,
		});

		new ComputeCommand(cutShader!)
		{
			ReadOnlyStorageBuffers = [src],
			ReadWriteStorageBuffers = [dst],
			UniformBuffers = [cutUniforms],
			GroupCountX = (RegionWidth + 7) / 8,
			GroupCountY = (RegionHeight + 7) / 8,
			GroupCountZ = 1,
		}.Submit(graphicsDevice);

		currentIsB = !currentIsB;

		Present();
	}

	/// <summary>
	/// Called every frame the player overlaps this region. Continuously
	/// disturbs the ripple field at the player's foot position, and
	/// occasionally seeds a new patch of growth nearby.
	/// </summary>
	public void OnPlayerStep(Vector2 footWorldPos)
	{
		if (!supported)
			return;

		Hit(footWorldPos, strength: 0.15f, radius: 4.0f);

		if (rng.Float() > SeedChance)
			return;

		for (var i = 0; i < SeedCount; i++)
			TrySeedNear(footWorldPos);
	}

	/// <summary>
	/// Attempts to plant a single seed cell at a jittered offset from the
	/// player's foot position, skipping cells outside the region, outside the
	/// growth mask, or already alive.
	/// </summary>
	private void TrySeedNear(Vector2 footWorldPos)
	{
		var jitter = rng.Float(SeedJitterMin, SeedJitterMax);
		var angle = rng.Float(0.0f, MathF.Tau);
		var localX = (int)MathF.Round(footWorldPos.X - Position.X + MathF.Cos(angle) * jitter);
		var localY = (int)MathF.Round(footWorldPos.Y - Position.Y + MathF.Sin(angle) * jitter);

		if (localX < 0 || localY < 0 || localX >= RegionWidth || localY >= RegionHeight)
			return;

		var index = localY * RegionWidth + localX;

		if (GrowthMask != null && GrowthMask[index] < 0.5f)
			return;

		if (cellCache != null && cellCache[index].C3 > 0.1f)
			return;

		ReadOnlySpan<CellState> seedSpan = [CellState.Seed];
		bufferA!.Upload(seedSpan, index);
		bufferB!.Upload(seedSpan, index);
	}

	/// <summary>
	/// Reads back last step's requested cell-grid download (if ready) into
	/// <see cref="cellCache"/>, then requests a fresh download of the
	/// current grid for next step. Non-stalling; the cache trails the GPU
	/// state by roughly one step.
	/// </summary>
	private void RefreshCellCache()
	{
		var cur = currentIsB ? bufferB! : bufferA!;
		cellDownload.TryRead(cellCache!);
		cellDownload = cur.RequestDownload(0, RegionWidth * RegionHeight, cellDownload);
	}

	private void Present()
	{
		var src = currentIsB ? bufferB! : bufferA!;
		var ripple = rippleIsB ? rippleB! : rippleA!;

		var presentUniforms = new UniformBuffer();
		presentUniforms.Set(new PresentParams
		{
			Width = (uint)RegionWidth,
			Height = (uint)RegionHeight,
			RippleDisplacement = RippleDisplacement,
			RippleTint = RippleTintStrength,
		});

		new ComputeCommand(presentShader!)
		{
			ReadOnlyStorageBuffers = [src, ripple],
			ReadWriteStorageTextures = [presentTexture!],
			UniformBuffers = [presentUniforms],
			GroupCountX = (RegionWidth + 7) / 8,
			GroupCountY = (RegionHeight + 7) / 8,
			GroupCountZ = 1,
		}.Submit(graphicsDevice);
	}

	public override void Render(Batcher batcher)
	{
		if (!supported || presentTexture == null)
			return;

		batcher.Image(presentTexture, Vector2.Zero, Color.White);
	}

	public override Rect GetVisualBounds()
		=> new(Position.X, Position.Y, RegionWidth, RegionHeight);

	public override void Destroyed()
	{
		base.Destroyed();

		bufferA?.Dispose();
		bufferB?.Dispose();
		rippleA?.Dispose();
		rippleB?.Dispose();
		weights?.Dispose();
		mask?.Dispose();
		presentTexture?.Dispose();
		updateShader?.Dispose();
		presentShader?.Dispose();
		cutShader?.Dispose();
		rippleUpdateShader?.Dispose();
		rippleHitShader?.Dispose();
	}
}
