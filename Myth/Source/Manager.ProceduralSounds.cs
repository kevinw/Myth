#if EDITOR && !IOS && !BROWSER
using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using ImGuiNET;

namespace Myth;

public partial class Manager
{
	const string ProceduralPreviewBus = "bus:/Gameplay/SFX";
	static readonly JsonSerializerOptions ProceduralSoundJson = CreateProceduralSoundJson();
	static readonly string[] ProceduralWaveNames = Enum.GetNames<ProceduralWave>();
	static readonly ProceduralSoundPreset[] ProceduralPresets = Enum.GetValues<ProceduralSoundPreset>();

	bool proceduralSoundEditorOpen;
	bool proceduralSoundsLoaded;
	bool proceduralSoundDirty;
	bool proceduralSoundRenderDirty;
	bool proceduralMissingEventsLoaded;
	string proceduralSoundNewName = "";
	string proceduralSoundStatus = "";
	string? proceduralSoundSelected;
	string? proceduralSoundPromotingEvent;
	string? proceduralMissingEventSelected;
	ProceduralSoundPatch? proceduralSoundDraft;
	ProceduralSoundRender proceduralSoundRender;
	Task<string>? proceduralSoundPromotion;
	readonly List<string> proceduralSoundNames = [];
	readonly List<string> proceduralMissingEvents = [];
	readonly float[] proceduralWaveformMin = new float[256];
	readonly float[] proceduralWaveformMax = new float[256];
	int proceduralMissingRuntimeCount = -1;

	static string ProceduralSoundSourceDirectory
		=> Path.Join(Assets.SourcesPath, "Sounds", "Procedural");

	static string ProceduralSoundWavDirectory
		=> Path.Join(Assets.SourcesPath, "Sounds", "FMOD", "Assets", "Procedural");

	static JsonSerializerOptions CreateProceduralSoundJson()
	{
		var options = new JsonSerializerOptions
		{
			IncludeFields = true,
			WriteIndented = true,
		};
		options.Converters.Add(new JsonStringEnumConverter());
		return options;
	}

	void ProceduralSoundEditorImGui()
	{
		if (!proceduralSoundEditorOpen)
			return;

		if (!proceduralSoundsLoaded)
			RefreshProceduralSoundList();
		CompleteProceduralSoundPromotion();
		if (!proceduralMissingEventsLoaded || proceduralMissingRuntimeCount != Game.MissingAudioEvents.Count)
			RefreshMissingAudioEvents();

		ImGui.SetNextWindowSize(new Vector2(500, 720), ImGuiCond.FirstUseEver);
		if (!ImGui.Begin("Procedural Sounds", ref proceduralSoundEditorOpen))
		{
			ImGui.End();
			return;
		}

		DrawMissingAudioEventRow();
		ImGui.Separator();
		DrawProceduralSoundFileRow();

		if (proceduralSoundDraft is { } patch)
		{
			DrawProceduralSoundActions(patch);
			RefreshProceduralSoundRender();
			DrawProceduralWaveform();
			ImGui.Separator();

			ImGui.BeginChild("ProceduralSoundParameters");
			DrawProceduralSoundParameters(patch);
			ImGui.EndChild();
		}
		else
			ImGui.TextDisabled("Create a sound to begin.");

		ImGui.End();
		if (!proceduralSoundEditorOpen && proceduralSoundDirty)
			SaveProceduralSound();
	}

