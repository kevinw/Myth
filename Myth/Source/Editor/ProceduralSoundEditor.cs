#if EDITOR && !BROWSER
using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Foster.Framework;
using ImGuiNET;

namespace Myth;

sealed class ProceduralSoundEditor
{
	enum PendingAction
	{
		None,
		Select,
		New,
	}

	const string PreviewBus = "bus:/Gameplay/SFX";
	static readonly JsonSerializerOptions Json = CreateJson();
	static readonly string[] WaveNames = Enum.GetNames<ProceduralWave>();
	static readonly ProceduralSoundPreset[] Presets = Enum.GetValues<ProceduralSoundPreset>();

	readonly Audio audio;
	public bool Open;
	bool loaded;
	bool dirty;
	bool renderDirty;
	bool missingEventsLoaded;
	string newName = "";
	string status = "";
	string? selected;
	string? syncingEvent;
	string? pendingName;
	ProceduralSoundPatch? draft;
	ProceduralSoundRender render;
	Task<string>? fmodSync;
	readonly List<string> names = [];
	readonly List<string> missingEvents = [];
	readonly List<ProceduralSoundPatch> history = [];
	readonly float[] waveformMin = new float[256];
	readonly float[] waveformMax = new float[256];
	PendingAction pendingAction;
	bool historyPending;
	bool pathPopupRequested;
	bool saveAs;
	bool confirmSwitchRequested;
	int historyIndex = -1;
	int savedHistoryIndex = -1;
	int missingRuntimeCount = -1;

	static string SourceDirectory
		=> Path.Join(Assets.SourcesPath, "Sounds", "Procedural");

	static string WavDirectory
		=> Path.Join(Assets.SourcesPath, "Sounds", "FMOD", "Assets", "Procedural");

	public ProceduralSoundEditor(Audio audio)
	{
		this.audio = audio;
	}

	public void AudioBanksReloaded()
		=> missingEventsLoaded = false;

	static JsonSerializerOptions CreateJson()
	{
		var options = new JsonSerializerOptions
		{
			IncludeFields = true,
			WriteIndented = true,
		};
		options.Converters.Add(new JsonStringEnumConverter());
		return options;
	}

	public void Draw(Action? onBegin = null)
	{
		if (!Open)
			return;

		if (!loaded)
			RefreshList();
		CompleteFmodSync();
		if (!missingEventsLoaded || missingRuntimeCount != Game.MissingAudioEvents.Count)
			RefreshMissingEvents();

		ImGui.SetNextWindowSize(new Vector2(500, 720), ImGuiCond.FirstUseEver);
		if (!ImGui.Begin("Procedural Sounds", ref Open))
		{
			onBegin?.Invoke();
			ImGui.End();
			return;
		}
		onBegin?.Invoke();

		DrawDraftRow();

		if (draft != null)
		{
			DrawActions();
			RefreshRender();
			DrawWaveform();
			ImGui.Separator();

			ImGui.BeginChild("ProceduralSoundParameters");
			DrawParameters(draft);
			ImGui.EndChild();
		}
		else
			ImGui.TextDisabled("Create a sound to begin.");

		DrawPathPopup();
		DrawSwitchPopup();
		ImGui.End();
	}

	void DrawDraftRow()
	{
		ImGui.TextUnformatted("Draft:");
		ImGui.SameLine();
		ImGui.SetNextItemWidth(260);
		if (ImGui.BeginCombo("##ProceduralSound", selected ?? "select sound"))
		{
			if (names.Count > 0)
				ImGui.SeparatorText("Saved drafts");
			foreach (var name in names)
			{
				var isSelected = name == selected;
				var label = IsMissingAudioEvent(name) ? $"{name}  (missing FMOD)" : name;
				if (ImGui.Selectable(label, isSelected))
					RequestAction(PendingAction.Select, name);
				if (isSelected)
					ImGui.SetItemDefaultFocus();
			}

			var missingHeader = false;
			foreach (var name in missingEvents)
			{
				if (Has(name) || name == selected)
					continue;
				if (!missingHeader)
				{
					ImGui.SeparatorText("Missing FMOD events");
					missingHeader = true;
				}
				if (ImGui.Selectable($"+ {name}"))
					RequestAction(PendingAction.New, name);
			}
			ImGui.EndCombo();
		}

		ImGui.SameLine();
		if (ImGui.Button("New…"))
		{
			saveAs = false;
			pathPopupRequested = true;
		}
		ImGui.SameLine();
		ImGui.BeginDisabled(draft == null);
		if (ImGui.Button("Save As…"))
		{
			saveAs = true;
			pathPopupRequested = true;
		}
		ImGui.EndDisabled();
	}

