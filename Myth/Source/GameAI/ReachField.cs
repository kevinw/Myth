using System.Diagnostics;
using System.Numerics;
using Foster.Framework;

namespace Myth;

// Where a pawn can get to in the room as it was when rasterized. The room collision (tiles, solid and jumpthru
// actors, ribbon pixel masks) is rasterized into SummedMasks; standing spots are found on that raster; recorded
// ReachMoves are swept from each spot to see where they end up; then a Dijkstra run backwards from the target
// gives every spot its cost to reach it. ReachSense keeps one current as the room changes.
public sealed class ReachField(ReachBody body, List<ReachMove> moves, Dictionary<(Point2 Start, int Move), int> misses)
{
	public enum NodeKind { Stand, Wall, Swim, Rope, Ride }
	// Side: which way a Stand's lip drops off, the wall a Wall node touches, a Rope node's rope, or a Ride node's
	// ride. Phase: where along its route the ride's platform is.
	public readonly record struct Node(Point2 Position, NodeKind Kind, int Side = 0, bool Ducking = false, int Phase = 0);
	// A moving platform sampled along its route. Its top at each phase is a row of Ride nodes; boarding one costs
	// the typical Wait for the platform to come round. A weight-activated one only boards where it rests.
	internal sealed record Ride(MovingPlatform Platform, List<Point2> Phases, int RestPhase, float Wait);
	// Move indexes Moves, swept from Start for Frames; -1 walks, crawls, climbs or swims straight to To. Bonk is
	// the frame the move hits its head, after which it falls from there instead of following its recording. Risk,
	// 0 to 1, is how much could go wrong: a narrow landing, a head bonk, a long drop, a moving target.
	public readonly record struct Edge(int From, int To, int Move, Point2 Start, int Frames, float Cost, int Bonk = 0, float Risk = 0);

	enum Hit { None, Invalid, Land, Wall, Water, Dead }
	struct Sweep
	{
		public Hit Hit;
		public Point2 At;
		public int Frame, Side, RopeNode, RopeFrame, TouchFrame, Bonk;
	}

	enum Step { Walked, Off, Blocked }
	// A pixel of a burrow's path: where the burrow point is, the surface's normal into the solid, which way it's
	// travelling and how fast it gets going there, and whether it's on a line.
	readonly record struct Dug(Point2 At, Vector2 Toward, Vector2 Heading, float Cruise, bool OnLine);
	record struct DigFrame(int At, float Speed, float Remainder, float Time);

	readonly RectInt Body = body.Stand;
	readonly RectInt DuckBody = body.Duck;
	readonly Point2 BodyCenter = new(0, body.Stand.Top / 2); // where Actor checks for water
	static float JumpPenalty => 0.1f;
	static float FallPenalty => 0.05f;
	static float RopePenalty => 0.2f;
	// A landing that misses when the arc runs a pixel off (the pawn's sub-pixel remainder) is a gamble.
	static float KnifeEdgePenalty => 1f;
	// Each time a move is tried from a spot and doesn't arrive, it costs this much more (for this room visit).
	static float MissPenalty => 1.5f;
	static float KnifeEdgeRisk => 0.5f;
	static float SlideOffRisk => 0.3f;
	static float BonkRisk => 0.2f;
	static float BoardingRisk => 0.3f;
	// A drop past DropRiskStart px adds risk at this much per px.
	static float DropRiskStart => 32f;
	static float DropRiskPerPx => 1 / 96f;
	static float WalkSpeed => Pawn.MaxGroundSpeed;
	static float CrawlSpeed => 28f;
	static float SwimSpeed => Pawn.MaxGroundSpeed * Pawn.WaterHorizontalSpeedMultiplier;
	static float SwimExitCost => 0.4f;
	static float ClimbSpeed => 40f;
	static float SlideSpeed => 80f;
	static float RopeGrabRadius => 7f; // the pawn grabs within 9px; the margin covers the rope's sway
	static int RopeGraceFrames => 15;
	static int MaxNodes => 6000;
	const int RasterMargin = 4; // tiles
	// The room can shift under a pawn between builds -- a surface that grows, a door that moves -- so this much
	// vertical drift still counts as standing in the same place, both for finding the stand it is on and for
	// reaching a takeoff. Every other localizer already forgives drift; StandNear walks exact pixels.
	public static int DriftTolerance => 3;
	static int TouchSearchRadius => 200;
	// How far the target has to move before a re-solve: further while it's in the air, where solving searches moves.
	static int ResolveDistance => 4;
	static int AirborneResolveDistance => 12;
	static int RidePhaseSpacing => 12;
	static int BoardReach => 160;
	static float BurrowPenalty => 0.3f;
	// A burrow that spends its whole timer is this risky.
	static float BurrowRisk => 0.6f;
	static float MantleRisk => 0.2f;
	// Let go this many frames before the burrow's timer would do it for us.
	static int BurrowMarginFrames => 3;
	static int BurrowPopFrames => 50;
	static int BurrowCeilingDrop => 64;
	static int BurrowMouthReach => Game.TileSize * 3;
	static int BurrowLinePixels => 1500;
	static int BurrowTraceSlice => 32;
	static readonly bool LogBuilds = Environment.GetEnvironmentVariable("LOG_REACH") == "1";

	public readonly List<Node> Nodes = [];
	public readonly List<Edge> Edges = [];
	public List<ReachMove> Moves => moves;
	public RectInt? Target { get; private set; }
	public float Cost(int node) => cost[node];
	public int BestEdge(int node) => best[node];

	readonly SummedMask solid = new(), jumpthru = new(), water = new();
	int ropes; // a signature of the room's ropes, which aren't in the raster
	bool solved;
	RectInt bounds;
	int roomBottom;
	readonly Dictionary<Point2, int> standAt = [];
	readonly Dictionary<Point2, int> swimAt = [];
	readonly Dictionary<(int X, int Y, int Side), int> wallAt = [];
	readonly List<int> ropeNodes = [];
	readonly List<(Vector2 Point, int Node)> ropePoints = [];
	RectInt ropeReach; // every rope point plus the grab radius, so a sweep clear of it skips the rope search
	internal readonly List<Ride> Rides = [];
	readonly Dictionary<(int Ride, int Phase, int Slot), int> rideAt = [];
	RectInt ghost; // a platform's footprint at one phase, solid while sweeping for landings on it
	readonly Dictionary<int, int> edgeTo = []; // cheapest edge so far from the node being expanded, by destination
	readonly List<int> runups = [.. moves.Where(move => move.Runup > 0).Select(move => move.Runup).Distinct()];
	readonly ReachMove fall = moves.First(move => move is { Kind: ReachMove.Kinds.Fall, Steer: 0 }); // how a bonked head comes back down
	float[] cost = [];
	int[] best = [];
	int[] touchFrame = []; // > 0 where the target is reached partway through the node's best move
	int[] incomingStart = [];
	int[] incoming = [];
	int[] fill = [];
	readonly bool burrows = body.Abilities.Has(PawnAbilities.Burrow);
	// A full jump in place, back down to the ground it left: how a burrow dives in.
	readonly int hopFrames = moves.Where(move => move is { Kind: ReachMove.Kinds.Jump, Dir: 0, Steer: 0 })
		.Max(move => Array.FindIndex(move.Offsets, move.GroundedFrames + 2, offset => offset.Y >= 0));
	// How a body in the air gathers speed to one side.
	readonly ReachMove driftLeft = moves.First(move => move is { Kind: ReachMove.Kinds.Fall, Steer: -1 });
	readonly ReachMove driftRight = moves.First(move => move is { Kind: ReachMove.Kinds.Fall, Steer: 1 });
	Game game = null!;
	float burrowTime;
	readonly List<Dug> dug = [];
	bool dugReversed;
	// Stands beside the open end of a BurrowLine, and the way to it.
	readonly HashSet<(int Node, int Dir)> mouths = [];
	bool solvedMidMove; // nothing stood in the last target, so it was reached partway through moves
	readonly PriorityQueue<int, float> queue = new();
	int sweeps;

	public int MissCount(Point2 start, int move) => misses.GetValueOrDefault((start, move));

	public void ReportMiss(Point2 start, int move)
	{
		misses[(start, move)] = misses.GetValueOrDefault((start, move)) + 1;
		solved = false;
	}

