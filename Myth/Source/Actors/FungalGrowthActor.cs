using System.Numerics;
using Foster.Framework;

namespace Myth;

public class FungalGrowthActor : Actor, IHitByHitbox
{
	public bool HitByHitbox(RectInt rectInt)
	{
		Rect rect = new(rectInt.X, rectInt.Y, rectInt.Width, rectInt.Height);
		if (!GetVisualBounds().Overlaps(rect))
			return false;

		Cut(rectInt);
		Hit(rectInt.Center);
		BroadcastSympathy(rectInt.Center);
		return true;
	}
	public override bool CastsSpriteShadow => false;
	public override bool CastsReflection => false;

	internal ulong RandomSeed;

	/// Pixel size of the region. Set by the caller before Added() runs.
	public int RegionWidth = 8;
	public int RegionHeight = 8;

	/// Per-pixel growth mask (RegionWidth*RegionHeight, row-major). >= 0.5 = the
	/// fungus may live here, otherwise it never does. Set before Added() runs.
	public float[]? GrowthMask;

	/// Simulation ticks per second (spore launches + maturation).
	public float StepsPerSecond = 20.0f;

	/// Spore walkers launched each simulation tick.
	public int WalkersPerTick = 10;

	/// Max random-walk steps before a spore that never touched fungus is discarded.
	public int MaxWalkSteps = 140;

	/// Fraction of the growable area at which ambient spread stops (keeps it tendrilly).
	public float MaxCoverage = 0.32f;

	/// How strongly the fungus glows in the emissive (bloom) layer.
	public float EmissiveStrength = 0.85f;

	/// Age (in ticks) at which a cell is considered fully mature (darkest).
	public int AgeCap = 48;

	/// Simulation ticks run at load so the room opens with established fungus.
	internal static int WarmupSteps => 1200;

	/// Chance per frame, while the player overlaps the region, of dropping spores.
	public float SeedChance = 0.25f;
	public int SeedCount = 2;
	public float SeedJitterMin = 1.0f;
	public float SeedJitterMax = 5.0f;

	/// How fast the "disturbed" brightening from a hit fades (per second).
	public float GlowDecay = 2.2f;

	/// Peak strength of the sympathetic shimmer other islands feel when this one
	/// is struck (at zero distance; falls off linearly to SympatheticRadius).
	public float SympatheticStrength = 0.26f;

	/// World distance beyond which a struck island produces no sympathetic glow,
	/// keeping the response local-dominant rather than a flat screen-wide flash.
	static float SympatheticRadius => 40.0f;
	static float SympatheticWaveSpeed => 48.0f;
	static float SympatheticWaveWidth => 10.0f;

	// Fungal colour ramp, fresh tip -> mature core. Cells dither between
	// adjacent entries by age, giving a classic pixel-art shaded look.
	static readonly Color[] Palette =
	[
		new Color(0.85f, 1.00f, 0.80f, 1.0f), // fresh luminous tip
		new Color(0.55f, 0.95f, 0.45f, 1.0f), // young green
		new Color(0.40f, 0.80f, 0.40f, 1.0f), // moss
		new Color(0.30f, 0.62f, 0.32f, 1.0f), // mature
		new Color(0.22f, 0.46f, 0.26f, 1.0f), // core (still clearly green)
	];

	// 4x4 ordered-dithering threshold matrix, normalised to [0,1).
	static readonly float[] Bayer4 =
	[
		 0f / 16f,  8f / 16f,  2f / 16f, 10f / 16f,
		12f / 16f,  4f / 16f, 14f / 16f,  6f / 16f,
		 3f / 16f, 11f / 16f,  1f / 16f,  9f / 16f,
		15f / 16f,  7f / 16f, 13f / 16f,  5f / 16f,
	];

	// 4-neighbourhood for sticking (thin dendrites); 8 for walker diffusion.
	static readonly int[] N4X = [1, -1, 0, 0];
	static readonly int[] N4Y = [0, 0, 1, -1];
	static readonly int[] N8X = [-1, 0, 1, -1, 1, -1, 0, 1];
	static readonly int[] N8Y = [-1, -1, -1, 0, 0, 1, 1, 1];

