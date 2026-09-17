namespace Kit.Patterns;

// A pattern answers "which haps are active in this span of cycles?". Build a pattern once, then
// query it every frame: queries append to the caller's list and reuse pooled scratch lists, so
// steady-state querying doesn't allocate.
public abstract class Pattern<T>
{
	public abstract void Query(Arc span, List<Hap<T>> output);

	public Pattern<T> Fast(Fraction factor) => factor <= 0 ? Pattern.Silence<T>() : factor == 1 ? this : new FastPattern<T>(this, factor);
	public Pattern<T> Fast(Pattern<Fraction> factor) => new PatternedPattern<T, Fraction, FastOp<T>>(this, factor);
	public Pattern<T> Slow(Fraction factor) => factor <= 0 ? Pattern.Silence<T>() : Fast(1 / factor);
	public Pattern<T> Slow(Pattern<Fraction> factor) => new PatternedPattern<T, Fraction, SlowOp<T>>(this, factor);
	public Pattern<T> Late(Fraction offset) => offset == 0 ? this : new ShiftPattern<T>(this, offset);
	public Pattern<T> Late(Pattern<Fraction> offset) => new PatternedPattern<T, Fraction, LateOp<T>>(this, offset);
	public Pattern<T> Early(Fraction offset) => Late(-offset);
	public Pattern<T> Early(Pattern<Fraction> offset) => new PatternedPattern<T, Fraction, EarlyOp<T>>(this, offset);
	public Pattern<T> Rev() => new RevPattern<T>(this);
	public Pattern<T> RepeatCycles(long count) => count <= 1 ? this : new RepeatCyclesPattern<T>(this, count);

	// Squeezes each cycle into [begin, end) of the same cycle, leaving the rest silent.
	public Pattern<T> Compress(Fraction begin, Fraction end)
		=> begin >= end || begin < 0 || end > 1 ? Pattern.Silence<T>() : new FastGapPattern<T>(this, 1 / (end - begin)).Late(begin);

	// Keeps this pattern's values, cut to the true steps of the structure pattern.
	public Pattern<T> Struct(Pattern<bool> structure) => new StructPattern<T>(this, structure);

	public Pattern<T> Euclid(int pulses, int steps, int rotation = 0)
		=> Struct(Pattern.FastCat(Array.ConvertAll(Pattern.Bjorklund(pulses, steps, rotation), static step => Pattern.Pure(step))));

	public Pattern<TResult> Select<TResult>(Func<T, TResult> map) => new SelectPattern<T, TResult>(this, map);
}

public static class Pattern
{
	public static Pattern<T> Pure<T>(T value) => new PurePattern<T>(value);
	public static Pattern<T> Silence<T>() => SilencePattern<T>.Instance;

	public static Pattern<T> Stack<T>(params Pattern<T>[] patterns) => patterns.Length switch
	{
		0 => Silence<T>(),
		1 => patterns[0],
		_ => new StackPattern<T>(patterns),
	};

	// One pattern per cycle; each pattern resumes from its own next cycle when its turn comes.
	public static Pattern<T> SlowCat<T>(params Pattern<T>[] patterns) => patterns.Length switch
	{
		0 => Silence<T>(),
		1 => patterns[0],
		_ => new SlowCatPattern<T>(patterns),
	};

	// All patterns squeezed into one cycle, in equal steps.
	public static Pattern<T> FastCat<T>(params Pattern<T>[] patterns) => SlowCat(patterns).Fast(Math.Max(patterns.Length, 1));

	// All patterns squeezed into one cycle, each taking a share proportional to its weight.
	public static Pattern<T> TimeCat<T>(params (Fraction Weight, Pattern<T> Pattern)[] parts)
	{
		if (parts.Length == 1)
			return parts[0].Pattern;
		var total = Fraction.Zero;
		foreach (var part in parts)
			total += part.Weight;
		var compressed = new List<Pattern<T>>(parts.Length);
		var begin = Fraction.Zero;
		foreach (var (weight, pattern) in parts)
		{
			if (weight == 0)
				continue;
			var end = begin + weight;
			compressed.Add(pattern.Compress(begin / total, end / total));
			begin = end;
		}
		return Stack(compressed.ToArray());
	}

