using System.Numerics;
using Foster.Framework;

namespace Myth;

// A giant whose only weak point is his head, far out of reach -- until a big attack spends him and he collapses with it
// on the floor for a few seconds. Body hits clank off. He telegraphs everything through his sculpted body: the limb
// about to strike heats up, swells and glows (SculptHeat), lighting the room and shedding embers.
// - Stomp: a quake along the floor (jump it).
// - Swipe: a low sweep of the arm (stay out of reach).
// - Throw: hurls a wall of fire that sweeps the room (burrow under it, or get above it), then collapses.
// Each third of his health lost speeds him up and adds to the fire.
[GameActor(Sprite = "player", Tags = ActorTag.Enemy | ActorTag.Boss, BoundsX = -12, BoundsY = -40, BoundsW = 24, BoundsH = 40,
	LivePositionEditable = true)]
public sealed class Colossus : RigPuppet
{
	enum Phase { None, Stomp, Swipe, Throw, Collapse, Down, Rise }

	const string StompTake = "Stomp";
	const string SwipeTake = "Swipe";
	const string ThrowTake = "ColossusThrow";
	const string CollapseTake = "ColossusCollapse";
	const string RiseTake = "ColossusRise";
	const string DeathTake = "Death";

	static float SwipeRange => 90f;
	static float StompRange => 220f;
	static float PreferredDistance => 110f; // walks in until the player is about this close
	static float WalkArriveDistance => 4f;
	static float WalkGiveUpTime => 3f;
	static float AttackCooldown => 1.1f;
	static int AttacksBeforeThrow => 3; // he always gets to the fire (and the collapse after it) this often

	// Fractions of each take: the windup heats until the release, then cools.
	static float StompRelease => 0.84f;
	static float SwipeWindupEnd => 0.42f;
	static float SwipeRecoveryStart => 0.72f;
	static float ThrowRelease => 0.55f;
	static float HeatCool => 3f; // per second after a release

	static float StompWaveSpeed => 130f;
	static float StompWaveRange => 520f;
	static Color StompWaveColor => new(0xff7a2a);
	static float FireWallHeight => 72f; // above a full jump: under it or over it, not through it
	static float FireWallSpeed => 230f;
	static float FireWallTelegraph => 1.2f;

	static float DownTime => 3f; // head on the floor
	static int HitsPerCollapse => 4; // head hits before he hauls himself up early
	static float HeadHitSlack => 1.25f; // × head radius
	static float HitContactGrace => 0.6f;

	static float LimbLightRadius => 60f;
	static float LimbLightIntensity => 1.4f;
	static float HeadLightRadius => 50f;
	static Color HeatLightColor => new(0xff7a2a);
	static Color VulnerableHeadTint => new(0xffb070);
	static float EmberRate => 40f; // per second from a fully hot limb

	Phase phase;
	int maxHealth;
	float cooldown;
	int attacksSinceThrow;
	float? walkTargetX;
	float walkTime;
	bool released;
	bool awake;
	float downTime;
	int headHitsThisCollapse;
	float contactGrace;
	float emberTimer;
	int sparkEffect = -1;
	int emberEffect = -1;
	// Heat targets per region, eased toward by the visual's SculptHeat.
	readonly float[] heat = new float[5];

	int Stage => Health > maxHealth * 2 / 3 ? 1 : Health > maxHealth / 3 ? 2 : 3;
	protected override float AnimationSpeedMultiplier => Stage switch { 1 => 1f, 2 => 1.15f, _ => 1.3f };
	protected override float? WalkTargetX => walkTargetX;
	protected override float PlayerStopDistance => PreferredDistance;
	bool HeadLow => phase is Phase.Down or Phase.Rise or Phase.Collapse;
	internal override bool ContactDamageEnabled => contactGrace <= 0 && Health > 0 && !HeadLow;
	Vector2 HeadWorld => Position + visual.HeadPosition;
	float HeadPulse => 0.5f + 0.5f * MathF.Sin(Time.SecondsF * 7);
	protected override Color RigHeadTint => phase == Phase.Down ? Color.Lerp(Color.White, VulnerableHeadTint, HeadPulse) : Color.White;

	public override void Added()
	{
		maxHealth = Health;
		base.Added();
		sparkEffect = AcquireParticleEffect("elp_hit_sparks");
		emberEffect = AcquireParticleEffect("embers");
	}

	public override void Destroyed()
	{
		base.Destroyed();
		ReleaseParticleEffect(ref sparkEffect);
		ReleaseParticleEffect(ref emberEffect);
	}

