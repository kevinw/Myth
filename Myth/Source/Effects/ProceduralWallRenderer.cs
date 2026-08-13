using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Foster.Framework;

namespace Myth;

sealed class ProceduralWallRenderer : IDisposable
{
	public enum Pass
	{
		Color,
		Normals,
		Emissive,
		ActorTileField,
	}

	public const string DefaultPatternTexture = "patterns/pattern_59";
	public const string DefaultDetailTexture = "patterns/noise/Noise_014";

	[StructLayout(LayoutKind.Sequential, Pack = 1)]
	struct Vertex(Vector3 position, Vector3 normal, Vector2 texCoord, Vector3 tangent) : IVertex
	{
		public Vector3 Position = position;
		public Vector3 Normal = normal;
		public Vector2 TexCoord = texCoord;
		public Vector3 Tangent = tangent;

		public readonly VertexFormat Format => format;

		static readonly VertexFormat format = VertexFormat.Create<Vertex>(
			new(0, VertexType.Float3, false),
			new(1, VertexType.Float3, false),
			new(2, VertexType.Float2, false),
			new(3, VertexType.Float3, false));
	}

	[StructLayout(LayoutKind.Sequential)]
	struct Instance
	{
		public Vector4 Rect;
		public Vector4 DepthMotion;
		public Vector4 AxisAmount;
		public Vector4 TintRoughness;
		public Vector4 Uv;
		public Vector4 Flags;
	}

	[StructLayout(LayoutKind.Sequential, Pack = 1)]
	struct PipeVertex(Vector2 centerline, Vector3 radial, Vector2 tangent, Vector2 texCoord) : IVertex
	{
		public Vector2 Centerline = centerline;
		public Vector3 Radial = radial;
		public Vector2 Tangent = tangent;
		public Vector2 TexCoord = texCoord;

		public readonly VertexFormat Format => format;

		static readonly VertexFormat format = VertexFormat.Create<PipeVertex>(
			new(0, VertexType.Float2, false),
			new(1, VertexType.Float3, false),
			new(2, VertexType.Float2, false),
			new(3, VertexType.Float2, false));
	}

	[StructLayout(LayoutKind.Sequential)]
	struct PipeInstance
	{
		public Vector4 OriginScale;
		public Vector4 Basis;
		public Vector4 DepthUvRoughness;
		public Vector4 Color;
	}

	[StructLayout(LayoutKind.Sequential)]
	struct VertexParams
	{
		public Vector2 Camera;
		public Vector2 TargetSize;
		public Vector2 ProjectionSkew;
		public float Zoom;
		public float Time;
	}

	[StructLayout(LayoutKind.Sequential)]
	struct FragmentParams
	{
		public Vector4 LightSpecular;
		public Vector4 ViewNormalPass;
		public Vector4 NormalSettings;
		public Vector4 ActorFieldSettings;
	}

	sealed class WallData(GraphicsDevice graphicsDevice) : IDisposable
	{
		public readonly StorageBuffer<Instance> InstancesBuffer = new(graphicsDevice, "MachineWallInstances");
		public readonly StorageBuffer<PipeInstance> StraightPipeBuffer = new(graphicsDevice, "MachineWallStraightPipes");
		public readonly StorageBuffer<PipeInstance> ElbowPipeBuffer = new(graphicsDevice, "MachineWallPipeElbows");
		public readonly List<Instance> Instances = [];
		public readonly List<PipeInstance> StraightPipes = [];
		public readonly List<PipeInstance> ElbowPipes = [];
		public Material? Material;
		public Material? PipeMaterial;
		public Assets.BucketTexture? PatternTexture;
		public Assets.BucketTexture? DetailTexture;
		public int LayoutHash;
		public bool HasLayout;

		public void Dispose()
		{
			InstancesBuffer.Dispose();
			StraightPipeBuffer.Dispose();
			ElbowPipeBuffer.Dispose();
		}
	}

	static int BaseModuleSize => 8;
	static Vector2 ProjectionSkew => new(-0.18f, 0.13f);
	static Vector3 LightDirection => Vector3.Normalize(new(-0.45f, -0.65f, 0.8f));
	static float SpecularStrength => 0.22f;
	static float CoarseNormalStrength => 0.38f;
	static float DetailNormalStrength => 0.12f;
	static float PipeNormalStrength => 0.18f;
	static readonly BlendMode NormalBufferBlend = new(
		BlendOp.Add, BlendFactor.One, BlendFactor.OneMinusSrcAlpha,
		BlendOp.Add, BlendFactor.One, BlendFactor.OneMinusSrcAlpha,
		BlendMask.Red | BlendMask.Green | BlendMask.Alpha, Color.White);

	readonly GraphicsDevice graphicsDevice;
	readonly Mesh<Vertex, ushort> mesh;
	readonly Mesh<PipeVertex, ushort> straightPipeMesh;
	readonly Mesh<PipeVertex, ushort> elbowPipeMesh;
	readonly Dictionary<ProceduralWall, WallData> walls = [];
	readonly Dictionary<Assets.BucketTexture, Texture> materialTextures = [];
	readonly List<ProceduralWall> staleWalls = [];
	bool[] occupied = [];
	Shader vertexShader;
	Shader fragmentShader;
	Shader pipeVertexShader;
	Shader pipeFragmentShader;

	public ProceduralWallRenderer(GraphicsDevice graphicsDevice)
	{
		this.graphicsDevice = graphicsDevice;
		mesh = new(graphicsDevice, "MachineWallCube");
		mesh.SetVertices(CubeVertices);
		mesh.SetIndices(CubeIndices);
		straightPipeMesh = new(graphicsDevice, "MachineWallStraightPipe");
		straightPipeMesh.SetVertices(CreatePipeVertices(elbow: false));
		straightPipeMesh.SetIndices(CreatePipeIndices(elbow: false));
		elbowPipeMesh = new(graphicsDevice, "MachineWallPipeElbow");
		elbowPipeMesh.SetVertices(CreatePipeVertices(elbow: true));
		elbowPipeMesh.SetIndices(CreatePipeIndices(elbow: true));

		var ext = graphicsDevice.Driver.GetShaderExtension();
		var vertexCode = Assets.TryReadCompiledShader($"MachineWall.vertex.{ext}")
			?? throw new FileNotFoundException($"MachineWall vertex shader not found for {ext}.");
		var fragmentCode = Assets.TryReadCompiledShader($"MachineWall.fragment.{ext}")
			?? throw new FileNotFoundException($"MachineWall fragment shader not found for {ext}.");
		vertexShader = new Shader(graphicsDevice, ShaderStage.Vertex, vertexCode, uniformBufferCount: 1,
			storageBufferCount: 1, entryPoint: "vertex_main", name: "MachineWallVertex");
		fragmentShader = new Shader(graphicsDevice, ShaderStage.Fragment, fragmentCode, samplerCount: 2,
			uniformBufferCount: 1, entryPoint: "fragment_main", name: "MachineWallFragment");

		var pipeVertexCode = Assets.TryReadCompiledShader($"MachineWallPipe.vertex.{ext}")
			?? throw new FileNotFoundException($"MachineWallPipe vertex shader not found for {ext}.");
		var pipeFragmentCode = Assets.TryReadCompiledShader($"MachineWallPipe.fragment.{ext}")
			?? throw new FileNotFoundException($"MachineWallPipe fragment shader not found for {ext}.");
		pipeVertexShader = new Shader(graphicsDevice, ShaderStage.Vertex, pipeVertexCode, uniformBufferCount: 1,
			storageBufferCount: 1, entryPoint: "vertex_main", name: "MachineWallPipeVertex");
		pipeFragmentShader = new Shader(graphicsDevice, ShaderStage.Fragment, pipeFragmentCode, samplerCount: 1,
			uniformBufferCount: 1, entryPoint: "fragment_main", name: "MachineWallPipeFragment");
	}

