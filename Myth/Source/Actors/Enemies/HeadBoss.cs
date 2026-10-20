using System.Numerics;
using Foster.Framework;

namespace Myth;

// A giant floating head that telegraphs every attack with its face, its body language and its own shape:
// - Volley: squints, cocks one brow and draws back, then sweeps an arc of shots starting from the cocked brow's side.
// - Puff: astonished, it swells and its cheeks inflate; the bigger the puff, the denser the ring it spits.
// - Slam: rises to the ceiling over you, clenched, its chin growing into an iron anvil, a glow marking where it'll
//   land; then drops, quaking the floor (jump it) and, from stage 2, rolling low fire out both ways. It's left wedged
//   and dizzy: the window to hit it hard.
// - Weep (stage 2+): sad, it drifts across the room crying tears that fall in sheets.
// - Split (stage 3): afraid, it cracks in half down the middle, baring a glowing core that spirals shots; the core
//   takes heavy damage.
// Any hit chips it, dents it and makes it wince. Each third of its health lost speeds it up and heats its eyes.
[GameActor(Sprite = "player", Tags = ActorTag.Enemy | ActorTag.Boss | ActorTag.Flying, BoundsX = -40, BoundsY = -40,
	BoundsW = 80, BoundsH = 80, LivePositionEditable = true)]
public sealed class HeadBoss : Actor
{
	enum Phase { Asleep, Waking, Idle, Volley, Puff, Slam, Stunned, Weep, Split, Dying }

	[I(16, 64)] public float Radius = 34;
	internal static ContentRef<HeadShapeContent> BigBruteHead => new("bigbrute");
	public ContentRef<HeadShapeContent> HeadShapeId = BigBruteHead;
	public Color HeadColor = new(0xc9a98c);
	public Color EyeColor = new(0xff5a3c);
	public Color BruiseColor = new(0x5a1a38);
	[I(1, 80)] public int Health = 30;
	[I(1, 60)] public float VisualFPS = 24; // how often the head re-marches; its springs still run every frame

	static float DentScale => 1 / 3f; // of a floating head's dent
	static float HoverHeight => 0.42f; // fraction of the room's height down from the ceiling
	static float HoverFollow => 1.6f; // how hard it steers toward its hover spot
	static float HoverBob => 4f;
	static float WakeTime => 1.1f;
	static float AttackCooldown => 1.1f;
	static int AttacksBeforeSlam => 3; // it always gets to the slam (and the opening after) this often

	static float VolleyWindup => 0.8f;
	static float VolleyArc => 1.6f; // radians
	static float VolleySpeed => 80f;
	static float VolleyStagger => 0.05f;

	static float PuffWindup => 1.1f;
	static float PuffSpeed => 65f;

	static float SlamRiseTime => 1.1f;
	static float SlamHangTime => 0.4f;
	static float SlamClearance => 1.5f; // radii below the ceiling at the top of the rise
	static float SlamGravity => 1400f;
	static float SlamWaveSpeed => 150f;
	static float SlamWaveRange => 600f;
	static float SlamFireHeight => 16f; // low enough to jump
	static float SlamFireSpeed => 170f;
	static Color SlamWaveColor => new(0xff7a2a);

	static float StunTime => 3.2f;
	static float StunDamage => 3;
	static float CoreDamage => 3;

	static float WeepTime => 3.2f;
	static float WeepDrift => 70f;
	static float TearInterval => 0.09f;
	static Color TearColor => new(0x7fc8ff);

	static float SplitOpenTime => 0.9f;
	static float SplitHoldTime => 3.4f;
	static float SpiralInterval => 0.08f;
	static float SpiralTurn => 0.42f; // radians between shots
	static float SpiralSpeed => 60f;

	static float FaceResponse => 9f;
	static float WobbleKick => 0.06f;
	static float SpinKick => 3f;
	static float HitShove => 70f;
	static float HitContactGrace => 0.5f;
	static Color StunTint => new(0xffb070);

