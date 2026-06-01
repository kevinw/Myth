using System.Numerics;
using Foster.Framework;

namespace TinyLink;

/// <summary>
/// The Factory holds meta information on how to populate the Game when loading a Room.
/// It describes tile types and editor actor palette entries.
/// </summary>
public static class Factory
{
	public const string DefaultSurfaceType = "Grass";

	public enum TileLayer
	{
		Foreground,
		Background
	}

	/// <summary>
	/// Used to Place a Tile
	/// </summary>
	public delegate void TileFn(Point2 tile, Room room, Grid fg, Grid fgVisual, Grid bg, DualGrid dual);

	public readonly struct Entry
	{
		public readonly string Name;
		public readonly Subtexture Image;
		public readonly Vector2 Origin;
		public readonly Point2 Offset;
		public readonly TileFn? Tile;
		public readonly string? SurfaceType;
		public readonly bool BlocksMovement;
		public readonly bool CastsShadow;
		public readonly bool ReflectsSprites;
		public readonly bool Destructible;
		public readonly bool IsSlope;
		public readonly string? TilesetName;
		public readonly string? SlopeTilesetName;
		public readonly string? DualTilesetName;
		public readonly TileLayer Layer;
		public readonly float AcousticReflectivity;

		public Entry(string name, Subtexture image, TileFn tile, string? surfaceType = null, bool blocksMovement = false, bool castsShadow = false, bool reflectsSprites = false, bool destructible = false, bool isSlope = false, string? slopeTilesetName = null, string? dualTilesetName = null, TileLayer layer = TileLayer.Foreground, string? tilesetName = null, float acousticReflectivity = 1.0f)
		{
			Name = name;
			Image = image;
			Tile = tile;
			SurfaceType = surfaceType;
			BlocksMovement = blocksMovement;
			CastsShadow = castsShadow;
			ReflectsSprites = reflectsSprites;
			Destructible = destructible;
			IsSlope = isSlope;
			TilesetName = tilesetName;
			SlopeTilesetName = slopeTilesetName;
			DualTilesetName = dualTilesetName;
			Layer = layer;
			AcousticReflectivity = Math.Clamp(acousticReflectivity, 0.0f, 1.0f);
		}
	}

	public readonly struct ActorPaletteEntry
	{
		public readonly string Name;
		public readonly Subtexture Image;
		public readonly Vector2 Origin;
		public readonly Point2 Offset;
		public readonly RectInt? EditorBounds;
		public readonly RectInt DefaultEditorBounds;
		public readonly string? ActorTypeName;
		public readonly string? ActorSpecId;

		private ActorPaletteEntry(string name, Subtexture image, Vector2 origin, Point2 offset, RectInt defaultEditorBounds, string? actorTypeName, string? actorSpecId, RectInt? editorBounds = null)
		{
			Name = name;
			Image = image;
			Origin = origin;
			Offset = offset;
			DefaultEditorBounds = defaultEditorBounds;
			ActorTypeName = actorTypeName;
			ActorSpecId = actorSpecId;
			EditorBounds = editorBounds;
		}

		public static ActorPaletteEntry FromActor<T>(string spriteName, Point2? offset = null, RectInt? editorBounds = null) where T : Actor
		{
			var sprite = Assets.GetSprite(spriteName);
			if (sprite == null)
				throw new InvalidOperationException($"Actor palette entry '{typeof(T).Name}' references missing sprite '{spriteName}'.");

			var typeName = typeof(T).Name;
			return new ActorPaletteEntry(typeName, sprite.Frames[0].VisibleSubtexture, sprite.Origin, offset ?? Point2.Zero, GetDefaultEditorBounds(sprite), typeName, null, editorBounds);
		}

		public static ActorPaletteEntry FromActorSpec(string specId, string spriteName, Point2? offset = null, RectInt? editorBounds = null)
		{
			var spec = ActorSpecs.Get(specId);
			var sprite = Assets.GetSprite(spriteName);
			if (spec == null)
				throw new InvalidOperationException($"Actor palette spec entry '{specId}' references missing actor spec.");
			if (sprite == null)
				throw new InvalidOperationException($"Actor palette spec entry '{specId}' references missing sprite '{spriteName}'.");

			return new ActorPaletteEntry(specId, sprite.Frames[0].VisibleSubtexture, sprite.Origin, offset ?? Point2.Zero, GetDefaultEditorBounds(sprite), ActorSpecs.GetTypeName(spec), specId, editorBounds);
		}

		private static RectInt GetDefaultEditorBounds(Sprite sprite)
		{
			if (sprite.Frames.Count <= 0)
				return new RectInt(-4, -4, 8, 8);

			var frame = sprite.Frames[0].Subtexture;
			var left = -sprite.Origin.X - frame.Frame.X;
			var top = -sprite.Origin.Y - frame.Frame.Y;
			var right = left + frame.Source.Width;
			var bottom = top + frame.Source.Height;

			var x = (int)MathF.Floor(left);
			var y = (int)MathF.Floor(top);
			var width = (int)MathF.Ceiling(right) - x;
			var height = (int)MathF.Ceiling(bottom) - y;

			return width > 0 && height > 0
				? new RectInt(x, y, width, height)
				: new RectInt(-4, -4, 8, 8);
		}
	}

