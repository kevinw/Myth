#if EDITOR
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json.Nodes;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Multimedia;
using ImGuiNET;

namespace Myth;

public partial class Manager
{
	readonly record struct MidiControl(int Number, int Value);
	readonly record struct MidiParameter(string Key, string Label, float Min, float Max, float Value, bool Integer);

	readonly ConcurrentQueue<MidiControl> midiEvents = new();
	readonly List<MidiControl> midiContinuousScratch = [];
	readonly List<MidiParameter> midiParameters = [];
	readonly List<string> midiVisibleActorProperties = [];
	readonly Dictionary<string, int> midiLearned = [];
	readonly int[] midiPhysical = new int[128];
	readonly bool[] midiSeen = new bool[128];
	readonly bool[] midiPickedUp = new bool[8];
	readonly float[] midiFaderOffsets = new float[8];
	readonly bool[] midiClutched = new bool[8];
	readonly string?[] midiFaderTargets = new string?[8];
	InputDevice? midiInput;
	string midiDeviceStatus = "Searching for nanoKONTROL2";
	string? midiLearnParameter;
	string? midiTargetId;
	string? midiVisibleActorTargetId;
	string? midiGestureRoomId;
	string? midiObservedLiveSelectionId;
	bool midiWindowOpen;
	bool midiRoomTarget;
	bool midiGestureActive;
	bool midiNeedsEditorSync;
	bool midiWatchingDevices;
	int midiBank;
	int midiReconnectFrames;
	int midiLastControl = -1;
	int midiLastValue;
	long midiLastEdit;
	long midiLastRefresh;
	long midiLastEditorSync;

	static string MidiSettingsPath => Path.Join(UserPathForApplicationName(APPLICATION_NAME), "midi-mappings.json");

	void UpdateMidiEditing()
	{
		if (!OperatingSystem.IsMacOS() && !OperatingSystem.IsWindows())
		{
			midiDeviceStatus = "MIDI input is supported on macOS and Windows";
			return;
		}
		if (midiInput == null && ++midiReconnectFrames >= 120)
		{
			midiReconnectFrames = 0;
			ConnectMidiInput();
		}

		if (midiGestureActive && Stopwatch.GetElapsedTime(midiLastEdit).TotalSeconds > 0.45)
			EndMidiGesture();
		if (!playing) midiObservedLiveSelectionId = null;
		if (playing && !UneditableSession && !midiRoomTarget && liveSelectedActorId != midiObservedLiveSelectionId)
		{
			midiObservedLiveSelectionId = liveSelectedActorId;
			if (liveSelectedActorId is { } selectedId && selectedId != liveInspectedActorId &&
				game?.GetActorById(selectedId) is { } selectedActor)
				OpenLiveActorInspector(selectedActor);
		}

		var targetId = MidiTargetId();
		if (targetId != midiTargetId)
		{
			EndMidiGesture();
			midiTargetId = targetId;
			Array.Clear(midiPickedUp);
			Array.Clear(midiFaderOffsets);
			RefreshMidiParameters();
			midiBank = Math.Min(midiBank, Math.Max(0, (midiParameters.Count - 1) / 8));
		}

		for (var processed = 0; processed < 256 && midiEvents.TryDequeue(out var message); processed++)
		{
			if (message.Number >= 0)
			{
				midiLastControl = message.Number;
				midiLastValue = message.Value;
			}
			if (message.Number is >= 0 and <= 7 or >= 16 and <= 23)
			{
				var index = -1;
				for (var i = 0; i < midiContinuousScratch.Count; i++)
					if (midiContinuousScratch[i].Number == message.Number) { index = i; break; }
				if (index >= 0) midiContinuousScratch[index] = message;
				else midiContinuousScratch.Add(message);
				continue;
			}
			ApplyPendingMidiControls();
			HandleMidiControl(message);
		}
		ApplyPendingMidiControls();
		FlushMidiEditorSync();
	}

	void ApplyPendingMidiControls()
	{
		foreach (var message in midiContinuousScratch)
			HandleMidiControl(message);
		midiContinuousScratch.Clear();
	}

