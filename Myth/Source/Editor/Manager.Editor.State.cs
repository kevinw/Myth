#if EDITOR
using System.Numerics;
using System.Text.Json.Nodes;
using Foster.Framework;

namespace Myth;

public partial class Manager
{
	readonly record struct RoomSettings(string? MusicArea, Color BackdropColor, Color? BackdropColor2, Color AmbientColor, float AmbientIntensity, float LightNoise, bool PlayerLightEnabled, float SporeFogStrength, bool ReverbAuto, float Reverb, Color SunColor, float SunStrength, Vector2 SunDirection, bool LightingDitherEnabled, float LightingDitherScale, float LightingDitherStrength, float NormalDitherSlopeBoost, bool NormalDitherQuantization);

	private sealed class EditorSelectionSnapshot
	{
		public RectInt Rect;
		public bool IncludeBackgroundTiles;
		public bool IncludeForegroundTiles;
		public bool IncludeActors;
		public string RoomJson = "";
		public char[,]? BackgroundTiles;
		public char[,]? ForegroundTiles;
		public readonly List<(int Index, JsonObject Actor, Point2 Position)> Actors = [];
	}

	private enum EditorDragKind
	{
		None,
		RoomActor,
		ActorPathPoint,
		ActorGizmo,
		ActorResize,
		SelectionRect,
		SelectionMove,
		RoomGeometry,
		TilePaint
	}

	private struct EditorDragState
	{
		public EditorDragKind Kind;
		public Point2 ActorStartPosition;
		public Vector2 ActorPressWorld;
		public bool ActorDragStarted;
		public bool ActorSnapsByDefault;
		public int PathPointIndex;
		public string? GizmoId;
		public ResizeHandle ResizeHandle;
		public ResizeHandle RoomGeometryHandle;
		public Point2 RoomGeometryOffset;
		public bool DuplicateSelection;
		public Point2 SelectionAnchor;
		public Point2 SelectionOffset;
		public EditorSelectionSnapshot? SelectionSnapshot;

		public static EditorDragState None => new()
		{
			Kind = EditorDragKind.None,
			ActorSnapsByDefault = true
		};
	}

	internal readonly struct EditorActor(Room room, JsonObject json)
	{
		internal readonly Room Room = room;
		internal readonly JsonObject Json = json;
		internal Vector2 RoomOrigin => Room.WorldBounds.TopLeft;
		internal RectInt Bounds => RoomActorBounds(Room, Json);

		internal Signs GetFacing()
			=> ActorSpecs.ReadSigns(ActorSpecs.GetResolvedProperty(Json, nameof(Actor.Facing)));

		internal T GetEnum<T>(string property, T fallback) where T : struct, Enum
			=> Enum.TryParse<T>(ActorSpecs.GetResolvedProperty(Json, property)?.GetValue<string>(), true, out var value) ? value : fallback;

		internal Cardinal GetCardinal(string property, Cardinal fallback)
			=> ActorSpecs.ReadCardinal(ActorSpecs.GetResolvedProperty(Json, property)) ?? fallback;

		internal int GetInt(string property, int fallback)
			=> ActorSpecs.GetResolvedProperty(Json, property)?.GetValue<int>() ?? fallback;

		internal Point2 GetRoomPoint(string property, Point2 fallback)
			=> ActorSpecs.ReadPoint(ActorSpecs.GetResolvedProperty(Json, property)) ?? fallback;

		internal bool Has(string property) => ActorSpecs.GetResolvedProperty(Json, property) != null;
		internal void Set(string property, int value) => ActorSpecs.SetProperty(Json, property, JsonValue.Create(value));
		internal void Set(string property, Point2 value) => ActorSpecs.SetProperty(Json, property, ActorSpecs.ToJsonNode(value));
	}

	internal abstract class ActorEditor
	{
		internal abstract Type ActorType { get; }
		internal abstract void Gizmos(EditorActor actor, ref EditorGizmos gizmos);
		internal virtual void BeforeMove(EditorActor actor) { }
		internal virtual void PropertyChanged(EditorActor actor, string property) { }
	}

	internal abstract class ActorEditor<T> : ActorEditor where T : Actor
	{
		internal override Type ActorType => typeof(T);
	}

	internal struct EditorGizmos
	{
		readonly Manager manager;
		readonly EditorActor actor;
		readonly Vector2 worldMouse;
		readonly float lineWeight;
		readonly bool draw;
		readonly bool canInteract;
		readonly bool keyboardBlocked;
		internal bool ConsumedPress;
		internal float LineWeight => lineWeight;

