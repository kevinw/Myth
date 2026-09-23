using System.Numerics;
using Foster.Framework;
#if EDITOR
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using ImGuiNET;
using static ImGuiNET.ImGui;
#endif

namespace Myth;

sealed class VisualTuning
{
	public float CrtBloomAmount = 0.09f;
	public float CrtBloomThreshold = 0.35f;
	public float CrtVignetteAmount = 0.06f;
	// In output pixels: the fringe is a property of the display, not of the art.
	public float CrtRgbOffset = 1.5f;
	public float CrtScanlineStrength = 0.1f;
	public float AmbientOcclusionStrength = 1.0f;
	public float AmbientOcclusionBlur = 8.0f;
	public Vector2 AmbientOcclusionOffset = new(0, -3);
	public float AmbientOcclusionMinVisibility = 0.35f;
	public float SunShadowSoftness = 2.3f;
	public float SpriteShadowStrength = 0.5f;
	public float SpriteShadowMinLength = 4.0f;
	public float SpriteShadowMaxLength = 28.0f;
	public float ShadowBias = 0.5f;
	public float DirectLightPresence = 1.0f;
	public float IndirectUnderpaint = 0.0f;
	public float LightingExposure = 0.0f;
	public float LightingShoulder = 0.75f;
	public float NormalLightFloor = 0.12f;
	public float NormalLightGain = 2.15f;
	public float IndirectBackdropStrength = 1.0f;
	public float IndirectLightStrength = 0.9f;
	public float IndirectForegroundWrapDepth = 8.0f;
	public float IndirectForegroundWrapStrength = 1.0f;
	public float IndirectWrapPatternStrength = 1.0f;
	public float IndirectForegroundStrength = 0.85f;
	public float IndirectResponseCurve = 0.85f;
	public float IndirectActorGain = 1.25f;
	public float IndirectHeroFill = 0.12f;
	public float IndirectLightRange = 192.0f;
	public float IndirectLightFalloff = 0.012f;
	public float IndirectBounceGain = 1.05f;
	public float IndirectBounceSaturation = 1.25f;
	public float IndirectHighlightColorRetention = 1.0f;
	public float IndirectBounceAlbedoFloor = 0.3f;
	public float IndirectBounceWarmth = 0.35f;
	public float IndirectColorBleed = 1.1f;
	public float IndirectForegroundReflectivity = 0.65f;
	public float IndirectStoneReflectivity = 0.95f;
	public float IndirectOrganicReflectivity = 1.25f;
	public float IndirectWaterReflectivity = 0.55f;
	public float IndirectOtherReflectivity = 1.0f;
	public float IndirectDirectionality = 0.7f;
	public float IndirectNormalWrap = 0.35f;
	public float DitherStrength = 1.0f;
	public float DitherBandSpacing = 1.0f;
	public float SporeFogScale = 1.0f / 80.0f;
	public Vector2 SporeFogDrift = new(2.0f, -0.5f);
	public float SporeFogContrast = 1.8f;
	public float SporeFogPointStrength = 0.65f;
	public float SporeFogSunStrength = 0.3f;
	public float WorldDistortionPixels = 3.0f;
}

#if EDITOR
public partial class Manager
{
	bool visualsOpen;

	// The shipped values every row resets to, and the field walk that drives undo capture and the
	// C# export, so adding a knob to VisualTuning needs no bookkeeping beyond its row.
	static readonly VisualTuning VisualDefaults = new();
	static readonly FieldInfo[] VisualFields = typeof(VisualTuning).GetFields();
	// The panel's checkboxes live in PerfToggles; they ride along in the same undo entry.
	static readonly FieldInfo[] VisualToggleFields = Array.FindAll(
		typeof(PerfToggles).GetFields(BindingFlags.NonPublic | BindingFlags.Static),
		field => field.FieldType == typeof(bool));

	const string VisualsUndoSlot = "visuals";