	void FlushMidiEditorSync(bool force = false)
	{
		if (!midiNeedsEditorSync || !force && Stopwatch.GetElapsedTime(midiLastEditorSync).TotalSeconds < 0.08)
			return;
		midiNeedsEditorSync = false;
		midiLastEditorSync = Stopwatch.GetTimestamp();
		SyncEditorGame();
	}

	void ConnectMidiInput()
	{
		try
		{
			if (OperatingSystem.IsMacOS() && !midiWatchingDevices)
			{
				DevicesWatcher.Instance.DeviceAdded += OnMidiDeviceAdded;
				DevicesWatcher.Instance.DeviceRemoved += OnMidiDeviceRemoved;
				midiWatchingDevices = true;
			}
			foreach (var device in InputDevice.GetAll())
			{
				if (midiInput == null && device.Name.Contains("nanoKONTROL2", StringComparison.OrdinalIgnoreCase))
				{
					midiInput = device;
					midiInput.EventReceived += OnMidiEvent;
					midiInput.ErrorOccurred += OnMidiError;
					midiInput.StartEventsListening();
					midiDeviceStatus = device.Name;
					LoadMidiMappings();
				}
				else
					device.Dispose();
			}
		}
		catch (Exception error)
		{
			midiDeviceStatus = $"MIDI: {error.Message}";
			DisposeMidiInput();
		}
	}

	void OnMidiEvent(object? sender, MidiEventReceivedEventArgs e)
	{
		if (e.Event is ControlChangeEvent cc)
			midiEvents.Enqueue(new((int)cc.ControlNumber, (int)cc.ControlValue));
	}

	void OnMidiError(object? sender, ErrorOccurredEventArgs e) => midiEvents.Enqueue(new(-1, 0));
	void OnMidiDeviceAdded(object? sender, DeviceAddedRemovedEventArgs e) => midiEvents.Enqueue(new(-2, 0));
	void OnMidiDeviceRemoved(object? sender, DeviceAddedRemovedEventArgs e)
	{
		if (e.Device is InputDevice removed && removed == midiInput)
			midiEvents.Enqueue(new(-1, 0));
	}

	void DisposeMidiInput()
	{
		if (midiInput == null) return;
		midiInput.EventReceived -= OnMidiEvent;
		midiInput.ErrorOccurred -= OnMidiError;
		midiInput.Dispose();
		midiInput = null;
	}

	void DisposeMidiEditing()
	{
		DisposeMidiInput();
		if (!midiWatchingDevices) return;
		DevicesWatcher.Instance.DeviceAdded -= OnMidiDeviceAdded;
		DevicesWatcher.Instance.DeviceRemoved -= OnMidiDeviceRemoved;
		midiWatchingDevices = false;
	}

	string? MidiTargetId()
	{
		if (midiRoomTarget)
			return (playing ? game?.CurrentRoom : CurrentRoom) is { } room ? $"room:{room.Id}" : null;
		if (playing)
			return liveInspectedActorId is { } liveId && game?.GetActorById(liveId) != null ? $"live:{liveId}" : null;
		return CurrentRoom is { } current && selectedRoomActorIndex >= 0 && selectedRoomActorIndex < current.Actors.Count
			? $"actor:{current.Id}:{ActorSpecs.GetActorId(current.Actors[selectedRoomActorIndex])}" : null;
	}