	// Following a moving target only re-solves once it has moved a few px.
	public void SolveFor(RectInt target)
	{
		var tolerance = solvedMidMove ? AirborneResolveDistance : ResolveDistance;
		if (solved && Target is { } solvedFor && solvedFor.Size == target.Size &&
			Math.Abs(solvedFor.X - target.X) < tolerance && Math.Abs(solvedFor.Y - target.Y) < tolerance)
			return;
		Solve(target);
		Target = target;
		solved = true;
	}

	public bool SameAs(ReachField other)
		=> ropes == other.ropes && solid.SamePixels(other.solid) && jumpthru.SamePixels(other.jumpthru) && water.SamePixels(other.water);

	// Lays the pixels down; Build sums them.
	public void Rasterize(Game game, Room room)
	{
		// Room collision clamps queries outside the room to its edge tiles, so the raster runs a margin past
		// the edges with them repeated: standing on top of the room's top row is real ground.
		var roomBounds = room.WorldBounds;
		var rasterBounds = roomBounds.Inflate(RasterMargin * Game.TileSize);
		solid.Reset(rasterBounds);
		jumpthru.Reset(rasterBounds);
		water.Reset(rasterBounds);
		for (var ty = -RasterMargin; ty < room.Size.Y + RasterMargin; ty++)
		for (var tx = -RasterMargin; tx < room.Size.X + RasterMargin; tx++)
		{
			var tile = new Point2(tx, ty);
			var clamped = new Point2(Math.Clamp(tx, 0, room.Size.X - 1), Math.Clamp(ty, 0, room.Size.Y - 1));
			var rect = new RectInt(roomBounds.TopLeft + tile * Game.TileSize, Game.TileSize, Game.TileSize);
			if (room.SolidTiles.Length > 0 && room.SolidTiles[clamped.Y * room.Size.X + clamped.X])
				solid.Fill(rect);
			else if (room.TryGetSlopeTile(clamped, out _))
				for (var y = rect.Top; y < rect.Bottom; y++)
				for (var x = rect.Left; x < rect.Right; x++)
					if (Game.OverlapsRoomCollision(room, new RectInt(x, y, 1, 1)))
						solid[new(x, y)] = true;
			if (room.IsWaterTile(tile))
				water.Fill(rect);
		}

		// Things that walk or ride around (pawns, enemies, moving platforms) aren't terrain: stamping them
		// would rebuild the field every refresh.
		foreach (var actor in game.Actors)
		{
			if (actor is Grid or Pawn or MovingPlatform || actor.Tags.Has(ActorTag.Enemy) ||
				actor.Room != null && actor.Room != room || game.destroying.Contains(actor))
				continue;
			var layer = actor.Mask.Has(Actor.Masks.Solid) ? solid : actor.Mask.Has(Actor.Masks.Jumpthru) ? jumpthru : null;
			if (layer == null)
				continue;
			var rect = actor.Hitbox.Rect + actor.Position;
			if (actor.Hitbox.Shape == Hitbox.Shapes.Rect)
				layer.Fill(rect);
			else if (actor.Hitbox.Shape == Hitbox.Shapes.Pixels)
				for (var y = rect.Top; y < rect.Bottom; y++)
				for (var x = rect.Left; x < rect.Right; x++)
					if (actor.Hitbox.Contains(new Point2(x, y) - actor.Position))
						layer[new(x, y)] = true;
		}

		ropes = RopeSignature(game);
		this.game = game;
		burrowTime = Pawn.BurrowDurationFor(game);
		bounds = solid.Bounds;
		roomBottom = room.WorldBounds.Bottom;
	}

	static int RopeSignature(Game game)
	{
		var hash = new HashCode();
		foreach (var actor in game.Actors)
			if (actor is Rope rope)
			{
				hash.Add(rope.Broken);
				hash.Add(rope.GrabPoints.Length);
			}
			else if (actor is MovingPlatform { WaitsForWeight: true, Parked: true } platform)
				hash.Add(platform.Position);
			else if (actor is BurrowLine line)
			{
				hash.Add(line.Position);
				hash.Add(line.Points.Length);
			}
		hash.Add(game.PickupCount(PickupKind.BurrowUpgrade));
		return hash.ToHashCode();
	}

	// Builds a few ms at a time (budgetMs per step) so a rebuild never hitches a frame.
	public IEnumerable<bool> Build(Game game, double budgetMs)
	{
		var buildStart = Stopwatch.GetTimestamp();
		var slice = buildStart;
		var longestSlice = 0.0;
		foreach (var _ in Steps(game))
			if (Stopwatch.GetElapsedTime(slice).TotalMilliseconds is var sliced && sliced > budgetMs)
			{
				longestSlice = Math.Max(longestSlice, sliced);
				yield return false;
				slice = Stopwatch.GetTimestamp();
			}

		if (incomingStart.Length < Nodes.Count + 1)
			(incomingStart, fill) = (new int[Nodes.Count + 1], new int[Nodes.Count]);
		if (incoming.Length < Edges.Count)
			incoming = new int[Edges.Count];
		Array.Clear(incomingStart, 0, Nodes.Count + 1);
		foreach (var edge in Edges)
			incomingStart[edge.To + 1]++;
		for (var i = 0; i < Nodes.Count; i++)
			incomingStart[i + 1] += incomingStart[i];
		Array.Copy(incomingStart, fill, Nodes.Count);
		for (var e = 0; e < Edges.Count; e++)
			incoming[fill[Edges[e].To]++] = e;
		solved = false;
		longestSlice = Math.Max(longestSlice, Stopwatch.GetElapsedTime(slice).TotalMilliseconds);
		if (LogBuilds)
			Console.WriteLine($"[Reach] {Nodes.Count} nodes, {Edges.Count} edges, {sweeps} sweeps, " +
				$"{Stopwatch.GetElapsedTime(buildStart).TotalMilliseconds:0.0}ms start to finish, longest slice {longestSlice:0.0}ms");
	}

	// A build in pieces, each small enough that stopping after any one of them is quick.
	IEnumerable<bool> Steps(Game game)
	{
		// Summing the raster costs more than laying it down and only a build reads the sums, so a refresh that
		// finds the room unchanged (most of them) stops at Rasterize and never pays for this.
		solid.Commit();
		yield return false;
		jumpthru.Commit();
		yield return false;
		water.Commit();
		yield return false;
		foreach (var step in Prepare(game))
			yield return step;
		// Ahead of the rest, which goes on to expand the wall nodes these pop out against.
		if (burrows)
			foreach (var step in Burrows())
				yield return step;
		for (var i = 0; i < Nodes.Count; i++)
		{
			Expand(i);
			for (var m = 0; m < Moves.Count; m++)
			{
				ExpandMove(i, m);
				yield return false;
			}
		}
		for (var ride = 0; ride < Rides.Count; ride++)
		for (var phase = 0; phase < Rides[ride].Phases.Count; phase++)
		{
			if (Rides[ride].RestPhase is var rest and >= 0 && phase != rest)
				continue;
			for (var from = 0; from < Nodes.Count; from++)
			{
				Board(ride, phase, from);
				yield return false;
			}
		}
		for (var e = 0; e < Edges.Count; e++)
		{
			if (Edges[e] is { Move: >= 0 } edge && !Moves[edge.Move].Burrows && Nodes[edge.To].Kind == NodeKind.Stand && Margins(edge) is var (knife, tooShort))
			{
				if (tooShort)
					Edges[e] = edge with { Cost = edge.Cost + KnifeEdgePenalty * 2, Risk = 1 };
				else if (knife)
					Edges[e] = edge with { Cost = edge.Cost + KnifeEdgePenalty, Risk = MathF.Min(1, edge.Risk + KnifeEdgeRisk) };
			}
			yield return false;
		}
	}

