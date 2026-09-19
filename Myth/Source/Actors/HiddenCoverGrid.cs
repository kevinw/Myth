using System.Numerics;
using Foster.Framework;

namespace Myth;

public class HiddenCoverGrid : Actor
{
	readonly record struct RevealDoorSpec(string Id, RectInt Rect, RevealDoor.RevealDirection Direction, string TileType);
	readonly record struct DrawCommand(Subtexture Tile, Subtexture Normal, Point2 Vertex, Vector2 Position, char TileType, int MaterialMask, int CoverMask, int RegionId);
	readonly record struct HiddenSegment(ShadowSegment Segment, int RegionId);
	readonly record struct SegmentRun(float Axis, float Start, float End, int RegionId);

	static readonly Point2[] MaskToTile =
	[
		new(0, 3),
		new(3, 3),
		new(0, 0),
		new(3, 2),
		new(0, 2),
		new(1, 2),
		new(2, 3),
		new(3, 1),
		new(1, 3),
		new(0, 1),
		new(3, 0),
		new(2, 0),
		new(1, 0),
		new(2, 2),
		new(1, 1),
		new(2, 1),
	];

	readonly List<DrawCommand> drawCommands = [];
	readonly List<HiddenSegment> shadowSegments = [];
	Point2 size;
	char[,] coverTiles = new char[0, 0];
	int[,] coverRegions = new int[0, 0];
	float[] regionAlpha = [];
	string[][] regionRevealDoorIds = [];
	readonly HashSet<string> revealedDoorIds = [];
	readonly List<(RectInt Rect, int RegionId)> triggers = [];
	float renderOpacityScale = 1.0f;

	public HiddenCoverGrid()
	{
		Depth = -1000;
		CollidesWithSolids = false;
	}

	public override bool CastsSpriteShadow => false;
	public override bool CastsReflection => false;

	public void SetRenderOpacityScale(float value)
		=> renderOpacityScale = Math.Clamp(value, 0.0f, 1.0f);

	public bool Covers(Vector2 worldPosition)
	{
		var tile = ((worldPosition - Position) / Game.TileSize).FloorToPoint2();
		return tile.X >= 0 && tile.Y >= 0 && tile.X < size.X && tile.Y < size.Y &&
			RegionVisible(coverRegions[tile.X, tile.Y]);
	}

	public void Build(Room room)
	{
		size = room.Size;
		drawCommands.Clear();
		shadowSegments.Clear();
		triggers.Clear();
		coverTiles = new char[size.X, size.Y];
		coverRegions = new int[size.X, size.Y];
		for (var y = 0; y < size.Y; y ++)
		for (var x = 0; x < size.X; x ++)
		{
			coverTiles[x, y] = '0';
			coverRegions[x, y] = -1;
		}
		regionAlpha = [];
		regionRevealDoorIds = [];
		revealedDoorIds.Clear();
		foreach (var id in Game.Save.RevealedDoors)
			if (!string.IsNullOrWhiteSpace(id))
				revealedDoorIds.Add(id);

		if (size.X <= 0 || size.Y <= 0)
			return;

		var doors = ReadRevealDoors(room);
		if (doors.Count <= 0)
			return;

		var doorBlockers = BuildDoorBlockers(size, doors);
		var air = BuildAirGrid(size, room, doorBlockers);
		var airRegions = BuildAirRegions(size, air);
		var hiddenRegions = MapDoorsToRegions(size, room, doors, airRegions);
		if (hiddenRegions.Count <= 0)
			return;

		regionAlpha = [.. Enumerable.Repeat(1.0f, hiddenRegions.Count)];
		regionRevealDoorIds = new string[hiddenRegions.Count][];
		var remap = hiddenRegions.Keys
			.Select((sourceRegionId, hiddenRegionId) => (sourceRegionId, hiddenRegionId))
			.ToDictionary(static it => it.sourceRegionId, static it => it.hiddenRegionId);

		foreach (var (sourceRegionId, spec) in hiddenRegions)
		{
			var hiddenRegionId = remap[sourceRegionId];
			regionRevealDoorIds[hiddenRegionId] = [.. spec.RevealDoorIds.Distinct()];
			if (regionRevealDoorIds[hiddenRegionId].Any(revealedDoorIds.Contains))
				regionAlpha[hiddenRegionId] = 0.0f;

			var tileType = ResolveCoverTile(room, spec.TileType, airRegions, sourceRegionId);
			foreach (var tile in spec.Tiles)
			{
				coverTiles[tile.X, tile.Y] = tileType;
				coverRegions[tile.X, tile.Y] = hiddenRegionId;
			}
			foreach (var trigger in spec.Triggers)
				triggers.Add((trigger, hiddenRegionId));
		}

		BuildDrawCommands(room);
	}