	void RefreshMidiParameters()
	{
		midiLastRefresh = Stopwatch.GetTimestamp();
		midiParameters.Clear();
		if (midiRoomTarget)
		{
			if ((playing ? game?.CurrentRoom : CurrentRoom) is { } room)
			{
				AddMidiRoomParameter("AmbientIntensity", "Ambient intensity", 0, 2, room.AmbientIntensity);
				AddMidiRoomParameter("LightNoise", "Light noise", 0, 1, room.LightNoise);
				AddMidiRoomParameter("SporeFogStrength", "Spore fog", 0, 1, room.SporeFogStrength);
				AddMidiRoomParameter("Reverb", "Reverb", 0, 1, room.Reverb);
				AddMidiRoomParameter("MusicMuffle", "Music muffle", 0, 1, room.MusicMuffle);
				AddMidiRoomParameter("SunStrength", "Sun strength", 0, 2, room.SunStrength);
				AddMidiRoomParameter("LightingDitherScale", "Dither scale", 1, 8, room.LightingDitherScale);
				AddMidiRoomParameter("LightingDitherStrength", "Dither strength", 0, 1, room.LightingDitherStrength);
				AddMidiRoomParameter("NormalDitherSlopeBoost", "Dither slope boost", 0, 1, room.NormalDitherSlopeBoost);
				AddMidiRoomParameter("Zoom", "Zoom", 1, 4, room.Zoom, integer: true);
			}
			ValidateMidiFaderTargets();
			return;
		}

		JsonObject? authored = null;
		Actor? runtime = null;
		if (playing)
		{
			if (liveInspectedActorId is { } id)
				runtime = game?.GetActorById(id);
			if (runtime != null && game?.CurrentRoom is { } liveRoom)
				TryGetLiveActorSource(game, liveRoom, runtime, positionEditableOnly: false, out _, out authored);
		}
		else if (CurrentRoom is { } room && selectedRoomActorIndex >= 0 && selectedRoomActorIndex < room.Actors.Count)
			authored = room.Actors[selectedRoomActorIndex];

		if (authored == null) { ValidateMidiFaderTargets(); return; }
		actorPropertyScratch.Clear();
		actorPropertyScratch.AddRange(runtime == null ? ActorSpecs.GetEditableProperties(authored) : ActorSpecs.GetEditableProperties(runtime));
		var type = ActorSpecs.GetTypeName(authored);
		foreach (var property in actorPropertyScratch)
		{
			if (property.Inspector is not { } range || range.Max <= range.Min ||
				property.Type != typeof(float) && property.Type != typeof(int) ||
				HideInactiveActorProperty(authored, property) ||
				midiVisibleActorTargetId == midiTargetId && !midiVisibleActorProperties.Contains(property.Name))
				continue;
			var value = property.Type == typeof(int) ? property.Value?.GetValue<int>() ?? 0 : property.Value?.GetValue<float>() ?? 0;
			midiParameters.Add(new($"actor:{type}:{property.Name}", property.DisplayName, range.Min, range.Max, value, property.Type == typeof(int)));
		}
		ValidateMidiFaderTargets();
	}

	void ValidateMidiFaderTargets()
	{
		for (var strip = 0; strip < 8; strip++)
		{
			var index = MidiParameterIndex(strip, strip);
			var key = index < 0 ? null : midiParameters[index].Key;
			if (key == midiFaderTargets[strip]) continue;
			midiFaderTargets[strip] = key;
			midiPickedUp[strip] = false;
			midiFaderOffsets[strip] = 0;
		}
	}

	void AddMidiRoomParameter(string name, string label, float min, float max, float value, bool integer = false)
		=> midiParameters.Add(new($"room:{name}", label, min, max, value, integer));

	void BeginMidiActorInspector(string targetId)
	{
		midiVisibleActorTargetId = targetId;
		midiVisibleActorProperties.Clear();
	}

	void NoteMidiActorProperty(ActorSpecs.EditableProperty property)
	{
		if (property.Inspector != null && (property.Type == typeof(float) || property.Type == typeof(int)))
			midiVisibleActorProperties.Add(property.Name);
	}

	bool MidiRoomSlider(string name, string label, ref float value, float min, float max)
	{
		var changed = ImGui.SliderFloat(label, ref value, min, max);
		MidiRoomLearnPopup(name);
		return changed;
	}

	bool MidiRoomSlider(string name, string label, ref int value, int min, int max)
	{
		var changed = ImGui.SliderInt(label, ref value, min, max);
		MidiRoomLearnPopup(name);
		return changed;
	}

	void MidiRoomLearnPopup(string name)
	{
		if (!ImGui.BeginPopupContextItem("##MidiLearn")) return;
		if (ImGui.MenuItem("MIDI Learn"))
		{
			midiRoomTarget = true;
			midiWindowOpen = true;
			midiLearnParameter = $"room:{name}";
		}
		ImGui.EndPopup();
	}

