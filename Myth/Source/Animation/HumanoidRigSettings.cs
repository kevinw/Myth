using Foster.Framework;

namespace Myth;

public readonly record struct HumanoidRigWireSpec(
	string From, string To, float Bend, float DepthOffset = 0, float WidthScale = 1);

public sealed class HumanoidRigWireSettings
{
	[I(0.5f, 4), InspectorGroup("Shape")] public float WireWidth = 1.35f;
	[I(0, 2)] public float WireOutlineWidth = 0.75f;
	[I(0, 2)] public float WireBendScale = 1;
	[I(0, 2)] public float WireMotion = 1;
	[I(0, 1)] public float WireIrregularity = 0.16f;
	[I(10, 300), InspectorGroup("Motion")] public float WireSpring = 90;
	[I(1, 40)] public float WireDamping = 14;
	[I(0, 1), InspectorGroup("Lighting")] public float WireNormalRoundness = 0.55f;
	[InspectorGroup("Palette")] public Color WireColor = new(0xb7cdd0);
	public Color WireOutlineColor = new(0x171d27);
}

public sealed class HumanoidRigSettings
{
	[InspectorGroup("Animation")] public bool SeedAnimationPhase;
	[I(0, 2)] public float SecondaryMotion;
	[I(0, 2)] public float HeadSecondaryMotion = 1;
	[I(1, 240)] public float VisualFPS = 20; // at or above the frame rate, samples once per frame

	[I(0, 1), InspectorGroup("Motion")] public float GaitAsymmetry;
	[I(0, 2)] public float PlantResponse;
	[I(0, 1)] public float TrackingLean;
	[I(0, 1)] public float VelocityLean;
	[I(0, 1)] public float SquishResponse;
	[I(0.02f, 0.5f)] public float TurnDuration = 0.28f;
	[I(0, 1)] public float TurnInertia;
	// 0 = joints spring toward the animation, smoothing away fast motion like a quick run's bob; 1 = joints ride
	// the animation and the springs only carry reactions (landings, hits, recoil).
	[I(0, 1)] public float RagdollCarry;
	// While walking, feet keep the clip's lift and only shift by the terrain under them, instead of snapping to it.
	[NoSerialize] public bool GaitFeetFollowTerrain;
	[I(0, 1)] public float EyeMotion;
	[NoSerialize] public bool TurnWhileMoving;

	[I(4, 120), InspectorGroup("Body")] public float PixelsPerMeter = 25;
	[I(-180, 180)] public float ProjectionYaw;
	[I(0.5f, 12)] public float HeadSize = 3.9f;
	[I(0, 2)] public float EyeSize = 1;
	[I(-4, 12)] public float NeckExtension;
	[I(0.5f, 12)] public float TorsoSize = 5;
	[I(0.5f, 10)] public float BicepSize = 3;
	[I(0.5f, 10)] public float ForearmSize = 2.5f;
	[I(0.5f, 12)] public float ThighSize = 4;
	[I(0, 0.75f)] public float LimbTaper;
	// 0 = each limb bone starts at its own width; 1 = it starts at the width its parent ended at.
	[I(0, 1)] public float JointContinuity = 1;
	[I(0, 3)] public float DepthColorContrast = 1;
	[I(0, 0.3f)] public float DepthWidthAmount;
	[I(0, 1), InspectorGroup("Lighting")] public float NormalRoundness;
	[I(0, 1)] public float NormalIrregularity;

	[InspectorGroup("Skin")] public RigSkinMode SkinMode = RigSkinMode.NCA;
	public ContentRef<SurfaceContent> SurfaceId = new("");
	public string HeadSpriteName = "";
	[I(0.25f, 2)] public float HeadSpriteScale = 1;
	[InspectorGroup("Costume")] public ContentRef<CostumeContent> CostumeId = new("");

	[I(0, 8), InspectorGroup("Combat")] public float CombatBoundsPadding = 1;
	[NoSerialize] public float UpperBodyHitStrength = 0.8f;

	[InspectorGroup("Palette")] public Color FarColor = new(0x435263);
	public Color NearColor = new(0xb7cdd0);
	public Color CoreColor = new(0x708b94);
	public Color OutlineColor = new(0x171d27);
	[NoSerialize] public Color AccentColor = new(0xd92240);
}

public sealed class NeuralSurfaceSettings
{
	public ContentRef<NeuralWeightsContent> NeuralWeightsFile = NeuralWeightsContent.RigDefault;
	[I(0, 1)] public float NeuralColorAmount = 0.25f;
	[I(0, 1)] public float NeuralNormalAmount = 0.55f;
	[I(1, 60)] public float NeuralFPS = 20;
	[I(0, 1)] public float NeuralFireRate = 0.5f;
	[I(0, 3)] public float NeuralContrast = 1;
	[I(0, 3)] public float NeuralSaturation = 1.1f;
	[I(-1, 1)] public float NeuralBrightness = 0.3f;
}

public class NeuralReactionSettings
{
	[I(0, 1)] public float NeuralHitStrength = 0.55f;
	[I(0, 3)] public float NeuralJostleInterval = 0.45f;
	[I(0, 1)] public float NeuralJostleStrength = 0.22f;
	[I(1, 12)] public float NeuralJostleRadius = 5;
}

public sealed class HumanoidNeuralReactionSettings : NeuralReactionSettings
{
	[I(0, 1)] public float NeuralFootContactStrength;
	[I(1, 12)] public float NeuralFootContactRadius = 4;
}
