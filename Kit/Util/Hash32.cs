using Foster.Framework;

namespace Kit;

public static class Hash32
{
	public static uint Mix(uint value)
	{
		unchecked
		{
			value ^= value >> 16;
			value *= 0x7feb352du;
			value ^= value >> 15;
			value *= 0x846ca68bu;
			return value ^ value >> 16;
		}
	}

	// FNV-1a: the same string hashes the same in every run and on every platform, unlike string.GetHashCode.
	public static uint Of(string text)
	{
		var hash = 2166136261u;
		foreach (var c in text)
			hash = unchecked((hash ^ c) * 16777619u);
		return hash;
	}

	public static int TileVariantSeed(Point2 worldTile, char tileType)
	{
		unchecked
		{
			var hash = (uint)worldTile.X * 0x8da6b343u;
			hash ^= (uint)worldTile.Y * 0xd8163841u;
			hash ^= tileType * 0xcb1ab31fu;
			return (int)Mix(hash);
		}
	}
}
