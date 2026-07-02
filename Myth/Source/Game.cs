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

	// Rendering facade: external callers (Manager, actors) reach the view through Game.
	// Forwards to View, or no-ops/defaults when headless (View == null).
	public void Render(in RectInt viewport, bool showHud = true, RectInt? scissor = null) => View?.Render(viewport, showHud, scissor);
	public void RenderWorldView(in RectInt viewport, Rect worldView, bool showHud = false, RectInt? scissor = null, bool applyCrt = true) => View?.RenderWorldView(viewport, worldView, showHud, scissor, applyCrt);
	public void RenderWorldToTarget(Target target, Rect worldView, bool showHud = false) => View?.RenderWorldToTarget(target, worldView, showHud);
	public void RenderSdfAura(Batcher batcher, Span<float> uniforms, Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3) => View?.RenderSdfAura(batcher, uniforms, p0, p1, p2, p3);
	public void PreviewParticlesInEditor(float dt, string? selectedActorId) => View?.PreviewParticlesInEditor(dt, selectedActorId);
	public void SetPreviewEffect(string name) => View?.SetPreviewEffect(name);
	public void BurstTestEffect() => View?.BurstTestEffect();
	public ParticleSystem? Particles => View?.Particles;
	public IReadOnlyList<Target> DebugTargets => View?.DebugTargets ?? [];
	public float SdfAuraScrollTime => View?.SdfAuraScrollTime ?? 0f;
	public Vector2 ViewCenterWorld => View?.ViewCenterWorld ?? default;
	public Target Screen => View!.Screen;
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

	public Time Time => Manager.Time.MultiplyDelta(timeScale);

	internal Room? room;
	internal Room? nextRoom;
	// Transition animation params: written by sim during room transitions, read by
	// GameView's lerping (Current*Color/Intensity). Exposed internal + forwarded on View.
	internal float nextRoomEase;
	Point2 transitionDirection;
	Vector2 transitionCameraFrom;
	Vector2 transitionCameraTo;
	internal Color transitionBackdropColorFrom;
	internal Color transitionBackdropColorTo;
	internal Color transitionBackdropColor2From;
	internal Color transitionBackdropColor2To;
	internal Color transitionAmbientColorFrom;
	internal Color transitionAmbientColorTo;
	internal float transitionAmbientIntensityFrom;
	internal float transitionAmbientIntensityTo;
	internal float transitionLightNoiseFrom;
	internal float transitionLightNoiseTo;
	internal Color transitionSunColorFrom;
	internal Color transitionSunColorTo;
	internal float transitionSunStrengthFrom;
	internal float transitionSunStrengthTo;
	internal Vector2 transitionSunDirectionFrom;
	internal Vector2 transitionSunDirectionTo;
	float linkedDoorCooldown;
	bool roomSwappedThisFrame;
	internal readonly List<Actor> destroying = [];
	readonly HashSet<string> sessionDestroyedActors = [];

	public bool GymBudDied;
	public bool GymMode;
	public Actor? GymTarget;
	static float DitherGlitchAttackSpeed => 15.0f;
	static float DitherGlitchDecaySpeed => 1.35f;
	// Visual feedback state, but sim-owned: decayed each frame in Update and set by the
	// HitStun/Shake/PulseDitherGlitch APIs; GameView only reads it (via forwarding).
	internal float hitStun;
	internal float shaking;
	internal float ditherGlitch;
	internal float ditherGlitchTarget;
	internal Vector2 ditherGlitchAxis = Vector2.UnitX;
	internal Point2 shake;
	internal Rng rng = new();


#if DEBUG
	internal bool draggingPlayer;
	internal bool DraggingPlayer => draggingPlayer;
#else
	internal bool DraggingPlayer => false;