		internal EditorGizmos(Manager manager, EditorActor actor, Vector2 worldMouse, float lineWeight,
			bool draw, bool canInteract, bool keyboardBlocked)
		{
			this.manager = manager;
			this.actor = actor;
			this.worldMouse = worldMouse;
			this.lineWeight = lineWeight;
			this.draw = draw;
			this.canInteract = canInteract;
			this.keyboardBlocked = keyboardBlocked;
			ConsumedPress = false;
		}

		internal void Line(Vector2 from, Vector2 to, float width, Color color)
		{
			if (draw)
				manager.batcher.Line(from, to, width, color);
		}

		internal void Arrow(Vector2 from, Vector2 to, float width, Color color, float headSize = 4f)
		{
			if (!draw)
				return;

			var shaft = to - from;
			var length = shaft.Length();
			manager.batcher.Line(from, to, width, color);
			if (length < 0.0001f)
				return;

			var dir = shaft / length;
			var side = new Vector2(-dir.Y, dir.X);
			var head = MathF.Min(headSize, length);
			manager.batcher.Line(to, to - dir * head + side * (head * 0.7f), width, color);
			manager.batcher.Line(to, to - dir * head - side * (head * 0.7f), width, color);
		}

		internal void CircleDashed(Vector2 center, float radius, float width, int steps, Color color, float dashLength)
		{
			if (draw && radius > 0)
				manager.batcher.CircleDashed(center, radius, width, steps, color, dashLength, 0);
		}

		internal void RoomPointHandle(string property, Vector2 position, Color color, string undoName)
		{
			var radius = PointPathHandleRadius(lineWeight);
			var active = manager.DragKind == EditorDragKind.ActorGizmo && manager.editorDrag.GizmoId == property;
			var hovered = Vector2.DistanceSquared(worldMouse, position) <= radius * radius * 2.25f;

			if (!draw && canInteract && !active && manager.Input.Mouse.LeftPressed && hovered)
			{
				manager.ClearEditorDragState();
				manager.editorDrag.Kind = EditorDragKind.ActorGizmo;
				manager.editorDrag.GizmoId = property;
				manager.editorDrag.ActorSnapsByDefault = true;
				manager.editorSelectionRect = null;
				manager.CancelTilePaint();
				ConsumedPress = active = true;
			}

			if (!draw && canInteract && active && manager.Input.Mouse.LeftDown)
			{
				manager.OpenPendingRoomUndo(ActorMoveUndoSlot, undoName, actor.Room);
				var local = worldMouse - actor.RoomOrigin;
				var point = manager.RoomActorDragShouldSnap(keyboardBlocked)
					? (local / Game.TileSize).RoundToPoint2() * Game.TileSize
					: local.RoundToPoint2();
				actor.Set(property, point);
				manager.editorGameDirty = true;
			}

			if (draw)
			{
				manager.batcher.Circle(position, radius, 12, active ? Color.White : color);
				manager.batcher.CircleLine(position, radius, lineWeight, 12, Color.White);
			}
		}

		internal void CardinalDistanceHandle(string property, Vector2 origin, Cardinal direction, int distance,
			Color color, string undoName)
		{
			var position = origin + direction * (float)distance;
			var radius = PointPathHandleRadius(lineWeight);
			var active = manager.DragKind == EditorDragKind.ActorGizmo && manager.editorDrag.GizmoId == property;
			var hovered = Vector2.DistanceSquared(worldMouse, position) <= radius * radius * 2.25f;

			if (!draw && canInteract && !active && manager.Input.Mouse.LeftPressed && hovered)
			{
				manager.ClearEditorDragState();
				manager.editorDrag.Kind = EditorDragKind.ActorGizmo;
				manager.editorDrag.GizmoId = property;
				manager.editorDrag.ActorSnapsByDefault = true;
				manager.editorSelectionRect = null;
				manager.CancelTilePaint();
				ConsumedPress = active = true;
			}

			if (!draw && canInteract && active && manager.Input.Mouse.LeftDown)
			{
				manager.OpenPendingRoomUndo(ActorMoveUndoSlot, undoName, actor.Room);
				var projected = Vector2.Dot(worldMouse - origin, direction.Normal);
				var next = manager.RoomActorDragShouldSnap(keyboardBlocked)
					? Calc.Round(projected / Game.TileSize) * Game.TileSize
					: Calc.Round(projected);
				actor.Set(property, Math.Max(0, next));
				manager.editorGameDirty = true;
			}

			if (draw)
			{
				manager.batcher.Circle(position, radius, 12, active ? Color.White : color);
				manager.batcher.CircleLine(position, radius, lineWeight, 12, Color.White);
			}
		}
	}
}
#endif
