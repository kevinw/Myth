using System.Globalization;

namespace Kit.Patterns;

// A mini-notation atom: a number when the word parses as one.
public readonly record struct Value(string? Word, double Number)
{
	public bool IsNumber => Word == null;
	public bool IsTruthy => IsNumber ? Number != 0 && !double.IsNaN(Number) : Word!.Length > 0;
	public Fraction ToFraction() => IsNumber && double.IsFinite(Number) ? Fraction.FromDouble(Number, 1_000_000_000) : Fraction.Zero;
	public override string ToString() => Word ?? Number.ToString("R", CultureInfo.InvariantCulture);
}

public sealed class PatternSyntaxException(string message, int position)
	: FormatException(string.Create(CultureInfo.InvariantCulture, $"{message} (at {position})"))
{
	public int Position => position;
}

// Tidal/Strudel mini-notation ("a [b c] <d e>*2") and a subset of Strudel's JavaScript expressions
// ("x ~ x".fast("<1 2>").rev()), so a pattern sketched in Strudel can be pasted into the game.
public static class Mini
{
	public static Pattern<Value> Parse(string source)
	{
		var parser = new Parser(source);
		return parser.ParseMiniToEnd(source.Length);
	}

	public static Pattern<Value> ParseExpression(string source)
	{
		var parser = new Parser(source);
		var pattern = parser.ParseChain();
		parser.SkipWhitespace();
		if (!parser.AtEnd)
			throw parser.Error($"unexpected '{source[parser.Position]}'");
		return pattern;
	}

	struct Parser(string text)
	{
		int position;
		int end = text.Length;
		int seed; // '?' and '|' offset the random signal by seed, counted per mini string

		public readonly int Position => position;
		public readonly bool AtEnd => position >= end;
		readonly char Peek => position < end ? text[position] : '\0';

		public readonly PatternSyntaxException Error(string message) => new(message, position);

		public void SkipWhitespace()
		{
			while (position < end && (char.IsWhiteSpace(text[position]) || text[position] == ' '))
				position++;
		}

		void Expect(char c)
		{
			SkipWhitespace();
			if (Peek != c)
				throw Error(AtEnd ? $"expected '{c}'" : $"expected '{c}' but found '{Peek}'");
			position++;
		}

		// --- expressions ---

		public Pattern<Value> ParseChain()
		{
			var pattern = ParsePrimary();
			while (true)
			{
				SkipWhitespace();
				if (Peek != '.' || position + 1 >= end || !char.IsLetter(text[position + 1]))
					return pattern;
				position++;
				var methodPosition = position;
				var name = ParseIdentifier();
				pattern = Method(pattern, name, ParseArguments(), methodPosition);
			}
		}

		Pattern<Value> ParsePrimary()
		{
			SkipWhitespace();
			var start = position;
			if (Peek == '"')
			{
				position++;
				var close = text.IndexOf('"', position);
				if (close < 0)
					throw Error("unterminated string");
				var mini = new Parser(text) { position = position, end = close };
				var pattern = mini.ParseMiniToEnd(close);
				position = close + 1;
				return pattern;
			}
			if (Peek == '(')
			{
				position++;
				var pattern = ParseChain();
				Expect(')');
				return pattern;
			}
			if (char.IsDigit(Peek) || Peek == '-' || Peek == '.')
				return Pattern.Pure(new Value(null, ParseNumber()));
			if (char.IsLetter(Peek))
			{
				var name = ParseIdentifier();
				SkipWhitespace();
				if (Peek != '(')
					return name switch
					{
						"silence" => Pattern.Silence<Value>(),
						"sine" => Numbers(Signal.Sine),
						"cosine" => Numbers(Signal.Cosine),
						"saw" => Numbers(Signal.Saw),
						"isaw" => Numbers(Signal.ISaw),
						"square" => Numbers(Signal.Square),
						"isquare" => Numbers(Signal.ISquare),
						"tri" => Numbers(Signal.Tri),
						"rand" => Numbers(Signal.Rand),
						"perlin" => Numbers(Signal.Perlin),
						_ => throw new PatternSyntaxException($"unknown name '{name}'", start),
					};
				var arguments = ParseArguments();
				var patterns = Array.ConvertAll(arguments, argument => argument.Pattern
					?? throw new PatternSyntaxException($"'{name}' wants patterns, not functions", argument.Position));
				return name switch
				{
					"stack" => Pattern.Stack(patterns),
					"cat" or "slowcat" => Pattern.SlowCat(patterns),
					"seq" or "sequence" or "fastcat" => Pattern.FastCat(patterns),
					"pure" when patterns.Length == 1 => patterns[0],
					"irand" when patterns.Length == 1 => Numbers(Signal.IRand((int)Number(patterns[0], start))),
					_ => throw new PatternSyntaxException($"unknown function '{name}'", start),
				};
			}
			throw Error(AtEnd ? "expected a pattern" : $"unexpected '{Peek}'");
		}

