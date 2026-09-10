using Foster.Framework;
using System.Numerics;

namespace Myth;

// Crumble tiles (Room.TileFlags.Crumble): standing on one starts a short shake during which
// the tile stays solid — that's the player's window to jump off — then it collapses out from
// under them. A few seconds later the tile returns as plain grey debris, still crumbleable.
// All tile mutations land on the runtime room clone, so everything resets when the room
// reloads, same as broken tiles.
public partial class Game
{
	enum CrumblePhase
	{
		Shaking,
		Gone
	}

	struct CrumbleState
	{
		public CrumblePhase Phase;
		public float Timer;
	}

	// The shake is the jump-grace window: collision is untouched until it elapses (and normal
	// coyote time still applies after the collapse).
	static float CrumbleShakeSeconds => 0.19f;
	static float CrumbleRespawnSeconds => 2.5f;
	// How long to wait before re-checking a respawn blocked by something standing in the hole.
	const float CrumbleRespawnRetrySeconds = 0.25f;
	// What a crumbled tile returns as. Stone reads as generic grey debris for now; register a
	// dedicated debris tileset in Factory.RegisterTypes and point this at its char to upgrade.
	const char CrumbleDebrisTile = 'T';

	readonly Dictionary<Point2, CrumbleState> crumbleTiles = [];
	readonly List<Point2> crumbleKeyScratch = [];
	readonly List<Point2> crumbleCollapseScratch = [];
	readonly List<Point2> crumbleRestoreScratch = [];
	Room? crumbleRoom;

	/// Called by the player every grounded frame: starts the shake for idle Crumble tiles
	/// directly under their feet. Tiles already shaking or gone are unaffected.
	public void TriggerCrumbleBelow(Actor actor)
	{
		if (room == null || actor.Hitbox.Shape != Hitbox.Shapes.Rect)
			return;

		var bounds = actor.Hitbox.Rect + actor.Position;
		var local = bounds - room.WorldBounds.TopLeft;
		var left = (int)MathF.Floor(local.Left / (float)TileSize);
		var right = (int)MathF.Floor((local.Right - 1) / (float)TileSize);
		var feetY = (int)MathF.Floor(local.Bottom / (float)TileSize);

		for (var x = left; x <= right; x++)
		{
			var tile = new Point2(x, feetY);
			if (!room.HasTileFlag(tile, Room.TileFlags.Crumble))
				continue;
			if (crumbleTiles.ContainsKey(tile))
				continue;
			if (room.GetEntry(tile) is not { BlocksMovement: true })
				continue;

			crumbleTiles[tile] = new CrumbleState { Phase = CrumblePhase.Shaking, Timer = CrumbleShakeSeconds };

			// Crack cue marking the grace window; the collapse itself pops via RemoveTiles.
			PopAt(tile);
		}
	}

	void PopAt(Point2 tile)
	{
		var scale = Vector2.One * 0.25f;
		Create<Pop>(TileWorldCenter(tile), (pop) => {
			pop.Squish = scale;
		});
	}

	void UpdateCrumbleTiles()
	{
		// During a transition both the outgoing and incoming rooms' tile grids are live at once,
		// and `room` is still the outgoing room. A collapse now would run RefreshRoomTiles and fill
		// the incoming grids from the outgoing room -- you'd play the new room's collision while
		// seeing the old room's tiles. Crumble state is keyed to the room we're leaving and gets
		// cleared on arrival, so just pause until the transition settles.
		if (nextRoom != null || LayerTransitionActive)
			return;

		// State is keyed by room-local tile coords; a room change orphans all of it.
		if (crumbleRoom != room)
		{
			crumbleTiles.Clear();
			crumbleRoom = room;
		}

		if (room == null || crumbleTiles.Count == 0)
			return;

		crumbleKeyScratch.Clear();
		foreach (var tile in crumbleTiles.Keys)
			crumbleKeyScratch.Add(tile);

		crumbleCollapseScratch.Clear();
		crumbleRestoreScratch.Clear();

		foreach (var tile in crumbleKeyScratch)
		{
			var state = crumbleTiles[tile];
			state.Timer -= Time.Delta;
			if (state.Timer > 0)
			{
				crumbleTiles[tile] = state;
				continue;
			}

			if (state.Phase == CrumblePhase.Shaking)
			{
				state.Phase = CrumblePhase.Gone;
				state.Timer = CrumbleRespawnSeconds;
				crumbleTiles[tile] = state;
				crumbleCollapseScratch.Add(tile);
			}
			else
			{
				// Don't restore into a pawn or a solid actor parked in the hole. (The tile
				// grids also carry Solid, but their per-cell hitbox can't match this cell
				// while it's empty.)
				var worldRect = new RectInt(
					room.WorldBounds.X + tile.X * TileSize,
					room.WorldBounds.Y + tile.Y * TileSize,
					TileSize, TileSize);
				if (OverlapsFirst(worldRect, Actor.Masks.Player | Actor.Masks.Enemy | Actor.Masks.Solid) != null)
				{
					state.Timer = CrumbleRespawnRetrySeconds;
					crumbleTiles[tile] = state;
					continue;
				}

				crumbleRestoreScratch.Add(tile);
			}
		}

		if (crumbleCollapseScratch.Count > 0)
			RemoveTiles(crumbleCollapseScratch, TileWorldCenter(crumbleCollapseScratch[0]));

		if (crumbleRestoreScratch.Count > 0)
		{
			foreach (var tile in crumbleRestoreScratch)
			{
				room.Set(tile, CrumbleDebrisTile, rebuildGeometry: false);
				// Setting a tile clears its flags; re-flag so the debris crumbles again.
				room.SetTileFlag(tile, Room.TileFlags.Crumble, true);
				crumbleTiles.Remove(tile);
				PopAt(tile);
			}

			room.RebuildGeometry(TileWorldBounds(crumbleRestoreScratch));
			RefreshRoomTiles();
			View?.MarkRoomNormalCacheDirty(room);
			View?.MarkRoomTileFieldCacheDirty(room);
		}
	}
}
