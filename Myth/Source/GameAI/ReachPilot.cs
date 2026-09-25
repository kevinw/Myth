using Foster.Framework;

namespace Myth;

// How a pawn carries out a route: how long it gathers itself at a takeoff (WindupMin for a sure thing, up to
// WindupMax for the riskiest move), and how much longer for each time that move has already failed it.
public readonly record struct ReachManner(float WindupMin, float WindupMax, float MissHesitation);

// Drives a pawn along a ReachField using inputs only. It walks to the takeoff of the best move from where it
// stands, arriving at rest on the exact pixel that move was swept from, winds up, then replays the move's
// recorded inputs. It re-decides whenever a move ends or strays from its recorded path, so a knock or a missed
// landing is just a new place to start from. Phase, Windup, Risk, Heading, StuckTime and Misses say what it's
// up to, for the pawn to show however suits it.
public sealed class ReachPilot(Pawn owner, Func<ReachManner> manner)
{
	public enum Phases { Idle, Walking, Winding, Timing, Moving, Riding, Climbing, Swimming, Stuck }

	static int StrayTolerance => 3;
	static float ArriveGain => 8f;
	static float ArriveMinSpeed => 10f;
	static float CrawlSpeed => 28f;
	static float SwimStrokeInterval => 0.25f;
	static int RopeBoardTolerance => 3;
	static int PlatformTolerance => 3;
	static float MissRisk => 0.25f;
	static int LandingGrace => 6;
	static float TimingRecheck => 0.25f;

	public string Label { get; private set; } = "";
	public Phases Phase { get; private set; }
	// 0 to 1 through gathering itself for the next move; timing a platform, it reaches 1 as the platform comes round.
	public float Windup { get; private set; }
	// How unsure the next move is, 0 to 1: the field's read of it, plus each time it has already missed.
	public float Risk { get; private set; }
	// Where the next move lands.
	public Point2 Heading { get; private set; }
	public float StuckTime { get; private set; }
	public int Misses { get; private set; }
	float windupTime;
	float timingCheck;
	float timeToWindow;
	int move = -1;
	Point2 moveStart;
	float moveTime;
	int moveFrames;
	int lastFrame;
	bool toRope;
	ReachField.Node intended; // where the move should have ended up
	Point2 edgeStart;
	bool strayed;
	int bonk; // past here the recording no longer describes the path
	bool checkStray;
	bool release;   // a drop through a jumpthru or off a rope starts with down+jump
	bool released;
	float strokeTimer;

	public void Cancel() => move = -1;

	public PawnInput GetInput(ReachField field)
	{
		if (move >= 0 && Continue(field, out var input))
			return input;
		if (move >= 0 && !Arrived())
		{
			field.ReportMiss(edgeStart, move);
			Misses++;
		}
		move = -1;
		var decided = Decide(field, owner.Position);
		if (Phase is not (Phases.Winding or Phases.Timing))
		{
			windupTime = 0;
			Windup = 0;
		}
		StuckTime = Phase == Phases.Stuck ? StuckTime + owner.Time.Delta : 0;
		return decided;
	}

	PawnInput Decide(ReachField field, Point2 p)
	{
		if (owner.State == Pawn.States.Climb)
			return Climb(field, p);
		if (owner.InWater)
			return Swim(field, p, p);
		if (!owner.IsGrounded)
			return Air(field, p);
		if (owner.GroundActor() is MovingPlatform platform)
			return Ride(field, platform, p);
		var node = field.StandUnder(p, Game.TileSize * 2);
		if (node < 0)
		{
			Label = "no stand";
			Phase = Phases.Stuck;
			return default;
		}
		return Walk(field, node, p);
	}

