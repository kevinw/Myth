using System.Numerics;
using Foster.Framework;

namespace Myth;

// An actor drawn by a HumanoidRigVisual: owns the visual and its settings, its render passes, combat against the drawn
// body, and the editor's animation preview. RigPuppet walks and fights; RigPuppetProp holds a pose.
public abstract class RigActor : Actor, INeuralTextureAgent
{
	[ActorSettings(typeof(HumanoidRigSettings))] public readonly HumanoidRigSettings Rig = new()
	{
		SeedAnimationPhase = true,
		SecondaryMotion = 1,
		GaitAsymmetry = 0.35f,
		PlantResponse = 0.65f,
		TrackingLean = 0.45f,
		TurnInertia = 0.6f,
		EyeMotion = 0.65f,
		Bones = { LimbTaper = 0.18f, DepthColorContrast = 1.25f, DepthWidthAmount = 0.08f },
	};
	// The NCA skin only paints bone bodies.
	[ActorSettings(typeof(NeuralSurfaceSettings), "Neural Surface", "Neural")]
	[VisibleWhen(nameof(HumanoidRigSettings.BodyStyle), RigBodyStyle.Bones), VisibleWhen(nameof(BoneBodySettings.SkinMode), RigSkinMode.NCA)]
	public readonly NeuralSurfaceSettings Neural = new();
	[ActorSettings(typeof(HumanoidNeuralReactionSettings), "Neural Reactions", "Neural")]
	[VisibleWhen(nameof(HumanoidRigSettings.BodyStyle), RigBodyStyle.Bones), VisibleWhen(nameof(BoneBodySettings.SkinMode), RigSkinMode.NCA)]
	public readonly HumanoidNeuralReactionSettings NeuralReactions = new()
	{
		NeuralFootContactStrength = 0.3f,
	};

	internal HumanoidRigVisual visual = null!;
	protected bool visualReady;
#if EDITOR
	internal bool AnimationPreviewEnabled;
	internal bool AnimationPreviewPaused;
	internal bool AnimationPreviewLooping = true;
	internal bool AnimationPreviewBlend = true;
	internal bool AnimationPreviewIK = true;
	internal int AnimationPreviewClipIndex;
	internal float AnimationPreviewSpeed = 1;
#endif

	protected static float Gravity => 360f;

	protected RigActor()
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

	// The clip the visual starts on, which also fixes its neural skin's UV layout.
	protected abstract string InitialClip { get; }
	protected virtual Color RigTint => Color.White;
	protected virtual bool ShowsHitBlink => true;
	protected virtual bool ArticulatedCombat => true;
	private protected virtual HumanoidRigVisual.DrawParts VisualParts => HumanoidRigVisual.DrawParts.All;

	public override void Added()
	{
		base.Added();
		CreateVisual();
	}

	void CreateVisual()
	{
		visual = new(this, Rig, Neural, NeuralReactions);
		SyncRigVisual();
		visual.Initialize(InitialClip);
		visualReady = true;
		VisualCreated();
	}

	protected virtual void VisualCreated() { }

	// Pushes the actor's state into the visual before it updates.
	protected virtual void SyncRigVisual() { }

	public override void Update()
	{
		if (visual == null)
			CreateVisual();
		if (Rig.VisualFPS <= 0)
			Rig.VisualFPS = 20;
#if EDITOR
		if (AnimationPreviewEnabled)
		{
			UpdateAnimationPreview();
			return;
		}
#endif
		UpdateRig();
	}

	protected abstract void UpdateRig();

	// Actor.Update (moving by Velocity), for subclasses whose own base.Update would land back here.
	protected void MoveActor() => base.Update();

	public override void Destroyed()
	{
		visual?.Dispose();
		visualReady = false;
	}

	public override void OnCollideY()
	{
		visual?.Land(Velocity.Y);
		base.OnCollideY();
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
		StopForAnimationPreview();
		AnimationPreviewEnabled = true;
		AnimationPreviewClipIndex = 0;
		for (var i = 0; i < visual.Clips.Count; i++)
			if (visual.Clips[i] == visual.ActiveClip)
			{
				AnimationPreviewClipIndex = i;
				break;
			}
		Velocity = Vector2.Zero;
		visual.ResetTurn();
		visual.ResetFeet();
	}

	// Drops whatever the actor was doing, so the preview has the visual to itself.
	protected virtual void StopForAnimationPreview() { }

	internal void EndAnimationPreview()
	{
		if (!AnimationPreviewEnabled)
			return;
		AnimationPreviewEnabled = false;
		AnimationPreviewPaused = false;
		if (visualReady)
			ResumeAfterAnimationPreview();
	}

	protected abstract void ResumeAfterAnimationPreview();

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
		AnimationPreviewSpeed = take.Speed;
		SetAnimationPreviewIK(take.FootIK);
		visual.Play(take, AnimationPreviewBlend ? 0.2f : 0);
	}

	internal void SetAnimationPreviewIK(bool enabled)
	{
		AnimationPreviewIK = enabled;
		if (!enabled)
			visual.ResetFeet();
	}

	// A body that doesn't collide (a placed prop) holds still and finds its footing by IK alone.
	void UpdateAnimationPreview()
	{
		Velocity = CollidesWithSolids ? new(0, Velocity.Y + Gravity * Time.Delta) : Vector2.Zero;
		MoveActor();
		var grounded = CollidesWithSolids ? Grounded() : AnimationPreviewIK;
		if (grounded && Velocity.Y > 0)
			Velocity.Y = 0;
		SyncPreviewVisual();
		visual.Update(grounded, AnimationPreviewIK, AnimationPreviewLooping, advance: !AnimationPreviewPaused);
	}

	internal void UpdateAnimationPreviewInEditor()
	{
		if (!AnimationPreviewEnabled)
			return;
		SyncPreviewVisual();
		visual.Update(AnimationPreviewIK, AnimationPreviewIK, AnimationPreviewLooping,
			advance: !AnimationPreviewPaused, seekGround: AnimationPreviewIK);
	}

	void SyncPreviewVisual()
	{
		SyncRigVisual();
		visual.PlaybackSpeed = AnimationPreviewSpeed;
	}
#endif

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

	public void PerturbNeuralTexture(NeuralTextureActor.PerturbContext context)
		=> visual?.PerturbNeuralTexture(context);

	internal override bool OverlapsCombatBounds(in RectInt bounds, CombatRole role = CombatRole.Vulnerable)
		=> !ArticulatedCombat || visual == null
			? base.OverlapsCombatBounds(bounds, role)
			: visual.OverlapsCombatBounds(bounds, role);

	internal override void RenderCollisionDebug(Batcher batcher)
	{
		if (ArticulatedCombat)
			visual?.DrawCombatBounds(batcher);
	}

	public override Rect GetVisualBounds()
		=> visual?.GetVisualBounds() ?? new(Position.X - 42, Position.Y - 48, 84, 58);
}
