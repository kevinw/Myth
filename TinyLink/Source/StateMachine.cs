namespace TinyLink;

public sealed class StateMachine<T>(T initial)
	where T : struct, Enum
{
	public T Current { get; private set; } = initial;
	public T Previous { get; private set; } = initial;
	public float Time { get; set; }
	public float PreviousTime { get; private set; }
	bool JustChanged;

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

	public void Update(float delta)
	{
		if (JustChanged)
		{
			JustChanged = false;
			return;
		}

		PreviousTime = Time;
		Time += delta;
	}
}
