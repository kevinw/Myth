using System.Numerics;
using System.Runtime.InteropServices;

namespace Myth;

public sealed class Audio
{
#if BROWSER_JS_FMOD
	bool initialized;

	public bool Initialized => initialized && BrowserFmod.IsInitialized();

	public void Initialize(int maxChannels = 1024, bool liveUpdate = false)
	{
		if (!BrowserFmod.IsInitialized())
			throw new AudioException("FMOD JS bridge was not initialized before managed startup.");
		initialized = true;
	}

	public void LoadBank(string path)
		=> ReloadBanks([path]);

	public void ReloadBanks(IEnumerable<string> paths, int maxChannels = 1024, bool liveUpdate = false)
	{
		Initialize(maxChannels, liveUpdate);
		BrowserFmod.ReloadBanks(paths.Select(NormalizeBrowserPath).ToArray());
	}

	public SoundInstance CreateEvent(string path)
	{
		Initialize();
		return new SoundInstance(BrowserFmod.CreateEvent(path), path);
	}

	public bool EventExists(string path) => false;

	public void PlayEvent(string path)
	{
		using var instance = CreateEvent(path);
		instance.Start();
		instance.Release();
	}

	public void SetParameter(string name, float value, bool ignoreSeekSpeed = false)
	{
		Initialize();
		BrowserFmod.SetParameter(name, value, ignoreSeekSpeed);
	}

	public void SetParameterLabel(string name, string label, bool ignoreSeekSpeed = false)
	{
		Initialize();
		BrowserFmod.SetParameterLabel(name, label, ignoreSeekSpeed);
	}

	public void SetListenerAttributes(Vector3 position, Vector3 velocity = default)
	{
		Initialize();
		BrowserFmod.SetListenerAttributes(position.X, position.Y, position.Z, velocity.X, velocity.Y, velocity.Z);
	}

	public void SetPaused(bool paused)
		=> SetBusPaused("bus:/", paused);

	public void SetBusPaused(string path, bool paused)
	{
		if (Initialized)
			BrowserFmod.SetBusPaused(path, paused);
	}

	public void SetVcaVolume(string path, float volume)
	{
		if (Initialized && !BrowserFmod.SetVcaVolume(path, volume))
			WarnMissingVca(path);
	}

	public string[] GetEventParameterLabels(string eventPath, string parameterName)
	{
		Initialize();
		var labels = BrowserFmod.GetEventParameterLabels(eventPath, parameterName);
		return labels.Length == 0 ? [] : labels.Split('\n');
	}

	internal void Update()
	{
		if (Initialized)
			BrowserFmod.Update();
	}

	// True once when the browser audio output was lost and needs rebuilding.
	public bool ConsumeAudioInterruption()
		=> Initialized && BrowserFmod.ConsumeReinitRequest();

	// Invalidates existing sound instances; callers must restore persistent audio.
	public bool Restart()
		=> Initialized && BrowserFmod.Reinitialize();

	internal void Shutdown()
	{
		if (!initialized)
			return;

		BrowserFmod.Shutdown();
		initialized = false;
	}

	static string NormalizeBrowserPath(string path)
		=> path.Replace('\\', '/').TrimStart('/');

#else
	readonly Dictionary<string, FMOD.Studio.EventDescription> events = [];
	readonly List<FMOD.Studio.Bank> banks = [];
	readonly List<(FMOD.Sound Sound, FMOD.Channel Channel)> pcmSounds = [];
	FMOD.Studio.System system;
	bool initialized;

	public bool Initialized => initialized;

	public void Initialize(int maxChannels = 1024, bool liveUpdate = false)
	{
		if (initialized)
			return;

		Check(FMOD.Studio.System.create(out system), "FMOD Studio system creation failed");

		var studioFlags = liveUpdate ? FMOD.Studio.INITFLAGS.LIVEUPDATE : FMOD.Studio.INITFLAGS.NORMAL;
		Check(system.initialize(maxChannels, studioFlags, FMOD.INITFLAGS.NORMAL, IntPtr.Zero), "FMOD Studio system initialization failed");
		initialized = true;
	}

