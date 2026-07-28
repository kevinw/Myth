using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Foster.Framework;
using OutLoud;

namespace Myth;

public partial class Game
{
	public const float AudioPixelsPerUnit = 72;

	// Each voice is its own FMOD event event:/voice/<name>; speakers pick one. The syllable
	// inventory (labels/vowels in manifest.json) is shared across voices.
	public static string BudVoiceName => "Kev";
	public static string PlayerVoiceName => "Kev";
	// Loaded from Assets/Voice/ by LoadVoice(): per-clip vowel tags, syllable label names
	// (for the FMOD labeled "syllable" parameter), and the English->German cache.
	Vowel[]? voiceClipVowels;
	string[]? voiceClipNames;
	Dictionary<string, string>? voiceGerman;

	// Falls back to 32 / random-clip babble until the bake (manifest.json) exists.
	public int VoiceSyllableCount => voiceClipVowels?.Length ?? 32;
	// Bud is a small companion -> higher, slightly quicker voice. Pitch in semitones.
	public VoiceProfile BudVoiceProfile => new(VoiceSyllableCount, BasePitch: 4f, PitchVariance: 2f, CharsPerSecond: 26f, ClipVowels: voiceClipVowels);
	public VoiceProfile PlayerVoiceProfile => new(VoiceSyllableCount, BasePitch: -3f, PitchVariance: 1.5f, CharsPerSecond: 22f, ClipVowels: voiceClipVowels);

	// German translation of an (already whitespace-normalized) line, for vowel-melody colouring.
	public string? GermanFor(string normalizedEnglish)
		=> voiceGerman != null && voiceGerman.TryGetValue(normalizedEnglish, out var g) ? g : null;

	void LoadVoice()
	{
		voiceClipVowels = null;
		voiceClipNames = null;
		voiceGerman = null;
		try
		{
			const string manifest = "Voice/manifest.json";
			if (Assets.Content.FileExists(manifest))
			{
				using var doc = JsonDocument.Parse(Assets.Content.ReadAllText(manifest));
				if (doc.RootElement.TryGetProperty("vowels", out var arr))
				{
					var vowels = new List<Vowel>();
					foreach (var e in arr.EnumerateArray())
						if (ParseVowel(e.GetString()) is { } v)
							vowels.Add(v);
					voiceClipVowels = [.. vowels];
				}
				if (doc.RootElement.TryGetProperty("names", out var nameArr))
				{
					var names = new List<string>();
					foreach (var e in nameArr.EnumerateArray())
						if (e.GetString() is { } name)
							names.Add(name);
					voiceClipNames = [.. names];
				}
			}

			const string de = "Voice/voice-de.json";
			// Source-generated (not reflection) so it works in the trimmed/AOT browser
			// build, where reflection-based JSON is disabled. See VoiceJsonContext below.
			if (Assets.Content.FileExists(de) && JsonSerializer.Deserialize(Assets.Content.ReadAllText(de), VoiceJsonContext.Default.DictionaryStringString) is { } map)
			{
				voiceGerman = new(map.Count);
				foreach (var (k, v) in map)
					voiceGerman[NormalizeSpeech(k)] = v;
			}
		}
		catch (Exception e)
		{
			Console.Error.WriteLine($"Failed to load voice assets: {e}");
		}
	}

	static Vowel? ParseVowel(string? s) => s switch
	{
		"a" => Vowel.A,
		"e" => Vowel.E,
		"i" => Vowel.I,
		"o" => Vowel.O,
		"u" => Vowel.U,
		_ => null
	};

	// Captures a speaker's voice + position so OutLoud can stay engine-agnostic; one per spoken line.
	sealed class VoiceSink(Game game, Actor speaker, Vector2 offset, string voice) : ISyllableSink
	{
		public void Play(int syllable, float pitch, float gain) => game.PlayVoiceSyllable(voice, syllable, pitch, gain, speaker.Position + offset);
	}

	public ISyllableSink CreateVoiceSink(Actor speaker, Vector2 offset, string voice) => new VoiceSink(this, speaker, offset, voice);

	static bool VoiceEnabled = true;

	void PlayVoiceSyllable(string voice, int syllable, float pitch, float gain, Vector2 position)
	{
		if (Simulating || AudioMuted || !AudioEventsEnabled || !Audio.Initialized || !VoiceEnabled)
			return;

		var path = $"voice/{voice}";
		try
		{
			using var instance = Audio.CreateEvent($"event:/{path}");
			// Labeled "syllable" parameter ("ba", "scho", …) so clips are legible in FMOD;
			// fall back to the raw index if names weren't baked.
			if (voiceClipNames is { } names && syllable >= 0 && syllable < names.Length)
				instance.SetParameterLabel("syllable", names[syllable], true);
			else
				instance.SetParameter("syllable", syllable);
			instance.SetParameter("pitch", pitch);
			SetAudioEventPosition(instance, position);
			instance.Start();
		}
		catch (Exception e)
		{
			if (audioFailures.Add(path))
				Console.Error.WriteLine($"Failed to play voice syllable: {e}");
		}
	}