	Rng rng;

	GraphicsDevice graphicsDevice = null!;
	Texture? texture;
	byte[] age = [];          // 0 = empty, else 1..AgeCap
	float[] glow = [];        // 0..1 transient brightening from hits
	Color[] pixels = [];
	bool[] growable = [];      // mask cached as bool
	readonly List<int> stuckCells = []; // indices of live cells, for spawning walkers nearby

	int stuck;                 // live fungus cell count

	// Growth budget is tracked per authored tile (TileSize x TileSize) rather
	// than once per island, so a dense blob can't starve a thin painted run:
	// every painted tile fills to ~MaxCoverage of its own growable cells.
	int tileSpan;              // == Game.TileSize
	int tilesX;                // authored-tile columns across the region
	int[] tileGrowable = [];   // growable cells per tile
	int[] tileCap = [];        // stuck target per tile (MaxCoverage of its cells)
	int[] tileStuck = [];      // current stuck cells per tile
	int underCapTiles;         // tiles still below their cap (0 => growth halts)

	float stepAccumulator;
	bool dirty;
	bool glowActive;
	Vector2 sympatheticWaveOrigin;
	float sympatheticWaveRadius;
	bool sympatheticWaveActive;

	public override void Added()
	{
		ActorId ??= ActorSpecs.GenerateActorId();
		rng = new Rng(RandomSeed);
		base.Added();

		graphicsDevice = Game.Manager.GraphicsDevice;

		var count = RegionWidth * RegionHeight;
		age = new byte[count];
		glow = new float[count];
		pixels = new Color[count];
		growable = new bool[count];

		tileSpan = Game.TileSize;
		tilesX = (RegionWidth + tileSpan - 1) / tileSpan;
		var tilesY = (RegionHeight + tileSpan - 1) / tileSpan;
		var tileCount = tilesX * tilesY;
		tileGrowable = new int[tileCount];
		tileCap = new int[tileCount];
		tileStuck = new int[tileCount];

		for (var i = 0; i < count; i++)
		{
			growable[i] = GrowthMask == null || GrowthMask[i] >= 0.5f;
			if (growable[i])
				tileGrowable[TileOf(i)]++;
		}

		underCapTiles = 0;
		for (var t = 0; t < tileCount; t++)
		{
			// At least 1 so even a sliver tile keeps its seed; MaxCoverage of its
			// cells otherwise, which preserves the tendrilly within-tile density.
			tileCap[t] = tileGrowable[t] > 0 ? Math.Max(1, (int)(tileGrowable[t] * MaxCoverage)) : 0;
			if (tileCap[t] > 0)
				underCapTiles++;
		}

		SeedNuclei();

		for (var i = 0; i < WarmupSteps; i++)
			Step();

		texture = new Texture(graphicsDevice, RegionWidth, RegionHeight, TextureFormat.Color, TextureFlags.None, "FungalGrowth");
		dirty = true;
		Rebuild();
	}

	// Seed one mature nucleus in every authored tile that has growable cells, so
	// a painted run is always occupied and each tile has its own origin for the
	// DLA to branch from (rather than a handful of random island-wide seeds).
	void SeedNuclei()
	{
		for (var ty = 0; ty < RegionHeight; ty += tileSpan)
			for (var tx = 0; tx < RegionWidth; tx += tileSpan)
				for (var attempt = 0; attempt < 24; attempt++)
				{
					var x = tx + rng.Int(Math.Min(tileSpan, RegionWidth - tx));
					var y = ty + rng.Int(Math.Min(tileSpan, RegionHeight - ty));
					var i = y * RegionWidth + x;
					if (!growable[i] || age[i] != 0)
						continue;
					Stick(x, y, (byte)rng.Int(AgeCap / 2, AgeCap));
					break;
				}
	}

	int TileOf(int cellIndex)
	{
		var x = cellIndex % RegionWidth;
		var y = cellIndex / RegionWidth;
		return (y / tileSpan) * tilesX + (x / tileSpan);
	}

