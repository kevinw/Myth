namespace Kit;

public sealed class Coroutine : IDisposable
{
	IEnumerator<float>? current;
	IEnumerator<float>? pending;
	float wait;
	bool updating;
	bool hasPending;

	public bool Active => current != null || hasPending;

	public void Start(IEnumerator<float> routine)
	{
		ArgumentNullException.ThrowIfNull(routine);
		if (updating)
		{
			pending?.Dispose();
			pending = routine;
			hasPending = true;
			return;
		}

		var previous = current;
		current = routine;
		wait = 0;
		previous?.Dispose();
	}

	public bool Update(float delta)
	{
		if (current == null)
			return false;
		if (wait > 0)
		{
			wait -= delta;
			return true;
		}

		var active = current;
		bool advanced;
		updating = true;
		try
		{
			advanced = active.MoveNext();
		}
		finally
		{
			updating = false;
		}

		if (hasPending)
		{
			current = pending;
			pending = null;
			hasPending = false;
			wait = 0;
			active.Dispose();
			return Active;
		}
		if (advanced)
		{
			wait = active.Current;
			return true;
		}

		current = null;
		wait = 0;
		active.Dispose();
		return Active;
	}

	public void Stop()
	{
		if (updating)
		{
			pending?.Dispose();
			pending = null;
			hasPending = true;
			return;
		}

		var active = current;
		var queued = pending;
		current = null;
		pending = null;
		hasPending = false;
		wait = 0;
		active?.Dispose();
		queued?.Dispose();
	}

	public void Dispose() => Stop();
}
