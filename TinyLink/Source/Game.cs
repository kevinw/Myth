using System.Numerics;
using Foster.Framework;

namespace TinyLink;

public class Game
{
	public const int Width = 240;
	public const int Height = 135;
	public const int Columns = Width / TileSize;
	public const int Rows = Height / TileSize + 1;
	public const int TileSize = 8;
	public const float AudioPixelsPerUnit = 72;
	private const int ActorShadowSteps = 8;
	private const float ActorShadowStrength = 0.5f;
	private const float ActorShadowMinLength = 4.0f;
	private const float ActorShadowMaxLength = 28.0f;

	public readonly Manager Manager;
	public readonly Controls Controls;
	public readonly Batcher Batcher;
	public readonly Target Screen;
	public readonly Target BackgroundScreen;
	public readonly Target ForegroundScreen;
	public readonly Target LightMask;
	public readonly Target BackgroundLightMask;
	public readonly Target ForegroundLightMask;
	public readonly Target SingleLightMask;
	public readonly Target LitBackground;
	public readonly Target LitForeground;
	public readonly Target LitScreen;
	public readonly Target NormalBuffer;
	public readonly List<Actor> Actors = [];

	public Input Input => Manager.Input;
	public GraphicsDevice GraphicsDevice => Manager.GraphicsDevice;
	public Audio Audio => Manager.Audio;
	public Time Time => Manager.Time.MultiplyDelta(timeScale);
	public bool AudioEventsEnabled { get; }

	private Room? room;
	private Room? nextRoom;
	private float nextRoomEase;
	private Vector2 transitionCameraFrom;
	private Vector2 transitionCameraTo;
	private readonly List<Actor> destroying = [];
	private readonly List<Actor> rendering = [];
	private readonly bool[,] roomNormalMask = new bool[Width, Height];
	private readonly HashSet<string> audioFailures = [];
	private readonly ReloadableMaterial? crtMaterial;
	private readonly ReloadableMaterial? lightingCompositeMaterial;
	private readonly ReloadableMaterial? normalLightMaterial;
	private SoundInstance? music;
	private string? musicArea;
	private float hitStun = 0;
	private float shaking = 0;
	private Point2 shake;
	private Rng rng = new();
	private bool showCollisionRects;
	private bool showLightMask;
	private bool showNormalBuffer;
	private bool mouseLightEnabled;
	private bool actorSpriteShadowsEnabled = true;
	private Vector2 mouseLightPosition;
	private float timeScale = 1.0f;

	public Vector2 Camera { get; private set; }
	public RectInt Bounds => room?.WorldBounds ?? new();
	public Point2 Cell => room?.Cell ?? Point2.Zero;
	public Room? CurrentRoom => room;

	public Game(Manager manager, Point2 start, bool audioEventsEnabled = true)
		: this(manager, Assets.GetRoom(start), audioEventsEnabled)
	{
	}

	public Game(Manager manager, Room? start, bool audioEventsEnabled = true)
	{
		Manager = manager;
		AudioEventsEnabled = audioEventsEnabled;
		Batcher = new(manager.GraphicsDevice, name: "GameBatcher");
		Screen = new(manager.GraphicsDevice, Width, Height, name: "GameScreen");
		BackgroundScreen = new(manager.GraphicsDevice, Width, Height, name: "BackgroundScreen");
		ForegroundScreen = new(manager.GraphicsDevice, Width, Height, name: "ForegroundScreen");
		LightMask = new(manager.GraphicsDevice, Width, Height, name: "LightMask");
		BackgroundLightMask = new(manager.GraphicsDevice, Width, Height, name: "BackgroundLightMask");
		ForegroundLightMask = new(manager.GraphicsDevice, Width, Height, name: "ForegroundLightMask");
		SingleLightMask = new(manager.GraphicsDevice, Width, Height, name: "SingleLightMask");
		LitBackground = new(manager.GraphicsDevice, Width, Height, name: "LitBackground");
		LitForeground = new(manager.GraphicsDevice, Width, Height, name: "LitForeground");
		LitScreen = new(manager.GraphicsDevice, Width, Height, name: "LitScreen");
		NormalBuffer = new(manager.GraphicsDevice, Width, Height, name: "NormalBuffer");
		Controls = new(manager.Input);
		crtMaterial = CreateFragmentMaterial("Crt", samplerCount: 1, uniformBufferCount: 1);
		lightingCompositeMaterial = CreateFragmentMaterial("LightingComposite", samplerCount: 2);
		normalLightMaterial = CreateFragmentMaterial("NormalLight", samplerCount: 1, uniformBufferCount: 1);

		if (start != null)
		{
			room = start;
			Camera = ClampCameraToRoom(room.WorldBounds.TopLeft);
			UpdateAudioListener();
			LoadRoom(room);
			if (AudioEventsEnabled)
			{
				StartMusic();
				ApplyMusicArea(room);
			}
		}
	}

