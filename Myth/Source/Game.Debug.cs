using System.Numerics;
using Foster.Framework;

namespace Myth;

public partial class Game
{
	enum DebugView
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

	private DebugView debugView;
	private float debugViewMessageTimer;
	public string? DebugViewMessage { get; private set; }
	private readonly PlatformNavGraph platformNavGraph = new();
	private Room? platformNavGraphRoom;
	public bool IsPlatformNavDebugVisible => debugView == DebugView.PlatformNav;

	private bool mouseLightEnabled;
	private Vector2 mouseLightPosition;

	// Independent overlay (not part of the exclusive DebugView): draws the point the
	// burrow surface-follower rides, as a 2px rectangle. Toggled with B.
	private bool showBurrowCenter;

	public List<RectInt> CollisionRectsThisFrame = new();

	private void ToggleBurrowCenter()
	{
		showBurrowCenter = !showBurrowCenter;
		DisplayMessage($"Debug: Burrow Center {(showBurrowCenter ? "On" : "Off")}");
	}

	private void DrawBurrowCenter()
	{
		if (GetFirst<Player>() is not { State: Pawn.States.Burrow } player)
			return;

		Batcher.Rect(player.Position.X - 1, player.Position.Y - 1, 2, 2, 0x00ffff);
	}

	public bool ShowBurrowDebug => showBurrowCenter;

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

	private static readonly List<VisibilityPolygon.Hit> debugHits = new();
	private static readonly List<VisibilityPolygon.Segment> debugSegments = new();

	private void DrawVisibilityDebug(Target target)
	{
		if (!TryGetVisibilityDebugLight(out var lightPosition, out var radius))
			return;

		GatherVisibilitySegments(lightPosition, radius, debugSegments);
		VisibilityPolygon.Compute(lightPosition, radius, debugSegments, debugHits);

		Batcher.PushMatrix(-(Point2)Camera + shake);
		Batcher.Circle(lightPosition, radius, 64, Color.Transparent, new Color(0x5fcde4, 0.35f));

		var hits = debugHits;
		for (var i = 0; i < hits.Count; i++)
		{
			var a = hits[i].Point;
			var b = hits[(i + 1) % hits.Count].Point;
			Batcher.Triangle(lightPosition, a, b, new Color(0x5fcde4, 0.08f));
		}

		for (var i = 0; i < debugSegments.Count; i++)
			Batcher.Line(debugSegments[i].A, debugSegments[i].B, 1, VisibilityDebugSegmentColor(i, 0.9f));

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

	private void DrawCollisionRects()
	{
		if (room == null)
			return;

		foreach (var rect in room.CollisionRects)
			Batcher.RectLine(rect, 1, Color.Red);
		foreach (var slope in room.SlopeTiles)
		{
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

	public PlatformNavGraph? GetPlatformNavGraph()
	{
		if (room == null)
			return null;

		if (platformNavGraphRoom != room)
		{
			platformNavGraph.Rebuild(this, room);
			platformNavGraphRoom = room;
		}

		platformNavGraph.Tick(2.0f);
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

	private void UpdateMouseLightPosition(RectInt viewport)
	{
		var scale = Calc.Min(viewport.Width / (float)Screen.Width, viewport.Height / (float)Screen.Height);
		var size = new Vector2(Screen.Width, Screen.Height) * scale;
		var topLeft = (Vector2)viewport.Center - size / 2;
		var local = (Input.Mouse.Position - topLeft) / scale;
		mouseLightPosition = RenderCamera + local;
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
		blurMaterial?.Reload();
		softShadowMaterial?.Reload();
		sdfAuraMaterial?.Reload();
	}
#endif
}
