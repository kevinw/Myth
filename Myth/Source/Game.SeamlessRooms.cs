using System.Numerics;
using Foster.Framework;

namespace Myth;

public partial class Game
{
	static bool SeamlessEdgeTransitions => true;
	static int SeamlessWakeMargin => TileSize * 2;

	readonly Dictionary<string, Room> residentRooms = new(StringComparer.OrdinalIgnoreCase);
	readonly HashSet<Room> awakeRooms = [];
	readonly Dictionary<Room, bool> residentRoomInversions = [];
	readonly List<Room> visibleResidentRooms = [];
	readonly HashSet<string> desiredResidentRoomIds = new(StringComparer.OrdinalIgnoreCase);
	readonly List<string> residentRoomRemovalScratch = [];
	readonly HashSet<string> connectedRoomIds = new(StringComparer.OrdinalIgnoreCase);
	readonly List<Room> connectedRoomScratch = [];
	Room? seamlessResidencyCurrent;
	RectInt seamlessWorldBounds;

	bool SeamlessRoomsActive => SeamlessEdgeTransitions && !IsEditorPreview && !GymMode;
	internal bool SeamlessMovementActive => SeamlessRoomsActive;

	internal bool RoomIsVisible(Room candidate)
		=> !SeamlessRoomsActive ? candidate == room || candidate == nextRoom : awakeRooms.Contains(candidate);

	internal List<Room>? VisibleResidentRooms
		=> SeamlessRoomsActive ? visibleResidentRooms : null;

	void UpdateSeamlessRooms()
	{
		if (!SeamlessRoomsActive || room == null || nextRoom != null)
			return;

		if (seamlessResidencyCurrent != room)
			ResetSeamlessResidency();

		RefreshSeamlessResidency();
		WakeNearbyRooms();
	}

	void ResetSeamlessResidency()
	{
		foreach (var candidate in residentRooms.Values)
			if (candidate != room)
			{
				DestroyResidentRoomActors(candidate);
				candidate.RoomGeoChanged -= OnRoomGeoChanged;
			}
		residentRooms.Clear();
		awakeRooms.Clear();
		residentRoomInversions.Clear();
		visibleResidentRooms.Clear();
		seamlessResidencyCurrent = room;
		if (room == null)
			return;

		residentRooms.Add(room.Id, room);
		awakeRooms.Add(room);
		residentRoomInversions.Add(room, invertedNow);
		visibleResidentRooms.Add(room);
		RebuildSeamlessWorldBounds();
	}

	void RefreshSeamlessResidency()
	{
		if (room == null)
			return;

		var residencyArea = SeamlessCameraArea().Inflate(
			(int)MathF.Ceiling(FbZoomedWidth),
			(int)MathF.Ceiling(FbZoomedHeight));
		desiredResidentRoomIds.Clear();
		desiredResidentRoomIds.Add(room.Id);
		foreach (var authored in Assets.RoomsById.Values)
			if (authored.Sublayer == room.Sublayer && authored.WorldBounds.Overlaps(residencyArea))
				desiredResidentRoomIds.Add(authored.Id);

		residentRoomRemovalScratch.Clear();
		foreach (var (id, _) in residentRooms)
			if (!desiredResidentRoomIds.Contains(id))
				residentRoomRemovalScratch.Add(id);

		foreach (var id in residentRoomRemovalScratch)
		{
			var departing = residentRooms[id];
			DestroyResidentRoomActors(departing);
			departing.RoomGeoChanged -= OnRoomGeoChanged;
			residentRooms.Remove(id);
			awakeRooms.Remove(departing);
			residentRoomInversions.Remove(departing);
			visibleResidentRooms.Remove(departing);
		}
		if (residentRoomRemovalScratch.Count > 0)
			View?.PruneInactiveRoomCaches();

		foreach (var id in desiredResidentRoomIds)
			if (!residentRooms.ContainsKey(id) && Assets.GetRoom(id) is { } authored)
				residentRooms.Add(id, authored.CloneRuntime());
	}

	void WakeNearbyRooms()
	{
		var wakePadding = new Point2(
			(int)MathF.Ceiling(FbZoomedWidth * 0.5f) + SeamlessWakeMargin,
			(int)MathF.Ceiling(FbZoomedHeight * 0.5f) + SeamlessWakeMargin);
		var wakeArea = SeamlessCameraArea().Inflate(wakePadding);
		foreach (var candidate in residentRooms.Values)
			if (!awakeRooms.Contains(candidate) && candidate.WorldBounds.Overlaps(wakeArea))
				WakeRoom(candidate);
	}