	void Stick(int x, int y, byte startAge)
	{
		var i = y * RegionWidth + x;
		age[i] = startAge;
		stuckCells.Add(i);
		stuck++;

		var t = TileOf(i);
		tileStuck[t]++;
		if (tileStuck[t] == tileCap[t])   // just reached this tile's cap
			underCapTiles--;

		dirty = true;
	}

	public override void Update()
	{
		base.Update();

		var stepInterval = 1.0f / StepsPerSecond;
		stepAccumulator += Time.Delta;
		while (stepAccumulator >= stepInterval)
		{
			stepAccumulator -= stepInterval;
			Step();
		}

		if (glowActive)
			DecayGlow();
		if (sympatheticWaveActive)
			UpdateSympatheticWave();
	}

	void Step()
	{
		MatureCells();
		if (stuck == 0 || underCapTiles == 0)
			return;
		for (var w = 0; w < WalkersPerTick && underCapTiles > 0; w++)
			LaunchSpore();
	}

	// Age every live cell one tick toward full maturity.
	void MatureCells()
	{
		for (var i = 0; i < age.Length; i++)
			if (age[i] != 0 && age[i] < AgeCap)
			{
				age[i]++;
				dirty = true;
			}
	}

	// One DLA walker: spawn on a small ring around an existing fungus cell (so
	// it reliably finds the aggregate even when coverage is tiny), random-walk,
	// and stick on first 4-neighbour contact. Sticking at the frontier of an
	// irregular aggregate is what produces branching tendrils.
	void LaunchSpore()
	{
		if (stuckCells.Count == 0)
			return;

		var anchor = PickAnchorUnderCap();
		var ax = anchor % RegionWidth;
		var ay = anchor / RegionWidth;

		var x = 0;
		var y = 0;
		var found = false;
		for (var attempt = 0; attempt < 10; attempt++)
		{
			var dist = rng.Float(3.0f, 8.0f);
			var angle = rng.Float(0.0f, MathF.Tau);
			x = ax + (int)MathF.Round(MathF.Cos(angle) * dist);
			y = ay + (int)MathF.Round(MathF.Sin(angle) * dist);
			if (x < 0 || y < 0 || x >= RegionWidth || y >= RegionHeight)
				continue;
			var i = y * RegionWidth + x;
			if (growable[i] && age[i] == 0 && !HasFungus4(x, y))
			{
				found = true;
				break;
			}
		}
		if (!found)
			return;

		for (var step = 0; step < MaxWalkSteps; step++)
		{
			if (HasFungus4(x, y))
			{
				// Don't pile onto a tile that has already reached its density; the
				// discarded walker leaves the budget for under-filled tiles.
				var t = TileOf(y * RegionWidth + x);
				if (tileStuck[t] < tileCap[t])
					Stick(x, y, 1);
				return;
			}

			var dir = rng.Int(8);
			var nx = x + N8X[dir];
			var ny = y + N8Y[dir];
			if (nx < 0 || ny < 0 || nx >= RegionWidth || ny >= RegionHeight)
				continue;
			if (!growable[ny * RegionWidth + nx])
				continue;
			x = nx;
			y = ny;
		}
	}

	// Prefer an anchor whose tile still has capacity, so walkers flow toward
	// under-filled painted tiles (thin runs) instead of the densest blob.
	int PickAnchorUnderCap()
	{
		for (var attempt = 0; attempt < 8; attempt++)
		{
			var cand = stuckCells[rng.Int(stuckCells.Count)];
			var t = TileOf(cand);
			if (tileStuck[t] < tileCap[t])
				return cand;
		}
		return stuckCells[rng.Int(stuckCells.Count)];
	}

	bool HasFungus4(int x, int y)
	{
		for (var d = 0; d < 4; d++)
		{
			var nx = x + N4X[d];
			var ny = y + N4Y[d];
			if (nx < 0 || ny < 0 || nx >= RegionWidth || ny >= RegionHeight)
				continue;
			if (age[ny * RegionWidth + nx] != 0)
				return true;
		}
		return false;
	}

