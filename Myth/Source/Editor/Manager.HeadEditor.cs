#if EDITOR
using System.Numerics;
using System.Reflection;
using System.Text.Json.Serialization;
using Foster.Framework;
using ImGuiNET;
using static ImGuiNET.ImGui;

namespace Myth;

// Sculpted head shapes (Sources/Heads): a thumbnail grid, a turnable preview, and a slider per feature knob.
// Edits show live on every rig using the shape and save when a slider is released.
public partial class Manager
{
	bool headEditorOpen;
	string? headEditorId;
	string newHeadName = "";
	float headPreviewYaw = -0.6f;
	float headPreviewMouth;
	readonly Dictionary<string, SculptedHead> headThumbnails = new(StringComparer.OrdinalIgnoreCase);
	SculptedHead? headPreview;
	static float HeadThumbnailRadius => 11f;
	static float HeadPreviewRadius => 26f;
	static float HeadPreviewDragTurn => 0.012f; // radians per pixel dragged
	static Vector2 HeadThumbnailButton => new(56);
	static readonly Color HeadNear = new(0xc8d0d8), HeadCore = new(0x8a96a3), HeadFar = new(0x4a5563), HeadEye = new(0xd92240);

	static readonly (string Group, string Name, float Min, float Max)[] HeadKnobs =
	[
		("Cranium", nameof(HeadShape.CraniumWidth), 0.4f, 1.2f), ("Cranium", nameof(HeadShape.CraniumHeight), 0.4f, 1.3f),
		("Cranium", nameof(HeadShape.CraniumDepth), 0.4f, 1.3f), ("Cranium", nameof(HeadShape.CraniumLift), -0.3f, 0.5f),
		("Jaw", nameof(HeadShape.JawWidth), 0.2f, 1.1f), ("Jaw", nameof(HeadShape.JawHeight), 0.15f, 0.9f),
		("Jaw", nameof(HeadShape.JawDepth), 0.2f, 1.1f), ("Jaw", nameof(HeadShape.JawDrop), 0f, 0.9f),
		("Jaw", nameof(HeadShape.JawForward), -0.2f, 0.6f), ("Jaw", nameof(HeadShape.JawBlend), 0.01f, 0.6f),
		("Brow", nameof(HeadShape.BrowSize), 0f, 0.35f), ("Brow", nameof(HeadShape.BrowWidth), 0.1f, 0.8f),
		("Brow", nameof(HeadShape.BrowHeight), -0.1f, 0.6f), ("Brow", nameof(HeadShape.BrowForward), 0.4f, 1.1f),
		("Brow", nameof(HeadShape.BrowHair), 0f, 0.12f),
		("Nose", nameof(HeadShape.NoseSize), 0f, 0.35f), ("Nose", nameof(HeadShape.NoseLength), 0f, 0.8f),
		("Nose", nameof(HeadShape.NoseHeight), -0.3f, 0.5f), ("Nose", nameof(HeadShape.NoseDroop), -0.3f, 0.8f),
		("Nose", nameof(HeadShape.NoseFlat), 0f, 1f), ("Nose", nameof(HeadShape.NostrilSize), 0f, 0.15f),
		("Eyes", nameof(HeadShape.EyeSpacing), 0.1f, 0.6f), ("Eyes", nameof(HeadShape.EyeHeight), -0.3f, 0.5f),
		("Eyes", nameof(HeadShape.EyeSize), 0f, 0.45f), ("Eyes", nameof(HeadShape.EyeDepth), 0.4f, 1f),
		("Eyes", nameof(HeadShape.EyeballSize), 0f, 0.4f), ("Eyes", nameof(HeadShape.IrisSize), 0.1f, 1.2f),
		("Eyes", nameof(HeadShape.LidDroop), 0f, 1f),
		("Mouth", nameof(HeadShape.MouthWidth), 0f, 0.6f), ("Mouth", nameof(HeadShape.MouthHeight), -0.9f, -0.1f),
		("Mouth", nameof(HeadShape.MouthSize), 0.01f, 0.2f), ("Mouth", nameof(HeadShape.LipSize), 0f, 0.1f),
		("Mouth", nameof(HeadShape.TeethSize), 0f, 0.2f),
		("Ears", nameof(HeadShape.EarSize), 0f, 0.5f), ("Ears", nameof(HeadShape.EarHeight), -0.4f, 0.4f),
	];

