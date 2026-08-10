namespace FosterGames;

public sealed class StateMachine<T>(T initial = default)
	where T : struct, Enum
{
	public T Current { get; private set; } = initial;
	public T Previous { get; private set; } = initial;
	public float Time { get; set; }
	public float PreviousTime { get; private set; }
	bool JustChanged;

	public bool JustCrossedTime(float threshold) => PreviousTime < threshold && Time >= threshold;

	public bool Change(T next)
	{
		if (EqualityComparer<T>.Default.Equals(Current, next))
			return false;

		Previous = Current;
		Current = next;
		Time = 0;
		PreviousTime = 0;
		JustChanged = true;
		return true;
	}

	public T Update(float delta)
	{
		if (JustChanged)
		{
			JustChanged = false;
			return Current;
		}

		PreviousTime = Time;
		Time += delta;
		return Current;
	}
}
