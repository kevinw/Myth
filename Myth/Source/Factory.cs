using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Reflection;
using Foster.Framework;

namespace Myth;

/// The Factory holds meta information on how to populate the Game when loading a Room.
/// It describes tile types and editor actor palette entries.
public static class Factory
{
	public const string DefaultSubstance = "Grass";
	public const string WaterSubstance = "Water";
	public const string MetalSubstance = "Metal";
	public static string SubstanceSoundType(string substance) => substance == MetalSubstance ? "Brick" : substance;
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
		public readonly string? Substance;
		public readonly bool BlocksMovement;
		public readonly bool CastsShadow;
		public readonly ReflectionMode Reflection;
		public readonly float ReflectionOpacity;
		public readonly bool ReflectsSprites => Reflection != ReflectionMode.None;
		public readonly bool Destructible;
		public readonly string? TilesetName;
		public readonly string? SlopeTilesetName;
		public readonly string? DualTilesetName;
		public readonly TileLayer Layer;
		public readonly float AcousticReflectivity;
		// Non-null on neural-texture tiles: room load grows one region simulation per island.
		public readonly NctTileSpec? NeuralTexture;
		public bool SupportsSlope => SlopeTilesetName != null || NeuralTexture is { HasSlope: true };

		public Entry(string name, Subtexture image, TileFn tile, string? substance = null, bool blocksMovement = false, bool castsShadow = false, ReflectionMode reflection = ReflectionMode.None, float reflectionOpacity = 0.5f, bool destructible = false, string? slopeTilesetName = null, string? dualTilesetName = null, TileLayer layer = TileLayer.Foreground, string? tilesetName = null, float acousticReflectivity = 1.0f, NctTileSpec? neuralTexture = null)
		{
			Name = name;
			Image = image;
			Tile = tile;
			Substance = substance;
			BlocksMovement = blocksMovement;
			CastsShadow = castsShadow;
			Reflection = reflection;
			ReflectionOpacity = reflectionOpacity;
			Destructible = destructible;
			TilesetName = tilesetName;
			SlopeTilesetName = slopeTilesetName;
			DualTilesetName = dualTilesetName;
			Layer = layer;
			AcousticReflectivity = Math.Clamp(acousticReflectivity, 0.0f, 1.0f);
			NeuralTexture = neuralTexture;
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
		public readonly float PreviewScale;

		ActorPaletteEntry(string name, Subtexture image, Vector2 origin, Point2 offset, RectInt defaultEditorBounds, string? actorTypeName, string? actorSpecId, RectInt? editorBounds = null, ActorTag tags = ActorTag.None, string? propSpriteName = null, Subtexture previewImage = default, float previewScale = 1)
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
			PreviewScale = previewScale;
		}

		public static ActorPaletteEntry FromActor<T>(string spriteName, Point2? offset = null, RectInt? editorBounds = null, ActorTag tags = ActorTag.None, SpriteAnchor anchor = SpriteAnchor.Auto) where T : Actor
		{
			var sprite = Assets.GetSprite(spriteName) ?? throw new InvalidOperationException($"Actor palette entry '{typeof(T).Name}' references missing sprite '{spriteName}'.");
			var typeName = typeof(T).Name;
			var origin = sprite.ResolveOrigin(anchor);
			return new ActorPaletteEntry(typeName, sprite.Frames[0].VisibleSubtexture, origin, offset ?? Point2.Zero, GetDefaultEditorBounds(sprite, origin), typeName, null, editorBounds, tags, previewImage: sprite.Frames[0].Subtexture);
		}

#if EDITOR
		public static ActorPaletteEntry FromActorIcon(string typeName, Subtexture icon, Point2 offset, RectInt editorBounds, ActorTag tags)
		{
			var origin = icon.Size / 2;
			var previewScale = Game.TileSize / MathF.Max(icon.Width, icon.Height);
			return new ActorPaletteEntry(typeName, icon, origin, offset, editorBounds, typeName, null,
				editorBounds, tags, previewImage: icon, previewScale: previewScale);
		}
#endif

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

	// Entry names are the stable identity of a tile type (room legends map file chars to
	// them); chars are session-local handles assigned at registration.
	static readonly Dictionary<string, char> charsByEntryName = new(StringComparer.OrdinalIgnoreCase);
	static readonly Dictionary<char, char> legacySlopeChars = [];
	static readonly HashSet<string> legacySlopeNames = new(StringComparer.OrdinalIgnoreCase);

	// Tags declared on each [GameActor], keyed by registered type name, so spec/prefab
	// palette entries can inherit their underlying type's tags.
	static readonly Dictionary<string, ActorTag> actorTags = new(StringComparer.OrdinalIgnoreCase);
	static readonly Dictionary<Type, ActorTag> actorTypeTags = [];
	static readonly HashSet<Type> livePositionEditableActorTypes = [];
	static readonly HashSet<Type> liveResizableActorTypes = [];