	static readonly PropertyInfo[] HeadColors =
		typeof(HeadShape).GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(property => property.PropertyType == typeof(Color)).ToArray();

	static bool headKnobsChecked;

	// Every serialized HeadShape knob needs a slider here, and every slider a knob: a field added to one and not the
	// other fails loudly the first time the window opens.
	static void CheckHeadKnobs()
	{
		var properties = typeof(HeadShape).GetProperties(BindingFlags.Public | BindingFlags.Instance)
			.Where(property => property.PropertyType == typeof(float) && property.GetCustomAttribute<JsonIgnoreAttribute>() == null)
			.Select(property => property.Name).ToHashSet();
		var knobs = HeadKnobs.Select(knob => knob.Name).ToHashSet();
		var unexposed = properties.Except(knobs).ToArray();
		var unknown = knobs.Except(properties).ToArray();
		if (unexposed.Length > 0 || unknown.Length > 0)
			throw new InvalidOperationException(
				$"HeadKnobs is out of sync with HeadShape. No slider for: [{string.Join(", ", unexposed)}]; " +
				$"no HeadShape float for: [{string.Join(", ", unknown)}].");
	}

	static string HeadShapePath(string id) => Path.Join(Assets.SourcesPath, "Heads", $"{id}.json");

	void HeadEditorImGui()
	{
		if (!headEditorOpen)
			return;
		if (!headKnobsChecked)
		{
			CheckHeadKnobs();
			headKnobsChecked = true;
		}
		SetNextWindowSize(new Vector2(460, 760), ImGuiCond.FirstUseEver);
		DockNextToolWindow("Heads");
		if (!Begin("Heads", ref headEditorOpen))
		{
			NoteDockedWindow();
			End();
			return;
		}
		NoteDockedWindow();

		if (headEditorId == null || !Assets.HeadShapes.ContainsKey(headEditorId))
			headEditorId = Assets.HeadShapes.Keys.Order().FirstOrDefault();
		DrawHeadGrid();
		Separator();
		if (headEditorId != null && Assets.HeadShapes.TryGetValue(headEditorId, out var shape))
		{
			BeginChild("HeadBody");
			DrawHeadPreview(shape);
			DrawHeadKnobs(headEditorId, shape);
			EndChild();
		}
		else
			TextDisabled("Add a head to get started.");
		DrawNewHeadPopup();
		End();
	}

	void DrawHeadGrid()
	{
		var perRow = Math.Max(1, (int)((GetContentRegionAvail().X + GetStyle().ItemSpacing.X) /
			(HeadThumbnailButton.X + GetStyle().ItemSpacing.X + GetStyle().FramePadding.X * 2)));
		var column = 0;
		foreach (var id in Assets.HeadShapes.Keys.Order())
		{
			var thumbnail = headThumbnails.TryGetValue(id, out var cached) ? cached
				: headThumbnails[id] = new SculptedHead(GraphicsDevice);
			thumbnail.Render(Assets.HeadShapes[id], HeadThumbnailRadius, -0.6f, 0, 0, default, HeadNear, HeadCore, HeadFar, HeadEye);
			if (column++ % perRow != 0)
				SameLine();
			if (ImGuiSubtextureButton($"##head-{id}", new Subtexture(thumbnail.ColorTexture!), id, id == headEditorId,
				HeadThumbnailButton, fitAspectRatio: true))
				headEditorId = id;
		}
		if (column % perRow != 0)
			SameLine();
		if (Button("+##NewHead", HeadThumbnailButton + GetStyle().FramePadding * 2))
		{
			newHeadName = "";
			OpenPopup("New Head");
		}
		if (IsItemHovered())
			SetTooltip(headEditorId == null ? "New head" : $"New head, starting from {headEditorId}");
	}

