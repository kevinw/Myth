namespace OutLoud;

// "Talk box" voice synthesis, Tux-and-Fanny style: a spoken line becomes a
// deterministic sequence of baked syllable blips, played one per spoken-syllable
// as the text reveals. Engine-agnostic: it produces a plan and asks an
// ISyllableSink to make sound. See OutLoud/README.md.

public enum Emotion { Neutral, Excited, Sad, Disappointed }

// Coarse vowel buckets. Baked clips are tagged with one; the German translation of a
// line is reduced to a sequence of these to colour the babble. German umlauts fold to
// the nearest: ä→E, ö→O, ü→U.
public enum Vowel { A, E, I, O, U }

// Implemented per engine. syllable indexes the baked clip set [0, SyllableCount);
// pitch is a semitone offset (0 = unshifted); gain is linear 0..1 volume.
public interface ISyllableSink
{
	void Play(int syllable, float pitch, float gain);
}

// Per-speaker voice character. SyllableCount must match the number of baked clips the
// sink can play. ClipVowels, when supplied, tags each clip [0, SyllableCount) with its
// vowel so a line can be coloured from its German translation. Pitch is in semitones.
public readonly record struct VoiceProfile(
	int SyllableCount,
	float BasePitch = 0f,
	float PitchVariance = 2f,
	float CharsPerSecond = 26f,
	Vowel[]? ClipVowels = null);

// One spoken line turned into a reveal+blip plan, plus the cursor that plays it.
// Create one per line when it appears, then Advance() it each frame with the
// elapsed seconds since it appeared.
public sealed class Utterance
{
	public readonly string Text;
	public readonly float RevealSeconds; // elapsed time at which the last char shows

	readonly record struct Blip(int CharIndex, int Syllable, float Pitch, float Gain);

	readonly float[] revealAt; // revealAt[i] = elapsed seconds when char i becomes visible
	readonly Blip[] blips;     // ascending by CharIndex
	int fired;

	Utterance(string text, float[] revealAt, Blip[] blips)
	{
		Text = text;
		this.revealAt = revealAt;
		this.blips = blips;
		RevealSeconds = revealAt.Length > 0 ? revealAt[^1] : 0f;
	}

	// How many characters should be visible at the given elapsed time.
	public int VisibleLength(float elapsed)
	{
		int lo = 0, hi = revealAt.Length;
		while (lo < hi)
		{
			var mid = (lo + hi) >> 1;
			if (revealAt[mid] <= elapsed) lo = mid + 1; else hi = mid;
		}
		return lo;
	}

	// Play any blips newly revealed since the last call; returns the visible char count.
	public int Advance(float elapsed, ISyllableSink sink)
	{
		var visible = VisibleLength(elapsed);
		while (fired < blips.Length && blips[fired].CharIndex < visible)
		{
			var b = blips[fired++];
			sink.Play(b.Syllable, b.Pitch, b.Gain);
		}
		return visible;
	}

	// text is the English subtitle (drives reveal + blip timing). german, when given, is the
	// translation used only to colour which clips play (see Vowel) — the screen still shows text.
	public static Utterance Build(string text, Emotion emotion, in VoiceProfile profile, string? german = null)
	{
		var n = text.Length;
		var revealAt = new float[n];
		var step = 1f / Math.Max(1f, profile.CharsPerSecond * RateScale(emotion));

		var t = 0f;
		for (var i = 0; i < n; i++)
		{
			revealAt[i] = t;
			t += step + PauseAfter(text[i]);
		}

		var rng = new Rng(Hash(text));
		var basePitch = profile.BasePitch + EmotionPitch(emotion);
		var variance = profile.PitchVariance * VarianceScale(emotion);
		var buckets = BuildBuckets(profile.ClipVowels);
		var germanVowels = german is null ? null : ExtractVowels(german);

		// One blip per English vowel group (≈ one spoken syllable); consonant runs stay silent.
		// Each blip plays a baked German clip; its vowel is walked from the German line (cycled),
		// so the babble tracks the translation's vowel melody while staying synced to the reveal.
		var list = new List<Blip>(n / 2 + 1);
		var prevVowel = false;
		for (var i = 0; i < n; i++)
		{
			var vowel = IsVowel(text[i]);
			if (vowel && !prevVowel && profile.SyllableCount > 0)
			{
				var target = germanVowels is { Count: > 0 } gv ? gv[list.Count % gv.Count] : (Vowel?)null;
				var clip = PickClip(ref rng, target, buckets, profile.SyllableCount);
				list.Add(new Blip(i, clip, basePitch + (rng.NextFloat() * 2f - 1f) * variance, 1f));
			}
			prevVowel = vowel;
		}

		ApplyIntonation(list, EndsWithQuestion(text));
		return new Utterance(text, revealAt, [.. list]);
	}

