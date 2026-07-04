using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Reflection;
using Foster.Framework;

namespace Myth;

/// The Factory holds meta information on how to populate the Game when loading a Room.
/// It describes tile types and editor actor palette entries.
public static class Factory
{
	public const string DefaultSurfaceType = "Grass";
	public const string WaterSurfaceType = "Water";
	const string PropSpritePrefix = "Props/";

	/// Background tile type that marks a region for NCALayer to grow fungus into.
	public const char FungusTile = 'f';

	public enum TileLayer
	{
		Foreground,
		Background
	}

	public enum ReflectionMode
	{
		None,
		Flat,
		Distorted
	}

	/// Used to Place a Tile
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
		public readonly ReflectionMode Reflection;
		public readonly float ReflectionOpacity;
		public readonly bool ReflectsSprites => Reflection != ReflectionMode.None;
		public readonly bool Destructible;
		public readonly bool IsSlope;
		public readonly string? TilesetName;
		public readonly string? SlopeTilesetName;
		public readonly string? DualTilesetName;
		public readonly TileLayer Layer;
		public readonly float AcousticReflectivity;

		public Entry(string name, Subtexture image, TileFn tile, string? surfaceType = null, bool blocksMovement = false, bool castsShadow = false, ReflectionMode reflection = ReflectionMode.None, float reflectionOpacity = 0.5f, bool destructible = false, bool isSlope = false, string? slopeTilesetName = null, string? dualTilesetName = null, TileLayer layer = TileLayer.Foreground, string? tilesetName = null, float acousticReflectivity = 1.0f)
		{
			Name = name;
			Image = image;
			Tile = tile;
			SurfaceType = surfaceType;
			BlocksMovement = blocksMovement;
			CastsShadow = castsShadow;
			Reflection = reflection;
			ReflectionOpacity = reflectionOpacity;
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
		public readonly Subtexture PreviewImage;
		public readonly Vector2 Origin;
		public readonly Point2 Offset;
		public readonly RectInt? EditorBounds;
		public readonly RectInt DefaultEditorBounds;
		public readonly string? ActorTypeName;
		public readonly string? ActorSpecId;
		public readonly string? PropSpriteName;
		public readonly ActorTag Tags;

		ActorPaletteEntry(string name, Subtexture image, Vector2 origin, Point2 offset, RectInt defaultEditorBounds, string? actorTypeName, string? actorSpecId, RectInt? editorBounds = null, ActorTag tags = ActorTag.None, string? propSpriteName = null, Subtexture previewImage = default)
		{
			Name = name;
			Image = image;
			PreviewImage = previewImage.Texture != null ? previewImage : image;
			Origin = origin;
			Offset = offset;
			DefaultEditorBounds = defaultEditorBounds;
			ActorTypeName = actorTypeName;
			ActorSpecId = actorSpecId;
			PropSpriteName = propSpriteName;
			EditorBounds = editorBounds;
			Tags = tags;
		}

		public static ActorPaletteEntry FromActor<T>(string spriteName, Point2? offset = null, RectInt? editorBounds = null, ActorTag tags = ActorTag.None, SpriteAnchor anchor = SpriteAnchor.Auto) where T : Actor
		{
			var sprite = Assets.GetSprite(spriteName) ?? throw new InvalidOperationException($"Actor palette entry '{typeof(T).Name}' references missing sprite '{spriteName}'.");
			var typeName = typeof(T).Name;
			var origin = sprite.ResolveOrigin(anchor);
			return new ActorPaletteEntry(typeName, sprite.Frames[0].VisibleSubtexture, origin, offset ?? Point2.Zero, GetDefaultEditorBounds(sprite, origin), typeName, null, editorBounds, tags, previewImage: sprite.Frames[0].Subtexture);
		}

		public static ActorPaletteEntry FromActorSpec(string specId, string spriteName, Point2? offset = null, RectInt? editorBounds = null)
		{
			var spec = ActorSpecs.Get(specId);
			var sprite = Assets.GetSprite(spriteName);
			if (spec == null) throw new InvalidOperationException($"Actor palette spec entry '{specId}' references missing actor spec.");
			if (sprite == null) throw new InvalidOperationException($"Actor palette spec entry '{specId}' references missing sprite '{spriteName}'.");

			var typeName = ActorSpecs.GetTypeName(spec);
			var origin = sprite.ResolveOrigin(AnchorForType(typeName));
			return new ActorPaletteEntry(specId, sprite.Frames[0].VisibleSubtexture, origin, offset ?? Point2.Zero, GetDefaultEditorBounds(sprite, origin), typeName, specId, editorBounds, TagsForType(typeName), previewImage: sprite.Frames[0].Subtexture);
		}

		public static ActorPaletteEntry FromProp(string spriteName)
		{
			var sprite = Assets.GetSprite(spriteName) ?? throw new InvalidOperationException($"Prop palette entry references missing sprite '{spriteName}'.");
			var origin = sprite.ResolveOrigin(SpriteAnchor.TopLeft);
			return new ActorPaletteEntry(spriteName[PropSpritePrefix.Length..], sprite.Frames[0].VisibleSubtexture, origin, Point2.Zero, GetDefaultEditorBounds(sprite, origin), nameof(Prop), null, tags: ActorTag.Decoration, propSpriteName: spriteName, previewImage: sprite.Frames[0].Subtexture);
		}

		public static RectInt GetDefaultEditorBounds(Sprite sprite, Vector2 origin)
		{
			if (sprite.Frames.Count <= 0)
				return new RectInt(-4, -4, 8, 8);

			var frame = sprite.Frames[0].Subtexture;
			var left = -origin.X - frame.Frame.X;
			var top = -origin.Y - frame.Frame.Y;
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
	public static readonly Dictionary<char, Entry> Entries = [];
	public static readonly List<ActorPaletteEntry> ActorPalette = [];

	// Tags declared on each [GameActor], keyed by registered type name, so spec/prefab
	// palette entries can inherit their underlying type's tags.
	static readonly Dictionary<string, ActorTag> actorTags = new(StringComparer.OrdinalIgnoreCase);

	// Declared SpriteAnchor for each [GameActor], so palette boxes and spawned instances
	// resolve the same origin. Keyed by registered type name.
	static readonly Dictionary<string, SpriteAnchor> actorAnchors = new(StringComparer.OrdinalIgnoreCase);

	public static ActorTag TagsForType(string? typeName)
		=> typeName != null && actorTags.TryGetValue(typeName, out var tags) ? tags : ActorTag.None;

	public static SpriteAnchor AnchorForType(string? typeName)
		=> typeName != null && actorAnchors.TryGetValue(typeName, out var anchor) ? anchor : SpriteAnchor.Auto;

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

	public static Entry AsTile(string tilesetName, bool isFg, string? surfaceType = null, bool? castsShadow = null, ReflectionMode reflection = ReflectionMode.None, float reflectionOpacity = 0.5f, bool destructible = false, string? name = null, TileLayer layer = TileLayer.Foreground, float acousticReflectivity = 1.0f, bool drawsVisual = true)
	{
		var tileset = Assets.GetTileset(tilesetName, logMissing: true);
		if (tileset == null)
			return new();

		return new Entry(name ?? tileset.Name, tileset.Tiles[0], (Point2 tile, Room room, Grid fg, Grid fgVisual, Grid bg, DualGrid dual) =>
		{
			// Water draws nothing into the tile field; WaterSurface renders it dynamically
			// instead. The surface type / reflection registration below still apply, so swim
			// physics and reflections are unaffected.
			if (!drawsVisual)
				return;
			Rng rng = new(tile.X + tile.Y * room.Size.X);
			var target = layer == TileLayer.Background ? bg : isFg ? fg : fgVisual;
			var image = tileset.GetRandomTile(ref rng, out var normal);
			target.Set(tile.X, tile.Y, image, reflection != ReflectionMode.None && !isFg ? 0.55f : 1.0f, normal);
		}, surfaceType, blocksMovement: isFg, castsShadow: castsShadow ?? isFg, reflection: reflection, reflectionOpacity: reflectionOpacity, destructible: destructible, layer: layer, tilesetName: tilesetName, acousticReflectivity: acousticReflectivity);
	}

	public static Entry AsSlopeTile(string tilesetName, string surfaceType, string? name = null, float acousticReflectivity = 1.0f)
	{
		var tileset = Assets.GetTileset(tilesetName, logMissing: true);
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

	public static Entry AsDualTile(string tilesetName, string dualTilesetName, string surfaceType, TileLayer layer = TileLayer.Foreground, string? name = null, float acousticReflectivity = 1.0f, ReflectionMode reflection = ReflectionMode.None, float reflectionOpacity = 0.5f)
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
		}, surfaceType, blocksMovement: layer == TileLayer.Foreground, castsShadow: layer == TileLayer.Foreground, reflection: reflection, reflectionOpacity: reflectionOpacity, dualTilesetName: dualTilesetName, layer: layer, tilesetName: tilesetName, acousticReflectivity: acousticReflectivity);
	}