	void DrawHeadPreview(HeadShape shape)
	{
		headPreview ??= new SculptedHead(GraphicsDevice);
		headPreview.Render(shape, HeadPreviewRadius, headPreviewYaw, 0, 0, new Expression(MouthOpen: headPreviewMouth), HeadNear, HeadCore, HeadFar, HeadEye);
		var size = headPreview.Size * Scale;
		SetCursorPosX(GetCursorPosX() + MathF.Max(0, (GetContentRegionAvail().X - size) * 0.5f));
		var at = GetCursorScreenPos();
		imgui.Image(new Subtexture(headPreview.ColorTexture!), new Vector2(size));
		// Dragging across the preview turns it, the same as the turn slider.
		SetCursorScreenPos(at);
		InvisibleButton("##HeadSpin", new Vector2(size));
		if (IsItemActive())
			headPreviewYaw = MathF.IEEERemainder(headPreviewYaw + GetIO().MouseDelta.X * HeadPreviewDragTurn, MathF.Tau);
		if (IsItemHovered())
			ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeEW);
		SetNextItemWidth(-1);
		SliderAngle("##Turn", ref headPreviewYaw, -180, 180, "turn %.0f deg");
		SetNextItemWidth(-1);
		SliderFloat("##Mouth", ref headPreviewMouth, 0, 1, "mouth open %.2f");
	}

	void DrawHeadKnobs(string id, HeadShape shape)
	{
		string? group = null;
		foreach (var knob in HeadKnobs)
		{
			if (knob.Group != group)
				SeparatorText(group = knob.Group);
			var property = typeof(HeadShape).GetProperty(knob.Name, BindingFlags.Public | BindingFlags.Instance)!;
			var value = (float)property.GetValue(shape)!;
			SetNextItemWidth(-140);
			if (SliderFloat(knob.Name, ref value, knob.Min, knob.Max, "%.3f"))
			{
				property.SetValue(shape, value);
				shape.Version++;
				InvalidateHeadUsers(id);
			}
			if (IsItemDeactivatedAfterEdit())
				shape.Save(HeadShapePath(id));
		}
		DrawHeadAdornments(id, shape);
		SeparatorText("Colors");
		foreach (var property in HeadColors)
		{
			var color = ((Color)property.GetValue(shape)!).ToVector3();
			if (ColorEdit3(property.Name, ref color, ImGuiColorEditFlags.NoInputs))
			{
				property.SetValue(shape, new Color(color));
				shape.Version++;
				InvalidateHeadUsers(id);
			}
			if (IsItemDeactivatedAfterEdit())
				shape.Save(HeadShapePath(id));
		}
	}

	void DrawHeadAdornments(string id, HeadShape shape)
	{
		SeparatorText("Adornments");
		var changed = false;
		var released = false;
		var remove = -1;
		for (var i = 0; i < shape.Adornments.Count; i++)
		{
			var adornment = shape.Adornments[i];
			PushID(i);
			var kind = (int)adornment.Kind;
			SetNextItemWidth(110);
			if (Combo("##Kind", ref kind, AdornmentKinds, AdornmentKinds.Length))
				(adornment.Kind, changed) = ((AdornmentKind)kind, true);
			SameLine();
			var mirror = adornment.Mirror;
			if (Checkbox("Mirror", ref mirror))
				(adornment.Mirror, changed) = (mirror, true);
			SameLine();
			var metal = adornment.Metal;
			if (Checkbox("Metal", ref metal))
				(adornment.Metal, changed) = (metal, true);
			SameLine();
			var color = adornment.Color.ToVector3();
			if (ColorEdit3("##Color", ref color, ImGuiColorEditFlags.NoInputs))
				(adornment.Color, changed) = (new Color(color), true);
			SameLine();
			if (SmallButton("x"))
				remove = i;
			changed |= Knob("X", adornment.X, -1.2f, 1.2f, v => adornment.X = v);
			changed |= Knob("Y", adornment.Y, -1.2f, 1.2f, v => adornment.Y = v);
			changed |= Knob("Z", adornment.Z, -1.2f, 1.2f, v => adornment.Z = v);
			changed |= Knob("Size", adornment.Size, 0.01f, 0.8f, v => adornment.Size = v);
			changed |= Knob("Thickness", adornment.Thickness, 0.005f, 0.3f, v => adornment.Thickness = v);
			changed |= Knob("Yaw", adornment.Yaw, -180, 180, v => adornment.Yaw = v);
			if (adornment.Kind == AdornmentKind.Horn)
				changed |= Knob("Pitch", adornment.Pitch, -90, 90, v => adornment.Pitch = v);
			PopID();
		}
		if (Button("Add adornment"))
		{
			shape.Adornments.Add(new() { X = 0.8f, Y = -0.1f, Z = 0.1f, Yaw = 90 });
			changed = true;
		}
		if (remove >= 0)
		{
			shape.Adornments.RemoveAt(remove);
			changed = true;
		}
		if (changed)
		{
			shape.Version++;
			InvalidateHeadUsers(id);
		}
		// Discrete edits save at once; sliders save when let go.
		if (changed && !IsAnyItemActive() || released)
			shape.Save(HeadShapePath(id));

		bool Knob(string label, float value, float min, float max, Action<float> set)
		{
			SetNextItemWidth(-140);
			var moved = SliderFloat(label, ref value, min, max, "%.3f");
			if (moved)
				set(value);
			released |= IsItemDeactivatedAfterEdit();
			return moved;
		}
	}

	static readonly string[] AdornmentKinds = Enum.GetNames<AdornmentKind>();

	// The edit view draws rooms from cached previews; rooms with an actor wearing this head redraw theirs.
	void InvalidateHeadUsers(string id)
	{
		foreach (var room in Assets.RoomsById.Values)
			for (var i = 0; i < room.Actors.Count; i++)
			{
				var actor = room.Actors[i];
				var worn = ActorSpecs.ReadResolvedString(actor, nameof(HumanoidRigSettings.HeadShapeId)) is { Length: > 0 } picked
					? picked
					: ActorSpecs.GetResolvedProperty(actor, nameof(HumanoidRigSettings.SculptedHead))?.GetValue<bool>() == true
						? Assets.StockHeadShape : null;
				if (string.Equals(worn, id, StringComparison.OrdinalIgnoreCase))
				{
					MarkRoomPreviewDirty(room);
					break;
				}
			}
	}

	void DrawNewHeadPopup()
	{
		var open = true;
		if (!BeginPaddedPopupModal("New Head", ref open, ImGuiWindowFlags.AlwaysAutoResize))
			return;
		TextUnformatted("Name the new head:");
		SetNextItemWidth(240);
		if (IsWindowAppearing())
			SetKeyboardFocusHere();
		var submit = InputTextWithHint("##NewHeadName", "head name", ref newHeadName, 64, ImGuiInputTextFlags.EnterReturnsTrue);
		var id = newHeadName.Trim().ToLowerInvariant();
		var valid = id.Length > 0 && char.IsAsciiLetter(id[0]) && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_') &&
			!Assets.HeadShapes.ContainsKey(id) && !File.Exists(HeadShapePath(id));
		BeginDisabled(!valid);
		var create = Button("Create", new Vector2(90, 0)) || submit;
		EndDisabled();
		SameLine();
		var cancel = Button("Cancel", new Vector2(90, 0)) || !open;
		if (create && valid)
		{
			// Starts from the selected head, so variations branch from what's on screen.
			var source = headEditorId != null && Assets.HeadShapes.TryGetValue(headEditorId, out var selected) ? selected : HeadShape.Default;
			var shape = HeadShape.Parse(System.Text.Json.JsonSerializer.Serialize(source, HeadShapeJsonContext.Default.HeadShape));
			shape.Save(HeadShapePath(id));
			Assets.HeadShapes[id] = shape;
			headEditorId = id;
			newHeadName = "";
			CloseCurrentPopup();
		}
		else if (cancel)
		{
			newHeadName = "";
			CloseCurrentPopup();
		}
		EndPaddedPopup();
	}
}
#endif
