#if EDITOR
using System.Numerics;
using System.Text.Json.Nodes;
using Foster.Framework;
using ImGuiNET;

namespace Myth;

public partial class Manager
{
	static float BoxSpacing => 0;
	static float BoxPadding => 4;
	static int PaletteColumns => 5;
	static int ToolbarSpacing => 4;
	static float ToolbarHeight => ButtonSize + 4;
	static float ToolbarButtonImagePadding => 4;
	static float PaletteWidth => BoxPadding * 2 + PaletteColumns * ButtonSize + (PaletteColumns - 1) * ButtonSpacing;
	static Color highlight => 0x5fcde4;

	private const float AssetReloadDelay = 0.25f;
	private const float DefaultRoomPreviewResolutionScale = 0.75f;

	bool editorGameDirty;
	bool editorViewInitialized;
	Vector2 editorCamera;
	float editorZoom = 1.0f;
	float editorZoomTarget = 1.0f;
	float roomPreviewResolutionScale = DefaultRoomPreviewResolutionScale;
	private const bool AutoSelectRoomByCamera = true;
	Vector2 editorCameraTweenFrom;
	Vector2 editorCameraTweenTo;
	float editorZoomTweenFrom;
	float editorZoomTweenTo;
	float editorCameraTween = 1.0f;

	/// <summary>
	/// The current Type we're painting in the Editor
	/// </summary>
	private char brush = '0';
	private Factory.TileLayer activeTileLayer = Factory.TileLayer.Foreground;
	private bool editorShowForegroundLayer = true;
	private bool editorShowBackgroundLayer = true;

	char tileTypePlacing;
	bool editingRoomGeometry;
	Point2 tileRectPaintFrom;
	Point2 tileRectPaintTo;
	RectInt? proposedRoomTiles;
	string? selectedActorSpecId;
	int selectedActorPaletteIndex = -1;
	bool actorSpecInspectorOpen;
	bool actorSelectTool = true;
	int selectedRoomActorIndex = -1;
	bool selectionIncludeBackgroundTiles = true;
	bool selectionIncludeForegroundTiles = true;
	bool selectionIncludeActors = true;
	RectInt? editorSelectionRect;
	Point2 editorSelectionFrom;
	Point2 editorSelectionTo;
	EditorDragState editorDrag = EditorDragState.None;
	JsonUndoScope? roomGeometryUndo;
	string? pickingLinkedDoorTargetForActorId;
	readonly JsonUndoManager undo = new();
	JsonUndoScope? tilePaintUndo;
	JsonUndoScope? actorMoveUndo;
	JsonUndoScope? prefabUndo;
	string? prefabUndoSpecId;
	readonly Dictionary<string, EditorRoomPreview> roomPreviewCache = [];
	readonly Dictionary<string, int> roomPreviewVersions = [];
	readonly Queue<string> roomPreviewBuildQueue = [];
	readonly HashSet<string> queuedRoomPreviewIds = [];
	readonly List<IDisposable> deferredRoomPreviewDisposals = [];
	int nextRoomPreviewVersion = 1;
	Target? activeRoomPreviewTarget;
	string? activeRoomPreviewId;
	readonly Dictionary<Cursor.SystemTypes, Cursor> editorCursors = [];
	RoomSettings? copiedRoomSettings;

#if DEBUG && !IOS
	FileSystemWatcher? assetWatcher;
	FileSystemWatcher? shaderWatcher;
	FileSystemWatcher? audioBankWatcher;
	bool assetReloadRequested;
	float assetReloadTimer;
	readonly Lock shaderReloadLock = new();
	bool shaderReloadRequested;
	float shaderReloadTimer;
	readonly Lock audioBankReloadLock = new();
	bool audioBankReloadRequested;
	float audioBankReloadTimer;
	int fpsFrames;
	float fpsElapsed;
#endif

	private Point2 roomCreatePosition;
	private Point2? editorRoomEntryDirection;
	private bool editingRoomName;
	private bool focusRoomNameEditor;
	private string roomNameEditor = string.Empty;
	private string? roomNameEditingRoomId;
	private string? pendingDeleteRoomId;
	private readonly ImGuiRenderer imgui = null!;

	/// Toolbar Space
	private Rect ToolbarRect => new(View.X, View.Y, View.Width, ToolbarHeight);

	/// Workspace, which is the full space below the Toolbar
	private Rect WorkRect => new(View.X, View.Y + ToolbarSpacing + ToolbarHeight, View.Width, View.Height - ToolbarHeight - ToolbarSpacing);

	/// Palette View when the Editor is Open
	private Rect PaletteOpenRect => new(WorkRect.X, WorkRect.Y, PaletteWidth, WorkRect.Height);

	/// Edit Workspace when the Editor is Open
	private Rect EditorOpenRect
		=> new Rect(WorkRect.X + PaletteWidth + BoxSpacing, WorkRect.Y, WorkRect.Width - PaletteWidth - BoxSpacing, WorkRect.Height);

	/// Eases between the EditView and the PlayView as the Game opens/closes
	private Rect WorkspaceRect
	{
		get
		{
			var a = Vector2.Lerp(EditorOpenRect.TopLeft, GameOpenRect.TopLeft, Ease.Cube.InOut(gameEase));
			var b = Vector2.Lerp(EditorOpenRect.BottomRight, GameOpenRect.BottomRight, Ease.Cube.InOut(gameEase));
			return Rect.Between(a, b);
		}
	}

	private Rect GameViewRect => WorkspaceRect;

	private IJsonUndoTarget CurrentRoomUndoTarget(Room room)
		=> new RoomUndoTarget(room);

	private IJsonUndoTarget EditorSelectionUndoTarget()
		=> new EditorSelectionTarget(this);

	private JsonUndoScope CaptureRoomUndo(string name, Room room)
		=> undo.Capture(name, CurrentRoomUndoTarget(room), EditorSelectionUndoTarget());

	private JsonUndoScope CaptureSelectionUndo(string name)
		=> undo.Capture(name, EditorSelectionUndoTarget());

	private JsonUndoScope CapturePrefabUndo(string name, string specId)
		=> undo.Capture(name, new ActorSpecUndoTarget(specId));


	/// Eases the PaletteView out/in as the Game opens/closes
	private Rect PaletteSlideRect =>
		PaletteOpenRect - Vector2.UnitX * Ease.Cube.InOut(gameEase) * PaletteOpenRect.Right;


	private void UndoEditor()
	{
		undo.Undo();
		AfterUndoRedo();
	}

	private void RedoEditor()
	{
		undo.Redo();
		AfterUndoRedo();
	}

	private void DuplicateSelectedActor()
	{
		if (CurrentRoom is not { } room || selectedRoomActorIndex < 0 || selectedRoomActorIndex >= room.Actors.Count)
			return;

		using (CaptureRoomUndo("Duplicate Actor", room))
		{
			var copy = ActorSpecs.CloneObject(room.Actors[selectedRoomActorIndex]);
			var position = ActorSpecs.ReadPoint(copy, "Position") ?? Point2.Zero;
			copy["Position"] = new JsonArray(position.X + Game.TileSize, position.Y);
			ActorSpecs.AssignNewActorId(copy);
			room.Actors.Add(copy);
			selectedRoomActorIndex = room.Actors.Count - 1;
			ClearEditorDragState();
			editorGameDirty = true;
		}
	}

	private void AfterUndoRedo()
	{
		if (CurrentRoom is { } room)
		{
			if (selectedRoomActorIndex >= room.Actors.Count)
				selectedRoomActorIndex = -1;
			room.Save();
			MarkRoomPreviewDirty(room);
		}

		ClearEditorDragState();
		roomGeometryUndo = null;
		CancelTilePaint();
		editorSelectionRect = null;
		actorMoveUndo = null;
		tilePaintUndo = null;
		prefabUndo = null;
		prefabUndoSpecId = null;
		SyncEditorGame();
	}

	private bool UpdateEditorUndoRedo()
	{
		if (ImGuiWantsKeyboard)
			return false;

		var modifier =
			Input.Keyboard.Down(Keys.LeftControl) ||
			Input.Keyboard.Down(Keys.RightControl) ||
			Input.Keyboard.Down(Keys.LeftOS) ||
			Input.Keyboard.Down(Keys.RightOS);
		if (!modifier)
			return false;

		if (Input.Keyboard.Pressed(Keys.D))
		{
			DuplicateSelectedActor();
			return true;
		}

		if (Input.Keyboard.Pressed(Keys.Z))
		{
			if (Input.Keyboard.Shift)
				RedoEditor();
			else
				UndoEditor();
			return true;
		}

		if (Input.Keyboard.Pressed(Keys.Y))
		{
			RedoEditor();
			return true;
		}

		return false;
	}

	private sealed class RoomUndoTarget(Room room) : IJsonUndoTarget
	{
		public string UndoId => $"room:{room.Id}";
		public string CaptureJson() => room.ToJsonString();
		public void ApplyJson(string json)
		{
			var oldCell = room.Cell;
			room.LoadJsonState(json);
			UpdateRoomCellIndex(room, oldCell);
		}
	}

	private sealed class EditorSelectionTarget(Manager manager) : IJsonUndoTarget
	{
		public string UndoId => "editor-selection";

		public string CaptureJson()
		{
			var json = new JsonObject
			{
				["roomId"] = manager.selectedRoomId,
				["actorIndex"] = manager.selectedRoomActorIndex,
				["actorSelectTool"] = manager.actorSelectTool,
				["brush"] = manager.brush.ToString(),
				["tileLayer"] = manager.activeTileLayer.ToString(),
				["showForegroundLayer"] = manager.editorShowForegroundLayer,
				["showBackgroundLayer"] = manager.editorShowBackgroundLayer,
				["actorPaletteIndex"] = manager.selectedActorPaletteIndex,
				["prefabId"] = manager.selectedActorSpecId,
				["prefabInspectorOpen"] = manager.actorSpecInspectorOpen,
				["entryDirection"] = manager.editorRoomEntryDirection is { } direction ? new JsonArray(direction.X, direction.Y) : null
			};
			return ActorSpecs.ToJsonString(json);
		}

		public void ApplyJson(string jsonText)
		{
			var json = JsonNode.Parse(jsonText)?.AsObject() ?? [];
			var roomId = JsonUtil.ReadString(json, "roomId");
			if (roomId != null && Assets.GetRoom(roomId) is { } room)
				manager.SelectRoom(room);

			manager.selectedRoomActorIndex = JsonUtil.ReadInt(json, "actorIndex", -1);
			manager.actorSelectTool = JsonUtil.ReadBool(json, "actorSelectTool");
			if (JsonUtil.ReadString(json, "brush") is { Length: > 0 } brush)
				manager.brush = brush[0];
			if (Enum.TryParse<Factory.TileLayer>(JsonUtil.ReadString(json, "tileLayer"), out var layer))
				manager.activeTileLayer = layer;
			manager.editorShowForegroundLayer = JsonUtil.ReadBool(json, "showForegroundLayer", true);
			manager.editorShowBackgroundLayer = JsonUtil.ReadBool(json, "showBackgroundLayer", true);
			manager.selectedActorPaletteIndex = JsonUtil.ReadInt(json, "actorPaletteIndex", -1);
			manager.selectedActorSpecId = JsonUtil.ReadString(json, "prefabId");
			manager.actorSpecInspectorOpen = JsonUtil.ReadBool(json, "prefabInspectorOpen");
			manager.editorRoomEntryDirection = JsonUtil.ReadPoint(json, "entryDirection");
		}
	}

	private sealed class ActorSpecUndoTarget(string specId) : IJsonUndoTarget
	{
		public string UndoId => $"actor-spec:{specId}";
		public string CaptureJson() => ActorSpecs.CaptureSpecJson(specId);
		public void ApplyJson(string json) => ActorSpecs.ApplySpecJson(specId, json);
	}

	private enum ResizeHandle
	{
		None,
		Left,
		Right,
		Top,
		Bottom,
		TopLeft,
		TopRight,
		BottomLeft,
		BottomRight
	}

	private void SetHiddenWallResizeCursor(ResizeHandle handle)
	{
		if (handle == ResizeHandle.None)
		{
			SetMouseCursor(null);
			return;
		}

		var type = handle switch
		{
			ResizeHandle.Left or ResizeHandle.Right => Foster.Framework.Cursor.SystemTypes.ResizeHorizontal,
			ResizeHandle.Top or ResizeHandle.Bottom => Foster.Framework.Cursor.SystemTypes.ResizeVertical,
			ResizeHandle.TopLeft or ResizeHandle.BottomRight => Foster.Framework.Cursor.SystemTypes.ResizeNWSE,
			ResizeHandle.TopRight or ResizeHandle.BottomLeft => Foster.Framework.Cursor.SystemTypes.ResizeNESW,
			_ => Foster.Framework.Cursor.SystemTypes.Default
		};

		if (!editorCursors.TryGetValue(type, out var cursor))
		{
			cursor = new Cursor(type);
			editorCursors[type] = cursor;
		}
		SetMouseCursor(cursor);
	}

	private void DisposeEditorCursors()
	{
		foreach (var cursor in editorCursors.Values)
			cursor.Dispose();
		editorCursors.Clear();
	}

	private void FocusEditorOnCurrentRoom()
	{
		if (CurrentRoom is not { } room)
			return;

		GetEditorRoomFocus(room, out editorCamera, out editorZoom);
		editorZoomTarget = editorZoom;
		editorCameraTween = 1.0f;
		editorViewInitialized = true;
	}

	private void TweenEditorCameraToRoom(Room room)
	{
		GetEditorRoomFocus(room, out editorCameraTweenTo, out editorZoomTweenTo);
		editorCameraTweenFrom = editorCamera;
		editorZoomTweenFrom = editorZoom;
		editorCameraTween = 0.0f;
		editorViewInitialized = true;
	}

	private void GetEditorRoomFocus(Room room, out Vector2 camera, out float zoom)
	{
		var bounds = room.WorldBounds;
		var view = GameViewRect;
		camera = bounds.Center;
		zoom = Math.Clamp(Math.Min(view.Width / bounds.Width, view.Height / bounds.Height), 0.15f, 8.0f);
	}

	private void UpdateEditorCameraTween()
	{
		if (editorCameraTween >= 1.0f)
			return;

		editorCameraTween = Calc.Approach(editorCameraTween, 1.0f, Time.Delta * 4.0f);
		var t = Ease.Cube.InOut(editorCameraTween);
		editorCamera = Vector2.Lerp(editorCameraTweenFrom, editorCameraTweenTo, t);
		editorZoom = float.Lerp(editorZoomTweenFrom, editorZoomTweenTo, t);
		editorZoomTarget = editorZoom;
	}

	private void MatchEditorCameraToGame()
	{
		if (game == null)
			return;

		editorCamera = game.Camera + new Vector2(Game.Width, Game.Height) / 2;
		var view = EditorOpenRect;
		editorZoom = Math.Clamp(Math.Min(view.Width / Game.Width, view.Height / Game.Height), 0.15f, 8.0f);
		editorZoomTarget = editorZoom;
		editorCameraTween = 1.0f;
		editorViewInitialized = true;
	}

	private void UpdateEditorNavigation()
	{
		var direction = Point2.Zero;
		if (controls.Move.PressedUp)
			direction = Point2.Up;
		if (controls.Move.PressedDown)
			direction = Point2.Down;
		if (controls.Move.PressedLeft)
			direction = Point2.Left;
		if (controls.Move.PressedRight)
			direction = Point2.Right;

		if (direction != Point2.Zero)
		{
			using (CaptureSelectionUndo("Select Room"))
				ShiftRoom(direction);
			controls.Move.Clear();
		}
	}