		// An argument is a pattern ("a b", 3, sine) or a function (rev, or x => x.fast(2)).
		readonly record struct Argument(Pattern<Value>? Pattern, Func<Pattern<Value>, Pattern<Value>>? Function, int Position);

		Argument[] ParseArguments()
		{
			Expect('(');
			var arguments = new List<Argument>();
			SkipWhitespace();
			if (Peek == ')')
			{
				position++;
				return [];
			}
			while (true)
			{
				arguments.Add(ParseArgument());
				SkipWhitespace();
				if (Peek == ',')
				{
					position++;
					continue;
				}
				Expect(')');
				return arguments.ToArray();
			}
		}

		Argument ParseArgument()
		{
			SkipWhitespace();
			var start = position;
			if (char.IsLetter(Peek))
			{
				var name = ParseIdentifier();
				SkipWhitespace();
				if (position + 1 < end && text[position] == '=' && text[position + 1] == '>')
				{
					position += 2;
					SkipWhitespace();
					var parameterPosition = position;
					if (ParseIdentifier() != name)
						throw new PatternSyntaxException($"expected '{name}' here", parameterPosition);
					return new(null, ParseMethodChain(), start);
				}
				if (Peek is ',' or ')' && BareFunction(name) is { } function)
					return new(null, function, start);
				position = start;
			}
			return new(ParseChain(), null, start);
		}

		// The '.method(...)' calls after an arrow function's parameter, as one function.
		Func<Pattern<Value>, Pattern<Value>> ParseMethodChain()
		{
			var steps = new List<(string Name, Argument[] Arguments, int Position)>();
			while (true)
			{
				SkipWhitespace();
				if (Peek != '.' || position + 1 >= end || !char.IsLetter(text[position + 1]))
					break;
				position++;
				var methodPosition = position;
				var name = ParseIdentifier();
				steps.Add((name, ParseArguments(), methodPosition));
			}
			return pattern =>
			{
				foreach (var step in steps)
					pattern = Method(pattern, step.Name, step.Arguments, step.Position);
				return pattern;
			};
		}

		static Func<Pattern<Value>, Pattern<Value>>? BareFunction(string name) => name switch
		{
			"rev" => static p => p.Rev(),
			"palindrome" => static p => p.Palindrome(),
			"degrade" => static p => p.DegradeBy(0.5),
			"id" => static p => p,
			_ => null,
		};