	void DrawMissingAudioEventRow()
	{
		if (proceduralMissingEvents.Count == 0)
		{
			ImGui.TextDisabled("No missing code-referenced FMOD events.");
			return;
		}

		proceduralMissingEventSelected ??= proceduralMissingEvents[0];
		ImGui.SetNextItemWidth(300);
		if (ImGui.BeginCombo("##MissingAudioEvent", proceduralMissingEventSelected))
		{
			foreach (var path in proceduralMissingEvents)
			{
				var selected = path == proceduralMissingEventSelected;
				if (ImGui.Selectable(path, selected))
					proceduralMissingEventSelected = path;
				if (selected)
					ImGui.SetItemDefaultFocus();
			}
			ImGui.EndCombo();
		}

		ImGui.SameLine();
		var hasDraft = HasProceduralSound(proceduralMissingEventSelected);
		if (ImGui.Button(hasDraft ? "Edit Draft" : "Create Draft"))
		{
			if (hasDraft)
				SelectProceduralSound(proceduralMissingEventSelected);
			else
				CreateProceduralSound(proceduralMissingEventSelected);
		}
	}

	void DrawProceduralSoundFileRow()
	{
		ImGui.SetNextItemWidth(180);
		if (ImGui.BeginCombo("##ProceduralSound", proceduralSoundSelected ?? "select sound"))
		{
			foreach (var name in proceduralSoundNames)
			{
				var selected = name == proceduralSoundSelected;
				if (ImGui.Selectable(name, selected))
					SelectProceduralSound(name);
				if (selected)
					ImGui.SetItemDefaultFocus();
			}
			ImGui.EndCombo();
		}

		ImGui.SameLine();
		ImGui.SetNextItemWidth(130);
		ImGui.InputTextWithHint("##NewProceduralSound", "new sound", ref proceduralSoundNewName, 128);
		ImGui.SameLine();
		var newName = proceduralSoundNewName.Trim().ToLowerInvariant();
		var validName = IsValidProceduralSoundName(newName) && !HasProceduralSound(newName);
		ImGui.BeginDisabled(!validName);
		if (ImGui.Button("New"))
			CreateProceduralSound(newName);
		ImGui.EndDisabled();
	}

	void CreateProceduralSound(string name)
	{
		if (!IsValidProceduralSoundName(name))
		{
			proceduralSoundStatus = $"Invalid event path: {name}";
			return;
		}
		if (proceduralSoundDirty)
			SaveProceduralSound();
		proceduralSoundDraft = ProceduralSoundSynth.Preset(ProceduralSoundPreset.Blip, 1);
		proceduralSoundSelected = name;
		proceduralSoundNewName = "";
		proceduralSoundDirty = true;
		proceduralSoundRenderDirty = true;
		SaveProceduralSound();
		RefreshProceduralSoundList(name);
	}

	void DrawProceduralSoundActions(ProceduralSoundPatch patch)
	{
		if (ImGui.Button("Play"))
			PreviewProceduralSound();
		ImGui.SameLine();
		if (ImGui.Button("Save"))
			SaveProceduralSound();
		ImGui.SameLine();
		if (ImGui.Button("Bake WAV"))
			BakeProceduralSound();
		ImGui.SameLine();
		var existsInFmod = proceduralSoundSelected != null && Audio.EventExists($"event:/{proceduralSoundSelected}");
		ImGui.BeginDisabled(proceduralSoundPromotion != null || existsInFmod);
		if (ImGui.Button(existsInFmod ? "In FMOD" : "Add to FMOD"))
			StartProceduralSoundPromotion();
		ImGui.EndDisabled();
		ImGui.SameLine();
		if (ImGui.Button("Open FMOD"))
			OpenFmodStudio();

		var status = !string.IsNullOrEmpty(proceduralSoundStatus)
			? proceduralSoundStatus
			: proceduralSoundDirty ? "modified" : "";
		if (!string.IsNullOrEmpty(status))
		{
			ImGui.SameLine();
			ImGui.SetCursorPosX(ImGui.GetCursorPosX() + MathF.Max(0, ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize(status).X));
			ImGui.TextDisabled(status);
		}

		ImGui.TextUnformatted("Copy from:");
		ImGui.SameLine();
		ImGui.SetNextItemWidth(180);
		if (ImGui.BeginCombo("##CopyProceduralSound", "select sound"))
		{
			foreach (var name in proceduralSoundNames)
			{
				if (name == proceduralSoundSelected)
					continue;
				if (ImGui.Selectable(name))
					CopyProceduralSoundSettings(name);
			}
			ImGui.EndCombo();
		}

		ImGui.TextUnformatted("Preset:");
		ImGui.SameLine();
		for (var i = 0; i < ProceduralPresets.Length; i++)
		{
			var preset = ProceduralPresets[i];
			if (ImGui.SmallButton(preset.ToString()))
			{
				proceduralSoundDraft = ProceduralSoundSynth.Preset(preset, patch.Seed + 1);
				MarkProceduralSoundEdited();
				PreviewProceduralSound();
			}
			if (i + 1 < ProceduralPresets.Length)
				ImGui.SameLine();
		}

		if (ImGui.SmallButton("Mutate"))
		{
			proceduralSoundDraft = ProceduralSoundSynth.Mutate(patch, patch.Seed + 1);
			MarkProceduralSoundEdited();
		}
	}

