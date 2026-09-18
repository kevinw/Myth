using System.Numerics;
using Foster.Framework;

namespace Myth;

public partial class Game
{
	public readonly record struct MapPin(string ActorId, Vector2 Position);

	static float MapRevealOpenDelay => 0.5f;
	static float MapRevealPanDuration => 1.2f;
	static float MapRevealHold => 0.4f;
	static float MapRevealAfterPop => 0.7f;

	readonly Coroutine mapReveal = new();
	bool mapRevealBlocking;
	static readonly HashSet<string> revealedMapGroups = [];
	static readonly List<MapPin> mapPinsBefore = [];

	// Derived fresh from the rooms and the save, so nothing about a pin is stored: a MapTarget in
	// this sublayer is lit when a read InspectPoint reveals its group, until the camera frames it.
	internal void CollectMapPins(List<MapPin> pins, int sublayer)
	{
		revealedMapGroups.Clear();
		foreach (var room in Assets.RoomsById.Values)
			foreach (var actor in room.Actors)
				if (ActorSpecs.GetTypeName(actor) == nameof(InspectPoint) &&
					ActorSpecs.GetActorId(actor) is { } id && Save.ReadInspectPoints.Contains(id) &&
					ReadActorString(actor, nameof(InspectPoint.RevealsMapTargets)) is { Length: > 0 } group)
					revealedMapGroups.Add(group);
		if (revealedMapGroups.Count == 0)
			return;

		foreach (var room in Assets.RoomsById.Values)
			if (room.Sublayer == sublayer)
				foreach (var actor in room.Actors)
					if (ActorSpecs.GetTypeName(actor) == nameof(MapTarget) &&
						revealedMapGroups.Contains(ReadActorString(actor, nameof(MapTarget.Group)) ?? "") &&
						ActorSpecs.GetActorId(actor) is { } id &&
						(Vector2)(room.WorldBounds.TopLeft + (ActorSpecs.ReadResolvedPosition(actor) ?? Point2.Zero)) is var position &&
						!Minimap.HasSeen(room, position))
						pins.Add(new(id, position));

		static string? ReadActorString(System.Text.Json.Nodes.JsonObject actor, string name)
			=> ActorSpecs.GetResolvedProperty(actor, name)?.GetValue<string>()?.Trim();
	}

	// Records the read (the only saved fact); if that lit new pins here, the reveal plays once the
	// line has finished typing.
	internal void ReadInspectPoint(InspectPoint point, StorySpeechBubble? speech)
	{
		if (string.IsNullOrWhiteSpace(point.RevealsMapTargets) || point.ActorId is not { Length: > 0 } id ||
			Save.ReadInspectPoints.Contains(id) || room == null)
			return;

		mapPinsBefore.Clear();
		CollectMapPins(mapPinsBefore, room.Sublayer);
		Save.ReadInspectPoints.Add(id);
		MarkSaveDirty();

		var revealed = new List<MapPin>();
		CollectMapPins(revealed, room.Sublayer);
		revealed.RemoveAll(pin => mapPinsBefore.Contains(pin));
		if (revealed.Count > 0 && speech != null)
			mapReveal.Start(RevealMapPins(speech, revealed));
	}

	// True while the reveal holds the sim (waiting on the map button, then the map sequence).
	bool UpdateMapReveal()
	{
		if (!mapReveal.Active)
			return false;
		if (!mapReveal.Update(Manager.Time.Delta))
			mapRevealBlocking = false;
		return mapRevealBlocking;
	}

	bool MapRevealSkipPressed()
	{
		var input = Manager.Input;
		return Controls.Map.ConsumePress() || Controls.Jump.Pressed ||
			input.AnyControllerButtonPressed(Buttons.Back) || input.AnyControllerButtonPressed(Buttons.South) ||
			input.AnyControllerButtonPressed(Buttons.East) || input.Keyboard.Pressed(Keys.Enter);
	}

	IEnumerator<float> RevealMapPins(StorySpeechBubble speech, List<MapPin> pins)
	{
		while (!speech.Finished && speech.VisibleLength < speech.Text.Length)
			yield return 0;

		mapRevealBlocking = true;
		while (!Controls.Map.ConsumePress() && !Manager.Input.AnyControllerButtonPressed(Buttons.Back))
		{
			if (Time.BetweenInterval(0.3, 0.5, 0))
				View?.contextPrompt = "Press {Map} to open the map.";
			yield return 0;
		}

		Minimap.WarpMode = false;
		Minimap.Open = true;
		PlayAudioEvent("map/open");
		Minimap.ResetView();
		Minimap.Rebuild(room!, this);
		foreach (var pin in pins)
			Minimap.PinPoppedAt[pin.ActorId] = float.PositiveInfinity;
		Minimap.Focus = GetFirst<Player>()?.Position ?? room!.WorldBounds.Center;

		var skipped = false;
		for (var elapsed = 0f; elapsed < MapRevealOpenDelay && !skipped; elapsed += Manager.Time.Delta)
		{
			skipped = MapRevealSkipPressed();
			yield return 0;
		}

		foreach (var pin in pins)
		{
			if (skipped)
				break;
			PlayAudioEvent("map/warp_move");
			var from = Minimap.Focus ?? pin.Position;
			for (var elapsed = 0f; elapsed < MapRevealPanDuration + MapRevealHold && !skipped; elapsed += Manager.Time.Delta)
			{
				Minimap.Focus = Vector2.Lerp(from, pin.Position, Ease.Cube.InOut(Math.Clamp(elapsed / MapRevealPanDuration, 0, 1)));
				skipped = MapRevealSkipPressed();
				yield return 0;
			}
			if (skipped)
				break;
			Minimap.Focus = pin.Position;
			Minimap.PinPoppedAt[pin.ActorId] = Time.SecondsF;
			PlayAudioEvent("reveal");
			for (var elapsed = 0f; elapsed < MapRevealAfterPop && !skipped; elapsed += Manager.Time.Delta)
			{
				skipped = MapRevealSkipPressed();
				yield return 0;
			}
		}

		if (!skipped || !float.IsPositiveInfinity(Minimap.PinPoppedAt[pins[^1].ActorId]))
			yield break;
		Minimap.Focus = pins[^1].Position;
		foreach (var pin in pins)
			if (float.IsPositiveInfinity(Minimap.PinPoppedAt[pin.ActorId]))
				Minimap.PinPoppedAt[pin.ActorId] = Time.SecondsF;
		PlayAudioEvent("reveal");
	}
}
