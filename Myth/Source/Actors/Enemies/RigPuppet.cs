using System.Numerics;
using Foster.Framework;

namespace Myth;

[GameActor(Sprite = "player", Tags = ActorTag.Enemy, BoundsX = -12, BoundsY = -40, BoundsW = 24, BoundsH = 40,
	LivePositionEditable = true)]
public class RigPuppet : Actor, INeuralTextureAgent
{
	enum StepTraversal { None, Up, Down }

	internal virtual void OnWasHurt() {

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
	[ActorSettings(typeof(HumanoidRigWireSettings), "Connecting Wires", "Costume")]
	public readonly HumanoidRigWireSettings Wires = new();
	[I(1, 4), InspectorGroup("Gameplay")] public int StepUp = 2;
	[I(1, 4)] public int StepDown = 2;
	public bool ArticulatedCombatBounds;
	public bool StraddlesPlayerDepth;
	[InspectorGroup("Costume")] public bool Ruff;
	[I(1, 20)] public int Health = 8;

	HumanoidRigVisual visual = null!;
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
#if EDITOR
		if (Game.IsEditorPreview)
			Game.EditorPreviewSync += SyncEditorPreviewPose;
#endif
	}

	void CreateVisual()
	{
		visual = new(this, Rig, Neural, NeuralReactions, Wires, RigWireLayout);
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
		var gaitScale = SyncGaitToSpeed && (!Performance || scripted) && walking && stepTraversal == StepTraversal.None
			? MathF.Abs(Velocity.X) / WalkSpeed : 1;
		visual.PlaybackSpeed = (abilityActive ? abilityPlaybackSpeed * abilityPlaybackSpeedMultiplier :
			scripted ? scriptedPlaybackSpeed : PlaybackSpeed) * gaitScale * AnimationSpeedMultiplier;
		visual.HeadTint = RigHeadTint;
		visual.BackArmCombatEnabled = BackArmCombatEnabled;
		visual.GaitActive = walking;
		visual.Offset = stepVisualOffset + (abilityActive ? abilityVisualOffset : Vector2.Zero);
		visual.LookTarget = abilityActive ? abilityLookTarget : TracksPlayer ? Game.GetFirst<Player>()?.Position : null;
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
			return;
		}
		ruff ??= Game.Create<RigPuppetRuff>(Position, it => it.Owner = this);
		ruff.KeepOnRoomTransition = KeepOnRoomTransition;
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
		abilityActive = !Performance && !scripted && !hitReacting && !recovering && player != null &&
			UpdateAbility(player, grounded);
		if (wasAbilityActive && !abilityActive)
			ResetAbilityControl();
		walking = (!Performance || scripted) && stepTraversal != StepTraversal.None;
		if (abilityActive)
			walking = false;
		else if (scripted)
			UpdateScriptedMovement(grounded);
		else if (!Performance && stepTraversal == StepTraversal.None && player != null &&
			MathF.Abs(player.Position.X - Position.X) > 1)
		{
			var desiredFacing = player.Position.X < Position.X ? Signs.Negative : Signs.Positive;
			Facing = desiredFacing;
			if (visual.FacingMatches && grounded && !hitReacting && !recovering &&
				MathF.Abs(player.Position.X - Position.X) > PlayerStopDistance)
			{
				walking = HasGroundAhead();
				if (!walking)
					TryStartStepDown((int)Facing);
			}
		}
		if (stepTraversal == StepTraversal.None && grounded)
			Velocity.X = Calc.Approach(Velocity.X, walking ? (int)Facing * WalkSpeed : 0,
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

	protected virtual bool UpdateAbility(Player player, bool grounded) => false;
	protected virtual void InterruptAbility() { }
	protected virtual bool AbilityControlsMovement => false;
	protected virtual bool SuppressKnockdownOnHit => false;
	protected virtual bool DeathIsPersistent => false;
	protected virtual float PlayerStopDistance => StopDistance;
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
	protected virtual IReadOnlyList<HumanoidRigWireSpec> RigWireLayout => Array.Empty<HumanoidRigWireSpec>();
	protected virtual void OnFootstep(Vector2 position) { }

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
		abilityYaw = null;
		abilityYawWeight = 0;
		visual.PoseModifierWeight = 1;
		visual.PoseJostlePixels = 0;
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

	internal Game.StorySpeechBubble? Say(string text)
		=> Game.ShowSpeech(this, new Vector2(0, -44), text, Game.PlayerVoiceProfile, Color.White,
			voice: Game.PlayerVoiceName);

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
		Velocity.X = 0;
		Velocity.Y += Gravity * Time.Delta;
		base.Update();
		var grounded = Grounded();
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
			Game.HitStun(0.3f);
			Game.Shake(0.1f);
			for (var i = 0; i < 3; i++)
				Game.Create<Pop>(Position + new Point2(0, -8 - i * 9));
			if (DeathIsPersistent)
				Game.DestroyPersistently(this);
			else
				Game.DestroyForThisSession(this);
			return;
		}
		else
		{
			Game.HitStun(0.25f);
		}

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

