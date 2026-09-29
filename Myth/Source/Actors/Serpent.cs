using System.Numerics;
using Foster.Framework;
using Kit;

namespace Myth;

// A creature whose whole body is the ribbon. The head walks the authored path and the body is the
// track it left, so the serpent follows slopes, climbs and crosses gaps without knowing they are
// there -- it only ever goes where the head went.
//
// Grip (inherited) decides how literally the body holds that track: at 1 it is a snake, below 1
// every point also falls and collides, so an unsupported span sags and a dead one collapses.
[GameActor(Icon = "wave-square", BoundsX = -4, BoundsY = -4, BoundsW = 8, BoundsH = 8,
	Tags = ActorTag.Enemy, ShowFacing = false, LivePositionEditable = true)]
public class Serpent : TrailCurve
{
	public static ContentRef<RibbonStyleContent> SerpentStyle => new("serpent");

	// Crawl hugs whatever solid it can reach, the same walk the player's burrow uses, so slopes
	// and corners come out identical to burrowing. Path walks the authored Points instead.
	public enum Motions { Crawl, Path }

	[InspectorGroup("Serpent")] public Motions Motion = Motions.Crawl;
	[I(4, 240)] public float Speed = 42;
	// Loop runs the path as a circuit; otherwise the head turns around at each end.
	public bool Loop = true;
	// Which side of its travel the solid is on -- the difference between orbiting an island
	// clockwise and anticlockwise, or crawling a floor rather than the ceiling above it.
	public bool SolidLeft;

	// A creature, so it stamps out of the lighting dither the way the other creatures do. The
	// default rule exempts Player and Enemy *masks*, and a ribbon's mask comes from its collision
	// mode -- Hazard, not Enemy -- so it would otherwise dither like scenery.
	public override bool LightingDithered => false;

	// Generous on purpose: a serpent is dropped roughly near the ground in the editor, not placed
	// against it the way a burrowing player already is.
	static float AttachProbe => 56f;

	// The furthest gap it will commit to crossing; 0 never leaps and it crawls down into pits
	// instead, following the boundary the whole way.
	[InspectorGroup("Leap"), I(0, 160)] public float MaxGap = 56;
	// How high the arc bows above the straight line between the lip and the landing.
	[I(0, 64)] public float LeapArc = 14;

	// The hold at the lip before it commits: the head rears, quivers and gapes, which is the
	// player's warning that a lunge is coming. An alarmed serpent spends less time deciding.
	[I(0, 2)] public float LeapWindup = 0.45f;
	// A leap is a lunge, faster than the crawl that led up to it.
	static float LeapSpeedup => 2.4f;

	// How much its pace wanders around Speed, and how quickly: surges, dawdles, near-stops.
	[InspectorGroup("Temperament"), I(0, 1)] public float Wander = 0.6f;
	[I(0, 4)] public float WanderRate = 0.7f;
	// Within this of the head the player is noticed: it freezes and hisses, then carries on
	// agitated -- quicker, twitchier, eye on the player -- until they have been gone a while.
	[I(0, 200)] public float NoticeRadius = 64;
	[I(0, 2)] public float StartlePause = 0.5f;
	static float AlarmSpeedup => 0.8f;
	static float AlarmRise => 3f;
	static float AlarmFall => 0.4f;

	// How far ahead to look for the ground running out, and the most pixels of surface one frame
	// may cover -- a long frame (loading, a breakpoint) would otherwise bank enough budget to
	// walk the head clean across the room in a single step.
	const int LookAhead = 6;
	const int MaxStepPerFrame = 12;
	const int MinGap = 6;
	// A landing may sit a little above or well below the lip it launched from.
	const int LandingRise = 12;
	const int LandingDrop = 28;

	Point2 crawlAt, crawlHeading;
#if DEBUG
	bool warnedUnattached;
#endif

	bool leaping;
	Vector2 leapFrom, leapControl, leapTo;
	float leapAt, leapSpan;
	int leapDirection;
	bool crawlSolidLeft, crawlAttached;
	// WalkSurface spends whole pixels, and at a walking speed a frame is worth less than one, so
	// the remainder is carried rather than truncated away -- otherwise the serpent never moves.
	float crawlBudget;

