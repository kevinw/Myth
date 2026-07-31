using System.Numerics;
using System.Runtime.CompilerServices;
using Foster.Framework;

namespace Myth;

public partial class Game
{
	internal enum DebugView
	{
		None,
		CollisionRects,
		LightMask,
		NormalBuffer,
		TileFieldRed,
		TileFieldGreen,
		DitherMask,
		Reflection,
		ShadowSegments,
		PlatformNav
	}

	// Debug/dev keyboard + controller shortcuts. Host concern: Manager reads input and
	// drives the sim through its public API (moved out of Game.Update for Phase 2).
	internal void HandleGameDebugShortcuts(Input Input)
	{
		if (!AcceptsLiveInput)
			return;

#if DEBUG
		if (Input.Keyboard.Pressed(Keys.R))
			ReloadRoom();
		// TODO: use an ingame console instead?
		if (Input.Keyboard.Pressed(Keys.P)) ToggleDebugView(DebugView.PlatformNav);
		if (Input.Keyboard.Pressed(Keys.G))
		{
			if (Input.Keyboard.Shift)
				ToggleGodMode();
			else
				ToggleRenderTargetOverlay();
		}
		if (Input.Keyboard.Pressed(Keys.C)) ToggleDebugView(DebugView.CollisionRects);
		if (Input.Keyboard.Pressed(Keys.N)) ToggleDebugView(DebugView.NormalBuffer);
		if (Input.Keyboard.Pressed(Keys.J)) ToggleAtlasWander();
		if (Input.Keyboard.Pressed(Keys.L)) ToggleDebugCenterCamera();
		if (Input.Keyboard.Pressed(Keys.U)) PerfToggles.RevealOne(this, !Input.Keyboard.Shift);

		// Cycle OutLoud test lines through Bud to dial in reveal rate / pitch / emotion.
		if (Input.Keyboard.Pressed(Keys.V) && VoiceEnabled) SpeakVoiceTest();
		int timeScaleChange = 0;
		if (Input.Keyboard.Pressed(Keys.Minus, Keys.KeypadMinus)) timeScaleChange = -1;
		if (Input.Keyboard.Pressed(Keys.Equals, Keys.KeypadPlus)) timeScaleChange = 1;
		foreach (var controller in Input.Controllers) if (controller.Connected)
		{
			if (controller.Pressed(Buttons.LeftShoulder)) timeScaleChange = -1;
			if (controller.Pressed(Buttons.RightShoulder)) timeScaleChange = 1;
		}

		if (timeScaleChange != 0)
		{
			ChangeTimeScale(timeScaleChange);
		}
#endif
	}

	// Locks the camera dead-centre on the player, ignoring room bounds/blockers —
	// for recording clean player-focused clips. See UpdateCamera.
	internal bool DebugCenterCamera { get; private set; }

	void ToggleDebugCenterCamera()
	{
		DebugCenterCamera = !DebugCenterCamera;
		DisplayMessage($"Center Camera: {(DebugCenterCamera ? "on" : "off")}");
	}

	internal bool AtlasWander { get; private set; }

	void ToggleAtlasWander()
	{
		AtlasWander = !AtlasWander;
		if (!AtlasWander)
			View?.ResetAtlasWander();
		DisplayMessage($"Atlas Wander: {(AtlasWander ? "On" : "Off")}");
	}

#if DEBUG
	int voiceTestIndex;
	// English subtitles; the voice plays back as computerized German syllables.
	static readonly (string Text, OutLoud.Emotion Emotion)[] VoiceTestLines =
	[
		("Hey, can you hear me?", OutLoud.Emotion.Neutral),
		("Look out, something's waiting up ahead!", OutLoud.Emotion.Excited),
		("I've gotten so tired.", OutLoud.Emotion.Sad),
		("Too bad, that could have gone better.", OutLoud.Emotion.Disappointed),
		("So, where do we go next?", OutLoud.Emotion.Neutral),
	];

