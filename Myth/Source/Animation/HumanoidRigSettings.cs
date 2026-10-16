using Foster.Framework;

namespace Myth;

public enum RigBodyStyle { Bones, Sculpted }

public sealed class HumanoidRigSettings
{
	[InspectorGroup("Animation")] public bool SeedAnimationPhase;
	[I(0, 2)] public float SecondaryMotion;
	[I(0, 2)] public float HeadSecondaryMotion = 1;

	[I(1, 240)] public float VisualFPS = 20; // at or above the frame rate, samples once per frame
	// Stepped below the frame rate, a clip change or a turn still shows on its own frame instead of the next step.
	public bool KeyEvents;
	// 0 cross-fades clip changes. Above 0, a change shows the new clip at once and carries the old pose as an offset
	// that eases out over this long (inertialization): the new action is fully on from its first frame, without a pop.
	[I(0, 0.3f)] public float InertialBlendTime;
	// A hard landing holds its compressed contact pose this long (scaled by impact) before recovering.
	[I(0, 0.15f)] public float LandingHold;
	// Rounds the drawn joints to whole pixels, so a small figure doesn't shimmer as they move between them.
	public bool PixelSnap;

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

	// Bones draws tapered quads (BoneBodySettings); Sculpted ray-marches one SDF body (SculptBodySettings).
	[InspectorGroup("Body")] public RigBodyStyle BodyStyle;
	[I(4, 120)] public float PixelsPerMeter = 25;
	[I(-180, 180)] public float ProjectionYaw;
	// Body sizes are meters, so the figure keeps its proportions at any PixelsPerMeter; outlines stay in pixels.
	[I(0, 2)] public float EyeSize = 1;
	[I(-0.15f, 0.3f)] public float NeckExtension;
	[I(-0.1f, 0.15f)] public float ShoulderDrop;

	[InspectorGroup("Head")]
	[I(0.02f, 0.5f)] public float HeadSize = 0.156f;
	public string HeadSpriteName = "";
	[I(0.25f, 2)] public float HeadSpriteScale = 1;
	// A ray-cast 3D head (SculptedHead): jaw, brow, nose, eye sockets, mouth, with real normals. Heads under
	// SculptedHeadMinRadius pixels, or with a head sprite, keep the simple one.
	public bool SculptedHead;
	// The sculpted head's features (Sources/Heads); setting one turns the sculpted head on.
	public ContentRef<HeadShapeContent> HeadShapeId = new("");

	[ActorSettings(typeof(BoneBodySettings)), VisibleWhen(nameof(BodyStyle), RigBodyStyle.Bones)]
	public readonly BoneBodySettings Bones = new();
	// Also shades a sculpted head on a bone body.
	[ActorSettings(typeof(SculptBodySettings)), VisibleWhen(nameof(BodyStyle), RigBodyStyle.Sculpted)]
	public readonly SculptBodySettings Sculpt = new();

	[I(0, 8), InspectorGroup("Combat")] public float CombatBoundsPadding = 1;
	[NoSerialize] public float UpperBodyHitStrength = 0.8f;

	[InspectorGroup("Palette")] public Color OutlineColor = new(0x171d27);
	[NoSerialize] public Color AccentColor = new(0xd92240);

	// Shares the body settings (Bones, Sculpt) with this one.
	public HumanoidRigSettings Copy() => (HumanoidRigSettings)MemberwiseClone();
}

public sealed class BoneBodySettings
{
	[I(0.02f, 0.5f), InspectorGroup("Bones")] public float TorsoSize = 0.2f;
	[I(0.02f, 0.5f)] public float ChestSize = 0.16f;
	[I(0.02f, 0.5f)] public float NeckSize = 0.12f;
	[I(0.02f, 0.4f)] public float BicepSize = 0.12f;
	[I(0.02f, 0.4f)] public float ForearmSize = 0.1f;
	[I(0.02f, 0.5f)] public float ThighSize = 0.16f;
	[I(0.02f, 0.5f)] public float ShinSize = 0.12f;
	[I(0.02f, 0.4f)] public float FootSize = 0.08f;
	[I(0, 0.75f)] public float LimbTaper;
	[I(0, 0.75f)] public float ChestTaper;
	[I(0, 0.75f)] public float NeckTaper;
	// 0 = each limb bone starts at its own width; 1 = it starts at the width its parent ended at.
	[I(0, 1)] public float JointContinuity = 1;
	[I(0, 3)] public float DepthColorContrast = 1;
	[I(0, 0.3f)] public float DepthWidthAmount;
	[I(0, 1), InspectorGroup("Lighting")] public float NormalRoundness;
	[I(0, 1)] public float NormalIrregularity;