	// Finds the spots worth standing, swimming, climbing and riding on, yielding between columns of the room
	// and between spots so a build can stop partway.
	IEnumerable<bool> Prepare(Game game)
	{
		Nodes.Clear();
		Edges.Clear();
		standAt.Clear();
		swimAt.Clear();
		wallAt.Clear();
		ropeNodes.Clear();
		ropePoints.Clear();
		ropeReach = default;
		mouths.Clear();
		sweeps = 0;

		for (var ty = bounds.Top; ty < bounds.Bottom; ty += Game.TileSize)
		for (var tx = bounds.Left; tx < bounds.Right; tx += Game.TileSize)
		{
			var feet = new Point2(tx + Game.TileSize / 2, ty + Game.TileSize);
			if (!water[feet + BodyCenter])
				continue;
			// A pawn only crouches in water standing on the bottom, so a cell too low to swim through upright needs floor.
			var ducking = Solid(Body + feet);
			if (!ducking || !Solid(DuckBody + feet) && Grounded(feet, DuckBody))
			{
				swimAt[new(tx, ty)] = Nodes.Count;
				Nodes.Add(new(feet, NodeKind.Swim, Ducking: ducking));
			}
		}

		yield return false;

		var ropeId = 0;
		foreach (var actor in game.Actors)
		{
			if (actor is not Rope rope || rope.Broken)
				continue;
			ropeId++;
			var points = rope.GrabPoints;
			for (var i = 0; i < points.Length; i++)
			{
				var feet = points[i].RoundToPoint2() - BodyCenter;
				if (i % 2 == 0 && bounds.Contains(feet) && !Solid(Body + feet))
				{
					ropeNodes.Add(Nodes.Count);
					Nodes.Add(new(feet, NodeKind.Rope, ropeId));
				}
				if (ropeNodes.Count > 0 && Nodes[ropeNodes[^1]].Side == ropeId)
				{
					var grab = new RectInt(points[i].RoundToPoint2(), Point2.Zero).Inflate((int)RopeGrabRadius + 1);
					ropeReach = ropePoints.Count == 0 ? grab : ropeReach.Conflate(grab);
					ropePoints.Add((points[i], ropeNodes[^1]));
				}
			}
		}

		yield return false;

		Rides.Clear();
		rideAt.Clear();
		foreach (var actor in game.Actors)
		{
			if (actor is not MovingPlatform { Visible: true } platform || !platform.Mask.Has(Actor.Masks.Solid) ||
				platform.Room != null && platform.Room != game.room)
				continue;
			var phases = new List<Point2>();
			platform.RoutePoints(phases, RidePhaseSpacing);
			var rest = platform.WaitsForWeight ? NearestPhase(phases, platform.Position) : -1;
			var ride = Rides.Count;
			Rides.Add(new(platform, phases, rest, platform.WaitsForWeight ? 0 : platform.RoutePeriod / 2));
			for (var phase = 0; phase < phases.Count; phase++)
				for (var slot = 0; slot * Game.TileSize + Game.TileSize <= platform.Size.X; slot++)
				{
					var feet = phases[phase] + new Point2(slot * Game.TileSize + Game.TileSize / 2, 0);
					if (Solid(Body + feet) || Nodes.Count >= MaxNodes)
						continue;
					rideAt[(ride, phase, slot)] = Nodes.Count;
					Nodes.Add(new(feet, NodeKind.Ride, ride, Phase: phase));
				}
		}

		yield return false;

		for (var x = bounds.Left + Game.TileSize / 2; x < bounds.Right; x += Game.TileSize)
		{
			for (var y = bounds.Top + 1; y <= bounds.Bottom; y++)
				if (TryStand(new(x, y), out var ducking))
					AddStand(new(x, y), 0, ducking);
			yield return false;
		}

		// Lips, where a walk-off or a max-distance jump leaves, and the spots a runup to each lip starts from.
		for (int i = 0, sampled = Nodes.Count; i < sampled; i++)
		{
			yield return false;
			if (Nodes[i] is not { Kind: NodeKind.Stand } stand)
				continue;
			foreach (var dir in (ReadOnlySpan<int>)[-1, 1])
			{
				var p = stand.Position;
				var crawl = stand.Ducking;
				for (var n = 0; n < Game.TileSize; n++)
				{
					var from = p;
					var wasCrawling = crawl;
					var step = WalkStep(ref p, dir, ref crawl);
					if (step == Step.Off)
						AddLip(from, dir, wasCrawling);
					if (step != Step.Walked || crawl != stand.Ducking || standAt.ContainsKey(p))
						break;
				}
			}
		}

		if (!burrows)
			yield break;
		foreach (var actor in game.Actors)
		{
			if (actor is not BurrowLine { Points.Length: > 1 } line || game.destroying.Contains(line))
				continue;
			foreach (var end in (ReadOnlySpan<int>)[0, line.Points.Length - 1])
			{
				if (line.IsDoorPoint(end) || line.IsSwitchPoint(end) || game.BurrowLinePointConnected(line, end))
					continue;
				var mouth = line.WorldPoint(end).RoundToPoint2();
				foreach (var dir in (ReadOnlySpan<int>)[-1, 1])
				{
					var nearest = -1;
					for (var i = 0; i < Nodes.Count; i++)
						if (Nodes[i] is { Kind: NodeKind.Stand, Ducking: false } stand && Math.Abs(stand.Position.Y - mouth.Y) <= 4 &&
							(mouth.X - stand.Position.X) * dir is > 0 and var away && away <= BurrowMouthReach &&
							(nearest < 0 || away < (mouth.X - Nodes[nearest].Position.X) * dir))
							nearest = i;
					if (nearest >= 0)
						mouths.Add((nearest, dir));
				}
			}
			yield return false;
		}
	}

	void AddLip(Point2 lip, int dir, bool ducking)
	{
		var index = AddStand(lip, dir, ducking);
		if (index < 0 || ducking)
			return;
		foreach (var runup in runups)
		{
			var p = lip;
			var crawl = false;
			var flat = true;
			for (var n = 0; n < runup && flat; n++)
				flat = WalkStep(ref p, -dir, ref crawl) == Step.Walked && p.Y == lip.Y && !crawl;
			if (flat)
				AddStand(p, 0, false);
		}
	}

	int AddStand(Point2 p, int side, bool ducking)
	{
		if (standAt.TryGetValue(p, out var existing))
		{
			if (side != 0)
				Nodes[existing] = Nodes[existing] with { Side = side };
			return existing;
		}
		if (Nodes.Count >= MaxNodes)
			return -1;
		standAt[p] = Nodes.Count;
		Nodes.Add(new(p, NodeKind.Stand, side, ducking));
		return Nodes.Count - 1;
	}

	// One node's own edges: the walks, slides, climbs and swims that don't replay a recorded move. Replaying the
	// moves is ExpandMove, one at a time, so a build can stop between them.
	void Expand(int index)
	{
		edgeTo.Clear();
		var node = Nodes[index];
		var p = node.Position;
		switch (node.Kind)
		{
			case NodeKind.Stand:
				foreach (var dir in (ReadOnlySpan<int>)[-1, 1])
					WalkEdge(index, dir);
				break;

			case NodeKind.Wall:
				Slide(index);
				break;

			case NodeKind.Rope:
				foreach (var neighbor in (ReadOnlySpan<int>)[index - 1, index + 1])
					if (neighbor >= 0 && neighbor < Nodes.Count && Nodes[neighbor] is { Kind: NodeKind.Rope } other && other.Side == node.Side)
						AddEdge(index, neighbor, -1, p, 0, Vector2.Distance(p, other.Position) / ClimbSpeed);
				break;

			case NodeKind.Ride:
				var ride = Rides[node.Side];
				var slot = SlotOf(node);
				foreach (var dir in (ReadOnlySpan<int>)[-1, 1])
					if (rideAt.TryGetValue((node.Side, node.Phase, slot + dir), out var beside))
						AddEdge(index, beside, -1, p, 0, Game.TileSize / WalkSpeed);
				// Riding on: a back-and-forth route goes both ways, a circle onward; a weight-activated one only out.
				var count = ride.Phases.Count;
				foreach (var step in (ReadOnlySpan<int>)[1, -1])
				{
					if (step < 0 && (ride.Platform.Circles || ride.RestPhase >= 0))
						continue;
					var next = ride.Platform.Circles ? (node.Phase + step + count) % count : node.Phase + step;
					if (next >= 0 && next < count && rideAt.TryGetValue((node.Side, next, slot), out var onward))
						AddEdge(index, onward, -1, p, 0, Vector2.Distance(ride.Phases[node.Phase], ride.Phases[next]) / ride.Platform.Speed);
				}
				break;

			case NodeKind.Swim:
				var tile = TileOf(p - new Point2(0, 1));
				for (var dy = -1; dy <= 1; dy++)
				for (var dx = -1; dx <= 1; dx++)
				{
					if (dx == 0 && dy == 0 || !swimAt.TryGetValue(tile + new Point2(dx, dy) * Game.TileSize, out var to))
						continue;
					if (dx != 0 && dy != 0 && (!swimAt.ContainsKey(tile + new Point2(dx, 0) * Game.TileSize) || !swimAt.ContainsKey(tile + new Point2(0, dy) * Game.TileSize)))
						continue;
					AddEdge(index, to, -1, p, 0, Vector2.Distance(p, Nodes[to].Position) / SwimSpeed);
				}
				if (swimAt.ContainsKey(tile - new Point2(0, Game.TileSize)))
					break;
				for (var i = 0; i < Nodes.Count; i++)
				{
					var delta = Nodes[i].Position - p;
					if (Nodes[i].Kind == NodeKind.Stand && Math.Abs(delta.X) <= 16 && delta.Y is >= -24 and <= 8 && ClearPath(p, Nodes[i].Position))
						AddEdge(index, i, -1, p, 0, SwimExitCost);
				}
				break;
		}
	}

