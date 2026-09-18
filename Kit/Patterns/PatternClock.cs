namespace Kit.Patterns;

// Turns game time into pattern time. Advance it once per frame, then query patterns over Span:
// every hap with an onset in that span is a trigger that frame, and no trigger can be missed or
// fire twice, because one frame's end is the next frame's begin exactly.
public sealed class PatternClock
{
	// Cycles per second. One cycle is the span a bare pattern like "x ~ x x" fills.
	public double Rate { get; set; } = 0.5;

	public Fraction Position { get; private set; }
	public Arc Span { get; private set; }

	// A frame that ran long (a room load, a breakpoint) would otherwise fire every trigger it
	// skipped all at once.
	static double MaxCatchUpSeconds => 0.25;

	// Time advances on a fixed grid of ticks per cycle. Frame deltas are arbitrary reals, and
	// accumulating them as exact fractions would grow denominators until they overflow.
	const long TicksPerCycle = 1 << 20;
	double cycles;

	public void Advance(double deltaSeconds)
	{
		cycles += Math.Clamp(deltaSeconds, 0, MaxCatchUpSeconds) * Rate;
		Span = new(Position, new Fraction((long)Math.Round(cycles * TicksPerCycle), TicksPerCycle));
		Position = Span.End;
	}

	public void Reset(Fraction position = default)
	{
		cycles = position.ToDouble();
		Position = position;
		Span = new(position, position);
	}

	// Haps whose onset falls in this frame.
	public void Triggers<T>(Pattern<T> pattern, List<Hap<T>> output)
	{
		if (Span.End == Span.Begin)
			return;
		var start = output.Count;
		pattern.Query(Span, output);
		for (var i = output.Count - 1; i >= start; i--)
			if (!output[i].HasOnset)
				output.RemoveAt(i);
	}

	// The haps covering this instant: a pattern read as state (is the block solid now?) rather than
	// as triggers. A step ending exactly now has finished, and doesn't come back.
	public void ValuesNow<T>(Pattern<T> pattern, List<Hap<T>> output)
		=> pattern.Query(new(Position, Position), output);

	// The value of a continuous pattern right now, for a sweep, a fade or a spread.
	public double Sample(Pattern<double> signal)
	{
		var haps = Scratch<double>.Rent();
		signal.Query(new(Position, Position), haps);
		var value = haps.Count > 0 ? haps[^1].Value : 0;
		Scratch<double>.Return(haps);
		return value;
	}
}