	// Euclidean rhythm: pulses spread as evenly as possible over steps, rotated right. Negative
	// pulses invert the rhythm.
	public static bool[] Bjorklund(int pulses, int steps, int rotation = 0)
	{
		var ons = Math.Abs(pulses);
		if (steps <= 0 || ons > steps)
			return [];
		var heads = new List<List<bool>>();
		var tails = new List<List<bool>>();
		for (var i = 0; i < ons; i++)
			heads.Add([true]);
		for (var i = 0; i < steps - ons; i++)
			tails.Add([false]);
		while (Math.Min(heads.Count, tails.Count) > 1)
		{
			var pairs = Math.Min(heads.Count, tails.Count);
			var merged = new List<List<bool>>(pairs);
			for (var i = 0; i < pairs; i++)
				merged.Add([.. heads[i], .. tails[i]]);
			var remainder = heads.Count > tails.Count ? heads.GetRange(pairs, heads.Count - pairs) : tails.GetRange(pairs, tails.Count - pairs);
			heads = merged;
			tails = remainder;
		}
		var result = new bool[steps];
		var index = 0;
		foreach (var group in heads)
			foreach (var step in group)
				result[index++] = step;
		foreach (var group in tails)
			foreach (var step in group)
				result[index++] = step;
		if (pulses < 0)
			for (var i = 0; i < steps; i++)
				result[i] = !result[i];
		var shift = ((rotation % steps) + steps) % steps;
		return shift == 0 ? result : [.. result[(steps - shift)..], .. result[..(steps - shift)]];
	}
}

static class Scratch<T>
{
	[ThreadStatic] static Stack<List<Hap<T>>>? pool;

	public static List<Hap<T>> Rent() => pool is { Count: > 0 } ? pool.Pop() : [];

	public static void Return(List<Hap<T>> list)
	{
		list.Clear();
		(pool ??= new()).Push(list);
	}
}

static class Queries
{
	public static void Fast<T>(Pattern<T> source, Fraction factor, Arc span, List<Hap<T>> output)
	{
		if (factor <= 0)
			return;
		var start = output.Count;
		source.Query(span.Scale(factor), output);
		var inverse = 1 / factor;
		for (var i = start; i < output.Count; i++)
			output[i] = output[i].Scale(inverse);
	}

	public static void Late<T>(Pattern<T> source, Fraction offset, Arc span, List<Hap<T>> output)
	{
		var start = output.Count;
		source.Query(span.Shift(-offset), output);
		for (var i = start; i < output.Count; i++)
			output[i] = output[i].Shift(offset);
	}
}

sealed class SilencePattern<T> : Pattern<T>
{
	public static readonly SilencePattern<T> Instance = new();
	public override void Query(Arc span, List<Hap<T>> output) { }
}

sealed class PurePattern<T>(T value) : Pattern<T>
{
	public T Value => value;

	public override void Query(Arc span, List<Hap<T>> output)
	{
		foreach (var cycle in span.Cycles())
		{
			var begin = cycle.Begin.Floor();
			output.Add(new(new Arc(begin, begin + 1), cycle, value));
		}
	}
}

sealed class StackPattern<T>(Pattern<T>[] patterns) : Pattern<T>
{
	public override void Query(Arc span, List<Hap<T>> output)
	{
		foreach (var pattern in patterns)
			pattern.Query(span, output);
	}
}

sealed class SlowCatPattern<T>(Pattern<T>[] patterns) : Pattern<T>
{
	public override void Query(Arc span, List<Hap<T>> output)
	{
		foreach (var cycle in span.Cycles())
		{
			var sam = cycle.Begin.Floor();
			var index = (int)(((sam.Numerator % patterns.Length) + patterns.Length) % patterns.Length);
			Queries.Late(patterns[index], sam - (cycle.Begin / patterns.Length).Floor(), cycle, output);
		}
	}
}

sealed class FastPattern<T>(Pattern<T> source, Fraction factor) : Pattern<T>
{
	public override void Query(Arc span, List<Hap<T>> output) => Queries.Fast(source, factor, span, output);
}

sealed class ShiftPattern<T>(Pattern<T> source, Fraction offset) : Pattern<T>
{
	public override void Query(Arc span, List<Hap<T>> output) => Queries.Late(source, offset, span, output);
}

sealed class RepeatCyclesPattern<T>(Pattern<T> source, long count) : Pattern<T>
{
	public override void Query(Arc span, List<Hap<T>> output)
	{
		foreach (var cycle in span.Cycles())
		{
			var sam = cycle.Begin.Floor();
			Queries.Late(source, sam - (sam / count).Floor(), cycle, output);
		}
	}
}

sealed class RevPattern<T>(Pattern<T> source) : Pattern<T>
{
	public override void Query(Arc span, List<Hap<T>> output)
	{
		foreach (var cycle in span.Cycles())
		{
			var mirror = cycle.Begin.Floor() * 2 + 1;
			var start = output.Count;
			source.Query(Reflect(cycle, mirror), output);
			for (var i = start; i < output.Count; i++)
			{
				var hap = output[i];
				output[i] = new(hap.Whole is { } whole ? Reflect(whole, mirror) : null, Reflect(hap.Part, mirror), hap.Value);
			}
		}
	}