		static Pattern<Value> Method(Pattern<Value> pattern, string name, Argument[] arguments, int position)
		{
			Pattern<Value> Pat(int index) => arguments[index].Pattern
				?? throw new PatternSyntaxException($"'{name}' wants a pattern here", arguments[index].Position);
			Func<Pattern<Value>, Pattern<Value>> Fn(int index) => arguments[index].Function
				?? throw new PatternSyntaxException($"'{name}' wants a function here, like x => x.rev()", arguments[index].Position);
			Fraction Const(int index) => ConstantOrThrow(Pat(index), arguments[index].Position);
			double Num(int index) => Number(Pat(index), arguments[index].Position);
			int Int(int index) => (int)Num(index);

			return (name, arguments.Length) switch
			{
				("fast", 1) => Constant(Pat(0), out var fast) ? pattern.Fast(fast) : pattern.Fast(Fractions(Pat(0))),
				("slow", 1) => Constant(Pat(0), out var slow) ? pattern.Slow(slow) : pattern.Slow(Fractions(Pat(0))),
				("late", 1) => Constant(Pat(0), out var late) ? pattern.Late(late) : pattern.Late(Fractions(Pat(0))),
				("early", 1) => Constant(Pat(0), out var early) ? pattern.Early(early) : pattern.Early(Fractions(Pat(0))),
				("rev", 0) => pattern.Rev(),
				("palindrome", 0) => pattern.Palindrome(),
				("repeatCycles", 1) => pattern.RepeatCycles(Int(0)),
				("compress", 2) => pattern.Compress(Const(0), Const(1)),
				("struct", 1) => pattern.Struct(Pat(0).Select(static v => v.IsTruthy)),
				("segment", 1) => pattern.Segment(Const(0)),
				("range", 2) => pattern.Select(Scale(Num(0), Num(1))),
				("degrade", 0) => pattern.DegradeBy(0.5),
				("degradeBy", 1) => pattern.DegradeBy(Num(0)),
				("undegradeBy", 1) => pattern.UndegradeBy(Num(0)),
				("euclid", 2) => Euclid(pattern, Pat(0), Pat(1), null, position),
				("euclidRot", 3) => Euclid(pattern, Pat(0), Pat(1), Pat(2), position),
				("every" or "firstOf", 2) => pattern.Every(Int(0), Fn(1)),
				("lastOf", 2) => pattern.LastOf(Int(0), Fn(1)),
				("off", 2) => pattern.Off(Const(0), Fn(1)),
				("superimpose", 1) => pattern.Superimpose(Fn(0)),
				("iter", 1) => pattern.Iter(Int(0)),
				("iterBack", 1) => pattern.Iter(Int(0), backwards: true),
				("ply", 1) => pattern.Ply(Int(0)),
				("chunk", 2) => pattern.Chunk(Int(0), Fn(1)),
				("sometimesBy", 2) => pattern.SometimesBy(Num(0), Fn(1)),
				("sometimes", 1) => pattern.SometimesBy(0.5, Fn(0)),
				("often", 1) => pattern.SometimesBy(0.75, Fn(0)),
				("rarely", 1) => pattern.SometimesBy(0.25, Fn(0)),
				("almostAlways", 1) => pattern.SometimesBy(0.9, Fn(0)),
				("almostNever", 1) => pattern.SometimesBy(0.1, Fn(0)),
				("always", 1) => Fn(0)(pattern),
				("never", 1) => pattern,
				_ => throw new PatternSyntaxException($"unknown method '{name}' with {arguments.Length} arguments", position),
			};
		}

		// Constant arguments build the rhythm once; patterned ones re-read it as they change.
		static Pattern<Value> Euclid(Pattern<Value> pattern, Pattern<Value> pulses, Pattern<Value> steps, Pattern<Value>? rotation, int position)
		{
			if (Constant(pulses, out var p) && Constant(steps, out var s) && (rotation == null || Constant(rotation, out _)))
			{
				var turn = rotation == null ? 0 : (int)Number(rotation, position);
				return pattern.Euclid((int)p.ToDouble(), (int)s.ToDouble(), turn);
			}
			var args = Pattern.AppLeft(
				Pattern.AppLeft(Fractions(pulses), Fractions(steps), static (a, b) => new EuclidArgs((int)a.ToDouble(), (int)b.ToDouble(), 0)),
				rotation == null ? Pattern.Pure(Fraction.Zero) : Fractions(rotation),
				static (a, turn) => a with { Rotation = (int)turn.ToDouble() });
			return pattern.Euclid(args);
		}

		string ParseIdentifier()
		{
			var start = position;
			while (position < end && (char.IsLetterOrDigit(text[position]) || text[position] == '_'))
				position++;
			return text[start..position];
		}

		// --- mini-notation ---

		public Pattern<Value> ParseMiniToEnd(int limit)
		{
			SkipWhitespace();
			var pattern = ParseStackOrChoose();
			SkipWhitespace();
			if (position < limit)
				throw Error($"unexpected '{Peek}'");
			return pattern;
		}

