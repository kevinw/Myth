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
		ReachField
	}

	// Debug/dev keyboard + controller shortcuts. Host concern: Manager reads input and
	// drives the sim through its public API (moved out of Game.Update for Phase 2).
	// Bindings live in DebugControls, which the '?' help window also reads.
	internal void HandleGameDebugShortcuts(DebugControls debug, Input Input)
	{
		if (!AcceptsLiveInput)
			return;

#if DEBUG
		if (debug.ReloadRoom.Pressed)
			ReloadRoom();
		// TODO: use an ingame console instead?
		if (debug.ReachFieldView.Pressed) ToggleDebugView(DebugView.ReachField);
		if (debug.RenderTargets.Pressed)
		{
			if (Input.Keyboard.Shift)
				ToggleGodMode();
			else
				ToggleRenderTargetOverlay();
		}
		if (debug.CollisionView.Pressed) ToggleDebugView(DebugView.CollisionRects);
		if (debug.NormalsView.Pressed) ToggleDebugView(DebugView.NormalBuffer);
		if (debug.AtlasWander.Pressed) ToggleAtlasWander();
#if EDITOR
		if (debug.ToggleVisuals.Pressed) Manager.ToggleVisuals();
#endif
		if (debug.RenderTargetShake.Pressed)
		{
			if (Input.Keyboard.Shift)
				ToggleRenderTargetShakeMode();
			else
				DebugShakeRenderTarget();
		}
		if (debug.CenterCamera.Pressed) ToggleDebugCenterCamera();
		if (debug.PerfReveal.Pressed) PerfToggles.RevealOne(this, !Input.Keyboard.Shift);

		// Cycle OutLoud test lines through Bud to dial in reveal rate / pitch / emotion.
		if (debug.VoiceTest.Pressed && VoiceEnabled) SpeakVoiceTest();
		int timeScaleChange = 0;
		if (debug.TimeScaleDown.Pressed) timeScaleChange = -1;
		if (debug.TimeScaleUp.Pressed) timeScaleChange = 1;

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

	void ToggleRenderTargetShakeMode()
	{
		ShakeInRenderTargetSpace = !ShakeInRenderTargetSpace;
		DisplayMessage($"Shake Space: {(ShakeInRenderTargetSpace ? "Render Target" : "World")}");
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
	internal readonly ReachSense reach = new(ReachBody.Bud);
	// Bud heads for the floor under a jumping player, if it's this close below.
	static int PlayerFloorReach => 64;
	// Widens the player's rect as Bud's goal, so he stops about 18px off instead of ~7px (bodies just touching).
	// The goal is any spot overlapping it, so where there's no footing that far out he still comes in closer.
	static int BudFollowGap => 11;

	void UpdateReachField()
	{
		if (room == null || debugView != DebugView.ReachField && GetFirst<Bud>() == null)
			return;
		if (GymTarget is { } goal)
			reach.Update(this, room, (goal.Hitbox.Rect + goal.Position).Inflate(1));
		else if (GetFirst<Player>() is { } player)
			reach.Update(this, room, (player.Hitbox.Rect + player.Position).InflateX(BudFollowGap), PlayerFloorReach);
	}
	internal bool IsCollisionDebugVisible => debugView == DebugView.CollisionRects;

	public List<(RectInt, float)> CollisionRectsThisFrame = [];

	internal void AddCollisionRectForThisFrame(RectInt rect)
	{
		CollisionRectsThisFrame.Add((rect, Time.SecondsF));
	}

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
		Manager.ReachDebugVisible = view == DebugView.ReachField;
		DisplayMessage($"Debug: {DebugViewName(view)}");
	}

	void RestorePersistentDebugViews()
	{
		if (Manager.ReachDebugVisible)
			debugView = DebugView.ReachField;
		if (Enum.TryParse<DebugView>(Manager.StartDebugView, ignoreCase: true, out var view))
			debugView = view;
	}

	// Forwards to the Manager, which owns the message so it survives Game recreation.
	public void DisplayMessage(string message, float duration = 1.85f, bool print = false) => Manager.DisplayMessage(message, duration, print);

	// Show(x) displays "x: <value>" using the source text of the argument, so
	// Game.Show(Velocity) reads as "Velocity: <1, 2>". Every call made during the
	// current update gets its own line. The expr parameters are filled in by the
	// compiler at each call site (CallerArgumentExpression); each named value needs
	// its own parameter, hence the arity overloads.
	public void Show<T>(T value,
		[CallerArgumentExpression(nameof(value))] string expr = "")
		=> Manager.ShowDebugValue($"{expr}: {value}");

	public void Show<T1, T2>(T1 a, T2 b,
		[CallerArgumentExpression(nameof(a))] string exprA = "",
		[CallerArgumentExpression(nameof(b))] string exprB = "")
		=> Manager.ShowDebugValue($"{exprA}: {a}   {exprB}: {b}");

	public void Show<T1, T2, T3>(T1 a, T2 b, T3 c,
		[CallerArgumentExpression(nameof(a))] string exprA = "",
		[CallerArgumentExpression(nameof(b))] string exprB = "",
		[CallerArgumentExpression(nameof(c))] string exprC = "")
		=> Manager.ShowDebugValue($"{exprA}: {a}   {exprB}: {b}   {exprC}: {c}");

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
			DebugView.ReachField => "Reach Field",
			_ => view.ToString()
		};

	void OnRoomGeoChanged(RoomGeoChange change)
	{
		RoomGeoChanged?.Invoke(change);
	}

	public bool TryGetBudDebugOverlay(out Vector2 worldPosition, out string label)
	{
		if (debugView == DebugView.ReachField && GetFirst<Bud>() is { } bud)
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