	void DrawPathPopup()
	{
		if (pathPopupRequested)
		{
			pathPopupRequested = false;
			newName = "";
			ImGui.OpenPopup("Draft Path");
		}

		var open = true;
		if (!ImGui.BeginPopupModal("Draft Path", ref open, ImGuiWindowFlags.AlwaysAutoResize))
			return;

		ImGui.TextUnformatted(saveAs ? "Save current draft as:" : "Create draft:");
		ImGui.SetNextItemWidth(300);
		if (ImGui.IsWindowAppearing())
			ImGui.SetKeyboardFocusHere();
		ImGui.InputTextWithHint("##FilePath", "event/path", ref newName, 128);

		var name = newName.Trim().ToLowerInvariant();
		var validName = IsValidName(name) && !Has(name);
		ImGui.BeginDisabled(!validName);
		if (ImGui.Button(saveAs ? "Save As" : "Create", new Vector2(90, 0)))
		{
			if (saveAs)
				SaveAs(name);
			else
				RequestAction(PendingAction.New, name);
			ImGui.CloseCurrentPopup();
		}
		ImGui.EndDisabled();
		ImGui.SameLine();
		if (ImGui.Button("Cancel", new Vector2(90, 0)) || !open)
		{
			newName = "";
			ImGui.CloseCurrentPopup();
		}
		ImGui.EndPopup();
	}

	void DrawSwitchPopup()
	{
		if (confirmSwitchRequested)
		{
			confirmSwitchRequested = false;
			ImGui.OpenPopup("Unsaved procedural sound");
		}

		var open = true;
		if (!ImGui.BeginPopupModal("Unsaved procedural sound", ref open, ImGuiWindowFlags.AlwaysAutoResize))
			return;

		ImGui.TextUnformatted($"Save changes to '{selected}'?");
		if (ImGui.Button("Save", new Vector2(90, 0)))
		{
			Save();
			if (!dirty)
			{
				PerformPendingAction();
				ImGui.CloseCurrentPopup();
			}
		}
		ImGui.SameLine();
		if (ImGui.Button("Discard", new Vector2(90, 0)))
		{
			PerformPendingAction();
			ImGui.CloseCurrentPopup();
		}
		ImGui.SameLine();
		if (ImGui.Button("Cancel", new Vector2(90, 0)) || !open)
		{
			ClearPendingAction();
			ImGui.CloseCurrentPopup();
		}
		ImGui.EndPopup();
	}

	void Create(string name)
	{
		if (!IsValidName(name))
		{
			status = $"Invalid event path: {name}";
			return;
		}

		draft = ProceduralSoundSynth.Preset(ProceduralSoundPreset.Blip, 1);
		selected = name;
		newName = "";
		dirty = true;
		renderDirty = true;
		status = "new, unsaved draft";
		ResetHistory(saved: false);
	}

