using System.Numerics;
using Foster.Framework;

namespace Myth;

public enum Cardinal { Right, Down, Left, Up }

// A wall/floor-mounted trap that periodically emits a 1-tile-thick band of lightning along
// Direction. The beam reflects off 45-degree reflective (metal) slopes, so its path is a polyline
// of axis-aligned segments. It gathers for a moment (light sparking in from both ends of the whole
// path) as a telegraph, then the full path becomes a lethal hurtbox for FireTime.
// Indestructible: the player can't hit it, only avoid the beam window.
[GameActor(Sprite = "Editor/particles", Tags = ActorTag.Hazard,
	BoundsX = -6, BoundsY = -6, BoundsW = 12, BoundsH = 12, Anchor = SpriteAnchor.Center)]
public class LightningTrap : Actor
{
	public Cardinal Direction = Cardinal.Right;
	public float WaitDelay = 1.5f;   // idle time between fires
	public float GatherTime = 0.4f;  // telegraph windup; beam gathers but isn't lethal yet
	public float FireTime = 0.7f;    // duration the lethal hurtbox is on
	public float StartDelay = 0f;    // one-time extra wait before the first fire, to stagger traps

	static float FireRampTime => 0.07f; // visual ramp to full intensity when the hurtbox turns on
	static float FireTailTime => 0.08f; // quick visual fade as the window closes
	static int HalfWidth => Game.TileSize / 2;
	static int MouthOffset => Game.TileSize / 2; // beam starts at the edge of the trap's own tile
	static int MaxScan => Game.TileSize * 64;    // per-segment safety cap
	static int MaxBounces => 8;                  // reflections before the beam gives up
	static float SparkInterval => 0.05f;         // cadence of the trickle of impact sparks
	static float SparkSpread => MathF.PI * 0.55f;

	// A straight run of the beam, in local space (relative to Position). StartDistance is the
	// cumulative path length up to From, so the gather can light the whole polyline progressively.
	readonly record struct Segment(Vector2 From, Vector2 To, float StartDistance)
	{
		public float Length => (To - From).Length();
	}

	enum Phase { Wait, Gather, Fire }
	Phase phase = Phase.Wait;
	float phaseTime;

