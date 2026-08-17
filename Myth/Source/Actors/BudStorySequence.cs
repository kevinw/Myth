using System.Globalization;
using System.Numerics;
using Foster.Framework;

namespace Myth;

public enum BudStorySequenceTrigger
{
	RoomEntered,
	SourceDestroyed
}

[GameActor(Sprite = "Editor/text", BoundsX = -4, BoundsY = -4, BoundsW = 8, BoundsH = 8,
	Tags = ActorTag.System, LivePositionEditable = true)]
public class BudStorySequence : Actor
{
	readonly record struct Step(string? Text, float Pause);

	public BudStorySequenceTrigger Trigger;
	public string SourceActorId = "";
	public bool Once;
	// One bark per line; a line such as [pause 2.5] waits between barks.
	public string Script = "";

	Step[] steps = [];
	Game.StorySpeechBubble? speech;
	bool? sourceActive;
	bool triggered;
	bool running;
	bool onceRecorded;
	int stepIndex;
	float pause;

	public BudStorySequence()
	{
		Sprite = null;
		CollidesWithSolids = false;
	}

	public override bool CastsSpriteShadow => false;
	public override bool CastsReflection => false;

	public override void Added()
	{
		base.Added();
		steps = ParseScript(Script);
		triggered = Trigger == BudStorySequenceTrigger.RoomEntered;
		if (!Game.IsEditorPreview)
			TryStart();
	}

	public override void Update()
	{
		base.Update();
		if (Game.IsEditorPreview)
			return;
		UpdateTrigger();
		TryStart();
		if (!running)
			return;

		if (speech is { Finished: false })
			return;
		speech = null;
		if (pause > 0)
		{
			pause -= Time.Delta;
			return;
		}

		while (stepIndex < steps.Length)
		{
			var step = steps[stepIndex];
			if (step.Pause > 0)
			{
				stepIndex++;
				pause = step.Pause;
				return;
			}
			if (step.Text != null && Game.CanShowSpeech() && Game.GetFirst<Bud>() is { } bud)
			{
				speech = bud.Say(step.Text);
				stepIndex++;
				if (speech != null && Once && !onceRecorded)
				{
					onceRecorded = true;
					Game.PreventActorRespawn(this);
				}
				return;
			}
			return;
		}

		running = false;
		triggered = false;
		Game.EndBudStorySequence(this);
	}

	void TryStart()
	{
		if (triggered && !running && Game.BeginBudStorySequence(this, interrupt: true))
			running = true;
	}

	void UpdateTrigger()
	{
		if (triggered || Trigger != BudStorySequenceTrigger.SourceDestroyed || string.IsNullOrWhiteSpace(SourceActorId))
			return;
		var next = Game.GetActorById(SourceActorId) != null;
		if (sourceActive == true && !next)
			triggered = true;
		sourceActive = next;
	}

	internal void Cancel()
	{
		Game.CancelSpeech(speech);
		speech = null;
		running = false;
		triggered = false;
		Game.EndBudStorySequence(this);
	}

	public override void Destroyed() => Cancel();

	static Step[] ParseScript(string script)
	{
		var result = new List<Step>();
		foreach (var raw in script.ReplaceLineEndings("\n").Split('\n'))
		{
			var line = raw.Trim();
			if (line.Length == 0)
				continue;
			if (line.StartsWith("[pause ", StringComparison.OrdinalIgnoreCase) && line.EndsWith(']') &&
				float.TryParse(line.AsSpan(7, line.Length - 8), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
			{
				if (seconds > 0)
					result.Add(new(null, seconds));
				continue;
			}
			result.Add(new(line, 0));
		}
		return [.. result];
	}

	public override void Render(Batcher batcher) { }
	public override void RenderNormals(Batcher batcher) { }
	public override void RenderShadow(Batcher batcher, Color color) { }
	public override void RenderReflection(Batcher batcher, Color color) { }
}
