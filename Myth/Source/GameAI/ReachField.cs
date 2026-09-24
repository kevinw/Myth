using System.Diagnostics;
using System.Numerics;
using Foster.Framework;

namespace Myth;

// Bud's sense of where he can get to in the room as it is right now. The live room collision (tiles, solid and
// jumpthru actors, ribbon pixel masks) is rasterized into SummedMasks; standing spots are found on that raster;
// recorded ReachMoves are swept from each spot to see where they end up; then a Dijkstra run backwards from the
// target gives every spot its cost to reach it. The raster is re-checked every RefreshInterval and everything
// rebuilds when it differs, so opened doors, grown ribbons and broken tiles are simply part of the next build.
public sealed class ReachField
{
	public enum NodeKind { Stand, Wall, Swim, Rope }
	// Side: which way a Stand's lip drops off, the wall a Wall node touches, or a Rope node's rope.
	public readonly record struct Node(Point2 Position, NodeKind Kind, int Side = 0, bool Ducking = false);
	// Move indexes Moves, swept from Start for Frames; -1 walks, crawls, climbs or swims straight to To. Bonk is
	// the frame the move hits its head, after which it falls from there instead of following its recording.
	public readonly record struct Edge(int From, int To, int Move, Point2 Start, int Frames, float Cost, int Bonk = 0);

	enum Hit { None, Invalid, Land, Wall, Water, Dead }
	struct Sweep
	{
		public Hit Hit;
		public Point2 At;
		public int Frame, Side, RopeNode, RopeFrame, TouchFrame, Bonk;
	}

	enum Step { Walked, Off, Blocked }

	static RectInt Body => new(-4, -12, 8, 12);
	static RectInt DuckBody => new(-4, -6, 8, 6);
	static Point2 BodyCenter => new(0, -6);
	static float RefreshInterval => 0.25f;
	static float JumpPenalty => 0.1f;
	static float FallPenalty => 0.05f;
	static float RopePenalty => 0.2f;
	// A landing that misses when the arc runs a pixel off (the pawn's sub-pixel remainder) is a gamble.
	static float KnifeEdgePenalty => 1f;
	// Each time a move is tried from a spot and doesn't arrive, it costs this much more (for this room visit).
	static float MissPenalty => 1.5f;
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
	static int TouchSearchRadius => 200;
	// REACH_REBUILD=1 rebuilds every refresh, to profile a build warm.
	static readonly bool AlwaysRebuild = Environment.GetEnvironmentVariable("REACH_REBUILD") == "1";

	public readonly List<Node> Nodes = [];
	public readonly List<Edge> Edges = [];
	public List<ReachMove> Moves { get; private set; } = [];
	public RectInt? Target { get; private set; }
	public double BuildMs { get; private set; }
	public float Cost(int node) => cost[node];
	public int BestEdge(int node) => best[node];

	SummedMask solid = new(), jumpthru = new(), water = new();
	SummedMask nextSolid = new(), nextJumpthru = new(), nextWater = new();
	Room? builtRoom;
	int builtRopes;
	float refreshTimer;
	bool solved;
	RectInt bounds;
	int roomBottom;
	readonly Dictionary<Point2, int> standAt = [];
	readonly Dictionary<Point2, int> swimAt = [];
	readonly Dictionary<(int X, int Y, int Side), int> wallAt = [];
	readonly List<int> ropeNodes = [];
	readonly List<(Vector2 Point, int Node)> ropePoints = [];
	readonly Dictionary<int, int> edgeTo = []; // cheapest edge so far from the node being expanded, by destination
	readonly List<int> runups = [];
	ReachMove fall = null!; // straight down from rest: how a bonked head comes back down
	float[] cost = [];
	int[] best = [];
	int[] touchFrame = []; // > 0 where the target is reached partway through the node's best move
	int[] incomingStart = [];
	int[] incoming = [];
	readonly PriorityQueue<int, float> queue = new();
	readonly Dictionary<(Point2 Start, int Move), int> misses = [];
	int sweeps;

	public void ReportMiss(Point2 start, int move)
	{
		misses[(start, move)] = misses.GetValueOrDefault((start, move)) + 1;
		solved = false;
	}

