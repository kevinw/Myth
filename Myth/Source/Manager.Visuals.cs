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

// Pixel-art upscalers the final pass can apply before (or without) the CRT; the value is the shader's mode.
enum PixelScaler { None, Scale2x, SuperEagle, Mmpx, Xbr }

sealed class VisualTuning
{
	// Not ambient glow: the whole term is scaled by fx.blooming, which only leaves zero for the
	// ~0.1s after fx.Bloom() is called. This is how hot the one-shot screen flash blows out, and
	// it contributes nothing to an ordinary frame.
	public float CrtFlashGlowAmount = 0.09f;
	public float CrtFlashGlowThreshold = 0.35f;
	public float CrtVignetteAmount = 0.06f;
	// In output pixels: the fringe is a property of the display, not of the art.
	public float CrtRgbOffset = 1.5f;
	public float CrtScanlineStrength = 0.1f;
	public int PixelScaler = int.Parse(Environment.GetEnvironmentVariable("TMP_SCALER") ?? "0"); // a PixelScaler
	// How different two lit colors can be (luma-weighted) and still count as the same pixel color.
	public float PixelScalerTolerance = 0.06f;
	public float AmbientOcclusionStrength = 1.0f;
	public float AmbientOcclusionBlur = 8.0f;
	public Vector2 AmbientOcclusionOffset = new(0, -3);
	public float AmbientOcclusionMinVisibility = 0.35f;
	public float SunShadowSoftness = 2.3f;
	public float SpriteShadowStrength = 0.5f;
	public float SpriteShadowMinLength = 4.0f;
	public float SpriteShadowMaxLength = 28.0f;
	public float ShadowBias = 0.5f;
	// Light is HDR (float16 masks) but the frame is 8-bit, so the composite rolls the product
	// off instead of clipping it per-channel -- a clip collapses channel ratios and turns a lit
	// pink white. Exposure buys the headroom that makes "brighter" readable at all: land normally
	// lit surfaces under the knee and lights have somewhere to push into.
	public float Exposure = 1.0f;
	public float TonemapKnee = 0.7f;
	// 0 keeps hue and saturation through the rolloff, 1 is the old per-channel clip.
	public float TonemapDesaturate = 0.0f;
	// What the rolloff discarded, scattered back as glow. The knee is the threshold, so this is
	// only how much of the overflow returns, not where glow starts.
	public float BloomStrength = 0.6f;
	public float BloomRadius = 6.0f;
	public float NormalLightFloor = 0.12f;
	public float NormalLightGain = 2.15f;
	// Light picked out along a sprite's silhouette, where the cutout normals lean outward. Its
	// own gain so a readable character rim doesn't drag every tile bevel up with it.
	public float NormalRimGain = 3.5f;
	public float NormalRimSharpness = 3.5f;
	// How far actors that opt in (UsesAuthoredColor) hold their own palette against the room's
	// light colour. 0 is ordinary multiplicative lighting; 1 responds to light amount only.
	public float AuthoredColorResponse = 0.7f;
	// Flat floor under dark-albedo pixels, in the light's colour. Its sibling below prints the
	// dither pattern there instead of flooding it; keeping them apart is what lets shadows stay
	// deep while the dither still reads.
	public float ShadowLift = 0.05f;
	public float DitherContrast = 0.06f;
	// The light range the dither pattern spans. Thresholds sit in ~0.06..0.94 and the pattern is
	// loudest where about half the cells clear, so narrowing this moves that peak -- but it
	// overshoots easily: measured, 0..0.65 flipped so many cells on that the pattern went quieter
	// than the identity range while still costing brightness. Identity by default; narrow it per
	// room only where a room's light genuinely sits too low to reach the spread.
	public float DitherLow = 0.0f;
	public float DitherHigh = 1.0f;
	public float DitherStrength = 1.0f;
	public int NormalDitherLevels = 4;
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