	public override void Update()
	{
		contactGrace = MathF.Max(0, contactGrace - Time.Delta);
		base.Update();
		UpdateHeat();
	}

	protected override bool UpdateAbility(Player player, bool grounded)
	{
		if (!awake)
		{
			Velocity.X = 0;
			PlayAbilityIdle();
			SetAbilityLookTarget(player.Position);
			awake = Game.WorldView.Overlaps(GetVisualBounds());
			return true;
		}

		var toPlayer = player.Position - Position;
		if (phase == Phase.None)
		{
			cooldown = MathF.Max(0, cooldown - Time.Delta * AnimationSpeedMultiplier);
			if (walkTargetX is { } target)
			{
				walkTime += Time.Delta;
				if (walkTime < WalkGiveUpTime && MathF.Abs(target - Position.X) > WalkArriveDistance)
					return false;
				walkTargetX = null;
			}
			if (!grounded)
				return false;
			Facing = toPlayer.X < 0 ? Signs.Negative : Signs.Positive;
			Velocity.X = 0;
			PlayAbilityIdle();
			SetAbilityLookTarget(player.Position);
			if (cooldown > 0 || !RigFacingMatches)
				return true;
			Choose(player, MathF.Abs(toPlayer.X));
			if (phase == Phase.None)
				return walkTargetX == null;
		}

		Velocity.X = 0;
		switch (phase)
		{
			case Phase.Stomp:
				SetAbilityLookTarget(null);
				Warm(StompRelease, released, HumanoidRigVisual.SculptRegion.LeftLeg, HumanoidRigVisual.SculptRegion.RightLeg);
				if (!released && AbilityAnimationProgress >= StompRelease)
					ReleaseStomp(player);
				if (AbilityAnimationFinished)
					FinishAttack();
				break;

			case Phase.Swipe:
				SetAbilityLookTarget(null);
				var progress = AbilityAnimationProgress;
				heat[(int)HumanoidRigVisual.SculptRegion.LeftArm] = progress < SwipeWindupEnd
					? Ease.Cube.In(progress / SwipeWindupEnd)
					: 1 - Math.Clamp((progress - SwipeRecoveryStart) / (1 - SwipeRecoveryStart), 0, 1);
				if (AbilityAnimationFinished)
					FinishAttack();
				break;

			case Phase.Throw:
				SetAbilityLookTarget(released ? null : player.Position);
				Warm(ThrowRelease, released, HumanoidRigVisual.SculptRegion.RightArm, HumanoidRigVisual.SculptRegion.Torso);
				if (!released && AbilityAnimationProgress >= ThrowRelease)
					ReleaseThrow(player);
				if (AbilityAnimationFinished)
					BeginCollapse();
				break;

			case Phase.Collapse:
				SetAbilityLookTarget(null);
				if (AbilityAnimationFinished)
				{
					phase = Phase.Down;
					downTime = 0;
					Game.Shake(0.25f, 2.5f);
					AudioEvent("boss/stomp");
				}
				break;

			case Phase.Down:
				// Hold the collapse's last frame: the head lies on the floor.
				SetAbilityPlaybackSpeedMultiplier(0);
				downTime += Time.Delta;
				if (downTime >= DownTime || headHitsThisCollapse >= HitsPerCollapse)
					BeginRise();
				break;

			case Phase.Rise:
				SetAbilityLookTarget(player.Position);
				if (AbilityAnimationFinished)
					FinishAttack();
				break;
		}
		return true;
	}

	void Choose(Player player, float distance)
	{
		var stage = Stage;
		if (attacksSinceThrow >= AttacksBeforeThrow)
		{
			BeginThrow();
			return;
		}
		var roll = Game.rng.Float();
		if (distance <= SwipeRange)
		{
			if (roll < 0.55f) BeginSwipe();
			else if (roll < 0.85f) BeginStomp(player);
			else BeginThrow();
		}
		else if (distance <= StompRange)
		{
			if (roll < 0.45f) BeginStomp(player);
			else if (roll < 0.65f + stage * 0.05f) BeginThrow();
			else BeginWalk(player);
		}
		else if (roll < 0.4f)
			BeginThrow();
		else
			BeginWalk(player);
	}

	void BeginWalk(Player player)
	{
		walkTime = 0;
		walkTargetX = player.Position.X - MathF.Sign(player.Position.X - Position.X) * PreferredDistance;
	}

	void BeginStomp(Player player)
	{
		phase = Phase.Stomp;
		released = false;
		attacksSinceThrow++;
		SetAbilityYaw(RigFacingYaw);
		LockAbilityAnimationYaw();
		PlayAbilityTake(StompTake);
	}

