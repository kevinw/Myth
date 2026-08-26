using Foster.Framework;
namespace Myth;

// Debug/dev shortcuts collected in one place — Controls' sibling for the development-only
// bindings that were scattered across Manager and Game. Each binding registers a display
// row alongside its action, so the '?' help window (Manager.DebugHelp) generates itself
// from this list; gesture-only rows (mouse drags, contextual chords still handled at their
// call sites) are registered with Note so the cheat sheet stays complete.
public class DebugControls
{
	public enum Context { Anywhere, Playing, Editing }
	public readonly record struct Entry(string Keys, string Description, Context Where);

	public readonly List<Entry> Entries = [];

	public readonly VirtualAction Tweaks;
	public readonly VirtualAction Help;
	public readonly VirtualAction ToggleEditor;
	public readonly VirtualAction Console;
	public readonly VirtualAction FpsOverlay;
	public readonly VirtualAction Fullscreen;
	public readonly VirtualAction BudAI;
	public readonly VirtualAction MovieRecording;
	public readonly VirtualAction AllocationTrace;
	public readonly VirtualAction ReloadRoom;
	public readonly VirtualAction RecordReplay;
	public readonly VirtualAction PlayReplay;
	public readonly VirtualAction CollisionView;
	public readonly VirtualAction NormalsView;
	public readonly VirtualAction RenderTargets;
	public readonly VirtualAction PlatformNavView;
	public readonly VirtualAction AtlasWander;
	public readonly VirtualAction RenderTargetShake;
	public readonly VirtualAction CenterCamera;
	public readonly VirtualAction PerfReveal;
	public readonly VirtualAction VoiceTest;
	public readonly VirtualAction TimeScaleDown;
	public readonly VirtualAction TimeScaleUp;
	public readonly VirtualAction AdvanceGym;
	public readonly VirtualAction UiScaleDown;
	public readonly VirtualAction UiScaleUp;
	public readonly VirtualAction ToggleLiveRoomEditing;

