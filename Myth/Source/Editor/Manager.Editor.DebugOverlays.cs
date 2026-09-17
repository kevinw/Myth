#if EDITOR
using LinkDotNet.StringBuilder;
using System.Numerics;
using Foster.Framework;
using ImGuiNET;

namespace Myth;

// Debug overlays drawn over the running game: the FPS/perf readout, Bud's state label, and the
// platform-nav graph. All of them project game-world positions into ImGui screen space through
// GameWorldToImGui, so they line up with the game view at any zoom.
public partial class Manager
{
	float debugOverlayTopBottom;

	static float DebugOverlayTopMargin => 32;
	static float DebugOverlayRowGap => 4;

	void ResetDebugOverlayLayout()
	{
		debugOverlayTopBottom = DebugOverlayTopMargin;
	}

	Vector2 AllocateDebugOverlayTopCenter(Rect viewport, float height)
	{
		var bottom = debugOverlayTopBottom;
		debugOverlayTopBottom += height + DebugOverlayRowGap;
		return viewport.TopLeft + new Vector2(viewport.Width * 0.5f, bottom);
	}

	void ActorPositionDebugOverlayImGui()
	{
		if (game == null || !playing || !game.IsCollisionDebugVisible)
			return;

		var viewport = GameViewRect;
		var draw = ImGui.GetForegroundDrawList();
		var color = ImGuiColor(1.0f, 0.72f, 0.24f, 1.0f);
		var label = new ValueStringBuilder(stackalloc char[96]);
		draw.PushClipRect(viewport.TopLeft, viewport.BottomRight, true);
		foreach (var actor in game.Actors)
		{
			if (!actor.Tags.Has(ActorTag.Enemy) || game.destroying.Contains(actor) || actor.Room is not { } room)
				continue;

			var position = GameWorldToImGui(viewport, game, actor.Position);
			var local = actor.Position - room.WorldBounds.TopLeft;
			label.Clear();
			label.Append(actor.GetType().Name);
			label.Append("  (");
			label.Append(local.X);
			label.Append(", ");
			label.Append(local.Y);
			label.Append(')');
			draw.AddLine(position - new Vector2(4, 0), position + new Vector2(4, 0), color, 1.5f);
			draw.AddLine(position - new Vector2(0, 4), position + new Vector2(0, 4), color, 1.5f);
			draw.AddText(position + new Vector2(5, -15), color, label.ToString());
		}
		draw.PopClipRect();
		label.Dispose();
	}

	void BudDebugOverlayImGui()
	{
		if (game == null || !playing || !game.TryGetBudDebugOverlay(out var worldPosition, out var label))
			return;

		var viewport = GameViewRect;
		var screenPosition = GameWorldToImGui(viewport, game, worldPosition);
		DebugOverlayLabelImGui("BudDebugOverlay", screenPosition, label, viewport);
	}