	void DrawActions()
	{
		var patch = draft!;
		ImGui.BeginDisabled(historyIndex <= 0 && !historyPending);
		if (ImGui.Button("<##ProceduralSoundHistoryBack"))
			MoveHistory(-1);
		ImGui.EndDisabled();
		ImGui.SameLine();
		ImGui.BeginDisabled(historyPending
			|| historyIndex < 0
			|| historyIndex + 1 >= history.Count);
		if (ImGui.Button(">##ProceduralSoundHistoryForward"))
			MoveHistory(1);
		ImGui.EndDisabled();
		ImGui.SameLine();
		ImGui.TextDisabled($"{historyIndex + 1}/{history.Count}");
		ImGui.SameLine();
		if (ImGui.Button("Play"))
			Preview();
		ImGui.SameLine();
		if (ImGui.Button("Save"))
			Save();
		ImGui.SameLine();
		if (ImGui.Button("Bake WAV"))
			Bake();
		ImGui.SameLine();
		var existsInFmod = selected != null && audio.EventExists($"event:/{selected}");
		ImGui.BeginDisabled(fmodSync != null);
		if (ImGui.Button(existsInFmod ? "Update FMOD" : "Add to FMOD"))
			StartFmodSync(existsInFmod);
		ImGui.EndDisabled();
		ImGui.SameLine();
		if (ImGui.Button("…"))
			ImGui.OpenPopup("ProceduralSoundMore");
		if (ImGui.BeginPopup("ProceduralSoundMore"))
		{
			if (ImGui.Selectable("Open FMOD"))
				FmodStudio.Open();
			var canCopy = names.Count > 1
				|| names.Count == 1 && names[0] != selected;
			if (canCopy)
			{
				ImGui.SeparatorText("Copy settings from");
				foreach (var name in names)
					if (name != selected && ImGui.Selectable(name))
						CopySettings(name);
			}
			ImGui.EndPopup();
		}

		var message = !string.IsNullOrEmpty(status)
			? status
			: dirty ? "modified" : "";
		if (!string.IsNullOrEmpty(message))
		{
			ImGui.SameLine();
			ImGui.SetCursorPosX(ImGui.GetCursorPosX() + MathF.Max(0, ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize(message).X));
			ImGui.TextDisabled(message);
		}

		ImGui.TextUnformatted("Preset:");
		ImGui.SameLine();
		for (var i = 0; i < Presets.Length; i++)
		{
			var preset = Presets[i];
			if (ImGui.SmallButton(preset.ToString()))
			{
				ReplaceDraft(ProceduralSoundSynth.Preset(preset, patch.Seed + 1));
				patch = draft!;
			}
			if (i + 1 < Presets.Length)
				ImGui.SameLine();
		}

		if (ImGui.SmallButton("Mutate"))
			ReplaceDraft(ProceduralSoundSynth.Mutate(patch, patch.Seed + 1));
	}

	void DrawWaveform()
	{
		var size = new Vector2(MathF.Max(1, ImGui.GetContentRegionAvail().X), 90);
		var position = ImGui.GetCursorScreenPos();
		ImGui.InvisibleButton("##ProceduralWaveform", size);

		var draw = ImGui.GetWindowDrawList();
		draw.AddRectFilled(position, position + size, ImGui.GetColorU32(new Vector4(0.05f, 0.04f, 0.08f, 1)));
		var middle = position.Y + size.Y * 0.5f;
		draw.AddLine(new Vector2(position.X, middle), new Vector2(position.X + size.X, middle), ImGui.GetColorU32(new Vector4(0.3f, 0.28f, 0.4f, 1)));

		var color = ImGui.GetColorU32(new Vector4(0.37f, 0.8f, 0.89f, 1));
		for (var i = 0; i < waveformMin.Length; i++)
		{
			var x = position.X + i * size.X / (waveformMin.Length - 1);
			var top = middle - waveformMax[i] * size.Y * 0.48f;
			var bottom = middle - waveformMin[i] * size.Y * 0.48f;
			draw.AddLine(new Vector2(x, top), new Vector2(x, bottom), color);
		}

		ImGui.TextDisabled($"{render.Duration:0.000}s · {render.Samples.Length} samples"
			+ (render.Clipped > 0 ? $" · {render.Clipped} clipped" : ""));
	}