	// List of all the tile types
	public static readonly Dictionary<char, Entry> Entries = new();
	public static readonly List<ActorPaletteEntry> ActorPalette = [];

	// Registers a new Type
	public static void Register(char id, in Entry entry)
	{
		Entries[id] = entry;
	}

	// Finds a given Type entry by its character ID
	public static Entry? Find(char id)
	{
		if (Entries.TryGetValue(id, out var entry))
			return entry;

		return null;
	}

	public static Entry AsTile(string tilesetName, bool isFg, string? surfaceType = null, bool? castsShadow = null, bool reflectsSprites = false, bool destructible = false, string? name = null, TileLayer layer = TileLayer.Foreground, float acousticReflectivity = 1.0f)
	{
		var tileset = Assets.GetTileset(tilesetName);
		if (tileset == null)
			return new();

		return new Entry(name ?? tileset.Name, tileset.Tiles[0], (Point2 tile, Room room, Grid fg, Grid fgVisual, Grid bg, DualGrid dual) =>
		{
			Rng rng = new(tile.X + tile.Y * room.Size.X);
			var target = layer == TileLayer.Background ? bg : isFg ? fg : fgVisual;
			target.Set(tile.X, tile.Y, tileset.GetRandomTile(ref rng), reflectsSprites && !isFg ? 0.55f : 1.0f);
		}, surfaceType, blocksMovement: isFg, castsShadow: castsShadow ?? isFg, reflectsSprites: reflectsSprites, destructible: destructible, layer: layer, tilesetName: tilesetName, acousticReflectivity: acousticReflectivity);
	}

	public static Entry AsSlopeTile(string tilesetName, string surfaceType, string? name = null, float acousticReflectivity = 1.0f)
	{
		var tileset = Assets.GetTileset(tilesetName);
		if (tileset == null)
			return new();

		var image = Assets.GetSlopeSubtexture(tilesetName, Room.SlopeDirection.DownRight, Room.SlopeFill.BelowLine, 1, 0);
		if (image.Texture == null)
			image = tileset.Tiles[0];

		return new Entry(name ?? $"{tileset.Name} Slope", image, (Point2 tile, Room room, Grid fg, Grid fgVisual, Grid bg, DualGrid dual) =>
		{
			var seed = StableVariantSeed(room, tilesetName, tile);
			if (room.TryGetSlopeTile(tile, out var slope))
				fg.Set(tile.X, tile.Y, Assets.GetSlopeSubtexture(tilesetName, slope.Direction, slope.Fill, slope.RunLength, slope.RunIndex, seed));
			else
				fg.Set(tile.X, tile.Y, image);
		}, surfaceType, blocksMovement: true, castsShadow: true, isSlope: true, slopeTilesetName: tilesetName, tilesetName: tilesetName, acousticReflectivity: acousticReflectivity);
	}