	void DrawProceduralWaveform()
	{
		var size = new Vector2(MathF.Max(1, ImGui.GetContentRegionAvail().X), 90);
		var position = ImGui.GetCursorScreenPos();
		ImGui.InvisibleButton("##ProceduralWaveform", size);

		var draw = ImGui.GetWindowDrawList();
		draw.AddRectFilled(position, position + size, ImGui.GetColorU32(new Vector4(0.05f, 0.04f, 0.08f, 1)));
		var middle = position.Y + size.Y * 0.5f;
		draw.AddLine(new Vector2(position.X, middle), new Vector2(position.X + size.X, middle), ImGui.GetColorU32(new Vector4(0.3f, 0.28f, 0.4f, 1)));

		var color = ImGui.GetColorU32(new Vector4(0.37f, 0.8f, 0.89f, 1));
		for (var i = 0; i < proceduralWaveformMin.Length; i++)
		{
			var x = position.X + i * size.X / (proceduralWaveformMin.Length - 1);
			var top = middle - proceduralWaveformMax[i] * size.Y * 0.48f;
			var bottom = middle - proceduralWaveformMin[i] * size.Y * 0.48f;
			draw.AddLine(new Vector2(x, top), new Vector2(x, bottom), color);
		}

		ImGui.TextDisabled($"{proceduralSoundRender.Duration:0.000}s · {proceduralSoundRender.Samples.Length} samples"
			+ (proceduralSoundRender.Clipped > 0 ? $" · {proceduralSoundRender.Clipped} clipped" : ""));
	}

	void DrawProceduralSoundParameters(ProceduralSoundPatch patch)
	{
		var wave = (int)patch.Wave;
		MarkProceduralSoundEdited(ImGui.Combo("Wave", ref wave, ProceduralWaveNames, ProceduralWaveNames.Length));
		patch.Wave = (ProceduralWave)wave;
		MarkProceduralSoundEdited(ImGui.InputInt("Seed", ref patch.Seed));
		ProceduralSlider("Volume", ref patch.Volume);

		if (ImGui.CollapsingHeader("Envelope", ImGuiTreeNodeFlags.DefaultOpen))
		{
			ProceduralSlider("Attack", ref patch.Attack);
			ProceduralSlider("Sustain", ref patch.Sustain);
			ProceduralSlider("Punch", ref patch.Punch);
			ProceduralSlider("Decay", ref patch.Decay);
		}
		if (ImGui.CollapsingHeader("Tone", ImGuiTreeNodeFlags.DefaultOpen))
		{
			ProceduralSlider("Start frequency", ref patch.StartFrequency);
			ProceduralSlider("Minimum frequency", ref patch.MinimumFrequency);
			ProceduralSlider("Frequency slide", ref patch.FrequencySlide, signed: true);
			ProceduralSlider("Frequency delta slide", ref patch.FrequencyDeltaSlide, signed: true);
			ProceduralSlider("Duty", ref patch.Duty);
			ProceduralSlider("Duty sweep", ref patch.DutySweep, signed: true);
		}
		if (ImGui.CollapsingHeader("Modulation", ImGuiTreeNodeFlags.DefaultOpen))
		{
			ProceduralSlider("Vibrato depth", ref patch.VibratoDepth);
			ProceduralSlider("Vibrato speed", ref patch.VibratoSpeed);
			ProceduralSlider("Arpeggio amount", ref patch.ArpeggioAmount, signed: true);
			ProceduralSlider("Arpeggio speed", ref patch.ArpeggioSpeed);
			ProceduralSlider("Repeat speed", ref patch.RepeatSpeed);
			ProceduralSlider("Flanger offset", ref patch.FlangerOffset, signed: true);
			ProceduralSlider("Flanger sweep", ref patch.FlangerSweep, signed: true);
		}
		if (ImGui.CollapsingHeader("Filters", ImGuiTreeNodeFlags.DefaultOpen))
		{
			ProceduralSlider("Low-pass cutoff", ref patch.LowPassCutoff);
			ProceduralSlider("Low-pass sweep", ref patch.LowPassSweep, signed: true);
			ProceduralSlider("Low-pass resonance", ref patch.LowPassResonance);
			ProceduralSlider("High-pass cutoff", ref patch.HighPassCutoff);
			ProceduralSlider("High-pass sweep", ref patch.HighPassSweep, signed: true);
		}
	}

