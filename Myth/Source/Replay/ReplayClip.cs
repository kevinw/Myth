using System.Numerics;
using System.Runtime.InteropServices;
using Foster.Framework;

namespace Myth;

// One recorded sim step: the dt the sim saw (post-timeScale, via Game.Time) and the input the
// player consumed that frame. Blittable so a whole clip's frames serialize as one memory blob.
struct ReplayFrame
{
	public float Dt;
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

// A self-contained recording of one room visit: seed + starting room/progression + the input
// stream. On playback the room loads deterministically and the recorded input regenerates
// everything (AI, transitions). VisibleFromFrame is the in-point for the (later) fast-forward
// warmup; 0 means "show from the clip's start".
sealed class ReplayClip
{
	const uint Magic = 0x5052_594D; // "MYRP"
	public const int CurrentVersion = 1;

	public int Version = CurrentVersion;
	public ulong Seed;
	public string RoomId = "";
	public ReplayStart Start;
	public string SaveJson = "";
	public int VisibleFromFrame;
	public readonly List<ReplayFrame> Frames = [];

	public float DurationSeconds
	{
		get { var s = 0f; foreach (var f in Frames) s += f.Dt; return s; }
	}

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
		w.Write(Frames.Count);
		w.Write(MemoryMarshal.AsBytes(CollectionsMarshal.AsSpan(Frames)));
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
		var count = r.ReadInt32();
		var frames = new ReplayFrame[count];
		r.BaseStream.ReadExactly(MemoryMarshal.AsBytes(frames.AsSpan()));
		clip.Frames.AddRange(frames);
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