	// Where one recorded move takes this node, for the moves its kind can make.
	void ExpandMove(int index, int m)
	{
		var node = Nodes[index];
		var p = node.Position;
		var move = Moves[m];
		switch (node.Kind)
		{
			case NodeKind.Stand when node.Ducking == move.Crawl:
				if (move.Kind is ReachMove.Kinds.Jump or ReachMove.Kinds.WalkOff)
					TryMove(index, m, p, false);
				// Standing on nothing but a jumpthru, a fall drops through it.
				else if (move.Kind == ReachMove.Kinds.Fall && !Solid(new RectInt(p.X + Body.Left, p.Y, Body.Width, 1)))
					TryMove(index, m, p + new Point2(0, 2), false);
				break;

			case NodeKind.Wall when move.Kind == ReachMove.Kinds.Kick && move.Dir == node.Side ||
				move.Kind == ReachMove.Kinds.Fall && move.Steer != node.Side:
				TryMove(index, m, p, false);
				break;

			case NodeKind.Rope when move is { Kind: ReachMove.Kinds.Jump, Runup: 0 } or { Kind: ReachMove.Kinds.Fall }:
				TryMove(index, m, p, true);
				break;

			// Off a platform: jumps that take off straight away, so the platform's carry doesn't matter.
			case NodeKind.Ride when move is { Kind: ReachMove.Kinds.Jump, GroundedFrames: 0 }:
				TryMove(index, m, p, false);
				break;
		}
	}

	// Moves from one nearby spot that land on the platform's top at this phase.
	void Board(int ride, int phase, int from)
	{
		var node = Nodes[from];
		var platform = Rides[ride].Platform;
		var top = new RectInt(Rides[ride].Phases[phase], platform.Size);
		if (!top.Inflate(BoardReach).Contains(node.Position) || node.Kind is NodeKind.Ride or NodeKind.Swim || node.Ducking)
			return;

		ghost = top;
		edgeTo.Clear();
		for (var m = 0; m < Moves.Count; m++)
		{
			var move = Moves[m];
			var fits = node.Kind switch
			{
				NodeKind.Stand => move.Kind is ReachMove.Kinds.Jump or ReachMove.Kinds.WalkOff && !move.Crawl,
				NodeKind.Wall => move.Kind == ReachMove.Kinds.Kick && move.Dir == node.Side,
				_ => move is { Kind: ReachMove.Kinds.Jump, GroundedFrames: 0 },
			};
			if (!fits || !(move.Reach + node.Position).Overlaps(ghost))
				continue;
			var sweep = SweepMove(move, node.Position, node.Kind == NodeKind.Rope, null);
			if (sweep.Hit != Hit.Land || !new RectInt(sweep.At.X + Body.Left, sweep.At.Y, Body.Width, 1).Overlaps(ghost))
				continue;
			var slot = Math.Clamp((sweep.At.X - ghost.X) / Game.TileSize, 0, platform.Size.X / Game.TileSize - 1);
			if (rideAt.TryGetValue((ride, phase, slot), out var to))
				AddEdge(from, to, m, node.Position, sweep.Frame, sweep.Frame * ReachMoves.Dt + JumpPenalty + Rides[ride].Wait, sweep.Bonk, BoardingRisk);
		}
		ghost = default;
	}

	// The path a burrow takes from a dive at these feet holding dir, and steer until it has turned that way, a
	// pixel at a time by the burrow's own travel. It ends where the burrow would have to stop or pop out: a dead
	// end, a switch, or a line that comes out in the open.
	IEnumerable<bool> TraceBurrow(Point2 feet, int dir, int steer)
	{
		dug.Clear();
		var intent = new Vector2(dir, steer);
		var (at, burrow) = (feet, new BurrowCursor { Toward = Vector2.UnitY });
		if (!game.SnapToSurface(ref at, ref burrow.Toward, Pawn.BurrowAttachProbe))
			yield break;
		burrow.Heading = new((int)MathF.Round(burrow.Toward.Y), -(int)MathF.Round(burrow.Toward.X));
		burrow.SolidLeft = dugReversed = dir * burrow.Heading.X < 0;
		if (dugReversed)
			burrow.Heading = new(-burrow.Heading.X, -burrow.Heading.Y);
		dug.Add(new(at, burrow.Toward, burrow.Travel, burrow.CruiseSpeed(false), false));
		for (var surface = Pawn.BurrowMaxSpeed * burrowTime + Game.TileSize; surface > 0 && dug.Count < BurrowLinePixels;)
		{
			game.BurrowTraceQuiet = true;
			var travel = game.TravelBurrow(ref at, ref burrow, 1, intent, false, out _);
			var moved = travel == BurrowTravel.Moving || travel == BurrowTravel.LineEnd && game.AttachBurrow(ref at, ref burrow, intent);
			game.ReleaseBurrowLineExit(ref burrow, at);
			game.BurrowTraceQuiet = false;
			if (!moved)
				yield break;
			if (burrow.Line == null)
				surface--;
			dug.Add(new(at, burrow.Toward, burrow.Travel, burrow.CruiseSpeed(false), burrow.Line != null));
			if (burrow.Travel.Y * steer > 0.5f)
				intent.Y = 0;
			if (dug.Count % BurrowTraceSlice == 0)
				yield return false;
		}
	}

	// A frame of travel along the traced path, as Pawn.BurrowState paces it: easing up to the speed it cruises
	// at there or braking to a stop, the timer running only off the lines. False once the path or the
	// timer runs out.
	bool Dig(ref DigFrame frame, bool pressing)
	{
		var target = pressing ? dug[frame.At].Cruise : 0;
		frame.Speed = Calc.Approach(frame.Speed, target, (target > frame.Speed ? Pawn.BurrowAccel : Pawn.BurrowFriction) * ReachMoves.Dt);
		frame.Remainder += frame.Speed * ReachMoves.Dt;
		var pixels = (int)frame.Remainder;
		frame.Remainder -= pixels;
		frame.At += pixels;
		if (frame.At >= dug.Count)
		{
			frame.At = dug.Count - 1;
			return false;
		}
		if (!dug[frame.At].OnLine)
			frame.Time -= ReachMoves.Dt;
		return frame.Time > 0;
	}

	// The burrows worth a look: up a wall or over a lip within a tile of a stand, or into a line's mouth. A burrow
	// dives into what's under the middle of the feet, so a lip's own stand, hanging over it, has none.
	IEnumerable<bool> Burrows()
	{
		for (int index = 0, stands = Nodes.Count; index < stands; index++)
		{
			if (Nodes[index] is not { Kind: NodeKind.Stand, Ducking: false, Position: var p } || !Solid(new RectInt(p.X, p.Y, 1, 1)))
				continue;
			for (var dir = -1; dir <= 1; dir += 2)
			{
				var (q, crawl, walked) = (p, false, true);
				for (var n = 0; n < Game.TileSize && walked; n++)
					walked = WalkStep(ref q, dir, ref crawl) == Step.Walked;
				var mouth = mouths.Contains((index, dir));
				for (var steer = mouth ? -1 : 0; steer <= (mouth ? 1 : 0) && (mouth || !walked); steer++)
					foreach (var step in BurrowFrom(index, dir, steer))
						yield return step;
			}
		}
	}