	public void Render(Target target, List<Actor> actors, Vector2 camera, float zoom, float time, Pass pass)
	{
		var skew = ProjectionSkew;
		var vertexParams = new VertexParams
		{
			Camera = camera,
			TargetSize = new(target.Width, target.Height),
			ProjectionSkew = skew,
			Zoom = zoom,
			Time = time,
		};
		var view = Vector3.Normalize(new Vector3(-skew, 1));
		var light = LightDirection;
		var parameters = new FragmentParams
		{
			LightSpecular = new(light, SpecularStrength),
			ViewNormalPass = new(view, (float)pass),
			NormalSettings = new(CoarseNormalStrength, DetailNormalStrength, 0, 0),
		};

		foreach (var actor in actors)
			if (actor is ProceduralWall { Visible: true } wall && walls.TryGetValue(wall, out var data) && data.Instances.Count > 0)
			{
				EnsureMaterial(wall, data);
				parameters.LightSpecular.W = SpecularStrength * MathF.Max(wall.Specular, 0);
				parameters.NormalSettings = new(
					CoarseNormalStrength * MathF.Max(wall.NormalStrength, 0),
					DetailNormalStrength * MathF.Max(wall.NormalStrength, 0),
					2.7f * MathF.Max(wall.DetailScale, 0.01f), 0);
				parameters.ActorFieldSettings = new(
					4 + (int)wall.DitherPattern,
					EncodeDitherScale(wall.DitherScale),
					Math.Clamp(wall.DitherDetailPreservation, 0, 1), 0);
				data.Material!.Vertex.SetUniformBuffer(vertexParams);
				data.PipeMaterial!.Vertex.SetUniformBuffer(vertexParams);
				data.Material.Fragment.SetUniformBuffer(parameters);
				var pipeParameters = parameters;
				pipeParameters.NormalSettings.X = PipeNormalStrength * MathF.Max(wall.NormalStrength, 0);
				pipeParameters.NormalSettings.Y = MathF.Max(PipeDetail(wall), 0.01f);
				data.PipeMaterial.Fragment.SetUniformBuffer(pipeParameters);
				var blend = pass == Pass.Normals ? NormalBufferBlend : BlendMode.Premultiply;
				DrawCubes(target, data, blend);
				if (pass != Pass.Emissive)
					DrawPipes(target, data, blend);
			}
	}

	static float EncodeDitherScale(float scale)
		=> Math.Clamp(MathF.Round(MathF.Log2(MathF.Max(scale, 0.25f)) * 8 + 16), 0, 31);

	static float PipeDetail(ProceduralWall wall) => wall switch
	{
		MachineWall machine => machine.PipeDetail,
		HyphaWall hypha => hypha.RootDetail,
		StrataWall strata => strata.CrackDetail,
		CircuitWall circuit => circuit.TraceDetail,
		_ => 1,
	};

	void DrawCubes(Target target, WallData data, BlendMode blend)
		=> new DrawCommand(target, mesh, data.Material!)
		{
			InstanceCount = data.Instances.Count,
			VertexStorageBuffers = [data.InstancesBuffer],
			BlendMode = blend,
		}.Submit(graphicsDevice);

	void DrawPipes(Target target, WallData data, BlendMode blend)
	{
		if (data.StraightPipes.Count > 0)
			new DrawCommand(target, straightPipeMesh, data.PipeMaterial!)
			{
				InstanceCount = data.StraightPipes.Count,
				VertexStorageBuffers = [data.StraightPipeBuffer],
				BlendMode = blend,
			}.Submit(graphicsDevice);
		if (data.ElbowPipes.Count > 0)
			new DrawCommand(target, elbowPipeMesh, data.PipeMaterial!)
			{
				InstanceCount = data.ElbowPipes.Count,
				VertexStorageBuffers = [data.ElbowPipeBuffer],
				BlendMode = blend,
			}.Submit(graphicsDevice);
	}

	void EnsureMaterial(ProceduralWall wall, WallData data)
	{
		var pattern = Assets.Textures2D?.Get(wall.PatternTexture)
			?? throw new FileNotFoundException($"MachineWall texture '{wall.PatternTexture}' not found.");
		var detail = Assets.Textures2D?.Get(wall.DetailTexture)
			?? throw new FileNotFoundException($"MachineWall texture '{wall.DetailTexture}' not found.");
		if (data.Material != null && data.PatternTexture == pattern && data.DetailTexture == detail)
			return;

		var patternMaterial = GetMaterialTexture(pattern);
		var detailMaterial = GetMaterialTexture(detail);
		data.PatternTexture = pattern;
		data.DetailTexture = detail;
		data.Material ??= new(vertexShader, fragmentShader);
		data.PipeMaterial ??= new(pipeVertexShader, pipeFragmentShader);
		var sampler = new TextureSampler(TextureFilter.Linear, TextureWrap.Repeat, TextureWrap.Repeat);
		data.Material.Fragment.Samplers[0] = new(patternMaterial, sampler);
		data.Material.Fragment.Samplers[1] = new(detailMaterial, sampler);
		data.PipeMaterial.Fragment.Samplers[0] = new(detailMaterial, sampler);
	}

	Texture GetMaterialTexture(Assets.BucketTexture source)
	{
		if (materialTextures.TryGetValue(source, out var texture))
			return texture;

		using var image = new Image(source.SourceImage);
		using var normal = Assets.GenerateNormalImage(image, wrap: true);
		using var packed = new Image(image.Width, image.Height);
		for (var i = 0; i < packed.PixelCount; i++)
			packed[i] = new Color(image[i].R, normal[i].R, normal[i].G, normal[i].B);
		texture = new Texture(graphicsDevice, packed, name: $"MachineWallMaterial:{source.Key}");
		materialTextures.Add(source, texture);
		return texture;
	}

	public bool Prepare(List<Actor> actors)
	{
		var hasVisibleWalls = false;
		foreach (var actor in actors)
			if (actor is ProceduralWall wall)
			{
				if (!walls.TryGetValue(wall, out var data))
					walls.Add(wall, data = new(graphicsDevice));
				var hash = WallLayoutHash(wall);
				if (!data.HasLayout || data.LayoutHash != hash)
					BuildWall(wall, data, hash);
				hasVisibleWalls |= wall.Visible && data.Instances.Count > 0;
			}

		staleWalls.Clear();
		foreach (var wall in walls.Keys)
			if (!actors.Contains(wall))
				staleWalls.Add(wall);
		foreach (var wall in staleWalls)
		{
			walls[wall].Dispose();
			walls.Remove(wall);
		}
		return hasVisibleWalls;
	}

