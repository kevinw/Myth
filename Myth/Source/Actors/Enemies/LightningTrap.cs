using System.Numerics;
using Foster.Framework;

namespace Myth;

// A wall/floor-mounted trap that periodically emits a 1-tile-thick band of lightning along
// Direction. The beam reflects off 45-degree reflective (metal) slopes, so its path is a polyline
// of axis-aligned segments. It gathers for a moment (light sparking in from both ends of the whole
// path) as a telegraph, then the full path becomes a lethal hurtbox for FireTime.
// Indestructible: the player can't hit it, only avoid the beam window.
[GameActor(Sprite = "Editor/particles", Icon = "bolt-lightning", Tags = ActorTag.Hazard,
	BoundsX = -6, BoundsY = -6, BoundsW = 12, BoundsH = 12, Anchor = SpriteAnchor.Center,
	LivePositionEditable = true)]
public class LightningTrap : Actor, IRoomInversionListener, IPenDrawn
{
	public Cardinal Direction = Cardinal.Right;
	[I(0.1f, 5)] public float WaitDelay = 1.5f;   // idle time between fires
	[I(0.1f, 2)] public float GatherTime = 0.4f;  // telegraph windup; beam gathers but isn't lethal yet
	[I(0.1f, 2)] public float FireTime = 0.7f;    // duration the lethal hurtbox is on
	[I(0, 8)] public float StartDelay = 0f;       // one-time extra wait before the first fire, to stagger traps

	static float FireRampTime => 0.07f; // visual ramp to full intensity when the hurtbox turns on
	static float FireTailTime => 0.08f; // quick visual fade as the window closes
	static int HalfWidth => Game.TileSize / 2;
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
	SoundInstance? sound;

	const string SoundEvent = "lightning_trap";
	const string WarmupParameter = "Warmup";
	const string FireDurationParameter = "FireDuration";

	public LightningTrap()
	{
		Sprite = null;
		CollidesWithSolids = false;
		Mask = Masks.Hazard;
		Depth = -6;
	}

	// Only the beam hurts. ApplyDamage drives that from the traced path, so the trap itself never presents a
	// hurtbox: an empty Hitbox isn't enough, since a zero-size rect still reads as the point it sits on
	// (RectInt.Overlaps compares X + Width > other.X), and brushing the housing landed on that point.
	internal override bool OverlapsCombatBounds(in RectInt bounds, CombatRole role = CombatRole.Vulnerable) => false;

	public override void Added()
	{
		seed = Position.X * 397 ^ Position.Y;
		phase = Phase.Wait;
		phaseTime = -MathF.Max(0, StartDelay); // stagger the first fire
		sparkEffect = AcquireParticleEffect("lightning_spark");
	}

	public override void Destroyed()
	{
		ReleaseParticleEffect(ref sparkEffect);
		StopAndReleaseSound(ref sound, true);
	}

	Vector2 Dir => DirectionVector(Direction);
	Vector2 HousingScale => Direction.Horizontal ? new Vector2(0.5f, 1) : new Vector2(1, 0.5f);

	static Vector2 DirectionVector(Cardinal direction) => direction.Normal;

	void IRoomInversionListener.RoomInverted()
	{
		Direction = Direction.Reverse;
		if (phase != Phase.Wait)
			TraceBeam();
	}

