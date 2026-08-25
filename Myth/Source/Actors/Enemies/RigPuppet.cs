using System.Numerics;
using Foster.Framework;

namespace Myth;

[GameActor(Sprite = "player", Tags = ActorTag.Enemy, BoundsX = -12, BoundsY = -40, BoundsW = 24, BoundsH = 40,
	LivePositionEditable = true)]
public class RigPuppet : Actor, INeuralTextureAgent
{
	enum StepTraversal { None, Up, Down }

	[InspectorGroup("Animation")] public string AnimationName = "Zombie_Walk_Fwd_Loop";
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
	public bool ArticulatedCombatBounds;
	public bool StraddlesPlayerDepth;
	[I(1, 20)] public int Health = 8;

	HumanoidRigVisual visual = null!;
	RigPuppetBackLegRenderer? backLegRenderer;
	StepTraversal stepTraversal;
	Vector2 stepStartOffset;
	Vector2 stepVisualOffset;
	float stepTime;
	float stepDuration;
	bool hitReacting;
	bool recovering;
	bool walking;
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
	}

	void CreateVisual()
	{
		visual = new(this, Rig, Neural, NeuralReactions);
		SyncVisual();
		visual.Initialize(Performance ? AnimationName : "Zombie_Idle_Loop");
	}

	void SyncVisual()
	{
		var gaitScale = SyncGaitToSpeed && !Performance && walking && stepTraversal == StepTraversal.None
			? MathF.Abs(Velocity.X) / WalkSpeed : 1;
		visual.PlaybackSpeed = PlaybackSpeed * gaitScale;
		visual.GaitActive = walking;
		visual.Offset = stepVisualOffset;
		visual.LookTarget = Game.GetFirst<Player>()?.Position;
		SyncBackLegRenderer();
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

	string LocomotionName()
		=> visual.TryGetClip(AnimationName, out _) ? AnimationName : "Zombie_Walk_Fwd_Loop";

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
		if (stepTraversal == StepTraversal.None)
			Velocity.Y += Gravity * Time.Delta;
		else
			Velocity = Vector2.Zero;
		base.Update();
		var grounded = Grounded();
		if (grounded && Velocity.Y > 0)
			Velocity.Y = 0;

		walking = !Performance && stepTraversal != StepTraversal.None;
		if (!Performance && stepTraversal == StepTraversal.None && Game.GetFirst<Player>() is { } player &&
			MathF.Abs(player.Position.X - Position.X) > 1)
		{
			var desiredFacing = player.Position.X < Position.X ? Signs.Negative : Signs.Positive;
			Facing = desiredFacing;
			if (visual.FacingMatches && grounded && !hitReacting && !recovering &&
				MathF.Abs(player.Position.X - Position.X) > StopDistance)
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

		if (hitReacting && grounded && visual.Time >= visual.ActiveClip.Duration)
		{
			hitReacting = false;
			recovering = true;
			visual.Play("LayToIdle");
		}
		else if (recovering && visual.Time >= visual.ActiveClip.Duration)
		{
			recovering = false;
			visual.Play(Performance ? LocomotionName() : "Zombie_Idle_Loop", seededPhase: Rig.SeedAnimationPhase);
		}
		else if (!hitReacting && !recovering)
			visual.Play(Performance || walking ? LocomotionName() : "Zombie_Idle_Loop", seededPhase: Rig.SeedAnimationPhase);

		SyncVisual();
		visual.Update(grounded, !Performance && !hitReacting && !recovering && stepTraversal == StepTraversal.None,
			loop: !hitReacting && !recovering);
	}

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

	internal void BeginAnimationPreview()
	{
		if (AnimationPreviewEnabled)
			return;
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
		visual.Play("Zombie_Idle_Loop");
	}

	internal void PreviewAnimation(int index)
	{
		AnimationPreviewClipIndex = Math.Clamp(index, 0, visual.Clips.Count - 1);
		visual.Play(AnimationPreviewClip.Name, AnimationPreviewBlend, restart: true);
		visual.ResetFeet();
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
		visual.Play(LocomotionName(), seededPhase: Rig.SeedAnimationPhase);
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
		visual?.Dispose();
		if (backLegRenderer != null)
			Game.Destroy(backLegRenderer);
		backLegRenderer = null;
	}

	public override void OnWasHit(Actor by)
	{
#if EDITOR
		if (AnimationPreviewEnabled)
			return;
#endif
		if (Health <= 0)
			return;
		Health--;
		if (Health <= 0)
		{
			for (var i = 0; i < 3; i++)
				Game.Create<Pop>(Position + new Point2(0, -8 - i * 9));
			Game.DestroyForThisSession(this);
			return;
		}

		var away = Position.X < by.Position.X ? -1f : 1f;
		visual.Hit(away);
		Velocity = new Vector2(away * HitKnockbackSpeed, -HitLiftSpeed);
		stepTraversal = StepTraversal.None;
		visual.ResetFeet();
		visual.Play("Hit_Knockback", blend: false, restart: true);
		hitReacting = true;
		recovering = false;
	}

	public override void Render(Batcher batcher)
	{
		if (!SpriteBlinkHidden)
		{
			if (StraddlesPlayerDepth)
				visual?.DrawInFrontOfPlayer(batcher, Color.White, silhouette: false);
			else
				visual?.Draw(batcher, Color.White, silhouette: false);
		}
	}

	public override void RenderShadow(Batcher batcher, Color color)
	{
		if (StraddlesPlayerDepth)
			visual?.DrawInFrontOfPlayer(batcher, color, silhouette: true);
		else
			visual?.Draw(batcher, color, silhouette: true);
	}

	public override void RenderReflection(Batcher batcher, Color color) => RenderShadow(batcher, color);
	public override void RenderNormals(Batcher batcher)
	{
		if (StraddlesPlayerDepth)
			visual?.DrawNormalsInFrontOfPlayer(batcher);
		else
			visual?.DrawNormals(batcher);
	}

	internal void RenderBehindPlayer(Batcher batcher)
	{
		if (Visible && StraddlesPlayerDepth && !SpriteBlinkHidden)
			visual?.DrawBehindPlayer(batcher, Color.White, silhouette: false);
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

	internal override bool OverlapsCombatBounds(in RectInt bounds)
		=> !ArticulatedCombatBounds || visual == null
			? base.OverlapsCombatBounds(bounds)
			: visual.OverlapsCombatBounds(bounds);

	internal override void RenderCollisionDebug(Batcher batcher)
	{
		if (ArticulatedCombatBounds)
			visual?.DrawCombatBounds(batcher);
	}

	public override Rect GetVisualBounds() => new(Position.X - 42, Position.Y - 48, 84, 58);
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
