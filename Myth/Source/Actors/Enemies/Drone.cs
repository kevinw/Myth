using System.Numerics;
using Foster.Framework;

namespace Myth;

[GameActor(Sprite = "fire", Tags = ActorTag.Enemy | ActorTag.Flying)]
public class Drone : Actor
{
	static float IdleRadius => 24f;
	static float IdleAcceleration => 190f;
	static float IdleMaxSpeed => 22f;
	static float NoticeRange => Game.Width * 0.75f;

	// Kiting: hover this far from the player, backing off when closer, closing when farther.
	static float PreferredDistance => 72f;
	static float DistanceDeadzone => 14f;
	static float Acceleration => 300f;
	static float MaxSpeed => 85f;

	// Air drag as a per-second approach-to-zero. NormalFriction is the resting hover feel;
	// a hit swaps it for KnockbackFriction so the drone sails, then it ramps back over the window.
	static float NormalFriction => 150f;
	static float KnockbackFriction => 8f;
	static float KnockbackSpeed => 220f;
	static float KnockbackDuration => 0.4f;
	static float WallRestitution => 0.72f;
	int sparkEffect = -1;         // particle effect slot for the digging sparks

	static float ShootInterval => 2.5f;
	static float ShootRange => 180f;
	static float BulletSpeed => 135f;
	static float RecoilSpeed => 40f;

	// Turret: a short barrel that rotates toward the player at a fixed rate (so it lags and
	// reads as mechanical) and fires bullets from its muzzle tip.
	static float TurretLength => 10f;     // muzzle reach from the body center
	static float TurretBack => -2f;       // back edge tucked into the body
	static float TurretWidth => 5f;
	static float TurretTurnSpeed => 4.5f; // radians/sec
	static float TurretBevel => 1.6f;     // width of the outward-normal rim in the normals pass

	// Banking: the rotor assembly leans in the direction of travel, eased in so it reads as
	// momentum rather than snapping. Tilt tracks velocity, which itself ramps via Acceleration.
	static float TiltPerSpeed => 0.005f;
	static float MaxTilt => 0.5f;
	static float TiltApproach => 6f;
	static float BladeSpinSpeed => 34f;
	static float BladeSquash => 0.65f; // horizontal squash so the spinning disc reads as tilted

	static float BodyRadius => 7f;

	// The buzz loop's "Intensity" (0-1) tracks speed, smoothed so the drone's constant jitter
	// doesn't chatter the parameter. start/stop_move use hysteresis around the same speed, set
	// above IdleMaxSpeed so drifting on the spot stays silent and only real darts announce.
	static float BuzzIntensityApproach => 3.5f;
	static float MoveSoundStartSpeed => 34f;
	static float MoveSoundStopSpeed => 14f;

	Rng rng = new();
	int health = 3;
	Point2 playerPosition;
	Vector2 toPlayer; // player -> drone (points away from the player)
	float friction;
	float tilt;
	float bladeSpin;
	float turretAngle;
	float shootTimer;
	Vector2 idleOrigin;
	bool noticedPlayer;
	SoundInstance? buzz;
	float buzzIntensity;
	bool moving;

	readonly Coroutine routine = new();

	public override void Destroyed() {
		routine.Stop();
		ReleaseParticleEffect(ref sparkEffect);
		StopAndReleaseSound(ref buzz, true);
	}

	public Drone()
	{
		Hitbox = new(new RectInt(-7, -7, 14, 14));
		Mask = Masks.Enemy;
		Depth = -5;
		friction = NormalFriction;
	}

	public override void Added()
	{
		rng = new(Position.X * 73856093 ^ Position.Y * 19349663);
		idleOrigin = Position;
		SetState(IdleRoutine());
		sparkEffect = AcquireParticleEffect("sparks");
		if (Game.AudioEventsEnabled)
			buzz = Game.CreateAudioEvent("enemies/drone/buzz", Position, start: true);
	}

	// A state is just its running routine (see ShieldKnight): transition by handing SetState a
	// fresh one; the routine's own body is the state's per-frame behavior.
	void SetState(IEnumerator<float> next)
		=> routine.Start(next);