	readonly List<Segment> segments = [];
	readonly List<(Vector2 Point, float Angle)> impacts = []; // world-space spark sources (bounces + terminal wall)
	float pathLength;

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
		phaseTime = -MathF.Max(0, StartDelay); // stagger the first fire
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
				}
				break;

			case Phase.Fire:
				ApplyDamage();
				UpdateSparks();
				if (phaseTime >= FireTime)
				{
					phase = Phase.Wait;
					phaseTime = 0;
				}
				break;
		}
	}

	void EnterGather()
	{
		phase = Phase.Gather;
		phaseTime = 0;
		sparkTimer = 0;
		TraceBeam();
		AudioEventDebounced("bubble", 0.1f);
	}

	// Walks the beam from the mouth, reflecting off reflective (metal) 45-degree slopes, building a
	// polyline of axis-aligned segments (a 45-degree reflection turns an axis beam exactly 90).
	void TraceBeam()
	{
		segments.Clear();
		impacts.Clear();
		pathLength = 0;

		var dir = Dir;
		// Center the beam in its tile lane so it travels cleanly down a column/row rather than along
		// a tile boundary — a boundary-aligned ray straddles the neighbouring solid tile and reads it
		// as a flat wall instead of seeing the slope it's aimed at.
		var visualStart = SnapToLane((Vector2)Position + Dir * MouthOffset, dir);
		// The trap is mounted inside a wall, so search from the first pixel clear of solid; otherwise
		// the ray hits the very tile it's embedded in and the beam collapses to zero length.
		var rayStart = AdvancePastSolid(visualStart, dir);

		for (var bounce = 0; ; bounce++)
		{
			var reflected = false;
			Vector2 worldEnd;
			if (Game.TryFindSurface(this, ProbeRect(rayStart), dir, MaxScan, out var hit))
			{
				worldEnd = ProjectOntoLane(hit.Point, visualStart, dir); // keep the segment axis-aligned
				impacts.Add((worldEnd, MathF.Atan2(hit.Normal.Y, hit.Normal.X)));
				reflected = bounce < MaxBounces && hit.Slope is { } slope && IsReflective(slope);
			}
			else
			{
				worldEnd = rayStart + dir * MaxScan; // open space: run to the cap, no impact sparks
			}

			var length = (worldEnd - visualStart).Length();
			segments.Add(new Segment(visualStart - Position, worldEnd - Position, pathLength));
			pathLength += length;

			if (!reflected)
				break;

			dir = Reflect(dir, hit.Normal);
			var pivot = SnapToLane(worldEnd, dir); // re-center on the new lane at the reflection tile
			visualStart = pivot;
			rayStart = AdvancePastSolid(pivot + dir * 2, dir); // clear the slope, then any solid, first
		}
	}

	// Snaps a point's cross-lane coordinate (perpendicular to travel) to its tile center.
	static Vector2 SnapToLane(Vector2 p, Vector2 dir)
		=> dir.Y == 0 ? new Vector2(p.X, TileCenter(p.Y)) : new Vector2(TileCenter(p.X), p.Y);

	// Keeps the hit's along-axis coordinate but pins the cross-lane one to the segment's lane.
	static Vector2 ProjectOntoLane(Vector2 hit, Vector2 laneRef, Vector2 dir)
		=> dir.Y == 0 ? new Vector2(hit.X, laneRef.Y) : new Vector2(laneRef.X, hit.Y);

	static float TileCenter(float v) => MathF.Floor(v / Game.TileSize) * Game.TileSize + Game.TileSize / 2f;

	// Steps forward along dir until the probe clears solid room collision (bounded to about a tile),
	// so a wall-mounted trap searches from open space rather than the tile it sits in.
	Vector2 AdvancePastSolid(Vector2 p, Vector2 dir)
	{
		for (var i = 0; i < Game.TileSize + HalfWidth; i++)
		{
			if (!Game.OverlapsRoomCollision(ProbeRect(p)))
				break;
			p += dir;
		}
		return p;
	}

	// Reflects an axis-aligned direction across a surface normal, snapping to a clean unit axis
	// (45-degree slopes yield exact 90-degree turns; rounding absorbs float drift).
	static Vector2 Reflect(Vector2 v, Vector2 n)
	{
		var r = v - 2 * Vector2.Dot(v, n) * n;
		return new Vector2(MathF.Round(r.X), MathF.Round(r.Y));
	}

	bool IsReflective(Room.SlopeTile slope)
	{
		if (Game.CurrentRoom is not { } room)
			return false;
		var t = slope.Tile;
		if (t.X < 0 || t.Y < 0 || t.X >= room.Size.X || t.Y >= room.Size.Y)
			return false;
		return Factory.Find(room.Tiles[t.X, t.Y]) is { Reflection: not Factory.ReflectionMode.None };
	}

	static RectInt ProbeRect(Vector2 worldCenter)
	{
		var c = worldCenter.RoundToPoint2();
		return new RectInt(c.X - 1, c.Y - 1, 2, 2);
	}

	// World-space 1-tile-thick band rect for a segment's lethal hurtbox.
	RectInt SegmentWorldRect(Segment seg)
	{
		var a = (Vector2)Position + seg.From;
		var b = (Vector2)Position + seg.To;
		int ax = (int)MathF.Round(a.X), ay = (int)MathF.Round(a.Y);
		int bx = (int)MathF.Round(b.X), by = (int)MathF.Round(b.Y);
		if (Math.Abs(ay - by) <= Math.Abs(ax - bx)) // horizontal run
		{
			var x0 = Math.Min(ax, bx);
			return new RectInt(x0, ay - HalfWidth, Math.Max(ax, bx) - x0, HalfWidth * 2);
		}
		var y0 = Math.Min(ay, by); // vertical run
		return new RectInt(ax - HalfWidth, y0, HalfWidth * 2, Math.Max(ay, by) - y0);
	}

	void ApplyDamage()
	{
		foreach (var seg in segments)
		{
			var rect = SegmentWorldRect(seg);
			if (rect.Width <= 0 || rect.Height <= 0)
				continue;
			if (Game.OverlapsFirst(rect, Masks.Player) is { } player)
			{
				Hit(player);
				return;
			}
		}
	}

	// A short-lived trickle of lightning sparks off each impact point (bounces + terminal wall),
	// spraying along the surface normal and falling with gravity.
	void UpdateSparks()
	{
		if (sparkEffect < 0 || impacts.Count == 0 || Game.Particles is not { } particles)
			return;

		sparkTimer -= Time.Delta;
		if (sparkTimer > 0)
			return;

		sparkTimer += SparkInterval;
		foreach (var impact in impacts)
			particles.Emit(sparkEffect, impact.Point, 2, impact.Angle, 1f, SparkSpread);
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
		if (phase == Phase.Wait || segments.Count == 0 || pathLength < 1)
			return;

		GetBeamState(out var intensity, out var coverStart, out var coverEnd, out var startGlow, out var endGlow);
		if (intensity <= 0.001f)
			return;

		// Coverage distances measured inward from each end of the whole polyline, split per segment.
		var coverStartDist = coverStart * pathLength;
		var coverEndDist = coverEnd * pathLength;
		foreach (var seg in segments)
		{
			var length = seg.Length;
			if (length < 0.5f)
				continue;
			var d0 = seg.StartDistance;
			var d1 = d0 + length;
			var reachFromStart = Math.Clamp((coverStartDist - d0) / length, 0, 1);
			var reachFromEnd = Math.Clamp((coverEndDist - (pathLength - d1)) / length, 0, 1);
			LightningEffect.RenderBeam(batcher, seg.From, seg.To, HalfWidth, Time.SecondsF,
				seed + (int)d0, intensity, reachFromStart, reachFromEnd, startGlow, endGlow);
		}
	}

	void GetBeamState(out float intensity, out float coverStart, out float coverEnd,
		out float startGlow, out float endGlow)
	{
		if (phase == Phase.Gather)
		{
			var g = Math.Clamp(phaseTime / MathF.Max(0.0001f, GatherTime), 0, 1);
			var e = Ease.Quad.Out(g);
			coverStart = e * 0.72f;                   // emitter end reaches farther...
			coverEnd = e * 0.5f;                      // ...far end weaker; together they connect by fire
			startGlow = Calc.Lerp(0.25f, 0.7f, g);    // emitter brighter
			endGlow = Calc.Lerp(0.12f, 0.4f, g);
			var sparkle = 0.7f + 0.3f * MathF.Abs(MathF.Sin(Time.SecondsF * 47f));
			intensity = Calc.Lerp(0.2f, 0.7f, g) * sparkle;
		}
		else // Fire: full connection, intensity ramps up fast then fades as the window closes
		{
			coverStart = coverEnd = 1f;
			startGlow = endGlow = 1f;
			var ramp = Ease.Cube.Out(Math.Clamp(phaseTime / FireRampTime, 0, 1));
			var tail = Math.Clamp((FireTime - phaseTime) / FireTailTime, 0, 1);
			intensity = ramp * tail;
		}
	}

	// The engine's lights are radial, so the beam glow is approximated as a strip of overlapping
	// point lights sampled along the whole (possibly bounced) path — a fixed budget so cost stays
	// flat regardless of how many times it reflects.
	public override void ForEachLight(Action<Vector2, float, float, float, Color> action)
	{
		if (phase == Phase.Wait || segments.Count == 0 || pathLength < 1)
			return;

		GetBeamState(out var intensity, out _, out _, out _, out _);
		if (intensity <= 0.001f)
			return;

		var t = Time.SecondsF;
		var color = new Color(0x8fd8ff);
		var samples = Math.Clamp((int)(pathLength / (Game.TileSize * 3 / 2)) + 1, 2, 8);
		var radius = pathLength / (samples - 1) + Game.TileSize;
		for (var i = 0; i < samples; i++)
		{
			var position = PointAtPathDistance(pathLength * (i / (float)(samples - 1)));
			var flicker = 0.45f + 0.55f * MathF.Abs(MathF.Sin(t * 37f + seed + i * 1.7f) * MathF.Cos(t * 23.3f + i));
			action(position, radius, intensity * 0.32f * flicker, 9f, color);
		}
	}

	// World point at a distance along the polyline.
	Vector2 PointAtPathDistance(float distance)
	{
		for (var i = 0; i < segments.Count; i++)
		{
			var seg = segments[i];
			var length = seg.Length;
			if (distance <= seg.StartDistance + length || i == segments.Count - 1)
			{
				var local = length < 0.5f ? seg.From
					: Vector2.Lerp(seg.From, seg.To, Math.Clamp((distance - seg.StartDistance) / length, 0, 1));
				return (Vector2)Position + local;
			}
		}
		return (Vector2)Position + segments[^1].To;
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
