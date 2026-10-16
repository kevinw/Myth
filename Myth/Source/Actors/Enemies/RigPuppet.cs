using System.Numerics;
using Foster.Framework;

namespace Myth;

[GameActor(Sprite = "player", Tags = ActorTag.Enemy, BoundsX = -12, BoundsY = -40, BoundsW = 24, BoundsH = 40,
	LivePositionEditable = true)]
public class RigPuppet : Actor, INeuralTextureAgent
{
	enum StepTraversal { None, Up, Down }

	internal virtual void OnWasHurt() {
		Game.Shake(0.15f);
		Game.HitStun(0.11f);
		AudioEvent(Health <= 0 ? "rig_puppet/die" : "rig_puppet/hurt");
	}

	[InspectorGroup("Animation")] public string IdleAnimationName = "Zombie_Idle_Loop";
	public string WalkAnimationName = "Zombie_Walk_Fwd_Loop";
	public bool Performance;
	[I(0.1f, 3)] public float PlaybackSpeed = 1;
	public bool SyncGaitToSpeed = true;
	[ActorSettings(typeof(HumanoidRigSettings))] public readonly HumanoidRigSettings Rig = new()
	{
		SeedAnimationPhase = true,
		SecondaryMotion = 1,
		GaitAsymmetry = 0.35f,
		PlantResponse = 0.65f,
		TrackingLean = 0.45f,
		TurnInertia = 0.6f,
		EyeMotion = 0.65f,
		LimbTaper = 0.18f,
		DepthColorContrast = 1.25f,
		DepthWidthAmount = 0.08f,
	};
	[ActorSettings(typeof(NeuralSurfaceSettings), "Neural Surface", "Neural")]
	public readonly NeuralSurfaceSettings Neural = new();
	[ActorSettings(typeof(HumanoidNeuralReactionSettings), "Neural Reactions", "Neural")]
	public readonly HumanoidNeuralReactionSettings NeuralReactions = new()
	{
		NeuralFootContactStrength = 0.3f,
	};
	[I(1, 4), InspectorGroup("Gameplay")] public int StepUp = 2;
	[I(1, 4)] public int StepDown = 2;
	[I(1, 40)] public int Health = 3;
	// Starts him pacing back and forth near his spawn, ignoring the player until he has a clear
	// sightline toward the side he's facing; then he wakes into normal behaviour for good.
	public bool IdlePatrol;
	// Once awake, closes in and swings an arm (AttackTake) at the player when in reach. The swing hurts where
	// the arm actually goes when ArticulatedCombatBounds is on.
	public bool SwingsAtPlayer;
	[InspectorGroup("Animation")] public string SurpriseTake = "Surprise"; // spotting the player out of a patrol
	public string AttackTake = "ArmSwing";
	public bool ArticulatedCombatBounds;
	public bool KnockedBackByHits = true; // off for the big ones: a hit dents and flinches them instead
	public bool StraddlesPlayerDepth;
	[InspectorGroup("Costume")] public bool Ruff;

	internal HumanoidRigVisual visual = null!;
	RigPuppetBackLegRenderer? backLegRenderer;
	RigPuppetRuff? ruff;
	StepTraversal stepTraversal;
	Vector2 stepStartOffset;
	Vector2 stepVisualOffset;
	float stepTime;
	float stepDuration;
	bool hitReacting;
	bool recovering;
	bool walking;
	bool patrolling;
	enum Reaction { None, Surprise, Attack }
	Reaction reaction;
	bool spotted;        // just saw the player out of a patrol: startle before anything else
	float attackCooldown;
	float patrolOriginX;
	float patrolSightTimer;
	bool abilityActive;
	bool visualReady;
	bool abilityLoop = true;
	bool abilityFootIK = true;
	bool abilityFootIKGrounded;
	float abilityPlaybackSpeed = 1;
	float abilityPlaybackSpeedMultiplier = 1;
	float abilityPoseModifierWeight = 1;
	float abilityPoseJostle;
	Vector2 abilityVisualOffset;
	Vector2? abilityLookTarget;
	float abilityLookPoseWeight;
	float? abilityYaw;
	float abilityYawWeight;
	bool scripted;
	bool scriptedArrived;
	bool scriptedLoop = true;
	bool scriptedFootIK = true;
	float scriptedPlaybackSpeed = 1;
	float scriptedWalkOffsetX;
	float scriptedStopDistance;
	float? scriptedWalkTargetX;
	Actor? scriptedWalkTargetActor;
	readonly Coroutine scriptedRoutine = new();
	Game.StorySpeechBubble? scriptedSpeech;
#if EDITOR
	internal bool AnimationPreviewEnabled;
	internal bool AnimationPreviewPaused;
	internal bool AnimationPreviewLooping = true;
	internal bool AnimationPreviewBlend = true;
	internal bool AnimationPreviewIK = true;
	internal int AnimationPreviewClipIndex;
#endif