	Phase phase;
	int maxHealth;
	int attacksSinceSlam;
	float cooldown;
	float phaseTime;
	float eyes; // 0 dark .. 1 lit
	float mouth;
	float pain; // a hit's wince, fading
	float contactGrace;
	Expression mood;
	Vector4 morph; // anvil, cheeks, split, core glow
	float cracks; // how far its cracks have spread past the head shape's own
	Vector2? slamMark; // where the slam will land, glowing on the floor
	readonly HeadSculpt sculpt;
	readonly Coroutine attack = new();
	int chunkEffect = -1;
	int emberEffect = -1;

	public HeadBoss()
	{
		Mask = Masks.Enemy;
		Depth = -5;
		sculpt = new(this, extent: 2.1f);
	}

	int Stage => Health > maxHealth * 2 / 3 ? 1 : Health > maxHealth / 3 ? 2 : 3;
	float Speedup => Stage switch { 1 => 1f, 2 => 1.15f, _ => 1.3f };
	internal override bool ContactDamageEnabled => contactGrace <= 0 && phase is not (Phase.Asleep or Phase.Stunned or Phase.Dying);
	public override string HitSoundType => "flesh";
	Vector2 Center => Position;
	Vector2 Mouth => Center + new Vector2(0, Radius * 0.45f);
	Player? Target => Game.GetFirst<Player>();
	Vector2 ToPlayer => Target is { } player ? player.Position + player.Hitbox.Rect.Center - Center : Vector2.Zero;
	Rect Arena => Room?.WorldBounds is { } bounds ? (Rect)bounds : new Rect(Center - new Vector2(160, 90), new Vector2(320, 180));

	public override void Added()
	{
		maxHealth = Health;
		var half = (int)(Radius * 0.8f);
		Hitbox = new(new RectInt(-half, -half, half * 2, half * 2));
		chunkEffect = AcquireParticleEffect("head_chunks");
		emberEffect = AcquireParticleEffect("embers");
		SyncVisual(false);
		base.Added();
	}

	public override void Destroyed()
	{
		attack.Dispose();
		ReleaseParticleEffect(ref chunkEffect);
		ReleaseParticleEffect(ref emberEffect);
		sculpt.Dispose();
	}

	public override void Update()
	{
		phaseTime += Time.Delta;
		contactGrace = MathF.Max(0, contactGrace - Time.Delta);
		pain = MathF.Max(0, pain - Time.Delta * 1.5f);
		if (attack.Active)
			attack.Update(Time.Delta);
		else
			Idle();
		cracks = Calc.Approach(cracks, phase == Phase.Split ? 1 : (Stage - 1) * 0.35f, Time.Delta * 0.5f);
		UpdateFace();
		base.Update();
		sculpt.FindWalls();
	}

	void Enter(Phase next)
	{
		phase = next;
		phaseTime = 0;
	}

	void Idle()
	{
		switch (phase)
		{
			case Phase.Asleep:
				Velocity = new(0, MathF.Sin(Timer * 1.2f) * HoverBob);
				mood = new(BrowRaise: -0.5f, Smile: 0.15f, LidClose: 1);
				if (Game.WorldView.Overlaps(GetVisualBounds()))
					attack.Start(Wake());
				return;
			case Phase.Dying:
				return;
		}
		Enter(Phase.Idle);
		Hover();
		mood = new(0.35f, -0.1f, 0, -0.3f);
		mouth = 0.25f + 0.15f * MathF.Sin(Timer * 3);
		cooldown -= Time.Delta * Speedup;
		if (cooldown <= 0 && Target != null)
			Choose();
	}

	// Hangs over the room's middle third, leaning toward the player.
	void Hover(float follow = 1)
	{
		var arena = Arena;
		var x = Math.Clamp(Center.X + ToPlayer.X * 0.5f, arena.Left + Radius * 1.5f, arena.Right - Radius * 1.5f);
		var target = new Vector2(x, arena.Top + arena.Height * HoverHeight + MathF.Sin(Timer * 1.3f) * HoverBob);
		Velocity = Calc.Approach(Velocity, (target - Center) * HoverFollow * follow, 400 * Time.Delta);
	}

