using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Text.Json.Nodes;
using Foster.Framework;
using DebugView = Myth.Game.DebugView;

namespace Myth;

// Presentation layer for Game: rendering pipeline, lighting, shadows, normals,
// tile-field, reflections, particles, post. Sim state lives on Game and is reached
// through `game`. See memory: game-gameview-split.
public sealed partial class GameView
{
	// Game's screen-space consts, re-aliased so render code reads them unqualified.
	const int Width = Game.Width;
	const int Height = Game.Height;
	const int TileSize = Game.TileSize;
	const int Columns = Game.Columns;
	const int Rows = Game.Rows;

	void RenderCurrentView(IDrawableTarget destination, in RectInt viewport, bool showHud, RectInt? scissor, bool applyCrt)
	{
		if (game.AtlasWander)
		{
			atlasWanderElapsed += Time.Delta;
			var ramp = Calc.ClampedMap(atlasWanderElapsed, 0, AtlasWanderRampDuration);
			ramp *= ramp * (3.0f - 2.0f * ramp);
			atlasWanderOffset += AtlasWanderSpeed * Calc.Lerp(AtlasWanderInitialSpeed, 1, ramp) * Time.Delta;
		}
		PrepareRenderingList();
		GetDecalLayers(out var hasBackgroundDecals, out var hasForegroundDecals);
		var buildAo = debugView is not DebugView.TileFieldRed and not DebugView.TileFieldGreen and not DebugView.DitherMask &&
			PerfToggles.Lighting && lightingCompositeMaterial != null && PerfToggles.AmbientOcclusion && blurMaterial != null;
		if (hasForegroundDecals)
			DrawParticleSolidMask();
		if (hasBackgroundDecals || buildAo)
			DrawBackgroundCoverage();

		using (PerfStats.Time(PerfStat.World))
		{
			using (GraphicsDevice.DebugGroup("Myth/background world"))
				DrawWorldLayer(BackgroundScreen, background: true);
			using (GraphicsDevice.DebugGroup("Myth/foreground world"))
				DrawWorldLayer(ForegroundScreen, background: false);
			if (PerfToggles.Reflections)
				using (GraphicsDevice.DebugGroup("Myth/flat reflections"))
					CompositeFlatReflections(ForegroundScreen);
		}
		if (PerfToggles.Emissive)
		{
			using (PerfStats.Time(PerfStat.Emissive))
			{
				using (GraphicsDevice.DebugGroup("Myth/background emissive"))
					DrawEmissiveLayer(BackgroundEmissive, background: true);
				using (GraphicsDevice.DebugGroup("Myth/foreground emissive"))
					DrawEmissiveLayer(ForegroundEmissive, background: false);
			}
		}
		else
		{
			BackgroundEmissive.Clear(Color.Transparent);
			ForegroundEmissive.Clear(Color.Transparent);
		}

		if (PerfToggles.NormalBuffer)
		{
			using (PerfStats.Time(PerfStat.Normal))
			using (GraphicsDevice.DebugGroup("Myth/normal buffer"))
			{
				DrawNormalBuffer();
			}
		}
		else
		{
			NormalBuffer.Clear(new Color(0.5f, 0.5f, 1.0f, 0.0f));
		}
		if (PerfToggles.DitherAndTileField)
		{
			using (PerfStats.Time(PerfStat.TileDither))
			{
				using (GraphicsDevice.DebugGroup("Myth/tile field"))
					DrawTileField();
				using (GraphicsDevice.DebugGroup("Myth/dither mask"))
					DrawDitherMask();
			}
		}
		else
		{
			TileField.Clear(Color.Transparent);
			DitherMask.Clear(Color.White);
		}

		Target output;
		if (debugView is DebugView.TileFieldRed or DebugView.TileFieldGreen)
		{
			using (GraphicsDevice.DebugGroup("Myth/tile field debug"))
				output = DrawTileFieldDebug();
		}
		else if (debugView == DebugView.DitherMask)
		{
			output = DitherMask;
		}
		else
		{
			if (PerfToggles.Lighting)
			{
				using (PerfStats.Time(PerfStat.Lighting))
				using (GraphicsDevice.DebugGroup("Myth/lighting"))
				{
					output = ApplyLighting();
				}
			}
			else
			{
				using (PerfStats.Time(PerfStat.Lighting))
				using (GraphicsDevice.DebugGroup("Myth/raw world composite"))
				{
					output = CompositeRawWorld();
				}
			}
			if (debugView == DebugView.LightMask)
				output = BackgroundLightMask;
			else if (debugView == DebugView.NormalBuffer)
				output = NormalBuffer;
			else if (debugView == DebugView.Reflection)
				output = ReflectionScreen;
			else if (debugView == DebugView.None)
			{
				if (PerfToggles.Glitch)
				{
					using (PerfStats.Time(PerfStat.Post))
					using (GraphicsDevice.DebugGroup("Myth/pixel sort glitch"))
					{
						output = ApplyPixelSortGlitch(output);
					}
				}
			}
		}
		if (PerfToggles.Particles && particleSystem is { Supported: true })
		{
			using (GraphicsDevice.DebugGroup("Myth/particles"))
				particleSystem.Render(output, RenderCamera, zoomLevel);
		}
		if (debugView is DebugView.None or DebugView.CollisionRects)
		{
			DrawOverlayElements(output);
		}
		if (debugView == DebugView.Visibility)
		{
			using (GraphicsDevice.DebugGroup("Myth/visibility debug"))
				DrawVisibilityDebug(output);
		}
		if (debugView == DebugView.PlatformNav)
		{
			game.GetPlatformNavGraph();
		}

		// draw screen to window
		var crt = applyCrt && PerfToggles.Crt ? crtMaterial : null;
		var useCrt = crt != null;
		using (PerfStats.Time(PerfStat.FinalComposite))
		using (GraphicsDevice.DebugGroup(useCrt ? "Myth/final composite crt" : "Myth/final composite"))
		{
			// Present into a 4:3 region when cropping (desktop pillarbox / portrait canvas),
			// cover-fitting the 16:9 frame so its sides are cropped; aspectUncrop lerps back to a
			// contained (full, letterboxed) frame while Zoom is held. The cover-scaled frame
			// overflows the present region, so clip it to never bleed past the pillarbox bars.
			var present = game.PresentRect(viewport);
			var center = present.Center;
			var scale = game.PresentScale(present, output.Width, output.Height);
			var clip = game.AspectCropActive
				? (scissor.HasValue ? scissor.Value.GetIntersection(present) : present)
				: scissor;

			Batcher.PushSampler(new(TextureFilter.Nearest, TextureWrap.Clamp, TextureWrap.Clamp));
			if (useCrt)
			{
				const float BloomAmount = 0.09f;
				const float VignetteAmount = 0.06f;
				const float RgbOffset = 0.35f;
				crt!.Material.Fragment.SetUniformBuffer([
					output.Width,
					output.Height,
					output.Width * scale,
					output.Height * scale,
					BloomAmount,
					VignetteAmount,
					RgbOffset,
					game.CrtShader,
				]);
				Batcher.PushMaterial(crt.Material);
			}
#if BROWSER
			var flipTargetTextures = Batcher.FlipTargetTextures;
			Batcher.FlipTargetTextures = false;
#endif
			Batcher.Image(output, center, output.Bounds.Size / 2, Vector2.One * scale, 0, Color.White);
#if BROWSER
			Batcher.FlipTargetTextures = flipTargetTextures;
#endif
			if (useCrt)
				Batcher.PopMaterial();
			Batcher.PopSampler();
			Batcher.Render(destination, scissor: clip);
			Batcher.Clear();
		}

		if (false && showHud && debugView is DebugView.None or DebugView.CollisionRects)
			DrawPlayerHud(destination, viewport, scissor);
	}

	Target CompositeRawWorld()
	{
		Screen.Clear(Color.Transparent);
		Batcher.PushSampler(new(TextureFilter.Nearest, TextureWrap.Clamp, TextureWrap.Clamp));
		Batcher.Image(BackgroundScreen, BackgroundScreen.Bounds.Center, BackgroundScreen.Bounds.Size / 2, Vector2.One, 0, Color.White);
		Batcher.Image(ForegroundScreen, ForegroundScreen.Bounds.Center, ForegroundScreen.Bounds.Size / 2, Vector2.One, 0, Color.White);
		Batcher.PopSampler();
		Batcher.Render(Screen);
		Batcher.Clear();
		return Screen;
	}

	void DrawOverlayElements(Target output)
	{
		using (GraphicsDevice.DebugGroup("Myth/story speech"))
			DrawStorySpeech(output);

		// Show << Rewinding text when rewinding the player position.
		if (game.Rewinding && Time.BetweenInterval(0.25))
		{
			var font = Assets.Font;
			if (font != null)
			{
				Batcher.Text(font, "<< Rewinding", new(20, 20), font.Size, Color.White);
				Batcher.Render(output); // TODO: why
				Batcher.Clear();
			}
		}
	}

	void PrepareRenderingList()
	{
		rendering.Clear();
		rendering.AddRange(Actors);
		rendering.Sort((a, b) => b.Depth - a.Depth);
	}

	Target DrawTileFieldDebug()
	{
		Screen.Clear(Color.Transparent);
		Batcher.PushSampler(new(TextureFilter.Nearest, TextureWrap.Clamp, TextureWrap.Clamp));
		if (tileFieldDebugMaterial != null)
		{
			tileFieldDebugMaterial.Material.Fragment.SetUniformBuffer([
				TileFieldDebugChannel(),
				0,
				1,
				0
			]);
			Batcher.PushMaterial(tileFieldDebugMaterial.Material);
		}
		Batcher.Image(TileField, TileField.Bounds.Center, TileField.Bounds.Size / 2, Vector2.One, 0, Color.White);
		if (tileFieldDebugMaterial != null)
			Batcher.PopMaterial();
		Batcher.PopSampler();
		Batcher.Render(Screen);
		Batcher.Clear();
		return Screen;
	}

	int TileFieldDebugChannel()
		=> debugView switch
		{
			DebugView.TileFieldGreen => 1,
			_ => 0
		};

	void DrawWorldLayer(Target target, bool background)
	{
		target.Clear(Color.Transparent);
		if (background)
			DrawBackdrop(target);

		if ((background && !ShowBackgroundLayer) || (!background && !ShowForegroundLayer))
		{
			Batcher.Render(target);
			Batcher.Clear();
			return;
		}

		PushWorldMatrix();
		var atlasWander = PushAtlasWanderMaterial();

		// draw actors
		foreach (var actor in rendering)
		{
			if (!actor.Visible)
				continue;
			if (IsBackgroundActor(actor) != background)
				continue;

			Batcher.PushMatrix(actor.Position);
			var decal = !atlasWander && PushPropDecalMaterial(actor);
			actor.Render(Batcher);
			if (decal)
				Batcher.PopMaterial();
			Batcher.PopMatrix();
		}

		if (!background && debugView == DebugView.CollisionRects)
			DrawCollisionRects(); // TODO: don't do this in post-processing context

		if (atlasWander)
			Batcher.PopMaterial();
		Batcher.PopMatrix();
		Batcher.Render(target);
		Batcher.Clear();

		if (PerfToggles.Reflections)
		{
			if (background)
				DrawSpriteReflections(target);
			else
				DrawPlatformSurfaceReflections(target);
		}
	}

	bool PushAtlasWanderMaterial()
	{
		if (!game.AtlasWander || atlasWanderMaterial == null || Assets.Atlas is not { } atlas)
			return false;
		atlasWanderMaterial.Material.Fragment.SetUniformBuffer([
			atlas.Width,
			atlas.Height,
			atlasWanderOffset.X,
			atlasWanderOffset.Y,
		]);
		Batcher.PushMaterial(atlasWanderMaterial.Material);
		return true;
	}

	internal void ResetAtlasWander()
	{
		atlasWanderOffset = Vector2.Zero;
		atlasWanderElapsed = 0;
	}

	void DrawBackdrop(Target target)
	{
		var (top, bottom) = CurrentBackdropColors();
		Batcher.Rect(new Rect(0, 0, target.Width, target.Height), top, top, bottom, bottom);
	}

