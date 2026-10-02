using System.Numerics;
using Foster.Framework;

namespace Myth;

public readonly record struct SurfaceHit(Vector2 Point, Vector2 Normal, float Distance, Actor? Actor, Room.SlopeTile? Slope, RectInt? Rect, Vector2 Correction);

// Where a burrow is and which way it's going: along a surface, or along a BurrowLine.
public struct BurrowCursor
{
	public Point2 Heading;    // world travel direction along the surface
	public bool SolidLeft;    // walk handedness (= orbit direction); only flips on a fresh press
	public Vector2 Toward;    // surface normal (into solid) at the burrow point
	public BurrowLine? Line;
	public BurrowLine? LineExit; // the line just left, not to be re-entered until clear of its end
	public int Segment;
	public float T;
	public int Direction;
	public Vector2 LineHeading;
	public bool Blocked;      // at a line's end inside solid, with no surface to take and no room to pop out
	public Actor? Seam;       // sealed between this crusher and what it closed on (see Pawn.UpdateBurrowSeam)
	public bool SeamFollow;   // walking the crusher's own outline rather than the solid it pinned us to

	public readonly Vector2 Travel => Line != null ? LineHeading : Heading;

	// Climbing slows it: a straight-up heading runs at BurrowClimbSpeedMultiplier, a 45° slope lands in
	// between on its own (the cardinal walker alternates up/flat steps), and descending counts as flat.
	public readonly float CruiseSpeed(bool dashing)
		=> Pawn.BurrowMaxSpeed * (dashing ? Pawn.DashBurrowSpeedMultiplier : 1f) * (Line?.SpeedScale ?? 1f) *
			(1f - (1f - Pawn.BurrowClimbSpeedMultiplier) * MathF.Max(0, -Travel.Y));
}

// How a stretch of burrow travel ended, where it's the traveller's to settle: a surface it can't go further
// along, a door it went through, a switch it has stopped on, or a line that ran out with no junction to take.
public enum BurrowTravel { Moving, DeadEnd, Door, Switch, LineEnd }

public partial class Game
{
	internal static float BurrowLineAttachDistance => 3;
	static float BurrowLineSteerThreshold => 0.3f;
	static readonly List<Vector2> surfaceTraceForward = [];
	static readonly List<Vector2> surfaceTraceBack = [];
	static readonly bool burrowTraceEnabled = Environment.GetEnvironmentVariable("MYTH_BURROW_TRACE") is { Length: > 0 } value && value != "0";
	internal bool BurrowTraceEnabled => burrowTraceEnabled && !BurrowTraceQuiet;
	// Set around travel that's only being looked ahead along.
	internal bool BurrowTraceQuiet;
	internal void TraceBurrow(string message)
		=> Console.WriteLine($"[Burrow t={Time.Seconds:0.000} room={CurrentRoom?.Id ?? "?"}] {message}");

