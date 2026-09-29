#if EDITOR
using System.Globalization;
using System.Numerics;
using Foster.Framework;
using ImGuiNET;
using static ImGuiNET.ImGui;

namespace Foster.Editor;

// Compact knob/XY-pad cells that flow into a grid between BeginFlow/EndFlow. Drag to edit (Shift for fine),
// double-click to reset, right-click to type an exact value. The arc tints when the value leaves its default.
public static class Knobs
{
	public static bool AlwaysShowValues;

	static float CellWidth => 60;
	static float KnobSize => 40;
	static float LabelScale => 0.78f;
	static float DragPixelsPerRange => 200;
	static float FineDragScale => 0.1f;
	static float ArcStart => MathF.PI * 0.75f;
	static float ArcSweep => MathF.PI * 1.5f;

	static bool flowing, flowFirst;

	public static void BeginFlow() => (flowing, flowFirst) = (true, true);
	public static void EndFlow() => flowing = false;

	public static bool Knob(string label, ref int value, int min, int max, int reset)
	{
		float edited = value;
		if (!Knob(label, ref edited, min, max, reset, integer: true))
			return false;
		value = (int)MathF.Round(edited);
		return true;
	}

	public static bool Knob(string label, ref float value, float min, float max, float reset, bool integer = false)
	{
		PushID(label);
		var cell = BeginCell(new Vector2(KnobSize));
		var changed = Drag(cell.Id, ref value, min, max, (GetIO().MouseDelta.X - GetIO().MouseDelta.Y) / DragPixelsPerRange, integer);
		changed |= ResetAndExact(cell, label, ref value, min, max, reset, integer);

		var drawList = GetWindowDrawList();
		var center = cell.Control.Center;
		var radius = KnobSize / 2 - 3;
		var thickness = 4f;
		var modified = !Same(value, reset, min, max);
		var from = min < 0 && max > 0 ? 0 : min;
		drawList.PathArcTo(center, radius, ArcStart, ArcStart + ArcSweep, 32);
		drawList.PathStroke(GetColorU32(cell.Hovered || cell.Active ? ImGuiCol.FrameBgHovered : ImGuiCol.FrameBg), ImDrawFlags.None, thickness);
		drawList.PathArcTo(center, radius, Angle(from, min, max), Angle(value, min, max), 32);
		drawList.PathStroke(FillColor(modified), ImDrawFlags.None, thickness);
		var tick = Direction(Angle(reset, min, max));
		drawList.AddLine(center + tick * (radius - thickness), center + tick * (radius + thickness * 0.75f), GetColorU32(ImGuiCol.TextDisabled), 1.5f);
		drawList.AddCircleFilled(center + Direction(Angle(value, min, max)) * radius, thickness * 0.6f, GetColorU32(ImGuiCol.Text));
		if (AlwaysShowValues || cell.Hovered || cell.Active)
			DrawValue(drawList, center, value, max - min, integer);

		EndCell(drawList, cell, label);
		PopID();
		return changed;
	}

