using System.Globalization;
using System.Text.Json;

namespace Kit.Patterns.Tests;

public class PatternTests
{
	record Case(string Expression, string[] Cycles, string[][] Spans, string[] Triggers);
	record Golden(string Strudel, Case[] Cases);

	static readonly Golden golden = JsonSerializer.Deserialize<Golden>(
		File.ReadAllText(Path.Join(AppContext.BaseDirectory, "golden.json")),
		new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

	// Keep in sync with strudel-oracle.mjs.
	static readonly Arc[] spans = [new(new(1, 3), new(7, 5)), new(new(5, 4), new(13, 4))];
	const int FramesPerCycle = 60;
	const int FrameCount = 240;

	public static TheoryData<string> Expressions => new(golden.Cases.Select(c => c.Expression));

	[Fact]
	public void GoldenMatchesCorpus()
	{
		var corpus = File.ReadAllLines(Path.Join(AppContext.BaseDirectory, "corpus.txt"))
			.Select(line => line.Trim())
			.Where(line => line.Length > 0 && !line.StartsWith('#'));
		Assert.True(corpus.SequenceEqual(golden.Cases.Select(c => c.Expression)),
			"golden.json is stale: run node Kit/Patterns/Tests/strudel-oracle.mjs");
	}

	[Theory, MemberData(nameof(Expressions))]
	public void MatchesStrudel(string expression)
	{
		var expected = golden.Cases.Single(c => c.Expression == expression);
		var pattern = Mini.ParseExpression(expression);
		AssertLines(expected.Cycles, Haps(pattern, new(0, 8)), "cycles [0, 8)");
		for (var i = 0; i < spans.Length; i++)
			AssertLines(expected.Spans[i], Haps(pattern, spans[i]), $"span [{spans[i]})");
		AssertLines(expected.Triggers, Triggers(pattern), "frame triggers");
	}

	// Querying frame by frame must trigger each onset exactly once, the same as one big query.
	[Theory, MemberData(nameof(Expressions))]
	public void FramesTriggerEachOnsetOnce(string expression)
	{
		var pattern = Mini.ParseExpression(expression);
		var whole = new List<Hap<Value>>();
		pattern.Query(new(0, new Fraction(FrameCount, FramesPerCycle)), whole);
		AssertLines(Sorted(whole.Where(h => h.HasOnset).Select(Trigger)), Triggers(pattern), "onsets");
	}

	[Theory, MemberData(nameof(Expressions))]
	public void RoundTripsAreIdentities(string expression)
	{
		var pattern = Mini.ParseExpression(expression);
		var triggers = Triggers(pattern);
		AssertLines(triggers, Triggers(pattern.Fast(3).Slow(3)), "fast(3).slow(3)");
		AssertLines(triggers, Triggers(pattern.Rev().Rev()), "rev().rev()");
		AssertLines(triggers, Triggers(pattern.Late(new Fraction(1, 3)).Early(new Fraction(1, 3))), "late(1/3).early(1/3)");
		AssertLines(triggers, Triggers(Pattern.Stack(pattern, Pattern.Silence<Value>())), "stack with silence");
	}

	[Fact]
	public void SteadyStateQueriesDontAllocate()
	{
		var patterns = golden.Cases.Select(c => Mini.ParseExpression(c.Expression)).ToArray();
		var output = new List<Hap<Value>>(256);
		void QueryFrames(int first)
		{
			foreach (var pattern in patterns)
				for (var frame = first; frame < first + FrameCount; frame++)
				{
					output.Clear();
					pattern.Query(new(new Fraction(frame, FramesPerCycle), new Fraction(frame + 1, FramesPerCycle)), output);
				}
		}
		QueryFrames(0);
		var before = GC.GetAllocatedBytesForCurrentThread();
		QueryFrames(FrameCount);
		Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
	}

	[Theory]
	[InlineData("\"a [b c\"", 7)]
	[InlineData("\"a b\".fast(", 11)]
	[InlineData("\"a b\".wobble(2)", 6)]
	[InlineData("\"a <b c\"", 7)]
	[InlineData("stack(\"a\", nope)", 11)]
	public void SyntaxErrorsReportTheirPosition(string expression, int position)
		=> Assert.Equal(position, Assert.Throws<PatternSyntaxException>(() => Mini.ParseExpression(expression)).Position);

	[Theory]
	[InlineData(0.1, 1, 10)]
	[InlineData(-2.5, -5, 2)]
	[InlineData(1.0 / 3, 1, 3)]
	[InlineData(7, 7, 1)]
	public void FractionsFromDoubles(double value, long numerator, long denominator)
		=> Assert.Equal(new Fraction(numerator, denominator), Fraction.FromDouble(value));

	static string[] Haps(Pattern<Value> pattern, Arc span)
	{
		var haps = new List<Hap<Value>>();
		pattern.Query(span, haps);
		return Sorted(haps.Select(h => $"{(h.Whole is { } w ? w.ToString() : "~")} {h.Part} {Json(h.Value)}"));
	}

	static string[] Triggers(Pattern<Value> pattern)
	{
		var haps = new List<Hap<Value>>();
		var triggers = new List<string>();
		for (var frame = 0; frame < FrameCount; frame++)
		{
			haps.Clear();
			pattern.Query(new(new Fraction(frame, FramesPerCycle), new Fraction(frame + 1, FramesPerCycle)), haps);
			triggers.AddRange(haps.Where(h => h.HasOnset).Select(Trigger));
		}
		return Sorted(triggers);
	}

	static string Trigger(Hap<Value> hap) => $"{hap.Whole} {Json(hap.Value)}";

	static string Json(Value value) => value.IsNumber
		? value.Number.ToString("R", CultureInfo.InvariantCulture)
		: $"\"{value.Word!.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";

	static string[] Sorted(IEnumerable<string> lines)
	{
		var array = lines.ToArray();
		Array.Sort(array, string.CompareOrdinal);
		return array;
	}

	static void AssertLines(string[] expected, string[] actual, string what)
	{
		if (expected.SequenceEqual(actual))
			return;
		var missing = expected.Except(actual).Take(5);
		var extra = actual.Except(expected).Take(5);
		Assert.Fail($"{what}: expected {expected.Length} haps, got {actual.Length}\n  missing: {string.Join(" | ", missing)}\n  extra: {string.Join(" | ", extra)}");
	}
}