	public void LoadBank(string path)
	{
		Initialize();
		Check(system.loadBankFile(path, FMOD.Studio.LOAD_BANK_FLAGS.NORMAL, out var bank), "Failed to load FMOD bank", path);
		banks.Add(bank);
		events.Clear();
	}

	public void ReloadBanks(IEnumerable<string> paths, int maxChannels = 1024, bool liveUpdate = false)
	{
		var bankPaths = paths.ToArray();
		Shutdown();
		Initialize(maxChannels, liveUpdate);
		foreach (var path in bankPaths)
			LoadBank(path);
	}

	public SoundInstance CreateEvent(string path)
	{
		Initialize();

		var description = GetEventDescription(path);
		Check(description.createInstance(out var instance), "Failed to create FMOD event instance", path);
		return new SoundInstance(instance, path);
	}

	public bool EventExists(string path)
		=> initialized && system.getEvent(path, out _) == FMOD.RESULT.OK;

	public void PlayEvent(string path)
	{
		using var instance = CreateEvent(path);
		instance.Start();
		instance.Release();
	}

	public void SetParameter(string name, float value, bool ignoreSeekSpeed = false)
	{
		Initialize();
		Check(system.setParameterByName(name, value, ignoreSeekSpeed), "Failed to set FMOD global parameter", name);
	}

	public void SetParameterLabel(string name, string label, bool ignoreSeekSpeed = false)
	{
		Initialize();
		Check(system.setParameterByNameWithLabel(name, label, ignoreSeekSpeed), "Failed to set FMOD global parameter", name, label);
	}

	public void SetListenerAttributes(Vector3 position, Vector3 velocity = default)
	{
		Initialize();
		var attributes = Create3DAttributes(position, velocity);
		Check(system.setListenerAttributes(0, attributes), "Failed to set FMOD listener attributes");
	}

	public void SetPaused(bool paused)
		=> SetBusPaused("bus:/", paused);

	public void SetBusPaused(string path, bool paused)
	{
		if (!initialized)
			return;

		Check(system.getBus(path, out var bus), "Failed to get FMOD bus", path);
		Check(bus.setPaused(paused), $"Failed to {(paused ? "pause" : "resume")} FMOD bus", path);
	}

	public void SetVcaVolume(string path, float volume)
	{
		if (!initialized)
			return;
		if (system.getVCA(path, out var vca) != FMOD.RESULT.OK)
		{
			WarnMissingVca(path);
			return;
		}
		Check(vca.setVolume(volume), "Failed to set FMOD VCA volume", path);
	}

	public void PlayPcm16(byte[] pcm, int sampleRate, string busPath = "bus:/")
	{
		ArgumentNullException.ThrowIfNull(pcm);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
		if (pcm.Length == 0)
			return;

		Initialize();
		Check(system.getCoreSystem(out var core), "Failed to get FMOD Core system");

		var info = new FMOD.CREATESOUNDEXINFO
		{
			cbsize = Marshal.SizeOf<FMOD.CREATESOUNDEXINFO>(),
			length = (uint)pcm.Length,
			numchannels = 1,
			defaultfrequency = sampleRate,
			format = FMOD.SOUND_FORMAT.PCM16,
		};

		FMOD.Sound sound = default;
		try
		{
			Check(core.createSound(pcm, FMOD.MODE.OPENMEMORY | FMOD.MODE.OPENRAW | FMOD.MODE.CREATESAMPLE | FMOD.MODE.LOOP_OFF | FMOD.MODE._2D, ref info, out sound),
				"Failed to create FMOD PCM sound");
			Check(system.getBus(busPath, out var bus), "Failed to get FMOD bus", busPath);
			Check(bus.lockChannelGroup(), "Failed to lock FMOD bus channel group", busPath);
			try
			{
				Check(system.flushCommands(), "Failed to create FMOD bus channel group", busPath);
				Check(bus.getChannelGroup(out var group), "Failed to get FMOD bus channel group", busPath);
				Check(core.playSound(sound, group, false, out var channel), "Failed to play FMOD PCM sound", busPath);
				pcmSounds.Add((sound, channel));
				sound.clearHandle();
			}
			finally
			{
				Check(bus.unlockChannelGroup(), "Failed to unlock FMOD bus channel group", busPath);
			}
		}
		finally
		{
			if (sound.hasHandle())
				sound.release();
		}
	}

