#if EDITOR
using System.Numerics;
using System.Text.Json.Nodes;
using Foster.Framework;

namespace Myth;

public partial class Manager
{
	private readonly record struct RoomSettings(string? MusicArea, Color BackdropColor, Color? BackdropColor2, Color AmbientColor, float AmbientIntensity, float LightNoise, bool ReverbAuto, float Reverb, Color SunColor, float SunStrength, Vector2 SunDirection);

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
		ActorResize,
		SelectionRect,
		SelectionMove,
		RoomGeometry,
		TilePaint
	}

	private struct EditorDragState
	{
		public EditorDragKind Kind;
		public Vector2 ActorOffset;
		public bool ActorSnapsByDefault;
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
}
#endif