	void DrawPlayerHud(IDrawableTarget target, in RectInt viewport, RectInt? scissor)
	{
		var scale = viewport.Height / (float)Height;
		Batcher.PushMatrix(
			Matrix3x2.CreateScale(scale) *
			Matrix3x2.CreateTranslation(viewport.Left, viewport.Bottom - Height * scale));
		var atlasWander = PushAtlasWanderMaterial();
		if (game.GetFirst<Player>() is { } player)
		{
#if BROWSER
			Point2 pos = new(0, 0);
#else
			Point2 pos = new(0, Height - 16);
#endif
			Batcher.Rect(new Rect(pos.X, pos.Y + 7, 48, 4), Color.Black);

			for (var i = 0; i < Player.MaxHealth; i++)
			{
				Batcher.Image(
					player.Health >= i + 1 ? Assets.GetSubtexture("heart/0") : Assets.GetSubtexture("heart/1"), pos,
					Color.White);
				pos.X += 12;
			}
		}
		if (atlasWander)
			Batcher.PopMaterial();
		Batcher.PopMatrix();

		Batcher.Render(target, scissor: scissor?.GetIntersection(viewport) ?? viewport);
		Batcher.Clear();
	}

	static bool IsBackgroundActor(Actor actor)
		=> actor switch
		{
			Grid grid => grid.Layer == Factory.TileLayer.Background,
			DualGrid dualGrid => dualGrid.Layer == Factory.TileLayer.Background,
			Prop prop => prop.Layer == Factory.TileLayer.Background,
			_ => false
		};

	void GetDecalLayers(out bool background, out bool foreground)
	{
		background = false;
		foreground = false;
		foreach (var actor in rendering)
		{
			if (actor is not Prop { Decal: true, Visible: true } prop)
				continue;
			if (prop.Layer == Factory.TileLayer.Background)
				background = true;
			else
				foreground = true;
			if (background && foreground)
				return;
		}
	}

	bool PushPropDecalMaterial(Actor actor)
	{
		if (actor is not Prop { Decal: true } prop)
			return false;
		var background = prop.Layer == Factory.TileLayer.Background;
		var coverage = background ? BackgroundCoverage : ParticleSolidMask;
		var material = background ? backgroundDecalMaskMaterial : foregroundDecalMaskMaterial;
		if (material == null)
			return false;
		material.Material.Fragment.SetUniformBuffer([coverage.Width, coverage.Height, 0, 0]);
		material.Material.Fragment.Samplers[1] = new(coverage, new(TextureFilter.Nearest, TextureWrap.Clamp));
		Batcher.PushMaterial(material.Material);
		return true;
	}

	void DrawEmissiveLayer(Target target, bool background)
	{
		target.Clear(Color.Transparent);
		PushWorldMatrix();
		foreach (var actor in rendering)
		{
			if (!actor.Visible)
				continue;
			if (IsBackgroundActor(actor) != background)
				continue;

			Batcher.PushMatrix(actor.Position);
			var decal = PushPropDecalMaterial(actor);
			Batcher.PushBlend(EmissiveOccluderBlend);
			actor.RenderEmissiveOccluder(Batcher);
			Batcher.PopBlend();
			actor.RenderEmissive(Batcher);
			if (decal)
				Batcher.PopMaterial();
			Batcher.PopMatrix();
		}
		Batcher.PopMatrix();
		Batcher.Render(target);
		Batcher.Clear();
	}

	void DrawSpriteReflections(Target target)
	{
		if (room == null)
			return;

		ReflectionScreen.Clear(Color.Transparent);
		EnsureReflectionSurfaceTops();
		var reflectionRects = room.ReflectionRects;
		for (var i = 0; i < reflectionRects.Count; i++)
			DrawReflectedActors(reflectionRects[i].Bounds, reflectionSurfaceTops[i], reflectionRects[i].Opacity, skip: null);

		ReflectionMask.Clear(Color.Transparent);
		foreach (var rect in room.ReflectionRects)
		{
			if (!rect.Distorted) continue;
			var scissor = GetReflectionScissor(rect.Bounds);
			if (scissor.Width <= 0 || scissor.Height <= 0) continue;
			Batcher.Rect(new Rect(scissor.X, scissor.Y, scissor.Width, scissor.Height), Color.White);
		}
		Batcher.Render(ReflectionMask);
		Batcher.Clear();

		CompositeReflections(target);
	}

	// Mirror every reflection-casting actor across a surface's top edge into ReflectionScreen,
	// clipped to the surface bounds. Shared by room reflection rects and reflective platforms.
	void DrawReflectedActors(RectInt bounds, int surfaceTop, float opacity, Actor? skip)
	{
		var scissor = GetReflectionScissor(bounds);
		if (scissor.Width <= 0 || scissor.Height <= 0)
			return;

		PushWorldMatrix();
		foreach (var actor in rendering)
		{
			if (!actor.Visible || !actor.CastsReflection || actor == skip)
				continue;
			if (!ActorCanReflectIn(actor, bounds))
				continue;

			Batcher.PushMatrix(Matrix3x2.CreateScale(1, -1, new Vector2(actor.Position.X, surfaceTop)));
			Batcher.PushMatrix(actor.Position);
			actor.RenderReflection(Batcher, Color.White * opacity);
			Batcher.PopMatrix();
			Batcher.PopMatrix();
		}
		Batcher.PopMatrix();
		Batcher.Render(ReflectionScreen, scissor: scissor);
		Batcher.Clear();
	}

	// Reflective moving platforms are foreground actors and move, so they can't be baked into the
	// room's (background) reflection rects; mirror sprites onto their surface here, in the foreground
	// pass, so the reflection lands over the platform's own skin.
	void DrawPlatformSurfaceReflections(Target target)
	{
		var any = false;
		ReflectionScreen.Clear(Color.Transparent);
		foreach (var actor in rendering)
			if (actor is MovingPlatform { Visible: true } platform && platform.ReflectsSurface)
			{
				DrawReflectedActors(platform.SurfaceReflectionBounds, platform.Position.Y, MovingPlatform.SurfaceReflectionOpacity, skip: platform);
				any = true;
			}

		if (!any)
			return;

		// Platforms are flat (never distorted); clear the mask the room pass left so the distort
		// shader composites their reflection without picking up stale distorted regions.
		ReflectionMask.Clear(Color.Transparent);
		CompositeReflections(target);
	}

	RectInt GetReflectionScissor(RectInt rect)
	{
		var screenRect = new RectInt(
			(int)MathF.Round((rect.X - Camera.X + shake.X) * zoomLevel),
			(int)MathF.Round((rect.Y - Camera.Y + shake.Y) * zoomLevel),
			(int)MathF.Round(rect.Width * zoomLevel),
			(int)MathF.Round(rect.Height * zoomLevel));
		return screenRect.GetIntersection(ReflectionScreen.Bounds);
	}

	void CompositeReflections(Target target) => BlitReflections(target, flatOnly: 0.0f);

	// foreach over the List's struct enumerator, not LINQ .Any (which boxes an enumerator each frame).
	bool HasFlatReflectionRect()
	{
		foreach (var r in room!.ReflectionRects)
			if (!r.Distorted)
				return true;
		return false;
	}

	void CompositeFlatReflections(Target target)
	{
		if (room == null || !HasFlatReflectionRect())
			return;
		BlitReflections(target, flatOnly: 1.0f);
	}

	void BlitReflections(Target target, float flatOnly)
	{
		Batcher.PushSampler(new(TextureFilter.Nearest, TextureWrap.Clamp, TextureWrap.Clamp));
		if (reflectionDistortMaterial != null)
		{
			reflectionDistortMaterial.Material.Fragment.SetUniformBuffer([
				ReflectionScreen.Width,
				ReflectionScreen.Height,
				Time.SecondsF,
				1.4f,
				0.10f,
				2.2f,
				flatOnly,
				0
			]);
			reflectionDistortMaterial.Material.Fragment.Samplers[1] = new(ReflectionMask, new(TextureFilter.Nearest, TextureWrap.Clamp, TextureWrap.Clamp));
			Batcher.PushMaterial(reflectionDistortMaterial.Material);
		}
		Batcher.Image(ReflectionScreen, ReflectionScreen.Bounds.Center, ReflectionScreen.Bounds.Size / 2, Vector2.One, 0, Color.White);
		if (reflectionDistortMaterial != null)
			Batcher.PopMaterial();
		Batcher.PopSampler();
		Batcher.Render(target);
		Batcher.Clear();
	}

	// Each reflection rect's surface top (the min top of its touching group) depends only on
	// the static rect geometry, so it's cached per room and recomputed only when the rect list
	// changes — the old per-frame-per-rect flood-fill allocated a bool[], a Stack, and a closure
	// on every rect every frame, which dominated allocation in water-heavy rooms.
	Room? reflectionSurfaceTopsRoom;
	readonly List<int> reflectionSurfaceTops = [];
	readonly Stack<int> reflectionFloodPending = new();
	bool[] reflectionFloodVisited = [];

	void EnsureReflectionSurfaceTops()
	{
		var rects = room!.ReflectionRects;
		if (reflectionSurfaceTopsRoom == room && reflectionSurfaceTops.Count == rects.Count)
			return;

		if (reflectionFloodVisited.Length < rects.Count)
			reflectionFloodVisited = new bool[rects.Count];

		reflectionSurfaceTops.Clear();
		for (var i = 0; i < rects.Count; i++)
			reflectionSurfaceTops.Add(ComputeReflectionSurfaceTop(i));
		reflectionSurfaceTopsRoom = room;
	}

	int ComputeReflectionSurfaceTop(int start)
	{
		var rects = room!.ReflectionRects;
		Array.Clear(reflectionFloodVisited, 0, rects.Count);
		reflectionFloodPending.Clear();
		reflectionFloodPending.Push(start);
		reflectionFloodVisited[start] = true;

		var top = rects[start].Bounds.Top;
		while (reflectionFloodPending.Count > 0)
		{
			var current = rects[reflectionFloodPending.Pop()].Bounds;
			top = Math.Min(top, current.Top);

			for (var i = 0; i < rects.Count; i++)
			{
				if (reflectionFloodVisited[i] || !ReflectionRectsTouch(current, rects[i].Bounds))
					continue;

				reflectionFloodVisited[i] = true;
				reflectionFloodPending.Push(i);
			}
		}

		return top;
	}

	static bool ReflectionRectsTouch(RectInt a, RectInt b)
		=> a.Left <= b.Right &&
		   a.Right >= b.Left &&
		   a.Top <= b.Bottom &&
		   a.Bottom >= b.Top;

	static bool ActorCanReflectIn(Actor actor, RectInt rect)
	{
		var bounds = actor.GetVisualBounds().Inflate(4);
		if (bounds.Right <= rect.Left || bounds.Left >= rect.Right)
			return false;

		return bounds.Bottom >= rect.Top - 64 && bounds.Top <= rect.Bottom + 64;
	}

	Target ApplyLighting()
	{
		if (lightingCompositeMaterial == null)
			return ForegroundScreen;

		DrawLightMask(BackgroundLightMask, includeShadows: true);
		DrawLightMask(ForegroundLightMask, includeShadows: false);

		var aoStrength = 0.0f;
		if (PerfToggles.AmbientOcclusion && blurMaterial != null)
		{
			using (GraphicsDevice.DebugGroup("Myth/ambient occlusion mask"))
				BuildAoMask();
			aoStrength = Manager.AmbientOcclusionStrength;
		}

		CompositeLighting(aoStrength);

		if (PerfToggles.LightingNoise)
			ApplyLightingNoise(LitScreen);

		return LitScreen;
	}

	void ApplyLightingNoise(Target target)
	{
		if (Assets.LightingNoise == null)
			return;

		var renderCamera = RenderCamera;
		Batcher.PushMatrix(Matrix3x2.CreateScale(zoomLevel));
		Batcher.PushBlend(BlendMode.Multiply);
		Batcher.PushSampler(new(TextureFilter.Nearest, TextureWrap.Repeat, TextureWrap.Repeat));
		Batcher.Image(
			Assets.LightingNoise,
			new Rect(renderCamera.X - Time.SecondsF * 5.0f, renderCamera.Y, ZoomedWidth, ZoomedHeight),
			Vector2.Zero,
			Color.White * CurrentLightNoise());
		Batcher.PopSampler();
		Batcher.PopBlend();
		Batcher.PopMatrix();
		Batcher.Render(target);
		Batcher.Clear();
	}

