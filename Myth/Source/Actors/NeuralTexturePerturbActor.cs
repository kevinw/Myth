using Foster.Framework;

namespace Myth;

public enum NeuralTexturePerturbMode
{
	Disabled,
	Pawns,
}

[GameActor(Sprite = "Editor/neural_texture_perturb", BoundsW = Game.TileSize * 4, BoundsH = Game.TileSize * 3, Anchor = SpriteAnchor.TopLeft, Tags = ActorTag.System)]
public class NeuralTexturePerturbActor : Actor, IResizable
{
	public static readonly Point2 DefaultSize = new(Game.Width, Game.Height);

	public Point2 Size { get; set; } = DefaultSize;
	public NeuralTexturePerturbMode Mode = NeuralTexturePerturbMode.Pawns;
	public bool DisturbStationary = true;
	public bool ScaleStrengthWithSpeed = true;
	public float Strength = 0.35f;
	public float SpeedForDoubleStrength = 100f;
	public float Radius = 6f;

	public RectInt WorldRect => new(Position, Size);

	public NeuralTexturePerturbActor()
	{
		Visible = false;
		CollidesWithSolids = false;
		Hitbox = new(new RectInt(0, 0, 0, 0));
	}
}
