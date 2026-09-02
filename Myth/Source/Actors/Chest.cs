using Foster.Framework;
namespace Myth;

public enum ChestLoot { Mycelon, BurrowUpgrade }

[GameActor(LivePositionEditable = true)]
public class Chest : Actor
{
	public ChestLoot Loot = ChestLoot.Mycelon;

	[I(1, 8), VisibleWhen(nameof(Loot), ChestLoot.Mycelon)]
	public int MycelonDrop = 3; // coins scattered when a Mycelon chest is broken

	bool opened;   // now an open husk; blocks re-breaking

	public Chest()
	{
		Mask = Masks.Solid | Masks.Destructible;
		Hitbox = Hitbox.FromSprite(Sprite);
	}

	public override void Added()
	{
		base.Added();
		if (SessionDestroyKey is { } key && Game.Save.OpenedChests.Contains(key))
			Open(spawnLoot: true); // reload of an already-opened chest: re-place missed loot
		else
			Play("closed");
	}

	internal override void OnWasHit(HitInfo hitInfo)
	{
		if (opened)
			return;
		if (SessionDestroyKey is { } key)
			Game.Save.OpenedChests.Add(key);
		Game.Create<Pop>(Position);
		Game.HitStun(0.22f);
		SpawnLoot(fresh: true);
		Open(spawnLoot: false); // fresh loot already spawned
	}

	// Becomes the open husk: swaps to the open sprite, stops being destructible, and (on reload)
	// re-places any still-uncollected loot floating above it.
	void Open(bool spawnLoot)
	{
		opened = true;
		Play("open");
		Mask = Masks.None;
		if (spawnLoot)
			SpawnLoot(fresh: false);
	}

	void SpawnLoot(bool fresh)
	{
		var key = SessionDestroyKey ?? "";
		if (Loot == ChestLoot.BurrowUpgrade)
		{
			var itemKey = $"{key}:burrow";
			if (!Collected(itemKey))
				SpawnFloating(itemKey, PickupKind.BurrowUpgrade, LootAnchor(0, 1));
			return;
		}

		// Mycelon: pop-and-scatter on a fresh break; on reload, place the missed coins floating.
		if (fresh)
		{
			Pickup.Scatter(Game, Position, PickupKind.Mycelon, MycelonDrop, baseKey: $"{key}:mycelon");
			return;
		}
		for (var i = 0; i < MycelonDrop; i++)
		{
			var itemKey = $"{key}:mycelon:{i}";
			if (!Collected(itemKey))
				SpawnFloating(itemKey, PickupKind.Mycelon, LootAnchor(i, MycelonDrop));
		}
	}

	bool Collected(string itemKey) => Game.Save.PersistentDestroyedActors.Contains(itemKey);

	void SpawnFloating(string itemKey, PickupKind kind, Point2 position)
		=> Game.Create<FloatingPickup>(position, p =>
		{
			p.Kind = kind;
			p.SessionDestroyKey = itemKey;
			p.Invert = Invert;
		});

	// A tidy row centered above the chest — deterministic so reload placement is stable.
	Point2 LootAnchor(int index, int count)
	{
		const int spacing = 7;
		var x = Position.X + (int)MathF.Round((index - (count - 1) / 2f) * spacing);
		return new Point2(x, Position.Y - Game.TileSize + 1);
	}
}