	void Choose()
	{
		var stage = Stage;
		if (attacksSinceSlam >= AttacksBeforeSlam)
		{
			attack.Start(Slam());
			return;
		}
		attacksSinceSlam++;
		var roll = Game.rng.Float();
		if (stage == 3 && roll < 0.25f)
			attack.Start(Split());
		else if (stage >= 2 && roll < 0.45f)
			attack.Start(Weep());
		else if (roll < 0.62f)
			attack.Start(Volley());
		else if (roll < 0.85f)
			attack.Start(Puff());
		else
		{
			attacksSinceSlam--;
			attack.Start(Slam());
		}
	}

	void FinishAttack()
	{
		Enter(Phase.Idle);
		cooldown = AttackCooldown;
		slamMark = null;
	}

	IEnumerator<float> Wake()
	{
		Enter(Phase.Waking);
		AudioEvent("boss/start_float");
		Squish = new(1.3f, 0.75f);
		for (var time = 0f; time < WakeTime; time += Time.Delta)
		{
			var t = time / WakeTime;
			eyes = Ease.Cube.Out(t);
			mood = Expression.Surprised;
			mouth = 0.8f * t;
			Velocity = Calc.Approach(Velocity, Vector2.Zero, 200 * Time.Delta);
			yield return 0;
		}
		FinishAttack();
	}

	// Squints and cocks the brow on the side the sweep starts from, drawing back; then sweeps the arc.
	IEnumerator<float> Volley()
	{
		Enter(Phase.Volley);
		var side = Game.rng.Chance(0.5f) ? 1 : -1;
		var windup = VolleyWindup / Speedup;
		for (var time = 0f; time < windup; time += Time.Delta)
		{
			var t = time / windup;
			mood = new(0.5f, -0.7f, side, -0.3f);
			mouth = Calc.Approach(mouth, 0, Time.Delta * 4);
			Velocity = Calc.Approach(Velocity, -ToPlayer.Normalized() * 25, 300 * Time.Delta);
			Squish = new(1 - 0.12f * t, 1 + 0.12f * t);
			sculpt.Wobble = MathF.Max(sculpt.Wobble, 0.015f * t);
			yield return 0;
		}
		AudioEvent("boss/knockback");
		Squish = new(1.2f, 0.85f);
		mouth = 1;
		var aim = ToPlayer.Normalized(Vector2.UnitY);
		var count = 7 + Stage * 2;
		// Start at whichever end of the arc lies on the cocked brow's side.
		var start = Rotate(aim, -VolleyArc / 2).X * side > Rotate(aim, VolleyArc / 2).X * side ? -1 : 1;
		for (var i = 0; i < count; i++)
		{
			var angle = start * VolleyArc * (0.5f - i / (float)(count - 1));
			BulletPattern.FireArc(Game, Mouth.RoundToPoint2(), Rotate(aim, angle), 1, 0, VolleySpeed * Speedup);
			Velocity = -aim * 30;
			yield return VolleyStagger;
		}
		yield return 0.35f;
		FinishAttack();
	}

	// Astonished, it puffs up: the bigger the puff, the more shots in the ring it spits.
	IEnumerator<float> Puff()
	{
		Enter(Phase.Puff);
		var puff = Game.rng.Float(0.5f, 1);
		var windup = PuffWindup / Speedup;
		AudioEvent("rig_puppet/surprised");
		for (var time = 0f; time < windup; time += Time.Delta)
		{
			var t = Ease.Cube.Out(time / windup);
			mood = new(-0.2f, 1, 0, 0.2f);
			mouth = 0;
			morph.Y = puff * t;
			Velocity = Calc.Approach(Velocity, Vector2.Zero, 300 * Time.Delta);
			Squish = Vector2.One * (1 + 0.18f * puff * t);
			sculpt.Wobble = MathF.Max(sculpt.Wobble, 0.02f * t);
			yield return 0;
		}
		AudioEvent("boss/knockback");
		Game.Shake(0.15f, 1.5f);
		mouth = 1;
		Squish = new(0.8f, 1.25f);
		var count = 8 + (int)MathF.Round(puff * 12) + Stage * 2;
		var aim = ToPlayer.Normalized(Vector2.UnitY);
		BulletPattern.FireArc(Game, Mouth.RoundToPoint2(), aim, count, MathF.Tau * (count - 1) / count, PuffSpeed * Speedup,
			bullet => bullet.LaunchDelay = 0.05f);
		Velocity = -aim * 60;
		for (var time = 0f; time < 0.4f; time += Time.Delta)
		{
			morph.Y = Calc.Approach(morph.Y, 0, Time.Delta * 4);
			yield return 0;
		}
		morph.Y = 0;
		FinishAttack();
	}

