using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Foster.Framework;

namespace Myth;

sealed class MachineWallRenderer : IDisposable
{
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
		public Texture? PatternNormal;
		public Texture? DetailNormal;
		public string PatternTexture = "";
		public string DetailTexture = "";
		public int LayoutHash;
		public bool HasLayout;

		public void Dispose()
		{
			PatternNormal?.Dispose();
			DetailNormal?.Dispose();
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

	readonly GraphicsDevice graphicsDevice;
	readonly Mesh<Vertex, ushort> mesh;
	readonly Mesh<PipeVertex, ushort> straightPipeMesh;
	readonly Mesh<PipeVertex, ushort> elbowPipeMesh;
	readonly Dictionary<MachineWall, WallData> walls = [];
	readonly List<MachineWall> staleWalls = [];
	bool[] occupied = [];
	Shader vertexShader;
	Shader fragmentShader;
	Shader pipeVertexShader;
	Shader pipeFragmentShader;

	public MachineWallRenderer(GraphicsDevice graphicsDevice)
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
		fragmentShader = new Shader(graphicsDevice, ShaderStage.Fragment, fragmentCode, samplerCount: 4,
			uniformBufferCount: 1, entryPoint: "fragment_main", name: "MachineWallFragment");

		var pipeVertexCode = Assets.TryReadCompiledShader($"MachineWallPipe.vertex.{ext}")
			?? throw new FileNotFoundException($"MachineWallPipe vertex shader not found for {ext}.");
		var pipeFragmentCode = Assets.TryReadCompiledShader($"MachineWallPipe.fragment.{ext}")
			?? throw new FileNotFoundException($"MachineWallPipe fragment shader not found for {ext}.");
		pipeVertexShader = new Shader(graphicsDevice, ShaderStage.Vertex, pipeVertexCode, uniformBufferCount: 1,
			storageBufferCount: 1, entryPoint: "vertex_main", name: "MachineWallPipeVertex");
		pipeFragmentShader = new Shader(graphicsDevice, ShaderStage.Fragment, pipeFragmentCode, samplerCount: 2,
			uniformBufferCount: 1, entryPoint: "fragment_main", name: "MachineWallPipeFragment");
	}

