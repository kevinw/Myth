#if EDITOR
using System.Numerics;
using Foster.Framework;
using ImGuiNET;

namespace Myth;

// The editor's docking regions: the palette on the left, the tool windows (Tile Creator, NCT
// Tiles, Particles, Music, SFX, Preferences) on the right. Both are real dock spaces -- drag a
// window out to float it, drop it on the other side, or tab windows together, and ImGui keeps
// the arrangement in imgui.ini. The game view is sized to what is left between them, so a panel
// never covers the room you are painting.
//
// ImGui.NET 1.91.6 does not bind the DockBuilder API, so a default layout cannot be built in
// code: SetNextWindowDockID is what homes a window, and it only applies on first use unless a
// layout reset forces it (see DockNextWindow).
public partial class Manager
{
	const float EditorDockMinWidth = 160;
	const float EditorDockMaxWidth = 900;
	static float EditorDockSplitterWidth => 6;

	/// A node holding one window drops its tab bar and shows only the small corner triangle,
	/// which opens the dock menu. Panels keep tabs as soon as two share a node.
	const ImGuiDockNodeFlags EditorDockNodeFlags = ImGuiDockNodeFlags.AutoHideTabBar;

	float editorDockWidth = DefaultToolDockWidth;
	static float DefaultToolDockWidth => 460;
	float editorPaletteWidth = DefaultPaletteDockWidth;

	/// The old fixed palette was PaletteWidth of pure content; a docked window spends some of
	/// its width on padding, border and scrollbar, so the default adds that back to keep the
	/// same five icons per row.
	static float DefaultPaletteDockWidth => PaletteWidth + 34;
	uint editorDockId;
	uint editorPaletteDockId;
	uint editorCenterDockId;

	// The Game window's content rect, read while it is submitted and used to draw the world
	// later in the same frame -- ImGui lays out during Update and renders after the world, so
	// routing the view through a dock window costs no extra frame of latency.
	Rect gameViewWindowRect;
	bool gameViewHovered;

	/// Bump to re-home every panel on the next launch (see EditorDockLayoutVersion in the window
	/// settings): imgui.ini already holds a position for every window that has ever been opened,
	/// and FirstUseEver never overrides that.
	public const int EditorDockLayoutVersion = 3;

	/// Windows forced into a dock since the last layout reset. Each is re-homed once, the next
	/// time it is submitted, so windows closed during the reset still land when next opened.
	readonly HashSet<string> editorDockForced = [];
	int editorDockResetFrames;
	bool pendingEditorDockLayoutReset;

	bool EditorDockResetting => editorDockResetFrames > 0;

	// Which regions actually held a window last frame. A region that is merely "open" is not
	// enough: dock every tool window on the left and the right region must give its width back
	// to the game view instead of leaving a gap. Counted from the previous frame because a
	// window reports its dock id while being submitted, after the region has been sized.
	int paletteDockedLastFrame;
	int toolDockedLastFrame;
	int paletteDockedThisFrame;
	int toolDockedThisFrame;

	/// The region takes space while it holds a window -- plus while its id is still unknown or a
	/// reset is running, so there is always somewhere for windows to land.
	bool EditorDockVisible
		=> !playing && !startingPlay &&
			(toolDockedLastFrame > 0 || EditorDockResetting || editorDockId == 0);

	bool EditorPaletteDockVisible
		=> gameEase < 1.0f && !startingPlay &&
			(paletteDockedLastFrame > 0 || EditorDockResetting || editorPaletteDockId == 0);

	/// Called by each dockable window just after its Begin, so the regions know what they hold.
	/// Matched by geometry rather than dock id: a docked window reports the leaf node it landed
	/// in, which is not the root dockspace id these regions create.
	void NoteDockedWindow()
	{
		if (!ImGui.IsWindowDocked())
			return;

		var center = ImGui.GetWindowPos() + ImGui.GetWindowSize() / 2;
		if (EditorPaletteDockRect.Contains(center))
			paletteDockedThisFrame++;
		else if (EditorDockRect.Contains(center))
			toolDockedThisFrame++;
	}

	float EditorDockReservedWidth => EditorDockVisible ? editorDockWidth + BoxSpacing : 0;
	float EditorPaletteReservedWidth => EditorPaletteDockVisible ? editorPaletteWidth + BoxSpacing : 0;

	Rect EditorDockRect => new(WorkRect.Right - editorDockWidth, WorkRect.Y, editorDockWidth, WorkRect.Height);

	/// Whatever the side regions leave: the home of the game view.
	Rect EditorCenterRect => new(
		WorkRect.X + EditorPaletteReservedWidth,
		WorkRect.Y,
		Math.Max(1, WorkRect.Width - EditorPaletteReservedWidth - EditorDockReservedWidth),
		WorkRect.Height);