	// Rises over the player clenched, its chin growing into an anvil, then drops and quakes the floor. Left dizzy.
	IEnumerator<float> Slam()
	{
		Enter(Phase.Slam);
		attacksSinceSlam = 0;
		var arena = Arena;
		var top = arena.Top + Radius * SlamClearance;
		var rise = SlamRiseTime / Speedup;
		AudioEvent("boss/start_float");
		for (var time = 0f; time < rise; time += Time.Delta)
		{
			var t = time / rise;
			mood = Expression.Angry;
			mouth = 0;
			morph.X = Ease.Cube.Out(t);
			var x = Math.Clamp(Center.X + ToPlayer.X, arena.Left + Radius, arena.Right - Radius);
			Velocity = Calc.Approach(Velocity, (new Vector2(x, top) - Center) * 4, 600 * Time.Delta);
			Squish = new(1 - 0.1f * t, 1 + 0.1f * t);
			sculpt.Wobble = MathF.Max(sculpt.Wobble, 0.02f * t);
			slamMark = FloorBelow();
			yield return 0;
		}
		for (var time = 0f; time < SlamHangTime / Speedup; time += Time.Delta)
		{
			Velocity = Vector2.Zero;
			sculpt.Wobble = MathF.Max(sculpt.Wobble, 0.035f);
			yield return 0;
		}
		AudioEvent("rig_puppet/swing");
		Squish = new(0.8f, 1.3f);
		var landed = false;
		for (var time = 0f; time < 1.5f && !landed; time += Time.Delta)
		{
			Velocity = new(0, Velocity.Y + SlamGravity * Time.Delta);
			landed = Grounded();
			yield return 0;
		}
		slamMark = null;
		if (landed)
			Land();
		for (var stunned = Stunned(); stunned.MoveNext();)
			yield return stunned.Current;
	}

	Vector2? FloorBelow()
	{
		var probe = new RectInt(Position.X - 1, Position.Y, 2, 2);
		return Game.TryFindSurface(this, probe, Vector2.UnitY, (int)Arena.Height, out var surface) ? surface.Point : null;
	}

	void Land()
	{
		Game.Shake(0.3f, 4);
		Game.HitStun(0.08f);
		Game.KickRenderTarget(Vector2.UnitY * 80);
		AudioEvent("boss/stomp");
		Squish = new(1.4f, 0.65f);
		sculpt.Impact(1, 400);
		sculpt.Wobble = WobbleKick * 1.5f;
		var floor = (Vector2)Position + new Vector2(0, Hitbox.Rect.Bottom);
		Game.Particles?.Emit(emberEffect, floor, 24, -MathF.PI / 2, 1, 1.4f);
		var wave = Game.Create<RadialWave>(floor.RoundToPoint2() + new Point2(0, 3));
		wave.Source = this;
		wave.Target = Target;
		wave.CanHit = static (wave, target) => target is Player p && (p.State == Player.States.Burrow || p.Grounded());
		wave.Medium = RadialWaveMedium.Solid;
		wave.Speed = SlamWaveSpeed * Speedup;
		wave.MaxRadius = SlamWaveRange;
		wave.Thickness = 6;
		wave.Color = SlamWaveColor;
		wave.CoreColor = Color.Lerp(SlamWaveColor, Color.White, 0.55f);
		if (Stage >= 2)
			foreach (var direction in (ReadOnlySpan<int>)[-1, 1])
				Game.Create<FireWall>(Position, wall =>
				{
					wall.Source = this;
					wall.StartX = Position.X;
					wall.EndX = direction < 0 ? Arena.Left : Arena.Right;
					wall.Bottom = floor.Y;
					wall.Top = floor.Y - SlamFireHeight;
					wall.Speed = SlamFireSpeed * Speedup;
					wall.Telegraph = 0.35f;
				});
	}