	Target ApplyPixelSortGlitch(Target input)
	{
		if (pixelSortGlitchMaterial == null || ditherGlitch <= 0.001f)
			return input;

		GlitchScreen.Clear(Color.Transparent);
		pixelSortGlitchMaterial.Material.Fragment.Samplers[1] = new(TileField, new(TextureFilter.Nearest, TextureWrap.Clamp));
		var glitchPosition = PlayerDitherGlitchPosition();
		var renderCamera = RenderCamera;
		Span<float> uniforms = stackalloc float[12 + DitherAnchorFloatCount];
		var i = 0;
		uniforms[i++] = input.Width;
		uniforms[i++] = input.Height;
		uniforms[i++] = renderCamera.X;
		uniforms[i++] = renderCamera.Y;
		uniforms[i++] = ditherGlitch;
		uniforms[i++] = glitchPosition.X;
		uniforms[i++] = glitchPosition.Y;
		uniforms[i++] = DitherGlitchRadius;
		uniforms[i++] = Time.SecondsF;
		uniforms[i++] = ditherGlitchAxis.X;
		uniforms[i++] = ditherGlitchAxis.Y;
		uniforms[i++] = zoomLevel;
		ditherAnchorOffsets.CopyTo(uniforms[i..]);
		pixelSortGlitchMaterial.Material.Fragment.SetUniformBuffer(uniforms);

		Batcher.PushSampler(new(TextureFilter.Nearest, TextureWrap.Clamp, TextureWrap.Clamp));
		Batcher.PushMaterial(pixelSortGlitchMaterial.Material);
		Batcher.Image(input, input.Bounds.Center, input.Bounds.Size / 2, Vector2.One, 0, Color.White);
		Batcher.PopMaterial();
		Batcher.PopSampler();
		Batcher.Render(GlitchScreen);
		Batcher.Clear();
		return GlitchScreen;
	}

	void CompositeLighting(float aoStrength)
	{
		var material = lightingCompositeMaterial!.Material;

		LitScreen.Clear(Color.Transparent);
		material.Fragment.Samplers[1] = new(BackgroundLightMask, new(TextureFilter.Linear, TextureWrap.Clamp));
		material.Fragment.Samplers[2] = new(TileField, new(TextureFilter.Nearest, TextureWrap.Clamp));
		material.Fragment.Samplers[3] = new(DitherMask, new(TextureFilter.Nearest, TextureWrap.Clamp));
		material.Fragment.Samplers[4] = new(BackgroundEmissive, new(TextureFilter.Nearest, TextureWrap.Clamp));
		material.Fragment.Samplers[5] = new(AoMask, new(TextureFilter.Linear, TextureWrap.Clamp));
		material.Fragment.Samplers[6] = new(BackgroundCoverage, new(TextureFilter.Nearest, TextureWrap.Clamp));
		material.Fragment.Samplers[7] = new(ForegroundScreen, new(TextureFilter.Nearest, TextureWrap.Clamp));
		material.Fragment.Samplers[8] = new(ForegroundLightMask, new(TextureFilter.Linear, TextureWrap.Clamp));
		material.Fragment.Samplers[9] = new(ForegroundEmissive, new(TextureFilter.Nearest, TextureWrap.Clamp));
		material.Fragment.Samplers[10] = new(NormalBuffer, new(TextureFilter.Nearest, TextureWrap.Clamp));
		var glitchPosition = PlayerDitherGlitchPosition();
		var renderCamera = RenderCamera;
		var aoOffset = Manager.AmbientOcclusionOffset;
		Span<float> uniforms = stackalloc float[24 + DitherAnchorFloatCount];
		var i = 0;
		uniforms[i++] = LitScreen.Width;
		uniforms[i++] = LitScreen.Height;
		uniforms[i++] = renderCamera.X;
		uniforms[i++] = renderCamera.Y;
		uniforms[i++] = Manager.LightingDitherEnabled ? 1.0f : 0.0f;
		uniforms[i++] = Manager.LightingDitherScale;
		uniforms[i++] = Manager.LightingDitherStrength;
		uniforms[i++] = ditherGlitch;
		uniforms[i++] = glitchPosition.X;
		uniforms[i++] = glitchPosition.Y;
		uniforms[i++] = DitherGlitchRadius;
		uniforms[i++] = Time.SecondsF;
		uniforms[i++] = ditherGlitchAxis.X;
		uniforms[i++] = ditherGlitchAxis.Y;
		uniforms[i++] = zoomLevel;
		uniforms[i++] = 0;
		uniforms[i++] = aoStrength;
		uniforms[i++] = aoOffset.X;
		uniforms[i++] = aoOffset.Y;
		uniforms[i++] = Manager.AmbientOcclusionMinVisibility;
		uniforms[i++] = Manager.NormalDitherSlopeBoost;
		uniforms[i++] = Manager.NormalDitherQuantization ? NormalDitherLevels : 0;
		uniforms[i++] = 0;
		uniforms[i++] = 0;
		ditherAnchorOffsets.CopyTo(uniforms[i..]);
		material.Fragment.SetUniformBuffer(uniforms);

		Batcher.PushSampler(new(TextureFilter.Nearest, TextureWrap.Clamp, TextureWrap.Clamp));
		Batcher.PushMaterial(material);
		Batcher.Image(BackgroundScreen, BackgroundScreen.Bounds.Center, BackgroundScreen.Bounds.Size / 2, Vector2.One, 0, Color.White);
		Batcher.PopMaterial();
		Batcher.PopSampler();
		Batcher.Render(LitScreen);
		Batcher.Clear();
	}

	// Renders background tiles (without the backdrop fill) so their alpha marks
	// where real tiles exist. The AO composite gates on this so the backdrop is
	// never darkened where no background tiles are present.
	void DrawBackgroundCoverage()
	{
		BackgroundCoverage.Clear(Color.Transparent);
		if (coverageMaskMaterial == null)
			return;
		if (!ShowBackgroundLayer)
		{
			Batcher.Render(BackgroundCoverage);
			Batcher.Clear();
			return;
		}

		Batcher.PushMaterial(coverageMaskMaterial.Material);
		PushWorldMatrix();
		foreach (var actor in rendering)
		{
			if (!actor.Visible || !IsBackgroundActor(actor) || actor is not Grid and not DualGrid)
				continue;

			Batcher.PushMatrix(actor.Position);
			actor.Render(Batcher);
			Batcher.PopMatrix();
		}
		Batcher.PopMatrix();
		Batcher.PopMaterial();
		Batcher.Render(BackgroundCoverage);
		Batcher.Clear();
	}

	// Blurs the foreground coverage (ForegroundScreen alpha) into AoMask using a
	// separable two-pass Gaussian. The result is the soft occluder field the
	// background composite samples to darken tiles beneath foreground overhangs.
	void BuildAoMask()
	{
		var radius = Manager.AmbientOcclusionBlur;
		RunBlurPass(ForegroundScreen, AoBlurScratch, new Vector2(1, 0), radius, sourceAlpha: true);
		RunBlurPass(AoBlurScratch, AoMask, new Vector2(0, 1), radius, sourceAlpha: false);
	}

	void RunBlurPass(Target source, Target destination, Vector2 direction, float radius, bool sourceAlpha)
	{
		destination.Clear(Color.Transparent);
		blurMaterial!.Material.Fragment.SetUniformBuffer([
			direction.X,
			direction.Y,
			radius,
			0,
			1.0f / source.Width,
			1.0f / source.Height,
			0,
			0,
			// ChannelMask: extract foreground coverage from alpha on the first
			// pass, then the single-channel scratch value (red) on the second.
			sourceAlpha ? 0.0f : 1.0f,
			0,
			0,
			sourceAlpha ? 1.0f : 0.0f
		]);

		Batcher.PushSampler(new(TextureFilter.Linear, TextureWrap.Clamp, TextureWrap.Clamp));
		Batcher.PushMaterial(blurMaterial.Material);
		Batcher.Image(source, source.Bounds.Center, source.Bounds.Size / 2, Vector2.One, 0, Color.White);
		Batcher.PopMaterial();
		Batcher.PopSampler();
		Batcher.Render(destination);
		Batcher.Clear();
	}

	void DrawTileField()
	{
		Array.Clear(ditherAnchorOffsets);
		RoomTileFieldCache? currentRoomTileFieldCache = null;
		RoomTileFieldCache? nextRoomTileFieldCache = null;
		if (room != null)
			currentRoomTileFieldCache = GetRoomTileFieldCache(room);
		if (nextRoom != null && nextRoom != room)
			nextRoomTileFieldCache = GetRoomTileFieldCache(nextRoom);

		TileField.Clear(Color.Transparent);
		PushWorldMatrix();
		Batcher.PushSampler(new(TextureFilter.Nearest, TextureWrap.Clamp, TextureWrap.Clamp));
		if (currentRoomTileFieldCache != null)
			DrawCachedRoomTileField(currentRoomTileFieldCache);
		if (nextRoomTileFieldCache != null)
			DrawCachedRoomTileField(nextRoomTileFieldCache);
		DrawMovingPlatformTileFields();
		Batcher.PopSampler();
		Batcher.PopMatrix();
		Batcher.Render(TileField);
		Batcher.Clear();
	}

	void DrawCachedRoomTileField(RoomTileFieldCache cache) =>
		Batcher.Image(cache.Target, cache.Room.WorldBounds.TopLeft, Color.White);

	// State for tileFieldStampColor so the per-vertex callback can be cached once instead of
	// reallocating a closure per platform per frame.
	char tileFieldStampTile;
	int tileFieldStampAnchor;
	Func<Point2, Color>? tileFieldStampColor;

	void DrawMovingPlatformTileFields()
	{
		if (!ShowForegroundLayer || tileFieldStampMaterial == null)
			return;

		var anchor = 1;
		Batcher.PushBlend(TileFieldStampBlend);
		Batcher.PushMaterial(tileFieldStampMaterial.Material);
		// Cached delegate + mutable state (per-platform tile/anchor) so the per-vertex color callback
		// doesn't allocate a closure each frame; see drawLightMaskLightAction for the same idiom.
		tileFieldStampColor ??= worldTile => TileFieldColor(tileFieldStampTile, worldTile, tileFieldStampAnchor);
		foreach (var actor in rendering)
		{
			if (actor is not MovingPlatform { Visible: true } platform)
				continue;

			var anchorId = anchor < MaxDitherAnchors ? anchor++ : 0;
			if (anchorId > 0)
			{
				var offset = platform.DitherAnchorOffset;
				var at = anchorId * 4;
				ditherAnchorOffsets[at] = offset.X;
				ditherAnchorOffsets[at + 1] = offset.Y;
			}

			tileFieldStampTile = platform.MaterialTile;
			tileFieldStampAnchor = anchorId;
			Batcher.PushMatrix(platform.Position);
			platform.RenderTileField(Batcher, tileFieldStampColor);
			Batcher.PopMatrix();
		}
		Batcher.PopMaterial();
		Batcher.PopBlend();
	}

	RoomTileFieldCache GetRoomTileFieldCache(Room renderRoom)
	{
		var bounds = renderRoom.WorldBounds;
		if (!roomTileFieldCaches.TryGetValue(renderRoom, out var cache) ||
			cache.Target.Width != bounds.Width ||
			cache.Target.Height != bounds.Height ||
			cache.Target.Attachments[0].Format != TextureFormat.R8G8)
		{
			cache?.Dispose();
			cache = new(renderRoom, CreateRenderTarget(bounds.Width, bounds.Height, TextureFormat.R8G8, $"RoomTileField:{renderRoom.Id}"));
			roomTileFieldCaches[renderRoom] = cache;
		}

		if (cache.Dirty)
			RebuildRoomTileFieldCache(cache);

		return cache;
	}

	internal void MarkRoomTileFieldCacheDirty(Room renderRoom)
	{
		if (roomTileFieldCaches.TryGetValue(renderRoom, out var cache))
			cache.Dirty = true;
	}

