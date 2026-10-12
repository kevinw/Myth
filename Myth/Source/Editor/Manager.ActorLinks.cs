#if EDITOR
using System.Numerics;
using System.Text.Json.Nodes;
using Foster.Framework;

namespace Myth;

// Canvas editing for [ActorRef] lists (a Button's targets, say): the selected source draws a line to
// each actor it links to, and holding Alt over an actor it could link to toggles that link on click.
// Alt is otherwise the tile eyedropper, so link picking only claims it while the cursor is over a
// linkable actor -- which is outlined, with a + or - for what the click will do.
public partial class Manager
{
	static readonly Color LinkColor = new(0xf2c14e);
	static readonly Color MissingLinkColor = new(0xe4505f);

	static ActorSpecs.EditableProperty? ActorLinkProperty(JsonObject actor)
	{
		foreach (var property in ActorSpecs.GetEditableProperties(actor))
			if (property.Type == typeof(string[]) && property.ActorRef != null)
				return property;
		return null;
	}

	static bool CanLink(JsonObject source, JsonObject candidate, ActorSpecs.EditableProperty link)
		=> candidate != source && MatchesActorRefKind(candidate, link.ActorRef!.Kind);

	// Returns true while it owns the Alt gesture (the cursor is over a linkable actor).
	bool UpdateActorLinks(Room room, int sourceIndex, int hoveredIndex, bool altHeld, bool canClick, float lineWeight)
	{
		if (sourceIndex < 0 || sourceIndex >= room.Actors.Count)
			return false;
		var source = room.Actors[sourceIndex];
		if (ActorLinkProperty(source) is not { } link)
			return false;

		var ids = ActorSpecs.ReadStringArray(link.Value) ?? [];
		var from = (Vector2)RoomActorBounds(room, source).Center;
		foreach (var id in ids)
		{
			if (Assets.FindActorById(id) is { } target)
				DrawLink(from, RoomActorBounds(target.Room, target.Actor), lineWeight, LinkColor);
			else
				batcher.Line(from, from + new Vector2(0, -6), lineWeight * 2, MissingLinkColor);
		}

		if (!altHeld)
			return false;

		foreach (var candidate in room.Actors)
			if (CanLink(source, candidate, link))
				batcher.RectLine(RoomActorBounds(room, candidate).Inflate((int)MathF.Ceiling(lineWeight * 2)), lineWeight, LinkColor * 0.5f);

		if (hoveredIndex < 0 || hoveredIndex >= room.Actors.Count || !CanLink(source, room.Actors[hoveredIndex], link))
			return false;

		var hovered = room.Actors[hoveredIndex];
		var hoveredId = ActorSpecs.GetActorId(hovered);
		var linked = hoveredId != null && Array.Exists(ids, id => string.Equals(id, hoveredId, StringComparison.OrdinalIgnoreCase));
		var bounds = RoomActorBounds(room, hovered).Inflate((int)MathF.Ceiling(lineWeight * 2));
		batcher.Rect(bounds, LinkColor * 0.2f);
		batcher.RectLine(bounds, lineWeight * 2, LinkColor);
		DrawLink(from, bounds, lineWeight, linked ? MissingLinkColor : LinkColor);
		var sign = (Vector2)bounds.TopRight;
		var arm = lineWeight * 4;
		batcher.Line(sign - new Vector2(arm, 0), sign + new Vector2(arm, 0), lineWeight * 2, linked ? MissingLinkColor : LinkColor);
		if (!linked)
			batcher.Line(sign - new Vector2(0, arm), sign + new Vector2(0, arm), lineWeight * 2, LinkColor);

		if (canClick && Input.Mouse.LeftPressed)
		{
			using (CaptureRoomUndo(linked ? "Unlink Actor" : "Link Actor", room))
			{
				ActorSpecs.EnsureActorId(hovered);
				var id = ActorSpecs.GetActorId(hovered)!;
				string[] next = linked
					? Array.FindAll(ids, it => !string.Equals(it, id, StringComparison.OrdinalIgnoreCase))
					: [.. ids, id];
				ActorSpecs.SetProperty(source, link.JsonName, ActorSpecs.ToJsonNode(next));
			}
			MarkRoomPreviewDirty(room);
			editorGameDirty = true;
		}
		return true;
	}

	void DrawLink(Vector2 from, RectInt target, float lineWeight, Color color)
	{
		var to = (Vector2)target.Center;
		batcher.Line(from, to, lineWeight, color);
		batcher.Rect(Rect.CenteredAt(to, Vector2.One * lineWeight * 4), color);
	}
}
#endif
