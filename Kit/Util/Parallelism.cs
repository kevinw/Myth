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

	// Hands one self-contained chunk of work to a background thread so the caller can get on
	// with something else and join later. Inline on the browser, where there is no thread to
	// wait on -- the returned task is already complete, so the join is a no-op, not a deadlock.
	public static Task Run(Action action)
	{
		if (Enabled)
			return Task.Run(action);

		try { action(); }
		catch (Exception e) { return Task.FromException(e); }
		return Task.CompletedTask;
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