	// Wedged and dizzy, brows wandering, eyes dim, the anvil sinking back into its chin: hits land hard.
	IEnumerator<float> Stunned()
	{
		Enter(Phase.Stunned);
		for (var time = 0f; time < StunTime; time += Time.Delta)
		{
			// Lids sagging, pupils rolling.
			mood = new(-0.5f, -0.6f, MathF.Sin(time * 4) * 0.8f, -0.2f, 0, 0.45f + 0.2f * MathF.Sin(time * 2.3f),
				MathF.Sin(time * 5), MathF.Cos(time * 5));
			mouth = 0.5f;
			eyes = Calc.Approach(eyes, 0.25f, Time.Delta * 3);
			morph.X = Calc.Approach(morph.X, 0, Time.Delta * 2);
			Velocity = Vector2.Zero;
			sculpt.RollVelocity += MathF.Sin(time * 3) * 2 * Time.Delta;
			yield return 0;
		}
		morph.X = 0;
		AudioEvent("boss/hurt");
		Squish = new(0.8f, 1.25f);
		for (var time = 0f; time < 0.8f; time += Time.Delta)
		{
			eyes = Calc.Approach(eyes, 1, Time.Delta * 3);
			mood = Expression.Angry;
			Hover(2);
			yield return 0;
		}
		FinishAttack();
	}

	// Sad, it drifts across the room, tears falling from both eyes.
	IEnumerator<float> Weep()
	{
		Enter(Phase.Weep);
		var arena = Arena;
		var direction = Center.X < arena.Center.X ? 1 : -1;
		var tearTimer = 0f;
		var eye = 0;
		for (var time = 0f; time < WeepTime; time += Time.Delta)
		{
			mood = Expression.Sad;
			mouth = 0.3f;
			var x = Center.X + direction * WeepDrift;
			if (x < arena.Left + Radius * 1.5f || x > arena.Right - Radius * 1.5f)
				direction = -direction;
			var target = new Vector2(Center.X + direction * WeepDrift, arena.Top + arena.Height * HoverHeight * 0.7f);
			Velocity = Calc.Approach(Velocity, new Vector2(direction * WeepDrift, (target.Y - Center.Y) * 2), 200 * Time.Delta);
			for (tearTimer -= Time.Delta; tearTimer <= 0; tearTimer += TearInterval / Speedup)
			{
				eye = 1 - eye;
				var at = Center + new Vector2((eye * 2 - 1) * Radius * 0.34f, Radius * 0.15f);
				BulletPattern.FireArc(Game, at.RoundToPoint2(), Vector2.UnitY, 1, 0, 150, tear =>
				{
					tear.Color = TearColor;
					tear.EdgeColor = new(0x1e3a5a);
					tear.Radius = 2.5f;
					tear.StartSpeedScale = 0.05f;
					tear.SpeedRampTime = 0.9f;
					tear.LaunchDelay = 0;
					tear.WobbleAmplitude = 0.6f;
				});
			}
			yield return 0;
		}
		FinishAttack();
	}