	// Where a burrow from this stand gets to, holding dir and steer: each frame along its path it could let go
	// partway up a wall (a Rise), or stop and pop out onto the floor there or down from the ceiling (a Dig).
	IEnumerable<bool> BurrowFrom(int index, int dir, int steer)
	{
		edgeTo.Clear();
		foreach (var step in TraceBurrow(Nodes[index].Position, dir, steer))
			yield return step;
		if (dug.Count < 2)
			yield break;
		var rise = Moves.FindIndex(move => move.Kind == ReachMove.Kinds.Rise && move.Dir == dir && move.Steer == steer);
		var dig = Moves.FindIndex(move => move.Kind == ReachMove.Kinds.Dig && move.Dir == dir && move.Steer == steer);
		var frame = new DigFrame(0, 0, 0, burrowTime - BurrowMarginFrames * ReachMoves.Dt);
		// What the pilot sees as it decides a frame's input: where the frame before left the burrow.
		var seen = dug[0].At;
		// Turning round to head the other way takes the first frame.
		for (var frames = dugReversed ? 2 : 1; ; frames++)
		{
			var (stop, stopFrames) = (frame, frames - 1);
			if (!Dig(ref frame, true))
				yield break;
			var here = dug[frame.At];
			var cost = (hopFrames + frames) * ReachMoves.Dt + BurrowPenalty;
			var risk = BurrowRisk * (1 - frame.Time / burrowTime);
			if (frame.At != stop.At && !here.OnLine && here.Toward.X == dir && here.Heading.Y < 0)
				RiseFrom(index, rise, here.At - new Point2(dir > 0 ? Body.Right : Body.Left, 0), dir, seen, cost, risk);

			var stopped = true;
			for (; stopped && stop.Speed > 0; stopFrames++)
				stopped = Dig(ref stop, false);
			var rest = dug[stop.At];
			if (stopped && stop.At > 0 && !rest.OnLine && stop.Time > ReachMoves.Dt)
			{
				var to = rest.Toward.Y > 0.5f ? StandAt(rest.At) : rest.Toward.Y < -0.5f ? DropStand(rest.At) : -1;
				if (to >= 0 && to != index)
					AddEdge(index, to, dig, seen, stopFrames, (hopFrames + stopFrames + BurrowPopFrames) * ReachMoves.Dt + BurrowPenalty, 0, risk);
			}
			seen = here.At;
			yield return false;
		}
	}

	// Popped out partway up a wall, holding into it: up to the wall node where the launch tops out (or bonks, or
	// runs out of wall), and on over the wall's top if the launch clears it.
	void RiseFrom(int index, int move, Point2 feet, int side, Point2 start, float cost, float risk)
	{
		if (Solid(Body + feet) || !WallBeside(feet, side))
			return;
		var rise = Moves[move].Offsets;
		var (top, k) = (feet, 1);
		for (; k < rise.Length && !Solid(Body + feet + rise[k]) && WallBeside(feet + rise[k], side); k++)
			top = feet + rise[k];
		if (top != feet && WallNode(top, side) is var wall and >= 0)
			AddEdge(index, wall, move, start, k - 1, cost + (k - 1) * ReachMoves.Dt, 0, risk);
		// Only a mantle that still lands when the pop comes a couple of px early or late.
		if (k < rise.Length && Mantle(move, feet, side, out var landing, out var frames) &&
			Mantle(move, feet + new Point2(0, 2), side, out _, out _) && Mantle(move, feet - new Point2(0, 2), side, out _, out _) &&
			StandAt(landing) is var to and >= 0)
			AddEdge(index, to, move, start, frames, cost + frames * ReachMoves.Dt, 0, MathF.Min(1, risk + MantleRisk));
	}

	// The launch held into a wall it clears the top of: up as the Rise recorded, then down as a fall, drifting
	// over the top from the frame the wall stops being in the way. False if it comes back down the wall, or
	// lands still sliding toward an edge.
	bool Mantle(int move, Point2 feet, int side, out Point2 landing, out int frames)
	{
		var rise = Moves[move].Offsets;
		var drift = (side < 0 ? driftLeft : driftRight).Offsets;
		var (p, freeFrom, free) = (feet, feet.X, 0);
		landing = default;
		for (frames = 1; frames < rise.Length + fall.Offsets.Length - 1; frames++)
		{
			for (var x = freeFrom + drift[Math.Min(free + 1, drift.Length - 1)].X; p.X != x; p.X += side)
				if (Solid(Body + p + new Point2(side, 0)))
				{
					(freeFrom, free) = (p.X, -1);
					break;
				}
			free++;
			var y = frames < rise.Length ? feet.Y + rise[frames].Y : feet.Y + rise[^1].Y + fall.Offsets[frames - rise.Length + 1].Y;
			for (var dy = Math.Sign(y - p.Y); p.Y != y; p.Y += dy)
				if (dy > 0 && Grounded(p, Body))
					break;
				else if (Solid(Body + p + new Point2(0, dy)))
					return false;
			if (frames < rise.Length)
				continue;
			if (p.X == feet.X)
				return false;
			if (Grounded(p, Body))
			{
				landing = p;
				free = Math.Clamp(free, 1, drift.Length - 1);
				var speed = (drift[free].X - drift[free - 1].X) / ReachMoves.Dt;
				var crawl = false;
				for (var n = (int)(speed * speed / 4000) + 2; n > 0; n--)
					if (WalkStep(ref p, side, ref crawl) == Step.Off)
						return false;
				return true;
			}
		}
		return false;
	}

	// The stand a pawn put down here is on: one a short walk along the surface, or a new one right here.
	int StandAt(Point2 p)
		=> !TryStand(p, out var ducking) ? -1 : StandNear(p, Game.TileSize, out _) is var near and >= 0 ? near : AddStand(p, 0, ducking);

	// Popped out of a ceiling: straight down to the floor below, crouched if that's all the room there is.
	int DropStand(Point2 ceiling)
	{
		for (var drop = DuckBody.Height; drop <= BurrowCeilingDrop; drop++)
			if (Solid(DuckBody + ceiling + new Point2(0, drop)))
				return -1;
			else if (StandAt(ceiling + new Point2(0, drop)) is var stand and >= 0)
				return stand;
		return -1;
	}

	static int NearestPhase(List<Point2> phases, Point2 at)
	{
		var nearest = 0;
		for (var i = 1; i < phases.Count; i++)
			if ((phases[i] - at).LengthSquared() < (phases[nearest] - at).LengthSquared())
				nearest = i;
		return nearest;
	}

	int SlotOf(Node node) => (node.Position.X - Rides[node.Side].Phases[node.Phase].X) / Game.TileSize;

	// Where a Ride node's platform has to be for it: its top-left at that phase.
	public Point2 RidePhase(Node node) => Rides[node.Side].Phases[node.Phase];

	// The Ride node for feet standing on a platform, at whichever phase it's nearest now.
	internal int RideNodeNear(MovingPlatform platform, Point2 feet)
	{
		var ride = Rides.FindIndex(ride => ride.Platform == platform);
		if (ride < 0)
			return -1;
		var phase = NearestPhase(Rides[ride].Phases, platform.Position);
		var slot = Math.Clamp((feet.X - platform.Position.X) / Game.TileSize, 0, platform.Size.X / Game.TileSize - 1);
		foreach (var nudge in (ReadOnlySpan<int>)[0, -1, 1])
			if (rideAt.TryGetValue((ride, phase, slot + nudge), out var node))
				return node;
		return -1;
	}

	void WalkEdge(int index, int dir)
	{
		var p = Nodes[index].Position;
		var crawl = Nodes[index].Ducking;
		var crawled = crawl;
		for (var n = 1; n <= Game.TileSize * 2; n++)
		{
			if (WalkStep(ref p, dir, ref crawl) != Step.Walked)
				return;
			crawled |= crawl;
			var speed = crawled ? CrawlSpeed : WalkSpeed;
			if (water[p + BodyCenter])
			{
				if (SwimNodeAt(p) is var swim and >= 0)
					AddEdge(index, swim, -1, Nodes[index].Position, 0, n / speed);
				return;
			}
			if (standAt.TryGetValue(p, out var to))
			{
				AddEdge(index, to, -1, Nodes[index].Position, 0, n / speed);
				return;
			}
		}
	}

