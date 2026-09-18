namespace Kit.Patterns;

// Continuous patterns: they have a value at every moment rather than events with a start and end,
// so their haps have no Whole. Sample them with Segment, or read one directly for a sweep or a fade.
public static class Signal
{
	public static Pattern<double> Of(Func<double, double> function) => new SignalPattern(function);

	public static readonly Pattern<double> Time = Of(static t => t);
	public static readonly Pattern<double> Saw = Of(static t => Mod(t, 1));
	public static readonly Pattern<double> ISaw = Of(static t => 1 - Mod(t, 1));
	public static readonly Pattern<double> Sine = Of(static t => (Math.Sin(Math.PI * 2 * t) + 1) / 2);
	public static readonly Pattern<double> Cosine = Sine.Early(new Fraction(1, 4));
	public static readonly Pattern<double> Square = Of(static t => Math.Floor(Mod(t * 2, 2)));
	public static readonly Pattern<double> ISquare = Of(static t => 1 - Math.Floor(Mod(t * 2, 2)));
	public static readonly Pattern<double> Tri = Pattern.FastCat(Saw, ISaw);
	public static readonly Pattern<double> Rand = Of(TimeToRand);
	public static readonly Pattern<double> Perlin = Of(static t =>
	{
		var previous = Math.Floor(t);
		var fade = t - previous;
		var smooth = 6 * Math.Pow(fade, 5) - 15 * Math.Pow(fade, 4) + 10 * Math.Pow(fade, 3);
		var from = TimeToRand(previous);
		return from + smooth * (TimeToRand(previous + 1) - from);
	});

	// Whole numbers 0..count-1.
	public static Pattern<double> IRand(int count) => Rand.Select(r => Math.Truncate(r * count));

	static double Mod(double value, double by) => ((value % by) + by) % by;

	// Strudel's "legacy" random: a 32-bit xorshift over the time, so a pattern plays the same way
	// every run and the same moment always gives the same number.
	static double TimeToRand(double t)
	{
		var seed = Xorwise((int)Math.Truncate((t / 300 - Math.Truncate(t / 300)) * 536870912.0));
		return Math.Abs((seed % 536870912) / 536870912.0);
	}

	static int Xorwise(int x)
	{
		unchecked
		{
			var a = (x << 13) ^ x;
			var b = (a >> 17) ^ a;
			return (b << 5) ^ b;
		}
	}
}

public static class NumberPatterns
{
	// Scales unipolar values (0..1), as the signals produce, into a useful range.
	public static Pattern<double> Range(this Pattern<double> pattern, double min, double max)
		=> pattern.Select(value => value * (max - min) + min);
}

sealed class SignalPattern(Func<double, double> function) : Pattern<double>
{
	public override void Query(Arc span, List<Hap<double>> output)
		=> output.Add(new(null, span, function(span.Begin.ToDouble())));
}

// Keeps the haps whose matching random value is above the threshold.
sealed class DegradePattern<T>(Pattern<T> source, Pattern<double> random, double amount) : Pattern<T>
{
	public override void Query(Arc span, List<Hap<T>> output)
	{
		var haps = Scratch<T>.Rent();
		var randoms = Scratch<double>.Rent();
		source.Query(span, haps);
		foreach (var hap in haps)
		{
			randoms.Clear();
			random.Query(hap.WholeOrPart, randoms);
			foreach (var value in randoms)
				if (value.Value > amount && value.Part.TryIntersect(hap.Part, out var part))
					output.Add(new(hap.Whole, part, hap.Value));
		}
		Scratch<double>.Return(randoms);
		Scratch<T>.Return(haps);
	}
}

// Picks one of the options per hap of the chooser, keeping the chosen pattern's own structure.
sealed class ChoosePattern<T>(Pattern<double> chooser, Pattern<T>[] options) : Pattern<T>
{
	public override void Query(Arc span, List<Hap<T>> output)
	{
		var choices = Scratch<double>.Rent();
		chooser.Query(span, choices);
		foreach (var choice in choices)
		{
			var index = Math.Clamp((int)Math.Floor(choice.Value * options.Length), 0, options.Length - 1);
			options[index].Query(choice.Part, output);
		}
		Scratch<double>.Return(choices);
	}
}
