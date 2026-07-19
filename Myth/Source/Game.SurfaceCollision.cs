using System.Numerics;
using Foster.Framework;

namespace Myth;

public readonly record struct SurfaceHit(Vector2 Point, Vector2 Normal, float Distance, Actor? Actor, Room.SlopeTile? Slope, RectInt? Rect, Vector2 Correction);

public partial class Game
{
	static readonly List<Vector2> surfaceTraceForward = [];
	static readonly List<Vector2> surfaceTraceBack = [];
	static readonly bool burrowTraceEnabled = Environment.GetEnvironmentVariable("MYTH_BURROW_TRACE") is { Length: > 0 } value && value != "0";
	internal bool BurrowTraceEnabled => burrowTraceEnabled;
	internal void TraceBurrow(string message)
		=> Console.WriteLine($"[Burrow t={Time.Seconds:0.000} room={CurrentRoom?.Id ?? "?"}] {message}");

	/// Trace the contour as a world-space polyline from an existing walk state (boundary
	/// `vertex` + `heading` + handedness), walking `maxArcPerSide` pixels forward and
	/// backward. Used by the burrow glow so it shares the player's exact, corner-correct
	/// walk position instead of re-seeding (which can mis-anchor at concave corners).
	public bool TraceSurfaceFromState(Point2 vertex, Point2 heading, bool solidLeft, float maxArcPerSide, List<Vector2> outPoints)
	{
		outPoints.Clear();
		surfaceTraceForward.Clear();
		surfaceTraceBack.Clear();
		WalkBoundary(vertex, heading, solidLeft, maxArcPerSide, surfaceTraceForward);
		WalkBoundary(vertex, new Point2(-heading.X, -heading.Y), !solidLeft, maxArcPerSide, surfaceTraceBack);

		for (var i = surfaceTraceBack.Count - 1; i >= 1; i--) // skip shared start vertex
			outPoints.Add(surfaceTraceBack[i]);
		outPoints.AddRange(surfaceTraceForward);
		return outPoints.Count >= 2;
	}

	// Find the solid/air face nearest `nearPoint` along `towardSolid`. Returns the
	// dominant-axis normal `n` (into solid), the `faceCorner` (the -tangent end of the
	// shared face, a grid vertex), and the `faceCenter` (the surface point under nearPoint).
	bool SeedBoundaryFace(Vector2 nearPoint, Vector2 towardSolid, int maxReach, out Point2 n, out Point2 faceCorner, out Vector2 faceCenter)
	{
		faceCorner = default;
		faceCenter = nearPoint;
		n = new Point2(
			MathF.Abs(towardSolid.X) >= MathF.Abs(towardSolid.Y) ? Math.Sign(towardSolid.X) : 0,
			MathF.Abs(towardSolid.Y) > MathF.Abs(towardSolid.X) ? Math.Sign(towardSolid.Y) : 0);
		if (n == Point2.Zero || room == null)
			return false;

		// Jumpthru only counts as solid here when probing downward (n points down), so the
		// player can attach to the top of a one-way platform but not its sides/underside.
		var nn = n; // local copy: a local function can't capture the `out` parameter
		bool Seed(Point2 cell) => SolidPixel(cell) || (nn.Y > 0 && JumpthruSolid(cell));

		var c = CellAt(nearPoint);
		var guard = 0;
		while (Seed(c) && guard++ < maxReach)
			c -= n;
		if (Seed(c))
		{
			if (BurrowTraceEnabled)
				TraceBurrow($"surface seed failed to escape solid near={nearPoint} normal={n} reach={maxReach}");
			return false;
		}
		guard = 0;
		while (!Seed(c + n) && guard++ < maxReach)
			c += n;
		if (!Seed(c + n))
			return false;

		faceCenter = new Vector2(c.X + 0.5f, c.Y + 0.5f) + (Vector2)n * 0.5f;
		var tangent = new Point2(n.Y, -n.X);
		faceCorner = RoundV(faceCenter - (Vector2)tangent * 0.5f);
		return true;
	}