	public void Update()
	{
		// Don't run normal gameplay loop if no room is loaded
		if (room == null)
			return;

		// Run Game normally when not moving to a new room
		if (nextRoom == null)
		{
			// Reload Room Debug
			if (Input.Keyboard.Pressed(Keys.R))
				ReloadRoom();

			if (Input.Keyboard.Pressed(Keys.C))
				showCollisionRects = !showCollisionRects;

			if (Input.Keyboard.Pressed(Keys.L))
				showLightMask = !showLightMask;

			if (Input.Keyboard.Pressed(Keys.N))
				showNormalBuffer = !showNormalBuffer;

			if (Input.Keyboard.Pressed(Keys.M))
				mouseLightEnabled = !mouseLightEnabled;

			if (Input.Keyboard.Pressed(Keys.O))
			{
				actorSpriteShadowsEnabled = !actorSpriteShadowsEnabled;
				Console.WriteLine($"Actor sprite shadows: {(actorSpriteShadowsEnabled ? "on" : "off")}");
			}

			if (Input.Keyboard.Pressed(Keys.Minus, Keys.KeypadMinus))
				ChangeTimeScale(-1);
			if (Input.Keyboard.Pressed(Keys.Equals, Keys.KeypadPlus))
				ChangeTimeScale(1);

			// only run normal updates if no hitStun
			if (hitStun <= 0)
			{
				// Remove Destroyed Actors
				for (var i = 0; i < destroying.Count; i ++)
				{
					destroying[i].Destroyed();
					Actors.Remove(destroying[i]);
				}
				destroying.Clear();

				// Update Actors
				for (var i = 0; i < Actors.Count; i ++)
					Actors[i].Update();

				UpdateCamera();

				// screen shaking
				if (shaking > 0)
				{
					shaking -= Time.Delta;
					if (Time.OnInterval(0.05f))
						shake = new(rng.Sign(), rng.Sign());
				}
				else
					shake = Point2.Zero;
			}
			else
				hitStun -= Time.Delta;
		}
		// Lerp the Camera to the new Room
		else if (nextRoomEase < 1.0f)
		{
			Camera = Vector2.Lerp(transitionCameraFrom, transitionCameraTo, Ease.Cube.InOut(nextRoomEase));
			nextRoomEase = Calc.Approach(nextRoomEase, 1.0f, Time.Delta * 4.0f);
		}
		// Finished Lerping the Camera to the new room, return to normal update
		else
		{
			room = nextRoom;
			nextRoom = null;
			ApplyMusicArea(room);
			HitStun(0.1f);
		}

		UpdateAudioListener();
	}

	private void ChangeTimeScale(int direction)
	{
		var scales = ReadOnlySpan<float>.Empty;
		scales = [0.05f, 0.1f, 0.25f, 0.5f, 1.0f, 2.0f, 4.0f];

		var index = 0;
		var bestDistance = float.MaxValue;
		for (var i = 0; i < scales.Length; i ++)
		{
			var distance = MathF.Abs(scales[i] - timeScale);
			if (distance < bestDistance)
			{
				bestDistance = distance;
				index = i;
			}
		}

		index = Math.Clamp(index + direction, 0, scales.Length - 1);
		timeScale = scales[index];
		Console.WriteLine($"Time scale: {timeScale:0.##}x");
	}

	private void UpdateCamera()
	{
		if (room == null || nextRoom != null)
			return;

		if (GetFirst<Player>() is not { } player)
			return;

		Camera = ClampCameraToRoom(player.Position - new Vector2(Width, Height) / 2);
	}

	private Vector2 ClampCameraToRoom(Vector2 target)
	{
		if (room == null)
			return target;

		return ClampCameraToRoom(target, room.WorldBounds);
	}

	private static Vector2 ClampCameraToRoom(Vector2 target, RectInt bounds)
	{
		return new Vector2(
			ClampCameraAxis(target.X, bounds.Left, bounds.Right, Width),
			ClampCameraAxis(target.Y, bounds.Top, bounds.Bottom, Height));
	}

	private static float ClampCameraAxis(float value, float min, float max, float viewportSize)
	{
		var roomSize = max - min;
		if (roomSize <= viewportSize)
			return min + (roomSize - viewportSize) / 2;

		return Math.Clamp(value, min, max - viewportSize);
	}