	Audio Audio => Manager.Audio;
	public bool AudioEventsEnabled { get; }

	readonly HashSet<string> audioFailures = [];
	SoundInstance? music;
	string? musicArea;

	static float PlayerHitSnapshotHoldTime => 0.12f;
	const string PlayerHitSnapshotPath = "snapshot:/player_hit";

	SoundInstance? playerHitSnapshot;
	float playerHitSnapshotTimer;

	float? roomReverb;

	public readonly record struct MusicReloadState(bool WasPlaying, int TimelinePosition, string? Area);

	bool NoAudio => Simulating || AudioMuted || !Manager.AudioEnabled || !Audio.Initialized;

	public void PlayAudioEvent(string path, string parameterName, float parameterFloat, Vector2? position = null)
	{
		if (NoAudio) return;

		try
		{
			using var instance = CreateAudioEvent(path, position);
			if (instance == null) return;

			if (parameterName != "")
				instance.SetParameter(parameterName, parameterFloat, true);

			instance.Start();
		}
		catch (Exception e)
		{
			Console.WriteLine(e);
		}

	}

	public void PlayAudioEvent(string path, string parameterName = "", string parameterLabel = "", Vector2? position = null)
	{
		if (NoAudio) return;

		try
		{
			using var instance = CreateAudioEvent(path, position);
			if (instance == null) return;

			if (parameterName != "" || parameterLabel != "")
				instance.SetParameterLabel(parameterName, parameterLabel, true);

			instance.Start();
		}
		catch (Exception e)
		{
			Console.WriteLine(e);
		}
	}

	public SoundInstance? CreateAudioEvent(string path, Vector2? position = null)
	{
		if (NoAudio) return null;

		try
		{
			var instance = Audio.CreateEvent($"event:/{path}");
			if (position is { } pos)
				SetAudioEventPosition(instance, pos);
			return instance;
		}
		catch (Exception e)
		{
			Console.WriteLine(e);
			return null;
		}
	}

	// Set a numeric FMOD parameter on a live instance. Guarded + logged-once so callers
	// can drive parameters that may not be authored in the event yet (FMOD throws on an
	// unknown parameter) without spamming or crashing.
	public void SetAudioEventParameter(SoundInstance? instance, string name, float value)
	{
		if (NoAudio || instance == null) return;

		try
		{
			instance.SetParameter(name, value);
		}
		catch (Exception e)
		{
			if (audioFailures.Add($"param:{name}"))
				Console.Error.WriteLine($"Failed to set FMOD parameter '{name}': {e}");
		}
	}

	public void SetAudioEventPosition(SoundInstance? instance, Vector2 position, Vector2? relativeTo = null)
	{
		if (NoAudio || instance == null)
			return;

		try
		{
			var effectivePosition = position;
			if (relativeTo is { } origin)
				effectivePosition = Camera + new Vector2(Width, Height) / 2 + position - origin;

			instance.Set3DAttributes(ToAudioPosition(effectivePosition));
		}
		catch (Exception e)
		{
			Console.WriteLine(e);
		}
	}

	void UpdateAudioListener()
	{
		if (!Audio.Initialized)
			return;

		Audio.SetListenerAttributes(ToAudioPosition(Camera + new Vector2(Width, Height) / 2));
	}

	static Vector3 ToAudioPosition(Vector2 position)
	{
		return new(-position.X / AudioPixelsPerUnit, position.Y / AudioPixelsPerUnit, 0);
	}

	public void StopMusic(bool immediate = false)
	{
		if (music == null)
			return;

		try
		{
			music.Stop(immediate);
			music.Release();
		}
		catch (Exception e)
		{
			Console.Error.WriteLine($"Failed to stop background music: {e}");
		}

		music = null;
		musicArea = null;
	}

	// Hand off the live background-music instance without stopping it, so a freshly
	// recreated Game (the next gym run) can keep it playing with no gap. Nulls our
	// own references so Dispose()'s StopMusic() no-ops instead of releasing it.
	public (SoundInstance? Music, string? Area) DetachMusic()
	{
		var carried = (music, musicArea);
		music = null;
		musicArea = null;
		return carried;
	}

	// Take ownership of a music instance handed off from a previous Game (see
	// DetachMusic). Skips StartMusic so there's no stop/start gap.
	public void AdoptMusic(SoundInstance carried, string? area)
	{
		music = carried;
		musicArea = area;
	}

	public void RefreshMusic()
	{
		if (!Manager.MusicEnabled)
		{
			StopMusic(true);
			return;
		}

		if (room == null)
		{
			return;
		}

		StartMusic();
		ApplyMusicArea(room);
	}