	static float Gravity => 360f;
	static float GroundFriction => 420f;
	static float WalkSpeed => 22f;
	static float WalkAcceleration => 180f;
	static float StopDistance => 18f;
	static int GroundLookahead => 7;
	static float HitKnockbackSpeed => 64f;
	static float HitLiftSpeed => 28f;
	static float StepBaseDuration => 0.22f;
	static float StepDurationPerPixel => 0.016f;
	static float StepArcHeight => 3f;
	static float InterruptedStepCatchupSpeed => 80f;
	static float ScriptWalkTimeout => 10f;
	static float PatrolReach => 80f;              // how far he paces each way from spawn
	static float PatrolSightInterval => 0.1f;     // seconds between line-of-sight checks
	static float PatrolSightHeight => 26f;         // eye height above the origin for the sightline
	static float SurpriseHop => 70f;               // a startled little jump
	static float SurpriseJostle => 2f;             // pixels of shake at the start of the startle, easing out
	static float AttackRange => 30f;               // horizontal reach that starts a swing
	static float AttackHeight => 24f;
	static float AttackLunge => 45f;               // the step into the swing
	static float AttackCooldown => 1.4f;

	public RigPuppet()
	{
		Sprite = null;
		Hitbox = new(new RectInt(-6, -31, 12, 31));
		Mask = Masks.Enemy;
		Depth = -5;
		IFrameTime = 0.18f;
	}

	public override bool CastsSpriteShadow => true;
	public override bool CastsReflection => true;
	public override bool LightingDithered => false;

	public override void Added()
	{
		base.Added();
		CreateVisual();
		patrolOriginX = Position.X;
		patrolling = IdlePatrol && !Game.IsEditorPreview;
#if EDITOR
		if (Game.IsEditorPreview)
			Game.EditorPreviewSync += SyncEditorPreviewPose;
#endif
	}

	void CreateVisual()
	{
		visual = new(this, Rig, Neural, NeuralReactions);
		visual.Footstep = OnFootstep;
		SyncVisual();
		visual.Initialize(IdleAnimationName);
		visualReady = true;
		if (UsesFrozenPose)
			SampleFrozenPose(resetDynamics: true);
		else
			SyncRuff();
	}

	void SyncVisual()
	{
		// The walk plays at whatever rate makes its planted feet keep pace with the ground, at any size.
		var stride = visual.WalkGroundSpeed(WalkAnimationName) * Rig.PixelsPerMeter;
		var gaitScale = SyncGaitToSpeed && (!Performance || scripted) && walking && stepTraversal == StepTraversal.None
			? MathF.Abs(Velocity.X) / (stride > 0 ? stride : WalkSpeed) : 1;
		visual.PlaybackSpeed = (abilityActive ? abilityPlaybackSpeed * abilityPlaybackSpeedMultiplier :
			scripted ? scriptedPlaybackSpeed : PlaybackSpeed) * gaitScale * AnimationSpeedMultiplier;
		visual.HeadTint = RigHeadTint;
		visual.BackArmCombatEnabled = BackArmCombatEnabled;
		visual.GaitActive = walking;
		visual.Offset = stepVisualOffset + (abilityActive ? abilityVisualOffset : Vector2.Zero);
		visual.LookTarget = abilityActive ? abilityLookTarget : !patrolling && TracksPlayer ? Game.GetFirst<Player>()?.Position : null;
		visual.LookPoseWeight = abilityActive ? abilityLookPoseWeight : 0;
		visual.PoseModifierWeight = abilityActive ? abilityPoseModifierWeight : 1;
		visual.PoseJostlePixels = abilityActive ? abilityPoseJostle : 0;
		visual.YawOverride = abilityActive ? abilityYaw : null;
		visual.YawOverrideWeight = abilityActive ? abilityYawWeight : 0;
		SyncBackLegRenderer();
		SyncRuff();
	}

	void SyncBackLegRenderer()
	{
		if (!StraddlesPlayerDepth)
		{
			if (backLegRenderer != null)
				Game.Destroy(backLegRenderer);
			backLegRenderer = null;
			return;
		}
		backLegRenderer ??= Game.Create<RigPuppetBackLegRenderer>(Position, it => it.Owner = this);
		backLegRenderer.Position = Position;
		backLegRenderer.KeepOnRoomTransition = KeepOnRoomTransition;
	}

	void SyncRuff()
	{
		if (!Ruff || !visualReady)
		{
			if (ruff != null)
				Game.Destroy(ruff);
			ruff = null;
		}
		else
		{
			ruff ??= Game.Create<RigPuppetRuff>(Position, it => it.Owner = this);
			ruff.KeepOnRoomTransition = KeepOnRoomTransition;
		}
		if (visualReady)
			visual.Accessory = ruff;
	}

	string IdleName()
		=> visual.TryGetClip(IdleAnimationName, out _) ? IdleAnimationName : "Zombie_Idle_Loop";

	string WalkName()
		=> visual.TryGetClip(WalkAnimationName, out _) ? WalkAnimationName : "Zombie_Walk_Fwd_Loop";

