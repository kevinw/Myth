using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Text.Json.Nodes;
using Foster.Framework;

namespace Myth;

public enum RoomChangeKind
{
	InitialLoad,
	EdgeTransition,
	LinkedDoor,
	Reload
}

public readonly record struct RoomChange(Room? From, Room To, RoomChangeKind Kind, Point2 Direction, Vector2 PlayerPosition);

public partial class Game : IDisposable
{
	private static string? pendingHotReloadMessage;

	internal static void NotifyHotReload(string message)
		=> pendingHotReloadMessage = message;
	public const int Width = 320;
	public const int Height = 180;
	public const int Columns = Width / TileSize;
	public const int Rows = Height / TileSize + 1;
	public const int TileSize = 8;
	private const int ActorShadowSteps = 8;
	private const float ActorShadowStrength = 0.5f;
	private const float ActorShadowMinLength = 4.0f;
	private const float ActorShadowMaxLength = 28.0f;
	private static readonly BlendMode ShadowBlend = new(
		BlendOp.Add, BlendFactor.One, BlendFactor.OneMinusSrcAlpha,
		BlendOp.Add, BlendFactor.One, BlendFactor.OneMinusSrcAlpha,
		BlendMask.RGB, Color.White);
	private static readonly BlendMode TileFieldStampBlend = new(
		BlendOp.Add, BlendFactor.One, BlendFactor.Zero,
		BlendOp.Add, BlendFactor.One, BlendFactor.Zero,
		BlendMask.Red | BlendMask.Green, Color.White);
	private static readonly Point2[] DualMaskToTile =
	[
		new(0, 3),
		new(3, 3),
		new(0, 0),
		new(3, 2),
		new(0, 2),
		new(1, 2),
		new(2, 3),
		new(3, 1),
		new(1, 3),
		new(0, 1),
		new(3, 0),
		new(2, 0),
		new(1, 0),
		new(2, 2),
		new(1, 1),
		new(2, 1),
	];

	private const float CameraSlackEpsilon = TileSize;
	static float DitherGlitchAttackSpeed => 15.0f;
	static float DitherGlitchDecaySpeed => 1.35f;
	static float DitherGlitchRadius => 150.0f;
	private const float ActorDitherAmount = 0.2f;

	internal readonly Manager Manager;
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
	Target ReflectionMask { get; set; } = null!;
	public readonly List<Actor> Actors = [];
	public bool ShowForegroundLayer { get; set; } = true;
	public bool ShowBackgroundLayer { get; set; } = true;

	Input Input => Manager.Input;
	GraphicsDevice GraphicsDevice => Manager.GraphicsDevice;
	public Time Time => Manager.Time.MultiplyDelta(timeScale);

	private Room? room;
	private Room? nextRoom;
	private float nextRoomEase;
	private Point2 transitionDirection;
	private Vector2 transitionCameraFrom;
	private Vector2 transitionCameraTo;
	private Color transitionBackdropColorFrom;
	private Color transitionBackdropColorTo;
	private Color transitionBackdropColor2From;
	private Color transitionBackdropColor2To;
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
	private float linkedDoorCooldown;
	private bool roomSwappedThisFrame;
	private readonly List<Actor> destroying = [];
	private readonly List<Actor> rendering = [];
	private readonly Dictionary<Room, RoomNormalCache> roomNormalCaches = [];
	private readonly Dictionary<Room, RoomTileFieldCache> roomTileFieldCaches = [];
	private readonly HashSet<string> sessionDestroyedActors = [];
	private readonly ReloadableMaterial? crtMaterial;
	private readonly ReloadableMaterial? lightingCompositeMaterial;
	private readonly ReloadableMaterial? normalLightMaterial;
	private readonly ReloadableMaterial? reflectionDistortMaterial;
	private readonly ReloadableMaterial? tileFieldStampMaterial;
	private readonly ReloadableMaterial? tileFieldDebugMaterial;
	private readonly ReloadableMaterial? pixelSortGlitchMaterial;
	public bool GymBudDied;
	public bool GymMode;
	public Actor? GymTarget;
	private float hitStun;
	private float shaking;
	private float ditherGlitch;
	private float ditherGlitchTarget;
	private Vector2 ditherGlitchAxis = Vector2.UnitX;
	private Point2 shake;
	internal Rng rng = new();

	private sealed class RoomNormalCache(Room room, Target target) : IDisposable
	{
		public readonly Room Room = room;
		public readonly Target Target = target;
		public bool Dirty = true;

		public void Dispose()
			=> Target.Dispose();
	}

	private sealed class RoomTileFieldCache(Room room, Target target) : IDisposable
	{
		public readonly Room Room = room;
		public readonly Target Target = target;
		public bool Dirty = true;

		public void Dispose()
			=> Target.Dispose();
	}

	private bool draggingPlayer;
	private float timeScale = 1.0f;
	private readonly Point2? respawnPosition;
	private readonly bool spawnBud;
	private bool disposed;
#if BROWSER
	private const float BrowserDebugStatsInterval = 0.5f;
	private int pendingRoomTransitionGcFrames;
	private RoomChangeKind pendingRoomTransitionGcKind;
	private int browserDebugFramesSinceRoomTransition = -1;
	private float browserDebugStatsElapsed;
	private int browserDebugStatsFrames;
	private long browserDebugStatsAllocatedBytes;
	private long browserDebugStatsMaxFrameBytes;
	private int browserDebugStatsLastGen0 = GC.CollectionCount(0);
	private int browserDebugStatsLastGen1 = GC.CollectionCount(1);
	private int browserDebugStatsLastGen2 = GC.CollectionCount(2);
	private double browserDebugLastGcMs = -1;
	private long browserDebugLastGcBeforeBytes;
	private long browserDebugLastGcAfterBytes;
	private RoomChangeKind browserDebugLastGcKind;
#endif

	public Vector2 Camera { get; private set; }
	private Vector2 RenderCamera => (Point2)Camera - shake;
	private float zoomLevel = 1.0f;
	public float ZoomLevel => zoomLevel;
	private float ZoomedWidth => Width / zoomLevel;
	private float ZoomedHeight => Height / zoomLevel;
	private float fbZoom = 1.0f;
	public float FbZoom => fbZoom;
	private float FbZoomedWidth => Width / fbZoom;
	private float FbZoomedHeight => Height / fbZoom;
	public RectInt Bounds => room?.WorldBounds ?? new();
	public Point2 Cell => room?.Cell ?? Point2.Zero;
	public Room? CurrentRoom => room;
	public bool IsEditorPreview => !AudioEventsEnabled;
	public event Action<RoomChange>? RoomChanged;

	public Game(Manager manager, Point2 start, bool audioEventsEnabled = true)
		: this(manager, Assets.GetRoom(start), audioEventsEnabled)
	{
	}

	public Game(Manager manager, Room? start, bool audioEventsEnabled = true, Point2? fallbackPlayerDirection = null, bool spawnFallbackPlayer = false, Point2? initialPlayerPosition = null, bool spawnBud = true)
	{
		Manager = manager;
		AudioEventsEnabled = audioEventsEnabled;
		this.spawnBud = spawnBud;
		Batcher = new(manager.GraphicsDevice, name: "GameBatcher");
		EnsureRenderTargets(Width, Height);
		Controls = new(manager.Input);
		crtMaterial = CreateFragmentMaterial("Crt", samplerCount: 1, uniformBufferCount: 1);
		lightingCompositeMaterial = CreateFragmentMaterial("LightingComposite", samplerCount: 5, uniformBufferCount: 1);
		normalLightMaterial = CreateFragmentMaterial("NormalLight", samplerCount: 1, uniformBufferCount: 1);
		reflectionDistortMaterial = CreateFragmentMaterial("ReflectionDistort", samplerCount: 2, uniformBufferCount: 1);
		tileFieldStampMaterial = CreateFragmentMaterial("TileFieldStamp", samplerCount: 1);
		tileFieldDebugMaterial = CreateFragmentMaterial("TileFieldDebug", samplerCount: 1, uniformBufferCount: 1);
		pixelSortGlitchMaterial = CreateFragmentMaterial("PixelSortGlitch", samplerCount: 2, uniformBufferCount: 1);
		LoadStory();
		RestorePersistentDebugViews();

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
			EnsureBudForPlayer(repositionExisting: true);
			RaiseRoomChanged(null, room, RoomChangeKind.InitialLoad, Point2.Zero);
			if (AudioEventsEnabled)
			{
				StartMusic();
				ApplyMusicArea(room);
				ApplyRoomReverb(room);
			}

			var props = Create<PropSystem>();
			props.KeepOnRoomTransition = true;
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
		DisposeRoomNormalCaches();
		DisposeRoomTileFieldCaches();
		crtMaterial?.Dispose();
		lightingCompositeMaterial?.Dispose();
		normalLightMaterial?.Dispose();
		reflectionDistortMaterial?.Dispose();
		tileFieldStampMaterial?.Dispose();
		tileFieldDebugMaterial?.Dispose();
		pixelSortGlitchMaterial?.Dispose();
		Batcher.Dispose();
	}