	public void Render(Target color, Target normals, Target? emissive, Target? actorTileField,
		List<Actor> actors, Vector2 camera, float zoom, float time)
	{
		EnsureWalls(actors);

		var skew = ProjectionSkew;
		var vertexParams = new VertexParams
		{
			Camera = camera,
			TargetSize = new(color.Width, color.Height),
			ProjectionSkew = skew,
			Zoom = zoom,
			Time = time,
		};
		var view = Vector3.Normalize(new Vector3(-skew, 1));
		var light = LightDirection;
		var parameters = new FragmentParams
		{
			LightSpecular = new(light, SpecularStrength),
			ViewNormalPass = new(view, 0),
			NormalSettings = new(CoarseNormalStrength, DetailNormalStrength, 0, 0),
		};

		foreach (var actor in actors)
			if (actor is MachineWall { Visible: true } wall && walls.TryGetValue(wall, out var data) && data.Instances.Count > 0)
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
				pipeParameters.NormalSettings.Y = MathF.Max(wall.PipeDetail, 0.01f);
				data.PipeMaterial.Fragment.SetUniformBuffer(pipeParameters);
				DrawCubes(color, data);
				DrawPipes(color, data);
				parameters.ViewNormalPass.W = pipeParameters.ViewNormalPass.W = 1;
				data.Material.Fragment.SetUniformBuffer(parameters);
				data.PipeMaterial.Fragment.SetUniformBuffer(pipeParameters);
				DrawCubes(normals, data);
				DrawPipes(normals, data);
				parameters.ViewNormalPass.W = 0;
				if (emissive != null)
				{
					parameters.ViewNormalPass.W = 2;
					data.Material.Fragment.SetUniformBuffer(parameters);
					DrawCubes(emissive, data);
					parameters.ViewNormalPass.W = 0;
				}
				if (actorTileField != null)
				{
					parameters.ViewNormalPass.W = pipeParameters.ViewNormalPass.W = 3;
					data.Material.Fragment.SetUniformBuffer(parameters);
					data.PipeMaterial.Fragment.SetUniformBuffer(pipeParameters);
					DrawCubes(actorTileField, data);
					DrawPipes(actorTileField, data);
					parameters.ViewNormalPass.W = 0;
				}
			}
	}

	static float EncodeDitherScale(float scale)
		=> Math.Clamp(MathF.Round(MathF.Log2(MathF.Max(scale, 0.25f)) * 8 + 16), 0, 31);

	void DrawCubes(Target target, WallData data)
		=> new DrawCommand(target, mesh, data.Material!)
		{
			InstanceCount = data.Instances.Count,
			VertexStorageBuffers = [data.InstancesBuffer],
			BlendMode = BlendMode.Premultiply,
		}.Submit(graphicsDevice);

	void DrawPipes(Target target, WallData data)
	{
		if (data.StraightPipes.Count > 0)
			new DrawCommand(target, straightPipeMesh, data.PipeMaterial!)
			{
				InstanceCount = data.StraightPipes.Count,
				VertexStorageBuffers = [data.StraightPipeBuffer],
				BlendMode = BlendMode.Premultiply,
			}.Submit(graphicsDevice);
		if (data.ElbowPipes.Count > 0)
			new DrawCommand(target, elbowPipeMesh, data.PipeMaterial!)
			{
				InstanceCount = data.ElbowPipes.Count,
				VertexStorageBuffers = [data.ElbowPipeBuffer],
				BlendMode = BlendMode.Premultiply,
			}.Submit(graphicsDevice);
	}

	void EnsureMaterial(MachineWall wall, WallData data)
	{
		if (data.Material != null && data.PatternTexture == wall.PatternTexture && data.DetailTexture == wall.DetailTexture)
			return;

		var pattern = Assets.Textures2D?.Get(wall.PatternTexture)
			?? throw new FileNotFoundException($"MachineWall texture '{wall.PatternTexture}' not found.");
		var detail = Assets.Textures2D?.Get(wall.DetailTexture)
			?? throw new FileNotFoundException($"MachineWall texture '{wall.DetailTexture}' not found.");
		data.PatternNormal?.Dispose();
		data.DetailNormal?.Dispose();
		data.PatternNormal = CreateNormalTexture(pattern, "MachineWallPatternNormal");
		data.DetailNormal = CreateNormalTexture(detail, "MachineWallDetailNormal");
		data.PatternTexture = wall.PatternTexture;
		data.DetailTexture = wall.DetailTexture;
		data.Material ??= new(vertexShader, fragmentShader);
		data.PipeMaterial ??= new(pipeVertexShader, pipeFragmentShader);
		var sampler = new TextureSampler(TextureFilter.Linear, TextureWrap.Repeat, TextureWrap.Repeat);
		data.Material.Fragment.Samplers[0] = new(pattern.Texture, sampler);
		data.Material.Fragment.Samplers[1] = new(detail.Texture, sampler);
		data.Material.Fragment.Samplers[2] = new(data.PatternNormal, sampler);
		data.Material.Fragment.Samplers[3] = new(data.DetailNormal, sampler);
		data.PipeMaterial.Fragment.Samplers[0] = new(detail.Texture, sampler);
		data.PipeMaterial.Fragment.Samplers[1] = new(data.DetailNormal, sampler);
	}

	void EnsureWalls(List<Actor> actors)
	{
		foreach (var actor in actors)
			if (actor is MachineWall wall)
			{
				if (!walls.TryGetValue(wall, out var data))
					walls.Add(wall, data = new(graphicsDevice));
				var hash = WallLayoutHash(wall);
				if (!data.HasLayout || data.LayoutHash != hash)
					BuildWall(wall, data, hash);
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
	}

	static int WallLayoutHash(MachineWall wall)
	{
		var hash = RuntimeHelpers.GetHashCode(wall);
		hash = unchecked(hash * 31 + wall.Visible.GetHashCode());
		hash = unchecked(hash * 31 + wall.Position.GetHashCode());
		hash = unchecked(hash * 31 + wall.Size.GetHashCode());
		hash = unchecked(hash * 31 + wall.Seed);
		hash = unchecked(hash * 31 + wall.MetalColor.GetHashCode());
		hash = unchecked(hash * 31 + wall.Motion.GetHashCode());
		hash = unchecked(hash * 31 + wall.GreebleScale.GetHashCode());
		hash = unchecked(hash * 31 + wall.GreebleDepth.GetHashCode());
		hash = unchecked(hash * 31 + wall.TextureScale.GetHashCode());
		hash = unchecked(hash * 31 + wall.PipeDensity.GetHashCode());
		hash = unchecked(hash * 31 + wall.PipeMinWidth.GetHashCode());
		hash = unchecked(hash * 31 + wall.PipeMaxWidth.GetHashCode());
		hash = unchecked(hash * 31 + wall.PipeBend.GetHashCode());
		hash = unchecked(hash * 31 + wall.PipeDepth.GetHashCode());
		hash = unchecked(hash * 31 + wall.PipeRoughness.GetHashCode());
		hash = unchecked(hash * 31 + wall.PipeColor1.GetHashCode());
		hash = unchecked(hash * 31 + wall.PipeColor2.GetHashCode());
		hash = unchecked(hash * 31 + wall.PipeColor3.GetHashCode());
		hash = unchecked(hash * 31 + wall.PipeColor4.GetHashCode());
		hash = unchecked(hash * 31 + wall.ControlPanelDensity.GetHashCode());
		hash = unchecked(hash * 31 + wall.LedBrightness.GetHashCode());
		return hash;
	}

	void BuildWall(MachineWall wall, WallData data, int hash)
	{
		data.HasLayout = true;
		data.LayoutHash = hash;
		data.Instances.Clear();
		data.StraightPipes.Clear();
		data.ElbowPipes.Clear();
		if (wall.Visible)
			AddWall(wall, data);
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

	void AddWall(MachineWall wall, WallData data)
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

	static Vector4 PipeColor(MachineWall wall, int index) => (Vector4)(index switch
	{
		0 => wall.PipeColor1,
		1 => wall.PipeColor2,
		2 => wall.PipeColor3,
		_ => wall.PipeColor4,
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

	static Texture CreateNormalTexture(Assets.BucketTexture source, string name)
	{
		using var image = new Image(source.SourceImage);
		using var normal = Assets.GenerateNormalImage(image, wrap: true);
		return new Texture(source.Texture.GraphicsDevice, normal, name: name);
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
