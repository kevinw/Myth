using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using Foster.Framework;

namespace Myth;

/// <summary>
/// Renders a Growing Neural Cellular Automata simulation
/// (https://distill.pub/2020/growing-ca/) on a per-pixel grid using
/// compute shaders.
/// </summary>
[GameActor(HideFromPalette = true)]
public class NeuralCellularActor : Actor
{
	[StructLayout(LayoutKind.Sequential)]
	struct CellState
	{
		public float C0, C1, C2, C3, C4, C5, C6, C7, C8, C9, C10, C11, C12, C13, C14, C15;
	}

	struct UpdateParams
	{
		public uint Width;
		public uint Height;
		public uint Seed;
		public float FireRate;
	}

	struct UpdateParamsDirectional
	{
		public uint Width;
		public uint Height;
		public uint Seed;
		public float FireRate;
		public float PlayerDirX;
		public float PlayerDirY;
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

	public int GridWidth = 96;
	public int GridHeight = 96;
	public float FireRate = 0.5f;
	public float StepsPerSecond = 30.0f;
	public string WeightsFile = "lizard_weights.bin";

	/// <summary>
	/// When true, uses NCAUpdateDirectional.hlsl, which feeds a normalized
	/// direction-to-player vector into the model so the creature can lean
	/// its growth toward the player. Requires weights trained with
	/// train.py --direction-aware.
	/// </summary>
	public bool DirectionAware = false;

	/// <summary>
	/// Wave speed for the ripple displacement field's discrete wave equation.
	/// Higher values propagate hit impulses outward faster.
	/// </summary>
	public float RippleWaveSpeed = 0.7f;

	/// <summary>
	/// Per-step decay applied to the ripple field (1.0 = no decay). Controls
	/// how quickly a ripple fades out after a hit.
	/// </summary>
	public float RippleDamping = 0.97f;

	/// <summary>
	/// Scales how far rendered cells are pushed by the local ripple
	/// gradient, in cells.
	/// </summary>
	public float RippleDisplacement = 3.9f;

	/// <summary>
	/// Scales how strongly the ripple wave's amplitude flashes the rendered
	/// cells toward white.
	/// </summary>
	public float RippleTintStrength = 0.8f;

	private static Rng rng = new();

	private GraphicsDevice graphicsDevice = null!;
	private ComputeStorageBuffer<CellState>? bufferA;
	private ComputeStorageBuffer<CellState>? bufferB;
	private ComputeStorageBuffer<RippleState>? rippleA;
	private ComputeStorageBuffer<RippleState>? rippleB;
	private StorageBuffer<float>? weights;
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
	// whether/how to spawn cut particles without stalling the GPU.
	private CellState[]? cellCache;
	private BufferDownload<CellState> cellDownload;

	public override void Added()
	{
		base.Added();

		graphicsDevice = Game.Manager.GraphicsDevice;

		var ext = graphicsDevice.Driver.GetShaderExtension();
		var shaderDir = Path.Join(Assets.AssetsPath, "Shaders", "Compiled");
		var updateName = DirectionAware ? "NCAUpdateDirectional" : "NCAUpdate";
		var updatePath = Path.Join(shaderDir, $"{updateName}.compute.{ext}");
		var presentPath = Path.Join(shaderDir, $"NCAPresent.compute.{ext}");
		var cutPath = Path.Join(shaderDir, $"NCACut.compute.{ext}");
		var rippleUpdatePath = Path.Join(shaderDir, $"RippleUpdate.compute.{ext}");
		var rippleHitPath = Path.Join(shaderDir, $"RippleHit.compute.{ext}");

		if (!File.Exists(updatePath) || !File.Exists(presentPath) || !File.Exists(cutPath)
			|| !File.Exists(rippleUpdatePath) || !File.Exists(rippleHitPath))
			return;

		updateShader = new Shader(
			graphicsDevice,
			ShaderStage.Compute,
			File.ReadAllBytes(updatePath),
			uniformBufferCount: 1,
			readOnlyStorageBufferCount: 2,
			readWriteStorageBufferCount: 1,
			threadCountX: 8,
			threadCountY: 8,
			threadCountZ: 1,
			entryPoint: "compute_main",
			name: updateName);

		presentShader = new Shader(
			graphicsDevice,
			ShaderStage.Compute,
			File.ReadAllBytes(presentPath),
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
			File.ReadAllBytes(cutPath),
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
			File.ReadAllBytes(rippleUpdatePath),
			uniformBufferCount: 1,
			readOnlyStorageBufferCount: 1,
			readWriteStorageBufferCount: 1,
			threadCountX: 8,
			threadCountY: 8,
			threadCountZ: 1,
			entryPoint: "compute_main",
			name: "RippleUpdate");

		rippleHitShader = new Shader(
			graphicsDevice,
			ShaderStage.Compute,
			File.ReadAllBytes(rippleHitPath),
			uniformBufferCount: 1,
			readOnlyStorageBufferCount: 1,
			readWriteStorageBufferCount: 1,
			threadCountX: 8,
			threadCountY: 8,
			threadCountZ: 1,
			entryPoint: "compute_main",
			name: "RippleHit");

		bufferA = new ComputeStorageBuffer<CellState>(graphicsDevice, "NCAStateA");
		bufferB = new ComputeStorageBuffer<CellState>(graphicsDevice, "NCAStateB");
		rippleA = new ComputeStorageBuffer<RippleState>(graphicsDevice, "NCARippleA");
		rippleB = new ComputeStorageBuffer<RippleState>(graphicsDevice, "NCARippleB");
		weights = new StorageBuffer<float>(graphicsDevice, "NCAWeights");
		presentTexture = new Texture(graphicsDevice, GridWidth, GridHeight, TextureFormat.Color, TextureFlags.ComputeWrite, "NCAPresent");

		cellCache = new CellState[GridWidth * GridHeight];

		LoadWeights();
		SeedGrid();
		SeedRipple();

		supported = true;

		Present();
	}

