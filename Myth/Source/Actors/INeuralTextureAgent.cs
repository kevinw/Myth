namespace Myth;

// Implemented by actors that want to disturb the neural textures they overlap. Every
// NeuralTextureActor calls this once per simulation step for each agent in the room, so an
// agent may be invoked once per texture; cull against context.Texture bounds to ignore
// textures elsewhere. Add disturbances in world space through the context, which maps them
// into the texture's state grid.
public interface INeuralTextureAgent
{
	void PerturbNeuralTexture(NeuralTextureActor.PerturbContext context);
}