	public static bool XYPad(string label, ref Vector2 value, float min, float max, Vector2 reset)
	{
		PushID(label);
		var cell = BeginCell(new Vector2(KnobSize));
		var delta = GetIO().MouseDelta / KnobSize;
		var changed = Drag(cell.Id, ref value.X, min, max, delta.X, integer: false);
		changed |= Drag(cell.Id + 1, ref value.Y, min, max, delta.Y, integer: false);
		changed |= ResetAndExact(cell, label, ref value, min, max, reset);

		var drawList = GetWindowDrawList();
		var rect = cell.Control;
		Vector2 ToPad(Vector2 point) => rect.TopLeft + (Vector2.Clamp(point, new(min), new(max)) - new Vector2(min)) / (max - min) * rect.Size;
		var modified = !Same(value.X, reset.X, min, max) || !Same(value.Y, reset.Y, min, max);
		var faint = GetColorU32(ImGuiCol.TextDisabled);
		drawList.AddRectFilled(rect.TopLeft, rect.BottomRight, GetColorU32(cell.Hovered || cell.Active ? ImGuiCol.FrameBgHovered : ImGuiCol.FrameBg), GetStyle().FrameRounding);
		var zero = ToPad(Vector2.Zero);
		drawList.AddLine(new(rect.Left, zero.Y), new(rect.Right, zero.Y), GetColorU32(ImGuiCol.Border));
		drawList.AddLine(new(zero.X, rect.Top), new(zero.X, rect.Bottom), GetColorU32(ImGuiCol.Border));
		var home = ToPad(reset);
		drawList.AddLine(home - new Vector2(3, 0), home + new Vector2(4, 0), faint, 1.5f);
		drawList.AddLine(home - new Vector2(0, 3), home + new Vector2(0, 4), faint, 1.5f);
		var dot = ToPad(value);
		if (modified)
			drawList.AddLine(home, dot, FillColor(true), 1.5f);
		drawList.AddCircleFilled(dot, 3.5f, modified ? FillColor(true) : GetColorU32(ImGuiCol.Text));
		if (AlwaysShowValues || cell.Hovered || cell.Active)
		{
			Span<char> text = stackalloc char[32];
			var written = Format(text, value.X, max - min, false);
			text[written++] = ',';
			written += Format(text[written..], value.Y, max - min, false);
			DrawSmallText(drawList, rect.Center + new Vector2(0, KnobSize * 0.3f), text[..written]);
		}

		EndCell(drawList, cell, label);
		PopID();
		return changed;
	}

	readonly record struct Cell(Vector2 Min, Rect Control, uint Id, bool Hovered, bool Active);

	static Cell BeginCell(Vector2 size)
	{
		if (flowing && !flowFirst && GetItemRectMax().X + GetStyle().ItemSpacing.X + CellWidth <= GetCursorScreenPos().X + GetContentRegionAvail().X)
			SameLine();
		flowFirst = false;
		var cellMin = GetCursorScreenPos();
		SetCursorScreenPos(cellMin + new Vector2((CellWidth - size.X) / 2, 0));
		InvisibleButton("##Control", size);
		if (IsItemHovered(ImGuiHoveredFlags.DelayNormal) && !IsItemActive())
			SetTooltip("Double-click: reset · Right-click: exact · Shift: fine");
		return new(cellMin, Rect.Between(GetItemRectMin(), GetItemRectMax()), GetItemID(), IsItemHovered(), IsItemActive());
	}

	static void EndCell(ImDrawListPtr drawList, Cell cell, string label)
	{
		var labelTop = cell.Control.Bottom + 2;
		DrawLabel(drawList, label, cell.Min.X, labelTop);
		SetCursorScreenPos(cell.Min);
		Dummy(new Vector2(CellWidth, labelTop - cell.Min.Y + LabelLineHeight * 2));
	}

	// Drag accumulates unclamped-by-rounding in ImGui storage so slow drags still move ints and fine drags stay smooth.
	static bool Drag(uint id, ref float value, float min, float max, float rangeDelta, bool integer)
	{
		var storage = GetStateStorage();
		if (IsItemActivated())
			storage.SetFloat(id, value);
		if (!IsItemActive() || rangeDelta == 0)
			return false;
		var raw = Math.Clamp(storage.GetFloat(id) + rangeDelta * (max - min) * (GetIO().KeyShift ? FineDragScale : 1), min, max);
		storage.SetFloat(id, raw);
		var next = integer ? MathF.Round(raw) : raw;
		if (next == value)
			return false;
		value = next;
		return true;
	}

