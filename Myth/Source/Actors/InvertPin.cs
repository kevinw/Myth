namespace Myth;

[GameActor(Icon = "speaker", EditorMarker = true, BoundsX = -4, BoundsY = -4, BoundsW = 8, BoundsH = 8, Anchor = SpriteAnchor.Center, Tags = ActorTag.System, LivePositionEditable = true)]
public class InvertPin : Actor
{
	public enum PinMode
	{
		FreezeCurrent,
		OriginalTerrain
	}

	public float Radius = 40;
	public PinMode Mode = PinMode.FreezeCurrent;
}