	// A wall node slides down its wall a tile at a time, ending on whatever floor it reaches.
	void Slide(int index)
	{
		var node = Nodes[index];
		var p = node.Position;
		for (var n = 1; n <= Game.TileSize; n++)
		{
			if (Grounded(p, Body))
			{
				if (LandingStand(p, out var walk) is var landed and >= 0)
					AddEdge(index, landed, -1, node.Position, 0, n / SlideSpeed + walk);
				return;
			}
			var below = p + Point2.Down;
			if (Solid(Body + below) || !WallBeside(below, node.Side) || water[below + BodyCenter])
				return;
			p = below;
		}
		if (WallNode(p, node.Side) is var lower and >= 0)
			AddEdge(index, lower, -1, node.Position, 0, Game.TileSize / SlideSpeed);
	}

	void TryMove(int index, int moveIndex, Point2 start, bool fromRope)
	{
		var move = Moves[moveIndex];
		var sweep = SweepMove(move, start, fromRope, null);
		var penalty = move.Kind is ReachMove.Kinds.Jump or ReachMove.Kinds.Kick ? JumpPenalty : FallPenalty;
		if (sweep.RopeNode >= 0 && !(Nodes[index].Kind == NodeKind.Rope && Nodes[sweep.RopeNode].Side == Nodes[index].Side))
			AddEdge(index, sweep.RopeNode, moveIndex, start, sweep.RopeFrame, sweep.RopeFrame * ReachMoves.Dt + RopePenalty, sweep.Bonk);

		var walk = 0f;
		var to = sweep.Hit switch
		{
			Hit.Land => LandingStand(sweep.At, out walk),
			Hit.Wall => WallNode(sweep.At, sweep.Side),
			Hit.Water => SwimNodeAt(sweep.At),
			_ => -1,
		};
		if (to < 0 || to == index)
			return;
		var slides = sweep.Hit == Hit.Land && SlidesOff(move, sweep);
		var risk = (slides ? SlideOffRisk : 0) + (sweep.Bonk > 0 ? BonkRisk : 0) +
			MathF.Max(0, sweep.At.Y - start.Y - DropRiskStart) * DropRiskPerPx;
		AddEdge(index, to, moveIndex, start, sweep.Frame, sweep.Frame * ReachMoves.Dt + penalty + walk + (slides ? KnifeEdgePenalty : 0),
			sweep.Bonk, MathF.Min(1, risk));
	}

	// Only a landing a few px from where its surface ends can be knocked off it by a pixel's error.
	bool NearLip(Point2 p)
	{
		foreach (var dir in (ReadOnlySpan<int>)[-1, 1])
		{
			var q = p;
			var crawl = false;
			for (var n = 0; n < Game.TileSize + 3; n++)
				if (WalkStep(ref q, dir, ref crawl) is var step && step == Step.Off)
					return true;
				else if (step == Step.Blocked)
					break;
		}
		return false;
	}

	// Even braking hard (a small input caps speed at 2000px/s²), a landing carries on a few px: off a narrow
	// ledge, that's a fall.
	bool SlidesOff(ReachMove move, Sweep landing)
	{
		var speed = (move.Offsets[landing.Frame].X - move.Offsets[landing.Frame - 1].X) / ReachMoves.Dt;
		var dir = Math.Sign(speed);
		var p = landing.At;
		var crawl = false;
		for (var n = (int)(speed * speed / 4000) + 2; n > 0 && dir != 0; n--)
		{
			var step = WalkStep(ref p, dir, ref crawl);
			if (step == Step.Off)
				return true;
			if (step == Step.Blocked)
				return false;
		}
		return false;
	}

	// Near a lip, would the landing survive the arc running a pixel off either way, or two px low? An arc that only
	// just tops a ledge (or a jumpthru, at its apex) is a coin flip on the pawn's sub-pixel remainder.
	(bool Knife, bool Short) Margins(Edge edge)
	{
		var landing = Nodes[edge.To].Position;
		if (!NearLip(landing))
			return default;
		return (!Holds(Point2.Left) || !Holds(Point2.Right), !Holds(new(0, 2)));

		bool Holds(Point2 shift)
		{
			var sweep = SweepMove(Moves[edge.Move], edge.Start, Nodes[edge.From].Kind == NodeKind.Rope, null, shift);
			return sweep.Hit == Hit.Land && Math.Abs(sweep.At.Y - landing.Y) <= 2 && Math.Abs(sweep.At.X - landing.X) <= Game.TileSize;
		}
	}

	void AddEdge(int from, int to, int move, Point2 start, int frames, float edgeCost, int bonk = 0, float risk = 0)
	{
		if (edgeTo.TryGetValue(to, out var existing))
		{
			if (Edges[existing].Cost > edgeCost)
				Edges[existing] = new(from, to, move, start, frames, edgeCost, bonk, risk);
			return;
		}
		edgeTo[to] = Edges.Count;
		Edges.Add(new(from, to, move, start, frames, edgeCost, bonk, risk));
	}

	// The stand a landing belongs to: an existing one a short walk along the surface, or a new one right there.
	int LandingStand(Point2 p, out float walk)
	{
		var found = StandNear(p, Game.TileSize, out var distance);
		walk = distance / WalkSpeed;
		return found >= 0 ? found : TryStand(p, out var ducking) && !ducking ? AddStand(p, 0, false) : -1;
	}

	// Which stand a pawn on the ground here is on, forgiving a few px of drift since the field was built, least
	// drift first. A pawn whose feet the raster puts inside a surface (or off the end of one) still gets an
	// answer, where walking the raster from the exact pixel gives up at once.
	public int StandUnder(Point2 feet, int maxWalk)
	{
		for (var drift = 0; drift <= DriftTolerance; drift++)
		{
			if (StandNear(feet - new Point2(0, drift), maxWalk, out _) is var above and >= 0)
				return above;
			if (drift > 0 && StandNear(feet + new Point2(0, drift), maxWalk, out _) is var below and >= 0)
				return below;
		}
		return -1;
	}

	// The stand at these feet exactly, or the nearest one a short walk along the same surface.
	int StandNear(Point2 feet, int maxWalk, out int distance)
	{
		distance = 0;
		if (standAt.TryGetValue(feet, out var exact))
			return exact;
		var found = -1;
		distance = int.MaxValue;
		foreach (var dir in (ReadOnlySpan<int>)[-1, 1])
		{
			var q = feet;
			var crawl = false;
			for (var n = 1; n < distance && n <= maxWalk; n++)
			{
				if (WalkStep(ref q, dir, ref crawl) != Step.Walked)
					break;
				if (standAt.TryGetValue(q, out var stand))
				{
					found = stand;
					distance = n;
				}
			}
		}
		return found;
	}

	public int WallNodeNear(Point2 feet, int side)
	{
		foreach (var dy in (ReadOnlySpan<int>)[0, -1, 1])
			if (wallAt.TryGetValue((feet.X, (feet.Y >> 2) + dy, side), out var node))
				return node;
		return -1;
	}

	public int RopeNodeNear(Point2 feet, int radius)
	{
		var found = -1;
		var nearest = radius * radius + 1f;
		foreach (var node in ropeNodes)
			if ((Nodes[node].Position - feet).LengthSquared() is var d && d < nearest)
			{
				nearest = d;
				found = node;
			}
		return found;
	}

	// Contacts a few px apart on the same wall share one node: kicks from them go nearly the same places.
	int WallNode(Point2 p, int side)
	{
		var key = (p.X, p.Y >> 2, side);
		if (wallAt.TryGetValue(key, out var existing))
			return existing;
		if (Nodes.Count >= MaxNodes)
			return -1;
		wallAt[key] = Nodes.Count;
		Nodes.Add(new(p, NodeKind.Wall, side));
		return Nodes.Count - 1;
	}

	public int SwimNodeAt(Point2 feet)
		=> swimAt.TryGetValue(TileOf(feet + BodyCenter), out var node) ? node : -1;

	Point2 TileOf(Point2 p)
		=> bounds.TopLeft + (p - bounds.TopLeft) / Game.TileSize * Game.TileSize;

	bool TryStand(Point2 p, out bool ducking)
	{
		ducking = false;
		if (water[p + BodyCenter])
			return false;
		if (!Solid(Body + p))
			return Grounded(p, Body);
		ducking = true;
		return !Solid(DuckBody + p) && Grounded(p, DuckBody);
	}

	// Up out of the water first, then across: the way a swimmer climbs out onto a ledge.
	bool ClearPath(Point2 from, Point2 to)
	{
		var corner = new Point2(from.X, Math.Min(from.Y, to.Y));
		return !Solid((Body + from).Conflate(Body + corner)) && !Solid((Body + corner).Conflate(Body + to));
	}