	void DrawParameters(ProceduralSoundPatch patch)
	{
		var wave = (int)patch.Wave;
		if (ImGui.Combo("Wave", ref wave, WaveNames, WaveNames.Length))
		{
			patch.Wave = (ProceduralWave)wave;
			MarkEdited(discrete: true);
		}
		MarkEdited(ImGui.InputInt("Seed", ref patch.Seed));
		if (ImGui.IsItemDeactivatedAfterEdit())
			CommitHistory();
		Slider("Volume", ref patch.Volume);

		if (ImGui.CollapsingHeader("Envelope", ImGuiTreeNodeFlags.DefaultOpen))
		{
			Slider("Attack", ref patch.Attack);
			Slider("Sustain", ref patch.Sustain);
			Slider("Punch", ref patch.Punch);
			Slider("Decay", ref patch.Decay);
		}
		if (ImGui.CollapsingHeader("Tone", ImGuiTreeNodeFlags.DefaultOpen))
		{
			Slider("Start frequency", ref patch.StartFrequency);
			Slider("Minimum frequency", ref patch.MinimumFrequency);
			Slider("Frequency slide", ref patch.FrequencySlide, signed: true);
			Slider("Frequency delta slide", ref patch.FrequencyDeltaSlide, signed: true);
			Slider("Duty", ref patch.Duty);
			Slider("Duty sweep", ref patch.DutySweep, signed: true);
			Slider("Noise mix", ref patch.NoiseMix);
			Slider("Wave shape", ref patch.WaveShape, signed: true);
			Slider("FM depth", ref patch.FmDepth);
			Slider("FM ratio", ref patch.FmRatio);
		}
		if (ImGui.CollapsingHeader("Modulation", ImGuiTreeNodeFlags.DefaultOpen))
		{
			Slider("Vibrato depth", ref patch.VibratoDepth);
			Slider("Vibrato speed", ref patch.VibratoSpeed);
			Slider("Tremolo depth", ref patch.TremoloDepth);
			Slider("Tremolo speed", ref patch.TremoloSpeed);
			Slider("Arpeggio amount", ref patch.ArpeggioAmount, signed: true);
			Slider("Arpeggio speed", ref patch.ArpeggioSpeed);
			Slider("Repeat speed", ref patch.RepeatSpeed);
			Slider("Flanger offset", ref patch.FlangerOffset, signed: true);
			Slider("Flanger sweep", ref patch.FlangerSweep, signed: true);
		}
		if (ImGui.CollapsingHeader("Filters", ImGuiTreeNodeFlags.DefaultOpen))
		{
			Slider("Low-pass cutoff", ref patch.LowPassCutoff);
			Slider("Low-pass sweep", ref patch.LowPassSweep, signed: true);
			Slider("Low-pass resonance", ref patch.LowPassResonance);
			Slider("High-pass cutoff", ref patch.HighPassCutoff);
			Slider("High-pass sweep", ref patch.HighPassSweep, signed: true);
		}
		if (ImGui.CollapsingHeader("Lo-fi & echo", ImGuiTreeNodeFlags.DefaultOpen))
		{
			Slider("Bit crush", ref patch.BitCrush);
			Slider("Sample-rate reduction", ref patch.SampleRateReduction);
			Slider("Delay time", ref patch.DelayTime);
			Slider("Delay feedback", ref patch.DelayFeedback);
			Slider("Delay mix", ref patch.DelayMix);
		}
	}

	void Slider(string label, ref float value, bool signed = false)
	{
		MarkEdited(ImGui.SliderFloat(label, ref value, signed ? -1 : 0, 1, "%.3f", ImGuiSliderFlags.AlwaysClamp));
		if (ImGui.IsItemDeactivatedAfterEdit())
			CommitHistory();
	}

	void MarkEdited(bool changed = true, bool discrete = false)
	{
		if (!changed)
			return;
		dirty = true;
		renderDirty = true;
		historyPending = true;
		status = "";
		if (discrete)
			CommitHistory();
	}

	void ResetHistory(bool saved)
	{
		history.Clear();
		historyIndex = -1;
		savedHistoryIndex = -1;
		historyPending = false;
		if (draft == null)
			return;

		history.Add(draft.Clone());
		historyIndex = 0;
		if (saved)
			savedHistoryIndex = 0;
	}

	void CommitHistory()
	{
		if (!historyPending || draft == null)
			return;

		TruncateHistory();
		history.Add(draft.Clone());
		historyIndex++;
		historyPending = false;
		dirty = historyIndex != savedHistoryIndex;
	}

	void ReplaceDraft(ProceduralSoundPatch patch)
	{
		CommitHistory();
		TruncateHistory();
		draft = patch;
		history.Add(patch.Clone());
		historyIndex++;
		historyPending = false;
		dirty = true;
		renderDirty = true;
		status = "";
		Preview();
	}

	void MoveHistory(int offset)
	{
		CommitHistory();
		var next = historyIndex + offset;
		if (next < 0 || next >= history.Count)
			return;

		historyIndex = next;
		draft = history[next].Clone();
		dirty = next != savedHistoryIndex;
		renderDirty = true;
		status = "";
		Preview();
	}

	void TruncateHistory()
	{
		var removeFrom = historyIndex + 1;
		if (removeFrom >= history.Count)
			return;
		if (savedHistoryIndex >= removeFrom)
			savedHistoryIndex = -1;
		history.RemoveRange(removeFrom, history.Count - removeFrom);
	}