	public override void Update()
	{
		EnsureState();
#if EDITOR
		if (AnimationPreviewEnabled)
		{
			UpdateAnimationPreview();
			return;
		}
#endif
		if (UsesFrozenPose)
		{
			UpdateFrozenPose();
			return;
		}
		UpdateScriptRoutine();
		if (stepTraversal == StepTraversal.None && !AbilityControlsMovement)
			Velocity.Y += Gravity * Time.Delta;
		else
			Velocity = Vector2.Zero;
		base.Update();
		var grounded = Grounded();
		if (grounded && Velocity.Y > 0)
			Velocity.Y = 0;

		var player = Game.GetFirst<Player>();
		var wasAbilityActive = abilityActive;
		abilityActive = !patrolling && !Performance && !scripted && !hitReacting && !recovering && player != null &&
			UpdateAbility(player, grounded);
		if (wasAbilityActive && !abilityActive)
			ResetAbilityControl();
		walking = (!Performance || scripted || patrolling) && stepTraversal != StepTraversal.None;
		if (abilityActive)
			walking = false;
		else if (patrolling)
			UpdatePatrol(grounded, player);
		else if (scripted)
			UpdateScriptedMovement(grounded);
		else if (!Performance && stepTraversal == StepTraversal.None && player != null)
		{
			var targetX = WalkTargetX ?? player.Position.X;
			var stopDistance = WalkTargetX.HasValue ? 0 : PlayerStopDistance;
			if (MathF.Abs(targetX - Position.X) > 1)
			{
				Facing = targetX < Position.X ? Signs.Negative : Signs.Positive;
				if (visual.FacingMatches && grounded && !hitReacting && !recovering &&
					MathF.Abs(targetX - Position.X) > stopDistance)
				{
					walking = HasGroundAhead();
					if (!walking)
						TryStartStepDown((int)Facing);
				}
			}
		}
		if (stepTraversal == StepTraversal.None && grounded)
			Velocity.X = Calc.Approach(Velocity.X,
				walking ? (int)Facing * WalkSpeed * (scripted ? 1 : WalkSpeedMultiplier) : 0,
				(walking ? WalkAcceleration : GroundFriction) * Time.Delta);
		UpdateStepTraversal();

		if (!abilityActive)
		{
			if (scripted)
			{
				if (walking)
					visual.Play(WalkName(), seededPhase: Rig.SeedAnimationPhase);
			}
			else if (hitReacting && grounded && visual.Time >= visual.ActiveClip.Duration)
			{
				hitReacting = false;
				recovering = true;
				visual.Play("LayToIdle");
			}
			else if (recovering && visual.Time >= visual.ActiveClip.Duration)
			{
				recovering = false;
				visual.Play(IdleName(), seededPhase: Rig.SeedAnimationPhase);
			}
			else if (!hitReacting && !recovering)
				visual.Play(walking ? WalkName() : IdleName(), seededPhase: Rig.SeedAnimationPhase);
		}

		SyncVisual();
		visual.Update(grounded,
			abilityActive ? abilityFootIK : scripted ? scriptedFootIK :
				!Performance && !hitReacting && !recovering && stepTraversal == StepTraversal.None,
			loop: abilityActive ? abilityLoop : scripted ? scriptedLoop : !hitReacting && !recovering,
			seekGround: abilityActive && abilityFootIKGrounded);
	}

	void UpdateFrozenPose()
	{
		Velocity = Vector2.Zero;
		base.Update();
		SampleFrozenPose();
	}

	void SampleFrozenPose(bool resetDynamics = false)
	{
		walking = false;
		var animation = visual.TryGetClip(FrozenPoseAnimationName, out _) ? FrozenPoseAnimationName : IdleName();
		visual.Play(animation, blend: false);
		visual.Time = visual.ActiveClip.Duration * Math.Clamp(FrozenPoseProgress, 0, 1);
		if (resetDynamics)
			visual.ResetPoseDynamics();
		SyncVisual();
		visual.Update(FrozenPoseFootIK, FrozenPoseFootIK, advance: false, seekGround: FrozenPoseFootIK);
	}

	// The plain puppet's abilities: the startle on spotting you, then arm swings. Subclasses with their own
	// repertoire (EyeLaserPuppet) override both.
	protected virtual bool UpdateAbility(Player player, bool grounded)
	{
		attackCooldown = MathF.Max(0, attackCooldown - Time.Delta);
		switch (reaction)
		{
			case Reaction.None:
				if (spotted)
					return BeginReaction(Reaction.Surprise, SurpriseTake, player);
				return SwingsAtPlayer && attackCooldown <= 0 && grounded && InSwingReach(player)
					&& BeginReaction(Reaction.Attack, AttackTake, player);
			case Reaction.Surprise:
				SetAbilityLookTarget(player.Position);
				SetAbilityPoseJostle(SurpriseJostle * (1 - AbilityAnimationProgress));
				break;
		}
		if (!AbilityAnimationFinished)
			return true;
		if (reaction == Reaction.Attack)
			attackCooldown = AttackCooldown;
		reaction = Reaction.None;
		return false;
	}

	protected virtual void InterruptAbility()
	{
		if (reaction == Reaction.Attack)
			attackCooldown = AttackCooldown;
		reaction = Reaction.None;
	}

	bool BeginReaction(Reaction next, string take, Player player)
	{
		spotted = false;
		if (!RigTakes.TryGet(take, out _))
			return false;
		Facing = player.Position.X < Position.X ? Signs.Negative : Signs.Positive;
		reaction = next;
		if (next == Reaction.Surprise)
		{
			PlayAbilityTake(take, 0.08f);
			SetAbilityLookTarget(player.Position);
			Velocity = new Vector2(0, -SurpriseHop);
			AudioEvent("rig_puppet/surprised");
		}
		else
		{
			SetAbilityYaw(RigFacingYaw);
			PlayAbilityTake(take);
			Velocity.X = (int)Facing * AttackLunge;
			AudioEvent("rig_puppet/swing");
		}
		return true;
	}

