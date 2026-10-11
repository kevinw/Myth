using System.Diagnostics;

namespace Myth;

public enum PerfStat
{
	Update,
	Render,
	World,
	Normal,
	TileDither,
	Lighting,
	Post,
	FinalComposite,
	Present
}

public enum PerfDetail
{
	DeferredRoomPreviewDisposal,
	Hotkeys,
	GameUpdate,
	EditorNavigation,
	BatchBuild,
	EditorWorld,
	DrawEditorRooms,
	ActiveRoomPreview,
	RoomPreviewCacheCheck,
	RoomPreviewBuild,
	EditorInteraction,
	EditorSync,
	ImGuiLayout,
	PaletteImGui,
	ToolbarImGui,
	DebugMessageImGui,
	FpsOverlayImGui
}

public readonly record struct PerfSnapshot(
	float Update,
	float Render,
	float World,
	float Normal,
	float TileDither,
	float Lighting,
	float Post,
	float FinalComposite,
	float Present
);

public static class PerfStats
{
	private static readonly double[] Totals = new double[Enum.GetValues<PerfStat>().Length];
	private static readonly int[] Counts = new int[Totals.Length];
	// Never published: whole-run averages since ResetRun, for --max-frames A/B runs.
	private static readonly double[] RunTotals = new double[Totals.Length];
	private static readonly int[] RunCounts = new int[Totals.Length];
	private static readonly double[] DetailTotals = new double[Enum.GetValues<PerfDetail>().Length];
	private static readonly int[] DetailCounts = new int[DetailTotals.Length];
	private static readonly string[] DetailNames =
	[
		"DisposePreviews",
		"Hotkeys",
		"GameUpdate",
		"EditorNav",
		"BatchBuild",
		"EditorWorld",
		"DrawRooms",
		"ActivePreview",
		"PreviewCheck",
		"PreviewBuild",
		"EditorInput",
		"EditorSync",
		"ImGuiLayout",
		"Palette",
		"Toolbar",
		"DebugMsg",
		"FpsOverlay"
	];

	public static PerfSnapshot Snapshot { get; private set; }
	public static string DetailSummary { get; private set; } = string.Empty;

	public static PerfScope Time(PerfStat stat)
		=> new(stat);

	public static PerfDetailScope Time(PerfDetail detail)
		=> new(detail);

	public static void Record(PerfStat stat, TimeSpan elapsed)
		=> Record(stat, elapsed.TotalMilliseconds);

	public static void Record(PerfStat stat, double milliseconds)
	{
		var index = (int)stat;
		Totals[index] += milliseconds;
		Counts[index]++;
		RunTotals[index] += milliseconds;
		RunCounts[index]++;
	}

	public static void ResetRun()
	{
		Array.Clear(RunTotals);
		Array.Clear(RunCounts);
	}

	public static string RunSummary()
	{
		var summary = new System.Text.StringBuilder();
		foreach (var stat in Enum.GetValues<PerfStat>())
			if (RunCounts[(int)stat] > 0)
				summary.Append($"{stat} {RunTotals[(int)stat] / RunCounts[(int)stat]:F3}  ");
		return summary.ToString();
	}

	public static void Record(PerfDetail detail, double milliseconds)
	{
		var index = (int)detail;
		DetailTotals[index] += milliseconds;
		DetailCounts[index]++;
	}

	public static void Publish()
	{
		Snapshot = new(
			Average(PerfStat.Update),
			Average(PerfStat.Render),
			Average(PerfStat.World),
			Average(PerfStat.Normal),
			Average(PerfStat.TileDither),
			Average(PerfStat.Lighting),
			Average(PerfStat.Post),
			Average(PerfStat.FinalComposite),
			Average(PerfStat.Present));
		DetailSummary = BuildDetailSummary();

		Array.Clear(Totals);
		Array.Clear(Counts);
		Array.Clear(DetailTotals);
		Array.Clear(DetailCounts);
	}

	private static float Average(PerfStat stat)
	{
		var index = (int)stat;
		return Counts[index] > 0 ? (float)(Totals[index] / Counts[index]) : 0;
	}

	private static string BuildDetailSummary()
	{
		Span<int> top = stackalloc int[6];
		Span<float> values = stackalloc float[6];
		var count = 0;

		for (var i = 0; i < DetailTotals.Length; i++)
		{
			if (DetailCounts[i] <= 0)
				continue;

			var value = (float)(DetailTotals[i] / DetailCounts[i]);
			if (value < 0.05f)
				continue;

			var insert = count;
			while (insert > 0 && value > values[insert - 1])
				insert--;

			if (insert >= top.Length)
				continue;

			if (count < top.Length)
				count++;
			for (var move = count - 1; move > insert; move--)
			{
				top[move] = top[move - 1];
				values[move] = values[move - 1];
			}
			top[insert] = i;
			values[insert] = value;
		}

		if (count <= 0)
			return string.Empty;

		var builder = new System.Text.StringBuilder();
		for (var i = 0; i < count; i++)
		{
			if (i > 0)
				builder.Append('\n');
			builder.Append(DetailNames[top[i]]);
			builder.Append(' ');
			builder.Append(values[i].ToString("0.0"));
		}
		return builder.ToString();
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

	public readonly struct PerfDetailScope : IDisposable
	{
		private readonly PerfDetail detail;
		private readonly long start;

		internal PerfDetailScope(PerfDetail detail)
		{
			this.detail = detail;
			start = Stopwatch.GetTimestamp();
		}

		public void Dispose()
			=> Record(detail, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
	}
}