	// Declared SpriteAnchor for each [GameActor], so palette boxes and spawned instances
	// resolve the same origin. Keyed by registered type name.
	static readonly Dictionary<string, SpriteAnchor> actorAnchors = new(StringComparer.OrdinalIgnoreCase);

	public static ActorTag TagsForType(string? typeName)
		=> typeName != null && actorTags.TryGetValue(typeName, out var tags) ? tags : ActorTag.None;

	public static ActorTag TagsForType(Type type)
		=> actorTypeTags.TryGetValue(type, out var tags) ? tags : ActorTag.None;
	public static bool LivePositionEditable(Type type) => livePositionEditableActorTypes.Contains(type);
	public static bool LiveResizable(Type type) => liveResizableActorTypes.Contains(type);

	public static SpriteAnchor AnchorForType(string? typeName)
		=> typeName != null && actorAnchors.TryGetValue(typeName, out var anchor) ? anchor : SpriteAnchor.Auto;

	// Registers a new Type
	public static void Register(char id, in Entry entry)
	{
		Entries[id] = entry;
		if (!string.IsNullOrEmpty(entry.Name))
			charsByEntryName[entry.Name] = id;
	}

	// Finds a given Type entry by its character ID
	public static Entry? Find(char id)
	{
		if (Entries.TryGetValue(id, out var entry))
			return entry;

		return null;
	}

	/// The session char for a tile type name (see room legends), or null if unregistered.
	public static char? CharForName(string name)
		=> charsByEntryName.TryGetValue(name, out var id) ? id : null;

	public static bool IsLegacySlopeName(string name)
		=> legacySlopeNames.Contains(name);

	public static bool TryGetLegacySlopeBase(char tile, out char baseTile)
		=> legacySlopeChars.TryGetValue(tile, out baseTile);

	static void RegisterLegacySlope(char tile, string name, char baseTile)
	{
		if (tile != '\0')
			legacySlopeChars[tile] = baseTile;
		legacySlopeNames.Add(name);
		charsByEntryName[name] = baseTile;
	}

	/// Picks a free char for a tile type: its preferred char if available, else the first
	/// letter of its name (upper then lower), else the first free char in a fixed alphabet.
	/// '0' means empty and 'f' is the legacy fungus tile; neither is ever assigned.
	static char AssignChar(char preferred, string name)
	{
		const string reserved = "0f2SY$J";
		bool Free(char c) => !reserved.Contains(c) && !Entries.ContainsKey(c)
			&& !TileKits.Registrations.Exists(it => it.HasSlope && it.SlopePreferredChar == c);

		if (preferred != '\0' && Free(preferred))
			return preferred;
		if (name.Length > 0 && char.IsAsciiLetter(name[0]))
		{
			if (Free(char.ToUpperInvariant(name[0])))
				return char.ToUpperInvariant(name[0]);
			if (Free(char.ToLowerInvariant(name[0])))
				return char.ToLowerInvariant(name[0]);
		}
		foreach (var c in "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz123456789!?*+=@&")
			if (Free(c))
				return c;
		throw new InvalidOperationException($"No free tile chars left registering '{name}'.");
	}

