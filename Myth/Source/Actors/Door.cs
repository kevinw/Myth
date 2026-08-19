using Foster.Framework;

namespace Myth;

[GameActor(Tags = ActorTag.Door, LivePositionEditable = true)]
public class Door : Actor, IActivatable
{
	public bool StartsVisible;
	public bool Locked;
	float delay;
	bool activationOpen;
	bool pendingClose;
	HashSet<Actor>? activationSources;

	static RectInt DoorHitbox => new(-6, -16, 12, 16);

	public Door()
	{
		Depth = -1;
		Visible = false;
		Play("idle");
	}

	public override void Added()
	{
		base.Added();
		Hitbox = new(DoorHitbox);
		if (StartsVisible || Locked)
			Appear();
	}

	public void Appear()
	{
		Mask = Masks.Solid;
		Visible = true;

		if (Timer > 0.1f)
		{
			Game.Create<Pop>(Position + new Point2(0, -8));
			AudioEvent("door/unlock"); // TODO: use a different sound for lock/unlock
		}
	}

	public override void Update()
	{
		base.Update();
		if (activationOpen)
			return;
		if (pendingClose)
		{
			if (CanClose())
			{
				pendingClose = false;
				Appear();
			}
			return;
		}

		// wait to appear
		if (!Visible)
		{
			if (Game.GetFirst(Masks.Player) is not Actor player || player.Position.X > Position.X + 12)
				Appear();
		}
		// wait for all enemies to be gone
		else if (!Locked && Timer > 0.25f)
		{
			bool anyEnemiesAlive = false;

			foreach (var it in Game.Actors)
				if (it.Mask == Masks.Enemy)
				{
					anyEnemiesAlive = true;
					break;
				}

			if (!anyEnemiesAlive)
			{
				if (delay > 0.50f)
				{
					AudioEvent("door/unlock");
					Game.Create<Pop>(Position + new Point2(0, -8));
					Game.Destroy(this);
				}
				delay += Time.Delta;
			}
		}
	}

	public void SetActive(Actor source, bool active)
	{
		activationSources ??= [];
		if (active ? !activationSources.Add(source) : !activationSources.Remove(source))
			return;

		activationOpen = activationSources.Count > 0;
		pendingClose = !activationOpen;
		if (!activationOpen)
			return;

		pendingClose = false;
		if (Visible)
			Game.Create<Pop>(Position + new Point2(0, -8));
		Mask = Masks.None;
		Visible = false;
	}

	bool CanClose()
	{
		foreach (var actor in Game.Actors)
			if ((actor is Pawn || actor is ICarryable { CarriedBy: null }) && actor.OverlapsAny(Point2.Zero, this))
				return false;
		return true;
	}
}
