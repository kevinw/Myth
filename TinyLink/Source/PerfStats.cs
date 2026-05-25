using System.Diagnostics;

namespace TinyLink;

public enum PerfStat
{
	Update,
	Render,
	World,
	Emissive,
	Normal,
	TileDither,
	Lighting,
	Post,
	Hud,
	FinalComposite,
	Present
}

public readonly record struct PerfSnapshot(
	float Update,
	float Render,
	float World,
	float Emissive,
	float Normal,
	float TileDither,
	float Lighting,
	float Post,
	float Hud,
	float FinalComposite,
	float Present
);

public static class PerfStats
{
	private static readonly double[] Totals = new double[Enum.GetValues<PerfStat>().Length];
	private static readonly int[] Counts = new int[Totals.Length];

	public static PerfSnapshot Snapshot { get; private set; }

	public static PerfScope Time(PerfStat stat)
		=> new(stat);

	public static void Record(PerfStat stat, TimeSpan elapsed)
		=> Record(stat, elapsed.TotalMilliseconds);

	public static void Record(PerfStat stat, double milliseconds)
	{
		var index = (int)stat;
		Totals[index] += milliseconds;
		Counts[index]++;
	}

	public static void Publish()
	{
		Snapshot = new(
			Average(PerfStat.Update),
			Average(PerfStat.Render),
			Average(PerfStat.World),
			Average(PerfStat.Emissive),
			Average(PerfStat.Normal),
			Average(PerfStat.TileDither),
			Average(PerfStat.Lighting),
			Average(PerfStat.Post),
			Average(PerfStat.Hud),
			Average(PerfStat.FinalComposite),
			Average(PerfStat.Present));

		Array.Clear(Totals);
		Array.Clear(Counts);
	}

	private static float Average(PerfStat stat)
	{
		var index = (int)stat;
		return Counts[index] > 0 ? (float)(Totals[index] / Counts[index]) : 0;
	}

	public readonly struct PerfScope : IDisposable
	{
		private readonly PerfStat stat;
		private readonly long start;

		internal PerfScope(PerfStat stat)
		{
			this.stat = stat;
			start = Stopwatch.GetTimestamp();
		}

		public void Dispose()
			=> Record(stat, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
	}
}