	static Arc Reflect(Arc arc, Fraction mirror) => new(mirror - arc.End, mirror - arc.Begin);
}

// Speeds each cycle up by factor, starting at the cycle's beginning, and leaves the rest of it silent.
sealed class FastGapPattern<T>(Pattern<T> source, Fraction factor) : Pattern<T>
{
	public override void Query(Arc span, List<Hap<T>> output)
	{
		foreach (var cycle in span.Cycles())
		{
			var sam = cycle.Begin.Floor();
			var begin = Fraction.Min((cycle.Begin - sam) * factor, 1);
			if (begin >= 1)
				continue;
			var end = Fraction.Min((cycle.End - sam) * factor, 1);
			var start = output.Count;
			source.Query(new Arc(sam + begin, sam + end), output);
			for (var i = start; i < output.Count; i++)
			{
				var hap = output[i];
				var partSam = hap.Part.Begin.Floor();
				var part = new Arc(
					partSam + Fraction.Min((hap.Part.Begin - partSam) / factor, 1),
					partSam + Fraction.Min((hap.Part.End - partSam) / factor, 1));
				var whole = hap.Whole is { } w
					? new Arc(part.Begin - (hap.Part.Begin - w.Begin) / factor, part.End + (w.End - hap.Part.End) / factor)
					: (Arc?)null;
				output[i] = new(whole, part, hap.Value);
			}
		}
	}
}

sealed class StructPattern<T>(Pattern<T> source, Pattern<bool> structure) : Pattern<T>
{
	public override void Query(Arc span, List<Hap<T>> output)
	{
		var steps = Scratch<bool>.Rent();
		var values = Scratch<T>.Rent();
		structure.Query(span, steps);
		foreach (var step in steps)
		{
			if (!step.Value)
				continue;
			values.Clear();
			source.Query(step.WholeOrPart, values);
			foreach (var value in values)
				if (value.Part.TryIntersect(step.Part, out var part))
					output.Add(new(step.Whole, part, value.Value));
		}
		Scratch<T>.Return(values);
		Scratch<bool>.Return(steps);
	}
}

sealed class SelectPattern<T, TResult>(Pattern<T> source, Func<T, TResult> map) : Pattern<TResult>
{
	public override void Query(Arc span, List<Hap<TResult>> output)
	{
		var haps = Scratch<T>.Rent();
		source.Query(span, haps);
		foreach (var hap in haps)
			output.Add(new(hap.Whole, hap.Part, map(hap.Value)));
		Scratch<T>.Return(haps);
	}
}

interface IPatternedOp<T, TArg>
{
	static abstract void Query(Pattern<T> source, TArg arg, Arc span, List<Hap<T>> output);
}

// A patterned argument: for each argument hap, the operation applies over that hap's span, and
// the results keep their own structure (Tidal's inner join).
sealed class PatternedPattern<T, TArg, TOp>(Pattern<T> source, Pattern<TArg> argument) : Pattern<T>
	where TOp : IPatternedOp<T, TArg>
{
	public override void Query(Arc span, List<Hap<T>> output)
	{
		var arguments = Scratch<TArg>.Rent();
		argument.Query(span, arguments);
		foreach (var arg in arguments)
			TOp.Query(source, arg.Value, arg.Part, output);
		Scratch<TArg>.Return(arguments);
	}
}

readonly struct FastOp<T> : IPatternedOp<T, Fraction>
{
	public static void Query(Pattern<T> source, Fraction factor, Arc span, List<Hap<T>> output) => Queries.Fast(source, factor, span, output);
}

readonly struct SlowOp<T> : IPatternedOp<T, Fraction>
{
	public static void Query(Pattern<T> source, Fraction factor, Arc span, List<Hap<T>> output)
	{
		if (factor > 0)
			Queries.Fast(source, 1 / factor, span, output);
	}
}

readonly struct LateOp<T> : IPatternedOp<T, Fraction>
{
	public static void Query(Pattern<T> source, Fraction offset, Arc span, List<Hap<T>> output) => Queries.Late(source, offset, span, output);
}

readonly struct EarlyOp<T> : IPatternedOp<T, Fraction>
{
	public static void Query(Pattern<T> source, Fraction offset, Arc span, List<Hap<T>> output) => Queries.Late(source, -offset, span, output);
}
