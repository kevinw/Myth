using Foster.Framework;

namespace Myth;

// Pre-builds GPU pipelines that would otherwise compile the first time an effect appears in a
// room, turning a multi-second mid-game hitch into loading-screen time at boot. Compute
// pipelines compile eagerly when their Shader is constructed (SDL_CreateGPUComputePipeline),
// so warming them is just building the shared shader set once, up front.
//
// The steps are driven one-per-frame by Manager's boot loading screen (see UpdateBootWarmup),
// so each entry should be a self-contained chunk of work. Add new heavy effects here.
public static class ShaderWarmup
{
	public readonly record struct Step(string Label, Action<GraphicsDevice> Warm);

	public static readonly Step[] Steps =
	[
		new("Neural textures", NeuralTextureActor.WarmPipelines),
	];

	// Runs every step immediately (used by non-windowed runs — gym/headless/screenshot — that
	// have no loading screen to spread the work across).
	public static void WarmAll(GraphicsDevice graphicsDevice)
	{
		foreach (var step in Steps)
			Warm(step, graphicsDevice);
	}

	// A failed warm must never block boot — the effect will just compile lazily as before.
	public static void Warm(in Step step, GraphicsDevice graphicsDevice)
	{
		try
		{
			step.Warm(graphicsDevice);
		}
		catch (Exception e)
		{
			Console.Error.WriteLine($"[Warmup] {step.Label} failed: {e.Message}");
		}
	}
}
