using System.Globalization;

namespace TinyLink;

public static class StoryExpression
{
	public static object Eval(object? value, Dictionary<string, object?> context)
	{
		if (value is bool or int or double)
			return value;
		if (value is string text)
			return new Parser(text, context).Parse();
		return value ?? false;
	}

	public static bool EvalBool(string? expression, Dictionary<string, object?> context)
		=> string.IsNullOrWhiteSpace(expression) || IsTruthy(Eval(expression, context));

	public static int Specificity(string? expression)
	{
		if (string.IsNullOrWhiteSpace(expression))
			return 0;
		var count = 1;
		foreach (var token in expression.Split(' ', StringSplitOptions.RemoveEmptyEntries))
			if (token is "and" or "or" || token.Contains("==") || token.Contains("!=") || token.Contains(">") || token.Contains("<"))
				count++;
		return count;
	}

	private static bool IsTruthy(object? value)
		=> value switch
		{
			bool b => b,
			int i => i != 0,
			double d => Math.Abs(d) > double.Epsilon,
			string s => !string.IsNullOrEmpty(s),
			_ => value != null
		};

	private sealed class Parser
	{
		private readonly List<Token> tokens;
		private readonly Dictionary<string, object?> context;
		private int index;

		public Parser(string text, Dictionary<string, object?> context)
		{
			tokens = Tokenize(text);
			this.context = context;
		}

		public object Parse()
			=> ParseOr();

		private object ParseOr()
		{
			var left = ParseAnd();
			while (Match("or") || Match("||"))
				left = IsTruthy(left) || IsTruthy(ParseAnd());
			return left;
		}

		private object ParseAnd()
		{
			var left = ParseCompare();
			while (Match("and") || Match("&&"))
				left = IsTruthy(left) && IsTruthy(ParseCompare());
			return left;
		}

		private object ParseCompare()
		{
			var left = ParseUnary();
			if (Match("=="))
				return EqualsNormalized(left, ParseUnary());
			if (Match("!="))
				return !EqualsNormalized(left, ParseUnary());
			if (Match(">="))
				return ToDouble(left) >= ToDouble(ParseUnary());
			if (Match("<="))
				return ToDouble(left) <= ToDouble(ParseUnary());
			if (Match(">"))
				return ToDouble(left) > ToDouble(ParseUnary());
			if (Match("<"))
				return ToDouble(left) < ToDouble(ParseUnary());
			return left;
		}

		private object ParseUnary()
		{
			if (Match("not") || Match("!"))
				return !IsTruthy(ParseUnary());
			return ParseValue();
		}

		private object ParseValue()
		{
			if (Match("("))
			{
				var groupValue = ParseOr();
				Match(")");
				return groupValue;
			}

			if (index >= tokens.Count)
				return false;

			var token = tokens[index++];
			if (token.Kind == TokenKind.String)
				return token.Text;
			if (token.Kind == TokenKind.Number)
				return double.Parse(token.Text, CultureInfo.InvariantCulture);
			if (string.Equals(token.Text, "true", StringComparison.OrdinalIgnoreCase))
				return true;
			if (string.Equals(token.Text, "false", StringComparison.OrdinalIgnoreCase))
				return false;
			return context.TryGetValue(token.Text, out var value) ? value ?? false : false;
		}

		private bool Match(string text)
		{
			if (index >= tokens.Count || !string.Equals(tokens[index].Text, text, StringComparison.OrdinalIgnoreCase))
				return false;
			index++;
			return true;
		}

		private static bool EqualsNormalized(object? left, object? right)
		{
			if (left is double or int || right is double or int)
				return Math.Abs(ToDouble(left) - ToDouble(right)) < 0.0001;
			return string.Equals(Convert.ToString(left, CultureInfo.InvariantCulture), Convert.ToString(right, CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase);
		}

		private static double ToDouble(object? value)
			=> value switch
			{
				int i => i,
				double d => d,
				float f => f,
				bool b => b ? 1 : 0,
				string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) => d,
				_ => 0
			};

		private static List<Token> Tokenize(string text)
		{
			var result = new List<Token>();
			for (var i = 0; i < text.Length;)
			{
				if (char.IsWhiteSpace(text[i]))
				{
					i++;
					continue;
				}

				if (text[i] is '\'' or '"')
				{
					var quote = text[i++];
					var start = i;
					while (i < text.Length && text[i] != quote)
						i++;
					result.Add(new(TokenKind.String, text[start..Math.Min(i, text.Length)]));
					if (i < text.Length)
						i++;
					continue;
				}

				if (char.IsDigit(text[i]) || text[i] == '.')
				{
					var start = i++;
					while (i < text.Length && (char.IsDigit(text[i]) || text[i] == '.'))
						i++;
					result.Add(new(TokenKind.Number, text[start..i]));
					continue;
				}

				if (i + 1 < text.Length && text.Substring(i, 2) is "==" or "!=" or ">=" or "<=" or "&&" or "||")
				{
					result.Add(new(TokenKind.Symbol, text.Substring(i, 2)));
					i += 2;
					continue;
				}

				if ("()!<>".Contains(text[i]))
				{
					result.Add(new(TokenKind.Symbol, text[i].ToString()));
					i++;
					continue;
				}

				var wordStart = i++;
				while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_'))
					i++;
				result.Add(new(TokenKind.Identifier, text[wordStart..i]));
			}
			return result;
		}
	}

	private enum TokenKind
	{
		Identifier,
		Number,
		String,
		Symbol
	}

	private readonly record struct Token(TokenKind Kind, string Text);
}
