using System.Numerics;
using Foster.Framework;

namespace TinyLink;

public partial class Game
{
	public const float AudioPixelsPerUnit = 72;

	Audio Audio => Manager.Audio;
	public bool AudioEventsEnabled { get; }

	private readonly HashSet<string> audioFailures = [];
	private SoundInstance? music;
	private string? musicArea;

	public void PlayAudioEvent(string path, string parameterName = "", string parameterLabel = "", Vector2? position = null)
	{
		try
		{
			using var instance = CreateAudioEvent(path, position);
			if (instance == null)
				return;

			if (parameterName != "" || parameterLabel != "")
				instance.SetParameterLabel(parameterName, parameterLabel, true);

			instance.Start();
		}
		catch(Exception e)
		{
			Console.WriteLine(e);
		}
	}

	public SoundInstance? CreateAudioEvent(string path, Vector2? position = null)
	{
		if (!AudioEventsEnabled || !Audio.Initialized)
			return null;

		try
		{
			var instance = Audio.CreateEvent($"event:/{path}");
			if (position is { } pos)
				SetAudioEventPosition(instance, pos);
			return instance;
		}
		catch(Exception e)
		{
			Console.WriteLine(e);
			return null;
		}
	}

	public void SetAudioEventPosition(SoundInstance? instance, Vector2 position, Vector2? relativeTo = null)
	{
		if (instance == null || !Audio.Initialized)
			return;

		try
		{
			var effectivePosition = position;
			if (relativeTo is { } origin)
				effectivePosition = Camera + new Vector2(Width, Height) / 2 + position - origin;

			instance.Set3DAttributes(ToAudioPosition(effectivePosition));
		}
		catch(Exception e)
		{
			Console.WriteLine(e);
		}
	}

	private void UpdateAudioListener()
	{
		if (!Audio.Initialized)
			return;

		Audio.SetListenerAttributes(ToAudioPosition(Camera + new Vector2(Width, Height) / 2));
	}

	private static Vector3 ToAudioPosition(Vector2 position)
	{
		return new(-position.X / AudioPixelsPerUnit, position.Y / AudioPixelsPerUnit, 0);
	}

	public void StopMusic(bool immediate = false) {
		if (music == null)
			return;

		try {
			music.Stop(immediate);
			music.Release();
		} catch (Exception e) {
			Console.Error.WriteLine($"Failed to stop background music: {e}");
		}

		music = null;
		musicArea = null;
	}

	public void RefreshMusic()
	{
		if (!Manager.MusicEnabled)
		{
			StopMusic(true);
			return;
		}

		if (room == null)
			return;

		StartMusic();
		ApplyMusicArea(room);
	}

	private void StartMusic()
	{
		if (!Manager.MusicEnabled || !Audio.Initialized || music != null)
			return;

		try
		{
			music = Audio.CreateEvent(Manager.BackgroundMusicEventPath);
			music.SetParameterLabel(Manager.BackgroundMusicAreaParameter, Manager.DefaultMusicArea, true);
			musicArea = Manager.DefaultMusicArea;
			music.Start();
		}
		catch (Exception e)
		{
			if (audioFailures.Add(Manager.BackgroundMusicEventPath))
				Console.Error.WriteLine($"Failed to start background music '{Manager.BackgroundMusicEventPath}': {e}");
			music = null;
			musicArea = null;
		}
	}

	private void ApplyMusicArea(Room forRoom)
	{
		if (!Manager.MusicEnabled)
		{
			StopMusic(true);
			return;
		}

		if (string.IsNullOrEmpty(forRoom.MusicArea))
			return;

		StartMusic();
		if (music == null || musicArea == forRoom.MusicArea)
			return;

		try
		{
			music.SetParameterLabel(Manager.BackgroundMusicAreaParameter, forRoom.MusicArea, true);
			musicArea = forRoom.MusicArea;
		}
		catch (Exception e)
		{
			if (audioFailures.Add($"{Manager.BackgroundMusicAreaParameter}:{forRoom.MusicArea}"))
				Console.Error.WriteLine($"Failed to set background music area '{forRoom.MusicArea}': {e}");
		}
	}
}