	void RefreshRender(bool force = false)
	{
		if (draft == null || (!force && (!renderDirty || ImGui.IsAnyItemActive())))
			return;

		try
		{
			render = ProceduralSoundSynth.Render(draft);
			renderDirty = false;
			for (var bin = 0; bin < waveformMin.Length; bin++)
			{
				var from = bin * render.Samples.Length / waveformMin.Length;
				var to = (bin + 1) * render.Samples.Length / waveformMin.Length;
				var min = 0f;
				var max = 0f;
				for (var sample = from; sample < to; sample++)
				{
					min = MathF.Min(min, render.Samples[sample]);
					max = MathF.Max(max, render.Samples[sample]);
				}
				waveformMin[bin] = min;
				waveformMax[bin] = max;
			}
		}
		catch (Exception e)
		{
			status = $"Render failed: {e.Message}";
		}
	}

	void Preview()
	{
		RefreshRender(force: true);
		if (render.Pcm16.Length == 0)
			return;
		try
		{
			audio.StopPcmSounds();
			audio.PlayPcm16(render.Pcm16, ProceduralSoundRender.SampleRate, PreviewBus);
			status = "";
		}
		catch (Exception e)
		{
			status = $"Preview failed: {e.Message}";
		}
	}

	void Bake()
	{
		if (selected == null || draft == null)
			return;
		try
		{
			var path = BakeWav();
			status = $"Baked {Path.GetRelativePath(Assets.AssetsPath, path)}";
			RevealInFinder(path);
		}
		catch (Exception e)
		{
			status = $"Bake failed: {e.Message}";
		}
	}

	string BakeWav()
	{
		Save();
		RefreshRender(force: true);
		var path = FilePath(WavDirectory, selected!, ".wav");
		render.WriteWav(path);
		return path;
	}

	void StartFmodSync(bool updating)
	{
		if (selected == null || draft == null || fmodSync != null)
			return;

		try
		{
			var processes = Process.GetProcessesByName("fmodstudio");
			foreach (var process in processes)
				process.Dispose();
			if (processes.Length > 0)
				throw new InvalidOperationException("Close FMOD Studio before syncing the event.");

			var wavPath = BakeWav();
			status = $"{(updating ? "Updating" : "Adding")} event:/{selected}…";
			syncingEvent = selected;
			fmodSync = SyncToFmod(
				selected,
				wavPath,
				render.Duration);
		}
		catch (Exception e)
		{
			status = $"FMOD failed: {e.Message}";
		}
	}

	void CompleteFmodSync()
	{
		if (fmodSync is not { IsCompleted: true } completed)
			return;

		try
		{
			status = completed.GetAwaiter().GetResult();
			Game.MissingAudioEvents.Remove(syncingEvent ?? "");
		}
		catch (Exception e)
		{
			status = $"FMOD failed: {e.Message}";
		}
		fmodSync = null;
		syncingEvent = null;
		RefreshMissingEvents();
	}

	static async Task<string> SyncToFmod(string eventPath, string wavPath, float duration)
	{
		var projectPath = FmodStudio.ProjectPath;
		var projectDirectory = Path.GetDirectoryName(projectPath)!;
		var modulePath = Path.Join(projectDirectory, "Scripts", "modules", "procedural-one-shot.js");
		if (!File.Exists(modulePath))
			throw new FileNotFoundException("FMOD procedural one-shot script not found.", modulePath);

		var cacheDirectory = Path.Join(projectDirectory, ".cache");
		Directory.CreateDirectory(cacheDirectory);
		var runnerPath = Path.Join(cacheDirectory, "myth-procedural-one-shot.js");
		var assetPath = Path.GetRelativePath(Path.Join(projectDirectory, "Assets"), Path.GetFullPath(wavPath))
			.Replace(Path.DirectorySeparatorChar, '/');
		var request = JsonSerializer.Serialize(new { eventPath, wavPath, assetPath, duration });
		File.WriteAllText(runnerPath,
			$"var procedural = studio.system.require({JsonSerializer.Serialize(modulePath)});\n"
			+ $"procedural.syncOneShot({request});\n");

		await RunFmodStudioCli(projectDirectory, "-script", runnerPath, projectPath);
		await RunFmodStudioCli(projectDirectory, "-build", "-banks", "Master, SFX", "-platforms", "Desktop", projectPath);
		return $"Synced event:/{eventPath} and rebuilt SFX.bank";
	}