	public void Update(Game game, Room room, RectInt target)
	{
		if (Moves.Count == 0)
		{
			Moves = ReachMoves.Record(game);
			foreach (var move in Moves)
				if (move.Runup > 0 && !runups.Contains(move.Runup))
					runups.Add(move.Runup);
			fall = Moves.First(move => move is { Kind: ReachMove.Kinds.Fall, Steer: 0 });
		}

		refreshTimer -= game.Time.Delta;
		if (room != builtRoom || refreshTimer <= 0)
		{
			refreshTimer = RefreshInterval;
			Rasterize(game, room);
			var ropes = RopeSignature(game);
			if (room != builtRoom || ropes != builtRopes || AlwaysRebuild || !nextSolid.SamePixels(solid) ||
				!nextJumpthru.SamePixels(jumpthru) || !nextWater.SamePixels(water))
			{
				(solid, nextSolid) = (nextSolid, solid);
				(jumpthru, nextJumpthru) = (nextJumpthru, jumpthru);
				(water, nextWater) = (nextWater, water);
				if (room != builtRoom)
					misses.Clear();
				builtRoom = room;
				builtRopes = ropes;
				var start = Stopwatch.GetTimestamp();
				Build(game);
				BuildMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
				Console.WriteLine($"[Reach] {room.Id}: {Nodes.Count} nodes, {Edges.Count} edges, {sweeps} sweeps, {BuildMs:0.0}ms");
				solved = false;
			}
		}

		// Following a moving target only re-solves once it has moved a few px.
		if (!solved || Target is not { } solvedFor || solvedFor.Size != target.Size ||
			Math.Abs(solvedFor.X - target.X) >= 4 || Math.Abs(solvedFor.Y - target.Y) >= 4)
		{
			Solve(target);
			Target = target;
			solved = true;
		}
	}

	void Rasterize(Game game, Room room)
	{
		// Room collision clamps queries outside the room to its edge tiles, so the raster runs a margin past
		// the edges with them repeated: standing on top of the room's top row is real ground.
		var roomBounds = room.WorldBounds;
		var rasterBounds = roomBounds.Inflate(RasterMargin * Game.TileSize);
		nextSolid.Reset(rasterBounds);
		nextJumpthru.Reset(rasterBounds);
		nextWater.Reset(rasterBounds);
		for (var ty = -RasterMargin; ty < room.Size.Y + RasterMargin; ty++)
		for (var tx = -RasterMargin; tx < room.Size.X + RasterMargin; tx++)
		{
			var tile = new Point2(tx, ty);
			var clamped = new Point2(Math.Clamp(tx, 0, room.Size.X - 1), Math.Clamp(ty, 0, room.Size.Y - 1));
			var rect = new RectInt(roomBounds.TopLeft + tile * Game.TileSize, Game.TileSize, Game.TileSize);
			if (room.SolidTiles.Length > 0 && room.SolidTiles[clamped.Y * room.Size.X + clamped.X])
				nextSolid.Fill(rect);
			else if (room.TryGetSlopeTile(clamped, out _))
				for (var y = rect.Top; y < rect.Bottom; y++)
				for (var x = rect.Left; x < rect.Right; x++)
					if (Game.OverlapsRoomCollision(room, new RectInt(x, y, 1, 1)))
						nextSolid[new(x, y)] = true;
			if (room.IsWaterTile(tile))
				nextWater.Fill(rect);
		}

		// Things that walk or ride around (pawns, enemies, moving platforms) aren't terrain: stamping them
		// would rebuild the field every refresh.
		foreach (var actor in game.Actors)
		{
			if (actor is Grid or Pawn or MovingPlatform || actor.Tags.Has(ActorTag.Enemy) ||
				actor.Room != null && actor.Room != room || game.destroying.Contains(actor))
				continue;
			var layer = actor.Mask.Has(Actor.Masks.Solid) ? nextSolid : actor.Mask.Has(Actor.Masks.Jumpthru) ? nextJumpthru : null;
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

		nextSolid.Commit();
		nextJumpthru.Commit();
		nextWater.Commit();
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
		return hash.ToHashCode();
	}

	void Build(Game game)
	{
		Nodes.Clear();
		Edges.Clear();
		standAt.Clear();
		swimAt.Clear();
		wallAt.Clear();
		ropeNodes.Clear();
		ropePoints.Clear();
		sweeps = 0;
		bounds = solid.Bounds;
		roomBottom = game.Bounds.Bottom;

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
					ropePoints.Add((points[i], ropeNodes[^1]));
			}
		}

		for (var x = bounds.Left + Game.TileSize / 2; x < bounds.Right; x += Game.TileSize)
		for (var y = bounds.Top + 1; y <= bounds.Bottom; y++)
			if (TryStand(new(x, y), out var ducking))
				AddStand(new(x, y), 0, ducking);

