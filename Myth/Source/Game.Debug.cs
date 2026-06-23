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

	private void ToggleRenderTargetOverlay()
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

	private void ToggleDebugView(DebugView toggle)
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

	public PlatformNavGraph? GetPlatformNavGraph()
	{
		if (room == null)
			return null;

		if (platformNavGraphRoom != room)
		{
			platformNavGraph.Rebuild(this, room);
			platformNavGraphRoom = room;
		}

		// Unthrottled (gym) runs build the whole graph in one tick so it becomes ready on
		// a deterministic frame; interactive play keeps the small per-frame budget so the
		// rebuild doesn't hitch.
		platformNavGraph.Tick(Manager.Unthrottled ? float.MaxValue : 2.0f);
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