	private void LoadWeights()
	{
		var path = Path.Join(Assets.AssetsPath, "NCA", WeightsFile);
		var bytes = File.ReadAllBytes(path);
		var floats = MemoryMarshal.Cast<byte, float>(bytes.AsSpan());
		weights!.Upload(floats);
	}

	private void SeedGrid()
	{
		var cells = new CellState[GridWidth * GridHeight];
		var cx = GridWidth / 2;
		var cy = GridHeight / 2;

		// Seed cell: alpha + all hidden channels = 1, RGB = 0.
		cells[cy * GridWidth + cx] = new CellState
		{
			C3 = 1, C4 = 1, C5 = 1, C6 = 1, C7 = 1, C8 = 1, C9 = 1, C10 = 1,
			C11 = 1, C12 = 1, C13 = 1, C14 = 1, C15 = 1,
		};

		bufferA!.Upload(cells);
		bufferB!.Upload(cells);
	}

	private void SeedRipple()
	{
		var cells = new RippleState[GridWidth * GridHeight];
		rippleA!.Upload(cells);
		rippleB!.Upload(cells);
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

		if (DirectionAware)
		{
			var dir = GetPlayerDirection();
			updateUniforms.Set(new UpdateParamsDirectional
			{
				Width = (uint)GridWidth,
				Height = (uint)GridHeight,
				Seed = seed++,
				FireRate = FireRate,
				PlayerDirX = dir.X,
				PlayerDirY = dir.Y,
			});
		}
		else
		{
			updateUniforms.Set(new UpdateParams
			{
				Width = (uint)GridWidth,
				Height = (uint)GridHeight,
				Seed = seed++,
				FireRate = FireRate,
			});
		}

		new ComputeCommand(updateShader!)
		{
			ReadOnlyStorageBuffers = [src, weights!],
			ReadWriteStorageBuffers = [dst],
			UniformBuffers = [updateUniforms],
			GroupCountX = (GridWidth + 7) / 8,
			GroupCountY = (GridHeight + 7) / 8,
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
			Width = (uint)GridWidth,
			Height = (uint)GridHeight,
			Speed = RippleWaveSpeed,
			Damping = RippleDamping,
		});

		new ComputeCommand(rippleUpdateShader!)
		{
			ReadOnlyStorageBuffers = [src],
			ReadWriteStorageBuffers = [dst],
			UniformBuffers = [rippleUniforms],
			GroupCountX = (GridWidth + 7) / 8,
			GroupCountY = (GridHeight + 7) / 8,
			GroupCountZ = 1,
		}.Submit(graphicsDevice);

		rippleIsB = !rippleIsB;
	}

