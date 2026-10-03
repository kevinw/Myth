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

// The property inspector: draws and edits actor-spec (prefab) and actor-instance properties in the
// ImGui side panels -- value widgets plus the pickers for surfaces, platform materials, and refs.
public partial class Manager
{
	string rigAnimationPickerSearch = "";

	void ActorSpecInspectorImGui()
	{
		if (!actorSpecInspectorOpen || selectedActorSpecId == null)
			return;

		var dirty = ActorSpecs.IsSpecDirty(selectedActorSpecId);
		var windowTitle = $"Prefab: {selectedActorSpecId}{(dirty ? " *" : "")}";
		SetNextWindowSize(new Vector2(260, 0), ImGuiCond.FirstUseEver);
		var open = actorSpecInspectorOpen;
		// Settings are saved: the "###" id means every prefab shares this one window, so the
		// place you put it is worth keeping across launches (and lets it be docked).
		if (!Begin($"{windowTitle}###ActorSpecInspector", ref open))
		{
			actorSpecInspectorOpen = open;
			End();
			return;
		}
		actorSpecInspectorOpen = open;
		if (IsWindowDocked())
			DrawDockedInspectorTitle(windowTitle);

		var changed = false;
		var spec = ActorSpecs.Get(selectedActorSpecId);
		actorPropertyScratch.Clear();
		actorPropertyScratch.AddRange(ActorSpecs.GetEditableProperties(selectedActorSpecId));
		if (spec != null)
			changed |= DrawRandomizeActorPropertiesButton(selectedActorSpecId, null, spec);
		string? propertyGroup = null;
		var propertyGroupOpen = true;
		foreach (var property in actorPropertyScratch)
		{
			if (spec != null && HideInactiveActorProperty(spec, property))
				continue;
			if (!PropertyGroupVisible(property, ref propertyGroup, ref propertyGroupOpen))
				continue;
			PushID(property.JsonName);
			changed |= DrawActorSpecProperty(selectedActorSpecId, property);
			PopID();
		}

		if (changed)
		{
			SyncEditorGame();
		}

		BeginDisabled(!dirty || !ActorSpecs.CanSaveSpec(selectedActorSpecId));
		if (Button("Save"))
			SaveSelectedActorSpec();
		EndDisabled();

		End();
	}

	bool SaveSelectedActorSpec()
	{
		if (!actorSpecInspectorOpen || selectedActorSpecId == null ||
			!ActorSpecs.IsSpecDirty(selectedActorSpecId) || !ActorSpecs.CanSaveSpec(selectedActorSpecId))
			return false;

		CommitPrefabUndo();
		ActorSpecs.SaveSpec(selectedActorSpecId);
		return true;
	}

	bool DrawActorSpecProperty(string specId, ActorSpecs.EditableProperty property)
	{
		if (ActorSpecs.Get(specId) is not { } spec)
			return false;

		return DrawActorProperty(specId, null, spec, property);
	}

	bool DrawRandomizeActorPropertiesButton(string? specId, Room? room, JsonObject actor)
	{
		var hasRanges = false;
		foreach (var property in actorPropertyScratch)
			if (property.Inspector != null)
			{
				hasRanges = true;
				break;
			}

		if (!hasRanges) return false;

		var change = Button($"{EditorIcons.Randomize}");
		if (IsItemHovered())
			SetTooltip($"Randomize All Actor Properties");

		if (!change) return false;

		if (specId != null)
		{
			using (CapturePrefabUndo("Randomize Actor Properties", specId))
				RandomizeActorProperties(specId, actor);
		}
		else if (room != null)
		{
			using (CaptureRoomUndo("Randomize Actor Properties", room))
				RandomizeActorProperties(null, actor);
		}
		return true;
	}

	void RandomizeActorProperties(string? specId, JsonObject actor)
	{
		foreach (var property in actorPropertyScratch)
			if (property.Inspector is { } inspector)
			{
				var value = RandomActorValue(property, inspector);
				if (specId != null)
					ActorSpecs.SetProperty(specId, property.JsonName, value);
				else
					ActorSpecs.SetProperty(actor, property.JsonName, value);
			}
	}

	static JsonNode RandomActorValue(ActorSpecs.EditableProperty property, IAttribute inspector)
		=> property.Type == typeof(int)
			? JsonValue.Create(Random.Shared.Next((int)MathF.Ceiling(inspector.Min), (int)MathF.Floor(inspector.Max) + 1))
			: JsonValue.Create(inspector.Min + Random.Shared.NextSingle() * (inspector.Max - inspector.Min));

	bool DrawActorProperty(string? specId, Room? room, JsonObject actor, ActorSpecs.EditableProperty property)
	{
		var row = BeginPropertyRow(property.DisplayName, property.HasLocalValue,
			additionalTrailingButtons: property.Inspector != null ? 1 : 0);
		if (BeginPopupContextItem("##PropertyContext"))
		{
			if (MenuItem(property.Inspector != null ? "Open [I] in Zed" : "Open declaration in Zed"))
				ActorZedLauncher.OpenActorProperty(property.DeclaringType, property.Name);
			if (specId == null && property.Inspector != null && (property.Type == typeof(float) || property.Type == typeof(int)))
				if (MenuItem("MIDI Learn"))
				{
					midiRoomTarget = false;
					midiWindowOpen = true;
					midiLearnParameter = $"actor:{ActorSpecs.GetTypeName(actor)}:{property.Name}";
				}
			EndPopup();
		}
		PushID("Value");
		var changed = DrawActorPropertyValue(specId, room, actor, property, row.ControlWidth);
		PopID();

		if (specId != null && undo.HasPending(PrefabUndoSlot, specId) && IsItemDeactivatedAfterEdit())
			CommitPrefabUndo();
		if (property.Inspector is { } range && IsItemHovered())
			SetTooltip($"Drag range: {range.Min:g} to {range.Max:g}\nRight-click to enter any value");

		if (property.Inspector is { } inspector)
		{
			SameLine();
			if (SmallButton($"{EditorIcons.Randomize}##Randomize"))
			{
				SetActorProperty(specId, room, actor, property, RandomActorValue(property, inspector), "Randomize Actor Property");
				changed = true;
			}
			if (IsItemHovered())
				SetTooltip($"Randomize from {inspector.Min:g} to {inspector.Max:g}");
		}

		if (property.HasLocalValue)
		{
			SameLine();
			if (DrawPropertyResetButton(property))
			{
				ResetActorProperty(specId, room, actor, property);
				changed = true;
			}
		}

		return changed;
	}

