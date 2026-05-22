using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Text.Json.Nodes;
using Foster.Framework;

namespace TinyLink;

public partial class Game : IDisposable
{
	public const int Width = 320;
	public const int Height = 180;
	public const int Columns = Width / TileSize;
	public const int Rows = Height / TileSize + 1;
	public const int TileSize = 8;
	public const float AudioPixelsPerUnit = 72;
	private const int ActorShadowSteps = 8;
	private const float ActorShadowStrength = 0.5f;
	private const float ActorShadowMinLength = 4.0f;
	private const float ActorShadowMaxLength = 28.0f;
	private static readonly BlendMode ShadowBlend = new(
		BlendOp.Add, BlendFactor.One, BlendFactor.OneMinusSrcAlpha,
		BlendOp.Add, BlendFactor.One, BlendFactor.OneMinusSrcAlpha,
		BlendMask.RGB, Color.White);
	private const float ReflectionOpacity = 0.5f;
	private const float CameraSlackEpsilon = TileSize;
	static float DitherGlitchAttackSpeed => 15.0f;
	static float DitherGlitchDecaySpeed => 1.35f;
	static float DitherGlitchRadius => 150.0f;
	private const float ActorDitherAmount = 0.2f;

	readonly Manager Manager;
	public readonly Controls Controls;
	readonly Batcher Batcher;
	public Target Screen { get; private set; } = null!;
	Target BackgroundScreen { get; set; } = null!;
	Target ForegroundScreen { get; set; } = null!;
	Target BackgroundEmissive { get; set; } = null!;
	Target ForegroundEmissive { get; set; } = null!;
	Target LightMask { get; set; } = null!;
	Target BackgroundLightMask { get; set; } = null!;
	Target ForegroundLightMask { get; set; } = null!;
	Target SingleLightMask { get; set; } = null!;
	Target LitBackground { get; set; } = null!;
	Target LitForeground { get; set; } = null!;
	Target LitScreen { get; set; } = null!;
	Target GlitchScreen { get; set; } = null!;
	Target NormalBuffer { get; set; } = null!;
	Target TileField { get; set; } = null!;
	Target DitherMask { get; set; } = null!;
	Target ReflectionScreen { get; set; } = null!;
	public readonly List<Actor> Actors = [];
	public bool ShowForegroundLayer { get; set; } = true;
	public bool ShowBackgroundLayer { get; set; } = true;

	Input Input => Manager.Input;
	GraphicsDevice GraphicsDevice => Manager.GraphicsDevice;
	Audio Audio => Manager.Audio;
	public Time Time => Manager.Time.MultiplyDelta(timeScale);
	public bool AudioEventsEnabled { get; }

	private Room? room;
	private Room? nextRoom;
	private float nextRoomEase;
	private Vector2 transitionCameraFrom;
	private Vector2 transitionCameraTo;
	private Color transitionAmbientColorFrom;
	private Color transitionAmbientColorTo;
	private float transitionAmbientIntensityFrom;
	private float transitionAmbientIntensityTo;
	private float transitionLightNoiseFrom;
	private float transitionLightNoiseTo;
	private Color transitionSunColorFrom;
	private Color transitionSunColorTo;
	private float transitionSunStrengthFrom;
	private float transitionSunStrengthTo;
	private Vector2 transitionSunDirectionFrom;
	private Vector2 transitionSunDirectionTo;
	private readonly List<Actor> destroying = [];
	private readonly List<Actor> rendering = [];
	private bool[,] roomNormalMask = new bool[Width, Height];
	private bool[,] roomNormalSuppressMask = new bool[Width, Height];
	private readonly HashSet<string> audioFailures = [];
	private readonly HashSet<string> sessionDestroyedActors = [];
	private readonly ReloadableMaterial? crtMaterial;
	private readonly ReloadableMaterial? lightingCompositeMaterial;
	private readonly ReloadableMaterial? normalLightMaterial;
	private readonly ReloadableMaterial? reflectionDistortMaterial;
	private readonly ReloadableMaterial? tileFieldDebugMaterial;
	private readonly ReloadableMaterial? pixelSortGlitchMaterial;
	private SoundInstance? music;
	private string? musicArea;
	private float? roomReverb;
	private float hitStun;
	private float shaking;
	private float ditherGlitch;
	private float ditherGlitchTarget;
	private Vector2 ditherGlitchAxis = Vector2.UnitX;
	private Point2 shake;
	internal Rng rng = new();

	enum DebugView
	{
		None,
		CollisionRects,
		LightMask,
		NormalBuffer,
		TileFieldRed,
		TileFieldGreen,
		TileFieldBlue,
		DitherMask,
		Visibility
	}

	private DebugView debugView;
	private float debugViewMessageTimer;
	public string? DebugViewMessage { get; private set; }

	private void ToggleDebugView(DebugView toggle)
	{
		SetDebugView(debugView == toggle ? DebugView.None : toggle);
	}

	private void CycleTileFieldDebugView()
	{
		SetDebugView(debugView switch
		{
			DebugView.TileFieldRed => DebugView.TileFieldGreen,
			DebugView.TileFieldGreen => DebugView.TileFieldBlue,
			DebugView.TileFieldBlue => DebugView.None,
			_ => DebugView.TileFieldRed
		});
	}

	private void SetDebugView(DebugView view)
	{
		debugView = view;
		DebugViewMessage = $"Debug: {DebugViewName(view)}";
		debugViewMessageTimer = 1.25f;
	}

	private static string DebugViewName(DebugView view)
		=> view switch
		{
			DebugView.None => "Off",
			DebugView.CollisionRects => "Collision Rects",
			DebugView.LightMask => "Light Mask",
			DebugView.NormalBuffer => "Normal Buffer",
			DebugView.TileFieldRed => "Tile Field R: Hash",
			DebugView.TileFieldGreen => "Tile Field G: Family",
			DebugView.TileFieldBlue => "Tile Field B: Solid",
			DebugView.DitherMask => "Dither Mask",
			DebugView.Visibility => "Visibility Polygon",
			_ => view.ToString()
		};

	private bool mouseLightEnabled;
	private bool draggingPlayer;
	private Vector2 mouseLightPosition;
	private float timeScale = 1.0f;
	private readonly Point2? respawnPosition;
	private bool disposed;

	public Vector2 Camera { get; private set; }
	public RectInt Bounds => room?.WorldBounds ?? new();
	public Point2 Cell => room?.Cell ?? Point2.Zero;
	public Room? CurrentRoom => room;

	public Game(Manager manager, Point2 start, bool audioEventsEnabled = true)
		: this(manager, Assets.GetRoom(start), audioEventsEnabled)
	{
	}

	public Game(Manager manager, Room? start, bool audioEventsEnabled = true, Point2? fallbackPlayerDirection = null, bool spawnFallbackPlayer = false, Point2? initialPlayerPosition = null)
	{
		Manager = manager;
		AudioEventsEnabled = audioEventsEnabled;
		Batcher = new(manager.GraphicsDevice, name: "GameBatcher");
		EnsureRenderTargets(Width, Height);
		Controls = new(manager.Input);
		crtMaterial = CreateFragmentMaterial("Crt", samplerCount: 1, uniformBufferCount: 1);
		lightingCompositeMaterial = CreateFragmentMaterial("LightingComposite", samplerCount: 5, uniformBufferCount: 1);
		normalLightMaterial = CreateFragmentMaterial("NormalLight", samplerCount: 1, uniformBufferCount: 1);
		reflectionDistortMaterial = CreateFragmentMaterial("ReflectionDistort", samplerCount: 1, uniformBufferCount: 1);
		tileFieldDebugMaterial = CreateFragmentMaterial("TileFieldDebug", samplerCount: 1, uniformBufferCount: 1);
		pixelSortGlitchMaterial = CreateFragmentMaterial("PixelSortGlitch", samplerCount: 2, uniformBufferCount: 1);

		if (start != null)
		{
			room = start.CloneRuntime();
			Camera = ClampCameraToRoom(room.WorldBounds.TopLeft);
			UpdateAudioListener();
			LoadRoom(room);
			if (initialPlayerPosition is { } position)
			{
				if (GetFirst<Player>() is { } player)
				{
					player.Position = position;
					player.Velocity = Vector2.Zero;
				}
				else if (spawnFallbackPlayer)
				{
					Create<Player>(position);
				}
				respawnPosition = position;
			}
			else if (spawnFallbackPlayer && GetFirst<Player>() == null)
			{
				respawnPosition = FindFallbackPlayerPosition(room, fallbackPlayerDirection);
				Create<Player>(respawnPosition);
			}
			else if (GetFirst<Player>() is { } player)
			{
				respawnPosition = player.Position;
			}
			if (AudioEventsEnabled)
			{
				StartMusic();
				ApplyMusicArea(room);
				ApplyRoomReverb(room);
			}
		}
	}

	public void Dispose()
	{
		if (disposed)
			return;

		disposed = true;
		StopPlayerHitSnapshot(true);
		StopMusic(true);

		var stopped = new HashSet<Actor>();
		foreach (var actor in Actors)
			if (stopped.Add(actor))
				actor.Destroyed();
		foreach (var actor in destroying)
			if (stopped.Add(actor))
				actor.Destroyed();

		Actors.Clear();
		destroying.Clear();
		rendering.Clear();
		DisposeRenderTargets();
		crtMaterial?.Dispose();
		lightingCompositeMaterial?.Dispose();
		normalLightMaterial?.Dispose();
		reflectionDistortMaterial?.Dispose();
		tileFieldDebugMaterial?.Dispose();
		Batcher.Dispose();
	}

