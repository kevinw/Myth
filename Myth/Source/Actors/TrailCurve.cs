using System.Numerics;
using System.Runtime.InteropServices;
using Foster.Framework;

namespace Myth;

// A ribbon whose spine is the path a moving point has taken: whatever drives it calls MoveHead() with
// a world position each frame, and the body is the history behind it, resampled at an even arc
// length and trimmed to TrailLength. Nothing is simulated -- a creature that travels this way
// follows slopes, climbs walls and arches over gaps for free, because the body simply goes where
// the head went.
//
// Index 0 is the live head and higher indices are older, so the ribbon's base (the thick end of a
// taper, and the end Grow grows from) is the head: a serpent emerging from a hole comes out head
// first. Note the strip is drawn base-to-tip, so where a trail crosses itself the tail paints over
// the head; a dark edge stripe is what makes that overlap read.
//
// Driven by code, never authored, so deliberately without a [GameActor] of its own.
public class TrailCurve : CurveRibbon
{
	// Distance the head must travel before the path behind it gains a point. Smaller is smoother
	// and costs more spine.
	[I(1, 32)] public float SampleSpacing = 4;
	// Arc length of path kept behind the head.
	[I(8, 512)] public float TrailLength = 96;

	// How strongly the body holds the path the head took. At 1 it holds it exactly, the way a
	// snake's body follows its own track, and no simulation runs at all. Below 1 each point also
	// falls, collides and carries momentum, so the body sags where nothing holds it up, gets
	// knocked off its track when hit, and -- at 0 -- stops being a creature and becomes a rope
	// that collapses and drapes over the terrain.
	[InspectorGroup("Physics"), I(0, 1)] public float Grip = 1;
	[I(0, 2000)] public float Gravity = 260;
	// Pull toward the point's place on the track, in px/s^2 per px of error.
	[I(0, 400)] public float GripStiffness = 90;
	// Per-second velocity bleed; the spring needs more of it than a rope does or the body rings.
	[I(0, 40)] public float BodyDamping = 6;

	readonly List<Vector2> body = [];
	readonly List<Vector2> bodyPrev = [];
	float lastDelta = 1f / 60f;

	readonly List<Vector2> points = [];
	int version, builtVersion;

	public int PointCount => points.Count;
	public Vector2 Head => points.Count > 0 ? points[0] : (Vector2)Position;

	// The path's arc length, which settles at TrailLength once the head has travelled that far.
	public float Length
	{
		get
		{
			var total = 0f;
			for (var i = 1; i < points.Count; i++)
				total += Vector2.Distance(points[i - 1], points[i]);
			return total;
		}
	}

	public void Clear()
	{
		if (points.Count == 0 && body.Count == 0)
			return;
		points.Clear();
		ResetBody();
		version++;
	}

	void ResetBody()
	{
		body.Clear();
		bodyPrev.Clear();
	}

	// The point this far back along the path from the head.
	public Vector2 PathAt(float distance)
	{
		if (points.Count == 0)
			return Position;

		var traveled = 0f;
		for (var i = 1; i < points.Count; i++)
		{
			var step = Vector2.Distance(points[i - 1], points[i]);
			if (traveled + step >= distance)
				return step > 0.0001f ? Vector2.Lerp(points[i - 1], points[i], (distance - traveled) / step) : points[i];
			traveled += step;
		}
		return points[^1];
	}

	public override void Update()
	{
		SimulateBody();
		base.Update();
	}

	// Grip crossfades between the two: the spring toward the track scales up with it, gravity
	// scales down, so the balance point is where the body settles when nothing is under it.
	void SimulateBody()
	{
		if (Grip >= 1 || points.Count < 2 || Time.Delta <= 0)
		{
			if (body.Count > 0)
			{
				ResetBody();
				version++;
			}
			return;
		}

		var spacing = MathF.Max(0.5f, SampleSpacing);
		var wanted = Math.Clamp((int)(Length / spacing) + 1, 2, 512);
		while (body.Count > wanted)
		{
			body.RemoveAt(body.Count - 1);
			bodyPrev.RemoveAt(bodyPrev.Count - 1);
		}
		// New points start on the track, so a lengthening body extends along it rather than
		// whipping out from the head.
		while (body.Count < wanted)
		{
			var seed = PathAt(body.Count * spacing);
			body.Add(seed);
			bodyPrev.Add(seed);
		}

		for (var steps = new Verlet.Steps(Time.Delta, ref lastDelta); steps.Next(out var dt, out var scale);)
			SimulateBodyStep(dt, scale, spacing);
		version++;
	}

