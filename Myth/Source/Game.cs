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
	BurrowDoor,
	Reload
}

// Where death puts the player back. SavePoint is the real game; NearWhereYouDied keeps a
// timed demo moving: the start of the room you died in, then the last safe ground, then the
// save point.
public enum DeathRespawnMode
{
	SavePoint,
	NearWhereYouDied
}

public readonly record struct RoomChange(Room? From, Room To, RoomChangeKind Kind, Point2 Direction, Vector2 PlayerPosition);
public readonly record struct RoomGeoChange(Room Room, RectInt? DirtyWorldBounds = null);

interface IRoomInversionListener
{
	void RoomInverted();
}

public partial class Game : IDisposable
{


	static string? pendingHotReloadMessage;

	internal static void NotifyHotReload(string message)
		=> pendingHotReloadMessage = message;
	public const int Width = 320;
	public const int Height = 180;
	public const int Columns = Width / TileSize;
	public const int Rows = Height / TileSize + 1;
	public const int TileSize = 8;

	const float CameraSlackEpsilon = TileSize;

	internal readonly Manager Manager;
	public GameView? View { get; }
	internal bool Editing => Manager.game == this && !Manager.playing;

	// Rendering facade: external callers (Manager, actors) reach the view through Game.
	// Forwards to View, or no-ops/defaults when headless (View == null).
	public void Render(in RectInt viewport, bool showHud = true, RectInt? scissor = null, bool applyCrt = true) => View?.Render(viewport, showHud, scissor, applyCrt);
	public void RenderWorldView(in RectInt viewport, Rect worldView, bool showHud = false, RectInt? scissor = null, bool applyCrt = true) => View?.RenderWorldView(viewport, worldView, showHud, scissor, applyCrt);
	public void RenderWorldToTarget(Target target, Rect worldView, bool showHud = false, float zoom = 1.0f) => View?.RenderWorldToTarget(target, worldView, showHud, zoom);
	public void RenderSdfAura(Batcher batcher, Span<float> uniforms, Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3) => View?.RenderSdfAura(batcher, uniforms, p0, p1, p2, p3);
	public void PreviewParticlesInEditor(float dt, string? selectedActorId) => View?.PreviewParticlesInEditor(dt, selectedActorId);
	public void SetPreviewEffect(string name) => View?.SetPreviewEffect(name);
	public void BurstTestEffect() => View?.BurstTestEffect();
	public ParticleSystem? Particles => View?.Particles;
	public IReadOnlyList<Target> DebugTargets => View?.DebugTargets ?? [];
	public float SdfAuraScrollTime => View?.SdfAuraScrollTime ?? 0f;
	public Vector2 ViewCenterWorld => View?.ViewCenterWorld ?? default;
	public Target Screen => View!.Screen;
	public void FlashAir(Color color, float hold = 0.05f, float fade = 0.22f)
	{
		airFlashColor = color;
		airFlashStartedAt = Time.SecondsF;
		airFlashHold = Math.Max(0, hold);
		airFlashFade = Math.Max(0.001f, fade);
	}
	public bool ShowForegroundLayer
	{
		get => View?.ShowForegroundLayer ?? true;
		set { if (View != null) View.ShowForegroundLayer = value; }
	}
	public bool ShowBackgroundLayer
	{
		get => View?.ShowBackgroundLayer ?? true;
		set { if (View != null) View.ShowBackgroundLayer = value; }
	}
#if DEBUG
	public void ReloadShaders() => View?.ReloadShaders();
#endif
	// Set each frame by Manager via Update(controls); the sim reads only this, never the device.
	public Controls Controls { get; private set; } = null!;

	public readonly List<Actor> Actors = [];
	readonly List<Actor> attachedActors = [];
	readonly List<Actor> parkedActors = [];
	int nextActorCreationOrder;

	// The sim reads time only through this. Normally it's the wall clock scaled by TimeScale (the
	// product of all live time factors -- see Game.TimeScale.cs); during replay playback it's a
	// clock advanced by the recording's per-frame dt, which decouples the sim from the display
	// framerate. See AdvanceReplayClock / EndReplayClock.
	public Time Time => replayClock ?? Manager.Time.MultiplyDelta(TimeScale);
	Time? replayClock;
	internal void AdvanceReplayClock(float dt) => replayClock = (replayClock ?? default).Advance(TimeSpan.FromSeconds(dt));
	internal void EndReplayClock() => replayClock = null;

	// Runs an offline sim (see ReachMoves) whose steps call AdvanceReplayClock, then hands the clock back.
	internal void WithSimClock(Action run)
	{
		var saved = replayClock;
		try { run(); }
		finally { replayClock = saved; }
	}

	internal Room? room;
	internal Room? nextRoom;
	Room? loadingRoom;
	// Transition animation params: written by sim during room transitions, read by
	// GameView's lerping (Current*Color/Intensity). Exposed internal + forwarded on View.
	internal float nextRoomEase;
	Point2 transitionDirection;
	Vector2 transitionCameraFrom;
	Vector2 transitionCameraTo;
	float transitionZoomFrom;
	float transitionZoomTo;
	ulong? transitionPreparedRenderFrame;
	float linkedDoorCooldown;
	float layerTransitionTime;
	static float LayerTransitionDuration => 0.25f;
	internal bool LayerTransitionActive => layerTransitionTime > 0;
	internal float LayerTransitionProgress => 1 - layerTransitionTime / LayerTransitionDuration;
	bool roomSwappedThisFrame;
	internal readonly List<Actor> destroying = [];

	// Playthrough-durable state. Manager owns saving it; Game owns mutating it while playing.
	public SaveState Save { get; private set; } = new();
	internal int DucksDestroyed { get; private set; }
	internal Vector2 DuckHudWorldOrigin { get; private set; }
	public int PickupCount(PickupKind kind) => Save.Pickups.TryGetValue(kind, out var count) ? count : 0;
	public void AddPickup(PickupKind kind, int amount = 1)
	{
		Save.Pickups[kind] = Math.Max(0, PickupCount(kind) + amount);
		MarkSaveDirty();
	}

	// Playthrough-durable like Save.Pickups: exploration tracking plus the pause-map overlay.
	public readonly Minimap Minimap = new();
	public bool MinimapOpen => Minimap.Open;
	internal bool CapturesMapButton => Minimap.Open || mapRevealBlocking;

	public bool GymBudDied;
	public bool GymMode;
	public Actor? GymTarget;
	static float DitherGlitchAttackSpeed => 15.0f;
	static float DitherGlitchDecaySpeed => 1.35f;
	// Visual feedback state, but sim-owned: decayed each frame in Update and set by the
	// HitStun/Shake/PulseDitherGlitch APIs; GameView only reads it (via forwarding).
	internal float hitStun;

	internal struct FX {
		internal float shaking;
		internal float blooming;

		internal Point2 shake;

		internal void Bloom() {
			blooming = 2.3f;
		}

		internal void Update(Rng rng, Time time)
		{
			// screen shaking
			if (shaking > 0)
			{
				shaking -= time.Delta;
				if (time.OnInterval(0.05f))
					shake = new(rng.Sign(), rng.Sign());
			}
			else
				shake = Point2.Zero;

			// screen blooming
			blooming = Calc.Approach(blooming, 0, time.Delta * 20f);
		}

	}
	internal FX fx;

	internal float ditherGlitch;
	internal float ditherGlitchTarget;
	internal Vector2 ditherGlitchAxis = Vector2.UnitX;
	internal Vector2? ditherGlitchPosition;
	internal Color airFlashColor;
	internal float airFlashStartedAt = float.NegativeInfinity;
	internal float airFlashHold;
	internal float airFlashFade;
	internal float AirFlashAmount
	{
		get
		{
			var age = Time.SecondsF - airFlashStartedAt;
			return age < 0 || age >= airFlashHold + airFlashFade ? 0 :
				age <= airFlashHold ? 1 : Ease.Cube.Out(1 - (age - airFlashHold) / airFlashFade);
		}
	}
	internal Vector2 renderTargetShake;
	Vector2 renderTargetNoise;
	Vector2 renderTargetKick;
	Vector2 renderTargetKickVelocity;
	internal bool ShakeInRenderTargetSpace = true;
	float renderTargetShaking;
	float renderTargetShakeIntensity;
	float renderTargetShakeStepTimer;
	// The one deterministic RNG stream the sim draws from (actors + camera shake). Seed it at the
	// start of a recording/replay clip and the whole run reproduces. Editor/authoring code uses its
	// own Random.Shared and stays out of this stream.
	internal Rng rng = new();
	internal ulong RngSeed { get => rng.Seed; set => rng = new Rng(value); }
#if DEBUG
	internal readonly ReplayRecorder Recorder = null!;
#endif
	internal Cutscene? ActiveCutscene;
	internal struct BurrowUpgradeHudState
	{
		public float Emphasis;
		public float Particles;
		public float LabelTravel;
		public Point2 LabelWorldOrigin;
		public bool Active;
		public bool Growing;
	}
	internal BurrowUpgradeHudState BurrowUpgradeHud;


	internal bool draggingPlayer;
	internal bool DraggingPlayer => draggingPlayer;
	// True when the sim is in a room and not mid-transition: when Manager applies live
	// input such as mouse-dragging the player (matches the old in-Update gating).
	internal bool AcceptsLiveInput => room != null && nextRoom == null && !SavePointWarpActive && !LayerTransitionActive;
	readonly Point2? respawnPosition;
	// Where the player entered the *current* room. Unlike respawnPosition (readonly, the
	// initial room's spawn), this is refreshed on every room activation — including edge
	// transitions, which reuse this Game instance — so it always points at the way back in.
	Point2 roomEntryPosition;
	// roomEntryPosition as of the last genuine entry into the room. Death respawns reload the
	// room and would otherwise overwrite it with the spot they just placed the player, walking
	// the respawn deeper into the room with every death.
	RespawnState? roomRespawnPoint;
	// First safe footing found since entering the room. Save.LastSafeGrounded trails the player, so
	// it would put a pit death right back on the lip of the pit; this is where the room started
	// being survivable, and it's what a mid-air arrival falls back to.
	RespawnState? firstGroundedInRoom;
	Point2 roomEntryDirection;
	readonly bool spawnBud;
	readonly bool loadGoals;
	// In the gym, Bud is the character under test, so it spawns exactly at the player
	// start (the PlayerStart marker) rather than at an offset follow position.
	readonly bool spawnBudAtPlayer;
	bool disposed;
	const float BrowserDebugStatsInterval = 0.5f;
	int pendingRoomTransitionGcFrames;
	RoomChangeKind pendingRoomTransitionGcKind;

#if BROWSER
	float browserDebugStatsElapsed;
	int browserDebugStatsFrames;
	long browserDebugStatsAllocatedBytes;
	long browserDebugStatsMaxFrameBytes;
	int browserDebugStatsLastGen0 = GC.CollectionCount(0);
	int browserDebugStatsLastGen1 = GC.CollectionCount(1);
	int browserDebugStatsLastGen2 = GC.CollectionCount(2);
	int debugFramesSinceRoomTransition = -1;
#endif

	double browserDebugLastGcMs = -1;
	long browserDebugLastGcBeforeBytes;
	long browserDebugLastGcAfterBytes;
	RoomChangeKind browserDebugLastGcKind;

	public Vector2 Camera { get; internal set; }
	internal Vector2 RenderCamera => (Point2)Camera - fx.shake;
	// World rect the current render target covers, set by GameView each pass. Room-sized
	// actors (DualGrid, Grid) cull their tiles against it -- without that, render cost
	// scales with room area instead of screen area. Starts unbounded so any path that
	// doesn't set it draws everything.
	internal Rect VisibleWorldBounds = new(float.MinValue / 4, float.MinValue / 4, float.MaxValue / 2, float.MaxValue / 2);
	// The camera hard-follows the player, except a discontinuity in the clamp target (a blocker
	// cap toggling as you cross a bite's span, or a blocker revealing) is absorbed into a
	// decaying error so the view glides over the jump instead of snapping.
	Vector2 cameraError;
	Vector2? previousCameraTarget;
	Vector2 previousCameraFocus;
	Vector2 cameraLookAhead;
	static float CameraEaseRate => 7f;
	static float CameraLookAheadEdgeInset => 0.15f;
	static float CameraLookAheadEaseRate => 8f;
	static float BurrowCameraLeadDistance => 40f;
	static float BurrowCameraLeadEaseRate => 1.5f;
	static float BurrowCameraLeadReturnRate => 6f;
	float burrowCameraLead;
	static float PlayerDragCameraEaseRate => 2.5539999f.T();
	const float CameraEaseTriggerEpsilon = 2f;
	// The world is drawn at this scale into the fixed Width x Height frame, so the camera frames
	// fewer world pixels as it grows. Rooms set it (Room.Zoom) and the Zoom control floors it at 2.
	internal float zoomLevel = 1.0f;
	public float ZoomLevel => zoomLevel;
	internal float ZoomedWidth => ZoomedSizeAt(zoomLevel).X;
	internal float ZoomedHeight => ZoomedSizeAt(zoomLevel).Y;
	internal Vector2 ZoomedSize => ZoomedSizeAt(zoomLevel);
	// The world the camera actually frames. In 4:3 crop mode that is narrower than the frame,
	// so the player stays inside the visible region rather than the partly-cropped whole.
	Vector2 ZoomedSizeAt(float zoom) => new((AspectCropActive ? VisibleCropWidth : Width) / zoom, Height / zoom);
	// World-space offset from the frame's top-left (the Camera) to that visible region's: zero
	// unless the 4:3 crop is hiding a slice of each side.
	Vector2 CropPadAt(float zoom) => new((Width - (AspectCropActive ? VisibleCropWidth : Width)) / (2 * zoom), 0);
	static float RoomZoom(Room? forRoom) => MathF.Max(forRoom?.Zoom ?? 1.0f, 1.0f);
	/// World rect the camera frames -- what the player actually sees, zoom and 4:3 crop included.
	public Rect WorldView => new(Camera + CropPadAt(zoomLevel), ZoomedSize);

	// The game renders at a fixed 16:9 (Width:Height). When presenting into a narrower 4:3
	// region (desktop --aspect-four-three, or a portrait mobile-browser canvas) we crop the
	// sides ("cover" fit) to reclaim vertical space. aspectUncrop in [0,1] lerps the present
	// scale from cover (0, cropped) to contain (1, full 16:9 letterboxed) while Zoom is held.
	const float OutputAspect = (float)Width / Height;
	float aspectUncrop = 0f;
	public float AspectUncrop => aspectUncrop;
	// True when the live presentation is narrower than the native 16:9 frame, so cropping applies.
	public bool AspectCropActive
	{
		get
		{
#if BROWSER
			if (Manager.Window is { } w && w.HeightInPixels > 0)
				return (float)w.WidthInPixels / w.HeightInPixels < OutputAspect - 0.01f;
#endif
			return false;
		}
	}

