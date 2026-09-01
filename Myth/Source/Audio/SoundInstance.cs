using System.Numerics;

namespace Myth;

public sealed class SoundInstance : IDisposable
{
	string eventPath;
	public string EventPath => eventPath;
#if BROWSER_JS_FMOD
	readonly int instance;
	bool released;

	internal SoundInstance(int instance, string path)
	{
		this.instance = instance;
		this.eventPath = path;
	}

	public void Start()
	{
		ThrowIfReleased();
		BrowserFmod.StartEvent(instance);
	}

	public void Stop(bool immediate = false)
	{
		ThrowIfReleased();
		BrowserFmod.StopEvent(instance, immediate);
	}

	public void SetPaused(bool paused)
	{
		ThrowIfReleased();
		BrowserFmod.SetEventPaused(instance, paused);
	}

	public void SetVolume(float volume)
	{
		ThrowIfReleased();
		BrowserFmod.SetEventVolume(instance, volume);
	}

	public int GetTimelinePosition()
	{
		ThrowIfReleased();
		return BrowserFmod.GetTimelinePosition(instance);
	}

	public void SetTimelinePosition(int position)
	{
		ThrowIfReleased();
		BrowserFmod.SetTimelinePosition(instance, position);
	}

	public void SetParameter(string name, float value, bool ignoreSeekSpeed = false)
	{
		ThrowIfReleased();
		BrowserFmod.SetEventParameter(instance, name, value, ignoreSeekSpeed);
	}

	public void SetParameterLabel(string name, string label, bool ignoreSeekSpeed = false)
	{
		ThrowIfReleased();
		BrowserFmod.SetEventParameterLabel(instance, name, label, ignoreSeekSpeed);
	}

	public void Set3DAttributes(Vector3 position, Vector3 velocity = default)
	{
		ThrowIfReleased();
		BrowserFmod.SetEvent3DAttributes(instance, position.X, position.Y, position.Z, velocity.X, velocity.Y, velocity.Z);
	}

	public void Release()
	{
		if (released)
			return;

		BrowserFmod.ReleaseEvent(instance);
		released = true;
	}
#else
	FMOD.Studio.EventInstance instance;
	bool released;

	internal SoundInstance(FMOD.Studio.EventInstance instance, string path)
	{
		this.instance = instance;
		this.eventPath = path;
	}

	public void Start()
	{
		ThrowIfReleased();
		Audio.Check(instance.start(), "Failed to start FMOD event instance");
	}

	public void Stop(bool immediate = false)
	{
		ThrowIfReleased();
		var mode = immediate ? FMOD.Studio.STOP_MODE.IMMEDIATE : FMOD.Studio.STOP_MODE.ALLOWFADEOUT;
		Audio.Check(instance.stop(mode), "Failed to stop FMOD event instance");
	}

	public void SetPaused(bool paused)
	{
		ThrowIfReleased();
		Audio.Check(instance.setPaused(paused), $"Failed to {(paused ? "pause" : "resume")} FMOD event instance");
	}

	public void SetVolume(float volume)
	{
		ThrowIfReleased();
		Audio.Check(instance.setVolume(volume), "Failed to set FMOD event volume");
	}

	public int GetTimelinePosition()
	{
		ThrowIfReleased();
		Audio.Check(instance.getTimelinePosition(out var position), "Failed to get FMOD event timeline position");
		return position;
	}

	public void SetTimelinePosition(int position)
	{
		ThrowIfReleased();
		Audio.Check(instance.setTimelinePosition(position), "Failed to set FMOD event timeline position");
	}

	public void SetParameter(string name, float value, bool ignoreSeekSpeed = false)
	{
		ThrowIfReleased();
		Audio.Check(instance.setParameterByName(name, value, ignoreSeekSpeed), "Failed to set FMOD event parameter", name);
	}

	public void SetParameterLabel(string name, string label, bool ignoreSeekSpeed = false)
	{
		ThrowIfReleased();
		Audio.Check(instance.setParameterByNameWithLabel(name, label, ignoreSeekSpeed), "Failed to set FMOD event parameter", name, label);
	}

	public void Set3DAttributes(Vector3 position, Vector3 velocity = default)
	{
		ThrowIfReleased();
		var attributes = Audio.Create3DAttributes(position, velocity);
		Audio.Check(instance.set3DAttributes(attributes), "Failed to set FMOD event 3D attributes");
	}

	public void Release()
	{
		if (released)
			return;

		Audio.Check(instance.release(), "Failed to release FMOD event instance");
		instance.clearHandle();
		released = true;
	}

#endif

	public void Dispose()
		=> Release();

	void ThrowIfReleased()
	{
		if (released)
			throw new ObjectDisposedException(nameof(SoundInstance));
	}
}
