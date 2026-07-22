using System.Numerics;
using Foster.Framework;

namespace Myth;

public enum Cardinal { Right, Down, Left, Up }

// A wall/floor-mounted trap that periodically emits a 1-tile-thick band of lightning along
// Direction, out to the first wall. It gathers for a moment (sparking in from both ends, stronger
// at the emitter) as a telegraph, then snaps a binary full-length hurtbox on for FireTime.
// Indestructible: the player can't hit it, only avoid the beam window.
[GameActor(Sprite = "Editor/particles", Tags = ActorTag.Hazard,
	BoundsX = -6, BoundsY = -6, BoundsW = 12, BoundsH = 12, Anchor = SpriteAnchor.Center)]
public class LightningTrap : Actor
{
	public Cardinal Direction = Cardinal.Right;
	public float WaitDelay = 1.5f;   // idle time between fires
	public float GatherTime = 0.4f;  // telegraph windup; beam gathers but isn't lethal yet
	public float FireTime = 0.7f;    // duration the lethal hurtbox is on

	static float FireRampTime => 0.07f; // visual ramp to full intensity when the hurtbox turns on
	static float FireTailTime => 0.08f; // quick visual fade as the window closes
	static int HalfWidth => Game.TileSize / 2;
	static int MouthOffset => Game.TileSize / 2; // beam starts at the edge of the trap's own tile
	static int MaxScan => Game.TileSize * 64;    // safety cap if no wall is ever hit
	static float SparkInterval => 0.05f;         // cadence of the trickle of impact sparks
	static float SparkSpread => MathF.PI * 0.55f;

	enum Phase { Wait, Gather, Fire }
	Phase phase = Phase.Wait;
	float phaseTime;
	int beamLength;
	bool beamHitsWall;
	int seed;
	int sparkEffect = -1;
	float sparkTimer;

	public LightningTrap()
	{
		Sprite = null;
		CollidesWithSolids = false;
		Mask = Masks.Hazard;
		Depth = -6;
		ClearHurtbox();
	}

	public override void Added()
	{
		seed = Position.X * 397 ^ Position.Y;
		phase = Phase.Wait;
		phaseTime = 0;
		ClearHurtbox();
		sparkEffect = Game.Particles?.AcquireEffect("lightning_spark") ?? -1;
	}

	public override void Destroyed() => Game.Particles?.ReleaseEffect(ref sparkEffect);

	Vector2 Dir => DirectionVector(Direction);

	static Vector2 DirectionVector(Cardinal direction) => direction switch
	{
		Cardinal.Right => new Vector2(1, 0),
		Cardinal.Left => new Vector2(-1, 0),
		Cardinal.Up => new Vector2(0, -1),
		_ => new Vector2(0, 1),
	};

	bool Horizontal => Direction is Cardinal.Left or Cardinal.Right;

	public override void Update()
	{
		base.Update();
		phaseTime += Time.Delta;
		switch (phase)
		{
			case Phase.Wait:
				if (phaseTime >= WaitDelay)
					EnterGather();
				break;

			case Phase.Gather:
				if (phaseTime >= GatherTime)
				{
					phase = Phase.Fire;
					phaseTime = 0;
					if (beamLength >= 1)
						Hitbox = new(BandRect(beamLength)); // binary: full-length for the whole window
				}
				break;

			case Phase.Fire:
				UpdateSparks();
				if (phaseTime >= FireTime)
				{
					phase = Phase.Wait;
					phaseTime = 0;
					ClearHurtbox();
				}
				break;
		}
	}

	void EnterGather()
	{
		phase = Phase.Gather;
		phaseTime = 0;
		sparkTimer = 0;
		beamLength = ScanToWall(out beamHitsWall);
		AudioEventDebounced("bubble", 0.1f);
	}

	// March out 1px band slices until one hits room collision, so the beam stops at the first wall.
	int ScanToWall(out bool hitWall)
	{
		for (var d = 1; d <= MaxScan; d++)
			if (Game.OverlapsRoomCollision(SliceRect(d) + Position))
			{
				hitWall = true;
				return d - 1;
			}
		hitWall = false;
		return MaxScan;
	}

	// A short-lived trickle of lightning sparks spraying back off the struck wall, falling with gravity.
	void UpdateSparks()
	{
		if (sparkEffect < 0 || !beamHitsWall || beamLength < 1 || Game.Particles is not { } particles)
			return;

		sparkTimer -= Time.Delta;
		if (sparkTimer > 0)
			return;

		sparkTimer += SparkInterval;
		var impact = (Vector2)Position + Dir * (MouthOffset + beamLength);
		var away = MathF.Atan2(-Dir.Y, -Dir.X);
		particles.Emit(sparkEffect, impact, 2, away, 1f, SparkSpread);
	}

	// Local-space band rect (relative to Position): 1 tile thick, `length` px along Dir.
	RectInt BandRect(int length)
	{
		if (Horizontal)
		{
			var x = Dir.X > 0 ? MouthOffset : -(MouthOffset + length);
			return new RectInt(x, -HalfWidth, length, HalfWidth * 2);
		}
		var y = Dir.Y > 0 ? MouthOffset : -(MouthOffset + length);
		return new RectInt(-HalfWidth, y, HalfWidth * 2, length);
	}

