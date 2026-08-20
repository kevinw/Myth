#if EDITOR
using LinkDotNet.StringBuilder;
using System.Numerics;
using System.Reflection;
using System.Text.Json.Nodes;
using Foster.Editor;
using Foster.Framework;
using ImGuiNET;
using static ImGuiNET.ImGui;

namespace Myth;

// Shared ImGui layout helpers for inspector property rows: label/control sizing, middle-ellipsized
// labels with hover tooltips, and the per-property reset button.
public partial class Manager
{

	readonly record struct PropertyRowLayout(float ControlWidth);

	static PropertyRowLayout BeginPropertyRow(string label, bool hasTrailingButton = false, int additionalTrailingButtons = 0)
	{
		const float MinimumLabelWidth = 94;
		const float MinimumControlWidth = 72;
		const float MaximumLabelWidth = 140;

		var style = GetStyle();
		var available = GetContentRegionAvail().X;
		var trailingButtons = (hasTrailingButton ? 1 : 0) + additionalTrailingButtons;
		var trailingWidth = trailingButtons * (CalcTextSize(EditorIcons.Reset).X + style.FramePadding.X * 2 + style.ItemSpacing.X);
		var stacked = available < MinimumLabelWidth + MinimumControlWidth + style.ItemSpacing.X + trailingWidth;
		var rowStartX = GetCursorPosX();

		AlignTextToFramePadding();
		if (stacked)
		{
			DrawPropertyLabel(label, available, rowStartX, rightAligned: false);
			var stackedControlWidth = MathF.Max(1, available - trailingWidth);
			SetNextItemWidth(stackedControlWidth);
			return new(stackedControlWidth);
		}

		var labelWidth = Math.Clamp(available * 0.25f, MinimumLabelWidth, MaximumLabelWidth);
		var controlWidth = available - labelWidth - trailingWidth - style.ItemSpacing.X;
		DrawPropertyLabel(label, labelWidth, rowStartX, rightAligned: true);
		SameLine();
		SetCursorPosX(rowStartX + labelWidth + style.ItemSpacing.X);
		SetNextItemWidth(controlWidth);

		return new(controlWidth);
	}

	static void DrawPropertyLabel(string label, float width, float startX, bool rightAligned)
	{
		var fullSize = CalcTextSize(label);
		var clipped = fullSize.X > width;
		var text = new ValueStringBuilder(stackalloc char[label.Length + 3]);
		if (clipped)
			BuildEllipsizedText(label, width, ref text);
		else
			text.Append(label);

		var textSize = CalcTextSize(text.AsSpan());
		SetCursorPosX(startX + (rightAligned ? MathF.Max(0, width - textSize.X) : 0));
		TextUnformatted(text.AsSpan());
		if (clipped && IsItemHovered())
			SetTooltip(label);
		text.Dispose();
	}

	static void BuildEllipsizedText(string label, float width, ref ValueStringBuilder text)
	{
		const string ellipsis = "...";
		var prefix = (label.Length + 1) / 2;
		var suffix = label.Length / 2;
		while (prefix >= 3 && suffix >= 3)
		{
			SetMiddleEllipsis(label, prefix, suffix, ref text);
			if (CalcTextSize(text.AsSpan()).X <= width)
			{
				var boundaryPrefix = PascalBoundaryAtOrBefore(label, prefix);
				var boundarySuffix = PascalBoundaryAtOrAfter(label, label.Length - suffix);
				if (boundaryPrefix >= 3 && label.Length - boundarySuffix >= 3 && boundaryPrefix < boundarySuffix)
					SetMiddleEllipsis(label, boundaryPrefix, label.Length - boundarySuffix, ref text);
				return;
			}
			if (prefix >= suffix)
				prefix--;
			else
				suffix--;
		}

		prefix = label.Length;
		do
		{
			prefix--;
			text.Clear();
			text.Append(label.AsSpan(0, prefix));
			text.Append(ellipsis);
		}
		while (prefix > 0 && CalcTextSize(text.AsSpan()).X > width);
	}

	static void SetMiddleEllipsis(string label, int prefix, int suffix, ref ValueStringBuilder text)
	{
		text.Clear();
		text.Append(label.AsSpan(0, prefix));
		text.Append("...");
		text.Append(label.AsSpan(label.Length - suffix));
	}

	static int PascalBoundaryAtOrBefore(string text, int index)
	{
		for (var i = Math.Min(index, text.Length - 1); i > 0; i--)
			if (char.IsUpper(text[i]))
				return i;
		return index;
	}

	static int PascalBoundaryAtOrAfter(string text, int index)
	{
		for (var i = Math.Max(1, index); i < text.Length; i++)
			if (char.IsUpper(text[i]))
				return i;
		return index;
	}

	static bool DrawPropertyResetButton(ActorSpecs.EditableProperty property)
	{
		var pressed = SmallButton(EditorIcons.Reset);
		if (IsItemHovered())
			SetTooltip($"Reset to {FormatPropertyValue(property.ResetValue)}");
		return pressed;
	}

	static string FormatPropertyValue(JsonNode? value)
	{
		if (value == null)
			return "None";
		if (value is JsonValue scalar && scalar.TryGetValue<string>(out var text))
			return string.IsNullOrEmpty(text) ? "(empty)" : text;
		return value.ToJsonString();
	}
}
#endif
