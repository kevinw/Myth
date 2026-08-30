using System.Numerics;
using Foster.Framework;

namespace Myth;

public sealed partial class GameView
{
	internal readonly List<InputPromptGlyph> InputPromptGlyphs = [];

	void DrawInputPromptGlyphs(IDrawableTarget target, in RectInt present, Vector2 outputSize, float scale, RectInt? scissor, bool mapOverlayVisible)
	{
		var prompts = Manager.InputPrompts;
		if (prompts == null || InputPromptGlyphs.Count == 0)
			return;

		var offset = (Vector2)present.Center - outputSize * scale * 0.5f;
		Batcher.PushMatrix(Matrix3x2.CreateScale(scale) * Matrix3x2.CreateTranslation(offset));
		Batcher.PushSampler(new(TextureFilter.Linear, TextureWrap.Clamp, TextureWrap.Clamp));
		foreach (var glyph in InputPromptGlyphs)
			if (glyph.MapOverlay == mapOverlayVisible)
				prompts.DrawGlyphCentered(Batcher, glyph.Action, glyph.Center, glyph.Height, glyph.Color);
		Batcher.PopSampler();
		Batcher.PopMatrix();
		Batcher.Render(target, scissor: scissor);
		Batcher.Clear();
	}

	void DrawPlayerHud(Target target)
	{
		var rectColor = new Color(30, 30, 30, 30);

		var atlasWander = PushAtlasWanderMaterial();
		if (game.GetFirst<Player>() is { } player)
		{
#if BROWSER
			Point2 pos = new(0, 0);
			var hudAnchor = Vector2.Zero;
#else
			Point2 pos = new(0, Height - 14);
			var hudAnchor = new Vector2(0, Height);
#endif
			var hudScale = 1 + (BurrowUpgradeHudScale - 1) * Ease.Cube.InOut(game.BurrowUpgradeHud.Emphasis);
			Batcher.PushMatrix(Matrix3x2.CreateScale(hudScale, hudAnchor));
			Batcher.Rect(new Rect(pos.X, pos.Y + 7, 40, 3), rectColor);

			// Burrow stamina: a green bar under the hearts. Its width scales with capacity so
			// collecting BurrowUpgrade pickups visibly lengthens it; the fill drains while burrowing.
			const float pixelsPerBurrowSecond = 35f; // base 1.133s ≈ the 40px hearts strip
			var capacity = player.BurrowCapacitySeconds;
			var previousCapacity = displayedBurrowCapacity;
			if (displayedBurrowCapacity < 0)
				displayedBurrowCapacity = capacity;
			else if (!game.BurrowUpgradeHud.Active || game.BurrowUpgradeHud.Growing)
				displayedBurrowCapacity = Calc.Approach(displayedBurrowCapacity, capacity,
					BurrowBarGrowRate * game.Manager.Time.Delta);
			if (previousCapacity < capacity && displayedBurrowCapacity >= capacity)
				burrowLabelPulseStart = game.Manager.Time.SecondsF;
			var burrowColor = new Color(99, 155, 255, 200);
			if (player.State == Player.States.Burrow)
			{
				var towardsWhite = Color.Lerp(burrowColor, Color.White, 0.3f);
				// public float SineWave(float from, float to, double duration, float offsetPercent = 0);
				burrowColor = Color.Lerp(burrowColor, towardsWhite, Time.SineWave(0, 1, 0.13f));
			}
			var burrowY = pos.Y + 7;
			var burrowBarHeight = 3;
			var burrowWidth = displayedBurrowCapacity * pixelsPerBurrowSecond;
			displayedBurrowBarOpacity = Calc.Approach(displayedBurrowBarOpacity, player.BurrowBarOpacity,
				BurrowBarOpacityRate * game.Manager.Time.Delta);
			Batcher.Rect(new Rect(0, burrowY, burrowWidth, burrowBarHeight), rectColor * displayedBurrowBarOpacity);
			Batcher.Rect(new Rect(0, burrowY, burrowWidth * player.BurrowFill, burrowBarHeight), burrowColor * displayedBurrowBarOpacity);
			if (game.BurrowUpgradeHud.Particles > 0)
				DrawBurrowGrowthParticles(new Vector2(burrowWidth, burrowY + 1), game.Manager.Time.SecondsF,
					game.BurrowUpgradeHud.Particles);
			Batcher.PopMatrix();
			DrawBurrowUpgradeLabel(capacity * pixelsPerBurrowSecond * BurrowUpgradeHudScale);

			// draw little icons for each unit of health
			var healthColor = Color.White * 0.75f;
			for (var i = 0; i < Player.MaxHealth; i++)
			{
				var subTexture = player.Health >= i + 1 ? Assets.GetSubtexture("heart/0") : Assets.GetSubtexture("heart/1");
				Batcher.Image(subTexture, pos, healthColor);
				pos.X += 10;
			}
		}
		if (atlasWander)
			Batcher.PopMaterial();

		Batcher.Render(target);
		Batcher.Clear();
	}