	// A burrow's travel, `pixels` of it: onto a line whose end it has come to, along the surface or the line,
	// and through the line's junctions by the held `intent`. `doors` takes the game through a burrow door.
	public BurrowTravel TravelBurrow(ref Point2 at, ref BurrowCursor burrow, int pixels, Vector2 intent, bool doors, out float remaining)
	{
		using var _ = WalkingSeam(burrow.Seam, burrow.SeamFollow);
		remaining = pixels;
		if (burrow.Line == null && TryEnterBurrowLine(at, burrow.Heading, burrow.LineExit, BurrowLineAttachDistance, intent,
			out var enteredLine, out burrow.Segment, out burrow.T, out burrow.Direction, out var entryPoint, out burrow.LineHeading))
		{
			burrow.Line = enteredLine;
			at = (Point2)entryPoint;
			burrow.LineExit = null;
			if (BurrowTraceEnabled)
				TraceBurrow(
					$"line entered id={burrow.Line?.ActorId ?? "?"} position={at} segment={burrow.Segment} t={burrow.T:0.###} direction={burrow.Direction} heading={burrow.LineHeading}");
		}

		if (burrow.Line == null)
		{
			var moved = WalkSurface(ref at, ref burrow.Heading, burrow.SolidLeft, pixels);
			burrow.Toward = SurfaceNormal(burrow.Heading, burrow.SolidLeft);
			return moved == 0 ? BurrowTravel.DeadEnd : BurrowTravel.Moving;
		}

		for (var junctions = 0; junctions < BurrowLine.MaxJunctionTransfers; junctions++)
		{
			var exitedLine = burrow.Line.Advance(ref burrow.Segment, ref burrow.T, burrow.Direction,
				remaining, out var linePoint, out burrow.LineHeading, out remaining, out var junctionPointIndex);
			at = (Point2)linePoint;
			if (!exitedLine)
				break;

			if (BurrowTraceEnabled)
				TraceBurrow(
					$"line endpoint reached id={burrow.Line.ActorId ?? "?"} position={at} heading={burrow.LineHeading} intent={intent}");
			if (doors && junctionPointIndex >= 0 && burrow.Line.IsDoorPoint(junctionPointIndex) &&
				TransitionToBurrowDoor(burrow.Line, junctionPointIndex, out var doorLine, out var doorPoint))
			{
				burrow.Line = doorLine;
				burrow.Segment = doorPoint == 0 ? 0 : burrow.Line.Points.Length - 2;
				burrow.T = doorPoint == 0 ? 0 : 1;
				burrow.Direction = doorPoint == 0 ? 1 : -1;
				burrow.LineHeading = burrow.Line.SegmentHeading(burrow.Segment, burrow.Direction);
				at = (Point2)burrow.Line.WorldPoint(doorPoint);
				burrow.LineExit = null;
				burrow.Blocked = false;
				if (BurrowTraceEnabled)
					TraceBurrow(
						$"line sublayer handoff id={burrow.Line.ActorId ?? "?"} position={at} heading={burrow.LineHeading}");
				return BurrowTravel.Door;
			}
			if (junctionPointIndex >= 0 && burrow.Line.IsSwitchPoint(junctionPointIndex))
				return BurrowTravel.Switch;
			if (junctionPointIndex < 0 || !TryContinueBurrowLine(burrow.Line, junctionPointIndex,
				burrow.Segment, burrow.Direction, burrow.LineHeading, intent,
				out var nextLine, out burrow.Segment, out burrow.T, out burrow.Direction,
				out var junctionPoint, out burrow.LineHeading))
				return BurrowTravel.LineEnd;

			var previousLineId = burrow.Line.ActorId ?? "?";
			burrow.Line = nextLine!;
			at = (Point2)junctionPoint;
			burrow.Blocked = false;
			if (BurrowTraceEnabled)
				TraceBurrow(
					$"line junction {previousLineId} -> {burrow.Line.ActorId ?? "?"} position={at} heading={burrow.LineHeading} remaining={remaining:0.##}");
			if (remaining <= 0)
				break;
		}
		return BurrowTravel.Moving;
	}

	// Off the end of its line and onto the surface there, if there is one.
	public bool AttachBurrow(ref Point2 at, ref BurrowCursor burrow, Vector2 intent)
	{
		if (!TryAttachBurrowSurface(at, burrow.LineHeading, intent, Pawn.BurrowAttachProbe,
			out var surfacePosition, out var surfaceHeading, out var surfaceSolidLeft, out var surfaceToward))
			return false;

		var exitedLineId = burrow.Line?.ActorId ?? "?";
		burrow.LineExit = burrow.Line;
		burrow.Line = null;
		at = surfacePosition;
		burrow.Heading = surfaceHeading;
		burrow.SolidLeft = surfaceSolidLeft;
		burrow.Toward = surfaceToward;
		burrow.Blocked = false;
		if (BurrowTraceEnabled)
			TraceBurrow(
				$"line attached to surface id={exitedLineId} position={at} heading={burrow.Heading} solidLeft={burrow.SolidLeft} normal={burrow.Toward}");
		return true;
	}

	internal void ReleaseBurrowLineExit(ref BurrowCursor burrow, Point2 at)
	{
		if (burrow.LineExit == null || burrow.LineExit.NearEndpoint(at, 4))
			return;
		if (BurrowTraceEnabled)
			TraceBurrow(
				$"line re-entry guard cleared id={burrow.LineExit.ActorId ?? "?"} position={at}");
		burrow.LineExit = null;
	}