	RectInt SeamlessCameraArea()
	{
		var size = new Vector2(FbZoomedWidth, FbZoomedHeight);
		var padding = (new Vector2(Width, Height) - size) / 2;
		var actualTopLeft = Camera + padding;
		var area = RectInt.Between(actualTopLeft.FloorToPoint2(), (actualTopLeft + size).CeilingToPoint2());
		if (GetFirst<Player>() is not { } player)
			return area;
		var centeredTopLeft = (Vector2)player.Position - size / 2;
		return area.Conflate(RectInt.Between(centeredTopLeft.FloorToPoint2(), (centeredTopLeft + size).CeilingToPoint2()));
	}

	void WakeRoom(Room candidate)
	{
		if (!awakeRooms.Add(candidate))
			return;
		residentRoomInversions.TryAdd(candidate, false);
		LoadRoom(candidate);
		visibleResidentRooms.Add(candidate);
	}

	bool TrySeamlessTransition(Point2 direction)
	{
		if (!SeamlessRoomsActive || room == null || nextRoom != null || GetFirst<Player>() is not { } player)
			return false;

		var from = room;
		if (Assets.FindAdjacentRoomPlayMode(from, direction, player.Position) is not { } authored)
			return false;

		if (!residentRooms.TryGetValue(authored.Id, out var target))
		{
			target = authored.CloneRuntime();
			residentRooms.Add(target.Id, target);
		}
		WakeRoom(target);

		transitionDirection = new Point2(Math.Sign(direction.X), Math.Sign(direction.Y));
		residentRoomInversions[from] = invertedNow;
		room = target;
		seamlessResidencyCurrent = target;
		invertedNow = residentRoomInversions.GetValueOrDefault(target);
		foreach (var actor in Actors)
			if (actor.Room == from && (actor is Player || actor.KeepOnRoomTransition))
				actor.Room = target;

		ResolvePlayerRoomTransition(transitionDirection);
		ApplyMusicArea(target);
		ApplyRoomReverb(target);
		ApplyRoomMusicMuffle(target);
		roomSwappedThisFrame = true;
		RaiseRoomChanged(from, target, RoomChangeKind.EdgeTransition, transitionDirection);
		FinishSeamlessRoomActivation();
		RefreshSeamlessResidency();
		return true;
	}

	void FinishSeamlessRoomActivation()
	{
		ResetPlatformNavGraphRoomState();
		if (room is { } entered && GetFirst<Player>() is { } player)
		{
			roomEntryPosition = player.Position;
			roomRespawnPoint = new RespawnState(entered.Id, roomEntryPosition);
			firstGroundedInRoom = null;
		}
		roomEntryDirection = transitionDirection;
		RemoveDestroyedActors();
	}

	void DestroyResidentRoomActors(Room departing)
	{
		foreach (var actor in Actors)
			if (actor.Room == departing && actor is not Player && !actor.KeepOnRoomTransition)
				Destroy(actor);
		foreach (var actor in parkedActors)
			if (actor.Room == departing && !actor.KeepOnRoomTransition)
				Destroy(actor);
	}

	bool OverlapsSeamlessRoomCollision(in RectInt bounds)
	{
		foreach (var candidate in residentRooms.Values)
			if (bounds.Overlaps(candidate.WorldBounds) && OverlapsRoomCollision(candidate, bounds))
				return true;
		return false;
	}

	RectInt SeamlessCameraBounds => seamlessWorldBounds.Width > 0 ? seamlessWorldBounds : room?.WorldBounds ?? new();

	Vector2 ClampCameraToGameplayBoundsFb(Vector2 target, Vector2 padding)
		=> SeamlessRoomsActive
			? ClampCameraToRoomFb(target, padding, SeamlessCameraBounds)
			: ClampCameraToRoomFb(target, padding);

	void RebuildSeamlessWorldBounds()
	{
		if (room == null)
		{
			seamlessWorldBounds = new();
			return;
		}

		connectedRoomIds.Clear();
		connectedRoomScratch.Clear();
		connectedRoomIds.Add(room.Id);
		connectedRoomScratch.Add(Assets.GetRoom(room.Id) ?? room);
		seamlessWorldBounds = room.WorldBounds;

		for (var i = 0; i < connectedRoomScratch.Count; i++)
		{
			var source = connectedRoomScratch[i];
			foreach (var candidate in Assets.RoomsById.Values)
			{
				if (candidate.Sublayer != room.Sublayer || connectedRoomIds.Contains(candidate.Id) ||
					!RoomsTouch(source.WorldBounds, candidate.WorldBounds))
					continue;
				connectedRoomIds.Add(candidate.Id);
				connectedRoomScratch.Add(candidate);
				seamlessWorldBounds = seamlessWorldBounds.Conflate(candidate.WorldBounds);
			}
		}
	}

	static bool RoomsTouch(in RectInt a, in RectInt b)
		=> (a.Right == b.Left || a.Left == b.Right) && a.Top < b.Bottom && a.Bottom > b.Top ||
			(a.Bottom == b.Top || a.Top == b.Bottom) && a.Left < b.Right && a.Right > b.Left;
}