	// Top-left readout of the playthrough-durable Mycelon count: the pickup's icon (falls back to
	// the airbubble sprite until mycelon.ase exists) followed by the number. It eases down from
	// above the top edge whenever the count changes (a pickup or spend), holds briefly, then eases
	// back up to hide — a transient notification rather than a permanent gauge.
	// Burrow bar width eases toward the player's capacity (in seconds) so collecting an upgrade
	// animates the bar growing; -1 snaps on first show / room load. Eased on real time so it still
	// grows while the BurrowUpgradeGet modal freezes the sim (TimeScale 0).
	float displayedBurrowCapacity = -1f;
	float displayedBurrowBarOpacity = 1;
	static float BurrowBarGrowRate => 0.6f;   // seconds of capacity closed per real second
	static float BurrowBarOpacityRate => 8f;
	static float BurrowUpgradeHudScale => 4f;
	static int BurrowGrowthParticleCount => 24;
	const string BurrowUpgradeLabel = "+ Bosonic Time";
	static float BurrowLabelPulseDuration => 0.5f;
	static float BurrowLabelPulseFrequency => 8f;
	float burrowLabelPulseStart = float.NegativeInfinity;
	static readonly BlendMode HudAdditiveBlend = new(BlendOp.Add, BlendFactor.SrcAlpha, BlendFactor.One);

	void DrawBurrowUpgradeLabel(float finalBarEnd)
	{
		if (!game.BurrowUpgradeHud.Active || (Assets.SpeechFont ?? Assets.Font) is not { } font)
			return;

#if BROWSER
		var finalPosition = new Vector2(finalBarEnd, 14 * BurrowUpgradeHudScale + 2);
		var finalJustify = new Vector2(0.5f, 0);
#else
		var finalPosition = new Vector2(finalBarEnd, Height - 14 * BurrowUpgradeHudScale - 2);
		var finalJustify = new Vector2(0.5f, 1);
#endif
		var travel = Ease.Cube.Out(game.BurrowUpgradeHud.LabelTravel);
		var position = Vector2.Lerp(((Vector2)game.BurrowUpgradeHud.LabelWorldOrigin - RenderCamera) * zoomLevel,
			finalPosition, travel);
		var justify = Vector2.Lerp(new Vector2(0.5f), finalJustify, travel);
		var alpha = game.BurrowUpgradeHud.Emphasis;
		var pulseTime = game.Manager.Time.SecondsF - burrowLabelPulseStart;
		var pulse = pulseTime < BurrowLabelPulseDuration
			? (1 - MathF.Cos(pulseTime * MathF.Tau * BurrowLabelPulseFrequency)) * 0.5f
			: 0;
		var color = Color.Lerp(Color.White, new Color(90, 205, 120, 255), pulse) * alpha;
		var shadowColor = new Color(0x000000, 170) * alpha;

		float sineStart = Time.SecondsF * 7.5f;
		float sineStep = 0.14f;
		Vector2 sineOffset = new (1f, 4f);
		font.DrawSineWave(Batcher, BurrowUpgradeLabel, position + Vector2.One, justify, font.Size, shadowColor, sineStart, sineStep, sineOffset);
		font.DrawSineWave(Batcher, BurrowUpgradeLabel, position, justify, font.Size, color, sineStart, sineStep, sineOffset);
	}

