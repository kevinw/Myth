#if EDITOR && !BROWSER
namespace Myth;

// Adapted from chr15m/jsfxr's public-domain sfxr.js, with additional synthesis
// ideas from KilledByAPixel/ZzFX:
// https://github.com/chr15m/jsfxr (see External/jsfxr/UNLICENSE).
// https://github.com/KilledByAPixel/ZzFX (see External/ZzFX/LICENSE).
public enum ProceduralWave
{
	Square,
	Sawtooth,
	Sine,
	Noise,
	Triangle,
}

public enum ProceduralSoundPreset
{
	Pickup,
	Laser,
	Explosion,
	PowerUp,
	Hit,
	Jump,
	Blip,
	Random,
}

public sealed class ProceduralSoundPatch
{
	public ProceduralWave Wave;
	public int Seed = 1;
	public float Volume = 0.25f;

	public float Attack;
	public float Sustain = 0.3f;
	public float Punch;
	public float Decay = 0.4f;

	public float StartFrequency = 0.3f;
	public float MinimumFrequency;
	public float FrequencySlide;
	public float FrequencyDeltaSlide;

	public float VibratoDepth;
	public float VibratoSpeed;
	public float ArpeggioAmount;
	public float ArpeggioSpeed;

	public float Duty;
	public float DutySweep;
	public float NoiseMix;
	public float WaveShape;
	public float FmDepth;
	public float FmRatio = 0.1f;
	public float RepeatSpeed;
	public float TremoloDepth;
	public float TremoloSpeed;

	public float FlangerOffset;
	public float FlangerSweep;
	public float BitCrush;
	public float SampleRateReduction;
	public float DelayTime;
	public float DelayFeedback;
	public float DelayMix;

	public float LowPassCutoff = 1;
	public float LowPassSweep;
	public float LowPassResonance;
	public float HighPassCutoff;
	public float HighPassSweep;

	public ProceduralSoundPatch Clone() => (ProceduralSoundPatch)MemberwiseClone();
}

public readonly record struct ProceduralSoundRender(float[] Samples, byte[] Pcm16, int Clipped)
{
	public const int SampleRate = 44100;

	public float Duration => Samples.Length / (float)SampleRate;

	public void WriteWav(string path)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		using var stream = File.Create(path);
		using var writer = new BinaryWriter(stream);
		writer.Write("RIFF"u8);
		writer.Write(36 + Pcm16.Length);
		writer.Write("WAVE"u8);
		writer.Write("fmt "u8);
		writer.Write(16);
		writer.Write((short)1);
		writer.Write((short)1);
		writer.Write(SampleRate);
		writer.Write(SampleRate * sizeof(short));
		writer.Write((short)sizeof(short));
		writer.Write((short)16);
		writer.Write("data"u8);
		writer.Write(Pcm16.Length);
		writer.Write(Pcm16);
	}
}

public static class ProceduralSoundSynth
{
	const int Oversampling = 8;
	const int MaxSamples = ProceduralSoundRender.SampleRate * 10;

	public static ProceduralSoundRender Render(ProceduralSoundPatch source)
		=> new Renderer(source).Render();

	sealed class Renderer
	{
		readonly ProceduralSoundPatch patch;
		SfxrRandom random;

		double period;
		double maximumPeriod;
		double periodMultiplier;
		double periodMultiplierSlide;
		float dutyCycle;
		float dutyCycleSlide;
		double arpeggioMultiplier;
		int arpeggioTime;
		int elapsedSinceRepeat;

		readonly float[] noise = new float[32];
		readonly float[] flanger = new float[1024];

		public Renderer(ProceduralSoundPatch source)
		{
			patch = source.Clone();
			ClampPatch(patch);
			random = new(source.Seed);
			ResetRepeat();
		}