	bool DrawActorPropertyValue(string? specId, Room? room, JsonObject actor, ActorSpecs.EditableProperty property, float controlWidth)
	{
		if (IsActorSurfaceProperty(property))
		{
			var value = ActorSpecs.ReadStringValue(property.Value) ?? string.Empty;
			var picked = DrawSurfacePicker(ref value, out var preview);
			if (picked)
			{
				ClearContentPickerPreview(restore: false);
				SetActorProperty(specId, room, actor, property, JsonValue.Create(value), continuous: false);
				return true;
			}
			if (room != null)
				PreviewActorProperty(room, actor, property.Name, value, preview);
		}
		else if (property.Type == typeof(ContentRef<CostumeContent>) || property.Type == typeof(ContentRef<RibbonStyleContent>))
		{
			var value = ActorSpecs.ReadStringValue(property.Value) ?? string.Empty;
			var changed = false;
			if (BeginCombo("##Value", value == "" ? "(none)" : value))
			{
				if (Selectable("(none)", value == ""))
				{
					value = "";
					changed = true;
				}
				foreach (var name in property.Type == typeof(ContentRef<CostumeContent>) ? (IEnumerable<string>)Assets.Costumes.Keys : Assets.RibbonStyles.Keys)
					if (Selectable(name, value == name))
					{
						value = name;
						changed = true;
					}
				EndCombo();
			}
			if (changed)
				SetActorProperty(specId, room, actor, property, JsonValue.Create(value), continuous: false);
			return changed;
		}
		else if (property.Type == typeof(ContentRef<VisualTextureContent>))
		{
			var value = ActorSpecs.ReadStringValue(property.Value) ?? string.Empty;
			if (DrawSurfaceTexturePicker(ref value, controlWidth))
			{
				SetActorProperty(specId, room, actor, property, JsonValue.Create(value), continuous: false);
				return true;
			}
		}
		else if (property.Type == typeof(float))
		{
			var value = property.Value?.GetValue<float>() ?? 0;
			var edited = property.Inspector is { } inspector
				? RangedFloat("##Value", ref value, inspector.Min, inspector.Max)
				: DragFloat("##Value", ref value, 0.05f);
			if (edited)
			{
				SetActorProperty(specId, room, actor, property, JsonValue.Create(value), continuous: true);
				return true;
			}
		}
		else if (property.Type == typeof(int))
		{
			var value = property.Value?.GetValue<int>() ?? 0;
			var edited = property.Inspector is { } inspector
				? RangedInt("##Value", ref value, (int)inspector.Min, (int)inspector.Max)
				: DragInt("##Value", ref value);
			if (edited)
			{
				SetActorProperty(specId, room, actor, property, JsonValue.Create(value), continuous: true);
				return true;
			}
		}
		else if (IsRevealDoorTileTypeProperty(actor, property))
		{
			var value = ActorSpecs.ReadStringValue(property.Value) ?? string.Empty;
			if (DrawRevealDoorTileTypeButtons(ref value, controlWidth))
			{
				SetActorProperty(specId, room, actor, property, JsonValue.Create(value), continuous: false);
				return true;
			}
		}
		else if (IsDualMaterialProperty(property))
		{
			var value = ActorSpecs.ReadStringValue(property.Value) ?? "metal-dual";
			if (DrawDualMaterialPicker(ref value, out var preview))
			{
				ClearContentPickerPreview(restore: false);
				SetActorProperty(specId, room, actor, property, JsonValue.Create(value), continuous: false);
				return true;
			}
			if (room != null)
				PreviewActorProperty(room, actor, property.Name, value, preview);
		}
		else if (property.Name == nameof(Prop.SpriteName) && IsPropActor(actor))
		{
			var value = ActorSpecs.ReadStringValue(property.Value) ?? string.Empty;
			if (DrawSpriteCombo(ref value))
			{
				SetActorProperty(specId, room, actor, property, JsonValue.Create(value), continuous: false);
				return true;
			}
		}
		else if (property.DeclaringType == typeof(RigPuppet) &&
			property.Name is nameof(RigPuppet.IdleAnimationName) or nameof(RigPuppet.WalkAnimationName) ||
			property.DeclaringType == typeof(RigPuppetProp) && property.Name == nameof(RigPuppetProp.PoseAnimationName))
		{
			var value = ActorSpecs.ReadStringValue(property.Value) ?? string.Empty;
			if (DrawRigAnimationCombo(ref value))
			{
				SetActorProperty(specId, room, actor, property, JsonValue.Create(value), continuous: false);
				return true;
			}
		}
		else if (NeuralTextureWeightsSlot(actor, property) is >= 0 and var weightsSlot)
		{
			var value = ActorSpecs.ReadStringValue(property.Value) ?? string.Empty;
			var picked = DrawNeuralModelPicker(ref value, allowNone: weightsSlot > 0, out var preview);
			if (picked)
			{
				ClearContentPickerPreview(restore: false);
				SetActorProperty(specId, room, actor, property, JsonValue.Create(value), continuous: false);
				return true;
			}
			if (room != null)
				PreviewNeuralModel(room, actor, weightsSlot, value, preview);
		}
		else if (property.Name == nameof(NeuralTextureActor.PaintedMask) && IsNeuralTextureActor(actor))
		{
			if (room != null)
				DrawMaskPaintControls(room, actor);
			else
				TextDisabled("(painted per room instance)");
		}
		else if (property.Type == typeof(string) || ActorSpecs.IsContentReference(property.Type))
		{
			var value = ActorSpecs.ReadStringValue(property.Value) ?? string.Empty;
			if (property.ActorRef is { } actorRef && room != null)
				return DrawActorRefProperty(specId, room, actor, property, actorRef, value);

			// Single line unless the field asks for prose -- or already holds a newline, so text
			// authored before the annotation stays readable.
			var lines = property.Multiline?.Lines ?? (value.Contains('\n') ? 4 : 0);
			var edited = lines > 0
				? InputTextMultiline("##Value", ref value, 2048, new Vector2(controlWidth, GetTextLineHeight() * lines + GetStyle().FramePadding.Y * 2))
				: InputText("##Value", ref value, 2048);
			if (edited)
			{
				SetActorProperty(specId, room, actor, property, ActorSpecs.StringToJson(value), continuous: true);
				return true;
			}
		}
		else if (property.Type == typeof(Point2) || property.Type == typeof(Point2?))
		{
			var point = ActorSpecs.ReadPoint(property.Value) ?? Point2.Zero;
			Span<int> values = stackalloc int[2] { point.X, point.Y };
			if (DragInt2("##Value", ref values[0]))
			{
				SetActorProperty(specId, room, actor, property, new JsonArray(values[0], values[1]), continuous: true);
				return true;
			}
		}
		else if (property.Type == typeof(Vector2))
		{
			var value = JsonUtil.ReadVector2(property.Value) ?? Vector2.Zero;
			if (DragFloat2("##Value", ref value, 0.05f))
			{
				SetActorProperty(specId, room, actor, property, ActorSpecs.ToJsonNode(value), continuous: true);
				return true;
			}
		}
		else if (property.Type == typeof(Point2[]))
		{
			var points = ActorSpecs.ReadPointArray(property.Value) ?? [];
			TextUnformatted($"{points.Length} points");
			SameLine();
			if (SmallButton("Add"))
			{
				var next = points.Length > 0 ? points[^1] + new Point2(Game.TileSize * 4, 0) : Point2.Zero;
				SetActorProperty(specId, room, actor, property,
					ActorSpecs.ToJsonNode((Point2[])[.. points, next]), "Add Path Point");
				return true;
			}
			if (points.Length > 2)
			{
				SameLine();
				if (SmallButton("Remove Last"))
				{
					SetActorProperty(specId, room, actor, property,
						ActorSpecs.ToJsonNode(points[..^1]), "Remove Path Point");
					return true;
				}
			}
			if (points.Length > 0 && livePropertyTarget == null)
			{
				SameLine();
				if (SmallButton("Reposition to Center"))
					return RecenterPathPoints(specId, room, actor, property, points);
				if (IsItemHovered())
					SetTooltip("Move the origin to the average of the points, keeping the path where it is");
			}
		}
		else if (property.Type == typeof(bool))
		{
			var value = property.Value?.GetValue<bool>() ?? false;
			if (Checkbox("##Value", ref value))
			{
				SetActorProperty(specId, room, actor, property, JsonValue.Create(value), continuous: false);
				return true;
			}
		}
		else if (property.Type == typeof(Color))
		{
			var rgb = JsonUtil.ReadColor(property.Value)?.ToVector3() ?? Vector3.One;
			if (ColorEdit3("##Value", ref rgb, ImGuiColorEditFlags.NoInputs))
			{
				var value = new Color(Vector3.Clamp(rgb, Vector3.Zero, Vector3.One)).ToHexStringRGB();
				SetActorProperty(specId, room, actor, property, JsonValue.Create(value), continuous: true);
				return true;
			}
		}
		else if (property.Type == typeof(Signs) && property.Name == "Facing")
		{
			var value = ActorSpecs.ReadSigns(property.Value);
			const string facingTooltip = "Click to face the other direction.";
			if (ImGuiSubtextureButton("##Value", Assets.GetSubtexture("buttons/15"), facingTooltip, false, new Vector2(ButtonSize, ButtonSize), true))
			{
				SetActorProperty(specId, room, actor, property, JsonValue.Create((-value).ToString()), continuous: false);
				return true;
			}
		}
		else if (property.Type == typeof(Cardinal))
		{
			var value = ActorSpecs.ReadCardinal(property.Value) ?? Cardinal.Right;
			if (DrawCardinalCombo(ref value))
			{
				SetActorProperty(specId, room, actor, property, ActorSpecs.ToJsonNode(value), continuous: false);
				return true;
			}
		}
		else if (property.Type.IsEnum)
		{
			var value = ActorSpecs.ReadStringValue(property.Value) ?? Enum.GetNames(property.Type).FirstOrDefault() ?? string.Empty;
			if (EditorGui.DrawEnumCombo(property.Type, ref value))
			{
				SetActorEnumProperty(specId, room, actor, property, value);
				return true;
			}
		}
		else
		{
			TextUnformatted("-");
		}

		return false;
	}