#endif
	// True when the sim is in a room and not mid-transition: when Manager applies live
	// debug shortcuts / editor mouse-drag (matches the old in-Update gating).
	internal bool AcceptsLiveInput => room != null && nextRoom == null;
	float timeScale = 1.0f;
	readonly Point2? respawnPosition;
	readonly bool spawnBud;
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
	internal Vector2 RenderCamera => (Point2)Camera - shake;
	// The camera hard-follows the player, except a discontinuity in the clamp target (a blocker
	// cap toggling as you cross a bite's span, or a blocker revealing) is absorbed into a
	// decaying error so the view glides over the jump instead of snapping.
	Vector2 cameraError;
	Vector2? previousCameraTarget;
	Vector2 previousPlayerPosition;
	static float CameraEaseRate => 10f;
	const float CameraEaseTriggerEpsilon = 2f;
	internal float zoomLevel = 1.0f;
	public float ZoomLevel => zoomLevel;
	internal float ZoomedWidth => Width / zoomLevel;
	internal float ZoomedHeight => Height / zoomLevel;
	internal float fbZoom = 1.0f;
	public float FbZoom => fbZoom;
	// The camera frames this "visible" view and clamps it to the room. It shrinks for the 2x
	// magnify (fbZoom) and, in 4:3 crop mode, narrows horizontally to the cropped width so the
	// player stays inside the actually-visible region rather than the full (partly cropped) frame.
	internal float FbZoomedWidth => AspectCropActive ? VisibleCropWidth : Width / fbZoom;
	internal float FbZoomedHeight => AspectCropActive ? Height : Height / fbZoom;

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
			if (Manager.AspectFourThree)
				return true;
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
			if (Manager.AspectFourThree)
				return 4f / 3f;
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
		if (Manager.AspectFourThree)
		{
			var s = Calc.Min(viewport.Width / 4f, viewport.Height / 3f);
			var w = (int)(4 * s);
			var h = (int)(3 * s);
			var c = viewport.Center;
			return new RectInt(c.X - w / 2, c.Y - h / 2, w, h);
		}
		return viewport;
	}

	// Total pixel scale the composite uses to draw the screen into the given viewport.
	internal float PresentScale(in RectInt present, float outputWidth, float outputHeight)
	{
		var contain = Calc.Min(present.Width / outputWidth, present.Height / outputHeight);
		if (!AspectCropActive)
			return contain * fbZoom;
		var cover = Calc.Max(present.Width / outputWidth, present.Height / outputHeight);
		return cover + (contain - cover) * aspectUncrop;
	}

	// Inverse of the final composite: map a window-space point to a world position, honoring the
	// present rect, fbZoom, and 4:3 cover-crop so touch/mouse picking lines up with what's drawn.
	public Vector2 WindowToWorld(Vector2 windowPoint, in RectInt viewport)
	{
		var present = PresentRect(viewport);
		var scale = PresentScale(present, Screen.Width, Screen.Height);
		var screenPixel = (windowPoint - (Vector2)present.Center) / scale + new Vector2(Screen.Width, Screen.Height) / 2;
		return Camera + screenPixel;
	}

	public RectInt Bounds => room?.WorldBounds ?? new();
	public Point2 Cell => room?.Cell ?? Point2.Zero;
	public Room? CurrentRoom => room;
	public bool IsEditorPreview => !AudioEventsEnabled;
	internal bool Disposed => disposed;
	public bool AudioMuted { get; set; }
	public bool Simulating { get; private set; }
	public NavTrace? NavTrace { get; set; }
	public event Action<RoomChange>? RoomChanged;

	public SimulationScope BeginSimulation()
		=> new(this);

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

		public void Dispose()
			=> game.Simulating = previous;
	}

	public Game(Manager manager, Point2 start, bool audioEventsEnabled = true)
		: this(manager, Assets.GetRoom(start), audioEventsEnabled)
	{
	}

	public Game(Manager manager, Room? start, bool audioEventsEnabled = true, Point2? fallbackPlayerDirection = null, bool spawnFallbackPlayer = false, Point2? initialPlayerPosition = null, bool spawnBud = true, bool enableParticles = true, bool spawnBudAtPlayer = false, (SoundInstance? Music, string? Area)? carryMusic = null)
	{
		rewinder = new(this);
		Manager = manager;
		AudioEventsEnabled = audioEventsEnabled && Manager.AudioEnabled;
		// A previous Game (the prior gym run) may hand off its still-playing
		// background music so it continues seamlessly across the dispose/recreate.
		// Adopting it before StartMusic() means StartMusic no-ops (music != null)
		// and ApplyMusicArea just retargets the live event to this room's area.
		if (carryMusic is { Music: { } carried })
			AdoptMusic(carried, carryMusic.Value.Area);
		this.spawnBud = spawnBud;
		this.spawnBudAtPlayer = spawnBudAtPlayer;
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
			RaiseRoomChanged(null, room, RoomChangeKind.InitialLoad, Point2.Zero);
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
		View?.Dispose();
	}

	// Manager owns input and feeds the per-frame controls in; the sim never reads the
	// live device. A headless Game receives Controls bound to a headless Input (no input).
	public void Update(Controls controls)
	{
		Controls = controls;
#if BROWSER
		var browserDebugFrameStartAllocated = GC.GetAllocatedBytesForCurrentThread();
#endif

		GymBudDied = false;
		roomSwappedThisFrame = false;
		CollisionRectsThisFrame.Clear();
		UpdatePlayerHitSnapshot();
		UpdateRoomTransitionGc();
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
				for (var i = 0; i < Actors.Count; i++)
				{
					Actors[i].Update();
					if (roomSwappedThisFrame)
						break;
				}
				PostUpdateSystems();
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

	void PreUpdateSystems()
	{
		View?.UpdateParticles(Time.Delta);
	}

	void PostUpdateSystems()
	{
		UpdateWaterSurfaces();
		UpdateCamera();
		UpdateStory();
		rewinder.UpdateDebugPlayerRewind();
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

	internal void ChangeTimeScale(int direction)
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

	void UpdateCamera()
	{
		if (room == null || nextRoom != null || DraggingPlayer)
		{
			// The camera is under transition/drag control; drop the easing state so normal
			// control resumes cleanly clamped to the new room rather than gliding from a stale
			// (old-room) target.
			previousCameraTarget = null;
			cameraError = Vector2.Zero;
			return;
		}

		// In 4:3 crop mode the Zoom control un-crops (reveals the cut-off sides) instead of the
		// usual 2x magnify, so the two never fight: suppress fbZoom whenever cropping is active.
		var cropActive = AspectCropActive;
		var fbZoomTarget = !cropActive && Controls.Zoom.Down ? 2.0f : 1.0f;
		fbZoom += (fbZoomTarget - fbZoom) * (1 - MathF.Exp(-Time.Delta * 12f));
		if (MathF.Abs(fbZoom - fbZoomTarget) < 0.005f)
			fbZoom = fbZoomTarget;

		var uncropTarget = cropActive && Controls.Zoom.Down ? 1.0f : 0.0f;
		aspectUncrop += (uncropTarget - aspectUncrop) * (1 - MathF.Exp(-Time.Delta * 12f));
		if (MathF.Abs(aspectUncrop - uncropTarget) < 0.005f)
			aspectUncrop = uncropTarget;

		UpdatePlayerCameraTarget();

		var target = GetGameplayCameraTarget();
		var player = GetFirst<Player>() is { } p ? (Vector2)p.Position : previousPlayerPosition;

		// A static clamp moves the target at most as far as the player moved on each axis, so
		// any excess this frame is a discontinuity (a cap toggling / a reveal). Fold that excess
		// into the error and let it decay, gliding the view across the jump.
		if (previousCameraTarget is { } prev)
		{
			var targetDelta = target - prev;
			var playerDelta = player - previousPlayerPosition;
			if (MathF.Abs(targetDelta.X) - MathF.Abs(playerDelta.X) > CameraEaseTriggerEpsilon)
				cameraError.X -= targetDelta.X - playerDelta.X;
			if (MathF.Abs(targetDelta.Y) - MathF.Abs(playerDelta.Y) > CameraEaseTriggerEpsilon)
				cameraError.Y -= targetDelta.Y - playerDelta.Y;
		}

		cameraError *= MathF.Exp(-Time.Delta * CameraEaseRate);
		if (cameraError.LengthSquared() < 0.25f)
			cameraError = Vector2.Zero;

		// The room edge is the hard outer limit: re-clamp after adding the easing offset so the
		// camera can never show outside the room (easing overflow falls into the dead/blocker
		// space, never past a room edge).
		var padding = new Vector2((Width - FbZoomedWidth) / 2, (Height - FbZoomedHeight) / 2);
		Camera = ClampCameraToRoomFb(target + cameraError, padding);
		previousCameraTarget = target;
		previousPlayerPosition = player;
	}

	// Deactivate any active camera blocker whose rect the just-revealed area overlaps, so a
	// RevealDoor opening also opens the camera over the secret it uncovered.
	public void RevealCameraBlockersOverlapping(RectInt worldArea)
	{
		foreach (var actor in Actors)
			if (actor is CameraBlocker { Active: true } blocker && blocker.WorldRect.Overlaps(worldArea))
				blocker.Deactivate();
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

	Point2 FindFallbackPlayerPosition(Room forRoom, Point2? direction)
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

	static bool IsTileWalkable(Room room, int x, int y)
		=> x >= 0 && y >= 0 && x < room.Size.X && y < room.Size.Y && !IsTileSolid(room, x, y);

	static bool IsTileSolid(Room room, int x, int y)
		=> room.GetEntry(new Point2(x, y)) is { BlocksMovement: true };

	Vector2 easedPlayerCameraTarget; // TODO: use nullable and clear on room switch or load
	Vector2 easedPlayerVelocity;

#if BROWSER
	bool hardCameraLock = false;
#else
	bool hardCameraLock = true;
#endif

	void UpdatePlayerCameraTarget()
	{
		if (GetFirst<Player>() is not { } player)
			return;

		if (hardCameraLock)
		{
			easedPlayerCameraTarget = player.Position;
		}
		else
		{
			if (Vector2.Distance(easedPlayerCameraTarget, player.Position) > 50.0f)
			{
				easedPlayerCameraTarget = player.Position;
			}
			easedPlayerVelocity = Vector2.Lerp(easedPlayerVelocity, player.Velocity, Time.Delta * 1.5f);
			Vector2 newTarget = player.Position + new Vector2(easedPlayerVelocity.X * 0.5f, 0);
			easedPlayerCameraTarget = Vector2.Lerp(easedPlayerCameraTarget, newTarget, Time.Delta * 4.0f);
			easedPlayerCameraTarget = Vector2.Round(easedPlayerCameraTarget);
		}
	}

	public Vector2 GetGameplayCameraTarget()
	{
		if (room == null)
			return Camera;

		var unclamped = easedPlayerCameraTarget - new Vector2(Width, Height) / 2;
		var padding = new Vector2((Width - FbZoomedWidth) / 2, (Height - FbZoomedHeight) / 2);

		var blockers = ActiveCameraBlockers();
		if (blockers.Count == 0)
			return ClampCameraToRoomFb(unclamped, padding);

		// Clamp the zoomed view (Camera + padding) against the room minus the active blockers,
		// then convert back to the Camera top-left.
		var viewSize = new Vector2(FbZoomedWidth, FbZoomedHeight);
		return ClampViewToRoomAndBlockers(unclamped + padding, viewSize, easedPlayerCameraTarget, blockers) - padding;
	}

	// Confine the zoomed view to the room with edge-anchored "bites" removed. A bite caps the
	// view on an axis only while the *player* is within the bite's span on the perpendicular
	// axis. That way a bite only ever hides its own interior -- which the player can't stand in
	// (entering deactivates it) -- so it can never trap the camera away from the player in a
	// narrow arm beside it, regardless of orientation or exact arm width.
	Vector2 ClampViewToRoomAndBlockers(Vector2 viewPos, Vector2 viewSize, Vector2 player, List<RectInt> blockers)
	{
		if (room == null)
			return viewPos;

		var bounds = room.WorldBounds;
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

		// If a bite leaves the arm narrower than the view on an axis, center on it (the
		// "locked" feel) -- mirrors ClampCameraAxis's room-smaller-than-viewport case.
		viewPos.X = minX <= maxX ? Math.Clamp(viewPos.X, minX, maxX) : (minX + maxX) * 0.5f;
		viewPos.Y = minY <= maxY ? Math.Clamp(viewPos.Y, minY, maxY) : (minY + maxY) * 0.5f;
		return viewPos;
	}

	public void SnapCameraToGameplayTarget()
	{
		cameraError = Vector2.Zero;
		Camera = GetGameplayCameraTarget();
		previousCameraTarget = Camera;
		previousPlayerPosition = GetFirst<Player>() is { } p ? (Vector2)p.Position : previousPlayerPosition;
	}

	Vector2 ClampCameraToRoom(Vector2 target)
	{
		if (room == null)
			return target;

		return ClampCameraToRoom(target, room.WorldBounds);
	}

	Vector2 ClampCameraToRoom(Vector2 target, RectInt bounds)
	{
		return new Vector2(
			ClampCameraAxis(target.X, bounds.Left, bounds.Right, ZoomedWidth),
			ClampCameraAxis(target.Y, bounds.Top, bounds.Bottom, ZoomedHeight));
	}

	Vector2 ClampCameraToRoomFb(Vector2 target, Vector2 padding)
	{
		if (room == null) return target;
		return ClampCameraToRoomFb(target, padding, room.WorldBounds);
	}

	Vector2 ClampCameraToRoomFb(Vector2 target, Vector2 padding, RectInt bounds)
	{
		return new Vector2(
			ClampCameraAxis(target.X + padding.X, bounds.Left, bounds.Right, FbZoomedWidth) - padding.X,
			ClampCameraAxis(target.Y + padding.Y, bounds.Top, bounds.Bottom, FbZoomedHeight) - padding.Y);
	}

	static float ClampCameraAxis(float value, float min, float max, float viewportSize)
	{
		var roomSize = max - min;
		if (roomSize <= viewportSize + CameraSlackEpsilon)
			return min + (roomSize - viewportSize) / 2;

		return Math.Clamp(value, min, max - viewportSize);
	}

	void FinishRoomActivation(RoomChangeKind kind)
	{
		RemoveDestroyedActors();
		View?.PruneInactiveRoomCaches();
		PrepareRoomTransitionGc();
		RequestRoomTransitionGc(kind);
	}

	void PrepareRoomTransitionGc()
	{
		// TODO: make this an event which Actors or Systems can subscribe to
		if (spawnBud && GetFirst<Bud>() != null)
		{
			GetPlatformNavGraph();
		}
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
			Position = position ?? Point2.Zero,
			Anchor = Factory.AnchorForType(typeof(T).Name)
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
		instance.Anchor = Factory.AnchorForType(type.Name);
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
		if (destroying.Contains(actor)) return;
		if (actor.Mask.Has(Actor.Masks.Enemy))
			NotifyEnemyKilledIfPlayerCaused(actor);
		destroying.Add(actor);
	}

	public void DestroyForThisSession(Actor actor)
	{
		if (!string.IsNullOrEmpty(actor.SessionDestroyKey))
			sessionDestroyedActors.Add(actor.SessionDestroyKey);
		Destroy(actor);
	}

	void RemoveDestroyedActors()
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
				hiddenCover.SetRenderOpacityScale(IsEditorPreview ? focused ? 0.5f : 0.0f : 1.0f);
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

	bool TryGetSurfaceTypeAtWorldPoint(Point2 point, out string surfaceType)
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

	// True when the given world point falls inside a water tile. Water is a non-solid
	// tile (Factory 'w'); pawns sample their torso here to drive swim physics.
	public bool IsWaterAt(Point2 worldPoint)
		=> TryGetSurfaceTypeAtWorldPoint(worldPoint, out var surfaceType)
			&& surfaceType == Factory.WaterSurfaceType;

	// Kicks the water surface under worldX (if any) into a splash. impulseSpeed is signed:
	// positive dips the surface down (plunging in), negative bulges it up (bursting out).
	public void SplashWater(float worldX, float impulseSpeed)
	{
		if (room == null)
			return;
		foreach (var surface in room.WaterSurfaces)
			if (surface.ContainsX(worldX))
			{
				surface.Splash(worldX, impulseSpeed);
				return;
			}
	}

	// Returns the exposed water surface whose horizontal span contains worldX, or null.
	public WaterSurface? WaterSurfaceAtX(float worldX)
	{
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

	bool TryGetSurfaceTypeOutwardFromBottom(RectInt bounds, out string surfaceType)
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
			if (Assets.FindAdjacentRoomPlayMode(room, direction, exitPosition) is { } foundRoom)
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

	static void ReadLinkedDoorDestination(JsonObject resolved, out Point2 localPosition, out Point2 spawnOffset)
	{
		localPosition = ActorSpecs.ReadPoint(resolved, "Position") ?? Point2.Zero;
		spawnOffset = ActorSpecs.ReadPoint(resolved, "SpawnOffset") ?? LinkedDoor.DefaultSpawnOffset;
	}

	void ResolvePlayerRoomTransition(Point2 direction)
	{
		if (room == null || direction == Point2.Zero || GetFirst<Player>() is not { } player)
			return;

		// A burrow that crossed the edge stays burrowed: re-anchor it to the continuing surface
		// rather than the normal push-out-of-collision placement.
		if (player.State == Player.States.Burrow)
		{
			player.ResolveBurrowRoomTransition();
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

	bool IsBreakableRoomCollisionPoint(Vector2 point)
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
			View?.MarkRoomNormalCacheDirty(room);
			View?.MarkRoomTileFieldCacheDirty(room);
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
		// Geometry is already built: every caller passes a freshly CloneRuntime()'d room,
		// and CloneRuntime() ends with RebuildGeometry(). Rebuilding here ran the full
		// slope/collision/occluder/visibility/reflection build a second time per load.
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

	/// Scans the room's background tiles for Factory.FungusTile and, if any
	/// are painted, spawns a single FungalGrowthActor covering their bounding
	/// box - growth is further constrained to the painted tiles via its mask.
	void SpawnFungusLayer(Room forRoom)
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
		// Each ty writes a disjoint band of rows in the local mask, so it parallelizes cleanly.
		Par.For(0, tileHeight, ty =>
		{
			for (var tx = 0; tx < tileWidth; tx++)
			{
				var growable = forRoom.BackgroundTiles[minX + tx, minY + ty] == Factory.FungusTile ? 1.0f : 0.0f;
				for (var py = 0; py < TileSize; py++)
					for (var px = 0; px < TileSize; px++)
						mask[(ty * TileSize + py) * width + (tx * TileSize + px)] = growable;
			}
		});

		var position = forRoom.WorldBounds.TopLeft + new Point2(minX * TileSize, minY * TileSize);

		Create<FungalGrowthActor>(position, layer =>
		{
			layer.RandomSeed = GetStringHash(forRoom.Id);
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
	}

	static string SessionDestroyKey(Room room, JsonObject actor, Point2 localPosition)
	{
		var id = ActorSpecs.ReadString(actor, "Id");
		if (!string.IsNullOrWhiteSpace(id))
			return $"{room.Id}:id:{id}";

		var source = ActorSpecs.ReadString(actor, "From") ?? ActorSpecs.ReadString(actor, "Type") ?? "Actor";
		return $"{room.Id}:{source}:{localPosition.X},{localPosition.Y}";
	}

	public void HitStun(float time) => hitStun = MathF.Max(hitStun, time);

	public void Shake(float time) => shaking = MathF.Max(shaking, time);

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