	// Trace the route the burrow movement would take from an existing surface state, including
	// transfers onto authored BurrowLines before the boundary walker chooses its next solid edge.
	internal bool TraceBurrowPathFromState(Point2 vertex, Point2 heading, bool solidLeft, BurrowLine? exclude,
		float maxArcPerSide, List<Vector2> outPoints, Vector2 steeringIntent = default)
	{
		outPoints.Clear();
		surfaceTraceForward.Clear();
		surfaceTraceBack.Clear();
		TraceBurrowPathDirection(vertex, heading, solidLeft, exclude, maxArcPerSide, surfaceTraceForward, steeringIntent);
		TraceBurrowPathDirection(vertex, new Point2(-heading.X, -heading.Y), !solidLeft, exclude, maxArcPerSide, surfaceTraceBack);

		for (var i = surfaceTraceBack.Count - 1; i >= 1; i--) // skip shared start vertex
			outPoints.Add(surfaceTraceBack[i]);
		outPoints.AddRange(surfaceTraceForward);
		return outPoints.Count >= 2;
	}

	void TraceBurrowPathDirection(Point2 start, Point2 heading, bool solidLeft, BurrowLine? exclude,
		float maxArc, List<Vector2> into, Vector2 steeringIntent = default)
	{
		var position = start;
		var distance = 0f;
		into.Add(position);
		while (distance < maxArc)
		{
			if (TryEnterBurrowLine(position, heading, exclude, BurrowLineAttachDistance, steeringIntent,
				out var line, out var segment, out var t,
				out var direction, out var entryPoint, out _))
			{
				var entryDistance = Vector2.Distance(position, entryPoint);
				if (entryDistance > 0.001f)
					into.Add(entryPoint);
				line!.TraceDirection(segment, t, direction, MathF.Max(0, maxArc - distance - entryDistance), into,
					includeStart: false, steeringIntent: steeringIntent);
				return;
			}

			var next = PickBoundaryDir(position, heading, solidLeft);
			if (next == Point2.Zero)
				return;
			position += next;
			heading = next;
			distance++;
			into.Add(position);
		}
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
		=> TryEnterBurrowLine(position, travelHeading, exclude, maxDistance, Vector2.Zero,
			out line, out segment, out t, out direction, out point, out heading);

	public bool TryEnterBurrowLine(Point2 position, Vector2 travelHeading, BurrowLine? exclude, float maxDistance,
		Vector2 steeringIntent, out BurrowLine? line, out int segment, out float t, out int direction,
		out Vector2 point, out Vector2 heading)
	{
		line = null;
		segment = 0;
		t = 0;
		direction = 1;
		point = position;
		heading = travelHeading;
		var bestDistance = maxDistance * maxDistance;
		var bestHeadingScore = float.MinValue;
		travelHeading = travelHeading.LengthSquared() > 0 ? Vector2.Normalize(travelHeading) : Vector2.UnitX;
		steeringIntent = steeringIntent.LengthSquared() > 0 ? Vector2.Normalize(steeringIntent) : Vector2.Zero;

		foreach (var actor in Actors)
		{
			if (actor is not BurrowLine candidate || candidate == exclude || destroying.Contains(candidate) ||
				!candidate.TryGetEndpoint(position, maxDistance, out var candidateSegment, out var candidateT,
					out var candidateDirection, out var candidatePoint, out var candidateHeading))
				continue;

			var toEndpoint = candidatePoint - position;
			var distance = toEndpoint.LengthSquared();
			if (distance > 0.001f && Vector2.Dot(Vector2.Normalize(toEndpoint), travelHeading) < 0)
				continue;

			var headingScore = BurrowLineHeadingScore(candidateHeading, travelHeading, steeringIntent);
			if (distance > bestDistance + 0.001f ||
				MathF.Abs(distance - bestDistance) <= 0.001f && headingScore <= bestHeadingScore)
				continue;

			bestDistance = distance;
			bestHeadingScore = headingScore;
			line = candidate;
			segment = candidateSegment;
			t = candidateT;
			direction = candidateDirection;
			point = candidatePoint;
			heading = candidateHeading;
		}

		return line != null;
	}

	// Room seams do not necessarily join line endpoints exactly: each room can author its guide
	// a few pixels across the shared edge. Reconnect to the closest interior point on a destination
	// segment while preserving the direction of travel.
	internal bool TryAttachBurrowLineAcrossRoom(Point2 near, Vector2 travelHeading, float maxDistance,
		out BurrowLine? line, out int segment, out float t, out int direction, out Vector2 point,
		out Vector2 heading)
	{
		line = null;
		segment = 0;
		t = 0;
		direction = 1;
		point = near;
		heading = travelHeading;
		if (room == null)
			return false;

		travelHeading = travelHeading.LengthSquared() > 0 ? Vector2.Normalize(travelHeading) : Vector2.UnitX;
		var bestDistance = maxDistance * maxDistance;
		var bestAlignment = float.MinValue;
		foreach (var actor in Actors)
		{
			if (actor is not BurrowLine candidate || destroying.Contains(candidate))
				continue;

			for (var candidateSegment = 0; candidateSegment < candidate.Points.Length - 1; candidateSegment++)
			{
				var from = candidate.WorldPoint(candidateSegment);
				var delta = candidate.WorldPoint(candidateSegment + 1) - from;
				var lengthSquared = delta.LengthSquared();
				if (lengthSquared <= 0.001f)
					continue;

				var candidateT = Math.Clamp(Vector2.Dot(near - from, delta) / lengthSquared, 0, 1);
				var candidatePoint = from + delta * candidateT;
				var roundedPoint = candidatePoint.RoundToPoint2();
				if (!room.WorldBounds.Contains(roundedPoint))
					continue;

				var distance = Vector2.DistanceSquared(near, candidatePoint);
				if (distance > bestDistance + 0.001f)
					continue;

				var forward = Vector2.Normalize(delta);
				var candidateDirection = Vector2.Dot(forward, travelHeading) >= 0 ? 1 : -1;
				var candidateHeading = forward * candidateDirection;
				var alignment = Vector2.Dot(candidateHeading, travelHeading);
				if (alignment <= BurrowLineSteerThreshold ||
					MathF.Abs(distance - bestDistance) <= 0.001f && alignment <= bestAlignment)
					continue;

				bestDistance = distance;
				bestAlignment = alignment;
				line = candidate;
				segment = candidateSegment;
				t = candidateT;
				direction = candidateDirection;
				point = roundedPoint;
				heading = candidateHeading;
			}
		}

		if (BurrowTraceEnabled)
			TraceBurrow(line != null
				? $"room line attach id={line.ActorId ?? "?"} near={near} point={point} segment={segment} t={t:0.###} direction={direction} heading={heading}"
				: $"room line attach failed near={near} travel={travelHeading} reach={maxDistance:0.##}");
		return line != null;
	}

	internal bool BurrowLinePointConnected(BurrowLine source, int sourcePoint)
	{
		var point = source.WorldPoint(sourcePoint);
		var maxDistanceSquared = BurrowLine.JunctionDistance * BurrowLine.JunctionDistance;
		foreach (var actor in Actors)
		{
			if (actor is not BurrowLine candidate || candidate == source || destroying.Contains(candidate))
				continue;
			for (var i = 0; i < candidate.Points.Length; i++)
				if (Vector2.DistanceSquared(point, candidate.WorldPoint(i)) <= maxDistanceSquared)
					return true;
		}

		return false;
	}

	public bool TryContinueBurrowLine(BurrowLine current, int currentPoint, int incomingSegment, int incomingDirection,
		Vector2 travelHeading, Vector2 steeringIntent, out BurrowLine? line, out int segment, out float t,
		out int direction, out Vector2 point, out Vector2 heading)
	{
		line = null;
		segment = 0;
		t = 0;
		direction = 1;
		var junctionPosition = current.WorldPoint(currentPoint);
		point = junctionPosition;
		heading = travelHeading;
		var bestDistance = BurrowLine.JunctionDistance * BurrowLine.JunctionDistance;
		var bestHeadingScore = float.MinValue;
		travelHeading = travelHeading.LengthSquared() > 0 ? Vector2.Normalize(travelHeading) : Vector2.UnitX;
		steeringIntent = steeringIntent.LengthSquared() > 0 ? Vector2.Normalize(steeringIntent) : Vector2.Zero;

		foreach (var actor in Actors)
		{
			if (actor is not BurrowLine candidate || destroying.Contains(candidate))
				continue;
			for (var candidatePointIndex = 0; candidatePointIndex < candidate.Points.Length; candidatePointIndex++)
			{
				var candidatePoint = candidate.WorldPoint(candidatePointIndex);
				var distance = Vector2.DistanceSquared(junctionPosition, candidatePoint);
				if (distance > BurrowLine.JunctionDistance * BurrowLine.JunctionDistance)
					continue;

				if (candidatePointIndex < candidate.Points.Length - 1 &&
					(candidate != current || candidatePointIndex != incomingSegment || incomingDirection != -1))
					ConsiderBurrowLineRoute(candidate, candidatePointIndex, 0, 1, candidatePoint, distance,
						travelHeading, steeringIntent, ref line, ref segment, ref t, ref direction, ref point,
						ref heading, ref bestDistance, ref bestHeadingScore);
				if (candidatePointIndex > 0 &&
					(candidate != current || candidatePointIndex - 1 != incomingSegment || incomingDirection != 1))
					ConsiderBurrowLineRoute(candidate, candidatePointIndex - 1, 1, -1, candidatePoint, distance,
						travelHeading, steeringIntent, ref line, ref segment, ref t, ref direction, ref point,
						ref heading, ref bestDistance, ref bestHeadingScore);
			}
		}

		return line != null;
	}

	void ConsiderBurrowLineRoute(BurrowLine candidate, int candidateSegment, float candidateT, int candidateDirection,
		Vector2 candidatePoint, float distance, Vector2 travelHeading, Vector2 steeringIntent,
		ref BurrowLine? line, ref int segment, ref float t, ref int direction, ref Vector2 point,
		ref Vector2 heading, ref float bestDistance, ref float bestHeadingScore)
	{
		var candidateHeading = candidate.SegmentHeading(candidateSegment, candidateDirection);
		var headingScore = BurrowLineHeadingScore(candidateHeading, travelHeading, steeringIntent);
		if (headingScore < bestHeadingScore - 0.001f ||
			MathF.Abs(headingScore - bestHeadingScore) <= 0.001f && distance >= bestDistance)
			return;

		line = candidate;
		segment = candidateSegment;
		t = candidateT;
		direction = candidateDirection;
		point = candidatePoint;
		heading = candidateHeading;
		bestDistance = distance;
		bestHeadingScore = headingScore;
	}

	static float BurrowLineHeadingScore(Vector2 candidateHeading, Vector2 travelHeading, Vector2 steeringIntent)
	{
		var travelAlignment = Vector2.Dot(candidateHeading, travelHeading);
		steeringIntent = BurrowLineSteeringDirection(travelHeading, steeringIntent);
		if (steeringIntent == Vector2.Zero)
			return travelAlignment;

		var intentAlignment = Vector2.Dot(candidateHeading, steeringIntent);
		return intentAlignment > BurrowLineSteerThreshold
			? 4 + intentAlignment + travelAlignment * 0.1f
			: travelAlignment;
	}

	internal static Vector2 BurrowLineSteeringDirection(Vector2 travelHeading, Vector2 steeringIntent)
	{
		if (steeringIntent.LengthSquared() <= 0)
			return Vector2.Zero;
		travelHeading = travelHeading.LengthSquared() > 0 ? Vector2.Normalize(travelHeading) : Vector2.UnitX;
		steeringIntent = Vector2.Normalize(steeringIntent);
		// Keeping the travel direction held while adding a perpendicular direction means
		// "take that branch," not "keep going straight because the diagonal is tied."
		var turnIntent = steeringIntent - travelHeading * Vector2.Dot(steeringIntent, travelHeading);
		return turnIntent.LengthSquared() > BurrowLineSteerThreshold * BurrowLineSteerThreshold
			? Vector2.Normalize(turnIntent)
			: steeringIntent;
	}

	internal static bool BurrowLineHeadingFollowsSteering(Vector2 heading, Vector2 travelHeading, Vector2 steeringIntent)
		=> Vector2.Dot(heading, BurrowLineSteeringDirection(travelHeading, steeringIntent)) > BurrowLineSteerThreshold;

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
		if (JumpthruAt(solidCell) is not { } jumpthru)
			return false;
		if (airCell == solidCell + Point2.Up)
			return true;

		// A curved (per-pixel) jumpthru's top is a staircase, so its risers count too: any
		// edge beside or above a pixel on its top skin, never one below.
		return jumpthru.Hitbox.Shape == Hitbox.Shapes.Pixels && airCell.Y <= solidCell.Y &&
			!jumpthru.Hitbox.Contains(solidCell + Point2.Up - jumpthru.Position);
	}