	/// The palette region, eased off the left edge as the game opens (what PaletteSlideRect used
	/// to do for the palette window itself; a docked window rides along with its host).
	Rect EditorPaletteDockRect
	{
		get
		{
			var open = new Rect(WorkRect.X, WorkRect.Y, editorPaletteWidth, WorkRect.Height);
			return open - Vector2.UnitX * Ease.Cube.InOut(gameEase) * open.Right;
		}
	}

	/// Homes the next window the first time it is ever opened. Later positions come from
	/// imgui.ini, so a window the user tore off stays torn off -- until a layout reset.
	void DockNextWindow(string name, uint dockId)
	{
		if (dockId == 0)
			return;

		var force = EditorDockResetting && editorDockForced.Add(name);
		ImGui.SetNextWindowDockID(dockId, force ? ImGuiCond.Always : ImGuiCond.FirstUseEver);
	}

	void DockNextToolWindow(string name)
		=> DockNextWindow(name, editorDockId);

	/// Pulls every panel back to its default side, including windows that are closed right now
	/// (they are re-homed when next opened).
	public void ResetEditorToolWindowLayout()
	{
		editorDockForced.Clear();
		// Two frames is enough for every window opened below to be submitted once; anything
		// still closed keeps its old position rather than being yanked around later.
		editorDockResetFrames = 2;
		tileKitEditorOpen = true;
		nctEditorOpen = true;
		musicRegionsOpen = true;
		OpenAllEditorPanels();
	}

	/// Must run before any dockable window is submitted: a dockspace has to exist in the frame
	/// before anything can dock into it.
	void EditorDockImGui()
	{
		if (editorDockResetFrames > 0)
			editorDockResetFrames--;

		paletteDockedLastFrame = paletteDockedThisFrame;
		toolDockedLastFrame = toolDockedThisFrame;
		paletteDockedThisFrame = 0;
		toolDockedThisFrame = 0;

		if (EditorPaletteDockVisible)
			EditorDockHostImGui("##EditorPaletteDockHost", "EditorPaletteDock", EditorPaletteDockRect, ref editorPaletteDockId, rightSide: false);
		if (EditorDockVisible)
			EditorDockHostImGui("##EditorDockHost", "EditorDock", EditorDockRect, ref editorDockId, rightSide: true);
		if (EditorPaletteDockVisible || EditorDockVisible || gameEase < 1.0f)
			EditorCenterDockImGui();
	}

	/// The game view's own dock region. It has no splitter: it simply takes what the side
	/// regions leave, so dragging either side resizes the view.
	void EditorCenterDockImGui()
	{
		var rect = EditorCenterRect;
		var flags =
			ImGuiWindowFlags.NoTitleBar |
			ImGuiWindowFlags.NoMove |
			ImGuiWindowFlags.NoResize |
			ImGuiWindowFlags.NoCollapse |
			ImGuiWindowFlags.NoSavedSettings |
			ImGuiWindowFlags.NoBringToFrontOnFocus |
			ImGuiWindowFlags.NoBackground |
			ImGuiWindowFlags.NoDocking;

		ImGui.SetNextWindowPos(rect.TopLeft, ImGuiCond.Always);
		ImGui.SetNextWindowSize(rect.Size, ImGuiCond.Always);
		// NoBackground is ignored on a dockspace host window: it still fills with WindowBg
		// (ImGui's default is near-black at alpha 0.94), which lands straight over the room.
		// Verified from the draw data -- this quad was rgba(15,15,15,240) covering the game rect.
		ImGui.PushStyleColor(ImGuiCol.WindowBg, 0u);
		ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
		if (ImGui.Begin("##EditorCenterHost", flags))
		{
			editorCenterDockId = ImGui.GetID("EditorCenterDock");
			ImGui.PushStyleColor(ImGuiCol.DockingEmptyBg, 0u);
			ImGui.DockSpace(editorCenterDockId, Vector2.Zero, EditorDockNodeFlags | ImGuiDockNodeFlags.PassthruCentralNode);
			ImGui.PopStyleColor();
		}
		ImGui.End();
		ImGui.PopStyleVar();
		ImGui.PopStyleColor();

		GameViewImGui();
	}