	void VisualsImGui()
	{
		if (!visualsOpen)
			return;

		SetNextWindowSize(new Vector2(360, 650), ImGuiCond.FirstUseEver);
		DockNextToolWindow("Visuals");
		if (!Begin("Visuals", ref visualsOpen, ImGuiWindowFlags.NoCollapse))
		{
			NoteDockedWindow();
			End();
			return;
		}
		NoteDockedWindow();

		if (Button("Reset Defaults"))
		{
			using (undo.Capture("Reset Visuals", new VisualsUndoTarget(this)))
			{
				Visuals = new();
				PerfToggles.Lighting = true;
				PerfToggles.NormalBuffer = true;
				PerfToggles.Emissive = true;
				PerfToggles.LightingNoise = true;
				PerfToggles.Crt = true;
				PerfToggles.AmbientOcclusion = true;
				PerfToggles.GlobalIllumination = true;
				PerfToggles.SoftShadows = true;
			}
		}
		SameLine();
		if (Button("Copy Values as C#"))
			Input.SetClipboardString(VisualTuningAsCSharp());

		if (CollapsingHeader("CRT", ImGuiTreeNodeFlags.DefaultOpen))
		{
			PushID("Crt");
			VisualToggle("Enabled", ref PerfToggles.Crt,
				"Applies the final CRT presentation pass: bloom, scanlines, vignette and RGB separation. Disable it to inspect the unprocessed game image.");
			VisualRow("RGB Separation", ref Visuals.CrtRgbOffset, VisualDefaults.CrtRgbOffset, 0, 6,
				"Offsets the red, green and blue channels in output pixels. Larger values feel more analog and unstable, but blur fine pixel-art edges.");
			VisualRow("Scanlines", ref Visuals.CrtScanlineStrength, VisualDefaults.CrtScanlineStrength, 0, 0.4f,
				"Darkens alternating output rows to suggest a scanned display. High values improve texture at large scale but reduce overall brightness and legibility.");
			VisualRow("Vignette", ref Visuals.CrtVignetteAmount, VisualDefaults.CrtVignetteAmount, 0, 0.5f,
				"Darkens the image toward the display edges. Use it to hold attention near the center; too much hides edge hazards and room exits.");
			VisualRow("Bloom Response", ref Visuals.CrtBloomAmount, VisualDefaults.CrtBloomAmount, 0, 0.5f,
				"Controls how much light bright pixels spread into neighboring output pixels. This is display bloom, so it softens the final image without contributing to GI.");
			VisualRow("Bloom Threshold", ref Visuals.CrtBloomThreshold, VisualDefaults.CrtBloomThreshold, 0, 1,
				"Sets how bright a pixel must be before it blooms. Lower values bloom more of the scene; higher values reserve bloom for lights and fullbright accents.");
			if (Button("Preview Bloom", new Vector2(-1, 0)))
				game?.fx.Bloom();
			PopID();
		}

		if (CollapsingHeader("Ambient Occlusion", ImGuiTreeNodeFlags.DefaultOpen))
		{
			PushID("Ao");
			VisualToggle("Enabled", ref PerfToggles.AmbientOcclusion,
				"Adds soft contact shadow where foreground coverage overhangs background tiles. It gives layers depth without changing light sources or GI propagation.");
			VisualRow("Strength", ref Visuals.AmbientOcclusionStrength, VisualDefaults.AmbientOcclusionStrength, 0, 4,
				"Scales foreground-to-background contact shadow. High values strongly separate the layers, but can resemble a dark outline around every ledge.");
			VisualRow("Blur", ref Visuals.AmbientOcclusionBlur, VisualDefaults.AmbientOcclusionBlur, 0, 32,
				"Sets the softness and spread of the occlusion mask in art pixels. Small values hug tile edges; large values create broad depth beneath overhangs.");
			VisualRow("Offset (px)", ref Visuals.AmbientOcclusionOffset, VisualDefaults.AmbientOcclusionOffset, -16, 16,
				"Moves the occlusion shadow relative to its foreground caster, in art pixels. This can imply a preferred light direction or deliberately exaggerate layer separation.");
			VisualRow("Min Visibility", ref Visuals.AmbientOcclusionMinVisibility, VisualDefaults.AmbientOcclusionMinVisibility, 0, 1,
				"Sets the darkest light multiplier AO is allowed to produce. Raise it to preserve background detail; lower it for deeper contact pockets.");
			PopID();
		}

		if (CollapsingHeader("Lighting", ImGuiTreeNodeFlags.DefaultOpen))
		{
			PushID("Lighting");
			VisualToggle("Enabled", ref PerfToggles.Lighting,
				"Enables the complete lighting composite. Disable it to view raw color layers without ambient, direct light, GI, emissive compositing or lighting noise.");
			VisualToggle("Normal Maps", ref PerfToggles.NormalBuffer,
				"Lets authored and generated normals shape direct lighting and GI response. Disable it to diagnose whether unwanted texture comes from normals or from light intensity.");
			VisualToggle("Emissive", ref PerfToggles.Emissive,
				"Keeps authored emissive pixels visible regardless of ordinary lighting. Light emission into GI is authored separately, although disabling this also disables those emission passes.");
			VisualToggle("Noise", ref PerfToggles.LightingNoise,
				"Multiplies the completed lit scene by the room's scrolling lighting-noise texture. It currently affects direct light, GI, emissive pixels and sprites together.");
			VisualRow("Direct Presence", ref Visuals.DirectLightPresence, VisualDefaults.DirectLightPresence, 0, 2,
				"Scales visible lamp and sunlight on surfaces and light-responsive particles. Zero hides direct surface light while it still feeds GI. Fog uses its own Point and Sun Response controls.");
			VisualRow("Exposure (stops)", ref Visuals.LightingExposure, VisualDefaults.LightingExposure, -4, 4,
				"Scales displayed lighting before dithering: +1 doubles it, -1 halves it. Does not change the energy used by GI propagation.");
			VisualRow("Highlight Shoulder", ref Visuals.LightingShoulder, VisualDefaults.LightingShoulder, 0, 0.95f,
				"Brightness where highlights begin rolling smoothly toward the display limit. Lower values soften more highlights; higher values preserve more contrast. Scales RGB together to preserve light color.");
			VisualRow("Normal Floor", ref Visuals.NormalLightFloor, VisualDefaults.NormalLightFloor, 0, 1,
				"Minimum diffuse response allowed when a normal faces away from a light. Raise it for readable, softly wrapped forms; lower it for stronger directional modeling.");
			VisualRow("Normal Gain", ref Visuals.NormalLightGain, VisualDefaults.NormalLightGain, 0, 4,
				"Amplifies contrast produced by normal maps. High values make surface relief dramatic, but can overpower pixel values and create harsh sparkling during motion.");
			VisualRow("Dither Multiplier", ref Visuals.DitherStrength, VisualDefaults.DitherStrength, 0, 2,
				"Scales how strongly lighting is expressed through the ordered dither pattern. Zero gives smooth lighting; larger values make light and shadow more graphic and textured.");
			VisualRow("Band Spacing", ref Visuals.DitherBandSpacing, VisualDefaults.DitherBandSpacing, 0.05f, 2,
				"Distance between exposed lighting bands. Small steps preserve gradients; larger steps produce sparse, bold marks. Pattern strength blends between smooth and quantized lighting.");
			PopID();
		}

		if (CollapsingHeader("Shadows", ImGuiTreeNodeFlags.DefaultOpen))
		{
			PushID("Shadows");
			VisualToggle("Soft Shadows", ref PerfToggles.SoftShadows,
				"Uses area-source sampling to soften direct-light shadow boundaries. Disable it for cheaper, crisp geometric shadows or to inspect raw occluders.");
			VisualRow("Sun Softness", ref Visuals.SunShadowSoftness, VisualDefaults.SunShadowSoftness, 0, 8,
				"Widens directional-light penumbrae. Small values read as hard sunlight; large values feel diffuse or overcast and may soften small shadow details away.");
			VisualRow("Sprite Strength", ref Visuals.SpriteShadowStrength, VisualDefaults.SpriteShadowStrength, 0, 1,
				"Controls the opacity of shadows cast from sprite silhouettes. Raise it to ground actors and props; lower it when their shadows clutter the tile lighting.");
			VisualRow("Sprite Min Length", ref Visuals.SpriteShadowMinLength, VisualDefaults.SpriteShadowMinLength, 0, 64,
				"Shortest projected sprite shadow in art pixels. It preserves a grounding mark near a light, but excessive values detach objects from the source direction.");
			VisualRow("Sprite Max Length", ref Visuals.SpriteShadowMaxLength, VisualDefaults.SpriteShadowMaxLength, 0, 64,
				"Caps projected sprite-shadow length in art pixels. Increase it for long dramatic shadows; reduce it to prevent distant lights from drawing shadows across the room.");
			Visuals.SpriteShadowMaxLength = Math.Max(Visuals.SpriteShadowMinLength, Visuals.SpriteShadowMaxLength);
			if (TreeNode("Advanced"))
			{
				VisualRow("Bias", ref Visuals.ShadowBias, VisualDefaults.ShadowBias, 0, 4,
					"Offsets shadow tests away from their casting surface to prevent self-shadowing artifacts. Too much bias separates shadows from walls and leaks light through corners.");
				TreePop();
			}
			PopID();
		}

		if (CollapsingHeader("Global Illumination", ImGuiTreeNodeFlags.DefaultOpen))
		{
			PushID("GlobalIllumination");
			VisualToggle("Enabled", ref PerfToggles.GlobalIllumination,
				"Propagates bounced radiance through the room after direct lighting. Disable it for a direct-light-only comparison; Z provides the same playtime toggle.");

			SeparatorText("Composition");
			VisualRow("GI Underpaint", ref Visuals.IndirectUnderpaint, VisualDefaults.IndirectUnderpaint, 0, 1,
				"Moves GI beneath the dither pattern. Zero dithers direct and indirect light together; one keeps GI smooth so the darker pattern pixels retain bounced color. Does not change GI propagation.");
			VisualRow("Backdrop GI", ref Visuals.IndirectBackdropStrength, VisualDefaults.IndirectBackdropStrength, 0, 4,
				"Bounced light on untiled room backdrop, using its existing color, gradient and surface as pigment. Zero hides backdrop GI; empty space still carries light. Tiles and actors use World Presence.");
			VisualRow("World Presence", ref Visuals.IndirectLightStrength, VisualDefaults.IndirectLightStrength, 0, 2,
				"Final multiplier for indirect light on background tiles and actors; untiled backdrop uses Backdrop GI. Raise it to make bounce obvious; lower it when rooms lose their dark hierarchy or begin to look uniformly filled.");
			VisualRow("Foreground Presence", ref Visuals.IndirectForegroundStrength, VisualDefaults.IndirectForegroundStrength, 0, 2,
				"Final indirect-light multiplier for solid foreground tiles. This is independent of how much foreground participates in generating bounce, so it primarily changes visible foreground response.");
			VisualRow("Wrap Depth (px)", ref Visuals.IndirectForegroundWrapDepth, VisualDefaults.IndirectForegroundWrapDepth, 0, 32,
				"Distance that nearby GI reaches into solid foreground artwork. Thin platforms catch color across their face; thick masses keep dark centers. Does not transmit light through walls.");
			VisualRow("Wrap Strength", ref Visuals.IndirectForegroundWrapStrength, VisualDefaults.IndirectForegroundWrapStrength, 0, 3,
				"Strength of borrowed boundary light on foreground tiles. Zero disables wrap. Wrap Pattern controls its texture independently of GI Underpaint, and Foreground Presence scales the result.");
			VisualRow("Wrap Pattern", ref Visuals.IndirectWrapPatternStrength, VisualDefaults.IndirectWrapPatternStrength, 0, 1,
				"Zero paints smooth boundary color into foreground; one expresses it through material marks. Independent of room dither strength and GI Underpaint; shares Band Spacing.");
			VisualRow("Actor Response", ref Visuals.IndirectActorGain, VisualDefaults.IndirectActorGain, 0, 4,
				"Multiplies GI where the actor tile field covers background-layer actors. Raise it to wrap characters and props in room color; high values can wash out their authored palettes.");
			VisualRow("Hero Lift", ref Visuals.IndirectHeroFill, VisualDefaults.IndirectHeroFill, 0, 0.5f,
				"Adds extra indirect illumination over actor coverage as a readability treatment. It is intentionally nonphysical; use it sparingly to keep heroes legible in dark rooms.");
			if (SmallButton("Reset Composition"))
				ResetGiComposition();

			SeparatorText("Travel");
			GiTravelPad();
			if (TreeNode("Exact Travel Values"))
			{
				VisualRow("Reach (px)", ref Visuals.IndirectLightRange, VisualDefaults.IndirectLightRange, 16, 384,
					"Maximum distance, in art pixels, that GI is allowed to propagate. Greater reach costs more propagation steps and can connect light between otherwise distinct compartments.");
				VisualRow("Falloff", ref Visuals.IndirectLightFalloff, VisualDefaults.IndirectLightFalloff, 0, 0.15f,
					"Exponential loss per art pixel: half-light distance is approximately 1 divided by this value. Higher values confine bounce near sources; zero preserves energy all the way to Reach.");
				TreePop();
			}
			if (SmallButton("Reset Travel"))
				ResetGiTravel();

			SeparatorText("Color Memory");
			VisualRow("Bounce Energy", ref Visuals.IndirectBounceGain, VisualDefaults.IndirectBounceGain, 0, 1.5f,
				"Scales radiance as it enters the GI field, before distance falloff and final presence multipliers. High values create richer bounce but can make small emissive sources dominate whole rooms.");
			VisualRow("Saturation", ref Visuals.IndirectBounceSaturation, VisualDefaults.IndirectBounceSaturation, 0, 3,
				"Controls saturation retained by bounced radiance. Zero makes bounce luminance-only; one preserves source color; values above one exaggerate colorful light separation.");
			VisualRow("Highlight Color Retention", ref Visuals.IndirectHighlightColorRetention, VisualDefaults.IndirectHighlightColorRetention, 0, 1,
				"One preserves all GI color. Lower values ease bright GI toward neutral while retaining weak shadow color and luminance. Applies to visible GI and wrap, without changing propagation.");
			VisualRow("Dark Surface Lift", ref Visuals.IndirectBounceAlbedoFloor, VisualDefaults.IndirectBounceAlbedoFloor, 0, 1,
				"Minimum diffuse color used when a very dark surface reflects light. Raise it to let black materials return useful bounce; too much makes material darkness stop mattering.");
			VisualRow("Warmth", ref Visuals.IndirectBounceWarmth, VisualDefaults.IndirectBounceWarmth, 0, 1,
				"Applies an artistic warm bias to all bounced radiance. It helps rooms feel inhabited and cohesive, but can muddy deliberately cold or blue lighting schemes.");
			VisualRow("Surface Color", ref Visuals.IndirectColorBleed, VisualDefaults.IndirectColorBleed, 0, 2,
				"Controls how strongly reflecting surface color tints bounced light. Zero reflects neutral luminance; one uses authored albedo; values above one exaggerate color bleeding.");
			if (SmallButton("Reset Color Memory"))
				ResetGiColor();

			SeparatorText("Material Response");
			VisualRow("Stone", ref Visuals.IndirectStoneReflectivity, VisualDefaults.IndirectStoneReflectivity, 0, 2,
				"Bounce multiplier for tile-field surfaces classified as stone. Lower values keep caves heavy and absorptive; higher values let masonry carry light around corners.");
			VisualRow("Organic", ref Visuals.IndirectOrganicReflectivity, VisualDefaults.IndirectOrganicReflectivity, 0, 2,
				"Bounce multiplier for organic tile-field materials such as growth and vegetation. Raising it can make living areas feel luminous and responsive to nearby color.");
			VisualRow("Water", ref Visuals.IndirectWaterReflectivity, VisualDefaults.IndirectWaterReflectivity, 0, 2,
				"Bounce multiplier for surfaces classified as water. Keep it low for deep, absorptive water or raise it for a magical, light-carrying liquid response.");
			VisualRow("Other", ref Visuals.IndirectOtherReflectivity, VisualDefaults.IndirectOtherReflectivity, 0, 2,
				"Fallback bounce multiplier for surfaces without a stone, organic or water classification. It is useful for balancing uncategorized content without changing the named materials.");
			if (SmallButton("Reset Materials"))
				ResetGiMaterials();

			if (TreeNode("Shaping / Advanced"))
			{
				VisualRow("Response Curve", ref Visuals.IndirectResponseCurve, VisualDefaults.IndirectResponseCurve, 0.25f, 2,
					"Power curve applied to resolved GI. Values below one lift faint bounce and extend its perceptual reach; values above one suppress low energy and preserve darker pockets.");
				VisualRow("Foreground Reflectivity", ref Visuals.IndirectForegroundReflectivity, VisualDefaults.IndirectForegroundReflectivity, 0, 2,
					"Scales foreground albedo when solid foreground pixels generate indirect light. This controls their contribution to the field, unlike Foreground Presence, which controls how much GI they receive.");
				VisualRow("Directionality", ref Visuals.IndirectDirectionality, VisualDefaults.IndirectDirectionality, 0, 1,
					"Uses the local GI gradient and normal map to favor surfaces facing incoming radiance. Zero gives uniform wrap; one gives the strongest available directional modeling.");
				VisualRow("Normal Wrap", ref Visuals.IndirectNormalWrap, VisualDefaults.IndirectNormalWrap, 0, 2,
					"Widens the range of surface normals that receive directional GI. Low values are selective and sculptural; high values wrap light around forms and reduce normal-map contrast.");
				TreePop();
			}
			if (Button("Toggle Indirect Debug", new Vector2(-1, 0)))
				game?.ToggleDebugView(Game.DebugView.IndirectLight);
			if (IsItemHovered())
				VisualTooltip("Switches the game view between the final composite and the resolved indirect-light buffer. Use it to inspect propagation color, reach and leaks without direct lighting or sprite color.");
			PopID();
		}

		if (CollapsingHeader("Spore Fog"))
		{
			PushID("SporeFog");
			VisualRow("Scale", ref Visuals.SporeFogScale, VisualDefaults.SporeFogScale, 0.001f, 0.1f,
				"Spatial frequency of the procedural fog density. Low values make broad rolling clouds; high values produce smaller, busier pockets that can shimmer at pixel scale.");
			VisualRow("Drift", ref Visuals.SporeFogDrift, VisualDefaults.SporeFogDrift, -10, 10,
				"World-space fog motion in art pixels per second. X controls horizontal flow and Y vertical flow; opposing values can suggest falling dust, rising spores or lateral wind.");
			VisualRow("Contrast", ref Visuals.SporeFogContrast, VisualDefaults.SporeFogContrast, 0.1f, 5,
				"Expands or compresses differences in procedural fog density. Low values form an even veil; high values create distinct empty gaps and dense tendrils.");
			VisualRow("Point Response", ref Visuals.SporeFogPointStrength, VisualDefaults.SporeFogPointStrength, 0, 2,
				"How strongly point lights illuminate spore fog, independently of Direct Presence. Requires room fog strength or a Spore Fog Volume. Raise it for visible shadowed pools and shafts.");
			VisualRow("Sun Response", ref Visuals.SporeFogSunStrength, VisualDefaults.SporeFogSunStrength, 0, 2,
				"How strongly sunlight illuminates spore fog, independently of Direct Presence. Requires room fog strength or a Spore Fog Volume. Lower it when sunlit fog flattens local point-light composition.");
			PopID();
		}

		if (CollapsingHeader("Effects"))
		{
			PushID("Effects");
			VisualRow("World Distortion", ref Visuals.WorldDistortionPixels, VisualDefaults.WorldDistortionPixels, 0, 12,
				"Maximum displacement applied by world-distortion effects, measured in art pixels. Larger values make waves and impacts more forceful, but can tear silhouettes and obscure collision-readable edges.");
			PopID();
		}

		End();
	}