	public void Render(in RectInt viewport, bool showHud = true, RectInt? scissor = null)
	{
		DrawWorldLayer(BackgroundScreen, background: true);
		DrawWorldLayer(ForegroundScreen, background: false);
		if (showHud)
			DrawPlayerHud(ForegroundScreen);

		UpdateMouseLightPosition(viewport);
		DrawNormalBuffer();

		var output = ApplyLighting();
		if (showLightMask)
			output = BackgroundLightMask;
		if (showNormalBuffer)
			output = NormalBuffer;

		// draw screen to window
		{
			var size = viewport.Size;
			var center = viewport.Center;
			var scale = Calc.Min(size.X / (float)output.Width, size.Y / (float)output.Height);

			Batcher.PushSampler(new(TextureFilter.Nearest, TextureWrap.Clamp, TextureWrap.Clamp));
			if (crtMaterial != null)
			{
				float CrtTime = Time.SecondsF * 0.2f;
				float Curvature = 0.005f;
				float BloomAmount = 0.19f;
				float ScanlineAmount = 0.07f;
				float VignetteAmount = 0.22f;
				float RgbOffset = 0.35f;
				crtMaterial.Material.Fragment.SetUniformBuffer([
					Screen.Width,
					Screen.Height,
					CrtTime,
					BloomAmount,
					ScanlineAmount,
					VignetteAmount,
					RgbOffset,
					Curvature
				]);
				Batcher.PushMaterial(crtMaterial.Material);
			}
			Batcher.Image(output, center, output.Bounds.Size / 2, Vector2.One * scale, 0, Color.White);
			if (crtMaterial != null)
				Batcher.PopMaterial();
			Batcher.PopSampler();
			Batcher.Render(Manager.Window, scissor: scissor);
			Batcher.Clear();
		}
	}

	private void DrawWorldLayer(Target target, bool background)
	{
		target.Clear(background ? 0x150e22 : Color.Transparent);
		Batcher.PushMatrix(-(Point2)Camera + shake);

		// draw actors
		rendering.AddRange(Actors);
		rendering.Sort((a, b) => b.Depth - a.Depth);
		foreach (var actor in rendering)
		{
			if (!actor.Visible)
				continue;
			if (IsBackgroundActor(actor) != background)
				continue;

			Batcher.PushMatrix(actor.Position);
			actor.Render(Batcher);
			Batcher.PopMatrix();
		}
		rendering.Clear();

		if (!background && showCollisionRects)
			DrawCollisionRects();

		Batcher.PopMatrix();
		Batcher.Render(target);
		Batcher.Clear();
	}

	private void DrawPlayerHud(Target target)
	{
		if (GetFirst<Player>() is { } player)
		{
			Point2 pos = new(0, Height - 16);
			Batcher.Rect(new Rect(pos.X, pos.Y + 7, 48, 4), Color.Black);

			for (int i = 0; i < Player.MaxHealth; i++)
			{
				if (player.Health >= i + 1)
					Batcher.Image(Assets.GetSubtexture("heart/0"), pos, Color.White);
				else
					Batcher.Image(Assets.GetSubtexture("heart/1"), pos, Color.White);
				pos.X += 12;
			}
		}

		Batcher.Render(target);
		Batcher.Clear();
	}

	private static bool IsBackgroundActor(Actor actor)
		=> actor is Grid grid && !grid.Mask.Has(Actor.Masks.Solid);

	private Target ApplyLighting()
	{
		if (lightingCompositeMaterial == null)
			return ForegroundScreen;

		DrawLightMask(BackgroundLightMask, includeShadows: true);
		DrawLightMask(ForegroundLightMask, includeShadows: false);

		CompositeLayer(BackgroundScreen, BackgroundLightMask, LitBackground);
		CompositeLayer(ForegroundScreen, ForegroundLightMask, LitForeground);

		LitScreen.Clear(Color.Transparent);
		Batcher.PushSampler(new(TextureFilter.Nearest, TextureWrap.Clamp, TextureWrap.Clamp));
		Batcher.Image(LitBackground, LitBackground.Bounds.Center, LitBackground.Bounds.Size / 2, Vector2.One, 0, Color.White);
		Batcher.Image(LitForeground, LitForeground.Bounds.Center, LitForeground.Bounds.Size / 2, Vector2.One, 0, Color.White);
		Batcher.PopSampler();
		Batcher.Render(LitScreen);
		Batcher.Clear();

		return LitScreen;
	}

	private void CompositeLayer(Target color, Target light, Target output)
	{
		output.Clear(Color.Transparent);
		lightingCompositeMaterial!.Material.Fragment.Samplers[1] = new(light, new(TextureFilter.Linear, TextureWrap.Clamp));

		Batcher.PushSampler(new(TextureFilter.Nearest, TextureWrap.Clamp, TextureWrap.Clamp));
		Batcher.PushMaterial(lightingCompositeMaterial.Material);
		Batcher.Image(color, color.Bounds.Center, color.Bounds.Size / 2, Vector2.One, 0, Color.White);
		Batcher.PopMaterial();
		Batcher.PopSampler();
		Batcher.Render(output);
		Batcher.Clear();
	}

