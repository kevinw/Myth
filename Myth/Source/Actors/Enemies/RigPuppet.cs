using System.Numerics;
using Foster.Framework;

namespace Myth;

[GameActor(Sprite = "player", Tags = ActorTag.Enemy, BoundsX = -12, BoundsY = -40, BoundsW = 24, BoundsH = 40,
	LivePositionEditable = true)]
public class RigPuppet : RigActor
{
	enum StepTraversal { None, Up, Down }

	// How whoever's in control (an ability, a script, or nobody) drives the visual. Scripts only steer the playback.
	struct RigDirective
	{
		public bool Loop, FootIK, SeekGround;
		public float PlaybackSpeed, SpeedMultiplier, PoseModifierWeight, PoseJostle, LookPoseWeight, YawWeight;
		public Vector2 VisualOffset;
		public Vector2? LookTarget;
		public float? Yaw;

		public static RigDirective Free(float playbackSpeed)
			=> new() { Loop = true, FootIK = true, PlaybackSpeed = playbackSpeed, SpeedMultiplier = 1, PoseModifierWeight = 1 };

		public void Play(float playbackSpeed, bool loop = true, bool footIK = true)
			=> (Loop, FootIK, SeekGround, PlaybackSpeed) = (loop, footIK, false, playbackSpeed);

		public void Play(RigTake take) => Play(take.Speed, take.Loop, take.FootIK);
	}

	RigDirective Directive => abilityActive ? ability : scripted ? script : turnTaking ? turn : RigDirective.Free(PlaybackSpeed);

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
	// Played whenever he turns around outside an ability (chasing, patrolling), and he finishes it before doing anything
	// else; the yaw sweep stretches to its length. Empty keeps the quick procedural turn.
	public string TurnTake = "";
	public bool ArticulatedCombatBounds;
	public bool KnockedBackByHits = true; // off for the big ones: a hit dents and flinches them instead
	public bool StraddlesPlayerDepth;
	[InspectorGroup("Costume")] public bool Ruff;

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
	RigDirective ability = RigDirective.Free(1);
	bool scripted;
	bool scriptedArrived;
	RigDirective script = RigDirective.Free(1);
	RigDirective turn = RigDirective.Free(1);
	bool turnTaking;
	Signs lastFacing;
	float scriptedWalkOffsetX;
	float scriptedStopDistance;
	float? scriptedWalkTargetX;
	Actor? scriptedWalkTargetActor;
	readonly Coroutine scriptedRoutine = new();
	Game.StorySpeechBubble? scriptedSpeech;

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

	public override void Added()
	{
		base.Added();
		patrolOriginX = Position.X;
		patrolling = IdlePatrol && !Game.IsEditorPreview;
		lastFacing = Facing;
	}

	protected override string InitialClip => IdleAnimationName;

	protected override void VisualCreated()
	{
		visual.Footstep = OnFootstep;
		SyncRuff();
	}

	protected override void SyncVisual()
	{
		// The walk plays at whatever rate makes its planted feet keep pace with the ground, at any size.
		var stride = visual.WalkGroundSpeed(WalkAnimationName) * Rig.PixelsPerMeter;
		var gaitScale = SyncGaitToSpeed && (!Performance || scripted) && walking && stepTraversal == StepTraversal.None
			? MathF.Abs(Velocity.X) / (stride > 0 ? stride : WalkSpeed) : 1;
		var directive = Directive;
		visual.PlaybackSpeed = directive.PlaybackSpeed * directive.SpeedMultiplier * gaitScale * AnimationSpeedMultiplier;
		visual.HeadTint = RigHeadTint;
		visual.GaitActive = walking;
		visual.Offset = stepVisualOffset + directive.VisualOffset;
		visual.LookTarget = abilityActive ? directive.LookTarget : !patrolling ? Game.GetFirst<Player>()?.Position : null;
		visual.LookPoseWeight = directive.LookPoseWeight;
		visual.PoseModifierWeight = directive.PoseModifierWeight;
		visual.PoseJostlePixels = directive.PoseJostle;
		visual.YawOverride = directive.Yaw;
		visual.YawOverrideWeight = directive.YawWeight;
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

	protected override void UpdateRig()
	{
		if (StepUp <= 0)
			StepUp = 2;
		if (StepDown <= 0)
			StepDown = 2;
		UpdateScriptRoutine();
		if (stepTraversal == StepTraversal.None && !AbilityControlsMovement)
			Velocity.Y += Gravity * Time.Delta;
		else
			Velocity = Vector2.Zero;
		MoveActor();
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
		turnTaking &= !abilityActive && !scripted && !hitReacting && !recovering;
		if (abilityActive)
			walking = false;
		else if (turnTaking)
			turnTaking = !visual.AnimationFinished || !visual.FacingMatches;
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
		if (Facing != lastFacing && !abilityActive && !scripted && !hitReacting && !recovering && grounded &&
			stepTraversal == StepTraversal.None && TurnTake.Length > 0 && RigTakes.TryGet(TurnTake, out var turnTake))
			BeginTurnTake(turnTake);
		lastFacing = Facing;
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
			else if (!hitReacting && !recovering && !turnTaking)
				visual.Play(walking ? WalkName() : IdleName(), seededPhase: Rig.SeedAnimationPhase);
		}

		SyncVisual();
		var directive = Directive;
		var directed = abilityActive || scripted || turnTaking;
		visual.Update(grounded,
			directed ? directive.FootIK : !Performance && !hitReacting && !recovering && stepTraversal == StepTraversal.None,
			loop: directed ? directive.Loop : !hitReacting && !recovering,
			seekGround: directive.SeekGround);
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
	protected virtual Color RigHeadTint => Color.White;

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
		ability.Play(PlaybackSpeed);
		visual.Play(IdleName(), blendDuration: blendDuration);
	}