	void ReleaseStomp(Player player)
	{
		released = true;
		Game.Shake(0.22f, 4f);
		var toe = RigLeftToePosition.Y > RigRightToePosition.Y ? RigLeftToePosition : RigRightToePosition;
		var probe = new RectInt((int)toe.X - 1, (int)toe.Y - 5, 2, 2);
		if (Game.TryFindSurface(this, probe, Vector2.UnitY, 16, out var surface) && surface.Normal.Y < -0.5f)
		{
			var wave = Game.Create<RadialWave>((surface.Point - surface.Normal * 3).RoundToPoint2());
			wave.Source = this;
			wave.Target = player;
			wave.CanHit = static (wave, target) => target is Player p && (p.State == Player.States.Burrow || p.Grounded());
			wave.Medium = RadialWaveMedium.Solid;
			wave.Speed = StompWaveSpeed * (Stage == 3 ? 1.25f : 1);
			wave.MaxRadius = StompWaveRange;
			wave.Thickness = 6;
			wave.Color = StompWaveColor;
			wave.CoreColor = Color.Lerp(StompWaveColor, Color.White, 0.55f);
		}
		AudioEvent("boss/stomp");
		Game.KickRenderTarget(Vector2.UnitY * 60);
		RecoilAbilityPose(-Vector2.UnitY * 60, 20);
	}

	void BeginSwipe()
	{
		phase = Phase.Swipe;
		attacksSinceThrow++;
		SetAbilityYaw(RigFacingYaw);
		PlayAbilityTake(SwipeTake);
		AudioEvent("rig_puppet/swing");
	}

	void BeginThrow()
	{
		phase = Phase.Throw;
		released = false;
		attacksSinceThrow = 0;
		SetAbilityYaw(RigFacingYaw);
		PlayAbilityTake(ThrowTake);
	}

	// The fire wall leaves from his hand toward the player; from stage 2 a second one runs the other way, and in stage 3
	// they're faster and warn for less.
	void ReleaseThrow(Player player)
	{
		released = true;
		Game.Shake(0.2f, 2.5f);
		AudioEvent("boss/knockback");
		var toward = player.Position.X < Position.X ? -1 : 1;
		SpawnFireWall(toward);
		if (Stage >= 2)
			SpawnFireWall(-toward);
	}

	void SpawnFireWall(int direction)
	{
		if (Room is not { } room)
			return;
		var bounds = room.WorldBounds;
		var floor = Position.Y;
		var stage = Stage;
		Game.Create<FireWall>(Position, wall =>
		{
			wall.Source = this;
			wall.StartX = Position.X;
			wall.EndX = direction < 0 ? bounds.Left : bounds.Right;
			wall.Bottom = floor;
			wall.Top = floor - FireWallHeight;
			wall.Speed = FireWallSpeed * (stage == 3 ? 1.2f : 1);
			wall.Telegraph = FireWallTelegraph * (stage == 3 ? 0.8f : 1);
		});
	}

	void BeginCollapse()
	{
		phase = Phase.Collapse;
		headHitsThisCollapse = 0;
		SetAbilityYaw(null);
		UnlockAbilityAnimationYaw();
		PlayAbilityTake(CollapseTake);
	}

	void BeginRise()
	{
		phase = Phase.Rise;
		SetAbilityPlaybackSpeedMultiplier(1);
		PlayAbilityTake(RiseTake);
		AudioEvent("boss/hurt");
	}

	void FinishAttack()
	{
		phase = Phase.None;
		released = false;
		cooldown = AttackCooldown;
		SetAbilityYaw(null);
		SetAbilityPlaybackSpeedMultiplier(1);
		UnlockAbilityAnimationYaw();
		SetAbilityLookTarget(null);
	}

	protected override void InterruptAbility()
	{
		FinishAttack();
		Array.Clear(heat);
	}

	// Windup heats the given regions toward the release, then they cool.
	void Warm(float releaseAt, bool releasedAlready, params ReadOnlySpan<HumanoidRigVisual.SculptRegion> regions)
	{
		var amount = releasedAlready ? 0 : Ease.Cube.In(Math.Clamp(AbilityAnimationProgress / releaseAt, 0, 1));
		foreach (var region in regions)
			heat[(int)region] = releasedAlready ? heat[(int)region] : amount;
	}

	void UpdateHeat()
	{
		if (!visualReady)
			return;
		for (var i = 0; i < heat.Length; i++)
		{
			if (released || phase == Phase.None)
				heat[i] = MathF.Max(0, heat[i] - HeatCool * Time.Delta);
			visual.SculptHeat[i] = heat[i];
		}
		EmitLimbEmbers();
	}