	public override void Update()
	{
		base.Update();

		if (Game.GetFirst<Player>() is { } player && player.Hitbox.Shape == Hitbox.Shapes.Rect)
			RevealForPlayer(player, immediate: false);

		for (var i = 0; i < regionAlpha.Length; i ++)
			if (regionAlpha[i] < 1.0f)
				regionAlpha[i] = Calc.Approach(regionAlpha[i], 0, Time.Delta * 5.0f);
	}

	public void RevealForCurrentPlayer(bool immediate)
	{
		if (Game.GetFirst<Player>() is { } player && player.Hitbox.Shape == Hitbox.Shapes.Rect)
			RevealForPlayer(player, immediate);
	}

	public override void Render(Batcher batcher)
		=> RenderTiles(batcher, static command => command.Tile);

	public override void RenderNormals(Batcher batcher)
		=> RenderTiles(batcher, static command => command.Normal);

	internal void RenderTileField(Batcher batcher, Func<char, Point2, Color> colorForTile)
	{
		var worldTileOrigin = Position / Game.TileSize;
		foreach (var command in drawCommands)
			if (command.Tile.Texture != null && RegionVisible(command.RegionId) && regionAlpha[command.RegionId] * renderOpacityScale > 0.01f)
				DualGrid.DrawTile(batcher, command.Tile, command.Position, command.MaterialMask,
					colorForTile(command.TileType, worldTileOrigin + command.Vertex));
	}

	public void AppendAllShadowSegments(List<ShadowSegment> segments)
	{
		foreach (var segment in shadowSegments)
			if (RegionVisible(segment.RegionId))
				segments.Add(segment.Segment);
	}

	public void ForEachVisibleCell(Action<RectInt, float, Subtexture> action)
	{
		for (var y = 0; y < size.Y; y ++)
		for (var x = 0; x < size.X; x ++)
		{
			var regionId = coverRegions[x, y];
			if (coverTiles[x, y] == '0' || !RegionVisible(regionId))
				continue;

			action(
				new RectInt(Position.X + x * Game.TileSize, Position.Y + y * Game.TileSize, Game.TileSize, Game.TileSize),
				regionAlpha[regionId],
				new Subtexture());
		}
	}

	void RevealForPlayer(Player player, bool immediate)
	{
		var playerBounds = player.Hitbox.Rect + player.Position - Position;
		var revealedAny = false;
		var newlyRevealedDoorIds = new List<string>();
		var newlyRevealedRegions = new HashSet<int>();
		foreach (var (rect, regionId) in triggers)
		{
			if (regionId >= 0 && regionId < regionAlpha.Length && regionAlpha[regionId] >= 1.0f && playerBounds.Overlaps(rect))
			{
				regionAlpha[regionId] = immediate ? 0.0f : 0.99f;
				newlyRevealedDoorIds.AddRange(MarkRegionRevealed(regionId));
				newlyRevealedRegions.Add(regionId);
				revealedAny = true;
			}
		}

		var left = Math.Clamp((int)MathF.Floor(playerBounds.Left / (float)Game.TileSize), 0, size.X - 1);
		var right = Math.Clamp((int)MathF.Floor((playerBounds.Right - 1) / (float)Game.TileSize), 0, size.X - 1);
		var top = Math.Clamp((int)MathF.Floor(playerBounds.Top / (float)Game.TileSize), 0, size.Y - 1);
		var bottom = Math.Clamp((int)MathF.Floor((playerBounds.Bottom - 1) / (float)Game.TileSize), 0, size.Y - 1);

		for (var y = top; y <= bottom; y++)
			for (var x = left; x <= right; x++)
			{
				var regionId = coverRegions[x, y];
				if (regionId >= 0 && regionId < regionAlpha.Length && regionAlpha[regionId] >= 1.0f)
				{
					regionAlpha[regionId] = immediate ? 0.0f : 0.99f;
					newlyRevealedDoorIds.AddRange(MarkRegionRevealed(regionId));
					newlyRevealedRegions.Add(regionId);
					revealedAny = true;
				}
			}

		if (revealedAny && !immediate)
		{
			Game.HitStun(0.1f);
			Game.PlayAudioEvent("reveal", position: player.Position);
			foreach (var id in newlyRevealedDoorIds.Distinct(StringComparer.OrdinalIgnoreCase))
				Game.NotifyPlayerEnteredRevealDoor(id, player.Position);
			// A revealed area that overlaps a camera blocker opens the camera there too.
			foreach (var regionId in newlyRevealedRegions)
				if (RegionWorldBounds(regionId) is { Width: > 0 } area)
					Game.RevealCameraBlockersOverlapping(area);
		}
	}