		public ProceduralSoundRender Render()
		{
			var lowPassPosition = 0f;
			var lowPassDelta = 0f;
			var highPassPosition = 0f;
			var lowPassCutoff = MathF.Pow(patch.LowPassCutoff, 3) * 0.1f;
			var lowPassEnabled = patch.LowPassCutoff != 1;
			var lowPassCutoffSlide = 1 + patch.LowPassSweep * 0.0001f;
			var lowPassDamping = 5 / (1 + patch.LowPassResonance * patch.LowPassResonance * 20) * (0.01f + lowPassCutoff);
			lowPassDamping = MathF.Min(lowPassDamping, 0.8f);
			var highPassCutoff = patch.HighPassCutoff * patch.HighPassCutoff * 0.1f;
			var highPassCutoffSlide = 1 + patch.HighPassSweep * 0.0003f;

			var vibratoPhase = 0f;
			var vibratoSpeed = patch.VibratoSpeed * patch.VibratoSpeed * 0.01f;
			var vibratoAmplitude = patch.VibratoDepth * 0.5f;

			Span<int> envelopeLength =
			[
				(int)(patch.Attack * patch.Attack * 100000),
				(int)(patch.Sustain * patch.Sustain * 100000),
				(int)(patch.Decay * patch.Decay * 100000),
			];
			var envelopeStage = 0;
			while (envelopeStage < envelopeLength.Length && envelopeLength[envelopeStage] == 0)
				envelopeStage++;
			if (envelopeStage >= envelopeLength.Length)
				return new([], [], 0);
			var envelopeElapsed = 0;

			var flangerOffset = SignedSquare(patch.FlangerOffset) * 1020;
			var flangerOffsetSlide = SignedSquare(patch.FlangerSweep);
			var flangerIndex = 0;
			Array.Clear(flanger);
			FillNoise();

			var repeatTime = (int)(MathF.Pow(1 - patch.RepeatSpeed, 2) * 20000 + 32);
			if (patch.RepeatSpeed == 0)
				repeatTime = 0;

			var gain = MathF.Exp(patch.Volume) - 1;
			var samples = new float[Math.Min(MaxSamples, envelopeLength[0] + envelopeLength[1] + envelopeLength[2])];
			var sampleCount = 0;
			var phase = 0;
			var fmPhase = 0f;
			var tremoloPhase = 0f;
			var heldSample = 0f;
			var sampleHoldTime = 0;
			var sampleHoldLength = 1 + (int)(patch.SampleRateReduction * patch.SampleRateReduction * 63);

			for (var time = 0; sampleCount < samples.Length; time++)
			{
				if (repeatTime != 0 && ++elapsedSinceRepeat >= repeatTime)
					ResetRepeat();

				if (arpeggioTime != 0 && time >= arpeggioTime)
				{
					arpeggioTime = 0;
					period *= arpeggioMultiplier;
				}

				periodMultiplier += periodMultiplierSlide;
				period *= periodMultiplier;
				if (period > maximumPeriod)
				{
					period = maximumPeriod;
					if (patch.MinimumFrequency > 0)
						break;
				}

				var vibratoPeriod = period;
				if (vibratoAmplitude > 0)
				{
					vibratoPhase += vibratoSpeed;
					vibratoPeriod = period * (1 + MathF.Sin(vibratoPhase) * vibratoAmplitude);
				}
				var integerPeriod = Math.Max(Oversampling, (int)vibratoPeriod);

				dutyCycle = Math.Clamp(dutyCycle + dutyCycleSlide, 0, 0.5f);

				if (++envelopeElapsed > envelopeLength[envelopeStage])
				{
					envelopeElapsed = 0;
					do envelopeStage++;
					while (envelopeStage < envelopeLength.Length && envelopeLength[envelopeStage] == 0);
					if (envelopeStage >= envelopeLength.Length)
						break;
				}

				var envelopeProgress = envelopeElapsed / (float)envelopeLength[envelopeStage];
				var envelopeVolume = envelopeStage switch
				{
					0 => envelopeProgress,
					1 => 1 + (1 - envelopeProgress) * 2 * patch.Punch,
					_ => 1 - envelopeProgress,
				};

				flangerOffset += flangerOffsetSlide;
				var flangerPhase = Math.Min(1023, Math.Abs((int)flangerOffset));

				highPassCutoff = Math.Clamp(highPassCutoff * highPassCutoffSlide, 0.00001f, 0.1f);

				var sample = 0f;
				for (var oversample = 0; oversample < Oversampling; oversample++)
				{
					phase++;
					if (phase >= integerPeriod)
					{
						phase %= integerPeriod;
						if (patch.Wave == ProceduralWave.Noise || patch.NoiseMix > 0)
							FillNoise();
					}

					var position = phase / (float)integerPeriod;
					if (patch.FmDepth > 0)
					{
						var fmRatio = 0.25f + patch.FmRatio * 7.75f;
						fmPhase += MathF.Tau * fmRatio / integerPeriod;
						position += MathF.Sin(fmPhase) * patch.FmDepth * 0.5f;
						position -= MathF.Floor(position);
					}
					var subSample = patch.Wave switch
					{
						ProceduralWave.Square => position < dutyCycle ? 0.5f : -0.5f,
						ProceduralWave.Sawtooth => position < dutyCycle
							? -1 + 2 * position / MathF.Max(dutyCycle, 0.0001f)
							: 1 - 2 * (position - dutyCycle) / MathF.Max(1 - dutyCycle, 0.0001f),
						ProceduralWave.Sine => MathF.Sin(position * 2 * MathF.PI),
						ProceduralWave.Triangle => 1 - 4 * MathF.Abs(position - 0.5f),
						_ => NoiseSample(phase, integerPeriod),
					};
					if (patch.NoiseMix > 0 && patch.Wave != ProceduralWave.Noise)
						subSample += (NoiseSample(phase, integerPeriod) - subSample) * patch.NoiseMix;
					if (patch.WaveShape != 0)
						subSample = MathF.CopySign(MathF.Pow(MathF.Abs(subSample), MathF.Pow(4, patch.WaveShape)), subSample);

					var previousLowPass = lowPassPosition;
					lowPassCutoff = Math.Clamp(lowPassCutoff * lowPassCutoffSlide, 0, 0.1f);
					if (lowPassEnabled)
					{
						lowPassDelta += (subSample - lowPassPosition) * lowPassCutoff;
						lowPassDelta -= lowPassDelta * lowPassDamping;
					}
					else
					{
						lowPassPosition = subSample;
						lowPassDelta = 0;
					}
					lowPassPosition += lowPassDelta;

					highPassPosition += lowPassPosition - previousLowPass;
					highPassPosition -= highPassPosition * highPassCutoff;
					subSample = highPassPosition;

					flanger[flangerIndex & 1023] = subSample;
					subSample += flanger[(flangerIndex - flangerPhase + 1024) & 1023];
					flangerIndex = (flangerIndex + 1) & 1023;

					sample += subSample * envelopeVolume;
				}

				var output = sample / Oversampling * gain;
				if (patch.TremoloDepth > 0)
				{
					var tremoloHz = 0.5f + patch.TremoloSpeed * patch.TremoloSpeed * 29.5f;
					tremoloPhase += MathF.Tau * tremoloHz / ProceduralSoundRender.SampleRate;
					output *= 1 - patch.TremoloDepth * 0.5f + MathF.Sin(tremoloPhase) * patch.TremoloDepth * 0.5f;
				}
				if (patch.BitCrush > 0)
				{
					var levels = MathF.Pow(2, 15 - patch.BitCrush * 14) - 1;
					output = MathF.Round(output * levels) / levels;
				}
				if (sampleHoldLength > 1)
				{
					if (sampleHoldTime++ % sampleHoldLength == 0)
						heldSample = output;
					output = heldSample;
				}
				samples[sampleCount++] = output;
			}

			if (sampleCount != samples.Length)
				Array.Resize(ref samples, sampleCount);
			ApplyDelay(ref samples);

			var pcm = new byte[samples.Length * sizeof(short)];
			var clipped = 0;
			for (var i = 0; i < samples.Length; i++)
			{
				var value = samples[i] * short.MaxValue;
				if (value > short.MaxValue || value < short.MinValue)
					clipped++;
				var integer = (short)Math.Clamp((int)value, short.MinValue, short.MaxValue);
				pcm[i * 2] = (byte)integer;
				pcm[i * 2 + 1] = (byte)(integer >> 8);
			}
			return new(samples, pcm, clipped);
		}