	private void PaletteImGui()
	{
		var rect = PaletteSlideRect;
		var flags =
			ImGuiWindowFlags.NoTitleBar |
			ImGuiWindowFlags.NoMove |
			ImGuiWindowFlags.NoResize |
			ImGuiWindowFlags.NoCollapse |
			ImGuiWindowFlags.NoSavedSettings |
			ImGuiWindowFlags.NoBringToFrontOnFocus;

		ImGui.SetNextWindowPos(rect.TopLeft, ImGuiCond.Always);
		ImGui.SetNextWindowSize(rect.Size, ImGuiCond.Always);

		ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(BoxPadding, BoxPadding));
		ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(ButtonSpacing, ButtonSpacing));
		ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, Vector2.Zero);
		ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, LineWeight);
		ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.0f, 0.0f, 0.0f, 1.0f));
		//ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(1.0f, 1.0f, 1.0f, 1.0f));

		if (ImGui.Begin("Palette", flags))
		{
			if (ImGuiTextButton("Select", new Vector2(ButtonSize * 2 + ButtonSpacing, ButtonSize), actorSelectTool))
				SelectEditorActorTool();
			ImGui.Spacing();
			DrawTilePaletteSection();
			ImGui.Spacing();
			DrawActorPaletteSection();
			ImGui.Spacing();
			DrawRoomPaletteSection();
			ImGui.Spacing();
			DrawDitherPaletteSection();
			ImGui.Spacing();
			//DrawPreviewPaletteSection();

			if (ImGui.IsWindowHovered() && !ImGui.IsAnyItemHovered() && Input.Mouse.LeftPressed)
				SelectEditorActorTool();
		}
		ImGui.End();

		ImGui.PopStyleColor(1);
		ImGui.PopStyleVar(4);

		ActorSpecInspectorImGui();
		ActorInstanceInspectorImGui();
		DrawEditorSelectionOptions();
	}

	private void SelectEditorActorTool()
	{
		actorSelectTool = true;
		brush = '0';
	}

	void DrawTilePaletteSection()
	{
		ImGui.TextUnformatted("Tiles");
		DrawTileLayerSelector();
		var index = 0;
		foreach (var (id, entry) in Factory.Entries)
		{
			if (entry.Layer != activeTileLayer) continue;
			if (entry.IsSlope) continue; // skip slope tiles; we can place them with control

			if (index > 0 && index % PaletteColumns != 0)
				ImGui.SameLine();

			var dualPreview = GetDualTilesetPreview(entry);

			if (ImGuiSubtextureButton($"Palette_Tiles_{id}", entry.Image, entry.Name, dualPreview,
				selectedActorPaletteIndex < 0 && brush == id, new Vector2(ButtonSize, ButtonSize)))
			{
				brush = id;
				selectedActorPaletteIndex = -1;
				actorSelectTool = false;
			}

			if (ImGui.BeginPopupContextItem($"Palette_Tiles_{id}"))
			{
				if (ImGui.Selectable("Open in Sprite Editor"))
				{
					EditSprite(entry);
				}
				ImGui.EndPopup();
			}

			index++;
		}
	}

	static void EditSprite(Factory.Entry entry)
	{
		var filename = GetTileEntryAsepriteFilename(entry);
		if (filename == null)
		{
			Console.WriteLine($"No sprite filename found for entry {entry.Name}");
			return;
		}

		var startInfo = CreateAsepriteStartInfo(filename);

		try
		{
			System.Diagnostics.Process.Start(startInfo);
		}
		catch (Exception e)
		{
			Console.Error.WriteLine($"Failed to open '{filename}' in Aseprite using '{startInfo.FileName}': {e.Message}");
		}
	}

	static System.Diagnostics.ProcessStartInfo CreateAsepriteStartInfo(string filename)
	{
		if (OperatingSystem.IsMacOS())
		{
			var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
			var steamAseprite = Path.Combine(homeDir, "Application Support/Steam/steamapps/common/Aseprite/Aseprite.app/Contents/MacOS/aseprite");
			if (File.Exists(steamAseprite))
				return CreateStartInfo(steamAseprite, filename);

			var open = "/usr/bin/open";
			if (File.Exists(open))
			{
				var startInfo = CreateStartInfo(open, "-a", "Aseprite", filename);
				return startInfo;
			}
		}

		return CreateStartInfo("aseprite", filename);
	}

	static System.Diagnostics.ProcessStartInfo CreateStartInfo(string executable, params string[] arguments)
	{
		var startInfo = new System.Diagnostics.ProcessStartInfo
		{
			FileName = executable,
			UseShellExecute = false
		};

		foreach (var argument in arguments)
			startInfo.ArgumentList.Add(argument);

		return startInfo;
	}

	static string? GetTileEntryAsepriteFilename(Factory.Entry entry)
	{
		foreach (var tilesetName in GetTileEntryAsepriteCandidates(entry))
		{
			if (FindAsepriteFile(Path.Join(Assets.AssetsPath, "Tilesets"), tilesetName) is { } filename)
				return filename;
		}

		return null;
	}

	static IEnumerable<string> GetTileEntryAsepriteCandidates(Factory.Entry entry)
	{
		if (!string.IsNullOrWhiteSpace(entry.TilesetName))
		{
			yield return entry.TilesetName;
			yield return $"{entry.TilesetName}-kit";
		}

		if (!string.IsNullOrWhiteSpace(entry.SlopeTilesetName) && entry.SlopeTilesetName != entry.TilesetName)
		{
			yield return entry.SlopeTilesetName;
			yield return $"{entry.SlopeTilesetName}-kit";
		}

		if (!string.IsNullOrWhiteSpace(entry.DualTilesetName))
		{
			yield return entry.DualTilesetName;
			yield return $"{entry.DualTilesetName}-kit";
		}
	}

	static string? FindAsepriteFile(string directory, string name)
	{
		foreach (var extension in new[] { ".ase", ".aseprite" })
		{
			var path = Path.GetFullPath(Path.Join(directory, name + extension));
			if (File.Exists(path))
				return path;
		}

		return null;
	}

	private void DrawTileLayerSelector()
	{
		if (ImGui.SmallButton("O ##ShowBG"))
			editorShowBackgroundLayer = !editorShowBackgroundLayer;
		ImGui.SameLine();
		if (ImGui.Button(activeTileLayer == Factory.TileLayer.Background ? "Background *" : "Background"))
			SelectTileLayer(Factory.TileLayer.Background);

		if (ImGui.SmallButton("O ##ShowFG"))
			editorShowForegroundLayer = !editorShowForegroundLayer;
		ImGui.SameLine();
		if (ImGui.Button(activeTileLayer == Factory.TileLayer.Foreground ? "Foreground *" : "Foreground"))
			SelectTileLayer(Factory.TileLayer.Foreground);
	}

	private void SelectTileLayer(Factory.TileLayer layer)
	{
		activeTileLayer = layer;
		if (Factory.Find(brush) is { } entry && entry.Layer == activeTileLayer)
			return;

		brush = Factory.Entries.FirstOrDefault(it => it.Value.Layer == activeTileLayer).Key;
		selectedActorPaletteIndex = -1;
		actorSelectTool = false;
	}

	private static Tileset? GetDualTilesetPreview(Factory.Entry entry)
		=> entry.DualTilesetName != null ? Assets.GetTileset(entry.DualTilesetName) : null;

	private void DrawActorPaletteSection()
	{
		ImGui.TextUnformatted("Actors");
		var index = 0;
		for (var i = 0; i < Factory.ActorPalette.Count; i++)
		{
			var entry = Factory.ActorPalette[i];
			if (entry.Image.Texture == null)
				continue;

			if (index > 0 && index % PaletteColumns != 0)
				ImGui.SameLine();

			if (ImGuiSubtextureButton($"Palette_Actors_{i}", entry.Image, entry.Name, selectedActorPaletteIndex == i, new Vector2(ButtonSize, ButtonSize), true))
			{
				selectedActorPaletteIndex = i;
				actorSelectTool = false;
				if (entry.ActorSpecId != null)
				{
					selectedActorSpecId = entry.ActorSpecId;
					actorSpecInspectorOpen = true;
				}
			}

			index++;
		}
	}

	private void DrawRoomPaletteSection()
	{
		if (CurrentRoom is not { } room)
			return;

		if (!ImGui.CollapsingHeader("Room"))
			return;

		var changed = false;
		if (ImGui.Button(editingRoomGeometry ? "Done" : "Resize"))
		{
			editingRoomGeometry = !editingRoomGeometry;
			ClearEditorDragState();
		}
		ImGui.SameLine();

		if (ImGui.Button("Copy"))
			copiedRoomSettings = CaptureRoomSettings(room);
		if (copiedRoomSettings is { } settings)
		{
			ImGui.SameLine();
			if (ImGui.Button("Paste"))
			{
				using (CaptureRoomUndo("Paste Room Settings", room))
					ApplyRoomSettings(room, settings);
				changed = true;
			}
		}

		ImGui.TextUnformatted("Notes");
		ImGui.SetNextItemWidth(-1);
		ImGui.InputTextMultiline("##RoomNotesEditor", ref room.Notes, 3000, new(-1, 80));

		ImGui.TextUnformatted("Backdrop");
		var backdrop = ColorToVector3(room.BackdropColor);
		ImGui.SetNextItemWidth(-1);
		if (ImGui.ColorEdit3("##RoomBackdropColor", ref backdrop, ImGuiColorEditFlags.NoInputs))
		{
			using (CaptureRoomUndo("Edit Room Backdrop", room))
				room.BackdropColor = Vector3ToColor(backdrop);
			changed = true;
		}

		var gradient = room.BackdropColor2.HasValue;
		if (ImGui.Checkbox("Gradient", ref gradient))
		{
			using (CaptureRoomUndo("Edit Room Backdrop", room))
				room.BackdropColor2 = gradient ? room.BackdropColor : null;
			changed = true;
		}

		if (room.BackdropColor2 is { } backdrop2Color)
		{
			ImGui.TextUnformatted("Backdrop Bottom");
			var backdrop2 = ColorToVector3(backdrop2Color);
			ImGui.SetNextItemWidth(-1);
			if (ImGui.ColorEdit3("##RoomBackdropColor2", ref backdrop2, ImGuiColorEditFlags.NoInputs))
			{
				using (CaptureRoomUndo("Edit Room Backdrop", room))
					room.BackdropColor2 = Vector3ToColor(backdrop2);
				changed = true;
			}
		}

		ImGui.TextUnformatted("Ambient");
		var ambient = ColorToVector3(room.AmbientColor);
		ImGui.SetNextItemWidth(-1);
		if (ImGui.ColorEdit3("##RoomAmbientColor", ref ambient, ImGuiColorEditFlags.NoInputs))
		{
			using (CaptureRoomUndo("Edit Room Lighting", room))
				room.AmbientColor = Vector3ToColor(ambient);
			changed = true;
		}

		ImGui.TextUnformatted("Intensity");
		var ambientIntensity = room.AmbientIntensity;
		ImGui.SetNextItemWidth(-1);
		if (ImGui.SliderFloat("##RoomAmbientIntensity", ref ambientIntensity, 0.0f, 2.0f))
		{
			using (CaptureRoomUndo("Edit Room Lighting", room))
				room.AmbientIntensity = ambientIntensity;
			changed = true;
		}

		ImGui.TextUnformatted("Noise");
		var lightNoise = room.LightNoise;
		ImGui.SetNextItemWidth(-1);
		if (ImGui.SliderFloat("##RoomLightNoise", ref lightNoise, 0.0f, 1.0f))
		{
			using (CaptureRoomUndo("Edit Room Lighting", room))
				room.LightNoise = lightNoise;
			changed = true;
		}

		ImGui.TextUnformatted("Reverb");
		var reverbAuto = room.ReverbAuto;
		if (ImGui.Checkbox("Auto Reverb", ref reverbAuto))
		{
			using (CaptureRoomUndo("Edit Room Reverb", room))
			{
				room.ReverbAuto = reverbAuto;
				if (room.ReverbAuto)
					room.RebuildGeometry();
			}
			changed = true;
		}

		var reverb = room.Reverb;
		ImGui.SetNextItemWidth(-1);
		if (ImGui.SliderFloat("##RoomReverb", ref reverb, 0.0f, 1.0f))
		{
			using (CaptureRoomUndo("Edit Room Reverb", room))
			{
				room.ReverbAuto = false;
				room.Reverb = Math.Clamp(reverb, 0.0f, 1.0f);
			}
			changed = true;
		}

		ImGui.TextUnformatted("Sun Strength");
		var sunStrength = room.SunStrength;
		ImGui.SetNextItemWidth(-1);
		if (ImGui.SliderFloat("##RoomSunStrength", ref sunStrength, 0.0f, 2.0f))
		{
			using (CaptureRoomUndo("Edit Room Sun", room))
				room.SunStrength = sunStrength;
			changed = true;
		}

		if (sunStrength > 0)
		{
			ImGui.TextUnformatted("Sun Color");
			var sun = ColorToVector3(room.SunColor);
			ImGui.SetNextItemWidth(-1);
			if (ImGui.ColorEdit3("##RoomSunColor", ref sun, ImGuiColorEditFlags.NoInputs))
			{
				using (CaptureRoomUndo("Edit Room Sun", room))
					room.SunColor = Vector3ToColor(sun);
				changed = true;
			}

			ImGui.TextUnformatted("Sun Direction");
			var sunDirection = room.SunDirection;
			ImGui.SetNextItemWidth(-1);
			if (ImGui.DragFloat2("##RoomSunDirection", ref sunDirection, 0.01f, -1.0f, 1.0f))
			{
				using (CaptureRoomUndo("Edit Room Sun", room))
					room.SunDirection = Room.NormalizeOrDefault(sunDirection, room.SunDirection);
				changed = true;
			}
		}

		if (musicAreas.Count > 0)
		{
			ImGui.TextUnformatted("Music");
			var area = room.MusicArea ?? DefaultMusicArea;
			ImGui.SetNextItemWidth(-1);
			if (ImGui.BeginCombo("##RoomMusicArea", area))
			{
				foreach (var musicArea in musicAreas)
				{
					var selected = area == musicArea;
					if (ImGui.Selectable(musicArea, selected))
					{
						using (CaptureRoomUndo("Edit Room Music", room))
							room.MusicArea = musicArea == DefaultMusicArea ? null : musicArea;
						changed = true;
					}
					if (selected)
						ImGui.SetItemDefaultFocus();
				}
				ImGui.EndCombo();
			}
		}

		if (changed)
		{
			editorGameDirty = true;
			SyncEditorGame();
		}
	}

	private void DrawDitherPaletteSection()
	{
		if (!ImGui.CollapsingHeader("Dither"))
			return;

		ImGui.Checkbox("Enabled", ref LightingDitherEnabled);

		ImGui.TextUnformatted("Scale");
		ImGui.SetNextItemWidth(-1);
		ImGui.SliderFloat("##LightingDitherScale", ref LightingDitherScale, 1.0f, 8.0f);

		ImGui.TextUnformatted("Strength");
		ImGui.SetNextItemWidth(-1);
		ImGui.SliderFloat("##LightingDitherStrength", ref LightingDitherStrength, 0.0f, 1.0f);
	}

#if false
	private void DrawPreviewPaletteSection()
	{
		if (!ImGui.CollapsingHeader("Previews"))
			return;

		ImGui.TextUnformatted("Resolution");
		ImGui.SetNextItemWidth(-1);
		var scale = roomPreviewResolutionScale;
		if (ImGui.SliderFloat("##RoomPreviewResolutionScale", ref scale, 0.05f, 1.0f))
		{
			roomPreviewResolutionScale = scale;
			ClearRoomPreviewCache();
		}
	}