	// World-space bounding rect of a region's covered tiles (empty if the region has none).
	RectInt RegionWorldBounds(int regionId)
	{
		var minX = int.MaxValue;
		var minY = int.MaxValue;
		var maxX = int.MinValue;
		var maxY = int.MinValue;
		for (var y = 0; y < size.Y; y ++)
		for (var x = 0; x < size.X; x ++)
			if (coverRegions[x, y] == regionId)
			{
				minX = Math.Min(minX, x);
				minY = Math.Min(minY, y);
				maxX = Math.Max(maxX, x);
				maxY = Math.Max(maxY, y);
			}

		if (minX > maxX)
			return new RectInt();

		return new RectInt(
			Position.X + minX * Game.TileSize,
			Position.Y + minY * Game.TileSize,
			(maxX - minX + 1) * Game.TileSize,
			(maxY - minY + 1) * Game.TileSize);
	}

	List<string> MarkRegionRevealed(int regionId)
	{
		if (regionId < 0 || regionId >= regionRevealDoorIds.Length)
			return [];

		var result = new List<string>();
		foreach (var id in regionRevealDoorIds[regionId])
			if (!string.IsNullOrWhiteSpace(id))
			{
				if (revealedDoorIds.Add(id))
					result.Add(id);
				Game.RevealDoor(id);
			}
		return result;
	}

	/// Tiles of every hidden region none of whose reveal doors has been revealed yet, so the
	/// minimap can keep drawing them filled-in until the player discovers them.
	public static void ForEachUnrevealedHiddenTile(Room room, Func<string, bool> doorRevealed, Action<Point2> emit)
	{
		var size = room.Size;
		if (size.X <= 0 || size.Y <= 0)
			return;

		var doors = ReadRevealDoors(room);
		if (doors.Count <= 0)
			return;

		var airRegions = BuildAirRegions(size, BuildAirGrid(size, room, BuildDoorBlockers(size, doors)));
		foreach (var spec in MapDoorsToRegions(size, room, doors, airRegions).Values)
			if (!spec.RevealDoorIds.Any(doorRevealed))
				foreach (var tile in spec.Tiles)
					emit(tile);
	}

	void RenderTiles(Batcher batcher, Func<DrawCommand, Subtexture> getTile)
	{
		foreach (var command in drawCommands)
		{
			var tile = getTile(command);
			var alpha = RegionVisible(command.RegionId) ? regionAlpha[command.RegionId] * renderOpacityScale : 0;
			if (tile.Texture != null && alpha > 0.01f)
				DualGrid.DrawTile(batcher, tile, command.Position, command.MaterialMask, Color.White * alpha);
		}
	}

	static List<RevealDoorSpec> ReadRevealDoors(Room room)
	{
		var doors = new List<RevealDoorSpec>();
		foreach (var actor in room.Actors)
		{
			if (ActorSpecs.GetTypeName(actor) != nameof(RevealDoor))
				continue;

			var localPosition = ActorSpecs.ReadResolvedPosition(actor) ?? Point2.Zero;
			var size = ActorSpecs.ReadResolvedPoint(actor, nameof(IResizable.Size)) ?? RevealDoor.DefaultSize;
			var direction = ReadDirection(actor);
			var tileType = ActorSpecs.ReadResolvedString(actor, nameof(RevealDoor.TileType)) ?? "";
			var id = ActorSpecs.ReadString(actor, "Id") ?? "";
			doors.Add(new(id, new RectInt(localPosition.X, localPosition.Y, size.X, size.Y), direction, tileType));
		}

		return doors;
	}