	static int WallLayoutHash(ProceduralWall wall)
	{
		var hash = RuntimeHelpers.GetHashCode(wall);
		hash = unchecked(hash * 31 + wall.Visible.GetHashCode());
		hash = unchecked(hash * 31 + wall.Position.GetHashCode());
		hash = unchecked(hash * 31 + wall.Size.GetHashCode());
		hash = unchecked(hash * 31 + wall.Seed);
		hash = unchecked(hash * 31 + wall.TextureScale.GetHashCode());
		if (wall is MachineWall machine)
		{
			hash = unchecked(hash * 31 + machine.MetalColor.GetHashCode());
			hash = unchecked(hash * 31 + machine.Motion.GetHashCode());
			hash = unchecked(hash * 31 + machine.GreebleScale.GetHashCode());
			hash = unchecked(hash * 31 + machine.GreebleDepth.GetHashCode());
			hash = unchecked(hash * 31 + machine.PipeDensity.GetHashCode());
			hash = unchecked(hash * 31 + machine.PipeMinWidth.GetHashCode());
			hash = unchecked(hash * 31 + machine.PipeMaxWidth.GetHashCode());
			hash = unchecked(hash * 31 + machine.PipeBend.GetHashCode());
			hash = unchecked(hash * 31 + machine.PipeDepth.GetHashCode());
			hash = unchecked(hash * 31 + machine.PipeRoughness.GetHashCode());
			hash = unchecked(hash * 31 + machine.PipeColor1.GetHashCode());
			hash = unchecked(hash * 31 + machine.PipeColor2.GetHashCode());
			hash = unchecked(hash * 31 + machine.PipeColor3.GetHashCode());
			hash = unchecked(hash * 31 + machine.PipeColor4.GetHashCode());
			hash = unchecked(hash * 31 + machine.ControlPanelDensity.GetHashCode());
			hash = unchecked(hash * 31 + machine.LedBrightness.GetHashCode());
		}
		else if (wall is HyphaWall hypha)
		{
			hash = unchecked(hash * 31 + hypha.SurfaceColor.GetHashCode());
			hash = unchecked(hash * 31 + hypha.PlateColor.GetHashCode());
			hash = unchecked(hash * 31 + hypha.VeinColor.GetHashCode());
			hash = unchecked(hash * 31 + hypha.GlowColor1.GetHashCode());
			hash = unchecked(hash * 31 + hypha.GlowColor2.GetHashCode());
			hash = unchecked(hash * 31 + hypha.GrowthScale.GetHashCode());
			hash = unchecked(hash * 31 + hypha.BranchDensity.GetHashCode());
			hash = unchecked(hash * 31 + hypha.BranchWidthMin.GetHashCode());
			hash = unchecked(hash * 31 + hypha.BranchWidthMax.GetHashCode());
			hash = unchecked(hash * 31 + hypha.Taper.GetHashCode());
			hash = unchecked(hash * 31 + hypha.Curl.GetHashCode());
			hash = unchecked(hash * 31 + hypha.PlateDensity.GetHashCode());
			hash = unchecked(hash * 31 + hypha.PlateSize.GetHashCode());
			hash = unchecked(hash * 31 + hypha.PlateDepth.GetHashCode());
			hash = unchecked(hash * 31 + hypha.GlowDensity.GetHashCode());
			hash = unchecked(hash * 31 + hypha.GlowPulse.GetHashCode());
			hash = unchecked(hash * 31 + hypha.BreathingAmount.GetHashCode());
			hash = unchecked(hash * 31 + hypha.Wetness.GetHashCode());
		}
		else if (wall is StrataWall strata)
		{
			hash = unchecked(hash * 31 + strata.RockColor.GetHashCode());
			hash = unchecked(hash * 31 + strata.LayerColor.GetHashCode());
			hash = unchecked(hash * 31 + strata.FaultColor.GetHashCode());
			hash = unchecked(hash * 31 + strata.FossilColor.GetHashCode());
			hash = unchecked(hash * 31 + strata.CrystalColor1.GetHashCode());
			hash = unchecked(hash * 31 + strata.CrystalColor2.GetHashCode());
			hash = unchecked(hash * 31 + strata.LayerCount);
			hash = unchecked(hash * 31 + strata.LayerVariation.GetHashCode());
			hash = unchecked(hash * 31 + strata.LayerDepth.GetHashCode());
			hash = unchecked(hash * 31 + strata.FaultDensity.GetHashCode());
			hash = unchecked(hash * 31 + strata.FaultWidth.GetHashCode());
			hash = unchecked(hash * 31 + strata.FossilDensity.GetHashCode());
			hash = unchecked(hash * 31 + strata.CrystalDensity.GetHashCode());
			hash = unchecked(hash * 31 + strata.CrystalScale.GetHashCode());
			hash = unchecked(hash * 31 + strata.Erosion.GetHashCode());
		}
		else if (wall is CircuitWall circuit)
		{
			hash = unchecked(hash * 31 + circuit.BoardColor.GetHashCode());
			hash = unchecked(hash * 31 + circuit.ModuleColor.GetHashCode());
			hash = unchecked(hash * 31 + circuit.TraceColor1.GetHashCode());
			hash = unchecked(hash * 31 + circuit.TraceColor2.GetHashCode());
			hash = unchecked(hash * 31 + circuit.TraceColor3.GetHashCode());
			hash = unchecked(hash * 31 + circuit.ContactColor.GetHashCode());
			hash = unchecked(hash * 31 + circuit.SignalColor1.GetHashCode());
			hash = unchecked(hash * 31 + circuit.SignalColor2.GetHashCode());
			hash = unchecked(hash * 31 + circuit.GridScale.GetHashCode());
			hash = unchecked(hash * 31 + circuit.ModuleDensity.GetHashCode());
			hash = unchecked(hash * 31 + circuit.ModuleDepth.GetHashCode());
			hash = unchecked(hash * 31 + circuit.TraceDensity.GetHashCode());
			hash = unchecked(hash * 31 + circuit.TraceWidth.GetHashCode());
			hash = unchecked(hash * 31 + circuit.TraceDepth.GetHashCode());
			hash = unchecked(hash * 31 + circuit.ContactDensity.GetHashCode());
			hash = unchecked(hash * 31 + circuit.SignalDensity.GetHashCode());
			hash = unchecked(hash * 31 + circuit.SignalPulse.GetHashCode());
		}
		return hash;
	}

	void BuildWall(ProceduralWall wall, WallData data, int hash)
	{
		data.HasLayout = true;
		data.LayoutHash = hash;
		data.Instances.Clear();
		data.StraightPipes.Clear();
		data.ElbowPipes.Clear();
		if (wall.Visible)
		{
			if (wall is MachineWall machine)
				AddMachineWall(machine, data);
			else if (wall is HyphaWall hypha)
				AddHyphaWall(hypha, data);
			else if (wall is StrataWall strata)
				AddStrataWall(strata, data);
			else if (wall is CircuitWall circuit)
				AddCircuitWall(circuit, data);
		}
		data.Instances.Sort(static (a, b) => a.DepthMotion.X.CompareTo(b.DepthMotion.X));
		data.StraightPipes.Sort(static (a, b) => a.DepthUvRoughness.X.CompareTo(b.DepthUvRoughness.X));
		data.ElbowPipes.Sort(static (a, b) => a.DepthUvRoughness.X.CompareTo(b.DepthUvRoughness.X));
		data.InstancesBuffer.Clear();
		data.InstancesBuffer.Upload(CollectionsMarshal.AsSpan(data.Instances));
		data.StraightPipeBuffer.Clear();
		data.StraightPipeBuffer.Upload(CollectionsMarshal.AsSpan(data.StraightPipes));
		data.ElbowPipeBuffer.Clear();
		data.ElbowPipeBuffer.Upload(CollectionsMarshal.AsSpan(data.ElbowPipes));
	}