#endif

	private static RoomSettings CaptureRoomSettings(Room room)
		=> new(room.MusicArea, room.BackdropColor, room.BackdropColor2, room.AmbientColor, room.AmbientIntensity, room.LightNoise, room.ReverbAuto, room.Reverb, room.SunColor, room.SunStrength, room.SunDirection);

	private static void ApplyRoomSettings(Room room, RoomSettings settings)
	{
		room.MusicArea = settings.MusicArea;
		room.BackdropColor = settings.BackdropColor;
		room.BackdropColor2 = settings.BackdropColor2;
		room.AmbientColor = settings.AmbientColor;
		room.AmbientIntensity = settings.AmbientIntensity;
		room.LightNoise = settings.LightNoise;
		room.ReverbAuto = settings.ReverbAuto;
		room.Reverb = settings.Reverb;
		room.SunColor = settings.SunColor;
		room.SunStrength = settings.SunStrength;
		room.SunDirection = settings.SunDirection;
		if (room.ReverbAuto)
			room.RebuildGeometry();
	}

	private void ActorSpecInspectorImGui()
	{
		if (!actorSpecInspectorOpen || selectedActorSpecId == null)
			return;

		ImGui.SetNextWindowSize(new Vector2(260, 0), ImGuiCond.FirstUseEver);
		var open = actorSpecInspectorOpen;
		if (!ImGui.Begin($"Prefab: {selectedActorSpecId}", ref open, ImGuiWindowFlags.NoSavedSettings))
		{
			actorSpecInspectorOpen = open;
			ImGui.End();
			return;
		}
		actorSpecInspectorOpen = open;

		var changed = false;
		foreach (var property in ActorSpecs.GetEditableProperties(selectedActorSpecId))
		{
			ImGui.PushID(property.JsonName);
			changed |= DrawActorSpecProperty(selectedActorSpecId, property);
			ImGui.PopID();
		}

		if (changed)
		{
			SyncEditorGame();
		}

		if (ImGui.Button("Save"))
			ActorSpecs.SaveSpec(selectedActorSpecId);

		ImGui.End();
	}

	private bool DrawActorSpecProperty(string specId, ActorSpecs.EditableProperty property)
	{
		var changed = false;
		var controlWidth = BeginInspectorPropertyRow(property.Name, property.HasLocalValue);
		ImGui.SetNextItemWidth(controlWidth);
		ImGui.PushID("Value");

		if (property.Type == typeof(float))
		{
			var value = property.Value?.GetValue<float>() ?? 0;
			if (ImGui.DragFloat("##Value", ref value, 0.05f))
			{
				BeginPrefabUndo(specId);
				ActorSpecs.SetProperty(specId, property.JsonName, JsonValue.Create(value));
				changed = true;
			}
		}
		else if (property.Type == typeof(int))
		{
			var value = property.Value?.GetValue<int>() ?? 0;
			if (ImGui.DragInt("##Value", ref value))
			{
				BeginPrefabUndo(specId);
				ActorSpecs.SetProperty(specId, property.JsonName, JsonValue.Create(value));
				changed = true;
			}
		}
		else if (IsHiddenWallTileTypeProperty(property))
		{
			var value = property.Value?.GetValue<string>() ?? "castle";
			if (DrawHiddenWallTileTypeButtons(value, next =>
			{
				BeginPrefabUndo(specId);
				ActorSpecs.SetProperty(specId, property.JsonName, JsonValue.Create(next));
			}))
				changed = true;
		}
		else if (property.Type == typeof(string))
		{
			var value = property.Value?.GetValue<string>() ?? string.Empty;
			if (ImGui.InputTextMultiline("##Value", ref value, 2048, new Vector2(controlWidth, 64)))
			{
				BeginPrefabUndo(specId);
				ActorSpecs.SetProperty(specId, property.JsonName, JsonValue.Create(value));
				changed = true;
			}
		}
		else if (property.Type == typeof(Point2))
		{
			var point = ActorSpecs.ReadPoint(property.Value) ?? Point2.Zero;
			var values = new[] { point.X, point.Y };
			if (ImGui.DragInt2("##Value", ref values[0]))
			{
				BeginPrefabUndo(specId);
				ActorSpecs.SetProperty(specId, property.JsonName, new JsonArray(values[0], values[1]));
				changed = true;
			}
		}
		else if (property.Type == typeof(bool))
		{
			var value = property.Value?.GetValue<bool>() ?? false;
			if (ImGui.Checkbox("##Value", ref value))
			{
				BeginPrefabUndo(specId);
				ActorSpecs.SetProperty(specId, property.JsonName, JsonValue.Create(value));
				changed = true;
			}
		}
		else if (property.Type == typeof(Color))
		{
			var color = property.Value?.GetValue<string>() ?? "ffffff";
			var rgb = HexToVector3(color);
			if (ImGui.ColorEdit3("##Value", ref rgb, ImGuiColorEditFlags.NoInputs))
			{
				BeginPrefabUndo(specId);
				ActorSpecs.SetProperty(specId, property.JsonName, JsonValue.Create(Vector3ToHex(rgb)));
				changed = true;
			}
		}
		else if (property.Type == typeof(Signs))
		{
			var value = ActorSpecs.ReadSigns(property.Value);
			if (ImGuiSubtextureButton("##Value", Assets.GetSubtexture("buttons/15"), FacingTooltip(value), false, new Vector2(ButtonSize, ButtonSize), true))
			{
				BeginPrefabUndo(specId);
				ActorSpecs.SetProperty(specId, property.JsonName, JsonValue.Create((-value).ToString()));
				changed = true;
			}
		}
		else if (property.Type.IsEnum)
		{
			var value = property.Value?.GetValue<string>() ?? Enum.GetNames(property.Type).FirstOrDefault() ?? string.Empty;
			if (DrawEnumCombo(property.Type, ref value))
			{
				BeginPrefabUndo(specId);
				ActorSpecs.SetProperty(specId, property.JsonName, JsonValue.Create(value));
				changed = true;
			}
		}
		else
		{
			ImGui.TextUnformatted("-");
		}
		ImGui.PopID();
		if (prefabUndoSpecId == specId && ImGui.IsItemDeactivatedAfterEdit())
			CommitPrefabUndo();

		if (property.HasLocalValue)
		{
			ImGui.SameLine();
			if (ImGui.SmallButton("Reset"))
			{
				using (CapturePrefabUndo("Reset Prefab Property", specId))
				{
					ActorSpecs.ResetProperty(specId, property.JsonName);
					changed = true;
				}
			}
		}

		return changed;
	}

	private void ActorInstanceInspectorImGui()
	{
		if (CurrentRoom is not { } room || selectedRoomActorIndex < 0 || selectedRoomActorIndex >= room.Actors.Count)
			return;

		var actor = room.Actors[selectedRoomActorIndex];
		var title = ActorSpecs.ReadString(actor, "From") ?? ActorSpecs.ReadString(actor, "Type") ?? "Actor";
		var inspectorPadding = Vector2.One * 16;
		var window = RequiredWindow;
		var inspectorPos = new Vector2(window.WidthInPixels / imgui.Scale, window.HeightInPixels / imgui.Scale) - inspectorPadding;
		ImGui.SetNextWindowPos(inspectorPos, ImGuiCond.Appearing, Vector2.One);
		ImGui.SetNextWindowSize(new Vector2(260, 0), ImGuiCond.FirstUseEver);
		var open = true;
		if (!ImGui.Begin($"Actor: {title}##ActorInstanceInspector", ref open, ImGuiWindowFlags.NoSavedSettings))
		{
			if (!open)
				selectedRoomActorIndex = -1;
			ImGui.End();
			return;
		}

		if (!open)
		{
			selectedRoomActorIndex = -1;
			ImGui.End();
			return;
		}

		if (ActorSpecs.ReadString(actor, "From") is { } specId && ActorSpecs.Get(specId) != null)
		{
			if (ImGui.Button("Jump to Prefab"))
			{
				selectedActorSpecId = specId;
				actorSpecInspectorOpen = true;
			}
			ImGui.Separator();
		}

		var changed = false;
		foreach (var property in ActorSpecs.GetEditableProperties(actor))
		{
			ImGui.PushID(property.JsonName);
			changed |= DrawActorInstanceProperty(room, actor, property);
			ImGui.PopID();
		}

		if (changed)
		{
			editorGameDirty = true;
			SyncEditorGame();
		}

		ImGui.End();
	}

	private bool DrawActorInstanceProperty(Room room, JsonObject actor, ActorSpecs.EditableProperty property)
	{
		if (property.Name == nameof(LinkedDoor.TargetActorId) && IsLinkedDoorActor(actor))
			return DrawLinkedDoorTargetProperty(room, actor, property);

		var changed = false;
		var controlWidth = BeginInspectorPropertyRow(property.Name, property.HasLocalValue);
		ImGui.SetNextItemWidth(controlWidth);
		ImGui.PushID("Value");

		if (property.Type == typeof(float))
		{
			var value = property.Value?.GetValue<float>() ?? 0;
			if (ImGui.DragFloat("##Value", ref value, 0.05f))
			{
				using (CaptureRoomUndo("Edit Actor Property", room))
					ActorSpecs.SetProperty(actor, property.JsonName, JsonValue.Create(value));
				changed = true;
			}
		}
		else if (property.Type == typeof(int))
		{
			var value = property.Value?.GetValue<int>() ?? 0;
			if (ImGui.DragInt("##Value", ref value))
			{
				using (CaptureRoomUndo("Edit Actor Property", room))
					ActorSpecs.SetProperty(actor, property.JsonName, JsonValue.Create(value));
				changed = true;
			}
		}
		else if (IsHiddenWallTileTypeProperty(property))
		{
			var value = property.Value?.GetValue<string>() ?? "castle";
			if (DrawHiddenWallTileTypeButtons(value, next =>
			{
				using (CaptureRoomUndo("Edit Actor Property", room))
					ActorSpecs.SetProperty(actor, property.JsonName, JsonValue.Create(next));
			}))
				changed = true;
		}
		else if (property.Type == typeof(string))
		{
			var value = property.Value?.GetValue<string>() ?? string.Empty;
			if (ImGui.InputTextMultiline("##Value", ref value, 2048, new Vector2(controlWidth, 64)))
			{
				using (CaptureRoomUndo("Edit Actor Property", room))
					ActorSpecs.SetProperty(actor, property.JsonName, JsonValue.Create(value));
				changed = true;
			}
		}
		else if (property.Type == typeof(Point2))
		{
			var point = ActorSpecs.ReadPoint(property.Value) ?? Point2.Zero;
			var values = new[] { point.X, point.Y };
			if (ImGui.DragInt2("##Value", ref values[0]))
			{
				using (CaptureRoomUndo("Edit Actor Property", room))
					ActorSpecs.SetProperty(actor, property.JsonName, new JsonArray(values[0], values[1]));
				changed = true;
			}
		}
		else if (property.Type == typeof(bool))
		{
			var value = property.Value?.GetValue<bool>() ?? false;
			if (ImGui.Checkbox("##Value", ref value))
			{
				using (CaptureRoomUndo("Edit Actor Property", room))
					ActorSpecs.SetProperty(actor, property.JsonName, JsonValue.Create(value));
				changed = true;
			}
		}
		else if (property.Type == typeof(Color))
		{
			var color = property.Value?.GetValue<string>() ?? "ffffff";
			var rgb = HexToVector3(color);
			if (ImGui.ColorEdit3("##Value", ref rgb, ImGuiColorEditFlags.NoInputs))
			{
				using (CaptureRoomUndo("Edit Actor Property", room))
					ActorSpecs.SetProperty(actor, property.JsonName, JsonValue.Create(Vector3ToHex(rgb)));
				changed = true;
			}
		}
		else if (property.Type == typeof(Signs))
		{
			var value = ActorSpecs.ReadSigns(property.Value);
			if (ImGuiSubtextureButton("##Value", Assets.GetSubtexture("buttons/15"), FacingTooltip(value), false, new Vector2(ButtonSize, ButtonSize), true))
			{
				using (CaptureRoomUndo("Edit Actor Property", room))
					ActorSpecs.SetProperty(actor, property.JsonName, JsonValue.Create((-value).ToString()));
				changed = true;
			}
		}
		else if (property.Type.IsEnum)
		{
			var value = property.Value?.GetValue<string>() ?? Enum.GetNames(property.Type).FirstOrDefault() ?? string.Empty;
			if (DrawEnumCombo(property.Type, ref value))
			{
				using (CaptureRoomUndo("Edit Actor Property", room))
					ActorSpecs.SetProperty(actor, property.JsonName, JsonValue.Create(value));
				changed = true;
			}
		}
		else
		{
			ImGui.TextUnformatted("-");
		}
		ImGui.PopID();

		if (property.HasLocalValue)
		{
			ImGui.SameLine();
			if (ImGui.SmallButton("Reset"))
			{
				using (CaptureRoomUndo("Reset Actor Property", room))
					ActorSpecs.ResetProperty(actor, property.JsonName);
				changed = true;
			}
		}

		return changed;
	}

	private static string FacingTooltip(Signs value) => "Click to face the other direction.";

	private bool DrawLinkedDoorTargetProperty(Room room, JsonObject actor, ActorSpecs.EditableProperty property)
	{
		var changed = false;
		var controlWidth = BeginInspectorPropertyRow(property.Name, property.HasLocalValue);
		var value = property.Value?.GetValue<string>() ?? string.Empty;
		var target = Assets.FindActorById(value);
		var sourceId = ActorSpecs.ReadString(actor, "Id") ?? "";
		var picking = !string.IsNullOrWhiteSpace(sourceId) &&
					  string.Equals(pickingLinkedDoorTargetForActorId, sourceId, StringComparison.OrdinalIgnoreCase);

		ImGui.BeginGroup();
		ImGui.SetNextItemWidth(controlWidth);
		ImGui.InputText("##Value", ref value, 128, ImGuiInputTextFlags.ReadOnly);

		if (target is { } found)
			ImGui.TextDisabled($"{found.Room.Id}");
		else if (!string.IsNullOrWhiteSpace(value))
			ImGui.TextColored(new Vector4(1.0f, 0.35f, 0.25f, 1.0f), "Missing target");
		else
			ImGui.TextDisabled("No target");

		if (ImGui.SmallButton(picking ? "Cancel Pick" : "Pick"))
			pickingLinkedDoorTargetForActorId = picking ? null : sourceId;

		ImGui.SameLine();
		if (ImGui.SmallButton("Clear"))
		{
			using (CaptureRoomUndo("Clear Linked Door Target", room))
				ActorSpecs.SetProperty(actor, property.JsonName, JsonValue.Create(""));
			if (picking)
				pickingLinkedDoorTargetForActorId = null;
			changed = true;
		}

		if (target is { } jumpTarget)
		{
			ImGui.SameLine();
			if (ImGui.SmallButton("Jump"))
			{
				CurrentRoom?.Save();
				SelectRoom(jumpTarget.Room);
				selectedRoomActorIndex = jumpTarget.Room.Actors.IndexOf(jumpTarget.Actor);
				SyncEditorGame();
				TweenEditorCameraToRoom(jumpTarget.Room);
			}
		}
		ImGui.EndGroup();

		if (property.HasLocalValue)
		{
			ImGui.SameLine();
			if (ImGui.SmallButton("Reset"))
			{
				using (CaptureRoomUndo("Reset Actor Property", room))
					ActorSpecs.ResetProperty(actor, property.JsonName);
				changed = true;
			}
		}

		return changed;
	}

	private static bool DrawEnumCombo(Type type, ref string value)
	{
		var changed = false;
		if (ImGui.BeginCombo("##Value", value))
		{
			foreach (var name in Enum.GetNames(type))
			{
				var selected = string.Equals(value, name, StringComparison.OrdinalIgnoreCase);
				if (ImGui.Selectable(name, selected))
				{
					value = name;
					changed = true;
				}
				if (selected)
					ImGui.SetItemDefaultFocus();
			}
			ImGui.EndCombo();
		}
		return changed;
	}

	private static bool IsHiddenWallTileTypeProperty(ActorSpecs.EditableProperty property)
		=> property.Type == typeof(string) && property.JsonName == "tileType";

	private bool DrawHiddenWallTileTypeButtons(string value, Action<string> setValue)
	{
		var current = NormalizeHiddenWallTileType(value);
		var changed = false;

		changed |= DrawHiddenWallTileTypeButton("castle", "Castle", current, setValue);
		ImGui.SameLine();
		changed |= DrawHiddenWallTileTypeButton("grass", "Grass", current, setValue);

		return changed;
	}

	private bool DrawHiddenWallTileTypeButton(string tileType, string label, string current, Action<string> setValue)
	{
		var image = Assets.GetTileset(tileType)?.Tiles.FirstOrDefault() ?? new Subtexture();
		if (!ImGuiSubtextureButton(tileType, image, label, current == tileType, new Vector2(ButtonSize, ButtonSize)))
			return false;

		if (current != tileType)
			setValue(tileType);
		return current != tileType;
	}

	private static string NormalizeHiddenWallTileType(string value)
		=> value == "grass" ? "grass" : "castle";

	private static float BeginInspectorPropertyRow(string label, bool hasResetButton)
	{
		var available = ImGui.GetContentRegionAvail().X;
		var labelWidth = MathF.Min(92, MathF.Max(56, available * 0.38f));
		var resetWidth = hasResetButton ? ImGui.CalcTextSize("Reset").X + ImGui.GetStyle().FramePadding.X * 2 + ImGui.GetStyle().ItemSpacing.X : 0;
		var controlWidth = MathF.Max(72, available - labelWidth - resetWidth - ImGui.GetStyle().ItemSpacing.X);

		ImGui.AlignTextToFramePadding();
		ImGui.TextUnformatted(label);
		if (ImGui.IsItemHovered() && ImGui.CalcTextSize(label).X > labelWidth)
			ImGui.SetTooltip(label);
		ImGui.SameLine(labelWidth);

		return controlWidth;
	}

	private void BeginPrefabUndo(string specId)
	{
		if (prefabUndoSpecId == specId && prefabUndo != null)
			return;

		CommitPrefabUndo();
		prefabUndoSpecId = specId;
		prefabUndo = CapturePrefabUndo("Edit Prefab", specId);
	}

	private void CommitPrefabUndo()
	{
		prefabUndo?.Commit();
		prefabUndo = null;
		prefabUndoSpecId = null;
	}

	private static Vector3 HexToVector3(string hex)
	{
		hex = hex.TrimStart('#');
		if (hex.Length < 6 || !uint.TryParse(hex[..6], System.Globalization.NumberStyles.HexNumber, null, out var value))
			return Vector3.One;

		return new Vector3(
			((value >> 16) & 0xff) / 255.0f,
			((value >> 8) & 0xff) / 255.0f,
			(value & 0xff) / 255.0f);
	}

	private static string Vector3ToHex(Vector3 value)
	{
		var r = (int)(Math.Clamp(value.X, 0, 1) * 255);
		var g = (int)(Math.Clamp(value.Y, 0, 1) * 255);
		var b = (int)(Math.Clamp(value.Z, 0, 1) * 255);
		return $"{r:X2}{g:X2}{b:X2}";
	}

	private static Vector3 ColorToVector3(Color color)
		=> new(color.R / 255.0f, color.G / 255.0f, color.B / 255.0f);

	private static Color Vector3ToColor(Vector3 value)
		=> new(
			Math.Clamp(value.X, 0, 1),
			Math.Clamp(value.Y, 0, 1),
			Math.Clamp(value.Z, 0, 1),
			1.0f);

	private static RectInt RoomActorBounds(Room room, JsonObject actor)
	{
		var local = ActorSpecs.ReadPoint(actor, "Position") ?? Point2.Zero;
		var position = room.WorldBounds.TopLeft + local;
		var resolved = ActorSpecs.Resolve(actor);
		if (ResizableRoomRectTypes.TryGetValue(ActorSpecs.ReadString(resolved, "Type") ?? "", out var defaultSize))
		{
			var size = ActorSpecs.ReadPoint(resolved, "Size") ?? defaultSize;
			return new RectInt(position.X, position.Y, size.X, size.Y);
		}

		if (FindRoomActorEntry(actor) is { } entry)
			return (entry.EditorBounds ?? entry.DefaultEditorBounds) + position;

		return new RectInt(position.X - 4, position.Y - 4, 8, 8);
	}

	private static bool IsHiddenWallActor(JsonObject actor)
		=> ActorSpecs.ReadString(ActorSpecs.Resolve(actor), "Type") == "HiddenWall";

	private static bool IsRevealDoorActor(JsonObject actor)
		=> ActorSpecs.ReadString(ActorSpecs.Resolve(actor), "Type") == "RevealDoor";

	private static bool IsLinkedDoorActor(JsonObject actor)
		=> ActorSpecs.ReadString(ActorSpecs.Resolve(actor), "Type") == "LinkedDoor";

	// Actor types that author as a tile-snapped room rect, sharing the resize-handle UI.
	// Maps type name -> default size for a freshly placed instance.
	private static readonly Dictionary<string, Point2> ResizableRoomRectTypes = new()
	{
		["HiddenWall"] = HiddenWall.DefaultSize,
		["RevealDoor"] = RevealDoor.DefaultSize,
		["CameraBlocker"] = CameraBlocker.DefaultSize,
	};

	private static bool IsResizableRoomRectActor(JsonObject actor)
		=> ResizableRoomRectTypes.ContainsKey(ActorSpecs.ReadString(ActorSpecs.Resolve(actor), "Type") ?? "");

	private static Point2 ResizableRoomRectDefaultSize(JsonObject actor)
		=> ResizableRoomRectTypes.GetValueOrDefault(
			ActorSpecs.ReadString(ActorSpecs.Resolve(actor), "Type") ?? "", HiddenWall.DefaultSize);

	private static Factory.ActorPaletteEntry? FindRoomActorEntry(JsonObject actor)
	{
		var specId = ActorSpecs.ReadString(actor, "From");
		var typeName = ActorSpecs.ReadString(actor, "Type");

		foreach (var entry in Factory.ActorPalette)
		{
			if (specId != null && entry.ActorSpecId == specId)
				return entry;
			if (typeName != null && entry.ActorTypeName == typeName && entry.ActorSpecId == null)
				return entry;
		}

		return null;
	}

	private int HitTestRoomActor(Room room, Vector2 world)
	{
		for (var i = room.Actors.Count - 1; i >= 0; i--)
			if (RoomActorBounds(room, room.Actors[i]).Contains(world))
				return i;

		return -1;
	}

	// A subtle screen-sized grid for orientation when zoomed far enough out to see several
	// gameplay screens at once. It's a fixed world grid anchored at the origin (a line passes
	// through 0,0), spaced one gameplay screen apart -- independent of any room's size or
	// position.
	private void DrawEditorScreenGrid(Rect visibleWorld)
	{
		const float minScreensVisible = 3f;
		float screenW = Game.Width;
		float screenH = Game.Height;
		if (MathF.Max(visibleWorld.Width / screenW, visibleWorld.Height / screenH) < minScreensVisible)
			return;

		var weight = 1.0f / editorZoom;
		var color = Color.Gray * 0.22f;

		var left = visibleWorld.X;
		var top = visibleWorld.Y;
		var right = visibleWorld.X + visibleWorld.Width;
		var bottom = visibleWorld.Y + visibleWorld.Height;

		for (var x = MathF.Floor(left / screenW) * screenW; x <= right; x += screenW)
			batcher.Line(new Vector2(x, top), new Vector2(x, bottom), weight, color);
		for (var y = MathF.Floor(top / screenH) * screenH; y <= bottom; y += screenH)
			batcher.Line(new Vector2(left, y), new Vector2(right, y), weight, color);
	}

	private void DrawRoomActorOverlays(Room room, float lineWeight, int hoveredIndex, Vector2 worldMouse)
	{
		DrawRevealDoorAreaPreview(room, lineWeight, hoveredIndex);

		for (var i = 0; i < room.Actors.Count; i++)
		{
			var selected = i == selectedRoomActorIndex;
			if ((DragKind is EditorDragKind.RoomActor or EditorDragKind.ActorResize) && !selected)
				continue;

			var hovered = i == hoveredIndex;
			var bounds = RoomActorBounds(room, room.Actors[i]);
			if (!selected && !hovered && !bounds.Inflate(bounds.Width * 1.5f, bounds.Height * 1.5f).Contains(worldMouse))
				continue;

			var color = selected ? highlight : hovered ? Color.White : Color.Gray;
			batcher.RectLine(bounds.Inflate(lineWeight * 2), lineWeight * 2, color);

			if (selected && IsResizableRoomRectActor(room.Actors[i]))
				DrawHiddenWallResizeHandles(bounds, lineWeight);
		}
	}

	private void DrawRevealDoorAreaPreview(Room room, float lineWeight, int hoveredIndex)
	{
		var show = selectedRoomActorIndex >= 0 &&
				   selectedRoomActorIndex < room.Actors.Count &&
				   IsRevealDoorActor(room.Actors[selectedRoomActorIndex]);
		show |= hoveredIndex >= 0 &&
				hoveredIndex < room.Actors.Count &&
				IsRevealDoorActor(room.Actors[hoveredIndex]);
		if (!show)
			return;

		var cells = HiddenCoverGrid.ComputePreviewCoverTiles(room);
		if (cells.Count <= 0)
			return;

		var filled = new bool[room.Size.X, room.Size.Y];
		foreach (var c in cells)
			filled[c.X, c.Y] = true;

		var color = highlight * 0.9f;
		var width = MathF.Max(1, lineWeight * 2);
		var dash = MathF.Max(2, Game.TileSize * 0.25f);
		var origin = (Vector2)room.WorldBounds.TopLeft;

		for (var y = 0; y < room.Size.Y; y++)
			for (var x = 0; x < room.Size.X; x++)
			{
				if (!filled[x, y])
					continue;

				var left = origin.X + x * Game.TileSize;
				var top = origin.Y + y * Game.TileSize;
				var right = left + Game.TileSize;
				var bottom = top + Game.TileSize;

				if (!Filled(x, y - 1))
					batcher.LineDashed(new Vector2(left, top), new Vector2(right, top), width, color, dash, 0);
				if (!Filled(x, y + 1))
					batcher.LineDashed(new Vector2(right, bottom), new Vector2(left, bottom), width, color, dash, 0);
				if (!Filled(x - 1, y))
					batcher.LineDashed(new Vector2(left, bottom), new Vector2(left, top), width, color, dash, 0);
				if (!Filled(x + 1, y))
					batcher.LineDashed(new Vector2(right, top), new Vector2(right, bottom), width, color, dash, 0);
			}

		bool Filled(int x, int y)
			=> x >= 0 && y >= 0 && x < room.Size.X && y < room.Size.Y && filled[x, y];
	}

	private void DrawHiddenWallResizeHandles(RectInt bounds, float lineWeight)
	{
		var size = HiddenWallHandleSize(lineWeight);
		foreach (var handle in Enum.GetValues<ResizeHandle>())
		{
			if (handle == ResizeHandle.None)
				continue;

			var rect = HiddenWallHandleRect(bounds, handle, size);
			var color = DragKind == EditorDragKind.ActorResize && editorDrag.ResizeHandle == handle ? Color.White : highlight;
			batcher.Rect(rect, Color.Black * 0.75f);
			batcher.RectLine(rect, lineWeight * 2, color);
		}
	}

	private void DrawRoomGeometryHandles(Room room, float lineWeight)
	{
		var bounds = room.WorldBounds;
		var color = DragKind == EditorDragKind.RoomGeometry ? Color.White : highlight;
		batcher.RectLine(bounds.Inflate(lineWeight * 3), lineWeight * 2, color);

		var size = HiddenWallHandleSize(lineWeight);
		foreach (var handle in Enum.GetValues<ResizeHandle>())
		{
			if (handle == ResizeHandle.None)
				continue;

			var rect = HiddenWallHandleRect(bounds, handle, size);
			var handleColor = DragKind == EditorDragKind.RoomGeometry && editorDrag.RoomGeometryHandle == handle ? Color.White : highlight;
			batcher.Rect(rect, Color.Black * 0.75f);
			batcher.RectLine(rect, lineWeight * 2, handleColor);
		}
	}

	private ResizeHandle HitTestHiddenWallResizeHandle(Room room, Vector2 world, float lineWeight)
	{
		if (selectedRoomActorIndex < 0 || selectedRoomActorIndex >= room.Actors.Count)
			return ResizeHandle.None;

		var actor = room.Actors[selectedRoomActorIndex];
		if (!IsResizableRoomRectActor(actor))
			return ResizeHandle.None;

		var bounds = RoomActorBounds(room, actor);
		var size = HiddenWallHandleSize(lineWeight);
		foreach (var handle in new[]
				 {
					 ResizeHandle.TopLeft,
					 ResizeHandle.TopRight,
					 ResizeHandle.BottomLeft,
					 ResizeHandle.BottomRight,
					 ResizeHandle.Left,
					 ResizeHandle.Right,
					 ResizeHandle.Top,
					 ResizeHandle.Bottom
				 })
		{
			if (HiddenWallHandleRect(bounds, handle, size).Contains(world))
				return handle;
		}

		return ResizeHandle.None;
	}

	private ResizeHandle HitTestRoomGeometryHandle(Room room, Vector2 world, float lineWeight)
	{
		var bounds = room.WorldBounds;
		var size = HiddenWallHandleSize(lineWeight);
		foreach (var handle in new[]
				 {
					 ResizeHandle.TopLeft,
					 ResizeHandle.TopRight,
					 ResizeHandle.BottomLeft,
					 ResizeHandle.BottomRight,
					 ResizeHandle.Left,
					 ResizeHandle.Right,
					 ResizeHandle.Top,
					 ResizeHandle.Bottom
				 })
		{
			if (HiddenWallHandleRect(bounds, handle, size).Contains(world))
				return handle;
		}

		return ResizeHandle.None;
	}

	private static float HiddenWallHandleSize(float lineWeight)
		=> MathF.Max(4, lineWeight * 8);

	private static Rect HiddenWallHandleRect(RectInt bounds, ResizeHandle handle, float size)
	{
		var half = size * 0.5f;
		var x = handle switch
		{
			ResizeHandle.Left or ResizeHandle.TopLeft or ResizeHandle.BottomLeft => bounds.X,
			ResizeHandle.Right or ResizeHandle.TopRight or ResizeHandle.BottomRight => bounds.X + bounds.Width,
			_ => bounds.X + bounds.Width * 0.5f
		};
		var y = handle switch
		{
			ResizeHandle.Top or ResizeHandle.TopLeft or ResizeHandle.TopRight => bounds.Y,
			ResizeHandle.Bottom or ResizeHandle.BottomLeft or ResizeHandle.BottomRight => bounds.Y + bounds.Height,
			_ => bounds.Y + bounds.Height * 0.5f
		};
		return new Rect(x - half, y - half, size, size);
	}

	private void DrawPlaytestPlayerMarker(Room room)
	{
		if (playtestPlayerPosition is not { } remembered || remembered.RoomId != room.Id)
			return;

		var sprite = Assets.GetSprite("player");
		if (sprite == null || sprite.Frames.Count <= 0)
			return;

		batcher.Image(sprite.Frames[0].Subtexture, remembered.Position, sprite.Origin, Vector2.One, 0, Color.White * 0.25f);
	}

	private void MoveRoomActorTo(Room room, int actorIndex, Vector2 world, bool snap)
	{
		if (actorIndex < 0 || actorIndex >= room.Actors.Count)
			return;

		var actor = room.Actors[actorIndex];
		var offset = GetRoomActorPaletteOffset(actor);

		var targetLocal = world - editorDrag.ActorOffset - (Vector2)room.WorldBounds.TopLeft;
		var local = snap
			? SnapRoomActorLocalPosition(targetLocal, offset)
			: (Point2)targetLocal;
		actor["Position"] = new JsonArray(local.X, local.Y);
		editorGameDirty = true;
	}

	private static Point2 GetRoomActorPaletteOffset(JsonObject actor)
	{
		var specId = ActorSpecs.ReadString(actor, "From");
		var typeName = ActorSpecs.ReadString(actor, "Type");
		if (specId == null && typeName == null)
			return Point2.Zero;

		foreach (var entry in Factory.ActorPalette)
		{
			if ((specId != null && entry.ActorSpecId == specId) ||
				(typeName != null && entry.ActorTypeName == typeName && entry.ActorSpecId == null))
				return entry.Offset;
		}

		return Point2.Zero;
	}

	private static bool RoomActorPositionIsSnapped(JsonObject actor)
	{
		var position = ActorSpecs.ReadPoint(actor, "Position") ?? Point2.Zero;
		return SnapRoomActorLocalPosition(position, GetRoomActorPaletteOffset(actor)) == position;
	}

	private bool RoomActorDragShouldSnap(bool keyboardBlocked)
	{
		var shiftDown = !keyboardBlocked && Input.Keyboard.Shift;
		return editorDrag.ActorSnapsByDefault ? !shiftDown : shiftDown;
	}

	private EditorDragKind DragKind => editorDrag.Kind;
	private bool TilePlacing => DragKind == EditorDragKind.TilePaint;

	private void ClearEditorDragState()
	{
		editorDrag = EditorDragState.None;
	}

	private void CancelTilePaint()
	{
		if (DragKind == EditorDragKind.TilePaint)
			ClearEditorDragState();
		tileTypePlacing = brush;
	}

	private void CommitEditorDragState()
	{
		ClearEditorDragState();
		actorMoveUndo?.Commit();
		actorMoveUndo = null;
		roomGeometryUndo?.Commit();
		roomGeometryUndo = null;
	}

	private static Point2 SnapRoomActorLocalPosition(Vector2 local, Point2 offset)
	{
		var adjusted = local - (Vector2)offset;
		return new Point2(
			(int)MathF.Floor(adjusted.X / Game.TileSize) * Game.TileSize + offset.X,
			(int)MathF.Floor(adjusted.Y / Game.TileSize) * Game.TileSize + offset.Y);
	}

	private void ResizeHiddenWallActor(Room room, int actorIndex, ResizeHandle handle, Vector2 world, Point2 tile, bool snap)
	{
		if (actorIndex < 0 || actorIndex >= room.Actors.Count || handle == ResizeHandle.None)
			return;

		var actor = room.Actors[actorIndex];
		if (!IsResizableRoomRectActor(actor))
			return;

		var resolved = ActorSpecs.Resolve(actor);
		var position = ActorSpecs.ReadPoint(actor, "Position") ?? Point2.Zero;
		var size = ActorSpecs.ReadPoint(resolved, "Size") ?? ResizableRoomRectDefaultSize(actor);
		var cursor = snap ? tile * Game.TileSize : (Point2)(world - (Vector2)room.WorldBounds.TopLeft);

		var left = position.X;
		var top = position.Y;
		var right = position.X + size.X;
		var bottom = position.Y + size.Y;

		switch (handle)
		{
			case ResizeHandle.Left:
			case ResizeHandle.TopLeft:
			case ResizeHandle.BottomLeft:
				left = cursor.X;
				break;
			case ResizeHandle.Right:
			case ResizeHandle.TopRight:
			case ResizeHandle.BottomRight:
				right = cursor.X;
				break;
		}

		switch (handle)
		{
			case ResizeHandle.Top:
			case ResizeHandle.TopLeft:
			case ResizeHandle.TopRight:
				top = cursor.Y;
				break;
			case ResizeHandle.Bottom:
			case ResizeHandle.BottomLeft:
			case ResizeHandle.BottomRight:
				bottom = cursor.Y;
				break;
		}

		var roomWidth = room.Size.X * Game.TileSize;
		var roomHeight = room.Size.Y * Game.TileSize;
		left = Math.Clamp(left, 0, roomWidth - Game.TileSize);
		top = Math.Clamp(top, 0, roomHeight - Game.TileSize);
		right = Math.Clamp(right, Game.TileSize, roomWidth);
		bottom = Math.Clamp(bottom, Game.TileSize, roomHeight);

		if (right - left < Game.TileSize)
		{
			if (handle is ResizeHandle.Left or ResizeHandle.TopLeft or ResizeHandle.BottomLeft)
				left = right - Game.TileSize;
			else
				right = left + Game.TileSize;
		}

		if (bottom - top < Game.TileSize)
		{
			if (handle is ResizeHandle.Top or ResizeHandle.TopLeft or ResizeHandle.TopRight)
				top = bottom - Game.TileSize;
			else
				bottom = top + Game.TileSize;
		}

		left = Math.Clamp(left, 0, roomWidth - Game.TileSize);
		top = Math.Clamp(top, 0, roomHeight - Game.TileSize);
		right = Math.Clamp(right, left + Game.TileSize, roomWidth);
		bottom = Math.Clamp(bottom, top + Game.TileSize, roomHeight);

		actor["Position"] = new JsonArray(left, top);
		actor["Size"] = new JsonArray(right - left, bottom - top);
		editorGameDirty = true;
	}

	private void ApplyRoomGeometryDrag(Room room, Vector2 world)
	{
		var mouseTile = WorldToRoomGeometryTile(world);
		var current = new RectInt(room.TileSpacePosition, room.Size);
		var candidate = editorDrag.RoomGeometryHandle == ResizeHandle.None
			? new RectInt(mouseTile - editorDrag.RoomGeometryOffset, room.Size)
			: ResizeRoomGeometryBounds(current, editorDrag.RoomGeometryHandle, mouseTile);

		if (candidate.Width <= 0 || candidate.Height <= 0 || RoomGeometryOverlaps(room, candidate))
			return;

		if (candidate.Position == room.TileSpacePosition && candidate.Size == room.Size)
			return;

		if (candidate.Size == room.Size)
			MoveRoomGeometry(room, candidate.Position);
		else
			ResizeRoomGeometry(room, candidate);

		selectedRoomId = room.Id;
		cell = room.Cell;
		roomCreatePosition = room.TileSpacePosition;
		editorGameDirty = true;
		InvalidateRoomPreview(room);
	}

	private static Point2 WorldToRoomGeometryTile(Vector2 world)
		=> new(
			(int)MathF.Floor(world.X / Game.TileSize),
			(int)MathF.Floor(world.Y / Game.TileSize));

	private static RectInt ResizeRoomGeometryBounds(RectInt bounds, ResizeHandle handle, Point2 mouseTile)
	{
		var left = bounds.Left;
		var top = bounds.Top;
		var right = bounds.Right;
		var bottom = bounds.Bottom;

		if (handle is ResizeHandle.Left or ResizeHandle.TopLeft or ResizeHandle.BottomLeft)
			left = Math.Min(mouseTile.X, right - 1);
		if (handle is ResizeHandle.Right or ResizeHandle.TopRight or ResizeHandle.BottomRight)
			right = Math.Max(mouseTile.X + 1, left + 1);
		if (handle is ResizeHandle.Top or ResizeHandle.TopLeft or ResizeHandle.TopRight)
			top = Math.Min(mouseTile.Y, bottom - 1);
		if (handle is ResizeHandle.Bottom or ResizeHandle.BottomLeft or ResizeHandle.BottomRight)
			bottom = Math.Max(mouseTile.Y + 1, top + 1);

		return new RectInt(left, top, right - left, bottom - top);
	}

	private static bool RoomGeometryOverlaps(Room editing, RectInt candidate)
	{
		foreach (var room in Assets.RoomsById.Values)
		{
			if (room == editing)
				continue;
			if (candidate.Overlaps(new RectInt(room.TileSpacePosition, room.Size)))
				return true;
		}

		return false;
	}

	private static void MoveRoomGeometry(Room room, Point2 position)
	{
		var oldCell = room.Cell;
		room.MoveTo(position);
		UpdateRoomCellIndex(room, oldCell);
	}

	private static void ResizeRoomGeometry(Room room, RectInt bounds)
	{
		var oldCell = room.Cell;
		room.ResizeTo(bounds);
		UpdateRoomCellIndex(room, oldCell);
	}

	private static void UpdateRoomCellIndex(Room room, Point2 oldCell)
	{
		if (Assets.Rooms.TryGetValue(oldCell, out var indexed) && indexed == room)
			Assets.Rooms.Remove(oldCell);
		Assets.Rooms[room.Cell] = room;
	}

	private void StampTiles(Room room, Point2 origin, int shift, int size, char tile)
	{
		for (var x = shift; x < shift + size; x++)
			for (var y = shift; y < shift + size; y++)
			{
				if (activeTileLayer == Factory.TileLayer.Background)
					room.SetBackground(origin + new Point2(x, y), tile, rebuildGeometry: false);
				else
					room.Set(origin + new Point2(x, y), tile, rebuildGeometry: false);
			}
		room.RebuildGeometry();
		editorGameDirty = true;
	}

	private static RectInt NormalizeTileRect(Point2 a, Point2 b)
	{
		var left = Math.Min(a.X, b.X);
		var top = Math.Min(a.Y, b.Y);
		var right = Math.Max(a.X, b.X);
		var bottom = Math.Max(a.Y, b.Y);
		return new RectInt(left, top, right - left + 1, bottom - top + 1);
	}

	private static RectInt ClampTileRect(RectInt rect, Room room)
	{
		var left = Math.Clamp(rect.Left, 0, room.Size.X);
		var top = Math.Clamp(rect.Top, 0, room.Size.Y);
		var right = Math.Clamp(rect.Right, 0, room.Size.X);
		var bottom = Math.Clamp(rect.Bottom, 0, room.Size.Y);
		return new RectInt(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
	}

	private static Point2 ClampTile(Room room, Point2 tile)
		=> new(Math.Clamp(tile.X, 0, Math.Max(0, room.Size.X - 1)), Math.Clamp(tile.Y, 0, Math.Max(0, room.Size.Y - 1)));

	private static bool TileRectContains(RectInt rect, Point2 tile)
		=> tile.X >= rect.Left && tile.X < rect.Right && tile.Y >= rect.Top && tile.Y < rect.Bottom;

	private static bool TileRectOverlaps(RectInt a, RectInt b)
		=> a.Width > 0 && a.Height > 0 && b.Width > 0 && b.Height > 0 &&
		   a.Left < b.Right && a.Right > b.Left && a.Top < b.Bottom && a.Bottom > b.Top;

	private static Rect TileRectToRoomWorldRect(Room room, RectInt rect)
		=> (new Rect(rect.X, rect.Y, rect.Width, rect.Height) * Game.TileSize) + (Vector2)room.WorldBounds.TopLeft;

	private static RectInt WorldRectToRoomTileRect(Room room, Rect world)
	{
		var roomTopLeft = (Vector2)room.WorldBounds.TopLeft;
		var local = new Rect(world.X - roomTopLeft.X, world.Y - roomTopLeft.Y, world.Width, world.Height);
		var left = (int)MathF.Floor(local.Left / Game.TileSize);
		var top = (int)MathF.Floor(local.Top / Game.TileSize);
		var right = (int)MathF.Ceiling(local.Right / Game.TileSize);
		var bottom = (int)MathF.Ceiling(local.Bottom / Game.TileSize);
		return ClampTileRect(new RectInt(left, top, right - left, bottom - top), room);
	}

	private void ApplyTileRect(Room room, RectInt rect, Factory.TileLayer layer, char tile)
	{
		rect = ClampTileRect(rect, room);
		for (var y = rect.Top; y < rect.Bottom; y++)
			for (var x = rect.Left; x < rect.Right; x++)
			{
				if (layer == Factory.TileLayer.Background)
					room.SetBackground(new Point2(x, y), tile, rebuildGeometry: false);
				else
					room.Set(new Point2(x, y), tile, rebuildGeometry: false);
			}

		room.RebuildGeometry();
		editorGameDirty = true;
	}

	private EditorSelectionSnapshot CaptureEditorSelection(Room room)
	{
		var rect = ClampTileRect(editorSelectionRect ?? new RectInt(), room);
		var snapshot = new EditorSelectionSnapshot
		{
			Rect = rect,
			IncludeBackgroundTiles = selectionIncludeBackgroundTiles,
			IncludeForegroundTiles = selectionIncludeForegroundTiles,
			IncludeActors = selectionIncludeActors,
			RoomJson = room.ToJsonString()
		};

		if (snapshot.IncludeBackgroundTiles)
			snapshot.BackgroundTiles = CaptureTiles(room.BackgroundTiles, rect);
		if (snapshot.IncludeForegroundTiles)
			snapshot.ForegroundTiles = CaptureTiles(room.Tiles, rect);
		if (snapshot.IncludeActors)
		{
			var worldRect = TileRectToRoomWorldRect(room, rect);
			for (var i = 0; i < room.Actors.Count; i++)
			{
				if (!RoomActorBounds(room, room.Actors[i]).Overlaps(worldRect))
					continue;

				var actor = ActorSpecs.CloneObject(room.Actors[i]);
				var position = ActorSpecs.ReadPoint(actor, "Position") ?? Point2.Zero;
				snapshot.Actors.Add((i, actor, position));
			}
		}

		return snapshot;
	}

	private static char[,] CaptureTiles(char[,] source, RectInt rect)
	{
		var result = new char[rect.Width, rect.Height];
		for (var y = 0; y < rect.Height; y++)
			for (var x = 0; x < rect.Width; x++)
				result[x, y] = source[rect.Left + x, rect.Top + y];
		return result;
	}

	private void ApplyEditorSelectionSnapshot(Room room, EditorSelectionSnapshot snapshot, Point2 offset, bool duplicate)
	{
		room.LoadJsonState(snapshot.RoomJson);
		var destination = ClampSelectionDestination(room, snapshot.Rect, offset);
		offset = destination.Position - snapshot.Rect.Position;

		if (!duplicate)
			ClearEditorSelectionSource(room, snapshot);

		if (snapshot.BackgroundTiles != null)
			WriteTiles(room, snapshot.BackgroundTiles, destination.Position, Factory.TileLayer.Background);
		if (snapshot.ForegroundTiles != null)
			WriteTiles(room, snapshot.ForegroundTiles, destination.Position, Factory.TileLayer.Foreground);

		if (snapshot.IncludeActors)
		{
			if (duplicate)
			{
				foreach (var (_, actor, position) in snapshot.Actors)
				{
					var copy = ActorSpecs.CloneObject(actor);
					copy["Position"] = new JsonArray(position.X + offset.X * Game.TileSize, position.Y + offset.Y * Game.TileSize);
					ActorSpecs.AssignNewActorId(copy);
					room.Actors.Add(copy);
				}
			}
			else
			{
				foreach (var (index, _, position) in snapshot.Actors)
					if (index >= 0 && index < room.Actors.Count)
						room.Actors[index]["Position"] = new JsonArray(position.X + offset.X * Game.TileSize, position.Y + offset.Y * Game.TileSize);
			}
		}

		room.RebuildGeometry();
		editorSelectionRect = destination;
		selectedRoomActorIndex = -1;
		editorGameDirty = true;
	}

	private void ClearEditorSelectionSource(Room room, EditorSelectionSnapshot snapshot)
	{
		if (snapshot.BackgroundTiles != null)
			ClearTiles(room, snapshot.Rect, Factory.TileLayer.Background);
		if (snapshot.ForegroundTiles != null)
			ClearTiles(room, snapshot.Rect, Factory.TileLayer.Foreground);
	}

	private static RectInt ClampSelectionDestination(Room room, RectInt source, Point2 offset)
	{
		var maxX = Math.Max(0, room.Size.X - source.Width);
		var maxY = Math.Max(0, room.Size.Y - source.Height);
		var x = Math.Clamp(source.X + offset.X, 0, maxX);
		var y = Math.Clamp(source.Y + offset.Y, 0, maxY);
		return new RectInt(x, y, source.Width, source.Height);
	}

	private void WriteTiles(Room room, char[,] tiles, Point2 destination, Factory.TileLayer layer)
	{
		var width = tiles.GetLength(0);
		var height = tiles.GetLength(1);
		for (var y = 0; y < height; y++)
			for (var x = 0; x < width; x++)
			{
				var tile = destination + new Point2(x, y);
				if (layer == Factory.TileLayer.Background)
					room.SetBackground(tile, tiles[x, y], rebuildGeometry: false);
				else
					room.Set(tile, tiles[x, y], rebuildGeometry: false);
			}
	}

	private void ClearTiles(Room room, RectInt rect, Factory.TileLayer layer)
	{
		for (var y = rect.Top; y < rect.Bottom; y++)
			for (var x = rect.Left; x < rect.Right; x++)
			{
				if (layer == Factory.TileLayer.Background)
					room.SetBackground(new Point2(x, y), '0', rebuildGeometry: false);
				else
					room.Set(new Point2(x, y), '0', rebuildGeometry: false);
			}
	}

	private static bool TryGetSlopeToggle(char tile, out char result)
	{
		result = tile switch
		{
			'1' => '2',
			'2' => '1',
			'G' => 'S',
			'S' => 'G',
			'T' => 'Y',
			'Y' => 'T',
			_ => '\0'
		};
		return result != '\0';
	}

	private static Subtexture GetPreviewTileImage(Room room, Point2 tile, char previewTile, Factory.Entry entry)
	{
		if (!entry.IsSlope || entry.SlopeTilesetName == null)
			return entry.Image;

		var previous = room.Tiles[tile.X, tile.Y];
		room.Tiles[tile.X, tile.Y] = previewTile;
		room.RebuildGeometry();
		var seed = Factory.StableVariantSeed(room, entry.SlopeTilesetName, tile);
		var image = room.TryGetSlopeTile(tile, out var slope)
			? Assets.GetSlopeSubtexture(entry.SlopeTilesetName, slope.Direction, slope.Fill, slope.RunLength, slope.RunIndex, seed)
			: entry.Image;
		room.Tiles[tile.X, tile.Y] = previous;
		room.RebuildGeometry();
		return image.Texture != null ? image : entry.Image;
	}

	private void UpdateEditorWorldView(Rect inner)
	{
		var cursor = Cursor;
		if (UpdateEditorTouchView(inner))
			return;

		var canInteract = !imgui.WantsMouseInput && inner.Contains(cursor);

		if (canInteract && Input.Mouse.MiddleDown)
		{
			editorCameraTween = 1.0f;
			editorZoomTarget = editorZoom;
			editorCamera -= Input.Mouse.Delta / Scale / editorZoom;
		}

		if (canInteract)
		{
			var wheel = Input.Mouse.Wheel;
			var useHorizontalWheel = MathF.Abs(wheel.X) > MathF.Abs(wheel.Y);
			var wheelAmount = useHorizontalWheel ? 0 : wheel.Y;
			if (wheelAmount != 0)
			{
				editorCameraTween = 1.0f;
				editorZoomTarget = Math.Clamp(editorZoomTarget * MathF.Pow(1.15f, wheelAmount), 0.15f, 8.0f);
			}
		}

		UpdateEditorZoomSmoothing(canInteract ? cursor : inner.Center, inner);
	}

	private bool UpdateEditorTouchView(Rect inner)
	{
		var touch = Input.Touch;
		if (touch.DownCount < 2)
			return false;

		var center = touch.Center / Scale;
		if (!inner.Contains(center))
			return false;

		editorCameraTween = 1.0f;
		var centerDelta = touch.CenterDelta / Scale;
		editorCamera -= centerDelta / editorZoom;

		var zoomScale = touch.PinchScale;
		if (zoomScale <= 0 || MathF.Abs(zoomScale - 1.0f) < 0.0001f)
			zoomScale = TouchDistanceScale(touch);

		if (zoomScale > 0 && MathF.Abs(zoomScale - 1.0f) > 0.0001f)
		{
			var before = ScreenToEditorWorld(center, inner);
			editorZoomTarget = Math.Clamp(editorZoomTarget * zoomScale, 0.15f, 8.0f);
			editorZoom = editorZoomTarget;
			editorCamera = before - (center - inner.Center) / editorZoom;
		}

		return true;
	}

	private bool SingleTouchInEditorWorld(Rect inner)
	{
		var touch = Input.Touch;
		return touch.DownCount == 1 && inner.Contains(touch.Center / Scale);
	}

	private static float TouchDistanceScale(TouchState touch)
	{
		TouchState.Finger? first = null;
		TouchState.Finger? second = null;
		for (var i = 0; i < touch.Count; i++)
		{
			var finger = touch[i];
			if (!finger.Down)
				continue;
			if (first == null)
				first = finger;
			else
			{
				second = finger;
				break;
			}
		}

		if (first is not { } a || second is not { } b)
			return 1.0f;

		var current = Vector2.Distance(a.Position, b.Position);
		var previous = Vector2.Distance(a.Position - a.Delta, b.Position - b.Delta);
		return previous > 0.001f ? current / previous : 1.0f;
	}

	private void UpdateEditorZoomSmoothing(Vector2 screenAnchor, Rect inner)
	{
		if (MathF.Abs(editorZoomTarget - editorZoom) <= 0.0001f)
		{
			editorZoom = editorZoomTarget;
			return;
		}

		var before = ScreenToEditorWorld(screenAnchor, inner);
		editorZoom = Calc.Approach(editorZoom, editorZoomTarget, Time.Delta * MathF.Max(editorZoom, editorZoomTarget) * 18.0f);
		editorCamera = before - (screenAnchor - inner.Center) / editorZoom;
	}

	private Vector2 ScreenToEditorWorld(Vector2 screen, Rect inner)
		=> editorCamera + (screen - inner.Center) / editorZoom;

	private void UpdateAutoSelectedRoomFromEditorCamera(Rect inner)
	{
		if (!AutoSelectRoomByCamera || editorCameraTween < 1.0f)
			return;

		var worldView = new Rect(
			editorCamera - inner.Size / (editorZoom * 2),
			inner.Size / editorZoom);
		var viewArea = worldView.Area;
		if (viewArea <= 0)
			return;

		Room? bestRoom = null;
		var bestCoverage = 0.0f;
		foreach (var room in Assets.RoomsById.Values)
		{
			var bounds = room.WorldBounds;
			var roomRect = new Rect(bounds.X, bounds.Y, bounds.Width, bounds.Height);
			var coverage = worldView.GetIntersection(roomRect).Area / viewArea;
			if (coverage > bestCoverage)
			{
				bestCoverage = coverage;
				bestRoom = room;
			}
		}

		if (bestRoom == null || bestRoom == CurrentRoom || bestCoverage < 0.65f)
			return;

		CurrentRoom?.Save();
		SelectRoom(bestRoom);
		selectedRoomActorIndex = -1;
		editorRoomEntryDirection = null;
		SyncEditorGame();
	}

	private void DrawEditorRooms(Rect visibleWorld)
	{
		using var perf = PerfStats.Time(PerfDetail.DrawEditorRooms);
		BuildQueuedRoomPreview();
		foreach (var room in Assets.RoomsById.Values)
		{
			var bounds = room.WorldBounds;
			if (visibleWorld.GetIntersection(new Rect(bounds.X, bounds.Y, bounds.Width, bounds.Height)).Area <= 0)
				continue;

			var line = MathF.Max(1.0f / editorZoom, LineWeight / editorZoom);

			if (GetEditorRoomTexture(room) is { } preview)
				DrawEditorRoomPreview(preview, bounds);
			else
				DrawEditorRoomBackdrop(room, bounds);
			batcher.RectLine(bounds, line, Color.Gray);
		}
	}

	private void DrawEditorRoomBackdrop(Room room, RectInt bounds)
	{
		var top = room.BackdropColor;
		var bottom = room.BackdropColor2 ?? top;
		batcher.Rect(bounds, top, top, bottom, bottom);
	}

	private void DrawEditorRoomPreview(Texture preview, RectInt bounds)
	{
		var scale = new Vector2(
			bounds.Width / (float)Math.Max(1, preview.Width),
			bounds.Height / (float)Math.Max(1, preview.Height));
		batcher.Image(preview, bounds.TopLeft, Vector2.Zero, scale, 0, Color.White);
	}

	private Texture? GetEditorRoomTexture(Room room)
		=> room == CurrentRoom && game != null ? GetActiveRoomPreview(room) : GetRoomPreview(room);

	private Texture GetActiveRoomPreview(Room room)
	{
		using var perf = PerfStats.Time(PerfDetail.ActiveRoomPreview);
		var bounds = room.WorldBounds;
		if (activeRoomPreviewTarget == null ||
			activeRoomPreviewId != room.Id ||
			activeRoomPreviewTarget.Width != bounds.Width ||
			activeRoomPreviewTarget.Height != bounds.Height)
		{
			DisposeRoomPreview(activeRoomPreviewTarget, defer: false);
			activeRoomPreviewTarget = new Target(GraphicsDevice, bounds.Width, bounds.Height, name: $"ActiveRoomPreview/{room.Id}");
			activeRoomPreviewId = room.Id;
		}

		if (game != null)
		{
			game.ShowForegroundLayer = editorShowForegroundLayer;
			game.ShowBackgroundLayer = editorShowBackgroundLayer;
			game.RenderWorldToTarget(activeRoomPreviewTarget, new Rect(bounds.X, bounds.Y, bounds.Width, bounds.Height));
			game.ShowForegroundLayer = true;
			game.ShowBackgroundLayer = true;
		}
		return activeRoomPreviewTarget.Attachments[0];
	}

	private Texture? GetRoomPreview(Room room)
	{
		EditorRoomPreview? entry;
		var version = GetRoomPreviewVersion(room);
		using (PerfStats.Time(PerfDetail.RoomPreviewCacheCheck))
		{
			if (roomPreviewCache.TryGetValue(room.Id, out entry) &&
				IsRoomPreviewCurrent(room, entry, version))
				return entry.Texture;
		}

		EnqueueRoomPreview(room);
		if (entry != null)
			return entry.Texture;
		return null;
	}

	private void BuildQueuedRoomPreview()
	{
		while (roomPreviewBuildQueue.Count > 0)
		{
			var roomId = roomPreviewBuildQueue.Dequeue();
			queuedRoomPreviewIds.Remove(roomId);
			if (!Assets.RoomsById.TryGetValue(roomId, out var room))
				continue;

			var version = GetRoomPreviewVersion(room);
			if (roomPreviewCache.TryGetValue(room.Id, out var cached) && IsRoomPreviewCurrent(room, cached, version))
				continue;

			BuildRoomPreview(room, version);
			return;
		}
	}

	private bool IsRoomPreviewCurrent(Room room, EditorRoomPreview entry, int version)
	{
		var previewSize = GetRoomPreviewPixelSize(room);
		return
			entry.Version == version &&
			entry.ShowForegroundLayer == editorShowForegroundLayer &&
			entry.ShowBackgroundLayer == editorShowBackgroundLayer &&
			entry.Target.Width == previewSize.X &&
			entry.Target.Height == previewSize.Y;
	}

	private void EnqueueRoomPreview(Room room)
	{
		if (queuedRoomPreviewIds.Add(room.Id))
			roomPreviewBuildQueue.Enqueue(room.Id);
	}

	private void BuildRoomPreview(Room room, int version)
	{
		using var perf = PerfStats.Time(PerfDetail.RoomPreviewBuild);
		if (roomPreviewCache.Remove(room.Id, out var entry))
			DisposeRoomPreview(entry, defer: false);

		var previewSize = GetRoomPreviewPixelSize(room);
		var target = new Target(GraphicsDevice, previewSize.X, previewSize.Y, name: $"RoomPreview/{room.Id}");
		using var previewGame = new Game(this, room, audioEventsEnabled: false, spawnBud: false);
		previewGame.ShowForegroundLayer = editorShowForegroundLayer;
		previewGame.ShowBackgroundLayer = editorShowBackgroundLayer;
		previewGame.RenderWorldToTarget(target, new Rect(room.WorldBounds.X, room.WorldBounds.Y, room.WorldBounds.Width, room.WorldBounds.Height));
		roomPreviewCache[room.Id] = new EditorRoomPreview(version, editorShowForegroundLayer, editorShowBackgroundLayer, target);
	}

	private Point2 GetRoomPreviewPixelSize(Room room)
	{
		var bounds = room.WorldBounds;
		var scale = Math.Clamp(roomPreviewResolutionScale, 0.05f, 1.0f);
		return new Point2(
			Math.Max(1, (int)MathF.Ceiling(bounds.Width * scale)),
			Math.Max(1, (int)MathF.Ceiling(bounds.Height * scale)));
	}

	private void ClearRoomPreviewCache(bool deferDisposal = true)
	{
		foreach (var preview in roomPreviewCache.Values)
			DisposeRoomPreview(preview, deferDisposal);
		roomPreviewCache.Clear();
		roomPreviewBuildQueue.Clear();
		queuedRoomPreviewIds.Clear();
		roomPreviewVersions.Clear();
		nextRoomPreviewVersion = 1;
		DisposeRoomPreview(activeRoomPreviewTarget, deferDisposal);
		activeRoomPreviewTarget = null;
		activeRoomPreviewId = null;
	}

	private int GetRoomPreviewVersion(Room room)
	{
		if (!roomPreviewVersions.TryGetValue(room.Id, out var version))
		{
			version = nextRoomPreviewVersion++;
			roomPreviewVersions[room.Id] = version;
		}
		return version;
	}

	private void MarkRoomPreviewDirty(Room room, bool deferDisposal = true)
	{
		roomPreviewVersions[room.Id] = nextRoomPreviewVersion++;
		if (roomPreviewCache.Remove(room.Id, out var preview))
			DisposeRoomPreview(preview, deferDisposal);
	}

	private void InvalidateRoomPreview(Room room, bool deferDisposal = true, bool bumpVersion = true)
	{
		if (bumpVersion)
			roomPreviewVersions[room.Id] = nextRoomPreviewVersion++;

		if (roomPreviewCache.Remove(room.Id, out var preview))
			DisposeRoomPreview(preview, deferDisposal);

		if (activeRoomPreviewId == room.Id)
		{
			DisposeRoomPreview(activeRoomPreviewTarget, deferDisposal);
			activeRoomPreviewTarget = null;
			activeRoomPreviewId = null;
		}
	}

	private void DisposeRoomPreview(IDisposable? disposable, bool defer)
	{
		if (disposable == null)
			return;

		if (defer)
			deferredRoomPreviewDisposals.Add(disposable);
		else
			disposable.Dispose();
	}

	private void DisposeDeferredRoomPreviews()
	{
		foreach (var disposable in deferredRoomPreviewDisposals)
			disposable.Dispose();
		deferredRoomPreviewDisposals.Clear();
	}

	private sealed class EditorRoomPreview(int version, bool showForegroundLayer, bool showBackgroundLayer, Target target) : IDisposable
	{
		public readonly int Version = version;
		public readonly bool ShowForegroundLayer = showForegroundLayer;
		public readonly bool ShowBackgroundLayer = showBackgroundLayer;
		public readonly Target Target = target;
		public Texture Texture => Target.Attachments[0];

		public void Dispose()
			=> Target.Dispose();
	}

	private Room? GetRoomAtWorld(Vector2 world)
	{
		foreach (var room in Assets.RoomsById.Values)
			if (room.WorldBounds.Contains(world))
				return room;

		return null;
	}

	private bool UpdateLinkedDoorTargetPicker(Rect inner, Rect visibleWorld, bool worldMouseBlocked, bool keyboardBlocked)
	{
		if (pickingLinkedDoorTargetForActorId == null)
			return false;

		if (!keyboardBlocked && Input.Keyboard.Pressed(Keys.Escape))
		{
			pickingLinkedDoorTargetForActorId = null;
			return true;
		}

		DrawLinkedDoorTargetPickerOverlay(visibleWorld);

		var canInteract = !worldMouseBlocked && inner.Contains(Cursor);
		if (!canInteract || !Input.Mouse.LeftPressed)
			return true;

		var world = ScreenToEditorWorld(Cursor, inner);
		if (FindLinkedDoorAtWorld(world, pickingLinkedDoorTargetForActorId) is { } target &&
			Assets.FindActorById(pickingLinkedDoorTargetForActorId) is { } source)
		{
			using (CaptureRoomUndo("Set Linked Door Target", source.Room))
				ActorSpecs.SetProperty(source.Actor, nameof(LinkedDoor.TargetActorId), JsonValue.Create(ActorSpecs.ReadString(target.Actor, "Id") ?? ""));
			if (source.Room == CurrentRoom)
				editorGameDirty = true;
			else
				source.Room.Save();
			pickingLinkedDoorTargetForActorId = null;
		}

		return true;
	}

	private void DrawLinkedDoorTargetPickerOverlay(Rect visibleWorld)
	{
		var lineWeight = MathF.Max(1.0f / editorZoom, LineWeight / editorZoom);
		foreach (var room in Assets.RoomsById.Values)
		{
			if (visibleWorld.GetIntersection(new Rect(room.WorldBounds.X, room.WorldBounds.Y, room.WorldBounds.Width, room.WorldBounds.Height)).Area <= 0)
				continue;

			foreach (var actor in room.Actors)
			{
				if (!IsLinkedDoorActor(actor))
					continue;
				if (string.Equals(ActorSpecs.ReadString(actor, "Id"), pickingLinkedDoorTargetForActorId, StringComparison.OrdinalIgnoreCase))
					continue;

				var bounds = RoomActorBounds(room, actor).Inflate(lineWeight * 3);
				batcher.Rect(bounds, highlight * 0.18f);
				batcher.RectLine(bounds, lineWeight * 2, highlight);
			}
		}
	}

	private static (Room Room, JsonObject Actor)? FindLinkedDoorAtWorld(Vector2 world, string excludeId)
	{
		foreach (var room in Assets.RoomsById.Values)
		{
			if (!room.WorldBounds.Contains(world))
				continue;

			for (var i = room.Actors.Count - 1; i >= 0; i--)
			{
				var actor = room.Actors[i];
				if (!IsLinkedDoorActor(actor))
					continue;
				if (string.Equals(ActorSpecs.ReadString(actor, "Id"), excludeId, StringComparison.OrdinalIgnoreCase))
					continue;
				if (RoomActorBounds(room, actor).Contains(world))
					return (room, actor);
			}
		}

		return null;
	}

	private void DrawEditorEyedropperCursor(Rect tileRect, float lineWeight)
	{
		var width = lineWeight * 2;
		batcher.RectLine(tileRect, width, highlight);

		var start = tileRect.TopLeft + tileRect.Size * 0.25f;
		var end = tileRect.BottomRight - tileRect.Size * 0.20f;
		batcher.Line(start, end, width, Color.White);
		batcher.Rect(new Rect(start - Vector2.One * width, Vector2.One * width * 2), highlight);
		batcher.Rect(new Rect(end - Vector2.One * width * 0.75f, Vector2.One * width * 1.5f), Color.White);
	}

	private void DrawBreakableTileMarkers(Room room, Vector2 roomTopLeft, float lineWeight)
	{
		var color = new Color(1.0f, 0.82f, 0.24f, 0.2f);
		var tileSize = Game.TileSize - lineWeight * 4;
		for (var x = 0; x < room.Size.X; x++)
			for (var y = 0; y < room.Size.Y; y++)
			{
				var tile = new Point2(x, y);
				if (!room.IsBreakable(tile))
					continue;

				var rect = new Rect(
					roomTopLeft.X + x * Game.TileSize + lineWeight * 2,
					roomTopLeft.Y + y * Game.TileSize + lineWeight * 2, tileSize, tileSize);
				batcher.RectDashed(rect, lineWeight, color, 1, 5.5f);
			}
	}

	private static Point2? GetEditorEntryDirection(Room from, Room to)
	{
		if (to.TileSpacePosition.X >= from.TileSpacePosition.X + from.Size.X) return Point2.Right;
		if (to.TileSpacePosition.X + to.Size.X <= from.TileSpacePosition.X)   return Point2.Left;
		if (to.TileSpacePosition.Y >= from.TileSpacePosition.Y + from.Size.Y) return Point2.Down;
		if (to.TileSpacePosition.Y + to.Size.Y <= from.TileSpacePosition.Y)   return Point2.Up;
		return null;
	}

	private void Editor()
	{
		using var perf = PerfStats.Time(PerfDetail.EditorWorld);
		var inner = GameViewRect;
		var lastCell = cell;
		var worldMouseBlocked = imgui.WantsMouseInput && !SingleTouchInEditorWorld(inner);
		var keyboardBlocked = ImGuiWantsKeyboard;
		var roomActivationClickConsumed = false;
		UpdateEditorUndoRedo();
		// Shoulder buttons cycle worlds in edit mode (same as time speed in play mode)
		if (!worldMouseBlocked)
		{
			foreach (var controller in Input.Controllers)
			{
				if (!controller.Connected)
					continue;

				if (controller.Pressed(Buttons.LeftShoulder) || controller.Pressed(Buttons.RightShoulder))
				{
					var worlds = Assets.GetWorldIds();
					if (worlds.Count > 0)
					{
						var currentIdx = -1;
						for (var i = 0; i < worlds.Count; i++)
						{
							if (string.Equals(worlds[i], Assets.CurrentWorldId, StringComparison.OrdinalIgnoreCase))
							{
								currentIdx = i;
								break;
							}
						}

						if (currentIdx < 0)
							currentIdx = 0;

						var nextIdx = controller.Pressed(Buttons.LeftShoulder)
							? (currentIdx - 1 + worlds.Count) % worlds.Count
							: (currentIdx + 1) % worlds.Count;

						SwitchWorld(worlds[nextIdx]);
					}
					break;
				}
			}
		}

		if (!editorViewInitialized)
			FocusEditorOnCurrentRoom();
		UpdateEditorCameraTween();
		UpdateEditorWorldView(inner);
		if (!editingRoomGeometry)
			UpdateAutoSelectedRoomFromEditorCamera(inner);

		batcher.PushScissor((RectInt)(inner * Scale));
		var worldMatrix =
			Matrix3x2.CreateTranslation(-editorCamera) *
			Matrix3x2.CreateScale(editorZoom) *
			Matrix3x2.CreateTranslation(inner.Center);
		var visibleWorld = new Rect(
			editorCamera - inner.Size / (editorZoom * 2),
			inner.Size / editorZoom);
		batcher.PushMatrix(worldMatrix);
		DrawEditorRooms(visibleWorld);
		DrawEditorScreenGrid(visibleWorld);

		var eyedropper = !keyboardBlocked && Input.Keyboard.Alt;
		if (UpdateLinkedDoorTargetPicker(inner, visibleWorld, worldMouseBlocked, keyboardBlocked))
			worldMouseBlocked = true;

		if (!editingRoomGeometry && !worldMouseBlocked && inner.Contains(Cursor) && Input.Mouse.LeftPressed && !eyedropper)
		{
			var worldMouse = ScreenToEditorWorld(Cursor, inner);
			if (GetRoomAtWorld(worldMouse) is { } selected && selected != CurrentRoom)
			{
				using (CaptureSelectionUndo("Select Room"))
				{
					if (CurrentRoom is { } previous)
						editorRoomEntryDirection = GetEditorEntryDirection(previous, selected);
					CurrentRoom?.Save();
					SelectRoom(selected);
					selectedRoomActorIndex = -1;
					ClearEditorDragState();
					SyncEditorGame();
					TweenEditorCameraToRoom(selected);
					roomActivationClickConsumed = true;
				}
			}
		}

		if (roomActivationClickConsumed)
		{
			worldMouseBlocked = true;
			CancelTilePaint();
		}

		if (CurrentRoom is { } editing)
		{
			using var editorInteractionPerf = PerfStats.Time(PerfDetail.EditorInteraction);
			var gridColor = Color.White * 0.07f;
			var gridColorHeavy = Color.White * 0.115f;
			var gridWeight = 1.0f / editorZoom;
			var roomBounds = editing.WorldBounds;
			var roomTopLeft = (Vector2)roomBounds.TopLeft;
			var width = roomBounds.Width;
			var height = roomBounds.Height;

			var localMouse = Vector2.Zero;
			var worldMouse = Vector2.Zero;
			var tileOver = Point2.Zero;
			if (Matrix3x2.Invert(worldMatrix, out var inverse))
			{
				worldMouse = Vector2.Transform(Cursor, inverse);
				localMouse = worldMouse - roomTopLeft;
				tileOver = (Point2)(localMouse / Game.TileSize);
			}
			var overGame = new Rect(0, 0, width, height).Contains(localMouse);
			proposedRoomTiles = editingRoomGeometry || worldMouseBlocked || overGame ? null : GetProposedRoomTiles(editing, worldMouse);
			if (selectedRoomActorIndex >= editing.Actors.Count)
				selectedRoomActorIndex = -1;
			var actorSelectActive = actorSelectTool && !eyedropper;
			var hoveredActorIndex = !worldMouseBlocked && overGame ? HitTestRoomActor(editing, worldMouse) : -1;
			var revealDoorFocused =
				hoveredActorIndex >= 0 && hoveredActorIndex < editing.Actors.Count && IsRevealDoorActor(editing.Actors[hoveredActorIndex]) ||
				selectedRoomActorIndex >= 0 && selectedRoomActorIndex < editing.Actors.Count && IsRevealDoorActor(editing.Actors[selectedRoomActorIndex]);
			game?.SetHiddenCoverEditorOpacity(revealDoorFocused);
			var hoveredHiddenWallHandle = !worldMouseBlocked && actorSelectActive && overGame
				? HitTestHiddenWallResizeHandle(editing, worldMouse, gridWeight)
				: ResizeHandle.None;
			var hoveredRoomGeometryHandle = editingRoomGeometry && !worldMouseBlocked
				? HitTestRoomGeometryHandle(editing, worldMouse, gridWeight)
				: ResizeHandle.None;
			SetHiddenWallResizeCursor(
				DragKind == EditorDragKind.RoomGeometry ? editorDrag.RoomGeometryHandle :
				hoveredRoomGeometryHandle != ResizeHandle.None ? hoveredRoomGeometryHandle :
				DragKind == EditorDragKind.ActorResize ? editorDrag.ResizeHandle :
				hoveredHiddenWallHandle);

			for (int x = 1; x < editing.Size.X; x++)
				batcher.Line(roomTopLeft + new Vector2(x * Game.TileSize, 0), roomTopLeft + new Vector2(x * Game.TileSize, height), gridWeight, (x - 1) % 4 == 0 ? gridColorHeavy : gridColor);
			for (int y = 1; y < editing.Size.Y; y++)
				batcher.Line(roomTopLeft + new Vector2(0, y * Game.TileSize), roomTopLeft + new Vector2(width, y * Game.TileSize), gridWeight, (y - 1) % 4 == 0 ? gridColorHeavy : gridColor);
			DrawBreakableTileMarkers(editing, roomTopLeft, gridWeight);

			var brushEntry = Factory.Find(brush);
			if (brushEntry is { } foundBrushEntry && foundBrushEntry.Layer != activeTileLayer)
				brushEntry = null;
			Factory.ActorPaletteEntry? actorBrush = selectedActorPaletteIndex >= 0 && selectedActorPaletteIndex < Factory.ActorPalette.Count
				? Factory.ActorPalette[selectedActorPaletteIndex]
				: null;
			var brushIsActor = actorBrush != null;
			var ctrlClick =
				Input.Mouse.LeftPressed &&
				(Input.Keyboard.Down(Keys.LeftControl) || Input.Keyboard.Down(Keys.RightControl));

			if (eyedropper)
				CancelTilePaint();

			if (worldMouseBlocked)
				CancelTilePaint();

			if (editingRoomGeometry && !worldMouseBlocked && Input.Mouse.LeftPressed && (overGame || hoveredRoomGeometryHandle != ResizeHandle.None))
			{
				ClearEditorDragState();
				editorDrag.Kind = EditorDragKind.RoomGeometry;
				roomGeometryUndo ??= CaptureRoomUndo("Edit Room Geometry", editing);
				editorDrag.RoomGeometryHandle = hoveredRoomGeometryHandle;
				var mouseTile = WorldToRoomGeometryTile(worldMouse);
				editorDrag.RoomGeometryOffset = mouseTile - editing.TileSpacePosition;
				CancelTilePaint();
			}
			else if (!editingRoomGeometry && !worldMouseBlocked && actorSelectActive && overGame && Input.Mouse.LeftPressed && hoveredHiddenWallHandle != ResizeHandle.None)
			{
				ClearEditorDragState();
				editorDrag.Kind = EditorDragKind.ActorResize;
				editorDrag.ResizeHandle = hoveredHiddenWallHandle;
				editorSelectionRect = null;
				CancelTilePaint();
			}
			else if (!editingRoomGeometry && !worldMouseBlocked && actorSelectActive && overGame && Input.Mouse.LeftPressed && editorSelectionRect is { } selection && TileRectContains(selection, tileOver))
			{
				var duplicateSelection = Input.Keyboard.Down(Keys.LeftControl) || Input.Keyboard.Down(Keys.RightControl);
				actorMoveUndo ??= CaptureRoomUndo(duplicateSelection ? "Duplicate Selection" : "Move Selection", editing);
				ClearEditorDragState();
				editorDrag.Kind = EditorDragKind.SelectionMove;
				editorDrag.DuplicateSelection = duplicateSelection;
				editorDrag.SelectionAnchor = tileOver;
				editorDrag.SelectionOffset = Point2.Zero;
				editorDrag.SelectionSnapshot = CaptureEditorSelection(editing);
				CancelTilePaint();
			}
			else if (!editingRoomGeometry && !worldMouseBlocked && actorSelectActive && overGame && Input.Mouse.LeftPressed)
			{
				if (hoveredActorIndex >= 0)
				{
					using (CaptureSelectionUndo("Select Actor"))
					{
						selectedRoomActorIndex = hoveredActorIndex;
						ClearEditorDragState();
						editorSelectionRect = null;
						editorDrag.Kind = EditorDragKind.RoomActor;
						editorDrag.ActorOffset = worldMouse - (roomTopLeft + (Vector2)(ActorSpecs.ReadPoint(editing.Actors[selectedRoomActorIndex], "Position") ?? Point2.Zero));
						editorDrag.ActorSnapsByDefault = RoomActorPositionIsSnapped(editing.Actors[selectedRoomActorIndex]);
						CancelTilePaint();
					}
				}
				else if (selectedRoomActorIndex < 0)
				{
					using (CaptureSelectionUndo("Select Area"))
					{
						ClearEditorDragState();
						editorDrag.Kind = EditorDragKind.SelectionRect;
						editorSelectionFrom = ClampTile(editing, tileOver);
						editorSelectionTo = editorSelectionFrom;
						editorSelectionRect = NormalizeTileRect(editorSelectionFrom, editorSelectionTo);
						CancelTilePaint();
					}
				}
				else
				{
					using (CaptureSelectionUndo("Clear Selection"))
					{
						selectedRoomActorIndex = -1;
						ClearEditorDragState();
						CancelTilePaint();
					}
				}
			}
			else if (!editingRoomGeometry && !worldMouseBlocked && overGame && brushIsActor && Input.Mouse.RightPressed && hoveredActorIndex >= 0)
			{
				using (CaptureRoomUndo("Delete Actor", editing))
				{
					editing.Actors.RemoveAt(hoveredActorIndex);
					if (selectedRoomActorIndex == hoveredActorIndex)
						selectedRoomActorIndex = -1;
					else if (selectedRoomActorIndex > hoveredActorIndex)
						selectedRoomActorIndex--;
					ClearEditorDragState();
					editorSelectionRect = null;
					CancelTilePaint();
					editorGameDirty = true;
				}
			}
			else if (!editingRoomGeometry && !worldMouseBlocked && eyedropper && overGame && Input.Mouse.LeftPressed)
			{
				brush = activeTileLayer == Factory.TileLayer.Background
					? editing.BackgroundTiles[tileOver.X, tileOver.Y]
					: editing.Tiles[tileOver.X, tileOver.Y];
				tileTypePlacing = brush;
				selectedActorPaletteIndex = -1;
				actorSelectTool = false;
				editorSelectionRect = null;
			}
			else if (!editingRoomGeometry && !worldMouseBlocked && activeTileLayer == Factory.TileLayer.Foreground && !actorSelectTool && !brushIsActor && overGame && ctrlClick && TryGetSlopeToggle(editing.Tiles[tileOver.X, tileOver.Y], out var toggledTile))
			{
				using (CaptureRoomUndo("Toggle Slope Tile", editing))
				{
					editing.Set(tileOver, toggledTile);
					brush = toggledTile;
					tileTypePlacing = toggledTile;
					selectedActorPaletteIndex = -1;
					CancelTilePaint();
					editorGameDirty = true;
				}
			}
			else if (!editingRoomGeometry && !worldMouseBlocked && activeTileLayer == Factory.TileLayer.Foreground && overGame && !keyboardBlocked && Input.Keyboard.Pressed(Keys.B))
			{
				if (editing.GetEntry(tileOver) is { BlocksMovement: true })
				{
					using (CaptureRoomUndo("Toggle Breakable Tile", editing))
					{
						editing.SetBreakable(tileOver, !editing.IsBreakable(tileOver));
						editorGameDirty = true;
					}
				}
			}
			else if (!editingRoomGeometry && !worldMouseBlocked && overGame && Input.Mouse.LeftPressed && actorBrush is { } actorEntry)
			{
				using (CaptureRoomUndo("Place Actor", editing))
				{
					var position = (Point2)(roomTopLeft + tileOver * Game.TileSize + actorEntry.Offset);
					if (actorEntry.ActorSpecId != null)
						editing.AddActorSpec(actorEntry.ActorSpecId, position);
					else if (actorEntry.ActorTypeName != null)
						editing.AddActorType(actorEntry.ActorTypeName, position);
					CancelTilePaint();
					editorGameDirty = true;
				}
			}
			else if (!editingRoomGeometry && !worldMouseBlocked && lastCell == cell && overGame && !brushIsActor && brushEntry != null && (Input.Mouse.LeftPressed || Input.Mouse.RightPressed))
			{
				tilePaintUndo?.Commit();
				tilePaintUndo = CaptureRoomUndo("Paint Tiles", editing);
				tileRectPaintFrom = ClampTile(editing, tileOver);
				tileRectPaintTo = tileRectPaintFrom;
				tileTypePlacing = Input.Mouse.LeftPressed ? brush : '0';
				ClearEditorDragState();
				editorDrag.Kind = EditorDragKind.TilePaint;
				editorSelectionRect = null;
			}

			if (!worldMouseBlocked && DragKind == EditorDragKind.RoomGeometry && Input.Mouse.LeftDown)
			{
				ApplyRoomGeometryDrag(editing, worldMouse);
			}
			else if (!worldMouseBlocked && actorSelectActive && DragKind == EditorDragKind.ActorResize && selectedRoomActorIndex >= 0 && Input.Mouse.LeftDown)
			{
				actorMoveUndo ??= CaptureRoomUndo("Resize Actor", editing);
				ResizeHiddenWallActor(editing, selectedRoomActorIndex, editorDrag.ResizeHandle, worldMouse, tileOver, keyboardBlocked || !Input.Keyboard.Shift);
			}
			else if (!worldMouseBlocked && actorSelectActive && DragKind == EditorDragKind.RoomActor && selectedRoomActorIndex >= 0 && Input.Mouse.LeftDown)
			{
				actorMoveUndo ??= CaptureRoomUndo("Move Actor", editing);
				MoveRoomActorTo(editing, selectedRoomActorIndex, worldMouse, RoomActorDragShouldSnap(keyboardBlocked));
			}
			else if (!worldMouseBlocked && actorSelectActive && DragKind == EditorDragKind.SelectionMove && editorDrag.SelectionSnapshot != null && Input.Mouse.LeftDown)
			{
				editorDrag.SelectionOffset = tileOver - editorDrag.SelectionAnchor;
				ApplyEditorSelectionSnapshot(editing, editorDrag.SelectionSnapshot, editorDrag.SelectionOffset, editorDrag.DuplicateSelection);
			}
			else if (!worldMouseBlocked && actorSelectActive && DragKind == EditorDragKind.SelectionRect && Input.Mouse.LeftDown)
			{
				editorSelectionTo = ClampTile(editing, tileOver);
				editorSelectionRect = NormalizeTileRect(editorSelectionFrom, editorSelectionTo);
			}

			if (!Input.Mouse.LeftDown && DragKind != EditorDragKind.TilePaint)
			{
				CommitEditorDragState();
			}

			if (!keyboardBlocked && actorSelectTool && selectedRoomActorIndex >= 0 && (Input.Keyboard.Pressed(Keys.Delete) || Input.Keyboard.Pressed(Keys.Backspace)))
			{
				using (CaptureRoomUndo("Delete Actor", editing))
				{
					editing.Actors.RemoveAt(selectedRoomActorIndex);
					selectedRoomActorIndex = -1;
					ClearEditorDragState();
					editorGameDirty = true;
				}
			}

			if (!Input.Mouse.RightDown && !TilePlacing)
				tileTypePlacing = brush;

			if (!editingRoomGeometry && !worldMouseBlocked && (!actorSelectTool || eyedropper) && overGame)
			{
				if (eyedropper)
				{
					var sampleRect = ((new Rect(tileOver.X, tileOver.Y, 1, 1) * Game.TileSize) + roomTopLeft).Inflate(gridWeight * 2);
					DrawEditorEyedropperCursor(sampleRect, gridWeight);
				}
				else if (actorBrush is { } actorPreview)
				{
					var position = roomTopLeft + tileOver * Game.TileSize + actorPreview.Offset;
					batcher.Image(actorPreview.Image, position, actorPreview.Origin, Vector2.One, 0, Color.White);
				}
				else if (brushEntry is { } placing)
				{
					var previewTileRect = TilePlacing ? NormalizeTileRect(tileRectPaintFrom, ClampTile(editing, tileOver)) : new RectInt(tileOver.X, tileOver.Y, 1, 1);
					var cursorRect = TileRectToRoomWorldRect(editing, previewTileRect).Inflate(gridWeight * 2);
					var cursorColor = tileTypePlacing == '0' ? Color.Red : highlight;
					batcher.RectLine(cursorRect, gridWeight * 2, cursorColor);
					if (tileTypePlacing != '0')
						for (var y = previewTileRect.Top; y < previewTileRect.Bottom; y++)
							for (var x = previewTileRect.Left; x < previewTileRect.Right; x++)
							{
								var tile = new Point2(x, y);
								var image = GetPreviewTileImage(editing, tile, tileTypePlacing, placing);
								batcher.Image(image, roomTopLeft + tile * Game.TileSize + placing.Offset, placing.Origin, Vector2.One, 0, Color.White);
							}
				}
			}

			if (!editingRoomGeometry && !worldMouseBlocked && !eyedropper && !brushIsActor && brushEntry != null && TilePlacing && (Input.Mouse.LeftDown || Input.Mouse.RightDown))
			{
				tileRectPaintTo = ClampTile(editing, tileOver);
			}

			batcher.RectLine(roomBounds, gridWeight * 2, Color.Black);
			if (editorSelectionRect is { } selectedRect)
			{
				var selectedBounds = TileRectToRoomWorldRect(editing, selectedRect).Inflate(gridWeight * 2);
				var lineWidth = MathF.Max(1, gridWeight);
				var dash = MathF.Max(2, Game.TileSize * 0.25f);
				var offset = (Time.SecondsF * 0.45f) % (dash * 2);
				var tl = selectedBounds.TopLeft;
				var tr = selectedBounds.TopRight;
				var bl = selectedBounds.BottomLeft;
				var br = selectedBounds.BottomRight;
				batcher.LineDashed(tl, tr, lineWidth, highlight, dash, offset);
				batcher.LineDashed(tr, br, lineWidth, highlight, dash, offset);
				batcher.LineDashed(br, bl, lineWidth, highlight, dash, offset);
				batcher.LineDashed(bl, tl, lineWidth, highlight, dash, offset);
			}
			if (editingRoomGeometry)
				DrawRoomGeometryHandles(editing, gridWeight);
			DrawPlaytestPlayerMarker(editing);
			if (!editingRoomGeometry && actorSelectActive)
				DrawRoomActorOverlays(editing, gridWeight, hoveredActorIndex, worldMouse);

			if (proposedRoomTiles is { } proposal)
			{
				var proposalBounds = TileRectToWorldRect(proposal);
				batcher.Rect(proposalBounds, Color.Gray * 0.35f);
				batcher.RectLine(proposalBounds, gridWeight * 2, Color.Gray);

				if (!worldMouseBlocked && inner.Contains(Cursor) && Input.Mouse.LeftPressed && !eyedropper)
				{
					CreateRoom(proposal.Position, proposal.Size);
					proposedRoomTiles = null;
				}
			}
		}

		batcher.PopMatrix();
		batcher.PopScissor();

		if (!Input.Mouse.LeftDown && !Input.Mouse.RightDown)
		{
			if (TilePlacing && CurrentRoom is { } room)
				ApplyTileRect(room, NormalizeTileRect(tileRectPaintFrom, tileRectPaintTo), activeTileLayer, tileTypePlacing);
			tileTypePlacing = brush;
			if (TilePlacing)
				ClearEditorDragState();
			tilePaintUndo?.Commit();
			tilePaintUndo = null;
		}

		using (PerfStats.Time(PerfDetail.EditorSync))
		{
			if (editorGameDirty && (TilePlacing || DragKind == EditorDragKind.RoomGeometry))
			{
				if (CurrentRoom is { } room)
					MarkRoomPreviewDirty(room);
				SyncEditorGame();
			}
			else if (editorGameDirty)
			{
				editorGameDirty = false;
				if (CurrentRoom is { } room)
				{
					MarkRoomPreviewDirty(room);
					room.Save();
				}
				SyncEditorGame();
			}
		}
	}

	private bool HasAdjacentEditorRoom(Point2 direction)
	{
		if (CurrentRoom is not { } room)
			return Assets.GetRoom(cell + direction) != null;

		return Assets.FindAdjacentRoom(room, direction, EditorExitPosition(room, direction)) != null;
	}

	private void DrawEditorSelectionOptions()
	{
		if (DragKind != EditorDragKind.SelectionRect && editorSelectionRect == null)
			return;

		var flags =
			ImGuiWindowFlags.NoDecoration |
			ImGuiWindowFlags.AlwaysAutoResize |
			ImGuiWindowFlags.NoSavedSettings |
			ImGuiWindowFlags.NoMove |
			ImGuiWindowFlags.NoFocusOnAppearing;

		var pos = new Vector2(PaletteSlideRect.Right + 10, ToolbarHeight + 10);
		ImGui.SetNextWindowPos(pos, ImGuiCond.Always);
		ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(8, 6));
		ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(6, 3));
		ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.04f, 0.04f, 0.05f, 0.82f));
		ImGui.Begin("Selection Options", flags);
		ImGui.Checkbox("BG Tiles", ref selectionIncludeBackgroundTiles);
		ImGui.Checkbox("FG Tiles", ref selectionIncludeForegroundTiles);
		ImGui.Checkbox("Actors", ref selectionIncludeActors);
		ImGui.End();
		ImGui.PopStyleColor();
		ImGui.PopStyleVar(2);
	}

	private RectInt? GetProposedRoomTiles(Room source, Vector2 worldMouse)
	{
		var sourceTiles = new RectInt(source.TileSpacePosition, source.Size);
		var mouseTile = new Point2(
			(int)MathF.Floor(worldMouse.X / Game.TileSize),
			(int)MathF.Floor(worldMouse.Y / Game.TileSize));

		if (mouseTile.X >= sourceTiles.Right &&
			mouseTile.X < sourceTiles.Right + Room.DefaultSize.X &&
			mouseTile.Y >= sourceTiles.Top &&
			mouseTile.Y < sourceTiles.Bottom)
			return FitProposedRoom(sourceTiles, Point2.Right, mouseTile.Y);

		if (mouseTile.X < sourceTiles.Left &&
			mouseTile.X >= sourceTiles.Left - Room.DefaultSize.X &&
			mouseTile.Y >= sourceTiles.Top &&
			mouseTile.Y < sourceTiles.Bottom)
			return FitProposedRoom(sourceTiles, Point2.Left, mouseTile.Y);

		if (mouseTile.Y >= sourceTiles.Bottom &&
			mouseTile.Y < sourceTiles.Bottom + Room.DefaultSize.Y &&
			mouseTile.X >= sourceTiles.Left &&
			mouseTile.X < sourceTiles.Right)
			return FitProposedRoom(sourceTiles, Point2.Down, mouseTile.X);

		if (mouseTile.Y < sourceTiles.Top &&
			mouseTile.Y >= sourceTiles.Top - Room.DefaultSize.Y &&
			mouseTile.X >= sourceTiles.Left &&
			mouseTile.X < sourceTiles.Right)
			return FitProposedRoom(sourceTiles, Point2.Up, mouseTile.X);

		return null;
	}

	private RectInt? FitProposedRoom(RectInt source, Point2 direction, int mouseAxis)
	{
		var vertical = direction.X != 0;
		var depth = vertical ? Room.DefaultSize.X : Room.DefaultSize.Y;
		var start = vertical ? source.Top : source.Left;
		var end = vertical ? source.Bottom : source.Right;
		var blocked = new bool[end - start];

		var candidate = direction switch
		{
			{ X: > 0 } => new RectInt(source.Right, source.Top, depth, source.Height),
			{ X: < 0 } => new RectInt(source.Left - depth, source.Top, depth, source.Height),
			{ Y: > 0 } => new RectInt(source.Left, source.Bottom, source.Width, depth),
			{ Y: < 0 } => new RectInt(source.Left, source.Top - depth, source.Width, depth),
			_ => new RectInt()
		};

		foreach (var room in Assets.RoomsById.Values)
		{
			var roomTiles = new RectInt(room.TileSpacePosition, room.Size);
			if (!candidate.Overlaps(roomTiles))
				continue;

			var blockStart = Math.Max(start, vertical ? roomTiles.Top : roomTiles.Left);
			var blockEnd = Math.Min(end, vertical ? roomTiles.Bottom : roomTiles.Right);
			for (var i = blockStart; i < blockEnd; i++)
				blocked[i - start] = true;
		}

		var index = mouseAxis - start;
		if (index < 0 || index >= blocked.Length || blocked[index])
			return null;

		var freeStart = index;
		while (freeStart > 0 && !blocked[freeStart - 1])
			freeStart--;

		var freeEnd = index + 1;
		while (freeEnd < blocked.Length && !blocked[freeEnd])
			freeEnd++;

		var axisStart = start + freeStart;
		var axisLength = freeEnd - freeStart;
		if (axisLength <= 0)
			return null;

		return direction switch
		{
			{ X: > 0 } => new RectInt(source.Right, axisStart, depth, axisLength),
			{ X: < 0 } => new RectInt(source.Left - depth, axisStart, depth, axisLength),
			{ Y: > 0 } => new RectInt(axisStart, source.Bottom, axisLength, depth),
			{ Y: < 0 } => new RectInt(axisStart, source.Top - depth, axisLength, depth),
			_ => null
		};
	}

	private static Rect TileRectToWorldRect(RectInt tiles)
		=> new(tiles.X * Game.TileSize, tiles.Y * Game.TileSize, tiles.Width * Game.TileSize, tiles.Height * Game.TileSize);

	private void CreateRoom(Point2 position, Point2 size)
	{
		var sourceSettings = CurrentRoom is { } source ? CaptureRoomSettings(source) : (RoomSettings?)null;
		CurrentRoom?.Save();

		var it = new Room(position, size);
		if (sourceSettings is { } settings)
			ApplyRoomSettings(it, settings);
		MirrorAdjacentRoomBorders(it);
		Assets.Rooms[it.Cell] = it;
		Assets.RoomsById[it.Id] = it;
		SelectRoom(it);
		it.Save();
		SyncEditorGame();
		TweenEditorCameraToRoom(it);
	}

	private static void MirrorAdjacentRoomBorders(Room room)
	{
		for (var x = 0; x < room.Size.X; x++)
		{
			room.Tiles[x, 0] = SampleAdjacentRoomTile(room, new Point2(room.TileSpacePosition.X + x, room.TileSpacePosition.Y - 1));
			room.Tiles[x, room.Size.Y - 1] = SampleAdjacentRoomTile(room, new Point2(room.TileSpacePosition.X + x, room.TileSpacePosition.Y + room.Size.Y));
		}

		for (var y = 0; y < room.Size.Y; y++)
		{
			room.Tiles[0, y] = SampleAdjacentRoomTile(room, new Point2(room.TileSpacePosition.X - 1, room.TileSpacePosition.Y + y));
			room.Tiles[room.Size.X - 1, y] = SampleAdjacentRoomTile(room, new Point2(room.TileSpacePosition.X + room.Size.X, room.TileSpacePosition.Y + y));
		}

		room.RebuildGeometry();
	}

	private static char SampleAdjacentRoomTile(Room exclude, Point2 worldTile)
	{
		foreach (var room in Assets.RoomsById.Values)
		{
			if (room == exclude)
				continue;
			if (worldTile.X < room.TileSpacePosition.X || worldTile.Y < room.TileSpacePosition.Y ||
				worldTile.X >= room.TileSpacePosition.X + room.Size.X || worldTile.Y >= room.TileSpacePosition.Y + room.Size.Y)
				continue;

			return room.Tiles[worldTile.X - room.TileSpacePosition.X, worldTile.Y - room.TileSpacePosition.Y];
		}

		return '0';
	}


	/// <summary>
	/// Shifts to display a new room
	/// </summary>
	private void ShiftRoom(Point2 direction)
	{
		var current = CurrentRoom;
		if (current == null)
			return;

		var exitPosition = EditorExitPosition(current, direction);
		if (Assets.FindAdjacentRoom(current, direction, exitPosition) is not { } adjacent)
			return;

		current.Save();
		SelectRoom(adjacent);
		editorRoomEntryDirection = direction;
		SyncEditorGame();
		TweenEditorCameraToRoom(adjacent);
	}

	private static Vector2 EditorExitPosition(Room room, Point2 direction)
	{
		var bounds = room.WorldBounds;
		if (direction.X > 0)
			return new Vector2(bounds.Right, bounds.CenterY);
		if (direction.X < 0)
			return new Vector2(bounds.Left - 1, bounds.CenterY);
		if (direction.Y > 0)
			return new Vector2(bounds.CenterX, bounds.Bottom);
		if (direction.Y < 0)
			return new Vector2(bounds.CenterX, bounds.Top - 1);

		return bounds.Center;
	}

	private void DrawCenteredRoomName(Room? room)
	{
		if (room == null)
		{
			DrawCenteredToolbarText($"Room {cell.X} x {cell.Y}");
			return;
		}

		if (editingRoomName && roomNameEditingRoomId != room.Id)
			CancelRoomRename();

		if (editingRoomName)
		{
			var width = Math.Max(160, ImGui.CalcTextSize(roomNameEditor).X + 24);
			var windowPos = ImGui.GetWindowPos();
			var windowSize = ImGui.GetWindowSize();
			var pos = new Vector2(
				windowPos.X + windowSize.X * 0.5f - width * 0.5f,
				windowPos.Y + windowSize.Y * 0.5f - ImGui.GetFrameHeight() * 0.5f);

			ImGui.SetCursorScreenPos(pos);
			ImGui.SetNextItemWidth(width);
			if (focusRoomNameEditor)
			{
				ImGui.SetKeyboardFocusHere();
				focusRoomNameEditor = false;
			}

			var commit = ImGui.InputText("##RoomNameEditor", ref roomNameEditor, 128, ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.AutoSelectAll);
			var cancel = ImGui.IsKeyPressed(ImGuiKey.Escape);
			if (cancel)
				CancelRoomRename();
			else if (commit || ImGui.IsItemDeactivated())
				CommitRoomRename(room);
			return;
		}

		DrawCenteredToolbarText(room.Id, canDoubleClick: true);
		var textHovered = ImGui.IsItemHovered();
		if (textHovered && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
			BeginRoomRename(room);

		var deleteSize = new Vector2(ButtonSize, ButtonSize);
		var deletePos = ImGui.GetItemRectMax() + new Vector2(ToolbarSpacing, -ButtonSize * 0.5f - ImGui.GetItemRectSize().Y * 0.5f);
		var deleteRect = new Rect(deletePos, deleteSize);
		var mouse = ImGui.GetMousePos();
		var showDelete = textHovered || deleteRect.Contains(mouse);
		if (showDelete)
		{
			ImGui.SetCursorScreenPos(deletePos);
			if (ImGuiSubtextureButton("DeleteRoom", Assets.GetSubtexture("buttons/14"), "Delete room", false, deleteSize, imagePadding: ToolbarButtonImagePadding))
			{
				pendingDeleteRoomId = room.Id;
				ImGui.OpenPopup("Delete Room?");
			}
		}
	}

	private void BeginRoomRename(Room room)
	{
		editingRoomName = true;
		focusRoomNameEditor = true;
		roomNameEditor = room.Id;
		roomNameEditingRoomId = room.Id;
	}

	private void CancelRoomRename()
	{
		editingRoomName = false;
		focusRoomNameEditor = false;
		roomNameEditor = string.Empty;
		roomNameEditingRoomId = null;
	}

	private void CommitRoomRename(Room room)
	{
		var next = roomNameEditor.Trim();
		if (!Assets.CanRenameRoom(room, next))
		{
			roomNameEditor = room.Id;
			CancelRoomRename();
			return;
		}

		if (Assets.RenameRoom(room, next))
			selectedRoomId = room.Id;

		CancelRoomRename();
	}

	private void DrawDeleteRoomPopup()
	{
		var open = true;
		if (!ImGui.BeginPopupModal("Delete Room?", ref open, ImGuiWindowFlags.AlwaysAutoResize))
			return;

		var room = pendingDeleteRoomId != null ? Assets.GetRoom(pendingDeleteRoomId) : null;
		ImGui.Text(room != null ? $"Delete room '{room.Id}'?" : "Room no longer exists.");
		ImGui.Spacing();

		if (ImGui.Button("Delete", new Vector2(90, 0)))
		{
			if (room != null)
				DeleteRoom(room);
			pendingDeleteRoomId = null;
			ImGui.CloseCurrentPopup();
		}
		ImGui.SameLine();
		if (ImGui.Button("Cancel", new Vector2(90, 0)) || !open)
		{
			pendingDeleteRoomId = null;
			ImGui.CloseCurrentPopup();
		}

		ImGui.EndPopup();
	}

	private void DeleteRoom(Room room)
	{
		if (CurrentRoom == room)
			CurrentRoom?.Save();

		Assets.DeleteRoom(room);
		if (selectedRoomId == room.Id)
			selectedRoomId = null;
		if (playtestPlayerPosition is { } remembered && remembered.RoomId == room.Id)
			playtestPlayerPosition = null;
		InvalidateRoomPreview(room);
		ResolveSelectedRoom();
		SyncEditorGame();
	}

	private static void DrawCenteredToolbarText(string text, bool canDoubleClick = false)
	{
		var size = ImGui.CalcTextSize(text);
		var windowPos = ImGui.GetWindowPos();
		var windowSize = ImGui.GetWindowSize();
		var pos = new Vector2(
			windowPos.X + windowSize.X * 0.5f - size.X * 0.5f,
			windowPos.Y + windowSize.Y * 0.5f - size.Y * 0.5f);
		if (canDoubleClick)
		{
			ImGui.SetCursorScreenPos(pos);
			ImGui.InvisibleButton("##CenteredToolbarText", size);
		}
		ImGui.GetWindowDrawList().AddText(pos, ImGui.GetColorU32(ImGuiCol.Text), text);
	}

	private bool ToggleEditorPressed()
	{
		if (Input.Keyboard.Pressed(Keys.Tab))
			return true;

		foreach (var controller in Input.Controllers)
		{
			if (controller.Connected)
			{
				if (controller.Pressed(Buttons.RightStick))
					return true;
			}
		}

		return false;
	}

	private void ToolbarImGui()
	{
		var flags =
			ImGuiWindowFlags.NoDecoration |
			ImGuiWindowFlags.NoMove |
			ImGuiWindowFlags.NoSavedSettings |
			ImGuiWindowFlags.NoScrollbar |
			ImGuiWindowFlags.NoScrollWithMouse |
			ImGuiWindowFlags.NoBringToFrontOnFocus;

		ImGui.SetNextWindowPos(Vector2.Zero, ImGuiCond.Always);
		var window = RequiredWindow;
		ImGui.SetNextWindowSize(new Vector2(window.WidthInPixels / imgui.Scale, ToolbarHeight), ImGuiCond.Always);

		ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(2, 2));
		ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(ButtonSpacing, 0));
		ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, Vector2.Zero);
		ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.18f, 0.08f, 0.15f, 1.0f));
		ImGui.Begin("Toolbar", flags);

		if (ImGuiIconButton("Play", Assets.GetSubtexture("buttons/0"), "Play [Tab/R3]", imagePadding: ToolbarButtonImagePadding))
			StartGame();
		ImGui.SameLine();

		/*
				if (ImGuiIconButton("SmallBrush", Assets.GetSubtexture("buttons/7"), "Smaller Brush", brushSize <= 1))
					brushSize = Math.Max(1, brushSize - 1);
				ImGui.SameLine();
				if (ImGuiIconButton("BigBrush", Assets.GetSubtexture("buttons/8"), "Larger Brush", brushSize > 1))
					brushSize = Math.Min(10, brushSize + 1);
				ImGui.SameLine(0, ToolbarSpacing);
		*/

		if (ImGuiIconButton("Music", Assets.GetSubtexture(musicEnabled ? "buttons/9" : "buttons/10"), "Music", musicEnabled, ToolbarButtonImagePadding))
		{
			musicEnabled = !musicEnabled;
			game?.RefreshMusic();
		}
		ImGui.SameLine();

