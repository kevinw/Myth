using Foster.Framework;

namespace Myth;

public enum RigPuppetPropLayer
{
	Background,
	Foreground,
}

[GameActor(Sprite = "player", Tags = ActorTag.Decoration, BoundsX = -42, BoundsY = -48,
	BoundsW = 84, BoundsH = 58, LivePositionEditable = true)]
public sealed class RigPuppetProp : RigPuppet
{
	[InspectorGroup("Pose")] public string PoseAnimationName = "Zombie_Idle_Loop";
	[I(0, 1)] public float PoseProgress;
	public bool PoseFootIK;
	[InspectorGroup("Placement")] public RigPuppetPropLayer Layer;
	[VisibleWhen(nameof(Layer), RigPuppetPropLayer.Foreground)]
	public bool Reactive;
	[VisibleWhen(nameof(Layer), RigPuppetPropLayer.Foreground)]
	[I(0, 2)] public float JostleStrength = 1;
	[VisibleWhen(nameof(Layer), RigPuppetPropLayer.Background)]
	[I(0, 1)] public float BackgroundBrightness = 0.55f;
	public Color Tint = Color.White;
	[VisibleWhen(nameof(Layer), RigPuppetPropLayer.Foreground)]
	public bool CastShadow = true;
	[VisibleWhen(nameof(Layer), RigPuppetPropLayer.Foreground)]
	public bool Reflect = true;

	protected override bool UsesFrozenPose => true;
	protected override string FrozenPoseAnimationName => PoseAnimationName;
	protected override float FrozenPoseProgress => PoseProgress;
	protected override bool FrozenPoseFootIK => PoseFootIK;
	protected override float HitLimbReactionStrength => JostleStrength;
	protected override Color RigTint => Layer == RigPuppetPropLayer.Background ? Darken(Tint, BackgroundBrightness) : Tint;
	protected override bool TracksPlayer => false;
	protected override bool ShowsHitBlink => false;

	public override bool CastsSpriteShadow => Layer == RigPuppetPropLayer.Foreground && CastShadow;
	public override bool CastsReflection => Layer == RigPuppetPropLayer.Foreground && Reflect;
	public override bool LightingDithered => Layer == RigPuppetPropLayer.Background;

	public RigPuppetProp()
	{
		Performance = true;
		ArticulatedCombatBounds = true;
		CollidesWithSolids = false;
		Rig.SeedAnimationPhase = false;
		Rig.SecondaryMotion = 0;
		Rig.HeadSecondaryMotion = 0;
		Rig.TrackingLean = 0;
		Rig.EyeMotion = 0;
		NeuralReactions.NeuralJostleStrength = 0;
		SyncPlacement();
	}

	public override void Update()
	{
		SyncPlacement();
		base.Update();
	}

	void SyncPlacement()
	{
		Depth = Layer == RigPuppetPropLayer.Background ? Depths.BackgroundProp : Depths.Enemy;
		Mask = Layer == RigPuppetPropLayer.Foreground && Reactive ? Masks.Destructible : Masks.None;
	}

	static Color Darken(Color color, float amount) => new(
		(byte)(color.R * amount), (byte)(color.G * amount), (byte)(color.B * amount), color.A);
}