	void RebuildRoomTileFieldCache(RoomTileFieldCache cache)
	{
		var renderRoom = cache.Room;
		var target = cache.Target;
		var previousMatrix = Batcher.Matrix;
		Batcher.Matrix = Matrix3x2.CreateTranslation(-renderRoom.WorldBounds.TopLeft);

		target.Clear(Color.Transparent);
		Batcher.PushSampler(new(TextureFilter.Nearest, TextureWrap.Clamp, TextureWrap.Clamp));
		Batcher.PushBlend(TileFieldStampBlend);
		DrawRoomTileField(renderRoom);
		Batcher.PopBlend();
		Batcher.PopSampler();
		Batcher.Render(target);
		Batcher.Clear();
		Batcher.Matrix = previousMatrix;
		cache.Dirty = false;
	}

	void DrawDitherMask()
	{
		DitherMask.Clear(Color.White);
		PushWorldMatrix();
		foreach (var actor in rendering)
		{
			if (!actor.Visible)
				continue;

			var amount = DitherAmountForActor(actor);
			if (amount >= 0.999f)
				continue;

			Batcher.PushMatrix(actor.Position);
			actor.RenderShadow(Batcher, new Color(amount, amount, amount, 1.0f));
			Batcher.PopMatrix();
		}
		Batcher.PopMatrix();
		Batcher.Render(DitherMask);
		Batcher.Clear();
	}

	static float DitherAmountForActor(Actor actor)
		=> actor.Mask.Has(Actor.Masks.Player | Actor.Masks.Enemy) ? ActorDitherAmount : 1.0f;

	void DrawParticleSolidMask()
	{
		ParticleSolidMask.Clear(Color.Transparent);
		if (room == null || coverageMaskMaterial == null)
			return;

		Batcher.PushMaterial(coverageMaskMaterial.Material);
		PushWorldMatrix();
		foreach (var rect in room.CollisionRects)
			Batcher.Rect(rect, Color.White);
		foreach (var slope in room.SlopeTiles)
		{
			var polygon = room.GetSlopePolygon(slope);
			if (polygon.Count < 3)
				continue;
			for (var i = 1; i + 1 < polygon.Count; i++)
				Batcher.Triangle(polygon[0], polygon[i], polygon[i + 1], Color.White);
		}
		foreach (var actor in Actors)
		{
			if (!ParticleCollidesWith(actor))
				continue;
			if (actor.Mask.Has(Actor.Masks.Jumpthru))
			{
				Batcher.Rect(actor.Hitbox.Rect + actor.Position, Color.White);
				continue;
			}
			Batcher.PushMatrix(actor.Position);
			actor.RenderShadow(Batcher, Color.White);
			Batcher.PopMatrix();
		}
		Batcher.PopMatrix();
		Batcher.PopMaterial();
		Batcher.Render(ParticleSolidMask);
		Batcher.Clear();
	}

	static bool ParticleCollidesWith(Actor actor)
	{
		if (!actor.Visible || actor is Grid || actor.Hitbox.Shape != Hitbox.Shapes.Rect)
			return false;
		return actor is Pawn and not Player || actor.Mask.Has(Actor.Masks.Solid | Actor.Masks.Jumpthru | Actor.Masks.Enemy | Actor.Masks.Hazard);
	}

	void DrawRoomTileField(Room renderRoom)
	{
		DrawRoomTileFieldBaseline(renderRoom);
		if (tileFieldStampMaterial != null)
			Batcher.PushMaterial(tileFieldStampMaterial.Material);
		DrawRoomVisualTileField(renderRoom);
		DrawRoomDualTileField(renderRoom, Factory.TileLayer.Foreground);
		if (tileFieldStampMaterial != null)
			Batcher.PopMaterial();
	}

	void DrawRoomTileFieldBaseline(Room renderRoom)
	{
		var baselineTile = tileFieldStampMaterial == null ? (char?)null : '0';
		var drawRect = new Rect(0, 0, TileSize, TileSize);
		for (var y = 0; y < renderRoom.Size.Y; y++)
			for (var x = 0; x < renderRoom.Size.X; x++)
			{
				var worldTile = renderRoom.TileSpacePosition + new Point2(x, y);
				drawRect.Position = renderRoom.WorldBounds.Position + new Point2(x, y) * TileSize;
				Batcher.Rect(drawRect, TileFieldColor(baselineTile ?? renderRoom.Tiles[x, y], worldTile));
			}
	}

	void DrawRoomVisualTileField(Room renderRoom)
	{
		for (var y = 0; y < renderRoom.Size.Y; y++)
			for (var x = 0; x < renderRoom.Size.X; x++)
			{
				var tile = renderRoom.Tiles[x, y];
				if (Factory.Find(tile) is not { Layer: Factory.TileLayer.Foreground, DualTilesetName: null } entry)
					continue;

				var image = entry.Image;
				if (entry is { IsSlope: true, SlopeTilesetName: not null } && renderRoom.TryGetSlopeTile(new Point2(x, y), out var slope))
				{
					var seed = Factory.StableVariantSeed(renderRoom, entry.SlopeTilesetName, new Point2(x, y));
					image = Assets.GetSlopeSubtexture(entry.SlopeTilesetName, slope.Direction, slope.Fill, slope.RunLength, slope.RunIndex, seed);
				}
				else if (entry.TilesetName != null && Assets.GetTileset(entry.TilesetName) is { } tileset)
				{
					Rng localRng = new(x + y * renderRoom.Size.X);
					image = tileset.GetRandomTile(ref localRng);
				}

				if (image.Texture == null)
					continue;

				var worldTile = renderRoom.TileSpacePosition + new Point2(x, y);
				Batcher.Image(
					image,
					new Vector2(renderRoom.WorldBounds.X + x * TileSize, renderRoom.WorldBounds.Y + y * TileSize),
					TileFieldColor(tile, worldTile));
			}
	}

	void DrawRoomDualTileField(Room renderRoom, Factory.TileLayer layer)
	{
		foreach (var (tileType, entry) in Factory.Entries)
		{
			if (entry.Layer != layer ||
				entry.DualTilesetName == null ||
				Assets.GetTileset(entry.DualTilesetName) is not { } tileset)
				continue;

			for (var y = 0; y <= renderRoom.Size.Y; y++)
				for (var x = 0; x <= renderRoom.Size.X; x++)
				{
					var materialMask = 0;
					if (renderRoom.IsDualMaterialFilled(tileType, layer, x - 1, y - 1))
						materialMask |= TileQuadrants.TopLeft;
					if (renderRoom.IsDualMaterialFilled(tileType, layer, x - 1, y))
						materialMask |= TileQuadrants.BottomLeft;
					if (renderRoom.IsDualMaterialFilled(tileType, layer, x, y - 1))
						materialMask |= TileQuadrants.TopRight;
					if (renderRoom.IsDualMaterialFilled(tileType, layer, x, y))
						materialMask |= TileQuadrants.BottomRight;

					if (materialMask == 0)
						continue;

					var joinMask = layer == Factory.TileLayer.Foreground
						? renderRoom.GetDualTerrainJoinMask(x, y)
						: materialMask;
					if (joinMask == 0)
						continue;

					var dualTile = DualGrid.MaskToTile[joinMask];
					var seed = StableDualVariantSeed(renderRoom, tileType, x, y);
					var image = tileset.GetVariantTile(dualTile.X, dualTile.Y, seed);
					if (image.Texture == null)
						continue;

					var position = new Vector2(
						renderRoom.WorldBounds.X + (x - 0.5f) * TileSize,
						renderRoom.WorldBounds.Y + (y - 0.5f) * TileSize);
					var color = TileFieldColor(tileType, renderRoom.TileSpacePosition + new Point2(x, y));
					DrawDualTileFieldQuadrants(image, position, materialMask, color);
				}
		}
	}

	void DrawDualTileFieldQuadrants(Subtexture image, Vector2 position, int materialMask, Color color)
	{
		if (materialMask == TileQuadrants.All)
		{
			Batcher.Image(image, position, color);
			return;
		}

		const int halfTileSize = (TileSize / 2);

		DrawDualTileFieldQuadrant(image, position, materialMask, color, TileQuadrants.TopLeft, new Rect(0, 0, halfTileSize, halfTileSize));
		DrawDualTileFieldQuadrant(image, position, materialMask, color, TileQuadrants.BottomLeft, new Rect(0, halfTileSize, halfTileSize, halfTileSize));
		DrawDualTileFieldQuadrant(image, position, materialMask, color, TileQuadrants.TopRight, new Rect(halfTileSize, 0, halfTileSize, halfTileSize));
		DrawDualTileFieldQuadrant(image, position, materialMask, color, TileQuadrants.BottomRight, new Rect(halfTileSize, halfTileSize, halfTileSize, halfTileSize));
	}

	void DrawDualTileFieldQuadrant(Subtexture image, Vector2 position, int materialMask, Color color, int mask, Rect clip)
	{
		if ((materialMask & mask) == 0)
			return;

		Batcher.Image(image, clip, position + clip.Position, Vector2.Zero, Vector2.One, 0, color);
	}

	static int StableDualVariantSeed(Room room, char tileType, int x, int y)
	{
		unchecked
		{
			var worldTile = room.WorldBounds.TopLeft / TileSize + new Point2(x, y);
			var hash = (uint)worldTile.X * 0x8da6b343u;
			hash ^= (uint)worldTile.Y * 0xd8163841u;
			hash ^= tileType * 0xcb1ab31fu;
			hash ^= hash >> 16;
			hash *= 0x7feb352du;
			hash ^= hash >> 15;
			hash *= 0x846ca68bu;
			hash ^= hash >> 16;
			return (int)hash;
		}
	}

	static Color TileFieldColor(char tile, Point2 worldTile, int anchor = 0)
	{
		var hash = TileFieldHash(tile, worldTile);
		var variant = (hash & 255) / 255.0f;
		var family = TileFieldFamily(tile);
		return new Color(variant, ((family << 5) | anchor) / 255.0f, 0.0f, 1.0f);
	}

	static int TileFieldFamily(char tile)
		=> tile switch
		{
			'0' => 0,
			'1' or '2' or 'x' => 1,
			'G' or 'S' or 'X' or 'g' => 2,
			'w' => 3,
			_ => 4
		};

	static uint TileFieldHash(char tile, Point2 worldTile)
	{
		var hash = 2166136261u;
		hash = (hash ^ (uint)worldTile.X) * 16777619u;
		hash = (hash ^ (uint)worldTile.Y) * 16777619u;
		hash = (hash ^ tile) * 16777619u;
		return hash;
	}

	// State for drawLightMaskLightAction so the delegate can be cached once instead
	// of reallocating a closure per actor per frame (DrawLightMask runs twice a frame,
	// and ForEachLight is called for every actor).
	Target? lightMaskTarget;
	bool lightMaskIncludeShadows;
	Action<Vector2, float, float, float, Color>? drawLightMaskLightAction;

	void DrawLightMask(Target lightMask, bool includeShadows)
	{
		lightMask.Clear(ScaleColor(CurrentAmbientColor(), CurrentAmbientIntensity()));

		// Dynamic occluders move, so they can't bake into a room's static occluder lists; gather
		// them once here (they're the same for every light this frame) and let the sun, point and
		// fallback shadow passes share the result.
		if (includeShadows)
			GatherDynamicOccluders();

		if (CurrentSunStrength() > SunStrengthEpsilon)
		{
			DrawSunLightMask(includeShadows);
			AddSingleLightMask(lightMask);
		}

		lightMaskTarget = lightMask;
		lightMaskIncludeShadows = includeShadows;
		drawLightMaskLightAction ??= (position, radius, intensity, height, lightColor) =>
		{
			DrawSingleLightMask(position, radius, intensity, height, lightColor, lightMaskIncludeShadows);
			AddSingleLightMask(lightMaskTarget!);
		};

		foreach (var actor in Actors)
		{
			if (actor is Light light && (game.IsEditorPreview || !LightIsCovered(light.Position)))
			{
				DrawSingleLightMask(light, includeShadows);
				AddSingleLightMask(lightMask);
			}

			actor.ForEachLight(drawLightMaskLightAction);
		}

	}

	bool LightIsCovered(Vector2 position)
	{
		foreach (var actor in Actors)
			if (actor is HiddenCoverGrid hiddenCover && hiddenCover.Covers(position))
				return true;
		return false;
	}

	Color CurrentAmbientColor()
	{
		if (nextRoom == null)
			return room?.AmbientColor ?? Room.DefaultAmbientColor;

		return LerpColor(transitionAmbientColorFrom, transitionAmbientColorTo, Ease.Cube.InOut(nextRoomEase));
	}

