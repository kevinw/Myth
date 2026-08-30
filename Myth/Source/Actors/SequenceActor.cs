using Foster.Framework;

namespace Myth;

public abstract class SequenceActor : Actor
{
	public bool Once;

	readonly Coroutine routine = new();
	bool started;
	bool ended;

	protected SequenceActor()
	{
		Sprite = null;
		Mask = Masks.None;
		CollidesWithSolids = false;
	}

	public override bool CastsSpriteShadow => false;
	public override bool CastsReflection => false;

	public override void Added()
	{
		base.Added();
		if (!Game.IsEditorPreview)
			routine.Start(Sequence());
	}

	public override void Update()
	{
		base.Update();
		if (!routine.Active || ended)
			return;

		try
		{
			if (!routine.Update(Time.Delta))
				EndSequence(completed: true);
		}
		catch (Exception e)
		{
			Console.Error.WriteLine($"{GetType().Name} failed: {e}");
			EndSequence(completed: false);
		}
	}

	protected abstract IEnumerator<float> Sequence();

	protected void SequenceStarted()
	{
		if (started)
			return;
		started = true;
		if (Once)
			Game.PreventActorRespawn(this);
	}

	protected virtual void SequenceEnded(bool completed) { }

	void EndSequence(bool completed)
	{
		if (ended)
			return;
		ended = true;
		routine.Stop();
		SequenceEnded(completed);
	}

	public sealed override void Destroyed() => EndSequence(completed: false);

	public override void Render(Batcher batcher) { }
	public override void RenderNormals(Batcher batcher) { }
	public override void RenderShadow(Batcher batcher, Color color) { }
	public override void RenderReflection(Batcher batcher, Color color) { }
}