		void ApplyDelay(ref float[] samples)
		{
			if (patch.DelayTime <= 0 || patch.DelayMix <= 0 || samples.Length == 0)
				return;

			var delaySamples = Math.Max(1,
				(int)((0.01f + patch.DelayTime * patch.DelayTime * 0.49f) * ProceduralSoundRender.SampleRate));
			var feedback = MathF.Min(patch.DelayFeedback, 0.95f);
			var repeats = 1;
			for (var amplitude = feedback; amplitude > 0.01f && repeats < 16; amplitude *= feedback)
				repeats++;

			var dryLength = samples.Length;
			Array.Resize(ref samples, Math.Min(MaxSamples, dryLength + delaySamples * repeats));
			var delayLine = new float[delaySamples];
			var delayIndex = 0;
			for (var i = 0; i < samples.Length; i++)
			{
				var dry = i < dryLength ? samples[i] : 0;
				var delayed = delayLine[delayIndex];
				samples[i] = dry + delayed * patch.DelayMix;
				delayLine[delayIndex] = dry + delayed * feedback;
				if (++delayIndex == delayLine.Length)
					delayIndex = 0;
			}
		}

		void ResetRepeat()
		{
			elapsedSinceRepeat = 0;
			period = 100 / (patch.StartFrequency * patch.StartFrequency + 0.001);
			maximumPeriod = 100 / (patch.MinimumFrequency * patch.MinimumFrequency + 0.001);
			periodMultiplier = 1 - Math.Pow(patch.FrequencySlide, 3) * 0.01;
			periodMultiplierSlide = -Math.Pow(patch.FrequencyDeltaSlide, 3) * 0.000001;
			dutyCycle = 0.5f - patch.Duty * 0.5f;
			dutyCycleSlide = -patch.DutySweep * 0.00005f;
			arpeggioMultiplier = patch.ArpeggioAmount >= 0
				? 1 - patch.ArpeggioAmount * patch.ArpeggioAmount * 0.9
				: 1 + patch.ArpeggioAmount * patch.ArpeggioAmount * 10;
			arpeggioTime = (int)(MathF.Pow(1 - patch.ArpeggioSpeed, 2) * 20000 + 32);
			if (patch.ArpeggioSpeed == 1)
				arpeggioTime = 0;
		}