	bool Continue(ReachField field, out PawnInput input)
	{
		input = default;
		var recorded = field.Moves[move];
		if (release)
		{
			release = false;
			released = true;
			input = new(0, 1, JumpPressed: true, JumpDown: false, DashPressed: false, AllowWallJump: false);
			return true;
		}
		if (released)
		{
			released = false;
			moveStart = owner.Position;
		}

		var k = (int)(moveTime / ReachMoves.Dt + 0.001f);
		// Headed for somewhere to stand, keep playing the move a few frames past its end until actually down.
		var landing = intended.Kind is ReachField.NodeKind.Stand or ReachField.NodeKind.Ride && !owner.IsGrounded && k < moveFrames + LandingGrace;
		if (k >= moveFrames && !landing || k >= recorded.Inputs.Length ||
			toRope && owner.State == Pawn.States.Climb ||
			k > 0 && owner.InWater ||
			k > recorded.GroundedFrames + 1 && owner.IsGrounded)
			return false;
		if (checkStray && k > 0 && (bonk == 0 || k < bonk))
		{
			var off = owner.Position - (moveStart + recorded.Offsets[k]);
			if (Math.Abs(off.X) > StrayTolerance || Math.Abs(off.Y) > StrayTolerance)
			{
				Label = "strayed";
				strayed = true;
				return false;
			}
		}

		// A slow frame can step over the frame the jump was pressed on; press it late rather than never.
		var pressed = false;
		for (var j = lastFrame + 1; j <= k; j++)
			pressed |= recorded.Inputs[j].JumpPressed;
		input = recorded.Inputs[k] with { JumpPressed = pressed, MoveY = toRope ? -1 : recorded.Inputs[k].MoveY };
		lastFrame = k;
		moveTime += owner.Time.Delta;
		return true;
	}

	PawnInput Start(ReachField field, ReachField.Edge edge, Point2 start, bool fromRope)
	{
		move = edge.Move;
		moveStart = start;
		moveTime = 0;
		lastFrame = -1;
		moveFrames = edge.Frames;
		intended = field.Nodes[edge.To];
		bonk = edge.Bonk;
		edgeStart = edge.Start;
		strayed = false;
		toRope = intended.Kind == ReachField.NodeKind.Rope;
		checkStray = !fromRope;
		release = field.Moves[move].Kind == ReachMove.Kinds.Fall && (fromRope || owner.IsGrounded);
		released = false;
		Label = field.Moves[move].Kind.ToString();
		Phase = Phases.Moving;
		Heading = intended.Position;
		return Continue(field, out var input) ? input : default;
	}

	bool Arrived()
	{
		var off = owner.Position - intended.Position;
		return !strayed && intended.Kind switch
		{
			ReachField.NodeKind.Stand => owner.IsGrounded && Math.Abs(off.X) <= Game.TileSize + 2 && Math.Abs(off.Y) <= 3,
			ReachField.NodeKind.Rope => owner.State == Pawn.States.Climb,
			ReachField.NodeKind.Ride => owner.IsGrounded && owner.GroundActor() is MovingPlatform,
			ReachField.NodeKind.Swim => owner.InWater,
			_ => !owner.IsGrounded && Math.Abs(off.X) <= 1 && Math.Abs(off.Y) <= 3,
		};
	}

	bool Settled(ReachField field, int node, out ReachField.Edge edge)
	{
		edge = default;
		var cost = field.Cost(node);
		Label = cost == 0 ? "at target" : float.IsFinite(cost) ? Label : "no path";
		if (cost == 0 || !float.IsFinite(cost) || field.BestEdge(node) is not (var e and >= 0))
		{
			Phase = cost == 0 ? Phases.Idle : Phases.Stuck;
			return true;
		}
		edge = field.Edges[e];
		return false;
	}