	(Color Top, Color Bottom) CurrentBackdropColors()
	{
		if (nextRoom == null)
		{
			var top = room?.BackdropColor ?? Room.DefaultBackdropColor;
			return (top, room?.BackdropColor2 ?? top);
		}

		var amount = Ease.Cube.InOut(nextRoomEase);
		return (
			LerpColor(transitionBackdropColorFrom, transitionBackdropColorTo, amount),
			LerpColor(transitionBackdropColor2From, transitionBackdropColor2To, amount));
	}

	float CurrentAmbientIntensity()
	{
		if (nextRoom == null)
			return room?.AmbientIntensity ?? Room.DefaultAmbientIntensity;

		return float.Lerp(transitionAmbientIntensityFrom, transitionAmbientIntensityTo, Ease.Cube.InOut(nextRoomEase));
	}

	float CurrentLightNoise()
	{
		if (nextRoom == null)
			return room?.LightNoise ?? Room.DefaultLightNoise;

		return float.Lerp(transitionLightNoiseFrom, transitionLightNoiseTo, Ease.Cube.InOut(nextRoomEase));
	}

	Color CurrentSunColor()
	{
		if (nextRoom == null)
			return room?.SunColor ?? Room.DefaultSunColor;

		return LerpColor(transitionSunColorFrom, transitionSunColorTo, Ease.Cube.InOut(nextRoomEase));
	}

	float CurrentSunStrength()
	{
		if (nextRoom == null)
			return room?.SunStrength ?? 0.0f;

		return float.Lerp(transitionSunStrengthFrom, transitionSunStrengthTo, Ease.Cube.InOut(nextRoomEase));
	}

	Vector2 CurrentSunDirection()
	{
		if (nextRoom == null)
			return room?.SunDirection ?? Room.DefaultSunDirection;

		var direction = Vector2.Lerp(transitionSunDirectionFrom, transitionSunDirectionTo, Ease.Cube.InOut(nextRoomEase));
		return Room.NormalizeOrDefault(direction, Room.DefaultSunDirection);
	}

	static Color LerpColor(Color from, Color to, float amount)
		=> new(Vector4.Lerp(from, to, amount));

	const float SunStrengthEpsilon = 0.001f;

	void DrawSunLightMask(bool includeShadows)
	{
		SingleLightMask.Clear(Color.Transparent);
		var strength = CurrentSunStrength();

		var direction = CurrentSunDirection();
		var lightColor = CurrentSunColor();
		if (normalLightMaterial != null)
		{
			var lightColorValue = (Vector4)lightColor;
			normalLightMaterial.Material.Fragment.SetUniformBuffer([
				NormalBuffer.Width,
				NormalBuffer.Height,
				0,
				0,
				direction.X,
				direction.Y,
				-1.0f,
				1.0f,
				lightColorValue.X,
				lightColorValue.Y,
				lightColorValue.Z,
				strength
			]);

			Batcher.PushSampler(new(TextureFilter.Nearest, TextureWrap.Clamp, TextureWrap.Clamp));
			Batcher.PushMaterial(normalLightMaterial.Material);
			Batcher.Image(NormalBuffer, NormalBuffer.Bounds.Center, NormalBuffer.Bounds.Size / 2, Vector2.One, 0, Color.White);
			Batcher.PopMaterial();
			Batcher.PopSampler();
		}
		else
		{
			Batcher.Rect(new Rect(0, 0, SingleLightMask.Width, SingleLightMask.Height), ScaleColor(lightColor, strength));
		}

		if (includeShadows)
		{
			PushWorldMatrix();
			Batcher.PushBlend(ShadowBlend);
			DrawSunShadows(direction);
			Batcher.PopBlend();
			Batcher.PopMatrix();
		}

		Batcher.Render(SingleLightMask);
		Batcher.Clear();

		if (includeShadows && SoftShadowsEnabled)
			SoftenSingleLightMask(Vector2.Zero, 0, uniform: true);
	}

	void DrawSingleLightMask(Light light, bool includeShadows)
		=> DrawSingleLightMask(
			light.Position,
			light.Radius * light.FlickerScale,
			light.Intensity * light.FlickerScale,
			light.Height,
			light.LightColor,
			includeShadows);

	void DrawSingleLightMask(Vector2 position, float radius, float intensity, float height, Color lightColor, bool includeShadows)
	{
		SingleLightMask.Clear(Color.Transparent);
		var drewVisibilityClippedLight = false;

		if (normalLightMaterial != null)
		{
			var screenPosition = (position - RenderCamera) * zoomLevel;
			var lightColorValue = (Vector4)lightColor;
			normalLightMaterial.Material.Fragment.SetUniformBuffer([
				NormalBuffer.Width,
				NormalBuffer.Height,
				0,
				0,
				screenPosition.X,
				screenPosition.Y,
				radius * zoomLevel,
				height * zoomLevel,
				lightColorValue.X,
				lightColorValue.Y,
				lightColorValue.Z,
				intensity
			]);

			Batcher.PushSampler(new(TextureFilter.Nearest, TextureWrap.Clamp, TextureWrap.Clamp));
			Batcher.PushMaterial(normalLightMaterial.Material);
			if (includeShadows)
			{
				DrawVisibilityClippedNormalLight(position, radius);
				drewVisibilityClippedLight = true;
			}
			else
			{
				Batcher.Image(NormalBuffer, NormalBuffer.Bounds.Center, NormalBuffer.Bounds.Size / 2, Vector2.One, 0, Color.White);
			}
			Batcher.PopMaterial();
			Batcher.PopSampler();
		}
		else
		{
			PushWorldMatrix();
			var color = ScaleColor(lightColor, intensity);
			Batcher.Circle(position, radius, 32, color, Color.Transparent);
			Batcher.PopMatrix();
		}

		if (includeShadows && !drewVisibilityClippedLight)
		{
			PushWorldMatrix();
			Batcher.PushBlend(ShadowBlend);
			DrawLightShadows(position, radius);
			Batcher.PopBlend();
			Batcher.PopMatrix();
		}

		Batcher.Render(SingleLightMask);
		Batcher.Clear();

		if (includeShadows && SoftShadowsEnabled)
		{
			var screenPosition = (position - RenderCamera) * zoomLevel;
			SoftenSingleLightMask(screenPosition, radius * zoomLevel, uniform: false);
		}
	}

	static readonly List<VisibilityPolygon.Hit> hits = new();
	static readonly List<VisibilityPolygon.Segment> _segments = new();

	void DrawVisibilityClippedNormalLight(Vector2 lightPosition, float radius)
	{
		GatherVisibilitySegments(lightPosition, radius, _segments);
		VisibilityPolygon.Compute(lightPosition, radius, _segments, hits);
		if (hits.Count < 3)
		{
			Batcher.Image(NormalBuffer, NormalBuffer.Bounds.Center, NormalBuffer.Bounds.Size / 2, Vector2.One, 0, Color.White);
			return;
		}

		var renderCamera = RenderCamera;
		var screenLight = (lightPosition - renderCamera) * zoomLevel;
		var texture = NormalBuffer.Attachments[0];
		var textureSize = new Vector2(NormalBuffer.Width, NormalBuffer.Height);
		for (var i = 0; i < hits.Count; i++)
		{
			var a = (hits[i].Point - renderCamera) * zoomLevel;
			var b = (hits[(i + 1) % hits.Count].Point - renderCamera) * zoomLevel;
			Batcher.Triangle(
				texture,
				screenLight,
				a,
				b,
				screenLight / textureSize,
				a / textureSize,
				b / textureSize,
				Color.White);
		}
	}

	bool SoftShadowsEnabled
		=> PerfToggles.SoftShadows && softShadowMaterial != null && Manager.SoftShadowPenumbra > 0.01f;

	// Softens the just-rendered per-light SingleLightMask with a separable
	// variable-radius blur before it is accumulated. The blur widens with
	// distance from the light, so shadows stay crisp near their caster and
	// soften further away. The sun blurs uniformly (uniform: true).
	void SoftenSingleLightMask(Vector2 lightScreenPosition, float lightRadiusPixels, bool uniform)
	{
		var maxPenumbra = Manager.SoftShadowPenumbra;
		RunSoftShadowPass(SingleLightMask, SoftShadowScratch, new Vector2(1, 0), maxPenumbra, lightScreenPosition, lightRadiusPixels, uniform);
		RunSoftShadowPass(SoftShadowScratch, SingleLightMask, new Vector2(0, 1), maxPenumbra, lightScreenPosition, lightRadiusPixels, uniform);
	}

	void RunSoftShadowPass(Target source, Target destination, Vector2 direction, float maxPenumbra, Vector2 lightScreenPosition, float lightRadiusPixels, bool uniform)
	{
		destination.Clear(Color.Transparent);
		softShadowMaterial!.Material.Fragment.SetUniformBuffer([
			direction.X,
			direction.Y,
			maxPenumbra,
			uniform ? 1.0f : 0.0f,
			lightScreenPosition.X,
			lightScreenPosition.Y,
			lightRadiusPixels,
			Manager.SoftShadowMinSoftness,
			1.0f / source.Width,
			1.0f / source.Height,
			0,
			0
		]);

		Batcher.PushSampler(new(TextureFilter.Linear, TextureWrap.Clamp, TextureWrap.Clamp));
		Batcher.PushMaterial(softShadowMaterial.Material);
		Batcher.Image(source, source.Bounds.Center, source.Bounds.Size / 2, Vector2.One, 0, Color.White);
		Batcher.PopMaterial();
		Batcher.PopSampler();
		Batcher.Render(destination);
		Batcher.Clear();
	}

	void AddSingleLightMask(Target lightMask)
	{
		Batcher.PushBlend(new(BlendOp.Add, BlendFactor.One, BlendFactor.One));
		Batcher.PushSampler(new(TextureFilter.Linear, TextureWrap.Clamp, TextureWrap.Clamp));
		Batcher.Image(SingleLightMask, SingleLightMask.Bounds.Center, SingleLightMask.Bounds.Size / 2, Vector2.One, 0, Color.White);
		Batcher.PopSampler();
		Batcher.PopBlend();
		Batcher.Render(lightMask);
		Batcher.Clear();
	}

	void DrawLightShadows(Vector2 lightPosition, float radius)
	{
		if (room != null)
			DrawRoomLightShadows(room, lightPosition, radius);
		if (nextRoom != null && nextRoom != room)
			DrawRoomLightShadows(nextRoom, lightPosition, radius);

		foreach (var actor in Actors)
		{
			if (actor is not HiddenWallGrid hiddenWalls)
				continue;

			hiddenWalls.ForEachVisibleCell((self: this, lightPosition, radius), static (s, rect, _, _) =>
			{
				if (CircleOverlapsRect(s.lightPosition, s.radius, rect))
					s.self.DrawShadowVolume(s.lightPosition, s.radius, rect);
			});
		}

		DrawActorSpriteShadows(lightPosition, radius);
	}

	// Struct enumerable (value-type GetEnumerator) so `foreach (… in VisibleRooms())`
	// in per-light/per-frame paths doesn't heap-allocate an iterator each call.
	internal VisibleRoomsEnumerable VisibleRooms() => new(room, nextRoom);

	internal readonly struct VisibleRoomsEnumerable(Room? room, Room? nextRoom)
	{
		public Enumerator GetEnumerator() => new(room, nextRoom);

		public struct Enumerator(Room? room, Room? nextRoom)
		{
			int index = -1;
			public Room Current { get; private set; } = null!;

			public bool MoveNext()
			{
				while (++index < 2)
				{
					var candidate = index == 0 ? room : nextRoom;
					if (candidate == null || (index == 1 && candidate == room))
						continue;
					Current = candidate;
					return true;
				}
				return false;
			}
		}
	}