	static RevealDoor.RevealDirection ReadDirection(System.Text.Json.Nodes.JsonObject json)
	{
		var text = ActorSpecs.ReadResolvedString(json, nameof(RevealDoor.Direction));
		return Enum.TryParse<RevealDoor.RevealDirection>(text, ignoreCase: true, out var direction)
			? direction
			: RevealDoor.RevealDirection.Right;
	}

	static bool[,] BuildDoorBlockers(Point2 size, List<RevealDoorSpec> doors)
	{
		var blockers = new bool[size.X, size.Y];
		foreach (var door in doors)
		{
			var bounds = TileBounds(size, door.Rect);
			for (var y = bounds.Top; y < bounds.Bottom; y ++)
			for (var x = bounds.Left; x < bounds.Right; x ++)
				blockers[x, y] = true;
		}
		return blockers;
	}

	static bool[,] BuildAirGrid(Point2 size, Room room, bool[,] blockers)
	{
		var air = new bool[size.X, size.Y];
		for (var y = 0; y < size.Y; y ++)
		for (var x = 0; x < size.X; x ++)
		{
			if (blockers[x, y])
				continue;
			air[x, y] = room.GetEntry(new Point2(x, y)) is not { BlocksMovement: true };
		}
		return air;
	}

	static int[,] BuildAirRegions(Point2 size, bool[,] air)
	{
		var regions = new int[size.X, size.Y];
		for (var y = 0; y < size.Y; y ++)
		for (var x = 0; x < size.X; x ++)
			regions[x, y] = -1;

		var nextRegion = 0;
		var queue = new Queue<Point2>();
		for (var y = 0; y < size.Y; y ++)
		for (var x = 0; x < size.X; x ++)
		{
			if (!air[x, y] || regions[x, y] >= 0)
				continue;

			queue.Enqueue(new Point2(x, y));
			regions[x, y] = nextRegion;
			while (queue.Count > 0)
			{
				var tile = queue.Dequeue();
				EnqueueAir(tile + Point2.Left);
				EnqueueAir(tile + Point2.Right);
				EnqueueAir(tile + Point2.Up);
				EnqueueAir(tile + Point2.Down);
			}
			nextRegion ++;
		}

		return regions;

		void EnqueueAir(Point2 tile)
		{
			if (tile.X < 0 || tile.Y < 0 || tile.X >= size.X || tile.Y >= size.Y)
				return;
			if (!air[tile.X, tile.Y] || regions[tile.X, tile.Y] >= 0)
				return;
			regions[tile.X, tile.Y] = nextRegion;
			queue.Enqueue(tile);
		}
	}

	static Dictionary<int, HiddenRegionSpec> MapDoorsToRegions(Point2 size, Room room, List<RevealDoorSpec> doors, int[,] airRegions)
	{
		var hiddenRegions = new Dictionary<int, HiddenRegionSpec>();
		foreach (var door in doors)
		{
			var seed = DoorSeedTile(size, door);
			if (seed.X < 0 || seed.Y < 0 || seed.X >= size.X || seed.Y >= size.Y)
				continue;

			var sourceRegionId = airRegions[seed.X, seed.Y];
			if (sourceRegionId < 0)
				continue;

			if (!hiddenRegions.TryGetValue(sourceRegionId, out var spec))
			{
				spec = new HiddenRegionSpec();
				for (var y = 0; y < size.Y; y ++)
				for (var x = 0; x < size.X; x ++)
					if (airRegions[x, y] == sourceRegionId)
						spec.Tiles.Add(new Point2(x, y));
				hiddenRegions[sourceRegionId] = spec;
			}

			if (!string.IsNullOrWhiteSpace(door.Id))
				spec.RevealDoorIds.Add(door.Id);
			spec.TileType = string.IsNullOrWhiteSpace(door.TileType) ? spec.TileType : door.TileType;
			spec.Triggers.Add(door.Rect);
			var doorBounds = TileBounds(size, door.Rect);
			for (var y = doorBounds.Top; y < doorBounds.Bottom; y ++)
			for (var x = doorBounds.Left; x < doorBounds.Right; x ++)
				spec.Tiles.Add(new Point2(x, y));
		}

		return hiddenRegions;
	}