		void FillNoise()
		{
			for (var i = 0; i < noise.Length; i++)
				noise[i] = random.NextFloat() * 2 - 1;
		}

		float NoiseSample(int phase, int integerPeriod)
			=> noise[Math.Min(noise.Length - 1, phase * noise.Length / integerPeriod)];
	}

	public static ProceduralSoundPatch Preset(ProceduralSoundPreset preset, int seed)
	{
		var random = new SfxrRandom(seed);
		var patch = new ProceduralSoundPatch { Seed = seed };
		switch (preset)
		{
			case ProceduralSoundPreset.Pickup:
				patch.Wave = ProceduralWave.Sawtooth;
				patch.StartFrequency = 0.4f + random.Range(0.5f);
				patch.Sustain = random.Range(0.1f);
				patch.Decay = 0.1f + random.Range(0.4f);
				patch.Punch = 0.3f + random.Range(0.3f);
				if (random.CoinFlip())
				{
					patch.ArpeggioSpeed = 0.5f + random.Range(0.2f);
					patch.ArpeggioAmount = 0.2f + random.Range(0.4f);
				}
				break;
			case ProceduralSoundPreset.Laser:
				patch.Wave = (ProceduralWave)random.NextInt(2);
				patch.StartFrequency = 0.5f + random.Range(0.5f);
				patch.MinimumFrequency = MathF.Max(0.2f, patch.StartFrequency - 0.2f - random.Range(0.6f));
				patch.FrequencySlide = -0.15f - random.Range(0.2f);
				patch.Duty = random.CoinFlip() ? random.Range(0.5f) : 0.4f + random.Range(0.5f);
				patch.DutySweep = random.CoinFlip() ? random.Range(0.2f) : -random.Range(0.7f);
				patch.Sustain = 0.1f + random.Range(0.2f);
				patch.Decay = random.Range(0.4f);
				patch.HighPassCutoff = random.Range(0.3f);
				break;
			case ProceduralSoundPreset.Explosion:
				patch.Wave = ProceduralWave.Noise;
				patch.StartFrequency = MathF.Pow(0.1f + random.Range(0.4f), 2);
				patch.FrequencySlide = -0.1f + random.Range(0.4f);
				patch.Sustain = 0.1f + random.Range(0.3f);
				patch.Decay = random.Range(0.5f);
				patch.Punch = 0.2f + random.Range(0.6f);
				if (random.CoinFlip())
				{
					patch.FlangerOffset = -0.3f + random.Range(0.9f);
					patch.FlangerSweep = -random.Range(0.3f);
				}
				break;
			case ProceduralSoundPreset.PowerUp:
				patch.Wave = ProceduralWave.Sawtooth;
				patch.StartFrequency = 0.2f + random.Range(0.3f);
				patch.FrequencySlide = 0.1f + random.Range(0.4f);
				patch.RepeatSpeed = 0.4f + random.Range(0.4f);
				patch.Sustain = random.Range(0.4f);
				patch.Decay = 0.1f + random.Range(0.4f);
				break;
			case ProceduralSoundPreset.Hit:
				patch.Wave = random.CoinFlip() ? ProceduralWave.Square : ProceduralWave.Noise;
				patch.Duty = random.Range(0.6f);
				patch.StartFrequency = 0.2f + random.Range(0.6f);
				patch.FrequencySlide = -0.3f - random.Range(0.4f);
				patch.Sustain = random.Range(0.1f);
				patch.Decay = 0.1f + random.Range(0.2f);
				patch.HighPassCutoff = random.Range(0.3f);
				break;
			case ProceduralSoundPreset.Jump:
				patch.Wave = ProceduralWave.Square;
				patch.Duty = random.Range(0.6f);
				patch.StartFrequency = 0.3f + random.Range(0.3f);
				patch.FrequencySlide = 0.1f + random.Range(0.2f);
				patch.Sustain = 0.1f + random.Range(0.3f);
				patch.Decay = 0.1f + random.Range(0.2f);
				if (random.CoinFlip()) patch.HighPassCutoff = random.Range(0.3f);
				if (random.CoinFlip()) patch.LowPassCutoff = 1 - random.Range(0.6f);
				break;
			case ProceduralSoundPreset.Blip:
				patch.Wave = random.CoinFlip() ? ProceduralWave.Square : ProceduralWave.Sawtooth;
				patch.Duty = patch.Wave == ProceduralWave.Square ? random.Range(0.6f) : 1;
				patch.StartFrequency = 0.2f + random.Range(0.4f);
				patch.Sustain = 0.1f + random.Range(0.1f);
				patch.Decay = random.Range(0.2f);
				patch.HighPassCutoff = 0.1f;
				break;
			default:
				Randomize(patch, ref random);
				break;
		}
		ClampPatch(patch);
		return patch;
	}

