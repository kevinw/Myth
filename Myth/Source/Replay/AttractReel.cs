using System.Text.Json;
using System.Text.Json.Serialization;

namespace Myth;

// One entry in the title-screen attract playlist (Assets/Attract/attract.json). Clips are raw
// recordings; the in/out points that make a clip into a demo shot are authored here, not baked
// into the clip.
sealed class AttractEntry
{
	public string Clip { get; set; } = "";  // filename under Assets/Attract/, e.g. "burrow_secret.myrp"
	public int Start { get; set; }           // in-point frame (silently fast-forwarded to on start); 0 = clip start
	public int End { get; set; } = -1;       // out-point frame; -1 = play to the clip's natural end
}

// The authored sequence of clips the title screen loops through. Lives in bundled content, so it
// packs into Content.pak automatically (Assets/Attract/ isn't excluded by ContentPakIncludes).
sealed class AttractReel
{
	const string Folder = "Attract";
	const string PlaylistKey = Folder + "/attract.json";

	readonly AttractEntry[] entries;
	int index;

	AttractReel(AttractEntry[] entries) => this.entries = entries;

	public static AttractReel? Load()
	{
		if (!Assets.Content.Exists(PlaylistKey)) return null;
		var entries = JsonSerializer.Deserialize(Assets.Content.ReadAllText(PlaylistKey), AttractJsonContext.Default.AttractEntryArray);
		return entries is { Length: > 0 } ? new AttractReel(entries) : null;
	}

	public AttractEntry Current => entries[index];
	public int Count => entries.Length;
	public void Advance() => index = (index + 1) % entries.Length;

	public static ReplayClip LoadClip(string clipName)
	{
		using var stream = Assets.Content.OpenRead($"{Folder}/{clipName}");
		return ReplayClip.Read(stream);
	}
}

// Case-insensitive so the hand-authored lowercase keys (clip/start/end) map to the properties.
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(AttractEntry[]))]
internal partial class AttractJsonContext : JsonSerializerContext;