	// The head walks a smoothed route rather than the authored polyline: a trail records exactly
	// where the head went, so a hard corner in the path becomes a hard kink in the body.
	readonly List<SplineSample> route = [];
	Point2[] routeFrom = [];
	float routeLength;

	float cursor;
	int heading = 1;

	// Own clock, so two serpents placed side by side do not wander in step.
	float life;
	uint seed;
	// 0..1: how worked up it is about the player. Drives pace, jitter, windup, gape and gaze.
	float alarm;
	float startle, windup;
	bool noticed;
	Vector2? lookAt;

	public Serpent()
	{
		Style = SerpentStyle;
		Collision = RibbonCollision.Hazard;
		Width = 8;
		EndWidth = 0.3f;
		TrailLength = 220;
		SampleSpacing = 3;
	}

	public override void Added()
	{
		base.Added();
		cursor = 0;
		seed = (uint)Position.GetHashCode();
		life = alarm = startle = windup = 0;
		noticed = false;
		lookAt = null;
		BuildRoute();
		crawlAttached = false;
		crawlSolidLeft = SolidLeft;
		SeedBody();
	}

	bool Solid(Point2 at)
	{
		var probe = new RectInt(at.X, at.Y, 1, 1);
		var actor = Game.OverlapsFirst(probe, Masks.Solid);
		return Game.OverlapsRoomCollision(probe) || (actor != null && actor != this);
	}

	// Somewhere the head could stand: clear itself, with something solid immediately beneath.
	bool TryFindLanding(int x, int fromY, out Point2 landing)
	{
		for (var y = fromY - LandingRise; y <= fromY + LandingDrop; y++)
		{
			var at = new Point2(x, y);
			if (Solid(at) || !Solid(at + new Point2(0, 1)))
				continue;
			landing = at;
			return true;
		}
		landing = default;
		return false;
	}

	// Leaps only from a level run along the top of something: the surface normal points down into
	// solid, the heading is horizontal, and walking on would drop away. Anything else -- a wall, a
	// ceiling, a slope that merely steepens -- keeps crawling.
	bool TryBeginLeap()
	{
		if (MaxGap < MinGap || crawlHeading.Y != 0 || crawlHeading.X == 0)
			return false;
		if (Game.SurfaceNormal(crawlHeading, crawlSolidLeft).Y < 0.5f)
			return false;

		// A lip is the ground running out ahead. Asking the surface walk what it would do is the
		// tempting test and the wrong one: at a convex corner it carries a few pixels out over the
		// void and stops rather than turning down the face, so it never reports a descent.
		var direction = Math.Sign(crawlHeading.X);
		var lip = false;
		for (var d = 1; d <= LookAhead && !lip; d++)
			lip = !Solid(crawlAt + new Point2(direction * d, 1));
		if (!lip)
			return false;

		for (var gap = MinGap; gap <= (int)MaxGap; gap++)
		{
			if (!TryFindLanding(crawlAt.X + direction * gap, crawlAt.Y, out var landing))
				continue;

			// Launch from the true edge, not from wherever the look-ahead first noticed the drop,
			// or it takes off short and the gap reads wider than it is.
			var launch = crawlAt;
			while (Math.Abs(launch.X - crawlAt.X) < LookAhead && Solid(launch + new Point2(direction, 1)))
				launch.X += direction;

			leaping = true;
			leapDirection = direction;
			leapFrom = launch;
			leapTo = landing;
			leapAt = 0;
			leapSpan = Vector2.Distance(leapFrom, leapTo);
			leapControl = (leapFrom + leapTo) * 0.5f - Vector2.UnitY * (LeapArc + leapSpan * 0.15f);
			windup = WindupDuration;
			return true;
		}
		return false;
	}