	void SpeakVoiceTest()
	{
		if (GetFirst<Bud>() is not { } bud)
			return;
		storySpeechBubbles.Clear(); // bypass the one-bubble gate so we can cycle quickly
		var line = VoiceTestLines[voiceTestIndex++ % VoiceTestLines.Length];
		bud.Say(line.Text, line.Emotion);
	}
#endif

	internal PlayerRewinder rewinder;
	internal bool Rewinding => rewinder.Rewinding;
	internal DebugView debugView;
	PlatformNavGraph platformNavGraph = new();
	Room? platformNavGraphRoom;
	bool platformNavGraphPristine = true;
	public bool IsPlatformNavDebugVisible => debugView == DebugView.PlatformNav;
	internal bool IsCollisionDebugVisible => debugView == DebugView.CollisionRects;

	public List<RectInt> CollisionRectsThisFrame = [];

	// Render-target inspector overlay, drawn by Manager as an ImGui window at full
	// window resolution. Toggled with G.
	public bool ShowRenderTargetOverlay { get; set; }

	internal void ToggleRenderTargetOverlay()
	{
		ShowRenderTargetOverlay = !ShowRenderTargetOverlay;
		DisplayMessage($"Debug: Graphics Inspector {(ShowRenderTargetOverlay ? "On" : "Off")}");
	}


	internal void ToggleGodMode() {
		Manager.GodMode = !Manager.GodMode;
		DisplayMessage($"God Mode {(Manager.GodMode ? "On" : "Off")}");
	}

	// Dump a `radius`-cell solid/air map around `center` (world pixel coords) for diagnosing
	// where the boundary walk stalls. '#' solid, '.' air, 'X' the center cell.
	public string DebugSolidGrid(Point2 center, int radius)
	{
		var sb = new System.Text.StringBuilder();
		for (var dy = -radius; dy <= radius; dy++)
		{
			for (var dx = -radius; dx <= radius; dx++)
			{
				var cell = new Point2(center.X + dx, center.Y + dy);
				sb.Append(dx == 0 && dy == 0 ? 'X' : SolidPixel(cell) ? '#' : '.');
			}
			sb.Append('\n');
		}
		return sb.ToString();
	}

	internal void ToggleDebugView(DebugView toggle)
	{
		SetDebugView(debugView == toggle ? DebugView.None : toggle);
	}

	void CycleTileFieldDebugView()
	{
		SetDebugView(debugView switch
		{
			DebugView.TileFieldRed => DebugView.TileFieldGreen,
			DebugView.TileFieldGreen => DebugView.None,
			_ => DebugView.TileFieldRed
		});
	}

	void SetDebugView(DebugView view)
	{
		debugView = view;
		Manager.PlatformNavDebugVisible = view == DebugView.PlatformNav;
		DisplayMessage($"Debug: {DebugViewName(view)}");
	}

	void RestorePersistentDebugViews()
	{
		if (Manager.PlatformNavDebugVisible)
			debugView = DebugView.PlatformNav;
	}

	// Forwards to the Manager, which owns the message so it survives Game recreation.
	public void DisplayMessage(string message, float duration = 1.85f, bool print = true)
		=> Manager.DisplayMessage(message, duration, print);

	// Show(x) displays "x: <value>" using the source text of the argument, so
	// Game.Show(Velocity) reads as "Velocity: <1, 2>". The expr parameters are
	// filled in by the compiler at each call site (CallerArgumentExpression);
	// each named value needs its own parameter, hence the arity overloads.
	public void Show<T>(T value,
		[CallerArgumentExpression(nameof(value))] string expr = "")
		=> DisplayMessage($"{expr}: {value}", print: false);

	public void Show<T1, T2>(T1 a, T2 b,
		[CallerArgumentExpression(nameof(a))] string exprA = "",
		[CallerArgumentExpression(nameof(b))] string exprB = "")
		=> DisplayMessage($"{exprA}: {a}   {exprB}: {b}", print: false);

