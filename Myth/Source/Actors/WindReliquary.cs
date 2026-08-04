using System.Numerics;
using Foster.Framework;

namespace Myth;

// A hanging pipe shrine: the Aeolian area's recurring background silhouette.
[GameActor(Sprite = "Editor/particles", Anchor = SpriteAnchor.TopLeft, Tags = ActorTag.Decoration, LivePositionEditable = true)]
public class WindReliquary : Actor, IResizable
{
	public static readonly Point2 DefaultSize = new(64, 80);

	public Point2 Size { get; set; } = DefaultSize;
	public Color MetalColor = new(0x8f765d);
	public Color GlowColor = new(0xa7ecff);
	public float Resonance = 1f;

	public WindReliquary()
	{
		Sprite = null;
		CollidesWithSolids = false;
		Depth = 7;
	}

	public override bool CastsSpriteShadow => false;
	public override bool CastsReflection => false;

	float Pulse => (0.55f + MathF.Sin(Time.SecondsF * 1.7f + Position.X * 0.03f) * 0.15f) * Resonance;

	public override void Render(Batcher batcher)
	{
		if (Size.X < 16 || Size.Y < 24)
			return;

		var center = new Vector2(Size.X / 2f, Size.Y * 0.27f);
		var haloRadius = MathF.Min(Size.X * 0.34f, Size.Y * 0.22f);
		var sway = MathF.Sin(Time.SecondsF * 0.9f + Position.Y * 0.02f) * 1.5f * Resonance;
		var metal = MetalColor * 0.72f;

		batcher.CircleLine(center, haloRadius, 2, 24, metal * 0.55f);
		batcher.Line(new Vector2(center.X, 0), new Vector2(center.X, center.Y - haloRadius), 2, metal);
		batcher.Line(new Vector2(center.X - haloRadius, center.Y), new Vector2(center.X + haloRadius, center.Y), 1, metal * 0.7f);

		const int pipeCount = 7;
		var spacing = Size.X / (pipeCount + 1f);
		for (var i = 0; i < pipeCount; i++)
		{
			var x = spacing * (i + 1);
			var distance = MathF.Abs(i - (pipeCount - 1) / 2f) / ((pipeCount - 1) / 2f);
			var top = center.Y + haloRadius * (0.35f + distance * 0.35f);
			var bottom = Size.Y - 5 - distance * Size.Y * 0.2f;
			var offset = sway * (0.35f + i * 0.11f);
			batcher.Line(new Vector2(x, top), new Vector2(x + offset, bottom), 2, metal);
			batcher.Line(new Vector2(x + offset - 2, bottom), new Vector2(x + offset + 2, bottom), 1, MetalColor);
		}
	}

	public override void RenderEmissive(Batcher batcher)
	{
		var center = new Vector2(Size.X / 2f, Size.Y * 0.27f);
		var radius = MathF.Min(Size.X * 0.34f, Size.Y * 0.22f);
		batcher.Circle(center, radius, 24, GlowColor * Pulse * 0.34f);
	}

	public override void RenderNormals(Batcher batcher) { }
	public override void RenderShadow(Batcher batcher, Color color) { }
	public override void RenderReflection(Batcher batcher, Color color) { }

	public override void ForEachLight(Action<Vector2, float, float, float, Color> action)
	{
		if (Resonance > 0)
			action((Vector2)Position + new Vector2(Size.X / 2f, Size.Y * 0.27f), MathF.Max(Size.X, Size.Y) * 0.55f, Pulse * 0.18f, 8f, GlowColor);
	}

	public override Rect GetVisualBounds() => new(Position, Size);
}
