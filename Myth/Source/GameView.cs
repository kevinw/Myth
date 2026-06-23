using System.Numerics;
using Foster.Framework;

namespace Myth;

// Presentation layer for Game: owns the render pipeline, lighting, shadows, normals,
// tile-field, reflections, particles and post. Holds a back-reference to the sim
// (Game) it draws. Rendering behavior is being moved here from Game.Rendering.cs via
// Rider "Move Instance Members"; sim state stays on Game and is reached through `game`.
// See memory: game-gameview-split.
public sealed class GameView
{
	internal readonly Game game;

	public GameView(Game game)
	{
		this.game = game;
	}
}