	static async Task RunFmodStudioCli(string workingDirectory, params string[] arguments)
	{
		var executable = Environment.GetEnvironmentVariable("FMOD_STUDIO_CLI");
		var useMacOpen = string.IsNullOrEmpty(executable) && OperatingSystem.IsMacOS();
		executable ??= useMacOpen ? "/usr/bin/open" : "fmodstudiocl";

		var info = new ProcessStartInfo(executable)
		{
			WorkingDirectory = workingDirectory,
			UseShellExecute = false,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			CreateNoWindow = true,
		};
		string? fmodOutputPath = null;
		string? fmodErrorPath = null;
		if (useMacOpen)
		{
			fmodOutputPath = Path.Join(workingDirectory, ".cache", "myth-fmod-stdout.log");
			fmodErrorPath = Path.Join(workingDirectory, ".cache", "myth-fmod-stderr.log");
			File.WriteAllText(fmodOutputPath, "");
			File.WriteAllText(fmodErrorPath, "");
			info.ArgumentList.Add("-W");
			info.ArgumentList.Add("-a");
			info.ArgumentList.Add("/Applications/FMOD Studio.app");
			info.ArgumentList.Add("--stdout");
			info.ArgumentList.Add(fmodOutputPath);
			info.ArgumentList.Add("--stderr");
			info.ArgumentList.Add(fmodErrorPath);
			info.ArgumentList.Add("--env");
			info.ArgumentList.Add("FMODSTUDIOCMDLINE=1");
			info.ArgumentList.Add("--args");
			info.ArgumentList.Add("-working-dir");
			info.ArgumentList.Add(workingDirectory);
		}
		foreach (var argument in arguments)
			info.ArgumentList.Add(argument);

		using var process = Process.Start(info)
			?? throw new InvalidOperationException($"Could not start {executable}.");
		var outputTask = process.StandardOutput.ReadToEndAsync();
		var errorTask = process.StandardError.ReadToEndAsync();
		await process.WaitForExitAsync();
		var output = (await outputTask).Trim();
		var error = (await errorTask).Trim();
		if (fmodOutputPath != null)
		{
			output = File.Exists(fmodOutputPath) ? File.ReadAllText(fmodOutputPath).Trim() : output;
			error = File.Exists(fmodErrorPath) ? File.ReadAllText(fmodErrorPath).Trim() : error;
		}
		if (process.ExitCode != 0
			|| output.Contains("[script-error]", StringComparison.Ordinal)
			|| output.Contains("Build failed", StringComparison.OrdinalIgnoreCase))
			throw new InvalidOperationException(FmodErrorSummary(output, error));
	}

	static string FmodErrorSummary(string output, string error)
	{
		var lines = output.Split('\n');
		foreach (var line in lines)
			if (line.Contains("[script-error]", StringComparison.Ordinal))
				return line.Trim();
		if (!string.IsNullOrEmpty(error))
			return error.Split('\n')[0].Trim();
		for (var i = lines.Length - 1; i >= 0; i--)
			if (!string.IsNullOrWhiteSpace(lines[i]))
				return lines[i].Trim();
		return "FMOD Studio command failed.";
	}

	void RefreshMissingEvents()
	{
		missingEvents.Clear();
		if (audio.Initialized)
		{
			foreach (var path in GeneratedAudioEvents.Paths)
				AddMissingEvent(path);
			foreach (var path in Game.MissingAudioEvents)
				AddMissingEvent(path);
		}
		missingEvents.Sort(StringComparer.OrdinalIgnoreCase);
		missingRuntimeCount = Game.MissingAudioEvents.Count;
		missingEventsLoaded = true;
	}

	void AddMissingEvent(string path)
	{
		if (audio.EventExists($"event:/{path}") || missingEvents.Contains(path))
			return;
		missingEvents.Add(path);
	}

	void RefreshList(string? select = null)
	{
		Directory.CreateDirectory(SourceDirectory);
		names.Clear();
		foreach (var file in Directory.EnumerateFiles(SourceDirectory, "*.json", SearchOption.AllDirectories))
			names.Add(Path.ChangeExtension(Path.GetRelativePath(SourceDirectory, file), null)
				.Replace(Path.DirectorySeparatorChar, '/'));
		names.Sort(StringComparer.OrdinalIgnoreCase);
		loaded = true;

		select ??= selected;
		if (select != null && Has(select))
			Select(select);
		else if (names.Count > 0)
			Select(names[0]);
	}

