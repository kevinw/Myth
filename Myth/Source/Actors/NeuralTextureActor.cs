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
	const int MaxPerturbations = 64;
	public const string WeightsFolder = "NCA/DynamicTexture";

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
		public uint OutputChannels;
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

	public static readonly Point2 DefaultSize = new(Game.Width, Game.Height);

	public Point2 Size { get; set; } = DefaultSize;
	public string WeightsFile = "chequered_0050_circular.bin";
	public float FireRate = 0.5f;
	public float StepsPerSecond = 30.0f;
	public Factory.TileLayer Layer = Factory.TileLayer.Background;

	public override bool CastsSpriteShadow => false;
	public override bool CastsReflection => false;

	readonly UniformBuffer updateUniforms = new();
	readonly UniformBuffer decodeUniforms = new();
	readonly UniformBuffer perturbUniforms = new();
	readonly PerturbStamp[] perturbStamps = new PerturbStamp[MaxPerturbations];
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
	bool currentIsB;
	bool hasNormals;
	bool supported;
	float stepAccumulator;
	uint seed = 1;

	public NeuralTextureActor()
	{
		Depth = 1000;
		CollidesWithSolids = false;
		Hitbox = new(new RectInt(0, 0, 0, 0));
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
			readOnlyStorageBufferCount: 2,
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
			readOnlyStorageBufferCount: 2,
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

		LoadWeights();
		output = new(
			graphicsDevice,
			Math.Max(1, Size.X),
			Math.Max(1, Size.Y),
			TextureFormat.Color,
			TextureFlags.ComputeWrite,
			"NeuralTextureOutput");
		// The decode shader statically binds a normal target, so color-only models get a 1x1 stub.
		normalOutput = new(
			graphicsDevice,
			hasNormals ? Math.Max(1, Size.X) : 1,
			hasNormals ? Math.Max(1, Size.Y) : 1,
			TextureFormat.Color,
			TextureFlags.ComputeWrite,
			"NeuralTextureNormalOutput");
		var state = new CellState[StateWidth * StateHeight];
		bufferA.Upload(state);
		bufferB.Upload(state);
		perturbations.Upload(perturbStamps);

		supported = true;
		Decode();
	}

	void LoadWeights()
	{
		var bytes = Assets.Content.ReadAllBytes($"{WeightsFolder}/{WeightsFile}");
		var values = MemoryMarshal.Cast<byte, Vector4>(bytes.AsSpan());
		if (values.Length != ColorWeightCount && values.Length != PairedWeightCount)
			throw new InvalidDataException($"Neural texture model '{WeightsFile}' has {values.Length} weight vectors; expected {ColorWeightCount} (color) or {PairedWeightCount} (color+normal).");
		hasNormals = values.Length == PairedWeightCount;
		weights!.Upload(values);
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
		ApplyPawnPerturbations();
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
			ReadOnlyStorageBuffers = [source, weights!],
			ReadWriteStorageBuffers = [destination],
			UniformBuffers = [updateUniforms],
			GroupCountX = (StateWidth + 7) / 8,
			GroupCountY = (StateHeight + 7) / 8,
		}.Submit(graphicsDevice);

		currentIsB = !currentIsB;
		Decode();
	}

	void ApplyPawnPerturbations()
	{
		var stampCount = 0;
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
				if (stampCount >= MaxPerturbations)
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
				if (strength <= 0 || radius <= 0)
					continue;

				var centerOffset = (Vector2)pawn.Hitbox.Rect.Center;
				perturbStamps[stampCount++] = new()
				{
					From = WorldToState((Vector2)previous.Position + centerOffset),
					To = WorldToState((Vector2)position + centerOffset),
					Radius = radius * StateScale,
					Strength = strength,
				};
			}

			pawnSamples[pawn] = new(position, now);
		}

		RemoveStalePawnSamples();
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

	float StateScale => StateWidth / (float)Math.Max(Size.X, Size.Y);

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
			OutputChannels = hasNormals ? 6u : 3u,
		});

		new ComputeCommand(decodeShader!)
		{
			ReadOnlyStorageBuffers = [source, weights!],
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
		if (supported && hasNormals && normalOutput != null)
			batcher.Image(normalOutput, Vector2.Zero, Color.White);
	}

	public override Rect GetVisualBounds()
		=> new(Position.X, Position.Y, Size.X, Size.Y);

	public override void Destroyed()
	{
		base.Destroyed();
		bufferA?.Dispose();
		bufferB?.Dispose();
		weights?.Dispose();
		perturbations?.Dispose();
		output?.Dispose();
		normalOutput?.Dispose();
		updateShader?.Dispose();
		decodeShader?.Dispose();
		perturbShader?.Dispose();
	}
}
