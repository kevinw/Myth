using Foster.Framework;

namespace Myth;

public enum NeuralTexturePerturbMode
{
	Disabled,
	Pawns,
}

public enum NeuralTexturePerturbEffect
{
	Erase,
	Wake,
}

[GameActor(Sprite = "Editor/neural_texture_perturb", BoundsW = Game.TileSize * 4, BoundsH = Game.TileSize * 3,
	Anchor = SpriteAnchor.TopLeft, Tags = ActorTag.System, LivePositionEditable = true)]
public class NeuralTexturePerturbActor : Actor, IResizable
{
	public static readonly Point2 DefaultSize = new(Game.Width, Game.Height);

	public Point2 Size { get; set; } = DefaultSize;
	public NeuralTexturePerturbMode Mode = NeuralTexturePerturbMode.Pawns;
	public NeuralTexturePerturbEffect Effect;
	public bool DisturbStationary = true;
	public bool ScaleStrengthWithSpeed = true;
	[I(0, 10)] public float Strength = 6f;
	[I(20, 240)] public float SpeedForDoubleStrength = 100f;
	[I(1, 32)] public float Radius = 11.95f;
	[I(0, 1)] public float WakeEraseStrength = 0.03f;
	[I(0, 2)] public float WakePush = 0.8f;

	public RectInt WorldRect => new(Position, Size);

	public NeuralTexturePerturbActor()
	{
		Visible = false;
		CollidesWithSolids = false;
		Hitbox = new(new RectInt(0, 0, 0, 0));
	}
}