	// Afraid, it cracks in half and bares its core, which spirals shots until it closes.
	IEnumerator<float> Split()
	{
		Enter(Phase.Split);
		AudioEvent("rig_puppet/surprised");
		for (var time = 0f; time < 0.6f; time += Time.Delta)
		{
			// The cracks race open and blaze before it gives.
			cracks = Calc.Approach(cracks, 1, Time.Delta * 2);
			morph.W = time / 0.6f * 0.6f;
			mood = Expression.Afraid;
			Velocity = Calc.Approach(Velocity, Vector2.Zero, 300 * Time.Delta);
			sculpt.Wobble = MathF.Max(sculpt.Wobble, 0.03f);
			yield return 0;
		}
		Game.Shake(0.4f, 2.5f);
		AudioEvent("boss/knockback");
		for (var time = 0f; time < SplitOpenTime; time += Time.Delta)
		{
			morph.Z = Ease.Back.Out(time / SplitOpenTime);
			morph.W = time / SplitOpenTime;
			yield return 0;
		}
		morph.Z = 1;
		var angle = Game.rng.Float(MathF.Tau);
		var shotTimer = 0f;
		for (var time = 0f; time < SplitHoldTime; time += Time.Delta)
		{
			mood = Expression.Pained;
			morph.W = 0.7f + 0.3f * MathF.Sin(time * 12);
			Hover(0.4f);
			for (shotTimer -= Time.Delta; shotTimer <= 0; shotTimer += SpiralInterval / Speedup)
			{
				angle += SpiralTurn;
				var direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
				BulletPattern.FireArc(Game, Center.RoundToPoint2(), direction, 1, 0, SpiralSpeed * Speedup);
				BulletPattern.FireArc(Game, Center.RoundToPoint2(), -direction, 1, 0, SpiralSpeed * Speedup);
			}
			yield return 0;
		}
		for (var time = 0f; time < 0.5f; time += Time.Delta)
		{
			morph.Z = 1 - Ease.Cube.In(time / 0.5f);
			morph.W = morph.Z;
			yield return 0;
		}
		morph.Z = morph.W = 0;
		Game.Shake(0.2f, 2);
		Squish = new(1.25f, 0.8f);
		FinishAttack();
	}

	void UpdateFace()
	{
		var target = Expression.Lerp(mood, Expression.Pained, MathF.Min(pain, 1));
		sculpt.Face = Expression.Lerp(sculpt.Face, target, 1 - MathF.Exp(-FaceResponse * Time.Delta)) with { MouthOpen = MathF.Max(mouth, pain * 0.7f) };
		var toPlayer = ToPlayer;
		var asleep = phase == Phase.Asleep;
		var targetYaw = asleep ? 0 : Math.Clamp(toPlayer.X / 160, -1, 1) * 0.6f;
		var targetPitch = asleep ? 0 : Math.Clamp(-toPlayer.Y / 200, -1, 1) * 0.3f - (phase == Phase.Slam ? 0.15f : 0);
		sculpt.Look(targetYaw, targetPitch, 40, 8);
		Spring.Step(ref sculpt.Roll, ref sculpt.RollVelocity, 0, phase == Phase.Stunned ? 8 : 40, 6, Time.Delta);
		sculpt.GazeTarget = phase is Phase.Asleep or Phase.Stunned or Phase.Dying ? null : Center + toPlayer;
		sculpt.Step();
	}

	// In the editor it's drawn awake, eyes lit, rather than asleep as the fight begins.
	internal override void SyncVisual(bool editor)
	{
		sculpt.Radius = Radius;
		sculpt.ShapeId = HeadShapeId;
		sculpt.Near = HeadColor;
		sculpt.Bruise = BruiseColor;
		// Each stage runs its eyes hotter.
		var heat = (Stage - 1) * 0.25f + morph.W * 0.3f;
		var lit = editor ? 1 : eyes;
		sculpt.Eye = lit > 0.02f ? Color.Lerp(Color.Black, Color.Lerp(EyeColor, Color.White, heat), lit) : default;
		sculpt.Tint = phase == Phase.Stunned ? Color.Lerp(Color.White, StunTint, 0.5f + 0.5f * MathF.Sin(Timer * 7)) : Color.White;
		sculpt.Morph = morph;
		// Its cracks spread out from the seam it splits along and burn hotter each stage; a hit flares them.
		sculpt.PatternBoost = new(cracks, (Stage - 1) * 0.35f + morph.W + pain * 0.3f);
		sculpt.VisualFPS = editor ? 0 : VisualFPS;
	}

	public override void OnCollideX()
	{
		sculpt.Impact(0, Velocity.X);
		StopX();
	}

	public override void OnCollideY()
	{
		sculpt.Impact(1, Velocity.Y);
		StopY();
	}

