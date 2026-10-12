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
#if !MYTHSHIP
		var blackAndWhite = Manager.BlackAndWhite ? blackAndWhiteMaterial : null;
		if (blackAndWhite != null)
			Batcher.PushMaterial(blackAndWhite.Material);
#endif
		foreach (var glyph in InputPromptGlyphs)
			if (glyph.MapOverlay == mapOverlayVisible)
				prompts.DrawGlyphCentered(Batcher, glyph.Action, glyph.Center, glyph.Height, glyph.Color);
#if !MYTHSHIP
		if (blackAndWhite != null)
			Batcher.PopMaterial();
#endif
		Batcher.PopSampler();
		Batcher.PopMatrix();
		// The glyphs draw at window resolution to stay readable, so clip them to the on-screen
		// frame themselves; otherwise one scrolled past the camera's edge draws into the letterbox.
		var frame = ((RectInt)Rect.CenteredAt(present.Center, outputSize * scale)).GetIntersection(present);
		Batcher.Render(target, scissor: scissor?.GetIntersection(frame) ?? frame);
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
			var burrowY = pos.Y + 11;
			var burrowBarHeight = 2;
			var burrowWidth = displayedBurrowCapacity * pixelsPerBurrowSecond;

			// Fade out while the player is behind the HUD so it never hides them.
			var hudRect = new Rect(0, pos.Y, MathF.Max(40, burrowWidth), 14);
			var playerWorld = player.Hitbox.Rect + player.Position;
			var occluded = !game.BurrowUpgradeHud.Active &&
				Rect.Between(WorldToFrame(playerWorld.TopLeft), WorldToFrame(playerWorld.BottomRight)).Overlaps(hudRect.Inflate(HudOcclusionMargin));
			hudOpacity = Calc.Approach(hudOpacity, occluded ? HudOccludedOpacity : 1, HudFadeRate * game.Manager.Time.Delta);
			rectColor *= hudOpacity;
			//Batcher.Rect(new Rect(pos.X, pos.Y + 7, 40, 3), rectColor);
			displayedBurrowBarOpacity = Calc.Approach(displayedBurrowBarOpacity, player.BurrowBarOpacity,
				BurrowBarOpacityRate * game.Manager.Time.Delta);
			Batcher.Rect(new Rect(0, burrowY, burrowWidth, burrowBarHeight), rectColor * displayedBurrowBarOpacity);
			Batcher.Rect(new Rect(0, burrowY, burrowWidth * player.BurrowFill, burrowBarHeight), burrowColor * (displayedBurrowBarOpacity * hudOpacity));
			if (game.BurrowUpgradeHud.Particles > 0)
				DrawBurrowGrowthParticles(new Vector2(burrowWidth, burrowY + 1), game.Manager.Time.SecondsF,
					game.BurrowUpgradeHud.Particles);
			Batcher.PopMatrix();
			DrawBurrowUpgradeLabel(capacity * pixelsPerBurrowSecond * BurrowUpgradeHudScale);

			DrawHealth(player, pos);
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
	// Health reads as a number that counts down to the real value, so a hit visibly ticks away. Lava's
	// drain heats it (color and a shiver) for a moment after each point, and the last hit's worth pulses.
	void DrawHealth(Player player, Vector2 pos)
	{
		var health = Math.Max(player.Health, 0);
		displayedHealth = displayedHealth < 0 || health > displayedHealth
			? health
			: Calc.Approach(displayedHealth, health, HealthCountRate * game.Manager.Time.Delta);
		var heat = Calc.ClampedMap(Time.SecondsF - player.BurnedAt, 0, HealthBurnGlowSeconds, 1, 0);
		var low = health <= Player.HitDamage ? Time.SineWave(0, 1, HealthLowPulseSeconds) : 0;
		var color = Color.Lerp(Color.Lerp(Color.White, HealthLowColor, low), HealthBurnColor, heat) * (0.75f * hudOpacity);
		pos = (pos + new Vector2(MathF.Sin(Time.SecondsF * 90), MathF.Cos(Time.SecondsF * 70)) * (heat * HealthBurnShiver)).Round();

		//var heart = Assets.GetSubtexture(health > 0 ? "heart/0" : "heart/1");
		//Batcher.Image(heart, pos, color);
		if (Assets.GetSprite("numbers") is not { Frames.Count: 10 } digits)
			return;
		Span<char> text = stackalloc char[3];
		((int)MathF.Ceiling(displayedHealth)).TryFormat(text, out var length);
		var at = (pos + Vector2.UnitX * 2).Round();
		foreach (var digit in text[..length])
		{
			var frame = digits.Frames[digit - '0'].Subtexture;
			Batcher.Image(frame, at, color);
			at.X += frame.Width;
		}
	}

	float displayedHealth = -1;
	static float HealthCountRate => 60f; // points per second the readout falls toward a hit
	static float HealthBurnGlowSeconds => 0.35f;
	static float HealthBurnShiver => 0.6f;
	static float HealthLowPulseSeconds => 0.8f;
	static readonly Color HealthBurnColor = new(1f, 0.55f, 0.2f, 1f);
	static readonly Color HealthLowColor = new(1f, 0.35f, 0.35f, 1f);

	float displayedBurrowCapacity = -1f;
	float displayedBurrowBarOpacity = 1;
	float hudOpacity = 1;
	static float HudOccludedOpacity => 0f;
	static float HudFadeRate => 4f;
	static float HudOcclusionMargin => 2f;
	static float BurrowBarGrowRate => 0.6f;   // seconds of capacity closed per real second
	static float BurrowBarOpacityRate => 8f;
	static float BurrowUpgradeHudScale => 4f;
	static int BurrowGrowthParticleCount => 24;
	const string BurrowUpgradeLabel = "+ Line Time";
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
		var color = Color.Lerp(Color.White, new Color(150, 205, 120, 255), pulse) * alpha;
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
	int duckLastCount;
	CountIncreaseHudEffect duckCountIncrease;
	Vector2 duckHudStartCenter;
	static float CurrencyRevealSpeed => 3.5f; // reveal/hide eases over ~0.3s
	static float CurrencyHoldTime => 2f;      // stay revealed this long after a change, then ease back up
	static float DuckHudScale => 0.5f;
	static float DuckHudExtraHoldTime => 3f;

	void DrawCurrencyHud(IDrawableTarget target, in RectInt viewport, RectInt? scissor)
	{
		var count = game.PickupCount(PickupKind.Mycelon);
		var duckCount = game.DucksDestroyed;
		if (count != currencyLastCount)
		{
			currencyHold = CurrencyHoldTime;
			currencyLastCount = count;
		}
		if (duckCount != duckLastCount)
		{
			if (duckCount > duckLastCount)
			{
				duckCountIncrease.Begin(duckLastCount, duckCount);
				duckHudStartCenter = (game.DuckHudWorldOrigin - RenderCamera) * zoomLevel - Vector2.UnitY * 6;
				currencyHold = MathF.Max(currencyHold, duckCountIncrease.HudHoldTime + DuckHudExtraHoldTime);
			}
			else
				currencyHold = CurrencyHoldTime;
			duckLastCount = duckCount;
		}
		duckCountIncrease.Update(Manager.Time.Delta);
		currencyHold = Calc.Approach(currencyHold, 0, Manager.Time.Delta);

		// The map overlay doubles as a status screen, so the count stays revealed while it's up.
		currencyReveal = Calc.Approach(currencyReveal, currencyHold > 0 || game.MinimapOpen ? 1f : 0f,
			CurrencyRevealSpeed * Manager.Time.Delta);
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
#if !MYTHSHIP
		var blackAndWhite = Manager.BlackAndWhite ? blackAndWhiteMaterial : null;
		if (blackAndWhite != null)
			Batcher.PushMaterial(blackAndWhite.Material);
#endif

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
		{
			Span<char> text = stackalloc char[11];
			count.TryFormat(text, out var length);
			var countText = text[..length];
			Batcher.Text(font, countText, new Vector2(x, y), font.Size, tint);
			x += font.WidthOf(countText) + 8;
		}

		var duckDestination = new Vector2(x, y);
		var duckIconWidth = 0f;
		var duckHeight = 0f;
		Subtexture? duckSubtexture = null;
		if (Assets.GetSprite("Props/duck") is { Frames.Count: > 0 } duck)
		{
			var subtexture = duck.Frames[0].Subtexture;
			duckSubtexture = subtexture;
			duckIconWidth = subtexture.Width * DuckHudScale;
			duckHeight = subtexture.Height * DuckHudScale;
		}
		var duckCountX = duckIconWidth > 0 ? duckIconWidth + 3 : 0;
		var duckWidth = duckCountX;
		if (font != null)
		{
			Span<char> text = stackalloc char[11];
			duckCountIncrease.DisplayedValue(duckCount).TryFormat(text, out var length);
			var shown = text[..length];
			duckWidth += font.WidthOf(shown);
			duckHeight = MathF.Max(duckHeight, font.HeightOf(shown));
		}
		var duckStart = duckHudStartCenter - new Vector2(duckWidth, duckHeight) * 0.5f;
		var duckPosition = Vector2.Lerp(duckStart,
			duckDestination, Ease.Cube.InOut(duckCountIncrease.SettleProgress));
		if (duckSubtexture is { } duckSub)
			Batcher.Image(duckSub, duckPosition, Vector2.Zero, Vector2.One * DuckHudScale, 0, tint);
		if (font != null)
			duckCountIncrease.Draw(Batcher, font, duckCount, duckPosition + Vector2.UnitX * duckCountX, tint);

#if !MYTHSHIP
		if (blackAndWhite != null)
			Batcher.PopMaterial();
#endif
		Batcher.PopMatrix();
		Batcher.Render(target, scissor: scissor?.GetIntersection(viewport) ?? viewport);
		Batcher.Clear();
	}
}

