using Foster.Framework;

namespace Myth;

// Owns the hidden simulation actor used by rig skins. Authored settings stay on the visible
// actor; this is the single boundary that translates them to NeuralTextureActor runtime state.
sealed class NeuralSkinRuntime(Actor owner, NeuralSurfaceSettings settings, Point2 size, Point2 origin, int prewarmSteps)
	: IDisposable
{
	NeuralTextureActor? texture;
	ContentRef<NeuralWeightsContent> activeWeightsFile = new("");

	public NeuralTextureActor? Texture => texture;
	public Texture? OutputTexture => texture?.OutputTexture;
	public Texture? OutputNormalTexture => texture?.OutputNormalTexture;

	public void Initialize(RigSkinMode mode)
	{
		Create(mode);
	}

	public void Update(RigSkinMode mode)
	{
		if (texture != null && activeWeightsFile != settings.NeuralWeightsFile)
		{
			owner.Game.Destroy(texture);
			texture = null;
			Create(mode);
		}
		Sync(mode);
	}

	void Create(RigSkinMode mode)
	{
		activeWeightsFile = settings.NeuralWeightsFile;
		texture = owner.Game.Create<NeuralTextureActor>(owner.Position + origin, it =>
		{
			it.AutoPerturb = false;
			it.Visible = false;
			it.Size = size;
			it.SimulationSize = size;
			it.WeightsFile = activeWeightsFile.Name;
			it.NearestStateSampling = true;
			it.EdgeTrim = 1;
		});
		Sync(mode);
		texture.SimulateFromSeed(prewarmSteps);
	}

	void Sync(RigSkinMode mode)
	{
		if (texture == null)
			return;
		texture.Position = owner.Position + origin;
		texture.KeepOnRoomTransition = owner.KeepOnRoomTransition;
		texture.StepsPerSecond = mode == RigSkinMode.NCA ? settings.NeuralFPS : 0;
		texture.FireRate = settings.NeuralFireRate;
		texture.Contrast = settings.NeuralContrast;
		texture.Saturation = settings.NeuralSaturation;
		texture.Brightness = settings.NeuralBrightness;
	}

	public void Dispose()
	{
		if (texture != null && !owner.Game.destroying.Contains(texture))
			owner.Game.Destroy(texture);
		texture = null;
	}
}