	void HandleMidiControl(MidiControl message)
	{
		var cc = message.Number;
		var value = message.Value;
		if (cc == -2) { midiReconnectFrames = 119; return; }
		if (cc < 0)
		{
			midiDeviceStatus = "MIDI disconnected";
			DisposeMidiInput();
			return;
		}
		var previous = midiPhysical[cc];
		var seen = midiSeen[cc];
		midiPhysical[cc] = value;
		midiSeen[cc] = true;
		if (UneditableSession) return;

		if (midiLearnParameter != null && cc is >= 0 and <= 7 or >= 16 and <= 23)
		{
			var scope = midiLearnParameter[..(midiLearnParameter.LastIndexOf(':') + 1)];
			string? previousBinding = null;
			foreach (var (key, assigned) in midiLearned)
				if (assigned == cc && key.StartsWith(scope, StringComparison.Ordinal))
				{
					previousBinding = key;
					break;
				}
			if (previousBinding != null) midiLearned.Remove(previousBinding);
			midiLearned[midiLearnParameter] = cc;
			midiLearnParameter = null;
			Array.Clear(midiPickedUp);
			SaveMidiMappings();
			return;
		}

		if (cc is >= 48 and <= 55)
		{
			var strip = cc - 48;
			if (midiClutched[strip] && value == 0)
			{
				RefreshMidiParameters();
				var index = MidiParameterIndex(strip, strip);
				if (index >= 0)
				{
					var parameter = midiParameters[index];
					midiFaderOffsets[strip] = (parameter.Value - parameter.Min) / (parameter.Max - parameter.Min) - midiPhysical[strip] / 127f;
					midiPickedUp[strip] = true;
				}
			}
			midiClutched[strip] = value != 0;
			return;
		}
		if (value > 0 && cc is 58 or 59)
		{
			SelectMidiActor(cc == 58 ? -1 : 1);
			midiTargetId = null;
			Array.Clear(midiPickedUp);
			return;
		}
		if (value > 0 && cc is 61 or 62) { ChangeMidiBank(cc == 61 ? -1 : 1); return; }
		if (value > 0 && cc == 41) { EndMidiGesture(); if (!playing && !startingPlay) StartGame(); return; }
		if (value > 0 && cc == 42) { EndMidiGesture(); if (playing || startingPlay) StopGame(); return; }
		if (cc is < 0 or > 7 && cc is < 16 or > 23 || midiTargetId == null) return;
		var stripIndex = cc is >= 16 and <= 23 ? cc - 16 : cc;
		if (midiClutched[stripIndex]) return;

		RefreshMidiParameters();
		var parameterIndex = MidiParameterIndex(cc, stripIndex);
		if (parameterIndex < 0) return;
		var target = midiParameters[parameterIndex];
		var next = target.Value;
		if (cc < 8)
		{
			var position = value / 127f + midiFaderOffsets[stripIndex];
			var current = Math.Clamp((target.Value - target.Min) / (target.Max - target.Min), 0, 1);
			if (!midiPickedUp[stripIndex])
			{
				if (MathF.Abs(position - current) > 2f / 127f && (!seen || (previous / 127f - current) * (position - current) > 0))
					return;
				midiPickedUp[stripIndex] = true;
			}
			next = target.Min + Math.Clamp(position, 0, 1) * (target.Max - target.Min);
		}
		else
		{
			if (!seen) return;
			next += (value - previous) / 127f * (target.Max - target.Min) * 0.08f;
		}
		next = Math.Clamp(next, target.Min, target.Max);
		if (target.Integer) next = MathF.Round(next);
		if (next == target.Value) return;
		ApplyMidiParameter(target, next);
		midiLastEdit = Stopwatch.GetTimestamp();
		midiGestureActive = true;
	}

	int MidiParameterIndex(int cc, int strip)
	{
		for (var i = 0; i < midiParameters.Count; i++)
			if (midiLearned.TryGetValue(midiParameters[i].Key, out var learned) && learned == cc)
				return i;
		var index = midiBank * 8 + strip;
		if (index >= midiParameters.Count) return -1;
		return !midiLearned.TryGetValue(midiParameters[index].Key, out var assigned) || (assigned < 8) != (cc < 8) ? index : -1;
	}