	bool InRoom(Point2 cell) => room?.WorldBounds.Contains(cell) ?? false;

	// The one place two touching solids read apart: in a seam the walk sees either just the crusher's outline,
	// or everything but the crusher.
	Actor? seam;
	bool seamFollow;

	internal SeamWalk WalkingSeam(Actor? seam, bool follow) => new(this, seam, follow);

	internal readonly ref struct SeamWalk
	{
		readonly Game game;
		public SeamWalk(Game game, Actor? seam, bool follow) => (this.game, game.seam, game.seamFollow) = (game, seam, follow);
		public void Dispose() => game.seam = null;
	}

	internal static readonly Point2[] BurrowDebugDirections = [Point2.Right, Point2.Down, Point2.Left, Point2.Up];

	// Which steps out of the burrow point are walkable with the solid kept on the cursor's side (bit i =
	// BurrowDebugDirections[i]), seen through its seam. For the debug overlay.
	internal int BurrowEdges(Point2 vertex, in BurrowCursor burrow)
	{
		using var _ = WalkingSeam(burrow.Seam, burrow.SeamFollow);
		var mask = 0;
		for (var i = 0; i < BurrowDebugDirections.Length; i++)
		{
			var d = BurrowDebugDirections[i];
			var left = (Vector2)new Point2(d.Y, -d.X);
			var valid = left == burrow.Toward ? BoundaryEdgeValid(vertex, d, true)
				: -left == burrow.Toward ? BoundaryEdgeValid(vertex, d, false)
				: BoundaryEdgeValid(vertex, d, true) || BoundaryEdgeValid(vertex, d, false);
			if (valid)
				mask |= 1 << i;
		}
		return mask;
	}