	void RequestAction(PendingAction action, string name)
	{
		if (action == PendingAction.Select
			&& name == selected
			&& draft != null)
			return;

		pendingAction = action;
		pendingName = name;
		if (dirty)
			confirmSwitchRequested = true;
		else
			PerformPendingAction();
	}

	void PerformPendingAction()
	{
		var action = pendingAction;
		var name = pendingName;
		ClearPendingAction();
		if (name == null)
			return;
		if (action == PendingAction.Select)
			Select(name);
		else if (action == PendingAction.New)
			Create(name);
	}

	void ClearPendingAction()
	{
		pendingAction = PendingAction.None;
		pendingName = null;
	}

	void Select(string name)
	{
		if (name == selected && draft != null)
			return;

		try
		{
			draft = Load(name);
			selected = name;
			dirty = false;
			renderDirty = true;
			status = "";
			ResetHistory(saved: true);
		}
		catch (Exception e)
		{
			status = $"Load failed: {e.Message}";
		}
	}

	void CopySettings(string name)
	{
		try
		{
			ReplaceDraft(Load(name));
			status = $"Copied settings from {name}";
		}
		catch (Exception e)
		{
			status = $"Copy failed: {e.Message}";
		}
	}

	static ProceduralSoundPatch Load(string name)
	{
		var path = FilePath(SourceDirectory, name, ".json");
		return JsonSerializer.Deserialize<ProceduralSoundPatch>(File.ReadAllText(path), Json)
			?? throw new InvalidDataException("Patch was empty.");
	}

	void Save()
	{
		if (selected == null || draft == null)
			return;
		try
		{
			CommitHistory();
			var path = FilePath(SourceDirectory, selected, ".json");
			Directory.CreateDirectory(Path.GetDirectoryName(path)!);
			File.WriteAllText(path, JsonSerializer.Serialize(draft, Json) + "\n");
			dirty = false;
			savedHistoryIndex = historyIndex;
			if (!Has(selected))
			{
				names.Add(selected);
				names.Sort(StringComparer.OrdinalIgnoreCase);
			}
			status = $"Saved {Path.GetFileName(path)}";
		}
		catch (Exception e)
		{
			status = $"Save failed: {e.Message}";
		}
	}

	void SaveAs(string name)
	{
		if (draft == null || !IsValidName(name) || Has(name))
			return;

		selected = name;
		dirty = true;
		Save();
	}

	bool Has(string? name)
	{
		if (name == null)
			return false;
		foreach (var candidate in names)
			if (string.Equals(candidate, name, StringComparison.OrdinalIgnoreCase))
				return true;
		return false;
	}

	bool IsMissingAudioEvent(string name)
	{
		foreach (var candidate in missingEvents)
			if (candidate == name)
				return true;
		return false;
	}

	static bool IsValidName(string name)
	{
		foreach (var segment in name.Split('/'))
		{
			if (segment.Length == 0 || !char.IsAsciiLetter(segment[0]))
				return false;
			foreach (var character in segment)
				if (!char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_')
					return false;
		}
		return true;
	}

	static void RevealInFinder(string path)
	{
		try
		{
			var info = new ProcessStartInfo("open") { UseShellExecute = false };
			info.ArgumentList.Add("-R");
			info.ArgumentList.Add(path);
			Process.Start(info);
		}
		catch { }
	}

	static string FilePath(string root, string name, string extension)
		=> Path.Join(root, name.Replace('/', Path.DirectorySeparatorChar) + extension);
}

static class FmodStudio
{
	public static string ProjectPath
		=> Path.GetFullPath(Path.Join(Assets.AssetsPath, "Sources/Sounds/FMOD/Myth.fspro"));

	public static void Open()
	{
		if (!File.Exists(ProjectPath))
		{
			Console.Error.WriteLine($"FMOD Studio project not found at '{ProjectPath}'.");
			return;
		}

		try
		{
			if (!OperatingSystem.IsMacOS())
			{
				Process.Start(new ProcessStartInfo(ProjectPath) { UseShellExecute = true });
				return;
			}

			var info = new ProcessStartInfo("/usr/bin/open") { UseShellExecute = false };
			info.ArgumentList.Add("-a");
			info.ArgumentList.Add("FMOD Studio");
			info.ArgumentList.Add(ProjectPath);
			Process.Start(info);
		}
		catch (Exception e)
		{
			Console.Error.WriteLine($"Failed to open FMOD Studio: {e}");
		}
	}
}
#endif
