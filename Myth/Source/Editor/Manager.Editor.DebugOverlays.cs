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

	// Collision-rects view: what a burrow thinks is around it -- the seam it's sealed in (cyan = walking the other
	// side, magenta = following the crusher's outline), the two
	// cells on its open side (red = solid; both red = buried), which of the four steps the walk accepts (white)
	// or refuses (dark red), and arrows for into-surface (red) and heading (green).
	void BurrowDebugOverlayImGui()
	{
		if (game == null || !playing || !game.IsCollisionDebugVisible)
			return;

		var viewport = GameViewRect;
		var draw = ImGui.GetForegroundDrawList();
		var pixel = GameWorldToImGui(viewport, game, Vector2.UnitX).X - GameWorldToImGui(viewport, game, Vector2.Zero).X;
		var label = new ValueStringBuilder(stackalloc char[256]);
		draw.PushClipRect(viewport.TopLeft, viewport.BottomRight, true);
		foreach (var actor in game.Actors)
		{
			if (actor is not Pawn { State: Pawn.States.Burrow } pawn)
				continue;

			ref readonly var burrow = ref pawn.BurrowCursorState;
			var at = pawn.Position;
			Vector2 Screen(Vector2 world) => GameWorldToImGui(viewport, game, world);
			void Box(RectInt rect, uint color, bool fill)
			{
				if (fill)
					draw.AddRectFilled(Screen(rect.TopLeft), Screen(rect.BottomRight), color);
				else
					draw.AddRect(Screen(rect.TopLeft), Screen(rect.BottomRight), color, 0, ImDrawFlags.None, 1.5f);
			}

			if (burrow.Seam is { } seam)
				Box(seam.Hitbox.Rect + seam.Position, burrow.SeamFollow ? ImGuiColor(1, 0.3f, 1, 1) : ImGuiColor(0.3f, 1, 1, 1), false);

			game.Buried(at, burrow.Toward, out var a, out var b);
			foreach (var cell in (ReadOnlySpan<Point2>)[a, b])
				Box(new RectInt(cell, 1, 1), game.BurrowLineSolidAt(cell + new Vector2(0.5f)) ? ImGuiColor(1, 0.2f, 0.2f, 0.7f) : ImGuiColor(1, 1, 1, 0.25f), true);

			var edges = game.BurrowEdges(at, burrow);
			for (var i = 0; i < Game.BurrowDebugDirections.Length; i++)
			{
				var step = (Vector2)Game.BurrowDebugDirections[i];
				draw.AddLine(Screen(at + step * 0.5f), Screen(at + step * 2.5f),
					(edges & (1 << i)) != 0 ? ImGuiColor(1, 1, 1, 1) : ImGuiColor(0.5f, 0, 0, 1), MathF.Max(2, pixel * 0.6f));
			}
			draw.AddLine(Screen(at), Screen(at + burrow.Toward * 5), ImGuiColor(1, 0.2f, 0.2f, 1), 2);
			draw.AddLine(Screen(at), Screen(at + (Vector2)burrow.Heading * 5), ImGuiColor(0.2f, 1, 0.2f, 1), 2);
			draw.AddCircleFilled(Screen(at), MathF.Max(2, pixel * 0.4f), ImGuiColor(1, 1, 1, 1));

			label.Clear();
			label.Append(burrow.Seam != null ? (burrow.SeamFollow ? "seam: follow " : "seam: other side of ")
				: game.ProbeBurrowSeam(at, burrow, pawn.BurrowIntent, Pawn.BurrowTangentDeadzone) switch
				{
					Game.SeamMiss.NotBuried => "no seam: not buried",
					Game.SeamMiss.NoCrusher => "no seam: nothing here is a crusher",
					_ => "no seam: no walkable side",
				});
			if (burrow.Seam != null)
				label.Append(burrow.Seam.ActorId ?? burrow.Seam.GetType().Name);
			label.Append("\ntoward ");
			label.Append(burrow.Toward.X); label.Append(','); label.Append(burrow.Toward.Y);
			label.Append("  heading ");
			label.Append(burrow.Heading.X); label.Append(','); label.Append(burrow.Heading.Y);
			label.Append(burrow.SolidLeft ? "  solid L" : "  solid R");
			label.Append("\nlast ");
			label.Append(pawn.LastBurrowTravel.ToString());
			label.Append(burrow.Blocked ? "  BLOCKED" : "");
			label.Append(burrow.Line != null ? "  on line" : "");
			label.Append(pawn.BurrowSealed ? "  sealed" : "");
			label.Append(!pawn.RoomToPop(out var crouch, out _) ? "  pop: no room" : crouch ? "  pop: crouch" : "  pop: stand");
			label.Append("\ntimer ");
			label.Append(pawn.BurrowTimeLeft, "0.00");
			label.Append("  speed ");
			label.Append(pawn.BurrowSpeed, "0");
			label.Append("  intent ");
			label.Append(pawn.BurrowIntent.X); label.Append(','); label.Append(pawn.BurrowIntent.Y);
			DebugOverlayLabelImGui($"BurrowDebug{pawn.GetHashCode()}", Screen(at - new Vector2(0, 10)), label.AsSpan(), viewport);
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
