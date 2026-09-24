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
		InputPromptGlyphs.Clear();
		hasRoomReflections = false;
		if (game.AtlasWander)
		{
			atlasWanderElapsed += Time.Delta;
			var ramp = Calc.ClampedMap(atlasWanderElapsed, 0, AtlasWanderRampDuration);
			ramp *= ramp * (3.0f - 2.0f * ramp);
			atlasWanderOffset += AtlasWanderSpeed * Calc.Lerp(AtlasWanderInitialSpeed, 1, ramp) * Time.Delta;
		}
		PrepareRenderingList();
		game.VisibleWorldBounds = new Rect(RenderCamera, new Vector2(Screen.Width, Screen.Height) / zoomLevel);
		NeuralTextureActor.DecodeVisible(Actors, game.VisibleWorldBounds);
		GetDecalLayers(out var hasBackgroundDecals, out var hasForegroundDecals);
		var buildAo = debugView is not DebugView.TileFieldRed and not DebugView.TileFieldGreen and not DebugView.DitherMask &&
#if EDITOR
			!Manager.FullBright &&
#endif
			PerfToggles.Lighting && lightingCompositeMaterial != null && PerfToggles.AmbientOcclusion &&
			Manager.Visuals.AmbientOcclusionStrength > 0 && blurMaterial != null;
		if (hasForegroundDecals)
			DrawParticleSolidMask();
		if (hasBackgroundDecals || buildAo)
			DrawBackgroundCoverage();

		using (PerfStats.Time(PerfStat.World))
		{
			using (GraphicsDevice.DebugGroup("Myth/background color field"))
				hasBackgroundColorField = DrawRoomColorField(ColorField, background: true);
			using (GraphicsDevice.DebugGroup("Myth/background world"))
				DrawWorldLayer(BackgroundScreen, background: true);
			using (GraphicsDevice.DebugGroup("Myth/foreground color field"))
				hasForegroundColorField = DrawRoomColorField(ColorField, background: false);
			using (GraphicsDevice.DebugGroup("Myth/foreground world"))
				DrawWorldLayer(ForegroundScreen, background: false);
			using (GraphicsDevice.DebugGroup("Myth/water"))
				DrawWaterBodies(ForegroundScreen);
			if (PerfToggles.Reflections)
				using (GraphicsDevice.DebugGroup("Myth/flat reflections"))
					CompositeFlatReflections(ForegroundScreen);
			using (GraphicsDevice.DebugGroup("Myth/hidden covers"))
				DrawHiddenCovers(ForegroundScreen);
		}
		if (
#if EDITOR
			!Manager.FullBright &&
#endif
			PerfToggles.Emissive)
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

		if (
#if EDITOR
			!Manager.FullBright &&
#endif
			PerfToggles.NormalBuffer)
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
		if (PerfToggles.DitherAndTileField || game.AirFlashAmount > 0 || game.RoomInversionEffectActive)
		{
			using (PerfStats.Time(PerfStat.TileDither))
			{
				using (GraphicsDevice.DebugGroup("Myth/tile field"))
					DrawTileField();
				using (GraphicsDevice.DebugGroup("Myth/actor tile field"))
					DrawActorTileField();
			}
		}
		else
		{
			TileField.Clear(Color.Transparent);
			ClearActorTileField();
		}

		Target output;
		if (debugView is DebugView.TileFieldRed or DebugView.TileFieldGreen)
		{
			using (GraphicsDevice.DebugGroup("Myth/tile field debug"))
				output = DrawFieldDebug(TileField, TileFieldDebugChannel());
		}
		else if (debugView == DebugView.DitherMask)
		{
			using (GraphicsDevice.DebugGroup("Myth/dither mask debug"))
			{
				DrawDitherMask();
				output = DrawFieldDebug(NormalBuffer, 2);
			}
		}
		else
		{
			if (
#if EDITOR
				!Manager.FullBright &&
#endif
				PerfToggles.Lighting)
			{
				using (PerfStats.Time(PerfStat.Lighting))
				using (GraphicsDevice.DebugGroup("Myth/lighting"))
				{
					output = ApplyLighting(buildAo);
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
			{
				if (!hasRoomReflections)
					ReflectionScreen.Clear(Color.Transparent);
				output = ReflectionScreen;
			}
			else if (debugView == DebugView.None)
			{
				DrawCutsceneOverlay(output);
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
				particleSystem.Render(output, BackgroundLightMask, RenderCamera, zoomLevel,
#if EDITOR
					!Manager.FullBright &&
#endif
					PerfToggles.Lighting,
					ScaleColor(CurrentAmbientColor(), CurrentAmbientIntensity()));
		}
		if (debugView == DebugView.None && game.AirFlashAmount > 0)
			output = ApplyAirFlash(output);
		if (debugView == DebugView.None)
		{
			CaptureInversionSnapshot(output);
			output = ApplyWorldDistortion(output);
		}
		DrawLayerTransition(output);
		var mapOverlayVisible = false;
		if (debugView is DebugView.None or DebugView.CollisionRects)
		{
			DrawOverlayElements(output);
			if (showHud)
			{
				DrawMinimap(output);
				mapOverlayVisible = minimapOpacity > 0.001f;
				DrawContextPrompt(output, mapOverlayVisible);
				DrawPlayerHud(output);
			}
		}
		if (debugView == DebugView.ShadowSegments)
		{
			using (GraphicsDevice.DebugGroup("Myth/shadow-segment debug"))
				DrawShadowDebug(output);
		}
		if (debugView == DebugView.PlatformNav)
		{
			game.GetPlatformNavGraph();
		}

		// draw screen to window
		var crt = applyCrt && (PerfToggles.Crt || Manager.Visuals.PixelScaler != 0) ? crtMaterial : null;
		var useCrt = crt != null;
		var present = game.PresentRect(viewport);
		var scale = game.PresentScale(present, output.Width, output.Height);
		var clip = game.AspectCropActive
			? (scissor.HasValue ? scissor.Value.GetIntersection(present) : present)
			: scissor;
		using (PerfStats.Time(PerfStat.FinalComposite))
		using (GraphicsDevice.DebugGroup(useCrt ? "Myth/final composite crt" : "Myth/final composite"))
		{
			// Present into a 4:3 region when cropping (desktop pillarbox / portrait canvas),
			// cover-fitting the 16:9 frame so its sides are cropped; aspectUncrop lerps back to a
			// contained (full, letterboxed) frame while Zoom is held. The cover-scaled frame
			// overflows the present region, so clip it to never bleed past the pillarbox bars.
			var shakeScale = (Vector2)present.Size / RenderTargetShakeReferenceSize;
			var center = (Vector2)present.Center + RenderTargetShake * MathF.Min(shakeScale.X, shakeScale.Y);

			// Windows get the frame composited (CRT included) at a whole multiple, then stretched
			// linearly: every game pixel stays the same width at fractional scales, and the CRT's
			// cost follows the resolution setting instead of the display (see VideoSettings).
			var composite = destination is Window ? EnsureCompositeTarget(output, present) : null;
			var compositeScale = composite == null ? 1 : composite.Width / output.Width;

			Batcher.PushSampler(new(TextureFilter.Nearest, TextureWrap.Clamp, TextureWrap.Clamp));
#if !MYTHSHIP
			var blackAndWhite = Manager.BlackAndWhite ? blackAndWhiteMaterial : null;
			if (composite == null && blackAndWhite != null)
				Batcher.PushMaterial(blackAndWhite.Material);
#endif
			if (useCrt)
			{
				crt!.Material.Fragment.Samplers[1] = new(TileField, new(TextureFilter.Nearest, TextureWrap.Clamp));
				crt!.Material.Fragment.SetUniformBuffer([
					output.Width,
					output.Height,
					Manager.Visuals.CrtBloomAmount,
					Manager.Visuals.CrtVignetteAmount,
					Manager.Visuals.CrtRgbOffset,
					game.fx.blooming,
					Manager.Visuals.CrtBloomThreshold,
					Manager.Visuals.CrtScanlineStrength,
					zoomLevel,
					composite != null ? compositeScale : scale,
					Manager.Visuals.PixelScaler,
					Manager.Visuals.PixelScalerTolerance,
					PerfToggles.Crt ? 1 : 0,
				]);
				Batcher.PushMaterial(crt.Material);
			}
			if (composite != null)
				Batcher.Image(output, Vector2.Zero, Vector2.Zero, Vector2.One * compositeScale, 0, Color.White);
			else
				Batcher.Image(output, center, output.Bounds.Size / 2, Vector2.One * scale, 0, Color.White);
			if (useCrt)
				Batcher.PopMaterial();
#if !MYTHSHIP
			if (composite == null && blackAndWhite != null)
				Batcher.PopMaterial();
#endif
			Batcher.PopSampler();
			if (composite != null)
			{
				Batcher.Render(composite);
				Batcher.Clear();
				Batcher.PushSampler(new(TextureFilter.Linear, TextureWrap.Clamp, TextureWrap.Clamp));
#if !MYTHSHIP
				if (blackAndWhite != null)
					Batcher.PushMaterial(blackAndWhite.Material);
#endif
				Batcher.Image(composite, center, composite.Bounds.Size / 2, Vector2.One * (scale / compositeScale), 0, Color.White);
#if !MYTHSHIP
				if (blackAndWhite != null)
					Batcher.PopMaterial();
#endif
				Batcher.PopSampler();
			}
			Batcher.Render(destination, scissor: clip);
			Batcher.Clear();
		}

		if (showHud && debugView is (DebugView.None or DebugView.CollisionRects))
			DrawInputPromptGlyphs(destination, present, output.Bounds.Size, scale, clip, mapOverlayVisible);

		if (showHud && game.ActiveCutscene == null && debugView is (DebugView.None or DebugView.CollisionRects))
			DrawCurrencyHud(destination, viewport, scissor);

		if (game.SavePointWarpFade > 0)
		{
			Batcher.Rect(viewport, Color.Black * game.SavePointWarpFade);
			Batcher.Render(destination, scissor: scissor?.GetIntersection(viewport) ?? viewport);
			Batcher.Clear();
		}
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

		if (game.ActiveCutscene is { } cutscene)
		{
			cutscene.RenderFade(Batcher);
			Batcher.Render(output);
			Batcher.Clear();
		}

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

	void DrawCutsceneOverlay(Target output)
	{
		if (game.ActiveCutscene is not { } cutscene)
			return;

		cutscene.RenderOverlay(Batcher);
		Batcher.Render(output);
		Batcher.Clear();
	}

	void PrepareRenderingList()
	{
		rendering.Clear();
		rendering.AddRange(Actors);
		rendering.Sort((a, b) => b.Depth - a.Depth);
		hasProceduralWalls = proceduralWallRenderer?.Prepare(rendering) ?? false;
	}

	Target DrawFieldDebug(Target field, int channel)
	{
		Screen.Clear(Color.Transparent);
		Batcher.PushSampler(new(TextureFilter.Nearest, TextureWrap.Clamp, TextureWrap.Clamp));
		if (tileFieldDebugMaterial != null)
		{
			tileFieldDebugMaterial.Material.Fragment.SetUniformBuffer([
				channel,
				0,
				1,
				0
			]);
			Batcher.PushMaterial(tileFieldDebugMaterial.Material);
		}
		Batcher.Image(field, field.Bounds.Center, field.Bounds.Size / 2, Vector2.One, 0, Color.White);
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
		{
			DrawBackdrop(target);
			Batcher.Render(target);
			Batcher.Clear();
			if (ShowBackgroundLayer && hasProceduralWalls)
				proceduralWallRenderer?.Render(target, rendering, RenderCamera, zoomLevel, Time.SecondsF,
					ProceduralWallRenderer.Pass.Color);
		}

		if ((background && !ShowBackgroundLayer) || (!background && !ShowForegroundLayer))
			return;

		PushWorldMatrix();
		var atlasWander = PushAtlasWanderMaterial();

		// draw actors
		foreach (var actor in rendering)
		{
			if (!actor.Visible || actor is HiddenCoverGrid)
				continue;
			if (IsBackgroundActor(actor) != background)
				continue;

			DrawWorldActor(actor, background, atlasWander);
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

	void DrawHiddenCovers(Target target)
	{
		if (!ShowForegroundLayer)
			return;

		PushWorldMatrix();
		var atlasWander = PushAtlasWanderMaterial();
		foreach (var actor in rendering)
			if (actor is HiddenCoverGrid { Visible: true })
				DrawWorldActor(actor, background: false, atlasWander);
		if (atlasWander)
			Batcher.PopMaterial();
		Batcher.PopMatrix();
		Batcher.Render(target);
		Batcher.Clear();
	}

	void DrawWorldActor(Actor actor, bool background, bool atlasWander)
	{
		Batcher.PushMatrix(actor.Position);
		var actorMaterial = !atlasWander && PushWorldActorMaterial(actor, background);
		actor.Render(Batcher);
		if (actorMaterial)
			Batcher.PopMaterial();
		Batcher.PopMatrix();
	}

	bool DrawRoomColorField(Target target, bool background)
	{
		if (background ? !ShowBackgroundLayer : !ShowForegroundLayer)
			return false;
		var drew = false;
		PushWorldMatrix();
		Batcher.PushBlend(ColorFieldCopyBlend);
		Batcher.PushSampler(new(TextureFilter.Linear, TextureWrap.Clamp, TextureWrap.Clamp));
		foreach (var visibleRoom in VisibleRooms())
		{
			var field = background ? visibleRoom.BackgroundColorField : visibleRoom.ForegroundColorField;
			if (!field.HasPaint)
				continue;
			if (!drew)
				target.Clear(Color.Transparent);
			var texture = GetRoomColorFieldTexture(field);
			Batcher.Image(texture, visibleRoom.WorldBounds.TopLeft, Vector2.Zero, Vector2.One * Game.TileSize, 0, Color.White);
			drew = true;
		}
		Batcher.PopSampler();
		Batcher.PopBlend();
		Batcher.PopMatrix();
		Batcher.Render(target);
		Batcher.Clear();
		return drew;
	}

	bool PushWorldActorMaterial(Actor actor, bool background)
	{
		var hasField = background ? hasBackgroundColorField : hasForegroundColorField;
		if (!hasField)
			return PushPropDecalMaterial(actor);

		var field = ColorField;
		if (actor is Prop { Decal: true })
		{
			var coverage = background ? BackgroundCoverage : ParticleSolidMask;
			var material = background ? backgroundColorFieldDecalMaterial : foregroundColorFieldDecalMaterial;
			if (material == null)
				return PushPropDecalMaterial(actor);
			material.Material.Fragment.SetUniformBuffer([field.Width, field.Height, 0, 0]);
			material.Material.Fragment.Samplers[1] = new(coverage, new(TextureFilter.Nearest, TextureWrap.Clamp));
			material.Material.Fragment.Samplers[2] = new(field, new(TextureFilter.Linear, TextureWrap.Clamp));
			Batcher.PushMaterial(material.Material);
			return true;
		}

		if (actor is not Grid and not DualGrid and not Prop and not HiddenCoverGrid and not HiddenWallGrid || colorFieldMaterial == null)
			return false;

		colorFieldMaterial.Material.Fragment.SetUniformBuffer([field.Width, field.Height, 0, 0]);
		colorFieldMaterial.Material.Fragment.Samplers[1] = new(field, new(TextureFilter.Linear, TextureWrap.Clamp));
		Batcher.PushMaterial(colorFieldMaterial.Material);
		return true;
	}

	// Draws the dynamic water bodies (the static 'w' tile visual is suppressed in Factory).
	// Surfaces store absolute world coords, so the world matrix places both the current and
	// (mid-transition) next room correctly; the wave sim itself ticks in Game.PostUpdateSystems.
	void DrawWaterBodies(Target target)
	{
		if (!HasWaterBody(room) && !HasWaterBody(nextRoom))
			return;

		PushWorldMatrix();
		DrawRoomWaterBody(room);
		DrawRoomWaterBody(nextRoom);
		Batcher.PopMatrix();
		Batcher.Render(target);
		Batcher.Clear();
	}

	static bool HasWaterBody(Room? r)
		=> r != null && (r.WaterSurfaces.Count > 0 || r.CappedWaterRects.Count > 0);

	void DrawRoomWaterBody(Room? r)
	{
		if (r == null)
			return;
		// Flat tint for capped water first, then the animated exposed surfaces + foam on top.
		foreach (var rect in r.CappedWaterRects)
			WaterSurface.RenderCappedBody(Batcher, rect);
		var time = Time.SecondsF;
		foreach (var surface in r.WaterSurfaces)
			surface.Render(Batcher, time);
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

		if (nextRoom == null || string.Equals(room?.BackdropSurfaceId, nextRoom.BackdropSurfaceId, StringComparison.OrdinalIgnoreCase))
		{
			DrawBackdropSurface(room ?? nextRoom, target, 1);
			return;
		}

		var amount = Ease.Cube.InOut(nextRoomEase);
		DrawBackdropSurface(room, target, 1 - amount);
		DrawBackdropSurface(nextRoom, target, amount);
	}

	void DrawBackdropSurface(Room? backdropRoom, Target target, float opacity)
	{
		if (opacity <= 0 || Assets.GetSurface(backdropRoom?.BackdropSurfaceId) is not { } surface)
			return;

		var bounds = new Rect(RenderCamera, new Vector2(target.Width, target.Height) / zoomLevel);
		PushWorldMatrix();
		surface.Render(Batcher, bounds, Time.SecondsF, opacity);
		Batcher.PopMatrix();
	}

	static bool IsBackgroundActor(Actor actor)
		=> actor switch
		{
			ProceduralWall => true,
			CurveRibbon ribbon => ribbon.Layer == Factory.TileLayer.Background,
			Grid grid => grid.Layer == Factory.TileLayer.Background,
			DualGrid dualGrid => dualGrid.Layer == Factory.TileLayer.Background,
			Prop prop => prop.Layer == Factory.TileLayer.Background,
			RigPuppetProp puppet => puppet.Layer == RigPuppetPropLayer.Background,
			NeuralTextureActor texture => texture.Layer == Factory.TileLayer.Background,
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
		if (background && ShowBackgroundLayer && hasProceduralWalls)
			proceduralWallRenderer?.Render(target, rendering, RenderCamera, zoomLevel, Time.SecondsF,
				ProceduralWallRenderer.Pass.Emissive);
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

		EnsureReflectionSurfaceTops();
		var reflectionRects = room.ReflectionRects;
		for (var i = 0; i < reflectionRects.Count; i++)
		{
			var rect = reflectionRects[i];
			var scissor = GetReflectionScissor(rect.Bounds);
			if (rect.Opacity <= 0 || scissor.Width <= 0 || scissor.Height <= 0)
				continue;
			if (!hasRoomReflections)
				ReflectionScreen.Clear(Color.Transparent);
			hasRoomReflections = true;
			DrawReflectedActors(ReflectionScreen, rect.Bounds, reflectionSurfaceTops[i], rect.Opacity, skip: null);
		}
		if (!hasRoomReflections)
			return;

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

	// Mirror every reflection-casting actor across a surface's top edge into the target,
	// clipped to the surface bounds. Shared by room reflection rects and reflective platforms.
	void DrawReflectedActors(Target target, RectInt bounds, int surfaceTop, float opacity, Actor? skip)
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
		Batcher.Render(target, scissor: scissor);
		Batcher.Clear();
	}

	// Reflective moving platforms are foreground actors and move, so they can't be baked into the
	// room's (background) reflection rects; mirror sprites onto their surface here, in the foreground
	// pass, so the reflection lands over the platform's own skin.
	void DrawPlatformSurfaceReflections(Target target)
	{
		// Flat reflections need no distortion pass. Draw directly so the room reflections
		// survive until CompositeFlatReflections, after water has been drawn.
		foreach (var actor in rendering)
			if (actor is IDualGridRect { Visible: true } skin && skin.ReflectsSurface())
				DrawReflectedActors(target, skin.Bounds(), skin.Position.Y, DualGridRects.SurfaceReflectionOpacity, skip: actor);
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
		if (!hasRoomReflections || !HasFlatReflectionRect())
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
	bool hasRoomReflections;
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

	Target ApplyLighting(bool buildAo)
	{
		if (lightingCompositeMaterial == null)
			return ForegroundScreen;

		DrawGpuLightMasks();
		if (PerfToggles.DitherAndTileField && debugView != DebugView.NormalBuffer)
		{
			using (GraphicsDevice.DebugGroup("Myth/dither mask"))
				DrawDitherMask();
		}

		var aoStrength = 0.0f;
		if (buildAo)
		{
			using (GraphicsDevice.DebugGroup("Myth/ambient occlusion mask"))
				BuildAoMask();
			aoStrength = Manager.Visuals.AmbientOcclusionStrength;
		}

		CompositeLighting(aoStrength);

		if (PerfToggles.LightingNoise)
			ApplyLightingNoise(Screen);

		return Screen;
	}

	void ApplyLightingNoise(Target target)
	{
		if (Assets.LightingNoise == null)
			return;

		// Tile to cover the whole target, not a fixed 320x180: the editor renders zoomed-out
		// views into larger-than-screen targets (RenderWorldView), so derive the tiled quad's
		// extent from the target itself. PushMatrix scales by zoomLevel, so divide it back out
		// (a no-op at zoomLevel 1, matching in-game).
		var renderCamera = RenderCamera;
		var worldWidth = target.Width / zoomLevel;
		var worldHeight = target.Height / zoomLevel;
		Batcher.PushMatrix(Matrix3x2.CreateScale(zoomLevel));
		Batcher.PushBlend(BlendMode.Multiply);
		Batcher.PushSampler(new(TextureFilter.Nearest, TextureWrap.Repeat, TextureWrap.Repeat));
		Batcher.Image(
			Assets.LightingNoise,
			new Rect(renderCamera.X - Time.SecondsF * 5.0f, renderCamera.Y, worldWidth, worldHeight),
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

	Target ApplyAirFlash(Target input)
	{
		if (airFlashMaterial == null)
			return input;
		var output = input == GlitchScreen ? Screen : GlitchScreen;
		output.Clear(Color.Transparent);
		var color = (Vector4)game.airFlashColor;
		airFlashMaterial.Material.Fragment.SetUniformBuffer([
			color.X, color.Y, color.Z, game.AirFlashAmount,
			Time.SecondsF, 0, 0, 0,
		]);
		airFlashMaterial.Material.Fragment.Samplers[1] = new(TileField,
			new(TextureFilter.Nearest, TextureWrap.Clamp));
		Batcher.PushSampler(new(TextureFilter.Nearest, TextureWrap.Clamp, TextureWrap.Clamp));
		Batcher.PushMaterial(airFlashMaterial.Material);
		Batcher.Image(input, input.Bounds.Center, input.Bounds.Size / 2, Vector2.One, 0, Color.White);
		Batcher.PopMaterial();
		Batcher.PopSampler();
		Batcher.Render(output);
		Batcher.Clear();
		return output;
	}

	// Keep last frame's composited world so the inversion morph can cross-dissolve out of the
	// pre-swap image (option C). The swap lands between two renders, so the frame it lands on
	// promotes the previous frame's capture to InversionSnapshot and keeps writing the scratch
	// -- a re-burrow chained inside the 0.7s effect then morphs out of the room it just made.
	void CaptureInversionSnapshot(Target world)
	{
		if (!game.RoomSupportsInversion)
			return;
		if (game.RoomInversionStartedAt != inversionSnapshotStartedAt)
		{
			inversionSnapshotStartedAt = game.RoomInversionStartedAt;
			(InversionSnapshot, InversionSnapshotScratch) = (InversionSnapshotScratch, InversionSnapshot);
			inversionSnapshotCamera = inversionSnapshotScratchCamera;
			inversionSnapshotValid = inversionSnapshotScratchValid;
		}

		InversionSnapshotScratch.Clear(Color.Transparent);
		Batcher.PushSampler(new(TextureFilter.Nearest, TextureWrap.Clamp, TextureWrap.Clamp));
		Batcher.Image(world, world.Bounds.Center, world.Bounds.Size / 2, Vector2.One, 0, Color.White);
		Batcher.PopSampler();
		Batcher.Render(InversionSnapshotScratch);
		Batcher.Clear();
		inversionSnapshotScratchCamera = RenderCamera;
		inversionSnapshotScratchValid = true;
	}

	Target ApplyWorldDistortion(Target input)
	{
		if (worldDistortionMaterial == null)
			return input;
		var hasActorDistortion = DrawWorldDistortionMask();
		var morphing = game.RoomInversionEffectActive && inversionSnapshotValid;
		if (!hasActorDistortion && !game.RoomInversionEffectActive)
			return input;
		if (!hasActorDistortion)
			DistortionMask.Clear(Color.Transparent);

		var output = input == GlitchScreen ? Screen : GlitchScreen;
		output.Clear(Color.Transparent);
		var inversionOrigin = (game.RoomInversionOrigin - RenderCamera) * zoomLevel;
		var snapshotOffset = (RenderCamera - inversionSnapshotCamera) * zoomLevel;
		worldDistortionMaterial.Material.Fragment.SetUniformBuffer([
			input.Width, input.Height, Manager.Visuals.WorldDistortionPixels, RoomInversionDistortionPixels,
			inversionOrigin.X, inversionOrigin.Y, game.RoomInversionEffectProgress,
			game.RoomInversionEffectActive ? 1 : 0,
			snapshotOffset.X, snapshotOffset.Y, morphing ? 1 : 0, 0,
		]);
		worldDistortionMaterial.Material.Fragment.Samplers[1] = new(DistortionMask,
			new(TextureFilter.Linear, TextureWrap.Clamp));
		worldDistortionMaterial.Material.Fragment.Samplers[2] = new(TileField,
			new(TextureFilter.Nearest, TextureWrap.Clamp));
		worldDistortionMaterial.Material.Fragment.Samplers[3] = new(InversionSnapshot,
			new(TextureFilter.Nearest, TextureWrap.Clamp));
		Batcher.PushSampler(new(TextureFilter.Nearest, TextureWrap.Clamp, TextureWrap.Clamp));
		Batcher.PushMaterial(worldDistortionMaterial.Material);
		Batcher.Image(input, input.Bounds.Center, input.Bounds.Size / 2, Vector2.One, 0, Color.White);
		Batcher.PopMaterial();
		Batcher.PopSampler();
		Batcher.Render(output);
		Batcher.Clear();
		return output;
	}

	bool DrawWorldDistortionMask()
	{
		var hasDistortion = false;
		for (var i = 0; i < rendering.Count; i++)
			if (rendering[i].Visible && rendering[i].HasWorldDistortion)
			{
				hasDistortion = true;
				break;
			}
		if (!hasDistortion)
			return false;

		DistortionMask.Clear(Color.Transparent);
		PushWorldMatrix();
		foreach (var actor in rendering)
		{
			if (!actor.Visible || !actor.HasWorldDistortion)
				continue;
			Batcher.PushMatrix(actor.Position);
			actor.RenderWorldDistortion(Batcher);
			Batcher.PopMatrix();
		}
		Batcher.PopMatrix();
		Batcher.Render(DistortionMask);
		Batcher.Clear();
		return true;
	}

	static float RoomInversionDistortionPixels => 4.0f;
	Vector2 inversionSnapshotCamera;
	Vector2 inversionSnapshotScratchCamera;
	bool inversionSnapshotValid;
	bool inversionSnapshotScratchValid;
	float inversionSnapshotStartedAt = float.NegativeInfinity;

	void CompositeLighting(float aoStrength)
	{
		var material = lightingCompositeMaterial!.Material;

		Screen.Clear(Color.Transparent);
		material.Fragment.Samplers[1] = new(BackgroundLightMask, new(TextureFilter.Linear, TextureWrap.Clamp));
		material.Fragment.Samplers[2] = new(TileField, new(TextureFilter.Nearest, TextureWrap.Clamp));
		material.Fragment.Samplers[3] = new(BackgroundEmissive, new(TextureFilter.Nearest, TextureWrap.Clamp));
		material.Fragment.Samplers[4] = new(AoMask, new(TextureFilter.Linear, TextureWrap.Clamp));
		material.Fragment.Samplers[5] = new(BackgroundCoverage, new(TextureFilter.Nearest, TextureWrap.Clamp));
		material.Fragment.Samplers[6] = new(ForegroundScreen, new(TextureFilter.Nearest, TextureWrap.Clamp));
		material.Fragment.Samplers[7] = new(ForegroundLightMask, new(TextureFilter.Linear, TextureWrap.Clamp));
		material.Fragment.Samplers[8] = new(ForegroundEmissive, new(TextureFilter.Nearest, TextureWrap.Clamp));
		material.Fragment.Samplers[9] = new(NormalBuffer, new(TextureFilter.Nearest, TextureWrap.Clamp));
		material.Fragment.Samplers[10] = new(SporeFogMask, new(TextureFilter.Linear, TextureWrap.Clamp));
		material.Fragment.Samplers[11] = new(ActorTileField, new(TextureFilter.Nearest, TextureWrap.Clamp));
		var glitchPosition = PlayerDitherGlitchPosition();
		var renderCamera = RenderCamera;
		var aoOffset = Manager.Visuals.AmbientOcclusionOffset;
		Span<float> uniforms = stackalloc float[24 + DitherAnchorFloatCount];
		var i = 0;
		uniforms[i++] = Screen.Width;
		uniforms[i++] = Screen.Height;
		uniforms[i++] = renderCamera.X;
		uniforms[i++] = renderCamera.Y;
		uniforms[i++] = CurrentDitherEnabled() ? 1.0f : 0.0f;
		uniforms[i++] = CurrentDitherScale();
		uniforms[i++] = CurrentDitherStrength() * Manager.Visuals.DitherStrength;
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
		uniforms[i++] = Manager.Visuals.AmbientOcclusionMinVisibility;
		uniforms[i++] = CurrentDitherSlopeBoost();
		uniforms[i++] = CurrentDitherQuantization() ? Manager.Visuals.NormalDitherLevels : 0;
		uniforms[i++] = actorTileFieldPopulated ? 1 : 0;
		uniforms[i++] = Manager.Visuals.RimLightStrength;
		ditherAnchorOffsets.CopyTo(uniforms[i..]);
		material.Fragment.SetUniformBuffer(uniforms);

		Batcher.PushSampler(new(TextureFilter.Nearest, TextureWrap.Clamp, TextureWrap.Clamp));
		Batcher.PushMaterial(material);
		Batcher.Image(BackgroundScreen, BackgroundScreen.Bounds.Center, BackgroundScreen.Bounds.Size / 2, Vector2.One, 0, Color.White);
		Batcher.PopMaterial();
		Batcher.PopSampler();
		Batcher.Render(Screen);
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
		var radius = Manager.Visuals.AmbientOcclusionBlur;
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
		DrawDynamicTileFields();
		Batcher.PopSampler();
		Batcher.PopMatrix();
		Batcher.Render(TileField);
		Batcher.Clear();
	}

	void DrawActorTileField()
	{
		if (!ShowBackgroundLayer || !hasProceduralWalls)
		{
			ClearActorTileField();
			return;
		}

		ActorTileField.Clear(Color.Transparent);
		proceduralWallRenderer?.Render(ActorTileField, rendering, RenderCamera, zoomLevel, Time.SecondsF,
			ProceduralWallRenderer.Pass.ActorTileField);
		actorTileFieldPopulated = true;
	}

	void ClearActorTileField()
	{
		if (!actorTileFieldPopulated)
			return;
		ActorTileField.Clear(Color.Transparent);
		actorTileFieldPopulated = false;
	}

	void DrawCachedRoomTileField(RoomTileFieldCache cache) =>
		Batcher.Image(cache.Target, cache.Room.WorldBounds.TopLeft, Color.White);

	// State for tileFieldStampColor so the per-vertex callback can be cached once instead of
	// reallocating a closure per actor per frame.
	char tileFieldStampTile;
	int tileFieldStampAnchor;
	Func<Point2, Color>? tileFieldStampColor;
	static readonly Func<char, Point2, Color> HiddenCoverTileFieldColor = static (tile, worldTile) => TileFieldColor(tile, worldTile);

	void DrawDynamicTileFields()
	{
		if (!ShowForegroundLayer || tileFieldStampMaterial == null)
			return;

		var anchor = 1;
		Batcher.PushBlend(TileFieldStampBlend);
		Batcher.PushMaterial(tileFieldStampMaterial.Material);
		// Cached delegate + mutable state (per-actor tile/anchor) so the per-vertex color callback
		// doesn't allocate a closure each frame; see drawLightMaskLightAction for the same idiom.
		tileFieldStampColor ??= worldTile => TileFieldColor(tileFieldStampTile, worldTile, tileFieldStampAnchor);
		foreach (var actor in rendering)
		{
			if (actor is DitherCorruption { Active: true } corruption)
			{
				corruption.RenderTileField(Batcher, Time.SecondsF);
				continue;
			}

			if (actor is HiddenCoverGrid { Visible: true } hiddenCover)
			{
				Batcher.PushMatrix(hiddenCover.Position);
				hiddenCover.RenderTileField(Batcher, HiddenCoverTileFieldColor);
				Batcher.PopMatrix();
				continue;
			}

			if (actor is NeuralTextureActor { Visible: true, Layer: Factory.TileLayer.Foreground, MaterialTile: not '\0' } texture)
			{
				tileFieldStampTile = texture.MaterialTile;
				tileFieldStampAnchor = 0;
				Batcher.PushMatrix(texture.Position);
				texture.RenderTileField(Batcher, tileFieldStampColor);
				Batcher.PopMatrix();
				continue;
			}

			if (actor is not IDualGridRect { Visible: true } skin)
				continue;

			// Anchor slots are limited, so only a skin that has travelled from where it was authored
			// needs one; anything still in place dithers against the world grid like the tiles do.
			var anchorId = skin.DitherAnchorOffset != Vector2.Zero && anchor < MaxDitherAnchors ? anchor++ : 0;
			if (anchorId > 0)
			{
				var offset = skin.DitherAnchorOffset;
				var at = anchorId * 4;
				ditherAnchorOffsets[at] = offset.X;
				ditherAnchorOffsets[at + 1] = offset.Y;
			}

			tileFieldStampTile = skin.MaterialTile();
			tileFieldStampAnchor = anchorId;
			Batcher.PushMatrix(skin.Position);
			skin.RenderTileField(Batcher, tileFieldStampColor);
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
			cache.Target.Attachments[0].Format != TextureFormat.R8G8B8A8)
		{
			cache?.Dispose();
			cache = new(renderRoom, CreateRenderTarget(bounds.Width, bounds.Height, TextureFormat.R8G8B8A8, $"RoomTileField:{renderRoom.Id}"));
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
		Batcher.PushBlend(DitherBufferBlend);
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
		Batcher.PopBlend();
		Batcher.Render(NormalBuffer);
		Batcher.Clear();
	}

	static float DitherAmountForActor(Actor actor)
		=> actor.LightingDithered ? 1.0f : ActorDitherAmount;

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
				Batcher.Rect(actor.Hitbox.Rect + actor.Position, Color.Red);
				continue;
			}
			Batcher.PushMatrix(actor.Position);
			actor.RenderShadow(Batcher, Color.Red);
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
				if (Factory.Find(tile) is not { Layer: Factory.TileLayer.Foreground } entry)
					continue;
				if (entry.NeuralTexture != null)
					continue;

				var image = entry.Image;
				if (entry.SlopeTilesetName is { } slopeTilesetName && renderRoom.TryGetSlopeTile(new Point2(x, y), out var slope))
				{
					var seed = Factory.StableVariantSeed(renderRoom, slopeTilesetName, new Point2(x, y));
					image = Assets.GetSlopeSubtexture(slopeTilesetName, slope.Direction, slope.Fill, slope.RunLength, slope.RunIndex, seed);
				}
				else if (entry.DualTilesetName != null)
					continue;
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
					var materialMask = renderRoom.GetDualMaterialMask(tileType, layer, x, y);
					if (materialMask == 0)
						continue;

					var joinMask = renderRoom.GetDualTerrainJoinMask(layer, x, y);
					if (joinMask == 0)
						continue;

					var worldTile = renderRoom.TileSpacePosition + new Point2(x, y);
					var image = DualGrid.LookupArt(tileset, DualGrid.MaskToTile[joinMask], worldTile, tileType).Tile;
					if (image.Texture == null)
						continue;

					var position = new Vector2(
						renderRoom.WorldBounds.X + (x - 0.5f) * TileSize,
						renderRoom.WorldBounds.Y + (y - 0.5f) * TileSize);
					DualGrid.DrawTile(Batcher, image, position, materialMask, TileFieldColor(tileType, worldTile));
				}
		}
	}

	static Color TileFieldColor(char tile, Point2 worldTile, int anchor = 0)
	{
		var hash = TileFieldHash(tile, worldTile);
		var variant = (hash & 255) / 255.0f;
		var family = TileFieldFamily(tile);
		var solid = Factory.Find(tile) is { BlocksMovement: true } ? 1 : 0;
		return new Color(variant, ((family << 5) | anchor) / 255.0f, solid, 1.0f);
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

	Action<Vector2, float, float, float, Color>? gatherGpuLightAction;
	Action<Vector2, float, float, float, Color>? gatherAutomaticLightAction;
	readonly List<ShadowSegment> gpuSegmentScratch = [];
	readonly HashSet<Actor> gpuLightEmitters = [];
	readonly List<AutomaticLight> automaticLights = [];
	Actor? gatheringGpuLightActor;
	readonly record struct AutomaticLight(Vector2 Position, float Radius, float Intensity, float Height, Color Color)
	{
		public float Score => Radius * Radius * Intensity;
	}
	const int MaxAutomaticLights = 16;

	void DrawGpuLightMasks()
	{
		if (gpuLighting is not { Supported: true } renderer)
		{
			var ambient = ScaleColor(CurrentAmbientColor(), CurrentAmbientIntensity());
			BackgroundLightMask.Clear(ambient);
			ForegroundLightMask.Clear(ambient);
			SporeFogMask.Clear(Color.Transparent);
			return;
		}

		renderer.Begin(game.VisibleWorldBounds);
		foreach (var visibleRoom in VisibleRooms())
			for (var i = 0; i < visibleRoom.ShadowSegments.Count; i++)
			{
				var segment = visibleRoom.ShadowSegments[i];
				renderer.AddSegment(segment.A, segment.B, castsSunShadow: i >= 4);
			}

		gpuSegmentScratch.Clear();
		foreach (var actor in Actors)
		{
			if (actor is SporeFogVolume fog)
				renderer.AddSporeFogVolume(fog.WorldRect, fog.Strength, fog.Feather);
			if (actor is DualGrid { Layer: Factory.TileLayer.Foreground } dualGrid)
				dualGrid.AppendAllShadowSegments(gpuSegmentScratch);
			if (actor is HiddenCoverGrid hiddenCover)
				hiddenCover.AppendAllShadowSegments(gpuSegmentScratch);
			if (actor is IShapeOccluder { CastsShadow: true } occluder)
				occluder.AppendOccluderSegments(gpuSegmentScratch);
		}
		foreach (var segment in gpuSegmentScratch)
			renderer.AddSegment(segment.A, segment.B);

		foreach (var actor in Actors)
			if (actor is HiddenWallGrid hiddenWalls)
				hiddenWalls.ForEachVisibleCell(renderer, static (gpu, rect, _, _) => gpu.AddRect(rect));

		gpuLightEmitters.Clear();
		gatherGpuLightAction ??= (position, radius, intensity, height, color) =>
		{
			if (gpuLighting!.AddLight(position, radius, intensity, height, color))
				gpuLightEmitters.Add(gatheringGpuLightActor!);
		};
		foreach (var actor in Actors)
		{
			if (actor is Light { Intensity: > 0, Enabled: true } light && (game.IsEditorPreview || !LightIsCovered(light.Position)))
			{
				var renderScale = light.RenderScale;
				var added = renderer.AddLight(
					light.Position,
					light.Radius * renderScale,
					light.Intensity * renderScale,
					light.Height,
					light.LightColor,
					light.Cookie,
					light.CookieAngleDegrees,
					light.CookieSpeedDegrees,
					light.CookieScale,
					light.CookieStrength,
					light.CookieFeather,
					light.CookieCutoff,
					light.RenderShadowSourceRadius,
					light.ShadowSoftness,
					light.ShadowNoise,
					light.ShadowNoiseSpeed);
				if (added && light.SpriteShadowOwner is { } owner)
					gpuLightEmitters.Add(owner);
			}
			gatheringGpuLightActor = actor;
			actor.ForEachLight(gatherGpuLightAction);
		}
		gatheringGpuLightActor = null;

		automaticLights.Clear();
		gatherAutomaticLightAction ??= (position, radius, intensity, height, color) =>
		{
			if (radius > 0 && intensity > 0 &&
				(position - Vector2.Clamp(position, game.VisibleWorldBounds.TopLeft, game.VisibleWorldBounds.BottomRight)).LengthSquared() < radius * radius)
				automaticLights.Add(new(position, radius, intensity, height, color));
		};
		foreach (var actor in Actors)
			actor.ForEachAutomaticLight(gatherAutomaticLightAction);
		automaticLights.Sort(static (a, b) => b.Score.CompareTo(a.Score));
		for (var i = 0; i < automaticLights.Count && i < MaxAutomaticLights; i++)
		{
			var light = automaticLights[i];
			renderer.AddLight(light.Position, light.Radius, light.Intensity, light.Height, light.Color);
		}
		DrawGpuSpriteShadowMask();

		renderer.Render(
			NormalBuffer,
			SpriteShadowMask,
			Assets.LightingNoise,
			BackgroundLightMask,
			ForegroundLightMask,
			SporeFogMask,
			RenderCamera,
			zoomLevel,
			Time.SecondsF,
			CurrentSporeFogStrength(),
			CurrentSporeFogColor(),
			CurrentAmbientColor(),
			CurrentAmbientIntensity(),
			CurrentSunDirection(),
			CurrentSunColor(),
			CurrentSunStrength(),
			PerfToggles.SoftShadows,
			Manager.Visuals);
	}

	void DrawGpuSpriteShadowMask()
	{
		SpriteShadowMask.Clear(Color.Transparent);
		PushWorldMatrix();
		foreach (var actor in Actors)
			if (actor.Visible && actor.CastsSpriteShadow && actor is not Grid && !gpuLightEmitters.Contains(actor))
			{
				Batcher.PushMatrix(actor.Position);
				actor.RenderShadow(Batcher, Color.White);
				Batcher.PopMatrix();
			}
		Batcher.PopMatrix();
		Batcher.Render(SpriteShadowMask);
		Batcher.Clear();
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

		return LerpColor(room!.AmbientColor, nextRoom.AmbientColor, Ease.Cube.InOut(nextRoomEase));
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
			LerpColor(room!.BackdropColor, nextRoom.BackdropColor, amount),
			LerpColor(room.BackdropColor2 ?? room.BackdropColor, nextRoom.BackdropColor2 ?? nextRoom.BackdropColor, amount));
	}

	float CurrentAmbientIntensity()
	{
		if (nextRoom == null)
			return room?.AmbientIntensity ?? Room.DefaultAmbientIntensity;

		return float.Lerp(room!.AmbientIntensity, nextRoom.AmbientIntensity, Ease.Cube.InOut(nextRoomEase));
	}

	float CurrentLightNoise()
	{
		if (nextRoom == null)
			return room?.LightNoise ?? Room.DefaultLightNoise;

		return float.Lerp(room!.LightNoise, nextRoom.LightNoise, Ease.Cube.InOut(nextRoomEase));
	}

	float CurrentSporeFogStrength()
	{
		if (nextRoom == null)
			return room?.SporeFogStrength ?? Room.DefaultSporeFogStrength;

		return float.Lerp(room!.SporeFogStrength, nextRoom.SporeFogStrength, Ease.Cube.InOut(nextRoomEase));
	}

	Color CurrentSporeFogColor()
	{
		if (nextRoom == null)
			return room?.SporeFogColor ?? Room.DefaultSporeFogColor;

		return Color.Lerp(room!.SporeFogColor, nextRoom.SporeFogColor, Ease.Cube.InOut(nextRoomEase));
	}

	// Dither floats blend across a room transition like the other lighting params; the two bools
	// can't lerp, so they snap to the destination room at the transition midpoint.
	bool CurrentDitherEnabled()
	{
		if (nextRoom == null)
			return room?.LightingDitherEnabled ?? Room.DefaultLightingDitherEnabled;

		return Ease.Cube.InOut(nextRoomEase) < 0.5f ? room!.LightingDitherEnabled : nextRoom.LightingDitherEnabled;
	}

	float CurrentDitherScale()
	{
		if (nextRoom == null)
			return room?.LightingDitherScale ?? Room.DefaultLightingDitherScale;

		return float.Lerp(room!.LightingDitherScale, nextRoom.LightingDitherScale, Ease.Cube.InOut(nextRoomEase));
	}

	float CurrentDitherStrength()
	{
		if (nextRoom == null)
			return room?.LightingDitherStrength ?? Room.DefaultLightingDitherStrength;

		return float.Lerp(room!.LightingDitherStrength, nextRoom.LightingDitherStrength, Ease.Cube.InOut(nextRoomEase));
	}

	float CurrentDitherSlopeBoost()
	{
		if (nextRoom == null)
			return room?.NormalDitherSlopeBoost ?? Room.DefaultNormalDitherSlopeBoost;

		return float.Lerp(room!.NormalDitherSlopeBoost, nextRoom.NormalDitherSlopeBoost, Ease.Cube.InOut(nextRoomEase));
	}

	bool CurrentDitherQuantization()
	{
		if (nextRoom == null)
			return room?.NormalDitherQuantization ?? Room.DefaultNormalDitherQuantization;

		return Ease.Cube.InOut(nextRoomEase) < 0.5f ? room!.NormalDitherQuantization : nextRoom.NormalDitherQuantization;
	}

	Color CurrentSunColor()
	{
		if (nextRoom == null)
			return room?.SunColor ?? Room.DefaultSunColor;

		return LerpColor(room!.SunColor, nextRoom.SunColor, Ease.Cube.InOut(nextRoomEase));
	}

	float CurrentSunStrength()
	{
		if (nextRoom == null)
			return room?.SunStrength ?? 0.0f;

		return float.Lerp(room!.SunStrength, nextRoom.SunStrength, Ease.Cube.InOut(nextRoomEase));
	}

	Vector2 CurrentSunDirection()
	{
		if (nextRoom == null)
			return room?.SunDirection ?? Room.DefaultSunDirection;

		var direction = Vector2.Lerp(room!.SunDirection, nextRoom.SunDirection, Ease.Cube.InOut(nextRoomEase));
		return Room.NormalizeOrDefault(direction, Room.DefaultSunDirection);
	}

	static Color LerpColor(Color from, Color to, float amount)
		=> new(Vector4.Lerp(from, to, amount));

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

	void DrawNormalBuffer()
	{
		NormalBuffer.Clear(new Color(0.5f, 0.5f, 1.0f, 0.0f));
		if (ShowBackgroundLayer && hasProceduralWalls)
			proceduralWallRenderer?.Render(NormalBuffer, rendering, RenderCamera, zoomLevel, Time.SecondsF,
				ProceduralWallRenderer.Pass.Normals);
		RoomNormalCache? currentRoomNormalCache = null;
		RoomNormalCache? nextRoomNormalCache = null;
		if (ShowForegroundLayer)
		{
			if (room != null)
				currentRoomNormalCache = GetRoomNormalCache(room);
			if (nextRoom != null && nextRoom != room)
				nextRoomNormalCache = GetRoomNormalCache(nextRoom);
		}

		Batcher.PushBlend(NormalBufferBlend);
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
		Batcher.PopBlend();
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

	// Scratch for RebuildRoomNormalCache, reused across rooms. The solid mask is one byte per room
	// pixel and the suppress mask one per room *tile* (what SuppressActorDrivenRoomNormals actually
	// resolves to), both flat rather than bool[,] so the inner loop indexes without the
	// multidimensional bounds check. Rebuilding allocated two room-pixel bool[,] every time --
	// 4MB per entry on a 6x6-screen room, thrown away each frame the cache went dirty.
	byte[] roomNormalMask = [];
	byte[] roomNormalSuppress = [];

	void RebuildRoomNormalCache(RoomNormalCache cache)
	{
		var renderRoom = cache.Room;
		var target = cache.Target;
		int width = target.Width, height = target.Height;
		var origin = renderRoom.WorldBounds.TopLeft;
		var tileWidth = renderRoom.Size.X;

		Grow(ref roomNormalMask, width * height);
		Grow(ref roomNormalSuppress, tileWidth * renderRoom.Size.Y);
		var mask = roomNormalMask;
		var suppress = roomNormalSuppress;

		FillRoomNormalMask(mask, width, height, renderRoom, origin);
		SuppressActorDrivenRoomNormals(suppress, renderRoom);

		var previousMatrix = Batcher.Matrix;
		Batcher.Matrix = Matrix3x2.Identity;
		target.Clear(Color.Transparent);
		for (var y = 0; y < height; y++)
		{
			var row = y * width;
			var above = row - width;
			var below = row + width;
			var suppressRow = (y / Game.TileSize) * tileWidth;
			var runStart = -1;
			var runColor = Color.Transparent;

			// Slide the horizontal neighbours along the row instead of re-reading them per pixel:
			// x's right neighbour is x+1's centre next step.
			var solidLeft = false;
			var solid = width > 0 && mask[row] != 0;

			for (var x = 0; x < width; x++)
			{
				var solidRight = x + 1 < width && mask[row + x + 1] != 0;
				var color = Color.Transparent;
				if (solid)
				{
					var sx = (solidLeft ? 1 : 0) - (solidRight ? 1 : 0);
					var sy = (y > 0 && mask[above + x] != 0 ? 1 : 0) - (y + 1 < height && mask[below + x] != 0 ? 1 : 0);
					if ((sx != 0 || sy != 0) && suppress[suppressRow + x / Game.TileSize] == 0)
						color = EncodeNormal(new Vector3(sx, sy, 1), 1);
				}
				solidLeft = solid;
				solid = solidRight;

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
				Batcher.Rect(new Rect(runStart, y, width - runStart, 1), runColor);
		}
		Batcher.Render(target);
		Batcher.Clear();
		Batcher.Matrix = previousMatrix;
		cache.Dirty = false;

		static void Grow(ref byte[] buffer, int length)
		{
			if (buffer.Length < length)
				buffer = new byte[length];
			Array.Clear(buffer, 0, length);
		}
	}

	void FillRoomNormalMask(byte[] mask, int width, int height, Room renderRoom, Point2 origin)
	{
		foreach (var rect in renderRoom.CollisionRects)
			FillRoomNormalMask(mask, width, height, rect, origin);
		foreach (var polygon in renderRoom.LightOccluderPolygons)
			FillRoomNormalMask(mask, width, height, polygon, origin);
	}

	// One entry per tile, not per pixel: the source data is the tile's Factory entry, so the old
	// per-pixel mask stored the same answer 64 times and cost 64x the fill.
	static void SuppressActorDrivenRoomNormals(byte[] suppress, Room renderRoom)
	{
		for (var tileY = 0; tileY < renderRoom.Size.Y; tileY++)
			for (var tileX = 0; tileX < renderRoom.Size.X; tileX++)
			{
				var entry = renderRoom.GetEntry(new Point2(tileX, tileY));
				var actorDrawsNormals = entry?.NeuralTexture != null ||
					entry is { DualTilesetName: { } dualTilesetName } &&
					Assets.GetTileset(dualTilesetName) is { NormalTiles.Count: > 0 } or { NormalVariants.Count: > 0 };
				if (actorDrawsNormals)
					suppress[tileY * renderRoom.Size.X + tileX] = 1;
			}
	}

	static void FillRoomNormalMask(byte[] mask, int width, int height, RectInt rect, Point2 origin)
	{
		var left = Math.Clamp(rect.Left - origin.X, 0, width);
		var right = Math.Clamp(rect.Right - origin.X, 0, width);
		var top = Math.Clamp(rect.Top - origin.Y, 0, height);
		var bottom = Math.Clamp(rect.Bottom - origin.Y, 0, height);
		if (right <= left)
			return;

		for (var y = top; y < bottom; y++)
			mask.AsSpan(y * width + left, right - left).Fill(1);
	}

	static void FillRoomNormalMask(byte[] mask, int width, int height, Room.LightOccluderPolygon polygon, Point2 origin)
	{
		var left = Math.Clamp(polygon.Bounds.Left - origin.X, 0, width);
		var right = Math.Clamp(polygon.Bounds.Right - origin.X, 0, width);
		var top = Math.Clamp(polygon.Bounds.Top - origin.Y, 0, height);
		var bottom = Math.Clamp(polygon.Bounds.Bottom - origin.Y, 0, height);

		for (var y = top; y < bottom; y++)
			for (var x = left; x < right; x++)
			{
				var worldPoint = new Vector2(origin.X + x + 0.5f, origin.Y + y + 0.5f);
				if (PointInPolygon(worldPoint, polygon.Points))
					mask[y * width + x] = 1;
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
		// and re-creating ~10 of them.
		static readonly Dictionary<(GraphicsDevice Device, string Name), Shader> SharedShaders = [];

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
				SharedShaders[(graphicsDevice, name)] = next; // newly built Games pick up the reloaded shader
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
			if (SharedShaders.TryGetValue((graphicsDevice, name), out var cached))
				return cached;
			var shader = CompileShader();
			SharedShaders[(graphicsDevice, name)] = shader;
			return shader;
		}

		public static void Clear(GraphicsDevice graphicsDevice)
		{
			var keys = new List<(GraphicsDevice Device, string Name)>();
			foreach (var entry in SharedShaders)
				if (ReferenceEquals(entry.Key.Device, graphicsDevice))
				{
					entry.Value.Dispose();
					keys.Add(entry.Key);
				}
			foreach (var key in keys)
				SharedShaders.Remove(key);
		}

		Shader CompileShader()
		{
			var ext = graphicsDevice.Driver.GetShaderExtension();
			var file = $"{name}.fragment.{ext}";
			var code = Assets.TryReadCompiledShader(file)
				?? throw new FileNotFoundException($"{name} shader not found in bundled content at 'Shaders/Compiled/{file}'.", file);

			return new Shader(
				graphicsDevice,
				ShaderStage.Fragment,
				code,
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

	public void Render(in RectInt viewport, bool showHud = true, RectInt? scissor = null, bool applyCrt = true)
	{
		if (Manager.Window is not { } window)
			return;

		EnsureRenderTargets(Width, Height);
		RenderCurrentView(window, viewport, showHud, scissor, applyCrt);
		game.NoteRoomTransitionRendered();
	}

	public void RenderWorldView(in RectInt viewport, Rect worldView, bool showHud = false, RectInt? scissor = null, bool applyCrt = true)
	{
		if (Manager.Window is not { } window)
			return;

		var oldCamera = Camera;
		var oldShake = shake;
		// This path frames the world rect it is handed at 1:1: the gameplay camera's zoom
		// (Room.Zoom / the Zoom control) is not this view's to apply.
		var oldZoom = zoomLevel;
		var width = Math.Max(1, (int)MathF.Ceiling(worldView.Width));
		var height = Math.Max(1, (int)MathF.Ceiling(worldView.Height));

		EnsureRenderTargets(width, height);
		Camera = worldView.TopLeft;
		shake = Point2.Zero;
		zoomLevel = 1.0f;
		RenderCurrentView(window, viewport, showHud, scissor, applyCrt);
		Camera = oldCamera;
		shake = oldShake;
		zoomLevel = oldZoom;
	}

	// Renders `worldView` into `target`. The default 1:1 zoom is the editor's (previews, bakes,
	// exports): the rect it hands over is the picture it wants. Pass the game's own zoom to
	// reproduce what a player sees, frame pixels and all.
	public void RenderWorldToTarget(Target target, Rect worldView, bool showHud = false, float zoom = 1.0f)
	{
		var oldCamera = Camera;
		var oldShake = shake;
		var oldZoom = zoomLevel;
		var width = Math.Max(1, (int)MathF.Ceiling(worldView.Width * zoom));
		var height = Math.Max(1, (int)MathF.Ceiling(worldView.Height * zoom));

		EnsureRenderTargets(width, height);
		Camera = worldView.TopLeft;
		shake = Point2.Zero;
		zoomLevel = zoom;
		RenderCurrentView(target, target.Bounds, showHud, scissor: null, applyCrt: false);
		Camera = oldCamera;
		shake = oldShake;
		zoomLevel = oldZoom;
	}

	void EnsureRenderTargets(int width, int height)
	{
		if (Screen != null && Screen.Width == width && Screen.Height == height)
			return;

		DisposeRenderTargets();

		Screen = Track(RenderTargetPool.RentColor(GraphicsDevice, width, height, "GameScreen"));
		BackgroundScreen = Track(RenderTargetPool.RentColor(GraphicsDevice, width, height, "BackgroundScreen"));
		ForegroundScreen = Track(RenderTargetPool.RentColor(GraphicsDevice, width, height, "ForegroundScreen"));
		ColorField = Track(RenderTargetPool.RentColor(GraphicsDevice, width, height, "ColorField"));
		BackgroundEmissive = Track(RenderTargetPool.RentColor(GraphicsDevice, width, height, "BackgroundEmissive"));
		ForegroundEmissive = Track(RenderTargetPool.RentColor(GraphicsDevice, width, height, "ForegroundEmissive"));
		BackgroundLightMask = Track(CreateComputeTarget(width, height, "BackgroundLightMask", TextureFormat.R16G16B16A16Float));
		ForegroundLightMask = Track(CreateComputeTarget(width, height, "ForegroundLightMask", TextureFormat.R16G16B16A16Float));
		SporeFogMask = Track(CreateComputeTarget(width, height, "SporeFogMask"));
		GlitchScreen = Track(RenderTargetPool.RentColor(GraphicsDevice, width, height, "GlitchScreen"));
		DistortionMask = Track(RenderTargetPool.RentColor(GraphicsDevice, width, height, "DistortionMask"));
		InversionSnapshot = Track(RenderTargetPool.RentColor(GraphicsDevice, width, height, "InversionSnapshot"));
		InversionSnapshotScratch = Track(RenderTargetPool.RentColor(GraphicsDevice, width, height, "InversionSnapshotScratch"));
		inversionSnapshotValid = inversionSnapshotScratchValid = false;
		LayerTransitionScreen = Track(RenderTargetPool.RentColor(GraphicsDevice, width, height, "LayerTransitionScreen"));
		NormalBuffer = Track(RenderTargetPool.RentColor(GraphicsDevice, width, height, "NormalBuffer"));
		TileField = Track(RenderTargetPool.RentColor(GraphicsDevice, width, height, "TileField"));
		ActorTileField = Track(RenderTargetPool.RentFormat(GraphicsDevice, width, height, TextureFormat.R8G8, "ActorTileField"));
		ActorTileField.Clear(Color.Transparent);
		actorTileFieldPopulated = false;
		ParticleSolidMask = Track(RenderTargetPool.RentFormat(GraphicsDevice, width, height, TextureFormat.R8G8, "ParticleSolidMask"));
		SpriteShadowMask = Track(RenderTargetPool.RentFormat(GraphicsDevice, width, height, TextureFormat.R8, "SpriteShadowMask"));
		ReflectionScreen = Track(RenderTargetPool.RentColor(GraphicsDevice, width, height, "ReflectionScreen"));
		ReflectionMask = Track(RenderTargetPool.RentFormat(GraphicsDevice, width, height, TextureFormat.R8, "ReflectionMask"));
		AoMask = Track(RenderTargetPool.RentFormat(GraphicsDevice, width, height, TextureFormat.R8, "AoMask"));
		AoBlurScratch = Track(RenderTargetPool.RentFormat(GraphicsDevice, width, height, TextureFormat.R8, "AoBlurScratch"));
		BackgroundCoverage = Track(RenderTargetPool.RentFormat(GraphicsDevice, width, height, TextureFormat.R8, "BackgroundCoverage"));
	}

	Target Track(Target target)
	{
		debugTargets.Add(target);
		return target;
	}

	Target CreateComputeTarget(int width, int height, string name, TextureFormat format = TextureFormat.Color)
	{
		ReadOnlySpan<TextureFormat> attachments = [format];
		return new Target(GraphicsDevice, width, height, attachments, TextureFlags.ComputeWrite, name);
	}


	// Used by the per-room normal/tile-field caches (not the pooled main render targets).
	Target CreateRenderTarget(int width, int height, TextureFormat format, string name)
	{
		ReadOnlySpan<TextureFormat> attachments = [format];
		return new Target(GraphicsDevice, width, height, attachments, name: name);
	}

	Target EnsureCompositeTarget(Target output, in RectInt present)
	{
		var video = Manager.Video;
		var size = output.Bounds.Size * video.CompositeScale(video.FitScale(present.Size, output.Bounds.Size));
		if (compositeTarget != null && compositeTarget.Bounds.Size == size)
			return compositeTarget;
		if (compositeTarget != null)
			RenderTargetPool.Return(compositeTarget);
		return compositeTarget = RenderTargetPool.RentColor(GraphicsDevice, size.X, size.Y, "GameComposite");
	}

	// debugTargets holds exactly the render targets created by EnsureRenderTargets, so
	// return them all to the pool for reuse. Clearing the list makes a repeat call a no-op
	// (no double-return) and the property fields are always reassigned before next use.
	void DisposeRenderTargets()
	{
		layerTransitionCaptured = false;
		foreach (var target in debugTargets)
			if (target == BackgroundLightMask || target == ForegroundLightMask || target == SporeFogMask)
				target.Dispose();
			else
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

	void DisposeRoomColorFieldCaches()
	{
		foreach (var cache in roomColorFieldCaches.Values)
			cache.Dispose();
		roomColorFieldCaches.Clear();
	}

	readonly Dictionary<Room, RoomNormalCache> roomNormalCaches = [];
	readonly Dictionary<Room, RoomTileFieldCache> roomTileFieldCaches = [];
	readonly Dictionary<RoomColorField, RoomColorFieldTextureCache> roomColorFieldCaches = [];

	internal void PruneInactiveRoomCaches()
	{
		PruneInactiveRoomNormalCaches();
		PruneInactiveRoomTileFieldCaches();
		PruneInactiveRoomColorFieldCaches();
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

	void PruneInactiveRoomColorFieldCaches()
	{
		List<RoomColorField>? remove = null;
		foreach (var (field, cache) in roomColorFieldCaches)
		{
			if (IsActiveRoomColorField(field))
				continue;
			remove ??= [];
			remove.Add(field);
			cache.Dispose();
		}
		if (remove == null)
			return;
		foreach (var field in remove)
			roomColorFieldCaches.Remove(field);
	}

	bool IsActiveRoomColorField(RoomColorField field)
		=> room != null && (field == room.ForegroundColorField || field == room.BackgroundColorField)
			|| nextRoom != null && (field == nextRoom.ForegroundColorField || field == nextRoom.BackgroundColorField);

	Texture GetRoomColorFieldTexture(RoomColorField field)
	{
		if (!roomColorFieldCaches.TryGetValue(field, out var cache) || cache.Texture.Size != field.Size)
		{
			cache?.Dispose();
			cache = new(new Texture(GraphicsDevice, field.Size.X, field.Size.Y, field.Pixels, name: "RoomColorField"));
			roomColorFieldCaches[field] = cache;
		}
		else if (cache.Version != field.Version)
			cache.Texture.SetData<Color>(field.Pixels);
		cache.Version = field.Version;
		return cache.Texture;
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
	// Fullscreen tuning display: 3008x1692 logical points at 2x backing scale.
	static Vector2 RenderTargetShakeReferenceSize => new(6016, 3384);
	static readonly BlendMode EmissiveOccluderBlend = new(BlendOp.Add, BlendFactor.Zero, BlendFactor.OneMinusSrcAlpha);
	static readonly BlendMode ColorFieldCopyBlend = new(BlendOp.Add, BlendFactor.One, BlendFactor.Zero);
	static readonly BlendMode TileFieldStampBlend = new(
		BlendOp.Add, BlendFactor.One, BlendFactor.Zero,
		BlendOp.Add, BlendFactor.One, BlendFactor.Zero,
		BlendMask.Red | BlendMask.Green | BlendMask.Blue, Color.White);
	static readonly BlendMode NormalBufferBlend = new(
		BlendOp.Add, BlendFactor.One, BlendFactor.OneMinusSrcAlpha,
		BlendOp.Add, BlendFactor.One, BlendFactor.OneMinusSrcAlpha,
		BlendMask.Red | BlendMask.Green | BlendMask.Alpha, Color.White);
	static readonly BlendMode DitherBufferBlend = new(
		BlendOp.Add, BlendFactor.One, BlendFactor.OneMinusSrcAlpha,
		BlendOp.Add, BlendFactor.One, BlendFactor.OneMinusSrcAlpha,
		BlendMask.Blue, Color.White);

	static float DitherGlitchRadius => 150.0f;
	static Vector2 AtlasWanderSpeed => new(13.0f, 7.0f);
	static float AtlasWanderInitialSpeed => 0.1f;
	static float AtlasWanderRampDuration => 10.0f;
	const int MaxDitherAnchors = 32;
	const int DitherAnchorFloatCount = MaxDitherAnchors * 4;
	const float ActorDitherAmount = 0.2f;
	readonly Batcher Batcher;
	readonly GpuLighting? gpuLighting;
	readonly ProceduralWallRenderer? proceduralWallRenderer;
	readonly float[] ditherAnchorOffsets = new float[DitherAnchorFloatCount];
	bool hasProceduralWalls;
	bool actorTileFieldPopulated;
	public Target Screen { get; private set; } = null!;

	Target BackgroundScreen { get; set; } = null!;
	Target ForegroundScreen { get; set; } = null!;
	Target ColorField { get; set; } = null!;
	Target BackgroundEmissive { get; set; } = null!;
	Target ForegroundEmissive { get; set; } = null!;
	Target BackgroundLightMask { get; set; } = null!;
	Target ForegroundLightMask { get; set; } = null!;
	Target SporeFogMask { get; set; } = null!;
	Target GlitchScreen { get; set; } = null!;
	Target DistortionMask { get; set; } = null!;
	Target InversionSnapshot { get; set; } = null!;
	Target InversionSnapshotScratch { get; set; } = null!;
	Target LayerTransitionScreen { get; set; } = null!;
	Target NormalBuffer { get; set; } = null!;
	Target TileField { get; set; } = null!;
	Target ActorTileField { get; set; } = null!;
	Target ParticleSolidMask { get; set; } = null!;
	Target SpriteShadowMask { get; set; } = null!;
	Target ReflectionScreen { get; set; } = null!;
	Target ReflectionMask { get; set; } = null!;
	Target AoMask { get; set; } = null!;
	Target AoBlurScratch { get; set; } = null!;
	Target BackgroundCoverage { get; set; } = null!;

	// Every render target created in EnsureRenderTargets, in creation order, for the
	// debug render-target overlay (toggled with G). Populated via Track().
	readonly List<Target> debugTargets = [];

	public IReadOnlyList<Target> DebugTargets => debugTargets;
	public bool ShowForegroundLayer { get; set; } = true;
	public bool ShowBackgroundLayer { get; set; } = true;
	readonly ReloadableMaterial? crtMaterial;
	Target? compositeTarget;
	readonly ReloadableMaterial? atlasWanderMaterial;
	readonly ReloadableMaterial? colorFieldMaterial;
	readonly ReloadableMaterial? lightingCompositeMaterial;
	readonly ReloadableMaterial? reflectionDistortMaterial;
	readonly ReloadableMaterial? tileFieldStampMaterial;
	readonly ReloadableMaterial? tileFieldDebugMaterial;
	readonly ReloadableMaterial? pixelSortGlitchMaterial;
	readonly ReloadableMaterial? airFlashMaterial;
	readonly ReloadableMaterial? worldDistortionMaterial;
#if !MYTHSHIP
	readonly ReloadableMaterial? blackAndWhiteMaterial;
#endif
	readonly ReloadableMaterial? blurMaterial;
	readonly ReloadableMaterial? sdfAuraMaterial;
	readonly ReloadableMaterial? backgroundDecalMaskMaterial;
	readonly ReloadableMaterial? foregroundDecalMaskMaterial;
	readonly ReloadableMaterial? backgroundColorFieldDecalMaterial;
	readonly ReloadableMaterial? foregroundColorFieldDecalMaterial;
	readonly ReloadableMaterial? coverageMaskMaterial;
	bool hasBackgroundColorField;
	bool hasForegroundColorField;
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

	sealed class RoomColorFieldTextureCache(Texture texture) : IDisposable
	{
		public readonly Texture Texture = texture;
		public int Version = -1;
		public void Dispose() => Texture.Dispose();
	}
}