	public void Update(bool allowKeyboardShortcuts = true)
	{
#if BROWSER
		var browserDebugFrameStartAllocated = GC.GetAllocatedBytesForCurrentThread();
#endif
		GymBudDied = false;
		roomSwappedThisFrame = false;
		CollisionRectsThisFrame.Clear();
		UpdatePlayerHitSnapshot();
#if BROWSER
		UpdateRoomTransitionGc();
#endif
		if (linkedDoorCooldown > 0)
			linkedDoorCooldown = Calc.Approach(linkedDoorCooldown, 0, Time.Delta);
		if (debugViewMessageTimer > 0)
		{
			debugViewMessageTimer -= Time.Delta;
			if (debugViewMessageTimer <= 0)
				DebugViewMessage = null;
		}
		if (pendingHotReloadMessage is { } msg)
		{
			pendingHotReloadMessage = null;
			DisplayMessage(msg, 2.0f);
		}
		var glitchSpeed = ditherGlitchTarget > ditherGlitch ? DitherGlitchAttackSpeed : DitherGlitchDecaySpeed;
		ditherGlitch = Calc.Approach(ditherGlitch, ditherGlitchTarget, Time.Delta * glitchSpeed);
		if (ditherGlitchTarget > 0 && ditherGlitch >= ditherGlitchTarget)
			ditherGlitchTarget = 0;

		// Don't run normal gameplay loop if no room is loaded
		if (room == null)
		{
#if BROWSER
			UpdateBrowserDebugStats(browserDebugFrameStartAllocated);
#endif
			return;
		}

		// Run Game normally when not moving to a new room
		if (nextRoom == null)
		{
			if (allowKeyboardShortcuts)
			{
				// Reload Room Debug
				if (Input.Keyboard.Pressed(Keys.R))
					ReloadRoom();

				if (Input.Keyboard.Pressed(Keys.C)) ToggleDebugView(DebugView.CollisionRects);
				if (Input.Keyboard.Pressed(Keys.K)) ToggleDebugView(DebugView.Reflection);
				if (Input.Keyboard.Pressed(Keys.L)) ToggleDebugView(DebugView.LightMask);
				if (Input.Keyboard.Pressed(Keys.N)) ToggleDebugView(DebugView.NormalBuffer);
				if (Input.Keyboard.Pressed(Keys.T)) CycleTileFieldDebugView();
				if (Input.Keyboard.Pressed(Keys.M)) ToggleDebugView(DebugView.DitherMask);
				if (Input.Keyboard.Pressed(Keys.V)) ToggleDebugView(DebugView.Visibility);
				if (Input.Keyboard.Pressed(Keys.P)) ToggleDebugView(DebugView.PlatformNav);
				if (Input.Keyboard.Pressed(Keys.M)) mouseLightEnabled = !mouseLightEnabled;
				if (Input.Keyboard.Pressed(Keys.U)) PerfToggles.RevealOne(this, !Input.Keyboard.Shift);
			}

#if DEBUG
			UpdatePlayerMouseDrag();
			if (allowKeyboardShortcuts)
			{
				if (Input.Keyboard.Pressed(Keys.Minus, Keys.KeypadMinus))
					ChangeTimeScale(-1);
				if (Input.Keyboard.Pressed(Keys.Equals, Keys.KeypadPlus))
					ChangeTimeScale(1);

				foreach (var controller in Input.Controllers)
				{
					if (!controller.Connected)
						continue;
					if (controller.Pressed(Buttons.LeftShoulder))
						ChangeTimeScale(-1);
					if (controller.Pressed(Buttons.RightShoulder))
						ChangeTimeScale(1);
				}
			}
#endif

			// only run normal updates if no hitStun
			if (hitStun <= 0)
			{
				// Remove Destroyed Actors
				RemoveDestroyedActors();

				// Update Actors
				for (var i = 0; i < Actors.Count; i++)
				{
					Actors[i].Update();
					if (roomSwappedThisFrame)
						break;
				}

				UpdateCamera();
				UpdateStory();

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
			ResolvePlayerRoomTransition(transitionDirection);
			ApplyMusicArea(room);
			HitStun(0.1f);
			FinishRoomActivation(RoomChangeKind.EdgeTransition);
		}

		UpdateAudioListener();
#if BROWSER
		UpdateBrowserDebugStats(browserDebugFrameStartAllocated);
#endif
	}

	private void ChangeTimeScale(int direction)
	{
		ReadOnlySpan<float> scales = [0.05f, 0.1f, 0.25f, 0.5f, 1.0f, 2.0f, 4.0f];

		var index = 0;
		var bestDistance = float.MaxValue;
		for (var i = 0; i < scales.Length; i++)
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
		DisplayMessage($"Time x{timeScale:0.##}");
	}

	private void PushWorldMatrix()
		=> Batcher.PushMatrix(
			Matrix3x2.CreateTranslation(-(Point2)Camera + shake) *
			Matrix3x2.CreateScale(zoomLevel));

	private void UpdateCamera()
	{
		if (room == null || nextRoom != null || draggingPlayer)
			return;

		var fbZoomTarget = Controls.Zoom.Down ? 2.0f : 1.0f;
		fbZoom += (fbZoomTarget - fbZoom) * (1 - MathF.Exp(-Time.Delta * 12f));
		if (MathF.Abs(fbZoom - fbZoomTarget) < 0.005f)
			fbZoom = fbZoomTarget;

		Camera = GetGameplayCameraTarget();
	}

	private void UpdatePlayerMouseDrag()
	{
		if (GetFirst<Player>() is not { } player)
		{
			draggingPlayer = false;
			return;
		}

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

		for (var y = 1; y < forRoom.Size.Y; y++)
			for (var x = 0; x < forRoom.Size.X; x++)
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

		var unclamped = player.Position - new Vector2(Width, Height) / 2;
		var padding = new Vector2((Width - FbZoomedWidth) / 2, (Height - FbZoomedHeight) / 2);
		return ClampCameraToRoomFb(unclamped, padding);
	}

	public void SnapCameraToGameplayTarget()
		=> Camera = GetGameplayCameraTarget();

	private Vector2 ClampCameraToRoom(Vector2 target)
	{
		if (room == null)
			return target;

		return ClampCameraToRoom(target, room.WorldBounds);
	}

	private Vector2 ClampCameraToRoom(Vector2 target, RectInt bounds)
	{
		return new Vector2(
			ClampCameraAxis(target.X, bounds.Left, bounds.Right, ZoomedWidth),
			ClampCameraAxis(target.Y, bounds.Top, bounds.Bottom, ZoomedHeight));
	}

	private Vector2 ClampCameraToRoomFb(Vector2 target, Vector2 padding)
	{
		if (room == null) return target;
		return ClampCameraToRoomFb(target, padding, room.WorldBounds);
	}

	private Vector2 ClampCameraToRoomFb(Vector2 target, Vector2 padding, RectInt bounds)
	{
		return new Vector2(
			ClampCameraAxis(target.X + padding.X, bounds.Left, bounds.Right, FbZoomedWidth) - padding.X,
			ClampCameraAxis(target.Y + padding.Y, bounds.Top, bounds.Bottom, FbZoomedHeight) - padding.Y);
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
		if (Manager.Window is not { } window)
			return;

		EnsureRenderTargets(Width, Height);
		RenderCurrentView(window, viewport, showHud, scissor, applyCrt: true);
	}

	public void RenderWorldView(in RectInt viewport, Rect worldView, bool showHud = false, RectInt? scissor = null, bool applyCrt = true)
	{
		if (Manager.Window is not { } window)
			return;

		var oldCamera = Camera;
		var oldShake = shake;
		var width = Math.Max(1, (int)MathF.Ceiling(worldView.Width));
		var height = Math.Max(1, (int)MathF.Ceiling(worldView.Height));

		EnsureRenderTargets(width, height);
		Camera = worldView.TopLeft;
		shake = Point2.Zero;
		RenderCurrentView(window, viewport, showHud, scissor, applyCrt);
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
		TileField = CreateRenderTarget(width, height, TextureFormat.R8G8, "TileField");
		DitherMask = CreateRenderTarget(width, height, TextureFormat.R8, "DitherMask");
		ReflectionScreen = new Target(GraphicsDevice, width, height, name: "ReflectionScreen");
		ReflectionMask = new Target(GraphicsDevice, width, height, name: "ReflectionMask");
	}

	private Target CreateRenderTarget(int width, int height, TextureFormat format, string name)
	{
		ReadOnlySpan<TextureFormat> attachments = [format];
		return new Target(GraphicsDevice, width, height, attachments, name: name);
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
		ReflectionMask?.Dispose();
	}

	private void DisposeRoomNormalCaches()
	{
		foreach (var cache in roomNormalCaches.Values)
			cache.Dispose();
		roomNormalCaches.Clear();
	}

	private void DisposeRoomTileFieldCaches()
	{
		foreach (var cache in roomTileFieldCaches.Values)
			cache.Dispose();
		roomTileFieldCaches.Clear();
	}

	private void PruneInactiveRoomCaches()
	{
		PruneInactiveRoomNormalCaches();
		PruneInactiveRoomTileFieldCaches();
	}

	private void FinishRoomActivation(RoomChangeKind kind)
	{
		RemoveDestroyedActors();
		PruneInactiveRoomCaches();
		PrepareBrowserRoomTransitionGc();
		RequestRoomTransitionGc(kind);
	}

	private bool IsActiveRoomCache(Room cachedRoom)
		=> cachedRoom == room || cachedRoom == nextRoom;

	private void PruneInactiveRoomNormalCaches()
	{
		List<Room>? remove = null;
		foreach (var (cachedRoom, cache) in roomNormalCaches)
		{
			if (IsActiveRoomCache(cachedRoom))
				continue;
			remove ??= [];
			remove.Add(cachedRoom);
			cache.Dispose();
		}

		if (remove == null)
			return;
		foreach (var cachedRoom in remove)
			roomNormalCaches.Remove(cachedRoom);
	}

	private void PruneInactiveRoomTileFieldCaches()
	{
		List<Room>? remove = null;
		foreach (var (cachedRoom, cache) in roomTileFieldCaches)
		{
			if (IsActiveRoomCache(cachedRoom))
				continue;
			remove ??= [];
			remove.Add(cachedRoom);
			cache.Dispose();
		}

		if (remove == null)
			return;
		foreach (var cachedRoom in remove)
			roomTileFieldCaches.Remove(cachedRoom);
	}

#if BROWSER
	private void PrepareBrowserRoomTransitionGc()
	{
		if (spawnBud && GetFirst<Bud>() != null)
			GetPlatformNavGraph();
	}

	private void RequestRoomTransitionGc(RoomChangeKind kind)
	{
		pendingRoomTransitionGcKind = kind;
		pendingRoomTransitionGcFrames = 2;
		browserDebugFramesSinceRoomTransition = 0;
	}

	private void UpdateRoomTransitionGc()
	{
		if (pendingRoomTransitionGcFrames <= 0)
			return;

		pendingRoomTransitionGcFrames--;
		if (pendingRoomTransitionGcFrames > 0)
			return;

		var before = GC.GetTotalMemory(false);
		var gen0Before = GC.CollectionCount(0);
		var gen1Before = GC.CollectionCount(1);
		var gen2Before = GC.CollectionCount(2);
		var start = Stopwatch.GetTimestamp();
		GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: false);
		var elapsed = Stopwatch.GetElapsedTime(start);
		var after = GC.GetTotalMemory(false);
		browserDebugLastGcMs = elapsed.TotalMilliseconds;
		browserDebugLastGcBeforeBytes = before;
		browserDebugLastGcAfterBytes = after;
		browserDebugLastGcKind = pendingRoomTransitionGcKind;
		Console.WriteLine(
			$"Room transition GC ({pendingRoomTransitionGcKind}): {elapsed.TotalMilliseconds:0.0}ms, " +
			$"{before / 1024}KB -> {after / 1024}KB, " +
			$"collections +{GC.CollectionCount(0) - gen0Before}/+{GC.CollectionCount(1) - gen1Before}/+{GC.CollectionCount(2) - gen2Before}");
	}