	/// <summary>
	/// Injects an impulse into the ripple displacement field at the given
	/// world-space position, e.g. from a player attack. The impulse
	/// propagates outward as a decaying ring, visually pushing and flashing
	/// the cells it passes through.
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
			Width = (uint)GridWidth,
			Height = (uint)GridHeight,
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
			GroupCountX = (GridWidth + 7) / 8,
			GroupCountY = (GridHeight + 7) / 8,
			GroupCountZ = 1,
		}.Submit(graphicsDevice);

		rippleIsB = !rippleIsB;
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
		cellDownload = cur.RequestDownload(0, GridWidth * GridHeight, cellDownload);
	}

	/// <summary>
	/// Normalized direction from this creature's center to the player, in
	/// world space, or zero if no player is present.
	/// </summary>
	private Vector2 GetPlayerDirection()
	{
		foreach (var actor in Game.Actors)
		{
			if (actor is not Player player)
				continue;

			var center = new Vector2(Position.X + GridWidth / 2.0f, Position.Y + GridHeight / 2.0f);
			var toPlayer = (Vector2)player.Position - center;
			if (toPlayer.LengthSquared() < 0.0001f)
				return Vector2.Zero;

			return Vector2.Normalize(toPlayer);
		}

		return Vector2.Zero;
	}

	/// <summary>
	/// Kills all cells overlapping the given world-space rectangle, e.g. from
	/// a player attack. The simulation will regrow into the cut region.
	/// </summary>
	public void Cut(RectInt worldRect)
	{
		if (!supported)
			return;

		var x0 = Math.Max(0, worldRect.X - Position.X);
		var y0 = Math.Max(0, worldRect.Y - Position.Y);
		var x1 = Math.Min(GridWidth, worldRect.X + worldRect.Width - Position.X);
		var y1 = Math.Min(GridHeight, worldRect.Y + worldRect.Height - Position.Y);

		if (x1 <= x0 || y1 <= y0)
			return;

		var src = currentIsB ? bufferB! : bufferA!;
		var dst = currentIsB ? bufferA! : bufferB!;
		var cutWidth = x1 - x0;
		var cutHeight = y1 - y0;

		var cutUniforms = new UniformBuffer();
		cutUniforms.Set(new CutParams
		{
			Width = (uint)GridWidth,
			Height = (uint)GridHeight,
			CutX = (uint)x0,
			CutY = (uint)y0,
			CutWidth = (uint)cutWidth,
			CutHeight = (uint)cutHeight,
			Seed = seed++,
		});

		new ComputeCommand(cutShader!)
		{
			ReadOnlyStorageBuffers = [src],
			ReadWriteStorageBuffers = [dst],
			UniformBuffers = [cutUniforms],
			GroupCountX = (GridWidth + 7) / 8,
			GroupCountY = (GridHeight + 7) / 8,
			GroupCountZ = 1,
		}.Submit(graphicsDevice);

		currentIsB = !currentIsB;

		Present();

		SpawnCutParticles(x0, y0, x1, y1);
	}

	/// <summary>
	/// Decodes a cell's premultiplied RGBA channels (C0-C3) into a straight
	/// display Color, or null if the cell is effectively dead (alpha ~ 0).
	/// </summary>
	private static Color? CellColor(in CellState cell)
	{
		var a = Math.Clamp(cell.C3, 0.0f, 1.0f);
		if (a < 0.1f)
			return null;

		return new Color(
			Math.Clamp(cell.C0 / a, 0.0f, 1.0f),
			Math.Clamp(cell.C1 / a, 0.0f, 1.0f),
			Math.Clamp(cell.C2 / a, 0.0f, 1.0f),
			1.0f);
	}

	private void SpawnCutParticles(int x0, int y0, int x1, int y1)
	{
		if (cellCache == null)
			return;

		// Sample colors of cells within the cut region from our (~1 step
		// stale) CPU-side cache. If nothing was alive there, skip the burst
		// entirely instead of spawning particles over empty space.
		var colors = new List<Color>();
		for (var y = y0; y < y1; y++)
		for (var x = x0; x < x1; x++)
		{
			if (CellColor(cellCache[y * GridWidth + x]) is { } color)
				colors.Add(color);
		}

		if (colors.Count == 0)
			return;

		const int count = 10;
		for (var i = 0; i < count; i++)
		{
			var px = Position.X + rng.Int(x0, x1);
			var py = Position.Y + rng.Int(y0, y1);
			var color = colors[rng.Int(0, colors.Count)];

			Game.Create<NCAParticle>(new Point2(px, py), p =>
			{
				p.Color = color;
				p.Velocity = new Vector2(rng.Float(-40.0f, 40.0f), rng.Float(-90.0f, -20.0f));
				p.Lifetime = rng.Float(0.3f, 0.7f);
			});
		}
	}

	private void Present()
	{
		var src = currentIsB ? bufferB! : bufferA!;
		var ripple = rippleIsB ? rippleB! : rippleA!;

		var presentUniforms = new UniformBuffer();
		presentUniforms.Set(new PresentParams
		{
			Width = (uint)GridWidth,
			Height = (uint)GridHeight,
			RippleDisplacement = RippleDisplacement,
			RippleTint = RippleTintStrength,
		});

		new ComputeCommand(presentShader!)
		{
			ReadOnlyStorageBuffers = [src, ripple],
			ReadWriteStorageTextures = [presentTexture!],
			UniformBuffers = [presentUniforms],
			GroupCountX = (GridWidth + 7) / 8,
			GroupCountY = (GridHeight + 7) / 8,
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
		=> new(Position.X, Position.Y, GridWidth, GridHeight);

	public override void Destroyed()
	{
		base.Destroyed();

		bufferA?.Dispose();
		bufferB?.Dispose();
		rippleA?.Dispose();
		rippleB?.Dispose();
		weights?.Dispose();
		presentTexture?.Dispose();
		updateShader?.Dispose();
		presentShader?.Dispose();
		cutShader?.Dispose();
		rippleUpdateShader?.Dispose();
		rippleHitShader?.Dispose();
	}
}