	static Point2 DoorSeedTile(Point2 size, RevealDoorSpec door)
	{
		var bounds = TileBounds(size, door.Rect);
		return door.Direction switch
		{
			RevealDoor.RevealDirection.Left => new Point2(bounds.Left - 1, (bounds.Top + bounds.Bottom - 1) / 2),
			RevealDoor.RevealDirection.Right => new Point2(bounds.Right, (bounds.Top + bounds.Bottom - 1) / 2),
			RevealDoor.RevealDirection.Up => new Point2((bounds.Left + bounds.Right - 1) / 2, bounds.Top - 1),
			RevealDoor.RevealDirection.Down => new Point2((bounds.Left + bounds.Right - 1) / 2, bounds.Bottom),
			_ => Point2.Zero
		};
	}

	static RectInt TileBounds(Point2 size, RectInt rect)
	{
		var left = Math.Clamp((int)MathF.Floor(rect.Left / (float)Game.TileSize), 0, size.X);
		var top = Math.Clamp((int)MathF.Floor(rect.Top / (float)Game.TileSize), 0, size.Y);
		var right = Math.Clamp((int)MathF.Ceiling(rect.Right / (float)Game.TileSize), 0, size.X);
		var bottom = Math.Clamp((int)MathF.Ceiling(rect.Bottom / (float)Game.TileSize), 0, size.Y);
		return new RectInt(left, top, right - left, bottom - top);
	}

	char ResolveCoverTile(Room room, string overrideTileType, int[,] airRegions, int sourceRegionId)
	{
		if (ResolveTileTypeOverride(overrideTileType) is { } tileType)
			return tileType;

		var counts = new Dictionary<char, int>();
		for (var y = 0; y < size.Y; y ++)
		for (var x = 0; x < size.X; x ++)
		{
			if (airRegions[x, y] != sourceRegionId)
				continue;

			CountNeighbor(x - 1, y);
			CountNeighbor(x + 1, y);
			CountNeighbor(x, y - 1);
			CountNeighbor(x, y + 1);
		}

		return counts.Count > 0
			? counts.OrderByDescending(static it => it.Value).ThenBy(static it => it.Key).First().Key
			: '1';

		void CountNeighbor(int x, int y)
		{
			if (x < 0 || y < 0 || x >= size.X || y >= size.Y)
				return;

			var tile = room.Tiles[x, y];
			if (Factory.Find(tile) is not { BlocksMovement: true, DualTilesetName: not null, Layer: Factory.TileLayer.Foreground } || room.IsSlope(new Point2(x, y)))
				return;

			counts[tile] = counts.GetValueOrDefault(tile) + 1;
		}
	}

	static char? ResolveTileTypeOverride(string value)
	{
		if (string.IsNullOrWhiteSpace(value))
			return null;

		if (value.Length == 1 && Factory.Find(value[0]) is { DualTilesetName: not null, Layer: Factory.TileLayer.Foreground })
			return value[0];

		foreach (var (tile, entry) in Factory.Entries)
		{
			if (entry.Layer != Factory.TileLayer.Foreground || entry.DualTilesetName == null)
				continue;
			if (string.Equals(entry.Name, value, StringComparison.OrdinalIgnoreCase) ||
			    string.Equals(entry.DualTilesetName, value, StringComparison.OrdinalIgnoreCase) ||
			    string.Equals(entry.DualTilesetName, $"{value}-dual", StringComparison.OrdinalIgnoreCase))
				return tile;
		}

		return null;
	}