	void GiTravelPad()
	{
		const float minRange = 16;
		const float maxRange = 384;
		const float maxFalloff = 0.15f;
		var size = new Vector2(MathF.Max(180, GetContentRegionAvail().X), 112);
		var origin = GetCursorScreenPos();
		InvisibleButton("##TravelPad", size);
		if (IsItemHovered())
			VisualTooltip("Drag right for greater reach. Drag down for faster decay; drag up for lingering light. The curve below shows the resulting energy over that distance.");
		if (IsItemActive())
		{
			var position = Vector2.Clamp((GetMousePos() - origin) / size, Vector2.Zero, Vector2.One);
			SetVisual(ref Visuals.IndirectLightRange, float.Lerp(minRange, maxRange, position.X));
			SetVisual(ref Visuals.IndirectLightFalloff, position.Y * maxFalloff);
		}

		var draw = GetWindowDrawList();
		var bottomRight = origin + size;
		draw.AddRectFilled(origin, bottomRight, GetColorU32(ImGuiCol.FrameBg), 4);
		for (var i = 1; i < 4; i++)
		{
			var amount = i / 4f;
			draw.AddLine(origin + new Vector2(size.X * amount, 0), origin + new Vector2(size.X * amount, size.Y),
				GetColorU32(new Vector4(1, 1, 1, 0.08f)));
			draw.AddLine(origin + new Vector2(0, size.Y * amount), origin + new Vector2(size.X, size.Y * amount),
				GetColorU32(new Vector4(1, 1, 1, 0.08f)));
		}
		draw.AddText(origin + new Vector2(6, 5), GetColorU32(new Vector4(0.75f, 0.8f, 0.9f, 0.8f)), "LINGERING");
		draw.AddText(origin + new Vector2(6, size.Y - GetTextLineHeight() - 5),
			GetColorU32(new Vector4(0.75f, 0.8f, 0.9f, 0.8f)), "DECISIVE");
		var far = "FAR";
		draw.AddText(bottomRight - new Vector2(CalcTextSize(far).X + 6, GetTextLineHeight() + 5),
			GetColorU32(new Vector4(0.75f, 0.8f, 0.9f, 0.8f)), far);

		var normalized = new Vector2(
			Math.Clamp((Visuals.IndirectLightRange - minRange) / (maxRange - minRange), 0, 1),
			Math.Clamp(Visuals.IndirectLightFalloff / maxFalloff, 0, 1));
		var marker = origin + normalized * size;
		draw.AddCircleFilled(marker, 6, GetColorU32(new Vector4(1, 0.72f, 0.32f, 1)), 16);
		draw.AddCircle(marker, 8, GetColorU32(new Vector4(0.12f, 0.08f, 0.03f, 0.9f)), 16, 2);

		var halfDistance = Visuals.IndirectLightFalloff > 0.0001f ? 1 / Visuals.IndirectLightFalloff : float.PositiveInfinity;
		var edge = MathF.Pow(2, -Visuals.IndirectLightRange * Visuals.IndirectLightFalloff);
		var halfText = float.IsFinite(halfDistance) ? $"half after {halfDistance / Game.TileSize:0.#} tiles" : "no distance decay";
		TextDisabled($"{Visuals.IndirectLightRange / Game.TileSize:0.#} tiles  •  {halfText}  •  {edge:P0} at edge");
		GiFalloffPreview();
	}

