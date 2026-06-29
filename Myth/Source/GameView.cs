using System.Numerics;
using Foster.Framework;

namespace Myth;

// Presentation layer for Game: owns the render pipeline, lighting, shadows, normals,
// tile-field, reflections, particles and post (see Game.Rendering.cs). Holds a
// back-reference to the sim (Game) it draws; sim state is exposed below as thin
// read-only views so the render code can read it unqualified. A headless Game has
// no GameView. See memory: game-gameview-split.
public sealed partial class GameView : IDisposable
{
	internal readonly Game game;
	private readonly List<Actor> rendering = [];

	// Render-init: all GPU resources live here now, built when a Game has a view.
	public GameView(Game game, bool enableParticles)
	{
		this.game = game;
		var gd = game.Manager.GraphicsDevice;
		Batcher = new(gd, name: "GameBatcher");
		EnsureRenderTargets(Width, Height);
		var loadVisualShaders = gd.Driver != GraphicsDriver.Headless;
		crtMaterial = loadVisualShaders ? CreateFragmentMaterial("Crt", samplerCount: 1, uniformBufferCount: 1) : null;
		lightingCompositeMaterial = loadVisualShaders ? CreateFragmentMaterial("LightingComposite", samplerCount: 11, uniformBufferCount: 1) : null;
		blurMaterial = loadVisualShaders ? CreateFragmentMaterial("Blur", samplerCount: 1, uniformBufferCount: 1) : null;
		softShadowMaterial = loadVisualShaders ? CreateFragmentMaterial("SoftShadow", samplerCount: 1, uniformBufferCount: 1) : null;
		sdfAuraMaterial = loadVisualShaders ? CreateFragmentMaterial("SdfAura", samplerCount: 1, uniformBufferCount: 1) : null;
		backgroundDecalMaskMaterial = loadVisualShaders ? CreateFragmentMaterial("DecalMask", samplerCount: 2, uniformBufferCount: 1) : null;
		foregroundDecalMaskMaterial = loadVisualShaders ? CreateFragmentMaterial("DecalMask", samplerCount: 2, uniformBufferCount: 1) : null;
		coverageMaskMaterial = loadVisualShaders ? CreateFragmentMaterial("CoverageMask", samplerCount: 1) : null;
		// Transient preview/thumbnail games skip particles: they render and dispose
		// in one synchronous call, which would free the GPU draw resources while the
		// submitted particle draw is still in flight (corruption).
		particleSystem = enableParticles ? new ParticleSystem(gd) : null;
		normalLightMaterial = loadVisualShaders ? CreateFragmentMaterial("NormalLight", samplerCount: 1, uniformBufferCount: 1) : null;
		reflectionDistortMaterial = loadVisualShaders ? CreateFragmentMaterial("ReflectionDistort", samplerCount: 2, uniformBufferCount: 1) : null;
		tileFieldStampMaterial = loadVisualShaders ? CreateFragmentMaterial("TileFieldStamp", samplerCount: 1) : null;
		tileFieldDebugMaterial = loadVisualShaders ? CreateFragmentMaterial("TileFieldDebug", samplerCount: 1, uniformBufferCount: 1) : null;
		pixelSortGlitchMaterial = loadVisualShaders ? CreateFragmentMaterial("PixelSortGlitch", samplerCount: 2, uniformBufferCount: 1) : null;
	}

	// --- thin read-only views onto sim state, so render code reads it unqualified ---
	Manager Manager => game.Manager;
	GraphicsDevice GraphicsDevice => game.Manager.GraphicsDevice;
	Input Input => game.Manager.Input;
	Time Time => game.Time;
	Room? room => game.room;
	Room? nextRoom => game.nextRoom;
	Vector2 Camera { get => game.Camera; set => game.Camera = value; }
	System.Collections.Generic.List<Actor> Actors => game.Actors;
	float zoomLevel => game.zoomLevel;
	Vector2 RenderCamera => game.RenderCamera;
	float ditherGlitch => game.ditherGlitch;
	Vector2 ditherGlitchAxis => game.ditherGlitchAxis;
	Point2 shake { get => game.shake; set => game.shake = value; }
	float ZoomedWidth => Width / zoomLevel;
	float ZoomedHeight => Height / zoomLevel;
	Game.DebugView debugView => game.debugView;
	// Transition animation params: sim writes during room transitions, view lerps.
	float nextRoomEase => game.nextRoomEase;
	Color transitionBackdropColorFrom => game.transitionBackdropColorFrom;
	Color transitionBackdropColorTo => game.transitionBackdropColorTo;
	Color transitionBackdropColor2From => game.transitionBackdropColor2From;
	Color transitionBackdropColor2To => game.transitionBackdropColor2To;
	Color transitionAmbientColorFrom => game.transitionAmbientColorFrom;
	Color transitionAmbientColorTo => game.transitionAmbientColorTo;
	float transitionAmbientIntensityFrom => game.transitionAmbientIntensityFrom;
	float transitionAmbientIntensityTo => game.transitionAmbientIntensityTo;
	float transitionLightNoiseFrom => game.transitionLightNoiseFrom;
	float transitionLightNoiseTo => game.transitionLightNoiseTo;
	Color transitionSunColorFrom => game.transitionSunColorFrom;
	Color transitionSunColorTo => game.transitionSunColorTo;
	float transitionSunStrengthFrom => game.transitionSunStrengthFrom;
	float transitionSunStrengthTo => game.transitionSunStrengthTo;
	Vector2 transitionSunDirectionFrom => game.transitionSunDirectionFrom;
	Vector2 transitionSunDirectionTo => game.transitionSunDirectionTo;