#if !IOS
		if (ImGuiIconButton("AlwaysOnTop", Assets.GetSubtexture(alwaysOnTop ? "buttons/11" : "buttons/12"), "Always on Top", alwaysOnTop, ToolbarButtonImagePadding))
		{
			alwaysOnTop = !alwaysOnTop;
			window.AlwaysOnTop = alwaysOnTop;
		}
		ImGui.SameLine();

		if (ImGuiIconButton("OpenFmodStudio", Assets.GetSubtexture("buttons/13"), "Open FMOD Studio", imagePadding: ToolbarButtonImagePadding))
			OpenFmodStudio();
		ImGui.SameLine(0, ToolbarSpacing);

		if (ImGuiTextButton("Transitions", new Vector2(70, ButtonSize), showTransitionViewer))
			showTransitionViewer = !showTransitionViewer;
		ImGui.SameLine(0, ToolbarSpacing);
#endif

		DrawWorldSelector();

		DrawCenteredRoomName(CurrentRoom);
		DrawDeleteRoomPopup();

		ImGui.End();
		ImGui.PopStyleColor();
		ImGui.PopStyleVar(3);
	}

	private void DrawWorldSelector()
	{
		var worlds = Assets.GetWorldIds();
		if (worlds.Count == 0)
			return;

		var width = Math.Clamp(ImGui.CalcTextSize(Assets.CurrentWorldId).X + 32, 96, 180);
		ImGui.SetCursorPosY((ToolbarHeight - ImGui.GetFrameHeight()) * 0.5f);
		ImGui.SetNextItemWidth(width);
		if (ImGui.BeginCombo("##WorldSelector", Assets.CurrentWorldId))
		{
			foreach (var world in worlds)
			{
				var selected = string.Equals(world, Assets.CurrentWorldId, StringComparison.OrdinalIgnoreCase);
				if (ImGui.Selectable(world, selected))
					SwitchWorld(world);
				if (selected)
					ImGui.SetItemDefaultFocus();
			}

			ImGui.EndCombo();
		}
		ImGui.SameLine(0, ToolbarSpacing);
	}

	private void SwitchWorld(string worldId)
	{
		if (string.Equals(worldId, Assets.CurrentWorldId, StringComparison.OrdinalIgnoreCase))
			return;

		if (playing || startingPlay)
			StopGame();

		CurrentRoom?.Save();
		Assets.SelectWorld(worldId);
		Assets.ReloadRooms();
		Assets.RebuildRoomGeometry();
		ClearRoomPreviewCache();
		undo.Clear();
		selectedRoomId = null;
		cell = Point2.Zero;
		roomCreatePosition = Point2.Zero;
		editorSelectionRect = null;
		selectedRoomActorIndex = -1;
		pendingDeleteRoomId = null;
		ResolveSelectedRoom();
		FocusEditorOnCurrentRoom();
		SyncEditorGame();
	}


