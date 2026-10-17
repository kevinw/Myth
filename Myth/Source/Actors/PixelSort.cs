using System.Numerics;
using Foster.Framework;
using Kit;

namespace Myth;

// A pixel sort that never finishes, over one screen-sized field anchored to the world. Each frame
// runs a few passes of a real sort while displaced pixels relax home. What it sorts is where each
// pixel looks, not its colour, so torches, the player and anything else animating keep moving
// through it.
//
// Nothing sorts unless something stirs it: every frame each actor may draw strokes into the
// field (Actor.StirPixelSort), and a stroke says how that patch sorts. Regions stir a rectangle
// that breathes; a ghosted FlipBlock stirs its own tiles; a burrow churns its path. The shared
// meaning: sorted is not-quite-real.

// Background sorts the layer behind the playfield before anything is lit or drawn over it; Frame
// sorts the finished, lit picture -- characters and all.
public enum PixelSortLayer { Background, Frame }
// The way the bright end of each streak runs.
public enum PixelSortDirection { Right, Left, Down, Up }

public record struct PixelSortStyle(
	PixelSortLayer Layer,
	PixelSortDirection Direction,
	// Chance per pass that a pair may swap: the speed of the sort, and 0 stirs nothing.
	float Gate,
	// Only pixels whose brightness falls in this band take part; the rest hold still and break
	// the streaks, so the art's own edges decide where a streak starts and stops.
	float BandLow = 0,
	float BandHigh = 1,
	// The longest distance a pixel may be carried from home: the streak length.
	float Reach = 24,
	// Pixels per second the displaced ones slide home.
	float RelaxRate = 40,
	int Steps = 6,
	// Fraction of a line stroke's half-width whose gate ramps away to nothing at the outer edge.
	// Gate is a per-pass swap chance, so the ramp dithers itself: the sort crumbles out into the
	// untouched picture instead of ending on a straight edge.
	float Feather = 0);

public sealed class PixelSortCanvas
{
	internal readonly record struct Stroke(Rect Rect, Vector2 From, Vector2 To, float Width, PixelSortStyle Style)
	{
		public bool IsLine => Width > 0;
	}

	internal readonly List<Stroke>[] Strokes = [[], []];

	public void Rect(in Rect rect, in PixelSortStyle style)
	{
		if (style.Gate > 0)
			Strokes[(int)style.Layer].Add(new(rect, default, default, 0, style));
	}

	public void Line(Vector2 from, Vector2 to, float width, in PixelSortStyle style)
	{
		if (style.Gate > 0)
			Strokes[(int)style.Layer].Add(new(default, from, to, width, style));
	}

	internal void Clear()
	{
		foreach (var strokes in Strokes)
			strokes.Clear();
	}
}

// Where something has been lately, stirred as a line that fades: fresh stretches hold their sort,
// older ones are let go and relax home, so the streak shrinks back toward whoever left it.
public sealed class PixelSortTrail(int capacity = 64)
{
	// A jump longer than this -- a teleport, a tunnel, a room change -- breaks the trail rather
	// than stirring a line across everything in between.
	const float MaxGap = 24;

	readonly (Vector2 At, float Time, PixelSortDirection Direction, float Strength)[] samples =
		new (Vector2, float, PixelSortDirection, float)[capacity];
	int head, count;
	float clock;

	public void Advance(float dt) => clock += dt;

	public void Record(Vector2 at, PixelSortDirection direction, float strength = 1, float spacing = 1)
	{
		if (count > 0 && Vector2.DistanceSquared(samples[head].At, at) < spacing * spacing)
			return;
		head = (head + 1) % samples.Length;
		samples[head] = (at, clock, direction, strength);
		count = Math.Min(count + 1, samples.Length);
	}

	// style.RelaxRate is how fast a fully faded stretch lets go.
	public void Stir(PixelSortCanvas canvas, float life, float width, in PixelSortStyle style)
	{
		for (var i = 1; i < count; i++)
		{
			var newer = samples[(head - i + 1 + samples.Length) % samples.Length];
			var older = samples[(head - i + samples.Length) % samples.Length];
			var fade = 1 - (clock - older.Time) / life;
			if (fade <= 0)
				break;
			if (Vector2.DistanceSquared(older.At, newer.At) > MaxGap * MaxGap)
				continue;
			canvas.Line(older.At, newer.At, width, style with
			{
				Direction = newer.Direction,
				Gate = style.Gate * fade * newer.Strength,
				RelaxRate = style.RelaxRate * (1 - fade),
			});
		}
	}
}

// An authored rectangle of the world that pixel-sorts itself and breathes: inhale and the streaks
// grow along Direction, exhale and they melt back into the scene.
[GameActor(Icon = "wand-magic-sparkles", BoundsW = Game.TileSize * 6, BoundsH = Game.TileSize * 4,
	LivePositionEditable = true, LiveResizable = true, ShowFacing = false)]
public class PixelSortRegion : Actor, IResizable
{
	public static readonly Point2 DefaultSize = new(Game.TileSize * 6, Game.TileSize * 4);
	public Point2 Size { get; set; } = DefaultSize;