	// Registers a single [GameActor] type: its spec type-name mapping, plus a palette
	// entry when the attribute carries a Sprite. The source generator calls this for every
	// [GameActor] via GeneratedActors.RegisterAll().
	public static void Register<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor | DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.PublicProperties)] T>() where T : Actor
	{
		var attr = typeof(T).GetCustomAttribute<GameActorAttribute>();
		var name = attr?.Name ?? typeof(T).Name;
		ActorSpecs.RegisterActor<T>(name);
		actorTags[name] = attr?.Tags ?? ActorTag.None;
		var anchor = attr?.Anchor ?? SpriteAnchor.Auto;
		actorAnchors[name] = anchor;

		if (attr is { HideFromPalette: true })
			return;

		// Palette icon defaults to a sprite named after the class. An explicit name that
		// doesn't resolve is a typo (surface it); a missing conventional sprite just means
		// the actor isn't placeable, so skip it quietly.
		var iconName = attr?.Sprite ?? typeof(T).Name;
		var sprite = Assets.GetSprite(iconName);
		if (sprite == null)
		{
			if (attr?.Sprite != null)
				throw new InvalidOperationException($"[GameActor] on '{typeof(T).Name}' references missing sprite '{iconName}'.");
			return;
		}

		// Tile-placement offset: an explicit attribute offset wins; resizable region actors
		// snap to the tile corner; otherwise derive it from the resolved origin.
		var offset = attr is { } a && (a.OffsetX != 0 || a.OffsetY != 0) ? a.Offset
			: attr?.EditorBounds != null ? Point2.Zero
			: DeriveTileOffset(sprite, anchor);

		ActorPalette.Add(ActorPaletteEntry.FromActor<T>(iconName, offset, attr?.EditorBounds, attr?.Tags ?? ActorTag.None, anchor));
	}

	// Where the resolved origin should land within a clicked tile: centered horizontally, and
	// sitting on the tile bottom when the origin is in the sprite's lower half (a grounded
	// actor's feet) or centered otherwise (flying/decorative). No origin => the tile corner.
	static Point2 DeriveTileOffset(Sprite sprite, SpriteAnchor anchor)
	{
		if (sprite.Frames.Count == 0)
			return Point2.Zero;

		var origin = sprite.ResolveOrigin(anchor);
		if (origin == Vector2.Zero)
			return Point2.Zero;

		var cellHeight = sprite.Frames[0].Subtexture.Height;
		var grounded = cellHeight > 0 && origin.Y / cellHeight > 0.55f;
		return new Point2(Game.TileSize / 2, grounded ? Game.TileSize : Game.TileSize / 2);
	}

	// Registers the Default types built into the game.
	public static void RegisterTypes()
	{
		ActorSpecs.ClearActorTypes();
		actorTags.Clear();
		GeneratedActors.RegisterAll();

		string GRASS_SURF = "Grass";
		string BRICK_SURF = "Brick";

		// Add Tile Types
		Register('1', AsDualTile("castle", "castle-dual", BRICK_SURF));
		Register('2', AsSlopeTile("castle", BRICK_SURF, "Castle Slope"));
		Register('G', AsDualTile("grass", "grass-dual", GRASS_SURF, acousticReflectivity: 0.15f));
		Register('S', AsSlopeTile("grass", GRASS_SURF, "Grass Slope", acousticReflectivity: 0.15f));
		Register('M', AsDualTile("metal", "metal-dual", BRICK_SURF, reflection: ReflectionMode.Flat, reflectionOpacity: 0.7f));


		Register('T', AsDualTile("stone", "stone-dual", BRICK_SURF));
		Register('Y', AsSlopeTile("stone", BRICK_SURF, "Stone Slope"));

		Register('%', AsDualTile("crosshatch", "crosshatch-dual", BRICK_SURF));
		Register('$', AsSlopeTile("crosshatch", BRICK_SURF, "Crosshatch Slope"));

		Register('P', AsDualTile("dirt", "dirt-dual", GRASS_SURF, acousticReflectivity: 0.15f));
		Register('J', AsSlopeTile("dirt", GRASS_SURF, "Dirt Slope", acousticReflectivity: 0.15f));
		Register('g', AsTile("plants", false, GRASS_SURF, acousticReflectivity: 0.20f));
		Register('i', AsTile("gates", isFg: false, surfaceType: BRICK_SURF, acousticReflectivity: 0.05f));
		Register('r', AsTile("rocks", false, acousticReflectivity: 0.75f));
		Register('w', AsTile("water", false, WaterSurfaceType, reflection: ReflectionMode.Distorted, reflectionOpacity: 0.5f, acousticReflectivity: 0.55f, drawsVisual: false));
		Register('#', AsTile("back", false, layer: TileLayer.Background));
		Register('-', AsTile("back-right", false, layer: TileLayer.Background));
		Register(')', AsTile("back-ne", false, layer: TileLayer.Background));
		Register('(', AsTile("back-nw", false, layer: TileLayer.Background));
		Register('m', AsTile("myth-back", false, "Background", name: "Myth Back", layer: TileLayer.Background, acousticReflectivity: 0.35f));
		Register('u', AsTile("myth-runes", false, "Background", name: "Myth Runes", layer: TileLayer.Background, acousticReflectivity: 0.35f));
		Register('v', AsTile("myth-vines", false, "Grass", name: "Myth Vines", layer: TileLayer.Background, acousticReflectivity: 0.20f));
		// Fungus is now a per-tile region flag (TileFlags.Fungal, toggled with 'G'
		// in the editor), not a paintable tile. FungusTile remains only so old
		// rooms migrate on load (see Room.MigrateLegacyFungusTiles).
		foreach (var spriteName in Assets.Sprites.Keys.Where(it => it.StartsWith(PropSpritePrefix, StringComparison.OrdinalIgnoreCase)).Order(StringComparer.OrdinalIgnoreCase))
			ActorPalette.Add(ActorPaletteEntry.FromProp(spriteName));
		RegisterActorSpecPaletteEntries();

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