	static bool RangedFloat(string label, ref float value, float min, float max)
	{
		var changed = SliderFloat(label, ref value, min, max, "%.3f");
		if (BeginPopupContextItem("##ExactValue"))
		{
			if (IsWindowAppearing())
				SetKeyboardFocusHere();
			SetNextItemWidth(140);
			changed |= InputFloat("##ExactValueInput", ref value, 0, 0, "%.3f");
			EndPopup();
		}
		return changed;
	}

	static bool RangedInt(string label, ref int value, int min, int max)
	{
		var changed = SliderInt(label, ref value, min, max);
		if (BeginPopupContextItem("##ExactValue"))
		{
			if (IsWindowAppearing())
				SetKeyboardFocusHere();
			SetNextItemWidth(140);
			changed |= InputInt("##ExactValueInput", ref value, 0);
			EndPopup();
		}
		return changed;
	}

	// Pulls the actor origin onto the middle of its own path and shifts the points back by the same amount, so
	// the shape doesn't move. Worth having because the origin is what everything else sees -- gizmos, the
	// attachment offset, and any solid whose rider sweep can snag it -- and a path authored far from its anchor
	// leaves that origin somewhere arbitrary.
	bool RecenterPathPoints(string? specId, Room? room, JsonObject actor, ActorSpecs.EditableProperty property, Point2[] points)
	{
		var sum = Point2.Zero;
		foreach (var point in points)
			sum += point;
		var center = ((Vector2)sum / points.Length).RoundToPoint2();
		if (center == Point2.Zero)
			return false;

		var centered = new Point2[points.Length];
		for (var i = 0; i < points.Length; i++)
			centered[i] = points[i] - center;

		var position = ActorSpecs.ReadPoint(actor, "Position") ?? Point2.Zero;
		(string, JsonNode?) path = (property.JsonName, ActorSpecs.ToJsonNode(centered));
		(string, JsonNode?) moved = ("Position", ActorSpecs.ToJsonNode(position + center));
		// An attached actor is placed from its target plus the offset every frame, so that has to move with it.
		if (string.IsNullOrWhiteSpace(ActorSpecs.ReadResolvedString(actor, nameof(Actor.AttachedToActorId))))
			SetActorProperties(specId, room, actor, "Center Path Points", [path, moved]);
		else
		{
			var offset = ActorSpecs.ReadPoint(actor, nameof(Actor.AttachmentOffset)) ?? Point2.Zero;
			SetActorProperties(specId, room, actor, "Center Path Points",
				[path, moved, (nameof(Actor.AttachmentOffset), ActorSpecs.ToJsonNode(offset + center))]);
		}
		return true;
	}

	// Several authored properties under one undo entry. The live inspector writes straight to the runtime actor,
	// so callers gate on livePropertyTarget rather than routing live edits through here.
	void SetActorProperties(string? specId, Room? room, JsonObject actor, string undoName,
		ReadOnlySpan<(string JsonName, JsonNode? Value)> values)
	{
		if (specId != null)
		{
			using (CapturePrefabUndo(undoName, specId))
				foreach (var (jsonName, value) in values)
					ActorSpecs.SetProperty(specId, jsonName, value);
		}
		else if (room != null)
		{
			using (CaptureRoomUndo(undoName, room))
				foreach (var (jsonName, value) in values)
					ActorSpecs.SetProperty(actor, jsonName, value);
		}
	}

	void SetActorProperty(string? specId, Room? room, JsonObject actor, ActorSpecs.EditableProperty property, JsonNode? value, bool continuous)
	{
		if (livePropertyTarget != null)
		{
			if (ActorSpecs.SetEditableProperty(livePropertyTarget, property.Name, value))
				liveInspectorEdits[property.Name] = value?.DeepClone();
			return;
		}
		if (specId != null)
		{
			BeginPrefabUndo(specId);
			ActorSpecs.SetProperty(specId, property.JsonName, value);
		}
		else if (room != null && continuous)
		{
			OpenActorPropertyUndo(room, actor);
			ActorSpecs.SetProperty(actor, property.JsonName, value);
		}
		else if (room != null)
		{
			using (CaptureRoomUndo("Edit Actor Property", room))
				ActorSpecs.SetProperty(actor, property.JsonName, value);
		}
	}

	void SetActorProperty(string? specId, Room? room, JsonObject actor, ActorSpecs.EditableProperty property, JsonNode? value, string undoName)
	{
		if (livePropertyTarget != null)
		{
			if (ActorSpecs.SetEditableProperty(livePropertyTarget, property.Name, value))
				liveInspectorEdits[property.Name] = value?.DeepClone();
			return;
		}
		if (specId != null)
		{
			using (CapturePrefabUndo(undoName, specId))
				ActorSpecs.SetProperty(specId, property.JsonName, value);
		}
		else if (room != null)
		{
			using (CaptureRoomUndo(undoName, room))
				ActorSpecs.SetProperty(actor, property.JsonName, value);
		}
	}

	void SetActorEnumProperty(string? specId, Room? room, JsonObject actor, ActorSpecs.EditableProperty property, string value)
	{
		if (livePropertyTarget != null)
		{
			var node = JsonValue.Create(value);
			if (ActorSpecs.SetEditableProperty(livePropertyTarget, property.Name, node))
				liveInspectorEdits[property.Name] = node;
			return;
		}
		if (specId != null)
		{
			BeginPrefabUndo(specId);
			ActorSpecs.SetProperty(specId, property.JsonName, JsonValue.Create(value));
		}
		else if (room != null)
		{
			using (CaptureRoomUndo("Edit Actor Property", room))
			{
				ActorSpecs.SetProperty(actor, property.JsonName, JsonValue.Create(value));
				EditorFor(actor)?.PropertyChanged(new EditorActor(room, actor), property.Name);
			}
		}
	}

	void ResetActorProperty(string? specId, Room? room, JsonObject actor, ActorSpecs.EditableProperty property)
	{
		if (livePropertyTarget != null)
		{
			if (liveInspectorOriginalValues.TryGetValue(property.Name, out var original))
				ActorSpecs.SetEditableProperty(livePropertyTarget, property.Name, original);
			liveInspectorEdits.Remove(property.Name);
			return;
		}
		if (specId != null)
		{
			using (CapturePrefabUndo("Reset Prefab Property", specId))
				ActorSpecs.ResetProperty(specId, property.JsonName);
		}
		else if (room != null)
		{
			using (CaptureRoomUndo("Reset Actor Property", room))
				ActorSpecs.ResetProperty(actor, property.JsonName);
		}
	}

