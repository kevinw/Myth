using System.Numerics;
using Foster.Framework;

namespace Myth;

[GameActor(Tags = ActorTag.Door, LivePositionEditable = true, LiveResizable = true)]
public class Door : Actor, IActivatable, IResizable
{
	public static readonly Point2 DefaultSize = new(12, 16);

	public bool StartsVisible;
	public bool Locked;
	// A persistent PressesOnce Button may live in another room; its saved latch keeps this shortcut open.
	public string PersistentButtonId = "";
	Point2 size = DefaultSize;
	public Point2 Size
	{
		get => size;
		set
		{
			size = value;
			Hitbox = new(DoorRect);
		}
	}
	float delay;
	bool activationOpen;
	bool pendingClose;
	HashSet<Actor>? activationSources;

	RectInt DoorRect => new(-Size.X / 2, -Size.Y, Size.X, Size.Y);

	public Door()
	{
		Depth = -1;
		Visible = false;
		Play("idle");
	}

	public override void Added()
	{
		base.Added();
		Hitbox = new(DoorRect);
		if ((StartsVisible || Locked) && !PersistentlyOpened)
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
		if (PersistentlyOpened)
		{
			OpenForGood();
			return;
		}
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

	// Opened by something that latches in the save: the door is gone for good, so it retires
	// itself and LoadRoom skips it on every later visit -- no spawn, no pop, no solid frame.
	void OpenForGood()
	{
		if (Visible)
		{
			Game.Create<Pop>(Position + new Point2(0, -8));
			AudioEvent("door/unlock");
		}
		Mask = Masks.None;
		Visible = false;
		Game.DestroyPersistently(this);
	}

	bool PersistentlyOpened => !string.IsNullOrWhiteSpace(PersistentButtonId) && Game.IsButtonPressed(PersistentButtonId);

	public override void Render(Batcher batcher) => RenderSized(batcher, SubtextureType.Color, Color.White);
	public override void RenderNormals(Batcher batcher) => RenderSized(batcher, SubtextureType.Normals, Color.White);
	public override void RenderEmissive(Batcher batcher) => RenderSized(batcher, SubtextureType.Emissive, Color.White);
	public override void RenderShadow(Batcher batcher, Color color) => RenderSized(batcher, SubtextureType.Color, color);
	public override void RenderReflection(Batcher batcher, Color color) => RenderSized(batcher, SubtextureType.Color, color);

	void RenderSized(Batcher batcher, SubtextureType type, Color color)
	{
		if (CurrentSpriteFrame is not { } frame)
			return;
		var texture = type switch
		{
			SubtextureType.Color => frame.Subtexture,
			SubtextureType.Normals => frame.NormalSubtexture,
			SubtextureType.Emissive => frame.EmissiveSubtexture,
			_ => default
		};
		if (texture.IsEmpty || texture.Size == Vector2.Zero)
			return;
		batcher.Image(texture, Vector2.Zero, Origin, (Vector2)Size / texture.Size, 0, color);
	}

	public override Rect GetVisualBounds() => new(Position + DoorRect.Position, DoorRect.Size);

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
		if (source is IActivationSource { ActivationIsPersistent: true })
		{
			OpenForGood();
			return;
		}
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