	internal override void OnWasHit(HitInfo hitInfo)
	{
		if (phase is Phase.Dying or Phase.Asleep || Health <= 0)
			return;
		var at = hitInfo.AttackBox.Rect.Center;
		var away = (Center - (Vector2)hitInfo.By.Position).Normalized(-Vector2.UnitY);
		var onCore = phase == Phase.Split && Vector2.Distance(at, Center) < Radius * 0.7f;
		var damage = phase == Phase.Stunned ? StunDamage : onCore ? CoreDamage : 1;
		sculpt.Dent(at, DentScale);
		sculpt.Wobble = MathF.Max(sculpt.Wobble, WobbleKick * (damage > 1 ? 1.5f : 1));
		sculpt.YawVelocity += MathF.Sign(away.X) * SpinKick;
		sculpt.RollVelocity += MathF.Sign(away.X) * SpinKick * 0.5f;
		pain = damage > 1 ? 1.5f : 1;
		Squish = new(0.85f, 1.15f);
		contactGrace = HitContactGrace;
		Game.Particles?.Emit(chunkEffect, at, damage > 1 ? 24 : 10, Calc.Angle(away + new Vector2(0, -0.5f)), 1, 0.9f);
		if (phase != Phase.Stunned)
			Velocity += away * HitShove;
		Health = Math.Max(0, Health - (int)damage);
		if (Health <= 0)
		{
			attack.Start(Die());
			return;
		}
		Game.HitStun(damage > 1 ? 0.15f : 0.08f);
		Game.Shake(damage > 1 ? 0.25f : 0.12f);
		AudioEvent(damage > 1 ? "boss/hurt" : "rig_puppet/hurt");
	}

	// Splits wide, flaring, shedding pops, then gone.
	IEnumerator<float> Die()
	{
		Enter(Phase.Dying);
		Mask = Masks.None;
		AudioEvent("boss/death");
		Game.HitStun(0.3f);
		for (var time = 0f; time < 3; time += Time.Delta)
		{
			var t = time / 3;
			mood = Expression.Pained;
			mouth = 1;
			morph = new(0, 0, Ease.Cube.Out(t), t);
			Velocity = Vector2.Zero;
			sculpt.Wobble = MathF.Max(sculpt.Wobble, 0.05f * t);
			Game.Shake(0.2f, float.Lerp(0.3f, 1.2f, t));
			if (Time.OnInterval(0.12f) && GetVisualBounds() is var bounds)
				Game.Create<Pop>((bounds.TopLeft + new Vector2(Game.rng.Float(bounds.Width), Game.rng.Float(bounds.Height))).RoundToPoint2());
			yield return 0;
		}
		Game.HitStun(0.3f);
		Game.Shake(0.5f, 3);
		Game.KillForThisSession(this);
		Game.Destroy(this);
	}

	static Vector2 Rotate(Vector2 v, float radians) => Vector2.Transform(v, Matrix3x2.CreateRotation(radians));

	public override void Render(Batcher batcher)
	{
		if (!SpriteBlinkHidden || phase == Phase.Dying)
			sculpt.Render(batcher);
	}

	public override void RenderNormals(Batcher batcher) => sculpt.RenderNormals(batcher);
	public override void RenderEmissive(Batcher batcher) => sculpt.RenderEmissive(batcher);
	public override void RenderShadow(Batcher batcher, Color color) => sculpt.RenderShadow(batcher, color);
	public override void RenderReflection(Batcher batcher, Color color) => sculpt.RenderShadow(batcher, color);
	public override Rect GetVisualBounds() => sculpt.VisualBounds;

	// The eyes tint the room; the slam's landing spot glows on the floor, brighter as the anvil grows; the bared core
	// blazes.
	public override void ForEachLight(Action<Vector2, float, float, float, Color> action)
	{
		// Its eyes tint the room, dimly: any brighter and the lighting pass blows a glowing patch across its own face.
		if (eyes > 0.05f)
			action(Center, Radius * 4.5f * eyes, 0.22f * eyes, 10, EyeColor);
		if (slamMark is { } mark)
			action(mark, Radius * (0.6f + morph.X), 0.6f + morph.X, 4, SlamWaveColor);
		if (morph.W > 0.05f)
			action(Center, Radius * 4 * morph.W, 1.4f * morph.W, 12, Color.Lerp(EyeColor, Color.White, 0.4f));
	}
}