	void AddCircuitWall(CircuitWall wall, WallData data)
	{
		if (wall.Size.X <= 0 || wall.Size.Y <= 0)
			return;

		var rng = new Rng(wall.Seed);
		var board = (Vector4)wall.BoardColor;
		var module = (Vector4)wall.ModuleColor;
		var grid = MathF.Max(4, 8 * MathF.Max(wall.GridScale, 0.25f));
		AddStaticBlock(data, (Vector2)wall.Position + (Vector2)wall.Size * 0.5f, wall.Size, 1,
			new(board.X * 0.52f, board.Y * 0.58f, board.Z * 0.56f, 0.74f));

		var area = wall.Size.X * wall.Size.Y;
		var moduleCount = Math.Clamp((int)MathF.Round(area / 2400f * MathF.Max(wall.ModuleDensity, 0)), 0, 80);
		for (var i = 0; i < moduleCount; i++)
		{
			var width = rng.Int(1, 5) * grid;
			var height = rng.Int(1, 4) * grid;
			var center = (Vector2)wall.Position + new Vector2(
				(MathF.Floor(rng.Float(0, MathF.Max(wall.Size.X - width, 1)) / grid) + 0.5f) * grid + width * 0.5f,
				(MathF.Floor(rng.Float(0, MathF.Max(wall.Size.Y - height, 1)) / grid) + 0.5f) * grid + height * 0.5f);
			var variation = rng.Float(0.72f, 1.15f);
			AddStaticBlock(data, center, new(MathF.Max(2, width - 1.2f), MathF.Max(2, height - 1.2f)),
				rng.Float(3, 9) * MathF.Max(wall.ModuleDepth, 0),
				new(module.X * variation, module.Y * variation, module.Z * variation, rng.Float(0.42f, 0.7f)));
		}

		var traceCount = Math.Clamp((int)MathF.Round((area / 3000f + 4) * MathF.Max(wall.TraceDensity, 0)), 0, 80);
		Span<Vector2> points = stackalloc Vector2[4];
		Span<float> radii = stackalloc float[3];
		for (var i = 0; i < traceCount; i++)
		{
			var horizontal = rng.Boolean();
			var major = horizontal ? wall.Size.X : wall.Size.Y;
			var cross = horizontal ? wall.Size.Y : wall.Size.X;
			var a = MathF.Round(rng.Float(0.04f, 0.22f) * major / grid) * grid;
			var b = MathF.Round(rng.Float(0.34f, 0.66f) * major / grid) * grid;
			var c = MathF.Round(rng.Float(0.78f, 0.96f) * major / grid) * grid;
			var cross0 = MathF.Round(rng.Float(0.06f, 0.94f) * cross / grid) * grid;
			var cross1 = Math.Clamp(cross0 + rng.Int(-5, 6) * grid, grid, cross - grid);
			points[0] = horizontal ? new(a, cross0) : new(cross0, a);
			points[1] = horizontal ? new(b, cross0) : new(cross0, b);
			points[2] = horizontal ? new(b, cross1) : new(cross1, b);
			points[3] = horizontal ? new(c, cross1) : new(cross1, c);
			for (var p = 0; p < points.Length; p++)
				points[p] += wall.Position;
			var radius = MathF.Max(0.35f, wall.TraceWidth * rng.Float(0.32f, 0.62f));
			for (var segment = 0; segment < radii.Length; segment++)
				radii[segment] = radius * rng.Float(0.82f, 1.18f);
			AddRoundedPipe(data, points, radii, grid * 0.42f, rng.Float(8, 13) * MathF.Max(wall.TraceDepth, 0),
				CircuitTraceColor(wall, rng.Int(3)), rng.Float(0.25f, 0.55f), rng.Float());

			if (rng.Float() < Math.Clamp(wall.ContactDensity * 0.68f, 0, 1))
			{
				var contact = (Vector4)wall.ContactColor;
				var size = rng.Float(1.4f, 2.8f);
				AddStaticBlock(data, points[rng.Boolean() ? 1 : 2], new(size, size), rng.Float(13, 16),
					new(contact.X, contact.Y, contact.Z, 0.28f), flat: true);
			}

			if (rng.Float() < Math.Clamp(wall.SignalDensity * 0.24f, 0, 1))
			{
				var signal = (Vector4)(rng.Boolean() ? wall.SignalColor1 : wall.SignalColor2);
				var size = rng.Float(1.6f, 3.2f);
				AddAnimatedBlock(data, points[rng.Boolean() ? 0 : 3], new(size, size), 17,
					new(signal.X, signal.Y, signal.Z, 0.2f), rng.Float(Calc.TAU), 0, 0,
					emissive: rng.Float(0.8f, 1.25f), flat: true, pulse: MathF.Max(wall.SignalPulse, 0));
			}
		}
	}

