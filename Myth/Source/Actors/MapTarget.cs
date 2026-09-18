namespace Myth;

// A map pin that lights up once an InspectPoint revealing its Group has been read, and goes
// out once the camera has framed this spot (see Game.CollectMapPins).
[GameActor(Sprite = "Editor/text", Icon = "location-dot", Tags = ActorTag.System,
	BoundsX = -4, BoundsY = -4, BoundsW = 8, BoundsH = 8, LivePositionEditable = true, ShowFacing = false)]
public sealed class MapTarget : Actor
{
	public string Group = "";

	public MapTarget()
	{
		Sprite = null;
		Mask = Masks.None;
		CollidesWithSolids = false;
	}

	public override bool CastsSpriteShadow => false;
	public override bool CastsReflection => false;
}
