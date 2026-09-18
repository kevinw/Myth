#if EDITOR
using System.Numerics;
using Foster.Framework;
using ImGuiNET;

namespace Myth;

public partial class Manager
{
	const string LiveRoomCreateUndoName = "Create Room While Playing";
	static bool LiveRoomCreationEnabled => false;
	static int LiveRoomPlatformExtensionTiles => 10;

	bool liveRoomEditingEnabled = true;
	bool liveCreateRoomPopupRequested;
	RectInt? liveCreateRoomTiles;
	string? liveCreateRoomSourceId;
	Point2 liveCreateRoomDirection;
	Vector2 liveCreateRoomExitPosition;
	string? liveDismissedRoomCreateSourceId;
	Point2 liveDismissedRoomCreateDirection;

	bool LiveRoomCreationModalOpen => liveCreateRoomTiles != null;
	bool LiveRoomEditorMenuOpen => liveCreateRoomPopupRequested || liveCreateRoomTiles != null;

	void ToggleLiveRoomEditing()
	{
		liveRoomEditingEnabled = !liveRoomEditingEnabled;
		DisplayMessage($"Live room editing {(liveRoomEditingEnabled ? "on" : "off")}");
		if (!liveRoomEditingEnabled)
			ClearLiveRoomCreateProposal();
	}

	bool UpdateLiveRoomEditing(Game liveGame, bool inputBlocked)
	{
		if (UneditableSession)
			return false;

		if (!inputBlocked && debugControls.ToggleLiveRoomEditing.Pressed)
			ToggleLiveRoomEditing();
		if (!inputBlocked && LiveRoomEditingControllerTogglePressed())
			ToggleLiveRoomEditing();
		if (liveDismissedRoomCreateSourceId != null && controls.Move.IntValue != liveDismissedRoomCreateDirection)
			ClearDismissedLiveRoomCreateProposal();

		if (LiveRoomCreationModalOpen)
		{
			controls.Move.Clear();
			return true;
		}

		return false;
	}

	bool LiveRoomEditingControllerTogglePressed()
	{
		foreach (var controller in Input.Controllers)
			if (controller.Connected &&
				controller.Down(Buttons.LeftShoulder) &&
				controller.Down(Buttons.RightShoulder) &&
				controller.Pressed(Buttons.Down))
				return true;
		return false;
	}

	internal bool NotifyMissingLiveAdjacentRoom(Game liveGame, Room runtimeRoom, Point2 direction, Vector2 exitPosition)
	{
		if (!LiveRoomCreationEnabled)
			return false;
		if (!playing || !liveRoomEditingEnabled || liveCreateRoomTiles != null || !liveGame.AcceptsLiveInput)
			return false;
		if (Assets.GetRoom(runtimeRoom.Id) is not { } source)
			return false;
		if (liveDismissedRoomCreateSourceId == source.Id && liveDismissedRoomCreateDirection == direction.Sign())
			return false;

		var proposal = GetLiveRoomProposal(source, direction, exitPosition);
		if (proposal == null)
		{
			DisplayMessage("No room space there");
			return false;
		}

		liveCreateRoomSourceId = source.Id;
		liveCreateRoomDirection = direction.Sign();
		liveCreateRoomExitPosition = exitPosition;
		liveCreateRoomTiles = proposal;
		liveCreateRoomPopupRequested = true;
		return true;
	}

	RectInt? GetLiveRoomProposal(Room source, Point2 direction, Vector2 exitPosition)
	{
		var sourceTiles = new RectInt(source.TileSpacePosition, source.Size);
		var exitTile = WorldToRoomGeometryTile(exitPosition);
		if (direction.X > 0)
			return FitProposedRoom(sourceTiles, Point2.Right, exitTile.Y);
		if (direction.X < 0)
			return FitProposedRoom(sourceTiles, Point2.Left, exitTile.Y);
		if (direction.Y > 0)
			return FitProposedRoom(sourceTiles, Point2.Down, exitTile.X);
		if (direction.Y < 0)
			return FitProposedRoom(sourceTiles, Point2.Up, exitTile.X);
		return null;
	}

	void LiveRoomEditingImGui()
	{
		if (!playing || UneditableSession || imgui == null)
			return;

		if (liveCreateRoomTiles is { } proposal)
			DrawLiveCreateRoomPreview(proposal);
	}

	void LiveRoomEditingMenuImGui()
	{
		if (!playing || UneditableSession)
			return;
		if (liveCreateRoomPopupRequested)
		{
			liveCreateRoomPopupRequested = false;
			ImGui.OpenPopup("Create Adjacent Room");
		}

		var open = liveCreateRoomTiles != null;
		if (!open)
			return;

		ImGui.SetNextWindowSize(new Vector2(280, 0), ImGuiCond.Appearing);
		if (!BeginPaddedPopupModal("Create Adjacent Room", ref open, ImGuiWindowFlags.AlwaysAutoResize))
			return;

		ImGui.TextUnformatted("No adjacent room exists.");
		ImGui.Spacing();
		var create = ImGui.Button("Create New Room", new Vector2(-1, 0));
		ImGui.SetItemDefaultFocus();
		if (create)
		{
			CreatePendingLiveRoom();
			ImGui.CloseCurrentPopup();
		}
		if (ImGui.Button("Cancel", new Vector2(-1, 0)) ||
			ImGui.IsKeyPressed(ImGuiKey.Escape) ||
			ImGui.IsKeyPressed(ImGuiKey.GamepadFaceRight))
		{
			DismissLiveRoomCreateProposal();
			ImGui.CloseCurrentPopup();
		}

		EndPaddedPopup();
		if (!open)
			DismissLiveRoomCreateProposal();
	}