	void ApplyMidiParameter(MidiParameter parameter, float value)
	{
		if (midiRoomTarget)
		{
			var runtime = playing ? game?.CurrentRoom : null;
			var authoredRoom = runtime == null ? CurrentRoom : Assets.GetRoom(runtime.Id);
			if (authoredRoom == null) return;
			OpenRoomSettingsUndo("Edit Room with MIDI", authoredRoom);
			SetMidiRoomValue(authoredRoom, parameter.Key, value);
			if (runtime != null) SetMidiRoomValue(runtime, parameter.Key, value);
			midiGestureRoomId = authoredRoom.Id;
			editorGameDirty = true;
			if (!playing) midiNeedsEditorSync = true;
			return;
		}

		var name = parameter.Key[(parameter.Key.LastIndexOf(':') + 1)..];
		if (playing)
		{
			if (liveInspectedActorId is not { } id || game?.GetActorById(id) is not { } runtime ||
				game.CurrentRoom is not { } liveRoom || !TryGetLiveActorSource(game, liveRoom, runtime, positionEditableOnly: false, out _, out var authored))
				return;
			foreach (var property in ActorSpecs.GetEditableProperties(runtime))
				if (property.Name == name)
				{
					livePropertyTarget = runtime;
					try { SetActorProperty(null, null, authored, property, MidiJsonValue(property, value), continuous: true); }
					finally { livePropertyTarget = null; }
					return;
				}
			return;
		}

		if (CurrentRoom is not { } room || selectedRoomActorIndex < 0 || selectedRoomActorIndex >= room.Actors.Count) return;
		var actor = room.Actors[selectedRoomActorIndex];
		foreach (var property in ActorSpecs.GetEditableProperties(actor))
			if (property.Name == name)
			{
				SetActorProperty(null, room, actor, property, MidiJsonValue(property, value), continuous: true);
				midiGestureRoomId = room.Id;
				editorGameDirty = true;
				midiNeedsEditorSync = true;
				return;
			}
	}

	static JsonNode MidiJsonValue(ActorSpecs.EditableProperty property, float value)
		=> property.Type == typeof(int) ? JsonValue.Create((int)value) : JsonValue.Create(value);

	static void SetMidiRoomValue(Room room, string key, float value)
	{
		switch (key)
		{
			case "room:AmbientIntensity": room.AmbientIntensity = value; break;
			case "room:LightNoise": room.LightNoise = value; break;
			case "room:SporeFogStrength": room.SporeFogStrength = value; break;
			case "room:Reverb": room.ReverbAuto = false; room.Reverb = value; break;
			case "room:MusicMuffle": room.MusicMuffle = value; break;
			case "room:SunStrength": room.SunStrength = value; break;
			case "room:LightingDitherScale": room.LightingDitherScale = value; break;
			case "room:LightingDitherStrength": room.LightingDitherStrength = value; break;
			case "room:NormalDitherSlopeBoost": room.NormalDitherSlopeBoost = value; break;
			case "room:Zoom": room.Zoom = value; break;
		}
	}

	void EndMidiGesture()
	{
		if (!midiGestureActive) return;
		FlushMidiEditorSync(force: true);
		undo.CommitPending(ActorPropertyUndoSlot);
		undo.CommitPending(RoomSettingsUndoSlot);
		if (midiGestureRoomId is { } roomId && Assets.GetRoom(roomId) is { } authored)
			authored.Save();
		midiGestureRoomId = null;
		midiGestureActive = false;
	}

	void SelectMidiActor(int direction)
	{
		EndMidiGesture();
		var room = playing ? game?.CurrentRoom : CurrentRoom;
		if (room == null || room.Actors.Count == 0) return;
		var current = playing ? FindAuthoredActorIndex(room, liveInspectedActorId ?? "") : selectedRoomActorIndex;
		for (var step = 1; step <= room.Actors.Count; step++)
		{
			var index = ((current < 0 ? direction < 0 ? 0 : -1 : current) + direction * step % room.Actors.Count + room.Actors.Count) % room.Actors.Count;
			if (playing)
			{
				if (ActorSpecs.GetActorId(room.Actors[index]) is not { } id || game?.GetActorById(id) is not { } runtime) continue;
				liveSelectedActorId = id;
				OpenLiveActorInspector(runtime);
			}
			else
			{
				SelectEditorActorTool();
				selectedRoomActorIndex = index;
			}
			midiRoomTarget = false;
			return;
		}
	}

