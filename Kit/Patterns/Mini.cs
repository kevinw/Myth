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
					return name == "silence" ? Pattern.Silence<Value>() : throw new PatternSyntaxException($"unknown name '{name}'", start);
				var arguments = ParseArguments();
				return name switch
				{
					"stack" => Pattern.Stack(arguments),
					"cat" or "slowcat" => Pattern.SlowCat(arguments),
					"seq" or "sequence" or "fastcat" => Pattern.FastCat(arguments),
					"pure" when arguments.Length == 1 => arguments[0],
					_ => throw new PatternSyntaxException($"unknown function '{name}'", start),
				};
			}
			throw Error(AtEnd ? "expected a pattern" : $"unexpected '{Peek}'");
		}

		Pattern<Value>[] ParseArguments()
		{
			Expect('(');
			var arguments = new List<Pattern<Value>>();
			SkipWhitespace();
			if (Peek == ')')
			{
				position++;
				return [];
			}
			while (true)
			{
				arguments.Add(ParseChain());
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

		static Pattern<Value> Method(Pattern<Value> pattern, string name, Pattern<Value>[] arguments, int position)
		{
			return (name, arguments.Length) switch
			{
				("fast", 1) => Constant(arguments[0], out var n) ? pattern.Fast(n) : pattern.Fast(Fractions(arguments[0])),
				("slow", 1) => Constant(arguments[0], out var n) ? pattern.Slow(n) : pattern.Slow(Fractions(arguments[0])),
				("late", 1) => Constant(arguments[0], out var n) ? pattern.Late(n) : pattern.Late(Fractions(arguments[0])),
				("early", 1) => Constant(arguments[0], out var n) ? pattern.Early(n) : pattern.Early(Fractions(arguments[0])),
				("rev", 0) => pattern.Rev(),
				("repeatCycles", 1) => pattern.RepeatCycles(Integer(arguments[0], position)),
				("compress", 2) => pattern.Compress(ConstantOrThrow(arguments[0], position), ConstantOrThrow(arguments[1], position)),
				("struct", 1) => pattern.Struct(arguments[0].Select(static v => v.IsTruthy)),
				("euclid", 2) => pattern.Euclid(Integer(arguments[0], position), Integer(arguments[1], position)),
				("euclidRot", 3) => pattern.Euclid(Integer(arguments[0], position), Integer(arguments[1], position), Integer(arguments[2], position)),
				_ => throw new PatternSyntaxException($"unknown method '{name}' with {arguments.Length} arguments", position),
			};
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
			if (Peek == '|')
				throw Error("random choice '|' isn't supported yet");
			if (Peek != ',')
				return head;
			var layers = new List<Pattern<Value>> { head };
			while (Peek == ',')
			{
				position++;
				layers.Add(ParseSequence().Pattern);
				SkipWhitespace();
			}
			return Pattern.Stack(layers.ToArray());
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
					operations.Add((false, p => p.Euclid(Integer(pulses, operatorPosition), Integer(steps, operatorPosition), Integer(rotation, operatorPosition))));
				}
				else if (c is '?' or ':' || (c == '.' && position + 1 < end && text[position + 1] == '.'))
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
					throw Error("polymeter '{' isn't supported yet");
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
	}
}
