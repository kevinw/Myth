using Foster.Framework;

namespace Myth;

// A maneuver recorded once, from a standstill, by running the real Pawn physics in empty space: the input
// pressed on each frame and where the body went. Swept through the live room raster (ReachField), the path
// holds until its first contact, so a move that sweeps clean there is the move the pawn will make.
public sealed class ReachMove(ReachMove.Kinds kind, int dir, int steer, int runup, bool crawl, int groundedFrames, Point2[] offsets, PawnInput[] inputs)
{
	// Jump: from standing, after running Runup px. WalkOff: run Runup px off a ledge. Fall: drop from rest in
	// the air. Kick: kick off a wall on the Dir side.
	public enum Kinds { Jump, WalkOff, Fall, Kick }

	public readonly Kinds Kind = kind;
	public readonly int Dir = dir;
	public readonly int Steer = steer;
	public readonly int Runup = runup;
	// Crouched until it leaves the ground: crawling off the end of a low tunnel.
	public readonly bool Crawl = crawl;
	// Offsets[1..GroundedFrames] are on the ground; the rest airborne.
	public readonly int GroundedFrames = groundedFrames;
	// Offsets[k] is where Inputs[k - 1] left the body; Offsets[0] is the start.
	public readonly Point2[] Offsets = offsets;
	public readonly PawnInput[] Inputs = inputs;
	// Everywhere the body goes along the whole recording, relative to the start.
	public readonly RectInt Reach = offsets.Aggregate(new RectInt(-4, -12, 8, 12), (reach, offset) => reach.Conflate(new RectInt(-4, -12, 8, 12) + offset));
}

public static class ReachMoves
{
	public const float Dt = 1f / 60;
	const int MaxFrames = 180;
	const int MaxDrop = 640;
	// So far outside any room that room collision, water and ropes never reach the recording.
	static readonly Point2 SimOrigin = new(-1 << 20, -1 << 20);

	public static List<ReachMove> Record(Game game)
	{
		var moves = new List<ReachMove>();
		game.WithSimClock(() =>
		{
			foreach (var hold in (ReadOnlySpan<float>)[0.055f, 0.1f, 0.16f, 0.22f])
			{
				foreach (var steer in (ReadOnlySpan<int>)[0, -1, 1])
					moves.Add(RecordJump(game, 0, 0, hold, steer, steer == 0 ? 99 : 0.1f));
				foreach (var dir in (ReadOnlySpan<int>)[-1, 1])
				foreach (var runup in (ReadOnlySpan<float>)[0, 0.1f, 0.2f, 0.35f])
				{
					moves.Add(RecordJump(game, dir, runup, hold, dir, 99));
					moves.Add(RecordJump(game, dir, runup, hold, 0, 0.2f));
					moves.Add(RecordJump(game, dir, runup, hold, -dir, 0.25f));
				}
			}
			foreach (var dir in (ReadOnlySpan<int>)[-1, 1])
			foreach (var runup in (ReadOnlySpan<int>)[-1, 0, 8, 16, 32, 48])
			{
				moves.Add(RecordWalkOff(game, dir, runup, dir, 99));
				moves.Add(RecordWalkOff(game, dir, runup, 0, 0.1f));
				moves.Add(RecordWalkOff(game, dir, runup, -dir, 0.15f));
			}
			foreach (var steer in (ReadOnlySpan<int>)[0, -1, 1])
				moves.Add(RecordFall(game, steer));
			foreach (var side in (ReadOnlySpan<int>)[-1, 1])
			foreach (var hold in (ReadOnlySpan<float>)[0.1f, 0.22f])
			foreach (var steer in (ReadOnlySpan<int>)[-side, 0, side])
				if (RecordKick(game, side, hold, steer) is { } kick)
					moves.Add(kick);
		});
		return moves;
	}

	static ReachMove RecordJump(Game game, int dir, float runupTime, float hold, int steer, float switchTime)
	{
		var pawn = new SimPawn(game, new RectInt(SimOrigin.X - 4000, SimOrigin.Y, 8000, 8));
		var jumpFrame = (int)MathF.Ceiling(runupTime / Dt - 0.001f);
		return Run(pawn, ReachMove.Kinds.Jump, dir, steer, frame =>
		{
			var sinceJump = (frame - jumpFrame) * Dt;
			return new(frame < jumpFrame || sinceJump < switchTime ? dir : steer, 0,
				JumpPressed: frame == jumpFrame, JumpDown: frame >= jumpFrame && sinceJump < hold, DashPressed: false, AllowWallJump: false);
		});
	}

