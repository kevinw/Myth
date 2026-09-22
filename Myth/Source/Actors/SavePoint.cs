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

	const string SavedText = "Saved!";
	const float SavedTextDuration = 1.1f;
	const float SavedTextFade = 0.35f;   // trailing seconds it spends fading out
	const float SavedTextY = -44;        // just above the 16px interact prompt at -31
	const float SavedTextRise = 6;
	const int SavedTextShadowOffset = 1;
	static readonly Color SavedTextShadowColor = new(0x000000, (byte)170);

	bool showInteract;
	float interactTime;
	bool playerWasOverlapping;
	float pulse; // 0..1, decays after a touch
	float savedTextTime; // counts down from SavedTextDuration after a touch

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
		savedTextTime = Calc.Approach(savedTextTime, 0, Time.Delta);

		showInteract = false;

		var player = Game.GetFirst<Player>();
		var touching = player != null && OverlapsAny(Point2.Zero, player);
		var canWarp = touching && player is { State: Player.States.Normal, IsGrounded: true };
		var activating = canWarp || touching && player?.State == Player.States.Burrow;

		if (canWarp)
		{
			showInteract = true;
			
			Game.View?.contextPrompt = "Press {MoveUp} to warp.";
			interactTime += Time.Delta;

			var moveInt = Game.Controls.Move.IntValue;
			if (moveInt.Y < 0 && moveInt.X == 0)
				Game.OpenSavePointWarp(this);
		}
		else
		{
			interactTime = 0;
		}

		// Rising edge: pulse and record the save (idempotent). Burrowing activates the
		// point too, but only a grounded normal player gets the warp interaction above.
		if (activating && !playerWasOverlapping)
		{
			pulse = 1f;
			savedTextTime = SavedTextDuration;
			player!.Health = Player.MaxHealth;
			Game.ActivateSavePoint(this);
			AudioEvent("save_point/activate");
		}

		playerWasOverlapping = activating;
	}

	float LightIntensity => (Activated ? 0.76f : 0.56f) + pulse * 0.5f;

	public override void Render(Batcher batcher)
	{
		// Red line on the ground, centered on Position.
		batcher.Rect(new Rect(-LineWidth / 2f, -1, LineWidth, 1), LineColor);

		DrawSavedText(batcher);
	}

	// Confirmation of the save the touch just recorded: drifts up and fades out. Same drop
	// shadow as speech and TitleText so it stays readable over whatever is behind it.
	void DrawSavedText(Batcher batcher)
	{
		if (savedTextTime <= 0 || (Assets.SpeechFont ?? Assets.Font) is not { } font)
			return;

		var elapsed = 1 - savedTextTime / SavedTextDuration;
		var alpha = Math.Clamp(savedTextTime / SavedTextFade, 0, 1);
		var position = new Vector2(0, MathF.Round(SavedTextY - Ease.Cube.Out(elapsed) * SavedTextRise));
		var justify = new Vector2(0.5f, 0.5f);

		batcher.Text(font, SavedText, position + Vector2.One * SavedTextShadowOffset, justify, font.Size, SavedTextShadowColor * alpha);
		batcher.Text(font, SavedText, position, justify, font.Size, LightColor * alpha);
	}

	public override void RenderEmissive(Batcher batcher)
	{
		RenderLightColumn(batcher);

		if (!showInteract)
			return;


		if (Assets.GetSprite("interact") is { Animations.Count: > 0 } interactSprite)
		{
			var frame = interactSprite.GetFrameAt(interactSprite.Animations[0], interactTime, true);
			batcher.Image(frame.Subtexture, new Vector2(-8, -31), interactSprite.Origin, Vector2.One, 0, Color.White);
		}
	}

	public override void RenderLightEmission(Batcher batcher) => RenderLightColumn(batcher);

	void RenderLightColumn(Batcher batcher)
	{
		var top = LightColor * 0f;
		var bottom = LightColor * LightIntensity;
		batcher.Rect(new Rect(-LineWidth / 2f, -LightHeight, LineWidth, LightHeight), top, top, bottom, bottom);
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
	{
		var bounds = new Rect(Position.X - LineWidth / 2f, Position.Y - LightHeight, LineWidth, LightHeight);
		// "Saved!" is wider than the light column, so it must not cull with it.
		return savedTextTime > 0 && (Assets.SpeechFont ?? Assets.Font) is { } font
			? bounds.InflateX(MathF.Ceiling(font.WidthOf(SavedText) * 0.5f))
			: bounds;
	}
}