	static void DebugOverlayLabelImGui(string id, Vector2 screenPosition, ReadOnlySpan<char> label, Rect viewport)
	{
		var textSize = ImGui.CalcTextSize(label) * 0.82f;
		var padding = new Vector2(4, 2);
		var size = textSize + padding * 2;
		var position = new Vector2(screenPosition.X - size.X * 0.5f, screenPosition.Y - size.Y);
		position.X = Math.Clamp(position.X, viewport.Left + 4, viewport.Right - size.X - 4);
		position.Y = Math.Clamp(position.Y, viewport.Top + 4, viewport.Bottom - size.Y - 4);

		ImGui.SetNextWindowPos(position, ImGuiCond.Always);
		ImGui.SetNextWindowSize(size, ImGuiCond.Always);
		ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, padding);
		ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 1);
		ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0, 0, 0, 0.72f));
		ImGui.Begin(
			id,
			ImGuiWindowFlags.NoDecoration |
			ImGuiWindowFlags.NoInputs |
			ImGuiWindowFlags.NoSavedSettings |
			ImGuiWindowFlags.NoFocusOnAppearing |
			ImGuiWindowFlags.NoNav);
		ImGui.SetWindowFontScale(0.82f);
		ImGui.TextUnformatted(label);
		ImGui.SetWindowFontScale(1.0f);
		ImGui.End();
		ImGui.PopStyleColor();
		ImGui.PopStyleVar(2);
	}

	void PlatformNavDebugOverlayImGui()
	{
		if (game == null || !playing || !game.IsPlatformNavDebugVisible)
			return;
		var graph = game.GetPlatformNavGraph();
		if (graph == null)
			return;

		var viewport = GameViewRect;
		var draw = ImGui.GetBackgroundDrawList();
		var hoveredNode = HoveredPlatformNavNode(viewport, game, graph);
		draw.PushClipRect(viewport.TopLeft, viewport.BottomRight, true);

		foreach (var edge in graph.Edges)
		{
			var color = edge.Kind switch
			{
				PlatformNavGraph.EdgeKind.Jump when edge.Jump is { WallJump: true } => ImGuiColor(1.00f, 0.42f, 0.96f, 0.36f),
				PlatformNavGraph.EdgeKind.Jump => ImGuiColor(1.00f, 0.95f, 0.66f, 0.28f),
				PlatformNavGraph.EdgeKind.Fall => ImGuiColor(1.00f, 0.47f, 0.66f, 0.22f),
				PlatformNavGraph.EdgeKind.RoomExit => ImGuiColor(0.72f, 0.55f, 1.00f, 0.45f),
				PlatformNavGraph.EdgeKind.Swim => ImGuiColor(0.30f, 0.62f, 1.00f, 0.55f),
				PlatformNavGraph.EdgeKind.Rope => ImGuiColor(0.71f, 0.48f, 0.25f, 0.75f),
				PlatformNavGraph.EdgeKind.MovingPlatform => ImGuiColor(0.35f, 1.00f, 0.75f, 0.75f),
				_ => ImGuiColor(0.37f, 0.80f, 0.89f, 0.18f)
			};
			DrawPlatformNavEdge(draw, viewport, game, graph, edge, color, edge.Kind == PlatformNavGraph.EdgeKind.RoomExit ? 3.0f : 2.0f);
		}

		if (game.GetFirst<Bud>() is { } bud)
			DrawBudPlatformNavOverlay(draw, viewport, game, graph, bud);

		foreach (var span in graph.SurfaceSpans)
			DrawPlatformNavSurfaceSpan(draw, viewport, game, graph, span);

		foreach (var node in graph.Nodes)
		{
			var color = node.Kind switch
			{
				PlatformNavGraph.NodeKind.Ledge => ImGuiColor(1.00f, 0.83f, 0.48f, 0.78f),
				PlatformNavGraph.NodeKind.RoomExit => ImGuiColor(0.72f, 0.55f, 1.00f, 0.90f),
				PlatformNavGraph.NodeKind.Swim => ImGuiColor(0.30f, 0.62f, 1.00f, 0.85f),
				_ => ImGuiColor(0.65f, 0.94f, 0.44f, 0.62f)
			};
			// A swim node is a volume, not a point: outline its water area so it reads
			// as the swimmable region rather than a stray dot in mid-air.
			if (node.Kind == PlatformNavGraph.NodeKind.Swim && node.Area is { } area)
			{
				var tl = GameWorldToImGui(viewport, game, new Vector2(area.X, area.Y));
				var br = GameWorldToImGui(viewport, game, new Vector2(area.X + area.Width, area.Y + area.Height));
				draw.AddRectFilled(tl, br, ImGuiColor(0.30f, 0.62f, 1.00f, 0.16f));
				draw.AddRect(tl, br, color, 0, ImDrawFlags.None, 2.0f);
			}
			var radius = node.Kind == PlatformNavGraph.NodeKind.RoomExit ? 7.0f : 4.0f;
			draw.AddCircleFilled(GameWorldToImGui(viewport, game, node.Position), radius, color, 8);
		}

		if (hoveredNode is { } hovered)
			draw.AddCircle(GameWorldToImGui(viewport, game, hovered.Position), 10, ImGuiColor(1, 1, 1, 0.95f), 12, 2.5f);
		draw.PopClipRect();

		if (hoveredNode is { } tooltipNode)
			PlatformNavNodeTooltipImGui(viewport, game, graph, tooltipNode);
	}

	static PlatformNavGraph.Node? HoveredPlatformNavNode(Rect viewport, Game game, PlatformNavGraph graph)
	{
		var mouse = ImGui.GetMousePos();
		if (!viewport.Contains(mouse))
			return null;

		const float hoverRadius = 10;
		var nearestDistanceSquared = hoverRadius * hoverRadius;
		PlatformNavGraph.Node? nearest = null;
		foreach (var node in graph.Nodes)
		{
			var distanceSquared = Vector2.DistanceSquared(mouse, GameWorldToImGui(viewport, game, node.Position));
			if (node.Kind == PlatformNavGraph.NodeKind.Swim && node.Area is { } area)
			{
				var topLeft = GameWorldToImGui(viewport, game, area.TopLeft);
				var bottomRight = GameWorldToImGui(viewport, game, area.BottomRight);
				if (Rect.Between(topLeft, bottomRight).Contains(mouse))
					distanceSquared = 0;
			}
			if (distanceSquared > nearestDistanceSquared)
				continue;

			nearest = node;
			nearestDistanceSquared = distanceSquared;
		}
		return nearest;
	}

	static void PlatformNavNodeTooltipImGui(Rect viewport, Game game, PlatformNavGraph graph, PlatformNavGraph.Node node)
	{
		var label = new ValueStringBuilder(stackalloc char[512]);
		label.Append("Node ");
		label.Append(node.Id);
		label.Append("  ");
		label.Append(node.Kind.ToString());
		if (node.RequiresDucking)
			label.Append("  ducking");

		var local = (node.Position - (game.CurrentRoom?.WorldBounds.TopLeft ?? Point2.Zero)).RoundToPoint2();
		label.Append("\nlocal (");
		label.Append(local.X);
		label.Append(", ");
		label.Append(local.Y);
		label.Append(')');

		if (graph.TryGetNodeSurfaceSpan(node.Id, out var span))
		{
			var roomLeft = game.CurrentRoom?.WorldBounds.Left ?? 0;
			var roomTop = game.CurrentRoom?.WorldBounds.Top ?? 0;
			label.Append("  span ");
			label.Append(span.Id);
			label.Append(" [");
			label.Append(span.Left - roomLeft);
			label.Append("..");
			label.Append(span.Right - roomLeft);
			label.Append("] y ");
			label.Append(span.Top - roomTop);
			if (span.IsSloped)
			{
				label.Append("..");
				label.Append(span.Bottom - roomTop);
			}
		}

		if (node.Area is { } area)
		{
			label.Append("\narea ");
			label.Append(area.Width);
			label.Append('x');
			label.Append(area.Height);
		}

		if (node.Exit is { } exit)
		{
			label.Append("\nexit ");
			label.Append(exit.Kind == PlatformNavGraph.RoomExitKind.Edge ? "edge" : "door");
			if (!string.IsNullOrEmpty(exit.TargetRoomId))
			{
				label.Append(" -> ");
				label.Append(exit.TargetRoomId);
			}
			label.Append(" dir ");
			label.Append(exit.Direction.X);
			label.Append(',');
			label.Append(exit.Direction.Y);
		}

		var incoming = 0;
		var outgoing = 0;
		Span<int> outgoingKinds = stackalloc int[(int)PlatformNavGraph.EdgeKind.MovingPlatform + 1];
		foreach (var edge in graph.Edges)
		{
			if (edge.To == node.Id)
				incoming++;
			if (edge.From == node.Id)
			{
				outgoing++;
				outgoingKinds[(int)edge.Kind]++;
			}
		}
		label.Append("\nedges in ");
		label.Append(incoming);
		label.Append("  out ");
		label.Append(outgoing);
		for (var i = 0; i < outgoingKinds.Length; i++)
		{
			if (outgoingKinds[i] <= 0)
				continue;
			label.Append("  ");
			label.Append(((PlatformNavGraph.EdgeKind)i).ToString());
			label.Append(' ');
			label.Append(outgoingKinds[i]);
		}

		var position = GameWorldToImGui(viewport, game, node.Position) - new Vector2(0, 12);
		DebugOverlayLabelImGui("PlatformNavNodeTooltip", position, label.AsSpan(), viewport);
		label.Dispose();
	}

	void DrawBudPlatformNavOverlay(ImDrawListPtr draw, Rect viewport, Game game, PlatformNavGraph graph, Bud bud)
	{
		foreach (var edge in bud.DebugSurfaceNavPath)
			DrawPlatformNavSurfaceEdge(draw, viewport, game, graph, edge, ImGuiColor(0.20f, 0.92f, 1.00f, 0.62f), 5.0f);

		foreach (var edge in bud.DebugNavPath)
		{
			var color = edge.Kind switch
			{
				PlatformNavGraph.EdgeKind.Jump when edge.Jump is { WallJump: true } => ImGuiColor(1.00f, 0.42f, 0.96f, 0.98f),
				PlatformNavGraph.EdgeKind.Jump => ImGuiColor(1.00f, 0.95f, 0.66f, 0.95f),
				PlatformNavGraph.EdgeKind.Rope => ImGuiColor(0.92f, 0.66f, 0.34f, 0.98f),
				PlatformNavGraph.EdgeKind.MovingPlatform => ImGuiColor(0.35f, 1.00f, 0.75f, 0.98f),
				_ => ImGuiColor(1.00f, 1.00f, 1.00f, 0.78f)
			};
			DrawPlatformNavEdge(draw, viewport, game, graph, edge, color, edge.Kind == PlatformNavGraph.EdgeKind.Jump ? 4.0f : 5.0f);
		}

		DrawPlatformNavNodeRing(draw, viewport, game, graph, bud.DebugNavStartNodeId, ImGuiColor(0.37f, 0.80f, 0.89f, 0.95f), 14.0f);
		DrawPlatformNavNodeRing(draw, viewport, game, graph, bud.DebugNavGoalNodeId, ImGuiColor(1.00f, 0.47f, 0.66f, 0.95f), 14.0f);
		DrawPlatformNavNodeRing(draw, viewport, game, graph, bud.DebugNavTargetNodeId, ImGuiColor(1.00f, 1.00f, 1.00f, 0.95f), 16.0f);

		var from = GameWorldToImGui(viewport, game, bud.Position);
		var to = GameWorldToImGui(viewport, game, bud.Position + new Vector2(0, -14));
		draw.AddLine(from, to, ImGuiColor(1.00f, 1.00f, 1.00f, 0.55f), 2.0f);

		if (bud.DebugNavProbeRect is { } probe)
		{
			var probeColor = bud.DebugNavProbeHit
				? ImGuiColor(0.00f, 1.00f, 0.00f, 0.50f)
				: ImGuiColor(1.00f, 0.50f, 0.00f, 0.50f);
			var probeTL = GameWorldToImGui(viewport, game, new Vector2(probe.X, probe.Y));
			var probeBR = GameWorldToImGui(viewport, game, new Vector2(probe.X + probe.Width, probe.Y + probe.Height));
			draw.AddRectFilled(probeTL, probeBR, probeColor);
			draw.AddRect(probeTL, probeBR, ImGuiColor(1.00f, 1.00f, 1.00f, 0.80f), 0, ImDrawFlags.None, 3.0f);
		}
	}

	static void DrawPlatformNavEdge(ImDrawListPtr draw, Rect viewport, Game game, PlatformNavGraph graph, PlatformNavGraph.Edge edge, uint color, float thickness)
	{
		if (edge.From < 0 || edge.From >= graph.Nodes.Count || edge.To < 0 || edge.To >= graph.Nodes.Count)
			return;

		var from = graph.Nodes[edge.From].Position;
		var to = graph.Nodes[edge.To].Position;
		if (edge.Kind == PlatformNavGraph.EdgeKind.Jump)
			DrawPlatformNavJump(draw, viewport, game, from, to, color, thickness, edge.Jump is { WallJump: true });
		else
			draw.AddLine(GameWorldToImGui(viewport, game, from), GameWorldToImGui(viewport, game, to), color, thickness);
	}

	static void DrawPlatformNavJump(ImDrawListPtr draw, Rect viewport, Game game, Vector2 from, Vector2 to, uint color, float thickness, bool wallJump = false)
	{
		var control = new Vector2((from.X + to.X) * 0.5f, MathF.Min(from.Y, to.Y) - 24);
		var previous = GameWorldToImGui(viewport, game, from);
		const int segments = 6;
		for (var i = 1; i <= segments; i++)
		{
			var t = i / (float)segments;
			var a = Vector2.Lerp(from, control, t);
			var b = Vector2.Lerp(control, to, t);
			var point = GameWorldToImGui(viewport, game, Vector2.Lerp(a, b, t));
			draw.AddLine(previous, point, color, thickness);
			previous = point;
		}

		if (wallJump)
		{
			var wallMarker = GameWorldToImGui(viewport, game, control);
			draw.AddCircleFilled(wallMarker, thickness + 2.0f, color, 8);
			draw.AddCircle(wallMarker, thickness + 4.0f, ImGuiColor(1.00f, 1.00f, 1.00f, 0.65f), 8, 1.5f);
		}
	}

	static void DrawPlatformNavSurfaceSpan(ImDrawListPtr draw, Rect viewport, Game game, PlatformNavGraph graph, PlatformNavGraph.SurfaceSpan span)
	{
		const int yOffset = -5;
		const int singletonHalfWidth = 5;
		var color = span.RequiresDucking
			? ImGuiColor(0.95f, 0.66f, 1.00f, 0.35f)
			: ImGuiColor(0.18f, 1.00f, 0.82f, 0.35f);
		var offset = new Vector2(0, yOffset);
		if (span.PointCount == 1)
		{
			var point = graph.SurfacePoints[span.PointStart] + offset;
			var from = GameWorldToImGui(viewport, game, point - new Vector2(singletonHalfWidth, 0));
			var to = GameWorldToImGui(viewport, game, point + new Vector2(singletonHalfWidth, 0));
			draw.AddLine(from, to, color, 3.0f);
			return;
		}

		var previous = GameWorldToImGui(viewport, game, graph.SurfacePoints[span.PointStart] + offset);
		for (var i = 1; i < span.PointCount; i++)
		{
			var point = GameWorldToImGui(viewport, game, graph.SurfacePoints[span.PointStart + i] + offset);
			draw.AddLine(previous, point, color, 3.0f);
			previous = point;
		}
	}

	static void DrawPlatformNavSurfaceEdge(ImDrawListPtr draw, Rect viewport, Game game, PlatformNavGraph graph, PlatformNavGraph.SurfaceEdge edge, uint color, float thickness)
	{
		if (edge.From < 0 || edge.From >= graph.SurfaceSpans.Count || edge.To < 0 || edge.To >= graph.SurfaceSpans.Count)
			return;

		var fromSpan = graph.SurfaceSpans[edge.From];
		var toSpan = graph.SurfaceSpans[edge.To];
		var from = graph.SurfacePointAtX(fromSpan, (edge.FromLeft + edge.FromRight) * 0.5f) - new Vector2(0, 9);
		var to = graph.SurfacePointAtX(toSpan, (edge.ToLeft + edge.ToRight) * 0.5f) - new Vector2(0, 9);
		if (edge.Kind == PlatformNavGraph.EdgeKind.Jump)
			DrawPlatformNavJump(draw, viewport, game, from, to, edge.Jump is { WallJump: true } ? ImGuiColor(1.00f, 0.42f, 0.96f, 0.78f) : color, thickness, edge.Jump is { WallJump: true });
		else
			draw.AddLine(GameWorldToImGui(viewport, game, from), GameWorldToImGui(viewport, game, to), color, thickness);
	}

	static void DrawPlatformNavNodeRing(ImDrawListPtr draw, Rect viewport, Game game, PlatformNavGraph graph, int? nodeId, uint color, float radius)
	{
		if (nodeId is not { } id || id < 0 || id >= graph.Nodes.Count)
			return;

		draw.AddCircle(GameWorldToImGui(viewport, game, graph.Nodes[id].Position), radius, color, 10, 4.0f);
	}

	static uint ImGuiColor(float r, float g, float b, float a)
		=> ImGui.ColorConvertFloat4ToU32(new Vector4(r, g, b, a));

	static Vector2 GameWorldToImGui(Rect viewport, Game game, Vector2 worldPosition)
	{
		var scale = MathF.Min(viewport.Width / game.Screen.Width, viewport.Height / game.Screen.Height);
		var size = new Vector2(game.Screen.Width, game.Screen.Height) * scale;
		var topLeft = viewport.Center - size / 2;
		return topLeft + (worldPosition - game.RenderCamera) * game.ZoomLevel * scale;
	}

	static Vector2 GameImGuiToWorld(Rect viewport, Game game, Vector2 screenPosition)
	{
		var scale = MathF.Min(viewport.Width / game.Screen.Width, viewport.Height / game.Screen.Height);
		var size = new Vector2(game.Screen.Width, game.Screen.Height) * scale;
		var topLeft = viewport.Center - size / 2;
		return (screenPosition - topLeft) / (game.ZoomLevel * scale) + game.RenderCamera;
	}

	void FpsOverlayImGui()
	{
		if (!fpsOverlayVisible || fpsOverlayValue <= 0)
			return;

		var viewport = !playing && !startingPlay ? View : GameViewRect;
		var perf = PerfStats.Snapshot;

		var vsb = new ValueStringBuilder(stackalloc char[2048]);
		vsb.Append("FPS ");
		if (fpsOverlayMode == 0) {
			vsb.Append((int)MathF.Round(fpsOverlayValue));
			vsb.Append(" (");
			vsb.Append(MathF.Round(1000.0f / fpsOverlayValue, 2));
			vsb.Append(" ms)");
#if DEBUG
			var gcMs = gcTotalPauseDuration.TotalMilliseconds;

			vsb.Append(" GC ");
			vsb.Append(threadAllocatedBytesOverlayValue);
			vsb.Append(" bytes (");
			if (gcMs > 1000)
			{
				vsb.Append((int)Math.Round(gcTotalPauseDuration.TotalSeconds));
				vsb.Append("s");
			}
			else {
				vsb.Append((int)Math.Round(gcTotalPauseDuration.TotalMilliseconds));
				vsb.Append("ms");
			}
			vsb.Append(")");
#endif
		} else {
			vsb.Append($"FPS {fpsOverlayValue:0}\n" +
			  $"Upd {perf.Update:0.0}  Rnd {perf.Render:0.0}  Prs {perf.Present:0.0}\n" +
			  $"Wld {perf.World:0.0}  Lit {perf.Lighting:0.0}  Fin {perf.FinalComposite:0.0}\n" +
			  $"Nrm {perf.Normal:0.0}  T+D {perf.TileDither:0.0}  Pst {perf.Post:0.0}");
		}

		var detailMessage = fpsOverlayMode >= 2 ? PerfStats.DetailSummary : string.Empty;
		var mainTextSize = ImGui.CalcTextSize(vsb.AsSpan());
		var detailTextSize = string.IsNullOrEmpty(detailMessage) ? Vector2.Zero : ImGui.CalcTextSize(detailMessage) * 0.8f;
		var padding = new Vector2(6, 3);
		var textSize = new Vector2(
			MathF.Max(mainTextSize.X, detailTextSize.X),
			mainTextSize.Y + (detailTextSize.Y > 0 ? detailTextSize.Y + 2 : 0));
		var size = textSize + padding * 2;
		var position = new Vector2(
			viewport.Right - size.X - 6,
			viewport.Top + 6);

		ImGui.SetNextWindowPos(position, ImGuiCond.Always);
		ImGui.SetNextWindowSize(size, ImGuiCond.Always);
		ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, padding);
		ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 1);
		ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0, 0, 0, 0.55f));
		ImGui.Begin(
			"FpsOverlay",
			ImGuiWindowFlags.NoDecoration |
			ImGuiWindowFlags.NoSavedSettings |
			ImGuiWindowFlags.NoFocusOnAppearing |
			ImGuiWindowFlags.NoNav);
		ImGui.TextUnformatted(vsb.AsSpan());
		if (!string.IsNullOrEmpty(detailMessage))
		{
			ImGui.SetWindowFontScale(0.8f);
			ImGui.TextUnformatted(detailMessage);
			ImGui.SetWindowFontScale(1.0f);
		}
		if (ImGui.IsWindowHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
			fpsOverlayMode = (fpsOverlayMode + 1) % 3;
		ImGui.End();
		ImGui.PopStyleColor();
		ImGui.PopStyleVar(2);
	}

#if DEBUG
	int fpsFrames;
	float fpsElapsed;

	void UpdateWindowTitleFps()
	{
		fpsFrames++;
		fpsElapsed += Time.Delta;
		if (fpsElapsed < 1.0f)
			return;

		var fps = fpsFrames / fpsElapsed;
		if (Window is { } window)
		{
			var vsb = new ValueStringBuilder(stackalloc char[256]);
			vsb.Append(APPLICATION_NAME);
			if (Manager.DOTNET_WATCH) {
				vsb.Append(" (watch)");
			}
			vsb.Append(" - ");
			vsb.Append((int)MathF.Round(fps));
			vsb.Append(" FPS");
			// Show the room name when we're in DEBUG
			if (game?.room is { } room) {
				vsb.Append(" - ");
				vsb.Append(room.Id);
			}
			window.Title = vsb.ToString();
		}
		fpsFrames = 0;
		fpsElapsed = 0;
	}

#endif
}
#endif