	// The walk can go somewhere from here: ahead, or back the way it came (a reversal flips the hand with the
	// heading, keeping the solid on the same side).
	internal bool BurrowCanWalk(Point2 vertex, in BurrowCursor burrow)
	{
		using var _ = WalkingSeam(burrow.Seam, burrow.SeamFollow);
		return PickBoundaryDir(vertex, burrow.Heading, burrow.SolidLeft) != Point2.Zero ||
			PickBoundaryDir(vertex, -burrow.Heading, !burrow.SolidLeft) != Point2.Zero;
	}

	// No air on either side of the burrow point's face: something closed over it.
	internal bool Buried(Point2 vertex, Vector2 toward, out Point2 a, out Point2 b)
	{
		FaceCells(vertex, -toward, out a, out b);
		return SolidPixel(a) && SolidPixel(b);
	}

	// The two cells beside `vertex` on its `side`.
	static void FaceCells(Point2 vertex, Vector2 side, out Point2 a, out Point2 b)
	{
		var across = new Vector2(side.Y, -side.X) * 0.5f;
		a = CellAt(vertex + side * 0.5f + across);
		b = CellAt(vertex + side * 0.5f - across);
	}

	static bool Covers(Actor actor, Point2 cell) => actor.Hitbox.Contains(cell - actor.Position);

