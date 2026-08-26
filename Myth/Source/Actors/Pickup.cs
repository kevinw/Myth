using System;
using System.Numerics;
using Foster.Framework;

namespace Myth;

// The collectible types the player can gather. The inventory is keyed by this enum in SaveState,
// so a new pickup is: add a value here, give it a sprite named below, and
// drop it somewhere. Mycelon is the common currency — a bio-digital spore harvested in the
// overgrown fungal datacenter.
public enum PickupKind
{
	Mycelon,
}

// A collectible that pops out of a broken container, arcs under gravity, settles, then is drawn
// toward the player and collected on contact — incrementing the playthrough-durable inventory.
public class Pickup : Actor
{
	public PickupKind Kind = PickupKind.Mycelon;

	static float Gravity => 300f;         // fall acceleration until it lands
	static float Friction => 100f;        // horizontal glide-down to a rest after the pop
	static float MagnetRadius => 5f;     // starts homing toward the player within this range
	static float CollectRadius => 6f;     // collected once this close to the player
	static float MagnetAccel => 520f;     // homing pull once in range
	static float ArmTime => 0.25f;        // ignore the player briefly so the pop reads before pickup
	static float LifeTime => 30f;         // safety despawn so stray pickups don't pile up

	float armTimer = ArmTime;
	float life = LifeTime;

	// Fan `count` pickups across an upward arc, evenly spaced and centered on straight up —
	// the standard broken-container burst (Chest, breakable Crate).
	public static void Scatter(Game game, Point2 position, PickupKind kind, int count, float spreadArc = 1.4f)
	{
		var up = -MathF.PI / 2f;
		for (var i = 0; i < count; i++)
		{
			var t = count > 1 ? i / (float)(count - 1) : 0.5f;
			var angle = up - spreadArc / 2f + spreadArc * t;
			var speed = game.rng.Float(60f, 100f);
			var velocity = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * speed;
			game.Create<Pickup>(position, p =>
			{
				p.Kind = kind;
				p.Velocity = velocity;
			});
		}
	}

	public Pickup()
	{
		Depth = -20;
		Hitbox = new(new RectInt(-2, -2, 4, 4));
		// Burst out unimpeded: it spawns inside the (still-solid, deferred-destroy) chest and on
		// the floor line, and the mover can't push an actor out of a solid it already overlaps.
		// Collision turns back on once the arm window clears it into open air (see Update).
		CollidesWithSolids = false;
	}

	public override void Added()
	{
		base.Added();
		Sprite = Assets.GetSprite(SpriteNameFor(Kind)) ?? Assets.GetSprite("airbubble");
		Play("idle");
	}

	static string SpriteNameFor(PickupKind kind) => kind switch
	{
		_ => "mycelon",
	};

	public override void Update()
	{
		armTimer = Calc.Approach(armTimer, 0, Time.Delta);
		life = Calc.Approach(life, 0, Time.Delta);
		CollidesWithSolids = armTimer <= 0; // once the pop has cleared the chest, land on terrain

		var homing = false;
		if (armTimer <= 0 && Game.GetFirst<Player>() is { } player)
		{
			var toPlayer = (Vector2)player.Position - Position;
			var dist = toPlayer.Length();
			if (dist <= CollectRadius)
			{
				Collect();
				return;
			}
			if (dist <= MagnetRadius)
			{
				homing = true;
				var dir = dist > 0.001f ? toPlayer / dist : Vector2.Zero;
				Velocity += dir * MagnetAccel * Time.Delta;
			}
		}

		// While homing, fly freely toward the player; otherwise fall and glide to a rest.
		if (!homing)
		{
			Velocity.Y += Gravity * Time.Delta;
			Velocity.X = Calc.Approach(Velocity.X, 0, Friction * Time.Delta);
		}

		base.Update(); // integrates Velocity, stops against solids via OnCollideX/Y

		if (life <= 0)
			Game.Destroy(this);
	}

	void Collect()
	{
		Game.AddPickup(Kind);
		AudioEvent("pickup"); // authored in FMOD later; no-op until then
		Game.Destroy(this);
	}

	public override void RenderNormals(Batcher batcher) { }
}