	bool InSwingReach(Player player)
	{
		var delta = player.Position - Position;
		return MathF.Abs(delta.X) <= AttackRange && MathF.Abs(delta.Y) <= AttackHeight
			&& Math.Sign(delta.X) == (int)Facing && visual.FacingMatches;
	}
	protected virtual bool AbilityControlsMovement => false;
	protected virtual bool SuppressKnockdownOnHit => !KnockedBackByHits;
	protected virtual bool DeathIsPersistent => false;
	protected virtual float PlayerStopDistance => StopDistance;
	/// Where non-scripted movement walks to. Null chases the player, stopping at PlayerStopDistance.
	protected virtual float? WalkTargetX => null;
	/// Scales WalkSpeed for non-scripted movement only; scripted walks keep the base pace.
	protected virtual float WalkSpeedMultiplier => 1;
	protected virtual float AnimationSpeedMultiplier => 1;
	protected virtual float HitLimbReactionStrength => 1;
	protected virtual bool UsesFrozenPose => false;
	protected virtual string FrozenPoseAnimationName => IdleAnimationName;
	protected virtual float FrozenPoseProgress => 0;
	protected virtual bool FrozenPoseFootIK => false;
	protected virtual Color RigTint => Color.White;
	protected virtual bool TracksPlayer => true;
	protected virtual bool ShowsHitBlink => true;
	protected virtual Color RigHeadTint => Color.White;
	protected virtual bool BackArmCombatEnabled => true;

	protected virtual void Die()
	{
		Game.HitStun(0.3f);
		Game.Shake(0.13f, intensity: 1.3f);
		for (var i = 0; i < 3; i++)
			Game.Create<Pop>(Position + new Point2(0, -8 - i * 9));
		DestroyOnDeath();
	}

	protected void DestroyOnDeath()
	{
		if (DeathIsPersistent)
			Game.DestroyPersistently(this);
		else
			Game.DestroyForThisSession(this);
	}

	protected virtual void OnFootstep(Vector2 position) {
		Game.Shake(0.1f, intensity: 1.5f);
		AudioEvent("rig_puppet/footstep");
	}

	protected bool RigFacingMatches => visual.FacingMatches;
	protected Vector2 RigEyePosition => visual.EyeWorldPosition;
	protected Vector2 RigEyeOffset => visual.EyeOffset;
	protected bool RigEyeVisible => visual.EyeVisible;
	protected Vector2 RigLeftToePosition => visual.LeftToeWorldPosition;
	protected Vector2 RigRightToePosition => visual.RightToeWorldPosition;
	protected Vector2 RigLeftHandPosition => visual.LeftHandWorldPosition;
	protected float RigFacingYaw => visual.CurrentFacingYaw;
	protected bool AbilityAnimationFinished => visual.AnimationFinished;
	protected float AbilityAnimationProgress => Math.Clamp(
		(visual.Time - visual.RangeStart) / MathF.Max(0.0001f, visual.RangeEnd - visual.RangeStart), 0, 1);

	protected void PlayAbilityIdle(float blendDuration = -1)
	{
		abilityLoop = abilityFootIK = true;
		abilityFootIKGrounded = false;
		abilityPlaybackSpeed = PlaybackSpeed;
		visual.Play(IdleName(), blendDuration: blendDuration);
	}

	protected void PlayAbilityTake(string name, float blendDuration = 0.2f)
	{
		var take = RigTakes.Get(name);
		abilityLoop = take.Loop;
		abilityFootIK = take.FootIK;
		abilityFootIKGrounded = false;
		abilityPlaybackSpeed = take.Speed;
		visual.Play(take, blendDuration);
		if (!take.FootIK)
			visual.ResetFeet();
	}

	protected void SetAbilityLookTarget(Vector2? target) => abilityLookTarget = target;
	protected void SetAbilityLookPoseWeight(float weight) => abilityLookPoseWeight = Math.Clamp(weight, 0, 1);
	protected void SetAbilityPoseModifierWeight(float weight) => abilityPoseModifierWeight = Math.Clamp(weight, 0, 1);
	protected void SetAbilityPoseJostle(float pixels) => abilityPoseJostle = MathF.Max(0, pixels);
	protected void SetAbilityVisualOffset(Vector2 offset) => abilityVisualOffset = offset;
	protected void SetAbilityFootIK(bool enabled, bool seekGround = false)
	{
		abilityFootIK = enabled;
		abilityFootIKGrounded = enabled && seekGround;
	}
	protected void FreezeAbilityPose(float duration) => visual.Freeze(duration);
	protected void SetAbilityPlaybackSpeedMultiplier(float multiplier)
		=> abilityPlaybackSpeedMultiplier = MathF.Max(0, multiplier);
	protected void LockAbilityAnimationYaw() => visual.LockAnimationYaw();
	protected void UnlockAbilityAnimationYaw() => visual.UnlockAnimationYaw();
	protected void RecoilAbilityPose(Vector2 screenVelocity, float spread) => visual.Recoil(screenVelocity, spread);
	protected void SetAbilityFingerSpin(bool enabled, bool leftHandOnly = false)
		=> visual.SetFingerSpin(enabled, leftHandOnly);
	protected void SetAbilityHandWidthScale(float scale, bool leftHandOnly = false)
		=> visual.SetHandWidthScale(scale, leftHandOnly);
	protected void SetAbilityYaw(float? yaw, float weight = 1)
	{
		abilityYaw = yaw;
		abilityYawWeight = Math.Clamp(weight, 0, 1);
	}
	protected float CameraFacingYaw => MathF.PI * 0.5f - Rig.ProjectionYaw * MathF.PI / 180f;
	protected void SetRuffSpin(float radiansPerSecond)
	{
		ruff?.SpinSpeed = radiansPerSecond;
	}
	protected bool TryGetRuffOrbitPoint(float angle, float radius, out Vector2 worldPoint)
	{
		if (ruff == null)
		{
			worldPoint = default;
			return false;
		}
		worldPoint = Position + ruff.ProjectOrbitPoint(angle, radius);
		return true;
	}