	public static int StableVariantSeed(Room room, string key, Point2 tile)
	{
		unchecked
		{
			var worldTile = room.TileSpacePosition + tile;
			var hash = (uint)worldTile.X * 0x8da6b343u;
			hash ^= (uint)worldTile.Y * 0xd8163841u;
			foreach (var c in key)
				hash = (hash ^ c) * 0xcb1ab31fu;
			hash ^= hash >> 16;
			hash *= 0x7feb352du;
			hash ^= hash >> 15;
			hash *= 0x846ca68bu;
			hash ^= hash >> 16;
			return (int)hash;
		}
	}

	public static Entry AsDualTile(string tilesetName, string dualTilesetName, string surfaceType, TileLayer layer = TileLayer.Foreground, string? name = null, float acousticReflectivity = 1.0f)
	{
		var tileset = Assets.GetTileset(tilesetName);
		var dualTileset = Assets.GetTileset(dualTilesetName);
		if (tileset == null || dualTileset == null)
			throw new InvalidOperationException($"Dual tile '{tilesetName}' requires tilesets '{tilesetName}' and '{dualTilesetName}'.");

		return new Entry(name ?? tileset.Name, tileset.Tiles[0], (Point2 tile, Room room, Grid fg, Grid fgVisual, Grid bg, DualGrid dual) =>
		{
			if (layer == TileLayer.Foreground)
				fg.Set(tile.X, tile.Y, new());
			else
				bg.Set(tile.X, tile.Y, new());
		}, surfaceType, blocksMovement: layer == TileLayer.Foreground, castsShadow: layer == TileLayer.Foreground, dualTilesetName: dualTilesetName, layer: layer, tilesetName: tilesetName, acousticReflectivity: acousticReflectivity);
	}

	// Registers the Default types built into the game.
	public static void RegisterTypes()
	{
		ActorSpecs.ClearActorTypes();
		ActorSpecs.RegisterActor<Player>("Player");
		ActorSpecs.RegisterActor<Light>("Light");
		ActorSpecs.RegisterActor<HiddenWall>("HiddenWall");
		ActorSpecs.RegisterActor<RevealDoor>("RevealDoor");
		ActorSpecs.RegisterActor<Jumpthru>("Jumpthru");
		ActorSpecs.RegisterActor<Bramble>("Bramble");
		ActorSpecs.RegisterActor<Spitter>("Spitter");
		ActorSpecs.RegisterActor<WallSpitter>("WallSpitter");
		ActorSpecs.RegisterActor<Mosquito>("Mosquito");
		ActorSpecs.RegisterActor<Floater>("Floater");
		ActorSpecs.RegisterActor<Watcher>("Watcher");
		ActorSpecs.RegisterActor<Burrower>("Burrower");
		ActorSpecs.RegisterActor<Dropper>("Dropper");
		ActorSpecs.RegisterActor<LedgePatrol>("LedgePatrol");
		ActorSpecs.RegisterActor<Door>("Door");
		ActorSpecs.RegisterActor<LinkedDoor>("LinkedDoor");
		ActorSpecs.RegisterActor<Blob>("Blob");
		ActorSpecs.RegisterActor<GhostFrog>("GhostFrog");
		ActorSpecs.RegisterActor<TitleText>("TitleText");
		ActorSpecs.RegisterActor<Peter>("Peter");
		ActorSpecs.RegisterActor<ArrowTrap>("ArrowTrap");
		ActorSpecs.RegisterActor<Arrow>("Arrow");
		ActorSpecs.RegisterActor<WallBullet>("WallBullet");

		// Empty Type, purely for the editor
		Register('0', new Entry("Empty", new Subtexture(), null!));

		// Add Tile Types
		Register('1', AsDualTile("castle", "castle-dual", "Brick"));
		Register('2', AsSlopeTile("castle", "Brick", "Castle Slope"));
		Register('G', AsDualTile("grass", "grass-dual", "Grass", acousticReflectivity: 0.15f));
		Register('S', AsSlopeTile("grass", "Grass", "Grass Slope", acousticReflectivity: 0.15f));
		Register('T', AsDualTile("stone", "stone-dual", "Brick"));
		Register('g', AsTile("plants", false, "Grass", acousticReflectivity: 0.20f));
		Register('i', AsTile("gates", isFg: false, surfaceType: "Brick", acousticReflectivity: 0.05f));
		Register('r', AsTile("rocks", false, acousticReflectivity: 0.75f));
		Register('w', AsTile("water", false, "Water", reflectsSprites: true, acousticReflectivity: 0.55f));
		Register('#', Assets.GetTileset("back-dual") != null
			? AsDualTile("back", "back-dual", "Background", TileLayer.Background)
			: AsTile("back", false, layer: TileLayer.Background));

		RegisterActorPaletteEntries();
		RegisterActorSpecPaletteEntries();

	}