	void EmitLimbEmbers()
	{
		if (emberEffect < 0 || Game.Particles is not { } particles)
			return;
		var hottest = 0f;
		foreach (var h in heat)
			hottest = MathF.Max(hottest, h);
		if (hottest <= 0.05f)
			return;
		emberTimer -= Time.Delta;
		for (var emitted = 0; emberTimer <= 0 && emitted < 4; emitted++, emberTimer += 1 / (EmberRate * hottest))
			for (var i = 0; i < heat.Length; i++)
				if (heat[i] > 0.05f && Game.rng.Chance(heat[i]))
					particles.Emit(emberEffect, RegionPoint((HumanoidRigVisual.SculptRegion)i), 1, -MathF.PI / 2, 1, 0.8f);
	}

	Vector2 RegionPoint(HumanoidRigVisual.SculptRegion region) => region switch
	{
		HumanoidRigVisual.SculptRegion.LeftArm => visual.LeftHandWorldPosition,
		HumanoidRigVisual.SculptRegion.RightArm => visual.RightHandWorldPosition,
		HumanoidRigVisual.SculptRegion.LeftLeg => RigLeftToePosition,
		HumanoidRigVisual.SculptRegion.RightLeg => RigRightToePosition,
		_ => Position + visual.TorsoCenter,
	};

	// Only the head takes damage; anything else clanks off in sparks.
	internal override void OnWasHit(HitInfo hitInfo)
	{
		var rect = hitInfo.AttackBox.Rect;
		var radius = visual.HeadRadius * HeadHitSlack;
		var nearest = Vector2.Clamp(HeadWorld, rect.TopLeft, rect.BottomRight);
		if (Vector2.DistanceSquared(nearest, HeadWorld) > radius * radius)
		{
			Clank(hitInfo);
			return;
		}
		contactGrace = HitContactGrace;
		if (phase == Phase.Down)
			headHitsThisCollapse++;
		Game.Create<Pop>(rect.Center, pop =>
		{
			pop.SpriteName = "impact";
			pop.AnimationName = "star_hit";
			pop.Flags |= Pop.PopFlags.AnimateDuringHitStun | Pop.PopFlags.Emissive;
		});
		base.OnWasHit(hitInfo);
	}

	void Clank(HitInfo hitInfo)
	{
		var at = hitInfo.AttackBox.Rect.Center;
		var away = MathF.Sign(Position.X - hitInfo.By.Position.X);
		Game.Particles?.Emit(sparkEffect, at, 12, Calc.Angle(new Vector2(away == 0 ? (int)Facing : -away, -0.6f).Normalized()));
		visual.Dent(hitInfo.AttackBox.Rect, 0.5f);
		Game.HitStun(0.05f);
		AudioEvent("rig_puppet/hurt");
		if (hitInfo.By is Player player)
			player.Velocity.X = MathF.Sign(player.Position.X - Position.X) * 90;
	}

	internal override void OnWasHurt()
	{
		Game.Shake(0.3f);
		Game.HitStun(0.15f);
		AudioEvent("boss/hurt");
	}

	protected override void Die()
	{
		InterruptAbility();
		AudioEvent("boss/death");
		StartScript(DeathRoutine());
	}

	IEnumerator<float> DeathRoutine()
	{
		PlayTake(DeathTake);
		for (var time = 0f; time < 3; time += Time.Delta)
		{
			Game.Shake(0.2f, Calc.Lerp(0.2f, 0.8f, time / 3));
			for (var i = 0; i < visual.SculptHeat.Length; i++)
				visual.SculptHeat[i] = Ease.Cube.In(time / 3);
			if (Time.OnInterval(0.12f) && GetVisualBounds() is var bounds)
				Game.Create<Pop>((bounds.TopLeft + new Vector2(Game.rng.Float(bounds.Width), Game.rng.Float(bounds.Height))).RoundToPoint2());
			yield return 0;
		}
		Game.HitStun(0.3f);
		Game.Shake(0.5f, 3f);
		DestroyOnDeath();
	}

	public override void ForEachLight(Action<Vector2, float, float, float, Color> action)
	{
		if (!visualReady)
			return;
		for (var i = 0; i < heat.Length; i++)
			if (heat[i] > 0.05f)
				action(RegionPoint((HumanoidRigVisual.SculptRegion)i), LimbLightRadius * heat[i],
					LimbLightIntensity * heat[i], 10, HeatLightColor);
		if (phase == Phase.Down)
			action(HeadWorld, HeadLightRadius, 0.6f + 0.6f * HeadPulse, 8, VulnerableHeadTint);
	}
}