	void AddStrataWall(StrataWall wall, WallData data)
	{
		if (wall.Size.X <= 0 || wall.Size.Y <= 0)
			return;

		var rng = new Rng(wall.Seed);
		var rock = (Vector4)wall.RockColor;
		var layer = (Vector4)wall.LayerColor;
		AddStaticBlock(data, (Vector2)wall.Position + (Vector2)wall.Size * 0.5f, wall.Size, 1,
			new(rock.X * 0.54f, rock.Y * 0.52f, rock.Z * 0.48f, 0.92f));

		var layerCount = Math.Clamp(wall.LayerCount, 1, 48);
		var layerHeight = wall.Size.Y / (float)layerCount;
		var erosion = Math.Clamp(wall.Erosion, 0, 1);
		for (var row = 0; row < layerCount; row++)
		{
			var y = wall.Position.Y + (row + 0.5f) * layerHeight + rng.Float(-1.5f, 1.5f) * wall.LayerVariation;
			var x = 0f;
			while (x < wall.Size.X)
			{
				var width = MathF.Min(rng.Float(18, 58) * MathF.Max(wall.LayerVariation, 0.2f), wall.Size.X - x);
				var gap = rng.Float(0.35f, 1.7f) * erosion;
				var height = MathF.Max(1, layerHeight - rng.Float(0.6f, 2.2f) * erosion);
				var variation = rng.Float(0.68f, 1.14f) * (row % 2 == 0 ? 0.9f : 1.05f);
				AddStaticBlock(data, new(wall.Position.X + x + width * 0.5f, y),
					new(MathF.Max(1, width - gap), height), rng.Float(2.5f, 8) * MathF.Max(wall.LayerDepth, 0),
					new(layer.X * variation, layer.Y * variation, layer.Z * variation, 0.86f));
				x += width;
			}
		}

		var area = wall.Size.X * wall.Size.Y;
		var faultCount = Math.Clamp((int)MathF.Round((area / 26000f + 1) * MathF.Max(wall.FaultDensity, 0)), 0, 12);
		Span<Vector2> segment = stackalloc Vector2[2];
		Span<float> radius = stackalloc float[1];
		for (var fault = 0; fault < faultCount; fault++)
		{
			var point = (Vector2)wall.Position + new Vector2(rng.Float(0, wall.Size.X), 0);
			var direction = new Vector2(rng.Float(-0.38f, 0.38f), 1).Normalized();
			var steps = Math.Clamp((int)(wall.Size.Y / 28f), 3, 10);
			for (var step = 0; step < steps; step++)
			{
				segment[0] = point;
				point += direction * wall.Size.Y / steps;
				point.X += rng.Float(-8, 8) * MathF.Max(wall.LayerVariation, 0);
				segment[1] = point;
				radius[0] = MathF.Max(0.45f, wall.FaultWidth * rng.Float(0.24f, 0.52f));
				AddRoundedPipe(data, segment, radius, 0, rng.Float(9, 13), wall.FaultColor, 0.94f, rng.Float());
			}
		}

		var fossilCount = Math.Clamp((int)MathF.Round(area / 14000f * MathF.Max(wall.FossilDensity, 0)), 0, 16);
		for (var fossil = 0; fossil < fossilCount; fossil++)
		{
			var center = (Vector2)wall.Position + new Vector2(rng.Float(8, wall.Size.X - 8), rng.Float(8, wall.Size.Y - 8));
			var size = rng.Float(3, 7);
			for (var arm = 0; arm < 3; arm++)
			{
				var angle = arm * MathF.PI / 3 + rng.Float(-0.18f, 0.18f);
				var axis = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * size;
				segment[0] = center - axis;
				segment[1] = center + axis;
				radius[0] = rng.Float(0.35f, 0.7f);
				AddRoundedPipe(data, segment, radius, 0, 10, wall.FossilColor, 0.8f, rng.Float());
			}
		}

		var crystalCount = Math.Clamp((int)MathF.Round(area / 18000f * MathF.Max(wall.CrystalDensity, 0)), 0, 14);
		for (var cluster = 0; cluster < crystalCount; cluster++)
		{
			var center = (Vector2)wall.Position + new Vector2(rng.Float(10, wall.Size.X - 10), rng.Float(10, wall.Size.Y - 10));
			var color = (Vector4)(rng.Boolean() ? wall.CrystalColor1 : wall.CrystalColor2);
			for (var shard = 0; shard < 3; shard++)
			{
				var size = new Vector2(rng.Float(2, 4), rng.Float(7, 15)) * MathF.Max(wall.CrystalScale, 0.1f);
				var angle = rng.Float(-0.72f, 0.72f);
				AddRotatedBlock(data, center + new Vector2((shard - 1) * 3, rng.Float(-2, 2)), size,
					rng.Float(11, 17), new(color.X, color.Y, color.Z, 0.34f), angle, emissive: 0.08f);
			}
		}
	}

	void AddHyphaWall(HyphaWall wall, WallData data)
	{
		if (wall.Size.X <= 0 || wall.Size.Y <= 0)
			return;

		var rng = new Rng(wall.Seed);
		var growth = MathF.Max(wall.GrowthScale, 0.1f);
		var surface = (Vector4)wall.SurfaceColor;
		var plate = (Vector4)wall.PlateColor;
		var vein = (Vector4)wall.VeinColor;
		AddStaticBlock(data, (Vector2)wall.Position + (Vector2)wall.Size * 0.5f, wall.Size, 1,
			new(surface.X * 0.46f, surface.Y * 0.52f, surface.Z * 0.48f, 0.9f));

		var area = wall.Size.X * wall.Size.Y;
		var plateCount = Math.Clamp((int)MathF.Round(area / 1500f * MathF.Max(wall.PlateDensity, 0)), 0, 96);
		for (var i = 0; i < plateCount; i++)
		{
			var scale = MathF.Max(wall.PlateSize, 0.1f) * growth;
			var size = new Vector2(rng.Float(10, 34), rng.Float(7, 24)) * scale;
			size = Vector2.Min(size, wall.Size);
			var center = (Vector2)wall.Position + new Vector2(
				rng.Float(size.X * 0.5f, wall.Size.X - size.X * 0.5f),
				rng.Float(size.Y * 0.5f, wall.Size.Y - size.Y * 0.5f));
			var variation = rng.Float(0.72f, 1.18f);
			AddAnimatedBlock(data, center, size, rng.Float(3, 9) * MathF.Max(wall.PlateDepth, 0),
				new(plate.X * variation, plate.Y * variation, plate.Z * variation, 0.68f + wall.Wetness * 0.18f),
				rng.Float(Calc.TAU), rng.Float(0.08f, 0.2f), rng.Float(0.15f, 0.75f) * MathF.Max(wall.BreathingAmount, 0));
		}

		var branchCount = Math.Clamp((int)MathF.Round((area / 7000f + 6) * MathF.Max(wall.BranchDensity, 0)), 0, 48);
		Span<Vector2> points = stackalloc Vector2[4];
		Span<float> radii = stackalloc float[3];
		Span<Vector2> offshoot = stackalloc Vector2[3];
		Span<float> offshootRadii = stackalloc float[2];
		for (var i = 0; i < branchCount; i++)
		{
			var horizontal = rng.Boolean();
			var width = (float)wall.Size.X;
			var height = (float)wall.Size.Y;
			var cross0 = rng.Float(0.08f, 0.92f) * (horizontal ? height : width);
			var cross1 = Math.Clamp(cross0 + rng.Float(-36, 36) * MathF.Max(wall.Curl, 0), 4, (horizontal ? height : width) - 4);
			var start = rng.Float(0.02f, 0.18f) * (horizontal ? width : height);
			var middle = rng.Float(0.35f, 0.65f) * (horizontal ? width : height);
			var end = rng.Float(0.82f, 0.98f) * (horizontal ? width : height);
			if (rng.Boolean())
				(start, end) = (end, start);
			points[0] = horizontal ? new(start, cross0) : new(cross0, start);
			points[1] = horizontal ? new(middle, cross0) : new(cross0, middle);
			points[2] = horizontal ? new(middle, cross1) : new(cross1, middle);
			points[3] = horizontal ? new(end, cross1) : new(cross1, end);
			for (var p = 0; p < points.Length; p++)
				points[p] += wall.Position;

			var minRadius = MathF.Max(0.5f, MathF.Min(wall.BranchWidthMin, wall.BranchWidthMax) * 0.5f * growth);
			var maxRadius = MathF.Max(minRadius, MathF.Max(wall.BranchWidthMin, wall.BranchWidthMax) * 0.5f * growth);
			var root = rng.Float(minRadius, maxRadius);
			for (var segment = 0; segment < radii.Length; segment++)
				radii[segment] = MathF.Max(0.45f, root * (1 - segment / 2f * Math.Clamp(wall.Taper, 0, 1) * 0.62f));
			var colorVariation = rng.Float(0.72f, 1.16f);
			AddRoundedPipe(data, points, radii, MathF.Max(3, root * 1.5f) * MathF.Max(wall.Curl, 0.15f),
				rng.Float(8, 15), new(vein.X * colorVariation, vein.Y * colorVariation, vein.Z * colorVariation, 1),
				Math.Clamp(0.78f - wall.Wetness * 0.5f, 0.08f, 0.9f), rng.Float());

			if (rng.Boolean())
			{
				var joint = points[rng.Boolean() ? 1 : 2];
				var reach = rng.Float(12, 34) * growth;
				var direction = horizontal ? new Vector2(0, rng.Boolean() ? -1 : 1) : new(rng.Boolean() ? -1 : 1, 0);
				offshoot[0] = joint;
				offshoot[1] = joint + direction * reach * 0.55f;
				offshoot[2] = offshoot[1] + (horizontal ? Vector2.UnitX : Vector2.UnitY) * rng.Float(-reach, reach);
				var twig = MathF.Max(0.45f, root * rng.Float(0.25f, 0.52f));
				offshootRadii[0] = twig;
				offshootRadii[1] = MathF.Max(0.4f, twig * (1 - Math.Clamp(wall.Taper, 0, 1) * 0.7f));
				AddRoundedPipe(data, offshoot, offshootRadii, MathF.Max(2, twig * 1.4f), rng.Float(9, 15),
					new(vein.X * colorVariation, vein.Y * colorVariation, vein.Z * colorVariation, 1),
					Math.Clamp(0.78f - wall.Wetness * 0.5f, 0.08f, 0.9f), rng.Float());
			}

			if (rng.Float() < Math.Clamp(wall.GlowDensity * 0.34f, 0, 1))
			{
				var glow = (Vector4)(rng.Boolean() ? wall.GlowColor1 : wall.GlowColor2);
				var nodeSize = rng.Float(2.5f, 5.5f) * growth;
				AddAnimatedBlock(data, points[rng.Boolean() ? 1 : 2], new(nodeSize, nodeSize), rng.Float(14, 18),
					new(glow.X, glow.Y, glow.Z, 0.25f), rng.Float(Calc.TAU), 0, 0,
					emissive: rng.Float(0.75f, 1.2f), flat: true, pulse: MathF.Max(wall.GlowPulse, 0));
			}
		}
	}

