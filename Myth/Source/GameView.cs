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
		gpuLighting = loadVisualShaders ? new(gd) : null;
		proceduralWallRenderer = loadVisualShaders ? new(gd) : null;
		crtMaterial = loadVisualShaders ? CreateFragmentMaterial("Crt", samplerCount: 1, uniformBufferCount: 1) : null;
		atlasWanderMaterial = loadVisualShaders ? CreateFragmentMaterial("AtlasWander", samplerCount: 1, uniformBufferCount: 1) : null;
		colorFieldMaterial = loadVisualShaders ? CreateFragmentMaterial("ColorField", samplerCount: 2, uniformBufferCount: 1) : null;
		lightingCompositeMaterial = loadVisualShaders ? CreateFragmentMaterial("LightingComposite", samplerCount: 12, uniformBufferCount: 1) : null;
		blurMaterial = loadVisualShaders ? CreateFragmentMaterial("Blur", samplerCount: 1, uniformBufferCount: 1) : null;
		sdfAuraMaterial = loadVisualShaders ? CreateFragmentMaterial("SdfAura", samplerCount: 1, uniformBufferCount: 1) : null;
		backgroundDecalMaskMaterial = loadVisualShaders ? CreateFragmentMaterial("DecalMask", samplerCount: 2, uniformBufferCount: 1) : null;
		foregroundDecalMaskMaterial = loadVisualShaders ? CreateFragmentMaterial("DecalMask", samplerCount: 2, uniformBufferCount: 1) : null;
		backgroundColorFieldDecalMaterial = loadVisualShaders ? CreateFragmentMaterial("ColorFieldDecalMask", samplerCount: 3, uniformBufferCount: 1) : null;
		foregroundColorFieldDecalMaterial = loadVisualShaders ? CreateFragmentMaterial("ColorFieldDecalMask", samplerCount: 3, uniformBufferCount: 1) : null;
		coverageMaskMaterial = loadVisualShaders ? CreateFragmentMaterial("CoverageMask", samplerCount: 1) : null;
		// Transient preview/thumbnail games skip particles: they render and dispose
		// in one synchronous call, which would free the GPU draw resources while the
		// submitted particle draw is still in flight (corruption).
		particleSystem = enableParticles ? new ParticleSystem(gd) : null;
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
	Vector2 RenderTargetShake => game.renderTargetShake;
	float ditherGlitch => game.ditherGlitch;
	Vector2 ditherGlitchAxis => game.ditherGlitchAxis;
	Point2 shake { get => game.shake; set => game.shake = value; }
	float ZoomedWidth => Width / zoomLevel;
	float ZoomedHeight => Height / zoomLevel;
	Game.DebugView debugView => game.debugView;
	float nextRoomEase => game.nextRoomEase;
	Vector2 layerTransitionPivot;
	bool layerTransitionCaptured;

	internal void BeginLayerTransition(Point2 worldPivot)
	{
		layerTransitionCaptured = false;
		LayerTransitionScreen.Clear(Color.Transparent);
		RenderCurrentView(LayerTransitionScreen, LayerTransitionScreen.Bounds, showHud: false, scissor: null, applyCrt: false);
		layerTransitionPivot = ((Vector2)worldPivot - RenderCamera) * zoomLevel;
		layerTransitionCaptured = true;
	}

	void DrawLayerTransition(Target output)
	{
		if (!game.LayerTransitionActive || !layerTransitionCaptured)
			return;

		var progress = Ease.Cube.Out(game.LayerTransitionProgress);
		var scale = Vector2.One * Calc.Lerp(1, 1.1f, progress);
		Batcher.Image(new Subtexture(LayerTransitionScreen.Attachments[0]), layerTransitionPivot, layerTransitionPivot, scale, 0, Color.White * (1 - progress));
		Batcher.Render(output);
		Batcher.Clear();
	}

	private void PushWorldMatrix()
		=> Batcher.PushMatrix(
			Matrix3x2.CreateTranslation(-(Point2)Camera + shake) *
			Matrix3x2.CreateScale(zoomLevel));

	private Vector2 PlayerDitherGlitchPosition()
		=> game.ditherGlitchPosition ?? game.GetFirst<Player>()?.Position ?? Camera + new Vector2(Width, Height) * 0.5f;

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

	static readonly List<ShadowSegment> debugSegments = [];

	void DrawShadowDebug(Target target)
	{
		if (!TryGetShadowDebugLight(out var lightPosition, out var radius))
			return;

		GatherShadowDebugSegments();

		Batcher.PushMatrix(-(Point2)Camera + shake);
		Batcher.Circle(lightPosition, radius, 64, Color.Transparent, new Color(0x5fcde4, 0.35f));

		for (var i = 0; i < debugSegments.Count; i++)
			Batcher.Line(debugSegments[i].A, debugSegments[i].B, 1, ShadowDebugSegmentColor(i, 0.9f));

		Batcher.Circle(lightPosition, 2.5f, 16, 0xffffff);
		Batcher.PopMatrix();
		Batcher.Render(target);
		Batcher.Clear();
	}

	void GatherShadowDebugSegments()
	{
		debugSegments.Clear();
		foreach (var visibleRoom in VisibleRooms())
			debugSegments.AddRange(visibleRoom.ShadowSegments);
		foreach (var actor in Actors)
		{
			if (actor is DualGrid { Layer: Factory.TileLayer.Foreground } dualGrid)
				dualGrid.AppendAllShadowSegments(debugSegments);
			if (actor is HiddenCoverGrid hiddenCover)
				hiddenCover.AppendAllShadowSegments(debugSegments);
			if (actor is IShapeOccluder { CastsShadow: true } occluder)
				occluder.AppendOccluderSegments(debugSegments);
			if (actor is HiddenWallGrid hiddenWalls)
				hiddenWalls.ForEachVisibleCell(debugSegments, static (segments, rect, _, _) =>
				{
					segments.Add(new(rect.TopLeft, rect.TopRight));
					segments.Add(new(rect.TopRight, rect.BottomRight));
					segments.Add(new(rect.BottomRight, rect.BottomLeft));
					segments.Add(new(rect.BottomLeft, rect.TopLeft));
				});
		}
	}

	private bool TryGetShadowDebugLight(out Vector2 lightPosition, out float radius)
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

	private static Color ShadowDebugSegmentColor(int index, float alpha)
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
			{
				if (it.Hitbox.Rect != new RectInt())
					Batcher.RectLine(it.Hitbox.Rect + it.Position, 1, Color.Red);
				it.RenderCollisionDebug(Batcher);
			}
	}

	private void DrawStorySpeech(Target target)
	{
		if (game.storySpeechBubbles.Count == 0)
			return;

		var font = Assets.SpeechFont ?? Assets.Font;
		if (font == null)
			return;

		var screen = new Point2(target.Width, target.Height);
		CollectSpeechObstacles(screen);
		foreach (var bubble in game.storySpeechBubbles)
			DrawStorySpeechBubble(font, bubble, screen);

		Batcher.Render(target);
		Batcher.Clear();
	}

	private void DrawStorySpeechBubble(SpriteFont font, Game.StorySpeechBubble bubble, Point2 screen)
	{
		var layout = Game.MeasureStorySpeech(font, bubble.Text);
		if (layout.Lines.Count == 0)
			return;

		// The bubble hangs up and to the left of its world anchor, then slides back onto the
		// screen so a line spoken at the view's edge is still readable in full.
		var anchor = (Point2)(bubble.Position - RenderCamera);
		var size = new Vector2(
			layout.Width + layout.Padding.X * 2,
			layout.Lines.Count * layout.LineHeight + layout.Padding.Y * 2);
		var box = new Rect(
			ClampToScreen(anchor.X - size.X + layout.Padding.X, size.X, screen.X),
			ClampToScreen(anchor.Y - size.Y + layout.Padding.Y, size.Y, screen.Y),
			size.X,
			size.Y);
		box = DodgeSpeechObstacles(box, screen);
		var speakerBounds = StorySpeechSpeakerBounds(bubble);
		box = NudgeStorySpeechForLeader(font, layout, box, screen, speakerBounds);
		DrawStorySpeechLeader(bubble, speakerBounds, ClippedStorySpeechGlyphPoint(font, layout, box,
			speakerBounds.Center - RenderCamera));

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
			{
				// Same drop shadow TitleText uses, so speech sits on busy tiles as readably.
				Batcher.Text(font, shown, at + new Point2(SpeechShadowOffset, SpeechShadowOffset), font.Size, SpeechShadowColor);
				Batcher.Text(font, shown, at, font.Size, bubble.Color);
			}
			remaining -= full.Length + 1; // +1 for the space that joined this line to the next
			if (remaining <= 0)
				break;
		}
	}

	static bool SpeechLineFollowsSpeaker => true;
	static float SpeechLineSpeakerGap => 1.5f;
	static float SpeechLineTextGap => 1f;
	static float SpeechLineMinimumLength => 10f;
	static float SpeechLineMaxTextNudge => 8f;
	static float SpeechLineWeight => 2f;

	Rect StorySpeechSpeakerBounds(Game.StorySpeechBubble bubble)
		=> SpeechLineFollowsSpeaker && Actors.Contains(bubble.Speaker) && !game.destroying.Contains(bubble.Speaker)
			? bubble.Speaker.GetVisualBounds()
			: bubble.SpeakerBounds;

	static Rect StorySpeechGlyphRun(SpriteFont font, in Game.StorySpeechLayout layout, Rect box, int index)
	{
		var width = MathF.Ceiling(font.WidthOf(layout.Lines[index]));
		return new(
			MathF.Round(box.Left + layout.Padding.X + (layout.Width - width) * 0.5f),
			MathF.Round(box.Top + layout.Padding.Y + index * layout.LineHeight),
			width,
			MathF.Ceiling(font.LineHeight));
	}

	static Vector2 ClippedStorySpeechGlyphPoint(SpriteFont font, in Game.StorySpeechLayout layout, Rect box,
		Vector2 speakerCenter)
	{
		var glyphBounds = StorySpeechGlyphRun(font, layout, box, 0);
		for (var i = 1; i < layout.Lines.Count; i++)
			glyphBounds = glyphBounds.Conflate(StorySpeechGlyphRun(font, layout, box, i));

		var direction = (glyphBounds.Center - speakerCenter).Normalized(-Vector2.UnitY);
		var ray = new Line(speakerCenter, glyphBounds.Center + direction * box.Size.Length());
		var closest = glyphBounds.Center;
		var closestDistance = float.MaxValue;
		for (var i = 0; i < layout.Lines.Count; i++)
		{
			var run = StorySpeechGlyphRun(font, layout, box, i);
			foreach (var edge in run.Edges)
				if (ray.Intersects(edge, out var point))
				{
					var distance = Vector2.DistanceSquared(point, speakerCenter);
					if (distance < closestDistance)
					{
						closest = point;
						closestDistance = distance;
					}
				}
		}
		return closest;
	}

	Rect NudgeStorySpeechForLeader(SpriteFont font, in Game.StorySpeechLayout layout, Rect box, Point2 screen,
		Rect speakerBounds)
	{
		var speakerCenter = speakerBounds.Center - RenderCamera;
		var text = ClippedStorySpeechGlyphPoint(font, layout, box, speakerCenter);
		var speaker = speakerBounds.ClosestPointOnEdges(text + RenderCamera) - RenderCamera;
		var shortfall = SpeechLineSpeakerGap + SpeechLineTextGap + SpeechLineMinimumLength -
			Vector2.Distance(speaker, text);
		if (shortfall <= 0)
			return box;

		var away = (box.Center - speakerCenter).Normalized(-Vector2.UnitY);
		var offset = away * MathF.Min(shortfall, SpeechLineMaxTextNudge);
		box.X = ClampToScreen(box.X + offset.X, box.Width, screen.X);
		box.Y = ClampToScreen(box.Y + offset.Y, box.Height, screen.Y);
		return box;
	}

	void DrawStorySpeechLeader(Game.StorySpeechBubble bubble, Rect speakerBounds, Vector2 text)
	{
		if (bubble.VoiceLevel <= 0)
			return;

		var speaker = speakerBounds.ClosestPointOnEdges(text + RenderCamera) - RenderCamera;
		var delta = text - speaker;
		var length = delta.Length();
		if (length <= SpeechLineSpeakerGap + SpeechLineTextGap)
			return;

		var direction = delta / length;
		var from = speaker + direction * SpeechLineSpeakerGap;
		var to = text - direction * SpeechLineTextGap;
		var opacity = MathF.Sqrt(bubble.VoiceLevel);
		Batcher.Line(from + Vector2.One, to + Vector2.One, SpeechLineWeight, SpeechShadowColor * opacity);
		Batcher.Line(from, to, SpeechLineWeight, bubble.Color * opacity);
	}

	private const int SpeechShadowOffset = 1;
	private static readonly Color SpeechShadowColor = new(0x000000, (byte)170);

	// Whole pixels, so a bubble pinned in world space doesn't shimmer as the camera moves.
	// A box too big for the screen keeps its leading edge rather than centering.
	private static float ClampToScreen(float position, float size, int screen)
	{
		const float Margin = 3;
		return MathF.Round(MathF.Max(Margin, MathF.Min(position, screen - Margin - size)));
	}

	private const float SpeechObstacleGap = 3;
	private readonly List<Rect> speechObstacles = [];

	// Text already on screen that a bubble must not print over -- TitleText signage today.
	// Collected once per frame in screen space, padded by the gap the bubble should keep.
	private void CollectSpeechObstacles(Point2 screen)
	{
		speechObstacles.Clear();
		if (game.Simulating)
			return; // TitleText doesn't draw while simulating either

		foreach (var actor in Actors)
		{
			if (actor is not TitleText { Visible: true } title || string.IsNullOrWhiteSpace(title.Text))
				continue;

			var bounds = TitleText.TextBounds(title.Position, title.Text, title.LineHeightMultiplier);
			var rect = new Rect(bounds.X - RenderCamera.X, bounds.Y - RenderCamera.Y, bounds.Width, bounds.Height)
				.Inflate(SpeechObstacleGap);
			if (rect.Right > 0 && rect.Left < screen.X && rect.Bottom > 0 && rect.Top < screen.Y)
				speechObstacles.Add(rect);
		}
	}

	// Slide a blocked bubble above the text in its way, or below it if there's no room above.
	// Each move can land on another obstacle, hence the retries; if nothing clears, the bubble
	// stays where it was so it at least reads near the speaker.
	private Rect DodgeSpeechObstacles(Rect box, Point2 screen)
	{
		for (var attempt = 0; attempt < 3 && speechObstacles.Count > 0; attempt++)
		{
			if (FirstSpeechObstacleOverlapping(box) is not { } blocker)
				break;

			var above = new Rect(box.X, ClampToScreen(blocker.Top - box.Height, box.Height, screen.Y), box.Width, box.Height);
			if (FirstSpeechObstacleOverlapping(above) == null)
				return above;

			var below = new Rect(box.X, ClampToScreen(blocker.Bottom, box.Height, screen.Y), box.Width, box.Height);
			if (FirstSpeechObstacleOverlapping(below) == null)
				return below;

			box = above; // keep pushing upward against a stack of obstacles
		}
		return box;
	}

	private Rect? FirstSpeechObstacleOverlapping(in Rect box)
	{
		foreach (var obstacle in speechObstacles)
			if (obstacle.Overlaps(box))
				return obstacle;
		return null;
	}