		Pattern<Value> ParseStackOrChoose()
		{
			var head = ParseSequence().Pattern;
			SkipWhitespace();
			if (Peek is not (',' or '|'))
				return head;
			var separator = Peek;
			var layers = new List<Pattern<Value>> { head };
			while (Peek == separator)
			{
				position++;
				layers.Add(ParseSequence().Pattern);
				SkipWhitespace();
			}
			return separator == ','
				? Pattern.Stack(layers.ToArray())
				: Pattern.ChooseIn(RandomSignal(seed++).Segment(1), layers.ToArray());
		}

		(Pattern<Value> Pattern, Fraction Steps) ParseSequence()
		{
			SkipWhitespace();
			if (Peek == '^')
				position++;
			var steps = new List<(Fraction Weight, Pattern<Value> Pattern)>();
			var total = Fraction.Zero;
			while (TryParseSliceWithOps(out var weight, out var pattern))
			{
				steps.Add((weight, pattern));
				total += weight;
			}
			if (steps.Count == 0)
				throw Error(AtEnd ? "expected a step" : $"unexpected '{Peek}'");
			return (Pattern.TimeCat(steps.ToArray()), total);
		}

		bool TryParseSliceWithOps(out Fraction weight, out Pattern<Value> pattern)
		{
			weight = 1;
			if (!TryParseSlice(out pattern))
				return false;
			// Replication is one operation that moves to the end each time another '!' appears.
			var repeats = 1L;
			var operations = new List<(bool Replicate, Func<Pattern<Value>, Pattern<Value>> Apply)>();
			while (true)
			{
				SkipWhitespace();
				var c = Peek;
				if (c is '@' or '_')
				{
					position++;
					weight += TryParseNumber(out var amount) ? Fraction.FromDouble(amount, 1_000_000_000) - 1 : 1;
				}
				else if (c == '!')
				{
					position++;
					repeats += (TryParseNumber(out var amount) ? (long)amount : 2) - 1;
					weight = repeats;
					var count = repeats;
					operations.RemoveAll(static operation => operation.Replicate);
					operations.Add((true, p => p.RepeatCycles(count).Fast(count)));
				}
				else if (c is '*' or '/')
				{
					var operatorPosition = position;
					position++;
					if (!TryParseSlice(out var amount))
						throw new PatternSyntaxException($"expected an amount after '{c}'", operatorPosition + 1);
					var fast = c == '*';
					operations.Add((false, p => Constant(amount, out var n)
						? (fast ? p.Fast(n) : p.Slow(n))
						: (fast ? p.Fast(Fractions(amount)) : p.Slow(Fractions(amount)))));
				}
				else if (c == '(')
				{
					var operatorPosition = position;
					position++;
					var pulses = ParseEuclidArgument();
					Expect(',');
					var steps = ParseEuclidArgument();
					SkipWhitespace();
					if (Peek == ',')
						position++;
					SkipWhitespace();
					var rotation = Peek == ')' ? Pattern.Pure(new Value(null, 0)) : ParseEuclidArgument();
					Expect(')');
					operations.Add((false, p => Euclid(p, pulses, steps, rotation, operatorPosition)));
				}
				else if (c == '?')
				{
					position++;
					var amount = TryParseNumber(out var value) ? value : 0.5;
					var random = RandomSignal(seed++);
					operations.Add((false, p => p.DegradeWith(random, amount)));
				}
				else if (c is ':' || (c == '.' && position + 1 < end && text[position + 1] == '.'))
					throw Error($"'{c}' isn't supported yet");
				else
					break;
			}
			foreach (var operation in operations)
				pattern = operation.Apply(pattern);
			return true;
		}

		Pattern<Value> ParseEuclidArgument()
		{
			SkipWhitespace();
			if (!TryParseSliceWithOps(out _, out var pattern))
				throw Error("expected a number");
			return pattern;
		}