	internal void ToggleVisuals()
	{
		visualsOpen = !visualsOpen;
	}

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
				PerfToggles.SoftShadows = true;
				PerfToggles.Bloom = true;
			}
		}
		SameLine();
		if (Button("Copy Values as C#"))
			Input.SetClipboardString(VisualTuningAsCSharp());

		if (CollapsingHeader("CRT", ImGuiTreeNodeFlags.DefaultOpen))
		{
			PushID("Crt");
			VisualToggle("Enabled", ref PerfToggles.Crt);
			VisualRow("RGB Separation", ref Visuals.CrtRgbOffset, VisualDefaults.CrtRgbOffset, 0, 6);
			VisualRow("Scanlines", ref Visuals.CrtScanlineStrength, VisualDefaults.CrtScanlineStrength, 0, 0.4f);
			VisualRow("Vignette", ref Visuals.CrtVignetteAmount, VisualDefaults.CrtVignetteAmount, 0, 0.5f);
			VisualRow("Flash Glow", ref Visuals.CrtFlashGlowAmount, VisualDefaults.CrtFlashGlowAmount, 0, 0.5f);
			VisualRow("Flash Threshold", ref Visuals.CrtFlashGlowThreshold, VisualDefaults.CrtFlashGlowThreshold, 0, 1);
			if (Button("Preview Flash", new Vector2(-1, 0)))
				game?.fx.Bloom();
			PopID();
		}

		if (CollapsingHeader("Pixel Scaler", ImGuiTreeNodeFlags.DefaultOpen))
		{
			PushID("PixelScaler");
			BeginPropertyRow("Scaler", hasTrailingButton: true);
			var scaler = Visuals.PixelScaler;
			var changed = Combo("##Value", ref scaler, Enum.GetNames<PixelScaler>(), Enum.GetValues<PixelScaler>().Length);
			changed |= VisualResetButton(VisualDefaults.PixelScaler, ref scaler);
			if (changed)
				SetVisual(ref Visuals.PixelScaler, scaler);
			VisualRow("Color Tolerance", ref Visuals.PixelScalerTolerance, VisualDefaults.PixelScalerTolerance, 0, 0.3f);
			PopID();
		}

		if (CollapsingHeader("Ambient Occlusion", ImGuiTreeNodeFlags.DefaultOpen))
		{
			PushID("Ao");
			VisualToggle("Enabled", ref PerfToggles.AmbientOcclusion);
			VisualRow("Strength", ref Visuals.AmbientOcclusionStrength, VisualDefaults.AmbientOcclusionStrength, 0, 4);
			VisualRow("Blur", ref Visuals.AmbientOcclusionBlur, VisualDefaults.AmbientOcclusionBlur, 0, 32);
			VisualRow("Offset (px)", ref Visuals.AmbientOcclusionOffset, VisualDefaults.AmbientOcclusionOffset, -16, 16);
			VisualRow("Min Visibility", ref Visuals.AmbientOcclusionMinVisibility, VisualDefaults.AmbientOcclusionMinVisibility, 0, 1);
			PopID();
		}

		if (CollapsingHeader("Lighting", ImGuiTreeNodeFlags.DefaultOpen))
		{
			PushID("Lighting");
			VisualToggle("Enabled", ref PerfToggles.Lighting);
			VisualToggle("Normal Maps", ref PerfToggles.NormalBuffer);
			VisualToggle("Emissive", ref PerfToggles.Emissive);
			VisualToggle("Noise", ref PerfToggles.LightingNoise);
			VisualRow("Normal Floor", ref Visuals.NormalLightFloor, VisualDefaults.NormalLightFloor, 0, 1);
			VisualRow("Normal Gain", ref Visuals.NormalLightGain, VisualDefaults.NormalLightGain, 0, 4);
			VisualRow("Silhouette Rim", ref Visuals.NormalRimGain, VisualDefaults.NormalRimGain, 0, 6);
			VisualRow("Rim Sharpness", ref Visuals.NormalRimSharpness, VisualDefaults.NormalRimSharpness, 0.25f, 8);
			VisualRow("Authored Color", ref Visuals.AuthoredColorResponse, VisualDefaults.AuthoredColorResponse, 0, 1);
			// Not the silhouette rim above: these act on *dark* pixels, where multiplying cannot
			// separate anything and contrast has to be added.
			VisualRow("Shadow Lift", ref Visuals.ShadowLift, VisualDefaults.ShadowLift, 0, 0.5f);
			VisualRow("Dither Contrast", ref Visuals.DitherContrast, VisualDefaults.DitherContrast, 0, 0.5f);
			VisualRow("Dither Low", ref Visuals.DitherLow, VisualDefaults.DitherLow, 0, 1);
			VisualRow("Dither High", ref Visuals.DitherHigh, VisualDefaults.DitherHigh, 0, 1);
			Visuals.DitherHigh = Math.Max(Visuals.DitherLow + 0.01f, Visuals.DitherHigh);
			VisualRow("Dither Multiplier", ref Visuals.DitherStrength, VisualDefaults.DitherStrength, 0, 2);
			VisualRow("Quantization Levels", ref Visuals.NormalDitherLevels, VisualDefaults.NormalDitherLevels, 2, 16);
			PopID();
		}

		if (CollapsingHeader("Tonemap", ImGuiTreeNodeFlags.DefaultOpen))
		{
			PushID("Tonemap");
			VisualRow("Exposure", ref Visuals.Exposure, VisualDefaults.Exposure, 0, 4);
			VisualRow("Knee", ref Visuals.TonemapKnee, VisualDefaults.TonemapKnee, 0, 1);
			VisualRow("Desaturate", ref Visuals.TonemapDesaturate, VisualDefaults.TonemapDesaturate, 0, 1);
			VisualToggle("Bloom", ref PerfToggles.Bloom);
			VisualRow("Bloom Strength", ref Visuals.BloomStrength, VisualDefaults.BloomStrength, 0, 3);
			VisualRow("Bloom Radius", ref Visuals.BloomRadius, VisualDefaults.BloomRadius, 0, 32);
			PopID();
		}

		if (CollapsingHeader("Shadows", ImGuiTreeNodeFlags.DefaultOpen))
		{
			PushID("Shadows");
			VisualToggle("Soft Shadows", ref PerfToggles.SoftShadows);
			VisualRow("Sun Softness", ref Visuals.SunShadowSoftness, VisualDefaults.SunShadowSoftness, 0, 8);
			VisualRow("Sprite Strength", ref Visuals.SpriteShadowStrength, VisualDefaults.SpriteShadowStrength, 0, 1);
			VisualRow("Sprite Min Length", ref Visuals.SpriteShadowMinLength, VisualDefaults.SpriteShadowMinLength, 0, 64);
			VisualRow("Sprite Max Length", ref Visuals.SpriteShadowMaxLength, VisualDefaults.SpriteShadowMaxLength, 0, 64);
			Visuals.SpriteShadowMaxLength = Math.Max(Visuals.SpriteShadowMinLength, Visuals.SpriteShadowMaxLength);
			if (TreeNode("Advanced"))
			{
				VisualRow("Bias", ref Visuals.ShadowBias, VisualDefaults.ShadowBias, 0, 4);
				TreePop();
			}
			PopID();
		}

		if (CollapsingHeader("Spore Fog"))
		{
			PushID("SporeFog");
			VisualRow("Scale", ref Visuals.SporeFogScale, VisualDefaults.SporeFogScale, 0.001f, 0.1f);
			VisualRow("Drift", ref Visuals.SporeFogDrift, VisualDefaults.SporeFogDrift, -10, 10);
			VisualRow("Contrast", ref Visuals.SporeFogContrast, VisualDefaults.SporeFogContrast, 0.1f, 5);
			VisualRow("Point Response", ref Visuals.SporeFogPointStrength, VisualDefaults.SporeFogPointStrength, 0, 2);
			VisualRow("Sun Response", ref Visuals.SporeFogSunStrength, VisualDefaults.SporeFogSunStrength, 0, 2);
			PopID();
		}

		if (CollapsingHeader("Effects"))
		{
			PushID("Effects");
			VisualRow("World Distortion", ref Visuals.WorldDistortionPixels, VisualDefaults.WorldDistortionPixels, 0, 12);
			PopID();
		}

		End();
	}

	// Every row edits a copy so the undo scope captures the pre-gesture state before the field moves.
	// The scope stays pending until EditorUI sees no active ImGui item, making a whole drag one entry.
	void VisualRow(string label, ref float value, float reset, float min, float max)
	{
		BeginPropertyRow(label, hasTrailingButton: true);
		PushID(label);
		var edited = value;
		var changed = SliderFloat("##Value", ref edited, min, max);
		changed |= VisualResetButton(reset, ref edited);
		PopID();
		if (changed)
			SetVisual(ref value, edited);
	}

	void VisualRow(string label, ref int value, int reset, int min, int max)
	{
		BeginPropertyRow(label, hasTrailingButton: true);
		PushID(label);
		var edited = value;
		var changed = SliderInt("##Value", ref edited, min, max);
		changed |= VisualResetButton(reset, ref edited);
		PopID();
		if (changed)
			SetVisual(ref value, edited);
	}

	void VisualRow(string label, ref Vector2 value, Vector2 reset, float min, float max)
	{
		BeginPropertyRow(label, hasTrailingButton: true);
		PushID(label);
		var edited = value;
		var changed = SliderFloat2("##Value", ref edited, min, max);
		changed |= VisualResetButton(reset, ref edited);
		PopID();
		if (changed)
			SetVisual(ref value, edited);
	}

	void VisualRow(string label, ref Color value, Color reset)
	{
		BeginPropertyRow(label, hasTrailingButton: true);
		PushID(label);
		var rgb = value.ToVector3();
		var edited = value;
		if (ColorEdit3("##Value", ref rgb, ImGuiColorEditFlags.NoInputs))
			edited = new Color(Vector3.Clamp(rgb, Vector3.Zero, Vector3.One));
		var changed = edited != value;
		changed |= VisualResetButton(reset, ref edited);
		PopID();
		if (changed)
			SetVisual(ref value, edited);
	}

	void VisualToggle(string label, ref bool value)
	{
		BeginPropertyRow(label, hasTrailingButton: true);
		PushID(label);
		var edited = value;
		var changed = Checkbox("##Value", ref edited);
		changed |= VisualResetButton(true, ref edited);
		PopID();
		if (changed)
			SetVisual(ref value, edited);
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
