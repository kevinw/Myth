
using Foster.Framework;

namespace TinyLink;

public class Tileset
{
	public string Name { get; }
	public int Columns { get; private set; }
	public int Rows { get; private set; }
	public readonly List<Subtexture> Tiles = new();
	public readonly List<List<Subtexture>> Variants = [];
	public readonly List<Subtexture> NormalTiles = new();
	public readonly List<List<Subtexture>> NormalVariants = [];

	public Tileset(string name, int columns, int rows)
	{
		Name = name;
		Columns = columns;
		Rows = rows;
		for (int i = 0; i < columns * rows; i ++)
			Tiles.Add(new());
	}

	public void Reload(int columns, int rows, List<Subtexture> tiles, List<List<Subtexture>>? variants = null, List<Subtexture>? normalTiles = null, List<List<Subtexture>>? normalVariants = null)
	{
		Columns = columns;
		Rows = rows;

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
	}

	public Subtexture GetRandomTile(ref Rng rng)
		=> Tiles[rng.Int(Tiles.Count)];

	public Subtexture GetTile(int x, int y)
	{
		if (x < 0 || y < 0 || x >= Columns || y >= Rows)
			return new();

		var index = x * Rows + y;
		if (index < 0 || index >= Tiles.Count)
			return new();

		return Tiles[index];
	}

	public Subtexture GetVariantTile(int x, int y, int seed)
	{
		if (Variants.Count <= 0)
			return GetTile(x, y);

		var variant = Variants[(seed & int.MaxValue) % Variants.Count];
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

	public Subtexture GetVariantNormalTile(int x, int y, int seed)
	{
		if (NormalVariants.Count <= 0)
			return GetNormalTile(x, y);

		var variant = NormalVariants[(seed & int.MaxValue) % NormalVariants.Count];
		if (x < 0 || y < 0 || x >= Columns || y >= Rows)
			return new();

		var index = x * Rows + y;
		if (index < 0 || index >= variant.Count)
			return new();

		return variant[index];
	}
}