	void DrawSunShadows(Vector2 direction)
	{
		var distance = SunShadowDistance();
		foreach (var visibleRoom in VisibleRooms())
			DrawRoomSunShadows(visibleRoom, direction, distance);

		foreach (var actor in Actors)
		{
			if (actor is HiddenCoverGrid hiddenCover)
				hiddenCover.AppendAllVisibilitySegments(sunSegmentBuffer);
			if (actor is DualGrid { Layer: Factory.TileLayer.Foreground } dualGrid)
				dualGrid.AppendAllVisibilitySegments(sunSegmentBuffer);
		}
		sunSegmentBuffer.AddRange(dynamicOccluderSegments);

		foreach (var segment in sunSegmentBuffer)
			DrawSunShadowSegment(segment, direction, distance);
		sunSegmentBuffer.Clear();

		foreach (var actor in Actors)
			if (actor is HiddenWallGrid hiddenWalls)
				hiddenWalls.ForEachVisibleCell((self: this, direction, distance), static (s, rect, _, _) =>
					s.self.DrawSunShadowVolume(s.direction, s.distance, rect));
	}

	readonly List<VisibilityPolygon.Segment> sunSegmentBuffer = [];

	// Per-frame silhouette segments of IShapeOccluder actors (moving platforms, etc.), gathered once
	// in DrawLightMask and consumed by the visibility + sun passes -- the same segment currency tiles
	// and DualGrid use, so a platform's shadow traces its own (rounded) art, not a bounding box.
	readonly List<VisibilityPolygon.Segment> dynamicOccluderSegments = [];

	void GatherDynamicOccluders()
	{
		dynamicOccluderSegments.Clear();
		foreach (var actor in Actors)
			if (actor is IShapeOccluder { CastsShadow: true } occluder)
				occluder.AppendOccluderSegments(dynamicOccluderSegments);
	}

	float SunShadowDistance()
		=> MathF.Sqrt(NormalBuffer.Width * NormalBuffer.Width + NormalBuffer.Height * NormalBuffer.Height) + TileSize * MathF.Max(Columns, Rows) * 2;

	void DrawRoomSunShadows(Room renderRoom, Vector2 direction, float distance)
	{
		foreach (var rect in renderRoom.LightOccluders)
			DrawSunShadowVolume(direction, distance, rect);

		foreach (var polygon in renderRoom.LightOccluderPolygons)
			DrawSunShadowVolume(direction, distance, polygon.Points);
	}

	void DrawSunShadowVolume(Vector2 direction, float distance, RectInt rect)
	{
		Span<Point2> corners =
		[
			rect.TopLeft,
			rect.TopRight,
			rect.BottomRight,
			rect.BottomLeft
		];
		DrawSunShadowVolume(direction, distance, corners);
	}

	void DrawSunShadowVolume(Vector2 direction, float distance, ReadOnlySpan<Point2> points)
	{
		for (var i = 0; i < points.Length; i++)
		{
			var a = (Vector2)points[i];
			var b = (Vector2)points[(i + 1) % points.Length];
			if (!IsSunShadowCastingEdge(a, b, direction))
				continue;

			DrawSunShadowSegment(new(a, b), direction, distance);
		}
	}

	static bool IsSunShadowCastingEdge(Vector2 a, Vector2 b, Vector2 direction)
	{
		var edge = b - a;
		var outward = new Vector2(edge.Y, -edge.X);
		return Vector2.Dot(outward, direction) > 0.001f;
	}

	void DrawSunShadowSegment(VisibilityPolygon.Segment segment, Vector2 direction, float distance)
	{
		var offset = direction * distance;
		Batcher.Quad(segment.A, segment.B, segment.B + offset, segment.A + offset, Color.Black);
	}

	void DrawRoomLightShadows(Room renderRoom, Vector2 lightPosition, float radius)
	{
		foreach (var rect in renderRoom.LightOccluders)
			if (CircleOverlapsRect(lightPosition, radius, rect))
				DrawShadowVolume(lightPosition, radius, rect);

		foreach (var polygon in renderRoom.LightOccluderPolygons)
			if (CircleOverlapsRect(lightPosition, radius, polygon.Bounds))
				DrawShadowVolume(lightPosition, radius, polygon.Points, polygon.Bounds);
	}

	void DrawActorSpriteShadows(Vector2 lightPosition, float radius)
	{
		foreach (var actor in Actors)
		{
			if (!actor.Visible || !actor.CastsSpriteShadow)
				continue;
			if (actor is Grid or DualGrid)
				continue;

			var toActor = actor.Position - lightPosition;
			var distanceSquared = toActor.LengthSquared();
			if (distanceSquared <= 0.0001f || distanceSquared > radius * radius)
				continue;

			var distance = MathF.Sqrt(distanceSquared);
			var direction = toActor / distance;
			var reach = 1.0f - distance / radius;
			var shadowLength = ActorShadowMinLength + (ActorShadowMaxLength - ActorShadowMinLength) * reach;

			for (var i = 1; i <= ActorShadowSteps; i++)
			{
				var t = i / (float)ActorShadowSteps;
				var offset = direction * shadowLength * t;
				var alpha = ActorShadowStrength * 0.25f * (1.0f - t * 0.75f);

				Batcher.PushMatrix(actor.Position + offset);
				actor.RenderShadow(Batcher, Color.Black * alpha);
				Batcher.PopMatrix();
			}
		}
	}

	void DrawShadowVolume(Vector2 lightPosition, float radius, RectInt rect)
	{
		Span<Point2> corners =
		[
			rect.TopLeft,
			rect.TopRight,
			rect.BottomRight,
			rect.BottomLeft
		];
		DrawShadowVolume(lightPosition, radius, corners, rect);
	}

	void DrawShadowVolume(Vector2 lightPosition, float radius, ReadOnlySpan<Point2> points, RectInt bounds)
	{
		if (bounds.Contains(lightPosition))
			return;

		var bestA = (Vector2)points[0];
		var bestB = (Vector2)points[1];
		var bestSpan = -1.0f;

		for (var i = 0; i < points.Length; i++)
			for (var j = i + 1; j < points.Length; j++)
			{
				var pointA = (Vector2)points[i];
				var pointB = (Vector2)points[j];
				var a = DirectionFromLight(lightPosition, pointA);
				var b = DirectionFromLight(lightPosition, pointB);
				var span = 1 - Vector2.Dot(a, b);
				if (span > bestSpan)
				{
					bestSpan = span;
					bestA = pointA;
					bestB = pointB;
				}
			}

		var aFar = ProjectToLightRadius(lightPosition, radius, bestA);
		var bFar = ProjectToLightRadius(lightPosition, radius, bestB);
		Batcher.Quad(bestA, bestB, bFar, aFar, Color.Black);
	}

	void GatherVisibilitySegments(Vector2 lightPosition, float radius, List<VisibilityPolygon.Segment> segments)
	{
		segments.Clear();
		foreach (var visibleRoom in VisibleRooms())
			AddRoomVisibilitySegments(segments, visibleRoom, lightPosition, radius);

		foreach (var actor in Actors)
		{
			if (actor is DualGrid { Layer: Factory.TileLayer.Foreground } dualGrid)
			{
				dualGrid.AppendVisibilitySegments(segments, lightPosition, radius);
				continue;
			}
			if (actor is HiddenCoverGrid hiddenCover)
			{
				hiddenCover.AppendVisibilitySegments(segments, lightPosition, radius);
				continue;
			}

			if (actor is HiddenWallGrid hiddenWalls)
			{
				hiddenWalls.ForEachVisibleCell((segments, lightPosition, radius), static (s, rect, _, _) =>
					AddRectVisibilitySegments(s.segments, rect, s.lightPosition, s.radius));
			}
		}

		foreach (var segment in dynamicOccluderSegments)
			if (VisibilityPolygon.CircleOverlaps(segment, lightPosition, radius))
				segments.Add(segment);
	}

	static void AddRoomVisibilitySegments(List<VisibilityPolygon.Segment> segments, Room renderRoom, Vector2 lightPosition, float radius)
	{
		if (!CircleOverlapsRect(lightPosition, radius, renderRoom.WorldBounds))
			return;

		foreach (var segment in renderRoom.VisibilitySegments)
			if (VisibilityPolygon.CircleOverlaps(segment, lightPosition, radius))
				segments.Add(segment);
	}

	static void AddRectVisibilitySegments(List<VisibilityPolygon.Segment> segments, RectInt rect, Vector2 lightPosition, float radius)
	{
		AddVisibilitySegment(segments, rect.TopLeft, rect.TopRight, lightPosition, radius);
		AddVisibilitySegment(segments, rect.TopRight, rect.BottomRight, lightPosition, radius);
		AddVisibilitySegment(segments, rect.BottomRight, rect.BottomLeft, lightPosition, radius);
		AddVisibilitySegment(segments, rect.BottomLeft, rect.TopLeft, lightPosition, radius);
	}

	static void AddVisibilitySegment(List<VisibilityPolygon.Segment> segments, Vector2 a, Vector2 b, Vector2 lightPosition, float radius)
	{
		var segment = new VisibilityPolygon.Segment(a, b);
		if (VisibilityPolygon.CircleOverlaps(segment, lightPosition, radius))
			segments.Add(segment);
	}

	static Vector2 ProjectToLightRadius(Vector2 lightPosition, float radius, Vector2 point)
	{
		var direction = point - lightPosition;
		var distance = direction.Length();
		if (distance <= 0.0001f)
			return point;

		return lightPosition + direction / distance * radius;
	}

	static Vector2 DirectionFromLight(Vector2 lightPosition, Vector2 point)
	{
		var direction = point - lightPosition;
		if (direction.LengthSquared() <= 0.0001f)
			return Vector2.Zero;

		return Vector2.Normalize(direction);
	}

	static bool CircleOverlapsRect(Vector2 center, float radius, RectInt rect)
	{
		var x = Math.Clamp(center.X, rect.Left, rect.Right);
		var y = Math.Clamp(center.Y, rect.Top, rect.Bottom);
		var closest = new Vector2(x, y);
		return Vector2.DistanceSquared(center, closest) <= radius * radius;
	}

	void DrawNormalBuffer()
	{
		NormalBuffer.Clear(new Color(0.5f, 0.5f, 1.0f, 0.0f));
		RoomNormalCache? currentRoomNormalCache = null;
		RoomNormalCache? nextRoomNormalCache = null;
		if (ShowForegroundLayer)
		{
			if (room != null)
				currentRoomNormalCache = GetRoomNormalCache(room);
			if (nextRoom != null && nextRoom != room)
				nextRoomNormalCache = GetRoomNormalCache(nextRoom);
		}

		PushWorldMatrix();
		foreach (var actor in rendering)
		{
			if (actor is not Grid || !actor.Visible)
				continue;
			if ((IsBackgroundActor(actor) && !ShowBackgroundLayer) || (!IsBackgroundActor(actor) && !ShowForegroundLayer))
				continue;
			Batcher.PushMatrix(actor.Position);
			actor.RenderNormals(Batcher);
			Batcher.PopMatrix();
		}

		if (ShowForegroundLayer)
		{
			if (currentRoomNormalCache != null)
				DrawCachedRoomNormals(currentRoomNormalCache);
			if (nextRoomNormalCache != null)
				DrawCachedRoomNormals(nextRoomNormalCache);
		}

		foreach (var actor in rendering)
		{
			if (!actor.Visible || actor is Grid)
				continue;
			if ((IsBackgroundActor(actor) && !ShowBackgroundLayer) || (!IsBackgroundActor(actor) && !ShowForegroundLayer))
				continue;
			if (actor is DualGrid { Layer: Factory.TileLayer.Background })
				continue;

			Batcher.PushMatrix(actor.Position);
			var decal = PushPropDecalMaterial(actor);
			actor.RenderNormals(Batcher);
			if (decal)
				Batcher.PopMaterial();
			Batcher.PopMatrix();
		}

		Batcher.PopMatrix();
		Batcher.Render(NormalBuffer);
		Batcher.Clear();
	}

	void DrawCachedRoomNormals(RoomNormalCache cache)
	{
		Batcher.Image(cache.Target, cache.Room.WorldBounds.TopLeft, Color.White);
	}

	RoomNormalCache GetRoomNormalCache(Room renderRoom)
	{
		var bounds = renderRoom.WorldBounds;
		if (!roomNormalCaches.TryGetValue(renderRoom, out var cache) ||
			cache.Target.Width != bounds.Width ||
			cache.Target.Height != bounds.Height)
		{
			cache?.Dispose();
			cache = new(renderRoom, new Target(GraphicsDevice, bounds.Width, bounds.Height, name: $"RoomNormals:{renderRoom.Id}"));
			roomNormalCaches[renderRoom] = cache;
		}

		if (cache.Dirty)
			RebuildRoomNormalCache(cache);

		return cache;
	}