	/// The game view as a real dock window. It draws nothing itself: the world is rendered into
	/// its rect by the existing editor/game path, and the window only contributes layout plus
	/// the "is the mouse over the world" answer. AutoHideTabBar keeps it chrome-free while it is
	/// alone in its node -- just the corner triangle for the dock menu.
	void GameViewImGui()
	{
		DockNextWindow("Game", editorCenterDockId);
		var flags =
			ImGuiWindowFlags.NoBackground |
			ImGuiWindowFlags.NoScrollbar |
			ImGuiWindowFlags.NoScrollWithMouse |
			ImGuiWindowFlags.NoCollapse |
			ImGuiWindowFlags.NoNavFocus |
			// Clicking the room must not raise this window above the overlays drawn on top of
			// it (the hamburger, the paint options): it is a canvas, never the foreground.
			ImGuiWindowFlags.NoBringToFrontOnFocus;

		// NoBackground is not honoured once the window is docked -- the dock node draws the
		// window background itself, which is ImGui's default near-black WindowBg at alpha 0.94
		// and would sit over the room. The colour has to be transparent as well as the flag set.
		ImGui.PushStyleColor(ImGuiCol.WindowBg, 0u);
		ImGui.PushStyleColor(ImGuiCol.ChildBg, 0u);
		ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
		if (ImGui.Begin("Game", flags))
		{
			NoteDockedWindow();
			gameViewHovered = ImGui.IsWindowHovered() && !EditorMenuButtonRect.Contains(ImGui.GetMousePos());
			var origin = ImGui.GetCursorScreenPos();
			var size = ImGui.GetContentRegionAvail();
			if (size.X > 1 && size.Y > 1)
				gameViewWindowRect = new Rect(origin.X, origin.Y, size.X, size.Y);
		}
		else
		{
			gameViewHovered = false;
		}
		ImGui.End();
		ImGui.PopStyleVar();
		ImGui.PopStyleColor(2);
	}

	void EditorDockHostImGui(string hostName, string dockLabel, Rect rect, ref uint dockId, bool rightSide)
	{
		var flags =
			ImGuiWindowFlags.NoTitleBar |
			ImGuiWindowFlags.NoMove |
			ImGuiWindowFlags.NoResize |
			ImGuiWindowFlags.NoCollapse |
			ImGuiWindowFlags.NoSavedSettings |
			ImGuiWindowFlags.NoBringToFrontOnFocus |
			ImGuiWindowFlags.NoDocking;

		ImGui.SetNextWindowPos(rect.TopLeft, ImGuiCond.Always);
		ImGui.SetNextWindowSize(rect.Size, ImGuiCond.Always);
		ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
		if (ImGui.Begin(hostName, flags))
		{
			dockId = ImGui.GetID(dockLabel);
			ImGui.DockSpace(dockId, Vector2.Zero, EditorDockNodeFlags);
		}
		ImGui.End();
		ImGui.PopStyleVar();

		DrawEditorDockSplitter(rect, rightSide);
	}

	// Drag handle on a region's inner edge. It is its own window so it sits above the dock host
	// and keeps working while a docked window is focused.
	void DrawEditorDockSplitter(Rect dock, bool rightSide)
	{
		var flags =
			ImGuiWindowFlags.NoTitleBar |
			ImGuiWindowFlags.NoMove |
			ImGuiWindowFlags.NoResize |
			ImGuiWindowFlags.NoCollapse |
			ImGuiWindowFlags.NoSavedSettings |
			ImGuiWindowFlags.NoScrollbar |
			ImGuiWindowFlags.NoBackground |
			ImGuiWindowFlags.NoDocking;

		var x = rightSide ? dock.X - EditorDockSplitterWidth : dock.Right;
		var maxWidth = Math.Min(EditorDockMaxWidth, WorkRect.Width - EditorDockMinWidth - BoxSpacing * 4);
		ImGui.SetNextWindowPos(new Vector2(x, dock.Y), ImGuiCond.Always);
		ImGui.SetNextWindowSize(new Vector2(EditorDockSplitterWidth, dock.Height), ImGuiCond.Always);
		ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
		if (ImGui.Begin(rightSide ? "##EditorDockSplitter" : "##EditorPaletteSplitter", flags))
		{
			ImGui.InvisibleButton("##Drag", new Vector2(EditorDockSplitterWidth, dock.Height));
			var hovered = ImGui.IsItemHovered();
			var active = ImGui.IsItemActive();
			if (hovered || active)
				ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeEW);
			if (active)
			{
				var delta = ImGui.GetIO().MouseDelta.X;
				if (rightSide)
					editorDockWidth = Math.Clamp(editorDockWidth - delta, EditorDockMinWidth, maxWidth);
				else
					editorPaletteWidth = Math.Clamp(editorPaletteWidth + delta, EditorDockMinWidth, maxWidth);
			}
			if (ImGui.IsItemDeactivated())
				SaveWindowSettings();
			if (hovered || active)
				ImGui.GetWindowDrawList().AddRectFilled(
					ImGui.GetWindowPos(),
					ImGui.GetWindowPos() + new Vector2(EditorDockSplitterWidth, dock.Height),
					ImGui.GetColorU32(ImGuiCol.SeparatorHovered));
		}
		ImGui.End();
		ImGui.PopStyleVar();
	}
}
#endif