	void ProceduralSlider(string label, ref float value, bool signed = false)
		=> MarkProceduralSoundEdited(ImGui.SliderFloat(label, ref value, signed ? -1 : 0, 1, "%.3f", ImGuiSliderFlags.AlwaysClamp));

	void MarkProceduralSoundEdited(bool changed = true)
	{
		if (!changed)
			return;
		proceduralSoundDirty = true;
		proceduralSoundRenderDirty = true;
		proceduralSoundStatus = "";
	}

	void RefreshProceduralSoundRender(bool force = false)
	{
		if (proceduralSoundDraft == null || (!force && (!proceduralSoundRenderDirty || ImGui.IsAnyItemActive())))
			return;

		try
		{
			proceduralSoundRender = ProceduralSoundSynth.Render(proceduralSoundDraft);
			proceduralSoundRenderDirty = false;
			for (var bin = 0; bin < proceduralWaveformMin.Length; bin++)
			{
				var from = bin * proceduralSoundRender.Samples.Length / proceduralWaveformMin.Length;
				var to = (bin + 1) * proceduralSoundRender.Samples.Length / proceduralWaveformMin.Length;
				var min = 0f;
				var max = 0f;
				for (var sample = from; sample < to; sample++)
				{
					min = MathF.Min(min, proceduralSoundRender.Samples[sample]);
					max = MathF.Max(max, proceduralSoundRender.Samples[sample]);
				}
				proceduralWaveformMin[bin] = min;
				proceduralWaveformMax[bin] = max;
			}
		}
		catch (Exception e)
		{
			proceduralSoundStatus = $"Render failed: {e.Message}";
		}
	}

	void PreviewProceduralSound()
	{
		RefreshProceduralSoundRender(force: true);
		if (proceduralSoundRender.Pcm16.Length == 0)
			return;
		try
		{
			Audio.StopPcmSounds();
			Audio.PlayPcm16(proceduralSoundRender.Pcm16, ProceduralSoundRender.SampleRate, ProceduralPreviewBus);
			proceduralSoundStatus = "";
		}
		catch (Exception e)
		{
			proceduralSoundStatus = $"Preview failed: {e.Message}";
		}
	}

	void BakeProceduralSound()
	{
		if (proceduralSoundSelected == null || proceduralSoundDraft == null)
			return;
		try
		{
			var path = BakeProceduralSoundWav();
			proceduralSoundStatus = $"Baked {Path.GetRelativePath(Assets.AssetsPath, path)}";
			RevealInFinder(path);
		}
		catch (Exception e)
		{
			proceduralSoundStatus = $"Bake failed: {e.Message}";
		}
	}