	void Leap()
	{
		leapAt += Speed * LeapSpeedup * Time.Delta / MathF.Max(1, leapSpan);
		var t = MathF.Min(1, leapAt);
		MoveHead(Calc.Bezier(leapFrom, leapControl, leapTo, t));
		if (t < 1)
			return;

		leaping = false;
		crawlAttached = false;
		if (!TryAttach((Point2)leapTo))
			return;

		// Attaching derives the heading from the surface alone, which can face back the way it
		// came; carrying the leap's direction through keeps it travelling onward.
		if (Math.Sign(crawlHeading.X) == -leapDirection)
		{
			crawlHeading = new Point2(-crawlHeading.X, -crawlHeading.Y);
			crawlSolidLeft = !crawlSolidLeft;
		}
	}

	// A trail has no shape until the head has travelled, so a serpent that has just been placed
	// would have no body to draw -- and the editor renders room previews without ever running
	// Update, so there it would never get one. The body is laid out at spawn instead: back along
	// the surface it is about to crawl, or along the authored route when it has no surface.
	void SeedBody()
	{
		Clear();
		if (Motion == Motions.Crawl && TryAttach() && SeedAlongSurface())
			return;
		SeedAlongRoute();
	}

	bool SeedAlongSurface()
	{
		// Walk backwards from the attach point to find where the tail belongs, then crawl forward
		// from there, so the trail fills in along the real surface rather than a straight guess.
		var tail = crawlAt;
		var tailHeading = new Point2(-crawlHeading.X, -crawlHeading.Y);
		var tailSolidLeft = !crawlSolidLeft;
		if (Game.WalkSurface(ref tail, ref tailHeading, tailSolidLeft, (int)TrailLength) <= 0)
			return false;

		var at = tail;
		var heading = new Point2(-tailHeading.X, -tailHeading.Y);
		var solidLeft = !tailSolidLeft;
		MoveHead(at);
		for (var i = 0; i < (int)TrailLength; i++)
		{
			if (Game.WalkSurface(ref at, ref heading, solidLeft, 1) == 0)
				break;
			MoveHead(at);
		}

		crawlAt = at;
		crawlHeading = heading;
		crawlSolidLeft = solidLeft;
		return true;
	}

	void SeedAlongRoute()
	{
		if (route.Count == 0 || routeLength <= 0.001f)
		{
			MoveHead(Position);
			return;
		}

		var step = MathF.Max(1, SampleSpacing);
		var reach = MathF.Min(TrailLength, routeLength);
		for (var d = 0f; d <= reach; d += step)
			MoveHead(RouteAt(d));
		cursor = reach;
	}

	// Finds the nearest surface and faces along it. Retried each frame until it takes, so a
	// serpent dropped near a ledge in the editor settles onto it rather than sitting in the air.
	bool TryAttach() => TryAttach(crawlAttached ? crawlAt : Position);

	// Takes the point to probe from explicitly: after a leap the search has to start where it
	// landed, and clearing crawlAttached to force a fresh attach would otherwise send it back to
	// the position it was authored at.
	bool TryAttach(Point2 from)
	{
		var at = from;
		var toward = Vector2.UnitY;
		if (!Game.SnapToSurface(ref at, ref toward, AttachProbe))
			return false;

		crawlAt = at;
		crawlHeading = crawlSolidLeft
			? new Point2(-(int)MathF.Round(toward.Y), (int)MathF.Round(toward.X))
			: new Point2((int)MathF.Round(toward.Y), -(int)MathF.Round(toward.X));
		crawlAttached = true;
		return true;
	}

	void Crawl()
	{
		if (leaping)
		{
			if (windup > 0)
				windup -= Time.Delta;
			else
				Leap();
			return;
		}

		if (!crawlAttached && !TryAttach())
		{
#if DEBUG
			if (!warnedUnattached)
			{
				warnedUnattached = true;
				Console.WriteLine($"Serpent at {Position} found no surface within {AttachProbe}px; it will lie still until it does.");
			}
#endif
			return;
		}

		crawlBudget = MathF.Min(crawlBudget + Speed * Pace() * Time.Delta, MaxStepPerFrame);
		var pixels = (int)crawlBudget;
		if (pixels <= 0)
			return;
		crawlBudget -= pixels;

		// Checked before walking: one step further and the boundary has already turned down the
		// inside of the pit, and the lip -- along with the direction of travel -- is gone.
		if (TryBeginLeap())
			return;

		if (Game.WalkSurface(ref crawlAt, ref crawlHeading, crawlSolidLeft, pixels) == 0)
		{
			// The boundary ran out -- a room edge, or a surface that simply ends. Travelling back
			// the way it came puts the solid on the other hand, so both have to flip together.
			crawlHeading = new Point2(-crawlHeading.X, -crawlHeading.Y);
			crawlSolidLeft = !crawlSolidLeft;
		}
		MoveHead(crawlAt);
	}