	public void Update(bool allowKeyboardShortcuts = true)
	{
		CollisionRectsThisFrame.Clear();
		UpdatePlayerHitSnapshot();
		if (debugViewMessageTimer > 0)
		{
			debugViewMessageTimer -= Time.Delta;
			if (debugViewMessageTimer <= 0)
				DebugViewMessage = null;
		}
		var glitchSpeed = ditherGlitchTarget > ditherGlitch ? DitherGlitchAttackSpeed : DitherGlitchDecaySpeed;
		ditherGlitch = Calc.Approach(ditherGlitch, ditherGlitchTarget, Time.Delta * glitchSpeed);
		if (ditherGlitchTarget > 0 && ditherGlitch >= ditherGlitchTarget)
			ditherGlitchTarget = 0;

		// Don't run normal gameplay loop if no room is loaded
		if (room == null)
			return;

		// Run Game normally when not moving to a new room
		if (nextRoom == null)
		{
			if (allowKeyboardShortcuts)
			{
				// Reload Room Debug
				if (Input.Keyboard.Pressed(Keys.R))
					ReloadRoom();

				if (Input.Keyboard.Pressed(Keys.C)) ToggleDebugView(DebugView.CollisionRects);
				if (Input.Keyboard.Pressed(Keys.L)) ToggleDebugView(DebugView.LightMask);
				if (Input.Keyboard.Pressed(Keys.N)) ToggleDebugView(DebugView.NormalBuffer);
				if (Input.Keyboard.Pressed(Keys.T)) CycleTileFieldDebugView();
				if (Input.Keyboard.Pressed(Keys.M)) ToggleDebugView(DebugView.DitherMask);
				if (Input.Keyboard.Pressed(Keys.V)) ToggleDebugView(DebugView.Visibility);
				if (Input.Keyboard.Pressed(Keys.M)) mouseLightEnabled = !mouseLightEnabled;
			}

			#if DEBUG
			UpdatePlayerMouseDrag();
			if (allowKeyboardShortcuts)
			{
				if (Input.Keyboard.Pressed(Keys.Minus, Keys.KeypadMinus))
					ChangeTimeScale(-1);
				if (Input.Keyboard.Pressed(Keys.Equals, Keys.KeypadPlus))
					ChangeTimeScale(1);
			}
			#endif

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
	}

	private void UpdateCamera()
	{
		if (room == null || nextRoom != null)
			return;

		Camera = GetGameplayCameraTarget();
	}

	private void UpdatePlayerMouseDrag()
	{
		if (GetFirst<Player>() is not { } player)
			return;

		if (Input.Mouse.LeftPressed)
		{
			var mouse = Manager.GameMouseWorld(this);
			if (player.GetVisualBounds().Inflate(8).Contains(mouse))
				draggingPlayer = true;
		}

		if (!Input.Mouse.LeftDown)
		{
			draggingPlayer = false;
			return;
		}

		if (!draggingPlayer)
			return;

		player.Position = (Point2)Manager.GameMouseWorld(this);
		player.Velocity = Vector2.Zero;
	}

	private Point2 FindFallbackPlayerPosition(Room forRoom, Point2? direction)
	{
		var fromLeft = direction?.X > 0;
		var fromRight = direction?.X < 0;
		var fromTop = direction?.Y > 0;
		var fromBottom = direction?.Y < 0;
		var best = forRoom.WorldBounds.Center;
		var bestScore = float.MaxValue;
		var targetX = fromLeft ? 0 : fromRight ? forRoom.Size.X - 1 : (forRoom.Size.X - 1) * 0.5f;
		var targetY = fromTop ? 0 : fromBottom ? forRoom.Size.Y - 1 : (forRoom.Size.Y - 1) * 0.5f;

		for (var y = 1; y < forRoom.Size.Y; y ++)
		for (var x = 0; x < forRoom.Size.X; x ++)
		{
			if (!IsTileWalkable(forRoom, x, y - 1) || !IsTileSolid(forRoom, x, y))
				continue;

			var dx = x - targetX;
			var dy = y - targetY;
			var score = dx * dx + dy * dy * 0.25f;
			if (score >= bestScore)
				continue;

			bestScore = score;
			best = forRoom.WorldBounds.TopLeft + new Point2(x * TileSize + TileSize / 2, y * TileSize);
		}

		return best;
	}

	private static bool IsTileWalkable(Room room, int x, int y)
		=> x >= 0 && y >= 0 && x < room.Size.X && y < room.Size.Y && !IsTileSolid(room, x, y);

	private static bool IsTileSolid(Room room, int x, int y)
		=> room.GetEntry(new Point2(x, y)) is { BlocksMovement: true };

	public Vector2 GetGameplayCameraTarget()
	{
		if (room == null || GetFirst<Player>() is not { } player)
			return Camera;

		return ClampCameraToRoom(player.Position - new Vector2(Width, Height) / 2);
	}

	public void SnapCameraToGameplayTarget()
		=> Camera = GetGameplayCameraTarget();

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
		if (roomSize <= viewportSize + CameraSlackEpsilon)
			return min + (roomSize - viewportSize) / 2;

		return Math.Clamp(value, min, max - viewportSize);
	}

	public void Render(in RectInt viewport, bool showHud = true, RectInt? scissor = null)
	{
		EnsureRenderTargets(Width, Height);
		RenderCurrentView(Manager.Window, viewport, showHud, scissor, applyCrt: true);
	}

	public void RenderWorldView(in RectInt viewport, Rect worldView, bool showHud = false, RectInt? scissor = null, bool applyCrt = true)
	{
		var oldCamera = Camera;
		var oldShake = shake;
		var width = Math.Max(1, (int)MathF.Ceiling(worldView.Width));
		var height = Math.Max(1, (int)MathF.Ceiling(worldView.Height));

		EnsureRenderTargets(width, height);
		Camera = worldView.TopLeft;
		shake = Point2.Zero;
		RenderCurrentView(Manager.Window, viewport, showHud, scissor, applyCrt);
		Camera = oldCamera;
		shake = oldShake;
	}

	public void RenderWorldToTarget(Target target, Rect worldView, bool showHud = false)
	{
		var oldCamera = Camera;
		var oldShake = shake;
		var width = Math.Max(1, (int)MathF.Ceiling(worldView.Width));
		var height = Math.Max(1, (int)MathF.Ceiling(worldView.Height));

		EnsureRenderTargets(width, height);
		Camera = worldView.TopLeft;
		shake = Point2.Zero;
		RenderCurrentView(target, target.Bounds, showHud, scissor: null, applyCrt: false);
		Camera = oldCamera;
		shake = oldShake;
	}

	private void EnsureRenderTargets(int width, int height)
	{
		if (Screen != null && Screen.Width == width && Screen.Height == height)
			return;

		DisposeRenderTargets();

		Screen = new Target(GraphicsDevice, width, height, name: "GameScreen");
		BackgroundScreen = new Target(GraphicsDevice, width, height, name: "BackgroundScreen");
		ForegroundScreen = new Target(GraphicsDevice, width, height, name: "ForegroundScreen");
		BackgroundEmissive = new Target(GraphicsDevice, width, height, name: "BackgroundEmissive");
		ForegroundEmissive = new Target(GraphicsDevice, width, height, name: "ForegroundEmissive");
		LightMask = new Target(GraphicsDevice, width, height, name: "LightMask");
		BackgroundLightMask = new Target(GraphicsDevice, width, height, name: "BackgroundLightMask");
		ForegroundLightMask = new Target(GraphicsDevice, width, height, name: "ForegroundLightMask");
		SingleLightMask = new Target(GraphicsDevice, width, height, name: "SingleLightMask");
		LitBackground = new Target(GraphicsDevice, width, height, name: "LitBackground");
		LitForeground = new Target(GraphicsDevice, width, height, name: "LitForeground");
		LitScreen = new Target(GraphicsDevice, width, height, name: "LitScreen");
		GlitchScreen = new Target(GraphicsDevice, width, height, name: "GlitchScreen");
		NormalBuffer = new Target(GraphicsDevice, width, height, name: "NormalBuffer");
		TileField = new Target(GraphicsDevice, width, height, name: "TileField");
		DitherMask = new Target(GraphicsDevice, width, height, name: "DitherMask");
		ReflectionScreen = new Target(GraphicsDevice, width, height, name: "ReflectionScreen");
		roomNormalMask = new bool[width, height];
		roomNormalSuppressMask = new bool[width, height];
	}

	private void DisposeRenderTargets()
	{
		Screen?.Dispose();
		BackgroundScreen?.Dispose();
		ForegroundScreen?.Dispose();
		BackgroundEmissive?.Dispose();
		ForegroundEmissive?.Dispose();
		LightMask?.Dispose();
		BackgroundLightMask?.Dispose();
		ForegroundLightMask?.Dispose();
		SingleLightMask?.Dispose();
		LitBackground?.Dispose();
		LitForeground?.Dispose();
		LitScreen?.Dispose();
		GlitchScreen?.Dispose();
		NormalBuffer?.Dispose();
		TileField?.Dispose();
		DitherMask?.Dispose();
		ReflectionScreen?.Dispose();
	}