	void ChangeMidiBank(int direction)
	{
		EndMidiGesture();
		RefreshMidiParameters();
		midiBank = Math.Clamp(midiBank + direction, 0, Math.Max(0, (midiParameters.Count - 1) / 8));
		Array.Clear(midiPickedUp);
		ValidateMidiFaderTargets();
	}

	void MidiEditingImGui()
	{
		if (!midiWindowOpen) return;
		ImGui.SetNextWindowSize(new(355, 360), ImGuiCond.FirstUseEver);
		DockNextToolWindow("MIDI Controls");
		if (!ImGui.Begin("MIDI Controls", ref midiWindowOpen)) { ImGui.End(); return; }
		NoteDockedWindow();
		ImGui.TextUnformatted(midiDeviceStatus);
		if (midiLastControl >= 0) ImGui.TextDisabled($"Last: CC {midiLastControl} = {midiLastValue}");
		ImGui.TextDisabled("Fader: full range · knob: fine · hold M: clutch");
		ImGui.TextDisabled("Track: actor · marker: bank · Play/Stop: game");
		if (ImGui.RadioButton("Actor", !midiRoomTarget)) { EndMidiGesture(); midiRoomTarget = false; }
		ImGui.SameLine();
		if (ImGui.RadioButton("Room", midiRoomTarget)) { EndMidiGesture(); midiRoomTarget = true; }
		if (midiLearnParameter != null)
		{
			ImGui.TextUnformatted("Move a fader or knob to learn it");
			ImGui.SameLine();
			if (ImGui.SmallButton("Cancel")) midiLearnParameter = null;
		}
		if (ImGui.SmallButton("Previous 8")) ChangeMidiBank(-1);
		ImGui.SameLine();
		ImGui.TextUnformatted($"Bank {midiBank + 1}");
		ImGui.SameLine();
		if (ImGui.SmallButton("Next 8")) ChangeMidiBank(1);
		if (Stopwatch.GetElapsedTime(midiLastRefresh).TotalSeconds > 0.25)
			RefreshMidiParameters();
		for (var strip = 0; strip < 8; strip++)
		{
			var index = midiBank * 8 + strip;
			if (index >= midiParameters.Count) break;
			var parameter = midiParameters[index];
			ImGui.PushID(strip);
			ImGui.TextUnformatted($"{strip + 1}  {parameter.Label}: {parameter.Value:0.###}");
			ImGui.SameLine();
			if (ImGui.SmallButton("Learn")) midiLearnParameter = parameter.Key;
			if (midiLearned.TryGetValue(parameter.Key, out var cc))
			{
				ImGui.SameLine();
				if (ImGui.SmallButton($"CC {cc} ×")) { midiLearned.Remove(parameter.Key); SaveMidiMappings(); }
			}
			if (midiSeen[strip]) ImGui.TextDisabled($"  Fader {midiPhysical[strip] / 127f:P0}{(midiPickedUp[strip] ? "" : " · waiting for pickup")}");
			ImGui.PopID();
		}
		ImGui.End();
	}

	void LoadMidiMappings()
	{
		try
		{
			if (JsonNode.Parse(File.ReadAllText(MidiSettingsPath)) is not JsonObject mappings) return;
			midiLearned.Clear();
			foreach (var item in mappings)
				if (item.Value is JsonValue value && value.TryGetValue<int>(out var cc) && cc is >= 0 and <= 7 or >= 16 and <= 23)
					midiLearned[item.Key] = cc;
		}
		catch (FileNotFoundException) { }
		catch (Exception error) { midiDeviceStatus = $"MIDI mappings: {error.Message}"; }
	}

	void SaveMidiMappings()
	{
		try
		{
			var mappings = new JsonObject();
			foreach (var (key, cc) in midiLearned) mappings[key] = cc;
			Directory.CreateDirectory(Path.GetDirectoryName(MidiSettingsPath)!);
			var temporary = MidiSettingsPath + ".tmp";
			File.WriteAllText(temporary, mappings.ToJsonString());
			File.Move(temporary, MidiSettingsPath, overwrite: true);
		}
		catch (Exception error) { midiDeviceStatus = $"MIDI mappings: {error.Message}"; }
	}
}
#endif
