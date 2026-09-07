using Foster.Framework;

namespace Kit;

public struct WeightedEnumSelector<T> where T : struct, Enum
{
	static readonly T[] Values = Enum.GetValues<T>();

	static readonly string[] Names = Array.ConvertAll(Values, v => v.ToString());

	public static ReadOnlySpan<T> Choices => Values;
	public static string Name(T value) => Names[IndexOf(value)];

	float[]? weights;
	byte[]? misses;
	bool[]? gainsPriority;
	float totalWeight;
	float priorityPerMiss;
	byte maximumMisses;

	public readonly float TotalWeight => totalWeight;

	public void Begin(float priorityPerMiss = 0, int maximumMisses = 0)
	{
		weights ??= new float[Values.Length];
		misses ??= new byte[Values.Length];
		gainsPriority ??= new bool[Values.Length];
		Array.Clear(weights);
		Array.Clear(gainsPriority);
		totalWeight = 0;
		this.priorityPerMiss = MathF.Max(0, priorityPerMiss);
		this.maximumMisses = (byte)Math.Clamp(maximumMisses, 0, byte.MaxValue);
	}

	public void Set(T value, float weight, bool increasePriorityWhenMissed = true)
	{
		if (weights == null || misses == null || gainsPriority == null)
			Begin();
		var index = IndexOf(value);
		if (index < 0)
			throw new ArgumentOutOfRangeException(nameof(value));
		var adjusted = MathF.Max(0, weight);
		if (increasePriorityWhenMissed)
			adjusted *= 1 + misses![index] * priorityPerMiss;
		totalWeight += adjusted - weights![index];
		weights[index] = adjusted;
		gainsPriority![index] = increasePriorityWhenMissed;
	}

	public bool TryChoose(ref Rng rng, out T choice)
	{
		if (weights == null || misses == null || gainsPriority == null || totalWeight <= 0)
		{
			choice = default;
			return false;
		}

		var roll = rng.Float(totalWeight);
		var selected = -1;
		for (var i = 0; i < weights.Length; i++)
		{
			if (weights[i] <= 0)
				continue;
			selected = i;
			if (roll < weights[i])
				break;
			roll -= weights[i];
		}

		for (var i = 0; i < weights.Length; i++)
			if (weights[i] > 0 && gainsPriority[i])
				misses[i] = i == selected ? (byte)0 : (byte)Math.Min(maximumMisses, misses[i] + 1);
		choice = Values[selected];
		return true;
	}

	public readonly float Weight(T value)
	{
		var index = IndexOf(value);
		return index >= 0 && weights != null ? weights[index] : 0;
	}

	public readonly int Misses(T value)
	{
		var index = IndexOf(value);
		return index >= 0 && misses != null ? misses[index] : 0;
	}

	static int IndexOf(T value)
	{
		for (var i = 0; i < Values.Length; i++)
			if (EqualityComparer<T>.Default.Equals(Values[i], value))
				return i;
		return -1;
	}
}
