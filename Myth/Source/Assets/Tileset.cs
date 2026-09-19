using System.Numerics;
using Foster.Framework;

namespace Myth;

public class Tileset
{
	public readonly record struct OccluderSegment(Vector2 A, Vector2 B);

	public string Name { get; }
	public int Columns { get; private set; }
	public int Rows { get; private set; }
	/// How many game tiles one fill pattern spans, per side (see TileKitSpec.FillTiles). 1 for an
	/// ordinary tileset, where every variant is interchangeable and the phase is always 0.
	public int FillTiles { get; private set; } = 1;
	public readonly List<Subtexture> Tiles = new();
	public readonly List<List<Subtexture>> Variants = [];
	public readonly List<Subtexture> NormalTiles = new();
	public readonly List<List<Subtexture>> NormalVariants = [];
	public readonly List<OccluderSegment[]> OccluderTiles = [];
	public readonly List<List<OccluderSegment[]>> OccluderVariants = [];

	public Tileset(string name, int columns, int rows)
	{
		Name = name;
		Columns = columns;
		Rows = rows;
		for (int i = 0; i < columns * rows; i ++)
			Tiles.Add(new());
	}

	public void Reload(int columns, int rows, List<Subtexture> tiles, List<List<Subtexture>>? variants = null, List<Subtexture>? normalTiles = null, List<List<Subtexture>>? normalVariants = null, List<OccluderSegment[]>? occluderTiles = null, List<List<OccluderSegment[]>>? occluderVariants = null, int fillTiles = 1)
	{
		Columns = columns;
		Rows = rows;
		FillTiles = fillTiles;

		Tiles.Clear();
		Tiles.AddRange(tiles);

		Variants.Clear();
		if (variants != null)
			Variants.AddRange(variants);

		NormalTiles.Clear();
		if (normalTiles != null)
			NormalTiles.AddRange(normalTiles);

		NormalVariants.Clear();
		if (normalVariants != null)
			NormalVariants.AddRange(normalVariants);

		OccluderTiles.Clear();
		if (occluderTiles != null)
			OccluderTiles.AddRange(occluderTiles);

		OccluderVariants.Clear();
		if (occluderVariants != null)
			OccluderVariants.AddRange(occluderVariants);
	}

	public Subtexture GetRandomTile(ref Rng rng)
		=> Tiles[rng.Int(Tiles.Count)];

	public Subtexture GetRandomTile(ref Rng rng, out Subtexture normal)
	{
		var index = rng.Int(Tiles.Count);
		normal = index < NormalTiles.Count ? NormalTiles[index] : new();
		return Tiles[index];
	}

	public Subtexture GetTile(int x, int y)
	{
		if (x < 0 || y < 0 || x >= Columns || y >= Rows)
			return new();

		var index = x * Rows + y;
		if (index < 0 || index >= Tiles.Count)
			return new();

		return Tiles[index];
	}

	/// Which tile of a multi-tile fill pattern a world tile shows. Floor-mod rather than `%`: rooms
	/// sit at negative world tiles, where `%` would mirror the pattern across the origin.
	public static int FillPhase(Point2 worldTile, int fillTiles)
		=> fillTiles <= 1 ? 0
			: FloorMod(worldTile.X, fillTiles) + FloorMod(worldTile.Y, fillTiles) * fillTiles;

	/// The macro block a world tile belongs to. Variant groups have to be hashed per block rather
	/// than per tile: if neighbours in one block landed on different groups they would show
	/// sub-tiles of different source patterns, and nothing would line up.
	public static Point2 FillBlock(Point2 worldTile, int fillTiles)
		=> fillTiles <= 1 ? worldTile
			: new Point2(FloorDiv(worldTile.X, fillTiles), FloorDiv(worldTile.Y, fillTiles));

	/// Variants are laid out group-major, phase-minor. The seed picks the group (pseudorandom, so
	/// contours vary); the phase is fixed by the tile's position, so one group's fills reassemble
	/// into their original pattern on the ground.
	///
	/// These three are static because the Tile Creator previews a draft kit before anything is
	/// packed into a Tileset, and has to land on the same variant the game will -- it used to carry
	/// its own copy of this arithmetic, which then silently drifted.
	public static int VariantSlot(int seed, int phase, int count, int fillTiles)
	{
		var stride = fillTiles * fillTiles;
		var groups = Math.Max(1, count / stride);
		return Math.Min((seed & int.MaxValue) % groups * stride + Math.Clamp(phase, 0, stride - 1), count - 1);
	}

	static int FloorMod(int value, int size) => (value % size + size) % size;
	static int FloorDiv(int value, int size) => (value >= 0 ? value : value - size + 1) / size;

	public int FillPhase(Point2 worldTile) => FillPhase(worldTile, FillTiles);
	public Point2 FillBlock(Point2 worldTile) => FillBlock(worldTile, FillTiles);
	int VariantIndex(int seed, int phase, int count) => VariantSlot(seed, phase, count, FillTiles);

	public Subtexture GetVariantTile(int x, int y, int seed, int phase = 0)
	{
		if (Variants.Count <= 0)
			return GetTile(x, y);

		var variant = Variants[VariantIndex(seed, phase, Variants.Count)];
		if (x < 0 || y < 0 || x >= Columns || y >= Rows)
			return new();

		var index = x * Rows + y;
		if (index < 0 || index >= variant.Count)
			return new();

		return variant[index];
	}

	public Subtexture GetNormalTile(int x, int y)
	{
		if (x < 0 || y < 0 || x >= Columns || y >= Rows)
			return new();

		var index = x * Rows + y;
		if (index < 0 || index >= NormalTiles.Count)
			return new();

		return NormalTiles[index];
	}

	public Subtexture GetVariantNormalTile(int x, int y, int seed, int phase = 0)
	{
		if (NormalVariants.Count <= 0)
			return GetNormalTile(x, y);

		var variant = NormalVariants[VariantIndex(seed, phase, NormalVariants.Count)];
		if (x < 0 || y < 0 || x >= Columns || y >= Rows)
			return new();

		var index = x * Rows + y;
		if (index < 0 || index >= variant.Count)
			return new();

		return variant[index];
	}

	public OccluderSegment[] GetVariantOccluders(int x, int y, int seed, int phase = 0)
	{
		if (OccluderVariants.Count <= 0)
			return GetOccluders(x, y);

		var variant = OccluderVariants[VariantIndex(seed, phase, OccluderVariants.Count)];
		if (x < 0 || y < 0 || x >= Columns || y >= Rows)
			return [];

		var index = x * Rows + y;
		if (index < 0 || index >= variant.Count)
			return [];

		return variant[index];
	}

	private OccluderSegment[] GetOccluders(int x, int y)
	{
		if (x < 0 || y < 0 || x >= Columns || y >= Rows)
			return [];

		var index = x * Rows + y;
		if (index < 0 || index >= OccluderTiles.Count)
			return [];

		return OccluderTiles[index];
	}
}