	private void DrawLightMask(Target lightMask, bool includeShadows)
	{
		lightMask.Clear(new Color(0.42f, 0.42f, 0.46f, 1.0f));

		foreach (var actor in Actors)
		{
			if (actor is not Light light)
				continue;

			DrawSingleLightMask(light, includeShadows);
			AddSingleLightMask(lightMask);
		}

		if (mouseLightEnabled)
		{
			DrawSingleLightMask(mouseLightPosition, 48, 2.0f, 12, 0xffd37a, includeShadows);
			AddSingleLightMask(lightMask);
		}
	}

	private void DrawSingleLightMask(Light light, bool includeShadows)
		=> DrawSingleLightMask(
			light.Position,
			light.Radius * light.FlickerScale,
			light.Intensity * light.FlickerScale,
			light.Height,
			light.LightColor,
			includeShadows);

	private void DrawSingleLightMask(Vector2 position, float radius, float intensity, float height, Color lightColor, bool includeShadows)
	{
		SingleLightMask.Clear(Color.Transparent);

		if (normalLightMaterial != null)
		{
			var screenPosition = position - Camera + shake;
			var lightColorValue = (Vector4)lightColor;
			normalLightMaterial.Material.Fragment.SetUniformBuffer([
				(float)NormalBuffer.Width,
				(float)NormalBuffer.Height,
				0,
				0,
				screenPosition.X,
				screenPosition.Y,
				radius,
				height,
				lightColorValue.X,
				lightColorValue.Y,
				lightColorValue.Z,
				intensity
			]);

			Batcher.PushSampler(new(TextureFilter.Nearest, TextureWrap.Clamp, TextureWrap.Clamp));
			Batcher.PushMaterial(normalLightMaterial.Material);
			Batcher.Image(NormalBuffer, NormalBuffer.Bounds.Center, NormalBuffer.Bounds.Size / 2, Vector2.One, 0, Color.White);
			Batcher.PopMaterial();
			Batcher.PopSampler();
		}
		else
		{
			Batcher.PushMatrix(-(Point2)Camera + shake);
			var color = ScaleColor(lightColor, intensity);
			Batcher.Circle(position, radius, 32, color, Color.Transparent);
			Batcher.PopMatrix();
		}

		if (includeShadows)
		{
			Batcher.PushMatrix(-(Point2)Camera + shake);
			DrawLightShadows(position, radius);
			Batcher.PopMatrix();
		}

		Batcher.Render(SingleLightMask);
		Batcher.Clear();
	}

	private void AddSingleLightMask(Target lightMask)
	{
		Batcher.PushBlend(new (BlendOp.Add, BlendFactor.One, BlendFactor.One));
		Batcher.PushSampler(new(TextureFilter.Linear, TextureWrap.Clamp, TextureWrap.Clamp));
		Batcher.Image(SingleLightMask, SingleLightMask.Bounds.Center, SingleLightMask.Bounds.Size / 2, Vector2.One, 0, Color.White);
		Batcher.PopSampler();
		Batcher.PopBlend();
		Batcher.Render(lightMask);
		Batcher.Clear();
	}

	private void DrawLightShadows(Vector2 lightPosition, float radius)
	{
		if (room == null)
			return;

		foreach (var rect in room.LightOccluders)
		{
			if (!CircleOverlapsRect(lightPosition, radius, rect))
				continue;

			DrawShadowVolume(lightPosition, radius, rect);
		}

		if (actorSpriteShadowsEnabled)
			DrawActorSpriteShadows(lightPosition, radius);
	}

	private void DrawActorSpriteShadows(Vector2 lightPosition, float radius)
	{
		foreach (var actor in Actors)
		{
			if (!actor.Visible || !actor.CastsSpriteShadow)
				continue;
			if (actor is Grid or DualGrid)
				continue;

			var toActor = actor.Position - lightPosition;
			var distanceSquared = toActor.LengthSquared();
			if (distanceSquared <= 0.0001f || distanceSquared > radius * radius)
				continue;

			var distance = MathF.Sqrt(distanceSquared);
			var direction = toActor / distance;
			var reach = 1.0f - distance / radius;
			var shadowLength = ActorShadowMinLength + (ActorShadowMaxLength - ActorShadowMinLength) * reach;

			for (var i = 1; i <= ActorShadowSteps; i ++)
			{
				var t = i / (float)ActorShadowSteps;
				var offset = direction * shadowLength * t;
				var alpha = ActorShadowStrength * 0.25f * (1.0f - t * 0.75f);

				Batcher.PushMatrix(actor.Position + offset);
				actor.RenderShadow(Batcher, Color.Black * alpha);
				Batcher.PopMatrix();
			}
		}
	}