	IEnumerator<float> IdleRoutine()
	{
		var target = idleOrigin;
		var retargetTimer = 0f;
		while (true)
		{
			retargetTimer -= Time.Delta;
			var fromOrigin = (Vector2)Position - idleOrigin;
			if (fromOrigin.LengthSquared() > IdleRadius * IdleRadius)
			{
				target = idleOrigin;
				retargetTimer = 0.25f;
			}
			else if (retargetTimer <= 0 || Vector2.DistanceSquared(Position, target) < 4)
			{
				target = idleOrigin + rng.Vec2() * rng.Float(IdleRadius * 0.35f, IdleRadius);
				retargetTimer = rng.Float(0.45f, 1.1f);
			}

			var direction = (target - (Vector2)Position).Normalized();
			Velocity += direction * IdleAcceleration * Time.Delta;
			var speed = Velocity.Length();
			if (speed > IdleMaxSpeed)
				Velocity *= IdleMaxSpeed / speed;
			yield return 0;
		}
	}

	IEnumerator<float> HoverRoutine()
	{
		while (true)
		{
			var dist = toPlayer.Length();
			var awayDir = dist > 0.01f ? toPlayer / dist : new Vector2(0, -1);

			var desired = Vector2.Zero;
			if (dist < PreferredDistance - DistanceDeadzone)
				desired = awayDir;
			else if (dist > PreferredDistance + DistanceDeadzone)
				desired = -awayDir;

			Velocity += desired * Acceleration * Time.Delta;
			var speed = Velocity.Length();
			if (speed > MaxSpeed)
				Velocity *= MaxSpeed / speed;

			shootTimer -= Time.Delta;
			if (shootTimer <= 0 && dist < ShootRange)
			{
				Shoot();
				shootTimer = ShootInterval;
			}

			yield return 0;
		}
	}

	// Hit sends us here: coast with almost no drag (set in OnWasHit) while it ramps back to
	// normal, so the knockback carries far and bounces off walls before the drone recovers.
	IEnumerator<float> KnockbackRoutine(Vector2 direction)
	{
		var directionAngle = MathF.Atan2(-direction.Y, -direction.X);
		Game.Particles?.Emit(sparkEffect, Position, 40, directionAngle, MathF.PI * 0.25f);

		float t = 0;
		while (t < KnockbackDuration)
		{
			t += Time.Delta;
			friction = Calc.Approach(friction, NormalFriction, (NormalFriction / KnockbackDuration) * Time.Delta);
			yield return 0;
		}
		friction = NormalFriction;
		SetState(HoverRoutine());
	}

	void Shoot()
	{
		// Fire along the turret's current facing (which lags the player) from its muzzle tip.
		var dir = Calc.AngleToVector(turretAngle);
		var muzzle = Position + (Point2)(dir * TurretLength);

		Game.Create<Bullet>(muzzle, b => b.Velocity = dir * BulletSpeed);
		Velocity -= dir * RecoilSpeed; // physical kick backward
		AudioEvent("enemies/drone/fire");
	}

	public override void Update()
	{
		base.Update();

		var player = Game.GetFirst<Player>();
		if (player != null)
		{
			playerPosition = player.Position;
			toPlayer = Position - playerPosition;
			if (!noticedPlayer && CanNotice(player))
			{
				noticedPlayer = true;
				SetState(HoverRoutine());
			}
		}

		bladeSpin += BladeSpinSpeed * Time.Delta;

		var targetTilt = Math.Clamp(Velocity.X * TiltPerSpeed, -MaxTilt, MaxTilt);
		tilt = Calc.Approach(tilt, targetTilt, TiltApproach * Time.Delta);

		if (noticedPlayer)
		{
			var toPlayerDir = -toPlayer; // turret aims at the player; toPlayer points away from them
			if (toPlayerDir.LengthSquared() > 0.0001f)
				turretAngle = Calc.AngleApproach(turretAngle, Calc.Angle(toPlayerDir), TurretTurnSpeed * Time.Delta);
		}

		routine.Update(Time.Delta);

		Velocity = Calc.Approach(Velocity, Vector2.Zero, friction * Time.Delta);
		UpdateBuzz(player);
	}

