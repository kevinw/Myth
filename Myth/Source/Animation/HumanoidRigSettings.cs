using Foster.Framework;

namespace Myth;

public sealed class HumanoidRigSettings
{
	[InspectorGroup("Animation")] public bool SeedAnimationPhase;
	[I(0, 2)] public float SecondaryMotion;
	[I(1, 60)] public float VisualFPS = 20;

	[I(0, 1), InspectorGroup("Motion")] public float GaitAsymmetry;
	[I(0, 2)] public float PlantResponse;
	[I(0, 1)] public float TrackingLean;
	[I(0, 1)] public float TurnInertia;
	[I(0, 1)] public float EyeMotion;
	[NoSerialize] public bool TurnWhileMoving;

	[I(12, 60), InspectorGroup("Body")] public float PixelsPerMeter = 25;
	[I(-180, 180)] public float ProjectionYaw;
	[I(2, 12)] public float HeadSize = 3.9f;
	[I(2, 12)] public float TorsoSize = 5;
	[I(1, 10)] public float BicepSize = 3;
	[I(1, 10)] public float ForearmSize = 2.5f;
	[I(1, 12)] public float ThighSize = 4;
	[I(0, 0.75f)] public float LimbTaper;
	[I(0, 3)] public float DepthColorContrast = 1;
	[I(0, 0.3f)] public float DepthWidthAmount;

	[InspectorGroup("Skin")] public RigSkinMode SkinMode = RigSkinMode.NCA;
	public ContentRef<SurfaceContent> SurfaceId = new("");

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
