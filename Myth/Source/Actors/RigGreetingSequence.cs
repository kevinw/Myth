using Foster.Framework;

namespace Myth;

[GameActor(Sprite = "Editor/text", BoundsX = -4, BoundsY = -4, BoundsW = 8, BoundsH = 8,
	Tags = ActorTag.System, LivePositionEditable = true)]
public class RigGreetingSequence : SequenceActor
{
	[ActorRef(typeof(RigPuppet))] public string RigId = "";
	public string Take = "KneelDown";
	[I(1, 64)] public float Distance = 28;
	[I(0, 2)] public float BlendDuration = 0.2f;
	[I(1, 60)] public float WalkTimeout = 10;
	[Multiline(4)] public string Line = "Sorry about this.";

	RigPuppet? rig;
	Game.StorySpeechBubble? speech;

	protected override IEnumerator<float> Sequence()
	{
		if (string.IsNullOrWhiteSpace(RigId))
			throw new InvalidOperationException("RigGreetingSequence requires a RigId.");
		Player? player;
		while ((player = Game.GetFirst<Player>()) == null ||
			(rig = Game.GetActorById(RigId) as RigPuppet) == null)
			yield return 0;

		SequenceStarted();
		rig.BeginScript();
		var side = rig.Position.X < player.Position.X ? -1 : 1;
		rig.WalkTo(player, side * Distance);
		var timeout = WalkTimeout;
		while (!rig.ScriptedArrived && timeout > 0)
		{
			timeout -= Time.Delta;
			yield return 0;
		}
		if (!rig.ScriptedArrived)
			yield break;

		rig.Facing = player.Position.X < rig.Position.X ? Signs.Negative : Signs.Positive;
		rig.PlayTake(Take, BlendDuration);
		while (!rig.AnimationFinished)
			yield return 0;

		while (!Game.CanShowSpeech())
			yield return 0;
		speech = rig.Say(Line);
		while (speech is { Finished: false })
			yield return 0;
		speech = null;
	}

	protected override void SequenceEnded(bool completed)
	{
		Game.CancelSpeech(speech);
		speech = null;
		rig?.EndScript();
	}
}