	void ActorInstanceInspectorImGui()
	{
		if (CurrentRoom is not { } room || selectedRoomActorIndex < 0 || selectedRoomActorIndex >= room.Actors.Count)
			return;

		var actor = room.Actors[selectedRoomActorIndex];
		var title = ActorSpecs.ReadString(actor, "From") ?? ActorSpecs.ReadString(actor, "Type") ?? "Actor";
		var windowTitle = $"Actor: {title}";
		var inspectorPadding = Vector2.One * 16;
		var window = RequiredWindow;
		var inspectorPos = new Vector2(window.WidthInPixels / imgui.Scale, window.HeightInPixels / imgui.Scale) - inspectorPadding;
		SetNextWindowPos(inspectorPos, ImGuiCond.FirstUseEver, Vector2.One);
		SetNextWindowSize(new Vector2(260, 0), ImGuiCond.FirstUseEver);
		var open = true;
		if (!Begin($"{windowTitle}###ActorInstanceInspector", ref open))
		{
			if (!open)
				selectedRoomActorIndex = -1;
			End();
			return;
		}
		var actorId = ActorSpecs.GetActorId(actor);
		var changed = false;
		if (IsWindowDocked())
			DrawDockedInspectorTitle(windowTitle, actorId, () => {
				changed |= DrawRandomizeActorPropertiesButton(null, room, actor);
			});
		else if (actorId is not null)
			DrawActorIdInWindowTitle(windowTitle, actorId);

		//changed |= DrawRandomizeActorPropertiesButton(null, room, actor);
		if (!open)
		{
			selectedRoomActorIndex = -1;
			End();
			return;
		}

		actorPropertyScratch.Clear();
		actorPropertyScratch.AddRange(ActorSpecs.GetEditableProperties(actor));
		BeginMidiActorInspector($"actor:{room.Id}:{actorId}");

		var inspectorAction = false;
		if (actorId is not null && game?.GetActorById(actorId) is RigPuppet)
		{
			if (Button("Rig Animations"))
			{
				rigAnimationViewerInitialized = true;
				rigAnimationViewerOpen = true;
			}
			if (IsItemHovered())
				SetTooltip("Open the Rig Animations window for this puppet.");
			inspectorAction = true;
		}
		if (ActorSpecs.ReadString(actor, "From") is { } specId && ActorSpecs.Get(specId) != null)
		{
			if (inspectorAction)
				SameLine();
			if (Button(EditorIcons.Cube + "##JumpToPrefab"))
			{
				selectedActorSpecId = specId;
				actorSpecInspectorOpen = true;
			}
			if (IsItemHovered())
				SetTooltip("Jump to Prefab.");
			inspectorAction = true;
		}
		if (ActorZedLauncher.ActorSourceFilename(actor) is { } sourceFilename)
		{
			if (inspectorAction)
				SameLine();
			if (Button(EditorIcons.Code + "##OpenActorSource"))
				ActorZedLauncher.OpenActor(actor);
			if (IsItemHovered())
				SetTooltip($"Open {sourceFilename}");
			inspectorAction = true;
		}
		// Locking is one bit wanted on every actor, so it's an icon here rather than an editorFlags
		// dropdown costing a row in every inspector (see HideInactiveActorProperty).
		{
			if (inspectorAction)
				SameLine();
			var locked = RoomActorIsLocked(actor);
			if (Button((locked ? EditorIcons.Lock : EditorIcons.Unlock) + "##ToggleActorLock"))
			{
				using (CaptureRoomUndo(locked ? "Unlock Actor" : "Lock Actor", room))
					ActorSpecs.SetProperty(actor, nameof(Actor.editorFlags), JsonValue.Create(
						(locked ? Actor.EditorFlags.None : Actor.EditorFlags.Locked).ToString()));
				changed = true;
			}
			if (IsItemHovered())
				SetTooltip(locked
					? "Locked: can't be picked or dragged in the room. Click to unlock."
					: "Unlocked. Click to lock it against picking and dragging.");
			inspectorAction = true;
		}
		// Facing is one bit too, and its arrow says which way without needing a label or a row.
		if (FindActorProperty(nameof(Actor.Facing)) is { } facing)
		{
			if (inspectorAction)
				SameLine();
			var value = ActorSpecs.ReadSigns(facing.Value);
			var left = value.AsInt < 0;
			if (Button((left ? EditorIcons.ArrowLeft : EditorIcons.ArrowRight) + "##ToggleFacing"))
			{
				SetActorProperty(null, room, actor, facing, JsonValue.Create((-value).ToString()), continuous: false);
				changed = true;
			}
			if (IsItemHovered())
				SetTooltip(left ? "Facing left. Click to face right." : "Facing right. Click to face left.");
			inspectorAction = true;
		}
		// Inversion is three states, so one button cycles them. Only shown where it can do anything:
		// a room the burrow doesn't flip never has an inverted version for the actor to be absent from.
		if (room.BurrowInverts && FindActorProperty(nameof(Actor.Invert)) is { } invert)
		{
			if (inspectorAction)
				SameLine();
			if (!Enum.TryParse<ActorInversion>(ActorSpecs.ReadStringValue(invert.Value), true, out var inversion))
				inversion = ActorInversion.Both;
			var (icon, tooltip, next) = inversion switch
			{
				ActorInversion.UninvertedOnly =>
					(EditorIcons.Sun, "Uninverted world only. Click for inverted only.", ActorInversion.InvertedOnly),
				ActorInversion.InvertedOnly =>
					(EditorIcons.Moon, "Inverted world only. Click for both.", ActorInversion.Both),
				_ =>
					(EditorIcons.CircleHalfStroke, "In both worlds. Click for uninverted only.", ActorInversion.UninvertedOnly),
			};
			if (Button(icon + "##CycleInversion"))
			{
				SetActorEnumProperty(null, room, actor, invert, next.ToString());
				changed = true;
			}
			if (IsItemHovered())
				SetTooltip(tooltip);
			inspectorAction = true;
		}
		if (inspectorAction)
			Separator();

		string? propertyGroup = null;
		var propertyGroupOpen = true;
		foreach (var property in actorPropertyScratch)
		{
			if (HideInactiveActorProperty(actor, property))
				continue;
			if (!PropertyGroupVisible(property, ref propertyGroup, ref propertyGroupOpen))
				continue;
			NoteMidiActorProperty(property);
			PushID(property.JsonName);
			changed |= DrawActorInstanceProperty(room, actor, property);
			PopID();
		}

		if (changed)
		{
			editorGameDirty = true;
			SyncEditorGame();
		}

		// Edit the shared effect asset directly. This mutates the live asset (not
		// the room), so it doesn't trigger SyncEditorGame - the selected emitter
		// reflects changes immediately, and edits autosave + are undoable.
		if (IsParticleEmitterActor(actor))
			particleEditor.DrawActorInspectorBox(game, actor);

		End();
	}

	void LiveActorInspectorImGui()
	{
		if (!playing || !liveActorInspectorOpen || liveInspectedActorId == null || game?.GetActorById(liveInspectedActorId) is not { } runtime ||
			game.destroying.Contains(runtime) || game.CurrentRoom is not { } runtimeRoom ||
			!TryGetLiveActorSource(game, runtimeRoom, runtime, positionEditableOnly: false, out var room, out var authored))
			return;

		var title = $"Live: {runtime.GetType().Name}";
		SetNextWindowSize(new Vector2(300, 0), ImGuiCond.FirstUseEver);
		var open = liveActorInspectorOpen;
		if (!Begin($"{title}###LiveActorInspector", ref open))
		{
			liveActorInspectorOpen = open;
			End();
			return;
		}
		liveActorInspectorOpen = open;
		if (!open)
		{
			End();
			return;
		}

		if (IsWindowDocked())
			DrawDockedInspectorTitle(title, runtime.ActorId);
		else if (runtime.ActorId != null)
			DrawActorIdInWindowTitle(title, runtime.ActorId);

		Separator();

		actorPropertyScratch.Clear();
		actorPropertyScratch.AddRange(ActorSpecs.GetEditableProperties(runtime));
		BeginMidiActorInspector($"live:{runtime.ActorId}");
		livePropertyTarget = runtime;
		string? propertyGroup = null;
		var propertyGroupOpen = true;
		foreach (var source in actorPropertyScratch)
		{
			var property = source with
			{
				HasLocalValue = liveInspectorEdits.ContainsKey(source.Name),
				ResetValue = liveInspectorOriginalValues.GetValueOrDefault(source.Name)?.DeepClone()
			};
			if (HideInactiveActorProperty(authored, property))
				continue;
			if (!PropertyGroupVisible(property, ref propertyGroup, ref propertyGroupOpen))
				continue;
			NoteMidiActorProperty(property);
			PushID(property.JsonName);
			DrawActorProperty(null, null, authored, property);
			PopID();
		}
		livePropertyTarget = null;

		Separator();
		BeginDisabled(liveInspectorEdits.Count == 0);
		if (Button("Apply to Room"))
			ApplyLiveInspectorEdits(runtime, room, authored);
		SameLine();
		if (Button("Reset Live Changes"))
			ResetLiveInspectorEdits(runtime);
		EndDisabled();
		End();
	}

	static bool PropertyGroupVisible(ActorSpecs.EditableProperty property, ref string? currentGroup, ref bool open)
	{
		if (property.Group == currentGroup)
			return open;
		currentGroup = property.Group;
		PushID(property.JsonName);
		open = currentGroup == null || CollapsingHeader(currentGroup);
		PopID();
		return open;
	}

	void ApplyLiveInspectorEdits(Actor runtime, Room room, JsonObject authored)
	{
		using (CaptureRoomUndo("Apply Live Actor Properties", room))
			foreach (var (name, value) in liveInspectorEdits)
				ActorSpecs.SetProperty(authored, name, value);
		room.Save();
		MarkRoomPreviewDirty(room);
		editorGameDirty = true;
		if (runtime.ActorId is { } actorId)
			ForgetLiveEditRecord(room.Id, actorId);
		CaptureLiveInspectorValues(runtime);
		DisplayMessage($"Applied live {runtime.GetType().Name} properties");
	}

