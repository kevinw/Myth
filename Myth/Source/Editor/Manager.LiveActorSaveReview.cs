#if EDITOR
using System.Numerics;
using System.Text.Json.Nodes;
using ImGuiNET;

namespace Myth;

// Inspector edits made while playing (see Manager.Editor.Inspector.cs) apply to the running actor
// immediately but only reach the room JSON when you click "Apply to Room". Switching actors,
// pressing Tab back to the editor, or quitting would silently drop the rest. This keeps a ledger of
// those pending edits, accumulated across actors, and shows a review modal on the way out so you can
// pick what to keep. (Drag-move, resize, and delete already persist on their own and aren't tracked
// here.)
public partial class Manager
{
	sealed class LiveEditField
	{
		public required string Name;      // property key, matches liveInspectorEdits / ActorSpecs.SetProperty
		public required string Label;
		public JsonNode? Original;
		public JsonNode? Current;
		public bool Apply = true;
	}

	sealed class LiveEditRecord
	{
		public required string RoomId;
		public required string ActorId;
		public required string TypeName;
		public readonly List<LiveEditField> Fields = [];
	}

	readonly Dictionary<(string RoomId, string ActorId), LiveEditRecord> liveEditLedger = [];

	enum LiveEditReviewAction { None, EnterEditor, Quit }
	LiveEditReviewAction liveEditReviewAction;
	bool liveEditReviewOpenRequested;
	bool liveEditReviewApplyAndProceed;

	const string LiveEditReviewPopupId = "Unsaved Live Edits";

	bool HasPendingLiveEdits => liveEditLedger.Count > 0 || liveInspectorEdits.Count > 0;

	// Fold the currently-inspected actor's working edits into the ledger (or drop its entry if it has
	// none). Called when switching actors and before opening the review.
	void StashInspectedLiveEdits()
	{
		if (liveInspectedActorId is not { } actorId || game is not { } liveGame ||
			liveGame.GetActorById(actorId) is not { } runtime || liveGame.CurrentRoom is not { } runtimeRoom ||
			!TryGetLiveActorSource(liveGame, runtimeRoom, runtime, positionEditableOnly: false, out var room, out _))
			return;

		var key = (room.Id, actorId);
		if (liveInspectorEdits.Count == 0)
		{
			liveEditLedger.Remove(key);
			return;
		}

		var record = new LiveEditRecord { RoomId = room.Id, ActorId = actorId, TypeName = runtime.GetType().Name };
		foreach (var property in ActorSpecs.GetEditableProperties(runtime))
			if (liveInspectorEdits.TryGetValue(property.Name, out var current))
				record.Fields.Add(new LiveEditField
				{
					Name = property.Name,
					Label = property.DisplayName,
					Original = liveInspectorOriginalValues.GetValueOrDefault(property.Name)?.DeepClone(),
					Current = current?.DeepClone(),
				});
		liveEditLedger[key] = record;
	}

	// Bring a re-inspected actor's pending edits back into the working set. The runtime already holds
	// the edited values (we no longer revert on switch); only the true originals need restoring so the
	// per-field reset stays honest.
	void RestoreInspectedLiveEdits(Actor actor)
	{
		if (actor.ActorId is not { } actorId || game?.CurrentRoom is not { } runtimeRoom ||
			!TryGetLiveActorSource(game, runtimeRoom, actor, positionEditableOnly: false, out var room, out _) ||
			!liveEditLedger.TryGetValue((room.Id, actorId), out var record))
			return;

		foreach (var field in record.Fields)
		{
			liveInspectorOriginalValues[field.Name] = field.Original?.DeepClone();
			liveInspectorEdits[field.Name] = field.Current?.DeepClone();
		}
	}

	void ForgetLiveEditRecord(string roomId, string actorId) => liveEditLedger.Remove((roomId, actorId));

	void PruneLiveEditLedger()
	{
		if (liveEditLedger.Count == 0)
			return;
		liveEditRemovalScratch.Clear();
		foreach (var (key, record) in liveEditLedger)
			if (Assets.GetRoom(record.RoomId) is not { } room || FindAuthoredActor(room, record.ActorId) == null)
				liveEditRemovalScratch.Add(key);
		foreach (var key in liveEditRemovalScratch)
			liveEditLedger.Remove(key);
	}
	readonly List<(string, string)> liveEditRemovalScratch = [];

	// Returns true if a review is now up (caller should hold off entering the editor / quitting).
	bool BeginLiveEditReview(LiveEditReviewAction action)
	{
		StashInspectedLiveEdits();
		PruneLiveEditLedger();
		if (liveEditLedger.Count == 0)
			return false;

		foreach (var record in liveEditLedger.Values)
			foreach (var field in record.Fields)
				field.Apply = true;
		liveEditReviewAction = action;
		liveEditReviewOpenRequested = true;
		return true;
	}