#if DEBUG && !IOS
	private void UpdateWindowTitleFps()
	{
		fpsFrames++;
		fpsElapsed += Time.Delta;
		if (fpsElapsed < 1.0f)
			return;

		var fps = fpsFrames / fpsElapsed;
		if (Window is { } window)
			window.Title = $"Myth - {fps:0} FPS";
		fpsFrames = 0;
		fpsElapsed = 0;
	}

	private void StartAssetWatcher()
	{
		assetWatcher = new FileSystemWatcher(Path.GetFullPath(Assets.AssetsPath))
		{
			IncludeSubdirectories = true,
			NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime
		};
		assetWatcher.Filters.Add("*.ase");
		assetWatcher.Filters.Add("*.aseprite");

		assetWatcher.Changed += (_, _) => RequestAssetReload();
		assetWatcher.Created += (_, _) => RequestAssetReload();
		assetWatcher.Deleted += (_, _) => RequestAssetReload();
		assetWatcher.Renamed += (_, _) => RequestAssetReload();
		assetWatcher.EnableRaisingEvents = true;
	}

	private void StartShaderWatcher()
	{
		var shadersPath = Path.GetFullPath(Path.Join(Assets.AssetsPath, "Shaders"));
		if (!Directory.Exists(shadersPath))
			return;

		shaderWatcher = new FileSystemWatcher(shadersPath, "*.hlsl")
		{
			IncludeSubdirectories = false,
			NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime
		};

		shaderWatcher.Changed += (_, _) => RequestShaderReload();
		shaderWatcher.Created += (_, _) => RequestShaderReload();
		shaderWatcher.Deleted += (_, _) => RequestShaderReload();
		shaderWatcher.Renamed += (_, _) => RequestShaderReload();
		shaderWatcher.EnableRaisingEvents = true;
	}

	private void StartAudioBankWatcher()
	{
		var fmodPath = Path.GetFullPath(Path.Join(Assets.AssetsPath, "Sounds", "FMOD"));
		if (!Directory.Exists(fmodPath))
			return;

		audioBankWatcher = new FileSystemWatcher(fmodPath, "*.bank")
		{
			IncludeSubdirectories = true,
			NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime
		};

		audioBankWatcher.Changed += (_, _) => RequestAudioBankReload();
		audioBankWatcher.Created += (_, _) => RequestAudioBankReload();
		audioBankWatcher.Deleted += (_, _) => RequestAudioBankReload();
		audioBankWatcher.Renamed += (_, _) => RequestAudioBankReload();
		audioBankWatcher.EnableRaisingEvents = true;
	}