	// Which side of a crusher seam the burrow walks, picked fresh from the geometry around `vertex` every frame,
	// so crushers meeting, parting and carrying us can't leave a stale choice behind. Only while the walk is
	// buried -- no edge through the touching solids -- and a crusher is one of them: follow the crusher's own
	// outline when `intent` presses toward it (or, with no input, when we already were), otherwise walk the solid
	// it pinned us to. Our current hand is tried before the flipped one. False, with no seam, when nothing is
	// buried here or no choice gives the walk an edge.
	internal bool ResolveBurrowSeam(Point2 vertex, ref BurrowCursor burrow, Vector2 intent, float deadzone)
	{
		var previous = burrow;
		burrow.Seam = null;
		if (BurrowCanWalk(vertex, burrow))
			return false;

		FaceCells(vertex, burrow.Toward, out var ahead0, out var ahead1);
		FaceCells(vertex, -burrow.Toward, out var behind0, out var behind1);
		foreach (var actor in Actors)
		{
			if (actor is not Crusher crusher)
				continue;
			var ahead = Covers(crusher, ahead0) || Covers(crusher, ahead1);
			if (!ahead && !Covers(crusher, behind0) && !Covers(crusher, behind1))
				continue;

			var towardCrusher = ahead ? burrow.Toward : -burrow.Toward;
			var follow = intent != Vector2.Zero
				? Vector2.Dot(intent, towardCrusher) > deadzone
				: previous.Seam == crusher && previous.SeamFollow;
			foreach (var seamFollow in (ReadOnlySpan<bool>)[follow, !follow])
			foreach (var flip in (ReadOnlySpan<bool>)[false, true])
			{
				var candidate = burrow with
				{
					Seam = crusher,
					SeamFollow = seamFollow,
					Toward = flip ? -burrow.Toward : burrow.Toward,
					SolidLeft = flip != burrow.SolidLeft
				};
				if (BurrowCanWalk(vertex, candidate))
				{
					burrow = candidate;
					return true;
				}
			}
		}
		return false;
	}