	// Walks the route's walking stretch in one go, stopping only where a move takes off.
	PawnInput Walk(ReachField field, int node, Point2 p)
	{
		var at = node;
		var target = field.Nodes[node].Position;
		var crawl = field.Nodes[node].Ducking;
		if (Settled(field, node, out var edge))
			return field.Cost(node) == 0 ? Arrive(p, target, crawl) : default;
		for (var steps = 0; steps < 64; steps++)
		{
			if (edge.Move >= 0)
			{
				var takeoff = field.Nodes[edge.From].Position;
				// X exact -- Arrive puts us on that pixel and it places the arc -- but the surface we stand on may
				// have drifted since the field last saw it.
				if (at == node && p.X == takeoff.X && Math.Abs(p.Y - takeoff.Y) <= ReachField.DriftTolerance &&
					owner.Velocity.X == 0)
					return WindUp(field, edge);
				target = takeoff;
				break;
			}
			var to = field.Nodes[edge.To];
			target = to.Position;
			crawl |= to.Ducking;
			at = edge.To;
			if (to.Kind != ReachField.NodeKind.Stand || field.Cost(at) == 0 || field.BestEdge(at) is not (var e and >= 0))
				break;
			edge = field.Edges[e];
		}
		Label = "walk";
		Phase = Phases.Walking;
		Heading = target;
		return Arrive(p, target, crawl);
	}

	// At a takeoff: gather ourselves for longer the riskier the move, then go. Boarding a moving platform, the
	// windup is paced to finish as the platform comes round, and the platform's timing wins either way.
	PawnInput WindUp(ReachField field, ReachField.Edge edge)
	{
		var missed = field.MissCount(edge.Start, edge.Move);
		var style = manner();
		Risk = MathF.Min(1, edge.Risk + missed * MissRisk);
		Heading = field.Nodes[edge.To].Position;
		var ready = float.Lerp(style.WindupMin, style.WindupMax, Risk) + missed * style.MissHesitation;
		windupTime += owner.Time.Delta;
		if (field.Nodes[edge.To] is { Kind: ReachField.NodeKind.Ride } boarding && !OnTime(field, boarding, edge.Frames))
		{
			if ((timingCheck -= owner.Time.Delta) <= 0)
			{
				timingCheck = TimingRecheck;
				timeToWindow = TimeUntilOnTime(field, boarding, edge.Frames);
			}
			else
				timeToWindow = MathF.Max(0, timeToWindow - owner.Time.Delta);
			Windup = ready > 0 ? Math.Clamp(1 - timeToWindow / ready, 0, 1) : 1;
			Label = "timing";
			Phase = Phases.Timing;
			return default;
		}
		timingCheck = 0;
		if (windupTime < ready && field.Nodes[edge.To].Kind != ReachField.NodeKind.Ride)
		{
			Windup = windupTime / ready;
			Label = "windup";
			Phase = Phases.Winding;
			return default;
		}
		return Start(field, edge, edge.Start, false);
	}

	// Will the platform be where this landing needs it if we go `after` seconds from now?
	static bool OnTime(ReachField field, ReachField.Node ride, int frames, float after = 0)
	{
		var off = field.Rides[ride.Side].Platform.PredictPosition(after + frames * ReachMoves.Dt) - field.RidePhase(ride);
		return Math.Abs(off.X) <= PlatformTolerance && Math.Abs(off.Y) <= PlatformTolerance;
	}

	static float TimeUntilOnTime(ReachField field, ReachField.Node ride, int frames)
	{
		const float Step = 1f / 30;
		var period = field.Rides[ride.Side].Platform.RoutePeriod;
		for (var after = 0f; after < period; after += Step)
			if (OnTime(field, ride, frames, after))
				return after;
		return period;
	}

	// Standing on a moving platform: keep to the route's spot on it (or walk along it) while it carries us, and
	// jump off when it reaches the phase the next move leaves from.
	PawnInput Ride(ReachField field, MovingPlatform platform, Point2 p)
	{
		if (field.RideNodeNear(platform, p) is not (var node and >= 0))
		{
			Label = "no ride";
			Phase = Phases.Stuck;
			return default;
		}
		var here = field.Nodes[node];
		var slot = here.Position.X - field.RidePhase(here).X;
		if (Settled(field, node, out var edge))
			return field.Cost(node) == 0 ? Arrive(p, platform.Position + new Point2(slot, 0), false) : default;
		if (edge.Move >= 0)
		{
			var off = platform.Position - field.RidePhase(here);
			if (Math.Abs(off.X) <= PlatformTolerance && Math.Abs(off.Y) <= PlatformTolerance && p.X - platform.Position.X == slot)
				return Start(field, edge, p, false);
			Label = "ride to jump";
			Phase = Phases.Riding;
			return Arrive(p, platform.Position + new Point2(slot, 0), false);
		}
		var to = field.Nodes[edge.To];
		Label = "ride";
		Phase = Phases.Riding;
		return Arrive(p, platform.Position + new Point2(to.Phase == here.Phase ? to.Position.X - field.RidePhase(to).X : slot, 0), false);
	}