#endif


	private void SelectRoom(Room room)
	{
		selectedRoomId = room.Id;
		cell = room.Cell;
		roomCreatePosition = room.TileSpacePosition;
	}

	private void ResolveSelectedRoom()
	{
		if (selectedRoomId != null && Assets.GetRoom(selectedRoomId) is { } byId)
		{
			SelectRoom(byId);
			return;
		}

		if (Assets.GetRoom(cell) is { } byCell)
		{
			SelectRoom(byCell);
			return;
		}

		if (Assets.GetRoom(Point2.Zero) is { } origin)
		{
			SelectRoom(origin);
			return;
		}

		selectedRoomId = null;
		cell = Point2.Zero;
		roomCreatePosition = Point2.Zero;
	}


	private void DebugViewMessageImGui()
	{
		if (game?.DebugViewMessage is not { } message)
			return;

		var viewport = GameViewRect;
		var textSize = ImGui.CalcTextSize(message);
		var padding = new Vector2(10, 6);
		var size = textSize + padding * 2;
		var position = new Vector2(
			viewport.CenterX - size.X * 0.5f,
			viewport.Bottom - size.Y - 8);

		ImGui.SetNextWindowPos(position, ImGuiCond.Always);
		ImGui.SetNextWindowSize(size, ImGuiCond.Always);
		ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, padding);
		ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0);
		ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0, 0, 0, 0.78f));
		ImGui.Begin(
			"DebugViewMessage",
			ImGuiWindowFlags.NoDecoration |
			ImGuiWindowFlags.NoInputs |
			ImGuiWindowFlags.NoSavedSettings |
			ImGuiWindowFlags.NoFocusOnAppearing |
			ImGuiWindowFlags.NoNav);
		ImGui.TextUnformatted(message);
		ImGui.End();
		ImGui.PopStyleColor();
		ImGui.PopStyleVar(2);
	}

	private void BudDebugOverlayImGui()
	{
		if (game == null || !playing || !game.TryGetBudDebugOverlay(out var worldPosition, out var label))
			return;

		var viewport = GameViewRect;
		var screenPosition = GameWorldToImGui(viewport, game, worldPosition);
		var textSize = ImGui.CalcTextSize(label) * 0.82f;
		var padding = new Vector2(4, 2);
		var size = textSize + padding * 2;
		var position = new Vector2(screenPosition.X - size.X * 0.5f, screenPosition.Y - size.Y);
		position.X = Math.Clamp(position.X, viewport.Left + 4, viewport.Right - size.X - 4);
		position.Y = Math.Clamp(position.Y, viewport.Top + 4, viewport.Bottom - size.Y - 4);

		ImGui.SetNextWindowPos(position, ImGuiCond.Always);
		ImGui.SetNextWindowSize(size, ImGuiCond.Always);
		ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, padding);
		ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 1);
		ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0, 0, 0, 0.72f));
		ImGui.Begin(
			"BudDebugOverlay",
			ImGuiWindowFlags.NoDecoration |
			ImGuiWindowFlags.NoInputs |
			ImGuiWindowFlags.NoSavedSettings |
			ImGuiWindowFlags.NoFocusOnAppearing |
			ImGuiWindowFlags.NoNav);
		ImGui.SetWindowFontScale(0.82f);
		ImGui.TextUnformatted(label);
		ImGui.SetWindowFontScale(1.0f);
		ImGui.End();
		ImGui.PopStyleColor();
		ImGui.PopStyleVar(2);
	}

	private void PlatformNavDebugOverlayImGui()
	{
		if (game == null || !playing || !game.IsPlatformNavDebugVisible)
			return;
		var graph = game.GetPlatformNavGraph();
		if (graph == null)
			return;

		var viewport = GameViewRect;
		var draw = ImGui.GetForegroundDrawList();
		draw.PushClipRect(viewport.TopLeft, viewport.BottomRight, true);

		foreach (var edge in graph.Edges)
		{
			var color = edge.Kind switch
			{
				PlatformNavGraph.EdgeKind.Jump => ImGuiColor(1.00f, 0.95f, 0.66f, 0.28f),
				PlatformNavGraph.EdgeKind.Fall => ImGuiColor(1.00f, 0.47f, 0.66f, 0.22f),
				PlatformNavGraph.EdgeKind.RoomExit => ImGuiColor(0.72f, 0.55f, 1.00f, 0.45f),
				_ => ImGuiColor(0.37f, 0.80f, 0.89f, 0.18f)
			};
			DrawPlatformNavEdge(draw, viewport, game, graph, edge, color, edge.Kind == PlatformNavGraph.EdgeKind.RoomExit ? 3.0f : 2.0f);
		}

		if (game.GetFirst<Bud>() is { } bud)
			DrawBudPlatformNavOverlay(draw, viewport, game, graph, bud);

		foreach (var node in graph.Nodes)
		{
			var color = node.Kind switch
			{
				PlatformNavGraph.NodeKind.Ledge => ImGuiColor(1.00f, 0.83f, 0.48f, 0.78f),
				PlatformNavGraph.NodeKind.RoomExit => ImGuiColor(0.72f, 0.55f, 1.00f, 0.90f),
				_ => ImGuiColor(0.65f, 0.94f, 0.44f, 0.62f)
			};
			var radius = node.Kind == PlatformNavGraph.NodeKind.RoomExit ? 7.0f : 4.0f;
			draw.AddCircleFilled(GameWorldToImGui(viewport, game, node.Position), radius, color, 8);
		}

		draw.PopClipRect();
	}

	private void DrawBudPlatformNavOverlay(ImDrawListPtr draw, Rect viewport, Game game, PlatformNavGraph graph, Bud bud)
	{
		foreach (var edge in bud.DebugNavPath)
		{
			var color = edge.Kind == PlatformNavGraph.EdgeKind.Jump
				? ImGuiColor(1.00f, 0.95f, 0.66f, 0.95f)
				: ImGuiColor(1.00f, 1.00f, 1.00f, 0.78f);
			DrawPlatformNavEdge(draw, viewport, game, graph, edge, color, edge.Kind == PlatformNavGraph.EdgeKind.Jump ? 4.0f : 5.0f);
		}

		DrawPlatformNavNodeRing(draw, viewport, game, graph, bud.DebugNavStartNodeId, ImGuiColor(0.37f, 0.80f, 0.89f, 0.95f), 14.0f);
		DrawPlatformNavNodeRing(draw, viewport, game, graph, bud.DebugNavGoalNodeId, ImGuiColor(1.00f, 0.47f, 0.66f, 0.95f), 14.0f);
		DrawPlatformNavNodeRing(draw, viewport, game, graph, bud.DebugNavTargetNodeId, ImGuiColor(1.00f, 1.00f, 1.00f, 0.95f), 16.0f);

		var from = GameWorldToImGui(viewport, game, bud.Position);
		var to = GameWorldToImGui(viewport, game, bud.Position + new Vector2(0, -14));
		draw.AddLine(from, to, ImGuiColor(1.00f, 1.00f, 1.00f, 0.55f), 2.0f);

		if (bud.DebugNavProbeRect is { } probe)
		{
			var probeColor = bud.DebugNavProbeHit
				? ImGuiColor(0.00f, 1.00f, 0.00f, 0.50f)
				: ImGuiColor(1.00f, 0.50f, 0.00f, 0.50f);
			var probeTL = GameWorldToImGui(viewport, game, new Vector2(probe.X, probe.Y));
			var probeBR = GameWorldToImGui(viewport, game, new Vector2(probe.X + probe.Width, probe.Y + probe.Height));
			draw.AddRectFilled(probeTL, probeBR, probeColor);
			draw.AddRect(probeTL, probeBR, ImGuiColor(1.00f, 1.00f, 1.00f, 0.80f), 0, ImDrawFlags.None, 3.0f);
		}
	}

	private static void DrawPlatformNavEdge(ImDrawListPtr draw, Rect viewport, Game game, PlatformNavGraph graph, PlatformNavGraph.Edge edge, uint color, float thickness)
	{
		if (edge.From < 0 || edge.From >= graph.Nodes.Count || edge.To < 0 || edge.To >= graph.Nodes.Count)
			return;

		var from = graph.Nodes[edge.From].Position;
		var to = graph.Nodes[edge.To].Position;
		if (edge.Kind == PlatformNavGraph.EdgeKind.Jump)
			DrawPlatformNavJump(draw, viewport, game, from, to, color, thickness);
		else
			draw.AddLine(GameWorldToImGui(viewport, game, from), GameWorldToImGui(viewport, game, to), color, thickness);
	}

	private static void DrawPlatformNavJump(ImDrawListPtr draw, Rect viewport, Game game, Vector2 from, Vector2 to, uint color, float thickness)
	{
		var control = new Vector2((from.X + to.X) * 0.5f, MathF.Min(from.Y, to.Y) - 24);
		var previous = GameWorldToImGui(viewport, game, from);
		const int segments = 12;
		for (var i = 1; i <= segments; i++)
		{
			var t = i / (float)segments;
			var a = Vector2.Lerp(from, control, t);
			var b = Vector2.Lerp(control, to, t);
			var point = GameWorldToImGui(viewport, game, Vector2.Lerp(a, b, t));
			draw.AddLine(previous, point, color, thickness);
			previous = point;
		}
	}

	private static void DrawPlatformNavNodeRing(ImDrawListPtr draw, Rect viewport, Game game, PlatformNavGraph graph, int? nodeId, uint color, float radius)
	{
		if (nodeId is not { } id || id < 0 || id >= graph.Nodes.Count)
			return;

		draw.AddCircle(GameWorldToImGui(viewport, game, graph.Nodes[id].Position), radius, color, 16, 4.0f);
	}

	private static uint ImGuiColor(float r, float g, float b, float a)
		=> ImGui.ColorConvertFloat4ToU32(new Vector4(r, g, b, a));

	private static Vector2 GameWorldToImGui(Rect viewport, Game game, Vector2 worldPosition)
	{
		var scale = MathF.Min(viewport.Width / game.Screen.Width, viewport.Height / game.Screen.Height);
		var size = new Vector2(game.Screen.Width, game.Screen.Height) * scale;
		var topLeft = viewport.Center - size / 2;
		return topLeft + (worldPosition - game.Camera) * scale;
	}

	private void FpsOverlayImGui()
	{
		if (!fpsOverlayVisible || fpsOverlayValue <= 0)
			return;

		var viewport = !playing && !startingPlay ? View : GameViewRect;
		var perf = PerfStats.Snapshot;
#if DEBUG
		var mainMessage = fpsOverlayMode == 0
			? $"FPS {fpsOverlayValue:0} GC {threadAllocatedBytesOverlayValue} bytes"
			: $"FPS {fpsOverlayValue:0}\n" +
			  $"Upd {perf.Update:0.0}  Rnd {perf.Render:0.0}  Prs {perf.Present:0.0}\n" +
			  $"Wld {perf.World:0.0}  Lit {perf.Lighting:0.0}  Fin {perf.FinalComposite:0.0}\n" +
			  $"Nrm {perf.Normal:0.0}  T+D {perf.TileDither:0.0}  Pst {perf.Post:0.0}";
#else
		var mainMessage = fpsOverlayMode == 0
			? $"FPS {fpsOverlayValue:0}"
			: $"FPS {fpsOverlayValue:0}\n" +
			  $"Upd {perf.Update:0.0}  Rnd {perf.Render:0.0}  Prs {perf.Present:0.0}\n" +
			  $"Wld {perf.World:0.0}  Lit {perf.Lighting:0.0}  Fin {perf.FinalComposite:0.0}\n" +
			  $"Nrm {perf.Normal:0.0}  T+D {perf.TileDither:0.0}  Pst {perf.Post:0.0}";
#endif
		var detailMessage = fpsOverlayMode >= 2 ? PerfStats.DetailSummary : string.Empty;
		var mainTextSize = ImGui.CalcTextSize(mainMessage);
		var detailTextSize = string.IsNullOrEmpty(detailMessage) ? Vector2.Zero : ImGui.CalcTextSize(detailMessage) * 0.8f;
		var padding = new Vector2(6, 3);
		var textSize = new Vector2(
			MathF.Max(mainTextSize.X, detailTextSize.X),
			mainTextSize.Y + (detailTextSize.Y > 0 ? detailTextSize.Y + 2 : 0));
		var size = textSize + padding * 2;
		var position = new Vector2(
			viewport.Right - size.X - 6,
			viewport.Top + 6);

		ImGui.SetNextWindowPos(position, ImGuiCond.Always);
		ImGui.SetNextWindowSize(size, ImGuiCond.Always);
		ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, padding);
		ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 1);
		ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0, 0, 0, 0.55f));
		ImGui.Begin(
			"FpsOverlay",
			ImGuiWindowFlags.NoDecoration |
			ImGuiWindowFlags.NoSavedSettings |
			ImGuiWindowFlags.NoFocusOnAppearing |
			ImGuiWindowFlags.NoNav);
		ImGui.TextUnformatted(mainMessage);
		if (!string.IsNullOrEmpty(detailMessage))
		{
			ImGui.SetWindowFontScale(0.8f);
			ImGui.TextUnformatted(detailMessage);
			ImGui.SetWindowFontScale(1.0f);
		}
		if (ImGui.IsWindowHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
			fpsOverlayMode = (fpsOverlayMode + 1) % 3;
		ImGui.End();
		ImGui.PopStyleColor();
		ImGui.PopStyleVar(2);
	}

	private void TransitionViewerImGui()
	{
		if (!showTransitionViewer)
			return;

		if (ImGui.Begin("Transition Viewer", ref showTransitionViewer, ImGuiWindowFlags.NoSavedSettings))
		{
			if (ImGui.BeginTable("transitions", 4, ImGuiTableFlags.BordersV | ImGuiTableFlags.BordersOuterH | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY,
				new Vector2(ImGui.GetContentRegionAvail().X, 480)))
			{
				ImGui.TableSetupScrollFreeze(0, 1);
				ImGui.TableSetupColumn("Sprite", ImGuiTableColumnFlags.WidthFixed, 100);
				ImGui.TableSetupColumn("Animation", ImGuiTableColumnFlags.WidthFixed, 160);
				ImGui.TableSetupColumn("Frames", ImGuiTableColumnFlags.WidthStretch);
				ImGui.TableSetupColumn("Count", ImGuiTableColumnFlags.WidthFixed, 60);
				ImGui.TableHeadersRow();

				foreach (var (spriteName, sprite) in Assets.Sprites)
				{
					foreach (var anim in sprite.Animations)
					{
						if (!anim.Name.Contains("->"))
							continue;

						ImGui.TableNextRow();
						ImGui.TableNextColumn();
						ImGui.TextUnformatted(spriteName);
						ImGui.TableNextColumn();
						ImGui.TextUnformatted(anim.Name);
						ImGui.TableNextColumn();

						float previewSize = 48;
						for (int i = 0; i < anim.FrameCount; i++)
						{
							if (i > 0) ImGui.SameLine(0, 2);
							int frameIdx = anim.FrameStart + i;
							if (frameIdx < sprite.Frames.Count)
							{
								var frame = sprite.Frames[frameIdx];
								var sub = frame.VisibleSubtexture;
								float scale = MathF.Min(previewSize / sub.Size.X, previewSize / sub.Size.Y);
								imgui.Image(sub, sub.Size * scale);
								if (ImGui.IsItemHovered())
									ImGui.SetTooltip($"Frame {i}\n{frame.Duration * 1000:F0}ms");
							}
						}

						ImGui.TableNextColumn();
						ImGui.TextUnformatted($"{anim.FrameCount}");
					}
				}

				ImGui.EndTable();
			}
		}
		ImGui.End();
	}

}
#endif
