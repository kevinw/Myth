using Foster.Framework;

namespace Kit;

// Who's inside a set of rects, frame to frame. Each Update tests every candidate's bounds (null skips it)
// against the zones -- their hull first, so a candidate nowhere near costs one rect test -- and diffs against
// last frame: Inside holds who's in now, Entered/Exited who crossed. Allocation-free once warm; pass a static
// lambda so the probe isn't reallocated each call.
public sealed class ZoneSensor<T> where T : class
{
	public readonly List<RectInt> Zones = [];
	public readonly List<(T Item, RectInt Bounds)> Inside = [];
	public readonly List<T> Entered = [], Exited = [];
	HashSet<T> was = [], now = [];

	public void Update(List<T> candidates, Func<T, RectInt?> bounds)
	{
		Inside.Clear();
		Entered.Clear();
		Exited.Clear();
		now.Clear();

		if (Zones.Count > 0)
		{
			var hull = Zones[0];
			foreach (var zone in Zones)
				hull = hull.Conflate(zone);

			foreach (var item in candidates)
			{
				if (bounds(item) is not { } b || !hull.Overlaps(b) || !InAnyZone(b))
					continue;
				Inside.Add((item, b));
				now.Add(item);
				if (!was.Contains(item))
					Entered.Add(item);
			}
		}

		foreach (var item in was)
			if (!now.Contains(item))
				Exited.Add(item);
		(was, now) = (now, was);
	}

	bool InAnyZone(in RectInt bounds)
	{
		foreach (var zone in Zones)
			if (zone.Overlaps(bounds))
				return true;
		return false;
	}
}