	private void DrawShadowVolume(Vector2 lightPosition, float radius, RectInt rect)
	{
		if (rect.Contains(lightPosition))
			return;

		Span<Vector2> corners =
		[
			new(rect.Left, rect.Top),
			new(rect.Right, rect.Top),
			new(rect.Right, rect.Bottom),
			new(rect.Left, rect.Bottom)
		];

		var bestA = corners[0];
		var bestB = corners[1];
		var bestSpan = -1.0f;

		for (var i = 0; i < corners.Length; i ++)
		for (var j = i + 1; j < corners.Length; j ++)
		{
			var a = DirectionFromLight(lightPosition, corners[i]);
			var b = DirectionFromLight(lightPosition, corners[j]);
			var span = 1 - Vector2.Dot(a, b);
			if (span > bestSpan)
			{
				bestSpan = span;
				bestA = corners[i];
				bestB = corners[j];
			}
		}

		var shadowLength = radius * 8.0f;
		var aFar = bestA + DirectionFromLight(lightPosition, bestA) * shadowLength;
		var bFar = bestB + DirectionFromLight(lightPosition, bestB) * shadowLength;
		Batcher.Quad(bestA, bestB, bFar, aFar, Color.Black);
	}

	private static Vector2 DirectionFromLight(Vector2 lightPosition, Vector2 point)
	{
		var direction = point - lightPosition;
		if (direction.LengthSquared() <= 0.0001f)
			return Vector2.Zero;

		return Vector2.Normalize(direction);
	}

	private static bool CircleOverlapsRect(Vector2 center, float radius, RectInt rect)
	{
		var x = Math.Clamp(center.X, rect.Left, rect.Right);
		var y = Math.Clamp(center.Y, rect.Top, rect.Bottom);
		var closest = new Vector2(x, y);
		return Vector2.DistanceSquared(center, closest) <= radius * radius;
	}

	private void DrawCollisionRects()
	{
		if (room == null)
			return;

		foreach (var rect in room.CollisionRects)
			Batcher.RectLine(rect, 1, Color.Red);
	}

	private void DrawNormalBuffer()
	{
		NormalBuffer.Clear(new Color(0.5f, 0.5f, 1.0f, 0.0f));
		Batcher.PushMatrix(-(Point2)Camera + shake);

		DrawRoomNormals();

		rendering.AddRange(Actors);
		rendering.Sort((a, b) => b.Depth - a.Depth);
		foreach (var actor in rendering)
		{
			if (!actor.Visible)
				continue;

			Batcher.PushMatrix(actor.Position);
			actor.RenderNormals(Batcher);
			Batcher.PopMatrix();
		}
		rendering.Clear();

		Batcher.PopMatrix();
		Batcher.Render(NormalBuffer);
		Batcher.Clear();
	}

	private void DrawRoomNormals()
	{
		if (room == null)
			return;

		Array.Clear(roomNormalMask);

		foreach (var rect in room.LightOccluders)
			FillRoomNormalMask(rect);

		var offset = room.WorldBounds.TopLeft;
		for (var y = 0; y < Height; y ++)
		for (var x = 0; x < Width; x ++)
		{
			if (!RoomNormalSolid(x, y))
				continue;

			var sx = SolidValue(x - 1, y) - SolidValue(x + 1, y);
			var sy = SolidValue(x, y - 1) - SolidValue(x, y + 1);
			if (sx == 0 && sy == 0)
				continue;

			Batcher.Rect(new Rect(offset.X + x, offset.Y + y, 1, 1), EncodeNormal(new Vector3(sx, sy, 1), 1));
		}
	}

	private void FillRoomNormalMask(RectInt rect)
	{
		if (room == null)
			return;

		var bounds = room.WorldBounds;
		var left   = Math.Clamp(rect.Left - bounds.Left, 0, Width);
		var right  = Math.Clamp(rect.Right - bounds.Left, 0, Width);
		var top    = Math.Clamp(rect.Top - bounds.Top, 0, Height);
		var bottom = Math.Clamp(rect.Bottom - bounds.Top, 0, Height);

		for (var y = top; y < bottom; y ++)
		for (var x = left; x < right; x ++)
			roomNormalMask[x, y] = true;
	}

	private bool RoomNormalSolid(int x, int y)
	{
		if (x < 0 || y < 0 || x >= Width || y >= Height)
			return false;

		return roomNormalMask[x, y];
	}