	void UpdateBuzz(Player? player)
	{
		var speed = Velocity.Length();
		buzzIntensity = Calc.Approach(buzzIntensity,
			Calc.ClampedMap(speed, 0, MaxSpeed), BuzzIntensityApproach * Time.Delta);
		Game.SetAudioEventPosition(buzz, Position, player?.Position);
		Game.SetAudioEventParameter(buzz, "Intensity", buzzIntensity);

		if (moving ? speed < MoveSoundStopSpeed : speed > MoveSoundStartSpeed)
		{
			moving = !moving;
			AudioEvent(moving ? "enemies/drone/start_move" : "enemies/drone/stop_move");
		}
	}

	bool CanNotice(Player player)
		=> Vector2.DistanceSquared(player.Position, Position) <= NoticeRange * NoticeRange;


	// Bounce instead of the base Stop, so knockback ricochets off walls.
	public override void OnCollideX() => Velocity.X = -Velocity.X * WallRestitution;
	public override void OnCollideY() => Velocity.Y = -Velocity.Y * WallRestitution;

	internal override void OnWasHit(HitInfo hitInfo)
	{
		if (health <= 0)
			return;
		noticedPlayer = true;

		Game.HitStun(0.15f);

		health--;
		if (health <= 0)
		{
			for (int i = 0; i < 3; i++)
				Game.Create<Pop>(Position + new Point2(rng.Int(-6, 6), rng.Int(-8, 4)));
			Game.HitStun(0.2f);
			AudioEvent("enemies/drone/death");
			Game.DestroyForThisSession(this);
			return;
		}
		AudioEvent("enemies/drone/hurt");

		var away = (Vector2)(Position - hitInfo.By.Position);
		var len = away.Length();
		away = len > 0.01f ? away / len : new Vector2(0, -1);

		Velocity = away * KnockbackSpeed;
		friction = KnockbackFriction;
		SetState(KnockbackRoutine(away));
	}

	public override void Render(Batcher batcher)
	{
		if (SpriteBlinkHidden) return;
		RenderShapes(batcher, Color.White);
	}

	public override void RenderShadow(Batcher batcher, Color color) {
		if (SpriteBlinkHidden) return;
		RenderShapes(batcher, color);
	}

	// The normals pass gets its own geometry: a lit sphere for the body (a triangle fan whose
	// center normal faces the viewer and whose rim normals point radially outward in screen
	// space, nz~0), and flat facing-camera normals for the rotor so it lights coherently with
	// the body rather than inheriting the background. The lighting shader normalizes the
	// interpolated result, so the fan reads as a smooth hemisphere.
	public override void RenderNormals(Batcher batcher)
	{
		if (SpriteBlinkHidden)
			return;

		var faceViewer = new Color(0x8080ff); // (0.5, 0.5, 1.0) => normal (0, 0, 1)
		DrawRotor(batcher, faceViewer, faceViewer);
		RenderTurretNormals(batcher);
		RenderSphereNormals(batcher, Vector2.Zero, BodyRadius, 24);
	}

	public override void RenderEmissive(Batcher batcher)
	{
		DrawEye(batcher);
	}

	void RenderShapes(Batcher batcher, Color color)
	{
		if (SpriteBlinkHidden)
			return;

		DrawRotor(batcher, new Color(0x2a2e38), new Color(0xc0c6d0));
		DrawTurret(batcher, new Color(0x23262e), new Color(0x8a90a0));
		batcher.PushMatrix(Matrix3x2.CreateScale(1.0f, 0.95f));
		batcher.Circle(Vector2.Zero, BodyRadius, 16, Color.Violet);
		DrawEye(batcher);
		batcher.PopMatrix();
	}

	void DrawTurret(Batcher batcher, Color barrelColor, Color muzzleColor)
	{
		batcher.PushMatrix(Matrix3x2.CreateRotation(turretAngle));
		batcher.Rect(TurretBack, -TurretWidth / 2, TurretLength - TurretBack, TurretWidth, barrelColor);
		batcher.Rect(TurretLength - 2, -TurretWidth / 2, 2, TurretWidth, muzzleColor); // muzzle tip
		batcher.PopMatrix();
	}