	bool SolidPixel(Point2 cell)
	{
		if (seamFollow && seam != null)
			return seam.Hitbox.Contains(cell - seam.Position);

		var point = new Vector2(cell.X + 0.5f, cell.Y + 0.5f);
		if (RoomCollisionContains(point))
			return true;

		foreach (var other in Actors)
		{
			// ICarryable actors (crates, buds) are transparent to the burrow surface walk so
			// the player can travel underneath them; they're still solid for normal physics.
			if (other is Grid || other is ICarryable || other == seam || !other.Mask.Has(Actor.Masks.Solid))
				continue;

			if (other.Hitbox.Contains(cell - other.Position))
				return true;
		}

		return false;
	}

	internal bool BurrowLineSolidAt(Vector2 point) => SolidPixel(CellAt(point));

	// A jumpthru (one-way platform) cell — same test as SolidPixel but for the Jumpthru
	// mask. The burrow boundary walk uses this to ride the *top* of jumpthru runs.
	bool JumpthruSolid(Point2 cell) => JumpthruAt(cell) != null;

	Actor? JumpthruAt(Point2 cell)
	{
		if (seamFollow && seam != null)
			return null;
		foreach (var other in Actors)
			if (other is not Grid && other.Mask.Has(Actor.Masks.Jumpthru) && other.Hitbox.Contains(cell - other.Position))
				return other;
		return null;
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
		if (OverlapsRoomCollision(actor, bounds))
			return true;

		var actorRoom = CollisionRoomFor(actor);
		foreach (var other in Actors)
		{
			if (other == actor || other is Grid || destroying.Contains(other) ||
				other.Room != actorRoom || !other.Mask.Has(Actor.Masks.Solid))
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
		var actorRoom = CollisionRoomFor(actor);
		if (actorRoom != null)
		{
			foreach (var slope in actorRoom.SlopeTiles) if (OverlapsSlope(bounds, actorRoom, slope))
			{
				actorRoom.GetSlopeSurface(slope, out var left, out var right);
				var normal = SlopeNormal(left, right, slope.Fill);
				var correction = (Vector2)offset + SlopeCorrection(bounds, left, normal);
				hit = new SurfaceHit(SurfacePoint(bounds, normal), normal, distance, null, slope, null, correction);
				return true;
			}

			foreach (var rect in actorRoom.CollisionRects) if (bounds.Overlaps(rect))
			{
				var normal = AxisNormal(searchDirection);
				hit = new SurfaceHit(SurfacePoint(bounds, normal), normal, distance, null, null, rect, Vector2.Zero);
				return true;
			}
		}

		foreach (var other in Actors)
		{
			if (other == actor || other is Grid || destroying.Contains(other) ||
				other.Room != actorRoom || !other.Mask.Has(Actor.Masks.Solid | Actor.Masks.Jumpthru))
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