	void BuildRoute()
	{
		routeFrom = [.. Points];
		route.Clear();
		routeLength = 0;
		if (Points.Length < 2)
			return;
		Ribbon.AppendSpline(route, Points, 6, 128);
		for (var i = 1; i < route.Count; i++)
			routeLength += Vector2.Distance(route[i - 1].Position, route[i].Position);
	}

	Vector2 RouteAt(float distance)
	{
		if (route.Count == 0)
			return Position;

		var traveled = 0f;
		for (var i = 1; i < route.Count; i++)
		{
			var step = Vector2.Distance(route[i - 1].Position, route[i].Position);
			if (traveled + step >= distance)
				return Position + (step > 0.0001f
					? Vector2.Lerp(route[i - 1].Position, route[i].Position, (distance - traveled) / step)
					: route[i].Position);
			traveled += step;
		}
		return Position + route[^1].Position;
	}

	// The ribbon tapers from the head back, which gives the body its shape but nothing to read as
	// a face. The head is drawn on top of it in profile: a skull and a snout along the direction of
	// travel, the jaw hinged on the belly side and a single eye on the back. Authorable, because
	// the body's look comes from the style's surface (or a SurfaceOverride) and the head has to be
	// matched to it by hand.
	[InspectorGroup("Head")] public Color HeadFill = new(0x5d8443);
	public Color HeadShade = new(0x31492e);
	public Color EyeFill = new(0xe8f0c0);
	public Color EyePupil = new(0x1a2416);
	public Color Tongue = new(0xc0404a);

	public override void Render(Batcher batcher)
	{
		base.Render(batcher);
		DrawHead(batcher);
	}

	// The side the belly faces: into whatever it crawls on -- a leap keeps the belly it launched
	// with -- or down, for a serpent walking a path.
	Vector2 Belly(Vector2 forward)
	{
		if (Motion == Motions.Crawl && crawlAttached)
			return crawlSolidLeft ? forward.TurnLeft() : forward.TurnRight();
		var down = forward.TurnRight();
		return down.Y >= 0 ? down : -down;
	}

	void DrawHead(Batcher batcher)
	{
		var forward = Head - PathAt(SampleSpacing * 2);
		if (forward.LengthSquared() < 0.0001f)
			return;
		forward = Vector2.Normalize(forward);
		var belly = Belly(forward);
		var radius = Width * 0.8f;

		// Rearing is shared by the leap's windup and the startle: the head lifts off its belly and
		// trembles, harder the closer the lunge is.
		var coil = leaping && windup > 0 ? 1 - windup / MathF.Max(0.01f, WindupDuration) : 0;
		var rear = MathF.Max(coil, startle / MathF.Max(0.01f, StartlePause));
		var tremble = MathF.Sin(life * 70) * coil * 0.7f;
		var at = Head - (Vector2)Position - belly * (radius * 0.45f * rear + tremble);

		var gape = MathF.Max(MathF.Max(coil, rear) * 0.7f, alarm * 0.12f);
		var hinge = at - forward * radius * 0.3f + belly * radius * 0.3f;
		var jaw = hinge + (forward * MathF.Cos(gape) + belly * MathF.Sin(gape)) * radius * 1.35f;
		var snout = at + forward * radius * 1.2f;

		batcher.Triangle(hinge, snout, jaw, EyePupil);
		batcher.Triangle(hinge + belly * radius * 0.35f, hinge - belly * radius * 0.1f, jaw, HeadShade);
		batcher.Circle(at, radius * 0.8f, 12, HeadShade);
		batcher.Circle(at + forward * radius * 0.7f, radius * 0.5f, 10, HeadShade);
		batcher.Circle(at - belly * radius * 0.12f, radius * 0.6f, 12, HeadFill);
		batcher.Circle(at + forward * radius * 0.7f - belly * radius * 0.08f, radius * 0.36f, 10, HeadFill);

		// Tasting the air: an alarmed serpent flicks its tongue, in short bursts, while its mouth is shut.
		if (alarm > 0.2f && gape < 0.3f && SmoothNoise.Signed(life * 6, seed ^ 0x85ebca6bu) > 0.3f)
		{
			var tip = snout + forward * radius * (0.6f + 0.5f * alarm);
			batcher.Line(snout, tip, 1, Tongue);
			batcher.Line(tip, tip + (forward - belly) * 1.5f, 1, Tongue);
			batcher.Line(tip, tip + (forward + belly) * 1.5f, 1, Tongue);
		}

		var gaze = lookAt is { } target && alarm > 0.05f && (target - Head).LengthSquared() > 1
			? Vector2.Normalize(Vector2.Lerp(forward, Vector2.Normalize(target - Head), alarm))
			: forward;
		DrawEye(batcher, at + forward * radius * 0.35f - belly * radius * 0.3f, gaze, radius * (1 + alarm * 0.25f));
	}