	protected void PlayAbilityTake(string name, float blendDuration = 0.2f)
	{
		var take = RigTakes.Get(name);
		ability.Play(take);
		visual.Play(take, blendDuration);
		if (!take.FootIK)
			visual.ResetFeet();
	}

	protected void SetAbilityLookTarget(Vector2? target) => ability.LookTarget = target;
	protected void SetAbilityLookPoseWeight(float weight) => ability.LookPoseWeight = Math.Clamp(weight, 0, 1);
	protected void SetAbilityPoseModifierWeight(float weight) => ability.PoseModifierWeight = Math.Clamp(weight, 0, 1);
	protected void SetAbilityPoseJostle(float pixels) => ability.PoseJostle = MathF.Max(0, pixels);
	protected void SetAbilityVisualOffset(Vector2 offset) => ability.VisualOffset = offset;
	protected void SetAbilityFootIK(bool enabled, bool seekGround = false)
		=> (ability.FootIK, ability.SeekGround) = (enabled, enabled && seekGround);
	protected void FreezeAbilityPose(float duration) => visual.Freeze(duration);
	protected void SetAbilityPlaybackSpeedMultiplier(float multiplier)
		=> ability.SpeedMultiplier = MathF.Max(0, multiplier);
	protected void LockAbilityAnimationYaw() => visual.LockAnimationYaw();
	protected void UnlockAbilityAnimationYaw() => visual.UnlockAnimationYaw();
	protected void RecoilAbilityPose(Vector2 screenVelocity, float spread) => visual.Recoil(screenVelocity, spread);
	protected void SetAbilityFingerSpin(bool enabled, bool leftHandOnly = false)
		=> visual.SetFingerSpin(enabled, leftHandOnly);
	protected void SetAbilityHandWidthScale(float scale, bool leftHandOnly = false)
		=> visual.SetHandWidthScale(scale, leftHandOnly);
	protected void SetAbilityYaw(float? yaw, float weight = 1)
		=> (ability.Yaw, ability.YawWeight) = (yaw, Math.Clamp(weight, 0, 1));
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
		ability = RigDirective.Free(PlaybackSpeed);
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

	// Stops dead and plays the turn; the visual turns the body with the clip's own spin, so FacingMatches (which walking
	// waits on) holds off until the feet are done.
	void BeginTurnTake(RigTake take)
	{
		turnTaking = true;
		walking = false;
		Velocity.X = 0;
		turn.Play(take);
		visual.Play(take);
		visual.TurnWithClip();
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
		script = RigDirective.Free(PlaybackSpeed);
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
		script = RigDirective.Free(PlaybackSpeed);
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
		script.Play(PlaybackSpeed);
		visual.Play(WalkName(), seededPhase: Rig.SeedAnimationPhase);
	}

	public void PlayTake(string name, float blendDuration = 0.2f)
	{
		BeginScript();
		var take = RigTakes.Get(name);
		scriptedWalkTargetActor = null;
		scriptedWalkTargetX = null;
		scriptedArrived = false;
		script.Play(take);
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

#if EDITOR
	protected override void StopForAnimationPreview()
	{
		EndScript();
		if (abilityActive)
		{
			InterruptAbility();
			abilityActive = false;
			ResetAbilityControl();
		}
		hitReacting = recovering = false;
		stepTraversal = StepTraversal.None;
		stepVisualOffset = Vector2.Zero;
		AnimationPreviewSpeed = PlaybackSpeed;
	}

	protected override void ResumeAfterAnimationPreview() => visual.Play(IdleName());
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

	public override void Destroyed()
	{
		if (abilityActive)
			InterruptAbility();
		EndScript();
		base.Destroyed();
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

	private protected override HumanoidRigVisual.DrawParts VisualParts
		=> StraddlesPlayerDepth ? HumanoidRigVisual.DrawParts.InFrontOfPlayer : HumanoidRigVisual.DrawParts.All;
	protected override bool ArticulatedCombat => ArticulatedCombatBounds;

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

	internal HumanoidRigVisual.RuffPose RuffPose => visual.GetRuffPose();
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