	// Above the raster the room's top row carries on up, as it does for room collision.
	bool Solid(RectInt rect)
	{
		if (rect.Top < bounds.Top)
			rect = new RectInt(rect.X, bounds.Top, rect.Width, Math.Max(1, rect.Bottom - bounds.Top));
		return solid.Any(rect) || ghost.Width > 0 && rect.Overlaps(ghost);
	}

	// Actor.Grounded on the raster: solid underfoot, or a jumpthru underfoot that the body isn't already inside.
	bool Grounded(Point2 p, RectInt body)
	{
		var underfoot = new RectInt(p.X + body.Left, p.Y, body.Width, 1);
		return Solid(underfoot) || jumpthru.Any(underfoot) && !jumpthru.Any(body + p);
	}

	public bool WallBeside(Point2 p, int side)
		=> Solid(new RectInt(side > 0 ? p.X + Body.Right : p.X + Body.Left - 1, p.Y + Body.Top, 1, Body.Height));

	// One pixel of walking the way Actor.Move takes it: level, a step up, or a step down, crouching under
	// anything too low to stand.
	Step WalkStep(ref Point2 p, int dir, ref bool ducking)
	{
		var next = p + new Point2(dir, 0);
		foreach (var duck in (ReadOnlySpan<bool>)[false, true])
		{
			var body = duck ? DuckBody : Body;
			if (!Solid(body + next))
			{
				ducking = duck;
				if (Grounded(next, body))
					p = next;
				else if (!Solid(body + next + Point2.Down) && Grounded(next + Point2.Down, body))
					p = next + Point2.Down;
				else
					return Step.Off;
				return Step.Walked;
			}
			if (!Solid(body + p + Point2.Up) && !Solid(body + next + Point2.Up))
			{
				ducking = duck;
				p = next + Point2.Up;
				return Step.Walked;
			}
		}
		return Step.Blocked;
	}

	// Replays a move's recorded path over the raster until something interrupts it: the pixels of each frame go
	// X first, then Y, as Actor.Move takes them.
	Sweep SweepMove(ReachMove move, Point2 start, bool fromRope, RectInt? target, Point2 shift = default)
	{
		sweeps++;
		var result = new Sweep { RopeNode = -1, TouchFrame = target is { } t && t.Overlaps(Body + start) ? 0 : -1 };
		var p = start;
		var offsets = move.Offsets;
		var body = move.Crawl ? DuckBody : Body;
		var grabRadius = RopeGrabRadius * RopeGrabRadius;
		var bonkY = 0;
		for (var k = 1; k < offsets.Length; k++)
		{
			var next = start + offsets[k];
			if (result.Bonk > 0)
				next.Y = bonkY + fall.Offsets[Math.Min(k - result.Bonk, fall.Offsets.Length - 1)].Y;
			if (k <= move.GroundedFrames)
			{
				if (Solid(body + next) || !Grounded(next, body))
					return Stop(ref result, Hit.Invalid, p, k);
				p = next;
				continue;
			}
			// A crawl stays crouched in the air until there's room to stand, as Pawn does.
			if (body != Body && !Solid(Body + p))
				body = Body;
			next += shift;

			// Walking off a lip that isn't there: the recorded pawn left the ground here, the raster's wouldn't.
			if (k == move.GroundedFrames + 1 && move.Kind == ReachMove.Kinds.WalkOff &&
				(Solid(body + next) || Grounded(next, body)))
				return Stop(ref result, Hit.Invalid, p, k);
			// Level at the top of the arc still counts as rising until the recording heads down: the pawn only lands
			// once it's falling.
			var descending = next.Y > p.Y || next.Y == p.Y && result.Bonk == 0 && (k + 1 >= offsets.Length || offsets[k + 1].Y >= offsets[k].Y);
			var swept = (body + p).Conflate(body + next);
			if (descending)
				swept = new RectInt(swept.X, swept.Y, swept.Width, swept.Height + 1);
			if (!Solid(swept) && !(descending && jumpthru.Any(swept)))
				p = next;
			else
			{
				var dx = Math.Sign(next.X - p.X);
				var wall = 0;
				for (; p.X != next.X; p.X += dx)
					if (Solid(body + p + new Point2(dx, 0)))
					{
						if (Grounded(p, body))
							return Stop(ref result, Hit.Invalid, p, k);
						wall = dx; // Actor.Move still takes the frame's Y pixels after the X ones stop
						break;
					}
				var dy = Math.Sign(next.Y - p.Y);
				for (; p.Y != next.Y; p.Y += dy)
				{
					if (dy > 0 && Grounded(p, body))
						break;
					if (Solid(body + p + new Point2(0, dy)))
					{
						if (dy < 0 && result.Bonk == 0)
						{
							result.Bonk = k;
							bonkY = p.Y;
						}
						break;
					}
				}
				if (descending && Grounded(p, body))
					return Stop(ref result, k == move.GroundedFrames + 1 ? Hit.Invalid : Hit.Land, p, k);
				if (wall != 0)
					return Stop(ref result, Hit.Wall, p, k, wall);
			}

			if (water[p + BodyCenter])
				return Stop(ref result, Hit.Water, p, k);
			// Most of a sweep is nowhere near a rope, and this runs every frame of every one of them.
			if (result.RopeNode < 0 && ropeReach.Contains(p + BodyCenter) && (!fromRope || k > RopeGraceFrames))
				foreach (var (point, node) in ropePoints)
					if (Vector2.DistanceSquared(p + BodyCenter, point) <= grabRadius)
					{
						result.RopeNode = node;
						result.RopeFrame = k;
						break;
					}
			if (result.TouchFrame < 0 && target is { } touch && touch.Overlaps(body + p))
				result.TouchFrame = k;
			if (p.Y < bounds.Top)
				return Stop(ref result, Hit.Invalid, p, k);
			if (p.Y > roomBottom + Game.TileSize * 2)
				return Stop(ref result, Hit.Dead, p, k);
		}
		return Stop(ref result, Hit.None, p, offsets.Length - 1);
	}

	static Sweep Stop(ref Sweep result, Hit hit, Point2 at, int frame, int side = 0)
	{
		result.Hit = hit;
		result.At = at;
		result.Frame = frame;
		result.Side = side;
		return result;
	}

	void Solve(RectInt target)
	{
		var count = Nodes.Count;
		if (cost.Length < count)
		{
			cost = new float[count];
			best = new int[count];
			touchFrame = new int[count];
		}
		Array.Fill(cost, float.PositiveInfinity, 0, count);
		Array.Fill(best, -1, 0, count);
		Array.Clear(touchFrame, 0, count);
		queue.Clear();

		for (var i = 0; i < count; i++)
			if (target.Overlaps((Nodes[i].Ducking ? DuckBody : Body) + Nodes[i].Position))
				Seed(i, 0, -1, 0);

		// Nothing stands in it (a goal hanging in the air): reach it mid-move instead.
		solvedMidMove = queue.Count == 0;
		if (solvedMidMove)
			for (var e = 0; e < Edges.Count; e++)
			{
				var edge = Edges[e];
				if (edge.Move < 0 || Moves[edge.Move].Burrows || Vector2.DistanceSquared(edge.Start, target.Center) > TouchSearchRadius * TouchSearchRadius ||
					TouchFrame(edge, target) is not (var frame and > 0))
					continue;
				// Past a bonk the recording only bounds the path, so sweep it for the real touch.
				if (edge.Bonk > 0 && frame >= edge.Bonk)
				{
					var sweep = SweepMove(Moves[edge.Move], edge.Start, Nodes[edge.From].Kind == NodeKind.Rope, target);
					frame = sweep.TouchFrame <= sweep.Frame ? sweep.TouchFrame : -1;
				}
				if (frame > 0)
					Seed(edge.From, frame * ReachMoves.Dt + JumpPenalty, e, frame);
			}

		while (queue.TryDequeue(out var node, out var reached))
		{
			if (reached > cost[node])
				continue;
			for (var i = incomingStart[node]; i < incomingStart[node + 1]; i++)
			{
				var edge = Edges[incoming[i]];
				var through = reached + edge.Cost +
					(misses.Count > 0 && edge.Move >= 0 ? misses.GetValueOrDefault((edge.Start, edge.Move)) * MissPenalty : 0);
				if (through < cost[edge.From])
				{
					cost[edge.From] = through;
					best[edge.From] = incoming[i];
					touchFrame[edge.From] = 0;
					queue.Enqueue(edge.From, through);
				}
			}
		}
	}

