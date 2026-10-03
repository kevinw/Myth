using System.Text.Json;
using System.Text.Json.Serialization;
using Foster.Framework;

namespace Myth;

public sealed class SaveState
{
	public int Version = 1;
	public HashSet<string> PersistentDestroyedActors = [];
	// Chests that have been opened. They persist as open husks (not destroyed); their individual loot
	// items ride PersistentDestroyedActors via keys derived from the chest's SessionDestroyKey.
	public HashSet<string> OpenedChests = [];
	public HashSet<string> PressedButtons = [];
	public HashSet<string> RevealedDoors = [];
	public HashSet<string> ReadInspectPoints = [];
	public Dictionary<PickupKind, int> Pickups = [];
	public MinimapState Map = new();
	public RespawnState? LastSafeGrounded;
	// Save points the player has activated (touched), by actor id, in activation order,
	// and the id of the most recent one — where death returns the player. Positions are
	// resolved from the live room spec at warp/respawn time (see Game.ResolveSavePoint),
	// so moving a save point in the editor relocates old saves instead of orphaning them.
	public List<string> SavePoints = [];
	public string? LastSavePoint;
	public SessionState Session = new();

	public string ToJsonString()
		=> JsonSerializer.Serialize(this, SaveStateJsonContext.Default.SaveState);

	public static SaveState FromJsonString(string json)
		=> JsonSerializer.Deserialize(json, SaveStateJsonContext.Default.SaveState) ?? new();
}

public sealed class SessionState
{
	public HashSet<string> DestroyedActors = [];
}

public sealed class MinimapState
{
	public List<string> VisitedRoomIds = [];
	public List<MinimapExitState> TakenExits = [];
	public Dictionary<string, RectInt> SeenTiles = [];
}

public readonly record struct MinimapExitState(string A, string B);
public readonly record struct RespawnState(string RoomId, Point2 Position);

[JsonSourceGenerationOptions(IncludeFields = true, UseStringEnumConverter = true, WriteIndented = true)]
[JsonSerializable(typeof(SaveState))]
internal partial class SaveStateJsonContext : JsonSerializerContext;