	static bool ResetAndExact(Cell cell, string label, ref float value, float min, float max, float reset, bool integer)
	{
		var changed = false;
		if (cell.Hovered && IsMouseDoubleClicked(ImGuiMouseButton.Left))
			(value, changed) = (reset, true);
		if (BeginPopupContextItem("##Exact"))
		{
			TextDisabled(label);
			if (IsWindowAppearing())
				SetKeyboardFocusHere();
			SetNextItemWidth(140);
			if (integer)
			{
				var number = (int)value;
				if (InputInt("##Value", ref number, 0))
					(value, changed) = (Math.Clamp(number, min, max), true);
			}
			else if (InputFloat("##Value", ref value, 0, 0, "%.4f"))
				changed = true;
			EndPopup();
		}
		return changed;
	}

	static bool ResetAndExact(Cell cell, string label, ref Vector2 value, float min, float max, Vector2 reset)
	{
		var changed = false;
		if (cell.Hovered && IsMouseDoubleClicked(ImGuiMouseButton.Left))
			(value, changed) = (reset, true);
		if (BeginPopupContextItem("##Exact"))
		{
			TextDisabled(label);
			if (IsWindowAppearing())
				SetKeyboardFocusHere();
			SetNextItemWidth(160);
			changed |= InputFloat2("##Value", ref value, "%.3f");
			EndPopup();
		}
		return changed;
	}

	static bool Same(float value, float reset, float min, float max) => MathF.Abs(value - reset) <= (max - min) * 1e-4f;

	static float Angle(float value, float min, float max) => ArcStart + Math.Clamp((value - min) / (max - min), 0, 1) * ArcSweep;

	static Vector2 Direction(float angle) => new(MathF.Cos(angle), MathF.Sin(angle));

	static uint FillColor(bool modified) => GetColorU32(modified ? ImGuiCol.PlotHistogram : ImGuiCol.SliderGrab);

	static int Format(Span<char> text, float value, float range, bool integer)
	{
		var format = integer ? "0" : range < 1 ? "0.000" : range <= 10 ? "0.00" : range <= 100 ? "0.0" : "0";
		value.TryFormat(text, out var written, format, CultureInfo.InvariantCulture);
		return written;
	}

	static void DrawValue(ImDrawListPtr drawList, Vector2 center, float value, float range, bool integer)
	{
		Span<char> text = stackalloc char[16];
		DrawSmallText(drawList, center, text[..Format(text, value, range, integer)]);
	}

	static void DrawSmallText(ImDrawListPtr drawList, Vector2 center, ReadOnlySpan<char> text)
	{
		const float Scale = 0.85f;
		var size = CalcTextSize(text) * Scale;
		drawList.AddText(GetFont(), GetFontSize() * Scale, center - size / 2, GetColorU32(ImGuiCol.Text), text);
	}

	// Up to two centered lines, split at the space nearest the middle, each clipped to the cell.
	static void DrawLabel(ImDrawListPtr drawList, ReadOnlySpan<char> label, float left, float top)
	{
		var split = -1;
		if (CalcTextSize(label).X * LabelScale > CellWidth)
			for (var i = 0; i < label.Length; i++)
				if (label[i] == ' ' && (split < 0 || Math.Abs(i - label.Length / 2) < Math.Abs(split - label.Length / 2)))
					split = i;
		if (split < 0)
			DrawLabelLine(drawList, label, left, top);
		else
		{
			DrawLabelLine(drawList, label[..split], left, top);
			DrawLabelLine(drawList, label[(split + 1)..], left, top + LabelLineHeight);
		}
	}

	static float LabelLineHeight => GetFontSize() * LabelScale;

	static void DrawLabelLine(ImDrawListPtr drawList, ReadOnlySpan<char> text, float left, float top)
	{
		var x = left + MathF.Max(0, (CellWidth - CalcTextSize(text).X * LabelScale) / 2);
		drawList.PushClipRect(new(left, top), new(left + CellWidth, top + LabelLineHeight), true);
		drawList.AddText(GetFont(), GetFontSize() * LabelScale, new Vector2(x, top), GetColorU32(ImGuiCol.Text), text);
		drawList.PopClipRect();
	}
}
#endif
