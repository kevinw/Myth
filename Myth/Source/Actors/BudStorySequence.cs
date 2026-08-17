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
	readonly record struct Step(string? Text, float Pause, bool PlayerSpeaks);

	public BudStorySequenceTrigger Trigger;
	[ActorRef] public string SourceActorId = "";
	public bool Once;
	// One bark per line; a line such as [pause 2.5] waits between barks. A line prefixed
	// "you:" is spoken by the player (over their head, in their color) instead of Bud;
	// "bud:" says so explicitly, and an unprefixed line is Bud as before.
	[Multiline(6)] public string Script = "";

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
			if (step.Text != null && Game.CanShowSpeech() && TrySpeak(step, out speech))
			{
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

	// False when the step's speaker isn't in the room, so the step waits for a later frame
	// (a null bubble is fine -- speech can be switched off, and the sequence still advances).
	bool TrySpeak(in Step step, out Game.StorySpeechBubble? bubble)
	{
		bubble = null;
		if (step.PlayerSpeaks)
		{
			if (Game.GetFirst<Player>() is not { } player)
				return false;
			bubble = player.Say(step.Text!);
			return true;
		}

		if (Game.GetFirst<Bud>() is not { } bud)
			return false;
		bubble = bud.Say(step.Text!);
		return true;
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
					result.Add(new(null, seconds, false));
				continue;
			}

			var playerSpeaks = TryTakeSpeaker(ref line, "you:");
			if (!playerSpeaks)
				TryTakeSpeaker(ref line, "bud:");
			if (line.Length == 0)
				continue;

			result.Add(new(line, 0, playerSpeaks));
		}
		return [.. result];
	}

	static bool TryTakeSpeaker(ref string line, string prefix)
	{
		if (!line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
			return false;

		line = line[prefix.Length..].TrimStart();
		return true;
	}

	public override void Render(Batcher batcher) { }
	public override void RenderNormals(Batcher batcher) { }
	public override void RenderShadow(Batcher batcher, Color color) { }
	public override void RenderReflection(Batcher batcher, Color color) { }
}
