using System.Numerics;
using Foster.Framework;

namespace Myth;

// A hanging, climbable rope: a verlet point chain pinned at the top anchor. Pawns attach via
// TryGrab and ride the simulated points (see Pawn.ClimbState), so idle sway, input pumping and
// swing-jump momentum all come from the one simulation. Break() snips the chain: the upper part
// stays pinned, the lower part becomes an unpinned chain that falls and drapes over the tiles
// as set dressing (not re-climbable).
[GameActor(Tags = ActorTag.Obstacle, Anchor = SpriteAnchor.TopLeft)]
public class Rope : Actor, IResizable
{
	public class Chain
	{
		public Vector2[] Points = [];
		public Vector2[] Prev = [];
		public bool Pinned;
		// Rider bookkeeping, re-registered by the climbing pawn every frame (cleared each Update):
		// the ridden point is heavier in the constraint solve and carries the pump acceleration.
		public int RiderIndex = -1;
		public float SwingAccel;
	}

	public static readonly Point2 DefaultSize = new(8, 96);
	public static readonly ResizeAxes ResizableAxes = ResizeAxes.Y;
	public Point2 Size { get; set; } = DefaultSize;

	// Heavier ropes hang calmer: ambient sway and pump response divide by Weight, damping
	// multiplies by it.
	public float Weight = 1f;
	// Deployed by a RopePile: points start gathered at the anchor and pay out one per frame as
	// the falling end pulls taut, so the rope feeds down cleanly instead of tangling on the lip.
	public bool StartCoiled;

	public const float SegmentLength = 4f;
	static float RopeGravity => 260f;
	static float DampingRate => 1.1f;           // per-second velocity bleed at Weight 1
	static float SwayAccel => 5.5f;             // px/s² ambient drift at Weight 1
	static float SwayFrequency => 0.35f;        // sway cycles per second
	static float SwayPointPhase => 0.09f;       // phase shift per point, so the rope bows instead of shifting rigidly
	public static float PumpAccel => 340f;      // holding left/right while attached, at Weight 1
	static float RiderExtraGravity => 500f;     // the hanging pawn's weight pulls the rope taut
	static float RiderConstraintShare => 0.15f; // ridden point moves this fraction in the solve (it's heavy)
	const int ConstraintIterations = 4;
	static float MaxSimDelta => 1 / 30f;        // clamp hitches so the verlet step can't explode

	readonly List<Chain> chains = [];
	int waiting; // leading pinned-chain points still held at the anchor; 1 means fully paid out
	Point2 builtSize;
	float lastDelta = 1 / 60f;

	public Rope()
	{
		Mask = Masks.None;
		CollidesWithSolids = false;
		Depth = 1000;
	}

	public override bool CastsSpriteShadow => false;
	public override bool CastsReflection => false;

	Point2 TopAnchor => Position + new Point2(Size.X / 2, 0);

	public override void Added()
	{
		base.Added();
		Hitbox = new Hitbox(new RectInt(0, 0, Size.X, Size.Y));
		BuildChains();
	}

	void BuildChains()
	{
		builtSize = Size;
		chains.Clear();
		var count = Math.Max(2, (int)(Size.Y / SegmentLength) + 1);
		var chain = new Chain { Points = new Vector2[count], Prev = new Vector2[count], Pinned = true };
		for (var i = 0; i < count; i++)
		{
			chain.Points[i] = StartCoiled ? TopAnchor : TopAnchor + new Vector2(0, i * SegmentLength);
			chain.Prev[i] = chain.Points[i];
		}
		waiting = StartCoiled ? count - 1 : 1;
		chains.Add(chain);
	}

	public override void Update()
	{
		if (builtSize != Size)
			BuildChains(); // editor resize while unbroken; runtime never resizes

		var dt = MathF.Min(Time.Delta, MaxSimDelta);
		if (dt <= 0)
			return;
		lastDelta = dt;

		var damp = Math.Clamp(1f - DampingRate * Weight * dt, 0f, 1f);
		var swayPhase = (float)(Time.Seconds * SwayFrequency * MathF.Tau);
		foreach (var chain in chains)
		{
			var held = chain.Pinned ? waiting : 0;
			Integrate(chain, held, dt, damp, swayPhase);
			Constrain(chain, held);
			Collide(chain, held);
			chain.RiderIndex = -1;
			chain.SwingAccel = 0;
		}
		PayOut();
	}