	void AddMachineWall(MachineWall wall, WallData data)
	{
		if (wall.Size.X <= 0 || wall.Size.Y <= 0)
			return;

		var tint = (Vector4)wall.MetalColor;
		data.Instances.Add(new Instance
		{
			Rect = new(wall.Position.X + wall.Size.X * 0.5f, wall.Position.Y + wall.Size.Y * 0.5f, wall.Size.X, wall.Size.Y),
			DepthMotion = new(1, 0, 0, 0),
			AxisAmount = new(0, 0, 1, 0),
			TintRoughness = new(tint.X * 0.52f, tint.Y * 0.56f, tint.Z * 0.60f, 0.82f),
			Uv = new(0, 0, MathF.Max(wall.Size.X / 48f, 1) * MathF.Max(wall.TextureScale, 0.01f),
				MathF.Max(wall.Size.Y / 48f, 1) * MathF.Max(wall.TextureScale, 0.01f)),
		});

		var moduleSize = Math.Max(2, (int)MathF.Round(BaseModuleSize * MathF.Max(wall.GreebleScale, 0.25f)));
		var columns = Math.Max(1, (wall.Size.X + moduleSize - 1) / moduleSize);
		var rows = Math.Max(1, (wall.Size.Y + moduleSize - 1) / moduleSize);
		var cells = columns * rows;
		if (occupied.Length < cells)
			occupied = new bool[cells];
		Array.Clear(occupied, 0, cells);

		var rng = new Rng(wall.Seed);
		for (var y = 0; y < rows; y++)
			for (var x = 0; x < columns; x++)
			{
				if (occupied[y * columns + x])
					continue;

				var roll = rng.Float();
				var preferred = roll < 0.12f ? 4 : roll < 0.32f ? 3 : roll < 0.68f ? 2 : 1;
				var block = LargestBlockThatFits(x, y, preferred, columns, rows);
				MarkOccupied(x, y, block, columns);

				var left = x * moduleSize;
				var top = y * moduleSize;
				var width = Math.Min(block * moduleSize, wall.Size.X - left);
				var height = Math.Min(block * moduleSize, wall.Size.Y - top);
				if (width <= 0 || height <= 0)
					continue;

				AddBlock(wall, data, ref rng, left, top, width, height, block, tint);
			}

		AddPipes(wall, data, ref rng);
		AddControlPanels(wall, data, ref rng, tint);
	}

	int LargestBlockThatFits(int x, int y, int preferred, int columns, int rows)
	{
		for (var size = preferred; size > 1; size--)
		{
			if (x + size > columns || y + size > rows)
				continue;
			var fits = true;
			for (var yy = y; yy < y + size && fits; yy++)
				for (var xx = x; xx < x + size; xx++)
					if (occupied[yy * columns + xx])
					{
						fits = false;
						break;
					}
			if (fits)
				return size;
		}
		return 1;
	}

	void MarkOccupied(int x, int y, int size, int columns)
	{
		for (var yy = y; yy < y + size; yy++)
			for (var xx = x; xx < x + size; xx++)
				occupied[yy * columns + xx] = true;
	}

	void AddBlock(MachineWall wall, WallData data, ref Rng rng, int left, int top, int width, int height, int block, Vector4 baseTint)
	{
		var motionRoll = rng.Float();
		var motionType = motionRoll < 0.55f ? 0 : motionRoll < 0.75f ? 1 : motionRoll < 0.825f ? 2 : motionRoll < 0.90f ? 3 : 4;
		var depth = (2.5f + block * 1.6f + rng.Float(0, 3.5f)) * MathF.Max(wall.GreebleDepth, 0);
		var amount = motionType switch
		{
			1 => depth * 0.45f,
			2 or 3 => MathF.Min(width, height) * 0.10f,
			4 => 0.58f,
			_ => 0,
		} * MathF.Max(wall.Motion, 0);
		var axis = motionType == 4
			? (rng.Boolean() ? Vector3.UnitX : Vector3.UnitY)
			: Vector3.UnitZ;
		var gap = block == 1 ? 0.7f : 1.0f;
		var variation = rng.Float(0.82f, 1.14f);

		data.Instances.Add(new Instance
		{
			Rect = new(
				wall.Position.X + left + width * 0.5f,
				wall.Position.Y + top + height * 0.5f,
				MathF.Max(1, width - gap),
				MathF.Max(1, height - gap)),
			DepthMotion = new(depth, motionType, rng.Float(Calc.TAU), rng.Float(0.18f, 0.42f)),
			AxisAmount = new(axis, amount),
			TintRoughness = new(baseTint.X * variation, baseTint.Y * variation, baseTint.Z * variation, rng.Float(0.42f, 0.72f)),
			Uv = new(rng.Float(), rng.Float(), MathF.Max(width / 42f, 0.35f) * MathF.Max(wall.TextureScale, 0.01f),
				MathF.Max(height / 42f, 0.35f) * MathF.Max(wall.TextureScale, 0.01f)),
			Flags = new(0, 0, rng.Float(), 0),
		});
	}