	// Normal "outline": the barrel's interior faces the viewer while its four edges bevel outward
	// (edge normals splay along the rim), so the turret catches a lit rim. The bevel normals must
	// rotate with the barrel, and the batcher matrix only transforms positions (not vertex colors),
	// so both the geometry and the encoded normals are rotated here in code rather than via PushMatrix.
	void RenderTurretNormals(Batcher batcher)
	{
		var fwd = Calc.AngleToVector(turretAngle);
		var side = new Vector2(-fwd.Y, fwd.X); // barrel +Y in screen space
		float x0 = TurretBack, x1 = TurretLength, hw = TurretWidth * 0.5f;
		var flat = new Color(0x8080ff);

		Vector2 P(float px, float py) => fwd * px + side * py;

		batcher.Quad(P(x0, -hw), P(x1, -hw), P(x1, hw), P(x0, hw), flat); // interior faces viewer
		TurretBevel_(batcher, P(x1, -hw), P(x1, hw), fwd, flat);          // muzzle end
		TurretBevel_(batcher, P(x0, hw), P(x0, -hw), -fwd, flat);         // back
		TurretBevel_(batcher, P(x1, hw), P(x0, hw), side, flat);          // one long side
		TurretBevel_(batcher, P(x0, -hw), P(x1, -hw), -side, flat);       // other long side
	}

	// A rim strip along edge a->b: outer vertices encode the outward normal, inner vertices are flat.
	void TurretBevel_(Batcher batcher, Vector2 a, Vector2 b, Vector2 outward, Color flat)
	{
		var oc = RimNormalColor(outward);
		batcher.Quad(a, b, b - outward * TurretBevel, a - outward * TurretBevel, oc, oc, flat, flat);
	}

	void DrawRotor(Batcher batcher, Color armColor, Color bladeColor)
	{
		const float armX = 5f;        // horizontal offset of each mast from center
		const float armWidth = 2f;
		const float mastTop = -12f;   // masts rise above the body (top ~ -BodyRadius)
		const float mastBottom = -4f;
		const float bladeLength = 11f;
		const float bladeThickness = 2f;

		// Rotor assembly (masts + blades) banks with travel; the body stays upright.
		batcher.PushMatrix(Matrix3x2.CreateRotation(tilt));
		batcher.Rect(-armX - armWidth / 2, mastTop, armWidth, mastBottom - mastTop, armColor);
		batcher.Rect(armX - armWidth / 2, mastTop, armWidth, mastBottom - mastTop, armColor);
		DrawBlade(batcher, new Vector2(-armX, mastTop), bladeLength, bladeThickness, bladeColor);
		DrawBlade(batcher, new Vector2(armX, mastTop), bladeLength, bladeThickness, bladeColor);
		batcher.PopMatrix();
	}

	// Body as a lit sphere: center normal faces the viewer, rim normals splay outward (nz~0).
	static void RenderSphereNormals(Batcher batcher, Vector2 center, float radius, int steps)
	{
		var faceViewer = new Color(0x8080ff); // (0.5, 0.5, 1.0)
		for (int i = 0; i < steps; i++)
		{
			var d0 = Calc.AngleToVector(i / (float)steps * Calc.TAU);
			var d1 = Calc.AngleToVector((i + 1) / (float)steps * Calc.TAU);
			batcher.Triangle(
				center, center + d0 * radius, center + d1 * radius,
				faceViewer, RimNormalColor(d0), RimNormalColor(d1));
		}
	}

	// Screen-space outward normal (x right, y down) encoded as color = normal * 0.5 + 0.5.
	static Color RimNormalColor(Vector2 dir)
		=> new(dir.X * 0.5f + 0.5f, dir.Y * 0.5f + 0.5f, 0.5f, 1.0f);

	static void DrawEye(Batcher batcher) {
		//batcher.Circle(new Vector2(0, -1), 1.6f, 8, new Color(0xff4040)); // eye
	}

	void DrawBlade(Batcher batcher, Vector2 at, float length, float thickness, Color color)
	{
		// Squash X in screen space (between the spin and the translate) so the disc's sweep is an
		// ellipse, reading as a rotor tilted away from the camera rather than a flat pinwheel.
		batcher.PushMatrix(Matrix3x2.CreateRotation(bladeSpin) * Matrix3x2.CreateScale(1.0f, BladeSquash) * Matrix3x2.CreateTranslation(at));
		batcher.Rect(-length / 2, -thickness / 2, length, thickness, color);
		batcher.PopMatrix();
	}
}
