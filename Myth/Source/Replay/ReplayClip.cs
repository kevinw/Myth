using System.Numerics;
using System.Runtime.InteropServices;
using Foster.Framework;

namespace Myth;

// One input change-point: the recorded input held from this frame until the next change-point.
// Clips are recorded at a fixed timestep (ReplayHz), so a frame index is exact time and dt never
// needs storing — the whole clip is just its starting state + the frames where input changed.
struct InputChange
{
	public int Frame;
	public PawnInput Input;
}

// The player's state at the clip's clean start (room-load boundary). Every other actor is freshly
// spawned from room data, so this is the only carried-in runtime state we need to snapshot.
readonly record struct ReplayStart(Point2 Position, Vector2 Velocity, Signs Facing, Player.States State)
{
	public static ReplayStart Capture(Player p) => new(p.Position, p.Velocity, p.Facing, p.State);

	public void ApplyTo(Player p)
	{
		p.Position = Position;
		p.Velocity = Velocity;
		p.Facing = Facing;
		p.State = State;
	}
}

// A self-contained recording of one room visit, captured and replayed at a fixed ReplayHz timestep.
// On playback the room loads deterministically and the recorded input regenerates everything (AI,
// physics, transitions). VisibleFromFrame is the in-point for the fast-forward warmup; 0 = start.
sealed class ReplayClip
{
	public const int ReplayHz = 60;
	public const float ReplayDelta = 1f / ReplayHz;

	const uint Magic = 0x5052_594D; // "MYRP"
	public const int CurrentVersion = 2; // v2: fixed timestep, input change-points (no per-frame dt)

	public int Version = CurrentVersion;
	public ulong Seed;
	public string RoomId = "";
	public ReplayStart Start;
	public string SaveJson = "";
	public int VisibleFromFrame;
	public int FrameCount;
	public readonly List<InputChange> Changes = [];

	public float DurationSeconds => FrameCount * ReplayDelta;

	public void Write(Stream stream)
	{
		using var w = new BinaryWriter(stream);
		w.Write(Magic);
		w.Write(Version);
		w.Write(Seed);
		w.Write(RoomId);
		w.Write(Start.Position.X); w.Write(Start.Position.Y);
		w.Write(Start.Velocity.X); w.Write(Start.Velocity.Y);
		w.Write((int)Start.Facing);
		w.Write((int)Start.State);
		w.Write(SaveJson);
		w.Write(VisibleFromFrame);
		w.Write(FrameCount);
		w.Write(Changes.Count);
		w.Write(MemoryMarshal.AsBytes(CollectionsMarshal.AsSpan(Changes)));
	}

	public static ReplayClip Read(Stream stream)
	{
		using var r = new BinaryReader(stream);
		if (r.ReadUInt32() != Magic) throw new InvalidDataException("Not a replay clip");
		var clip = new ReplayClip { Version = r.ReadInt32() };
		if (clip.Version != CurrentVersion) throw new InvalidDataException($"Unsupported replay version {clip.Version}");
		clip.Seed = r.ReadUInt64();
		clip.RoomId = r.ReadString();
		clip.Start = new ReplayStart(
			new Point2(r.ReadInt32(), r.ReadInt32()),
			new Vector2(r.ReadSingle(), r.ReadSingle()),
			(Signs)r.ReadInt32(),
			(Player.States)r.ReadInt32());
		clip.SaveJson = r.ReadString();
		clip.VisibleFromFrame = r.ReadInt32();
		clip.FrameCount = r.ReadInt32();
		var count = r.ReadInt32();
		var changes = new InputChange[count];
		r.BaseStream.ReadExactly(MemoryMarshal.AsBytes(changes.AsSpan()));
		clip.Changes.AddRange(changes);
		return clip;
	}

	public void Save(string path)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		using var f = File.Create(path);
		Write(f);
	}

	public static ReplayClip Load(string path)
	{
		using var f = File.OpenRead(path);
		return Read(f);
	}
}