	private float SolidValue(int x, int y)
		=> RoomNormalSolid(x, y) ? 1 : 0;

	private static Color EncodeNormal(Vector3 normal, float strength)
	{
		normal = Vector3.Normalize(normal) * 0.5f + Vector3.One * 0.5f;
		return new Color(normal.X, normal.Y, normal.Z, strength);
	}

	private void UpdateMouseLightPosition(RectInt viewport)
	{
		var scale = Calc.Min(viewport.Width / (float)Width, viewport.Height / (float)Height);
		var size = new Vector2(Width, Height) * scale;
		var topLeft = (Vector2)viewport.Center - size / 2;
		var local = (Input.Mouse.Position - topLeft) / scale;
		mouseLightPosition = Camera - shake + local;
	}

#if DEBUG
	public void ReloadShaders()
	{
		crtMaterial?.Reload();
		lightingCompositeMaterial?.Reload();
		normalLightMaterial?.Reload();
	}
#endif

	private ReloadableMaterial? CreateFragmentMaterial(string name, int samplerCount, int uniformBufferCount = 0)
	{
		try
		{
			return new ReloadableMaterial(GraphicsDevice, name, samplerCount, uniformBufferCount);
		}
		catch(Exception e)
		{
			Console.Error.WriteLine($"Failed to load {name} shader: {e}");
			return null;
		}
	}

	private sealed class ReloadableMaterial
	{
		private readonly GraphicsDevice graphicsDevice;
		private readonly string name;
		private readonly int samplerCount;
		private readonly int uniformBufferCount;
		private Shader fragmentShader;

		public Material Material { get; }

		public ReloadableMaterial(GraphicsDevice graphicsDevice, string name, int samplerCount, int uniformBufferCount)
		{
			this.graphicsDevice = graphicsDevice;
			this.name = name;
			this.samplerCount = samplerCount;
			this.uniformBufferCount = uniformBufferCount;
			fragmentShader = CreateShader();
			Material = new Material(vertexShader: null, fragmentShader: fragmentShader);
		}

#if DEBUG
		public void Reload()
		{
			try
			{
				var next = CreateShader();
				Material.Fragment.Shader = next;
				fragmentShader.Dispose();
				fragmentShader = next;
				Console.WriteLine($"Reloaded {name} shader.");
			}
			catch (Exception e)
			{
				Console.Error.WriteLine($"Failed to reload {name} shader: {e}");
			}
		}
#endif

		private Shader CreateShader()
		{
			var ext = graphicsDevice.Driver.GetShaderExtension();
			var path = Path.Join(Assets.AssetsPath, "Shaders", "Compiled", $"{name}.fragment.{ext}");
			if (!File.Exists(path))
				throw new FileNotFoundException($"{name} shader not found at '{Path.GetFullPath(path)}'.", path);

			return new Shader(
				graphicsDevice,
				ShaderStage.Fragment,
				File.ReadAllBytes(path),
				samplerCount: samplerCount,
				uniformBufferCount: uniformBufferCount,
				entryPoint: "fragment_main",
				name: $"{name}Fragment");
		}
	}

	private static Color ScaleColor(Color color, float scale)
	{
		var value = (Vector4)color;
		value.X = Math.Clamp(value.X * scale, 0, 1);
		value.Y = Math.Clamp(value.Y * scale, 0, 1);
		value.Z = Math.Clamp(value.Z * scale, 0, 1);
		value.W = Math.Clamp(value.W * scale, 0, 1);
		return value;
	}

	public T Create<T>(Point2? position = null) where T : Actor, new()
	{
		var instance = new T
		{
			Game = this,
			Position = position ?? Point2.Zero
		};
		instance.Added();
		Actors.Add(instance);
		return instance;
	}

	public T? GetFirst<T>() where T : Actor
	{
		foreach (var it in Actors)
			if (it is T instance && !destroying.Contains(it))
				return instance;
		return null;
	}

	public Actor? GetFirst(Actor.Masks mask)
	{
		foreach (var it in Actors)
			if (it.Mask.Has(mask) && !destroying.Contains(it))
				return it;
		return null;
	}

	public void Destroy(Actor actor)
	{
		if (!destroying.Contains(actor))
			destroying.Add(actor);
	}

	public void PlayAudioEvent(string path, string parameterName = "", string parameterLabel = "", Vector2? position = null)
	{
		try
		{
			using var instance = CreateAudioEvent(path, position);
			if (instance == null)
				return;

			if (parameterName != "" || parameterLabel != "")
				instance.SetParameterLabel(parameterName, parameterLabel, true);

			instance.Start();
		}
		catch(Exception e)
		{
			Console.WriteLine(e);
		}
	}