	public static ProceduralSoundPatch Mutate(ProceduralSoundPatch source, int seed)
	{
		var patch = source.Clone();
		patch.Seed = seed;
		var random = new SfxrRandom(seed);
		Mutate(ref patch.StartFrequency, ref random);
		Mutate(ref patch.FrequencySlide, ref random);
		Mutate(ref patch.FrequencyDeltaSlide, ref random);
		Mutate(ref patch.Duty, ref random);
		Mutate(ref patch.DutySweep, ref random);
		Mutate(ref patch.NoiseMix, ref random);
		Mutate(ref patch.WaveShape, ref random);
		Mutate(ref patch.FmDepth, ref random);
		Mutate(ref patch.FmRatio, ref random);
		Mutate(ref patch.VibratoDepth, ref random);
		Mutate(ref patch.VibratoSpeed, ref random);
		Mutate(ref patch.TremoloDepth, ref random);
		Mutate(ref patch.TremoloSpeed, ref random);
		Mutate(ref patch.Attack, ref random);
		Mutate(ref patch.Sustain, ref random);
		Mutate(ref patch.Decay, ref random);
		Mutate(ref patch.Punch, ref random);
		Mutate(ref patch.LowPassResonance, ref random);
		Mutate(ref patch.LowPassCutoff, ref random);
		Mutate(ref patch.LowPassSweep, ref random);
		Mutate(ref patch.HighPassCutoff, ref random);
		Mutate(ref patch.HighPassSweep, ref random);
		Mutate(ref patch.FlangerOffset, ref random);
		Mutate(ref patch.FlangerSweep, ref random);
		Mutate(ref patch.BitCrush, ref random);
		Mutate(ref patch.SampleRateReduction, ref random);
		Mutate(ref patch.DelayTime, ref random);
		Mutate(ref patch.DelayFeedback, ref random);
		Mutate(ref patch.DelayMix, ref random);
		Mutate(ref patch.RepeatSpeed, ref random);
		Mutate(ref patch.ArpeggioSpeed, ref random);
		Mutate(ref patch.ArpeggioAmount, ref random);
		ClampPatch(patch);
		return patch;
	}

