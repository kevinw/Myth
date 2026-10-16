using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;

namespace Kit;

public sealed partial class OzzRig : IDisposable
{
	public sealed class Clip(string name, float duration, int index)
	{
		public string Name => name;
		public float Duration => duration;
		internal int Index => index;
	}

	readonly nint handle;
	readonly string[] jointNames;
	readonly short[] jointParents;
	readonly Clip[] clips;
	readonly Dictionary<string, int> jointsByName;
	readonly Dictionary<string, Clip> clipsByName;
	readonly Dictionary<int, float> groundSpeeds = [];
	bool disposed;

	public int JointCount => jointNames.Length;
	public int JointParent(int joint) => jointParents[joint]; // -1 at the root
	public IReadOnlyList<Clip> Clips => clips;

	unsafe OzzRig(nint handle)
	{
		this.handle = handle;
		jointNames = new string[Native.RigJointCount(handle)];
		jointParents = new short[jointNames.Length];
		jointsByName = new(jointNames.Length, StringComparer.OrdinalIgnoreCase);
		for (var i = 0; i < jointNames.Length; i++)
		{
			jointNames[i] = Marshal.PtrToStringUTF8(Native.RigJointName(handle, i))
				?? throw new InvalidDataException($"Ozz rig joint {i} has no name.");
			jointParents[i] = checked((short)Native.RigJointParent(handle, i));
			jointsByName.Add(jointNames[i], i);
		}

		clips = new Clip[Native.RigClipCount(handle)];
		clipsByName = new(clips.Length, StringComparer.OrdinalIgnoreCase);
		for (var i = 0; i < clips.Length; i++)
		{
			var name = Marshal.PtrToStringUTF8(Native.RigClipName(handle, i))
				?? throw new InvalidDataException($"Ozz rig clip {i} has no name.");
			clips[i] = new(name, Native.RigClipDuration(handle, i), i);
			clipsByName.Add(name, clips[i]);
		}
	}

	public static unsafe OzzRig Load(Stream stream)
	{
		using var bytes = new MemoryStream();
		stream.CopyTo(bytes);
		var data = bytes.GetBuffer();
		Span<byte> error = stackalloc byte[512];
		fixed (byte* dataPtr = data)
		fixed (byte* errorPtr = error)
		{
			var handle = Native.RigLoad(dataPtr, checked((int)bytes.Length), errorPtr, error.Length);
			if (handle == 0)
			{
				var length = error.IndexOf((byte)0);
				if (length < 0)
					length = error.Length;
				throw new InvalidDataException(Encoding.UTF8.GetString(error[..length]));
			}
			try
			{
				return new(handle);
			}
			catch
			{
				Native.RigDestroy(handle);
				throw;
			}
		}
	}

	public int FindJoint(string name) => jointsByName.TryGetValue(name, out var index) ? index : -1;
	public OzzJointMask CreateMask() => new(this, jointParents);
	public Clip GetClip(string name) => clipsByName.TryGetValue(name, out var clip)
		? clip : throw new KeyNotFoundException($"Rig clip '{name}' does not exist.");
	public bool TryGetClip(string name, out Clip clip) => clipsByName.TryGetValue(name, out clip!);
	// How fast a looping in-place walk carries the body, in meters per second at its natural rate: the speed its
	// planted feet sweep back (a foot counts as planted within 2 cm of its lowest point). Sampled once per clip.
	public float GroundSpeed(Clip clip, ReadOnlySpan<int> feet)
	{
		if (groundSpeeds.TryGetValue(clip.Index, out var cached))
			return cached;
		const int Samples = 120;
		using var instance = CreateInstance();
		var positions = new Vector3[JointCount];
		var rotations = new Quaternion[JointCount];
		var trail = new Vector3[feet.Length, Samples];
		for (var i = 0; i < Samples; i++)
		{
			instance.Sample([clip.Index], [clip.Duration * i / Samples], [1f], [0], [0], new float[JointCount], positions, rotations);
			for (var f = 0; f < feet.Length; f++)
				trail[f, i] = positions[feet[f]];
		}
		var (distance, time) = (0f, 0f);
		var step = clip.Duration / Samples;
		for (var f = 0; f < feet.Length; f++)
		{
			var lowest = float.MaxValue;
			for (var i = 0; i < Samples; i++)
				lowest = MathF.Min(lowest, trail[f, i].Y);
			for (var i = 1; i < Samples; i++)
				if (trail[f, i].Y < lowest + 0.02f && trail[f, i - 1].Y < lowest + 0.02f)
				{
					var (from, to) = (trail[f, i - 1], trail[f, i]);
					distance += new Vector2(to.X - from.X, to.Z - from.Z).Length();
					time += step;
				}
		}
		return groundSpeeds[clip.Index] = time > 0 ? distance / time : 0;
	}

	public OzzRigInstance CreateInstance()
	{
		ObjectDisposedException.ThrowIf(disposed, this);
		return new(this, Native.InstanceCreate(handle));
	}

	public void Dispose()
	{
		if (disposed)
			return;
		disposed = true;
		Native.RigDestroy(handle);
		GC.SuppressFinalize(this);
	}