	void BuildDrawCommands(Room room)
	{
		foreach (var tileType in CoverTileTypes())
		{
			if (Factory.Find(tileType) is not { DualTilesetName: { } dualTilesetName } || Assets.GetTileset(dualTilesetName) is not { } tileset)
				continue;

			for (var x = 0; x <= size.X; x ++)
			for (var y = 0; y <= size.Y; y ++)
			{
				var coverMask = CoverMask(tileType, x, y);
				if (coverMask == 0)
					continue;

				var materialMask = MaterialMask(room, tileType, x, y);
				if (materialMask == 0)
					continue;

				var art = DualGrid.LookupArt(tileset, MaskToTile[materialMask], room.TileSpacePosition + new Point2(x, y), tileType);
				if (art.Tile.Texture == null)
					continue;

				var position = new Vector2(x - 0.5f, y - 0.5f) * Game.TileSize;
				var regionId = CoverRegionId(x, y);
				if (regionId < 0)
					continue;

				var command = new DrawCommand(art.Tile, art.Normal, new Point2(x, y), position, tileType, materialMask, coverMask, regionId);
				drawCommands.Add(command);
				AppendWorldShadowSegments(position, command, art.Occluders);
			}
		}

		MergeAxisAlignedShadowSegments();
	}

	IEnumerable<char> CoverTileTypes()
	{
		var seen = new HashSet<char>();
		for (var y = 0; y < size.Y; y ++)
		for (var x = 0; x < size.X; x ++)
			if (coverTiles[x, y] != '0' && seen.Add(coverTiles[x, y]))
				yield return coverTiles[x, y];
	}

	int CoverMask(char tileType, int x, int y)
	{
		var mask = 0;
		if (IsCoverFilled(tileType, x - 1, y - 1))
			mask |= TileQuadrants.TopLeft;
		if (IsCoverFilled(tileType, x - 1, y))
			mask |= TileQuadrants.BottomLeft;
		if (IsCoverFilled(tileType, x, y - 1))
			mask |= TileQuadrants.TopRight;
		if (IsCoverFilled(tileType, x, y))
			mask |= TileQuadrants.BottomRight;
		return mask;
	}

	int MaterialMask(Room room, char tileType, int x, int y)
	{
		var mask = 0;
		if (IsMaterialFilled(room, tileType, x - 1, y - 1))
			mask |= TileQuadrants.TopLeft;
		if (IsMaterialFilled(room, tileType, x - 1, y))
			mask |= TileQuadrants.BottomLeft;
		if (IsMaterialFilled(room, tileType, x, y - 1))
			mask |= TileQuadrants.TopRight;
		if (IsMaterialFilled(room, tileType, x, y))
			mask |= TileQuadrants.BottomRight;
		return mask;
	}

	bool IsCoverFilled(char tileType, int x, int y)
		=> x >= 0 && y >= 0 && x < size.X && y < size.Y && coverTiles[x, y] == tileType;

	bool IsMaterialFilled(Room room, char tileType, int x, int y)
	{
		if (IsCoverFilled(tileType, x, y))
			return true;
		if (x < 0 || y < 0 || x >= size.X || y >= size.Y)
			return false;
		return room.Tiles[x, y] == tileType;
	}

	int CoverRegionId(int x, int y)
	{
		var regionId = -1;
		MergeRegion(x - 1, y - 1);
		MergeRegion(x - 1, y);
		MergeRegion(x, y - 1);
		MergeRegion(x, y);
		return regionId;

		void MergeRegion(int cellX, int cellY)
		{
			if (cellX < 0 || cellY < 0 || cellX >= size.X || cellY >= size.Y)
				return;
			var candidate = coverRegions[cellX, cellY];
			if (candidate < 0)
				return;
			regionId = regionId < 0 ? candidate : Math.Min(regionId, candidate);
		}
	}

	bool RegionVisible(int regionId)
		=> regionId >= 0 && regionId < regionAlpha.Length && regionAlpha[regionId] > 0.01f;

	void AppendWorldShadowSegments(Vector2 commandPosition, DrawCommand command, Tileset.OccluderSegment[] occluders)
	{
		foreach (var local in occluders)
		{
			var owner = OwnerCell(command, local);
			if (owner == null)
				continue;

			shadowSegments.Add(new(
				new(Position + commandPosition + local.A, Position + commandPosition + local.B),
				coverRegions[owner.Value.X, owner.Value.Y]));
		}
	}