	static void Randomize(ProceduralSoundPatch patch, ref SfxrRandom random)
	{
		patch.Wave = (ProceduralWave)random.NextInt(4);
		patch.StartFrequency = random.CoinFlip() ? MathF.Pow(random.Range(2) - 1, 3) + 0.5f : MathF.Pow(random.Range(1), 2);
		patch.FrequencySlide = MathF.Pow(random.Range(2) - 1, 5);
		patch.FrequencyDeltaSlide = MathF.Pow(random.Range(2) - 1, 3);
		patch.Duty = random.Range(2) - 1;
		patch.DutySweep = MathF.Pow(random.Range(2) - 1, 3);
		if (random.CoinFlip()) patch.NoiseMix = random.Range(0.5f);
		if (random.CoinFlip()) patch.WaveShape = random.Range(2) - 1;
		if (random.CoinFlip())
		{
			patch.FmDepth = random.Range(0.6f);
			patch.FmRatio = random.Range(1);
		}
		patch.VibratoDepth = MathF.Pow(random.Range(2) - 1, 3);
		patch.VibratoSpeed = random.Range(2) - 1;
		if (random.CoinFlip())
		{
			patch.TremoloDepth = random.Range(0.7f);
			patch.TremoloSpeed = random.Range(1);
		}
		patch.Attack = MathF.Pow(random.Range(2) - 1, 3);
		patch.Sustain = MathF.Pow(random.Range(2) - 1, 2);
		patch.Decay = random.Range(2) - 1;
		patch.Punch = MathF.Pow(random.Range(0.8f), 2);
		if (patch.Attack + patch.Sustain + patch.Decay < 0.2f)
		{
			patch.Sustain += 0.2f + random.Range(0.3f);
			patch.Decay += 0.2f + random.Range(0.3f);
		}
		patch.LowPassResonance = random.Range(2) - 1;
		patch.LowPassCutoff = 1 - MathF.Pow(random.Range(1), 3);
		patch.LowPassSweep = MathF.Pow(random.Range(2) - 1, 3);
		patch.HighPassCutoff = MathF.Pow(random.Range(1), 5);
		patch.HighPassSweep = MathF.Pow(random.Range(2) - 1, 5);
		patch.FlangerOffset = MathF.Pow(random.Range(2) - 1, 3);
		patch.FlangerSweep = MathF.Pow(random.Range(2) - 1, 3);
		if (random.CoinFlip()) patch.BitCrush = random.Range(0.8f);
		if (random.CoinFlip()) patch.SampleRateReduction = random.Range(0.8f);
		if (random.CoinFlip())
		{
			patch.DelayTime = random.Range(0.6f);
			patch.DelayFeedback = random.Range(0.7f);
			patch.DelayMix = random.Range(0.6f);
		}
		patch.RepeatSpeed = random.Range(2) - 1;
		patch.ArpeggioSpeed = random.Range(2) - 1;
		patch.ArpeggioAmount = random.Range(2) - 1;
	}

