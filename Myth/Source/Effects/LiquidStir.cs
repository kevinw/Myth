using System.Numerics;
using Foster.Framework;

namespace Myth;

// One stir field over the current room, shared by every liquid: water tiles, water volumes and lava.
// Swimmers, bubbles and splashes heat it and it cools back off. LiquidBody.hlsl reads it (lava melts
// its crust and brightens, water aerates and its caustics wash out), and so do water refraction and
// lava light.
public sealed class LiquidStir : IDisposable
{
	public const int Cell = 4; // world px per cell
	static float CoolTime => 1.2f;
	static float SwimmerRadius => 10f;
	static float SwimmerRate => 1.5f; // heat per second at a standstill; moving faster stirs more

	// Starts as one calm cell, so liquids drawn before the first update (room load, hit stun, a
	// transition) already go through the body shader instead of popping in from flat color.
	RectInt bounds;
	Point2 size = Point2.One;
	float[] heat = new float[1];
	byte[] pixels = new byte[1];
	Texture? texture;
	bool dirty, wasWarm;

	public RectInt Bounds => new(bounds.TopLeft, size * Cell);

	public void Update(Game game, RectInt roomBounds, float dt)
	{
		if (roomBounds != bounds)
		{
			bounds = roomBounds;
			size = Point2.Max(roomBounds.Size / Cell, Point2.One);
			heat = new float[size.X * size.Y];
			pixels = new byte[heat.Length];
			texture?.Dispose();
			texture = null;
		}

		foreach (var actor in game.Actors)
			if (actor.InLiquidBody)
				Stir((actor.Hitbox.Rect + actor.Position).Center, SwimmerRadius, SwimmerRate * dt * (0.3f + actor.Velocity.Length() / 100f));

		var keep = MathF.Exp(-dt / CoolTime);
		var warm = false;
		for (var i = 0; i < heat.Length; i++)
		{
			heat[i] = heat[i] * keep < 0.004f ? 0 : heat[i] * keep;
			warm |= heat[i] > 0;
		}
		dirty |= warm || wasWarm; // one more upload after the last cell cools to zero
		wasWarm = warm;
	}

	// Heats the liquid around a world point, falling off to nothing at radius.
	public void Stir(Vector2 world, float radius, float amount)
	{
		var local = (world - bounds.TopLeft) / Cell;
		var cells = radius / Cell;
		var area = RectInt.Between((local - Vector2.One * cells).FloorToPoint2(), (local + Vector2.One * cells).CeilingToPoint2())
			.GetIntersection(new RectInt(Point2.Zero, size));
		for (var y = area.Top; y < area.Bottom; y++)
			for (var x = area.Left; x < area.Right; x++)
			{
				var falloff = 1 - (new Vector2(x + 0.5f, y + 0.5f) - local).Length() / cells;
				if (falloff > 0)
					heat[y * size.X + x] = MathF.Min(1, heat[y * size.X + x] + amount * falloff);
			}
		dirty = true;
	}

	public float At(Vector2 world)
	{
		var cell = ((world - bounds.TopLeft) / Cell).FloorToPoint2();
		return new RectInt(Point2.Zero, size).Contains(cell) ? heat[cell.Y * size.X + cell.X] : 0;
	}

	public Texture Upload(GraphicsDevice gfx)
	{
		if (texture == null)
		{
			texture = new Texture(gfx, size.X, size.Y, TextureFormat.R8, name: "LiquidStir");
			dirty = true; // a new texture's contents are undefined
		}
		if (dirty)
		{
			for (var i = 0; i < heat.Length; i++)
				pixels[i] = (byte)(heat[i] * 255);
			texture.SetData<byte>(pixels);
			dirty = false;
		}
		return texture;
	}

	public void Dispose()
	{
		texture?.Dispose();
		texture = null;
	}
}