	internal void MarkRoomNormalCacheDirty(Room renderRoom)
	{
		if (roomNormalCaches.TryGetValue(renderRoom, out var cache))
			cache.Dirty = true;
	}

	void RebuildRoomNormalCache(RoomNormalCache cache)
	{
		var renderRoom = cache.Room;
		var target = cache.Target;
		var mask = new bool[target.Width, target.Height];
		var suppressMask = new bool[target.Width, target.Height];
		var origin = renderRoom.WorldBounds.TopLeft;

		FillRoomNormalMask(mask, renderRoom, origin);
		SuppressBakedDualRoomNormals(suppressMask, renderRoom);

		var previousMatrix = Batcher.Matrix;
		Batcher.Matrix = Matrix3x2.Identity;
		target.Clear(Color.Transparent);
		for (var y = 0; y < target.Height; y++)
		{
			var runStart = -1;
			var runColor = Color.Transparent;

			for (var x = 0; x < target.Width; x++)
			{
				var color = RoomNormalColor(mask, suppressMask, x, y);
				if (color.A > 0 && runStart >= 0 && color == runColor)
					continue;

				if (runStart >= 0)
					Batcher.Rect(new Rect(runStart, y, x - runStart, 1), runColor);

				if (color.A > 0)
				{
					runStart = x;
					runColor = color;
				}
				else
				{
					runStart = -1;
				}
			}

			if (runStart >= 0)
				Batcher.Rect(new Rect(runStart, y, target.Width - runStart, 1), runColor);
		}
		Batcher.Render(target);
		Batcher.Clear();
		Batcher.Matrix = previousMatrix;
		cache.Dirty = false;
	}

	static Color RoomNormalColor(bool[,] mask, bool[,] suppressMask, int x, int y)
	{
		if (!RoomNormalSolid(mask, x, y))
			return Color.Transparent;

		var sx = SolidValue(mask, x - 1, y) - SolidValue(mask, x + 1, y);
		var sy = SolidValue(mask, x, y - 1) - SolidValue(mask, x, y + 1);
		if (sx == 0 && sy == 0)
			return Color.Transparent;
		if (RoomNormalSuppressed(suppressMask, x, y))
			return Color.Transparent;

		return EncodeNormal(new Vector3(sx, sy, 1), 1);
	}

	void FillRoomNormalMask(bool[,] mask, Room renderRoom, Point2 origin)
	{
		foreach (var rect in renderRoom.CollisionRects)
			FillRoomNormalMask(mask, rect, origin);
		foreach (var polygon in renderRoom.LightOccluderPolygons)
			FillRoomNormalMask(mask, polygon, origin);
	}

	static void SuppressBakedDualRoomNormals(bool[,] suppressMask, Room renderRoom)
	{
		for (var tileY = 0; tileY < renderRoom.Size.Y; tileY++)
			for (var tileX = 0; tileX < renderRoom.Size.X; tileX++)
			{
				if (renderRoom.GetEntry(new Point2(tileX, tileY)) is not { DualTilesetName: { } dualTilesetName } ||
					Assets.GetTileset(dualTilesetName) is not { NormalTiles.Count: > 0 } and not { NormalVariants.Count: > 0 })
					continue;

				var left = Math.Clamp(tileX * Game.TileSize, 0, suppressMask.GetLength(0));
				var right = Math.Clamp((tileX + 1) * Game.TileSize, 0, suppressMask.GetLength(0));
				var top = Math.Clamp(tileY * Game.TileSize, 0, suppressMask.GetLength(1));
				var bottom = Math.Clamp((tileY + 1) * Game.TileSize, 0, suppressMask.GetLength(1));

				for (var y = top; y < bottom; y++)
					for (var x = left; x < right; x++)
						suppressMask[x, y] = true;
			}
	}

	static void FillRoomNormalMask(bool[,] mask, RectInt rect, Point2 origin)
	{
		var left = Math.Clamp(rect.Left - origin.X, 0, mask.GetLength(0));
		var right = Math.Clamp(rect.Right - origin.X, 0, mask.GetLength(0));
		var top = Math.Clamp(rect.Top - origin.Y, 0, mask.GetLength(1));
		var bottom = Math.Clamp(rect.Bottom - origin.Y, 0, mask.GetLength(1));

		for (var y = top; y < bottom; y++)
			for (var x = left; x < right; x++)
				mask[x, y] = true;
	}

	static void FillRoomNormalMask(bool[,] mask, Room.LightOccluderPolygon polygon, Point2 origin)
	{
		var left = Math.Clamp(polygon.Bounds.Left - origin.X, 0, mask.GetLength(0));
		var right = Math.Clamp(polygon.Bounds.Right - origin.X, 0, mask.GetLength(0));
		var top = Math.Clamp(polygon.Bounds.Top - origin.Y, 0, mask.GetLength(1));
		var bottom = Math.Clamp(polygon.Bounds.Bottom - origin.Y, 0, mask.GetLength(1));

		for (var y = top; y < bottom; y++)
			for (var x = left; x < right; x++)
			{
				var worldPoint = new Vector2(origin.X + x + 0.5f, origin.Y + y + 0.5f);
				if (PointInPolygon(worldPoint, polygon.Points))
					mask[x, y] = true;
			}
	}

	internal static bool PointInPolygon(Vector2 point, ReadOnlySpan<Point2> polygon)
	{
		var inside = false;
		for (var i = 0; i < polygon.Length; i++)
		{
			var a = (Vector2)polygon[i];
			var b = (Vector2)polygon[(i + polygon.Length - 1) % polygon.Length];
			if ((a.Y > point.Y) != (b.Y > point.Y) &&
				point.X < (b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y) + a.X)
				inside = !inside;
		}

		return inside;
	}

	static bool RoomNormalSolid(bool[,] mask, int x, int y)
	{
		if (x < 0 || y < 0 || x >= mask.GetLength(0) || y >= mask.GetLength(1))
			return false;

		return mask[x, y];
	}

	static bool RoomNormalSuppressed(bool[,] suppressMask, int x, int y)
	{
		if (x < 0 || y < 0 || x >= suppressMask.GetLength(0) || y >= suppressMask.GetLength(1))
			return false;

		return suppressMask[x, y];
	}

	static float SolidValue(bool[,] mask, int x, int y)
		=> RoomNormalSolid(mask, x, y) ? 1 : 0;

	static Color EncodeNormal(Vector3 normal, float strength)
	{
		normal = Vector3.Normalize(normal) * 0.5f + Vector3.One * 0.5f;
		return new Color(normal.X, normal.Y, normal.Z, strength);
	}

	ReloadableMaterial? CreateFragmentMaterial(string name, int samplerCount, int uniformBufferCount = 0)
	{
		try
		{
			return new ReloadableMaterial(GraphicsDevice, name, samplerCount, uniformBufferCount);
		}
		catch (Exception e)
		{
			Console.Error.WriteLine($"Failed to load {name} shader: {e}");
			return null;
		}
	}

	sealed class ReloadableMaterial : IDisposable
	{
		// Fragment shaders are precompiled, room- and game-independent programs, so compile
		// each one once per process and share it across every Game/GameView. Rebuilding a
		// Game (e.g. an editor room change) reuses the cached shaders instead of re-reading
		// and re-creating ~10 of them. Keyed by name (single GraphicsDevice per process).
		static readonly Dictionary<string, Shader> SharedShaders = [];

		readonly GraphicsDevice graphicsDevice;
		readonly string name;
		readonly int samplerCount;
		readonly int uniformBufferCount;

		public Material Material { get; }

		public ReloadableMaterial(GraphicsDevice graphicsDevice, string name, int samplerCount, int uniformBufferCount)
		{
			this.graphicsDevice = graphicsDevice;
			this.name = name;
			this.samplerCount = samplerCount;
			this.uniformBufferCount = uniformBufferCount;
			Material = new Material(vertexShader: null, fragmentShader: GetOrCreateShader());
		}

		// Shaders are process-lifetime and shared, so disposing a material does not dispose
		// its shader (other live materials may reference it; recreating a Game reuses it).
		public void Dispose() { }

#if DEBUG
		public void Reload()
		{
			try
			{
				var next = CompileShader();
				Material.Fragment.Shader = next;
				SharedShaders[name] = next; // newly built Games pick up the reloaded shader
				Console.WriteLine($"Reloaded {name} shader.");
			}
			catch (Exception e)
			{
				Console.Error.WriteLine($"Failed to reload {name} shader: {e}");
			}
		}
#endif

		Shader GetOrCreateShader()
		{
			if (SharedShaders.TryGetValue(name, out var cached))
				return cached;
			var shader = CompileShader();
			SharedShaders[name] = shader;
			return shader;
		}

		Shader CompileShader()
		{
			var ext = graphicsDevice.Driver.GetShaderExtension();
			var path = Path.Join(Assets.AssetsPath, "Shaders", "Compiled", $"{name}.fragment.{ext}");
			if (!File.Exists(path))
				throw new FileNotFoundException($"{name} shader not found at '{Path.GetFullPath(path)}'.", path);

			return new Shader(
				graphicsDevice,
				ShaderStage.Fragment,
				File.ReadAllBytes(path),
				samplerCount: samplerCount,
				uniformBufferCount: uniformBufferCount,
				entryPoint: "fragment_main",
				name: $"{name}Fragment");
		}
	}

	static Color ScaleColor(Color color, float scale)
	{
		var value = (Vector4)color;
		value.X = Math.Clamp(value.X * scale, 0, 1);
		value.Y = Math.Clamp(value.Y * scale, 0, 1);
		value.Z = Math.Clamp(value.Z * scale, 0, 1);
		value.W = Math.Clamp(value.W * scale, 0, 1);
		return value;
	}

	public void Render(in RectInt viewport, bool showHud = true, RectInt? scissor = null)
	{
		if (Manager.Window is not { } window)
			return;

		EnsureRenderTargets(Width, Height);
		RenderCurrentView(window, viewport, showHud, scissor, applyCrt: true);
	}

	public void RenderWorldView(in RectInt viewport, Rect worldView, bool showHud = false, RectInt? scissor = null, bool applyCrt = true)
	{
		if (Manager.Window is not { } window)
			return;

		var oldCamera = Camera;
		var oldShake = shake;
		var width = Math.Max(1, (int)MathF.Ceiling(worldView.Width));
		var height = Math.Max(1, (int)MathF.Ceiling(worldView.Height));

		EnsureRenderTargets(width, height);
		Camera = worldView.TopLeft;
		shake = Point2.Zero;
		RenderCurrentView(window, viewport, showHud, scissor, applyCrt);
		Camera = oldCamera;
		shake = oldShake;
	}

	public void RenderWorldToTarget(Target target, Rect worldView, bool showHud = false)
	{
		var oldCamera = Camera;
		var oldShake = shake;
		var width = Math.Max(1, (int)MathF.Ceiling(worldView.Width));
		var height = Math.Max(1, (int)MathF.Ceiling(worldView.Height));

		EnsureRenderTargets(width, height);
		Camera = worldView.TopLeft;
		shake = Point2.Zero;
		RenderCurrentView(target, target.Bounds, showHud, scissor: null, applyCrt: false);
		Camera = oldCamera;
		shake = oldShake;
	}