	static void Mutate(ref float value, ref SfxrRandom random)
	{
		if (random.CoinFlip())
			value += random.Range(0.1f) - 0.05f;
	}

	static float SignedSquare(float value) => MathF.CopySign(value * value, value);

	static void ClampPatch(ProceduralSoundPatch patch)
	{
		patch.Volume = Math.Clamp(patch.Volume, 0, 1);
		patch.Attack = Math.Clamp(patch.Attack, 0, 1);
		patch.Sustain = Math.Clamp(patch.Sustain, 0, 1);
		patch.Punch = Math.Clamp(patch.Punch, 0, 1);
		patch.Decay = Math.Clamp(patch.Decay, 0, 1);
		patch.StartFrequency = Math.Clamp(patch.StartFrequency, 0, 1);
		patch.MinimumFrequency = Math.Clamp(patch.MinimumFrequency, 0, 1);
		patch.VibratoDepth = Math.Clamp(patch.VibratoDepth, 0, 1);
		patch.VibratoSpeed = Math.Clamp(patch.VibratoSpeed, 0, 1);
		patch.ArpeggioSpeed = Math.Clamp(patch.ArpeggioSpeed, 0, 1);
		patch.Duty = Math.Clamp(patch.Duty, 0, 1);
		patch.NoiseMix = Math.Clamp(patch.NoiseMix, 0, 1);
		patch.FmDepth = Math.Clamp(patch.FmDepth, 0, 1);
		patch.FmRatio = Math.Clamp(patch.FmRatio, 0, 1);
		patch.RepeatSpeed = Math.Clamp(patch.RepeatSpeed, 0, 1);
		patch.TremoloDepth = Math.Clamp(patch.TremoloDepth, 0, 1);
		patch.TremoloSpeed = Math.Clamp(patch.TremoloSpeed, 0, 1);
		patch.BitCrush = Math.Clamp(patch.BitCrush, 0, 1);
		patch.SampleRateReduction = Math.Clamp(patch.SampleRateReduction, 0, 1);
		patch.DelayTime = Math.Clamp(patch.DelayTime, 0, 1);
		patch.DelayFeedback = Math.Clamp(patch.DelayFeedback, 0, 1);
		patch.DelayMix = Math.Clamp(patch.DelayMix, 0, 1);
		patch.LowPassCutoff = Math.Clamp(patch.LowPassCutoff, 0, 1);
		patch.LowPassResonance = Math.Clamp(patch.LowPassResonance, 0, 1);
		patch.HighPassCutoff = Math.Clamp(patch.HighPassCutoff, 0, 1);

		patch.FrequencySlide = Math.Clamp(patch.FrequencySlide, -1, 1);
		patch.FrequencyDeltaSlide = Math.Clamp(patch.FrequencyDeltaSlide, -1, 1);
		patch.ArpeggioAmount = Math.Clamp(patch.ArpeggioAmount, -1, 1);
		patch.DutySweep = Math.Clamp(patch.DutySweep, -1, 1);
		patch.WaveShape = Math.Clamp(patch.WaveShape, -1, 1);
		patch.FlangerOffset = Math.Clamp(patch.FlangerOffset, -1, 1);
		patch.FlangerSweep = Math.Clamp(patch.FlangerSweep, -1, 1);
		patch.LowPassSweep = Math.Clamp(patch.LowPassSweep, -1, 1);
		patch.HighPassSweep = Math.Clamp(patch.HighPassSweep, -1, 1);
	}

	struct SfxrRandom
	{
		uint state;

		public SfxrRandom(int seed)
		{
			state = (uint)seed;
			if (state == 0)
				state = 0x6D2B79F5u;
		}

		public float NextFloat()
		{
			state ^= state << 13;
			state ^= state >> 17;
			state ^= state << 5;
			return (state >> 8) * (1f / 16777216f);
		}

		public float Range(float range) => NextFloat() * range;
		public int NextInt(int inclusiveMaximum) => (int)(NextFloat() * (inclusiveMaximum + 1));
		public bool CoinFlip() => NextInt(1) != 0;
	}
}
#endif