	void GiFalloffPreview()
	{
		var size = new Vector2(MathF.Max(180, GetContentRegionAvail().X), 42);
		var origin = GetCursorScreenPos();
		Dummy(size);
		var draw = GetWindowDrawList();
		draw.AddRectFilled(origin, origin + size, GetColorU32(new Vector4(0.04f, 0.045f, 0.06f, 0.9f)), 3);
		var previous = origin + new Vector2(0, 3);
		for (var i = 1; i <= 48; i++)
		{
			var amount = i / 48f;
			var response = MathF.Pow(2, -Visuals.IndirectLightRange * amount * Visuals.IndirectLightFalloff);
			var point = origin + new Vector2(size.X * amount, (1 - response) * (size.Y - 6) + 3);
			draw.AddLine(previous, point, GetColorU32(new Vector4(1, 0.58f, 0.28f, 0.95f)), 2);
			previous = point;
		}
	}

	void ResetGiComposition()
	{
		SetVisual(ref Visuals.IndirectUnderpaint, VisualDefaults.IndirectUnderpaint);
		SetVisual(ref Visuals.IndirectBackdropStrength, VisualDefaults.IndirectBackdropStrength);
		SetVisual(ref Visuals.IndirectLightStrength, VisualDefaults.IndirectLightStrength);
		SetVisual(ref Visuals.IndirectForegroundWrapDepth, VisualDefaults.IndirectForegroundWrapDepth);
		SetVisual(ref Visuals.IndirectForegroundWrapStrength, VisualDefaults.IndirectForegroundWrapStrength);
		SetVisual(ref Visuals.IndirectWrapPatternStrength, VisualDefaults.IndirectWrapPatternStrength);
		SetVisual(ref Visuals.IndirectForegroundStrength, VisualDefaults.IndirectForegroundStrength);
		SetVisual(ref Visuals.IndirectActorGain, VisualDefaults.IndirectActorGain);
		SetVisual(ref Visuals.IndirectHeroFill, VisualDefaults.IndirectHeroFill);
	}