	public override void Update()
	{
		base.Update();
		phaseTime += Time.Delta;
		if (phase != Phase.Wait || segments.Count == 0)
			TraceBeam();
		switch (phase)
		{
			case Phase.Wait:
				if (phaseTime >= WaitDelay)
					EnterGather();
				break;

			case Phase.Gather:
				UpdateSound(GatherProgress, 0);
				if (phaseTime >= GatherTime)
				{
					phase = Phase.Fire;
					phaseTime = 0;
					UpdateSound(1, 0);
				}
				break;

			case Phase.Fire:
				UpdateSound(1, FireProgress);
				ApplyDamage();
				UpdateSparks();
				if (phaseTime >= FireTime)
				{
					UpdateSound(1, 1);
					StopAndReleaseSound(ref sound);
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
		StopAndReleaseSound(ref sound, true);
		sound = Game.CreateAudioEvent(SoundEvent, Position);
		UpdateSound(0, 0);
		sound?.Start();
	}

	void UpdateSound(float warmup, float fireDuration)
	{
		Game.SetAudioEventPosition(sound, Position);
		Game.SetAudioEventParameter(sound, WarmupParameter, warmup);
		Game.SetAudioEventParameter(sound, FireDurationParameter, fireDuration);
	}

	// Walks the beam from the mouth, reflecting off reflective (metal) 45-degree slopes, building a
	// polyline of axis-aligned segments (a 45-degree reflection turns an axis beam exactly 90).
	void TraceBeam()
	{
		segments.Clear();
		impacts.Clear();
		pathLength = 0;

		var dir = Dir;
		// Keep the beam centered on its authored housing. The collision probe still snaps to the
		// tile lane so a boundary-aligned ray doesn't straddle a solid and miss the slope it targets.
		var visualStart = (Vector2)Position;
		// The trap is mounted inside a wall, so search from the first pixel clear of solid; otherwise
		// the ray hits the very tile it's embedded in and the beam collapses to zero length.
		var rayStart = AdvancePastSolid(SnapToLane(visualStart, dir), dir);

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
			if (Game.OverlapsFirstCombat(rect, Masks.Player) is { } player)
			{
				Hit(player, new(rect));
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
			particles.Emit(sparkEffect, impact.Point, 8, impact.Angle, 1f, SparkSpread);
	}

	// The whole beam path is unsafe footing between fires too, hurtbox or not -- waking up in a lane
	// that lights up a second later is just a slower death.
	internal override bool UnsafeFooting(in RectInt standing)
	{
		foreach (var seg in segments)
			if (SegmentWorldRect(seg).Overlaps(standing))
				return true;
		return false;
	}

	float GatherProgress => Math.Clamp(phaseTime / MathF.Max(0.0001f, GatherTime), 0, 1);
	float FireProgress => Math.Clamp(phaseTime / MathF.Max(0.0001f, FireTime), 0, 1);

	// 0 idle, ramps up through the gather, full while firing.
	float Glow => phase switch
	{
		Phase.Gather => Calc.Lerp(0.2f, 0.8f, GatherProgress),
		Phase.Fire => 1f,
		_ => 0f,
	};

	ContentRef<VisualTextureContent> IPenDrawn.PenTexture => PenMaterial.MachinedTexture;
	static Color ElectrodeColor => 0x79cfff;

	void IPenDrawn.Draw(Pen pen)
	{
		// The housing is squashed along the beam, so it reads as a flat plate set into the wall.
		const int Steps = 16;
		var material = new Vector2(48);
		Span<Vector2> plate = stackalloc Vector2[Steps];
		Span<Vector2> plateMaterial = stackalloc Vector2[Steps];
		Span<Vector2> mouth = stackalloc Vector2[Steps];
		Span<Vector2> mouthMaterial = stackalloc Vector2[Steps];
		for (var i = 0; i < Steps; i++)
		{
			var direction = Calc.AngleToVector(i / (float)Steps * MathF.Tau);
			plate[i] = direction * 6 * HousingScale;
			plateMaterial[i] = material + direction * 6;
			mouth[i] = direction * 2.5f * HousingScale;
			mouthMaterial[i] = material + direction * 2.5f;
		}
		pen.Plate(plate, plateMaterial, new Color(0x2a2140), new Color(0x171319));
		pen.Plate(mouth, mouthMaterial, new Color(0x3d2f5c), new Color(0x171319));

		// Electrodes: studs that warm toward the beam's color through the gather, so the telegraph is on the
		// hardware too, and catch the beam's own light while it fires.
		var glow = Glow;
		var perp = Dir.TurnLeft();
		for (var s = -1; s <= 1; s += 2)
		{
			var at = perp * (s * 2.5f);
			pen.Ball(at, 1.4f, material + at, Color.Lerp(new Color(0x3a3550), ElectrodeColor, glow), new Color(0x171319), round: true);
			if (glow > 0.01f)
				pen.Glow(at, 1.6f, ElectrodeColor * glow);
		}

		// The beam is energy: colors and glow only, drawn last so the housing never erases it.
		if (pen.Pass is PenPass.Color or PenPass.Emissive)
		{
			pen.Batcher.PushBlend(pen.Pass == PenPass.Emissive ? GameView.EmissiveBlend : BlendMode.Premultiply);
			DrawBeam(pen.Batcher);
			pen.Batcher.PopBlend();
		}
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
			var g = GatherProgress;
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

#if EDITOR
	sealed class Editor : Manager.ActorEditor<LightningTrap>
	{
		internal override void Gizmos(Manager.EditorActor actor, ref Manager.EditorGizmos gizmos)
		{
			var dir = DirectionVector(actor.GetCardinal(nameof(Direction), Cardinal.Right));
			var center = actor.Bounds.CenterF;
			gizmos.Arrow(center + dir * 5, center + dir * 13, gizmos.LineWeight * 2, new Color(0xff3040));
		}
	}
#endif
}