	// The first frame this edge's move puts the body in the target, or -1, judging by the recording alone: up to
	// where the edge ends, the sweep that made it followed the recording exactly. The reach of the whole recording
	// first, then frame by frame. Past a bonk the path leaves the recording -- its X still follows it, but its Y
	// runs from the bonk on down to wherever the edge ended.
	int TouchFrame(Edge edge, RectInt target)
	{
		var offsets = Moves[edge.Move].Offsets;
		var landing = Body + Nodes[edge.To].Position;
		if (!(Moves[edge.Move].Reach + edge.Start).Conflate(landing).Overlaps(target))
			return -1;
		var bonked = edge.Bonk > 0 && edge.Bonk < offsets.Length;
		var falling = bonked ? (Body + edge.Start + offsets[edge.Bonk]).Conflate(landing) : default;
		for (var k = 1; k <= edge.Frames && k < offsets.Length; k++)
		{
			var at = Body + edge.Start + offsets[k];
			if (target.Overlaps(bonked && k >= edge.Bonk ? new RectInt(at.X, falling.Y, at.Width, falling.Height) : at))
				return k;
		}
		return -1;
	}

	// A target in the air reaches down to the floor below it, if that's within maxDrop: it's about to land there.
	public RectInt DownToFloor(RectInt target, int maxDrop)
	{
		for (var drop = 0; drop <= maxDrop; drop++)
		{
			var row = new RectInt(target.X, target.Bottom + drop, target.Width, 1);
			if (Solid(row) || jumpthru.Any(row))
				return target with { Height = target.Height + drop };
		}
		return target;
	}

	void Seed(int node, float at, int edge, int frame)
	{
		if (at >= cost[node])
			return;
		cost[node] = at;
		best[node] = edge;
		touchFrame[node] = frame;
		queue.Enqueue(node, at);
	}

	public int NearestNode(Point2 feet, int radius)
	{
		var found = -1;
		var nearest = radius * radius + 1f;
		for (var i = 0; i < Nodes.Count; i++)
		{
			var d = (Nodes[i].Position - feet).LengthSquared();
			if (d < nearest)
			{
				nearest = d;
				found = i;
			}
		}
		return found;
	}

	static Color Near => new(0x3cff6e);
	static Color Mid => new(0xffe14a);
	static Color Far => new(0xff4a4a);
	static Color Unreachable => new Color(0x606070) * 0.6f;
	static Color PathColor => new(0x7fe8ff);

	public void Render(Batcher batcher, Point2? from)
	{
		if (!solved)
			return;
		var maxCost = 0f;
		for (var i = 0; i < Nodes.Count; i++)
			if (float.IsFinite(cost[i]))
				maxCost = MathF.Max(maxCost, cost[i]);

		for (var i = 0; i < Nodes.Count; i++)
		{
			var node = Nodes[i];
			var t = maxCost > 0 ? cost[i] / maxCost : 0;
			var color = !float.IsFinite(cost[i]) ? Unreachable : t < 0.5f ? Color.Lerp(Near, Mid, t * 2) : Color.Lerp(Mid, Far, t * 2 - 1);
			var p = (Vector2)node.Position;
			switch (node.Kind)
			{
				case NodeKind.Ride:
					batcher.Rect(new Rect(p.X - 1, p.Y - 2, 2, 2), color * 0.6f);
					break;
				case NodeKind.Stand:
					batcher.Rect(new Rect(p.X - 1.5f, p.Y - 3, 3, 3), color);
					if (node.Side != 0)
						batcher.Rect(new Rect(p.X + node.Side * 2 - 0.5f, p.Y - 2, 1, 1), color);
					break;
				case NodeKind.Wall:
					batcher.Rect(new Rect(node.Side > 0 ? p.X + 3 : p.X - 4, p.Y - 10, 1, 6), color);
					break;
				case NodeKind.Swim:
					batcher.Rect(new Rect(p.X - 2, p.Y - 8, 4, 4), color * 0.7f);
					break;
				case NodeKind.Rope:
					batcher.Rect(new Rect(p.X - 1, p.Y - 7, 2, 2), color);
					break;
			}
		}

		if (Target is { } target)
			batcher.RectLine(target, 1, PathColor);
		if (from is not { } feet || NearestNode(feet, 24) is not (var at and >= 0))
			return;
		for (var steps = 0; steps < 256 && cost[at] > 0 && best[at] is var e and >= 0; steps++)
		{
			var edge = Edges[e];
			var start = (Vector2)edge.Start + new Vector2(0, -2);
			if (edge.Move < 0 || Moves[edge.Move].Burrows)
				batcher.Line(start, (Vector2)Nodes[edge.To].Position + new Vector2(0, -2), 1, PathColor);
			else
			{
				var offsets = Moves[edge.Move].Offsets;
				var frames = touchFrame[at] > 0 ? touchFrame[at] : edge.Frames;
				for (var k = 1; k <= frames && k < offsets.Length; k++)
					batcher.Line(start + (Vector2)offsets[k - 1], start + (Vector2)offsets[k], 1, PathColor);
			}
			if (touchFrame[at] > 0)
				break;
			at = edge.To;
		}
	}
}

// Keeps a ReachField current: re-rasterizes the room every RefreshInterval and, when it differs, builds the next
// field a slice per frame while the last one stays in use, then swaps it in. Opened doors, grown ribbons and
// broken tiles are simply part of the next build.
public sealed class ReachSense(ReachBody body)
{
	static float RefreshInterval => 0.25f;
	static double BuildBudgetMs => 2;
	// Until there is a field the pawn has no idea where it can go, so a cold build spends more of each frame:
	// nothing is in use to go stale, and the frames it lands on are a room transition's anyway.
	// 8ms is as much as fits a 60fps frame: the floor for a slice is one SweepMove, which is 5-7ms for a long
	// arc in a Debug build.
	static double ColdBudgetMs => 4;
	// REACH_REBUILD=1 rebuilds every refresh, to profile a build warm.
	static readonly bool AlwaysRebuild = Environment.GetEnvironmentVariable("REACH_REBUILD") == "1";
	// REACH_SLICE=1 makes the gym slice as it would in play; nothing else exercises the sliced path.
	static readonly bool AlwaysSlice = Environment.GetEnvironmentVariable("REACH_SLICE") == "1";

	public ReachField? Field { get; private set; }
	ReachField? next;
	IEnumerator<bool>? building;
	List<ReachMove>? moves;
	Room? room;
	float refreshTimer;
	float buildTime; // how long the build in flight has taken, in game time so the gym and a rewind agree
	// Moves tried and missed this room visit, by where they started: they cost more from then on.
	readonly Dictionary<(Point2 Start, int Move), int> misses = [];

	// floorReach > 0 stretches an airborne target down to the floor that far below it.
	public void Update(Game game, Room room, RectInt target, int floorReach = 0)
	{
		moves ??= ReachMoves.For(game, body);
		if (room != this.room)
		{
			this.room = room;
			Field = null;
			building = null;
			refreshTimer = 0;
			misses.Clear();
		}

		refreshTimer -= game.Time.Delta;
		if (building == null && refreshTimer <= 0)
		{
			refreshTimer = RefreshInterval;
			buildTime = 0;
			next ??= new(body, moves, misses);
			next.Rasterize(game, room);
			// The gym runs unthrottled and must build on the same frame every run.
			if (Field == null || AlwaysRebuild || !next.SameAs(Field))
			{
				var budget = Field == null ? ColdBudgetMs : BuildBudgetMs;
				building = next.Build(game, game.Manager.Unthrottled && !AlwaysSlice ? double.PositiveInfinity : budget).GetEnumerator();
			}
		}
		if (building != null)
		{
			buildTime += game.Time.Delta;
			if (!building.MoveNext())
			{
				building = null;
				// A room that keeps changing -- a surface that grows -- would otherwise start the next build on the
				// very next frame, since refreshTimer ran down the whole time this one took. Resting as long as the
				// build did caps that at half the frames, and changes nothing where builds are rare.
				refreshTimer = Math.Max(refreshTimer, buildTime);
				(Field, next) = (next, Field);
			}
		}
		Field?.SolveFor(floorReach > 0 ? Field.DownToFloor(target, floorReach) : target);
	}
}