	[InspectorGroup("Pixel Sort")] public PixelSortLayer Layer = PixelSortLayer.Background;
	public PixelSortDirection Direction = PixelSortDirection.Down;
	[I(0, 1)] public float BandLow = 0.25f;
	[I(0, 1)] public float BandHigh = 1f;
	[I(1, 128)] public float Reach = 24;
	[I(1, 16)] public int StepsPerFrame = 6;
	// Scales everything; drive it from code for a moment that surges and fades.
	[I(0, 1)] public float Strength = 1;

	[InspectorGroup("Breath")] [I(0.5f, 30)] public float BreathPeriod = 7;
	// How far it exhales: 0 holds a steady sort, 1 lets it relax all the way home.
	[I(0, 1)] public float BreathDepth = 0.8f;
	[I(0, 1)] public float BreathPhase;
	// Pixels per second the displaced ones slide home at the bottom of an exhale.
	[I(0, 200)] public float RelaxRate = 40;

	// 0..1, where it is in its breath: public so sound, light or creatures can follow along.
	[NoSerialize] public float Breath { get; private set; } = 1;
	float life;

	public PixelSortRegion()
	{
		Visible = false;
		CollidesWithSolids = false;
		Hitbox = new(new RectInt(0, 0, 0, 0));
	}

	public override void Update()
	{
		base.Update();
		life += Time.Delta;
		var wave = 0.5f - 0.5f * MathF.Cos(MathF.Tau * (life / BreathPeriod + BreathPhase));
		// A little noise on top so it breathes like something alive rather than a metronome.
		wave = Math.Clamp(wave + 0.15f * SmoothNoise.Signed(life * 0.6f, (uint)Position.GetHashCode()), 0, 1);
		Breath = 1 - BreathDepth * (1 - wave);
	}

	public override void StirPixelSort(PixelSortCanvas canvas)
		=> canvas.Rect(new RectInt(Position, Size), new(Layer, Direction, Strength * Breath,
			BandLow, BandHigh, Reach, RelaxRate * (1 - Breath), StepsPerFrame));
}

public sealed partial class GameView
{
	sealed class PixelSortField
	{
		public Target? Front, Back, StirA, StirB;
		public Point2 Origin;
		public int Pass;
		public bool Live;
		public float Idle;
	}

	// Once nothing has stirred a field for this long, everything in it has relaxed home and it
	// stops running.
	static float PixelSortIdleSeconds => 3f;
	// How fast pixels outside any stroke -- a wake the player has left behind -- slide home.
	static float PixelSortHomeRate => 60f;

	// `pixelsort_stir` in the console: paints each layer's gate field back over the scene in red,
	// so you can see the strokes themselves -- their shape, their feathered edges and where they
	// overlap -- instead of inferring them from the streaks they leave.
	internal static bool ShowPixelSortStir;

	readonly PixelSortCanvas pixelSortCanvas = new();
	readonly PixelSortField[] pixelSortFields = [new(), new()];

	void GatherPixelSortStrokes()
	{
		pixelSortCanvas.Clear();
		foreach (var actor in rendering)
			actor.StirPixelSort(pixelSortCanvas);
	}

	void ApplyPixelSort(Target scene, PixelSortLayer layer)
	{
		var field = pixelSortFields[(int)layer];
		var strokes = pixelSortCanvas.Strokes[(int)layer];
		field.Idle = strokes.Count > 0 ? 0 : field.Idle + Time.Delta;
		if (pixelSortMaterial == null || field.Idle > PixelSortIdleSeconds)
		{
			field.Live = false;
			return;
		}

		var size = new Point2(Width, Height);
		field.Front ??= NewPixelSortTarget(size, TextureFormat.R16G16B16A16Float);
		field.Back ??= NewPixelSortTarget(size, TextureFormat.R16G16B16A16Float);
		field.StirA ??= NewPixelSortTarget(size, TextureFormat.Color);
		field.StirB ??= NewPixelSortTarget(size, TextureFormat.Color);

		// The field is anchored to the world: when the camera moves, last frame's offsets are
		// read back from where they now sit. A jump bigger than the field starts it over.
		var origin = (Point2)RenderCamera;
		var shift = origin - field.Origin;
		field.Origin = origin;
		if (!field.Live || Math.Abs(shift.X) >= size.X || Math.Abs(shift.Y) >= size.Y)
		{
			field.Front.Clear(Color.Transparent);
			shift = Point2.Zero;
			field.Live = true;
		}

		DrawPixelSortStir(field, strokes, origin);

		var material = pixelSortMaterial.Material;
		material.Fragment.Samplers[1] = new(scene, PixelSortSampler);
		material.Fragment.Samplers[2] = new(field.StirA, PixelSortSampler);
		material.Fragment.Samplers[3] = new(field.StirB, PixelSortSampler);
		var steps = 4;
		foreach (var stroke in strokes)
			steps = Math.Max(steps, stroke.Style.Steps);
		var relaxScale = Time.Delta / steps;
		for (var i = 0; i < steps; i++, field.Pass++)
		{
			material.Fragment.SetUniformBuffer([
				scene.Width, scene.Height, zoomLevel, 0,
				size.X, size.Y, i == 0 ? shift.X : 0, i == 0 ? shift.Y : 0,
				field.Pass & 1, field.Pass & 1023, relaxScale, PixelSortHomeRate * relaxScale,
			]);
			DrawPixelSortPass(field.Front, Vector2.Zero, 1, field.Back, material);
			(field.Front, field.Back) = (field.Back, field.Front);
		}

		// Composite from a copy, so the scene is never sampled while it is being drawn.
		var source = scene == GlitchScreen ? Screen : GlitchScreen;
		DrawPixelSortPass(scene, Vector2.Zero, 1, source, null);
		material.Fragment.Samplers[1] = new(source, PixelSortSampler);
		material.Fragment.SetUniformBuffer([
			scene.Width, scene.Height, zoomLevel, 1,
			size.X, size.Y, 0, 0,
			0, 0, 0, 0,
		]);
		DrawPixelSortPass(field.Front, Vector2.Zero, zoomLevel, scene, material);

		if (ShowPixelSortStir)
			DrawPixelSortPass(field.StirA, Vector2.Zero, zoomLevel, scene, null, PixelSortStirDebugBlend);
	}

