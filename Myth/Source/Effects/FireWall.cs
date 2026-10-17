using System.Numerics;
using Foster.Framework;

namespace Myth;

// A wall of fire sweeping across a room: a band from Bottom up to Top (world y) whose leading edge runs from StartX to
// EndX. It warns first, for Telegraph seconds -- a glowing seam along its path, and a line of sparks at Top marking the
// safe height -- then sweeps at Speed, Thickness thick, with a guttering trail. It burns whoever it overlaps, unless
// they're burrowed: get under it (burrow) or over it (up a wall).
public sealed class FireWall : Actor
{
	public Actor? Source; // credited with the hit
	public float StartX, EndX, Top, Bottom;
	public float Speed = 240;
	public float Thickness = 18;
	public float Telegraph = 1.1f;
	public Color Color = new(0xff6a1a);
	public Color CoreColor = new(0xffe08a);

	static float TrailLength => 56f; // guttering flames left behind the front
	static float TrailTime => 0.6f;
	static float FlickerHeight => 0.22f; // of the band's height
	static float ColumnWidth => 2f;
	static float FadeTime => 0.35f; // once the front is past EndX
	static float LightSpacing => 40f;
	static float LightRadius => 70f;
	static float LightIntensity => 1.2f;
	static float TelegraphLightIntensity => 0.6f;
	static float ScorchSpacing => 10f;
	static float ScorchRadius => 9f;
	static float ScorchStrength => 0.3f;
	static float EmberRate => 70f; // per second, from the front
	static float TelegraphEmberRate => 26f;
	static float ShakeIntensity => 0.6f;

	float time;
	float front; // the leading edge's x
	float fade = 1;
	float emberTimer;
	float lastScorchX;
	bool hit;
	int emberEffect = -1;
	int smokeEffect = -1;

	int Direction => EndX >= StartX ? 1 : -1;
	bool Sweeping => time >= Telegraph;
	float Height => Bottom - Top;

	public FireWall()
	{
		Sprite = null;
		Mask = Masks.None;
		CollidesWithSolids = false;
		Depth = -100;
	}

	public override void Added()
	{
		base.Added();
		front = lastScorchX = StartX;
		emberEffect = AcquireParticleEffect("embers");
		smokeEffect = AcquireParticleEffect("smoke");
	}

	public override void Destroyed()
	{
		base.Destroyed();
		ReleaseParticleEffect(ref emberEffect);
		ReleaseParticleEffect(ref smokeEffect);
	}

	public override void Update()
	{
		time += Time.Delta;
		if (!Sweeping)
		{
			EmitEmbers(TelegraphEmberRate, telegraph: true);
			return;
		}

		front += Direction * Speed * Time.Delta;
		var past = (front - EndX) * Direction;
		if (past > 0)
		{
			fade = MathF.Max(0, fade - Time.Delta / FadeTime);
			if (fade <= 0)
			{
				Game.Destroy(this);
				return;
			}
		}
		else
		{
			Game.Shake(0.05f, ShakeIntensity);
			EmitEmbers(EmberRate, telegraph: false);
			Scorch();
		}
		TryBurn();
	}

	// The band the front covers this frame: Thickness behind the leading edge, clipped to the sweep.
	RectInt Band()
	{
		var from = MathF.Min(front, front - Direction * Thickness);
		var to = MathF.Max(front, front - Direction * Thickness);
		var lo = MathF.Max(from, MathF.Min(StartX, EndX));
		var hi = MathF.Min(to, MathF.Max(StartX, EndX));
		return new RectInt((int)lo, (int)Top, (int)MathF.Max(1, hi - lo), (int)Height);
	}

	void TryBurn()
	{
		if (hit || fade < 1 || Source == null || Game.destroying.Contains(Source) || Game.GetFirst<Player>() is not { } player ||
			player.State == Player.States.Burrow)
			return;
		var band = Band();
		if (player.OverlapsCombatBounds(band))
			hit = Source.Hit(player, new(band));
	}