	void ResetLiveInspectorEdits(Actor runtime)
	{
		foreach (var (name, _) in liveInspectorEdits)
			if (liveInspectorOriginalValues.TryGetValue(name, out var original))
				ActorSpecs.SetEditableProperty(runtime, name, original);
		liveInspectorEdits.Clear();
		if (runtime.ActorId is { } actorId && game?.CurrentRoom is { } runtimeRoom &&
			TryGetLiveActorSource(game, runtimeRoom, runtime, positionEditableOnly: false, out var room, out _))
			ForgetLiveEditRecord(room.Id, actorId);
	}

	static void DrawDockedInspectorTitle(string title, string? detail = null, Action? sameLineCallback = null)
	{
		AlignTextToFramePadding();
		TextUnformatted(title);
		if (detail is not null)
		{
			SameLine(0, GetStyle().ItemInnerSpacing.X);
			var textColor = GetStyle().Colors[(int)ImGuiCol.Text];
			textColor.W *= 0.5f;
			PushStyleColor(ImGuiCol.Text, textColor);
			TextUnformatted(detail);
			PopStyleColor();
		}
		if (sameLineCallback != null) {
			SameLine(0, GetStyle().ItemInnerSpacing.X);
			sameLineCallback();
		}
		Separator();
	}

	static void DrawActorIdInWindowTitle(string title, string actorId)
	{
		var style = GetStyle();
		var windowPos = GetWindowPos();
		var windowSize = GetWindowSize();
		var titleSize = CalcTextSize(title);
		var titleHeight = GetFrameHeight();
		var titleStartX = windowPos.X + style.WindowBorderSize + style.FramePadding.X +
			GetFontSize() + style.ItemInnerSpacing.X * 2;
		var textColor = style.Colors[(int)ImGuiCol.Text];
		textColor.W *= 0.5f;
		var position = new Vector2(
			titleStartX + titleSize.X + style.ItemInnerSpacing.X,
			windowPos.Y + (titleHeight - titleSize.Y) * 0.5f);
		var draw = GetWindowDrawList();
		draw.PushClipRect(windowPos, windowPos + new Vector2(windowSize.X - titleHeight, titleHeight), false);
		draw.AddText(position, ColorConvertFloat4ToU32(textColor), actorId);
		draw.PopClipRect();
	}

	ActorSpecs.EditableProperty? FindActorProperty(string name)
	{
		foreach (var property in actorPropertyScratch)
			if (property.Name == name)
				return property;
		return null;
	}

	bool HideInactiveActorProperty(JsonObject actor, ActorSpecs.EditableProperty property)
	{
		// Both are single bits edited by icons in the inspector header, so they don't need a row.
		if (property.Name is nameof(Actor.editorFlags) or nameof(Actor.Facing) or nameof(Actor.Invert))
			return true;
		if (property.VisibleWhen is { } visibleWhen && !visibleWhen.IsVisibleForActor(actor))
			return true;
		if (ActorSpecs.GetTypeName(actor) == nameof(CurveRibbon) &&
			Assets.GetRibbonStyle(ActorSpecs.ReadResolvedString(actor, nameof(CurveRibbon.Style)) ?? CurveRibbon.DefaultStyle.Name) is { } ribbonStyle &&
			(!ribbonStyle.Taper && property.Name == nameof(CurveRibbon.EndWidth) || !ribbonStyle.Branches &&
				property.Name is nameof(CurveRibbon.BranchSpacing) or nameof(CurveRibbon.BranchLength) or nameof(CurveRibbon.BranchWidth)))
			return true;

		var typeName = ActorSpecs.GetTypeName(actor);
		if (typeName is nameof(RigPuppet) or nameof(RigPuppetProp) or nameof(Stalker))
		{
			var mode = RigSkinMode.NCA;
			for (var i = 0; i < actorPropertyScratch.Count; i++)
				if (actorPropertyScratch[i].Name == nameof(HumanoidRigSettings.SkinMode))
				{
					Enum.TryParse(ActorSpecs.ReadStringValue(actorPropertyScratch[i].Value), true, out mode);
					break;
				}
			if (property.Name == nameof(HumanoidRigSettings.SurfaceId))
				return mode != RigSkinMode.Surface;
			if (property.Name.StartsWith("Neural", StringComparison.Ordinal) ||
				property.Name.StartsWith("RigNeural", StringComparison.Ordinal))
				return mode != RigSkinMode.NCA;
		}

		if (typeName == nameof(RigPuppetProp) && property.Name is
			nameof(RigPuppet.IdleAnimationName) or nameof(RigPuppet.WalkAnimationName) or
			nameof(RigPuppet.Performance) or nameof(RigPuppet.PlaybackSpeed) or nameof(RigPuppet.SyncGaitToSpeed) or
			nameof(RigPuppet.StepUp) or nameof(RigPuppet.StepDown) or nameof(RigPuppet.Health) or
			nameof(RigPuppet.ArticulatedCombatBounds) or nameof(RigPuppet.StraddlesPlayerDepth))
			return true;
		if (typeName == nameof(RigPuppetProp) && property.Name == nameof(RigPuppetProp.JostleStrength) &&
			ActorSpecs.GetResolvedProperty(actor, nameof(RigPuppetProp.Reactive))?.GetValue<bool>() != true)
			return true;

		if (typeName != nameof(Light))
			return false;

		// The Cookie* knobs only mean something once a cookie is chosen; the Tint* ones only
		// once the sprite is being tinted. Both gates leave the property they key off visible.
		if (property.Name != nameof(Light.Cookie) &&
			property.Name.StartsWith(nameof(Light.Cookie), StringComparison.Ordinal))
			return !Enum.TryParse<LightCookie>(ActorSpecs.GetResolvedProperty(actor, nameof(Light.Cookie))?.GetValue<string>(), true, out var cookie) ||
				cookie == LightCookie.None;

		if (property.Name != nameof(Light.TintSprite) &&
			property.Name.StartsWith("Tint", StringComparison.Ordinal))
			return ActorSpecs.GetResolvedProperty(actor, nameof(Light.TintSprite))?.GetValue<bool>() != true;

		return false;
	}

	bool DrawActorInstanceProperty(Room room, JsonObject actor, ActorSpecs.EditableProperty property)
	{
		if (property.Name == nameof(LinkedDoor.TargetActorId) && IsLinkedDoorActor(actor))
			return DrawLinkedDoorTargetProperty(room, actor, property);
		if (property.Name == nameof(ParticleEmitter.EffectName) && IsParticleEmitterActor(actor))
			return DrawParticleEffectNameProperty(room, actor, property);

		return DrawActorProperty(null, room, actor, property);
	}

	static bool DrawCardinalCombo(ref Cardinal value)
	{
		var changed = false;
		if (BeginCombo("##Value", value.ToString()))
		{
			for (var raw = Cardinal.RightValue; raw <= Cardinal.UpValue; raw++)
			{
				var direction = Cardinal.FromRawValue(raw);
				var selected = value == direction;
				if (Selectable(direction.ToString(), selected))
				{
					value = direction;
					changed = true;
				}
				if (selected)
					SetItemDefaultFocus();
			}
			EndCombo();
		}
		return changed;
	}


	static bool IsParticleEmitterActor(JsonObject actor)
		=> string.Equals(ActorSpecs.ReadString(actor, "Type"), "ParticleEmitter", StringComparison.OrdinalIgnoreCase);

	bool DrawParticleEffectNameProperty(Room room, JsonObject actor, ActorSpecs.EditableProperty property)
	{
		var changed = false;
		var row = BeginPropertyRow(property.DisplayName, property.HasLocalValue);
		PushID("Value");

		var current = ActorSpecs.ReadStringValue(property.Value) ?? string.Empty;
		if (BeginCombo("##Value", current))
		{
			foreach (var name in ParticleEffectAssets.Names)
				if (Selectable(name, name == current))
				{
					using (CaptureRoomUndo("Edit Actor Property", room))
						ActorSpecs.SetProperty(actor, property.JsonName, JsonValue.Create(name));
					changed = true;
				}
			EndCombo();
		}

		PopID();
		if (property.HasLocalValue)
		{
			SameLine();
			if (DrawPropertyResetButton(property))
			{
				ResetActorProperty(null, room, actor, property);
				changed = true;
			}
		}
		return changed;
	}