	void ResetGiTravel()
	{
		SetVisual(ref Visuals.IndirectLightRange, VisualDefaults.IndirectLightRange);
		SetVisual(ref Visuals.IndirectLightFalloff, VisualDefaults.IndirectLightFalloff);
	}

	void ResetGiColor()
	{
		SetVisual(ref Visuals.IndirectBounceGain, VisualDefaults.IndirectBounceGain);
		SetVisual(ref Visuals.IndirectBounceSaturation, VisualDefaults.IndirectBounceSaturation);
		SetVisual(ref Visuals.IndirectHighlightColorRetention, VisualDefaults.IndirectHighlightColorRetention);
		SetVisual(ref Visuals.IndirectBounceAlbedoFloor, VisualDefaults.IndirectBounceAlbedoFloor);
		SetVisual(ref Visuals.IndirectBounceWarmth, VisualDefaults.IndirectBounceWarmth);
		SetVisual(ref Visuals.IndirectColorBleed, VisualDefaults.IndirectColorBleed);
	}

	void ResetGiMaterials()
	{
		SetVisual(ref Visuals.IndirectStoneReflectivity, VisualDefaults.IndirectStoneReflectivity);
		SetVisual(ref Visuals.IndirectOrganicReflectivity, VisualDefaults.IndirectOrganicReflectivity);
		SetVisual(ref Visuals.IndirectWaterReflectivity, VisualDefaults.IndirectWaterReflectivity);
		SetVisual(ref Visuals.IndirectOtherReflectivity, VisualDefaults.IndirectOtherReflectivity);
	}