	// At this scale an eye is two or three pixels across, so it is a dark bead with one lit pixel
	// in it; the glint sits toward whatever the eye is looking at.
	void DrawEye(Batcher batcher, Vector2 centre, Vector2 gaze, float radius)
	{
		batcher.Circle(centre, MathF.Max(1.2f, radius * 0.24f), 8, EyePupil);
		var glint = centre + gaze * 0.6f - new Vector2(0.5f);
		batcher.Rect(new Rect(glint.X, glint.Y, 1, 1), EyeFill);
	}

	float WindupDuration => LeapWindup * (1 - alarm * 0.6f);

	// A multiplier on Speed. Two octaves of noise so it both drifts and flickers; alarm speeds the
	// noise up as well as the serpent, so an agitated one reads as twitchy rather than just fast.
	float Pace()
	{
		if (startle > 0)
			return 0;
		var t = life * WanderRate * (1 + alarm);
		var wander = SmoothNoise.Signed(t, seed) * 0.75f + SmoothNoise.Signed(t * 3.1f, seed ^ 0x9e3779b9u) * 0.25f;
		return MathF.Max(0.05f, 1 + Wander * wander) * (1 + alarm * AlarmSpeedup);
	}

	void Sense()
	{
		life += Time.Delta;
		startle = MathF.Max(0, startle - Time.Delta);

		var player = Game.GetFirst<Player>();
		lookAt = player != null ? (player.Hitbox.Rect + player.Position).CenterF : null;
		var distance = lookAt is { } target ? Vector2.Distance(target, Head) : float.MaxValue;
		// Forgets further out than it notices, so a player hovering at the edge does not
		// startle it every other frame.
		var near = distance < NoticeRadius * (noticed ? 1.4f : 1);
		if (near && !noticed && !leaping)
			startle = StartlePause;
		noticed = near;
		alarm = Calc.Approach(alarm, near ? 1 : 0, Time.Delta * (near ? AlarmRise : AlarmFall));
	}

	public override void Update()
	{
		if (Time.Delta > 0)
			Sense();

		if (Motion == Motions.Crawl)
		{
			if (Time.Delta > 0)
				Crawl();
			base.Update();
			return;
		}

		if (!routeFrom.AsSpan().SequenceEqual(Points))
			BuildRoute();

		var path = routeLength;
		if (path > 0.001f && Time.Delta > 0)
		{
			cursor += Speed * Pace() * Time.Delta * heading;
			if (Loop)
			{
				// A circuit whose ends do not meet teleports the head; TrailCurve restarts the
				// body there rather than streaking it across the room.
				while (cursor > path) cursor -= path;
				while (cursor < 0) cursor += path;
			}
			else if (cursor > path)
			{
				cursor = path;
				heading = -1;
			}
			else if (cursor < 0)
			{
				cursor = 0;
				heading = 1;
			}
			MoveHead(RouteAt(cursor));
		}
		base.Update();
	}
}