	private void PushWorldMatrix()
		=> Batcher.PushMatrix(
			Matrix3x2.CreateTranslation(-(Point2)Camera + shake) *
			Matrix3x2.CreateScale(zoomLevel));

	private Vector2 PlayerDitherGlitchPosition()
		=> game.GetFirst<Player>()?.Position ?? Camera + new Vector2(Width, Height) * 0.5f;

	// Drives the GPU particle sim each frame and auto-bursts at the screen center.
	// Emitter actors enqueue emission in their own Update; this consumes the queue.
	internal void UpdateParticles(float dt)
	{
		if (!PerfToggles.Particles || particleSystem is not { Supported: true })
			return;
		StepParticles(dt);
		if (particleSystem.AutoBurst)
		{
			particleBurstTimer -= dt;
			if (particleBurstTimer <= 0)
			{
				particleBurstTimer = 0.25f;
				BurstTestEffect();
			}
		}
	}

	private static readonly List<VisibilityPolygon.Hit> debugHits = new();
	private static readonly List<VisibilityPolygon.Segment> debugSegments = new();

	private void DrawVisibilityDebug(Target target)
	{
		if (!TryGetVisibilityDebugLight(out var lightPosition, out var radius))
			return;

		GatherVisibilitySegments(lightPosition, radius, debugSegments);
		VisibilityPolygon.Compute(lightPosition, radius, debugSegments, debugHits);

		Batcher.PushMatrix(-(Point2)Camera + shake);
		Batcher.Circle(lightPosition, radius, 64, Color.Transparent, new Color(0x5fcde4, 0.35f));

		var hits = debugHits;
		for (var i = 0; i < hits.Count; i++)
		{
			var a = hits[i].Point;
			var b = hits[(i + 1) % hits.Count].Point;
			Batcher.Triangle(lightPosition, a, b, new Color(0x5fcde4, 0.08f));
		}

		for (var i = 0; i < debugSegments.Count; i++)
			Batcher.Line(debugSegments[i].A, debugSegments[i].B, 1, VisibilityDebugSegmentColor(i, 0.9f));

		foreach (var hit in hits)
		{
			var color = hit.HitSegment
				? VisibilityDebugSegmentColor(hit.SegmentIndex, 0.75f)
				: new Color(0xcbdbfc, 0.45f);
			Batcher.Line(lightPosition, hit.Point, 1, color * 0.45f);
			Batcher.Circle(hit.Point, 1.5f, 8, color);
		}

		Batcher.Circle(lightPosition, 2.5f, 16, 0xffffff);
		Batcher.PopMatrix();
		Batcher.Render(target);
		Batcher.Clear();
	}

	private bool TryGetVisibilityDebugLight(out Vector2 lightPosition, out float radius)
	{
		if (game.GetFirst<Player>() is { } player)
		{
			lightPosition = player.GetVisualBounds().Center;
			radius = 64;
			return true;
		}

		lightPosition = default;
		radius = 0;
		return false;
	}

	private static Color VisibilityDebugSegmentColor(int index, float alpha)
	{
		var hue = (index * 0.61803398875f) % 1.0f;
		return ColorFromHsv(hue, 0.72f, 1.0f, alpha);
	}

	private static Color ColorFromHsv(float hue, float saturation, float value, float alpha)
	{
		var h = hue * 6.0f;
		var c = value * saturation;
		var x = c * (1.0f - MathF.Abs(h % 2.0f - 1.0f));
		var m = value - c;
		var rgb = h switch
		{
			< 1.0f => new Vector3(c, x, 0),
			< 2.0f => new Vector3(x, c, 0),
			< 3.0f => new Vector3(0, c, x),
			< 4.0f => new Vector3(0, x, c),
			< 5.0f => new Vector3(x, 0, c),
			_ => new Vector3(c, 0, x),
		};
		return new Color(rgb.X + m, rgb.Y + m, rgb.Z + m, alpha);
	}