	void ResetAbilityControl()
	{
		abilityLoop = abilityFootIK = true;
		abilityFootIKGrounded = false;
		abilityPlaybackSpeed = PlaybackSpeed;
		abilityPlaybackSpeedMultiplier = 1;
		abilityPoseModifierWeight = 1;
		abilityPoseJostle = 0;
		abilityVisualOffset = Vector2.Zero;
		abilityLookTarget = null;
		abilityLookPoseWeight = 0;
		abilityYaw = null;
		abilityYawWeight = 0;
		visual.PoseModifierWeight = 1;
		visual.PoseJostlePixels = 0;
		visual.LookPoseWeight = 0;
		visual.Resume();
		visual.SetHandWidthScale(1, false);
		visual.UnlockAnimationYaw();
		visual.YawOverride = null;
		visual.YawOverrideWeight = 0;
	}

	void UpdateScriptedMovement(bool grounded)
	{
		if (scriptedWalkTargetActor != null)
			scriptedWalkTargetX = scriptedWalkTargetActor.Position.X + scriptedWalkOffsetX;
		if (scriptedWalkTargetX is not { } target)
			return;

		var distance = target - Position.X;
		if (MathF.Abs(distance) <= scriptedStopDistance && stepTraversal == StepTraversal.None)
		{
			scriptedWalkTargetActor = null;
			scriptedWalkTargetX = null;
			scriptedArrived = true;
			walking = false;
			Velocity.X = 0;
			return;
		}

		Facing = distance < 0 ? Signs.Negative : Signs.Positive;
		if (stepTraversal == StepTraversal.None && visual.FacingMatches && grounded)
		{
			walking = HasGroundAhead();
			if (!walking)
				TryStartStepDown((int)Facing);
		}
	}

	void UpdatePatrol(bool grounded, Player? player)
	{
		// Spot the player only when the sightline toward the side he's pacing is clear; once seen
		// he wakes for good (OnWasHit also wakes him). Throttled to a few frames a second.
		patrolSightTimer -= Time.Delta;
		if (patrolSightTimer <= 0)
		{
			patrolSightTimer = PatrolSightInterval;
			if (player != null && Math.Sign(player.Position.X - Position.X) == (int)Facing && CanSeePlayer(player))
			{
				patrolling = false;
				spotted = true;
				return;
			}
		}

		if (stepTraversal != StepTraversal.None || !grounded || hitReacting || recovering)
			return;

		// Reverse at the ends of the patrol band, then walk unless a ledge stops him — pacing the
		// platform rather than descending it.
		var dir = (int)Facing;
		if (dir > 0 && Position.X >= patrolOriginX + PatrolReach)
			dir = -1;
		else if (dir < 0 && Position.X <= patrolOriginX - PatrolReach)
			dir = 1;
		Facing = dir < 0 ? Signs.Negative : Signs.Positive;

		if (!visual.FacingMatches)
			return;
		walking = HasGroundAhead();
		if (!walking)
			Facing = dir < 0 ? Signs.Positive : Signs.Negative; // ledge ahead: turn back next frame
	}

	// Clear line from his eye to the player's midsection, blocked only by real solids (room
	// geometry and solid actors); jumpthru platforms don't count.
	bool CanSeePlayer(Player player)
	{
		var from = (Vector2)Position - Vector2.UnitY * PatrolSightHeight;
		var to = (Vector2)player.Position - Vector2.UnitY * (PatrolSightHeight * 0.5f);
		var delta = to - from;
		var distance = delta.Length();
		if (distance < 1)
			return true;
		var direction = delta / distance;
		for (var t = 4f; t < distance; t += 4f)
		{
			var point = from + direction * t;
			if (Game.OverlapsSolid(this, new RectInt((int)MathF.Floor(point.X), (int)MathF.Floor(point.Y), 1, 1)))
				return false;
		}
		return true;
	}

	void UpdateScriptRoutine()
	{
		if (!scriptedRoutine.Active)
			return;
		if (!scriptedRoutine.Update(Time.Delta))
			EndScript();
	}

	public void BeginScript()
	{
		if (scripted)
			return;
		if (abilityActive && !SuppressKnockdownOnHit)
		{
			InterruptAbility();
			abilityActive = false;
			ResetAbilityControl();
		}
		scripted = true;
		scriptedPlaybackSpeed = PlaybackSpeed;
		hitReacting = recovering = false;
	}

	public void StartScript(IEnumerator<float> routine)
	{
		ArgumentNullException.ThrowIfNull(routine);
		EndScript();
		BeginScript();
		scriptedRoutine.Start(routine);
	}