	// Aspect of the region the game is presented into (4:3 on desktop --aspect-four-three, the
	// live canvas aspect on web). Drives both the side-crop and the camera's visible width.
	float PresentAspect
	{
		get
		{
#if BROWSER
			if (Manager.Window is { } w && w.HeightInPixels > 0)
				return (float)w.WidthInPixels / w.HeightInPixels;
#endif
			return OutputAspect;
		}
	}

	// Horizontal slice of the 16:9 frame actually visible after the side-crop, in frame pixels.
	// Full crop (aspectUncrop 0) -> the 4:3 center; held (aspectUncrop 1) -> the full width.
	// Mirrors the composite's cover->contain scale so the camera frames exactly what's drawn.
	float VisibleCropWidth
	{
		get
		{
			var r = MathF.Min(PresentAspect / OutputAspect, 1f);
			return Width * r / (1 + (r - 1) * aspectUncrop);
		}
	}

	// The window region the game is presented into. On desktop --aspect-four-three the window
	// stays wide, so carve a centered 4:3 rect (pillarboxed). Otherwise (including a portrait
	// mobile-browser canvas that is already ~4:3) present into the whole viewport. Read by
	// GameView's final composite as well, so it is internal rather than private.
	internal RectInt PresentRect(in RectInt viewport)
	{
		/*
		if (Manager.AspectFourThree)
		{
			var s = Calc.Min(viewport.Width / 4f, viewport.Height / 3f);
			var w = (int)(4 * s);
			var h = (int)(3 * s);
			var c = viewport.Center;
			return new RectInt(c.X - w / 2, c.Y - h / 2, w, h);
		}
		*/
		return viewport;
	}

	// Total pixel scale the composite uses to draw the screen into the given viewport.
	internal float PresentScale(in RectInt present, float outputWidth, float outputHeight)
	{
		var contain = Manager.Video.FitScale(present.Size, new(outputWidth, outputHeight));
		if (!AspectCropActive)
			return contain;
		var cover = Calc.Max(present.Width / outputWidth, present.Height / outputHeight);
		return cover + (contain - cover) * aspectUncrop;
	}

	// Inverse of the final composite: map a window-space point to a world position, honoring the
	// present rect, world zoom, and 4:3 cover-crop so touch/mouse picking lines up with what's drawn.
	public Vector2 WindowToWorld(Vector2 windowPoint, in RectInt viewport)
	{
		var present = PresentRect(viewport);
		var scale = PresentScale(present, Screen.Width, Screen.Height);
		var screenPixel = (windowPoint - (Vector2)present.Center) / scale + new Vector2(Screen.Width, Screen.Height) / 2;
		return Camera + screenPixel / zoomLevel;
	}

	public RectInt Bounds => room?.WorldBounds ?? new();
	// The point the player entered the current room from — the respawn spot after a
	// non-fatal fall off the bottom of the room. See roomEntryPosition.
	public Point2 RoomEntryPosition => roomEntryPosition;
	public Point2 Cell => room?.Cell ?? Point2.Zero;
	public Room? CurrentRoom => room;
	public bool IsEditorPreview => !AudioEventsEnabled;
	internal bool Disposed => disposed;
	public bool AudioMuted { get; set; }
	public bool Simulating { get; set; }
	public event Action<RoomChange>? RoomChanged;
	public event Action<RoomGeoChange>? RoomGeoChanged;
	// Raised when the editor (re)builds or switches this preview game's room, so preview-only
	// actors can settle themselves to a representative state. Never fires during play.
	public event Action? EditorPreviewSync;
	internal void RaiseEditorPreviewSync() => EditorPreviewSync?.Invoke();

	public SimulationScope BeginSimulation()
		=> new(this);

	internal void UseSaveState(SaveState save)
	{
		Save = save;
		DucksDestroyed = CountPersistentlyDestroyedProps("duck");
		Minimap.ApplyState(Save.Map);
		Minimap.ApplyRevealedDoors(Save.RevealedDoors);
		Minimap.Changed = () => MarkSaveDirty();
	}

	void MarkSaveDirty(bool autosave = true, bool captureMap = true)
	{
		if (captureMap)
			Save.Map = Minimap.CaptureState();
		if (autosave)
			Manager.MarkSaveDirty();
		else
			Manager.MarkSaveDirtyWithoutAutosave();
	}

	public bool IsRevealDoorRevealed(string doorId) => Save.RevealedDoors.Contains(doorId);
	public bool IsButtonPressed(string? buttonId) => buttonId != null && Save.PressedButtons.Contains(buttonId);

	public void PressButtonPersistently(string? buttonId)
	{
		if (!string.IsNullOrWhiteSpace(buttonId) && Save.PressedButtons.Add(buttonId))
			MarkSaveDirty();
	}

	public void RevealDoor(string doorId)
	{
		if (string.IsNullOrWhiteSpace(doorId))
			return;
		var changed = Save.RevealedDoors.Add(doorId);
		Minimap.NoteDoorRevealed(doorId);
		if (changed)
			MarkSaveDirty();
	}

	public readonly struct SimulationScope : IDisposable
	{
		readonly Game game;
		readonly bool previous;

		internal SimulationScope(Game game)
		{
			this.game = game;
			previous = game.Simulating;
			game.Simulating = true;
		}

		public void Dispose() => game.Simulating = previous;
	}

	public Game(Manager manager, Point2 start, bool audioEventsEnabled = true)
		: this(manager, Assets.GetRoom(start), audioEventsEnabled)
	{
	}

