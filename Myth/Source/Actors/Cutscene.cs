using System.Numerics;
using Foster.Framework;
using OutLoud;

namespace Myth;

[GameActor(Sprite = "Editor/text", Icon = "clapperboard", BoundsX = -4, BoundsY = -4, BoundsW = 8, BoundsH = 8, Tags = ActorTag.System,
	LivePositionEditable = true)]
public class Cutscene : Actor
{
	static float DescentSpeed => 0.12f;
	static float SlowTimeScale => 0.12f;
	static float PortraitScale => 12f;
	static float PupilScale => 80f;
	static Vector2 PortraitPupilOffset => new(1, -9);

	readonly Coroutine routine = new();
	float previousTimeScale;
	float previousDitherGlitch;
	float previousDitherGlitchTarget;
	Vector2 previousDitherGlitchAxis;
	Vector2? previousDitherGlitchPosition;
	Player? player;
	Bud? bud;
	bool playerWasVisible;
	bool budWasVisible;
	float portraitScale;
	Vector2 portraitPosition;
	float backdropAlpha;
	float fadeAlpha;
	bool active;

	public Cutscene()
	{
		Sprite = Assets.GetSprite("Editor/text");
		Mask = Masks.None;
		CollidesWithSolids = false;
		Depth = -10000;
	}

	public override bool CastsSpriteShadow => false;
	public override bool CastsReflection => false;

	public override void Added()
	{
		base.Added();
		if (Game.IsEditorPreview || Game.ActiveCutscene != null)
			return;

		active = true;
		previousTimeScale = Game.TimeScale;
		previousDitherGlitch = Game.ditherGlitch;
		previousDitherGlitchTarget = Game.ditherGlitchTarget;
		previousDitherGlitchAxis = Game.ditherGlitchAxis;
		previousDitherGlitchPosition = Game.ditherGlitchPosition;
		Game.ActiveCutscene = this;
		Game.Minimap.Open = false;
		routine.Start(PlayCutscene());
	}

	public override void Update()
	{
		base.Update();
		if (!active || !routine.Active)
			return;

		var delta = Game.Manager.Time.Delta;
		if (!routine.Update(delta))
			EndCutscene();
	}

	IEnumerator<float> PlayCutscene()
	{
		Rope? rope = null;
		while (player == null || bud == null || rope == null)
		{
			player = Game.GetFirst<Player>();
			bud = Game.GetFirst<Bud>();
			foreach (var actor in Game.Actors)
				if (actor is Rope found && !found.Broken)
				{
					rope = found;
					break;
				}
			yield return 0;
		}

		playerWasVisible = player.Visible;
		budWasVisible = bud.Visible;
		var ropeAnchor = rope.Position + new Point2(rope.Size.X / 2, 0);
		player.Position = ropeAnchor + new Point2(0, 28) - player.Hitbox.Rect.Center;
		bud.Position = ropeAnchor + new Point2(0, 48) - bud.Hitbox.Rect.Center;
		player.Stop();
		bud.Stop();
		player.ScriptedInput = new(0, 1, false, false, false);
		bud.ScriptedInput = new(0, 1, false, false, false);
		player.ScriptedClimbSpeed = DescentSpeed;
		bud.ScriptedClimbSpeed = DescentSpeed;

		if (!player.TryGrabRope() || !bud.TryGrabRope())
		{
			Console.Error.WriteLine("Cutscene could not attach Player and Bud to the rope.");
			yield break;
		}

		yield return 0.5f;

		var speech = bud.Say("How much farther down do you think this goes?", Emotion.Neutral);
		while (speech is { Finished: false })
			yield return 0;

		speech = player.Say("You asked me that ten minutes ago.", Emotion.Neutral);
		while (speech is { Finished: false })
			yield return 0;

		speech = bud.Say("I was hoping the answer had changed.", Emotion.Disappointed);
		while (speech is { Finished: false })
			yield return 0;

		yield return 0.35f;
		rope.Break(ropeAnchor + new Point2(0, 12));
		yield return 0.2f;

		var elapsed = 0f;
		while (elapsed < 0.45f)
		{
			elapsed += Game.Manager.Time.Delta;
			Game.TimeScale = float.Lerp(previousTimeScale, SlowTimeScale, Ease.Cube.Out(Math.Clamp(elapsed / 0.45f, 0, 1)));
			yield return 0;
		}

		yield return 1.25f;

		player.Visible = false;
		bud.Visible = false;
		portraitPosition = ((Vector2)bud.Position - Game.RenderCamera) * Game.ZoomLevel;
		portraitScale = 1;
		var portraitFrom = portraitPosition;

		elapsed = 0;
		while (elapsed < 1.8f)
		{
			elapsed += Game.Manager.Time.Delta;
			var t = Ease.Cube.InOut(Math.Clamp(elapsed / 1.8f, 0, 1));
			portraitScale = float.Lerp(1, PortraitScale, t);
			portraitPosition = Vector2.Lerp(portraitFrom, new Vector2(Game.Width, Game.Height) / 2, t);
			backdropAlpha = t * 0.85f;
			SetGlitch(t * 0.65f, new Vector2(1, 0.2f));
			yield return 0;
		}

		elapsed = 0;
		while (elapsed < 1.4f)
		{
			elapsed += Game.Manager.Time.Delta;
			var t = Ease.Cube.In(Math.Clamp(elapsed / 1.4f, 0, 1));
			portraitScale = float.Lerp(PortraitScale, PupilScale, t);
			portraitPosition = new Vector2(Game.Width, Game.Height) / 2 - ScaledPupilOffset();
			backdropAlpha = 1;
			SetGlitch(float.Lerp(0.65f, 1, t), new Vector2(MathF.Cos(elapsed * 17), MathF.Sin(elapsed * 13)));
			yield return 0;
		}

		elapsed = 0;
		while (elapsed < 0.8f)
		{
			elapsed += Game.Manager.Time.Delta;
			var t = Math.Clamp(elapsed / 0.8f, 0, 1);
			fadeAlpha = Ease.Cube.InOut(t);
			SetGlitch(1 - t, Vector2.UnitY);
			yield return 0;
		}

		Game.TimeScale = 0;
		Game.ditherGlitch = 0;
		Game.ditherGlitchTarget = 0;
		Game.ditherGlitchPosition = null;
		while (true)
			yield return 0;
	}