	// Full speed, easing off to come to rest on the target pixel.
	static PawnInput Arrive(Point2 p, Point2 target, bool crawl)
	{
		var dx = target.X - p.X;
		var speed = Math.Clamp(Math.Abs(dx) * ArriveGain, ArriveMinSpeed, Pawn.MaxGroundSpeed);
		var moveX = dx == 0 ? 0 : Math.Sign(dx) * MathF.Min(1, speed / (crawl ? CrawlSpeed : Pawn.MaxGroundSpeed));
		return new(moveX, crawl ? 1 : 0, false, false, false);
	}

	// Airborne between moves: against a wall the route asks for, kick off it, slide down it or let go.
	PawnInput Air(ReachField field, Point2 p)
	{
		// Bobbing up out of the water: keep swimming.
		if (field.SwimNodeAt(p + new Point2(0, Game.TileSize)) >= 0)
			return Swim(field, p, p + new Point2(0, Game.TileSize));
		foreach (var side in (ReadOnlySpan<int>)[-1, 1])
		{
			if (!field.WallBeside(p, side) || field.WallNodeNear(p, side) is not (var wall and >= 0) || Settled(field, wall, out var edge))
				continue;
			if (edge.Move >= 0)
				return Start(field, edge, p, false);
			Label = "slide";
			Phase = Phases.Moving;
			return new(side, 0, false, false, false);
		}
		// A rope in reach: grab it rather than fall past (the route re-decides from it).
		if (field.RopeNodeNear(p, Game.TileSize * 2) >= 0)
		{
			Label = "grab";
			Phase = Phases.Moving;
			return new(0, -1, false, false, false);
		}
		Label = "airborne";
		Phase = Phases.Moving;
		return default;
	}

	PawnInput Climb(ReachField field, Point2 p)
	{
		if (field.RopeNodeNear(p, Game.TileSize * 2) is not (var node and >= 0) || Settled(field, node, out var edge))
			return default;
		var board = edge.Move >= 0 ? edge.Start : field.Nodes[edge.To].Position;
		if (edge.Move >= 0 && Math.Abs(board.Y - p.Y) <= RopeBoardTolerance)
			return Start(field, edge, p, true);
		Label = "climb";
		Phase = Phases.Climbing;
		return new(0, Math.Abs(board.Y - p.Y) <= 1 ? 0 : Math.Sign(board.Y - p.Y), false, false, false);
	}

	PawnInput Swim(ReachField field, Point2 p, Point2 at)
	{
		var node = field.SwimNodeAt(at);
		if (node < 0)
			node = field.NearestNode(at, Game.TileSize * 2);
		if (node < 0 || Settled(field, node, out var edge))
			return default;
		var to = field.Nodes[edge.To];
		var delta = to.Position - p;
		var crouch = (to.Ducking || field.Nodes[node].Ducking) && Math.Abs(delta.Y) <= 2; // along a low passage
		strokeTimer -= owner.Time.Delta;
		var stroke = delta.Y < -2 && strokeTimer <= 0;
		if (stroke)
			strokeTimer = SwimStrokeInterval;
		Label = "swim";
		Phase = Phases.Swimming;
		return new(Math.Clamp(delta.X / 8f, -1, 1), delta.Y > 2 || crouch ? 1 : 0, stroke && !crouch, stroke && !crouch, false);
	}
}
