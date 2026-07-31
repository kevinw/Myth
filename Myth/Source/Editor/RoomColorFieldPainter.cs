#if EDITOR
using System.Numerics;
using Foster.Framework;
using ImGuiNET;

namespace Myth;

public sealed class RoomColorFieldPainter
{
	string? roomId;
	Vector3 color = new(0.72f, 0.46f, 0.62f);
	float radius = 28.0f;
	float softness = 0.55f;
	float strength = 0.8f;

	public Factory.TileLayer Layer { get; private set; } = Factory.TileLayer.Foreground;
	public bool Stroke { get; set; }
	public bool Active => roomId != null;
	public float Radius => radius;
	public float Softness => softness;
	public Color BrushColor => new(color);

	public bool IsActive(Room room) => roomId == room.Id;

	public bool DrawStartStop(Room room, Factory.TileLayer initialLayer)
	{
		var active = IsActive(room);
		if (!ImGui.Button(active ? "Stop Painting Color" : "Paint Room Color"))
			return false;
		if (active)
			Stop();
		else
			Start(room, initialLayer);
		return true;
	}

	public void Start(Room room, Factory.TileLayer layer)
	{
		roomId = room.Id;
		Layer = layer;
	}

	public void DrawBrushControls(out bool clear)
	{
		clear = false;
		if (ImGui.RadioButton("Foreground##RoomColorLayer", Layer == Factory.TileLayer.Foreground))
			Layer = Factory.TileLayer.Foreground;
		ImGui.SameLine();
		if (ImGui.RadioButton("Background##RoomColorLayer", Layer == Factory.TileLayer.Background))
			Layer = Factory.TileLayer.Background;

		ImGui.TextUnformatted("Color");
		ImGui.SetNextItemWidth(-1);
		ImGui.ColorEdit3("##RoomColorBrush", ref color, ImGuiColorEditFlags.NoInputs);
		ImGui.SliderFloat("Radius##RoomColor", ref radius, 4.0f, 128.0f);
		ImGui.SliderFloat("Softness##RoomColor", ref softness, 0.05f, 1.0f);
		ImGui.SliderFloat("Strength##RoomColor", ref strength, 0.05f, 1.0f);

		if (ImGui.Button($"Clear {Layer}"))
			clear = true;
	}

	public bool Paint(Room room, Vector2 world, float delta, bool erase)
	{
		if (!IsActive(room) || !room.WorldBounds.Contains(world))
			return false;

		var field = Field(room);
		var version = field.Version;
		field.Paint(world - (Vector2)room.WorldBounds.TopLeft, radius, softness,
			Math.Clamp(strength * delta * 10.0f, 0.0f, 1.0f), BrushColor, erase);
		return version != field.Version;
	}

	public RoomColorField Field(Room room)
		=> Layer == Factory.TileLayer.Foreground ? room.ForegroundColorField : room.BackgroundColorField;

	public void Stop()
	{
		roomId = null;
	}
}
#endif