	bool DrawLinkedDoorTargetProperty(Room room, JsonObject actor, ActorSpecs.EditableProperty property)
	{
		var changed = false;
		var row = BeginPropertyRow(property.DisplayName, property.HasLocalValue);
		var value = ActorSpecs.ReadStringValue(property.Value) ?? string.Empty;
		var target = Assets.FindActorById(value);
		var sourceId = ActorSpecs.ReadString(actor, "Id") ?? "";
		var picking = !string.IsNullOrWhiteSpace(sourceId) &&
					  string.Equals(pickingLinkedDoorTargetForActorId, sourceId, StringComparison.OrdinalIgnoreCase);

		BeginGroup();
		InputText("##Value", ref value, 128, ImGuiInputTextFlags.ReadOnly);

		if (target is { } found)
			TextDisabled($"{found.Room.Id}");
		else if (!string.IsNullOrWhiteSpace(value))
			TextColored(new Vector4(1.0f, 0.35f, 0.25f, 1.0f), "Missing target");
		else
			TextDisabled("No target");

		if (SmallButton(picking ? "Cancel Pick" : "Pick"))
			pickingLinkedDoorTargetForActorId = picking ? null : sourceId;

		SameLine();
		if (SmallButton("Clear"))
		{
			using (CaptureRoomUndo("Clear Linked Door Target", room))
				ActorSpecs.SetProperty(actor, property.JsonName, JsonValue.Create(""));
			if (picking)
				pickingLinkedDoorTargetForActorId = null;
			changed = true;
		}

		if (target is { } jumpTarget)
		{
			SameLine();
			if (SmallButton("Jump"))
			{
				CurrentRoom?.Save();
				SelectRoom(jumpTarget.Room);
				selectedRoomActorIndex = jumpTarget.Room.Actors.IndexOf(jumpTarget.Actor);
				SyncEditorGame();
				TweenEditorCameraToRoom(jumpTarget.Room);
			}
		}
		EndGroup();

		if (property.HasLocalValue)
		{
			SameLine();
			if (DrawPropertyResetButton(property))
			{
				using (CaptureRoomUndo("Reset Actor Property", room))
					ActorSpecs.ResetProperty(actor, property.JsonName);
				changed = true;
			}
		}

		return changed;
	}

	// [ActorRef]: pick another actor in the room by type and id rather than typing its id.
	// A picked actor that has no id yet gets one, so anything placed is linkable.
	bool DrawActorRefProperty(string? specId, Room room, JsonObject actor, ActorSpecs.EditableProperty property, ActorRefAttribute actorRef, string value)
	{
		var changed = false;
		var target = Assets.FindActorById(value);
		var label = target is { } found
			? $"{ActorSpecs.GetTypeName(found.Actor)} ({ShortActorId(value)})"
			: string.IsNullOrWhiteSpace(value) ? "(none)" : $"Missing ({ShortActorId(value)})";

		if (!BeginCombo("##Value", label))
			return false;

		if (Selectable("(none)", string.IsNullOrWhiteSpace(value)))
		{
			SetActorProperty(specId, room, actor, property, JsonValue.Create(""), "Clear Actor Reference");
			changed = true;
		}

		for (var i = 0; i < room.Actors.Count; i++)
		{
			var candidate = room.Actors[i];
			if (candidate == actor || !MatchesActorRefKind(candidate, actorRef.Kind))
				continue;

			var candidateId = ActorSpecs.GetActorId(candidate);
			var typeName = ActorSpecs.GetTypeName(candidate) ?? "Actor";
			var selected = !string.IsNullOrWhiteSpace(candidateId) &&
				string.Equals(value, candidateId, StringComparison.OrdinalIgnoreCase);
			var candidateLabel = string.IsNullOrWhiteSpace(candidateId) ? typeName : $"{typeName} ({ShortActorId(candidateId)})";
			if (Selectable($"{candidateLabel}##{i}", selected))
			{
				using (CaptureRoomUndo("Set Actor Reference", room))
				{
					ActorSpecs.EnsureActorId(candidate);
					ActorSpecs.SetProperty(actor, property.JsonName, JsonValue.Create(ActorSpecs.GetActorId(candidate)));
					if (property.JsonName == nameof(Actor.AttachedToActorId))
					{
						var actorPosition = ActorSpecs.ReadPoint(actor, "Position") ?? Point2.Zero;
						var targetPosition = ActorSpecs.ReadPoint(candidate, "Position") ?? Point2.Zero;
						ActorSpecs.SetProperty(actor, nameof(Actor.AttachmentOffset), ActorSpecs.ToJsonNode(actorPosition - targetPosition));
					}
				}
				changed = true;
			}
			if (selected)
				SetItemDefaultFocus();
		}
		EndCombo();

		return changed;
	}

	static string ShortActorId(string? id)
		=> string.IsNullOrEmpty(id) ? "" : id[..Math.Min(8, id.Length)];

	static bool MatchesActorRefKind(JsonObject candidate, Type? kind)
		=> kind == null || ActorTypesAssignableTo(kind).Contains(ActorSpecs.GetTypeName(candidate) ?? "");

	static bool DrawSpriteCombo(ref string value)
	{
		var changed = false;
		if (BeginCombo("##Value", value))
		{
			foreach (var name in Assets.Sprites.Keys)
			{
				var selected = string.Equals(value, name, StringComparison.OrdinalIgnoreCase);
				if (Selectable(name, selected))
				{
					value = name;
					changed = true;
				}
				if (selected)
					SetItemDefaultFocus();
			}
			EndCombo();
		}
		return changed;
	}

	bool DrawRigAnimationCombo(ref string value)
	{
		var changed = false;
		var clips = RigLibrary.Get("ual2").Clips;
		if (BeginCombo("##Value", value))
		{
			if (IsWindowAppearing())
			{
				rigAnimationPickerSearch = "";
				SetKeyboardFocusHere();
			}
			SetNextItemWidth(-1);
			InputTextWithHint("##Search", "Search clips", ref rigAnimationPickerSearch, 128);
			Separator();
			for (var i = 0; i < clips.Count; i++)
			{
				var clip = clips[i];
				if (rigAnimationPickerSearch.Length > 0 &&
					!clip.Name.Contains(rigAnimationPickerSearch, StringComparison.OrdinalIgnoreCase))
					continue;

				var selected = string.Equals(value, clip.Name, StringComparison.OrdinalIgnoreCase);
				if (Selectable(clip.Name, selected))
				{
					value = clip.Name;
					changed = true;
				}
				if (IsItemHovered())
					SetTooltip($"{clip.Duration:0.###} s");
				if (selected)
					SetItemDefaultFocus();
			}
			EndCombo();
		}
		return changed;
	}

	void DrawMaskPaintControls(Room room, JsonObject actor)
	{
		if (ActorSpecs.ReadString(actor, "Id") is not { } actorId)
		{
			TextDisabled("(actor has no id)");
			return;
		}

		var painting = maskPaintActorId == actorId;
		if (Button(painting ? "Stop Painting" : "Paint Mask"))
			ToggleMaskPainting(actorId);
	}