	void SimulateBodyStep(float dt, float displacementScale, float spacing)
	{
		var damp = Verlet.Damping(BodyDamping, dt);
		var gravity = new Vector2(0, Gravity * (1 - Grip));
		var stiffness = GripStiffness * Grip;

		// The head is driven outright -- it is wherever the AI put it.
		body[0] = bodyPrev[0] = Head;
		for (var i = 1; i < body.Count; i++)
		{
			var accel = gravity + (PathAt(i * spacing) - body[i]) * stiffness;
			Verlet.Integrate(ref CollectionsMarshal.AsSpan(body)[i], ref CollectionsMarshal.AsSpan(bodyPrev)[i],
				accel, dt, displacementScale, damp);
		}

		// The chain is driven at one end, so corrections only ever propagate outward from the
		// head: one forward pass places every point exactly a segment behind the one in front.
		// A symmetric relaxation would need as many iterations as there are links to do the same,
		// and with fewer it lags behind a fast head and stretches the body.
		Space(spacing);
		Collide();
		// Cancelling a move into a wall undoes the spacing that was just set, so re-space after.
		// Whatever the second pass pushes back into solid is resolved on the next step.
		Space(spacing);
	}

	// Places every point exactly a segment behind the one in front.
	void Space(float spacing)
	{
		for (var i = 1; i < body.Count; i++)
		{
			var delta = body[i] - body[i - 1];
			var distance = delta.Length();
			body[i] = body[i - 1] + (distance > 0.0001f ? delta / distance : Vector2.UnitY) * spacing;
		}
	}

	// One probe per point, cancelling a move into anything solid outright -- position and velocity
	// both -- which rests the body on what holds it up and lets a limp one pile up and drape.
	void Collide()
	{
		for (var i = 1; i < body.Count; i++)
		{
			var at = (Point2)body[i];
			var probe = new RectInt(at.X, at.Y, 1, 1);
			var solid = Game.OverlapsFirst(probe, Masks.Solid);
			if (Game.OverlapsRoomCollision(probe) || (solid != null && solid != this))
				body[i] = bodyPrev[i];
		}
	}

	// Call once per frame with the head's world position.
	public void MoveHead(Vector2 head)
	{
		if (points.Count > 0 && points[0] == head)
			return;

		if (points.Count < 2)
		{
			points.Clear();
			points.Add(head);
			points.Add(head);
		}

		var spacing = MathF.Max(0.5f, SampleSpacing);
		if (Vector2.Distance(head, points[1]) > TrailLength)
		{
			// A jump further than the body is long -- a teleport, a room change -- has no path
			// worth keeping, so the trail starts again at the head instead of streaking across it.
			// The simulated body goes with it: left where it was, the constraints would haul it
			// across the room and stretch every segment on the way.
			points.Clear();
			points.Add(head);
			points.Add(head);
			ResetBody();
		}

		// points[0] tracks the head exactly every frame, and points[1..] are samples an even
		// spacing apart laid *along the path travelled* rather than dropped wherever a frame
		// landed -- so a head moving 10px a frame leaves the same body as one crawling at 0.1px.
		for (var gap = Vector2.Distance(head, points[1]); gap >= spacing; gap -= spacing)
			points.Insert(1, Vector2.Lerp(points[1], head, spacing / gap));
		points[0] = head;

		Trim();
		version++;
	}

	// Drops whatever hangs past TrailLength, pulling the last kept point back to exactly that
	// length so the tail slides away instead of popping a sample at a time.
	void Trim()
	{
		var traveled = 0f;
		for (var i = 1; i < points.Count; i++)
		{
			var step = Vector2.Distance(points[i - 1], points[i]);
			traveled += step;
			if (traveled < TrailLength)
				continue;

			if (step > 0.0001f)
				points[i] = Vector2.Lerp(points[i], points[i - 1], (traveled - TrailLength) / step);
			points.RemoveRange(i + 1, points.Count - i - 1);
			return;
		}
	}

	// The path is already an even polyline, so there is no spline to build.
	protected override void PrepareSpine() { }

	protected override void AppendSpine(List<Vector2> into, float phase)
	{
		var origin = (Vector2)Position;
		// The simulated body when there is one, the bare track when Grip holds it there exactly.
		foreach (var point in body.Count >= 2 ? body : points)
			into.Add(point - origin);
	}

	// The spine is new every frame, so the collision shape is too.
	protected override bool UseCapsuleCollision => true;

	protected override bool SpineChanged => version != builtVersion;
	protected override void CaptureSpine() => builtVersion = version;
}