	// Each stroke is drawn twice, once per stir texture, each colour channel carrying one of its
	// settings; see PixelSort.hlsl for the packing. Strokes take the strongest of whatever they
	// overlap (Max), not the last drawn -- a feathered edge would otherwise cut a notch out of
	// the neighbour it laps over, which reads worse than the hard edge it is there to soften.
	// (Where two strokes with different relax rates or directions meet, the overlap takes the
	// higher of each; along one trail they barely differ.)
	void DrawPixelSortStir(PixelSortField field, List<PixelSortCanvas.Stroke> strokes, Point2 origin)
	{
		for (var pass = 0; pass < 2; pass++)
		{
			var target = pass == 0 ? field.StirA! : field.StirB!;
			target.Clear(Color.Transparent);
			Batcher.PushBlend(PixelSortStirBlend);
			Batcher.PushMatrix(-origin);
			foreach (var stroke in strokes)
			{
				var style = stroke.Style;
				var color = pass == 0
					? new Color(style.Gate, style.BandLow, style.BandHigh, 1)
					: new Color(style.Reach / 255f, style.RelaxRate / 255f, (int)style.Direction / 3f, 1);
				// Only the gate fades out across the feather: the rest of the packing has to stay
				// intact out there (a zeroed band or reach would stop the sort dead on a line of
				// its own, which is the edge we are trying to lose).
				var edge = pass == 0 ? color with { R = 0 } : color;
				if (stroke.IsLine)
					StirLine(stroke.From, stroke.To, stroke.Width, style.Feather, color, edge);
				else
					Batcher.Rect(stroke.Rect, color);
			}
			Batcher.PopMatrix();
			Batcher.PopBlend();
			Batcher.Render(target);
			Batcher.Clear();
		}
	}

	// A full-strength core with a band either side of it ramping to `edge` at the outer rim.
	void StirLine(Vector2 from, Vector2 to, float width, float feather, Color color, Color edge)
	{
		if (feather <= 0)
		{
			Batcher.Line(from, to, width, color);
			return;
		}

		var normal = (to - from).Normalized();
		var perp = new Vector2(-normal.Y, normal.X) * width * 0.5f;
		var core = perp * (1 - Math.Clamp(feather, 0, 1));
		Batcher.Quad(from + core, from - core, to - core, to + core, color);
		Batcher.Quad(from + perp, from + core, to + core, to + perp, edge, color, color, edge);
		Batcher.Quad(from - core, from - perp, to - perp, to - core, color, edge, edge, color);
	}

	static readonly TextureSampler PixelSortSampler = new(TextureFilter.Nearest, TextureWrap.Clamp, TextureWrap.Clamp);

	void DrawPixelSortPass(Texture texture, Vector2 position, float scale, Target destination, Material? material, BlendMode? blend = null)
	{
		Batcher.PushSampler(PixelSortSampler);
		Batcher.PushBlend(blend ?? ColorFieldCopyBlend);
		if (material != null)
			Batcher.PushMaterial(material);
		Batcher.Image(texture, position, Vector2.Zero, Vector2.One * scale, 0, Color.White);
		if (material != null)
			Batcher.PopMaterial();
		Batcher.PopBlend();
		Batcher.PopSampler();
		Batcher.Render(destination);
		Batcher.Clear();
	}

	Target NewPixelSortTarget(Point2 size, TextureFormat format)
	{
		var target = CreateRenderTarget(size.X, size.Y, format, "PixelSort");
		target.Clear(Color.Transparent);
		return target;
	}

	void DisposePixelSortFields()
	{
		foreach (var field in pixelSortFields)
		{
			field.Front?.Dispose();
			field.Back?.Dispose();
			field.StirA?.Dispose();
			field.StirB?.Dispose();
			field.Front = field.Back = field.StirA = field.StirB = null;
			field.Live = false;
		}
	}
}