	// Every row edits a copy so the undo scope captures the pre-gesture state before the field moves.
	// The scope stays pending until EditorUI sees no active ImGui item, making a whole drag one entry.
	void VisualRow(string label, ref float value, float reset, float min, float max, string? tooltip = null)
	{
		BeginPropertyRow(label, hasTrailingButton: true, tooltip: tooltip);
		PushID(label);
		var edited = value;
		var changed = SliderFloat("##Value", ref edited, min, max);
		if (tooltip != null && IsItemHovered())
			VisualTooltip(tooltip);
		changed |= VisualResetButton(reset, ref edited);
		PopID();
		if (changed)
			SetVisual(ref value, edited);
	}

	void VisualRow(string label, ref int value, int reset, int min, int max, string? tooltip = null)
	{
		BeginPropertyRow(label, hasTrailingButton: true, tooltip: tooltip);
		PushID(label);
		var edited = value;
		var changed = SliderInt("##Value", ref edited, min, max);
		if (tooltip != null && IsItemHovered())
			VisualTooltip(tooltip);
		changed |= VisualResetButton(reset, ref edited);
		PopID();
		if (changed)
			SetVisual(ref value, edited);
	}

	void VisualRow(string label, ref Vector2 value, Vector2 reset, float min, float max, string? tooltip = null)
	{
		BeginPropertyRow(label, hasTrailingButton: true, tooltip: tooltip);
		PushID(label);
		var edited = value;
		var changed = SliderFloat2("##Value", ref edited, min, max);
		if (tooltip != null && IsItemHovered())
			VisualTooltip(tooltip);
		changed |= VisualResetButton(reset, ref edited);
		PopID();
		if (changed)
			SetVisual(ref value, edited);
	}