	string BakeProceduralSoundWav()
	{
		SaveProceduralSound();
		RefreshProceduralSoundRender(force: true);
		var path = ProceduralSoundPath(ProceduralSoundWavDirectory, proceduralSoundSelected!, ".wav");
		proceduralSoundRender.WriteWav(path);
		return path;
	}

	void StartProceduralSoundPromotion()
	{
		if (proceduralSoundSelected == null || proceduralSoundDraft == null || proceduralSoundPromotion != null)
			return;

		try
		{
			var processes = Process.GetProcessesByName("fmodstudio");
			foreach (var process in processes)
				process.Dispose();
			if (processes.Length > 0)
				throw new InvalidOperationException("Close FMOD Studio before adding the event.");

			var wavPath = BakeProceduralSoundWav();
			proceduralSoundStatus = $"Adding event:/{proceduralSoundSelected}…";
			proceduralSoundPromotingEvent = proceduralSoundSelected;
			proceduralSoundPromotion = AddProceduralSoundToFmod(
				proceduralSoundSelected,
				wavPath,
				proceduralSoundRender.Duration);
		}
		catch (Exception e)
		{
			proceduralSoundStatus = $"FMOD failed: {e.Message}";
		}
	}

	void CompleteProceduralSoundPromotion()
	{
		if (proceduralSoundPromotion is not { IsCompleted: true } promotion)
			return;

		try
		{
			proceduralSoundStatus = promotion.GetAwaiter().GetResult();
			Game.MissingAudioEvents.Remove(proceduralSoundPromotingEvent ?? "");
		}
		catch (Exception e)
		{
			proceduralSoundStatus = $"FMOD failed: {e.Message}";
		}
		proceduralSoundPromotion = null;
		proceduralSoundPromotingEvent = null;
		RefreshMissingAudioEvents();
	}

