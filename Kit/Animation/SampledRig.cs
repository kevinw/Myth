using System.Numerics;
using System.Text;

namespace Kit;

public sealed class SampledRig
{
	const uint Magic = 0x4749524d; // MRIG
	const uint Version = 1;

	public sealed class Clip(string name, float duration, int frameCount, Vector3[] joints)
	{
		public string Name => name;
		public float Duration => duration;
		public int FrameCount => frameCount;
		internal Vector3[] Joints => joints;
	}

	public string[] JointNames { get; }
	public short[] JointParents { get; }
	public int JointCount => JointNames.Length;
	public IReadOnlyList<Clip> Clips => clips;

	readonly Clip[] clips;
	readonly Dictionary<string, int> jointsByName;
	readonly Dictionary<string, Clip> clipsByName;

	SampledRig(string[] jointNames, short[] jointParents, Clip[] clips)
	{
		JointNames = jointNames;
		JointParents = jointParents;
		this.clips = clips;
		jointsByName = new(jointNames.Length, StringComparer.OrdinalIgnoreCase);
		for (var i = 0; i < jointNames.Length; i++)
			jointsByName.Add(jointNames[i], i);
		clipsByName = new(clips.Length, StringComparer.OrdinalIgnoreCase);
		foreach (var clip in clips)
			clipsByName.Add(clip.Name, clip);
	}

	public static SampledRig Load(Stream stream)
	{
		using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
		if (reader.ReadUInt32() != Magic || reader.ReadUInt32() != Version)
			throw new InvalidDataException("Unsupported sampled rig format.");

		var jointCount = reader.ReadUInt16();
		if (jointCount <= 0)
			throw new InvalidDataException("Sampled rig has no joints.");
		var jointNames = new string[jointCount];
		var jointParents = new short[jointCount];
		for (var i = 0; i < jointCount; i++)
		{
			jointNames[i] = ReadString(reader);
			jointParents[i] = reader.ReadInt16();
			if (jointParents[i] >= i)
				throw new InvalidDataException($"Joint '{jointNames[i]}' has an invalid parent.");
		}

		var clipCount = reader.ReadUInt16();
		var clips = new Clip[clipCount];
		for (var clipIndex = 0; clipIndex < clipCount; clipIndex++)
		{
			var name = ReadString(reader);
			var duration = reader.ReadSingle();
			var frameCount = checked((int)reader.ReadUInt32());
			if (duration <= 0 || frameCount < 2)
				throw new InvalidDataException($"Clip '{name}' has invalid timing.");
			var joints = new Vector3[checked(frameCount * jointCount)];
			for (var i = 0; i < joints.Length; i++)
				joints[i] = new(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
			clips[clipIndex] = new(name, duration, frameCount, joints);
		}
		if (stream.ReadByte() != -1)
			throw new InvalidDataException("Sampled rig has trailing data.");
		return new(jointNames, jointParents, clips);
	}

	static string ReadString(BinaryReader reader)
	{
		var length = reader.ReadUInt16();
		var bytes = reader.ReadBytes(length);
		return bytes.Length == length
			? Encoding.UTF8.GetString(bytes)
			: throw new EndOfStreamException();
	}

	public int FindJoint(string name)
		=> jointsByName.TryGetValue(name, out var index) ? index : -1;

	public Clip GetClip(string name)
		=> clipsByName.TryGetValue(name, out var clip)
			? clip
			: throw new KeyNotFoundException($"Rig clip '{name}' does not exist.");

	public bool TryGetClip(string name, out Clip clip)
		=> clipsByName.TryGetValue(name, out clip!);

	public void Sample(Clip clip, float time, bool loop, Span<Vector3> pose)
	{
		if (pose.Length < JointCount)
			throw new ArgumentException("Pose buffer is smaller than the rig.", nameof(pose));

		time = loop
			? ((time % clip.Duration) + clip.Duration) % clip.Duration
			: Math.Clamp(time, 0, clip.Duration);
		var frame = time / clip.Duration * (clip.FrameCount - 1);
		var from = Math.Min((int)frame, clip.FrameCount - 1);
		var to = Math.Min(from + 1, clip.FrameCount - 1);
		var amount = frame - from;
		var fromOffset = from * JointCount;
		var toOffset = to * JointCount;
		for (var i = 0; i < JointCount; i++)
			pose[i] = Vector3.Lerp(clip.Joints[fromOffset + i], clip.Joints[toOffset + i], amount);
	}
}
