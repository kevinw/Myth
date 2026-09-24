using Foster.Framework;

namespace Myth;

// Drives a pawn along a ReachField using inputs only. It walks to the takeoff of the best move from where it
// stands, arriving at rest on the exact pixel that move was swept from, then replays the move's recorded inputs.
// It re-decides whenever a move ends or strays from its recorded path, so a knock or a missed landing is just
// a new place to start from.
public sealed class ReachPilot(Pawn owner)
{
	static int StrayTolerance => 3;
	static float ArriveGain => 8f;
	static float ArriveMinSpeed => 10f;
	static float CrawlSpeed => 28f;
	static float SwimStrokeInterval => 0.25f;
	static int RopeBoardTolerance => 3;

	public string Label { get; private set; } = "";
	int move = -1;
	Point2 moveStart;
	float moveTime;
	int moveFrames;
	int lastFrame;
	bool toRope;
	ReachField.Node intended; // where the move should have ended up
	Point2 edgeStart;
	bool strayed;
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
			field.ReportMiss(edgeStart, move);
		move = -1;
		var p = owner.Position;
		if (owner.State == Pawn.States.Climb)
			return Climb(field, p);
		if (owner.InWater)
			return Swim(field, p, p);
		if (!owner.IsGrounded)
			return Air(field, p);
		var node = field.StandNear(p, Game.TileSize * 2, out _);
		if (node < 0)
		{
			Label = "no stand";
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
		if (k >= moveFrames || k >= recorded.Inputs.Length ||
			toRope && owner.State == Pawn.States.Climb ||
			k > 0 && owner.InWater ||
			k > recorded.GroundedFrames + 1 && owner.IsGrounded)
			return false;
		if (checkStray && k > 0)
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
		edgeStart = edge.Start;
		strayed = false;
		toRope = intended.Kind == ReachField.NodeKind.Rope;
		checkStray = !fromRope;
		release = field.Moves[move].Kind == ReachMove.Kinds.Fall && (fromRope || owner.IsGrounded);
		released = false;
		Label = field.Moves[move].Kind.ToString();
		return Continue(field, out var input) ? input : default;
	}

	bool Arrived()
	{
		var off = owner.Position - intended.Position;
		return !strayed && intended.Kind switch
		{
			ReachField.NodeKind.Stand => owner.IsGrounded && Math.Abs(off.X) <= Game.TileSize + 2 && Math.Abs(off.Y) <= 3,
			ReachField.NodeKind.Rope => owner.State == Pawn.States.Climb,
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
			return true;
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
				if (at == node && p == takeoff && owner.Velocity.X == 0)
					return Start(field, edge, edge.Start, false);
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
		return Arrive(p, target, crawl);
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
			return new(side, 0, false, false, false);
		}
		// A rope in reach: grab it rather than fall past (the route re-decides from it).
		if (field.RopeNodeNear(p, Game.TileSize * 2) >= 0)
		{
			Label = "grab";
			return new(0, -1, false, false, false);
		}
		Label = "airborne";
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
		return new(Math.Clamp(delta.X / 8f, -1, 1), delta.Y > 2 || crouch ? 1 : 0, stroke && !crouch, stroke && !crouch, false);
	}
}