	void DrawLiveCreateRoomPreview(RectInt proposal)
	{
		var viewport = GameViewRect;
		var draw = ImGui.GetForegroundDrawList();
		draw.PushClipRect(viewport.TopLeft, viewport.BottomRight, true);
		var rect = TileRectToWorldRect(proposal);
		var topLeft = GameWorldToImGui(viewport, game!, rect.TopLeft);
		var bottomRight = GameWorldToImGui(viewport, game!, rect.BottomRight);
		draw.AddRectFilled(topLeft, bottomRight, ImGuiColor(0.5f, 0.6f, 0.7f, 0.18f));
		draw.AddRect(topLeft, bottomRight, ImGuiColor(0.75f, 0.9f, 1, 0.9f), 0, ImDrawFlags.None, 2);
		draw.PopClipRect();
	}

	void CreatePendingLiveRoom()
	{
		if (liveCreateRoomTiles is not { } proposal || liveCreateRoomSourceId == null ||
			Assets.GetRoom(liveCreateRoomSourceId) is not { } source)
		{
			ClearLiveRoomCreateProposal();
			return;
		}

		var direction = liveCreateRoomDirection;
		var exitPosition = liveCreateRoomExitPosition;
		var created = CreateRoomFromSource(source, proposal.Position, proposal.Size, select: false, tween: false, undoName: LiveRoomCreateUndoName,
			configure: room => ExtendPlatformIntoLiveRoom(source, room, direction, exitPosition));
		MarkRoomPreviewDirty(source);
		MarkRoomPreviewDirty(created);
		game?.CheatShiftRoom(direction);
		DisplayMessage($"Created {created.Id}");
		ClearLiveRoomCreateProposal();
		ClearDismissedLiveRoomCreateProposal();
	}

	void DismissLiveRoomCreateProposal()
	{
		liveDismissedRoomCreateSourceId = liveCreateRoomSourceId;
		liveDismissedRoomCreateDirection = liveCreateRoomDirection;
		ClearLiveRoomCreateProposal();
	}

	void ClearLiveRoomCreateProposal()
	{
		liveCreateRoomTiles = null;
		liveCreateRoomSourceId = null;
		liveCreateRoomDirection = Point2.Zero;
		liveCreateRoomExitPosition = Vector2.Zero;
		liveCreateRoomPopupRequested = false;
	}

	void ClearDismissedLiveRoomCreateProposal()
	{
		liveDismissedRoomCreateSourceId = null;
		liveDismissedRoomCreateDirection = Point2.Zero;
	}

	void ExtendPlatformIntoLiveRoom(Room source, Room target, Point2 direction, Vector2 exitPosition)
	{
		if (direction.X == 0)
			return;

		var sourceTiles = new RectInt(source.TileSpacePosition, source.Size);
		var targetTiles = new RectInt(target.TileSpacePosition, target.Size);
		var sourceX = direction.X > 0 ? source.Size.X - 1 : 0;
		var startX = direction.X > 0 ? 0 : target.Size.X - 1;
		var stepX = Math.Sign(direction.X);
		var exitY = Math.Clamp(WorldToRoomGeometryTile(exitPosition).Y, sourceTiles.Top, sourceTiles.Bottom - 1);
		var platformY = FindPlatformRowAtExit(source, sourceX, exitY - source.TileSpacePosition.Y);
		if (platformY < 0)
			return;

		var worldY = source.TileSpacePosition.Y + platformY;
		if (worldY < targetTiles.Top || worldY >= targetTiles.Bottom)
			return;

		var tile = source.Tiles[sourceX, platformY];
		var localY = worldY - target.TileSpacePosition.Y;
		var length = Math.Min(LiveRoomPlatformExtensionTiles, target.Size.X);
		for (var i = 0; i < length; i++)
		{
			var x = startX + i * stepX;
			if (x < 0 || x >= target.Size.X)
				break;
			target.SetTile(Factory.TileLayer.Foreground, new Point2(x, localY), tile, rebuildGeometry: false);
		}
		target.RebuildGeometry();
	}

	static int FindPlatformRowAtExit(Room room, int x, int centerY)
	{
		for (var radius = 0; radius < room.Size.Y; radius++)
		{
			if (TryPlatformRow(room, x, centerY + radius, out var found))
				return found;
			if (radius > 0 && TryPlatformRow(room, x, centerY - radius, out found))
				return found;
		}
		return -1;
	}

	static bool TryPlatformRow(Room room, int x, int y, out int row)
	{
		row = -1;
		if (y < 0 || y >= room.Size.Y)
			return false;
		if (Factory.Find(room.Tiles[x, y]) is not { BlocksMovement: true })
			return false;
		if (y > 0 && Factory.Find(room.Tiles[x, y - 1]) is { BlocksMovement: true })
			return false;
		row = y;
		return true;
	}
}
#endif