	public static Entry AsTile(string tilesetName, bool isFg, string? substance = null, bool? castsShadow = null, ReflectionMode reflection = ReflectionMode.None, float reflectionOpacity = 0.5f, bool destructible = false, string? name = null, TileLayer layer = TileLayer.Foreground, float acousticReflectivity = 1.0f, bool drawsVisual = true)
	{
		var tileset = Assets.GetTileset(tilesetName, logMissing: true);
		if (tileset == null)
			return new();

		return new Entry(name ?? tileset.Name, tileset.Tiles[0], (Point2 tile, Room room, Grid fg, Grid fgVisual, Grid bg, DualGrid dual) =>
		{
			// Water draws nothing into the tile field; WaterSurface renders it dynamically
			// instead. The substance / reflection registration below still apply, so swim
			// physics and reflections are unaffected.
			if (!drawsVisual)
				return;
			Rng rng = new(tile.X + tile.Y * room.Size.X);
			var target = layer == TileLayer.Background ? bg : isFg ? fg : fgVisual;
			var image = tileset.GetRandomTile(ref rng, out var normal);
			target.Set(tile.X, tile.Y, image, reflection != ReflectionMode.None && !isFg ? 0.55f : 1.0f, normal);
		}, substance, blocksMovement: isFg, castsShadow: castsShadow ?? isFg, reflection: reflection, reflectionOpacity: reflectionOpacity, destructible: destructible, layer: layer, tilesetName: tilesetName, acousticReflectivity: acousticReflectivity);
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

	public static Entry AsDualTile(string tilesetName, string dualTilesetName, string substance, TileLayer layer = TileLayer.Foreground, string? name = null, float acousticReflectivity = 1.0f, ReflectionMode reflection = ReflectionMode.None, float reflectionOpacity = 0.5f, bool hasSlope = false)
	{
		var tileset = Assets.GetTileset(tilesetName);
		var dualTileset = Assets.GetTileset(dualTilesetName);
		if (tileset == null || dualTileset == null)
			throw new InvalidOperationException($"Dual tile '{tilesetName}' requires tilesets '{tilesetName}' and '{dualTilesetName}'.");

		return new Entry(name ?? tileset.Name, tileset.Tiles[0], (Point2 tile, Room room, Grid fg, Grid fgVisual, Grid bg, DualGrid dual) =>
		{
			var grid = layer == TileLayer.Foreground ? fg : bg;
			if (hasSlope && room.TryGetSlopeTile(tile, layer, out var slope))
				grid.Set(tile.X, tile.Y, Assets.GetSlopeSubtexture(tilesetName, slope.Direction, slope.Fill, slope.RunLength, slope.RunIndex, StableVariantSeed(room, tilesetName, tile)));
			else
				grid.Set(tile.X, tile.Y, new());
		}, substance, blocksMovement: layer == TileLayer.Foreground, castsShadow: layer == TileLayer.Foreground, reflection: reflection, reflectionOpacity: reflectionOpacity, slopeTilesetName: hasSlope ? tilesetName : null, dualTilesetName: dualTilesetName, layer: layer, tilesetName: tilesetName, acousticReflectivity: acousticReflectivity);
	}

	// A neural-texture tile: paints like a normal tile but draws nothing itself. At room load,
	// SpawnNctRegions grows one region simulation per connected island (see NctTileSpec). All the
	// look lives in the spec's JSON, so a new one is data, not code.
	public static Entry AsNeuralTextureTile(NctTileSpec spec)
	{
		// No tileset image; use a neutral placeholder for the palette icon until we bake per-model
		// swatches. GetTileset is safe to call here — tilesets are built before tile registration.
		var icon = Assets.GetTileset("myth-back")?.Tiles[0] ?? default;
		return new Entry(spec.Name, icon, (Point2 tile, Room room, Grid fg, Grid fgVisual, Grid bg, DualGrid dual) =>
		{
			if (spec.Layer == TileLayer.Foreground)
				fg.Set(tile.X, tile.Y, new());
			else
				bg.Set(tile.X, tile.Y, new());
		}, substance: spec.Solid ? spec.Substance : null, blocksMovement: spec.Solid, layer: spec.Layer, neuralTexture: spec);
	}

	// Neural-texture tile types declared by *.nct.json specs (Assets/Sources/Tilesets), loaded
	// alongside the tile kits so a new one needs no code change; chars are assigned at registration
	// and room legends keep files stable.
	static void RegisterNctTiles()
	{
		foreach (var spec in NctTiles.Specs)
			Register(AssignChar(spec.PreferredChar, spec.Name), AsNeuralTextureTile(spec));
	}

	// Registers a single [GameActor] type: its spec type-name mapping, plus a palette
	// entry when the attribute carries an Icon or Sprite. The source generator calls this for every
	// [GameActor] via GeneratedActors.RegisterAll().
	public static void Register<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor | DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.PublicProperties)] T>() where T : Actor
	{
		var attr = typeof(T).GetCustomAttribute<GameActorAttribute>();
		var name = attr?.Name ?? typeof(T).Name;
		var tags = attr?.Tags ?? ActorTag.None;
		ActorSpecs.RegisterActor<T>(name, attr?.ShowFacing ?? true);
		actorTags[name] = tags;
		actorTypeTags[typeof(T)] = tags;
		if (attr?.LivePositionEditable == true)
			livePositionEditableActorTypes.Add(typeof(T));
		if (attr?.LiveResizable == true)
		{
			if (!attr.LivePositionEditable)
				throw new InvalidOperationException($"[GameActor] on '{typeof(T).Name}' enables LiveResizable without LivePositionEditable.");
			if (!typeof(IResizable).IsAssignableFrom(typeof(T)))
				throw new InvalidOperationException($"[GameActor] on '{typeof(T).Name}' enables LiveResizable without implementing IResizable.");
			liveResizableActorTypes.Add(typeof(T));
		}
		var anchor = attr?.Anchor ?? SpriteAnchor.Auto;
		actorAnchors[name] = anchor;

		if (attr is { HideFromPalette: true })
			return;

		if (attr?.Icon is { } editorIconName)
		{
#if EDITOR
			if (attr.EditorBounds is not { } editorBounds)
				throw new InvalidOperationException($"[GameActor] on '{typeof(T).Name}' uses Icon without explicit editor bounds.");
			if (!EditorIcons.TryGetPaletteIcon(editorIconName, out var icon))
				throw new InvalidOperationException($"[GameActor] on '{typeof(T).Name}' references unknown editor icon '{editorIconName}'.");
			var iconOffset = attr.OffsetX != 0 || attr.OffsetY != 0 ? attr.Offset : Point2.Zero;
			ActorPalette.Add(ActorPaletteEntry.FromActorIcon(name, icon, iconOffset, editorBounds, tags));
#endif
			return;
		}

		// Palette icon defaults to a sprite named after the class. An explicit name that
		// doesn't resolve is a typo (surface it); a missing conventional sprite leaves the
		// actor out of the palette, so log it rather than skipping silently.
		var iconName = attr?.Sprite ?? typeof(T).Name;
		var sprite = Assets.GetSprite(iconName);
		if (sprite == null)
		{
			if (attr?.Sprite != null)
				throw new InvalidOperationException($"[GameActor] on '{typeof(T).Name}' references missing sprite '{iconName}'.");
			Log.Error($"[GameActor] '{typeof(T).Name}' has no palette icon: no sprite named '{iconName}' (set Sprite on the attribute or HideFromPalette to skip).");
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
		actorTypeTags.Clear();
		livePositionEditableActorTypes.Clear();
		liveResizableActorTypes.Clear();
		GeneratedActors.RegisterAll();

		string GRASS_SUBSTANCE = "Grass";
		string BRICK_SUBSTANCE = "Brick";

		// Add Tile Types
		Register('1', AsDualTile("castle", "castle-dual", BRICK_SUBSTANCE, hasSlope: true));
		RegisterLegacySlope('2', "Castle Slope", '1');
		// 'G' grass is a JSON kit (grass.kit.json) -- registered by RegisterTileKits below.
		Register('M', AsDualTile("metal", "metal-dual", MetalSubstance, reflection: ReflectionMode.Flat, reflectionOpacity: 0.7f, hasSlope: true));


		Register('T', AsDualTile("stone", "stone-dual", BRICK_SUBSTANCE, hasSlope: true));
		RegisterLegacySlope('Y', "Stone Slope", 'T');

		Register('%', AsDualTile("crosshatch", "crosshatch-dual", BRICK_SUBSTANCE, hasSlope: true));
		RegisterLegacySlope('$', "Crosshatch Slope", '%');

		Register('P', AsDualTile("dirt", "dirt-dual", GRASS_SUBSTANCE, acousticReflectivity: 0.15f, hasSlope: true));
		RegisterLegacySlope('J', "Dirt Slope", 'P');
		Register('g', AsTile("plants", false, GRASS_SUBSTANCE, acousticReflectivity: 0.20f));
		Register('i', AsTile("gates", isFg: false, substance: BRICK_SUBSTANCE, acousticReflectivity: 0.05f));
		Register('r', AsTile("rocks", false, acousticReflectivity: 0.75f));
		Register('w', AsTile("water", false, WaterSubstance, reflection: ReflectionMode.Distorted, reflectionOpacity: 0.5f, acousticReflectivity: 0.55f, drawsVisual: false));
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
		RegisterTileKits();
		RegisterNctTiles();
		foreach (var spriteName in Assets.Sprites.Keys.Where(it => it.StartsWith(PropSpritePrefix, StringComparison.OrdinalIgnoreCase)).Order(StringComparer.OrdinalIgnoreCase))
			ActorPalette.Add(ActorPaletteEntry.FromProp(spriteName));
		RegisterActorSpecPaletteEntries();

#if EDITOR
		ActorSpecs.CaptureSavedSpecState();
#endif
	}

	// Tile types declared by JSON kit specs. The registration data comes from the specs in
	// dev and from the visual asset pack manifest in pack mode (TileKits.Registrations), so
	// a new tileset needs no code change; chars are assigned at registration and room files
	// stay stable through their legends.
	static void RegisterTileKits()
	{
		foreach (var kit in TileKits.Registrations)
		{
			var tile = AssignChar(kit.PreferredChar, kit.Name);
			Register(tile, AsDualTile(kit.Name, kit.DualName, kit.Substance, layer: kit.Layer, reflection: kit.Reflection, reflectionOpacity: kit.ReflectionOpacity, acousticReflectivity: kit.AcousticReflectivity, hasSlope: kit.HasSlope));
			if (kit.HasSlope)
				RegisterLegacySlope(kit.SlopePreferredChar, kit.SlopeEntryName, tile);
		}
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
		charsByEntryName.Clear();
		legacySlopeChars.Clear();
		legacySlopeNames.Clear();
		ActorPalette.Clear();
	}

}