	public Game(Manager manager, Room? start, bool audioEventsEnabled = true, Point2? fallbackPlayerDirection = null, bool spawnFallbackPlayer = false, Point2? initialPlayerPosition = null, bool spawnBud = true, bool enableParticles = true, bool spawnBudAtPlayer = false, bool loadGoals = false, (SoundInstance? Music, string? Area)? carryMusic = null, SaveState? save = null)
	{
		rewinder = new(this);
		Manager = manager;
#if DEBUG
		Recorder = new ReplayRecorder(this);
		RoomChanged += Recorder.OnRoomChanged;
#endif
		UseSaveState(save ?? new());
		AudioEventsEnabled = audioEventsEnabled && Manager.AudioEnabled;
		// A previous Game (the prior gym run) may hand off its still-playing
		// background music so it continues seamlessly across the dispose/recreate.
		// Adopting it before StartMusic() means StartMusic no-ops (music != null)
		// and ApplyMusicArea just retargets the live event to this room's area.
		if (carryMusic is { Music: { } carried })
			AdoptMusic(carried, carryMusic.Value.Area);
		this.spawnBud = spawnBud;
		this.spawnBudAtPlayer = spawnBudAtPlayer;
		this.loadGoals = loadGoals;
		// All GPU/render state lives in GameView. A headless Game (the gym, or a
		// nav-graph worker) has no view and allocates no render targets/materials.
		View = manager.GraphicsDevice.Driver == GraphicsDriver.Headless
			? null
			: new GameView(this, enableParticles);
		LoadStory();
		RestorePersistentDebugViews();

		if (start != null)
		{
			room = start.CloneRuntime();
			zoomLevel = RoomZoom(room);
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
			else if (spawnFallbackPlayer && GetFirst<PlayerStart>() == null)
			{
				respawnPosition = FindFallbackPlayerPosition(room, fallbackPlayerDirection);
				Create<Player>(respawnPosition);
			}
			else if (GetFirst<PlayerStart>() is { } playerStart)
			{
				if (GetFirst<Player>() is { } player)
				{
					player.Position = playerStart.Position;
					respawnPosition = player.Position;
				}
				else
				{
					respawnPosition = playerStart.Position;
					Create<Player>(respawnPosition);
				}
			}
			EnsureBudForPlayer(repositionExisting: true, atPlayerPosition: spawnBudAtPlayer);
			if (GetFirst<Player>() is { } initialPlayer)
			{
				roomEntryPosition = initialPlayer.Position;
				roomRespawnPoint = new RespawnState(room.Id, roomEntryPosition);
				firstGroundedInRoom = null;
			}
			RaiseRoomChanged(null, room, RoomChangeKind.InitialLoad, Point2.Zero);
			if (AudioEventsEnabled)
			{
				StartMusic();
				ApplyMusicArea(room);
				ApplyRoomReverb(room);
				ApplyRoomMusicMuffle(room);
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
			if (actor.AddedOnce && stopped.Add(actor))
				actor.Destroyed();
		foreach (var actor in parkedActors)
			if (actor.AddedOnce && stopped.Add(actor))
				actor.Destroyed();

		Actors.Clear();
		destroying.Clear();
		parkedActors.Clear();
		View?.Dispose();
	}

	// Manager owns input and feeds the per-frame controls in; the sim never reads the
	// live device. A headless Game receives Controls bound to a headless Input (no input).
	public void Update(Controls controls)
	{
		UpdateTimeFactors();

		Controls = controls;
#if BROWSER
		var browserDebugFrameStartAllocated = GC.GetAllocatedBytesForCurrentThread();
#endif

		GymBudDied = false;
		roomSwappedThisFrame = false;

		for (var i = 0; i < CollisionRectsThisFrame.Count; i++) {
			var (rect, time) = CollisionRectsThisFrame[i];
			if (Time.SecondsF - time > 0.1f) {
				CollisionRectsThisFrame.RemoveAt(i--);
			}
		}

		UpdatePlayerHitSnapshot();
		UpdateRoomTransitionGc();
		UpdateCrumbleTiles();
		if (linkedDoorCooldown > 0)
			linkedDoorCooldown = Calc.Approach(linkedDoorCooldown, 0, Time.Delta);
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

		if (layerTransitionTime > 0)
		{
			layerTransitionTime = Calc.Approach(layerTransitionTime, 0, Time.Delta);
			UpdateAudioListener();
#if BROWSER
			UpdateBrowserDebugStats(browserDebugFrameStartAllocated);
#endif
			return;
		}

		// The map only shows what the camera has actually framed, so credit the visible span
		// every normal-play frame (skipping the mid-transition camera lerp).
		if (nextRoom == null && !Simulating)
			Minimap.NoteCameraView(room, VisibleCameraTileBounds());

		if (UpdateMapReveal())
		{
			UpdateAudioListener();
#if BROWSER
			UpdateBrowserDebugStats(browserDebugFrameStartAllocated);
#endif
			return;
		}

		if (Minimap.Open && Minimap.WarpMode)
		{
			UpdateWarpModeInput();
		}
		else
		{
			// Escape / gamepad Start / gamepad Back (Select) toggles the map overlay; the
			// sim pauses underneath while it's up. Back is checked directly rather than
			// bound to Controls.Map so it doesn't also open the pause menu.
			if (ActiveCutscene == null && nextRoom == null && (Controls.Map.ConsumePress() || Manager.Input.AnyControllerButtonPressed(Buttons.Back)))
			{
				Minimap.Open = !Minimap.Open;
				Minimap.WarpMode = false;
				PlayAudioEvent(Minimap.Open ? "map/open" : "map/close");
				if (Minimap.Open)
				{
					Minimap.ResetView();
					Minimap.Rebuild(room, this);
				}
			}

			// While the map is up, any face button (N/S/E/W) also dismisses it.
			if (Minimap.Open)
			{
				var input = Manager.Input;
				if (input.AnyControllerButtonPressed(Buttons.North) ||
					input.AnyControllerButtonPressed(Buttons.South) ||
					input.AnyControllerButtonPressed(Buttons.East) ||
					input.AnyControllerButtonPressed(Buttons.West))
				{
					Minimap.Open = false;
					PlayAudioEvent("map/close");
				}
			}
		}

		if (Minimap.Open)
		{
			if (!Minimap.WarpMode)
				Minimap.UpdateView(Controls.Move.Value, Time.Delta);
			UpdateAudioListener();
#if BROWSER
			UpdateBrowserDebugStats(browserDebugFrameStartAllocated);
#endif
			return;
		}

		if (UpdateSavePointWarp())
		{
			if (!Simulating) UpdateAudioListener();
#if BROWSER
			UpdateBrowserDebugStats(browserDebugFrameStartAllocated);
#endif
			return;
		}

		// Run Game normally when not moving to a new room
		if (nextRoom == null)
		{
			// Debug shortcuts and editor mouse-drag are host concerns; Manager reads input
			// and drives them (see Manager.HandleGameDebugShortcuts / UpdatePlayerMouseDrag).

			// only run normal updates if no hitStun
			if (hitStun <= 0)
			{
				PreUpdateSystems();
				RemoveDestroyedActors();
				NeuralTextureActor.StepAll(this);
				for (var i = 0; i < Actors.Count; i++)
				{
					var actor = Actors[i];
					actor.Update();
					DestroyFallenEnemy(actor);
					if (roomSwappedThisFrame || nextRoom != null) // note: we stop .Update()ing actors once we're transitioning to a new room
						break;
				}
				PostUpdateSystems();
			}
			else
			{
				// Some actors ask to be updated during hitStun
				for (var i = 0; i < HitStunActors.Count; i++)
					HitStunActors[i].Update();

				hitStun -= Time.Delta;
			}
		}
		// Lerp the Camera to the new Room
		// Room setup and its first draw can compile cold GPU pipelines. Fixed-step catch-up may
		// otherwise advance this tween several times before that first frame is ever presented.
		else if (transitionPreparedRenderFrame is { } preparedFrame && Time.RenderFrame != preparedFrame)
		{
			if (nextRoomEase < 1.0f)
			{
				View?.UpdateParticles(Time.Delta);
				var ease = Ease.Cube.InOut(nextRoomEase);
				Camera = Vector2.Lerp(transitionCameraFrom, transitionCameraTo, ease);
				zoomLevel = float.Lerp(transitionZoomFrom, transitionZoomTo, ease);
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
		}

		UpdateRenderTargetShake();
		if (!Simulating) UpdateAudioListener();
#if BROWSER
		UpdateBrowserDebugStats(browserDebugFrameStartAllocated);
#endif
	}

	internal List<Actor> HitStunActors = [];

	void PreUpdateSystems()
	{
		ResolveActorAttachments();
		View?.UpdateParticles(Time.Delta);
		UpdateReachField();
	}

	void ResolveActorAttachments()
	{
		attachedActors.Clear();
		foreach (var actor in Actors)
			actor.AttachmentTarget = string.IsNullOrWhiteSpace(actor.AttachedToActorId)
				? null
				: GetActorById(actor.AttachedToActorId);

		foreach (var actor in Actors)
		{
			if (actor.AttachmentTarget == null || AttachmentCreatesCycle(actor))
			{
				actor.AttachmentTarget = null;
				continue;
			}
			attachedActors.Add(actor);
		}

		foreach (var actor in attachedActors)
		{
			var desired = actor.AttachmentTarget!.Position + actor.AttachmentOffset;
			if (desired != actor.Position)
				actor.ApplyAttachmentMovement(desired - actor.Position);
		}
	}

	bool AttachmentCreatesCycle(Actor actor)
	{
		var target = actor.AttachmentTarget;
		for (var i = 0; i < Actors.Count && target != null; i++, target = target.AttachmentTarget)
			if (target == actor)
				return true;
		return false;
	}

	internal void ActorMoved(Actor actor, Point2 delta)
	{
		foreach (var attached in attachedActors)
			if (attached.AttachmentTarget == actor)
				attached.ApplyAttachmentMovement(delta);
	}

	void PostUpdateSystems()
	{
		UpdateWaterSurfaces();
		UpdateCamera();
		UpdateStory();
		rewinder.UpdateDebugPlayerRewind();

		fx.Update(rng, Time);
	}

	static float RenderTargetShakeAmount => 15f;
	static float RenderTargetShakeFrequency => 88.9570007f.T(1, 120);
	static float RenderTargetKickSpring => 180f;
	static float RenderTargetKickDamping => 18f;
	static float DebugRenderTargetShakeSeconds => 0.226999998f.T(0, 2);

	void UpdateRenderTargetShake()
	{
		if (renderTargetShaking <= 0)
		{
			renderTargetNoise = Vector2.Zero;
			renderTargetShakeIntensity = 0;
			renderTargetShakeStepTimer = 0;
		}
		else
		{
			renderTargetShaking -= Time.Delta;
			renderTargetShakeStepTimer -= Time.Delta;
			if (renderTargetShakeStepTimer <= 0)
			{
				renderTargetShakeStepTimer += 1.0f / RenderTargetShakeFrequency;
				renderTargetNoise = rng.Vec2() * (RenderTargetShakeAmount * renderTargetShakeIntensity);
			}
		}

		Spring.Step(ref renderTargetKick, ref renderTargetKickVelocity, Vector2.Zero,
			RenderTargetKickSpring, RenderTargetKickDamping, MathF.Min(Time.Delta, 1 / 30f));
		renderTargetShake = renderTargetNoise + renderTargetKick;
	}


	// The world-tile span the camera currently frames, honoring the zoom / 4:3 crop.
	RectInt VisibleCameraTileBounds()
	{
		var view = new Rect(Camera + CropPadAt(zoomLevel), ZoomedSize);
		return RectInt.Between((view.TopLeft / TileSize).FloorToPoint2(), (view.BottomRight / TileSize).CeilingToPoint2());
	}

	internal void CutRopes(Vector2 worldPos) {
		foreach (var actor in Actors)
			if (actor is Rope rope)
					rope.Break(worldPos);
	}

	void UpdateCamera()
	{
		// Debug capture mode: keep the player dead-centre, ignoring room edges entirely.
		if (DebugCenterCamera && GetFirst<Player>() is { } centerPlayer)
		{
			Camera = Vector2.Round((Vector2)centerPlayer.Position - ZoomedSize / 2);
			previousCameraTarget = null;
			cameraError = Vector2.Zero;
			cameraLookAhead = Vector2.Zero;
			burrowCameraLead = 0;
			return;
		}

		if (room == null || nextRoom != null)
		{
			// The camera is under transition control; drop the easing state so normal
			// control resumes cleanly clamped to the new room rather than gliding from a stale
			// (old-room) target.
			previousCameraTarget = null;
			cameraError = Vector2.Zero;
			cameraLookAhead = Vector2.Zero;
			burrowCameraLead = 0;
			return;
		}

		if (DraggingPlayer && GetFirst<Player>() is { } draggedPlayer)
		{
			UpdateDraggedPlayerCamera(draggedPlayer);
			return;
		}

		// In 4:3 crop mode the Zoom control un-crops (reveals the cut-off sides) instead of the
		// usual 2x magnify, so the two never fight: drop its floor whenever cropping is active.
		// The room's own zoom still applies -- it is content, not presentation.
		var cropActive = AspectCropActive;
		var zoomTarget = MathF.Max(RoomZoom(room), !cropActive && Controls.Zoom.Down ? 2.0f : 1.0f);
		zoomLevel += (zoomTarget - zoomLevel) * (1 - MathF.Exp(-Time.Delta * 12f));
		if (MathF.Abs(zoomLevel - zoomTarget) < 0.005f)
			zoomLevel = zoomTarget;

		var uncropTarget = cropActive && Controls.Zoom.Down ? 1.0f : 0.0f;
		aspectUncrop += (uncropTarget - aspectUncrop) * (1 - MathF.Exp(-Time.Delta * 12f));
		if (MathF.Abs(aspectUncrop - uncropTarget) < 0.005f)
			aspectUncrop = uncropTarget;

		UpdatePlayerCameraTarget();

		var lookRange = ZoomedSize * (0.5f - CameraLookAheadEdgeInset);
		var lookTarget = ActiveCutscene == null ? Controls.Look.Value * lookRange : Vector2.Zero;
		cameraLookAhead = Vector2.Lerp(cameraLookAhead, lookTarget, 1 - MathF.Exp(-Time.Delta * CameraLookAheadEaseRate));
		if ((cameraLookAhead - lookTarget).LengthSquared() < 0.01f)
			cameraLookAhead = lookTarget;

		UpdateBurrowCameraLead();
		var cameraFocus = easedPlayerCameraTarget + CameraOffset;
		var target = GetGameplayCameraTarget();

		// A static clamp moves the target at most as far as the camera focus moved on each axis, so
		// any excess this frame is a discontinuity (a cap toggling / a reveal). Fold that excess
		// into the error and let it decay, gliding the view across the jump.
		if (previousCameraTarget is { } prev)
		{
			var targetDelta = target - prev;
			var focusDelta = cameraFocus - previousCameraFocus;
			if (MathF.Abs(targetDelta.X) - MathF.Abs(focusDelta.X) > CameraEaseTriggerEpsilon)
				cameraError.X -= targetDelta.X - focusDelta.X;
			if (MathF.Abs(targetDelta.Y) - MathF.Abs(focusDelta.Y) > CameraEaseTriggerEpsilon)
				cameraError.Y -= targetDelta.Y - focusDelta.Y;
		}

		cameraError *= MathF.Exp(-Time.Delta * CameraEaseRate);
		if (cameraError.LengthSquared() < 0.25f)
			cameraError = Vector2.Zero;

		// The room edge is the hard outer limit: re-clamp after adding the easing offset so the
		// camera can never show outside the room (easing overflow falls into the dead/blocker
		// space, never past a room edge).
		Camera = ClampCameraToRoom(target + cameraError);
		previousCameraTarget = target;
		previousCameraFocus = cameraFocus;
	}

	void UpdateDraggedPlayerCamera(Player player)
	{
		easedPlayerCameraTarget = player.Position;
		cameraError = Vector2.Zero;
		cameraLookAhead = Vector2.Zero;
		burrowCameraLead = 0;

		var target = GetGameplayCameraTarget();
		var amount = 1 - MathF.Exp(-Time.Delta * PlayerDragCameraEaseRate);
		Camera = ClampCameraToRoom(Vector2.Lerp(Camera, target, amount));
		previousCameraTarget = null;
		previousCameraFocus = easedPlayerCameraTarget;
	}

	// Deactivate any active camera blocker whose rect the just-revealed area overlaps, so a
	// RevealDoor opening also opens the camera over the secret it uncovered.
	public void RevealCameraBlockersOverlapping(RectInt worldArea)
	{
		foreach (var actor in Actors)
		{
			if (actor is CameraBlocker { Active: true } blocker && blocker.WorldRect.Overlaps(worldArea))
			{
				blocker.Deactivate();
			}
		}
	}

	readonly List<RectInt> cameraBlockerBuffer = [];
	List<RectInt> ActiveCameraBlockers()
	{
		cameraBlockerBuffer.Clear();
		foreach (var actor in Actors)
			if (actor is CameraBlocker { Active: true } blocker)
				cameraBlockerBuffer.Add(blocker.WorldRect);
		return cameraBlockerBuffer;
	}

	// A room's camera-blocker world rects, read straight from its actor specs. Used to aim a
	// transition's ease before the incoming room's live actor list is populated. Omit blockers the
	// predicted entry has already crossed, matching the deactivation their first Update will perform.
	static List<RectInt> CameraBlockerRects(Room forRoom, Vector2 entry, List<RectInt> into)
	{
		into.Clear();
		foreach (var actor in forRoom.Actors)
			if (ActorSpecs.GetTypeName(actor) == nameof(CameraBlocker))
			{
				var rect = CameraBlocker.WorldRectFromSpec(forRoom, actor);
				if (!rect.Contains(entry))
					into.Add(rect);
			}
		return into;
	}

	Point2 FindFallbackPlayerPosition(Room forRoom, Point2? direction)
	{
		var fromLeft = direction?.X > 0;
		var fromRight = direction?.X < 0;
		var fromTop = direction?.Y > 0;
		var fromBottom = direction?.Y < 0;
		// An authored save point beats the room's geometric center, which is usually midair.
		var best = FindSavePointPosition(forRoom) ?? forRoom.WorldBounds.Center;
		var bestScore = float.MaxValue;
		var targetX = fromLeft ? 0 : fromRight ? forRoom.Size.X - 1 : (forRoom.Size.X - 1) * 0.5f;
		var targetY = fromTop ? 0 : fromBottom ? forRoom.Size.Y - 1 : (forRoom.Size.Y - 1) * 0.5f;

		for (var y = 1; y < forRoom.Size.Y; y++)
			for (var x = 0; x < forRoom.Size.X; x++)
			{
				var dx = x - targetX;
				var dy = y - targetY;
				var score = dx * dx + dy * dy * 0.25f;
				if (score >= bestScore)
					continue;

				// The real standing footprint, not "a free tile over a solid one" -- otherwise a
				// 1x1 pocket in the geometry scores well and the player spawns wedged in it.
				var candidate = forRoom.WorldBounds.TopLeft + new Point2(x * TileSize + TileSize / 2, y * TileSize);
				if (!Player.HasSupportedFooting(forRoom, candidate))
					continue;

				bestScore = score;
				best = candidate;
			}

		return best;
	}

	static Point2? FindSavePointPosition(Room forRoom)
	{
		foreach (var actor in forRoom.Actors)
			if (ActorSpecs.GetTypeName(actor) == nameof(SavePoint) && ActorSpecs.ReadResolvedPosition(actor) is { } local)
				return forRoom.WorldBounds.TopLeft + local;
		return null;
	}

	// Low-passed so alternating flat/vertical segments (a staircase of corners) average into a
	// steady lead instead of snapping at every turn.
	Vector2 CameraOffset => cameraLookAhead + Vector2.UnitY * burrowCameraLead;

	void UpdateBurrowCameraLead()
	{
		var (target, rate) = GetFirst<Player>() is { State: Player.States.Burrow } player
			? (player.BurrowTravelHeading.Y * player.BurrowSpeed / Pawn.BurrowMaxSpeed * BurrowCameraLeadDistance, BurrowCameraLeadEaseRate)
			: (0, BurrowCameraLeadReturnRate);
		burrowCameraLead = Calc.Lerp(burrowCameraLead, target, 1 - MathF.Exp(-Time.Delta * rate));
	}

	Vector2 easedPlayerCameraTarget; // TODO: use nullable and clear on room switch or load

	void UpdatePlayerCameraTarget()
	{
		if (GetFirst<Player>() is { } player)
			easedPlayerCameraTarget = player.Position;
	}

	public Vector2 GetGameplayCameraTarget()
	{
		if (room == null)
			return Camera;

		var unclamped = easedPlayerCameraTarget + CameraOffset - ZoomedSize / 2;
		return ClampCameraToRoomAndBlockers(unclamped, zoomLevel, room.WorldBounds, easedPlayerCameraTarget, ActiveCameraBlockers());
	}

	// Clamp a zoomed camera top-left against a room (with its active blockers bitten out), then
	// convert back to a Camera top-left. Works for any room's bounds/blockers, not just the current
	// room, so a transition can aim its ease at the same place live control will hold the camera.
	Vector2 ClampCameraToRoomAndBlockers(Vector2 cameraTopLeft, float zoom, RectInt bounds, Vector2 player, List<RectInt> blockers)
	{
		if (blockers.Count == 0)
			return ClampCameraToRoom(cameraTopLeft, zoom, bounds);
		var padding = CropPadAt(zoom);
		return ClampViewToRoomAndBlockers(cameraTopLeft + padding, ZoomedSizeAt(zoom), player, blockers, bounds) - padding;
	}

	// Confine the zoomed view to the room with edge-anchored "bites" removed. A bite caps the
	// view on an axis only while the *player* is within the bite's span on the perpendicular
	// axis. That way a bite only ever hides its own interior -- which the player can't stand in
	// (entering deactivates it) -- so it can never trap the camera away from the player in a
	// narrow arm beside it, regardless of orientation or exact arm width.
	Vector2 ClampViewToRoomAndBlockers(Vector2 viewPos, Vector2 viewSize, Vector2 player, List<RectInt> blockers, RectInt bounds)
	{
		var minX = (float)bounds.Left;
		var maxX = bounds.Right - viewSize.X;
		var minY = (float)bounds.Top;
		var maxY = bounds.Bottom - viewSize.Y;

		foreach (var b in blockers)
		{
			// Player beside the bite (within its Y span) -> cap horizontally toward it.
			if (player.Y >= b.Top && player.Y < b.Bottom)
			{
				if (b.Left <= bounds.Left) minX = MathF.Max(minX, b.Right);
				if (b.Right >= bounds.Right) maxX = MathF.Min(maxX, b.Left - viewSize.X);
			}
			// Player above/below the bite (within its X span) -> cap vertically toward it.
			if (player.X >= b.Left && player.X < b.Right)
			{
				if (b.Top <= bounds.Top) minY = MathF.Max(minY, b.Bottom);
				if (b.Bottom >= bounds.Bottom) maxY = MathF.Min(maxY, b.Top - viewSize.Y);
			}
		}

		// An arm within a tile of the view centers on it and locks, the same slack ClampCameraAxis
		// gives a room -- so a blocker biting a room down to one screen holds as still as a
		// one-screen room does. A negative span (arm narrower than the view) falls out of the same test.
		viewPos.X = CenterOrClamp(viewPos.X, minX, maxX);
		viewPos.Y = CenterOrClamp(viewPos.Y, minY, maxY);
		return viewPos;
	}

	public void SnapCameraToGameplayTarget()
	{
		zoomLevel = RoomZoom(room);
		cameraError = Vector2.Zero;
		Camera = GetGameplayCameraTarget();
		previousCameraTarget = Camera;
		previousCameraFocus = easedPlayerCameraTarget + CameraOffset;
	}

	Vector2 ClampCameraToRoom(Vector2 target)
		=> room == null ? target : ClampCameraToRoom(target, zoomLevel, room.WorldBounds);

	// Clamps the visible view to the room and converts back to a Camera (frame) top-left. Takes
	// the zoom rather than reading it, so a transition can aim at the room it is easing into.
	Vector2 ClampCameraToRoom(Vector2 target, float zoom, RectInt bounds)
	{
		var view = ZoomedSizeAt(zoom);
		var padding = CropPadAt(zoom);
		return new Vector2(
			ClampCameraAxis(target.X + padding.X, bounds.Left, bounds.Right, view.X) - padding.X,
			ClampCameraAxis(target.Y + padding.Y, bounds.Top, bounds.Bottom, view.Y) - padding.Y);
	}

	static float ClampCameraAxis(float value, float min, float max, float viewportSize)
		=> CenterOrClamp(value, min, max - viewportSize);

	// Clamp a view edge between its limits, or center between them when the slack left is within
	// a tile -- the slop that lets a room built one tile taller than the screen still sit locked.
	static float CenterOrClamp(float value, float min, float max)
		=> max - min > CameraSlackEpsilon ? Math.Clamp(value, min, max) : (min + max) * 0.5f;

	void FinishRoomActivation(RoomChangeKind kind)
	{
		// The player has been placed into the (new) room by now, so this is the way back in.
		if (GetFirst<Player>() is { } player)
		{
			roomEntryPosition = player.Position;
			if (room is { } entered && (kind != RoomChangeKind.Reload || roomRespawnPoint?.RoomId != entered.Id))
			{
				roomRespawnPoint = new RespawnState(entered.Id, roomEntryPosition);
				firstGroundedInRoom = null;
			}
		}
		roomEntryDirection = kind == RoomChangeKind.EdgeTransition ? transitionDirection : Point2.Zero;
		RemoveDestroyedActors();
		for (var i = 0; i < Actors.Count; i++)
			Actors[i].Room = room;
		View?.PruneInactiveRoomCaches();
		RequestRoomTransitionGc(kind);
	}

	// A respawn marker only needs to step once the footing is meaningfully elsewhere; rewriting it
	// every pixel of a walk churns the save and re-runs the safety sweep for nothing.
	static int SafeGroundedStep => TileSize / 2;
	// Nothing lethal may sit this close to footing we are about to remember.
	static int SafeGroundedClearance => TileSize;

	// Remembers footing as the respawn point, if it is somewhere new and nothing can reach the
	// player there. Called every frame the player is settled on ground, so the cheap gates run first.
	public void NoteSafeGrounded(Point2 position, RectInt standing)
	{
		if (room == null || nextRoom != null || Simulating)
			return;
		deathsWithoutSafeGround = 0; // standing on anything at all is proof we are not falling in a loop
		if (Save.LastSafeGrounded is { RoomId: var lastRoom, Position: var lastPosition } &&
			lastRoom == room.Id && (lastPosition - position).LengthSquared() < SafeGroundedStep * SafeGroundedStep)
			return;
		if (!IsSafeFooting(standing))
			return;

		var respawn = new RespawnState(room.Id, position);
		firstGroundedInRoom ??= respawn;
		Save.LastSafeGrounded = respawn;
		MarkSaveDirty(autosave: false, captureMap: false);
	}

	// Footing is only worth remembering if nothing lethal sits within a tile of it and nothing can
	// reach it where it stands -- a crusher's lane, a lightning beam's path (see Actor.UnsafeFooting).
	bool IsSafeFooting(RectInt standing)
	{
		var near = standing.Inflate(SafeGroundedClearance);
		for (var i = 0; i < Actors.Count; i++)
		{
			var actor = Actors[i];
			if (destroying.Contains(actor))
				continue;
			if (actor.Mask.Has(Actor.Masks.Enemy | Actor.Masks.Hazard) && actor.OverlapsCombatBounds(near))
				return false;
			if (actor.UnsafeFooting(standing))
				return false;
		}
		return true;
	}

	public bool TryRespawnPlayerAtLastSafeGrounded(Player player)
	{
		if (room == null)
			return false;

		var target = RespawnsAtRoomStart
			? RoomStartRespawn()
			: roomEntryDirection.Y != 0 ? Save.LastSafeGrounded : null;
		if (target is not { } respawn)
			return false;

		RespawnPlayerAt(player, respawn, reloadRoom: false);
		return true;
	}

	// The demo is deliberately forgiving; flip this back for the real game.
	static DeathRespawnMode DeathRespawn => DeathRespawnMode.NearWhereYouDied;

	public void RespawnPlayerAtLastSafeGrounded(bool restartMusic = false)
	{
		if (restartMusic)
			StopMusic(true);

		var target = RespawnsAtRoomStart
			? RoomStartRespawn() ?? SavePointRespawn()
			: SavePointRespawn() ?? Save.LastSafeGrounded;

		if (GetFirst<Player>() is { } player && target is { } respawn && RespawnPlayerAt(player, respawn, reloadRoom: true))
		{
			RestorePlayerAfterRespawn(player);
			if (restartMusic)
				MusicWasRestarted();
		}
		else
			ReloadRoom(restartMusic);

		DeathLoopRescue();
	}

	// The gym scores runs against the shipping rules, so it always respawns at the save point.
	bool RespawnsAtRoomStart => !Simulating && DeathRespawn == DeathRespawnMode.NearWhereYouDied;

	RespawnState? SavePointRespawn()
		=> Save.LastSavePoint is { } lastId ? ResolveSavePoint(lastId) : null;

	// The start of the room the player is in: where they came in, pulled inward to solid footing.
	// A mid-air arrival (fell in, or came through a burrow door) has no footing to inset from, so
	// it falls back to the first ground they found in the room.
	RespawnState? RoomStartRespawn()
	{
		if (room is not { } current)
			return null;
		if (roomRespawnPoint is { } entry && entry.RoomId == current.Id &&
			GetFirst<Player>() is { } player && player.TryInsetRespawnPoint(entry.Position, out var inset))
			return new RespawnState(current.Id, inset);
		return firstGroundedInRoom is { } grounded && grounded.RoomId == current.Id ? grounded : null;
	}

	void RestorePlayerAfterRespawn(Player player)
	{
		player.Health = Player.MaxHealth;
		player.State = Player.States.Normal;
		player.Stop();
		EnsureBudForPlayer(repositionExisting: true);
	}

	// Every respawn target can end up hanging over a pit -- a room edited out from under the
	// player, or a room start the player can no longer survive -- so each respawn falls straight
	// back to its death and the loop never breaks on its own. Deaths with no safe grounding in
	// between count up (NoteSafeGrounded resets); after a few, break out. The editor moves
	// the player to the nearest supported footing (the room is being edited, so that is the fix);
	// anywhere else, fall back to the save point.
	int deathsWithoutSafeGround;
	const int DeathLoopRescueThreshold = 4;

	void DeathLoopRescue()
	{
		if (++deathsWithoutSafeGround < DeathLoopRescueThreshold || GetFirst<Player>() is not { } player)
			return;

		deathsWithoutSafeGround = 0;
#if EDITOR
		if (player.TryFindSafeFootingNear(player.Position, out var safe))
		{
			player.Position = safe;
			player.Stop();
			NoteSafeGrounded(safe, Player.StandingHitboxRect + safe);
			EnsureBudForPlayer(repositionExisting: true);
			DisplayMessage("death loop detected: moved player to safe ground");
			return;
		}
#endif
		if (SavePointRespawn() is { } savePoint && RespawnPlayerAt(player, savePoint, reloadRoom: true))
			RestorePlayerAfterRespawn(player);
	}

	// Resolves an activated save point's actor id to a concrete respawn location by
	// reading its current position from the live room spec. Returns null if the save
	// point no longer exists (deleted from the level), so callers skip it.
	public RespawnState? ResolveSavePoint(string actorId)
	{
		if (Assets.FindActorById(actorId) is not { } found)
			return null;

		var local = ActorSpecs.ReadResolvedPosition(found.Actor) ?? Point2.Zero;
		return new RespawnState(found.Room.Id, found.Room.WorldBounds.TopLeft + local);
	}

	// Records the given save point as a warp/respawn destination (idempotent) and makes
	// it the one death returns to. Called when the player touches it.
	public void ActivateSavePoint(SavePoint savePoint)
	{
		if (savePoint.ActorId is not { } id || string.IsNullOrWhiteSpace(id))
			return;

		if (!Save.SavePoints.Contains(id))
			Save.SavePoints.Add(id);
		Save.LastSavePoint = id;
		MarkSaveDirty(captureMap: false);
	}

	// Opens the map in warp mode, showing every activated save point (resolved to its
	// live position) and starting the selection on the one the player is standing at.
	public void OpenSavePointWarp(SavePoint savePoint)
	{
		if (room == null)
			return;

		var targets = new List<Minimap.WarpTarget>();
		foreach (var id in Save.SavePoints)
			if (ResolveSavePoint(id) is { } r)
				targets.Add(new(id, r.RoomId, r.Position));

		if (targets.Count == 0)
			return;

		Minimap.BeginWarp(targets, savePoint.ActorId);
		Minimap.ResetView();
		Minimap.Rebuild(room, this);
		PlayAudioEvent("map/open");
	}

	// Whether "save point" belongs in the pause menu at all. Deliberately the cheap check -- the
	// menu is immediate-mode, and confirming the save point still exists means walking every room's
	// actors (ResolveSavePoint), which TryWarpToLastSavePoint does once, when it is actually asked.
	public bool HasLastSavePoint => !string.IsNullOrWhiteSpace(Save.LastSavePoint);

	// Pause-menu "save point": travel back to the one the player last touched, with the same warp
	// the map's warp mode plays. Nothing happens without a save point, or when the one on record
	// has since been deleted from the level.
	public bool TryWarpToLastSavePoint()
	{
		if (room == null || SavePointWarpActive || Save.LastSavePoint is not { } id ||
			ResolveSavePoint(id) is not { } target)
			return false;

		BeginSavePointWarp(new Minimap.WarpTarget(id, target.RoomId, target.Position));
		return SavePointWarpActive;
	}

	// Travels to an arbitrary world position with the save-point warp (the demo teleport menu).
	// The destination is not a save point, so it carries no actor id.
	public bool TryWarpTo(string roomId, Point2 position)
	{
		if (room == null || SavePointWarpActive || (room.Id != roomId && Assets.GetRoom(roomId) == null))
			return false;

		BeginSavePointWarp(new Minimap.WarpTarget("", roomId, position));
		return SavePointWarpActive;
	}

	// Travels to a chosen save point (from warp mode) and closes the map.
	public void WarpToSavePoint(Minimap.WarpTarget target)
	{
		var returnToPlaying = target.ActorId == Minimap.WarpOriginActorId;
		Minimap.CloseWarp();
		if (!returnToPlaying)
			BeginSavePointWarp(target);
	}

	// Warp-mode map controls: movement picks a save point, South/X confirms the warp,
	// East/Circle (or Escape) cancels.
	void UpdateWarpModeInput()
	{
		var input = Manager.Input;

		var moved = false;
		if (Controls.Move.PressedUp)
			moved = Minimap.MoveWarpSelection(Point2.Up);
		else if (Controls.Move.PressedDown)
			moved = Minimap.MoveWarpSelection(Point2.Down);
		else if (Controls.Move.PressedLeft)
			moved = Minimap.MoveWarpSelection(Point2.Left);
		else if (Controls.Move.PressedRight)
			moved = Minimap.MoveWarpSelection(Point2.Right);

		if (moved)
			PlayAudioEvent("map/warp_move");

		if (input.AnyControllerButtonPressed(Buttons.South) || input.Keyboard.Pressed(Keys.Enter, Keys.Space))
		{
			if (Minimap.SelectedWarp is { } selected)
			{
				PlayAudioEvent("map/warp_confirm");
				WarpToSavePoint(selected);
			}
			return;
		}

		if (input.AnyControllerButtonPressed(Buttons.East) || input.Keyboard.Pressed(Keys.Escape))
		{
			PlayAudioEvent("map/close");
			Minimap.CloseWarp();
		}
	}

	bool RespawnPlayerAt(Player player, RespawnState respawn, bool reloadRoom)
	{
		if (room == null)
			return false;

		if (!reloadRoom && room.Id == respawn.RoomId)
		{
			player.Position = respawn.Position;
			return true;
		}

		if (Assets.GetRoom(respawn.RoomId) is not { } target)
			return false;

		ActivateRoomImmediately(target.CloneRuntime(), player, respawn.Position, RoomChangeKind.Reload, Point2.Zero);
		return true;
	}

	void ActivateRoomImmediately(Room targetRoom, Player player, Point2 playerPosition, RoomChangeKind kind, Point2 direction)
	{
		var fromRoom = room;
		foreach (var actor in Actors)
			if (actor is not Player && !actor.KeepOnRoomTransition)
			{
				actor.Visible = false;
				Destroy(actor);
			}

		room?.RoomGeoChanged -= OnRoomGeoChanged;
		room = targetRoom;
		nextRoom = null;
		transitionDirection = direction;
		LoadRoom(room);
		player.Position = playerPosition;
		player.Velocity = Vector2.Zero;
		zoomLevel = RoomZoom(room);
		Camera = ClampCameraToRoom(player.Position - ZoomedSize / 2);
		ApplyMusicArea(room);
		ApplyRoomReverb(room);
		ApplyRoomMusicMuffle(room);
		roomSwappedThisFrame = true;
		RaiseRoomChanged(fromRoom, room, kind, direction);
		FinishRoomActivation(kind);
	}

	void RequestRoomTransitionGc(RoomChangeKind kind)
	{
		pendingRoomTransitionGcKind = kind;
		pendingRoomTransitionGcFrames = 1;
#if BROWSER
		debugFramesSinceRoomTransition = 0;
#endif
	}

	void UpdateRoomTransitionGc()
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
		if (OperatingSystem.IsBrowser())
		{
			Console.WriteLine(
				$"Room transition GC ({pendingRoomTransitionGcKind}): {elapsed.TotalMilliseconds:0.0}ms, " +
				$"{before / 1024}KB -> {after / 1024}KB, " +
				$"collections +{GC.CollectionCount(0) - gen0Before}/+{GC.CollectionCount(1) - gen1Before}/+{GC.CollectionCount(2) - gen2Before}");
		}
	}

#if BROWSER
	void UpdateBrowserDebugStats(long frameStartAllocatedBytes)
	{
		var allocatedNow = GC.GetAllocatedBytesForCurrentThread();
		var frameAllocatedBytes = Math.Max(0, allocatedNow - frameStartAllocatedBytes);
		browserDebugStatsAllocatedBytes += frameAllocatedBytes;
		browserDebugStatsMaxFrameBytes = Math.Max(browserDebugStatsMaxFrameBytes, frameAllocatedBytes);
		browserDebugStatsFrames++;
		browserDebugStatsElapsed += Time.Delta;
		if (debugFramesSinceRoomTransition >= 0)
			debugFramesSinceRoomTransition++;

		if (browserDebugStatsElapsed < BrowserDebugStatsInterval)
			return;

		var gen0 = GC.CollectionCount(0);
		var gen1 = GC.CollectionCount(1);
		var gen2 = GC.CollectionCount(2);
		var frames = Math.Max(browserDebugStatsFrames, 1);
		var avgAllocatedBytes = browserDebugStatsAllocatedBytes / frames;
		var heapBytes = GC.GetTotalMemory(false);
		var transitionText = debugFramesSinceRoomTransition >= 0
			? $"{debugFramesSinceRoomTransition}f since transition"
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
#endif

	static string FormatBytes(long bytes)
	{
		if (bytes < 1024)
			return $"{bytes}B";

		var kb = bytes / 1024.0;
		if (kb < 1024)
			return $"{kb:0.0}KB";

		return $"{kb / 1024.0:0.0}MB";
	}

	public T Create<T>(Point2? position = null, Action<T>? init = null) where T : Actor, new()
	{
		var instance = new T
		{
			Game = this,
			Room = loadingRoom ?? room,
			Position = position ?? Point2.Zero,
			Tags = Factory.TagsForType(typeof(T)),
			Anchor = Factory.AnchorForType(typeof(T).Name),
			CreationOrder = nextActorCreationOrder++
		};
		init?.Invoke(instance);
		AddOrParkActor(instance);
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
		instance.Room = loadingRoom ?? room;
		instance.Position = position ?? Point2.Zero;
		instance.Tags = Factory.TagsForType(type);
		instance.Anchor = Factory.AnchorForType(type.Name);
		instance.CreationOrder = nextActorCreationOrder++;
		init?.Invoke(instance);
		AddOrParkActor(instance);
		return instance;
	}

	void AddOrParkActor(Actor actor)
	{
		if (!ActorActiveForRoomInversion(actor))
		{
			parkedActors.Add(actor);
			return;
		}

		if (!actor.AddedOnce)
		{
			actor.Added();
			actor.AddedOnce = true;
		}
		Actors.Add(actor);
	}

	bool ActorActiveForRoomInversion(Actor actor)
		=> actor.Invert == ActorInversion.Both || invertedNow == (actor.Invert == ActorInversion.InvertedOnly);

	public T? GetFirst<T>() where T : Actor
	{
		foreach (var it in Actors)
			if (it is T instance && !destroying.Contains(it))
				return instance;
		return null;
	}

	public T? GetActorById<T>(string actorId) where T : Actor
	{
		var actor = GetActorById(actorId);
		if (actor is T) return actor as T; // TODO: better way to phrase this i'm sure
		return default(T);
	}

	public Actor? GetActorById(string actorId)
	{
		foreach (var it in Actors)
			if (string.Equals(it.ActorId, actorId, StringComparison.OrdinalIgnoreCase) && !destroying.Contains(it))
				return it;
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
		if (destroying.Contains(actor)) return;
		if (actor.Mask.Has(Actor.Masks.Enemy))
			NotifyEnemyKilledIfPlayerCaused(actor);
		destroying.Add(actor);
	}

	public void DestroyForThisSession(Actor actor)
	{
		if (!string.IsNullOrEmpty(actor.SessionDestroyKey))
		{
			Save.Session.DestroyedActors.Add(actor.SessionDestroyKey);
			MarkSaveDirty();
		}
		Destroy(actor);
	}

	public bool DestroyPersistently(Actor actor)
	{
		var newlyDestroyed = false;
		if (!string.IsNullOrEmpty(actor.SessionDestroyKey))
		{
			newlyDestroyed = Save.PersistentDestroyedActors.Add(actor.SessionDestroyKey);
			if (newlyDestroyed)
				MarkSaveDirty();
		}
		Destroy(actor);
		return newlyDestroyed;
	}

	internal void DuckDestroyed(Vector2 worldOrigin)
	{
		DuckHudWorldOrigin = worldOrigin;
		DucksDestroyed++;
	}

	// Whether a save records a kill of any actor of this type, anywhere. Walks the rooms because
	// PersistentDestroyedActors holds placement keys, not types. Static so achievement rules can ask
	// it about the save on disk without a session running.
	internal static bool AnyPersistentlyDestroyedOfType(SaveState save, string typeName)
	{
		if (save.PersistentDestroyedActors.Count == 0)
			return false;

		foreach (var room in Assets.RoomsById.Values)
			foreach (var actor in room.Actors)
			{
				if (!string.Equals(ActorSpecs.GetTypeName(actor), typeName, StringComparison.OrdinalIgnoreCase))
					continue;
				var localPosition = ActorSpecs.ReadPoint(actor, "Position") ?? Point2.Zero;
				if (save.PersistentDestroyedActors.Contains(SessionDestroyKey(room, actor, localPosition)))
					return true;
			}
		return false;
	}

	int CountPersistentlyDestroyedProps(string category)
	{
		if (Save.PersistentDestroyedActors.Count == 0)
			return 0;

		var count = 0;
		foreach (var room in Assets.RoomsById.Values)
			foreach (var actor in room.Actors)
			{
				if (!string.Equals(ActorSpecs.GetTypeName(actor), nameof(Prop), StringComparison.OrdinalIgnoreCase) ||
					!string.Equals(ActorSpecs.ReadResolvedString(actor, nameof(Prop.Category)), category, StringComparison.OrdinalIgnoreCase))
					continue;
				var localPosition = ActorSpecs.ReadPoint(actor, "Position") ?? Point2.Zero;
				if (Save.PersistentDestroyedActors.Contains(SessionDestroyKey(room, actor, localPosition)))
					count++;
			}
		return count;
	}

	internal void PreventActorRespawn(Actor actor)
	{
		if (!string.IsNullOrEmpty(actor.SessionDestroyKey) && Save.PersistentDestroyedActors.Add(actor.SessionDestroyKey))
			MarkSaveDirty();
	}

	internal bool WasActorDestroyedPersistently(Room? actorRoom, string actorId)
		=> actorRoom != null && Save.PersistentDestroyedActors.Contains(SessionDestroyKey(actorRoom, actorId));

	void RemoveDestroyedActors()
	{
		for (var i = 0; i < destroying.Count; i++)
		{
			var actor = destroying[i];
			if (actor.AddedOnce)
				actor.Destroyed();
			Actors.Remove(actor);
			parkedActors.Remove(actor);
			DropTimeFactorsOwnedBy(actor);
		}
		destroying.Clear();
	}

	public void SetHiddenCoverEditorOpacity(bool focused)
	{
		foreach (var actor in Actors)
			if (actor is HiddenCoverGrid hiddenCover)
				hiddenCover.SetRenderOpacityScale(IsEditorPreview ? focused ? 0.5f : 0.0f : 1.0f);
	}

	public string GetSubstanceBelow(Actor actor, bool searchOutward = false)
	{
		if (room == null || actor.Hitbox.Shape != Hitbox.Shapes.Rect)
			return Factory.DefaultSubstance;

		var bounds = actor.Hitbox.Rect + actor.Position;
		var y = bounds.Bottom;
		if (TryGetSubstanceAtWorldPoint(new(bounds.Left + 1, y), out var substance))
			return substance;
		if (TryGetSubstanceAtWorldPoint(new(bounds.Right - 1, y), out substance))
			return substance;

		// Nothing underfoot is tiled, so ask the actor being stood on what it is made of. Anything
		// jumpthru-masked that doesn't answer stays wooden, as every jumpthru sounded before
		// SurfaceSubstance existed.
		var footprint = actor.Hitbox.Rect + actor.Position + new Point2(0, 1);
		if (OverlapsFirstSurface(footprint) is { } surface)
			return surface.SurfaceSubstance ?? "Wood";

		// Edge landings can sit just beside the supporting tile, so landing audio gets a one-tile horizontal grace scan.
		if (searchOutward && TryGetSubstanceOutwardFromBottom(bounds, out substance))
			return substance;

		return Factory.DefaultSubstance;
	}

	public bool TryGetSubstanceAtWorldPoint(Point2 point, out string substance)
	{
		substance = Factory.DefaultSubstance;
		if (room == null)
			return false;

		var local = point - room.WorldBounds.TopLeft;
		var tile = new Point2(
			(int)MathF.Floor(local.X / (float)TileSize),
			(int)MathF.Floor(local.Y / (float)TileSize));

		if (tile.X < 0 || tile.Y < 0 || tile.X >= room.Size.X || tile.Y >= room.Size.Y)
			return false;

		if (room.GetEntry(tile) is { Substance: { } foundSubstance })
		{
			substance = foundSubstance;
			return true;
		}

		return false;
	}

	// True when the given world point falls inside a water tile. Water is a non-solid
	// tile (Factory 'w'); pawns sample their torso here to drive swim physics.
	public bool HasWater => LiquidVolumes.Count > 0 || room is { } && (room.WaterSurfaces.Count > 0 || room.CappedWaterRects.Count > 0);
	internal readonly List<LiquidVolume> LiquidVolumes = [];

	public bool IsWaterAt(Point2 worldPoint, bool continuePastTopEdge = false)
	{
		foreach (var volume in LiquidVolumes)
			if (volume.Contains(worldPoint))
				return true;
		if (room == null)
			return false;
		var local = worldPoint - room.WorldBounds.TopLeft;
		var tile = new Point2(
			(int)MathF.Floor(local.X / (float)TileSize),
			(int)MathF.Floor(local.Y / (float)TileSize));
		if (continuePastTopEdge && tile.Y < 0 && tile.X >= 0 && tile.X < room.Size.X)
			tile.Y = 0;
		return room.IsWaterTile(tile);
	}

	// Kicks the water surface under worldX (if any) into a splash. impulseSpeed is signed:
	// positive dips the surface down (plunging in), negative bulges it up (bursting out).
	public void SplashWater(float worldX, float impulseSpeed)
		=> WaterSurfaceAtX(worldX)?.Splash(worldX, impulseSpeed);

	// Returns the exposed water surface whose horizontal span contains worldX, or null.
	public WaterSurface? WaterSurfaceAtX(float worldX)
	{
		foreach (var volume in LiquidVolumes)
			if (volume.Surface is { } surface && surface.ContainsX(worldX))
				return surface;
		if (room == null)
			return null;
		foreach (var surface in room.WaterSurfaces)
			if (surface.ContainsX(worldX))
				return surface;
		return null;
	}

	void UpdateWaterSurfaces()
	{
		if (room == null)
			return;
		foreach (var surface in room.WaterSurfaces)
			surface.Update(Time.Delta);
	}

	bool TryGetSubstanceOutwardFromBottom(RectInt bounds, out string substance)
	{
		var centerX = bounds.Center.X;
		var y = bounds.Bottom;
		for (var offset = 0; offset <= TileSize; offset++)
		{
			if (TryGetSubstanceAtWorldPoint(new(centerX - offset, y), out substance))
				return true;
			if (offset > 0 && TryGetSubstanceAtWorldPoint(new(centerX + offset, y), out substance))
				return true;
		}

		substance = Factory.DefaultSubstance;
		return false;
	}

	public bool Transition(Point2 direction)
	{
		if (room != null && nextRoom == null)
		{
			var fromRoom = room;
			var exitPosition = GetFirst<Player>()?.Position ?? (Vector2)room.WorldBounds.Center;
			if (Assets.FindAdjacentRoomPlayMode(room, direction, exitPosition) is { } foundRoom)
			{
				nextRoom = foundRoom.CloneRuntime();
				transitionDirection = new Point2(Math.Sign(direction.X), Math.Sign(direction.Y));
				foreach (var it in Actors)
					if (it is not Player && !it.KeepOnRoomTransition)
						Destroy(it);

				transitionCameraFrom = Camera;
				transitionZoomFrom = zoomLevel;
				transitionZoomTo = RoomZoom(nextRoom);
				// Aim the ease where live control will hold the camera: clamp against the new room's
				// camera blockers, except any the player crosses on entry, so we never glide into a
				// stale blocked bite and snap out when control resumes. Blocker gating keys off the
				// predicted entry -- the exit position clamped into the new room.
				var nextBounds = nextRoom.WorldBounds;
				var entry = new Vector2(
					Math.Clamp(exitPosition.X, (float)nextBounds.Left, nextBounds.Right - 1f),
					Math.Clamp(exitPosition.Y, (float)nextBounds.Top, nextBounds.Bottom - 1f));
				transitionCameraTo = ClampCameraToRoomAndBlockers(
					exitPosition - ZoomedSizeAt(transitionZoomTo) / 2, transitionZoomTo, nextBounds, entry,
					CameraBlockerRects(nextRoom, entry, cameraBlockerBuffer));
				nextRoomEase = 0.0f;
				transitionPreparedRenderFrame = null;
				LoadRoom(nextRoom);
				RaiseRoomChanged(fromRoom, nextRoom, RoomChangeKind.EdgeTransition, transitionDirection);
				ApplyMusicArea(nextRoom);
				ApplyRoomReverb(nextRoom);
				ApplyRoomMusicMuffle(nextRoom);
				return true;
			}
#if EDITOR
			if (Manager.NotifyMissingLiveAdjacentRoom(this, fromRoom, direction, exitPosition))
				return true;
#endif
		}

		return false;
	}

	internal void NoteRoomTransitionRendered()
	{
		if (nextRoom != null)
			transitionPreparedRenderFrame ??= Time.RenderFrame;
	}

	public bool CheatShiftRoom(Point2 direction)
	{
		if (room == null || nextRoom != null || GetFirst<Player>() is not { } player)
			return false;

		var source = Assets.GetRoom(room.Id) ?? room;
		var bounds = source.WorldBounds;
		var exitPosition = direction.X > 0 ? new Vector2(bounds.Right, bounds.CenterY)
			: direction.X < 0 ? new Vector2(bounds.Left - 1, bounds.CenterY)
			: direction.Y > 0 ? new Vector2(bounds.CenterX, bounds.Bottom)
			: new Vector2(bounds.CenterX, bounds.Top - 1);
		if (Assets.FindAdjacentRoom(source, direction, exitPosition) is not { } adjacent)
			return false;

		var target = adjacent.CloneRuntime();
		var playerPosition = FindFallbackPlayerPosition(target, direction);
		foreach (var actor in target.Actors)
		{
			if (string.Equals(ActorSpecs.GetTypeName(actor), nameof(PlayerStart), StringComparison.OrdinalIgnoreCase))
			{
				playerPosition = target.WorldBounds.TopLeft + (ActorSpecs.ReadResolvedPosition(actor) ?? Point2.Zero);
				break;
			}
		}

		ActivateRoomImmediately(target, player, playerPosition, RoomChangeKind.Reload, direction);
		if (player.Health <= 0)
			player.Health = Player.MaxHealth;
		player.State = Player.States.Normal;
		player.Stop();
		EnsureBudForPlayer(repositionExisting: true);
		return true;
	}

	public bool TransitionToLinkedDoor(string targetActorId)
	{
		if (room == null || nextRoom != null || linkedDoorCooldown > 0 || GetFirst<Player>() is not { } player)
			return false;
		if (string.IsNullOrWhiteSpace(targetActorId))
			return false;
		if (Assets.FindActorById(targetActorId) is not { } target)
			return false;

		if (!string.Equals(ActorSpecs.GetTypeName(target.Actor), nameof(LinkedDoor), StringComparison.OrdinalIgnoreCase))
			return false;

		ReadLinkedDoorDestination(target.Actor, out var localPosition, out var spawnOffset);
		var targetRoom = target.Room.CloneRuntime();
		var crossLayer = targetRoom.Sublayer != room.Sublayer;
		var targetPosition = crossLayer ? player.Position + spawnOffset : targetRoom.WorldBounds.TopLeft + localPosition + spawnOffset;
		var oldCamera = Camera;
		if (crossLayer)
			View?.BeginLayerTransition(player.Position);

		ActivateRoomImmediately(targetRoom, player, targetPosition, RoomChangeKind.LinkedDoor, Point2.Zero);
		if (crossLayer)
		{
			Camera = oldCamera;
			layerTransitionTime = LayerTransitionDuration;
		}
		linkedDoorCooldown = 0.35f;
		if (!crossLayer)
			PulseDitherGlitch();
		return true;
	}

	public bool TransitionToBurrowDoor(BurrowLine source, int sourcePoint,
		[NotNullWhen(true)] out BurrowLine? targetLine, out int targetPoint)
	{
		targetLine = null;
		targetPoint = -1;
		if (room == null || nextRoom != null || GetFirst<Player>() is not { } player ||
			!source.IsDoorPoint(sourcePoint) || !TryFindBurrowDoorDestination(source, sourcePoint, out var target))
			return false;

		var targetRoom = target.Room.CloneRuntime();
		var oldCamera = Camera;
		View?.BeginLayerTransition(player.Position);
		ActivateRoomImmediately(targetRoom, player, target.Position, RoomChangeKind.BurrowDoor, Point2.Zero);
		Camera = oldCamera;
		layerTransitionTime = LayerTransitionDuration;
		targetLine = GetActorById(target.ActorId) as BurrowLine;
		targetPoint = target.Point;
		return targetLine != null;
	}

	readonly record struct BurrowDoorDestination(Room Room, string ActorId, int Point, Point2 Position);

	bool TryFindBurrowDoorDestination(BurrowLine source, int sourcePoint, out BurrowDoorDestination destination)
	{
		destination = default;
		if (room == null)
			return false;

		var worldPoint = (Point2)source.WorldPoint(sourcePoint);
		var matches = 0;
		foreach (var candidateRoom in Assets.RoomsById.Values)
		{
			if (candidateRoom.Sublayer == room.Sublayer)
				continue;

			foreach (var actor in candidateRoom.Actors)
			{
				if (!string.Equals(ActorSpecs.GetTypeName(actor), nameof(BurrowLine), StringComparison.OrdinalIgnoreCase) ||
					ActorSpecs.GetActorId(actor) is not { } actorId)
					continue;

				var nodes = ActorSpecs.GetResolvedProperty(actor, nameof(IPointPath.Points)) as JsonArray;
				var pointCount = nodes?.Count ?? 2;
				if (pointCount < 2 ||
					!Enum.TryParse<BurrowLine.BurrowDoorPoints>(ActorSpecs.ReadResolvedString(actor, nameof(BurrowLine.BurrowDoors)), true, out var doors))
					continue;

				var origin = candidateRoom.WorldBounds.TopLeft + (ActorSpecs.ReadResolvedPosition(actor) ?? Point2.Zero);
				var first = nodes != null ? ActorSpecs.ReadPoint(nodes[0]) ?? Point2.Zero : Point2.Zero;
				var last = nodes != null ? ActorSpecs.ReadPoint(nodes[^1]) ?? Point2.Zero : Point2.Right * (TileSize * 8);
				if ((doors & BurrowLine.BurrowDoorPoints.First) != 0 && origin + first == worldPoint)
				{
					destination = new(candidateRoom, actorId, 0, worldPoint);
					matches++;
				}
				if ((doors & BurrowLine.BurrowDoorPoints.Last) != 0 && origin + last == worldPoint)
				{
					destination = new(candidateRoom, actorId, pointCount - 1, worldPoint);
					matches++;
				}
			}
		}

		return matches == 1;
	}

	static void ReadLinkedDoorDestination(JsonObject actor, out Point2 localPosition, out Point2 spawnOffset)
	{
		localPosition = ActorSpecs.ReadResolvedPosition(actor) ?? Point2.Zero;
		spawnOffset = ActorSpecs.ReadResolvedPoint(actor, nameof(LinkedDoor.SpawnOffset)) ?? LinkedDoor.DefaultSpawnOffset;
	}

	void ResolvePlayerRoomTransition(Point2 direction)
	{
		if (room == null || direction == Point2.Zero || GetFirst<Player>() is not { } player)
			return;

		// A burrow that crossed the edge stays burrowed: re-anchor it to the continuing surface
		// rather than the normal push-out-of-collision placement.
		if (player.State == Player.States.Burrow)
		{
			player.ResolveBurrowRoomTransition(direction);
			return;
		}

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

	bool TryPlacePlayerAfterTransition(Player player, Point2 original, Point2 forward, Point2 perpendicular, int perpendicularDistance, int maxForwardDistance)
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

	// First actor overlapping rect that something can stand on: one that names its own
	// SurfaceSubstance, or failing that any jumpthru (see GetSubstanceBelow).
	Actor? OverlapsFirstSurface(in RectInt rect)
	{
		Actor? jumpthru = null;
		foreach (var actor in Actors)
		{
			if (!actor.Mask.Has(Actor.Masks.Solid | Actor.Masks.Jumpthru) || !actor.Hitbox.Overlaps(rect - actor.Position))
				continue;
			if (actor.SurfaceSubstance != null)
				return actor;
			if (jumpthru == null && actor.Mask.Has(Actor.Masks.Jumpthru))
				jumpthru = actor;
		}

		return jumpthru;
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

	public Actor? OverlapsFirstCombat(in RectInt rect, Actor.Masks mask, bool contactDamageOnly = false)
	{
		foreach (var actor in Actors)
			if (!destroying.Contains(actor) && (!contactDamageOnly || actor.ContactDamageEnabled) &&
				actor.Mask.Has(mask) && actor.OverlapsCombatBounds(rect))
				return actor;
		return null;
	}

	public bool OverlapsRoomCollision(Actor actor, Point2 offset)
	{
		if (actor.Hitbox.Shape != Hitbox.Shapes.Rect)
			return false;

		return OverlapsRoomCollision(actor, actor.Hitbox.Rect + actor.Position + offset);
	}

	internal bool OverlapsRoomCollision(Actor actor, in RectInt bounds)
		=> CollisionRoomFor(actor) is { } actorRoom && OverlapsRoomCollision(actorRoom, bounds);

	internal Room? CollisionRoomFor(Actor actor)
		=> loadingRoom != null && actor.Room == loadingRoom ? loadingRoom : room ?? actor.Room;

	internal bool OverlapsEnemyRoomBoundary(Actor actor, Point2 offset)
	{
		if (!actor.Tags.Has(ActorTag.Enemy) || actor.Hitbox.Shape != Hitbox.Shapes.Rect || actor.Room is not { } actorRoom)
			return false;

		var current = actor.Hitbox.Rect + actor.Position;
		var candidate = current + offset;
		var bounds = actorRoom.WorldBounds;
		return candidate.Left < bounds.Left && candidate.Left < current.Left ||
			candidate.Right > bounds.Right && candidate.Right > current.Right ||
			candidate.Top < bounds.Top && candidate.Top < current.Top ||
			actor.Tags.Has(ActorTag.Flying) && candidate.Bottom > bounds.Bottom && candidate.Bottom > current.Bottom;
	}

	void DestroyFallenEnemy(Actor actor)
	{
		if (destroying.Contains(actor) || !actor.Tags.Has(ActorTag.Enemy) || actor.Tags.Has(ActorTag.Flying) ||
			actor.Hitbox.Shape != Hitbox.Shapes.Rect || actor.Room is not { } actorRoom ||
			(actor.Hitbox.Rect + actor.Position).Top < actorRoom.WorldBounds.Bottom)
			return;

#if DEBUG
		//var local = actor.Position - actorRoom.WorldBounds.TopLeft;
		//Console.WriteLine($"[RoomBounds] {actor.GetType().Name} fell out of {actorRoom.Id} at local {local}, world {actor.Position}");
#endif
		DestroyForThisSession(actor);
	}

	public bool OverlapsRoomCollision(in RectInt bounds)
		=> room != null && OverlapsRoomCollision(room, bounds);

	// Pure room-geometry collision: depends only on the room's tile data, so it is safe
	// to call off the main thread (e.g. the nav-graph worker build via NavWorld).
	internal static bool OverlapsRoomCollision(Room room, in RectInt bounds)
	{
		// Collision is tile-aligned, so only the tiles the bounds cover can matter.
		// Walk that tile range once, testing the precomputed solid mask (equivalent
		// to scanning CollisionRects, since rects are unions of solid tiles) and any
		// slope occupying a covered tile (equivalent to scanning SlopeTiles, since a
		// slope only matters where its tile is covered). Geometry not built yet -> no
		// mask -> no collision (matches empty CollisionRects/SlopeTiles).
		var solid = room.SolidTiles;
		if (solid.Length == 0)
			return false;

		var origin = room.WorldBounds.TopLeft;
		int sizeX = room.Size.X, sizeY = room.Size.Y;

		// world bounds -> inclusive tile range (Right/Bottom are exclusive).
		// Coordinates here are >= origin in the in-room case; clamping covers
		// bounds that spill outside the room.
		int tx0 = (bounds.Left - origin.X) / TileSize;
		int tx1 = (bounds.Right - 1 - origin.X) / TileSize;
		int ty0 = (bounds.Top - origin.Y) / TileSize;
		int ty1 = (bounds.Bottom - 1 - origin.Y) / TileSize;
		if (tx0 < 0) tx0 = 0;
		if (ty0 < 0) ty0 = 0;
		if (tx1 >= sizeX) tx1 = sizeX - 1;
		if (ty1 >= sizeY) ty1 = sizeY - 1;

		var hasSlopes = room.SlopeTiles.Count > 0;
		for (var ty = ty0; ty <= ty1; ty++)
		{
			var rowBase = ty * sizeX;
			for (var tx = tx0; tx <= tx1; tx++)
			{
				if (solid[rowBase + tx])
					return true;

				if (hasSlopes && room.TryGetSlopeTile(new Point2(tx, ty), out var slope) && OverlapsSlope(bounds, room, slope))
					return true;
			}
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

	bool RoomCollisionContains(Vector2 point, bool includeBreakableTiles = true)
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

			if (GameView.PointInPolygon(point, room.GetSlopePolygon(slope)))
				return includeBreakableTiles || !IsBreakableRoomCollisionPoint(point);
		}

		return false;
	}

	bool IsBreakableRoomCollisionPoint(Vector2 point) => room?.IsBreakableRoomCollisionPoint(point) ?? false;

	static bool OverlapsSlope(RectInt bounds, Room room, Room.SlopeTile slope)
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
				if (GameView.PointInPolygon(new Vector2(x + 0.5f, y + 0.5f), polygon))
					return true;
			}

		return false;
	}

	readonly List<Point2> tileRemovalScratch = [];

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

		tileRemovalScratch.Clear();
		for (var y = top; y <= bottom; y++)
			for (var x = left; x <= right; x++)
			{
				var point = new Point2(x, y);
				if (room.IsBreakable(point) || Factory.Find(room.Tiles[x, y]) is { Destructible: true })
					tileRemovalScratch.Add(point);
			}

		return RemoveTiles(tileRemovalScratch, hitbox.Center);
	}

	// Removes the given room tiles as one batch: a single geometry rebuild + visual refresh
	// (not one per tile), a Pop per tile, and one break sound per substance removed.
	bool RemoveTiles(List<Point2> tiles, Vector2 soundPosition)
	{
		if (room == null || tiles.Count == 0)
			return false;

		var substancesBroken = new HashSet<string>();
		foreach (var point in tiles)
		{
			if (Factory.Find(room.Tiles[point.X, point.Y]) is { Substance: { } substance })
				substancesBroken.Add(substance);
			Create<Pop>(TileWorldCenter(point));
		}

		ClearTiles(tiles);
		foreach (var substance in substancesBroken)
			PlayAudioEvent("tile_break", "Type", Factory.SubstanceSoundType(substance), position: soundPosition);

		return true;
	}

	// One geometry rebuild + visual refresh for the whole batch, not one per tile.
	void ClearTiles(List<Point2> tiles)
	{
		foreach (var point in tiles)
			room!.Set(point, '0', rebuildGeometry: false);
		room!.RebuildGeometry(TileWorldBounds(tiles));
		RefreshRoomTiles();
		View?.MarkRoomNormalCacheDirty(room);
		View?.MarkRoomTileFieldCacheDirty(room);
	}

	readonly List<Point2> evaporateScratch = [];
	readonly HashSet<Point2> evaporateVisited = [];

	// Boils off every authored water body touching the given current-room tiles: each touched
	// tile flood-fills its whole connected pool, so nothing is left floating.
	internal void EvaporateWater(List<Point2> touchedTiles, int steamEffect)
	{
		if (room == null || touchedTiles.Count == 0)
			return;

		evaporateScratch.Clear();
		evaporateVisited.Clear();
		foreach (var start in touchedTiles)
		{
			if (!room.IsWaterTile(start) || !evaporateVisited.Add(start))
				continue;
			var head = evaporateScratch.Count;
			evaporateScratch.Add(start);
			while (head < evaporateScratch.Count)
			{
				var tile = evaporateScratch[head++];
				foreach (var next in (ReadOnlySpan<Point2>)[tile + Point2.Left, tile + Point2.Right, tile + Point2.Up, tile + Point2.Down])
					if (room.IsWaterTile(next) && evaporateVisited.Add(next))
						evaporateScratch.Add(next);
			}
		}
		if (evaporateScratch.Count == 0)
			return;

		var center = Vector2.Zero;
		foreach (var tile in evaporateScratch)
		{
			var position = (Vector2)TileWorldCenter(tile);
			center += position;
			if (steamEffect >= 0)
				Particles?.Emit(steamEffect, position, 6);
		}
		ClearTiles(evaporateScratch);
		PlayAudioEvent("liquid/steam", "Size", evaporateScratch.Count, position: center / evaporateScratch.Count);
	}

	RectInt TileWorldBounds(List<Point2> tiles)
	{
		var bounds = new RectInt(room!.WorldBounds.TopLeft + tiles[0] * TileSize, Point2.One * TileSize);
		for (var i = 1; i < tiles.Count; i++)
			bounds = bounds.Conflate(new RectInt(room.WorldBounds.TopLeft + tiles[i] * TileSize, Point2.One * TileSize));
		return bounds;
	}

	Point2 TileWorldCenter(Point2 tile)
		=> room != null
			? room.WorldBounds.TopLeft + new Point2(tile.X * TileSize + TileSize / 2, tile.Y * TileSize + TileSize / 2)
			: Point2.Zero;

	public void HitVisualActors(RectInt hitbox, HashSet<Actor> consumed)
	{
		for (int i = 0, count = Actors.Count; i < count; i++)
		{
			var actor = Actors[i];
			if (!destroying.Contains(actor) && !consumed.Contains(actor) &&
				actor is IHitByHitbox { } hittable && hittable.HitByHitbox(hitbox))
				consumed.Add(actor);
		}
	}

	public void ReloadRoom(bool restartMusic = false, bool reloadRigs = false)
	{
		if (room == null) return;

		if (restartMusic)
			StopMusic(true);

		var fromRoom = room;
		var reloaded = Assets.GetRoom(room.Id)?.CloneRuntime() ?? room.CloneRuntime();
		foreach (var actor in Actors)
			Destroy(actor);
		if (reloadRigs)
		{
			RemoveDestroyedActors();
			RigLibrary.Clear();
		}

		room.RoomGeoChanged -= OnRoomGeoChanged;
		room = reloaded;

		LoadRoom(room);
		if (GetFirst<Player>() == null && respawnPosition is { } position)
			Create<Player>(position);
		EnsureBudForPlayer(repositionExisting: true);
		RaiseRoomChanged(fromRoom, room, RoomChangeKind.Reload, Point2.Zero);
		FinishRoomActivation(RoomChangeKind.Reload);


		if (restartMusic)
			MusicWasRestarted();
	}

	internal void MusicWasRestarted() {
		StartMusic();
		if (room != null)
		{
			ApplyMusicArea(room);
			ApplyRoomReverb(room);
			ApplyRoomMusicMuffle(room);
		}
	}

	// Swaps the editor preview to a different room while reusing this Game and its
	// GameView, instead of the old dispose + `new Game` path that reallocated the
	// entire GameView (render targets + materials, ~750KB) on every switch. Mirrors
	// the constructor's editor-preview activation, preceded by an actor teardown;
	// FinishRoomActivation flushes the destroyed actors and prunes the previous
	// room's GPU caches so nothing accumulates across switches.
	public void SwitchEditorRoom(Room target)
	{
		var fromRoom = room;
		foreach (var actor in Actors)
			Destroy(actor);

		room?.RoomGeoChanged -= OnRoomGeoChanged;
		room = target.CloneRuntime();
		Camera = ClampCameraToRoom(room.WorldBounds.TopLeft);
		UpdateAudioListener();
		LoadRoom(room);

		// respawnPosition is gameplay-only (readonly, set in the ctor) and unused by
		// the non-playing editor preview, so we just place the player for display.
		if (GetFirst<PlayerStart>() is { } playerStart)
		{
			if (GetFirst<Player>() is { } player)
				player.Position = playerStart.Position;
			else
				Create<Player>(playerStart.Position);
		}
		EnsureBudForPlayer(repositionExisting: true, atPlayerPosition: spawnBudAtPlayer);
		RaiseRoomChanged(fromRoom, room, RoomChangeKind.InitialLoad, Point2.Zero);

		FinishRoomActivation(RoomChangeKind.Reload);
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
		HiddenCoverGrid? hiddenCover = null;
		foreach (var actor in Actors)
		{
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

		for (var x = 0; x < room.Size.X; x++)
			for (var y = 0; y < room.Size.Y; y++)
			{
				var tile = new Point2(x, y);
				if (room.GetBackgroundEntry(tile) is { } backgroundEntry)
					backgroundEntry.Tile?.Invoke(tile, room, fg, fgVisual, bg, bgDual ?? fgDual);
				if (room.GetEntry(tile) is { } entry)
					entry.Tile?.Invoke(tile, room, fg, fgVisual, bg, fgDual);
			}

		hiddenCover?.Build(room);

		if (bgDual != null)
			BuildDualTiles(room, bgDual, Factory.TileLayer.Background);
		BuildDualTiles(room, fgDual, Factory.TileLayer.Foreground);
		View?.MarkRoomNormalCacheDirty(room);
		View?.MarkRoomTileFieldCacheDirty(room);
	}

	bool invertedNow;
	readonly List<InvertPin> inversionPins = [];
	internal bool RoomInverted => invertedNow;
	internal Vector2 RoomInversionOrigin { get; private set; }
	internal float RoomInversionEffectProgress => Math.Clamp((Time.SecondsF - roomInversionStartedAt) / RoomInversionEffectDuration, 0, 1);
	internal bool RoomInversionEffectActive => Time.SecondsF - roomInversionStartedAt < RoomInversionEffectDuration;
	internal bool RoomSupportsInversion => room?.SupportsBurrowInverse == true;
	internal float RoomInversionStartedAt => roomInversionStartedAt;
	static float RoomInversionEffectDuration => 0.7f;
	float roomInversionStartedAt = float.NegativeInfinity;

	internal bool ToggleBurrowRoomInverse(Vector2 origin)
	{
		if (!ToggleRoomInverse())
			return false;

		RoomInversionOrigin = origin;
		roomInversionStartedAt = Time.SecondsF;
		//Shake(0.1f);
		HitStun(0.05f);
		fx.Bloom();
		PlayAudioEvent("invert", "Enabled", invertedNow ? 1.0f : 0.0f);
		return true;
	}

	internal bool SetEditorRoomInverted(bool inverted)
		=> IsEditorPreview && invertedNow != inverted && ToggleRoomInverse();

	bool ToggleRoomInverse()
	{
		if (room?.SupportsBurrowInverse != true)
			return false;

		inversionPins.Clear();
		foreach (var actor in Actors)
			if (actor is InvertPin pin && pin.Room == room && !destroying.Contains(pin))
				inversionPins.Add(pin);

		if (!room.ToggleBurrowInverse(!invertedNow, inversionPins))
			return false;

		invertedNow = !invertedNow;
		ActivateParkedActors(room);

		for (var i = 0; i < Actors.Count; i++)
			if (Actors[i] is IRoomInversionListener listener && Actors[i].Room == room && !destroying.Contains(Actors[i]))
				listener.RoomInverted();
		ParkInactiveActors(room);

		RefreshRoomTiles();
		return true;
	}

	void ActivateParkedActors(Room forRoom)
	{
		for (var i = 0; i < parkedActors.Count;)
		{
			var actor = parkedActors[i];
			if (actor.Room != forRoom || destroying.Contains(actor) || !ActorActiveForRoomInversion(actor))
			{
				i++;
				continue;
			}

			parkedActors.RemoveAt(i);
			AddOrParkActor(actor);
		}
	}

	void ParkInactiveActors(Room forRoom)
	{
		for (var i = Actors.Count - 1; i >= 0; i--)
		{
			var actor = Actors[i];
			if (actor.Room != forRoom || destroying.Contains(actor) || ActorActiveForRoomInversion(actor))
				continue;

			Actors.RemoveAt(i);
			parkedActors.Add(actor);
		}
	}

	void BuildDualTiles(Room forRoom, DualGrid dual, Factory.TileLayer layer)
	{
		dual.Clear();
		// Iterate the dictionary directly (struct enumerator) instead of a capturing
		// Where(...).ToArray(); this runs twice per LoadRoom (bg + fg).
		foreach (var (tileType, entry) in Factory.Entries)
		{
			if (entry.DualTilesetName == null || entry.Layer != layer)
				continue;
			if (Assets.GetTileset(entry.DualTilesetName) is not { } dualTileset)
				continue;

			dual.Build(forRoom, tileType, dualTileset, layer);
		}
	}

	public void LoadRoom(Room forRoom)
	{
		DiscardParkedActors();
		invertedNow = false;
		var previousLoadingRoom = loadingRoom;
		loadingRoom = forRoom;
		try
		{
			LoadRoomContents(forRoom);
		}
		finally
		{
			loadingRoom = previousLoadingRoom;
		}
	}

	void DiscardParkedActors()
	{
		foreach (var actor in parkedActors)
			if (actor.AddedOnce)
				actor.Destroyed();
		parkedActors.Clear();
	}

	void LoadRoomContents(Room forRoom)
	{
		forRoom.RoomGeoChanged += OnRoomGeoChanged;

		// Geometry is already built: every caller passes a freshly CloneRuntime()'d room,
		// and CloneRuntime() ends with RebuildGeometry(). Rebuilding here ran the full
		// slope/collision/occluder/shadow/reflection build a second time per load.
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

		var hiddenCover = Create<HiddenCoverGrid>(offset);

		// Size to the room before filling. Grid.Set grows on demand to exactly the tile it was
		// handed, so filling a room wider than the default 40x23 reallocated and copied all four
		// backing arrays once per column -- quadratic in room width, and two of those arrays hold
		// Subtextures, so every copy is a write-barrier bulk move. Matches RebuildGeometry, which
		// already resizes up front.
		bg.Resize(forRoom.Size);
		fgVisual.Resize(forRoom.Size);
		fg.Resize(forRoom.Size);

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

		BuildDualTiles(forRoom, bgDual, Factory.TileLayer.Background);
		BuildDualTiles(forRoom, fgDual, Factory.TileLayer.Foreground);

		foreach (var actor in forRoom.Actors)
		{
			var type = ActorSpecs.GetTypeName(actor);
			if (!loadGoals && string.Equals(type, nameof(Goal), StringComparison.OrdinalIgnoreCase))
				continue;
			if (GetFirst<Player>() != null && string.Equals(type, nameof(Player), StringComparison.OrdinalIgnoreCase))
				continue;

			var localPosition = ActorSpecs.ReadPoint(actor, "Position") ?? Point2.Zero;
			var sessionDestroyKey = SessionDestroyKey(forRoom, actor, localPosition);
			if (Save.PersistentDestroyedActors.Contains(sessionDestroyKey) || Save.Session.DestroyedActors.Contains(sessionDestroyKey))
				continue;

			ActorSpecs.Create(this, actor, offset + localPosition, sessionDestroyKey);
		}

		hiddenCover.Build(forRoom);
		hiddenCover.RevealForCurrentPlayer(immediate: true);
		EnsureBudForPlayer();

		SpawnFungusLayer(forRoom);
		SpawnNctRegions(forRoom);
	}

	/// Finds each connected island of Fungal-flagged tiles and spawns one
	/// FungalGrowthActor per island, tightly bounded to that island's tiles.
	/// Islands are independent: each is seeded from its own (room, min-tile)
	/// key, so painting one island never reshuffles another's growth.
	void SpawnFungusLayer(Room forRoom)
	{
		var sizeX = forRoom.Size.X;
		var sizeY = forRoom.Size.Y;
		var visited = new bool[sizeX, sizeY];
		var frontier = new Stack<Point2>();

		for (var sx = 0; sx < sizeX; sx++)
			for (var sy = 0; sy < sizeY; sy++)
			{
				if (visited[sx, sy] || !forRoom.IsFungal(new Point2(sx, sy)))
					continue;

				// Flood-fill this island (4-connected), tracking its tile bbox.
				var minX = sx;
				var minY = sy;
				var maxX = sx;
				var maxY = sy;

				var island = new List<Point2>();
				visited[sx, sy] = true;
				frontier.Push(new Point2(sx, sy));
				while (frontier.Count > 0)
				{
					var t = frontier.Pop();
					island.Add(t);
					minX = Math.Min(minX, t.X);
					maxX = Math.Max(maxX, t.X);
					minY = Math.Min(minY, t.Y);
					maxY = Math.Max(maxY, t.Y);

					for (var d = 0; d < 4; d++)
					{
						var nx = t.X + FloodDX[d];
						var ny = t.Y + FloodDY[d];
						if (nx < 0 || ny < 0 || nx >= sizeX || ny >= sizeY)
							continue;
						if (visited[nx, ny] || !forRoom.IsFungal(new Point2(nx, ny)))
							continue;
						visited[nx, ny] = true;
						frontier.Push(new Point2(nx, ny));
					}
				}

				SpawnFungusIsland(forRoom, island, minX, minY, maxX, maxY);
			}
	}

	static readonly int[] FloodDX = [1, -1, 0, 0];
	static readonly int[] FloodDY = [0, 0, 1, -1];

	void SpawnFungusIsland(Room forRoom, List<Point2> island, int minX, int minY, int maxX, int maxY)
	{
		var tileWidth = maxX - minX + 1;
		var tileHeight = maxY - minY + 1;
		var width = tileWidth * TileSize;
		var height = tileHeight * TileSize;

		var mask = new float[width * height];
		foreach (var tile in island)
		{
			var tx = tile.X - minX;
			var ty = tile.Y - minY;
			for (var py = 0; py < TileSize; py++)
				for (var px = 0; px < TileSize; px++)
					mask[(ty * TileSize + py) * width + (tx * TileSize + px)] = 1.0f;
		}

		var position = forRoom.WorldBounds.TopLeft + new Point2(minX * TileSize, minY * TileSize);

		Create<FungalGrowthActor>(position, layer =>
		{
			// Seed from the island's min tile so unrelated islands stay stable.
			layer.RandomSeed = GetStringHash($"{forRoom.Id}:{minX},{minY}");
			layer.RegionWidth = width;
			layer.RegionHeight = height;
			layer.GrowthMask = mask;
		});
	}

	/// Grows neural-texture tile regions: for every NCT tile type, finds each connected island of
	/// its char and spawns one region simulation that fills the island's shape (see NctTileSpec).
	/// Like the fungus layer, these actors live only in Game.Actors — never Room.Actors — so they
	/// regenerate from the tiles each load and are never serialized.
	void SpawnNctRegions(Room forRoom)
	{
		foreach (var (id, entry) in Factory.Entries)
			if (entry.NeuralTexture is { } spec)
			{
				var grid = spec.Layer == Factory.TileLayer.Foreground ? forRoom.Tiles : forRoom.BackgroundTiles;
				SpawnNctIslands(forRoom, id, spec, grid);
			}
	}

	void SpawnNctIslands(Room forRoom, char id, NctTileSpec spec, char[,] grid)
	{
		var sizeX = forRoom.Size.X;
		var sizeY = forRoom.Size.Y;
		var visited = new bool[sizeX, sizeY];
		var frontier = new Stack<Point2>();

		for (var sx = 0; sx < sizeX; sx++)
			for (var sy = 0; sy < sizeY; sy++)
			{
				if (visited[sx, sy] || grid[sx, sy] != id)
					continue;

				int minX = sx, minY = sy, maxX = sx, maxY = sy;
				var island = new List<Point2>();
				visited[sx, sy] = true;
				frontier.Push(new Point2(sx, sy));
				while (frontier.Count > 0)
				{
					var t = frontier.Pop();
					island.Add(t);
					minX = Math.Min(minX, t.X);
					maxX = Math.Max(maxX, t.X);
					minY = Math.Min(minY, t.Y);
					maxY = Math.Max(maxY, t.Y);
					for (var d = 0; d < 4; d++)
					{
						var nx = t.X + FloodDX[d];
						var ny = t.Y + FloodDY[d];
						if (nx < 0 || ny < 0 || nx >= sizeX || ny >= sizeY)
							continue;
						if (visited[nx, ny] || grid[nx, ny] != id)
							continue;
						visited[nx, ny] = true;
						frontier.Push(new Point2(nx, ny));
					}
				}

				SpawnNctIsland(forRoom, id, spec, island, minX, minY, maxX, maxY);
			}
	}

	void SpawnNctIsland(Room forRoom, char id, NctTileSpec spec, List<Point2> island, int minX, int minY, int maxX, int maxY)
	{
		var tileW = maxX - minX + 1;
		var tileH = maxY - minY + 1;
		var member = new bool[tileW, tileH];
		foreach (var t in island)
			member[t.X - minX, t.Y - minY] = true;

		var tileOrigin = new Point2(minX, minY);
		var padding = (int)MathF.Ceiling(MathF.Max(0, spec.OutsetPixels));
		var position = forRoom.WorldBounds.TopLeft + tileOrigin * TileSize - new Point2(padding, padding);
		var outputSize = new Point2(tileW * TileSize, tileH * TileSize) + new Point2(padding * 2, padding * 2);
		var simulationSize = Point2.Max(Point2.One, (outputSize * (1f / spec.FeatureScale)).CeilingToPoint2());
		var mask = BuildNctRegionMask(member, spec, forRoom, tileOrigin, position, outputSize, simulationSize);

		var region = Create<NeuralTextureActor>(position, a =>
		{
			a.AutoPerturb = false;
			a.Size = outputSize;
			a.SimulationScale = 1f / spec.FeatureScale;
			a.FlowOrigin = -new Vector2(64 * spec.FeatureScale) - position;
			a.FlowSize = new Vector2(128 * spec.FeatureScale);
			a.DetailOrigin = (Vector2)outputSize * 0.5f;
			a.Layer = spec.Layer;
			a.MaterialTile = id;
			a.CastsMaskShadow = spec.Solid;
			a.WeightsFile = spec.Interior;
			a.WeightsFile1 = spec.Edge;
			a.WeightsFile2 = "";
			a.WeightsFile3 = "";
			a.FireRate = spec.FireRate;
			a.Contrast = spec.Contrast;
			a.Saturation = spec.Saturation;
			a.Brightness = spec.Brightness;
			a.Tint = spec.Tint;
			a.NormalStrength = spec.NormalStrength;
			a.SilhouetteNormalStrength = spec.SilhouetteNormalStrength;
			a.DetailScale = spec.DetailScale;
			a.DetailPhase = spec.DetailPhase;
			a.PatchFrequency = spec.PatchFrequency;
			a.SirenFrequency = spec.SirenFrequency;
			a.LatentGain = spec.LatentGain;
			a.LatentBias = spec.LatentBias;
			a.NearestStateSampling = spec.NearestStateSampling;
			// Frozen regions never step in Update; SimulateFromSeed below advances them once.
			a.StepsPerSecond = spec.Frozen ? 0f : spec.StepsPerSecond;
			a.InitialMaskCells = mask;
		});

		if (spec.Frozen)
			region.SimulateFromSeed(spec.FreezeSteps);
	}

	/// Builds the per-slot blend mask for one island at its fixed world-space simulation scale: the interior model
	/// (slot 0) fills the core and the edge model (slot 1) takes over in a distance-feathered band
	/// along the boundary. With no edge model, the interior alone feathers out to nothing. The grid
	/// border counts as outside, so a solid rectangular island still feathers from its edges inward.
	static uint[] BuildNctRegionMask(bool[,] member, NctTileSpec spec, Room room, Point2 tileOrigin, Point2 position, Point2 outputSize, Point2 simulationSize)
	{
		var width = simulationSize.X;
		var height = simulationSize.Y;
		var clipSlopes = spec.Layer == Factory.TileLayer.Foreground;

		// Sample the same actor-local world positions as the simulation and decoder.
		var region = new bool[width * height];
		for (var cy = 0; cy < height; cy++)
			for (var cx = 0; cx < width; cx++)
			{
				var outputPoint = new Vector2(cx + 0.5f, cy + 0.5f) * spec.FeatureScale;
				if (outputPoint.X < 0 || outputPoint.Y < 0 || outputPoint.X >= outputSize.X || outputPoint.Y >= outputSize.Y)
					continue;
				region[cy * width + cx] = NctShapeContains(member, room, tileOrigin, (Vector2)position + outputPoint, clipSlopes, spec.RoundingPixels);
			}

		ApplyNctOutset(region, width, height, spec.OutsetPixels, spec.FeatureScale);

		// Multi-source BFS: distance (in state cells) from each region cell to the nearest cell
		// outside the region — real dead cells, plus the grid border as a virtual outside.
		var dist = new int[width * height];
		Array.Fill(dist, -1);
		var queue = new Queue<int>();
		for (var i = 0; i < width * height; i++)
			if (!region[i])
			{
				dist[i] = 0;
				queue.Enqueue(i);
			}
		for (var i = 0; i < width * height; i++)
		{
			var x = i % width;
			var y = i / width;
			if (region[i] && dist[i] == -1 && (x == 0 || x == width - 1 || y == 0 || y == height - 1))
			{
				dist[i] = 1;
				queue.Enqueue(i);
			}
		}
		while (queue.Count > 0)
		{
			var i = queue.Dequeue();
			var x = i % width;
			var y = i / width;
			var nd = dist[i] + 1;
			if (x > 0 && dist[i - 1] == -1) { dist[i - 1] = nd; queue.Enqueue(i - 1); }
			if (x < width - 1 && dist[i + 1] == -1) { dist[i + 1] = nd; queue.Enqueue(i + 1); }
			if (y > 0 && dist[i - width] == -1) { dist[i - width] = nd; queue.Enqueue(i - width); }
			if (y < height - 1 && dist[i + width] == -1) { dist[i + width] = nd; queue.Enqueue(i + width); }
		}

		var band = Math.Max(1, spec.EdgeBandCells);
		var hasEdge = !string.IsNullOrEmpty(spec.Edge);
		var mask = new uint[width * height];
		for (var i = 0; i < width * height; i++)
		{
			if (!region[i])
				continue;
			var t = Math.Clamp(Math.Max(dist[i], 0) / (float)band, 0f, 1f);
			var smooth = MathF.Pow(t * t * (3f - 2f * t), spec.BlendCurve);
			var edge = hasEdge ? (1f - smooth) * spec.EdgeWeight : 0f;
			var slot0 = (uint)Math.Clamp((int)MathF.Round(255f * (hasEdge ? 1f - edge : smooth)), 0, 255);
			var slot1 = (uint)Math.Clamp((int)MathF.Round(255f * edge), 0, 255);
			mask[i] = slot0 | (slot1 << 8);
		}
		return mask;
	}

	static bool NctShapeContains(bool[,] member, Room room, Point2 tileOrigin, Vector2 point, bool clipSlopes, float rounding)
	{
		var inside = NctRawShapeContains(member, room, tileOrigin, point, clipSlopes);
		if (rounding <= 0)
			return inside;

		var vertex = ((point - (Vector2)room.WorldBounds.TopLeft) / TileSize).RoundToPoint2();
		var vertexPoint = (Vector2)room.WorldBounds.TopLeft + vertex * TileSize;
		var delta = point - vertexPoint;
		if (MathF.Abs(delta.X) >= rounding || MathF.Abs(delta.Y) >= rounding)
			return inside;

		var radius = new Vector2(rounding);
		var topLeft = NctRawShapeContains(member, room, tileOrigin, vertexPoint - radius, clipSlopes) ? 1f : 0f;
		var topRight = NctRawShapeContains(member, room, tileOrigin, vertexPoint + new Vector2(rounding, -rounding), clipSlopes) ? 1f : 0f;
		var bottomLeft = NctRawShapeContains(member, room, tileOrigin, vertexPoint + new Vector2(-rounding, rounding), clipSlopes) ? 1f : 0f;
		var bottomRight = NctRawShapeContains(member, room, tileOrigin, vertexPoint + radius, clipSlopes) ? 1f : 0f;
		var amount = delta / (rounding * 2) + new Vector2(0.5f);
		return float.Lerp(float.Lerp(topLeft, topRight, amount.X), float.Lerp(bottomLeft, bottomRight, amount.X), amount.Y) >= 0.5f;
	}

	static bool NctRawShapeContains(bool[,] member, Room room, Point2 tileOrigin, Vector2 point, bool clipSlopes)
	{
		var tile = ((point - (Vector2)room.WorldBounds.TopLeft) / TileSize).FloorToPoint2();
		var local = tile - tileOrigin;
		if (local.X < 0 || local.Y < 0 || local.X >= member.GetLength(0) || local.Y >= member.GetLength(1) || !member[local.X, local.Y])
			return false;

		return !clipSlopes || !room.TryGetSlopeTile(tile, out var slope) || room.SlopeContainsPoint(slope, point);
	}

	static void ApplyNctOutset(bool[] region, int width, int height, float outsetPixels, float pixelsPerCell)
	{
		if (MathF.Abs(outsetPixels) < 0.01f)
			return;

		var source = (bool[])region.Clone();
		var grow = outsetPixels > 0;
		var distances = new float[region.Length];
		Array.Fill(distances, float.PositiveInfinity);
		var queue = new PriorityQueue<int, float>();
		for (var y = 0; y < height; y++)
			for (var x = 0; x < width; x++)
			{
				var index = y * width + x;
				if (source[index] == grow)
				{
					distances[index] = 0;
					queue.Enqueue(index, 0);
				}
				else if (!grow && (x == 0 || y == 0 || x == width - 1 || y == height - 1))
				{
					distances[index] = 1;
					queue.Enqueue(index, 1);
				}
			}

		Span<Point2> directions =
		[
			new(-1, -1), new(0, -1), new(1, -1),
			new(-1, 0),              new(1, 0),
			new(-1, 1),  new(0, 1),  new(1, 1)
		];
		while (queue.TryDequeue(out var index, out var queuedDistance))
		{
			if (queuedDistance != distances[index])
				continue;

			var x = index % width;
			var y = index / width;
			foreach (var direction in directions)
			{
				var next = new Point2(x, y) + direction;
				if (next.X < 0 || next.Y < 0 || next.X >= width || next.Y >= height)
					continue;

				var nextIndex = next.Y * width + next.X;
				var nextDistance = queuedDistance + (direction.X != 0 && direction.Y != 0 ? 1.41421356f : 1f);
				if (nextDistance >= distances[nextIndex])
					continue;
				distances[nextIndex] = nextDistance;
				queue.Enqueue(nextIndex, nextDistance);
			}
		}

		var limit = MathF.Abs(outsetPixels);
		for (var i = 0; i < region.Length; i++)
			if (source[i] != grow && MathF.Max(0, distances[i] - 0.5f) * pixelsPerCell <= limit)
				region[i] = grow;
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
					  (UInt64)LS4B[2] << 8 | (UInt64)LS4B[3];
		return hash;
	}

	void EnsureBudForPlayer(bool repositionExisting = false, bool atPlayerPosition = false)
	{
		if (!spawnBud)
			return;

		if (GetFirst<Player>() is not { } player)
			return;

		Point2 position = atPlayerPosition ? player.Position : FindBudSpawnPosition(player);
		if (GetFirst<Bud>() is { } bud)
		{
			if (repositionExisting)
			{
				bud.Position = position;
				bud.Velocity = Vector2.Zero;
				bud.Facing = player.Facing;
				bud.CancelFallRespawn();
			}
			return;
		}

		Create<Bud>(position, bud =>
		{
			bud.Facing = player.Facing;
			bud.KeepOnRoomTransition = true;
		});
	}

	Point2 FindBudSpawnPosition(Player player)
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

	void RaiseRoomChanged(Room? from, Room to, RoomChangeKind kind, Point2 direction)
	{
		if (!Simulating)
			Minimap.NoteRoomChange(from, to, kind);
		var playerPosition = GetFirst<Player>()?.Position ?? (Vector2)to.WorldBounds.Center;
		var change = new RoomChange(from, to, kind, direction, playerPosition);
		RoomChanged?.Invoke(change);
		if (kind == RoomChangeKind.EdgeTransition && !Simulating)
		{
			string RoomChangeDirection = "";
			if (direction.X > 0) RoomChangeDirection = "GoingRight";
			else if (direction.X < 0) RoomChangeDirection = "GoingLeft";
			else if (direction.Y > 0) RoomChangeDirection = "GoingDown";
			else if (direction.Y < 0) RoomChangeDirection = "GoingUp";
			PlayAudioEvent("room_change", "RoomChangeDirection", RoomChangeDirection);
		}
		OnStoryRoomChanged(change);
		if (!Simulating)
			Manager.FlushSaveState();
	}

	static string SessionDestroyKey(Room room, JsonObject actor, Point2 localPosition)
	{
		var id = ActorSpecs.ReadString(actor, "Id");
		if (!string.IsNullOrWhiteSpace(id))
			return SessionDestroyKey(room, id);

		var source = ActorSpecs.ReadString(actor, "From") ?? ActorSpecs.ReadString(actor, "Type") ?? "Actor";
		return $"{room.Id}:{source}:{localPosition.X},{localPosition.Y}";
	}

	static string SessionDestroyKey(Room room, string actorId) => $"{room.Id}:id:{actorId}";

	public void HitStun(float time) => hitStun = MathF.Max(hitStun, time);

	public void Shake(float time, float intensity = 1)
	{
		if (ShakeInRenderTargetSpace)
			ShakeRenderTarget(time, intensity);
		else
			fx.shaking = MathF.Max(fx.shaking, time);
	}

	public void ShakeRenderTarget(float time, float intensity = 1)
	{
		renderTargetShaking = MathF.Max(renderTargetShaking, time);
		renderTargetShakeIntensity = MathF.Max(renderTargetShakeIntensity, Math.Clamp(intensity, 0, 1));
	}
	public void KickRenderTarget(Vector2 impulse) => renderTargetKickVelocity += impulse;

	internal void DebugShakeRenderTarget() => ShakeRenderTarget(DebugRenderTargetShakeSeconds);

	public void PulseDitherGlitch(Vector2? axis = null, float amount = 1.0f)
	{
		if (axis is { } value && value.LengthSquared() > 0.001f)
			ditherGlitchAxis = Vector2.Normalize(value);
		ditherGlitchTarget = amount;
	}

	internal void DestroyEnemy(Actor actor)
	{
		HitStun(0.1f);
		DestroyForThisSession(actor);
		Create<Pop>(actor.Position + new Point2(0, -4));
	}
}
