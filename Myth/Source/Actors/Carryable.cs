using System.Numerics;
using Foster.Framework;

namespace Myth;

// An object the player can hoist overhead by burrow-popping underneath it (Player.Carry.cs).
// While carried it is collision-inert and rides above the player's head — the player's
// swollen standing hitbox is the combined body, so too-short passages block naturally.
// Released, it flies under pawn gravity and settles where it lands. Not itself Solid: it
// obstructs only through the carry hitbox.
public abstract class Carryable : Actor
{
	static float LandSquishX => 1.35f;
	static float LandSquishY => 0.65f;
	static int OutOfRoomResetMargin => 24;

	[NoSerialize] public Player? CarriedBy;
	[NoSerialize] bool airborne;
	[NoSerialize] Point2 spawnPosition;

	// Height added on top of the player's standing hitbox while carried.
	public int CarryHeight => Hitbox.Rect.Height;

	public Carryable()
	{
		Hitbox = new(new RectInt(-6, -10, 12, 10));
		Anchor = SpriteAnchor.BottomCenter;
	}

	public override void Added()
	{
		base.Added();
		spawnPosition = Position;
	}

	public void OnPickedUp(Player player)
	{
		CarriedBy = player;
		airborne = false;
		KeepOnRoomTransition = true; // the carry can span rooms
		Stop();
		Squish = new Vector2(0.8f, 1.25f);
	}

	public void Release(Point2 position, Vector2 velocity)
	{
		CarriedBy = null;
		KeepOnRoomTransition = false;
		airborne = true;
		Position = position;
		Velocity = velocity;
		// A hurt-drop or tight-corner release can start with a corner inside geometry.
		TryUnstick(new Vector2(0, -1));
	}

	public override void Update()
	{
		if (Game.Rewinding) return;

		base.Update();

		if (CarriedBy != null)
			return; // the carrier syncs our position after its own move

		if (!airborne)
			return;

		Velocity.Y += Pawn.Gravity * Time.Delta;
		if (Velocity.Y > Pawn.TerminalVelocity)
			Velocity.Y = Pawn.TerminalVelocity;

		if (Velocity.Y >= 0 && Grounded())
		{
			Stop();
			airborne = false;
			Squish = new Vector2(LandSquishX, LandSquishY);
			AudioEvent("land", "Type", Game.GetSurfaceTypeBelow(this));
		}

		// Fell out of the room (thrown off a cliff edge): reset to spawn so the puzzle
		// can't soft-lock.
		if (Position.Y > Game.Bounds.Bottom + OutOfRoomResetMargin)
		{
			Position = spawnPosition;
			Stop();
		}
	}

	public override bool CastsSpriteShadow => true;
	public override bool CastsReflection => true;

	public override void Render(Batcher batcher)
	{
		if (Sprite != null)
		{
			base.Render(batcher);
			return;
		}
		RenderBox(batcher, FrameColor, FaceColor);
	}

	public override void RenderShadow(Batcher batcher, Color color)
	{
		if (Sprite != null)
		{
			base.RenderShadow(batcher, color);
			return;
		}
		RenderBox(batcher, color, color);
	}

	public override void RenderReflection(Batcher batcher, Color color)
	{
		if (Sprite != null)
		{
			base.RenderReflection(batcher, color);
			return;
		}
		RenderBox(batcher, color, color);
	}

	static Color FrameColor => 0x6a4a2b;
	static Color FaceColor => 0x9a7444;

	// Placeholder box until real art exists (a sprite named after the class auto-attaches
	// and takes over).
	void RenderBox(Batcher batcher, Color frame, Color face)
	{
		var rect = Hitbox.Rect;
		batcher.PushMatrix(Matrix3x2.CreateScale(Squish.X, Squish.Y));
		batcher.Rect(new Rect(rect.X, rect.Y, rect.Width, rect.Height), frame);
		batcher.Rect(new Rect(rect.X + 1, rect.Y + 1, rect.Width - 2, rect.Height - 2), face);
		batcher.PopMatrix();
	}
}

[GameActor(Sprite = "Editor/crate", Tags = ActorTag.Obstacle, Anchor = SpriteAnchor.BottomCenter,
	BoundsX = -6, BoundsY = -10, BoundsW = 12, BoundsH = 10)]
public class Crate : Carryable
{
}
