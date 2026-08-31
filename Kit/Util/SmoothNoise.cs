namespace Kit;

public static class SmoothNoise
{
	public static float Signed(float position, uint seed)
	{
		var cell = (int)MathF.Floor(position);
		var amount = position - cell;
		amount = amount * amount * amount * (amount * (amount * 6 - 15) + 10);
		return float.Lerp(Value(cell, seed), Value(cell + 1, seed), amount);
	}

	static float Value(int cell, uint seed)
		=> (Hash32.Mix(seed ^ unchecked((uint)cell) * 0x9e3779b9u) & 0xffff) / 32767.5f - 1;
}