	private void UpdateBrowserDebugStats(long frameStartAllocatedBytes)
	{
		var allocatedNow = GC.GetAllocatedBytesForCurrentThread();
		var frameAllocatedBytes = Math.Max(0, allocatedNow - frameStartAllocatedBytes);
		browserDebugStatsAllocatedBytes += frameAllocatedBytes;
		browserDebugStatsMaxFrameBytes = Math.Max(browserDebugStatsMaxFrameBytes, frameAllocatedBytes);
		browserDebugStatsFrames++;
		browserDebugStatsElapsed += Time.Delta;
		if (browserDebugFramesSinceRoomTransition >= 0)
			browserDebugFramesSinceRoomTransition++;

		if (browserDebugStatsElapsed < BrowserDebugStatsInterval)
			return;

		var gen0 = GC.CollectionCount(0);
		var gen1 = GC.CollectionCount(1);
		var gen2 = GC.CollectionCount(2);
		var frames = Math.Max(browserDebugStatsFrames, 1);
		var avgAllocatedBytes = browserDebugStatsAllocatedBytes / frames;
		var heapBytes = GC.GetTotalMemory(false);
		var transitionText = browserDebugFramesSinceRoomTransition >= 0
			? $"{browserDebugFramesSinceRoomTransition}f since transition"
			: "none";
		var gcText = browserDebugLastGcMs >= 0
			? $"{browserDebugLastGcKind} {browserDebugLastGcMs:0.0}ms {FormatBytes(browserDebugLastGcBeforeBytes)} -> {FormatBytes(browserDebugLastGcAfterBytes)}"
			: "none";

		BrowserDebugOverlay.SetStats(
			$"update alloc avg {FormatBytes(avgAllocatedBytes)}   max {FormatBytes(browserDebugStatsMaxFrameBytes)}   window {frames}f\n" +
			$"managed heap {FormatBytes(heapBytes)}   gc +{gen0 - browserDebugStatsLastGen0}/+{gen1 - browserDebugStatsLastGen1}/+{gen2 - browserDebugStatsLastGen2}\n" +
			$"last gc {gcText}   transition {transitionText}");

		browserDebugStatsElapsed = 0;
		browserDebugStatsFrames = 0;
		browserDebugStatsAllocatedBytes = 0;
		browserDebugStatsMaxFrameBytes = 0;
		browserDebugStatsLastGen0 = gen0;
		browserDebugStatsLastGen1 = gen1;
		browserDebugStatsLastGen2 = gen2;
	}

	private static string FormatBytes(long bytes)
	{
		if (bytes < 1024)
			return $"{bytes}B";

		var kb = bytes / 1024.0;
		if (kb < 1024)
			return $"{kb:0.0}KB";

		return $"{kb / 1024.0:0.0}MB";
	}
#else
	private void PrepareBrowserRoomTransitionGc() {}
	private void RequestRoomTransitionGc(RoomChangeKind kind) {}
#endif

	private void RenderCurrentView(IDrawableTarget destination, in RectInt viewport, bool showHud, RectInt? scissor, bool applyCrt)
	{
		PrepareRenderingList();

		using (PerfStats.Time(PerfStat.World))
		{
			using (GraphicsDevice.DebugGroup("Myth/background world"))
				DrawWorldLayer(BackgroundScreen, background: true);
			using (GraphicsDevice.DebugGroup("Myth/foreground world"))
				DrawWorldLayer(ForegroundScreen, background: false);
			if (PerfToggles.Reflections)
				using (GraphicsDevice.DebugGroup("Myth/flat reflections"))
					CompositeFlatReflections(ForegroundScreen);
		}
		if (PerfToggles.Emissive)
		{
			using (PerfStats.Time(PerfStat.Emissive))
			{
				using (GraphicsDevice.DebugGroup("Myth/background emissive"))
					DrawEmissiveLayer(BackgroundEmissive, background: true);
				using (GraphicsDevice.DebugGroup("Myth/foreground emissive"))
					DrawEmissiveLayer(ForegroundEmissive, background: false);
			}
		}
		else
		{
			BackgroundEmissive.Clear(Color.Transparent);
			ForegroundEmissive.Clear(Color.Transparent);
		}

		UpdateMouseLightPosition(viewport);
		if (PerfToggles.NormalBuffer)
		{
			using (PerfStats.Time(PerfStat.Normal))
			using (GraphicsDevice.DebugGroup("Myth/normal buffer"))
			{
				DrawNormalBuffer();
			}
		}
		else
		{
			NormalBuffer.Clear(new Color(0.5f, 0.5f, 1.0f, 0.0f));
		}
		if (PerfToggles.DitherAndTileField)
		{
			using (PerfStats.Time(PerfStat.TileDither))
			{
				using (GraphicsDevice.DebugGroup("Myth/tile field"))
					DrawTileField();
				using (GraphicsDevice.DebugGroup("Myth/dither mask"))
					DrawDitherMask();
			}
		}
		else
		{
			TileField.Clear(Color.Transparent);
			DitherMask.Clear(Color.White);
		}

		Target output;
		if (debugView is DebugView.TileFieldRed or DebugView.TileFieldGreen)
		{
			using (GraphicsDevice.DebugGroup("Myth/tile field debug"))
				output = DrawTileFieldDebug();
		}
		else if (debugView == DebugView.DitherMask)
		{
			output = DitherMask;
		}
		else
		{
			if (PerfToggles.Lighting)
			{
				using (PerfStats.Time(PerfStat.Lighting))
				using (GraphicsDevice.DebugGroup("Myth/lighting"))
				{
					output = ApplyLighting();
				}
			}
			else
			{
				using (PerfStats.Time(PerfStat.Lighting))
				using (GraphicsDevice.DebugGroup("Myth/raw world composite"))
				{
					output = CompositeRawWorld();
				}
			}
			if (debugView == DebugView.LightMask)
				output = BackgroundLightMask;
			else if (debugView == DebugView.NormalBuffer)
				output = NormalBuffer;
			else if (debugView == DebugView.Reflection)
				output = ReflectionScreen;
			else if (debugView == DebugView.None)
			{
				if (PerfToggles.Glitch)
				{
					using (PerfStats.Time(PerfStat.Post))
					using (GraphicsDevice.DebugGroup("Myth/pixel sort glitch"))
					{
						output = ApplyPixelSortGlitch(output);
					}
				}
			}
		}
		if (debugView is DebugView.None or DebugView.CollisionRects)
		{
			using (GraphicsDevice.DebugGroup("Myth/story speech"))
				DrawStorySpeech(output);
		}
		if (showHud && debugView is DebugView.None or DebugView.CollisionRects)
		{
			using (PerfStats.Time(PerfStat.Hud))
			using (GraphicsDevice.DebugGroup("Myth/hud"))
			{
				DrawPlayerHud(output);
			}
		}
		if (debugView == DebugView.Visibility)
		{
			using (GraphicsDevice.DebugGroup("Myth/visibility debug"))
				DrawVisibilityDebug(output);
		}
		if (debugView == DebugView.PlatformNav)
		{
			GetPlatformNavGraph();
		}

		// draw screen to window
		var crt = applyCrt && PerfToggles.Crt ? crtMaterial : null;
		var useCrt = crt != null;
		using (PerfStats.Time(PerfStat.FinalComposite))
		using (GraphicsDevice.DebugGroup(useCrt ? "Myth/final composite crt" : "Myth/final composite"))
		{
			var size = viewport.Size;
			var center = viewport.Center;
			var scale = Calc.Min(size.X / (float)output.Width, size.Y / (float)output.Height);

			Batcher.PushSampler(new(TextureFilter.Nearest, TextureWrap.Clamp, TextureWrap.Clamp));
			if (useCrt)
			{
				var CrtTime = Time.SecondsF * 0.2f;
				const float BloomAmount = 0.09f;
				const float ScanlineAmount = 0.22f;
				const float VignetteAmount = 0.06f;
				const float RgbOffset = 0.35f;
				crt!.Material.Fragment.SetUniformBuffer([
					Screen.Width,
					Screen.Height,
					CrtTime,
					BloomAmount,
					ScanlineAmount,
					VignetteAmount,
					RgbOffset,
				]);
				Batcher.PushMaterial(crt.Material);
			}
#if BROWSER
			var flipTargetTextures = Batcher.FlipTargetTextures;
			Batcher.FlipTargetTextures = false;
#endif
			Batcher.Image(output, center, output.Bounds.Size / 2, Vector2.One * scale * fbZoom, 0, Color.White);
#if BROWSER
			Batcher.FlipTargetTextures = flipTargetTextures;
#endif
			if (useCrt)
				Batcher.PopMaterial();
			Batcher.PopSampler();
			Batcher.Render(destination, scissor: scissor);
			Batcher.Clear();
		}
	}

	private Target CompositeRawWorld()
	{
		Screen.Clear(Color.Transparent);
		Batcher.PushSampler(new(TextureFilter.Nearest, TextureWrap.Clamp, TextureWrap.Clamp));
		Batcher.Image(BackgroundScreen, BackgroundScreen.Bounds.Center, BackgroundScreen.Bounds.Size / 2, Vector2.One, 0, Color.White);
		Batcher.Image(ForegroundScreen, ForegroundScreen.Bounds.Center, ForegroundScreen.Bounds.Size / 2, Vector2.One, 0, Color.White);
		Batcher.PopSampler();
		Batcher.Render(Screen);
		Batcher.Clear();
		return Screen;
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
			_ => 0
		};