	private void DrawCollisionRects()
	{
		if (room == null)
			return;

		foreach (var rect in room.CollisionRects)
			Batcher.RectLine(rect, 1, Color.Red);
		foreach (var slope in room.SlopeTiles)
		{
			var polygon = room.GetSlopePolygon(slope);
			for (var i = 0; i < polygon.Count; i++)
				Batcher.Line(polygon[i], polygon[(i + 1) % polygon.Count], 1, Color.Red);
		}
		foreach (var rect in game.CollisionRectsThisFrame)
			Batcher.RectLine(rect, 1, Color.Red);
		foreach (var it in Actors)
			if (!game.destroying.Contains(it))
				if (it.Hitbox.Rect != new RectInt())
					Batcher.RectLine(it.Hitbox.Rect + it.Position, 1, Color.Red);
	}

	private void DrawStorySpeech(Target target)
	{
		if (game.storySpeechBubbles.Count == 0)
			return;

		var font = Assets.SpeechFont ?? Assets.Font;
		if (font == null)
			return;

		foreach (var bubble in game.storySpeechBubbles)
			DrawStorySpeechBubble(font, bubble);

		Batcher.Render(target);
		Batcher.Clear();
	}

	private void DrawStorySpeechBubble(SpriteFont font, Game.StorySpeechBubble bubble)
	{
		var layout = Game.MeasureStorySpeech(font, bubble.Text);
		if (layout.Lines.Count == 0)
			return;

		var anchor = (Point2)(bubble.Position - RenderCamera);
		var box = new Rect(
			anchor.X - layout.Width - layout.Padding.X,
			anchor.Y - layout.Lines.Count * layout.LineHeight - layout.Padding.Y,
			layout.Width + layout.Padding.X * 2,
			layout.Lines.Count * layout.LineHeight + layout.Padding.Y * 2);

		// Reveal characters over the canonical (whitespace-normalized) text: lines join
		// back into it with one space each, so a per-line budget with a -1 per join is exact.
		var remaining = bubble.VisibleLength;
		for (var i = 0; i < layout.Lines.Count; i++)
		{
			var full = layout.Lines[i];
			var shown = remaining >= full.Length ? full : full[..Math.Max(0, remaining)];
			var lineWidth = MathF.Ceiling(font.WidthOf(full));
			var at = new Point2(
				(int)MathF.Round(box.Left + layout.Padding.X + (layout.Width - lineWidth) * 0.5f),
				(int)MathF.Round(box.Top + layout.Padding.Y + i * layout.LineHeight));
			if (shown.Length > 0)
				Batcher.Text(font, shown, at, font.Size, Color.White);
			remaining -= full.Length + 1; // +1 for the space that joined this line to the next
			if (remaining <= 0)
				break;
		}
	}

#if DEBUG
	public void ReloadShaders()
	{
		crtMaterial?.Reload();
		lightingCompositeMaterial?.Reload();
		normalLightMaterial?.Reload();
		reflectionDistortMaterial?.Reload();
		tileFieldDebugMaterial?.Reload();
		pixelSortGlitchMaterial?.Reload();
		blurMaterial?.Reload();
		softShadowMaterial?.Reload();
		sdfAuraMaterial?.Reload();
		backgroundDecalMaskMaterial?.Reload();
		foregroundDecalMaskMaterial?.Reload();
		coverageMaskMaterial?.Reload();
	}
#endif

	public void Dispose()
	{
		rendering.Clear();
		DisposeRenderTargets();
		DisposeRoomNormalCaches();
		DisposeRoomTileFieldCaches();
		crtMaterial?.Dispose();
		lightingCompositeMaterial?.Dispose();
		normalLightMaterial?.Dispose();
		reflectionDistortMaterial?.Dispose();
		tileFieldStampMaterial?.Dispose();
		tileFieldDebugMaterial?.Dispose();
		pixelSortGlitchMaterial?.Dispose();
		blurMaterial?.Dispose();
		softShadowMaterial?.Dispose();
		sdfAuraMaterial?.Dispose();
		backgroundDecalMaskMaterial?.Dispose();
		foregroundDecalMaskMaterial?.Dispose();
		coverageMaskMaterial?.Dispose();
		particleSystem?.Dispose();
		Batcher.Dispose();
	}
}
