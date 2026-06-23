using System.Numerics;
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
		Visibility,
		PlatformNav
	}

	internal DebugView debugView;
	private float debugViewMessageTimer;
	public string? DebugViewMessage { get; private set; }
	private readonly PlatformNavGraph platformNavGraph = new();
	private Room? platformNavGraphRoom;
	public bool IsPlatformNavDebugVisible => debugView == DebugView.PlatformNav;

	public List<RectInt> CollisionRectsThisFrame = [];

	// Render-target inspector overlay, drawn by Manager as an ImGui window at full
	// window resolution. Toggled with G.
	public bool ShowRenderTargetOverlay { get; set; }

	internal void ToggleRenderTargetOverlay()
	{
		ShowRenderTargetOverlay = !ShowRenderTargetOverlay;
		DisplayMessage($"Debug: Render Targets {(ShowRenderTargetOverlay ? "On" : "Off")}");
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

	private void CycleTileFieldDebugView()
	{
		SetDebugView(debugView switch
		{
			DebugView.TileFieldRed => DebugView.TileFieldGreen,
			DebugView.TileFieldGreen => DebugView.None,
			_ => DebugView.TileFieldRed
		});
	}

	private void SetDebugView(DebugView view)
	{
		debugView = view;
		Manager.PlatformNavDebugVisible = view == DebugView.PlatformNav;
		DisplayMessage($"Debug: {DebugViewName(view)}");
	}

	private void RestorePersistentDebugViews()
	{
		if (Manager.PlatformNavDebugVisible)
			debugView = DebugView.PlatformNav;
	}

	public void DisplayMessage(string message, float duration = 1.25f)
	{
		DebugViewMessage = message;
		debugViewMessageTimer = duration;
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
			DebugView.DitherMask => "Dither Mask",
			DebugView.Reflection => "Reflection Screen",
			DebugView.Visibility => "Visibility Polygon",
			DebugView.PlatformNav => "Platform Nav",
			_ => view.ToString()
		};

	// Hold the per-frame nav build off while a room camera slide is animating (and briefly
	// after, while it settles) so the budgeted build doesn't compete with the slide and
	// hitch it. Set by Manager from the editor camera tween; gameplay transitions are
	// detected from nextRoom + this settle timer.
	internal bool navBuildSuppressedByHost;
	private const float NavBuildSettleSeconds = 0.3f;
	private float navBuildSettleTimer;

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