	void DrawBurrowGrowthParticles(Vector2 barEnd, float time, float amount)
	{
		Batcher.PushBlend(HudAdditiveBlend);
		for (var i = 0; i < BurrowGrowthParticleCount; i++)
		{
			var seed = Hash32.Mix((uint)(i + 1));
			var speed = 0.8f + HudParticleUnit(seed) * 0.8f;
			var age = (time * speed + HudParticleUnit(Hash32.Mix(seed))) % 1f;
			var direction = new Vector2(
				HudParticleUnit(Hash32.Mix(seed ^ 0x9e3779b9u)) * 2 - 0.4f,
				HudParticleUnit(Hash32.Mix(seed ^ 0x85ebca6bu)) * 2 - 1);
			var position = barEnd + direction * (2 + HudParticleUnit(seed ^ 0xc2b2ae35u) * 6) * age;
			var size = 1.1f + HudParticleUnit(seed ^ 0x27d4eb2du) * 1.5f;
			var alpha = MathF.Sqrt(1 - age) * amount;
			Batcher.Circle(position, size / 2, 8, new Color(90, 205, 120, (byte)(255 * alpha)));
		}
		Batcher.PopBlend();
	}

	static float HudParticleUnit(uint value) => (value & 0xffff) / 65535f;

	float currencyReveal;                     // 0 = fully hidden above the top edge, 1 = fully revealed
	float currencyHold;                       // seconds left to stay revealed after the last count change
	int currencyLastCount;                    // last count seen, to detect pickups/spends
	static float CurrencyRevealSpeed => 3.5f; // reveal/hide eases over ~0.3s
	static float CurrencyHoldTime => 2f;      // stay revealed this long after a change, then ease back up

	void DrawCurrencyHud(IDrawableTarget target, in RectInt viewport, RectInt? scissor)
	{
		var count = game.PickupCount(PickupKind.Mycelon);
		if (count != currencyLastCount)
		{
			currencyHold = CurrencyHoldTime; // a pickup (or spend) re-reveals and restarts the timer
			currencyLastCount = count;
		}
		currencyHold = Calc.Approach(currencyHold, 0, Time.Delta);

		// The map overlay doubles as a status screen, so the count stays revealed while it's up.
		currencyReveal = Calc.Approach(currencyReveal, currencyHold > 0 || game.MinimapOpen ? 1f : 0f, CurrencyRevealSpeed * Time.Delta);
		if (currencyReveal <= 0.001f)
			return;

		// Smoothstep the linear progress into an ease, then drive a vertical slide (starting above
		// the top edge, where the viewport scissor clips it) plus a matching fade.
		var ease = currencyReveal * currencyReveal * (3f - 2f * currencyReveal);
		var y = Calc.Lerp(-16f, 4f, ease);
		var tint = Color.White * ease;

		var scale = viewport.Height / (float)Height;
		Batcher.PushMatrix(
			Matrix3x2.CreateScale(scale) *
			Matrix3x2.CreateTranslation(viewport.Left, viewport.Top));

		var x = 4f;
		var icon = Assets.GetSprite("mycelon") ?? Assets.GetSprite("airbubble");
		if (icon is { Frames.Count: > 0 })
		{
			var sub = icon.Frames[0].Subtexture;
			Batcher.Image(sub, new Vector2(x, y), Vector2.Zero, Vector2.One, 0, tint);
			x += sub.Width + 3;
		}

		var font = Assets.Font;
		if (font != null)
			Batcher.Text(font, count.ToString(), new Vector2(x, y), font.Size, tint);

		Batcher.PopMatrix();
		Batcher.Render(target, scissor: scissor?.GetIntersection(viewport) ?? viewport);
		Batcher.Clear();
	}
}
