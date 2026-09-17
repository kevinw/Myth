using System.Threading.Tasks;

namespace Myth;

// Small gate around data-parallel work: runs in parallel on desktop (multi-core,
// real threads) and falls back to serial on the browser (wasm has no guaranteed
// threads). Use only for embarrassingly-parallel work whose tasks touch disjoint state.
public static class Par
{
	public static readonly bool Enabled = !OperatingSystem.IsBrowser() && Environment.ProcessorCount > 1;

	public static void Invoke(params Action[] actions)
	{
		if (Enabled)
			Parallel.Invoke(actions);
		else
			foreach (var action in actions)
				action();
	}

	public static void For(int fromInclusive, int toExclusive, Action<int> body)
	{
		if (Enabled)
			Parallel.For(fromInclusive, toExclusive, body);
		else
			for (var i = fromInclusive; i < toExclusive; i++)
				body(i);
	}
}