	private static void RegisterActorPaletteEntries()
	{
		ActorPalette.Add(ActorPaletteEntry.FromActor<Jumpthru>("jumpthru"));
		ActorPalette.Add(ActorPaletteEntry.FromActor<Player>("player", new Point2(4, 8)));
		ActorPalette.Add(ActorPaletteEntry.FromActor<HiddenWall>("door", editorBounds: new RectInt(0, 0, HiddenWall.DefaultSize.X, HiddenWall.DefaultSize.Y)));
		ActorPalette.Add(ActorPaletteEntry.FromActor<RevealDoor>("door", editorBounds: new RectInt(0, 0, RevealDoor.DefaultSize.X, RevealDoor.DefaultSize.Y)));
		ActorPalette.Add(ActorPaletteEntry.FromActor<Bramble>("bramble", new Point2(4, 8)));
		ActorPalette.Add(ActorPaletteEntry.FromActor<Spitter>("spitter", new Point2(4, 8)));
		ActorPalette.Add(ActorPaletteEntry.FromActor<WallSpitter>("spitter", new Point2(4, 8)));
		ActorPalette.Add(ActorPaletteEntry.FromActor<Mosquito>("mosquito", new Point2(4, 4)));
		ActorPalette.Add(ActorPaletteEntry.FromActor<Floater>("floater", new Point2(4, 4)));
		ActorPalette.Add(ActorPaletteEntry.FromActor<Watcher>("spitter", new Point2(4, 8)));
		ActorPalette.Add(ActorPaletteEntry.FromActor<Burrower>("blob", new Point2(4, 8)));
		ActorPalette.Add(ActorPaletteEntry.FromActor<Dropper>("bramble", new Point2(4, 8)));
		ActorPalette.Add(ActorPaletteEntry.FromActor<LedgePatrol>("blob", new Point2(4, 8)));
		ActorPalette.Add(ActorPaletteEntry.FromActor<Light>("fire", new Point2(4, 4)));
		ActorPalette.Add(ActorPaletteEntry.FromActor<Door>("door", new Point2(4, 8)));
		ActorPalette.Add(ActorPaletteEntry.FromActor<LinkedDoor>("back_door", new Point2(4, 8)));
		ActorPalette.Add(ActorPaletteEntry.FromActor<Blob>("blob", new Point2(4, 8)));
		ActorPalette.Add(ActorPaletteEntry.FromActor<GhostFrog>("ghostfrog", new Point2(4, 8)));
		ActorPalette.Add(ActorPaletteEntry.FromActor<TitleText>("heart", new Point2(4, 4)));
		ActorPalette.Add(ActorPaletteEntry.FromActor<Peter>("peter", new Point2(4, 4)));
		ActorPalette.Add(ActorPaletteEntry.FromActor<ArrowTrap>("arrow_trap", new Point2(4, 4)));
	}

	private static void RegisterActorSpecPaletteEntries()
	{
		foreach (var (id, spec) in ActorSpecs.Specs.OrderBy(it => it.Id))
		{
			if (string.Equals(ActorSpecs.GetTypeName(spec), "Light", StringComparison.OrdinalIgnoreCase))
			{
				ActorPalette.Add(ActorPaletteEntry.FromActorSpec(id, "fire", new Point2(4, 4)));
				continue;
			}

			var typeName = ActorSpecs.GetTypeName(spec);
			if (string.Equals(typeName, "Door", StringComparison.OrdinalIgnoreCase) ||
			    string.Equals(typeName, "LinkedDoor", StringComparison.OrdinalIgnoreCase))
				ActorPalette.Add(ActorPaletteEntry.FromActorSpec(id, "door", new Point2(4, 8)));
		}
	}

	public static void Clear()
	{
		Entries.Clear();
		ActorPalette.Clear();
	}

}
