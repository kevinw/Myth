#:project ../../Foster/Framework/Foster.Framework.csproj

using Foster.Framework;

const int samples = 1_000_000;
const double sigmaLimit = 6;
var failures = 0;

Console.WriteLine($"Foster Rng check: {samples:N0} fixed-seed samples per test, fail beyond {sigmaLimit}σ");
Console.WriteLine("This is a game-readiness smoke test, not a proof or a cryptographic test.\n");

var left = new Rng(0x123456789abcdef0);
var right = new Rng(0x123456789abcdef0);
var reproducible = true;
for (var i = 0; i < 10_000; i++)
	reproducible &= left.U64() == right.U64();
Check(reproducible, "same seed reproduces the same stream");

var raw = new Rng(0x243f6a8885a308d3);
var ones = new int[64];
var previousLowBit = false;
var lowBitChanges = 0;
for (var i = 0; i < samples; i++)
{
	var value = raw.U64();
	for (var bit = 0; bit < 64; bit++)
		ones[bit] += (int)((value >> bit) & 1);

	var lowBit = (value & 1) != 0;
	if (i > 0 && lowBit != previousLowBit)
		lowBitChanges++;
	previousLowBit = lowBit;
}

var bitSigma = Math.Sqrt(samples / 4d);
var worstBit = 0;
var worstBitZ = 0d;
for (var bit = 0; bit < ones.Length; bit++)
{
	var z = (ones[bit] - samples / 2d) / bitSigma;
	if (Math.Abs(z) > Math.Abs(worstBitZ))
		(worstBit, worstBitZ) = (bit, z);
}
Check(Math.Abs(worstBitZ) <= sigmaLimit, "U64 per-bit balance", $"worst bit {worstBit}: z={worstBitZ:+0.00;-0.00;0.00}");

var transitions = samples - 1;
var transitionZ = (lowBitChanges - transitions / 2d) / Math.Sqrt(transitions / 4d);
Check(Math.Abs(transitionZ) <= sigmaLimit, "U64 low-bit adjacent transitions", $"z={transitionZ:+0.00;-0.00;0.00}");

var byteRng = new Rng(0x13198a2e03707344);
Uniform("U8() across all 256 values", 256, () => byteRng.U8());

var intRng = new Rng(0xa4093822299f31d0);
Uniform("Int(10)", 10, () => intRng.Int(10));

var longRng = new Rng(0x082efa98ec4e6c89);
Uniform("Long(10)", 10, () => (int)longRng.Long(10));

var floatRng = new Rng(0x452821e638d01377);
Uniform("Float() across 32 bins", 32, () => (int)(floatRng.Float() * 32));

var doubleRng = new Rng(0xbe5466cf34e90c6c);
Uniform("Double() across 32 bins", 32, () => (int)(doubleRng.Double() * 32));

// These narrower bounded overloads make otherwise tiny modulo bias large enough to observe,
// and exercise the signed Math.Abs edge cases left behind by the recent Int/Long fixes.
var boundedByteRng = new Rng(0xc0ac29b7c97c50dd);
Uniform("U8(6)", 6, () => boundedByteRng.U8(6));

var shortRng = new Rng(0x3f84d5b5b5470917);
Uniform("Short(10)", 10, () => shortRng.Short(10));

var sbyteRng = new Rng(0x9216d5d98979fb1b);
Uniform("SByte(10)", 10, () => sbyteRng.SByte(10));

Console.WriteLine(failures == 0
	? "\nPASS: no suspicious result in this smoke test."
	: $"\nFAIL: {failures} suspicious result(s).");
return failures == 0 ? 0 : 1;

void Uniform(string name, int buckets, Func<int> next)
{
	var counts = new int[buckets];
	var exceptions = 0;
	var outOfRange = 0;
	string? firstException = null;

	for (var i = 0; i < samples; i++)
	{
		try
		{
			var value = next();
			if ((uint)value < (uint)buckets)
				counts[value]++;
			else
				outOfRange++;
		}
		catch (Exception exception)
		{
			exceptions++;
			firstException ??= exception.GetType().Name;
		}
	}

	var counted = counts.Sum();
	var expected = counted / (double)buckets;
	var chiSquared = counts.Sum(count => Math.Pow(count - expected, 2) / expected);
	var degreesOfFreedom = buckets - 1;
	var z = (Math.Pow(chiSquared / degreesOfFreedom, 1d / 3d) - (1 - 2d / (9 * degreesOfFreedom)))
		/ Math.Sqrt(2d / (9 * degreesOfFreedom));
	var details = $"χ²={chiSquared:0.0}, approx z={z:+0.00;-0.00;0.00}";
	if (exceptions > 0)
		details += $", exceptions={exceptions:N0} ({firstException})";
	if (outOfRange > 0)
		details += $", out-of-range={outOfRange:N0}";
	Check(exceptions == 0 && outOfRange == 0 && Math.Abs(z) <= sigmaLimit, name, details);
}

void Check(bool passed, string name, string? details = null)
{
	Console.WriteLine($"{(passed ? "PASS" : "FAIL")}  {name}{(details == null ? "" : $" — {details}")}");
	if (!passed)
		failures++;
}
