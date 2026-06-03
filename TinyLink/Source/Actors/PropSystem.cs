using Foster.Framework;
namespace TinyLink;

public struct Prop
{
	public string SpriteName;
}

public class ActorSystem : Actor
{

}

public class PropSystem : ActorSystem
{
	public Prop[] props = [];

	public override void Render(Batcher batcher)
	{
		base.Render(batcher);

		/*
		TODO: implement static prop rendering here.
		foreach (var visibleRoom in Game.VisibleRooms())
		{
			var rect = new Rect(10, 10, 40, 20);
			rect.Position += visibleRoom.WorldBounds.Position;
			batcher.RectLine(rect, 2.0f, Color.AliceBlue);
		}
		*/
	}
}