		bool TryParseSlice(out Pattern<Value> pattern)
		{
			SkipWhitespace();
			switch (Peek)
			{
				case '[':
					position++;
					SkipWhitespace();
					pattern = ParseStackOrChoose();
					Expect(']');
					return true;
				case '<':
				{
					position++;
					var layers = new List<Pattern<Value>>();
					do
					{
						if (layers.Count > 0)
							position++;
						var (sequence, steps) = ParseSequence();
						layers.Add(sequence.Slow(steps));
						SkipWhitespace();
					} while (Peek == ',');
					Expect('>');
					pattern = Pattern.Stack(layers.ToArray());
					return true;
				}
				case '{':
				{
					// Every sequence runs at the same step rate, so they drift against each other.
					position++;
					var sequences = new List<(Pattern<Value> Pattern, Fraction Steps)>();
					do
					{
						if (sequences.Count > 0)
							position++;
						sequences.Add(ParseSequence());
						SkipWhitespace();
					} while (Peek == ',');
					Expect('}');
					var stepsPerCycle = sequences[0].Steps;
					if (Peek == '%')
					{
						var stepsPosition = position++;
						if (!TryParseSlice(out var amount) || !Constant(amount, out stepsPerCycle))
							throw new PatternSyntaxException("expected a number of steps after '%'", stepsPosition + 1);
					}
					pattern = Pattern.Stack(sequences.ConvertAll(s => s.Steps == 0 ? s.Pattern : s.Pattern.Fast(stepsPerCycle / s.Steps)).ToArray());
					return true;
				}
			}
			var start = position;
			while (position < end && IsStepChar(text[position]))
				position++;
			var word = text[start..position];
			if (word.Length == 0 || word is "." or "_")
			{
				position = start;
				pattern = Pattern.Silence<Value>();
				return false;
			}
			pattern = word is "~" or "-"
				? Pattern.Silence<Value>()
				: Pattern.Pure(double.TryParse(word, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && !double.IsNaN(number)
					? new Value(null, number)
					: new Value(word, 0));
			return true;
		}

		static bool IsStepChar(char c)
			=> char.IsLetterOrDigit(c) || char.GetUnicodeCategory(c) == UnicodeCategory.LetterNumber || c is '~' or '-' or '#' or '.' or '^' or '_';

		bool TryParseNumber(out double number)
		{
			var start = position;
			if (Peek == '-')
				position++;
			while (position < end && (char.IsDigit(text[position]) || text[position] is '.' or 'e' or 'E' || (text[position] is '+' or '-' && text[position - 1] is 'e' or 'E')))
				position++;
			if (double.TryParse(text.AsSpan(start, position - start), NumberStyles.Float, CultureInfo.InvariantCulture, out number))
				return true;
			position = start;
			return false;
		}

		double ParseNumber() => TryParseNumber(out var number) ? number : throw Error("expected a number");

		// --- arguments ---

		static bool Constant(Pattern<Value> pattern, out Fraction value)
		{
			var isConstant = pattern is PurePattern<Value> { Value.IsNumber: true };
			value = isConstant ? ((PurePattern<Value>)pattern).Value.ToFraction() : Fraction.Zero;
			return isConstant;
		}

		static Fraction ConstantOrThrow(Pattern<Value> pattern, int position)
			=> Constant(pattern, out var value) ? value : throw new PatternSyntaxException("expected a number", position);

		static int Integer(Pattern<Value> pattern, int position)
			=> pattern is PurePattern<Value> { Value: { IsNumber: true } value } ? (int)value.Number : throw new PatternSyntaxException("patterned arguments aren't supported here yet", position);

		static Pattern<Fraction> Fractions(Pattern<Value> pattern) => pattern.Select(static v => v.ToFraction());

		// Each '?' and '|' in a mini string uses its own slice of the random signal.
		static Pattern<double> RandomSignal(int seed) => Signal.Rand.Early(Fraction.FromDouble(0.0003 * seed));

		static Pattern<Value> Numbers(Pattern<double> pattern) => pattern.Select(static v => new Value(null, v));

		static double Number(Pattern<Value> pattern, int position)
			=> pattern is PurePattern<Value> { Value: { IsNumber: true } value } ? value.Number : throw new PatternSyntaxException("expected a number", position);

		static Func<Value, Value> Scale(double min, double max) => value => new Value(null, value.Number * (max - min) + min);
	}
}