	public SoundInstance? CreateAudioEvent(string path, Vector2? position = null)
	{
		if (!AudioEventsEnabled || !Audio.Initialized)
			return null;

		try
		{
			var instance = Audio.CreateEvent($"event:/{path}");
			if (position is { } pos)
				SetAudioEventPosition(instance, pos);
			return instance;
		}
		catch(Exception e)
		{
			Console.WriteLine(e);
			return null;
		}
	}

	public void SetAudioEventPosition(SoundInstance? instance, Vector2 position, Vector2? relativeTo = null)
	{
		if (instance == null || !Audio.Initialized)
			return;

		try
		{
			var effectivePosition = position;
			if (relativeTo is { } origin)
				effectivePosition = Camera + new Vector2(Width, Height) / 2 + position - origin;

			instance.Set3DAttributes(ToAudioPosition(effectivePosition));
		}
		catch(Exception e)
		{
			Console.WriteLine(e);
		}
	}

	private void UpdateAudioListener()
	{
		if (!Audio.Initialized)
			return;

		Audio.SetListenerAttributes(ToAudioPosition(Camera + new Vector2(Width, Height) / 2));
	}

	private static Vector3 ToAudioPosition(Vector2 position)
	{
		return new(-position.X / AudioPixelsPerUnit, position.Y / AudioPixelsPerUnit, 0);
	}

	public string GetSurfaceTypeBelow(Actor actor)
	{
		if (room == null || actor.Hitbox.Shape != Hitbox.Shapes.Rect)
			return Factory.DefaultSurfaceType;

		var bounds = actor.Hitbox.Rect + actor.Position;
		var y = bounds.Bottom;
		var left = GetSurfaceTypeAtWorldPoint(new(bounds.Left + 1, y));
		var right = GetSurfaceTypeAtWorldPoint(new(bounds.Right - 1, y));

		if (left != Factory.DefaultSurfaceType)
			return left;
		if (right != Factory.DefaultSurfaceType)
			return right;
		return left;
	}

	private string GetSurfaceTypeAtWorldPoint(Point2 point)
	{
		if (room == null)
			return Factory.DefaultSurfaceType;

		var local = point - room.WorldBounds.TopLeft;
		var tile = new Point2(
			(int)MathF.Floor(local.X / (float)TileSize),
			(int)MathF.Floor(local.Y / (float)TileSize));

		if (tile.X < 0 || tile.Y < 0 || tile.X >= room.Size.X || tile.Y >= room.Size.Y)
			return Factory.DefaultSurfaceType;

		if (Factory.Find(room.Tiles[tile.X, tile.Y]) is { SurfaceType: { } surfaceType })
			return surfaceType;

		return Factory.DefaultSurfaceType;
	}

	public void StopMusic(bool immediate = false) {
		if (music == null)
			return;

		try {
			music.Stop(immediate);
			music.Release();
		} catch (Exception e) {
			Console.Error.WriteLine($"Failed to stop background music: {e}");
		}

		music = null;
		musicArea = null;
	}

	public void RefreshMusic()
	{
		if (!Manager.MusicEnabled)
		{
			StopMusic(true);
			return;
		}

		if (room == null)
			return;

		StartMusic();
		ApplyMusicArea(room);
	}

	private void StartMusic()
	{
		if (!Manager.MusicEnabled || !Audio.Initialized || music != null)
			return;

		try
		{
			music = Audio.CreateEvent(Manager.BackgroundMusicEventPath);
			music.SetParameterLabel(Manager.BackgroundMusicAreaParameter, Manager.DefaultMusicArea, true);
			musicArea = Manager.DefaultMusicArea;
			music.Start();
		}
		catch (Exception e)
		{
			if (audioFailures.Add(Manager.BackgroundMusicEventPath))
				Console.Error.WriteLine($"Failed to start background music '{Manager.BackgroundMusicEventPath}': {e}");
			music = null;
			musicArea = null;
		}
	}

	private void ApplyMusicArea(Room room)
	{
		if (!Manager.MusicEnabled)
		{
			StopMusic(true);
			return;
		}

		if (string.IsNullOrEmpty(room.MusicArea))
			return;

		StartMusic();
		if (music == null || musicArea == room.MusicArea)
			return;

		try
		{
			music.SetParameterLabel(Manager.BackgroundMusicAreaParameter, room.MusicArea, true);
			musicArea = room.MusicArea;
		}
		catch (Exception e)
		{
			if (audioFailures.Add($"{Manager.BackgroundMusicAreaParameter}:{room.MusicArea}"))
				Console.Error.WriteLine($"Failed to set background music area '{room.MusicArea}': {e}");
		}
	}