	Vector2 ScaledPupilOffset()
	{
		var facing = bud?.Facing ?? Signs.Positive;
		return PortraitPupilOffset * new Vector2((int)facing, 1) * portraitScale;
	}

	void SetGlitch(float amount, Vector2 axis)
	{
		Game.ditherGlitch = amount;
		Game.ditherGlitchTarget = 0;
		if (axis.LengthSquared() > 0.001f)
			Game.ditherGlitchAxis = Vector2.Normalize(axis);
		Game.ditherGlitchPosition = Game.RenderCamera +
			(portraitPosition + ScaledPupilOffset()) / Game.ZoomLevel;
	}

	internal void RenderOverlay(Batcher batcher)
	{
		if (backdropAlpha > 0)
			batcher.Rect(new Rect(0, 0, Game.Width, Game.Height), Color.Black * backdropAlpha);
		if (portraitScale <= 0 || bud?.Sprite is not { } sprite)
			return;

		var frame = sprite.GetFrameAt(bud.Animation, bud.AnimationTime, true);
		batcher.PushSampler(new(TextureFilter.Nearest, TextureWrap.Clamp, TextureWrap.Clamp));
		batcher.Image(frame.Subtexture, portraitPosition, bud.Origin,
			new Vector2((int)bud.Facing, 1) * portraitScale, 0, Bud.Tint);
		batcher.PopSampler();
	}

	internal void RenderFade(Batcher batcher)
	{
		if (fadeAlpha > 0)
			batcher.Rect(new Rect(0, 0, Game.Width, Game.Height), Color.Black * fadeAlpha);
	}

	public override void Render(Batcher batcher)
	{
		if (Game.IsEditorPreview)
			base.Render(batcher);
	}

	public override void RenderNormals(Batcher batcher) { }
	public override void RenderEmissive(Batcher batcher) { }
	public override void RenderEmissiveOccluder(Batcher batcher) { }
	public override void RenderShadow(Batcher batcher, Color color) { }
	public override void RenderReflection(Batcher batcher, Color color) { }

	public override void Destroyed() => EndCutscene();

	void EndCutscene()
	{
		if (!active)
			return;

		active = false;
		routine.Stop();
		if (player != null)
		{
			player.ScriptedInput = null;
			player.ScriptedClimbSpeed = 1;
			player.Visible = playerWasVisible;
		}
		if (bud != null)
		{
			bud.ScriptedInput = null;
			bud.ScriptedClimbSpeed = 1;
			bud.Visible = budWasVisible;
		}
		Game.TimeScale = previousTimeScale;
		Game.ditherGlitch = previousDitherGlitch;
		Game.ditherGlitchTarget = previousDitherGlitchTarget;
		Game.ditherGlitchAxis = previousDitherGlitchAxis;
		Game.ditherGlitchPosition = previousDitherGlitchPosition;
		if (Game.ActiveCutscene == this)
			Game.ActiveCutscene = null;
	}
}