	static async Task<string> AddProceduralSoundToFmod(string eventPath, string wavPath, float duration)
	{
		var projectPath = Path.GetFullPath(Path.Join(Assets.AssetsPath, FmodProjectPath));
		var projectDirectory = Path.GetDirectoryName(projectPath)!;
		var modulePath = Path.Join(projectDirectory, "Scripts", "modules", "procedural-one-shot.js");
		if (!File.Exists(modulePath))
			throw new FileNotFoundException("FMOD procedural one-shot script not found.", modulePath);

		var cacheDirectory = Path.Join(projectDirectory, ".cache");
		Directory.CreateDirectory(cacheDirectory);
		var runnerPath = Path.Join(cacheDirectory, "myth-procedural-one-shot.js");
		var request = JsonSerializer.Serialize(new { eventPath, wavPath, duration });
		File.WriteAllText(runnerPath,
			$"var procedural = studio.system.require({JsonSerializer.Serialize(modulePath)});\n"
			+ $"procedural.createOneShot({request});\n");

		await RunFmodStudioCli(projectDirectory, "-script", runnerPath, projectPath);
		await RunFmodStudioCli(projectDirectory, "-build", "-banks", "Master, SFX", "-platforms", "Desktop", projectPath);
		return $"Added event:/{eventPath} and rebuilt SFX.bank";
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

	void RefreshMissingAudioEvents()
	{
		proceduralMissingEvents.Clear();
		if (Audio.Initialized)
		{
			foreach (var path in GeneratedAudioEvents.Paths)
				AddMissingAudioEvent(path);
			foreach (var path in Game.MissingAudioEvents)
				AddMissingAudioEvent(path);
		}
		proceduralMissingEvents.Sort(StringComparer.OrdinalIgnoreCase);
		proceduralMissingRuntimeCount = Game.MissingAudioEvents.Count;
		proceduralMissingEventsLoaded = true;

		if (proceduralMissingEventSelected == null || !proceduralMissingEvents.Contains(proceduralMissingEventSelected))
			proceduralMissingEventSelected = proceduralMissingEvents.Count > 0 ? proceduralMissingEvents[0] : null;
	}

	void AddMissingAudioEvent(string path)
	{
		if (Audio.EventExists($"event:/{path}") || proceduralMissingEvents.Contains(path))
			return;
		proceduralMissingEvents.Add(path);
	}

	void RefreshProceduralSoundList(string? select = null)
	{
		Directory.CreateDirectory(ProceduralSoundSourceDirectory);
		proceduralSoundNames.Clear();
		foreach (var file in Directory.EnumerateFiles(ProceduralSoundSourceDirectory, "*.json", SearchOption.AllDirectories))
			proceduralSoundNames.Add(Path.ChangeExtension(Path.GetRelativePath(ProceduralSoundSourceDirectory, file), null)
				.Replace(Path.DirectorySeparatorChar, '/'));
		proceduralSoundNames.Sort(StringComparer.OrdinalIgnoreCase);
		proceduralSoundsLoaded = true;

		select ??= proceduralSoundSelected;
		if (select != null && HasProceduralSound(select))
			SelectProceduralSound(select);
		else if (proceduralSoundNames.Count > 0)
			SelectProceduralSound(proceduralSoundNames[0]);
	}

	void SelectProceduralSound(string name)
	{
		if (name == proceduralSoundSelected && proceduralSoundDraft != null)
			return;
		if (proceduralSoundDirty)
			SaveProceduralSound();

		try
		{
			proceduralSoundDraft = LoadProceduralSound(name);
			proceduralSoundSelected = name;
			proceduralSoundDirty = false;
			proceduralSoundRenderDirty = true;
			proceduralSoundStatus = "";
		}
		catch (Exception e)
		{
			proceduralSoundStatus = $"Load failed: {e.Message}";
		}
	}

	void CopyProceduralSoundSettings(string name)
	{
		try
		{
			proceduralSoundDraft = LoadProceduralSound(name);
			MarkProceduralSoundEdited();
			proceduralSoundStatus = $"Copied settings from {name}";
		}
		catch (Exception e)
		{
			proceduralSoundStatus = $"Copy failed: {e.Message}";
		}
	}

	static ProceduralSoundPatch LoadProceduralSound(string name)
	{
		var path = ProceduralSoundPath(ProceduralSoundSourceDirectory, name, ".json");
		return JsonSerializer.Deserialize<ProceduralSoundPatch>(File.ReadAllText(path), ProceduralSoundJson)
			?? throw new InvalidDataException("Patch was empty.");
	}

	void SaveProceduralSound()
	{
		if (proceduralSoundSelected == null || proceduralSoundDraft == null)
			return;
		try
		{
			var path = ProceduralSoundPath(ProceduralSoundSourceDirectory, proceduralSoundSelected, ".json");
			Directory.CreateDirectory(Path.GetDirectoryName(path)!);
			File.WriteAllText(path, JsonSerializer.Serialize(proceduralSoundDraft, ProceduralSoundJson) + "\n");
			proceduralSoundDirty = false;
			if (!HasProceduralSound(proceduralSoundSelected))
			{
				proceduralSoundNames.Add(proceduralSoundSelected);
				proceduralSoundNames.Sort(StringComparer.OrdinalIgnoreCase);
			}
			proceduralSoundStatus = $"Saved {Path.GetFileName(path)}";
		}
		catch (Exception e)
		{
			proceduralSoundStatus = $"Save failed: {e.Message}";
		}
	}

	bool HasProceduralSound(string? name)
	{
		if (name == null)
			return false;
		foreach (var candidate in proceduralSoundNames)
			if (string.Equals(candidate, name, StringComparison.OrdinalIgnoreCase))
				return true;
		return false;
	}

	static bool IsValidProceduralSoundName(string name)
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

	static string ProceduralSoundPath(string root, string name, string extension)
		=> Path.Join(root, name.Replace('/', Path.DirectorySeparatorChar) + extension);
}
#endif