	// The floor ends where a body standing `runup` px back from the lip has walked it off; -1 crawls off from the lip.
	static ReachMove RecordWalkOff(Game game, int dir, int runup, int steer, float switchTime)
	{
		var crawl = runup < 0;
		runup = Math.Max(0, runup);
		var floor = dir > 0
			? new RectInt(SimOrigin.X - 4000, SimOrigin.Y, 4000 - 3 + runup, 8)
			: new RectInt(SimOrigin.X + 3 - runup, SimOrigin.Y, 4000, 8);
		var pawn = new SimPawn(game, floor);
		var leftGround = -1;
		return Run(pawn, ReachMove.Kinds.WalkOff, dir, steer, frame =>
		{
			if (leftGround < 0 && !pawn.IsGrounded && frame > 0)
				leftGround = frame;
			var move = leftGround >= 0 && (frame - leftGround) * Dt >= switchTime ? steer : dir;
			return new(move, crawl && leftGround < 0 ? 1 : 0, false, false, false, AllowWallJump: false);
		}, runup, crawl);
	}

	static ReachMove RecordFall(Game game, int steer)
		=> Run(new SimPawn(game, null), ReachMove.Kinds.Fall, 0, steer, _ => new(steer, 0, false, false, false, AllowWallJump: false));

	static ReachMove? RecordKick(Game game, int side, float hold, int steer)
	{
		var wall = side > 0
			? new RectInt(SimOrigin.X + 4, SimOrigin.Y - 400, 8, 800)
			: new RectInt(SimOrigin.X - 12, SimOrigin.Y - 400, 8, 800);
		var pawn = new SimPawn(game, wall);
		var move = Run(pawn, ReachMove.Kinds.Kick, side, steer, frame => frame == 0
			? new(side, 0, JumpPressed: true, JumpDown: true, DashPressed: false)
			: new(steer, 0, JumpPressed: false, JumpDown: frame * Dt < hold, DashPressed: false, AllowWallJump: false));
		if (Math.Sign(move.Offsets[1].X) == -side)
			return move;
		Console.WriteLine($"[ReachMoves] kick off side {side} didn't push away; skipped");
		return null;
	}

	// Removes the floor (or wall) once the body leaves it, so it can't catch the rest of the path.
	static ReachMove Run(SimPawn pawn, ReachMove.Kinds kind, int dir, int steer, Func<int, PawnInput> input, int runup = -1, bool crawl = false)
	{
		var offsets = new List<Point2> { Point2.Zero };
		var inputs = new List<PawnInput>();
		var groundedFrames = 0;
		var airborne = false;
		for (var frame = 0; frame < MaxFrames; frame++)
		{
			pawn.Input = input(frame);
			pawn.Game.AdvanceReplayClock(Dt);
			pawn.Update();
			inputs.Add(pawn.Input);
			offsets.Add(pawn.Position - SimOrigin);
			if (!airborne && pawn.IsGrounded)
				groundedFrames++;
			else if (!airborne)
			{
				airborne = true;
				pawn.Solids.Clear();
			}
			if (kind == ReachMove.Kinds.Kick && frame == 0)
				pawn.Solids.Clear();
			if (offsets[^1].Y > MaxDrop)
				break;
		}
		return new(kind, dir, steer, runup >= 0 ? runup : Math.Abs(offsets[groundedFrames].X), crawl, groundedFrames, [.. offsets], [.. inputs]);
	}

	sealed class SimPawn : Pawn
	{
		static readonly List<Actor> noWind = [];
		public readonly List<Actor> Solids = [];
		public PawnInput Input;
		internal override List<Actor> CollisionActors => Solids;
		internal override List<Actor> WindActors => noWind;

		public SimPawn(Game game, RectInt? solid)
		{
			Game = game;
			Position = SimOrigin;
			Hitbox = new(StandingHitboxRect);
			if (solid is { } rect)
				Solids.Add(new Actor { Mask = Masks.Solid, Hitbox = new(rect) });
			grounded = Grounded();
		}

		public override PawnInput GetInput() => Input;
	}
}