	void AddPipes(MachineWall wall, WallData data, ref Rng rng)
	{
		var width = (float)wall.Size.X;
		var height = (float)wall.Size.Y;
		if (width < 48 || height < 24)
			return;

		var count = Math.Clamp((int)MathF.Round((wall.Size.X * wall.Size.Y / 24000f + 2) * MathF.Max(wall.PipeDensity, 0)), 0, 24);
		Span<Vector2> points = stackalloc Vector2[4];
		Span<float> radii = stackalloc float[3];
		for (var i = 0; i < count; i++)
		{
			var horizontal = width >= 48 && (height < 48 || rng.Boolean());
			var major = horizontal ? width : height;
			var cross = horizontal ? height : width;
			if (major < 48 || cross < 20)
				continue;

			var a = rng.Float(major * 0.06f, major * 0.18f);
			var b = rng.Float(major * 0.34f, major * 0.58f);
			var c = rng.Float(major * 0.78f, major * 0.94f);
			var y0 = rng.Float(cross * 0.14f, cross * 0.86f);
			var direction = rng.Boolean() ? 1f : -1f;
			var y1 = Math.Clamp(y0 + direction * rng.Float(cross * 0.18f, cross * 0.42f), cross * 0.1f, cross * 0.9f);
			if (MathF.Abs(y1 - y0) < 12)
				y1 = Math.Clamp(y0 + direction * 12, cross * 0.1f, cross * 0.9f);
			if (rng.Boolean())
			{
				a = major - a;
				b = major - b;
				c = major - c;
			}

			points[0] = horizontal ? new(a, y0) : new(y0, a);
			points[1] = horizontal ? new(b, y0) : new(y0, b);
			points[2] = horizontal ? new(b, y1) : new(y1, b);
			points[3] = horizontal ? new(c, y1) : new(y1, c);
			for (var p = 0; p < points.Length; p++)
				points[p] += wall.Position;

			var minRadius = MathF.Max(0.5f, MathF.Min(wall.PipeMinWidth, wall.PipeMaxWidth) * 0.5f);
			var maxRadius = MathF.Max(minRadius, MathF.Max(wall.PipeMinWidth, wall.PipeMaxWidth) * 0.5f);
			for (var segment = 0; segment < radii.Length; segment++)
				radii[segment] = rng.Float(minRadius, maxRadius);
			var largestRadius = MathF.Max(radii[0], MathF.Max(radii[1], radii[2]));
			AddRoundedPipe(data, points, radii, MathF.Max(4, largestRadius * 1.4f) * MathF.Max(wall.PipeBend, 0.1f),
				(13 + rng.Float(0, 4)) * MathF.Max(wall.PipeDepth, 0), PipeColor(wall, rng.Int(4)),
				Math.Clamp(wall.PipeRoughness, 0, 1), rng.Float());
		}
	}

	void AddRoundedPipe(WallData data, ReadOnlySpan<Vector2> points, ReadOnlySpan<float> radii,
		float preferredBend, float depth, Vector4 color, float roughness, float uvOffset)
	{
		Span<float> bends = stackalloc float[4];
		for (var i = 1; i < points.Length - 1; i++)
			bends[i] = MathF.Min(preferredBend,
				MathF.Min(Vector2.Distance(points[i - 1], points[i]), Vector2.Distance(points[i], points[i + 1])) * 0.38f);

		for (var i = 0; i < points.Length - 1; i++)
		{
			var direction = (points[i + 1] - points[i]).Normalized();
			var start = points[i] + direction * bends[i];
			var end = points[i + 1] - direction * bends[i + 1];
			var length = Vector2.Distance(start, end);
			if (length > 0.5f)
			{
				data.StraightPipes.Add(new PipeInstance
				{
					OriginScale = new((start + end) * 0.5f, length, radii[i]),
					Basis = new(direction.X, direction.Y, -direction.Y, direction.X),
					DepthUvRoughness = new(depth, uvOffset, MathF.Max(length / 6, 0.25f), roughness),
					Color = new(color.X, color.Y, color.Z, radii[i]),
				});
				uvOffset += length / 6;
			}

			if (i + 1 >= points.Length - 1)
				continue;
			var outgoing = (points[i + 2] - points[i + 1]).Normalized();
			var bend = bends[i + 1];
			var center = points[i + 1] - direction * bend + outgoing * bend;
			data.ElbowPipes.Add(new PipeInstance
			{
				OriginScale = new(center, bend, radii[i]),
				Basis = new(-outgoing.X, -outgoing.Y, direction.X, direction.Y),
				DepthUvRoughness = new(depth, uvOffset, MathF.Max(bend * MathF.PI * 0.5f / 6, 0.25f), roughness),
				Color = new(color.X, color.Y, color.Z, radii[i + 1]),
			});
			uvOffset += bend * MathF.PI * 0.5f / 6;
		}
	}

	void AddControlPanels(MachineWall wall, WallData data, ref Rng rng, Vector4 wallTint)
	{
		if (wall.Size.X < 24 || wall.Size.Y < 16)
			return;
		var count = Math.Clamp((int)MathF.Round((wall.Size.X * wall.Size.Y / 48000f + 1) * MathF.Max(wall.ControlPanelDensity, 0)), 0, 10);
		for (var i = 0; i < count; i++)
		{
			var width = MathF.Min(rng.Float(18, 34), wall.Size.X - 4);
			var height = MathF.Min(rng.Float(8, 14), wall.Size.Y - 4);
			var position = new Vector2(
				wall.Position.X + rng.Float(2, wall.Size.X - width - 2),
				wall.Position.Y + rng.Float(2, wall.Size.Y - height - 2));
			var depth = 14 + rng.Float(0, 3);
			AddStaticBlock(data, position + new Vector2(width, height) * 0.5f, new(width, height), depth,
				new(wallTint.X * 0.28f, wallTint.Y * 0.30f, wallTint.Z * 0.32f, 0.78f));

			var ledCount = Math.Clamp((int)(width - 6) / 5, 2, 5);
			var ledSize = rng.Float(2, 3.5f);
			var ledStart = position + new Vector2((width - (ledCount - 1) * 5) * 0.5f, height * 0.5f);
			for (var led = 0; led < ledCount; led++)
				AddStaticBlock(data, ledStart + Vector2.UnitX * led * 5, new(ledSize, ledSize), depth + 1.2f,
					LedColor(rng.Int(3)), emissive: rng.Float(0.85f, 1.25f) * MathF.Max(wall.LedBrightness, 0), flat: true);
		}
	}

	void AddStaticBlock(WallData data, Vector2 center, Vector2 size, float depth, Vector4 color, float emissive = 0, bool flat = false)
		=> data.Instances.Add(new Instance
		{
			Rect = new(center.X, center.Y, size.X, size.Y),
			DepthMotion = new(depth, 0, 0, 0),
			AxisAmount = new(0, 0, 1, 0),
			TintRoughness = color,
			Uv = new(0, 0, MathF.Max(size.X / 12, 0.2f), MathF.Max(size.Y / 12, 0.2f)),
			Flags = new(emissive, flat ? 1 : 0, 0, 0),
		});

	void AddAnimatedBlock(WallData data, Vector2 center, Vector2 size, float depth, Vector4 color,
		float phase, float speed, float amount, float emissive = 0, bool flat = false, float pulse = 0)
		=> data.Instances.Add(new Instance
		{
			Rect = new(center.X, center.Y, size.X, size.Y),
			DepthMotion = new(depth, amount > 0 ? 1 : 0, phase, speed),
			AxisAmount = new(0, 0, 1, amount),
			TintRoughness = color,
			Uv = new(phase / Calc.TAU, phase / Calc.TAU, MathF.Max(size.X / 12, 0.2f), MathF.Max(size.Y / 12, 0.2f)),
			Flags = new(emissive, flat ? 1 : 0, phase / Calc.TAU, pulse),
		});

	void AddRotatedBlock(WallData data, Vector2 center, Vector2 size, float depth, Vector4 color,
		float angle, float emissive = 0)
		=> data.Instances.Add(new Instance
		{
			Rect = new(center.X, center.Y, size.X, size.Y),
			DepthMotion = new(depth, 5, 0, 0),
			AxisAmount = new(0, 0, 1, angle),
			TintRoughness = color,
			Uv = new(0, 0, MathF.Max(size.X / 12, 0.2f), MathF.Max(size.Y / 12, 0.2f)),
			Flags = new(emissive, 0, 0, 0),
		});

