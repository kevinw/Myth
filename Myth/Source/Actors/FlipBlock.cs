using System.Numerics;
using Foster.Framework;
using Kit.Patterns;

namespace Myth;

// Tiles that phase between solid and ghost on a pattern. "1 0" alternates every half cycle,
// "1 0 1 1" limps, "1@3 0" stays up three times as long as it is down, and two blocks running
// "{1 0}" against "{1 0 0}" drift in and out of step for a long, readable rhythm. The block
// brightens as its next flip approaches, so the timing can be read before it lands.
[GameActor(Tags = ActorTag.Obstacle, Sprite = "Editor/moving_platform", Icon = "wave-square",
	Anchor = SpriteAnchor.TopLeft, LivePositionEditable = true, LiveResizable = true)]
class FlipBlock : Actor, IResizable, IShapeOccluder, IDualGridRect
{
	public Point2 Size { get; set; } = DefaultSize;
	public static readonly Point2 DefaultSize = new(Game.TileSize * 3, Game.TileSize);
	[Multiline(2)] public string Phases = "1 0";
	[I(0.05f, 4)] public float CyclesPerSecond = 0.5f;
	[I(0, 1)] public float Telegraph = 0.4f;
	public string Material = "metal-dual";

	static float GhostAlpha => 0.3f.T();
	static Color WarningColor => new(0xff9d5c);
	static Color GhostGlowColor => new(0x79cfff);

	Point2 IDualGridRect.Position => Position;
	bool IDualGridRect.Visible => Visible;
	string IDualGridRect.Material => Material;
	// Authored in place, so its tile variants and dither come from where it sits.
	public Point2 VariantTileOrigin => Position / Game.TileSize;
	public Vector2 DitherAnchorOffset => Vector2.Zero;

	readonly PatternClock clock = new();
	readonly List<Hap<Value>> haps = [];
	Pattern<Value> pattern = Pattern.Silence<Value>();
	string parsed = "";
	bool solid = true;
	float charge;

	public FlipBlock()
	{
		Mask = Masks.Solid;
		CollidesWithSolids = false;
	}

	public override void Added()
	{
		base.Added();
		// Size is applied from the spec before Added, like MovingPlatform's.
		Hitbox = new Hitbox(new RectInt(0, 0, Size.X, Size.Y));
	}

	public override void Update()
	{
		base.Update();
		ParsePhases();
		clock.Rate = CyclesPerSecond;
		clock.Advance(Time.Delta);
		SetSolid(SolidNow());
		charge = ChargeToFlip();
	}

	// The last valid pattern keeps running while the text is half-typed.
	void ParsePhases()
	{
		if (parsed == Phases)
			return;
		parsed = Phases;
		try
		{
			pattern = Mini.Parse(Phases);
		}
		catch (PatternSyntaxException error)
		{
			Game.DisplayMessage($"phases: {error.Message}");
		}
	}

	bool SolidNow()
	{
		haps.Clear();
		clock.ValuesNow(pattern, haps);
		foreach (var hap in haps)
			if (hap.Value.IsTruthy)
				return true;
		return false;
	}

	// The block always flips on time, so its rhythm stays readable. Anyone caught inside is pushed
	// out by Pawn's per-frame TryUnstick, which prefers upwards.
	void SetSolid(bool wanted)
	{
		if (wanted == solid)
			return;
		solid = wanted;
		Mask = solid ? Masks.Solid : Masks.None;
		AudioEvent(solid ? "tile_break" : "bubble");
	}

	// How near the next flip is, as 0..1 over the Telegraph seconds before it.
	float ChargeToFlip()
	{
		if (Telegraph <= 0)
			return 0;
		var window = Telegraph * clock.Rate;
		haps.Clear();
		pattern.Query(new Arc(clock.Position, clock.Position + Fraction.FromDouble(window)), haps);
		var nearest = double.MaxValue;
		foreach (var hap in haps)
			if (hap.HasOnset && hap.Value.IsTruthy != solid)
				nearest = Math.Min(nearest, (hap.WholeOrPart.Begin - clock.Position).ToDouble());
		return nearest == double.MaxValue ? 0 : (float)(1 - nearest / window);
	}

	public override void Render(Batcher batcher)
		=> RenderMaterial(batcher, normals: false, solid
			? Color.Lerp(Color.White, WarningColor, charge)
			: Color.White * (GhostAlpha + charge * 0.2f));

	public override void RenderNormals(Batcher batcher)
	{
		if (solid)
			RenderMaterial(batcher, normals: true, Color.White);
	}

	public override void RenderShadow(Batcher batcher, Color color)
	{
		if (solid)
			RenderMaterial(batcher, normals: false, color);
	}

	// A ghosted block glows as it is about to return, so the gap you are standing in announces itself.
	public override void RenderEmissive(Batcher batcher)
	{
		//if (!solid && charge > 0)
			//batcher.Rect(new Rect(0, 0, Size.X, Size.Y), GhostGlowColor * (charge * 0.5f));
	}

	void RenderMaterial(Batcher batcher, bool normals, Color color) => this.Render(batcher, normals, color);

	// Only a solid block casts a shadow, occludes light, dithers into the tile field or reflects.
	public bool CastsShadow => Visible && solid;
	public void AppendOccluderSegments(List<ShadowSegment> segments) => this.AppendOccluders(segments);
	public override bool CastsReflection => solid;
	public override void RenderReflection(Batcher batcher, Color color) => RenderMaterial(batcher, normals: false, color);

	// Spriteless, so the sprite-based default is a zero rect; report the real footprint.
	public override Rect GetVisualBounds() => new(Position.X, Position.Y, Size.X, Size.Y);
}