	public void EndScript()
	{
		if (!scripted && !scriptedRoutine.Active)
			return;
		scriptedRoutine.Stop();
		Game.CancelSpeech(scriptedSpeech);
		scriptedSpeech = null;
		scripted = false;
		scriptedWalkTargetActor = null;
		scriptedWalkTargetX = null;
		scriptedArrived = false;
		scriptedLoop = true;
		scriptedFootIK = true;
		scriptedPlaybackSpeed = PlaybackSpeed;
		walking = false;
		Velocity.X = 0;
		visual?.Play(IdleName());
	}

	public void WalkTo(Point2 target, float stoppingDistance = 2)
	{
		BeginScript();
		scriptedWalkTargetActor = null;
		scriptedWalkTargetX = target.X;
		BeginScriptWalk(stoppingDistance);
	}

	public void WalkTo(Actor target, float offsetX = 0, float stoppingDistance = 2)
	{
		BeginScript();
		scriptedWalkTargetActor = target;
		scriptedWalkOffsetX = offsetX;
		scriptedWalkTargetX = target.Position.X + offsetX;
		BeginScriptWalk(stoppingDistance);
	}

	void BeginScriptWalk(float stoppingDistance)
	{
		scriptedStopDistance = Math.Max(0, stoppingDistance);
		scriptedArrived = false;
		scriptedLoop = true;
		scriptedFootIK = true;
		scriptedPlaybackSpeed = PlaybackSpeed;
		visual.Play(WalkName(), seededPhase: Rig.SeedAnimationPhase);
	}

	public void PlayTake(string name, float blendDuration = 0.2f)
	{
		BeginScript();
		var take = RigTakes.Get(name);
		scriptedWalkTargetActor = null;
		scriptedWalkTargetX = null;
		scriptedArrived = false;
		scriptedLoop = take.Loop;
		scriptedFootIK = take.FootIK;
		scriptedPlaybackSpeed = take.Speed;
		walking = false;
		Velocity.X = 0;
		visual.Play(take, blendDuration);
		if (!take.FootIK)
			visual.ResetFeet();
	}

	public bool AnimationFinished => visual.AnimationFinished;
	public bool ScriptedArrived => scriptedArrived;

	public void GreetPlayer(string text)
		=> StartScript(WalkToPlayerKneelAndSay(text));

	public IEnumerator<float> WalkToPlayerKneelAndSay(string text)
	{
		Player? player;
		while ((player = Game.GetFirst<Player>()) == null)
			yield return 0;

		var side = Position.X < player.Position.X ? -1 : 1;
		const float GreetingDistance = 40f;
		WalkTo(player, side * GreetingDistance);
		var timeout = ScriptWalkTimeout;
		while (!ScriptedArrived && timeout > 0)
		{
			timeout -= Time.Delta;
			yield return 0;
		}
		if (!ScriptedArrived)
			yield break;
		Facing = player.Position.X < Position.X ? Signs.Negative : Signs.Positive;

		PlayTake("KneelDown");
		while (!AnimationFinished)
			yield return 0;

		while (!Game.CanShowSpeech())
			yield return 0;
		scriptedSpeech = Say(text);
		while (scriptedSpeech is { Finished: false })
			yield return 0;
		scriptedSpeech = null;
	}

	protected virtual string VoiceName => Game.PlayerVoiceName;

	internal Game.StorySpeechBubble? Say(string text)
		=> Game.ShowSpeech(this, new Vector2(0, -44), text, Game.PlayerVoiceProfile, Color.White,
			voice: VoiceName);

	void EnsureState()
	{
		if (visual == null)
			CreateVisual();
		if (Rig.VisualFPS <= 0)
			Rig.VisualFPS = 20;
		if (StepUp <= 0)
			StepUp = 2;
		if (StepDown <= 0)
			StepDown = 2;
	}

#if EDITOR
	internal IReadOnlyList<OzzRig.Clip> AnimationClips => visual.Clips;
	internal OzzRig.Clip AnimationPreviewClip => visual.Clips[AnimationPreviewClipIndex];
	internal float AnimationPreviewTime
	{
		get => visual.Time;
		set => visual.Time = value;
	}
	internal float AnimationPreviewStart => visual.RangeStart;
	internal float AnimationPreviewEnd => visual.RangeEnd;

	internal void BeginAnimationPreview()
	{
		if (AnimationPreviewEnabled)
			return;
		EndScript();
		if (abilityActive)
		{
			InterruptAbility();
			abilityActive = false;
			ResetAbilityControl();
		}
		AnimationPreviewEnabled = true;
		AnimationPreviewClipIndex = 0;
		for (var i = 0; i < visual.Clips.Count; i++)
			if (visual.Clips[i] == visual.ActiveClip)
			{
				AnimationPreviewClipIndex = i;
				break;
			}
		hitReacting = recovering = false;
		stepTraversal = StepTraversal.None;
		stepVisualOffset = Vector2.Zero;
		Velocity = Vector2.Zero;
		visual.ResetTurn();
		visual.ResetFeet();
	}

	internal void EndAnimationPreview()
	{
		if (!AnimationPreviewEnabled)
			return;
		AnimationPreviewEnabled = false;
		AnimationPreviewPaused = false;
		if (!visualReady)
			return;
		if (UsesFrozenPose)
			SampleFrozenPose(resetDynamics: true);
		else
			visual.Play(IdleName());
	}

	internal void PreviewAnimation(int index)
	{
		AnimationPreviewClipIndex = Math.Clamp(index, 0, visual.Clips.Count - 1);
		visual.Play(AnimationPreviewClip.Name, AnimationPreviewBlend, restart: true);
		visual.ResetFeet();
	}