	///
	/// Briefly brightens the fungus around a world-space point (e.g. the player
	/// brushing past or an impact), fading over ~half a second. A subtle
	/// shimmer, not a displacement - no watery wobble.
	///
	public void Hit(Vector2 worldPos, float strength = 0.6f, float radius = 5.0f)
	{
		var cx = worldPos.X - Position.X;
		var cy = worldPos.Y - Position.Y;
		var r = (int)MathF.Ceiling(radius);
		var r2 = radius * radius;

		for (var dy = -r; dy <= r; dy++)
			for (var dx = -r; dx <= r; dx++)
			{
				var x = (int)MathF.Round(cx) + dx;
				var y = (int)MathF.Round(cy) + dy;
				if (x < 0 || y < 0 || x >= RegionWidth || y >= RegionHeight)
					continue;
				var dist2 = dx * dx + dy * dy;
				if (dist2 > r2)
					continue;
				var i = y * RegionWidth + x;
				if (age[i] == 0)
					continue;
				var falloff = 1.0f - MathF.Sqrt(dist2) / radius;
				glow[i] = MathF.Min(1.0f, glow[i] + strength * falloff);
			}

		glowActive = true;
		dirty = true;
	}

	// A hit here sends a radial glow through nearby fungus, including untouched
	// reaches of the struck island. Each island receives the original world-space
	// impact so disconnected growth still follows one continuous wavefront.
	void BroadcastSympathy(Vector2 hitWorld)
	{
		foreach (var actor in Game.Actors)
		{
			if (actor is not FungalGrowthActor island)
				continue;

			var dist = DistanceToBounds(island.GetVisualBounds(), hitWorld);
			if (dist >= SympatheticRadius)
				continue;
			island.BeginSympatheticWave(hitWorld);
		}
	}

	static float DistanceToBounds(Rect bounds, Vector2 p)
	{
		var dx = MathF.Max(MathF.Max(bounds.X - p.X, p.X - (bounds.X + bounds.Width)), 0.0f);
		var dy = MathF.Max(MathF.Max(bounds.Y - p.Y, p.Y - (bounds.Y + bounds.Height)), 0.0f);
		return MathF.Sqrt(dx * dx + dy * dy);
	}

	void BeginSympatheticWave(Vector2 origin)
	{
		sympatheticWaveOrigin = origin;
		sympatheticWaveRadius = 0;
		sympatheticWaveActive = true;
	}

	void UpdateSympatheticWave()
	{
		sympatheticWaveRadius += SympatheticWaveSpeed * Time.Delta;
		var halfWidth = SympatheticWaveWidth * 0.5f;
		var any = false;
		for (var y = 0; y < RegionHeight; y++)
			for (var x = 0; x < RegionWidth; x++)
			{
				var i = y * RegionWidth + x;
				if (age[i] == 0)
					continue;

				var cell = (Vector2)Position + new Vector2(x + 0.5f, y + 0.5f);
				var distance = Vector2.Distance(cell, sympatheticWaveOrigin);
				var wave = 1.0f - MathF.Abs(distance - sympatheticWaveRadius) / halfWidth;
				if (wave <= 0.0f || distance >= SympatheticRadius)
					continue;

				var falloff = 1.0f - distance / SympatheticRadius;
				glow[i] = MathF.Max(glow[i], SympatheticStrength * falloff * wave);
				any = true;
			}

		if (any)
		{
			glowActive = true;
			dirty = true;
		}

		if (sympatheticWaveRadius >= SympatheticRadius + halfWidth)
			sympatheticWaveActive = false;
	}

	void DecayGlow()
	{
		var step = GlowDecay * Time.Delta;
		var any = false;
		for (var i = 0; i < glow.Length; i++)
		{
			if (glow[i] <= 0.0f)
				continue;
			glow[i] = MathF.Max(0.0f, glow[i] - step);
			any = true;
		}
		glowActive = any;
		dirty = true;
	}