	/// Snap `position` onto the nearest solid surface within `maxReach` along `towardSolid`,
	/// setting `position` to the surface point and `towardSolid` to its (axis) normal.
	public bool SnapToSurface(ref Point2 position, ref Vector2 towardSolid, float maxReach)
	{
		if (!SeedBoundaryFace(position, towardSolid, (int)MathF.Ceiling(maxReach), out var n, out _, out var faceCenter))
			return false;

		position = RoundV(faceCenter);
		towardSolid = (Vector2)n;
		return true;
	}

	/// Advance a point `pixels` along the solid/air boundary. `dir > 0` travels +tangent
	/// (tangent = (toward.Y, -toward.X)), `dir < 0` travels -tangent. Uses the same
	/// deterministic grid walk as the glow, so it follows slopes/corners without snagging.
	/// Updates `position` and `towardSolid`; returns the number of pixels actually moved
	/// (0 means a dead end, e.g. the room edge).
	/// Stateful surface walk: step `pixels` along the boundary from the current
	/// `position`/`heading`, keeping solid on the `solidLeft` side, without re-seeding
	/// (so it can't churn in place on slope/corner seams). Updates `position` and
	/// `heading`; returns pixels moved (0 = dead end, e.g. the room edge).
	public int WalkSurface(ref Point2 position, ref Point2 heading, bool solidLeft, int pixels)
	{
		var v = position;
		var d = heading;
		var moved = 0;
		for (var i = 0; i < pixels; i++)
		{
			var nd = PickBoundaryDir(v, d, solidLeft);
			if (nd == Point2.Zero)
				break;
			v += nd;
			d = nd;
			moved++;
		}

		position = v;
		heading = d;
		return moved;
	}

	/// The surface normal (into solid) for a walk state.
	public Vector2 SurfaceNormal(Point2 heading, bool solidLeft)
		=> (Vector2)(solidLeft ? new Point2(heading.Y, -heading.X) : new Point2(-heading.Y, heading.X));

	public bool TryEnterBurrowLine(Point2 position, Vector2 travelHeading, BurrowLine? exclude, float maxDistance,
		out BurrowLine? line, out int segment, out float t, out int direction, out Vector2 point, out Vector2 heading)
	{
		line = null;
		segment = 0;
		t = 0;
		direction = 1;
		point = position;
		heading = travelHeading;
		var bestDistance = maxDistance * maxDistance;

		foreach (var actor in Actors)
		{
			if (actor is not BurrowLine candidate || candidate == exclude ||
				!candidate.TryGetEndpoint(position, maxDistance, out var candidateSegment, out var candidateT,
					out var candidateDirection, out var candidatePoint, out var candidateHeading))
				continue;

			var toEndpoint = candidatePoint - position;
			var distance = toEndpoint.LengthSquared();
			if (distance > 0.001f && Vector2.Dot(Vector2.Normalize(toEndpoint), travelHeading) < 0)
				continue;
			if (distance > bestDistance)
				continue;

			bestDistance = distance;
			line = candidate;
			segment = candidateSegment;
			t = candidateT;
			direction = candidateDirection;
			point = candidatePoint;
			heading = candidateHeading;
		}

		return line != null;
	}