	[InspectorGroup("Skin")] public RigSkinMode SkinMode = RigSkinMode.NCA;
	[VisibleWhen(nameof(SkinMode), RigSkinMode.Surface)] public ContentRef<SurfaceContent> SurfaceId = new("");
	[InspectorGroup("Costume")] public ContentRef<CostumeContent> CostumeId = new("");

	[InspectorGroup("Palette")] public Color FarColor = new(0x435263);
	public Color NearColor = new(0xb7cdd0);
	public Color CoreColor = new(0x708b94);
	// Silhouette: one rim around the whole figure; inside it, only limbs in front of the body get an outline --
	// translucent black, so it reads as a darker shade of whatever they cross.
	public RigOutlineMode OutlineMode = RigOutlineMode.PerPiece;
	[I(0, 1), VisibleWhen(nameof(OutlineMode), RigOutlineMode.Silhouette)] public float FrontLimbOutline = 0.35f;
	[I(0, 0.3f), VisibleWhen(nameof(OutlineMode), RigOutlineMode.Silhouette)] public float FrontLimbDepth = 0.05f; // meters nearer than the pelvis
}

// The body as one ray-marched SDF (SculptedBody): fleshy masses over metal joints, instead of drawn bones.
public sealed class SculptBodySettings
{
	[InspectorGroup("Sculpted Body")] public BodyShadeMode BodyShading;
	// Each part shades from its own color toward its color times BodyShadow, more so on the side away from the viewer
	// (BodyDepthShade). Metallic skin shades by what it reflects and tints its highlights, which come from the nearest
	// scene lights.
	public Color BodyShadow = new(0x705c8c);
	[I(0.3f, 3, Knob = true)] public float BodyBulk = 1;
	[I(0, 0.3f, Knob = true)] public float FleshBlend = 0.06f; // meters of smooth union between fleshy pieces
	[I(0, 3, Knob = true)] public float ChestMass = 1;
	[I(0, 3, Knob = true)] public float Abs = 1;
	[I(0.3f, 2, Knob = true)] public float HipSize = 1;
	[I(0, 2, Knob = true)] public float Glutes = 1;
	[I(0, 2, Knob = true)] public float Codpiece = 1;
	[I(0, 2, Knob = true)] public float Claws = 1; // length; 0 leaves plain hands
	[I(0.3f, 2, Knob = true)] public float BootSize = 1;
	[I(0, 0.15f, Knob = true)] public float HeelLength = 0.04f; // meters the heel reaches back past the ankle
	[I(0, 1, Knob = true)] public float BootHeight = 0.3f; // how far up the shin the shaft rises
	[I(0, 1, Knob = true)] public float SkinMetallic;
	[I(0, 1, Knob = true)] public float SkinRoughness = 0.5f;
	[I(0, 1, Knob = true)] public float MetalRoughness = 0.3f;
	[I(0, 3, Knob = true)] public float Specular = 1;
	[I(0, 1, Knob = true)] public float BodyDepthShade = 0.35f;
	[InspectorGroup("Sculpted Body Colors")] public Color HeadColor = new(0xe3ad98);
	public Color TorsoColor = new(0xe3ad98);
	public Color ChestColor = new(0xe3ad98);
	public Color AbsColor = new(0xe3ad98);
	public Color ArmColor = new(0xe3ad98);
	public Color LegColor = new(0xe3ad98);
	public Color CalfColor = new(0xe3ad98);
	public Color JointColor = new(0xd8c08a);
	public Color ForearmColor = new(0xd8c08a);
	public Color ShinColor = new(0xd8c08a);
	public Color HandColor = new(0xd8c08a);
	public Color FootColor = new(0xd8c08a);
	public Color ClawColor = new(0xe8e0c8);
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