	/// Kills all fungus overlapping the given world-space rectangle; it regrows over time.
	public void Cut(RectInt worldRect)
	{
		var x0 = Math.Max(0, worldRect.X - Position.X);
		var y0 = Math.Max(0, worldRect.Y - Position.Y);
		var x1 = Math.Min(RegionWidth, worldRect.X + worldRect.Width - Position.X);
		var y1 = Math.Min(RegionHeight, worldRect.Y + worldRect.Height - Position.Y);
		if (x1 <= x0 || y1 <= y0)
			return;

		for (var y = y0; y < y1; y++)
			for (var x = x0; x < x1; x++)
			{
				var i = y * RegionWidth + x;
				age[i] = 0;
				glow[i] = 0.0f;
			}

		RebuildStuckList();
		dirty = true;
	}

	// Rescan the grid to rebuild the live-cell list + per-tile counts after a cut,
	// reopening any tiles a cut dropped back below their cap so they regrow.
	void RebuildStuckList()
	{
		stuckCells.Clear();
		Array.Clear(tileStuck);
		for (var i = 0; i < age.Length; i++)
			if (age[i] != 0)
			{
				stuckCells.Add(i);
				tileStuck[TileOf(i)]++;
			}
		stuck = stuckCells.Count;

		underCapTiles = 0;
		for (var t = 0; t < tileCap.Length; t++)
			if (tileStuck[t] < tileCap[t])
				underCapTiles++;
	}

	///
	/// Called every frame the player overlaps the region: drops fresh spores
	/// along their path (new growth nuclei) and brightens nearby fungus.
	///
	public void OnPlayerStep(Vector2 footWorldPos)
	{
		Hit(footWorldPos, strength: 0.35f, radius: 4.0f);

		if (!rng.Chance(SeedChance))
			return;

		for (var i = 0; i < SeedCount; i++)
			TryDropSpore(footWorldPos);
	}

	void TryDropSpore(Vector2 footWorldPos)
	{
		var jitter = rng.Float(SeedJitterMin, SeedJitterMax);
		var angle = rng.Float(0.0f, MathF.Tau);
		var x = (int)MathF.Round(footWorldPos.X - Position.X + MathF.Cos(angle) * jitter);
		var y = (int)MathF.Round(footWorldPos.Y - Position.Y + MathF.Sin(angle) * jitter);
		if (x < 0 || y < 0 || x >= RegionWidth || y >= RegionHeight)
			return;
		var idx = y * RegionWidth + x;
		if (!growable[idx] || age[idx] != 0)
			return;
		Stick(x, y, 1);
	}

	// Map the age/glow grid through the dithered colour ramp into `pixels`.
	void Rebuild()
	{
		var last = Palette.Length - 1;
		for (var y = 0; y < RegionHeight; y++)
			for (var x = 0; x < RegionWidth; x++)
			{
				var i = y * RegionWidth + x;
				var a = age[i];
				if (a == 0)
				{
					pixels[i] = Color.Transparent;
					continue;
				}

				// 0 = fresh tip, 1 = mature core; static hash adds mottling.
				var u = (a - 1) / (float)(AgeCap - 1);
				u += (Hash01(x, y) - 0.5f) * 0.12f;
				u -= glow[i] * 0.7f; // disturbed fungus brightens (toward tip)
				u = Math.Clamp(u, 0.0f, 1.0f);

				var scaled = u * last;
				var lo = (int)MathF.Floor(scaled);
				var frac = scaled - lo;
				var threshold = Bayer4[(y & 3) * 4 + (x & 3)];
				var idx = lo + (frac > threshold ? 1 : 0);
				pixels[i] = Palette[Math.Clamp(idx, 0, last)];
			}

		texture!.SetData<Color>(pixels);
		dirty = false;
	}

	static float Hash01(int x, int y)
	{
		var h = (uint)(x * 374761393 + y * 668265263);
		h = (h ^ (h >> 13)) * 1274126177u;
		h ^= h >> 16;
		return (h & 0xFFFF) / 65535.0f;
	}

	public override void Render(Batcher batcher)
	{
		if (texture == null) return;
		if (dirty) Rebuild();
		batcher.Image(texture, Vector2.Zero, Color.White);
	}

	public override Rect GetVisualBounds() => new(Position.X, Position.Y, RegionWidth, RegionHeight);

	public override void Destroyed()
	{
		base.Destroyed();
		texture?.Dispose();
	}
}