	// 1px leading-edge slice at reach `d`, in local space.
	RectInt SliceRect(int d)
	{
		if (Horizontal)
		{
			var x = Dir.X > 0 ? MouthOffset + d - 1 : -(MouthOffset + d);
			return new RectInt(x, -HalfWidth, 1, HalfWidth * 2);
		}
		var y = Dir.Y > 0 ? MouthOffset + d - 1 : -(MouthOffset + d);
		return new RectInt(-HalfWidth, y, HalfWidth * 2, 1);
	}

	void ClearHurtbox() => Hitbox = new(new RectInt(0, 0, 0, 0));

	// 0 idle, ramps up through the gather, full while firing.
	float Glow => phase switch
	{
		Phase.Gather => Calc.Lerp(0.2f, 0.8f, Math.Clamp(phaseTime / MathF.Max(0.0001f, GatherTime), 0, 1)),
		Phase.Fire => 1f,
		_ => 0f,
	};

	public override void Render(Batcher batcher)
	{
		DrawHousing(batcher);
		DrawBeam(batcher);
	}

	public override void RenderEmissive(Batcher batcher)
	{
		DrawBeam(batcher);
		DrawElectrodes(batcher, emissive: true);
	}

	void DrawBeam(Batcher batcher)
	{
		if (phase == Phase.Wait || beamLength < 1)
			return;

		GetBeamState(out var intensity, out var reachFromStart, out var reachFromEnd, out var startGlow, out var endGlow);
		if (intensity <= 0.001f)
			return;

		LightningEffect.RenderBeam(batcher, Dir * MouthOffset, Dir * (MouthOffset + beamLength),
			HalfWidth, Time.SecondsF, seed, intensity, reachFromStart, reachFromEnd, startGlow, endGlow);
	}

	void GetBeamState(out float intensity, out float reachFromStart, out float reachFromEnd,
		out float startGlow, out float endGlow)
	{
		if (phase == Phase.Gather)
		{
			var g = Math.Clamp(phaseTime / MathF.Max(0.0001f, GatherTime), 0, 1);
			var e = Ease.Quad.Out(g);
			reachFromStart = e * 0.72f;               // emitter end reaches farther...
			reachFromEnd = e * 0.5f;                  // ...wall end weaker; together they connect by fire
			startGlow = Calc.Lerp(0.25f, 0.7f, g);    // emitter brighter
			endGlow = Calc.Lerp(0.12f, 0.4f, g);
			var sparkle = 0.7f + 0.3f * MathF.Abs(MathF.Sin(Time.SecondsF * 47f));
			intensity = Calc.Lerp(0.2f, 0.7f, g) * sparkle;
		}
		else // Fire: full connection, intensity ramps up fast then fades as the window closes
		{
			reachFromStart = reachFromEnd = 1f;
			startGlow = endGlow = 1f;
			var ramp = Ease.Cube.Out(Math.Clamp(phaseTime / FireRampTime, 0, 1));
			var tail = Math.Clamp((FireTime - phaseTime) / FireTailTime, 0, 1);
			intensity = ramp * tail;
		}
	}

	// The engine's lights are radial, so a rectangular beam glow is approximated as a strip of
	// overlapping point lights sampled along the centerline — reads as a 1-tile-tall area light
	// instead of one hot spot, and each sample shades normals from its own position.
	public override void ForEachLight(Action<Vector2, float, float, float, Color> action)
	{
		if (phase == Phase.Wait || beamLength < 1)
			return;

		GetBeamState(out var intensity, out _, out _, out _, out _);
		if (intensity <= 0.001f)
			return;

		var t = Time.SecondsF;
		var color = new Color(0x8fd8ff);
		var samples = Math.Clamp(beamLength / (Game.TileSize * 3 / 2) + 1, 2, 6);
		var radius = beamLength / (float)(samples - 1) + Game.TileSize;
		for (var i = 0; i < samples; i++)
		{
			var along = MouthOffset + beamLength * (i / (float)(samples - 1));
			var position = (Vector2)Position + Dir * along;
			var flicker = 0.45f + 0.55f * MathF.Abs(MathF.Sin(t * 37f + seed + i * 1.7f) * MathF.Cos(t * 23.3f + i));
			action(position, radius, intensity * 0.32f * flicker, 9f, color);
		}
	}

	void DrawHousing(Batcher batcher)
	{
		var center = -Dir * 2;
		batcher.Circle(center, 6, 16, new Color(0x171319));
		batcher.Circle(center, 5, 16, new Color(0x2a2140));
		batcher.Circle(center, 2.5f, 12, new Color(0x3d2f5c));
		DrawElectrodes(batcher, emissive: false);
	}

	void DrawElectrodes(Batcher batcher, bool emissive)
	{
		var glow = Glow;
		if (emissive && glow <= 0.01f)
			return;

		var perp = new Vector2(-Dir.Y, Dir.X);
		var color = emissive
			? new Color(0x79cfff) * glow
			: Color.Lerp(new Color(0x3a3550), new Color(0x79cfff), glow);
		for (var s = -1; s <= 1; s += 2)
			batcher.Circle(Dir * (MouthOffset - 1) + perp * (s * 2.5f), emissive ? 1.6f : 1.4f, 8, color);
	}

#if EDITOR
	sealed class Editor : Manager.ActorEditor<LightningTrap>
	{
		internal override void Gizmos(Manager.EditorActor actor, ref Manager.EditorGizmos gizmos)
		{
			var dir = DirectionVector(actor.GetEnum(nameof(Direction), Cardinal.Right));
			var center = actor.Bounds.CenterF;
			gizmos.Arrow(center + dir * 5, center + dir * 13, gizmos.LineWeight * 2, new Color(0xff3040));
		}
	}
#endif
}