	public void StopPcmSounds()
	{
		for (var i = pcmSounds.Count - 1; i >= 0; i--)
		{
			pcmSounds[i].Channel.stop();
			pcmSounds[i].Sound.release();
		}
		pcmSounds.Clear();
	}

	public string[] GetEventParameterLabels(string eventPath, string parameterName)
	{
		Initialize();

		var description = GetEventDescription(eventPath);
		Check(description.getParameterDescriptionByName(parameterName, out var parameter), "Failed to find FMOD parameter on event", parameterName, eventPath);

		var count = Math.Max(0, (int)MathF.Floor(parameter.maximum - parameter.minimum) + 1);
		var labels = new List<string>(count);
		for (int i = 0; i < count; i++)
		{
			Check(description.getParameterLabelByName(parameterName, i, out var label), "Failed to find FMOD parameter label", parameterName, eventPath);
			labels.Add(label);
		}

		return labels.ToArray();
	}

	internal void Update()
	{
		if (initialized)
		{
			Check(system.update(), "FMOD Studio system update failed");
			for (var i = pcmSounds.Count - 1; i >= 0; i--)
			{
				var result = pcmSounds[i].Channel.isPlaying(out var playing);
				if (result == FMOD.RESULT.OK && playing)
					continue;
				pcmSounds[i].Sound.release();
				pcmSounds.RemoveAt(i);
			}
		}
	}

	// Native FMOD recovers its own audio device.
	public bool ConsumeAudioInterruption() => false;

	public bool Restart() => false;

	internal void Shutdown()
	{
		if (!initialized)
			return;

		StopPcmSounds();
		events.Clear();

		for (int i = banks.Count - 1; i >= 0; i--)
			Check(banks[i].unload(), "Failed to unload FMOD bank");
		banks.Clear();

		Check(system.flushCommands(), "FMOD Studio command flush failed");
		Check(system.release(), "FMOD Studio system release failed");
		system.clearHandle();
		initialized = false;
	}

	FMOD.Studio.EventDescription GetEventDescription(string path)
	{
		if (events.TryGetValue(path, out var description))
			return description;

		Check(system.getEvent(path, out description), "Failed to find FMOD event", path);
		events.Add(path, description);
		return description;
	}
#endif

	internal static void Check(FMOD.RESULT result, string message, string? message2 = null, string? message3 = null)
	{
		if (result == FMOD.RESULT.OK)
			return;

		var fmodErrorString = FMOD.Error.String(result);

		if (message3 != null)
			throw new AudioException($"{message} {message2} {message3} - {result} ({fmodErrorString})");
		else if (message2 != null)
			throw new AudioException($"{message} {message2} - {result} ({fmodErrorString})");
		else
			throw new AudioException($"{message} - {result} ({fmodErrorString})");
	}

	internal static FMOD.ATTRIBUTES_3D Create3DAttributes(Vector3 position, Vector3 velocity = default)
	{
		return new()
		{
			position = ToFmodVector(position),
			velocity = ToFmodVector(velocity),
			forward = ToFmodVector(Vector3.UnitZ),
			up = ToFmodVector(-Vector3.UnitY)
		};
	}

	static FMOD.VECTOR ToFmodVector(Vector3 vector)
	{
		return new()
		{
			x = vector.X,
			y = vector.Y,
			z = vector.Z
		};
	}

	readonly HashSet<string> warnedMissingVcas = [];

	// A renamed or unbuilt VCA shouldn't take audio down, but its slider silently doing nothing
	// should still be noticed.
	void WarnMissingVca(string path)
	{
		if (warnedMissingVcas.Add(path))
			Console.Error.WriteLine($"Warning: FMOD VCA '{path}' not found; its volume setting does nothing.");
	}
}
