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
	readonly Clip[] clips;
	readonly Dictionary<string, int> jointsByName;
	readonly Dictionary<string, Clip> clipsByName;
	bool disposed;

	public int JointCount => jointNames.Length;
	public IReadOnlyList<Clip> Clips => clips;

	unsafe OzzRig(nint handle)
	{
		this.handle = handle;
		jointNames = new string[Native.RigJointCount(handle)];
		jointsByName = new(jointNames.Length, StringComparer.OrdinalIgnoreCase);
		for (var i = 0; i < jointNames.Length; i++)
		{
			jointNames[i] = Marshal.PtrToStringUTF8(Native.RigJointName(handle, i))
				?? throw new InvalidDataException($"Ozz rig joint {i} has no name.");
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
	public Clip GetClip(string name) => clipsByName.TryGetValue(name, out var clip)
		? clip : throw new KeyNotFoundException($"Rig clip '{name}' does not exist.");
	public bool TryGetClip(string name, out Clip clip) => clipsByName.TryGetValue(name, out clip!);
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
			float* times, float* weights, Vector3* positions);
	}
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
		ReadOnlySpan<float> weights, Span<Vector3> positions)
	{
		ObjectDisposedException.ThrowIf(disposed, this);
		if (clips.Length == 0 || clips.Length != times.Length || clips.Length != weights.Length ||
			positions.Length < rig.JointCount)
			throw new ArgumentException("Invalid ozz sample buffers.");
		fixed (int* clipsPtr = clips)
		fixed (float* timesPtr = times)
		fixed (float* weightsPtr = weights)
		fixed (Vector3* positionsPtr = positions)
			if (OzzRig.Native.InstanceSample(handle, clips.Length, clipsPtr, timesPtr, weightsPtr, positionsPtr) == 0)
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