	public override void Render(Batcher batcher)
	{
		if (ShowsHitBlink && SpriteBlinkHidden)
			return;
		if (ruff != null)
		{
			if (StraddlesPlayerDepth)
				visual.DrawInFrontOfPlayerWithAccessory(batcher, RigTint, silhouette: false, ruff);
			else
				visual.DrawWithAccessory(batcher, RigTint, silhouette: false, ruff);
		}
		else if (StraddlesPlayerDepth)
			visual?.DrawInFrontOfPlayer(batcher, RigTint, silhouette: false);
		else
			visual?.Draw(batcher, RigTint, silhouette: false);
	}

	public override void RenderShadow(Batcher batcher, Color color)
	{
		if (ruff != null)
		{
			if (StraddlesPlayerDepth)
				visual.DrawInFrontOfPlayerWithAccessory(batcher, color, silhouette: true, ruff);
			else
				visual.DrawWithAccessory(batcher, color, silhouette: true, ruff);
		}
		else if (StraddlesPlayerDepth)
			visual?.DrawInFrontOfPlayer(batcher, color, silhouette: true);
		else
			visual?.Draw(batcher, color, silhouette: true);
	}

	public override void RenderReflection(Batcher batcher, Color color) => RenderShadow(batcher, color);
	public override void RenderNormals(Batcher batcher)
	{
		if (ruff != null)
		{
			if (StraddlesPlayerDepth)
				visual.DrawNormalsInFrontOfPlayerWithAccessory(batcher, ruff);
			else
				visual.DrawNormalsWithAccessory(batcher, ruff);
		}
		else if (StraddlesPlayerDepth)
			visual?.DrawNormalsInFrontOfPlayer(batcher);
		else
			visual?.DrawNormals(batcher);
	}

	internal void RenderBehindPlayer(Batcher batcher)
	{
		if (Visible && StraddlesPlayerDepth && (!ShowsHitBlink || !SpriteBlinkHidden))
			visual?.DrawBehindPlayer(batcher, RigTint, silhouette: false);
	}

	internal void RenderShadowBehindPlayer(Batcher batcher, Color color)
	{
		if (Visible && StraddlesPlayerDepth)
			visual?.DrawBehindPlayer(batcher, color, silhouette: true);
	}

	internal void RenderNormalsBehindPlayer(Batcher batcher)
	{
		if (Visible && StraddlesPlayerDepth)
			visual?.DrawNormalsBehindPlayer(batcher);
	}

	public void PerturbNeuralTexture(NeuralTextureActor.PerturbContext context)
		=> visual?.PerturbNeuralTexture(context);

	internal HumanoidRigVisual.RuffPose RuffPose => visual.GetRuffPose();

	internal override bool OverlapsCombatBounds(in RectInt bounds)
		=> !ArticulatedCombatBounds || visual == null
			? base.OverlapsCombatBounds(bounds)
			: visual.OverlapsCombatBounds(bounds);

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