	internal void PreviewTake(RigTake take)
	{
		for (var i = 0; i < visual.Clips.Count; i++)
			if (string.Equals(visual.Clips[i].Name, take.Clip, StringComparison.OrdinalIgnoreCase))
			{
				AnimationPreviewClipIndex = i;
				break;
			}
		AnimationPreviewLooping = take.Loop;
		PlaybackSpeed = take.Speed;
		SetAnimationPreviewIK(take.FootIK);
		visual.Play(take, AnimationPreviewBlend ? 0.2f : 0);
	}

	internal void SetAnimationPreviewIK(bool enabled)
	{
		AnimationPreviewIK = enabled;
		if (!enabled)
			visual.ResetFeet();
	}

	void UpdateAnimationPreview()
	{
		Velocity = UsesFrozenPose ? Vector2.Zero : new(0, Velocity.Y + Gravity * Time.Delta);
		base.Update();
		var grounded = UsesFrozenPose ? AnimationPreviewIK : Grounded();
		if (grounded && Velocity.Y > 0)
			Velocity.Y = 0;
		SyncVisual();
		visual.Update(grounded, AnimationPreviewIK, AnimationPreviewLooping, advance: !AnimationPreviewPaused);
	}

	internal void UpdateAnimationPreviewInEditor()
	{
		if (!AnimationPreviewEnabled)
			return;
		SyncVisual();
		visual.Update(AnimationPreviewIK, AnimationPreviewIK, AnimationPreviewLooping,
			advance: !AnimationPreviewPaused, seekGround: AnimationPreviewIK);
	}

	void SyncEditorPreviewPose()
	{
		if (UsesFrozenPose && !AnimationPreviewEnabled)
			SampleFrozenPose(resetDynamics: true);
	}
#endif

	bool HasGroundAhead()
	{
		var bounds = Hitbox.Rect + Position + new Point2((int)Facing * GroundLookahead, 0);
		return Game.TryFindSurface(this, bounds, Vector2.UnitY, 2, out _);
	}

	bool TryStartStepDown(int direction)
	{
		var forward = new Point2(direction * GroundLookahead, 0);
		var bounds = Hitbox.Rect + Position + forward;
		if (!Game.TryFindSurface(this, bounds, Vector2.UnitY, StepDown * Game.TileSize + 1, out var hit))
			return false;
		var drop = (int)hit.Distance - 1;
		var step = forward + Point2.Down * drop;
		if (drop <= 1 || drop > StepDown * Game.TileSize || OverlapsAny(step, Masks.Solid))
			return false;
		BeginStep(step, StepTraversal.Down);
		return true;
	}

	public override void OnCollideX()
	{
		var direction = Math.Sign(Velocity.X);
		if (direction == 0 || !Grounded())
		{
			base.OnCollideX();
			return;
		}
		for (var rise = 1; rise <= StepUp * Game.TileSize; rise++)
		{
			var up = Point2.Up * rise;
			if (OverlapsAny(up, Masks.Solid))
				break;
			var step = up + Point2.UnitX * direction;
			if (!OverlapsAny(step, Masks.Solid) && OverlapsAny(step + Point2.Down, Masks.Solid))
			{
				for (var forward = 2; forward <= GroundLookahead; forward++)
				{
					var candidate = up + Point2.UnitX * direction * forward;
					if (OverlapsAny(candidate, Masks.Solid) || !OverlapsAny(candidate + Point2.Down, Masks.Solid))
						break;
					step = candidate;
				}
				BeginStep(step, StepTraversal.Up);
				return;
			}
		}
		base.OnCollideX();
	}

	void BeginStep(Point2 step, StepTraversal traversal)
	{
		Position += step;
		stepTraversal = traversal;
		stepStartOffset = -(Vector2)step;
		stepVisualOffset = stepStartOffset;
		stepTime = 0;
		stepDuration = StepBaseDuration + Math.Abs(step.Y) * StepDurationPerPixel;
		Velocity = Vector2.Zero;
		walking = true;
		visual.ResetFeet();
		visual.Play(WalkName(), seededPhase: Rig.SeedAnimationPhase);
	}

	void UpdateStepTraversal()
	{
		if (stepTraversal == StepTraversal.None)
		{
			stepVisualOffset = Calc.Approach(stepVisualOffset, Vector2.Zero, InterruptedStepCatchupSpeed * Time.Delta);
			return;
		}
		stepTime = MathF.Min(stepTime + Time.Delta, stepDuration);
		var progress = stepTime / stepDuration;
		var eased = progress * progress * (3 - 2 * progress);
		stepVisualOffset = stepStartOffset * (1 - eased) - Vector2.UnitY * MathF.Sin(progress * MathF.PI) * StepArcHeight;
		if (stepTime < stepDuration)
			return;
		stepTraversal = StepTraversal.None;
		stepVisualOffset = Vector2.Zero;
	}

	public override void OnCollideY()
	{
		visual?.Land(Velocity.Y);
		base.OnCollideY();
	}

	public override void Destroyed()
	{
#if EDITOR
		if (Game.IsEditorPreview)
			Game.EditorPreviewSync -= SyncEditorPreviewPose;
#endif
		if (abilityActive)
			InterruptAbility();
		EndScript();
		visual?.Dispose();
		visualReady = false;
		if (backLegRenderer != null)
			Game.Destroy(backLegRenderer);
		backLegRenderer = null;
		if (ruff != null)
			Game.Destroy(ruff);
		ruff = null;
	}