	void EnsureRenderTargets(int width, int height)
	{
		if (Screen != null && Screen.Width == width && Screen.Height == height)
			return;

		DisposeRenderTargets();

		Screen = Track(RenderTargetPool.RentColor(GraphicsDevice, width, height, "GameScreen"));
		BackgroundScreen = Track(RenderTargetPool.RentColor(GraphicsDevice, width, height, "BackgroundScreen"));
		ForegroundScreen = Track(RenderTargetPool.RentColor(GraphicsDevice, width, height, "ForegroundScreen"));
		BackgroundEmissive = Track(RenderTargetPool.RentColor(GraphicsDevice, width, height, "BackgroundEmissive"));
		ForegroundEmissive = Track(RenderTargetPool.RentColor(GraphicsDevice, width, height, "ForegroundEmissive"));
		BackgroundLightMask = Track(RenderTargetPool.RentColor(GraphicsDevice, width, height, "BackgroundLightMask"));
		ForegroundLightMask = Track(RenderTargetPool.RentColor(GraphicsDevice, width, height, "ForegroundLightMask"));
		SingleLightMask = Track(RenderTargetPool.RentColor(GraphicsDevice, width, height, "SingleLightMask"));
		LitScreen = Track(RenderTargetPool.RentColor(GraphicsDevice, width, height, "LitScreen"));
		GlitchScreen = Track(RenderTargetPool.RentColor(GraphicsDevice, width, height, "GlitchScreen"));
		NormalBuffer = Track(RenderTargetPool.RentColor(GraphicsDevice, width, height, "NormalBuffer"));
		TileField = Track(RenderTargetPool.RentFormat(GraphicsDevice, width, height, TextureFormat.R8G8, "TileField"));
		DitherMask = Track(RenderTargetPool.RentFormat(GraphicsDevice, width, height, TextureFormat.R8, "DitherMask"));
		ParticleSolidMask = Track(RenderTargetPool.RentFormat(GraphicsDevice, width, height, TextureFormat.R8, "ParticleSolidMask"));
		ReflectionScreen = Track(RenderTargetPool.RentColor(GraphicsDevice, width, height, "ReflectionScreen"));
		ReflectionMask = Track(RenderTargetPool.RentFormat(GraphicsDevice, width, height, TextureFormat.R8, "ReflectionMask"));
		AoMask = Track(RenderTargetPool.RentFormat(GraphicsDevice, width, height, TextureFormat.R8, "AoMask"));
		AoBlurScratch = Track(RenderTargetPool.RentFormat(GraphicsDevice, width, height, TextureFormat.R8, "AoBlurScratch"));
		BackgroundCoverage = Track(RenderTargetPool.RentFormat(GraphicsDevice, width, height, TextureFormat.R8, "BackgroundCoverage"));
		SoftShadowScratch = Track(RenderTargetPool.RentColor(GraphicsDevice, width, height, "SoftShadowScratch"));
	}

	Target Track(Target target)
	{
		debugTargets.Add(target);
		return target;
	}


	// Used by the per-room normal/tile-field caches (not the pooled main render targets).
	Target CreateRenderTarget(int width, int height, TextureFormat format, string name)
	{
		ReadOnlySpan<TextureFormat> attachments = [format];
		return new Target(GraphicsDevice, width, height, attachments, name: name);
	}

	// debugTargets holds exactly the render targets created by EnsureRenderTargets, so
	// return them all to the pool for reuse. Clearing the list makes a repeat call a no-op
	// (no double-return) and the property fields are always reassigned before next use.
	void DisposeRenderTargets()
	{
		foreach (var target in debugTargets)
			RenderTargetPool.Return(target);
		debugTargets.Clear();
	}

	void DisposeRoomNormalCaches()
	{
		foreach (var cache in roomNormalCaches.Values)
			cache.Dispose();
		roomNormalCaches.Clear();
	}

	void DisposeRoomTileFieldCaches()
	{
		foreach (var cache in roomTileFieldCaches.Values)
			cache.Dispose();
		roomTileFieldCaches.Clear();
	}

	readonly Dictionary<Room, RoomNormalCache> roomNormalCaches = [];
	readonly Dictionary<Room, RoomTileFieldCache> roomTileFieldCaches = [];

	internal void PruneInactiveRoomCaches()
	{
		PruneInactiveRoomNormalCaches();
		PruneInactiveRoomTileFieldCaches();
	}

	bool IsActiveRoomCache(Room cachedRoom)
		=> cachedRoom == room || cachedRoom == nextRoom;

	void PruneInactiveRoomNormalCaches()
	{
		List<Room>? remove = null;
		foreach (var (cachedRoom, cache) in roomNormalCaches)
		{
			if (IsActiveRoomCache(cachedRoom))
				continue;
			remove ??= [];
			remove.Add(cachedRoom);
			cache.Dispose();
		}

		if (remove == null)
			return;
		foreach (var cachedRoom in remove)
			roomNormalCaches.Remove(cachedRoom);
	}

	void PruneInactiveRoomTileFieldCaches()
	{
		List<Room>? remove = null;
		foreach (var (cachedRoom, cache) in roomTileFieldCaches)
		{
			if (IsActiveRoomCache(cachedRoom))
				continue;
			remove ??= [];
			remove.Add(cachedRoom);
			cache.Dispose();
		}

		if (remove == null)
			return;
		foreach (var cachedRoom in remove)
			roomTileFieldCaches.Remove(cachedRoom);
	}

	public float SdfAuraScrollTime => Time.SecondsF * SdfAuraScrollSpeed;

	/// <summary>
	/// Draws the combined SDF aura over a quad (corners <paramref name="p0"/>..<paramref name="p3"/>
	/// in the caller's local space), with the shader uniforms describing the two
	/// silhouettes. The SDF atlas is the sampled texture.
	/// </summary>
	public void RenderSdfAura(Batcher batcher, Span<float> uniforms, Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3)
	{
		if (sdfAuraMaterial == null || SpriteSdf.Atlas is not { } atlas)
			return;

		// Fill in the world camera (RTopLeft.zw) and zoom (A1.z) so the dither grid
		// is evaluated in world-pixel space (matching the lighting dither).
		var camera = RenderCamera;
		uniforms[6] = camera.X;
		uniforms[7] = camera.Y;
		uniforms[30] = zoomLevel;

		sdfAuraMaterial.Material.Fragment.SetUniformBuffer(uniforms);
		batcher.PushMaterial(sdfAuraMaterial.Material);
		batcher.PushBlend(new BlendMode(BlendOp.Add, BlendFactor.SrcAlpha, BlendFactor.One));
		batcher.Quad(atlas, p0, p1, p2, p3, new(0, 0), new(1, 0), new(1, 1), new(0, 1), Color.White);
		batcher.PopBlend();
		batcher.PopMaterial();
	}

	/// <summary>World-space center of the current view (for test bursts).</summary>
	public Vector2 ViewCenterWorld => RenderCamera + new Vector2(Width, Height) / 2f / zoomLevel;

	int previewEffectSlot = -1;
	string? previewEffectName;

	/// <summary>Selects which effect asset the tuning window previews/bursts.</summary>
	public void SetPreviewEffect(string name)
	{
		if (particleSystem is not { Supported: true } || name == previewEffectName)
			return;
		if (previewEffectSlot >= 0)
			particleSystem.ReleaseEffect(previewEffectSlot);
		previewEffectName = name;
		previewEffectSlot = particleSystem.AcquireEffect(name);
	}

	/// <summary>Emits the tuning-window's selected effect at the view center.</summary>
	public void BurstTestEffect()
	{
		if (previewEffectSlot >= 0 && previewEffectName != null)
			particleSystem?.Emit(previewEffectSlot, ViewCenterWorld, ParticleEffectAssets.Get(previewEffectName).BurstCount);
	}

	/// <summary>
	/// Editor-only preview: actor Update does not run in edit mode. Only the
	/// selected emitter emits; the sim still advances so existing particles age
	/// out when nothing is selected.
	/// </summary>
	public void PreviewParticlesInEditor(float dt, string? selectedActorId)
	{
		if (particleSystem is not { Supported: true })
			return;
		if (selectedActorId != null)
			foreach (var actor in Actors)
				if (actor is ParticleEmitter emitter && emitter.ActorId == selectedActorId)
					emitter.EmitStep(dt);
		StepParticles(dt);
	}

	// Advances the particle sim, sampling collision from last frame's foreground
	// coverage (1-frame stale, fine for collision).
	void StepParticles(float dt)
	{
		if (particleSystem is not { Supported: true })
			return;
		DrawParticleSolidMask();
		particleSystem.Update(dt, ParticleSolidMask.Attachments[0], RenderCamera, zoomLevel);
	}

	// --- view state moved from Game.cs (fields/consts/caches) ---
	const int ActorShadowSteps = 8;
	const float ActorShadowStrength = 0.5f;
	const float ActorShadowMinLength = 4.0f;
	const float ActorShadowMaxLength = 28.0f;
	static readonly BlendMode ShadowBlend = new(
		BlendOp.Add, BlendFactor.One, BlendFactor.OneMinusSrcAlpha,
		BlendOp.Add, BlendFactor.One, BlendFactor.OneMinusSrcAlpha,
		BlendMask.RGB, Color.White);
	static readonly BlendMode EmissiveOccluderBlend = new(BlendOp.Add, BlendFactor.Zero, BlendFactor.OneMinusSrcAlpha);
	static readonly BlendMode TileFieldStampBlend = new(
		BlendOp.Add, BlendFactor.One, BlendFactor.Zero,
		BlendOp.Add, BlendFactor.One, BlendFactor.Zero,
		BlendMask.Red | BlendMask.Green, Color.White);

	static float DitherGlitchRadius => 150.0f;
	static float NormalDitherLevels => 4.0f;
	static Vector2 AtlasWanderSpeed => new(13.0f, 7.0f);
	static float AtlasWanderInitialSpeed => 0.1f;
	static float AtlasWanderRampDuration => 10.0f;
	const int MaxDitherAnchors = 32;
	const int DitherAnchorFloatCount = MaxDitherAnchors * 4;
	const float ActorDitherAmount = 0.2f;
	readonly Batcher Batcher;
	readonly float[] ditherAnchorOffsets = new float[DitherAnchorFloatCount];
	public Target Screen { get; private set; } = null!;

	Target BackgroundScreen { get; set; } = null!;
	Target ForegroundScreen { get; set; } = null!;
	Target BackgroundEmissive { get; set; } = null!;
	Target ForegroundEmissive { get; set; } = null!;
	Target BackgroundLightMask { get; set; } = null!;
	Target ForegroundLightMask { get; set; } = null!;
	Target SingleLightMask { get; set; } = null!;
	Target LitScreen { get; set; } = null!;
	Target GlitchScreen { get; set; } = null!;
	Target NormalBuffer { get; set; } = null!;
	Target TileField { get; set; } = null!;
	Target DitherMask { get; set; } = null!;
	Target ParticleSolidMask { get; set; } = null!;
	Target ReflectionScreen { get; set; } = null!;
	Target ReflectionMask { get; set; } = null!;
	Target AoMask { get; set; } = null!;
	Target AoBlurScratch { get; set; } = null!;
	Target BackgroundCoverage { get; set; } = null!;
	Target SoftShadowScratch { get; set; } = null!;

	// Every render target created in EnsureRenderTargets, in creation order, for the
	// debug render-target overlay (toggled with G). Populated via Track().
	readonly List<Target> debugTargets = [];

	public IReadOnlyList<Target> DebugTargets => debugTargets;
	public bool ShowForegroundLayer { get; set; } = true;
	public bool ShowBackgroundLayer { get; set; } = true;
	readonly ReloadableMaterial? crtMaterial;
	readonly ReloadableMaterial? atlasWanderMaterial;
	readonly ReloadableMaterial? lightingCompositeMaterial;
	readonly ReloadableMaterial? normalLightMaterial;
	readonly ReloadableMaterial? reflectionDistortMaterial;
	readonly ReloadableMaterial? tileFieldStampMaterial;
	readonly ReloadableMaterial? tileFieldDebugMaterial;
	readonly ReloadableMaterial? pixelSortGlitchMaterial;
	readonly ReloadableMaterial? blurMaterial;
	readonly ReloadableMaterial? softShadowMaterial;
	readonly ReloadableMaterial? sdfAuraMaterial;
	readonly ReloadableMaterial? backgroundDecalMaskMaterial;
	readonly ReloadableMaterial? foregroundDecalMaskMaterial;
	readonly ReloadableMaterial? coverageMaskMaterial;
	Vector2 atlasWanderOffset;
	float atlasWanderElapsed;
	const float SdfAuraScrollSpeed = 14f; // px/sec the rainbow bands travel outward
	readonly ParticleSystem? particleSystem;
	float particleBurstTimer;
	public ParticleSystem? Particles => particleSystem;
	sealed class RoomNormalCache(Room room, Target target) : IDisposable
	{
		public readonly Room Room = room;
		public readonly Target Target = target;
		public bool Dirty = true;

		public void Dispose()
			=> Target.Dispose();
	}

	sealed class RoomTileFieldCache(Room room, Target target) : IDisposable
	{
		public readonly Room Room = room;
		public readonly Target Target = target;
		public bool Dirty = true;

		public void Dispose()
			=> Target.Dispose();
	}
}