	public MusicReloadState CaptureMusicReloadState()
	{
		if (music == null)
			return new(false, 0, null);

		var state = new MusicReloadState(true, music.GetTimelinePosition(), musicArea);
		StopMusic(true);
		return state;
	}

	public void RestoreMusicReloadState(MusicReloadState state)
	{
		if (!state.WasPlaying || !Manager.MusicEnabled || !Audio.Initialized)
			return;

		try
		{
			music = Audio.CreateEvent(Manager.BackgroundMusicEventPath);
			var area = state.Area ?? Manager.DefaultMusicArea;
			music.SetParameterLabel(Manager.BackgroundMusicAreaParameter, area);
			music.SetTimelinePosition(state.TimelinePosition);
			music.Start();
			musicArea = area;
		}
		catch (Exception e)
		{
			if (audioFailures.Add($"{Manager.BackgroundMusicEventPath}:reload"))
				Console.Error.WriteLine($"Failed to restore background music after bank reload: {e}");
			music = null;
			musicArea = null;
			RefreshMusic();
		}
	}


	void StartMusic()
	{
		if (!Manager.MusicEnabled || !Audio.Initialized || music != null)
			return;

		try
		{
			music = Audio.CreateEvent(Manager.BackgroundMusicEventPath);
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

	void ApplyMusicArea(Room forRoom)
	{
		if (!Manager.MusicEnabled)
		{
			StopMusic(true);
			return;
		}

		var prevMusicArea = musicArea;
		var area = MusicRegions.Resolve(forRoom) ?? Manager.DefaultMusicArea;
		if (string.IsNullOrEmpty(area))
			return;

		StartMusic();
		if (music == null)
		{
			return;
		}

		try
		{
			music.SetParameterLabel(Manager.BackgroundMusicAreaParameter, area);
			if (prevMusicArea == "Silence") // HACK: Start() on an empty fmod timeline previously, means we need to call start here. there's probably a better way to check "isplaying" or something here...
				music.Start();
			musicArea = area;
		}
		catch (Exception e)
		{
			if (audioFailures.Add($"{Manager.BackgroundMusicAreaParameter}:{area}"))
				Console.Error.WriteLine($"Failed to set background music area '{area}': {e}");
		}
	}

	void ApplyRoomReverb(Room forRoom)
	{
		var reverb = EffectiveRoomReverb(forRoom);
		if (!AudioEventsEnabled || !Audio.Initialized || roomReverb == reverb)
			return;

		try
		{
			Audio.SetParameter(Manager.RoomReverbParameter, reverb, true);
			roomReverb = reverb;
		}
		catch (Exception e)
		{
			if (audioFailures.Add(Manager.RoomReverbParameter))
				Console.Error.WriteLine($"Failed to set room reverb '{reverb:0.###}': {e}");
		}
	}

	static float EffectiveRoomReverb(Room forRoom)
	{
#if BROWSER
		return BrowserPlatform.IsMobileBrowser() ? 0.0f : forRoom.Reverb;
#else
		return forRoom.Reverb;
#endif
	}

	public void TriggerPlayerHitSnapshot()
	{
		if (!AudioEventsEnabled || !Audio.Initialized)
			return;

		playerHitSnapshotTimer = PlayerHitSnapshotHoldTime;
		if (playerHitSnapshot != null)
			return;

		try
		{
			playerHitSnapshot = Audio.CreateEvent(PlayerHitSnapshotPath);
			playerHitSnapshot.Start();
		}
		catch (Exception e)
		{
			playerHitSnapshot = null;
			playerHitSnapshotTimer = 0;
			if (audioFailures.Add(PlayerHitSnapshotPath))
				Console.Error.WriteLine($"Failed to start FMOD snapshot '{PlayerHitSnapshotPath}': {e}");
		}
	}

	void UpdatePlayerHitSnapshot()
	{
		if (playerHitSnapshot == null)
			return;

		playerHitSnapshotTimer -= Time.Delta;
		if (playerHitSnapshotTimer > 0)
			return;

		StopPlayerHitSnapshot(false);
	}

	void StopPlayerHitSnapshot(bool immediate)
	{
		if (playerHitSnapshot == null)
			return;

		try
		{
			playerHitSnapshot.Stop(immediate);
			playerHitSnapshot.Release();
		}
		catch (Exception e)
		{
			if (audioFailures.Add($"{PlayerHitSnapshotPath}:stop"))
				Console.Error.WriteLine($"Failed to stop FMOD snapshot '{PlayerHitSnapshotPath}': {e}");
		}
		finally
		{
			playerHitSnapshot = null;
			playerHitSnapshotTimer = 0;
		}
	}
}

// Source-generated metadata for voice-de.json (English -> German line cache). Lets
// LoadVoice() deserialize without reflection, which is disabled in the trimmed/AOT
// browser build.
[JsonSerializable(typeof(Dictionary<string, string>))]
internal partial class VoiceJsonContext : JsonSerializerContext;