	public bool TryAttachBurrowSurface(Point2 near, Vector2 travelHeading, Vector2 heldIntent, float maxReach,
		out Point2 position, out Point2 heading, out bool solidLeft, out Vector2 towardSolid)
	{
		position = near;
		heading = Point2.Right;
		solidLeft = false;
		towardSolid = Vector2.UnitY;
		var found = false;
		var bestScore = float.MaxValue;
		Span<Vector2> normals = [Vector2.UnitX, -Vector2.UnitX, Vector2.UnitY, -Vector2.UnitY];
		travelHeading = travelHeading.LengthSquared() > 0 ? Vector2.Normalize(travelHeading) : Vector2.UnitX;
		if (BurrowTraceEnabled)
			TraceBurrow($"attach begin at={near} travel={travelHeading} held={heldIntent}");

		// Authored guide endpoints normally land exactly on a boundary vertex. Prefer the
		// boundary walk's own solid/air test here: unlike an axis probe, it distinguishes
		// which edge leaves a corner (for example, the lower-left versus lower-right edge
		// of a pillar). A forward-aligned edge preserves guide momentum; when the guide meets
		// the surface orthogonally, held input chooses between the valid outgoing edges.
		var bestExactScore = float.MinValue;
		Span<Point2> headings = [Point2.Right, Point2.Down, Point2.Left, Point2.Up];
		foreach (var candidateHeading in headings)
		{
			for (var handedness = 0; handedness < 2; handedness++)
			{
				var candidateSolidLeft = handedness != 0;
				if (!BoundaryEdgeValid(near, candidateHeading, candidateSolidLeft))
					continue;

				var candidateVector = (Vector2)candidateHeading;
				var travelAlignment = Vector2.Dot(candidateVector, travelHeading);
				var heldAlignment = Vector2.Dot(candidateVector, heldIntent);
				var score = (travelAlignment > 0.25f ? 4f : 0f) + travelAlignment * 0.25f + heldAlignment;
				if (BurrowTraceEnabled)
					TraceBurrow(
						$"attach exact candidate heading={candidateHeading} solidLeft={candidateSolidLeft} travel={travelAlignment:0.##} held={heldAlignment:0.##} score={score:0.##}");
				if (score <= bestExactScore)
					continue;

				bestExactScore = score;
				found = true;
				position = near;
				heading = candidateHeading;
				solidLeft = candidateSolidLeft;
				towardSolid = SurfaceNormal(candidateHeading, candidateSolidLeft);
			}
		}

		if (found)
		{
			if (BurrowTraceEnabled)
				TraceBurrow(
					$"attach exact chosen position={position} heading={heading} solidLeft={solidLeft} normal={towardSolid}");
			return true;
		}

		foreach (var normal in normals)
		{
			var candidate = near;
			var toward = normal;
			if (!SnapToSurface(ref candidate, ref toward, maxReach))
				continue;

			var tangent = new Vector2(toward.Y, -toward.X);
			var alignment = Vector2.Dot(tangent, travelHeading);
			// A guide meeting a surface orthogonally has no momentum-based answer for left
			// versus right. Use held input only for that ambiguous seam; otherwise preserve
			// incoming travel so ordinary guide exits and the boundary walk keep their orbit.
			var directionAlignment = MathF.Abs(alignment) > 0.25f
				? alignment
				: Vector2.Dot(tangent, heldIntent);
			var candidateSolidLeft = directionAlignment < 0;
			if (candidateSolidLeft)
				tangent = -tangent;
			var score = Vector2.DistanceSquared(candidate, near) + (1 - MathF.Abs(alignment)) * 2;
			if (BurrowTraceEnabled)
				TraceBurrow(
					$"attach snap candidate position={candidate} heading={tangent} normal={toward} score={score:0.##}");
			if (score >= bestScore)
				continue;

			bestScore = score;
			found = true;
			position = candidate;
			heading = new Point2((int)MathF.Round(tangent.X), (int)MathF.Round(tangent.Y));
			solidLeft = candidateSolidLeft;
			towardSolid = toward;
		}
		if (BurrowTraceEnabled)
			TraceBurrow(found
				? $"attach snap chosen position={position} heading={heading} solidLeft={solidLeft} normal={towardSolid}"
				: "attach failed: no valid exact edge or nearby surface");

		return found;
	}

	void WalkBoundary(Point2 startVertex, Point2 heading, bool solidLeft, float maxArc, List<Vector2> outPoints)
	{
		var v = startVertex;
		var d = heading;
		outPoints.Add(v);
		var len = 0f;
		while (len < maxArc)
		{
			var nd = PickBoundaryDir(v, d, solidLeft);
			if (nd == Point2.Zero)
				break;
			v += nd;
			d = nd;
			len += 1f;
			outPoints.Add(v);
		}
	}

	// Left-hand rule: prefer turning toward the solid, then straight, then away. No
	// reverse — at a genuine dead end (e.g. the room edge) we stop rather than fold back.
	Point2 PickBoundaryDir(Point2 v, Point2 heading, bool solidLeft)
	{
		var left = new Point2(heading.Y, -heading.X);
		var right = new Point2(-heading.Y, heading.X);
		Span<Point2> candidates = solidLeft
			? [left, heading, right]
			: [right, heading, left];

		foreach (var nd in candidates)
			if (BoundaryEdgeValid(v, nd, solidLeft))
				return nd;
		return Point2.Zero;
	}