	private void DrawWorldLayer(Target target, bool background)
	{
		target.Clear(Color.Transparent);
		if (background)
			DrawBackdrop(target);

		if ((background && !ShowBackgroundLayer) || (!background && !ShowForegroundLayer))
		{
			Batcher.Render(target);
			Batcher.Clear();
			return;
		}

		PushWorldMatrix();

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

		if (background && PerfToggles.Reflections)
			DrawSpriteReflections(target);
	}

	private void DrawBackdrop(Target target)
	{
		var (top, bottom) = CurrentBackdropColors();
		Batcher.Rect(new Rect(0, 0, target.Width, target.Height), top, top, bottom, bottom);
	}

	private void DrawPlayerHud(Target target)
	{
		if (GetFirst<Player>() is { } player)
		{
			Point2 pos = new(0, target.Height - 16);
			Batcher.Rect(new Rect(pos.X, pos.Y + 7, 48, 4), Color.Black);

			for (var i = 0; i < Player.MaxHealth; i++)
			{
				Batcher.Image(
					player.Health >= i + 1 ? Assets.GetSubtexture("heart/0") : Assets.GetSubtexture("heart/1"), pos,
					Color.White);
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
		PushWorldMatrix();
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
			var scissor = GetReflectionScissor(rect.Bounds);
			if (scissor.Width <= 0 || scissor.Height <= 0)
				continue;

			var surfaceTop = ReflectionSurfaceTop(rect.Bounds);
			PushWorldMatrix();
			foreach (var actor in rendering)
			{
				if (!actor.Visible || !actor.CastsReflection)
					continue;
				if (!ActorCanReflectIn(actor, rect.Bounds))
					continue;

				Batcher.PushMatrix(Matrix3x2.CreateScale(1, -1, new Vector2(actor.Position.X, surfaceTop)));
				Batcher.PushMatrix(actor.Position);
				actor.RenderReflection(Batcher, Color.White * rect.Opacity);
				Batcher.PopMatrix();
				Batcher.PopMatrix();
			}
			Batcher.PopMatrix();
			Batcher.Render(ReflectionScreen, scissor: scissor);
			Batcher.Clear();
		}

		ReflectionMask.Clear(Color.Transparent);
		foreach (var rect in room.ReflectionRects)
		{
			if (!rect.Distorted) continue;
			var scissor = GetReflectionScissor(rect.Bounds);
			if (scissor.Width <= 0 || scissor.Height <= 0) continue;
			Batcher.Rect(new Rect(scissor.X, scissor.Y, scissor.Width, scissor.Height), Color.White);
		}
		Batcher.Render(ReflectionMask);
		Batcher.Clear();

		CompositeReflections(target);
	}

	private RectInt GetReflectionScissor(RectInt rect)
	{
		var screenRect = new RectInt(
			(int)MathF.Round((rect.X - Camera.X + shake.X) * zoomLevel),
			(int)MathF.Round((rect.Y - Camera.Y + shake.Y) * zoomLevel),
			(int)MathF.Round(rect.Width * zoomLevel),
			(int)MathF.Round(rect.Height * zoomLevel));
		return screenRect.GetIntersection(ReflectionScreen.Bounds);
	}

	private void CompositeReflections(Target target) => BlitReflections(target, flatOnly: 0.0f);

	private void CompositeFlatReflections(Target target)
	{
		if (room == null || !room.ReflectionRects.Any(r => !r.Distorted))
			return;
		BlitReflections(target, flatOnly: 1.0f);
	}

	private void BlitReflections(Target target, float flatOnly)
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
				flatOnly,
				0
			]);
			reflectionDistortMaterial.Material.Fragment.Samplers[1] = new(ReflectionMask, new(TextureFilter.Nearest, TextureWrap.Clamp, TextureWrap.Clamp));
			Batcher.PushMaterial(reflectionDistortMaterial.Material);
		}
		Batcher.Image(ReflectionScreen, ReflectionScreen.Bounds.Center, ReflectionScreen.Bounds.Size / 2, Vector2.One, 0, Color.White);
		if (reflectionDistortMaterial != null)
			Batcher.PopMaterial();
		Batcher.PopSampler();
		Batcher.Render(target);
		Batcher.Clear();
	}

	private int ReflectionSurfaceTop(RectInt rect)
	{
		if (room == null)
			return rect.Top;

		var rects = room.ReflectionRects;
		var start = rects.FindIndex(r => r.Bounds == rect);
		if (start < 0)
			return rect.Top;

		var visited = new bool[rects.Count];
		var pending = new Stack<int>();
		pending.Push(start);
		visited[start] = true;

		var top = rect.Top;
		while (pending.Count > 0)
		{
			var current = rects[pending.Pop()].Bounds;
			top = Math.Min(top, current.Top);

			for (var i = 0; i < rects.Count; i++)
			{
				if (visited[i] || !ReflectionRectsTouch(current, rects[i].Bounds))
					continue;

				visited[i] = true;
				pending.Push(i);
			}
		}

		return top;
	}

	private static bool ReflectionRectsTouch(RectInt a, RectInt b)
		=> a.Left <= b.Right &&
		   a.Right >= b.Left &&
		   a.Top <= b.Bottom &&
		   a.Bottom >= b.Top;

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

		if (PerfToggles.LightingNoise)
			ApplyLightingNoise(LitScreen);

		return LitScreen;
	}

	private void ApplyLightingNoise(Target target)
	{
		if (Assets.LightingNoise == null)
			return;

		var renderCamera = RenderCamera;
		Batcher.PushMatrix(Matrix3x2.CreateScale(zoomLevel));
		Batcher.PushBlend(BlendMode.Multiply);
		Batcher.PushSampler(new(TextureFilter.Nearest, TextureWrap.Repeat, TextureWrap.Repeat));
		Batcher.Image(
			Assets.LightingNoise,
			new Rect(renderCamera.X - Time.SecondsF * 5.0f, renderCamera.Y, ZoomedWidth, ZoomedHeight),
			Vector2.Zero,
			Color.White * CurrentLightNoise());
		Batcher.PopSampler();
		Batcher.PopBlend();
		Batcher.PopMatrix();
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
		var renderCamera = RenderCamera;
		pixelSortGlitchMaterial.Material.Fragment.SetUniformBuffer([
			input.Width,
			input.Height,
			renderCamera.X,
			renderCamera.Y,
			ditherGlitch,
			glitchPosition.X,
			glitchPosition.Y,
			DitherGlitchRadius,
			Time.SecondsF,
			ditherGlitchAxis.X,
			ditherGlitchAxis.Y,
			zoomLevel
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
		var renderCamera = RenderCamera;
		lightingCompositeMaterial.Material.Fragment.SetUniformBuffer([
			output.Width,
			output.Height,
			renderCamera.X,
			renderCamera.Y,
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
			zoomLevel,
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
		RoomTileFieldCache? currentRoomTileFieldCache = null;
		RoomTileFieldCache? nextRoomTileFieldCache = null;
		if (room != null)
			currentRoomTileFieldCache = GetRoomTileFieldCache(room);
		if (nextRoom != null && nextRoom != room)
			nextRoomTileFieldCache = GetRoomTileFieldCache(nextRoom);

		TileField.Clear(Color.Transparent);
		PushWorldMatrix();
		Batcher.PushSampler(new(TextureFilter.Nearest, TextureWrap.Clamp, TextureWrap.Clamp));
		if (currentRoomTileFieldCache != null)
			DrawCachedRoomTileField(currentRoomTileFieldCache);
		if (nextRoomTileFieldCache != null)
			DrawCachedRoomTileField(nextRoomTileFieldCache);
		Batcher.PopSampler();
		Batcher.PopMatrix();
		Batcher.Render(TileField);
		Batcher.Clear();
	}

	private void DrawCachedRoomTileField(RoomTileFieldCache cache) =>
		Batcher.Image(cache.Target, cache.Room.WorldBounds.TopLeft, Color.White);

	private RoomTileFieldCache GetRoomTileFieldCache(Room renderRoom)
	{
		var bounds = renderRoom.WorldBounds;
		if (!roomTileFieldCaches.TryGetValue(renderRoom, out var cache) ||
			cache.Target.Width != bounds.Width ||
			cache.Target.Height != bounds.Height ||
			cache.Target.Attachments[0].Format != TextureFormat.R8G8)
		{
			cache?.Dispose();
			cache = new(renderRoom, CreateRenderTarget(bounds.Width, bounds.Height, TextureFormat.R8G8, $"RoomTileField:{renderRoom.Id}"));
			roomTileFieldCaches[renderRoom] = cache;
		}

		if (cache.Dirty)
			RebuildRoomTileFieldCache(cache);

		return cache;
	}

	private void MarkRoomTileFieldCacheDirty(Room renderRoom)
	{
		if (roomTileFieldCaches.TryGetValue(renderRoom, out var cache))
			cache.Dirty = true;
	}

	private void RebuildRoomTileFieldCache(RoomTileFieldCache cache)
	{
		var renderRoom = cache.Room;
		var target = cache.Target;
		var previousMatrix = Batcher.Matrix;
		Batcher.Matrix = Matrix3x2.CreateTranslation(-renderRoom.WorldBounds.TopLeft);

		target.Clear(Color.Transparent);
		Batcher.PushSampler(new(TextureFilter.Nearest, TextureWrap.Clamp, TextureWrap.Clamp));
		Batcher.PushBlend(TileFieldStampBlend);
		DrawRoomTileField(renderRoom);
		Batcher.PopBlend();
		Batcher.PopSampler();
		Batcher.Render(target);
		Batcher.Clear();
		Batcher.Matrix = previousMatrix;
		cache.Dirty = false;
	}

	private void DrawDitherMask()
	{
		DitherMask.Clear(Color.White);
		PushWorldMatrix();
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
		DrawRoomTileFieldBaseline(renderRoom);
		if (tileFieldStampMaterial != null)
			Batcher.PushMaterial(tileFieldStampMaterial.Material);
		DrawRoomVisualTileField(renderRoom);
		DrawRoomDualTileField(renderRoom, Factory.TileLayer.Foreground);
		if (tileFieldStampMaterial != null)
			Batcher.PopMaterial();
	}

	private void DrawRoomTileFieldBaseline(Room renderRoom)
	{
		var baselineTile = tileFieldStampMaterial == null ? (char?)null : '0';
		var drawRect = new Rect(0, 0, TileSize, TileSize);
		for (var y = 0; y < renderRoom.Size.Y; y++)
			for (var x = 0; x < renderRoom.Size.X; x++)
			{
				var worldTile = renderRoom.TileSpacePosition + new Point2(x, y);
				drawRect.Position = renderRoom.WorldBounds.Position + new Point2(x, y) * TileSize;
				Batcher.Rect(drawRect, TileFieldColor(baselineTile ?? renderRoom.Tiles[x, y], worldTile));
			}
	}

	private void DrawRoomVisualTileField(Room renderRoom)
	{
		for (var y = 0; y < renderRoom.Size.Y; y++)
			for (var x = 0; x < renderRoom.Size.X; x++)
			{
				var tile = renderRoom.Tiles[x, y];
				if (Factory.Find(tile) is not { Layer: Factory.TileLayer.Foreground, DualTilesetName: null } entry)
					continue;

				var image = entry.Image;
				if (entry is { IsSlope: true, SlopeTilesetName: not null } && renderRoom.TryGetSlopeTile(new Point2(x, y), out var slope))
				{
					var seed = Factory.StableVariantSeed(renderRoom, entry.SlopeTilesetName, new Point2(x, y));
					image = Assets.GetSlopeSubtexture(entry.SlopeTilesetName, slope.Direction, slope.Fill, slope.RunLength, slope.RunIndex, seed);
				}
				else if (entry.TilesetName != null && Assets.GetTileset(entry.TilesetName) is { } tileset)
				{
					Rng localRng = new(x + y * renderRoom.Size.X);
					image = tileset.GetRandomTile(ref localRng);
				}

				if (image.Texture == null)
					continue;

				var worldTile = renderRoom.TileSpacePosition + new Point2(x, y);
				Batcher.Image(
					image,
					new Vector2(renderRoom.WorldBounds.X + x * TileSize, renderRoom.WorldBounds.Y + y * TileSize),
					TileFieldColor(tile, worldTile));
			}
	}

	private void DrawRoomDualTileField(Room renderRoom, Factory.TileLayer layer)
	{
		foreach (var (tileType, entry) in Factory.Entries)
		{
			if (entry.Layer != layer ||
				entry.DualTilesetName == null ||
				Assets.GetTileset(entry.DualTilesetName) is not { } tileset)
				continue;

			for (var y = 0; y <= renderRoom.Size.Y; y++)
				for (var x = 0; x <= renderRoom.Size.X; x++)
				{
					var materialMask = 0;
					if (renderRoom.IsDualMaterialFilled(tileType, layer, x - 1, y - 1))
						materialMask |= TileQuadrants.TopLeft;
					if (renderRoom.IsDualMaterialFilled(tileType, layer, x - 1, y))
						materialMask |= TileQuadrants.BottomLeft;
					if (renderRoom.IsDualMaterialFilled(tileType, layer, x, y - 1))
						materialMask |= TileQuadrants.TopRight;
					if (renderRoom.IsDualMaterialFilled(tileType, layer, x, y))
						materialMask |= TileQuadrants.BottomRight;

					if (materialMask == 0)
						continue;

					var joinMask = layer == Factory.TileLayer.Foreground
						? renderRoom.GetDualTerrainJoinMask(x, y)
						: materialMask;
					if (joinMask == 0)
						continue;

					var dualTile = DualMaskToTile[joinMask];
					var seed = StableDualVariantSeed(renderRoom, tileType, x, y);
					var image = tileset.GetVariantTile(dualTile.X, dualTile.Y, seed);
					if (image.Texture == null)
						continue;

					var position = new Vector2(
						renderRoom.WorldBounds.X + (x - 0.5f) * TileSize,
						renderRoom.WorldBounds.Y + (y - 0.5f) * TileSize);
					var color = TileFieldColor(tileType, renderRoom.TileSpacePosition + new Point2(x, y));
					DrawDualTileFieldQuadrants(image, position, materialMask, color);
				}
		}
	}

	private void DrawDualTileFieldQuadrants(Subtexture image, Vector2 position, int materialMask, Color color)
	{
		if (materialMask == TileQuadrants.All)
		{
			Batcher.Image(image, position, color);
			return;
		}

		const int halfTileSize = (TileSize / 2);

		DrawDualTileFieldQuadrant(image, position, materialMask, color, TileQuadrants.TopLeft, new Rect(0, 0, halfTileSize, halfTileSize));
		DrawDualTileFieldQuadrant(image, position, materialMask, color, TileQuadrants.BottomLeft, new Rect(0, halfTileSize, halfTileSize, halfTileSize));
		DrawDualTileFieldQuadrant(image, position, materialMask, color, TileQuadrants.TopRight, new Rect(halfTileSize, 0, halfTileSize, halfTileSize));
		DrawDualTileFieldQuadrant(image, position, materialMask, color, TileQuadrants.BottomRight, new Rect(halfTileSize, halfTileSize, halfTileSize, halfTileSize));
	}

	private void DrawDualTileFieldQuadrant(Subtexture image, Vector2 position, int materialMask, Color color, int mask, Rect clip)
	{
		if ((materialMask & mask) == 0)
			return;

		Batcher.Image(image, clip, position + clip.Position, Vector2.Zero, Vector2.One, 0, color);
	}

	private static int StableDualVariantSeed(Room room, char tileType, int x, int y)
	{
		unchecked
		{
			var worldTile = room.WorldBounds.TopLeft / TileSize + new Point2(x, y);
			var hash = (uint)worldTile.X * 0x8da6b343u;
			hash ^= (uint)worldTile.Y * 0xd8163841u;
			hash ^= tileType * 0xcb1ab31fu;
			hash ^= hash >> 16;
			hash *= 0x7feb352du;
			hash ^= hash >> 15;
			hash *= 0x846ca68bu;
			hash ^= hash >> 16;
			return (int)hash;
		}
	}

	private static Color TileFieldColor(char tile, Point2 worldTile)
	{
		var hash = TileFieldHash(tile, worldTile);
		var variant = (hash & 255) / 255.0f;
		var family = TileFieldFamily(tile);
		return new Color(variant, family, 0.0f, 1.0f);
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

	private (Color Top, Color Bottom) CurrentBackdropColors()
	{
		if (nextRoom == null)
		{
			var top = room?.BackdropColor ?? Room.DefaultBackdropColor;
			return (top, room?.BackdropColor2 ?? top);
		}

		var amount = Ease.Cube.InOut(nextRoomEase);
		return (
			LerpColor(transitionBackdropColorFrom, transitionBackdropColorTo, amount),
			LerpColor(transitionBackdropColor2From, transitionBackdropColor2To, amount));
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
			PushWorldMatrix();
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
			var screenPosition = (position - RenderCamera) * zoomLevel;
			var lightColorValue = (Vector4)lightColor;
			normalLightMaterial.Material.Fragment.SetUniformBuffer([
				NormalBuffer.Width,
				NormalBuffer.Height,
				0,
				0,
				screenPosition.X,
				screenPosition.Y,
				radius * zoomLevel,
				height * zoomLevel,
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
			PushWorldMatrix();
			var color = ScaleColor(lightColor, intensity);
			Batcher.Circle(position, radius, 32, color, Color.Transparent);
			Batcher.PopMatrix();
		}

		if (includeShadows && !drewVisibilityClippedLight)
		{
			PushWorldMatrix();
			Batcher.PushBlend(ShadowBlend);
			DrawLightShadows(position, radius);
			Batcher.PopBlend();
			Batcher.PopMatrix();
		}

		Batcher.Render(SingleLightMask);
		Batcher.Clear();
	}

	private static readonly List<VisibilityPolygon.Hit> hits = new();
	private static readonly List<VisibilityPolygon.Segment> _segments = new();

	private void DrawVisibilityClippedNormalLight(Vector2 lightPosition, float radius)
	{
		GatherVisibilitySegments(lightPosition, radius, _segments);
		VisibilityPolygon.Compute(lightPosition, radius, _segments, hits);
		if (hits.Count < 3)
		{
			Batcher.Image(NormalBuffer, NormalBuffer.Bounds.Center, NormalBuffer.Bounds.Size / 2, Vector2.One, 0, Color.White);
			return;
		}

		var renderCamera = RenderCamera;
		var screenLight = (lightPosition - renderCamera) * zoomLevel;
		var texture = NormalBuffer.Attachments[0];
		var textureSize = new Vector2(NormalBuffer.Width, NormalBuffer.Height);
		for (var i = 0; i < hits.Count; i++)
		{
			var a = (hits[i].Point - renderCamera) * zoomLevel;
			var b = (hits[(i + 1) % hits.Count].Point - renderCamera) * zoomLevel;
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
		Batcher.PushBlend(new(BlendOp.Add, BlendFactor.One, BlendFactor.One));
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

	internal IEnumerable<Room> VisibleRooms()
	{
		if (room != null) yield return room;
		if (nextRoom != null && nextRoom != room) yield return nextRoom;
	}

	private void DrawSunShadows(Vector2 direction)
	{
		var distance = SunShadowDistance();
		foreach (var visibleRoom in VisibleRooms())
			DrawRoomSunShadows(visibleRoom, direction, distance);

		foreach (var actor in Actors)
		{
			if (actor is HiddenCoverGrid hiddenCover)
				hiddenCover.AppendAllVisibilitySegments(sunSegmentBuffer);
			if (actor is DualGrid { Layer: Factory.TileLayer.Foreground } dualGrid)
				dualGrid.AppendAllVisibilitySegments(sunSegmentBuffer);
		}

		foreach (var segment in sunSegmentBuffer)
			DrawSunShadowSegment(segment, direction, distance);
		sunSegmentBuffer.Clear();

		foreach (var actor in Actors)
			if (actor is HiddenWallGrid hiddenWalls)
				hiddenWalls.ForEachVisibleCell((rect, _, _) => DrawSunShadowVolume(direction, distance, rect));
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

			for (var i = 1; i <= ActorShadowSteps; i++)
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

		for (var i = 0; i < points.Length; i++)
			for (var j = i + 1; j < points.Length; j++)
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

	private void GatherVisibilitySegments(Vector2 lightPosition, float radius, List<VisibilityPolygon.Segment> segments)
	{
		segments.Clear();
		foreach (var visibleRoom in VisibleRooms())
			AddRoomVisibilitySegments(segments, visibleRoom, lightPosition, radius);

		foreach (var actor in Actors)
		{
			if (actor is DualGrid { Layer: Factory.TileLayer.Foreground } dualGrid)
			{
				dualGrid.AppendVisibilitySegments(segments, lightPosition, radius);
				continue;
			}
			if (actor is HiddenCoverGrid hiddenCover)
			{
				hiddenCover.AppendVisibilitySegments(segments, lightPosition, radius);
				continue;
			}

			if (actor is HiddenWallGrid hiddenWalls)
			{
				hiddenWalls.ForEachVisibleCell((rect, _, _) =>
					AddRectVisibilitySegments(segments, rect, lightPosition, radius));
			}
		}
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

	private void DrawNormalBuffer()
	{
		NormalBuffer.Clear(new Color(0.5f, 0.5f, 1.0f, 0.0f));
		RoomNormalCache? currentRoomNormalCache = null;
		RoomNormalCache? nextRoomNormalCache = null;
		if (ShowForegroundLayer)
		{
			if (room != null)
				currentRoomNormalCache = GetRoomNormalCache(room);
			if (nextRoom != null && nextRoom != room)
				nextRoomNormalCache = GetRoomNormalCache(nextRoom);
		}

		PushWorldMatrix();

		if (ShowForegroundLayer)
		{
			if (currentRoomNormalCache != null)
				DrawCachedRoomNormals(currentRoomNormalCache);
			if (nextRoomNormalCache != null)
				DrawCachedRoomNormals(nextRoomNormalCache);
		}

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

	private void DrawCachedRoomNormals(RoomNormalCache cache)
	{
		Batcher.Image(cache.Target, cache.Room.WorldBounds.TopLeft, Color.White);
	}

	private RoomNormalCache GetRoomNormalCache(Room renderRoom)
	{
		var bounds = renderRoom.WorldBounds;
		if (!roomNormalCaches.TryGetValue(renderRoom, out var cache) ||
			cache.Target.Width != bounds.Width ||
			cache.Target.Height != bounds.Height)
		{
			cache?.Dispose();
			cache = new(renderRoom, new Target(GraphicsDevice, bounds.Width, bounds.Height, name: $"RoomNormals:{renderRoom.Id}"));
			roomNormalCaches[renderRoom] = cache;
		}

		if (cache.Dirty)
			RebuildRoomNormalCache(cache);

		return cache;
	}

	private void MarkRoomNormalCacheDirty(Room renderRoom)
	{
		if (roomNormalCaches.TryGetValue(renderRoom, out var cache))
			cache.Dirty = true;
	}

	private void RebuildRoomNormalCache(RoomNormalCache cache)
	{
		var renderRoom = cache.Room;
		var target = cache.Target;
		var mask = new bool[target.Width, target.Height];
		var suppressMask = new bool[target.Width, target.Height];
		var origin = renderRoom.WorldBounds.TopLeft;

		FillRoomNormalMask(mask, renderRoom, origin);
		SuppressBakedDualRoomNormals(suppressMask, renderRoom);

		var previousMatrix = Batcher.Matrix;
		Batcher.Matrix = Matrix3x2.Identity;
		target.Clear(Color.Transparent);
		for (var y = 0; y < target.Height; y++)
		{
			var runStart = -1;
			var runColor = Color.Transparent;

			for (var x = 0; x < target.Width; x++)
			{
				var color = RoomNormalColor(mask, suppressMask, x, y);
				if (color.A > 0 && runStart >= 0 && color == runColor)
					continue;

				if (runStart >= 0)
					Batcher.Rect(new Rect(runStart, y, x - runStart, 1), runColor);

				if (color.A > 0)
				{
					runStart = x;
					runColor = color;
				}
				else
				{
					runStart = -1;
				}
			}

			if (runStart >= 0)
				Batcher.Rect(new Rect(runStart, y, target.Width - runStart, 1), runColor);
		}
		Batcher.Render(target);
		Batcher.Clear();
		Batcher.Matrix = previousMatrix;
		cache.Dirty = false;
	}

	private static Color RoomNormalColor(bool[,] mask, bool[,] suppressMask, int x, int y)
	{
		if (!RoomNormalSolid(mask, x, y))
			return Color.Transparent;

		var sx = SolidValue(mask, x - 1, y) - SolidValue(mask, x + 1, y);
		var sy = SolidValue(mask, x, y - 1) - SolidValue(mask, x, y + 1);
		if (sx == 0 && sy == 0)
			return Color.Transparent;
		if (RoomNormalSuppressed(suppressMask, x, y))
			return Color.Transparent;

		return EncodeNormal(new Vector3(sx, sy, 1), 1);
	}

	private void FillRoomNormalMask(bool[,] mask, Room renderRoom, Point2 origin)
	{
		foreach (var rect in renderRoom.CollisionRects)
			FillRoomNormalMask(mask, rect, origin);
		foreach (var polygon in renderRoom.LightOccluderPolygons)
			FillRoomNormalMask(mask, polygon, origin);
	}

	private static void SuppressBakedDualRoomNormals(bool[,] suppressMask, Room renderRoom)
	{
		for (var tileY = 0; tileY < renderRoom.Size.Y; tileY++)
			for (var tileX = 0; tileX < renderRoom.Size.X; tileX++)
			{
				if (renderRoom.GetEntry(new Point2(tileX, tileY)) is not { DualTilesetName: { } dualTilesetName } ||
					Assets.GetTileset(dualTilesetName) is not { NormalTiles.Count: > 0 } and not { NormalVariants.Count: > 0 })
					continue;

				var left = Math.Clamp(tileX * Game.TileSize, 0, suppressMask.GetLength(0));
				var right = Math.Clamp((tileX + 1) * Game.TileSize, 0, suppressMask.GetLength(0));
				var top = Math.Clamp(tileY * Game.TileSize, 0, suppressMask.GetLength(1));
				var bottom = Math.Clamp((tileY + 1) * Game.TileSize, 0, suppressMask.GetLength(1));

				for (var y = top; y < bottom; y++)
					for (var x = left; x < right; x++)
						suppressMask[x, y] = true;
			}
	}

	private static void FillRoomNormalMask(bool[,] mask, RectInt rect, Point2 origin)
	{
		var left = Math.Clamp(rect.Left - origin.X, 0, mask.GetLength(0));
		var right = Math.Clamp(rect.Right - origin.X, 0, mask.GetLength(0));
		var top = Math.Clamp(rect.Top - origin.Y, 0, mask.GetLength(1));
		var bottom = Math.Clamp(rect.Bottom - origin.Y, 0, mask.GetLength(1));

		for (var y = top; y < bottom; y++)
			for (var x = left; x < right; x++)
				mask[x, y] = true;
	}

	private static void FillRoomNormalMask(bool[,] mask, Room.LightOccluderPolygon polygon, Point2 origin)
	{
		var left = Math.Clamp(polygon.Bounds.Left - origin.X, 0, mask.GetLength(0));
		var right = Math.Clamp(polygon.Bounds.Right - origin.X, 0, mask.GetLength(0));
		var top = Math.Clamp(polygon.Bounds.Top - origin.Y, 0, mask.GetLength(1));
		var bottom = Math.Clamp(polygon.Bounds.Bottom - origin.Y, 0, mask.GetLength(1));

		for (var y = top; y < bottom; y++)
			for (var x = left; x < right; x++)
			{
				var worldPoint = new Vector2(origin.X + x + 0.5f, origin.Y + y + 0.5f);
				if (PointInPolygon(worldPoint, polygon.Points))
					mask[x, y] = true;
			}
	}

	private static bool PointInPolygon(Vector2 point, ReadOnlySpan<Point2> polygon)
	{
		var inside = false;
		for (var i = 0; i < polygon.Length; i++)
		{
			var a = (Vector2)polygon[i];
			var b = (Vector2)polygon[(i + polygon.Length - 1) % polygon.Length];
			if ((a.Y > point.Y) != (b.Y > point.Y) &&
				point.X < (b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y) + a.X)
				inside = !inside;
		}

		return inside;
	}

	private static bool RoomNormalSolid(bool[,] mask, int x, int y)
	{
		if (x < 0 || y < 0 || x >= mask.GetLength(0) || y >= mask.GetLength(1))
			return false;

		return mask[x, y];
	}

	private static bool RoomNormalSuppressed(bool[,] suppressMask, int x, int y)
	{
		if (x < 0 || y < 0 || x >= suppressMask.GetLength(0) || y >= suppressMask.GetLength(1))
			return false;

		return suppressMask[x, y];
	}

	private static float SolidValue(bool[,] mask, int x, int y)
		=> RoomNormalSolid(mask, x, y) ? 1 : 0;

	private static Color EncodeNormal(Vector3 normal, float strength)
	{
		normal = Vector3.Normalize(normal) * 0.5f + Vector3.One * 0.5f;
		return new Color(normal.X, normal.Y, normal.Z, strength);
	}

	private ReloadableMaterial? CreateFragmentMaterial(string name, int samplerCount, int uniformBufferCount = 0)
	{
		try
		{
			return new ReloadableMaterial(GraphicsDevice, name, samplerCount, uniformBufferCount);
		}
		catch (Exception e)
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
		{
			if (actor.Mask.Has(Actor.Masks.Enemy))
				NotifyEnemyKilledIfPlayerCaused(actor);
			destroying.Add(actor);
		}
	}

	public void DestroyForThisSession(Actor actor)
	{
		if (!string.IsNullOrEmpty(actor.SessionDestroyKey))
			sessionDestroyedActors.Add(actor.SessionDestroyKey);
		Destroy(actor);
	}

	private void RemoveDestroyedActors()
	{
		for (var i = 0; i < destroying.Count; i++)
		{
			destroying[i].Destroyed();
			Actors.Remove(destroying[i]);
		}
		destroying.Clear();
	}

	public void SetHiddenCoverEditorOpacity(bool focused)
	{
		foreach (var actor in Actors)
			if (actor is HiddenCoverGrid hiddenCover)
				hiddenCover.SetRenderOpacityScale(IsEditorPreview ? focused ? 1.0f : 0.10f : 1.0f);
	}

	public string GetSurfaceTypeBelow(Actor actor, bool searchOutward = false)
	{
		if (room == null || actor.Hitbox.Shape != Hitbox.Shapes.Rect)
			return Factory.DefaultSurfaceType;

		var bounds = actor.Hitbox.Rect + actor.Position;
		var y = bounds.Bottom;
		if (TryGetSurfaceTypeAtWorldPoint(new(bounds.Left + 1, y), out var surfaceType))
			return surfaceType;
		if (TryGetSurfaceTypeAtWorldPoint(new(bounds.Right - 1, y), out surfaceType))
			return surfaceType;

		// TODO: make a more generic system for landing on Actors, and let them provide a surface type.
		var hitActor = OverlapsFirst(actor.Hitbox.Rect + actor.Position + new Point2(0, 1), Actor.Masks.Jumpthru);
		if (hitActor != null) return "Wood";

		// Edge landings can sit just beside the supporting tile, so landing audio gets a one-tile horizontal grace scan.
		if (searchOutward && TryGetSurfaceTypeOutwardFromBottom(bounds, out surfaceType))
			return surfaceType;

		return Factory.DefaultSurfaceType;
	}

	private bool TryGetSurfaceTypeAtWorldPoint(Point2 point, out string surfaceType)
	{
		surfaceType = Factory.DefaultSurfaceType;
		if (room == null)
			return false;

		var local = point - room.WorldBounds.TopLeft;
		var tile = new Point2(
			(int)MathF.Floor(local.X / (float)TileSize),
			(int)MathF.Floor(local.Y / (float)TileSize));

		if (tile.X < 0 || tile.Y < 0 || tile.X >= room.Size.X || tile.Y >= room.Size.Y)
			return false;

		if (room.GetEntry(tile) is { SurfaceType: { } foundSurfaceType })
		{
			surfaceType = foundSurfaceType;
			return true;
		}

		return false;
	}

	private bool TryGetSurfaceTypeOutwardFromBottom(RectInt bounds, out string surfaceType)
	{
		var centerX = bounds.Center.X;
		var y = bounds.Bottom;
		for (var offset = 0; offset <= TileSize; offset++)
		{
			if (TryGetSurfaceTypeAtWorldPoint(new(centerX - offset, y), out surfaceType))
				return true;
			if (offset > 0 && TryGetSurfaceTypeAtWorldPoint(new(centerX + offset, y), out surfaceType))
				return true;
		}

		surfaceType = Factory.DefaultSurfaceType;
		return false;
	}

	public bool Transition(Point2 direction)
	{
		if (room != null && nextRoom == null)
		{
			var fromRoom = room;
			var exitPosition = GetFirst<Player>()?.Position ?? (Vector2)room.WorldBounds.Center;
			if (Assets.FindAdjacentRoom(room, direction, exitPosition) is { } foundRoom)
			{
				nextRoom = foundRoom.CloneRuntime();
				transitionDirection = new Point2(Math.Sign(direction.X), Math.Sign(direction.Y));
				foreach (var it in Actors)
					if (it is not Player && !it.KeepOnRoomTransition)
						Destroy(it);

				transitionCameraFrom = Camera;
				var fbPad = new Vector2((Width - FbZoomedWidth) / 2, (Height - FbZoomedHeight) / 2);
				transitionCameraTo = ClampCameraToRoomFb(exitPosition - new Vector2(Width, Height) / 2, fbPad, nextRoom.WorldBounds);
				transitionBackdropColorFrom = room.BackdropColor;
				transitionBackdropColorTo = nextRoom.BackdropColor;
				transitionBackdropColor2From = room.BackdropColor2 ?? room.BackdropColor;
				transitionBackdropColor2To = nextRoom.BackdropColor2 ?? nextRoom.BackdropColor;
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
				RaiseRoomChanged(fromRoom, nextRoom, RoomChangeKind.EdgeTransition, transitionDirection);
				ApplyMusicArea(nextRoom);
				ApplyRoomReverb(nextRoom);
				return true;
			}
		}

		return false;
	}

	public bool TransitionToLinkedDoor(string targetActorId)
	{
		if (room == null || nextRoom != null || linkedDoorCooldown > 0 || GetFirst<Player>() is not { } player)
			return false;
		if (string.IsNullOrWhiteSpace(targetActorId))
			return false;
		if (Assets.FindActorById(targetActorId) is not { } target)
			return false;

		var resolvedTarget = ActorSpecs.Resolve(target.Actor);
		if (!string.Equals(ActorSpecs.ReadString(resolvedTarget, "Type"), "LinkedDoor", StringComparison.OrdinalIgnoreCase))
			return false;

		ReadLinkedDoorDestination(resolvedTarget, out var localPosition, out var spawnOffset);
		var fromRoom = room;
		var targetRoom = target.Room.CloneRuntime();

		foreach (var actor in Actors)
			if (actor is not Player && !actor.KeepOnRoomTransition)
			{
				actor.Visible = false;
				Destroy(actor);
			}

		room = targetRoom;
		nextRoom = null;
		transitionDirection = Point2.Zero;
		LoadRoom(room);

		var targetPosition = room.WorldBounds.TopLeft + localPosition + spawnOffset;
		player.Position = targetPosition;
		player.Velocity = Vector2.Zero;
		var fbPad = new Vector2((Width - FbZoomedWidth) / 2, (Height - FbZoomedHeight) / 2);
		Camera = ClampCameraToRoomFb(player.Position - new Vector2(Width, Height) / 2, fbPad);
		ApplyMusicArea(room);
		ApplyRoomReverb(room);
		linkedDoorCooldown = 0.35f;
		roomSwappedThisFrame = true;
		RaiseRoomChanged(fromRoom, room, RoomChangeKind.LinkedDoor, Point2.Zero);
		PulseDitherGlitch();
		FinishRoomActivation(RoomChangeKind.LinkedDoor);
		return true;
	}

	private static void ReadLinkedDoorDestination(JsonObject resolved, out Point2 localPosition, out Point2 spawnOffset)
	{
		localPosition = ActorSpecs.ReadPoint(resolved, "Position") ?? Point2.Zero;
		spawnOffset = ActorSpecs.ReadPoint(resolved, "SpawnOffset") ?? LinkedDoor.DefaultSpawnOffset;
	}

	private void ResolvePlayerRoomTransition(Point2 direction)
	{
		if (room == null || direction == Point2.Zero || GetFirst<Player>() is not { } player)
			return;

		if (!OverlapsRoomCollision(player, Point2.Zero))
			return;

		var original = player.Position;
		var forward = Math.Abs(direction.X) > Math.Abs(direction.Y)
			? Point2.UnitX * Math.Sign(direction.X)
			: Point2.UnitY * Math.Sign(direction.Y);
		var perpendicular = forward.X != 0 ? Point2.UnitY : Point2.UnitX;
		var perpendicularVelocity = forward.X != 0 ? player.Velocity.Y : player.Velocity.X;

		const int maxForwardDistance = TileSize * 4;
		const int maxPerpendicularDistance = TileSize * 4;

		for (var distance = 0; distance <= maxPerpendicularDistance; distance++)
		{
			if (distance == 0)
			{
				if (TryPlacePlayerAfterTransition(player, original, forward, perpendicular, 0, maxForwardDistance))
					return;
				continue;
			}

			var firstSign = perpendicularVelocity > 0 ? -1 : 1;
			if (TryPlacePlayerAfterTransition(player, original, forward, perpendicular, firstSign * distance, maxForwardDistance))
				return;
			if (TryPlacePlayerAfterTransition(player, original, forward, perpendicular, -firstSign * distance, maxForwardDistance))
				return;
		}

		player.Position = original;
	}

	private bool TryPlacePlayerAfterTransition(Player player, Point2 original, Point2 forward, Point2 perpendicular, int perpendicularDistance, int maxForwardDistance)
	{
		for (var forwardDistance = 0; forwardDistance <= maxForwardDistance; forwardDistance++)
		{
			player.Position = original + perpendicular * perpendicularDistance + forward * forwardDistance;
			if (!OverlapsRoomCollision(player, Point2.Zero))
				return true;
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

	public float GetRoomCollisionOverlapRatio(in RectInt bounds, bool includeBreakableTiles = true)
	{
		if (room == null || bounds.Width <= 0 || bounds.Height <= 0)
			return 0;

		var solidPixels = 0;
		for (var y = bounds.Top; y < bounds.Bottom; y++)
			for (var x = bounds.Left; x < bounds.Right; x++)
			{
				if (RoomCollisionContains(new Vector2(x + 0.5f, y + 0.5f), includeBreakableTiles))
					solidPixels++;
			}

		return solidPixels / (float)(bounds.Width * bounds.Height);
	}

	private bool RoomCollisionContains(Vector2 point, bool includeBreakableTiles = true)
	{
		if (room == null)
			return false;

		foreach (var rect in room.CollisionRects)
		{
			if (point.X >= rect.Left && point.X < rect.Right && point.Y >= rect.Top && point.Y < rect.Bottom)
				return includeBreakableTiles || !IsBreakableRoomCollisionPoint(point);
		}

		foreach (var slope in room.SlopeTiles)
		{
			var tileBounds = slope.WorldBounds(room);
			if (point.X < tileBounds.Left || point.X >= tileBounds.Right || point.Y < tileBounds.Top || point.Y >= tileBounds.Bottom)
				continue;

			if (PointInPolygon(point, room.GetSlopePolygon(slope)))
				return includeBreakableTiles || !IsBreakableRoomCollisionPoint(point);
		}

		return false;
	}

	private bool IsBreakableRoomCollisionPoint(Vector2 point)
	{
		if (room == null)
			return false;

		var local = point - (Vector2)room.WorldBounds.TopLeft;
		var tile = new Point2(
			(int)MathF.Floor(local.X / TileSize),
			(int)MathF.Floor(local.Y / TileSize));

		if (tile.X < 0 || tile.Y < 0 || tile.X >= room.Size.X || tile.Y >= room.Size.Y)
			return false;

		return room.IsBreakable(tile) || Factory.Find(room.Tiles[tile.X, tile.Y]) is { Destructible: true };
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

		for (var y = top; y < bottom; y++)
			for (var x = left; x < right; x++)
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

		for (var y = top; y <= bottom; y++)
			for (var x = left; x <= right; x++)
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
			MarkRoomNormalCacheDirty(room);
			MarkRoomTileFieldCacheDirty(room);
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

		var fromRoom = room;
		var reloaded = Assets.GetRoom(room.Id)?.CloneRuntime() ?? room.CloneRuntime();
		foreach (var actor in Actors)
			Destroy(actor);
		room = reloaded;
		LoadRoom(room);
		if (GetFirst<Player>() == null && respawnPosition is { } position)
			Create<Player>(position);
		EnsureBudForPlayer(repositionExisting: true);
		RaiseRoomChanged(fromRoom, room, RoomChangeKind.Reload, Point2.Zero);
		FinishRoomActivation(RoomChangeKind.Reload);

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
		HiddenCoverGrid? hiddenCover = null;
		foreach (var actor in Actors)
		{
			if (actor is HiddenWallGrid hiddenWallGrid)
			{
				hiddenWalls = hiddenWallGrid;
				continue;
			}
			if (actor is HiddenCoverGrid hiddenCoverGrid)
			{
				hiddenCover = hiddenCoverGrid;
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

		bg.Resize(room.Size);
		fgVisual.Resize(room.Size);
		fg.Resize(room.Size);
		bgDual?.Clear();
		fgDual.Clear();
		hiddenWalls?.Resize(room.Size);

		for (var x = 0; x < room.Size.X; x++)
			for (var y = 0; y < room.Size.Y; y++)
			{
				var tile = new Point2(x, y);
				if (room.GetBackgroundEntry(tile) is { } backgroundEntry)
					backgroundEntry.Tile?.Invoke(tile, room, fg, fgVisual, bg, bgDual ?? fgDual);
				if (room.GetEntry(tile) is { } entry)
					entry.Tile?.Invoke(tile, room, fg, fgVisual, bg, fgDual);
			}

		if (hiddenWalls != null)
			PopulateHiddenWalls(room, hiddenWalls);
		hiddenCover?.Build(room);

		if (bgDual != null)
			BuildDualTiles(room, bgDual, Factory.TileLayer.Background);
		BuildDualTiles(room, fgDual, Factory.TileLayer.Foreground);
		MarkRoomNormalCacheDirty(room);
		MarkRoomTileFieldCacheDirty(room);
	}

	void BuildDualTiles(Room forRoom, DualGrid dual, Factory.TileLayer layer)
	{
		dual.Clear();
		var dualEntries = Factory.Entries
			.Where(it => it.Value.DualTilesetName != null && it.Value.Layer == layer)
			.ToArray();

		foreach (var (tileType, entry) in dualEntries)
		{
			if (entry.DualTilesetName == null || Assets.GetTileset(entry.DualTilesetName) is not { } dualTileset)
				continue;

			dual.Build(forRoom, tileType, dualTileset, layer);
		}
	}

	public void LoadRoom(Room forRoom)
	{
		forRoom.RebuildGeometry();

		var offset = forRoom.WorldBounds.TopLeft;

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

		var hiddenWalls = Create<HiddenWallGrid>(offset, it => it.Resize(forRoom.Size));
		var hiddenCover = Create<HiddenCoverGrid>(offset);

		// loop over room grid placing objects
		for (var x = 0; x < forRoom.Size.X; x++)
			for (var y = 0; y < forRoom.Size.Y; y++)
			{
				var tile = new Point2(x, y);
				if (forRoom.GetBackgroundEntry(tile) is { } backgroundEntry)
					backgroundEntry.Tile?.Invoke(tile, forRoom, fg, fgVisual, bg, bgDual);
				if (forRoom.GetEntry(tile) is { } entry)
					entry.Tile?.Invoke(tile, forRoom, fg, fgVisual, bg, fgDual);
			}

		PopulateHiddenWalls(forRoom, hiddenWalls);
		BuildDualTiles(forRoom, bgDual, Factory.TileLayer.Background);
		BuildDualTiles(forRoom, fgDual, Factory.TileLayer.Foreground);

		foreach (var actor in forRoom.Actors)
		{
			if (GetFirst<Player>() != null && string.Equals(ActorSpecs.GetTypeName(actor), "Player", StringComparison.OrdinalIgnoreCase))
				continue;

			var localPosition = ActorSpecs.ReadPoint(actor, "Position") ?? Point2.Zero;
			var sessionDestroyKey = SessionDestroyKey(forRoom, actor, localPosition);
			if (sessionDestroyedActors.Contains(sessionDestroyKey))
				continue;

			if (ActorSpecs.Create(this, actor, offset + localPosition) is { } created)
				created.SessionDestroyKey = sessionDestroyKey;
		}

		hiddenCover.Build(forRoom);
		hiddenCover.RevealForCurrentPlayer(immediate: true);
		EnsureBudForPlayer();

		SpawnFungusLayer(forRoom);
	}

	/// <summary>
	/// Scans the room's background tiles for Factory.FungusTile and, if any
	/// are painted, spawns a single FungalGrowthActor covering their bounding
	/// box - growth is further constrained to the painted tiles via its mask.
	/// </summary>
	private void SpawnFungusLayer(Room forRoom)
	{
		var minX = int.MaxValue;
		var minY = int.MaxValue;
		var maxX = int.MinValue;
		var maxY = int.MinValue;

		for (var x = 0; x < forRoom.Size.X; x++)
			for (var y = 0; y < forRoom.Size.Y; y++)
			{
				if (forRoom.BackgroundTiles[x, y] != Factory.FungusTile)
					continue;

				minX = Math.Min(minX, x);
				maxX = Math.Max(maxX, x);
				minY = Math.Min(minY, y);
				maxY = Math.Max(maxY, y);
			}

		if (maxX < minX || maxY < minY)
			return;

		var tileWidth = maxX - minX + 1;
		var tileHeight = maxY - minY + 1;
		var width = tileWidth * TileSize;
		var height = tileHeight * TileSize;

		var mask = new float[width * height];
		for (var ty = 0; ty < tileHeight; ty++)
			for (var tx = 0; tx < tileWidth; tx++)
			{
				var growable = forRoom.BackgroundTiles[minX + tx, minY + ty] == Factory.FungusTile ? 1.0f : 0.0f;
				for (var py = 0; py < TileSize; py++)
					for (var px = 0; px < TileSize; px++)
						mask[(ty * TileSize + py) * width + (tx * TileSize + px)] = growable;
			}

		var position = forRoom.WorldBounds.TopLeft + new Point2(minX * TileSize, minY * TileSize);

		Create<FungalGrowthActor>(position, layer =>
		{
			layer.RandomSeed = GetStringHash(room.Id);
			layer.RegionWidth = width;
			layer.RegionHeight = height;
			layer.GrowthMask = mask;
		});
	}

	static UInt64 GetStringHash(string text)
	{
		string s1 = text.Substring(0, text.Length / 2);
		string s2 = text.Substring(text.Length / 2);

		var MS4B = BitConverter.GetBytes(s1.GetHashCode());
		var LS4B = BitConverter.GetBytes(s2.GetHashCode());
		UInt64 hash = (UInt64)MS4B[0] << 56 | (UInt64)MS4B[1] << 48 |
		              (UInt64)MS4B[2] << 40 | (UInt64)MS4B[3] << 32 |
		              (UInt64)LS4B[0] << 24 | (UInt64)LS4B[1] << 16 |
		              (UInt64)LS4B[2] << 8  | (UInt64)LS4B[3] ;
		return hash;
	}

	private void EnsureBudForPlayer(bool repositionExisting = false)
	{
		if (!spawnBud)
			return;

		if (GetFirst<Player>() is not { } player)
			return;

		var position = FindBudSpawnPosition(player);
		if (GetFirst<Bud>() is { } bud)
		{
			if (repositionExisting)
			{
				bud.Position = position;
				bud.Velocity = Vector2.Zero;
				bud.Facing = player.Facing;
			}
			return;
		}

		Create<Bud>(position, bud =>
		{
			bud.Facing = player.Facing;
			bud.KeepOnRoomTransition = true;
		});
	}

	private Point2 FindBudSpawnPosition(Player player)
	{
		var offsets = new[] {
			new Point2(-12 * player.Facing, 0),
			new Point2(12 * player.Facing, 0),
			new Point2(-20 * player.Facing, 0),
			new Point2(20 * player.Facing, 0),
			new Point2(0, -8),
			Point2.Zero
		};

		foreach (var offset in offsets)
		{
			var position = player.Position + offset;
			var bounds = new RectInt(-4, -12, 8, 12) + position;
			if (!OverlapsRoomCollision(bounds))
				return position;
		}

		return player.Position;
	}

	private void RaiseRoomChanged(Room? from, Room to, RoomChangeKind kind, Point2 direction)
	{
		var playerPosition = GetFirst<Player>()?.Position ?? (Vector2)to.WorldBounds.Center;
		var change = new RoomChange(from, to, kind, direction, playerPosition);
		RoomChanged?.Invoke(change);
		OnStoryRoomChanged(change);
	}

	private static string SessionDestroyKey(Room room, JsonObject actor, Point2 localPosition)
	{
		var id = ActorSpecs.ReadString(actor, "Id");
		if (!string.IsNullOrWhiteSpace(id))
			return $"{room.Id}:id:{id}";

		var source = ActorSpecs.ReadString(actor, "From") ?? ActorSpecs.ReadString(actor, "Type") ?? "Actor";
		return $"{room.Id}:{source}:{localPosition.X},{localPosition.Y}";
	}

	private static void PopulateHiddenWalls(Room room, HiddenWallGrid hiddenWalls)
	{
		foreach (var actor in room.Actors)
		{
			var resolved = ActorSpecs.Resolve(actor);
			if (ActorSpecs.ReadString(resolved, "Type") != "HiddenWall")
				continue;

			var localPosition = ActorSpecs.ReadPoint(resolved, "Position") ?? Point2.Zero;
			var size = ActorSpecs.ReadPoint(resolved, "Size") ?? HiddenWall.DefaultSize;
			var tileType = ActorSpecs.ReadString(resolved, "TileType") ?? "castle";
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