	private void RenderCurrentView(IDrawableTarget destination, in RectInt viewport, bool showHud, RectInt? scissor, bool applyCrt)
	{
		PrepareRenderingList();

		DrawWorldLayer(BackgroundScreen, background: true);
		DrawWorldLayer(ForegroundScreen, background: false);
		DrawEmissiveLayer(BackgroundEmissive, background: true);
		DrawEmissiveLayer(ForegroundEmissive, background: false);

		UpdateMouseLightPosition(viewport);
		DrawNormalBuffer();
		DrawTileField();
		DrawDitherMask();

		Target output = Screen;
		if (debugView is DebugView.TileFieldRed or DebugView.TileFieldGreen or DebugView.TileFieldBlue)
		{
			output = DrawTileFieldDebug();
		}
		else if (debugView == DebugView.DitherMask)
		{
			output = DitherMask;
		}
		else
		{
			output = ApplyLighting();
			if (debugView == DebugView.LightMask)
				output = BackgroundLightMask;
			else if (debugView == DebugView.NormalBuffer)
				output = NormalBuffer;
			else if (debugView == DebugView.None)
				output = ApplyPixelSortGlitch(output);
		}
		if (showHud && debugView is DebugView.None or DebugView.CollisionRects)
			DrawPlayerHud(output);
		if (debugView == DebugView.Visibility)
			DrawVisibilityDebug(output);

		// draw screen to window
		{
			var size = viewport.Size;
			var center = viewport.Center;
			var scale = Calc.Min(size.X / (float)output.Width, size.Y / (float)output.Height);

			Batcher.PushSampler(new(TextureFilter.Nearest, TextureWrap.Clamp, TextureWrap.Clamp));
			if (applyCrt && crtMaterial != null)
			{
				float CrtTime = Time.SecondsF * 0.2f;
				float Curvature = 0.005f;
				float BloomAmount = 0.19f;
				float ScanlineAmount = 0.22f;
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
			if (applyCrt && crtMaterial != null)
				Batcher.PopMaterial();
			Batcher.PopSampler();
			Batcher.Render(destination, scissor: scissor);
			Batcher.Clear();
		}
	}

	private void PrepareRenderingList()
	{
		rendering.Clear();
		rendering.AddRange(Actors);
		rendering.Sort((a, b) => b.Depth - a.Depth);
	}

	private Target DrawTileFieldDebug()
	{
		Screen.Clear(Color.Transparent);
		Batcher.PushSampler(new(TextureFilter.Nearest, TextureWrap.Clamp, TextureWrap.Clamp));
		if (tileFieldDebugMaterial != null)
		{
			tileFieldDebugMaterial.Material.Fragment.SetUniformBuffer([
				TileFieldDebugChannel(),
				0,
				1,
				0
			]);
			Batcher.PushMaterial(tileFieldDebugMaterial.Material);
		}
		Batcher.Image(TileField, TileField.Bounds.Center, TileField.Bounds.Size / 2, Vector2.One, 0, Color.White);
		if (tileFieldDebugMaterial != null)
			Batcher.PopMaterial();
		Batcher.PopSampler();
		Batcher.Render(Screen);
		Batcher.Clear();
		return Screen;
	}

	private int TileFieldDebugChannel()
		=> debugView switch
		{
			DebugView.TileFieldGreen => 1,
			DebugView.TileFieldBlue => 2,
			_ => 0
		};

	private void DrawWorldLayer(Target target, bool background)
	{
		target.Clear(background ? 0x150e22 : Color.Transparent);
		if ((background && !ShowBackgroundLayer) || (!background && !ShowForegroundLayer))
		{
			Batcher.Render(target);
			Batcher.Clear();
			return;
		}

		Batcher.PushMatrix(-(Point2)Camera + shake);

		// draw actors
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

		if (!background && debugView == DebugView.CollisionRects)
			DrawCollisionRects(); // TODO: don't do this in post-processing context

		Batcher.PopMatrix();
		Batcher.Render(target);
		Batcher.Clear();

		if (background)
			DrawSpriteReflections(target);
	}

	private void DrawPlayerHud(Target target)
	{
		if (GetFirst<Player>() is { } player)
		{
			Point2 pos = new(0, target.Height - 16);
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
		=> actor switch
		{
			Grid grid => grid.Layer == Factory.TileLayer.Background,
			DualGrid dualGrid => dualGrid.Layer == Factory.TileLayer.Background,
			_ => false
		};

	private void DrawEmissiveLayer(Target target, bool background)
	{
		target.Clear(Color.Transparent);
		Batcher.PushMatrix(-(Point2)Camera + shake);
		foreach (var actor in rendering)
		{
			if (!actor.Visible)
				continue;
			if (IsBackgroundActor(actor) != background)
				continue;

			Batcher.PushMatrix(actor.Position);
			actor.RenderEmissive(Batcher);
			Batcher.PopMatrix();
		}
		Batcher.PopMatrix();
		Batcher.Render(target);
		Batcher.Clear();
	}

	private void DrawSpriteReflections(Target target)
	{
		if (room == null)
			return;

		ReflectionScreen.Clear(Color.Transparent);
		foreach (var rect in room.ReflectionRects)
		{
			var scissor = GetReflectionScissor(rect);
			if (scissor.Width <= 0 || scissor.Height <= 0)
				continue;

			Batcher.PushMatrix(-(Point2)Camera + shake);
			foreach (var actor in rendering)
			{
				if (!actor.Visible || !actor.CastsReflection)
					continue;
				if (!ActorCanReflectIn(actor, rect))
					continue;

				Batcher.PushMatrix(Matrix3x2.CreateScale(1, -1, new Vector2(actor.Position.X, rect.Top)));
				Batcher.PushMatrix(actor.Position);
				actor.RenderReflection(Batcher, Color.White * ReflectionOpacity);
				Batcher.PopMatrix();
				Batcher.PopMatrix();
			}
			Batcher.PopMatrix();
			Batcher.Render(ReflectionScreen, scissor: scissor);
			Batcher.Clear();
		}

		CompositeReflections(target);
	}

	private RectInt GetReflectionScissor(RectInt rect)
	{
		var screenRect = new RectInt(
			rect.X - (int)Camera.X + shake.X,
			rect.Y - (int)Camera.Y + shake.Y,
			rect.Width,
			rect.Height);
		return screenRect.GetIntersection(ReflectionScreen.Bounds);
	}

	private void CompositeReflections(Target target)
	{
		Batcher.PushSampler(new(TextureFilter.Nearest, TextureWrap.Clamp, TextureWrap.Clamp));
		if (reflectionDistortMaterial != null)
		{
			reflectionDistortMaterial.Material.Fragment.SetUniformBuffer([
				ReflectionScreen.Width,
				ReflectionScreen.Height,
				Time.SecondsF,
				1.4f,
				0.10f,
				2.2f,
				0,
				0
			]);
			Batcher.PushMaterial(reflectionDistortMaterial.Material);
		}
		Batcher.Image(ReflectionScreen, ReflectionScreen.Bounds.Center, ReflectionScreen.Bounds.Size / 2, Vector2.One, 0, Color.White);
		if (reflectionDistortMaterial != null)
			Batcher.PopMaterial();
		Batcher.PopSampler();
		Batcher.Render(target);
		Batcher.Clear();
	}

	private static bool ActorCanReflectIn(Actor actor, RectInt rect)
	{
		var bounds = actor.GetVisualBounds().Inflate(4);
		if (bounds.Right <= rect.Left || bounds.Left >= rect.Right)
			return false;

		return bounds.Bottom >= rect.Top - 64 && bounds.Top <= rect.Bottom + 64;
	}

	private Target ApplyLighting()
	{
		if (lightingCompositeMaterial == null)
			return ForegroundScreen;

		DrawLightMask(BackgroundLightMask, includeShadows: true);
		DrawLightMask(ForegroundLightMask, includeShadows: false);

		CompositeLayer(BackgroundScreen, BackgroundLightMask, BackgroundEmissive, LitBackground);
		CompositeLayer(ForegroundScreen, ForegroundLightMask, ForegroundEmissive, LitForeground);

		LitScreen.Clear(Color.Transparent);
		Batcher.PushSampler(new(TextureFilter.Nearest, TextureWrap.Clamp, TextureWrap.Clamp));
		Batcher.Image(LitBackground, LitBackground.Bounds.Center, LitBackground.Bounds.Size / 2, Vector2.One, 0, Color.White);
		Batcher.Image(LitForeground, LitForeground.Bounds.Center, LitForeground.Bounds.Size / 2, Vector2.One, 0, Color.White);
		Batcher.PopSampler();
		Batcher.Render(LitScreen);
		Batcher.Clear();

		ApplyLightingNoise(LitScreen);

		return LitScreen;
	}

	private void ApplyLightingNoise(Target target)
	{
		if (Assets.LightingNoise == null)
			return;

		Batcher.PushBlend(BlendMode.Multiply);
		Batcher.PushSampler(new(TextureFilter.Nearest, TextureWrap.Repeat, TextureWrap.Repeat));
		Batcher.Image(
			Assets.LightingNoise,
			new Rect(Camera.X - Time.SecondsF * 5.0f, Camera.Y, target.Width, target.Height),
			Vector2.Zero,
			Color.White * CurrentLightNoise());
		Batcher.PopSampler();
		Batcher.PopBlend();
		Batcher.Render(target);
		Batcher.Clear();
	}

	private Target ApplyPixelSortGlitch(Target input)
	{
		if (pixelSortGlitchMaterial == null || ditherGlitch <= 0.001f)
			return input;

		GlitchScreen.Clear(Color.Transparent);
		pixelSortGlitchMaterial.Material.Fragment.Samplers[1] = new(TileField, new(TextureFilter.Nearest, TextureWrap.Clamp));
		var glitchPosition = PlayerDitherGlitchPosition();
		pixelSortGlitchMaterial.Material.Fragment.SetUniformBuffer([
			input.Width,
			input.Height,
			Camera.X - shake.X,
			Camera.Y - shake.Y,
			ditherGlitch,
			glitchPosition.X,
			glitchPosition.Y,
			DitherGlitchRadius,
			Time.SecondsF,
			ditherGlitchAxis.X,
			ditherGlitchAxis.Y,
			0
		]);

		Batcher.PushSampler(new(TextureFilter.Nearest, TextureWrap.Clamp, TextureWrap.Clamp));
		Batcher.PushMaterial(pixelSortGlitchMaterial.Material);
		Batcher.Image(input, input.Bounds.Center, input.Bounds.Size / 2, Vector2.One, 0, Color.White);
		Batcher.PopMaterial();
		Batcher.PopSampler();
		Batcher.Render(GlitchScreen);
		Batcher.Clear();
		return GlitchScreen;
	}

	private void CompositeLayer(Target color, Target light, Target emissive, Target output)
	{
		output.Clear(Color.Transparent);
		lightingCompositeMaterial!.Material.Fragment.Samplers[1] = new(light, new(TextureFilter.Linear, TextureWrap.Clamp));
		lightingCompositeMaterial.Material.Fragment.Samplers[2] = new(TileField, new(TextureFilter.Nearest, TextureWrap.Clamp));
		lightingCompositeMaterial.Material.Fragment.Samplers[3] = new(DitherMask, new(TextureFilter.Nearest, TextureWrap.Clamp));
		lightingCompositeMaterial.Material.Fragment.Samplers[4] = new(emissive, new(TextureFilter.Nearest, TextureWrap.Clamp));
		var glitchPosition = PlayerDitherGlitchPosition();
		lightingCompositeMaterial.Material.Fragment.SetUniformBuffer([
			output.Width,
			output.Height,
			Camera.X - shake.X,
			Camera.Y - shake.Y,
			Manager.LightingDitherEnabled ? 1.0f : 0.0f,
			Manager.LightingDitherScale,
			Manager.LightingDitherStrength,
			ditherGlitch,
			glitchPosition.X,
			glitchPosition.Y,
			DitherGlitchRadius,
			Time.SecondsF,
			ditherGlitchAxis.X,
			ditherGlitchAxis.Y,
			0,
			0
		]);

		Batcher.PushSampler(new(TextureFilter.Nearest, TextureWrap.Clamp, TextureWrap.Clamp));
		Batcher.PushMaterial(lightingCompositeMaterial.Material);
		Batcher.Image(color, color.Bounds.Center, color.Bounds.Size / 2, Vector2.One, 0, Color.White);
		Batcher.PopMaterial();
		Batcher.PopSampler();
		Batcher.Render(output);
		Batcher.Clear();
	}

	private void DrawTileField()
	{
		TileField.Clear(Color.Transparent);
		Batcher.PushMatrix(-(Point2)Camera + shake);
		if (room != null)
			DrawRoomTileField(room);
		if (nextRoom != null && nextRoom != room)
			DrawRoomTileField(nextRoom);
		Batcher.PopMatrix();
		Batcher.Render(TileField);
		Batcher.Clear();
	}

	private void DrawDitherMask()
	{
		DitherMask.Clear(Color.White);
		Batcher.PushMatrix(-(Point2)Camera + shake);
		foreach (var actor in rendering)
		{
			if (!actor.Visible)
				continue;

			var amount = DitherAmountForActor(actor);
			if (amount >= 0.999f)
				continue;

			Batcher.PushMatrix(actor.Position);
			actor.RenderShadow(Batcher, new Color(amount, amount, amount, 1.0f));
			Batcher.PopMatrix();
		}
		Batcher.PopMatrix();
		Batcher.Render(DitherMask);
		Batcher.Clear();
	}

	private static float DitherAmountForActor(Actor actor)
		=> actor.Mask.Has(Actor.Masks.Player | Actor.Masks.Enemy) ? ActorDitherAmount : 1.0f;

	private void DrawRoomTileField(Room renderRoom)
	{
		for (var y = 0; y < renderRoom.Size.Y; y ++)
		for (var x = 0; x < renderRoom.Size.X; x ++)
		{
			var tile = renderRoom.Tiles[x, y];
			var worldTile = renderRoom.Position + new Point2(x, y);
			Batcher.Rect(
				new Rect(
					renderRoom.WorldBounds.X + x * TileSize,
					renderRoom.WorldBounds.Y + y * TileSize,
					TileSize,
					TileSize),
				TileFieldColor(tile, worldTile));
		}
	}

	private static Color TileFieldColor(char tile, Point2 worldTile)
	{
		var hash = TileFieldHash(tile, worldTile);
		var variant = (hash & 255) / 255.0f;
		var family = TileFieldFamily(tile);
		var solid = Factory.Find(tile) is { BlocksMovement: true } ? 1.0f : 0.0f;
		return new Color(variant, family, solid, 1.0f);
	}

	private static float TileFieldFamily(char tile)
		=> tile switch
		{
			'0' => 0.00f,
			'1' or '2' or 'x' => 0.25f,
			'G' or 'S' or 'X' or 'g' => 0.50f,
			'w' => 0.75f,
			_ => 1.00f
		};

	private static uint TileFieldHash(char tile, Point2 worldTile)
	{
		var hash = 2166136261u;
		hash = (hash ^ (uint)worldTile.X) * 16777619u;
		hash = (hash ^ (uint)worldTile.Y) * 16777619u;
		hash = (hash ^ tile) * 16777619u;
		return hash;
	}

	private void DrawLightMask(Target lightMask, bool includeShadows)
	{
		lightMask.Clear(ScaleColor(CurrentAmbientColor(), CurrentAmbientIntensity()));
		if (CurrentSunStrength() > SunStrengthEpsilon)
		{
			DrawSunLightMask(includeShadows);
			AddSingleLightMask(lightMask);
		}

		foreach (var actor in Actors)
		{
			if (actor is Light light)
			{
				DrawSingleLightMask(light, includeShadows);
				AddSingleLightMask(lightMask);
			}

			actor.ForEachLight((position, radius, intensity, height, lightColor) =>
			{
				DrawSingleLightMask(position, radius, intensity, height, lightColor, includeShadows);
				AddSingleLightMask(lightMask);
			});
		}

		if (mouseLightEnabled)
		{
			DrawSingleLightMask(mouseLightPosition, 48, 2.0f, 12, 0xffd37a, includeShadows);
			AddSingleLightMask(lightMask);
		}
	}

	private Color CurrentAmbientColor()
	{
		if (nextRoom == null)
			return room?.AmbientColor ?? Room.DefaultAmbientColor;

		return LerpColor(transitionAmbientColorFrom, transitionAmbientColorTo, Ease.Cube.InOut(nextRoomEase));
	}

	private float CurrentAmbientIntensity()
	{
		if (nextRoom == null)
			return room?.AmbientIntensity ?? Room.DefaultAmbientIntensity;

		return float.Lerp(transitionAmbientIntensityFrom, transitionAmbientIntensityTo, Ease.Cube.InOut(nextRoomEase));
	}

	private float CurrentLightNoise()
	{
		if (nextRoom == null)
			return room?.LightNoise ?? Room.DefaultLightNoise;

		return float.Lerp(transitionLightNoiseFrom, transitionLightNoiseTo, Ease.Cube.InOut(nextRoomEase));
	}

	private Color CurrentSunColor()
	{
		if (nextRoom == null)
			return room?.SunColor ?? Room.DefaultSunColor;

		return LerpColor(transitionSunColorFrom, transitionSunColorTo, Ease.Cube.InOut(nextRoomEase));
	}

	private float CurrentSunStrength()
	{
		if (nextRoom == null)
			return room?.SunStrength ?? 0.0f;

		return float.Lerp(transitionSunStrengthFrom, transitionSunStrengthTo, Ease.Cube.InOut(nextRoomEase));
	}

	private Vector2 CurrentSunDirection()
	{
		if (nextRoom == null)
			return room?.SunDirection ?? Room.DefaultSunDirection;

		var direction = Vector2.Lerp(transitionSunDirectionFrom, transitionSunDirectionTo, Ease.Cube.InOut(nextRoomEase));
		return Room.NormalizeOrDefault(direction, Room.DefaultSunDirection);
	}

	private static Color LerpColor(Color from, Color to, float amount)
		=> new(Vector4.Lerp(from, to, amount));

	private const float SunStrengthEpsilon = 0.001f;

	private void DrawSunLightMask(bool includeShadows)
	{
		SingleLightMask.Clear(Color.Transparent);
		var strength = CurrentSunStrength();

		var direction = CurrentSunDirection();
		var lightColor = CurrentSunColor();
		if (normalLightMaterial != null)
		{
			var lightColorValue = (Vector4)lightColor;
			normalLightMaterial.Material.Fragment.SetUniformBuffer([
				NormalBuffer.Width,
				NormalBuffer.Height,
				0,
				0,
				direction.X,
				direction.Y,
				-1.0f,
				1.0f,
				lightColorValue.X,
				lightColorValue.Y,
				lightColorValue.Z,
				strength
			]);

			Batcher.PushSampler(new(TextureFilter.Nearest, TextureWrap.Clamp, TextureWrap.Clamp));
			Batcher.PushMaterial(normalLightMaterial.Material);
			Batcher.Image(NormalBuffer, NormalBuffer.Bounds.Center, NormalBuffer.Bounds.Size / 2, Vector2.One, 0, Color.White);
			Batcher.PopMaterial();
			Batcher.PopSampler();
		}
		else
		{
			Batcher.Rect(new Rect(0, 0, SingleLightMask.Width, SingleLightMask.Height), ScaleColor(lightColor, strength));
		}

		if (includeShadows)
		{
			Batcher.PushMatrix(-(Point2)Camera + shake);
			Batcher.PushBlend(ShadowBlend);
			DrawSunShadows(direction);
			Batcher.PopBlend();
			Batcher.PopMatrix();
		}

		Batcher.Render(SingleLightMask);
		Batcher.Clear();
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
		var drewVisibilityClippedLight = false;

		if (normalLightMaterial != null)
		{
			var screenPosition = position - Camera + shake;
			var lightColorValue = (Vector4)lightColor;
			normalLightMaterial.Material.Fragment.SetUniformBuffer([
				NormalBuffer.Width,
				NormalBuffer.Height,
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
			if (includeShadows)
			{
				DrawVisibilityClippedNormalLight(position, radius);
				drewVisibilityClippedLight = true;
			}
			else
			{
				Batcher.Image(NormalBuffer, NormalBuffer.Bounds.Center, NormalBuffer.Bounds.Size / 2, Vector2.One, 0, Color.White);
			}
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

		if (includeShadows && !drewVisibilityClippedLight)
		{
			Batcher.PushMatrix(-(Point2)Camera + shake);
			Batcher.PushBlend(ShadowBlend);
			DrawLightShadows(position, radius);
			Batcher.PopBlend();
			Batcher.PopMatrix();
		}

		Batcher.Render(SingleLightMask);
		Batcher.Clear();
	}

	private void DrawVisibilityClippedNormalLight(Vector2 lightPosition, float radius)
	{
		var segments = GatherVisibilitySegments(lightPosition, radius);
		var hits = VisibilityPolygon.Compute(lightPosition, radius, segments);
		if (hits.Count < 3)
		{
			Batcher.Image(NormalBuffer, NormalBuffer.Bounds.Center, NormalBuffer.Bounds.Size / 2, Vector2.One, 0, Color.White);
			return;
		}

		var screenLight = lightPosition - Camera + shake;
		var texture = NormalBuffer.Attachments[0];
		var textureSize = new Vector2(NormalBuffer.Width, NormalBuffer.Height);
		for (var i = 0; i < hits.Count; i++)
		{
			var a = hits[i].Point - Camera + shake;
			var b = hits[(i + 1) % hits.Count].Point - Camera + shake;
			Batcher.Triangle(
				texture,
				screenLight,
				a,
				b,
				screenLight / textureSize,
				a / textureSize,
				b / textureSize,
				Color.White);
		}
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
		if (room != null)
			DrawRoomLightShadows(room, lightPosition, radius);
		if (nextRoom != null && nextRoom != room)
			DrawRoomLightShadows(nextRoom, lightPosition, radius);

		foreach (var actor in Actors)
		{
			if (actor is not HiddenWallGrid hiddenWalls)
				continue;

			hiddenWalls.ForEachVisibleCell((rect, _, _) =>
			{
				if (CircleOverlapsRect(lightPosition, radius, rect))
					DrawShadowVolume(lightPosition, radius, rect);
			});
		}

		DrawActorSpriteShadows(lightPosition, radius);
	}

	private void DrawSunShadows(Vector2 direction)
	{
		var distance = SunShadowDistance();
		if (room != null)
			DrawRoomSunShadows(room, direction, distance);
		if (nextRoom != null && nextRoom != room)
			DrawRoomSunShadows(nextRoom, direction, distance);

		foreach (var actor in Actors)
		{
			if (actor is DualGrid { Layer: Factory.TileLayer.Foreground } dualGrid)
				dualGrid.AppendAllVisibilitySegments(sunSegmentBuffer);
		}

		foreach (var segment in sunSegmentBuffer)
			DrawSunShadowSegment(segment, direction, distance);
		sunSegmentBuffer.Clear();

		foreach (var actor in Actors)
		{
			if (actor is not HiddenWallGrid hiddenWalls)
				continue;

			hiddenWalls.ForEachVisibleCell((rect, _, _) =>
			{
				DrawSunShadowVolume(direction, distance, rect);
			});
		}
	}

	private readonly List<VisibilityPolygon.Segment> sunSegmentBuffer = [];

	private float SunShadowDistance()
		=> MathF.Sqrt(NormalBuffer.Width * NormalBuffer.Width + NormalBuffer.Height * NormalBuffer.Height) + TileSize * MathF.Max(Columns, Rows) * 2;

	private void DrawRoomSunShadows(Room renderRoom, Vector2 direction, float distance)
	{
		foreach (var rect in renderRoom.LightOccluders)
			DrawSunShadowVolume(direction, distance, rect);

		foreach (var polygon in renderRoom.LightOccluderPolygons)
			DrawSunShadowVolume(direction, distance, polygon.Points);
	}

	private void DrawSunShadowVolume(Vector2 direction, float distance, RectInt rect)
	{
		Span<Point2> corners =
		[
			rect.TopLeft,
			rect.TopRight,
			rect.BottomRight,
			rect.BottomLeft
		];
		DrawSunShadowVolume(direction, distance, corners);
	}

	private void DrawSunShadowVolume(Vector2 direction, float distance, ReadOnlySpan<Point2> points)
	{
		for (var i = 0; i < points.Length; i++)
		{
			var a = (Vector2)points[i];
			var b = (Vector2)points[(i + 1) % points.Length];
			if (!IsSunShadowCastingEdge(a, b, direction))
				continue;

			DrawSunShadowSegment(new(a, b), direction, distance);
		}
	}

	private static bool IsSunShadowCastingEdge(Vector2 a, Vector2 b, Vector2 direction)
	{
		var edge = b - a;
		var outward = new Vector2(edge.Y, -edge.X);
		return Vector2.Dot(outward, direction) > 0.001f;
	}

	private void DrawSunShadowSegment(VisibilityPolygon.Segment segment, Vector2 direction, float distance)
	{
		var offset = direction * distance;
		Batcher.Quad(segment.A, segment.B, segment.B + offset, segment.A + offset, Color.Black);
	}

	private void DrawRoomLightShadows(Room renderRoom, Vector2 lightPosition, float radius)
	{
		foreach (var rect in renderRoom.LightOccluders)
			if (CircleOverlapsRect(lightPosition, radius, rect))
				DrawShadowVolume(lightPosition, radius, rect);

		foreach (var polygon in renderRoom.LightOccluderPolygons)
			if (CircleOverlapsRect(lightPosition, radius, polygon.Bounds))
				DrawShadowVolume(lightPosition, radius, polygon.Points, polygon.Bounds);
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
		Span<Point2> corners =
		[
			rect.TopLeft,
			rect.TopRight,
			rect.BottomRight,
			rect.BottomLeft
		];
		DrawShadowVolume(lightPosition, radius, corners, rect);
	}

	private void DrawShadowVolume(Vector2 lightPosition, float radius, ReadOnlySpan<Point2> points, RectInt bounds)
	{
		if (bounds.Contains(lightPosition))
			return;

		var bestA = (Vector2)points[0];
		var bestB = (Vector2)points[1];
		var bestSpan = -1.0f;

		for (var i = 0; i < points.Length; i ++)
		for (var j = i + 1; j < points.Length; j ++)
		{
			var pointA = (Vector2)points[i];
			var pointB = (Vector2)points[j];
			var a = DirectionFromLight(lightPosition, pointA);
			var b = DirectionFromLight(lightPosition, pointB);
			var span = 1 - Vector2.Dot(a, b);
			if (span > bestSpan)
			{
				bestSpan = span;
				bestA = pointA;
				bestB = pointB;
			}
		}

		var aFar = ProjectToLightRadius(lightPosition, radius, bestA);
		var bFar = ProjectToLightRadius(lightPosition, radius, bestB);
		Batcher.Quad(bestA, bestB, bFar, aFar, Color.Black);
	}

	private void DrawVisibilityDebug(Target target)
	{
		if (!TryGetVisibilityDebugLight(out var lightPosition, out var radius))
			return;

		var segments = GatherVisibilitySegments(lightPosition, radius);
		var hits = VisibilityPolygon.Compute(lightPosition, radius, segments);

		Batcher.PushMatrix(-(Point2)Camera + shake);
		Batcher.Circle(lightPosition, radius, 64, Color.Transparent, new Color(0x5fcde4, 0.35f));

		for (var i = 0; i < hits.Count; i++)
		{
			var a = hits[i].Point;
			var b = hits[(i + 1) % hits.Count].Point;
			Batcher.Triangle(lightPosition, a, b, new Color(0x5fcde4, 0.08f));
		}

		for (var i = 0; i < segments.Count; i++)
			Batcher.Line(segments[i].A, segments[i].B, 1, VisibilityDebugSegmentColor(i, 0.9f));

		foreach (var hit in hits)
		{
			var color = hit.HitSegment
				? VisibilityDebugSegmentColor(hit.SegmentIndex, 0.75f)
				: new Color(0xcbdbfc, 0.45f);
			Batcher.Line(lightPosition, hit.Point, 1, color * 0.45f);
			Batcher.Circle(hit.Point, 1.5f, 8, color);
		}

		Batcher.Circle(lightPosition, 2.5f, 16, 0xffffff);
		Batcher.PopMatrix();
		Batcher.Render(target);
		Batcher.Clear();
	}

	private bool TryGetVisibilityDebugLight(out Vector2 lightPosition, out float radius)
	{
		if (mouseLightEnabled)
		{
			lightPosition = mouseLightPosition;
			radius = 48;
			return true;
		}

		if (GetFirst<Player>() is { } player)
		{
			lightPosition = player.GetVisualBounds().Center;
			radius = 64;
			return true;
		}

		lightPosition = default;
		radius = 0;
		return false;
	}

	private List<VisibilityPolygon.Segment> GatherVisibilitySegments(Vector2 lightPosition, float radius)
	{
		var segments = new List<VisibilityPolygon.Segment>();
		if (room != null)
			AddRoomVisibilitySegments(segments, room, lightPosition, radius);
		if (nextRoom != null && nextRoom != room)
			AddRoomVisibilitySegments(segments, nextRoom, lightPosition, radius);

		foreach (var actor in Actors)
		{
			if (actor is DualGrid { Layer: Factory.TileLayer.Foreground } dualGrid)
			{
				dualGrid.AppendVisibilitySegments(segments, lightPosition, radius);
				continue;
			}

			if (actor is not HiddenWallGrid hiddenWalls)
				continue;

			hiddenWalls.ForEachVisibleCell((rect, _, _) =>
			{
				AddRectVisibilitySegments(segments, rect, lightPosition, radius);
			});
		}

		return segments;
	}

	private static void AddRoomVisibilitySegments(List<VisibilityPolygon.Segment> segments, Room renderRoom, Vector2 lightPosition, float radius)
	{
		if (!CircleOverlapsRect(lightPosition, radius, renderRoom.WorldBounds))
			return;

		foreach (var segment in renderRoom.VisibilitySegments)
			if (VisibilityPolygon.CircleOverlaps(segment, lightPosition, radius))
				segments.Add(segment);
	}

	private static void AddRectVisibilitySegments(List<VisibilityPolygon.Segment> segments, RectInt rect, Vector2 lightPosition, float radius)
	{
		AddVisibilitySegment(segments, rect.TopLeft, rect.TopRight, lightPosition, radius);
		AddVisibilitySegment(segments, rect.TopRight, rect.BottomRight, lightPosition, radius);
		AddVisibilitySegment(segments, rect.BottomRight, rect.BottomLeft, lightPosition, radius);
		AddVisibilitySegment(segments, rect.BottomLeft, rect.TopLeft, lightPosition, radius);
	}

	private static void AddVisibilitySegment(List<VisibilityPolygon.Segment> segments, Vector2 a, Vector2 b, Vector2 lightPosition, float radius)
	{
		var segment = new VisibilityPolygon.Segment(a, b);
		if (VisibilityPolygon.CircleOverlaps(segment, lightPosition, radius))
			segments.Add(segment);
	}

	private static Color VisibilityDebugSegmentColor(int index, float alpha)
	{
		var hue = (index * 0.61803398875f) % 1.0f;
		return ColorFromHsv(hue, 0.72f, 1.0f, alpha);
	}

	private static Color ColorFromHsv(float hue, float saturation, float value, float alpha)
	{
		var h = hue * 6.0f;
		var c = value * saturation;
		var x = c * (1.0f - MathF.Abs(h % 2.0f - 1.0f));
		var m = value - c;
		var rgb = h switch
		{
			< 1.0f => new Vector3(c, x, 0),
			< 2.0f => new Vector3(x, c, 0),
			< 3.0f => new Vector3(0, c, x),
			< 4.0f => new Vector3(0, x, c),
			< 5.0f => new Vector3(x, 0, c),
			_ => new Vector3(c, 0, x),
		};
		return new Color(rgb.X + m, rgb.Y + m, rgb.Z + m, alpha);
	}

	private static Vector2 ProjectToLightRadius(Vector2 lightPosition, float radius, Vector2 point)
	{
		var direction = point - lightPosition;
		var distance = direction.Length();
		if (distance <= 0.0001f)
			return point;

		return lightPosition + direction / distance * radius;
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
		foreach (var slope in room.SlopeTiles)
		{
			var rect = slope.WorldBounds(room);
			Batcher.RectLine(rect, 1, Color.Red);
			if (slope.Direction == Room.SlopeDirection.DownRight)
				Batcher.Line(rect.TopLeft, rect.BottomRight, 1, Color.Red);
			else
				Batcher.Line(rect.BottomLeft, rect.TopRight, 1, Color.Red);
			var polygon = room.GetSlopePolygon(slope);
			for (var i = 0; i < polygon.Length; i ++)
				Batcher.Line(polygon[i], polygon[(i + 1) % polygon.Length], 1, Color.Red);
		}
		foreach (var rect in CollisionRectsThisFrame)
			Batcher.RectLine(rect, 1, Color.Red);
		foreach (var it in Actors)
			if (!destroying.Contains(it))
				if (it.Hitbox.Rect != new RectInt())
					Batcher.RectLine(it.Hitbox.Rect + it.Position, 1, Color.Red);
	}

	public List<RectInt> CollisionRectsThisFrame = new();

	private void DrawNormalBuffer()
	{
		NormalBuffer.Clear(new Color(0.5f, 0.5f, 1.0f, 0.0f));
		Batcher.PushMatrix(-(Point2)Camera + shake);

		if (ShowForegroundLayer)
			DrawRoomNormals();

		foreach (var actor in rendering)
		{
			if (!actor.Visible)
				continue;
			if ((IsBackgroundActor(actor) && !ShowBackgroundLayer) || (!IsBackgroundActor(actor) && !ShowForegroundLayer))
				continue;
			if (actor is DualGrid { Layer: Factory.TileLayer.Background })
				continue;

			Batcher.PushMatrix(actor.Position);
			actor.RenderNormals(Batcher);
			Batcher.PopMatrix();
		}

		Batcher.PopMatrix();
		Batcher.Render(NormalBuffer);
		Batcher.Clear();
	}

	private void DrawRoomNormals()
	{
		Array.Clear(roomNormalMask);
		Array.Clear(roomNormalSuppressMask);

		if (room != null)
		{
			FillRoomNormalMask(room);
			SuppressBakedDualRoomNormals(room);
		}
		if (nextRoom != null && nextRoom != room)
		{
			FillRoomNormalMask(nextRoom);
			SuppressBakedDualRoomNormals(nextRoom);
		}

		foreach (var actor in Actors)
			if (actor is HiddenWallGrid hiddenWalls)
				hiddenWalls.ForEachVisibleCell((rect, _, _) => FillRoomNormalMask(rect));

		var offset = (Point2)Camera;
		for (var y = 0; y < NormalBuffer.Height; y ++)
		for (var x = 0; x < NormalBuffer.Width; x ++)
		{
			if (!RoomNormalSolid(x, y))
				continue;

			var sx = SolidValue(x - 1, y) - SolidValue(x + 1, y);
			var sy = SolidValue(x, y - 1) - SolidValue(x, y + 1);
			if (sx == 0 && sy == 0)
				continue;
			if (RoomNormalSuppressed(x, y))
				continue;

			Batcher.Rect(new Rect(offset.X + x, offset.Y + y, 1, 1), EncodeNormal(new Vector3(sx, sy, 1), 1));
		}
	}

	private void FillRoomNormalMask(Room renderRoom)
	{
		foreach (var rect in renderRoom.CollisionRects)
			FillRoomNormalMask(rect);
		foreach (var polygon in renderRoom.LightOccluderPolygons)
			FillRoomNormalMask(polygon);
	}

	private void SuppressBakedDualRoomNormals(Room renderRoom)
	{
		var camera = (Point2)Camera;
		for (var tileY = 0; tileY < renderRoom.Size.Y; tileY++)
		for (var tileX = 0; tileX < renderRoom.Size.X; tileX++)
		{
			if (renderRoom.GetEntry(new Point2(tileX, tileY)) is not { DualTilesetName: { } dualTilesetName } ||
			    Assets.GetTileset(dualTilesetName) is not { NormalTiles.Count: > 0 } and not { NormalVariants.Count: > 0 })
				continue;

			var worldLeft = renderRoom.WorldBounds.X + tileX * Game.TileSize;
			var worldTop = renderRoom.WorldBounds.Y + tileY * Game.TileSize;
			var left = Math.Clamp(worldLeft - camera.X, 0, NormalBuffer.Width);
			var right = Math.Clamp(worldLeft + Game.TileSize - camera.X, 0, NormalBuffer.Width);
			var top = Math.Clamp(worldTop - camera.Y, 0, NormalBuffer.Height);
			var bottom = Math.Clamp(worldTop + Game.TileSize - camera.Y, 0, NormalBuffer.Height);

			for (var y = top; y < bottom; y++)
			for (var x = left; x < right; x++)
				roomNormalSuppressMask[x, y] = true;
		}
	}

	private void FillRoomNormalMask(RectInt rect)
	{
		var left   = Math.Clamp(rect.Left - (int)Camera.X, 0, NormalBuffer.Width);
		var right  = Math.Clamp(rect.Right - (int)Camera.X, 0, NormalBuffer.Width);
		var top    = Math.Clamp(rect.Top - (int)Camera.Y, 0, NormalBuffer.Height);
		var bottom = Math.Clamp(rect.Bottom - (int)Camera.Y, 0, NormalBuffer.Height);

		for (var y = top; y < bottom; y ++)
		for (var x = left; x < right; x ++)
			roomNormalMask[x, y] = true;
	}

	private void FillRoomNormalMask(Room.LightOccluderPolygon polygon)
	{
		var left   = Math.Clamp(polygon.Bounds.Left - (int)Camera.X, 0, NormalBuffer.Width);
		var right  = Math.Clamp(polygon.Bounds.Right - (int)Camera.X, 0, NormalBuffer.Width);
		var top    = Math.Clamp(polygon.Bounds.Top - (int)Camera.Y, 0, NormalBuffer.Height);
		var bottom = Math.Clamp(polygon.Bounds.Bottom - (int)Camera.Y, 0, NormalBuffer.Height);
		var camera = (Point2)Camera;

		for (var y = top; y < bottom; y ++)
		for (var x = left; x < right; x ++)
		{
			var worldPoint = new Vector2(camera.X + x + 0.5f, camera.Y + y + 0.5f);
			if (PointInPolygon(worldPoint, polygon.Points))
				roomNormalMask[x, y] = true;
		}
	}

	private static bool PointInPolygon(Vector2 point, ReadOnlySpan<Point2> polygon)
	{
		var inside = false;
		for (var i = 0; i < polygon.Length; i ++)
		{
			var a = (Vector2)polygon[i];
			var b = (Vector2)polygon[(i + polygon.Length - 1) % polygon.Length];
			if ((a.Y > point.Y) != (b.Y > point.Y) &&
			    point.X < (b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y) + a.X)
				inside = !inside;
		}

		return inside;
	}

	private bool RoomNormalSolid(int x, int y)
	{
		if (x < 0 || y < 0 || x >= NormalBuffer.Width || y >= NormalBuffer.Height)
			return false;

		return roomNormalMask[x, y];
	}

	private bool RoomNormalSuppressed(int x, int y)
	{
		if (x < 0 || y < 0 || x >= NormalBuffer.Width || y >= NormalBuffer.Height)
			return false;

		return roomNormalSuppressMask[x, y];
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
		var scale = Calc.Min(viewport.Width / (float)Screen.Width, viewport.Height / (float)Screen.Height);
		var size = new Vector2(Screen.Width, Screen.Height) * scale;
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
		reflectionDistortMaterial?.Reload();
		tileFieldDebugMaterial?.Reload();
		pixelSortGlitchMaterial?.Reload();
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

	private sealed class ReloadableMaterial : IDisposable
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

		public void Dispose()
			=> fragmentShader.Dispose();

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

	public T Create<T>(Point2? position = null, Action<T>? init = null) where T : Actor, new()
	{
		var instance = new T
		{
			Game = this,
			Position = position ?? Point2.Zero
		};
		init?.Invoke(instance);
		instance.Added();
		Actors.Add(instance);
		return instance;
	}

	public Actor Create(
		[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor | DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.PublicProperties)]
		Type type,
		Point2? position = null,
		Action<Actor>? init = null)
	{
		var instance = (Actor)(Activator.CreateInstance(type) ?? throw new InvalidOperationException($"Failed to create actor '{type.Name}'."));
		instance.Game = this;
		instance.Position = position ?? Point2.Zero;
		init?.Invoke(instance);
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

	public void DestroyForThisSession(Actor actor)
	{
		if (!string.IsNullOrEmpty(actor.SessionDestroyKey))
			sessionDestroyedActors.Add(actor.SessionDestroyKey);
		Destroy(actor);
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

		if (room.GetEntry(tile) is { SurfaceType: { } surfaceType })
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

	private void ApplyMusicArea(Room forRoom)
	{
		if (!Manager.MusicEnabled)
		{
			StopMusic(true);
			return;
		}

		if (string.IsNullOrEmpty(forRoom.MusicArea))
			return;

		StartMusic();
		if (music == null || musicArea == forRoom.MusicArea)
			return;

		try
		{
			music.SetParameterLabel(Manager.BackgroundMusicAreaParameter, forRoom.MusicArea, true);
			musicArea = forRoom.MusicArea;
		}
		catch (Exception e)
		{
			if (audioFailures.Add($"{Manager.BackgroundMusicAreaParameter}:{forRoom.MusicArea}"))
				Console.Error.WriteLine($"Failed to set background music area '{forRoom.MusicArea}': {e}");
		}
	}

	private void ApplyRoomReverb(Room forRoom)
	{
		if (!AudioEventsEnabled || !Audio.Initialized || roomReverb == forRoom.Reverb)
			return;

		try
		{
			Audio.SetParameter(Manager.RoomReverbParameter, forRoom.Reverb, true);
			roomReverb = forRoom.Reverb;
		}
		catch (Exception e)
		{
			if (audioFailures.Add(Manager.RoomReverbParameter))
				Console.Error.WriteLine($"Failed to set room reverb '{forRoom.Reverb:0.###}': {e}");
		}
	}

	public bool Transition(Point2 direction)
	{
		if (room != null && nextRoom == null)
		{
			var exitPosition = GetFirst<Player>()?.Position ?? (Vector2)room.WorldBounds.Center;
			if (Assets.FindAdjacentRoom(room, direction, exitPosition) is { } foundRoom)
			{
				nextRoom = foundRoom.CloneRuntime();
				foreach (var it in Actors)
					if (it is not Player && !it.KeepOnRoomTransition)
						Destroy(it);

				transitionCameraFrom = Camera;
				transitionCameraTo = ClampCameraToRoom(exitPosition - new Vector2(Width, Height) / 2, nextRoom.WorldBounds);
				transitionAmbientColorFrom = room.AmbientColor;
				transitionAmbientColorTo = nextRoom.AmbientColor;
				transitionAmbientIntensityFrom = room.AmbientIntensity;
				transitionAmbientIntensityTo = nextRoom.AmbientIntensity;
				transitionLightNoiseFrom = room.LightNoise;
				transitionLightNoiseTo = nextRoom.LightNoise;
				transitionSunColorFrom = room.SunColor;
				transitionSunColorTo = nextRoom.SunColor;
				transitionSunStrengthFrom = room.SunStrength;
				transitionSunStrengthTo = nextRoom.SunStrength;
				transitionSunDirectionFrom = room.SunDirection;
				transitionSunDirectionTo = nextRoom.SunDirection;
				nextRoomEase = 0.0f;
				LoadRoom(nextRoom);
				ApplyMusicArea(nextRoom);
				ApplyRoomReverb(nextRoom);
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

		return OverlapsRoomCollision(actor.Hitbox.Rect + actor.Position + offset);
	}

	public bool OverlapsRoomCollision(in RectInt bounds)
	{
		if (room == null)
			return false;

		foreach (var rect in room.CollisionRects)
		{
			if (bounds.Overlaps(rect))
				return true;
		}

		foreach (var slope in room.SlopeTiles)
		{
			if (OverlapsSlope(bounds, room, slope))
				return true;
		}

		return false;
	}

	public float GetRoomCollisionOverlapRatio(in RectInt bounds)
	{
		if (room == null || bounds.Width <= 0 || bounds.Height <= 0)
			return 0;

		var solidPixels = 0;
		for (var y = bounds.Top; y < bounds.Bottom; y ++)
		for (var x = bounds.Left; x < bounds.Right; x ++)
		{
			if (RoomCollisionContains(new Vector2(x + 0.5f, y + 0.5f)))
				solidPixels ++;
		}

		return solidPixels / (float)(bounds.Width * bounds.Height);
	}

	private bool RoomCollisionContains(Vector2 point)
	{
		if (room == null)
			return false;

		foreach (var rect in room.CollisionRects)
		{
			if (point.X >= rect.Left && point.X < rect.Right && point.Y >= rect.Top && point.Y < rect.Bottom)
				return true;
		}

		foreach (var slope in room.SlopeTiles)
		{
			var tileBounds = slope.WorldBounds(room);
			if (point.X < tileBounds.Left || point.X >= tileBounds.Right || point.Y < tileBounds.Top || point.Y >= tileBounds.Bottom)
				continue;

			if (PointInPolygon(point, room.GetSlopePolygon(slope)))
				return true;
		}

		return false;
	}

	private static bool OverlapsSlope(RectInt bounds, Room room, Room.SlopeTile slope)
	{
		var tileBounds = slope.WorldBounds(room);
		if (!bounds.Overlaps(tileBounds))
			return false;

		var left = Math.Max(bounds.Left, tileBounds.Left);
		var right = Math.Min(bounds.Right, tileBounds.Right);
		var top = Math.Max(bounds.Top, tileBounds.Top);
		var bottom = Math.Min(bounds.Bottom, tileBounds.Bottom);
		var polygon = room.GetSlopePolygon(slope);

		for (var y = top; y < bottom; y ++)
		for (var x = left; x < right; x ++)
		{
			if (PointInPolygon(new Vector2(x + 0.5f, y + 0.5f), polygon))
				return true;
		}

		return false;
	}

	public bool BreakTiles(RectInt hitbox)
	{
		if (room == null)
			return false;

		if (!hitbox.Overlaps(room.WorldBounds))
			return false;

		var local = hitbox - room.WorldBounds.TopLeft;
		var left = Math.Clamp((int)MathF.Floor(local.Left / (float)TileSize), 0, room.Size.X - 1);
		var right = Math.Clamp((int)MathF.Floor((local.Right - 1) / (float)TileSize), 0, room.Size.X - 1);
		var top = Math.Clamp((int)MathF.Floor(local.Top / (float)TileSize), 0, room.Size.Y - 1);
		var bottom = Math.Clamp((int)MathF.Floor((local.Bottom - 1) / (float)TileSize), 0, room.Size.Y - 1);
		var brokeAny = false;

		var tileTypesBroken = new HashSet<string>();

		for (var y = top; y <= bottom; y ++)
		for (var x = left; x <= right; x ++)
		{
			var tile = room.Tiles[x, y];
			var point = new Point2(x, y);

			if (!room.IsBreakable(point) && Factory.Find(tile) is not { Destructible: true })
				continue;

			if (Factory.Find(tile) is { SurfaceType: { } surfaceType })
				tileTypesBroken.Add(surfaceType);

			room.Set(point, '0');
			Create<Pop>(room.WorldBounds.TopLeft + new Point2(x * TileSize + TileSize / 2, y * TileSize + TileSize / 2));
			brokeAny = true;
		}

		if (brokeAny)
		{
			RefreshRoomTiles();
			foreach (var surfaceType in tileTypesBroken)
			{
				PlayAudioEvent("tile_break", "Type", surfaceType, position: hitbox.Center);
			}
		}

		return brokeAny;
	}

	public void ReloadRoom(bool restartMusic = false)
	{
		if (room == null) return;

		if (restartMusic)
			StopMusic(true);

		var reloaded = Assets.GetRoom(room.Id)?.CloneRuntime() ?? room.CloneRuntime();
		foreach (var actor in Actors)
			Destroy(actor);
		room = reloaded;
		LoadRoom(room);
		if (GetFirst<Player>() == null && respawnPosition is { } position)
			Create<Player>(position);

		ApplyRoomReverb(room);

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
		Grid? fgVisual = null;
		Grid? fg = null;
		DualGrid? bgDual = null;
		DualGrid? fgDual = null;
		HiddenWallGrid? hiddenWalls = null;
		foreach (var actor in Actors)
		{
			if (actor is HiddenWallGrid hiddenWallGrid)
			{
				hiddenWalls = hiddenWallGrid;
				continue;
			}

			if (actor is DualGrid dualGrid)
			{
				if (dualGrid.Layer == Factory.TileLayer.Background)
					bgDual = dualGrid;
				else
					fgDual = dualGrid;
				continue;
			}

			if (actor is not Grid grid)
				continue;

			if (grid.Layer == Factory.TileLayer.Background)
				bg = grid;
			else if (grid.Mask.Has(Actor.Masks.Solid))
				fg = grid;
			else
				fgVisual = grid;
		}

		if (bg == null || fgVisual == null || fg == null || fgDual == null)
			return;

		bg.Clear();
		fgVisual.Clear();
		fg.Clear();
		bgDual?.Clear();
		fgDual.Clear();
		hiddenWalls?.Resize(room.Size);

		for (var x = 0; x < room.Size.X; x ++)
		for (var y = 0; y < room.Size.Y; y ++)
		{
			var tile = new Point2(x, y);
			if (room.GetBackgroundEntry(tile) is { } backgroundEntry)
				backgroundEntry.Tile?.Invoke(tile, room, fg, fgVisual, bg, bgDual ?? fgDual);
			if (room.GetEntry(tile) is { } entry)
				entry.Tile?.Invoke(tile, room, fg, fgVisual, bg, fgDual);
		}

		if (hiddenWalls != null)
			PopulateHiddenWalls(room, hiddenWalls);

		if (bgDual != null)
			BuildDualTiles(room, bgDual, Factory.TileLayer.Background);
		BuildDualTiles(room, fgDual, Factory.TileLayer.Foreground);
	}

	void BuildDualTiles(Room room, DualGrid dual, Factory.TileLayer layer)
	{
		dual.Clear();
		var dualEntries = Factory.Entries
			.Where(it => it.Value.DualTilesetName != null && it.Value.Layer == layer)
			.ToArray();

		foreach (var (tileType, entry) in dualEntries)
		{
			if (entry.DualTilesetName == null || Assets.GetTileset(entry.DualTilesetName) is not { } dualTileset)
				continue;

			dual.Build(room, tileType, dualTileset, layer);
		}
	}

	public void LoadRoom(Room room)
	{
		room.RebuildGeometry();

		var offset = room.WorldBounds.TopLeft;

		var bg = Create<Grid>(offset);
		bg.Layer = Factory.TileLayer.Background;
		bg.Depth = 10;

		var bgDual = Create<DualGrid>(offset);
		bgDual.Layer = Factory.TileLayer.Background;
		bgDual.Depth = 9;

		var fgDual = Create<DualGrid>(offset);
		fgDual.Layer = Factory.TileLayer.Foreground;
		fgDual.Depth = 7;

		var fgVisual = Create<Grid>(offset);
		fgVisual.Layer = Factory.TileLayer.Foreground;
		fgVisual.Depth = 6;

		var fg = Create<Grid>(offset);
		fg.Layer = Factory.TileLayer.Foreground;
		fg.Mask = Actor.Masks.Solid;
		fg.Depth = 5;

		var hiddenWalls = Create<HiddenWallGrid>(offset, it => it.Resize(room.Size));

		// loop over room grid placing objects
		for (var x = 0; x < room.Size.X; x++)
		for (var y = 0; y < room.Size.Y; y++)
		{
			var tile = new Point2(x, y);
			if (room.GetBackgroundEntry(tile) is { } backgroundEntry)
				backgroundEntry.Tile?.Invoke(tile, room, fg, fgVisual, bg, bgDual);
			if (room.GetEntry(tile) is { } entry)
				entry.Tile?.Invoke(tile, room, fg, fgVisual, bg, fgDual);
		}

		PopulateHiddenWalls(room, hiddenWalls);
		BuildDualTiles(room, bgDual, Factory.TileLayer.Background);
		BuildDualTiles(room, fgDual, Factory.TileLayer.Foreground);

		foreach (var actor in room.Actors)
		{
			if (GetFirst<Player>() != null && string.Equals(ActorSpecs.GetTypeName(actor), "Player", StringComparison.OrdinalIgnoreCase))
				continue;

			var localPosition = ActorSpecs.ReadPoint(actor, "position") ?? Point2.Zero;
			var sessionDestroyKey = SessionDestroyKey(room, actor, localPosition);
			if (sessionDestroyedActors.Contains(sessionDestroyKey))
				continue;

			if (ActorSpecs.Create(this, actor, offset + localPosition) is { } created)
				created.SessionDestroyKey = sessionDestroyKey;
		}
	}

	private static string SessionDestroyKey(Room room, JsonObject actor, Point2 localPosition)
	{
		var id = ActorSpecs.ReadString(actor, "id");
		if (!string.IsNullOrWhiteSpace(id))
			return $"{room.Id}:id:{id}";

		var source = ActorSpecs.ReadString(actor, "from") ?? ActorSpecs.ReadString(actor, "type") ?? "Actor";
		return $"{room.Id}:{source}:{localPosition.X},{localPosition.Y}";
	}

	private static void PopulateHiddenWalls(Room room, HiddenWallGrid hiddenWalls)
	{
		foreach (var actor in room.Actors)
		{
			var resolved = ActorSpecs.Resolve(actor);
			if (ActorSpecs.ReadString(resolved, "type") != "HiddenWall")
				continue;

			var localPosition = ActorSpecs.ReadPoint(resolved, "position") ?? Point2.Zero;
			var size = ActorSpecs.ReadPoint(resolved, "size") ?? HiddenWall.DefaultSize;
			var tileType = ActorSpecs.ReadString(resolved, "tileType") ?? "castle";
			hiddenWalls.AddRect(localPosition, size, tileType);
		}
	}

	public void HitStun(float time) => hitStun = MathF.Max(hitStun, time);

	public void Shake(float time) => shaking = MathF.Max(shaking, time);

	public void PulseDitherGlitch(Vector2? axis = null)
	{
		if (axis is { } value && value.LengthSquared() > 0.001f)
			ditherGlitchAxis = Vector2.Normalize(value);
		ditherGlitchTarget = 1.0f;
	}

	private Vector2 PlayerDitherGlitchPosition()
		=> GetFirst<Player>()?.Position ?? Camera + new Vector2(Width, Height) * 0.5f;

	internal void DestroyEnemy(Actor actor)
	{
		HitStun(0.1f);
		DestroyForThisSession(actor);
		Create<Pop>(actor.Position + new Point2(0, -4));
	}
}