	static Vector4 PipeColor(MachineWall wall, int index) => (Vector4)(index switch
	{
		0 => wall.PipeColor1,
		1 => wall.PipeColor2,
		2 => wall.PipeColor3,
		_ => wall.PipeColor4,
	});

	static Vector4 CircuitTraceColor(CircuitWall wall, int index) => (Vector4)(index switch
	{
		0 => wall.TraceColor1,
		1 => wall.TraceColor2,
		_ => wall.TraceColor3,
	});

	static Vector4 LedColor(int index)
	{
		var color = (Vector4)(index switch
		{
			0 => new Color(0xff4038),
			1 => new Color(0x5cff86),
			_ => new Color(0xf4f7ff),
		});
		color.W = 0.18f;
		return color;
	}

	static PipeVertex[] CreatePipeVertices(bool elbow)
	{
		const int ringSegments = 8;
		var lengthSegments = elbow ? 6 : 1;
		var vertices = new PipeVertex[(lengthSegments + 1) * (ringSegments + 1)];
		for (var along = 0; along <= lengthSegments; along++)
		{
			var u = along / (float)lengthSegments;
			var angle = u * MathF.PI * 0.5f;
			var centerline = elbow ? new Vector2(MathF.Cos(angle), MathF.Sin(angle)) : new Vector2(u - 0.5f, 0);
			var tangent = elbow ? new Vector2(-MathF.Sin(angle), MathF.Cos(angle)) : Vector2.UnitX;
			for (var around = 0; around <= ringSegments; around++)
			{
				var v = around / (float)ringSegments;
				var ring = v * MathF.Tau;
				var radial = elbow
					? new Vector3(centerline * MathF.Cos(ring), MathF.Sin(ring))
					: new Vector3(0, MathF.Cos(ring), MathF.Sin(ring));
				vertices[along * (ringSegments + 1) + around] = new(centerline, radial, tangent, new(u, v));
			}
		}
		return vertices;
	}

	static ushort[] CreatePipeIndices(bool elbow)
	{
		const int ringSegments = 8;
		var lengthSegments = elbow ? 6 : 1;
		var indices = new ushort[lengthSegments * ringSegments * 6];
		var index = 0;
		for (var along = 0; along < lengthSegments; along++)
			for (var around = 0; around < ringSegments; around++)
			{
				var a = (ushort)(along * (ringSegments + 1) + around);
				var b = (ushort)(a + ringSegments + 1);
				indices[index++] = a;
				indices[index++] = b;
				indices[index++] = (ushort)(a + 1);
				indices[index++] = (ushort)(a + 1);
				indices[index++] = b;
				indices[index++] = (ushort)(b + 1);
			}
		return indices;
	}

#if DEBUG
	public void ReloadShaders()
	{
		var ext = graphicsDevice.Driver.GetShaderExtension();
		vertexShader.Recreate(Assets.TryReadCompiledShader($"MachineWall.vertex.{ext}")!, "vertex_main");
		fragmentShader.Recreate(Assets.TryReadCompiledShader($"MachineWall.fragment.{ext}")!, "fragment_main");
		pipeVertexShader.Recreate(Assets.TryReadCompiledShader($"MachineWallPipe.vertex.{ext}")!, "vertex_main");
		pipeFragmentShader.Recreate(Assets.TryReadCompiledShader($"MachineWallPipe.fragment.{ext}")!, "fragment_main");
	}
#endif

	public void Dispose()
	{
		foreach (var data in walls.Values)
			data.Dispose();
		foreach (var texture in materialTextures.Values)
			texture.Dispose();
		mesh.Dispose();
		straightPipeMesh.Dispose();
		elbowPipeMesh.Dispose();
		vertexShader.Dispose();
		fragmentShader.Dispose();
		pipeVertexShader.Dispose();
		pipeFragmentShader.Dispose();
	}

	static readonly Vertex[] CubeVertices =
	[
		new(new(-.5f, -.5f, .5f), Vector3.UnitZ, new(0, 0), Vector3.UnitX),
		new(new(.5f, -.5f, .5f), Vector3.UnitZ, new(1, 0), Vector3.UnitX),
		new(new(.5f, .5f, .5f), Vector3.UnitZ, new(1, 1), Vector3.UnitX),
		new(new(-.5f, .5f, .5f), Vector3.UnitZ, new(0, 1), Vector3.UnitX),

		new(new(.5f, -.5f, -.5f), -Vector3.UnitZ, new(0, 0), -Vector3.UnitX),
		new(new(-.5f, -.5f, -.5f), -Vector3.UnitZ, new(1, 0), -Vector3.UnitX),
		new(new(-.5f, .5f, -.5f), -Vector3.UnitZ, new(1, 1), -Vector3.UnitX),
		new(new(.5f, .5f, -.5f), -Vector3.UnitZ, new(0, 1), -Vector3.UnitX),

		new(new(.5f, -.5f, .5f), Vector3.UnitX, new(0, 0), -Vector3.UnitZ),
		new(new(.5f, -.5f, -.5f), Vector3.UnitX, new(1, 0), -Vector3.UnitZ),
		new(new(.5f, .5f, -.5f), Vector3.UnitX, new(1, 1), -Vector3.UnitZ),
		new(new(.5f, .5f, .5f), Vector3.UnitX, new(0, 1), -Vector3.UnitZ),

		new(new(-.5f, -.5f, -.5f), -Vector3.UnitX, new(0, 0), Vector3.UnitZ),
		new(new(-.5f, -.5f, .5f), -Vector3.UnitX, new(1, 0), Vector3.UnitZ),
		new(new(-.5f, .5f, .5f), -Vector3.UnitX, new(1, 1), Vector3.UnitZ),
		new(new(-.5f, .5f, -.5f), -Vector3.UnitX, new(0, 1), Vector3.UnitZ),

		new(new(-.5f, .5f, .5f), Vector3.UnitY, new(0, 0), Vector3.UnitX),
		new(new(.5f, .5f, .5f), Vector3.UnitY, new(1, 0), Vector3.UnitX),
		new(new(.5f, .5f, -.5f), Vector3.UnitY, new(1, 1), Vector3.UnitX),
		new(new(-.5f, .5f, -.5f), Vector3.UnitY, new(0, 1), Vector3.UnitX),

		new(new(-.5f, -.5f, -.5f), -Vector3.UnitY, new(0, 0), Vector3.UnitX),
		new(new(.5f, -.5f, -.5f), -Vector3.UnitY, new(1, 0), Vector3.UnitX),
		new(new(.5f, -.5f, .5f), -Vector3.UnitY, new(1, 1), Vector3.UnitX),
		new(new(-.5f, -.5f, .5f), -Vector3.UnitY, new(0, 1), Vector3.UnitX),
	];

	static readonly ushort[] CubeIndices =
	[
		0, 1, 2, 0, 2, 3,
		4, 5, 6, 4, 6, 7,
		8, 9, 10, 8, 10, 11,
		12, 13, 14, 12, 14, 15,
		16, 17, 18, 16, 18, 19,
		20, 21, 22, 20, 22, 23,
	];
}