	public bool Transition(Point2 direction)
	{
		if (room != null && nextRoom == null)
		{
			var exitPosition = GetFirst<Player>()?.Position ?? (Vector2)room.WorldBounds.Center;
			if (Assets.FindAdjacentRoom(room, direction, exitPosition) is { } foundRoom)
			{
				nextRoom = foundRoom;
				foreach (var it in Actors)
					if (it is not Player && !it.KeepOnRoomTransition)
						Destroy(it);

				transitionCameraFrom = Camera;
				transitionCameraTo = ClampCameraToRoom(exitPosition - new Vector2(Width, Height) / 2, nextRoom.WorldBounds);
				nextRoomEase = 0.0f;
				LoadRoom(nextRoom);
				ApplyMusicArea(nextRoom);
				return true;
			}
		}

		return false;
	}

	public bool OverlapsAll(in RectInt rect, Actor.Masks mask, List<Actor> results)
	{
		foreach (var actor in Actors)
		{
			var local = rect - actor.Position;
			if (actor.Mask.Has(mask) && actor.Hitbox.Overlaps(local))
				results.Add(actor);
		}

		return results.Count > 0;
	}

	public Actor? OverlapsFirst(in RectInt rect, Actor.Masks mask)
	{
		foreach (var actor in Actors)
		{
			var local = rect - actor.Position;
			if (actor.Mask.Has(mask) && actor.Hitbox.Overlaps(local))
				return actor;
		}

		return null;
	}

	public bool OverlapsRoomCollision(Actor actor, Point2 offset)
	{
		if (room == null || actor.Hitbox.Shape != Hitbox.Shapes.Rect)
			return false;

		var bounds = actor.Hitbox.Rect + actor.Position + offset;
		foreach (var rect in room.CollisionRects)
		{
			if (bounds.Overlaps(rect))
				return true;
		}

		return false;
	}

	public void ReloadRoom(bool restartMusic = false)
	{
		if (room == null) return;

		if (restartMusic)
			StopMusic(true);

		foreach (var actor in Actors)
			Destroy(actor);
		LoadRoom(room);

		if (restartMusic)
		{
			StartMusic();
			ApplyMusicArea(room);
		}
	}

	public void RefreshRoomTiles()
	{
		if (room == null)
			return;

		Grid? bg = null;
		Grid? fg = null;
		DualGrid? dual = null;
		foreach (var actor in Actors)
		{
			if (actor is DualGrid dualGrid)
			{
				dual = dualGrid;
				continue;
			}

			if (actor is not Grid grid)
				continue;

			if (grid.Mask.Has(Actor.Masks.Solid))
				fg = grid;
			else
				bg = grid;
		}

		if (bg == null || fg == null || dual == null)
			return;

		bg.Clear();
		fg.Clear();
		dual.Clear();

		for (var x = 0; x < room.Size.X; x ++)
		for (var y = 0; y < room.Size.Y; y ++)
		{
			var tile = new Point2(x, y);
			if (Factory.Find(room.Tiles[x, y]) is { } entry)
				entry.Tile?.Invoke(tile, room, fg, bg, dual);
		}

		BuildDualTiles();
	}

	void BuildDualTiles()
	{

	}

	void BuildDualTilesBroken()
	{
		if (room == null)
			return;

		DualGrid? dual = null;
		foreach (var actor in Actors)
		{
			if (actor is DualGrid dualGrid)
			{
				dual = dualGrid;
				continue;
			}
		}

		if (dual == null) return;

		dual.Clear();
		if (Assets.GetTileset("grass-dual") is { } grassDual)
		    dual.Build(room, 'G', grassDual);
		if (Assets.GetTileset("castle-dual") is { }  castleDual)
		    dual.Build(room, '1', castleDual);
	}

	public void LoadRoom(Room room)
	{
		room.RebuildGeometry();

		var offset = room.WorldBounds.TopLeft;

		var bg = Create<Grid>(offset);
		bg.Depth = 10;

		var dual = Create<DualGrid>(offset);
		dual.Depth = 7;

		var fg = Create<Grid>(offset);
		fg.Mask = Actor.Masks.Solid;
		fg.Depth = 5;

		// loop over room grid placing objects
		for (var x = 0; x < room.Size.X; x++)
		for (var y = 0; y < room.Size.Y; y++)
		{
			var tile = new Point2(x, y);
			var at = offset + tile * TileSize;

			if (Factory.Find(room.Tiles[x, y]) is { } entry)
			{
				entry.Spawn?.Invoke(at + entry.Offset, this);
				entry.Tile?.Invoke(tile, room, fg, bg, dual);
			}
		}

		BuildDualTiles();
	}

	public void HitStun(float time) => hitStun = MathF.Max(hitStun, time);

	public void Shake(float time) => shaking = MathF.Max(shaking, time);
}