	static List<int>[]? BuildBuckets(Vowel[]? clipVowels)
	{
		if (clipVowels is null)
			return null;
		var buckets = new List<int>[5];
		for (var v = 0; v < buckets.Length; v++)
			buckets[v] = [];
		for (var i = 0; i < clipVowels.Length; i++)
			buckets[(int)clipVowels[i]].Add(i);
		return buckets;
	}

	static int PickClip(ref Rng rng, Vowel? target, List<int>[]? buckets, int count)
	{
		if (target is { } v && buckets?[(int)v] is { Count: > 0 } bucket)
			return bucket[rng.Next(bucket.Count)];
		return rng.Next(count); // no translation / no clip in that vowel: any clip
	}

	static List<Vowel> ExtractVowels(string text)
	{
		var vowels = new List<Vowel>();
		var prev = false;
		foreach (var c in text)
		{
			var v = Classify(c);
			if (v is { } value && !prev)
				vowels.Add(value);
			prev = v.HasValue;
		}
		return vowels;
	}

	// Lift (question) or settle (statement) the pitch over the last few blips.
	static void ApplyIntonation(List<Blip> blips, bool question)
	{
		var k = Math.Min(3, blips.Count);
		for (var j = 0; j < k; j++)
		{
			var idx = blips.Count - k + j;
			var frac = (j + 1f) / k;
			var shift = (question ? QuestionRise : -StatementFall) * frac;
			blips[idx] = blips[idx] with { Pitch = blips[idx].Pitch + shift };
		}
	}

	// Tunables (semitones / multipliers).
	static float QuestionRise => 4f;
	static float StatementFall => 1.5f;
	static float RateScale(Emotion e) => e switch { Emotion.Excited => 1.25f, Emotion.Sad => 0.8f, Emotion.Disappointed => 0.9f, _ => 1f };
	static float EmotionPitch(Emotion e) => e switch { Emotion.Excited => 3f, Emotion.Sad => -3f, Emotion.Disappointed => -2f, _ => 0f };
	static float VarianceScale(Emotion e) => e switch { Emotion.Excited => 1.4f, Emotion.Sad => 0.6f, Emotion.Disappointed => 0.7f, _ => 1f };
	static float PauseAfter(char c) => c switch { ',' or ';' or ':' => 0.18f, '.' or '!' or '?' => 0.32f, '…' => 0.45f, _ => 0f };

	static bool IsVowel(char c) => Classify(c).HasValue;

	static Vowel? Classify(char c) => char.ToLowerInvariant(c) switch
	{
		'a' => Vowel.A,
		'e' or 'ä' => Vowel.E,
		'i' or 'y' => Vowel.I,
		'o' or 'ö' => Vowel.O,
		'u' or 'ü' => Vowel.U,
		_ => null,
	};

	static bool EndsWithQuestion(string text)
	{
		for (var i = text.Length - 1; i >= 0; i--)
		{
			var c = text[i];
			if (char.IsWhiteSpace(c) || c is '"' or '\'' or ')' or '»' or '”') continue;
			return c == '?';
		}
		return false;
	}

	// FNV-1a: a stable hash so the same line always sounds the same, across platforms.
	static uint Hash(string s)
	{
		var h = 2166136261u;
		foreach (var c in s) { h ^= c; h *= 16777619u; }
		return h;
	}

	// xorshift32: deterministic everywhere, unlike System.Random's unspecified algorithm.
	struct Rng(uint seed)
	{
		uint state = seed == 0 ? 1u : seed;
		uint NextUInt()
		{
			var x = state;
			x ^= x << 13; x ^= x >> 17; x ^= x << 5;
			return state = x;
		}
		public int Next(int max) => max <= 0 ? 0 : (int)(NextUInt() % (uint)max);
		public float NextFloat() => (NextUInt() >> 8) * (1f / 16777216f);
	}
}