	void DrawPaintOverlay()
	{
		if (CurrentRoom is not { } room)
			return;

		JsonObject? maskActor = null;
		if (maskPaintActorId != null)
			foreach (var actor in room.Actors)
				if (ActorSpecs.ReadString(actor, "Id") == maskPaintActorId)
				{
					maskActor = actor;
					break;
				}

		var colorPainting = roomColorFieldPainter.IsActive(room);
		if (!colorPainting && maskActor == null)
			return;

		var flags =
			ImGuiWindowFlags.NoDecoration |
			ImGuiWindowFlags.AlwaysAutoResize |
			ImGuiWindowFlags.NoSavedSettings |
			ImGuiWindowFlags.NoMove |
			ImGuiWindowFlags.NoFocusOnAppearing;
		SetNextWindowPos(GameViewRect.TopLeft + new Vector2(10, EditorMenuButtonSize + 12), ImGuiCond.Always);
		PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(12, 10));
		PushStyleVar(ImGuiStyleVar.WindowBorderSize, 2);
		PushStyleVar(ImGuiStyleVar.WindowRounding, 2);
		PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(7, 5));
		PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.12f, 0.015f, 0.08f, 0.96f));
		PushStyleColor(ImGuiCol.Border, new Vector4(1.0f, 0.24f, 0.55f, 1.0f));
		Begin("Painting Controls", flags);
		TextColored(new Vector4(1.0f, 0.4f, 0.65f, 1.0f),
			colorPainting ? "ROOM COLOR PAINTING ACTIVE" : "NEURAL MASK PAINTING ACTIVE");
		Separator();
		PushItemWidth(220);
		if (colorPainting)
			DrawRoomColorPaintOverlay(room);
		else
			DrawMaskPaintOverlay(room, maskActor!);
		PopItemWidth();
		End();
		PopStyleColor(2);
		PopStyleVar(4);
	}

	void DrawRoomColorPaintOverlay(Room room)
	{
		if (Button("Stop Painting"))
		{
			EndRoomColorFieldPainting();
			return;
		}

		roomColorFieldPainter.DrawBrushControls(out var clear);
		if (clear)
		{
			using (CaptureRoomUndo($"Clear {roomColorFieldPainter.Layer} Color Field", room))
				roomColorFieldPainter.Field(room).Clear();
			SyncRoomColorFieldPreview(room);
			room.Save();
		}
		TextDisabled("Drag in the room to paint. Wheel resizes.\nRight-drag erases. P or Esc stops.");
	}

	void DrawMaskPaintOverlay(Room room, JsonObject actor)
	{
		var actorId = maskPaintActorId!;
		if (Button("Stop Painting"))
		{
			EndMaskPainting();
			return;
		}
		Button("Hold to Show Mask");
		maskPaintPreviewHeld = IsItemActive();

		// Which material you're painting with reads far better as its own swatch than as a filename.
		var swatch = new Vector2(ButtonSize, ButtonSize);
		var painted = 0;
		for (var slot = 0; slot < 4; slot++)
		{
			var file = ActorSpecs.ReadString(actor, slot switch
			{
				0 => nameof(NeuralTextureActor.WeightsFile),
				1 => nameof(NeuralTextureActor.WeightsFile1),
				2 => nameof(NeuralTextureActor.WeightsFile2),
				_ => nameof(NeuralTextureActor.WeightsFile3),
			});
			if (slot > 0 && string.IsNullOrEmpty(file))
				continue;
			var name = string.IsNullOrEmpty(file)
				? "(default)"
				: Path.GetFileNameWithoutExtension(file)!.Replace('_', ' ');
			if (painted++ > 0)
				SameLine();
			var selected = maskPaintSlot == slot;
			var picked = file != null && GetNeuralModelPreview(file) is { } slotSwatch
				? ImGuiSubtextureButton($"##MaskSlot{slot}", slotSwatch, name, selected, swatch, true, 0f)
				: ToggleButton($"{slot + 1}##MaskSlot{slot}", selected, swatch, name);
			if (picked)
				maskPaintSlot = slot;
		}
		SameLine();
		if (ToggleButton("Erase##MaskSlot", maskPaintSlot < 0, swatch, "Eraser"))
			maskPaintSlot = -1;

		SliderFloat("Radius", ref maskPaintRadius, 2.0f, 96.0f);
		SliderFloat("Softness", ref maskPaintSoftness, 0.05f, 1.0f);
		SliderFloat("Strength", ref maskPaintStrength, 0.05f, 1.0f);
		SliderInt("Preview Steps", ref maskPaintPreviewSteps, 0, 256);
		if (Button("Resimulate") && FindLiveNeuralTextureActor(actorId) is { } preview)
		{
			preview.SimulateFromSeed(maskPaintPreviewSteps);
			MarkRoomPreviewDirty(room);
		}

		if (Button("Clear Mask"))
		{
			using (CaptureRoomUndo("Clear Neural Texture Mask", room))
				ActorSpecs.SetProperty(actor, nameof(NeuralTextureActor.PaintedMask), JsonValue.Create(""));
			if (FindLiveNeuralTextureActor(actorId) is { } live)
			{
				live.PaintedMask = "";
				live.SimulateFromSeed(maskPaintPreviewSteps);
			}
			MarkRoomPreviewDirty(room);
		}
		TextDisabled("Drag in the room to paint. Release to resimulate.\nRight-drag erases. P or Esc stops.");
	}

	static int NeuralTextureWeightsSlot(JsonObject actor, ActorSpecs.EditableProperty property)
	{
		if (IsNeuralTextureActor(actor))
			return property.Name switch
			{
				nameof(NeuralTextureActor.WeightsFile) => 0,
				nameof(NeuralTextureActor.WeightsFile1) => 1,
				nameof(NeuralTextureActor.WeightsFile2) => 2,
				nameof(NeuralTextureActor.WeightsFile3) => 3,
				_ => -1,
			};
		return property.Type == typeof(ContentRef<NeuralWeightsContent>) ? 0 : -1;
	}

	static bool IsRevealDoorTileTypeProperty(JsonObject actor, ActorSpecs.EditableProperty property)
		=> IsRevealDoorActor(actor) && property.Type == typeof(string) && property.JsonName == nameof(RevealDoor.TileType);

	bool DrawRevealDoorTileTypeButtons(ref string value, float controlWidth)
	{
		var style = GetStyle();
		var size = MathF.Min(ButtonSize, controlWidth);
		var columns = Math.Max(1, (int)((controlWidth + style.ItemSpacing.X) / (size + style.ItemSpacing.X)));
		var index = 0;
		var changed = false;
		var auto = string.IsNullOrWhiteSpace(value);
		if (auto)
			PushStyleColor(ImGuiCol.Button, style.Colors[(int)ImGuiCol.ButtonActive]);
		if (Button("Auto##RevealDoorTileType", new Vector2(size, size)) && !auto)
		{
			value = string.Empty;
			changed = true;
		}
		if (auto)
			PopStyleColor();
		if (IsItemHovered())
			SetTooltip("Infer the cover material from nearby solid tiles");
		index++;

		foreach (var (tile, entry) in Factory.Entries)
		{
			if (entry.Layer != Factory.TileLayer.Foreground || entry.DualTilesetName == null)
				continue;
			if (index % columns != 0)
				SameLine();

			var selected = RevealDoorTileTypeMatches(value, tile, entry);
			if (ImGuiSubtextureButton(entry.Name, entry.Image, entry.Name, selected, new Vector2(size, size)) && !selected)
			{
				value = entry.Name;
				changed = true;
			}
			index++;
		}

		return changed;
	}

	static bool RevealDoorTileTypeMatches(string value, char tile, Factory.Entry entry)
	{
		if ((value.Length == 1 && value[0] == tile) || string.Equals(value, entry.Name, StringComparison.OrdinalIgnoreCase))
			return true;
		if (entry.DualTilesetName is not { } dual)
			return false;
		if (string.Equals(value, dual, StringComparison.OrdinalIgnoreCase))
			return true;
		const string suffix = "-dual";
		return dual.Length == value.Length + suffix.Length && dual.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) &&
			dual.AsSpan(0, value.Length).Equals(value, StringComparison.OrdinalIgnoreCase);
	}

	static bool IsDualMaterialProperty(ActorSpecs.EditableProperty property)
		=> property.Name == nameof(DualGridRect.Material) && typeof(DualGridRect).IsAssignableFrom(property.DeclaringType);

	static bool IsActorSurfaceProperty(ActorSpecs.EditableProperty property)
		=> property.Type == typeof(ContentRef<SurfaceContent>);

	enum ContentPickerPreviewKind { None, RoomSurface, ActorProperty, NeuralModel }
	ContentPickerPreviewKind contentPickerPreviewKind;
	string contentPickerPreviewRoomId = "";
	string contentPickerPreviewActorId = "";
	string contentPickerPreviewProperty = "";
	string contentPickerPreviewOriginal = "";
	string contentPickerPreviewValue = "";
	int contentPickerPreviewSlot = -1;
	int contentPickerPreviewFrame = -1;

	bool BeginContentPickerPreview(ContentPickerPreviewKind kind, Room room, string actorId, string property, int slot,
		string original, string? preview)
	{
		var sameTarget = contentPickerPreviewKind == kind && contentPickerPreviewRoomId == room.Id &&
			contentPickerPreviewActorId == actorId && contentPickerPreviewProperty == property &&
			contentPickerPreviewSlot == slot;
		if (preview == null)
		{
			if (sameTarget)
				ClearContentPickerPreview();
			return false;
		}
		if (!sameTarget)
			ClearContentPickerPreview();
		if (string.Equals(preview, original, StringComparison.OrdinalIgnoreCase))
		{
			if (sameTarget)
				ClearContentPickerPreview();
			return false;
		}
		if (sameTarget && string.Equals(preview, contentPickerPreviewValue, StringComparison.OrdinalIgnoreCase))
		{
			contentPickerPreviewFrame = GetFrameCount();
			return false;
		}

		contentPickerPreviewKind = kind;
		contentPickerPreviewRoomId = room.Id;
		contentPickerPreviewActorId = actorId;
		contentPickerPreviewProperty = property;
		contentPickerPreviewOriginal = original;
		contentPickerPreviewValue = preview;
		contentPickerPreviewSlot = slot;
		contentPickerPreviewFrame = GetFrameCount();
		return true;
	}

	void PreviewRoomSurface(Room room, string original, string? preview)
	{
		if (game is not { IsEditorPreview: true, CurrentRoom: { } runtimeRoom } || runtimeRoom.Id != room.Id ||
			!BeginContentPickerPreview(ContentPickerPreviewKind.RoomSurface, room, "", "", -1, original, preview))
			return;
		runtimeRoom.BackdropSurfaceId = preview!;
	}

	void PreviewActorProperty(Room room, JsonObject actor, string property, string original, string? preview)
	{
		if (ActorSpecs.GetActorId(actor) is not { } actorId ||
			game is not { IsEditorPreview: true, CurrentRoom: { } runtimeRoom } || runtimeRoom.Id != room.Id ||
			game.GetActorById(actorId) is not { } runtime ||
			!BeginContentPickerPreview(ContentPickerPreviewKind.ActorProperty, room, actorId, property, -1, original, preview))
			return;
		ActorSpecs.SetEditableProperty(runtime, property, JsonValue.Create(preview));
	}

	void PreviewNeuralModel(Room room, JsonObject actor, int slot, string original, string? preview)
	{
		if (ActorSpecs.GetActorId(actor) is not { } actorId ||
			game is not { IsEditorPreview: true, CurrentRoom: { } runtimeRoom } || runtimeRoom.Id != room.Id ||
			game.GetActorById<NeuralTextureActor>(actorId) is not { } texture ||
			!BeginContentPickerPreview(ContentPickerPreviewKind.NeuralModel, room, actorId, "", slot, original, preview))
			return;
		texture.SetEditorPreviewModel(slot, preview!);
	}

	void ClearContentPickerPreview(bool restore = true)
	{
		if (restore && game is { IsEditorPreview: true, CurrentRoom: { } room } && room.Id == contentPickerPreviewRoomId)
			switch (contentPickerPreviewKind)
			{
				case ContentPickerPreviewKind.RoomSurface:
					room.BackdropSurfaceId = contentPickerPreviewOriginal;
					break;
				case ContentPickerPreviewKind.ActorProperty:
					if (game.GetActorById(contentPickerPreviewActorId) is { } actor)
						ActorSpecs.SetEditableProperty(actor, contentPickerPreviewProperty,
							JsonValue.Create(contentPickerPreviewOriginal));
					break;
				case ContentPickerPreviewKind.NeuralModel:
					game.GetActorById<NeuralTextureActor>(contentPickerPreviewActorId)?
						.SetEditorPreviewModel(contentPickerPreviewSlot, contentPickerPreviewOriginal);
					break;
			}

		contentPickerPreviewKind = ContentPickerPreviewKind.None;
		contentPickerPreviewRoomId = "";
		contentPickerPreviewActorId = "";
		contentPickerPreviewProperty = "";
		contentPickerPreviewOriginal = "";
		contentPickerPreviewValue = "";
		contentPickerPreviewSlot = -1;
		contentPickerPreviewFrame = -1;
	}

	void ClearStaleContentPickerPreview()
	{
		if (contentPickerPreviewKind != ContentPickerPreviewKind.None && contentPickerPreviewFrame != GetFrameCount())
			ClearContentPickerPreview();
	}

	// A "color picker"-style surface selector: a preview button that opens a grid of
	// surface previews. Returns true (and updates value) when a surface is chosen; an
	// empty string selects "None" (solid fill).
	const int SurfacePickerColumns = 8;

	bool DrawSurfacePicker(ref string value, out string? preview)
	{
		preview = null;
		var changed = false;
		var cell = new Vector2(ButtonSize, ButtonSize);
		var current = Assets.GetSurface(value);

		if (current != null)
		{
			var clicked = ImGuiSubtextureButton("##SurfacePreview", GetSurfacePreview(current), $"Surface '{value}' — click to change, double-click to edit", false, cell, true, 0f);
			if (IsItemHovered() && IsMouseDoubleClicked(ImGuiMouseButton.Left))
				OpenSurfaceEditor(current.Id);
			else if (clicked)
				OpenPopup("##SurfaceGrid");
		}
		else if (Button("None##SurfacePreview", cell))
		{
			OpenPopup("##SurfaceGrid");
		}
		if (BeginPopupContextItem("##SurfaceContext"))
		{
			BeginDisabled(current == null);
			if (MenuItem("Edit") && current != null)
				OpenSurfaceEditor(current.Id);
			EndDisabled();
			EndPopup();
		}

		PushSwatchGridStyle();
		if (BeginPopup("##SurfaceGrid"))
		{
			var swatch = new Vector2(SwatchGridCell);
			var canCreate = Assets.Textures2D?.Order.Count > 0;
			var column = 0;
			if (Assets.Surfaces.Count == 0)
			{
				TextDisabled("No surfaces loaded (Assets/Sources/Surfaces/*.json)");
			}
			else
			{
				if (Button("None", swatch))
				{
					value = string.Empty;
					changed = true;
					CloseCurrentPopup();
				}
				if (IsItemHovered())
					preview = string.Empty;
				column = 1;
				foreach (var (id, surface) in Assets.Surfaces)
				{
					SwatchGridWrap(ref column, SurfacePickerColumns);
					if (SwatchGridButton($"##surf{id}", GetSurfacePreview(surface), id, value == id))
					{
						value = id;
						changed = true;
						CloseCurrentPopup();
					}
					if (IsItemHovered())
						preview = id;
				}
			}
			SwatchGridWrap(ref column, SurfacePickerColumns);
			BeginDisabled(!canCreate);
			if (Button($"{EditorIcons.Add}##CreateRandomSurface", swatch))
			{
				value = CreateRandomSurface();
				changed = true;
				CloseCurrentPopup();
			}
			EndDisabled();
			if (IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
				SetTooltip(canCreate ? "Create random surface" : "No surface textures loaded");
			EndPopup();
		}
		PopSwatchGridStyle();

		return changed;
	}

	// Every dual tileset a tile kit declares, so a dual-grid block can wear any of them.
	static readonly List<(string Tileset, string Label)> dualMaterials = [];

	static List<(string Tileset, string Label)> DualMaterials()
	{
		dualMaterials.Clear();
		foreach (var entry in Factory.Entries.Values)
			if (entry.DualTilesetName is { } tileset && !dualMaterials.Exists(m => m.Tileset == tileset))
				dualMaterials.Add((tileset, entry.Name));
		return dualMaterials;
	}

	// The fully-interior dual tile: the material itself, with no edge art.
	static Subtexture DualMaterialSwatch(string tileset) => Assets.GetTileset(tileset)?.GetTile(2, 1) ?? new Subtexture();

	const int DualMaterialPickerColumns = 8;

	// Like the surface picker: the current material's swatch opens a grid of every dual tileset, and hovering
	// one previews it on the live actor.
	bool DrawDualMaterialPicker(ref string value, out string? preview)
	{
		preview = null;
		var changed = false;
		if (ImGuiSubtextureButton("##DualMaterial", DualMaterialSwatch(value), $"Material '{value}' — click to change", false,
			new Vector2(ButtonSize), true, 0f))
			OpenPopup("##DualMaterialGrid");

		PushSwatchGridStyle();
		if (BeginPopup("##DualMaterialGrid"))
		{
			var column = 0;
			foreach (var (tileset, label) in DualMaterials())
			{
				SwatchGridWrap(ref column, DualMaterialPickerColumns);
				if (SwatchGridButton($"##dual{tileset}", DualMaterialSwatch(tileset), label, value == tileset))
				{
					value = tileset;
					changed = true;
					CloseCurrentPopup();
				}
				if (IsItemHovered())
					preview = tileset;
			}
			EndPopup();
		}
		PopSwatchGridStyle();
		return changed;
	}

}
#endif