	void VisualRow(string label, ref Color value, Color reset, string? tooltip = null)
	{
		BeginPropertyRow(label, hasTrailingButton: true, tooltip: tooltip);
		PushID(label);
		var rgb = value.ToVector3();
		var edited = value;
		if (ColorEdit3("##Value", ref rgb, ImGuiColorEditFlags.NoInputs))
			edited = new Color(Vector3.Clamp(rgb, Vector3.Zero, Vector3.One));
		if (tooltip != null && IsItemHovered())
			VisualTooltip(tooltip);
		var changed = edited != value;
		changed |= VisualResetButton(reset, ref edited);
		PopID();
		if (changed)
			SetVisual(ref value, edited);
	}

	void VisualToggle(string label, ref bool value, string? tooltip = null)
	{
		BeginPropertyRow(label, hasTrailingButton: true, tooltip: tooltip);
		PushID(label);
		var edited = value;
		var changed = Checkbox("##Value", ref edited);
		if (tooltip != null && IsItemHovered())
			VisualTooltip(tooltip);
		changed |= VisualResetButton(true, ref edited);
		PopID();
		if (changed)
			SetVisual(ref value, edited);
	}

	static void VisualTooltip(string tooltip)
	{
		BeginTooltip();
		PushTextWrapPos(GetFontSize() * 32);
		TextUnformatted(tooltip);
		PopTextWrapPos();
		EndTooltip();
	}