	~OzzRig() => Dispose();

	internal static partial class Native
	{
		const string Library = "myth_ozz";

		[LibraryImport(Library, EntryPoint = "myth_ozz_rig_load")]
		internal static unsafe partial nint RigLoad(byte* data, int size, byte* error, int errorCapacity);
		[LibraryImport(Library, EntryPoint = "myth_ozz_rig_destroy")]
		internal static partial void RigDestroy(nint rig);
		[LibraryImport(Library, EntryPoint = "myth_ozz_rig_joint_count")]
		internal static partial int RigJointCount(nint rig);
		[LibraryImport(Library, EntryPoint = "myth_ozz_rig_clip_count")]
		internal static partial int RigClipCount(nint rig);
		[LibraryImport(Library, EntryPoint = "myth_ozz_rig_joint_name")]
		internal static partial nint RigJointName(nint rig, int index);
		[LibraryImport(Library, EntryPoint = "myth_ozz_rig_joint_parent")]
		internal static partial int RigJointParent(nint rig, int index);
		[LibraryImport(Library, EntryPoint = "myth_ozz_rig_clip_name")]
		internal static partial nint RigClipName(nint rig, int index);
		[LibraryImport(Library, EntryPoint = "myth_ozz_rig_clip_duration")]
		internal static partial float RigClipDuration(nint rig, int index);
		[LibraryImport(Library, EntryPoint = "myth_ozz_instance_create")]
		internal static partial nint InstanceCreate(nint rig);
		[LibraryImport(Library, EntryPoint = "myth_ozz_instance_destroy")]
		internal static partial void InstanceDestroy(nint instance);
		[LibraryImport(Library, EntryPoint = "myth_ozz_instance_sample")]
		internal static unsafe partial int InstanceSample(nint instance, int layerCount, int* clips,
			float* times, float* weights, byte* additive, byte* masked, float* jointWeights,
			Vector3* positions, Quaternion* rotations);
	}
}

public sealed class OzzJointMask
{
	readonly OzzRig rig;
	readonly short[] parents;
	internal readonly float[] Weights;
	internal OzzRig Rig => rig;

	internal OzzJointMask(OzzRig rig, short[] parents)
	{
		this.rig = rig;
		this.parents = parents;
		Weights = new float[rig.JointCount];
	}

	public void Set(string joint, float weight, bool descendants = false)
	{
		var root = rig.FindJoint(joint);
		if (root < 0)
			throw new KeyNotFoundException($"Rig joint '{joint}' does not exist.");
		weight = Math.Clamp(weight, 0, 1);
		Weights[root] = weight;
		if (!descendants)
			return;
		for (var i = root + 1; i < parents.Length; i++)
			for (var parent = parents[i]; parent >= 0; parent = parents[parent])
				if (parent == root)
				{
					Weights[i] = weight;
					break;
				}
	}

	internal float this[int joint] => Weights[joint];
}

public sealed class OzzRigInstance : IDisposable
{
	readonly OzzRig rig;
	readonly nint handle;
	bool disposed;

	internal OzzRigInstance(OzzRig rig, nint handle)
	{
		this.rig = rig;
		this.handle = handle != 0 ? handle : throw new InvalidOperationException("Unable to create ozz rig instance.");
	}

	public unsafe void Sample(ReadOnlySpan<int> clips, ReadOnlySpan<float> times,
		ReadOnlySpan<float> weights, ReadOnlySpan<byte> additive, ReadOnlySpan<byte> masked,
		ReadOnlySpan<float> jointWeights, Span<Vector3> positions, Span<Quaternion> rotations)
	{
		ObjectDisposedException.ThrowIf(disposed, this);
		if (clips.Length == 0 || clips.Length != times.Length || clips.Length != weights.Length ||
			clips.Length != additive.Length || clips.Length != masked.Length ||
			jointWeights.Length < clips.Length * rig.JointCount || positions.Length < rig.JointCount ||
			rotations.Length < rig.JointCount)
			throw new ArgumentException("Invalid ozz sample buffers.");
		fixed (int* clipsPtr = clips)
		fixed (float* timesPtr = times)
		fixed (float* weightsPtr = weights)
		fixed (byte* additivePtr = additive)
		fixed (byte* maskedPtr = masked)
		fixed (float* jointWeightsPtr = jointWeights)
		fixed (Vector3* positionsPtr = positions)
		fixed (Quaternion* rotationsPtr = rotations)
			if (OzzRig.Native.InstanceSample(handle, clips.Length, clipsPtr, timesPtr, weightsPtr,
				additivePtr, maskedPtr, jointWeightsPtr, positionsPtr, rotationsPtr) == 0)
				throw new InvalidOperationException("Ozz pose evaluation failed.");
	}

	public void Dispose()
	{
		if (disposed)
			return;
		disposed = true;
		OzzRig.Native.InstanceDestroy(handle);
		GC.KeepAlive(rig);
		GC.SuppressFinalize(this);
	}

	~OzzRigInstance() => Dispose();
}