	public DebugControls(Input input)
	{
		VirtualAction Make(string name, ActionBindingSet set, string keys, string description, Context where)
		{
			Entries.Add(new(keys, description, where));
			return new(input, name, set);
		}
		void Note(string keys, string description, Context where)
			=> Entries.Add(new(keys, description, where));

		ToggleEditor = Make("DebugToggleEditor",
			new ActionBindingSet().Add(Keys.Tab).Add(Buttons.RightStick),
			"Tab", "Toggle editor / play", Context.Anywhere);
		Console = Make("DebugConsole",
			new ActionBindingSet().Add(Keys.Tilde),
			"~", "Command console", Context.Anywhere);
		Tweaks = Make("DebugTweaks",
			new ActionBindingSet().Add(Keys.T),
			"T", "Tweaks panel", Context.Anywhere);
		FpsOverlay = Make("DebugFpsOverlay",
			new ActionBindingSet().Add(Keys.F5),
			"F5", "FPS overlay", Context.Anywhere);
		Fullscreen = Make("DebugFullscreen",
			new ActionBindingSet().Add(Keys.F4),
			"F4", "Fullscreen", Context.Anywhere);
		BudAI = Make("DebugBudAI",
			new ActionBindingSet().Add(Keys.B),
			"B / Shift+B", "Toggle Bud AI / Bud story barks", Context.Anywhere);
		MovieRecording = Make("DebugMovieRecording",
			new ActionBindingSet().Add(Keys.M),
			"M / Shift+M", "Movie recording", Context.Anywhere);
		AllocationTrace = Make("DebugAllocationTrace",
			new ActionBindingSet().Add(Keys.F9),
			"Shift+F9", "Start / stop allocation trace", Context.Anywhere);
		Help = Make("DebugHelp",
			new ActionBindingSet().Add(Keys.Slash),
			"?", "This window", Context.Anywhere);

		ReloadRoom = Make("DebugReloadRoom",
			new ActionBindingSet().Add(Keys.R),
			"R", "Reload room", Context.Playing);
		RecordReplay = Make("DebugRecordReplay",
			new ActionBindingSet().Add(Keys.F7),
			"F7", "Record replay clips", Context.Playing);
		PlayReplay = Make("DebugPlayReplay",
			new ActionBindingSet().Add(Keys.F8),
			"F8 / Shift+F8", "Play newest replay / attract playlist", Context.Anywhere);
		CollisionView = Make("DebugCollisionView",
			new ActionBindingSet().Add(Keys.C),
			"C", "Collision rects view", Context.Playing);
		NormalsView = Make("DebugNormalsView",
			new ActionBindingSet().Add(Keys.N),
			"N", "Normal buffer view", Context.Playing);
		RenderTargets = Make("DebugRenderTargets",
			new ActionBindingSet().Add(Keys.G),
			"G / Shift+G", "Render-target overlay / god mode", Context.Playing);
		PlatformNavView = Make("DebugPlatformNavView",
			new ActionBindingSet().Add(Keys.P),
			"P", "Platform-nav view", Context.Playing);
		AtlasWander = Make("DebugAtlasWander",
			new ActionBindingSet().Add(Keys.J),
			"J", "Atlas wander", Context.Playing);
		RenderTargetShake = Make("DebugRenderTargetShake",
			new ActionBindingSet().Add(Keys.K),
			"K / Shift+K", "Test target-space shake / toggle shake mode", Context.Playing);
		CenterCamera = Make("DebugCenterCamera",
			new ActionBindingSet().Add(Keys.L),
			"L", "Center camera on player", Context.Playing);
		PerfReveal = Make("DebugPerfReveal",
			new ActionBindingSet().Add(Keys.U),
			"U / Shift+U", "Perf toggles: reveal / hide one", Context.Playing);
		VoiceTest = Make("DebugVoiceTest",
			new ActionBindingSet().Add(Keys.V),
			"V", "Bud voice test line", Context.Playing);
		TimeScaleDown = Make("DebugTimeScaleDown",
			new ActionBindingSet().Add(Keys.Minus, Keys.KeypadMinus),//.Add(Buttons.LeftShoulder),
			"- / LB", "Time scale down", Context.Playing);
		TimeScaleUp = Make("DebugTimeScaleUp",
			new ActionBindingSet().Add(Keys.Equals, Keys.KeypadPlus),//.Add(Buttons.RightShoulder),
			"= / RB", "Time scale up", Context.Playing);
		AdvanceGym = Make("DebugAdvanceGym",
			new ActionBindingSet().Add(Keys.Enter, Keys.KeypadEnter),
			"Enter", "Advance gym (gym loop)", Context.Playing);
		Note("Left drag", "Drag the player", Context.Playing);
		Note("Click / drag", "Select / move live-editable actor", Context.Playing);
		Note("Handles", "Resize live-editable actor", Context.Playing);
		Note("Shift (dragging)", "Toggle position snap", Context.Playing);
		Note("LB+RB+DPad Up", "Toggle controller live tile brush", Context.Playing);
		Note("Live brush: DPad/left stick/touchpad", "Move tile cursor", Context.Playing);
		Note("Live brush: South/East/West/North", "Paint / erase / sample / layer", Context.Playing);
		Note("Backspace / Delete", "Delete selected live actor", Context.Playing);
		Note("Esc", "Cancel live drag / deselect", Context.Playing);
		Note("Ctrl+Z / Ctrl+Y", "Undo / redo live actor edit", Context.Playing);
		ToggleLiveRoomEditing = Make("DebugToggleLiveRoomEditing",
			new ActionBindingSet().Add(Keys.F8),
			"F8 / LB+RB+DPad Down", "Toggle live room creation", Context.Playing);

		UiScaleDown = Make("DebugUiScaleDown",
			new ActionBindingSet().Add(Keys.Minus, Keys.KeypadMinus),
			"-", "Editor UI scale down", Context.Editing);
		UiScaleUp = Make("DebugUiScaleUp",
			new ActionBindingSet().Add(Keys.Equals, Keys.KeypadPlus),
			"=", "Editor UI scale up", Context.Editing);
		Note("Move keys", "Jump to adjacent room", Context.Editing);
		Note("Shift+Move", "Pan camera", Context.Editing);
		Note("Cmd+Z / Shift+Z / Y", "Undo / redo", Context.Editing);
		Note("Cmd+C / X / V / D", "Copy / cut / paste / duplicate actor (room while room-editing)", Context.Editing);
		Note("Delete", "Delete selected actor", Context.Editing);
		Note("Esc", "Actor select tool", Context.Editing);
		Note("P", "Paint room color field", Context.Editing);
		Note("B", "Toggle breakable on hovered tile", Context.Editing);
		Note("C", "Toggle crumble on hovered tile", Context.Editing);
		Note("G", "Toggle fungal on hovered tile", Context.Editing);
		Note("Shift+G", "Show fungal markers", Context.Editing);
		Note("Cmd+S", "Save selected actor spec", Context.Editing);
		Note("Cmd+,", "Preferences (macOS)", Context.Editing);
	}
}
