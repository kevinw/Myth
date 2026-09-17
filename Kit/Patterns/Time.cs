using System.Globalization;

namespace Kit.Patterns;

// Exact rational time, in cycles. Pattern math stays exact so adjacent queries share boundaries.
public readonly struct Fraction : IEquatable<Fraction>, IComparable<Fraction>
{
	public readonly long Numerator;
	public readonly long Denominator;

	public static readonly Fraction Zero = new(0);
	public static readonly Fraction One = new(1);

	public Fraction(long numerator, long denominator = 1)
		=> this = Reduce(numerator, denominator);

	Fraction(long numerator, long denominator, bool _)
	{
		Numerator = numerator;
		Denominator = denominator;
	}

	static Fraction Reduce(Int128 numerator, Int128 denominator)
	{
		if (denominator == 0)
			throw new DivideByZeroException("Fraction with a zero denominator.");
		if (denominator < 0)
		{
			numerator = -numerator;
			denominator = -denominator;
		}
		var gcd = Gcd(Int128.Abs(numerator), denominator);
		if (gcd > 1)
		{
			numerator /= gcd;
			denominator /= gcd;
		}
		return new Fraction(checked((long)numerator), checked((long)denominator), true);
	}

	static Int128 Gcd(Int128 a, Int128 b)
	{
		while (b != 0)
			(a, b) = (b, a % b);
		return a;
	}

	// Closest fraction with a bounded denominator (continued fractions), for converting clock time.
	public static Fraction FromDouble(double value, long maxDenominator = 1 << 20)
	{
		if (!double.IsFinite(value))
			throw new ArgumentOutOfRangeException(nameof(value));
		var whole = Math.Floor(value);
		var rest = value - whole;
		long previousP = 1, previousQ = 0, p = 0, q = 1;
		for (var i = 0; i < 64 && rest > 1e-12; i++)
		{
			var inverse = 1 / rest;
			if (inverse > maxDenominator)
				break;
			var term = (long)Math.Floor(inverse);
			var nextQ = term * q + previousQ;
			if (nextQ > maxDenominator)
				break;
			(previousP, previousQ, p, q) = (p, q, term * p + previousP, nextQ);
			rest = inverse - term;
		}
		return new Fraction((long)whole) + new Fraction(p, q);
	}

	public Fraction Floor() => new(FloorToLong());
	public long FloorToLong() => Numerator >= 0 ? Numerator / Denominator : -((-Numerator + Denominator - 1) / Denominator);
	public Fraction CyclePosition() => this - Floor();
	public double ToDouble() => (double)Numerator / Denominator;

	public static Fraction Min(Fraction a, Fraction b) => a < b ? a : b;
	public static Fraction Max(Fraction a, Fraction b) => a > b ? a : b;

	public static implicit operator Fraction(long value) => new(value, 1, true);
	public static Fraction operator -(Fraction a) => new(-a.Numerator, a.Denominator, true);
	public static Fraction operator +(Fraction a, Fraction b) => a.Denominator == b.Denominator
		? Reduce((Int128)a.Numerator + b.Numerator, a.Denominator)
		: Reduce((Int128)a.Numerator * b.Denominator + (Int128)b.Numerator * a.Denominator, (Int128)a.Denominator * b.Denominator);
	public static Fraction operator -(Fraction a, Fraction b) => a + -b;
	public static Fraction operator *(Fraction a, Fraction b) => Reduce((Int128)a.Numerator * b.Numerator, (Int128)a.Denominator * b.Denominator);
	public static Fraction operator /(Fraction a, Fraction b) => Reduce((Int128)a.Numerator * b.Denominator, (Int128)a.Denominator * b.Numerator);

	public int CompareTo(Fraction other) => ((Int128)Numerator * other.Denominator).CompareTo((Int128)other.Numerator * Denominator);
	public static bool operator <(Fraction a, Fraction b) => a.CompareTo(b) < 0;
	public static bool operator >(Fraction a, Fraction b) => a.CompareTo(b) > 0;
	public static bool operator <=(Fraction a, Fraction b) => a.CompareTo(b) <= 0;
	public static bool operator >=(Fraction a, Fraction b) => a.CompareTo(b) >= 0;
	public bool Equals(Fraction other) => Numerator == other.Numerator && Denominator == other.Denominator;
	public override bool Equals(object? obj) => obj is Fraction other && Equals(other);
	public static bool operator ==(Fraction a, Fraction b) => a.Equals(b);
	public static bool operator !=(Fraction a, Fraction b) => !a.Equals(b);
	public override int GetHashCode() => HashCode.Combine(Numerator, Denominator);

	public override string ToString() => Denominator == 1
		? Numerator.ToString(CultureInfo.InvariantCulture)
		: string.Create(CultureInfo.InvariantCulture, $"{Numerator}/{Denominator}");
}

// A half-open span of time [Begin, End).
public readonly record struct Arc(Fraction Begin, Fraction End)
{
	public Arc Shift(Fraction offset) => new(Begin + offset, End + offset);
	public Arc Scale(Fraction factor) => new(Begin * factor, End * factor);

	// Splits at cycle boundaries. A zero-width arc yields itself once.
	public CycleEnumerator Cycles() => new(this);

	// Overlap of two arcs. A point at the end of a non-empty arc doesn't count as overlapping it.
	public bool TryIntersect(Arc other, out Arc result)
	{
		result = new(Fraction.Max(Begin, other.Begin), Fraction.Min(End, other.End));
		if (result.Begin > result.End)
			return false;
		if (result.Begin == result.End &&
			((result.Begin == End && Begin < End) || (result.Begin == other.End && other.Begin < other.End)))
			return false;
		return true;
	}

	public override string ToString() => $"{Begin}-{End}";

	public struct CycleEnumerator(Arc arc)
	{
		Fraction begin = arc.Begin;
		bool started;

		public Arc Current { get; private set; }
		public readonly CycleEnumerator GetEnumerator() => this;

		public bool MoveNext()
		{
			if (arc.Begin == arc.End)
			{
				if (started)
					return false;
				started = true;
				Current = arc;
				return true;
			}
			if (begin >= arc.End)
				return false;
			var end = Fraction.Min(begin.Floor() + 1, arc.End);
			Current = new(begin, end);
			begin = end;
			return true;
		}
	}
}

// One event: a value active over Part, which is a fragment of its Whole. Continuous values have no Whole.
public readonly record struct Hap<T>(Arc? Whole, Arc Part, T Value)
{
	public bool HasOnset => Whole is { } whole && whole.Begin == Part.Begin;
	public Arc WholeOrPart => Whole ?? Part;

	public Hap<T> Shift(Fraction offset) => new(Whole?.Shift(offset), Part.Shift(offset), Value);
	public Hap<T> Scale(Fraction factor) => new(Whole?.Scale(factor), Part.Scale(factor), Value);
}