	Point2? OwnerCell(DrawCommand command, Tileset.OccluderSegment segment)
	{
		var midpoint = (segment.A + segment.B) * 0.5f;
		var owner = midpoint.X < Game.TileSize / 2
			? midpoint.Y < Game.TileSize / 2 ? new Point2(command.Vertex.X - 1, command.Vertex.Y - 1) : new Point2(command.Vertex.X - 1, command.Vertex.Y)
			: midpoint.Y < Game.TileSize / 2 ? new Point2(command.Vertex.X, command.Vertex.Y - 1) : new Point2(command.Vertex.X, command.Vertex.Y);
		return owner.X >= 0 && owner.Y >= 0 && owner.X < size.X && owner.Y < size.Y && coverTiles[owner.X, owner.Y] != '0'
			? owner
			: null;
	}

	readonly static List<HiddenSegment> _merged = [];
	void MergeAxisAlignedShadowSegments()
	{
		if (shadowSegments.Count <= 1)
			return;

		var merged = _merged;
		merged.Clear();
		MergeHorizontalShadowSegments(merged);
		MergeVerticalShadowSegments(merged);

		foreach (var segment in shadowSegments)
			if (!IsAxisAligned(segment.Segment))
				merged.Add(segment);

		shadowSegments.Clear();
		shadowSegments.AddRange(merged);
	}

	void MergeHorizontalShadowSegments(List<HiddenSegment> merged)
	{
		var horizontal = shadowSegments
			.Where(static it => NearlyEqual(it.Segment.A.Y, it.Segment.B.Y))
			.Select(static it =>
			{
				var y = Quantize(it.Segment.A.Y);
				var left = MathF.Min(it.Segment.A.X, it.Segment.B.X);
				var right = MathF.Max(it.Segment.A.X, it.Segment.B.X);
				return new SegmentRun(y, left, right, it.RegionId);
			})
			.OrderBy(static it => it.RegionId)
			.ThenBy(static it => it.Axis)
			.ThenBy(static it => it.Start)
			.ToArray();

		MergeRuns(horizontal, merged, static run => new(new(new Vector2(run.Start, run.Axis), new Vector2(run.End, run.Axis)), run.RegionId));
	}

	void MergeVerticalShadowSegments(List<HiddenSegment> merged)
	{
		var vertical = shadowSegments
			.Where(static it => NearlyEqual(it.Segment.A.X, it.Segment.B.X))
			.Select(static it =>
			{
				var x = Quantize(it.Segment.A.X);
				var top = MathF.Min(it.Segment.A.Y, it.Segment.B.Y);
				var bottom = MathF.Max(it.Segment.A.Y, it.Segment.B.Y);
				return new SegmentRun(x, top, bottom, it.RegionId);
			})
			.OrderBy(static it => it.RegionId)
			.ThenBy(static it => it.Axis)
			.ThenBy(static it => it.Start)
			.ToArray();

		MergeRuns(vertical, merged, static run => new(new(new Vector2(run.Axis, run.Start), new Vector2(run.Axis, run.End)), run.RegionId));
	}

	static void MergeRuns(SegmentRun[] runs, List<HiddenSegment> merged, Func<SegmentRun, HiddenSegment> createSegment)
	{
		if (runs.Length == 0)
			return;

		var current = runs[0];
		for (var i = 1; i < runs.Length; i++)
		{
			var next = runs[i];
			if (current.RegionId == next.RegionId && NearlyEqual(current.Axis, next.Axis) && next.Start <= current.End + 0.001f)
			{
				current = current with { End = MathF.Max(current.End, next.End) };
				continue;
			}

			if (current.End > current.Start)
				merged.Add(createSegment(current));
			current = next;
		}

		if (current.End > current.Start)
			merged.Add(createSegment(current));
	}

	static bool IsAxisAligned(ShadowSegment segment)
		=> NearlyEqual(segment.A.X, segment.B.X) || NearlyEqual(segment.A.Y, segment.B.Y);

	static bool NearlyEqual(float a, float b)
		=> MathF.Abs(a - b) <= 0.001f;

	static float Quantize(float value)
		=> MathF.Round(value * 1000.0f) / 1000.0f;

	sealed class HiddenRegionSpec
	{
		public readonly List<Point2> Tiles = [];
		public readonly List<RectInt> Triggers = [];
		public readonly List<string> RevealDoorIds = [];
		public string TileType = "";
	}
}