#if DEBUG
	public void ReloadShaders()
	{
		crtMaterial?.Reload();
		atlasWanderMaterial?.Reload();
		colorFieldMaterial?.Reload();
		lightingCompositeMaterial?.Reload();
		gpuLighting?.ReloadShaders();
		proceduralWallRenderer?.ReloadShaders();
		reflectionDistortMaterial?.Reload();
		tileFieldDebugMaterial?.Reload();
		pixelSortGlitchMaterial?.Reload();
		blurMaterial?.Reload();
		sdfAuraMaterial?.Reload();
		backgroundDecalMaskMaterial?.Reload();
		foregroundDecalMaskMaterial?.Reload();
		backgroundColorFieldDecalMaterial?.Reload();
		foregroundColorFieldDecalMaterial?.Reload();
		coverageMaskMaterial?.Reload();
	}
#endif

	public void Dispose()
	{
		rendering.Clear();
		DisposeRenderTargets();
		DisposeRoomNormalCaches();
		DisposeRoomTileFieldCaches();
		DisposeRoomColorFieldCaches();
		crtMaterial?.Dispose();
		atlasWanderMaterial?.Dispose();
		colorFieldMaterial?.Dispose();
		lightingCompositeMaterial?.Dispose();
		reflectionDistortMaterial?.Dispose();
		tileFieldStampMaterial?.Dispose();
		tileFieldDebugMaterial?.Dispose();
		pixelSortGlitchMaterial?.Dispose();
		blurMaterial?.Dispose();
		sdfAuraMaterial?.Dispose();
		backgroundDecalMaskMaterial?.Dispose();
		foregroundDecalMaskMaterial?.Dispose();
		backgroundColorFieldDecalMaterial?.Dispose();
		foregroundColorFieldDecalMaterial?.Dispose();
		coverageMaskMaterial?.Dispose();
		particleSystem?.Dispose();
		gpuLighting?.Dispose();
		proceduralWallRenderer?.Dispose();
		Batcher.Dispose();
	}
}
