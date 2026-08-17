using Foster.Framework;

namespace Myth;

[GameActor(Icon = "speaker", BoundsX = -4, BoundsY = -4, BoundsW = 8, BoundsH = 8,
	Anchor = SpriteAnchor.Center, Tags = ActorTag.System, LivePositionEditable = true)]
public class LinkedSound : Actor
{
	public string EventName = "";
	[ActorRef] public string SourceActorId = "";
	public string EnabledParameter = "Enabled";
	public string DisabledEventName = "";

	SoundInstance? sound;
	bool? enabled;

	public LinkedSound()
	{
		Sprite = null;
		CollidesWithSolids = false;
	}

	public override bool CastsSpriteShadow => false;
	public override bool CastsReflection => false;

	public override void Added()
	{
		base.Added();
		if (string.IsNullOrWhiteSpace(EventName))
			return;

	}

	public override void Update()
	{
		if (sound == null && !Game.Editing) {
			sound = Game.CreateAudioEvent(EventName, Position);
			UpdateEnabled();
			sound?.Start();
		}
		base.Update();
		Game.SetAudioEventPosition(sound, Position);
		UpdateEnabled();
	}

	void UpdateEnabled()
	{
		var next = string.IsNullOrWhiteSpace(SourceActorId) || Game.GetActorById(SourceActorId) != null;
		if (enabled == next)
			return;

		if (enabled == true && !next && sound != null && !string.IsNullOrWhiteSpace(DisabledEventName))
			Game.PlayAudioEvent(DisabledEventName, position: Position);
		enabled = next;
		if (!string.IsNullOrWhiteSpace(EnabledParameter))
			Game.SetAudioEventParameter(sound, EnabledParameter, next ? 1 : 0);
	}

	public override void Destroyed()
		=> StopAndReleaseSound(ref sound, true);
}