	// The rope feeds out at the anchor, tip first: the hanging span grows from the last index
	// toward index 0, and each release hands the span's top segment to the next held point, so a
	// segment is never stretched more than one length. Release when the span pulls the boundary
	// segment taut; one point per frame caps the feed rate, like rope running over a lip.
	void PayOut()
	{
		if (waiting <= 1 || PinnedChain is not { } chain || waiting >= chain.Points.Length)
			return;
		if (Vector2.DistanceSquared(chain.Points[waiting], TopAnchor) >= (SegmentLength - 1) * (SegmentLength - 1))
			waiting--;
	}

	void Integrate(Chain chain, int held, float dt, float damp, float swayPhase)
	{
		var points = chain.Points;
		var prev = chain.Prev;
		for (var i = 0; i < held; i++)
			points[i] = prev[i] = TopAnchor;
		for (var i = held; i < points.Length; i++)
		{
			var accel = new Vector2(0, RopeGravity);
			if (chain.Pinned)
				accel.X += SwayAccel / Weight * MathF.Sin(swayPhase + i * SwayPointPhase);
			if (i == chain.RiderIndex)
				accel += new Vector2(chain.SwingAccel, RiderExtraGravity);

			var p = points[i];
			points[i] = p + (p - prev[i]) * damp + accel * (dt * dt);
			prev[i] = p;
		}
	}

	void Constrain(Chain chain, int held)
	{
		var points = chain.Points;
		for (var it = 0; it < ConstraintIterations; it++)
		{
			for (var i = Math.Max(held, 1); i < points.Length; i++)
			{
				var delta = points[i] - points[i - 1];
				var dist = delta.Length();
				if (dist <= 0.0001f)
					continue;
				var correction = delta / dist * (dist - SegmentLength);

				// Inverse-mass weights: pinned/held anchor points never move, the ridden point barely.
				var wa = i - 1 < held ? 0f : i - 1 == chain.RiderIndex ? RiderConstraintShare : 1f;
				var wb = i == chain.RiderIndex ? RiderConstraintShare : 1f;
				points[i - 1] += correction * (wa / (wa + wb));
				points[i] -= correction * (wb / (wa + wb));
			}
			if (chain.Pinned)
				points[0] = TopAnchor;
		}
	}

	// One point-probe per node: a move into solid is cancelled outright (position and velocity),
	// which both stops a swing at a wall and lets unpinned remnants pile up and drape on floors.
	void Collide(Chain chain, int held)
	{
		var points = chain.Points;
		var prev = chain.Prev;
		for (var i = held; i < points.Length; i++)
		{
			var p = (Point2)points[i];
			if (Game.OverlapsRoomCollision(new RectInt(p.X, p.Y, 1, 1)))
				points[i] = prev[i];
		}
	}

	// Snip the pinned chain at the joint nearest worldY. The freed lower part keeps simulating
	// unpinned (it falls and drapes); attached pawns are handed the new chain so they can ride it
	// down (see Pawn.OnRopeCut).
	public bool Break(Vector2 worldPos)
	{
		var chain = PinnedChain;
		if (chain == null || chain.Points.Length < 4 || waiting > 1)
			return false; // no snipping mid-unfurl: the held points would fall as a clump

		float worldY = worldPos.Y;

		var minDistance = float.PositiveInfinity;
		var minI = -1;

		for (var i = 0; i < chain.Points.Length; ++i)
		{
			var point = chain.Points[i];
			float distance = Vector2.DistanceSquared(point, worldPos);
			if (distance < minDistance)
			{
				minDistance = distance;
				minI = i;
			}
		}

		if (minI == -1) {
			return false;
		}

		//var cut = Math.Clamp((int)MathF.Round((worldY - TopAnchor.Y) / SegmentLength), 2, chain.Points.Length - 2);
		var cut = minI;
		var lower = new Chain { Points = chain.Points[cut..], Prev = chain.Prev[cut..] };
		chain.Points = chain.Points[..cut];
		chain.Prev = chain.Prev[..cut];
		chains.Add(lower);

		foreach (var actor in Game.Actors)
			if (actor is Pawn pawn)
				pawn.OnRopeCut(this, chain, cut, lower);

		return true;
	}

	public bool Broken => chains.Count > 1 || PinnedChain == null;

	Chain? PinnedChain
	{
		get
		{
			foreach (var chain in chains)
				if (chain.Pinned)
					return chain;
			return null;
		}
	}