	static bool VisualResetButton<T>(T reset, ref T edited)
	{
		SameLine();
		var pressed = SmallButton(EditorIcons.Reset);
		if (IsItemHovered())
			SetTooltip($"Reset to {reset}");
		if (pressed)
			edited = reset;
		return pressed;
	}

	void SetVisual<T>(ref T value, T edited)
	{
		if (!undo.HasPending(VisualsUndoSlot))
			undo.OpenPending(VisualsUndoSlot, "Edit Visuals", new VisualsUndoTarget(this));
		value = edited;
	}

	string VisualTuningAsCSharp()
	{
		var text = new StringBuilder("Visuals = new()\n{\n");
		foreach (var field in VisualFields)
			text.Append('\t').Append(field.Name).Append(" = ").Append(VisualLiteral(field.GetValue(Visuals)!)).Append(",\n");
		return text.Append("};").ToString();
	}

	static string VisualLiteral(object value) => value switch
	{
		float number => number.ToString("0.####", CultureInfo.InvariantCulture) + "f",
		int number => number.ToString(CultureInfo.InvariantCulture),
		Vector2 vector => $"new({VisualLiteral(vector.X)}, {VisualLiteral(vector.Y)})",
		Color color => $"new({VisualLiteral(color.R / 255f)}, {VisualLiteral(color.G / 255f)}, {VisualLiteral(color.B / 255f)}, {VisualLiteral(color.A / 255f)})",
		_ => throw new NotSupportedException($"No C# literal for {value.GetType().Name}"),
	};

	sealed class VisualsUndoTarget(Manager manager) : IJsonUndoTarget
	{
		public string UndoId => "visuals";

		public string CaptureJson()
		{
			var json = new JsonObject();
			foreach (var field in VisualFields)
				json[field.Name] = VisualNode(field.GetValue(manager.Visuals)!);
			foreach (var toggle in VisualToggleFields)
				json[toggle.Name] = JsonValue.Create((bool)toggle.GetValue(null)!);
			return json.ToJsonString();
		}

		public void ApplyJson(string json)
		{
			var node = JsonNode.Parse(json)!;
			foreach (var field in VisualFields)
				field.SetValue(manager.Visuals, VisualValue(field.FieldType, node[field.Name]!));
			foreach (var toggle in VisualToggleFields)
				toggle.SetValue(null, (bool)node[toggle.Name]!);
		}

		static JsonNode VisualNode(object value) => value switch
		{
			float number => JsonValue.Create(number),
			int number => JsonValue.Create(number),
			Vector2 vector => new JsonArray(vector.X, vector.Y),
			Color color => JsonValue.Create(color.RGBA),
			_ => throw new NotSupportedException($"No undo capture for {value.GetType().Name}"),
		};

		static object VisualValue(Type type, JsonNode node) =>
			type == typeof(int) ? (int)node :
			type == typeof(Vector2) ? new Vector2((float)node[0]!, (float)node[1]!) :
			type == typeof(Color) ? new Color((uint)node) :
			(float)node;
	}
}
#endif
