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

	/// <summary>
	/// Used to Place a Tile
	/// </summary>
	public delegate void TileFn(Point2 tile, Room room, Grid fg, Grid bg, DualGrid dual);

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

		public Entry(string name, Subtexture image, TileFn tile, string? surfaceType = null, bool blocksMovement = false, bool castsShadow = false, bool reflectsSprites = false, bool destructible = false)
		{
			Name = name;
			Image = image;
			Tile = tile;
			SurfaceType = surfaceType;
			BlocksMovement = blocksMovement;
			CastsShadow = castsShadow;
			ReflectsSprites = reflectsSprites;
			Destructible = destructible;
		}
	}

	public readonly struct ActorPaletteEntry
	{
		public readonly string Name;
		public readonly Subtexture Image;
		public readonly Vector2 Origin;
		public readonly Point2 Offset;
		public readonly RectInt? EditorBounds;
		public readonly string? ActorTypeName;
		public readonly string? ActorSpecId;

		private ActorPaletteEntry(string name, Subtexture image, Vector2 origin, Point2 offset, string? actorTypeName, string? actorSpecId, RectInt? editorBounds = null)
		{
			Name = name;
			Image = image;
			Origin = origin;
			Offset = offset;
			ActorTypeName = actorTypeName;
			ActorSpecId = actorSpecId;
			EditorBounds = editorBounds;
		}

		public static ActorPaletteEntry FromActor<T>(string spriteName, Point2? offset = null, RectInt? editorBounds = null) where T : Actor
		{
			var sprite = Assets.GetSprite(spriteName);
			if (sprite == null)
				return new();

			var typeName = typeof(T).Name;
			return new ActorPaletteEntry(typeName, sprite.Frames[0].Subtexture, sprite.Origin, offset ?? Point2.Zero, typeName, null, editorBounds);
		}

		public static ActorPaletteEntry FromActorSpec(string specId, string spriteName, Point2? offset = null, RectInt? editorBounds = null)
		{
			var spec = ActorSpecs.Get(specId);
			var sprite = Assets.GetSprite(spriteName);
			if (sprite == null || spec == null)
				return new();

			return new ActorPaletteEntry(specId, sprite.Frames[0].Subtexture, sprite.Origin, offset ?? Point2.Zero, ActorSpecs.GetTypeName(spec), specId, editorBounds);
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

	public static Entry AsTile(string tilesetName, bool isFg, string? surfaceType = null, bool? castsShadow = null, bool reflectsSprites = false, bool destructible = false, string? name = null)
	{
		var tileset = Assets.GetTileset(tilesetName);
		if (tileset == null)
			return new();

		return new Entry(name ?? tileset.Name, tileset.Tiles[0], (Point2 tile, Room room, Grid fg, Grid bg, DualGrid dual) =>
		{
			Rng rng = new(tile.X + tile.Y * room.Size.X);
			(isFg ? fg : bg).Set(tile.X, tile.Y, tileset.GetRandomTile(ref rng));
		}, surfaceType, blocksMovement: isFg, castsShadow: castsShadow ?? isFg, reflectsSprites: reflectsSprites, destructible: destructible);
	}

	public static Entry AsDualTile(string tilesetName, string dualTilesetName, string surfaceType)
	{
		var tileset = Assets.GetTileset(tilesetName);
		if (tileset == null)
			return new();

		return new Entry(tileset.Name, tileset.Tiles[0], (Point2 tile, Room room, Grid fg, Grid bg, DualGrid dual) =>
		{
			fg.Set(tile.X, tile.Y, new());
		}, surfaceType, blocksMovement: true, castsShadow: true);
	}

	// Registers the Default types built into the game.
	public static void RegisterTypes()
	{
		ActorSpecs.ClearActorTypes();
		ActorSpecs.RegisterActor<Player>("Player");
		ActorSpecs.RegisterActor<Light>("Light");
		ActorSpecs.RegisterActor<Jumpthru>("Jumpthru");
		ActorSpecs.RegisterActor<Bramble>("Bramble");
		ActorSpecs.RegisterActor<Spitter>("Spitter");
		ActorSpecs.RegisterActor<Mosquito>("Mosquito");
		ActorSpecs.RegisterActor<Door>("Door");
		ActorSpecs.RegisterActor<Blob>("Blob");
		ActorSpecs.RegisterActor<GhostFrog>("GhostFrog");
		ActorSpecs.RegisterActor<TitleText>("TitleText");

		// Empty Type, purely for the editor
		Register('0', new Entry("Empty", new Subtexture(), null!));

		// Add Tile Types
		Register('1', AsTile("castle", true, "Brick"));
		Register('x', AsTile("castle", true, "Brick", destructible: true, name: "Breakable Brick"));
		Register('G', AsTile("grass", true, "Grass"));
		Register('X', AsTile("grass", true, "Grass", destructible: true, name: "Breakable Grass"));
		//Register('1', AsDualTile("castle", "castle-dual", "Brick"));
		//Register('G', AsDualTile("grass", "grass-dual", "Grass"));
		Register('g', AsTile("plants", false, "Grass"));
		Register('w', AsTile("water", false, "Water", reflectsSprites: true));
		Register('#', AsTile("back", false));

		RegisterActorPaletteEntries();
		RegisterActorSpecPaletteEntries();
	}

	private static void RegisterActorPaletteEntries()
	{
		ActorPalette.Add(ActorPaletteEntry.FromActor<Jumpthru>("jumpthru", editorBounds: new RectInt(0, 0, Game.TileSize, Game.TileSize / 4)));
		ActorPalette.Add(ActorPaletteEntry.FromActor<Player>("player", new Point2(4, 8)));
		ActorPalette.Add(ActorPaletteEntry.FromActor<Bramble>("bramble", new Point2(4, 8)));
		ActorPalette.Add(ActorPaletteEntry.FromActor<Spitter>("spitter", new Point2(4, 8)));
		ActorPalette.Add(ActorPaletteEntry.FromActor<Mosquito>("mosquito", new Point2(4, 4)));
		ActorPalette.Add(ActorPaletteEntry.FromActor<Light>("fire", new Point2(4, 4)));
		ActorPalette.Add(ActorPaletteEntry.FromActor<Door>("door", new Point2(4, 8)));
		ActorPalette.Add(ActorPaletteEntry.FromActor<Blob>("blob", new Point2(4, 8)));
		ActorPalette.Add(ActorPaletteEntry.FromActor<GhostFrog>("ghostfrog", new Point2(4, 8)));
		ActorPalette.Add(ActorPaletteEntry.FromActor<TitleText>("heart", new Point2(4, 4)));
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

			if (string.Equals(ActorSpecs.GetTypeName(spec), "Door", StringComparison.OrdinalIgnoreCase))
				ActorPalette.Add(ActorPaletteEntry.FromActorSpec(id, "door", new Point2(4, 8)));
		}
	}

	public static void Clear()
	{
		Entries.Clear();
		ActorPalette.Clear();
	}

}