	bool BoundaryEdgeValid(Point2 v, Point2 d, bool solidLeft)
	{
		var left = CellAt((Vector2)v + (Vector2)d * 0.5f + new Vector2(d.Y, -d.X) * 0.5f);
		var right = CellAt((Vector2)v + (Vector2)d * 0.5f + new Vector2(-d.Y, d.X) * 0.5f);
		var solidCell = solidLeft ? left : right;
		var airCell = solidLeft ? right : left;

		// A walkable edge has its air side empty AND inside the room, so the walk never
		// rides the outer faces of the room's perimeter tiles. A jumpthru on the air side
		// also counts as "not air" so a jumpthru continuing a floor doesn't read as a
		// convex corner — otherwise the walk dives down a solid's face at the seam instead
		// of stepping straight onto the jumpthru top.
		if (SolidPixel(airCell) || JumpthruSolid(airCell) || !InRoom(airCell))
			return false;

		// Real solids are walkable on any face.
		if (SolidPixel(solidCell))
			return true;

		// Jumpthru platforms are one-way, so only their TOP face is burrowable: the air
		// cell must sit directly above the jumpthru cell. This lets the player ride along
		// the top of a jumpthru run without wrapping under it or around its ends.
		return JumpthruSolid(solidCell) && airCell == solidCell + new Point2(0, -1);
	}

	bool InRoom(Point2 cell) => room?.WorldBounds.Contains(cell) ?? false;

	bool SolidPixel(Point2 cell)
	{
		var point = new Vector2(cell.X + 0.5f, cell.Y + 0.5f);
		if (RoomCollisionContains(point))
			return true;

		foreach (var other in Actors)
		{
			if (other is Grid || !other.Mask.Has(Actor.Masks.Solid) || other.Hitbox.Shape != Hitbox.Shapes.Rect)
				continue;

			var rect = other.Hitbox.Rect + other.Position;
			if (point.X >= rect.Left && point.X < rect.Right && point.Y >= rect.Top && point.Y < rect.Bottom)
				return true;
		}

		return false;
	}

	// A jumpthru (one-way platform) cell — same rect test as SolidPixel but for the
	// Jumpthru mask. The burrow boundary walk uses this to ride the *top* of jumpthru runs.
	bool JumpthruSolid(Point2 cell)
	{
		var point = new Vector2(cell.X + 0.5f, cell.Y + 0.5f);
		foreach (var other in Actors)
		{
			if (other is Grid || !other.Mask.Has(Actor.Masks.Jumpthru) || other.Hitbox.Shape != Hitbox.Shapes.Rect)
				continue;

			var rect = other.Hitbox.Rect + other.Position;
			if (point.X >= rect.Left && point.X < rect.Right && point.Y >= rect.Top && point.Y < rect.Bottom)
				return true;
		}

		return false;
	}

	/// True when the burrow surface just inside `toward` from `position` is a jumpthru
	/// (one-way platform) rather than real solid. Lets the player ride on top of thin
	/// jumpthru runs (no submerge) instead of sinking through them.
	public bool SurfaceIsJumpthru(Point2 position, Vector2 toward)
	{
		var cell = CellAt((Vector2)position + toward * 0.5f);
		return !SolidPixel(cell) && JumpthruSolid(cell);
	}

	static Point2 CellAt(Vector2 p) => new((int)MathF.Floor(p.X), (int)MathF.Floor(p.Y));
	static Point2 RoundV(Vector2 p) => new((int)MathF.Round(p.X), (int)MathF.Round(p.Y));

	public bool OverlapsSolid(Actor actor, in RectInt bounds)
	{
		if (OverlapsRoomCollision(bounds))
			return true;

		foreach (var other in Actors)
		{
			if (other == actor || other is Grid || !other.Mask.Has(Actor.Masks.Solid))
				continue;

			if (other.Hitbox.Overlaps(bounds - other.Position))
				return true;
		}

		return false;
	}