	void ApplyCheckedLiveEdits()
	{
		liveEditRoomScratch.Clear();
		foreach (var record in liveEditLedger.Values)
			liveEditRoomScratch.Add(record.RoomId);

		foreach (var roomId in liveEditRoomScratch)
		{
			if (Assets.GetRoom(roomId) is not { } room)
				continue;
			var wrote = false;
			using (CaptureRoomUndo("Apply Live Actor Properties", room))
				foreach (var record in liveEditLedger.Values)
				{
					if (record.RoomId != roomId || FindAuthoredActor(room, record.ActorId) is not { } authored)
						continue;
					foreach (var field in record.Fields)
						if (field.Apply)
						{
							ActorSpecs.SetProperty(authored, field.Name, field.Current?.DeepClone());
							wrote = true;
						}
				}
			if (wrote)
			{
				room.Save();
				MarkRoomPreviewDirty(room);
			}
		}
		editorGameDirty = true;
	}
	readonly HashSet<string> liveEditRoomScratch = [];

	// Runs before ImGui each tick so entering the editor / quitting happens outside the popup draw,
	// where tearing down the game is safe.
	void ResolveDeferredLiveEditReview()
	{
		if (!liveEditReviewApplyAndProceed)
			return;
		liveEditReviewApplyAndProceed = false;
		var action = liveEditReviewAction;
		liveEditReviewAction = LiveEditReviewAction.None;

		ApplyCheckedLiveEdits();
		liveEditLedger.Clear();

		if (action == LiveEditReviewAction.EnterEditor)
		{
			if (playing || startingPlay)
				StopGame();
		}
		else if (action == LiveEditReviewAction.Quit)
			QuitNow();
	}

	void LiveEditReviewImGui()
	{
		if (liveEditReviewOpenRequested)
		{
			liveEditReviewOpenRequested = false;
			ImGui.OpenPopup(LiveEditReviewPopupId);
		}

		if (liveEditReviewAction == LiveEditReviewAction.None)
			return;

		var open = true;
		ImGui.SetNextWindowSize(new Vector2(480, 0), ImGuiCond.Appearing);
		if (!BeginPaddedPopupModal(LiveEditReviewPopupId, ref open, ImGuiWindowFlags.AlwaysAutoResize))
		{
			if (!open)
				liveEditReviewAction = LiveEditReviewAction.None;
			return;
		}

		ImGui.TextUnformatted("These live edits haven't been saved to disk. Pick what to keep:");
		ImGui.Spacing();
		if (ImGui.Button("Check All"))
			SetAllLiveEditChecks(true);
		ImGui.SameLine();
		if (ImGui.Button("Check None"))
			SetAllLiveEditChecks(false);
		ImGui.Separator();

		ImGui.BeginChild("live-edit-list", new Vector2(0, 320), ImGuiChildFlags.Borders);
		foreach (var record in liveEditLedger.Values)
			DrawLiveEditRecord(record);
		ImGui.EndChild();

		ImGui.Separator();
		var applyLabel = liveEditReviewAction == LiveEditReviewAction.Quit ? "Apply and Quit" : "Apply and Enter Edit Mode";
		if (ImGui.Button(applyLabel))
		{
			liveEditReviewApplyAndProceed = true; // resolved next tick, outside the popup
			ImGui.CloseCurrentPopup();
		}
		ImGui.SameLine();
		if (ImGui.Button("Cancel") || ImGui.IsKeyPressed(ImGuiKey.Escape))
		{
			liveEditReviewAction = LiveEditReviewAction.None; // stay in play, keep pending edits
			ImGui.CloseCurrentPopup();
		}

		EndPaddedPopup();

		if (!open) // window's X: same as Cancel
			liveEditReviewAction = LiveEditReviewAction.None;
	}

	void DrawLiveEditRecord(LiveEditRecord record)
	{
		var allChecked = true;
		var anyChecked = false;
		foreach (var field in record.Fields)
		{
			allChecked &= field.Apply;
			anyChecked |= field.Apply;
		}

		ImGui.PushID(record.RoomId);
		ImGui.PushID(record.ActorId);

		var actorChecked = allChecked;
		var mixed = anyChecked && !allChecked;
		var actorLabel = mixed ? $"{record.TypeName}  (partial)" : record.TypeName;
		if (ImGui.Checkbox(actorLabel, ref actorChecked))
			foreach (var field in record.Fields)
				field.Apply = actorChecked;
		ImGui.SameLine();
		ImGui.TextDisabled(record.ActorId);

		ImGui.Indent();
		foreach (var field in record.Fields)
		{
			ImGui.PushID(field.Name);
			ImGui.Checkbox(field.Label, ref field.Apply);
			ImGui.SameLine();
			ImGui.TextDisabled($"{LiveEditValueText(field.Original)} → {LiveEditValueText(field.Current)}");
			ImGui.PopID();
		}
		ImGui.Unindent();

		ImGui.PopID();
		ImGui.PopID();
	}

	void SetAllLiveEditChecks(bool value)
	{
		foreach (var record in liveEditLedger.Values)
			foreach (var field in record.Fields)
				field.Apply = value;
	}

	static string LiveEditValueText(JsonNode? node)
	{
		if (node == null)
			return "—";
		var text = node.ToJsonString();
		return text.Length <= 40 ? text : text[..39] + "…";
	}
}
#endif
