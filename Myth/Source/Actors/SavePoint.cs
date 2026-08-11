using System.Numerics;
using Foster.Framework;

namespace Myth;

// A ground marker the player can rest at: touching it records a warp/respawn
// destination (see SaveState.SavePoints / LastSavePoint), and pressing up opens the
// map in warp mode to travel between activated save points (see Minimap warp mode).
// For now it renders as a red line on the ground with a soft column of light above.
[GameActor(Sprite = "Editor/savepoint", Tags = ActorTag.System, BoundsX = -8, BoundsY = -40, BoundsW = 16, BoundsH = 40,
	LivePositionEditable = true, ShowFacing = false)]
public class SavePoint : Actor
{
	const int LineWidth = 2 * Game.TileSize;   // 2 tiles wide
	const int LightHeight = 5 * Game.TileSize;  // column rises 5 tiles

	static readonly Color LineColor = 0xd23c3c;
	static readonly Color LightColor = 0xefefef;

	bool showInteract;
	float interactTime;
	bool playerWasOverlapping;
	float pulse; // 0..1, decays after a touch

	public SavePoint()
	{
		Sprite = null;
		Hitbox = new(new RectInt(-LineWidth / 2, -Game.TileSize * 2, LineWidth, Game.TileSize * 2));
		CollidesWithSolids = false;
		Depth = 2;
	}

	public override bool CastsSpriteShadow => false;
	public override bool CastsReflection => false;

	bool Activated => ActorId is { } id && Game.Save.SavePoints.Contains(id);

	public override void Update()
	{
		base.Update();

		pulse = Calc.Approach(pulse, 0, Time.Delta * 2.5f);

		showInteract = false;

		var player = Game.GetFirst<Player>();
		var overlapping = player != null
			&& player.State == Player.States.Normal
			&& player.IsGrounded
			&& OverlapsAny(Point2.Zero, player);

		if (overlapping)
		{
			showInteract = true;
			interactTime += Time.Delta;

			// Rising edge: pulse and record the save (idempotent).
			if (!playerWasOverlapping)
			{
				pulse = 1f;
				Game.ActivateSavePoint(this);
			}

			var moveInt = Game.Controls.Move.IntValue;
			if (moveInt.Y < 0 && moveInt.X == 0)
				Game.OpenSavePointWarp(this);
		}
		else
		{
			interactTime = 0;
		}

		playerWasOverlapping = overlapping;
	}

	float LightIntensity => (Activated ? 0.76f : 0.56f) + pulse * 0.5f;

	public override void Render(Batcher batcher)
	{
		// Red line on the ground, centered on Position.
		batcher.Rect(new Rect(-LineWidth / 2f, -1, LineWidth, 1), LineColor);
	}

	public override void RenderEmissive(Batcher batcher)
	{
		// Soft column of light rising from the line, brighter at the base, fading up.
		var alpha = LightIntensity;
		var top = LightColor * 0f;
		var bottom = LightColor * alpha;
		batcher.Rect(new Rect(-LineWidth / 2f, -LightHeight, LineWidth, LightHeight), top, top, bottom, bottom);

		if (!showInteract)
			return;

		if (Assets.GetSprite("interact") is { Animations.Count: > 0 } interactSprite)
		{
			var frame = interactSprite.GetFrameAt(interactSprite.Animations[0], interactTime, true);
			batcher.Image(frame.Subtexture, new Vector2(-8, -31), interactSprite.Origin, Vector2.One, 0, Color.White);
		}
	}

	// A faint point light a couple tiles up so the save point actually lights its surroundings.
	public override void ForEachLight(Action<Vector2, float, float, float, Color> action)
	{
		var intensity = LightIntensity;
		if (intensity <= 0.001f)
			return;
		var origin = (Vector2)Position + new Vector2(0, -Game.TileSize * 2);
		action(origin, LightHeight * 0.8f, intensity, 12f, LightColor);
	}

	public override Rect GetVisualBounds()
		=> new(Position.X - LineWidth / 2f, Position.Y - LightHeight, LineWidth, LightHeight);
}