	public bool TryFindSurface(Actor actor, in RectInt bounds, Vector2 direction, float distance, out SurfaceHit hit)
	{
		hit = default;
		if (direction.LengthSquared() <= 0.0001f || distance < 0)
			return false;

		direction = Vector2.Normalize(direction);
		var steps = MathF.Ceiling(distance);
		var previousOffset = Point2.Zero;

		for (var i = 0; i <= steps; i++)
		{
			var offset = Round(direction * i);
			if (offset == previousOffset && i > 0)
				continue;

			previousOffset = offset;
			var candidate = bounds + offset;
			if (TryGetSurfaceAt(actor, candidate, direction, offset, i, out hit))
				return true;
		}

		return false;
	}

	bool TryGetSurfaceAt(Actor actor, in RectInt bounds, Vector2 searchDirection, Point2 offset, float distance, out SurfaceHit hit)
	{
		if (room != null)
		{
			foreach (var slope in room.SlopeTiles) if (OverlapsSlope(bounds, room, slope))
			{
				room.GetSlopeSurface(slope, out var left, out var right);
				var normal = SlopeNormal(left, right, slope.Fill);
				var correction = (Vector2)offset + SlopeCorrection(bounds, left, normal);
				hit = new SurfaceHit(SurfacePoint(bounds, normal), normal, distance, null, slope, null, correction);
				return true;
			}

			foreach (var rect in room.CollisionRects) if (bounds.Overlaps(rect))
			{
				var normal = AxisNormal(searchDirection);
				hit = new SurfaceHit(SurfacePoint(bounds, normal), normal, distance, null, null, rect, Vector2.Zero);
				return true;
			}
		}

		foreach (var other in Actors)
		{
			if (other == actor || other is Grid || !other.Mask.Has(Actor.Masks.Solid | Actor.Masks.Jumpthru))
				continue;

			if (!other.Hitbox.Overlaps(bounds - other.Position))
				continue;

			var normal = AxisNormal(searchDirection);
			hit = new (SurfacePoint(bounds, normal), normal, distance, other, null, other.Hitbox.Rect + other.Position, Vector2.Zero);
			return true;
		}

		hit = default;
		return false;
	}

	static Vector2 SlopeNormal(Vector2 left, Vector2 right, Room.SlopeFill fill)
	{
		var tangent = Vector2.Normalize(right - left);
		var normal = new Vector2(tangent.Y, -tangent.X);
		return fill == Room.SlopeFill.BelowLine ? normal : -normal;
	}

	static Vector2 AxisNormal(Vector2 searchDirection)
	{
		if (MathF.Abs(searchDirection.X) > MathF.Abs(searchDirection.Y))
			return new Vector2(-MathF.Sign(searchDirection.X), 0);

		return new Vector2(0, -MathF.Sign(searchDirection.Y));
	}

	static Vector2 SurfacePoint(RectInt bounds, Vector2 normal)
	{
		var center = (Vector2)bounds.Center;
		return new Vector2(
			normal.X < 0 ? bounds.Left : normal.X > 0 ? bounds.Right : center.X,
			normal.Y < 0 ? bounds.Top : normal.Y > 0 ? bounds.Bottom : center.Y);
	}

	static Vector2 SlopeCorrection(RectInt bounds, Vector2 surfacePoint, Vector2 normal)
	{
		var center = bounds.CenterF;
		var radius = SupportRadius(bounds, normal);
		var distance = Vector2.Dot(center - surfacePoint, normal);
		return normal * (radius - distance);
	}

	static float SupportRadius(RectInt bounds, Vector2 normal)
	{
		var center = bounds.CenterF;
		return MathF.Max(
			MathF.Max(
				MathF.Abs(Vector2.Dot((Vector2)bounds.TopLeft - center, normal)),
				MathF.Abs(Vector2.Dot((Vector2)bounds.TopRight - center, normal))),
			MathF.Max(
				MathF.Abs(Vector2.Dot((Vector2)bounds.BottomLeft - center, normal)),
				MathF.Abs(Vector2.Dot((Vector2)bounds.BottomRight - center, normal))));
	}

	static Point2 Round(Vector2 value) => new((int)MathF.Round(value.X), (int)MathF.Round(value.Y));
}