	void EmitEmbers(float rate, bool telegraph)
	{
		if (emberEffect < 0 || Game.Particles is not { } particles)
			return;
		emberTimer -= Time.Delta;
		for (var emitted = 0; emberTimer <= 0 && emitted < 6; emitted++, emberTimer += 1 / rate)
		{
			if (telegraph)
			{
				// Along the safe line and the seam, across the whole path, thickening as the sweep nears.
				var x = Calc.Lerp(StartX, EndX, Game.rng.Float());
				var y = Game.rng.Chance(0.5f) ? Top : Bottom;
				particles.Emit(emberEffect, new Vector2(x, y), 1, -MathF.PI / 2, 0.5f + time / Telegraph, 0.6f);
			}
			else
			{
				var at = new Vector2(front, Calc.Lerp(Top, Bottom, Game.rng.Float()));
				particles.Emit(emberEffect, at, 1, Calc.Angle(new Vector2(Direction, -1.5f)), 1.6f, 0.9f);
				if (smokeEffect >= 0 && Game.rng.Chance(0.25f))
					particles.Emit(smokeEffect, at with { Y = Top }, 1, -MathF.PI / 2, 1, 0.5f);
			}
		}
	}

	// The floor it passes over is left blackened (session only: the game plays a runtime clone of the room).
	void Scorch()
	{
		if (Room is not { } room || MathF.Abs(front - lastScorchX) < ScorchSpacing)
			return;
		lastScorchX = front;
		var at = new Vector2(front, Bottom);
		if (room.WorldBounds.Contains(at))
			room.ForegroundColorField.Paint(at - (Vector2)room.WorldBounds.TopLeft, ScorchRadius, 0.6f, ScorchStrength,
				Color.Black, erase: false);
	}

	public override void Render(Batcher batcher) => RenderFire(batcher, emissive: false);
	public override void RenderEmissive(Batcher batcher) => RenderFire(batcher, emissive: true);

	void RenderFire(Batcher batcher, bool emissive)
	{
		var local = -(Vector2)Position;
		if (!Sweeping)
		{
			// The seam and the safe line, pulsing faster as the sweep nears.
			var warn = time / Telegraph;
			var pulse = 0.55f + 0.45f * MathF.Sin(time * Calc.Lerp(8, 30, warn));
			var alpha = Ease.Cube.In(warn) * pulse * (emissive ? 1 : 0.7f);
			var (left, right) = (MathF.Min(StartX, EndX), MathF.Max(StartX, EndX));
			batcher.Rect(new Rect(left, Bottom - 1, right - left, 1) + local, Color * alpha);
			for (var x = left; x < right; x += 6)
				batcher.Rect(new Rect(x, Top, 3, 1) + local, CoreColor * (alpha * 0.8f));
			return;
		}

		var t = Time.SecondsF;
		var span = Thickness + TrailLength;
		for (var offset = 0f; offset < span; offset += ColumnWidth)
		{
			var x = front - Direction * offset;
			if ((x - StartX) * Direction < 0 || (x - EndX) * Direction > 0)
				continue;
			// Full height in the wall itself, guttering lower through the trail behind it.
			var trail = offset <= Thickness ? 1 : 1 - (offset - Thickness) / TrailLength;
			var age = offset <= Thickness ? 0 : (offset - Thickness) / Speed / TrailTime;
			var life = MathF.Max(0, trail * (1 - age)) * fade;
			if (life <= 0)
				continue;
			var flicker = MathF.Sin(x * 0.7f + t * 17) * MathF.Sin(x * 0.31f - t * 9);
			var height = Height * life * (1 - FlickerHeight + FlickerHeight * flicker);
			var top = Bottom - height;
			if (emissive)
			{
				batcher.Rect(new Rect(x, top, ColumnWidth, height) + local, Color * (0.5f * life));
				batcher.Rect(new Rect(x, Bottom - height * 0.45f, ColumnWidth, height * 0.45f) + local, CoreColor * life);
			}
			else
			{
				batcher.Rect(new Rect(x, top, ColumnWidth, height) + local, Color.Lerp(new Color(0x7a1d0a), Color, life));
				batcher.Rect(new Rect(x, Bottom - height * 0.4f, ColumnWidth, height * 0.4f) + local, CoreColor);
			}
		}
	}

	public override void ForEachLight(Action<Vector2, float, float, float, Color> action)
	{
		if (!Sweeping)
		{
			var warn = Ease.Cube.In(time / Telegraph);
			action(new Vector2(StartX, Bottom - 4), LightRadius, TelegraphLightIntensity * warn, 6, Color);
			return;
		}
		for (var y = Bottom - 6; y > Top; y -= LightSpacing)
			action(new Vector2(front - Direction * Thickness * 0.5f, y), LightRadius, LightIntensity * fade, 8, Color);
	}

	public override Rect GetVisualBounds()
	{
		var (left, right) = (MathF.Min(StartX, EndX), MathF.Max(StartX, EndX));
		return new Rect(left, Top - 4, right - left, Height + 8);
	}
}
