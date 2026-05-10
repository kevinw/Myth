
using Foster.Framework;

namespace TinyLink;

public class Tileset
{
	public string Name { get; }
	public int Columns { get; private set; }
	public int Rows { get; private set; }
	public readonly List<Subtexture> Tiles = new();

	public Tileset(string name, int columns, int rows)
	{
		Name = name;
		Columns = columns;
		Rows = rows;
		for (int i = 0; i < columns * rows; i ++)
			Tiles.Add(new());
	}

	public void Reload(int columns, int rows, List<Subtexture> tiles)
	{
		Columns = columns;
		Rows = rows;

		Tiles.Clear();
		Tiles.AddRange(tiles);
	}

	public Subtexture GetRandomTile(ref Rng rng)
		=> Tiles[rng.Int(Tiles.Count)];
}
