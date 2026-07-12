using Foster.Framework;

namespace Myth;

// A coiled rope sitting on a ledge. Touch it and it finds the open drop beside the lip and
// unfurls a climbable Rope down it -- lets the player "pave the way" so Bud can follow down
// (or up) a cliff. One-shot per room load.
[GameActor(Tags = ActorTag.Obstacle)]
public class RopePile : Actor
{
	public int MaxLength = 160;
	public float RopeWeight = 1f;

	static float TriggerRadius => 14f;
	const int DropProbeStep = 4;

	bool deployed;

	public RopePile()
	{
		Mask = Masks.None;
		CollidesWithSolids = false;
	}

	public override void Update()
	{
		base.Update();
		if (deployed || Game.GetFirst<Player>() is not { } player)
			return;
		if ((player.Position - Position).LengthSquared() > TriggerRadius * TriggerRadius)
			return;

		deployed = true;
		Squish = new(1.4f, 0.6f);
		Deploy();
	}

	// The pile sits on the ledge floor, so straight down is solid; scan outward for the open
	// column at the lip and hang the rope there.
	void Deploy()
	{
		ReadOnlySpan<int> offsets = [0, 4, -4, 8, -8, 12, -12, 16, -16];
		foreach (var dx in offsets)
		{
			var x = Position.X + dx;
			if (Game.OverlapsRoomCollision(new RectInt(x, Position.Y + 1, 1, Game.TileSize * 2)))
				continue;

			var length = DropLength(x);
			if (length < Game.TileSize * 2)
				continue;

			Game.Create<Rope>(new Point2(x - Rope.DefaultSize.X / 2, Position.Y), rope =>
			{
				rope.Size = new(Rope.DefaultSize.X, length);
				rope.Weight = RopeWeight;
				rope.StartCoiled = true;
			});
			return;
		}
		deployed = false; // nowhere to drop; stay armed (maybe the world changes)
	}

	int DropLength(int x)
	{
		var length = 0;
		while (length < MaxLength && !Game.OverlapsRoomCollision(new RectInt(x, Position.Y + 1 + length, 1, DropProbeStep)))
			length += DropProbeStep;
		return length;
	}
}