	// Nearest grabbable point to `from` within `radius`, on the pinned chain only (fallen
	// remnants are set dressing, not ladders).
	public bool TryGrab(Vector2 from, float radius, out Chain chain, out float index)
	{
		if (TryGetClosestGrabbablePoint(from, radius * radius, out chain, out var pointIndex))
		{
			index = pointIndex;
			return true;
		}
		index = 0;
		return false;
	}

	internal bool TryGetGrabTarget(Vector2 from, out Vector2 target)
	{
		if (TryGetClosestGrabbablePoint(from, float.PositiveInfinity, out var chain, out var index))
		{
			target = chain.Points[index];
			return true;
		}
		target = default;
		return false;
	}

	bool TryGetClosestGrabbablePoint(Vector2 from, float bestSq, out Chain chain, out int index)
	{
		chain = null!;
		index = -1;
		if (PinnedChain is not { } pinned)
			return false;

		for (var i = waiting > 1 ? waiting : 0; i < pinned.Points.Length; i++)
		{
			var distSq = Vector2.DistanceSquared(pinned.Points[i], from);
			if (distSq < bestSq)
			{
				bestSq = distSq;
				index = i;
			}
		}
		if (index < 0)
			return false;
		chain = pinned;
		return true;
	}

	public bool Contains(Chain chain) => chains.Contains(chain);
	public static int LastIndex(Chain chain) => chain.Points.Length - 1;

	public Vector2 Sample(Chain chain, float index)
	{
		var i = Math.Clamp((int)index, 0, chain.Points.Length - 2);
		return Vector2.Lerp(chain.Points[i], chain.Points[i + 1], Math.Clamp(index - i, 0, 1));
	}

	public Vector2 PointVelocity(Chain chain, float index)
	{
		var i = Math.Clamp((int)MathF.Round(index), 0, chain.Points.Length - 1);
		return (chain.Points[i] - chain.Prev[i]) / lastDelta;
	}

	public void AddPointVelocity(Chain chain, float index, Vector2 velocity)
	{
		var i = Math.Clamp((int)MathF.Round(index), chain.Pinned ? 1 : 0, chain.Points.Length - 1);
		chain.Prev[i] -= velocity * lastDelta;
	}

	// Registered by the climbing pawn every frame: adds its weight to the solve and applies the
	// left/right pump. Pump strength falls out of the sim naturally — the same force swings the
	// free end far and the near-anchor points barely at all.
	public void SetRider(Chain chain, float index, float pumpDirection)
	{
		chain.RiderIndex = Math.Clamp((int)MathF.Round(index), chain.Pinned ? waiting : 0, LastIndex(chain));
		chain.SwingAccel = pumpDirection * PumpAccel / Weight;
	}

	static readonly Color ColorDark = new(0x3a2416);
	static readonly Color ColorMain = new(0x8a5e36);
	static readonly Color ColorLight = new(0xb5885a);

	public override void Render(Batcher batcher)
	{
		if (builtSize != Size)
			BuildChains(); // editor drag-resize renders without Update running
		RenderChains(batcher, null);
	}
	public override void RenderShadow(Batcher batcher, Color color) => RenderChains(batcher, color);
	public override void RenderReflection(Batcher batcher, Color color) => RenderChains(batcher, color);

	void RenderChains(Batcher batcher, Color? flat)
	{
		// Render happens in actor-local space; points are world.
		foreach (var chain in chains)
			for (var i = 1; i < chain.Points.Length; i++)
			{
				var color = flat ?? (i % 2 == 0 ? ColorMain : ColorLight);
				batcher.Line(((Point2)chain.Points[i - 1]) - Position, ((Point2)chain.Points[i]) - Position, 1, color);
			}
		if (PinnedChain != null)
			batcher.Rect(new Rect(TopAnchor.X - Position.X - 1, TopAnchor.Y - Position.Y, 3, 2), flat ?? ColorDark);
	}

	public override Rect GetVisualBounds()
	{
		var min = (Vector2)TopAnchor;
		var max = min;
		foreach (var chain in chains)
			foreach (var p in chain.Points)
			{
				min = Vector2.Min(min, p);
				max = Vector2.Max(max, p);
			}
		return new Rect(min.X - 2, min.Y - 2, max.X - min.X + 4, max.Y - min.Y + 4);
	}
}
