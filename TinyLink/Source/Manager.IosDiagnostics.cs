using System.Runtime.InteropServices;

namespace TinyLink;

public partial class Manager
{
#if IOS
	private int iosMemoryFrameLogCount;

	private static double? GetPhysicalFootprintMb()
	{
		return TryGetTaskVmInfo(out var info)
			? info.PhysFootprint / (1024.0 * 1024.0)
			: null;
	}

	private static double? GetResidentMemoryMb()
	{
		return TryGetTaskVmInfo(out var info)
			? info.ResidentSize / (1024.0 * 1024.0)
			: null;
	}

	private static bool TryGetTaskVmInfo(out TaskVmInfo info)
	{
		info = default;
		var count = Marshal.SizeOf<TaskVmInfo>() / sizeof(int);
		return task_info(mach_task_self(), TaskVmInfoFlavor, out info, ref count) == 0;
	}

	private static string FormatMb(double? value)
		=> value.HasValue ? $"{value.Value:F1}MB" : "unknown";

	private const int TaskVmInfoFlavor = 22;

	[DllImport("/usr/lib/libSystem.dylib")]
	private static extern IntPtr mach_task_self();

	[DllImport("/usr/lib/libSystem.dylib")]
	private static extern int task_info(IntPtr targetTask, int flavor, out TaskVmInfo taskInfo, ref int taskInfoCount);

	[StructLayout(LayoutKind.Sequential)]
	private struct TaskVmInfo
	{
		public ulong VirtualSize;
		public int RegionCount;
		public int PageSize;
		public ulong ResidentSize;
		public ulong ResidentSizePeak;
		public ulong Device;
		public ulong DevicePeak;
		public ulong Internal;
		public ulong InternalPeak;
		public ulong External;
		public ulong ExternalPeak;
		public ulong Reusable;
		public ulong ReusablePeak;
		public ulong PurgeableVolatilePmap;
		public ulong PurgeableVolatileResident;
		public ulong PurgeableVolatileVirtual;
		public ulong Compressed;
		public ulong CompressedPeak;
		public ulong CompressedLifetime;
		public ulong PhysFootprint;
	}
#endif
}
