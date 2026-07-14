using System.Numerics;
using System.Runtime.InteropServices;
using Foster.Framework;

namespace Myth;

[GameActor(Sprite = "editor_prop", BoundsW = Game.TileSize * 4, BoundsH = Game.TileSize * 3, Anchor = SpriteAnchor.TopLeft)]
public class NeuralTextureActor : Actor, IResizable
{
	const int StateWidth = 128;
	const int StateHeight = 128;
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
	static readonly CellState[] EmptyState = new CellState[StateWidth * StateHeight];

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
	}

	struct DecodeParams
	{
		public uint Width;
		public uint Height;
		public uint OutputWidth;
		public uint OutputHeight;
		public uint NormalSlots;
		public uint Pad0, Pad1, Pad2;
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
	public string WeightsFile = DefaultWeightsFile;
	public string WeightsFile1 = "";
	public string WeightsFile2 = "";
	public string WeightsFile3 = "";
	public float FireRate = 0.5f;
	public float StepsPerSecond = 30.0f;
	public Factory.TileLayer Layer = Factory.TileLayer.Background;

	public override bool CastsSpriteShadow => false;
	public override bool CastsReflection => false;

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
	Shader? updateShader;
	Shader? decodeShader;
	Shader? perturbShader;
	StorageBuffer<uint>? maskBuffer;
	uint[] maskCells = [];
	string maskData = "";
	uint normalSlots;
	bool currentIsB;
	bool supported;
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

		var ext = graphicsDevice.Driver.GetShaderExtension();
		var updateCode = Assets.TryReadCompiledShader($"NeuralTextureUpdate.compute.{ext}");
		var decodeCode = Assets.TryReadCompiledShader($"NeuralTextureDecode.compute.{ext}");
		var perturbCode = Assets.TryReadCompiledShader($"NeuralTexturePerturb.compute.{ext}");
		if (updateCode == null || decodeCode == null || perturbCode == null)
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
			name: "NeuralTextureUpdate");

		decodeShader = new Shader(
			graphicsDevice,
			ShaderStage.Compute,
			decodeCode,
			uniformBufferCount: 1,
			readOnlyStorageBufferCount: 3,
			readWriteStorageTextureCount: 2,
			threadCountX: 8,
			threadCountY: 8,
			threadCountZ: 1,
			entryPoint: "compute_main",
			name: "NeuralTextureDecode");

		perturbShader = new Shader(
			graphicsDevice,
			ShaderStage.Compute,
			perturbCode,
			uniformBufferCount: 1,
			readOnlyStorageBufferCount: 2,
			readWriteStorageBufferCount: 1,
			threadCountX: 8,
			threadCountY: 8,
			threadCountZ: 1,
			entryPoint: "compute_main",
			name: "NeuralTexturePerturb");

		bufferA = new(graphicsDevice, "NeuralTextureStateA");
		bufferB = new(graphicsDevice, "NeuralTextureStateB");
		weights = new(graphicsDevice, "NeuralTextureWeights");
		perturbations = new(graphicsDevice, "NeuralTexturePerturbations");
		maskBuffer = new(graphicsDevice, "NeuralTextureMask");

		LoadWeights();
		DecodeMaskData();
		maskBuffer.Upload(maskCells);
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
		bufferA.Upload(EmptyState);
		bufferB.Upload(EmptyState);
		perturbations.Upload(perturbStamps);

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
		if (supported)
			SimulateFromSeed(EditorPreviewSteps);
	}

	public string[] SlotWeightsFiles => [WeightsFile, WeightsFile1, WeightsFile2, WeightsFile3];

	void LoadWeights()
	{
		var packed = new Vector4[MaxSlots * PairedWeightCount];
		normalSlots = 0;
		var files = SlotWeightsFiles;
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
				// Pad the three-channel layout to the six-channel stride: the extra
				// final-layer rows stay zero with a flat-normal bias.
				values[..FinalWeightRowsEnd].CopyTo(dest);
				var bias = values[FinalWeightRowsEnd];
				dest[FinalBiasOffset] = new Vector4(bias.X, bias.Y, bias.Z, 0.5f);
				dest[FinalBiasOffset + 1] = new Vector4(0.5f, 1.0f, 0.0f, 0.0f);
			}
			else
			{
				throw new InvalidDataException($"Neural texture model '{files[slot]}' has {values.Length} weight vectors; expected {ColorWeightCount} (color) or {PairedWeightCount} (color+normal).");
			}
		}
		weights!.Upload(packed);
	}

	void DecodeMaskData()
	{
		maskCells = new uint[StateWidth * StateHeight];
		if (string.IsNullOrEmpty(maskData))
		{
			Array.Fill(maskCells, 0xFFu); // slot 0 fully painted
			return;
		}

		try
		{
			using var stream = new MemoryStream(Convert.FromBase64String(maskData));
			var image = new Image(stream);
			if (image.Width != StateWidth || image.Height != StateHeight)
				throw new InvalidDataException($"mask is {image.Width}x{image.Height}; expected {StateWidth}x{StateHeight}");
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
		var image = new Image(StateWidth, StateHeight);
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
		var maxX = Math.Min((int)(center.X + radius * 2) + 1, StateWidth - 1);
		var minY = Math.Max((int)(center.Y - radius * 2), 0);
		var maxY = Math.Min((int)(center.Y + radius * 2) + 1, StateHeight - 1);

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

				var index = y * StateWidth + x;
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
		Decode();
	}

	public void SimulateFromSeed(int steps)
	{
		if (!supported)
			return;

		bufferA!.Upload(EmptyState);
		bufferB!.Upload(EmptyState);
		currentIsB = false;
		stepAccumulator = 0;
		seed = 1;
		pawnSamples.Clear();
		for (var i = 0; i < steps; i++)
			StepState();
		Decode();
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
		var source = currentIsB ? bufferB! : bufferA!;
		var destination = currentIsB ? bufferA! : bufferB!;
		updateUniforms.Set(new UpdateParams
		{
			Width = StateWidth,
			Height = StateHeight,
			Seed = seed++,
			FireRate = FireRate,
		});

		new ComputeCommand(updateShader!)
		{
			ReadOnlyStorageBuffers = [source, weights!, maskBuffer!],
			ReadWriteStorageBuffers = [destination],
			UniformBuffers = [updateUniforms],
			GroupCountX = (StateWidth + 7) / 8,
			GroupCountY = (StateHeight + 7) / 8,
		}.Submit(graphicsDevice);

		currentIsB = !currentIsB;
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
			Width = StateWidth,
			Height = StateHeight,
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
			GroupCountX = (StateWidth + 7) / 8,
			GroupCountY = (StateHeight + 7) / 8,
		}.Submit(graphicsDevice);
		currentIsB = !currentIsB;
	}

	public float StateScale => StateWidth / (float)Math.Max(Size.X, Size.Y);

	Vector2 WorldToState(Vector2 world)
		=> (world - (Vector2)Position - (Vector2)Size * 0.5f) * StateScale + new Vector2((StateWidth - 1) * 0.5f, (StateHeight - 1) * 0.5f);

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
			Width = StateWidth,
			Height = StateHeight,
			OutputWidth = (uint)output!.Width,
			OutputHeight = (uint)output.Height,
			NormalSlots = normalSlots,
		});

		new ComputeCommand(decodeShader!)
		{
			ReadOnlyStorageBuffers = [source, weights!, maskBuffer!],
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
		weights?.Dispose();
		perturbations?.Dispose();
		maskBuffer?.Dispose();
		output?.Dispose();
		normalOutput?.Dispose();
		updateShader?.Dispose();
		decodeShader?.Dispose();
		perturbShader?.Dispose();
	}
}