		// Lips, where a walk-off or a max-distance jump leaves, and the spots a runup to each lip starts from.
		for (int i = 0, sampled = Nodes.Count; i < sampled; i++)
		{
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

		for (var i = 0; i < Nodes.Count; i++)
			Expand(i);
		for (var e = 0; e < Edges.Count; e++)
			if (Edges[e] is { Move: >= 0 } edge && Nodes[edge.To].Kind == NodeKind.Stand && KnifeEdge(edge))
				Edges[e] = edge with { Cost = edge.Cost + KnifeEdgePenalty };

		incomingStart = new int[Nodes.Count + 1];
		incoming = new int[Edges.Count];
		foreach (var edge in Edges)
			incomingStart[edge.To + 1]++;
		for (var i = 0; i < Nodes.Count; i++)
			incomingStart[i + 1] += incomingStart[i];
		var fill = incomingStart[..^1];
		for (var e = 0; e < Edges.Count; e++)
			incoming[fill[Edges[e].To]++] = e;
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
				var onJumpthruOnly = !Solid(new RectInt(p.X + Body.Left, p.Y, Body.Width, 1));
				for (var m = 0; m < Moves.Count; m++)
				{
					if (node.Ducking != Moves[m].Crawl)
						continue;
					if (Moves[m].Kind is ReachMove.Kinds.Jump or ReachMove.Kinds.WalkOff)
						TryMove(index, m, p, false);
					else if (Moves[m].Kind == ReachMove.Kinds.Fall && onJumpthruOnly)
						TryMove(index, m, p + new Point2(0, 2), false);
				}
				break;

			case NodeKind.Wall:
				for (var m = 0; m < Moves.Count; m++)
					if (Moves[m].Kind == ReachMove.Kinds.Kick && Moves[m].Dir == node.Side ||
						Moves[m].Kind == ReachMove.Kinds.Fall && Moves[m].Steer != node.Side)
						TryMove(index, m, p, false);
				Slide(index);
				break;

			case NodeKind.Rope:
				foreach (var neighbor in (ReadOnlySpan<int>)[index - 1, index + 1])
					if (neighbor >= 0 && neighbor < Nodes.Count && Nodes[neighbor] is { Kind: NodeKind.Rope } other && other.Side == node.Side)
						AddEdge(index, neighbor, -1, p, 0, Vector2.Distance(p, other.Position) / ClimbSpeed);
				for (var m = 0; m < Moves.Count; m++)
					if (Moves[m] is { Kind: ReachMove.Kinds.Jump, Runup: 0 } or { Kind: ReachMove.Kinds.Fall })
						TryMove(index, m, p, true);
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
		if (to >= 0 && to != index)
			AddEdge(index, to, moveIndex, start, sweep.Frame, sweep.Frame * ReachMoves.Dt + penalty + walk +
				(sweep.Hit == Hit.Land && SlidesOff(move, sweep) ? KnifeEdgePenalty : 0), sweep.Bonk);
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

	bool KnifeEdge(Edge edge)
	{
		var landing = Nodes[edge.To].Position;
		foreach (var shift in (ReadOnlySpan<Point2>)[Point2.Left, Point2.Right, Point2.Down])
		{
			var sweep = SweepMove(Moves[edge.Move], edge.Start, Nodes[edge.From].Kind == NodeKind.Rope, null, shift);
			if (sweep.Hit != Hit.Land || Math.Abs(sweep.At.Y - landing.Y) > 2 || Math.Abs(sweep.At.X - landing.X) > Game.TileSize)
				return true;
		}
		return false;
	}

	void AddEdge(int from, int to, int move, Point2 start, int frames, float edgeCost, int bonk = 0)
	{
		if (edgeTo.TryGetValue(to, out var existing))
		{
			if (Edges[existing].Cost > edgeCost)
				Edges[existing] = new(from, to, move, start, frames, edgeCost, bonk);
			return;
		}
		edgeTo[to] = Edges.Count;
		Edges.Add(new(from, to, move, start, frames, edgeCost, bonk));
	}

	// The stand a landing belongs to: an existing one a short walk along the surface, or a new one right there.
	int LandingStand(Point2 p, out float walk)
	{
		var found = StandNear(p, Game.TileSize, out var distance);
		walk = distance / WalkSpeed;
		return found >= 0 ? found : TryStand(p, out var ducking) && !ducking ? AddStand(p, 0, false) : -1;
	}

	// The stand at these feet, or the nearest one a short walk along the same surface.
	public int StandNear(Point2 feet, int maxWalk, out int distance)
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
		return solid.Any(rect);
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
			var descending = next.Y >= p.Y;
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
			if (result.RopeNode < 0 && (!fromRope || k > RopeGraceFrames))
				foreach (var (point, node) in ropePoints)
					if (Vector2.DistanceSquared(p + BodyCenter, point) <= RopeGrabRadius * RopeGrabRadius)
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
		if (queue.Count == 0)
			for (var e = 0; e < Edges.Count; e++)
			{
				var edge = Edges[e];
				if (edge.Move < 0 || Vector2.DistanceSquared(edge.Start, target.Center) > TouchSearchRadius * TouchSearchRadius)
					continue;
				var sweep = SweepMove(Moves[edge.Move], edge.Start, Nodes[edge.From].Kind == NodeKind.Rope, target);
				if (sweep.TouchFrame > 0 && sweep.TouchFrame <= sweep.Frame)
					Seed(edge.From, sweep.TouchFrame * ReachMoves.Dt + JumpPenalty, e, sweep.TouchFrame);
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
			if (edge.Move < 0)
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