	public void Show<T1, T2, T3>(T1 a, T2 b, T3 c,
		[CallerArgumentExpression(nameof(a))] string exprA = "",
		[CallerArgumentExpression(nameof(b))] string exprB = "",
		[CallerArgumentExpression(nameof(c))] string exprC = "")
		=> DisplayMessage($"{exprA}: {a}   {exprB}: {b}   {exprC}: {c}", print: false);

	static string DebugViewName(DebugView view)
		=> view switch
		{
			DebugView.None => "Off",
			DebugView.CollisionRects => "Collision Rects",
			DebugView.LightMask => "Light Mask",
			DebugView.NormalBuffer => "Normal Buffer",
			DebugView.TileFieldRed => "Tile Field R: Hash",
			DebugView.TileFieldGreen => "Tile Field G: Family",
			DebugView.DitherMask => "Dither Mask",
			DebugView.Reflection => "Reflection Screen",
			DebugView.ShadowSegments => "GPU Shadow Segments",
			DebugView.PlatformNav => "Platform Nav",
			_ => view.ToString()
		};

	// Hold the per-frame nav build off while a room camera slide is animating (and briefly
	// after, while it settles) so the budgeted build doesn't compete with the slide and
	// hitch it. Set by Manager from the editor camera tween; gameplay transitions are
	// detected from nextRoom + this settle timer.
#if EDITOR
	internal bool navBuildSuppressedByHost;
#else
	internal const bool navBuildSuppressedByHost = false;
#endif
	const float NavBuildSettleSeconds = 0.3f;
	float navBuildSettleTimer;

	public PlatformNavGraph? GetPlatformNavGraph()
	{
		if (room == null)
			return null;

		// Unthrottled (gym) builds the whole graph in one tick so it becomes ready on a
		// deterministic frame, and never defers — its determinism must not depend on
		// camera/slide state.
		var unthrottled = Manager.Unthrottled;

		if (platformNavGraphRoom != room)
		{
			platformNavGraph.Rebuild(this, room);
			platformNavGraphRoom = room;
			navBuildSettleTimer = unthrottled ? 0f : NavBuildSettleSeconds;
		}

		// While a room transition is sliding or the post-slide camera is still settling,
		// skip the budgeted build (return the existing graph, or null if none yet).
		if (!unthrottled && (nextRoom != null || navBuildSuppressedByHost || navBuildSettleTimer > 0f))
		{
			if (nextRoom == null && !navBuildSuppressedByHost && navBuildSettleTimer > 0f)
				navBuildSettleTimer = MathF.Max(0f, navBuildSettleTimer - Time.Delta);
			return platformNavGraph.IsReady ? platformNavGraph : null;
		}

		platformNavGraph.Tick(unthrottled ? float.MaxValue : 2.0f);
		return platformNavGraph.IsReady ? platformNavGraph : null;
	}

	internal PlatformNavGraph? ReadyPlatformNavGraph
		=> platformNavGraphPristine && platformNavGraph.IsReady ? platformNavGraph : null;

	internal void ReusePlatformNavGraph(PlatformNavGraph graph)
	{
		platformNavGraph = graph;
		platformNavGraphRoom = room;
		platformNavGraphPristine = true;
		graph.ResetPathStats();
	}

	void OnRoomGeoChanged(RoomGeoChange change)
	{
		RoomGeoChanged?.Invoke(change);
		if (change.Room != room)
			return;

		platformNavGraphPristine = false;
		if (platformNavGraphRoom == room)
		{
			platformNavGraph.Rebuild(this, room, change.DirtyWorldBounds);
			navBuildSettleTimer = 0;
		}
	}

	void ResetPlatformNavGraphRoomState()
		=> platformNavGraphPristine = true;

	public bool TryGetBudDebugOverlay(out Vector2 worldPosition, out string label)
	{
		if (debugView == DebugView.PlatformNav && GetFirst<Bud>() is { } bud)
		{
			worldPosition = bud.Position + new Vector2(0, -22);
			label = bud.DebugNavLabel;
			return true;
		}

		worldPosition = default;
		label = string.Empty;
		return false;
	}

}