struct CountIncreaseHudEffect
{
	int from;
	int to;
	float elapsed;
	public bool Active { get; private set; }

	static float LeadInTime => 0.25f;
	static float GrowTime => 0.3f;
	static float PauseTime => 1f;
	static float SettleTime => 0.55f;
	static float NormalHoldTime => 0.3f;
	static float ChangeTime => LeadInTime + GrowTime + PauseTime;
	static float Duration => ChangeTime + SettleTime;
	public float HudHoldTime => Duration + NormalHoldTime;

	public void Begin(int from, int to)
	{
		this.from = from;
		this.to = to;
		elapsed = 0;
		Active = true;
	}

	public void Update(float delta)
	{
		if (!Active)
			return;
		elapsed += delta;
		if (elapsed >= Duration)
			Active = false;
	}

	public readonly void Draw(Batcher batcher, SpriteFont font, int value, Vector2 position, Color tint)
	{
		Span<char> text = stackalloc char[11];
		DisplayedValue(value).TryFormat(text, out var length);
		var shown = text[..length];
		if (!Active || elapsed < LeadInTime)
		{
			batcher.Text(font, shown, position, font.Size, tint);
			return;
		}

		var growing = elapsed < LeadInTime + GrowTime;
		var progress = growing
			? Ease.Cube.Out((elapsed - LeadInTime) / GrowTime)
			: elapsed < ChangeTime
				? 1f
				: 1 - Ease.Cube.Out((elapsed - ChangeTime) / SettleTime);
		var size = font.Size * Calc.Lerp(1, 2, progress);
		var color = Color.Lerp(tint, Color.HotPink * (tint.A / 255f), progress);
		var center = position + new Vector2(font.WidthOf(shown), font.HeightOf(shown)) * 0.5f;
		var sineSpeed = elapsed < ChangeTime ? 8f : 24f;
		font.DrawSineWave(batcher, shown, center, new Vector2(0.5f), size, color,
			elapsed * sineSpeed, 0.3f, new Vector2(0, 2 * progress));
	}

	public readonly int DisplayedValue(int value)
		=> Active && elapsed < ChangeTime ? from : Active ? to : value;

	public readonly float SettleProgress
		=> !Active ? 1 : elapsed <= ChangeTime ? 0 : Math.Clamp((elapsed - ChangeTime) / SettleTime, 0, 1);
}