	internal override void OnWasHit(HitInfo hitInfo)
	{
		var by = hitInfo.By;
#if EDITOR
		if (AnimationPreviewEnabled)
			return;
#endif
		if (UsesFrozenPose)
		{
			var propAway = Position.X < by.Position.X ? -1f : 1f;
			visual.Hit(((Vector2)(Position - by.Position)).Normalized(Vector2.UnitX * propAway),
				HitLimbReactionStrength);
			return;
		}
		if (scripted || Health <= 0)
			return;
		patrolling = false;
		visual.Dent(hitInfo.AttackBox.Rect);
		var suppressKnockdown = SuppressKnockdownOnHit;
		if (abilityActive && !suppressKnockdown)
		{
			InterruptAbility();
			abilityActive = false;
			ResetAbilityControl();
		}
		Health--;
		OnWasHurt();
		if (Health <= 0)
		{
			Die();
			return;
		}
		Game.HitStun(0.25f);

		var away = Position.X < by.Position.X ? -1f : 1f;
		var hitDirection = ((Vector2)(Position - by.Position)).Normalized(Vector2.UnitX * away);
		visual.Hit(hitDirection, HitLimbReactionStrength);
		if (suppressKnockdown)
			return;
		Velocity = new Vector2(away * HitKnockbackSpeed, -HitLiftSpeed);
		stepTraversal = StepTraversal.None;
		visual.ResetFeet();
		visual.Play("Hit_Knockback", blend: false, restart: true);
		OnEvent("knockback");
		hitReacting = true;
		recovering = false;
	}

	internal virtual void OnEvent(string eventName)
	{
	}

	HumanoidRigVisual.DrawParts VisualParts
		=> StraddlesPlayerDepth ? HumanoidRigVisual.DrawParts.InFrontOfPlayer : HumanoidRigVisual.DrawParts.All;

	public override void Render(Batcher batcher)
	{
		if (!ShowsHitBlink || !SpriteBlinkHidden)
			visual?.Draw(batcher, RigTint, silhouette: false, VisualParts);
	}

	public override void RenderShadow(Batcher batcher, Color color)
		=> visual?.Draw(batcher, color, silhouette: true, VisualParts);

	public override void RenderReflection(Batcher batcher, Color color) => RenderShadow(batcher, color);
	public override void RenderNormals(Batcher batcher) => visual?.DrawNormals(batcher, VisualParts);
	public override void RenderEmissive(Batcher batcher) => visual?.DrawEmissive(batcher);

	internal void RenderBehindPlayer(Batcher batcher)
	{
		if (Visible && StraddlesPlayerDepth && (!ShowsHitBlink || !SpriteBlinkHidden))
			visual?.Draw(batcher, RigTint, silhouette: false, HumanoidRigVisual.DrawParts.BehindPlayer);
	}

	internal void RenderShadowBehindPlayer(Batcher batcher, Color color)
	{
		if (Visible && StraddlesPlayerDepth)
			visual?.Draw(batcher, color, silhouette: true, HumanoidRigVisual.DrawParts.BehindPlayer);
	}

	internal void RenderNormalsBehindPlayer(Batcher batcher)
	{
		if (Visible && StraddlesPlayerDepth)
			visual?.DrawNormals(batcher, HumanoidRigVisual.DrawParts.BehindPlayer);
	}

	public void PerturbNeuralTexture(NeuralTextureActor.PerturbContext context)
		=> visual?.PerturbNeuralTexture(context);

	internal HumanoidRigVisual.RuffPose RuffPose => visual.GetRuffPose();

	internal override bool OverlapsCombatBounds(in RectInt bounds, CombatRole role = CombatRole.Vulnerable)
		=> !ArticulatedCombatBounds || visual == null
			? base.OverlapsCombatBounds(bounds, role)
			: visual.OverlapsCombatBounds(bounds, role);

	internal override void RenderCollisionDebug(Batcher batcher)
	{
		if (ArticulatedCombatBounds)
			visual?.DrawCombatBounds(batcher);
	}

	public override Rect GetVisualBounds()
		=> visual?.GetVisualBounds() ?? new(Position.X - 42, Position.Y - 48, 84, 58);
}

sealed class RigPuppetBackLegRenderer : Actor
{
	public RigPuppet Owner = null!;

	public RigPuppetBackLegRenderer()
	{
		Sprite = null;
		Depth = Depths.BehindPlayer;
	}

	public override bool CastsSpriteShadow => Owner.CastsSpriteShadow;
	public override bool CastsReflection => Owner.CastsReflection;
	public override bool LightingDithered => Owner.LightingDithered;

	public override void Update()
	{
		Position = Owner.Position;
		KeepOnRoomTransition = Owner.KeepOnRoomTransition;
	}

	public override void Render(Batcher batcher) => Owner.RenderBehindPlayer(batcher);
	public override void RenderShadow(Batcher batcher, Color color) => Owner.RenderShadowBehindPlayer(batcher, color);
	public override void RenderReflection(Batcher batcher, Color color) => Owner.RenderShadowBehindPlayer(batcher, color);
	public override void RenderNormals(Batcher batcher) => Owner.RenderNormalsBehindPlayer(batcher);
	public override Rect GetVisualBounds() => Owner.GetVisualBounds();
}
