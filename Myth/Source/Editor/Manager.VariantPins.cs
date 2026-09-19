#if EDITOR
using System.Numerics;
using Foster.Framework;
using ImGuiNET;

namespace Myth;

// V over a dual-grid corner hand-picks the fill variant it shows instead of the positional hash:
// tap to cycle (Shift cycles back), hold for a picker and release over a choice.
public partial class Manager
{
	static float VariantPickerHoldTime => 0.28f;
	static float VariantPickerThumbSize => 40f;
	static Color VariantPinnedOnlyColor => new(1.0f, 0.45f, 0.85f, 1.0f);

	readonly record struct VariantPinPress(Point2 Vertex, char TileType, Tileset Tileset, Factory.TileLayer Layer, float Held)
	{
		public bool PickerOpen => Held >= VariantPickerHoldTime;
	}

	VariantPinPress? variantPinPress;
	int hoveredVariantOption = -1;

	// Options run random, then the pinned-only groups, then the random pool's groups.
	static int? VariantPinOption(Tileset tileset, int option)
	{
		var pinnedOnly = tileset.VariantGroups - tileset.RandomGroups;
		return option == 0 ? null : option <= pinnedOnly ? -option : option - pinnedOnly - 1;
	}

	static int VariantPinOptionIndex(Tileset tileset, int? pin)
	{
		var index = pin switch { null => 0, < 0 => -pin.Value, _ => tileset.VariantGroups - tileset.RandomGroups + 1 + pin.Value };
		return index <= tileset.VariantGroups ? index : 0;
	}

	static string VariantPinLabel(int? pin)
		=> pin switch { null => "Variant: random", < 0 => $"Variant: pinned-only {-pin.Value}", _ => $"Variant: {pin.Value + 1}" };

	// True while V is down, so tile painting and selection stand aside.
	bool UpdateVariantPins(Room room, Vector2 localMouse, bool canStart)
	{
		if (variantPinPress is not { } press)
		{
			if (!canStart || Input.Keyboard.CtrlOrCommand || !Input.Keyboard.Pressed(Keys.V))
				return false;

			var vertex = (localMouse / Game.TileSize).RoundToPoint2();
			if (FindVariantPinMaterial(room, localMouse, vertex) is not { } material)
			{
				DisplayMessage("No tile-kit terrain at this corner");
				return true;
			}
			variantPinPress = new(vertex, material.TileType, material.Tileset, activeTileLayer, 0);
			return true;
		}

		if (Input.Keyboard.Down(Keys.V))
		{
			variantPinPress = press with { Held = press.Held + Time.Delta };
			return true;
		}

		variantPinPress = null;
		var fillTiles = press.Tileset.FillTiles;
		var count = press.Tileset.VariantGroups + 1;
		if (press.PickerOpen && hoveredVariantOption < 0)
			return true;
		var option = press.PickerOpen
			? hoveredVariantOption
			: (VariantPinOptionIndex(press.Tileset, room.GetVariantPin(press.Layer, press.Vertex, fillTiles)) + (Input.Keyboard.Shift ? count - 1 : 1)) % count;
		var pin = VariantPinOption(press.Tileset, option);

		using (CaptureRoomUndo("Pin Tile Variant", room))
		{
			room.SetVariantPin(press.Layer, press.Vertex, fillTiles, pin);
			editorGameDirty = true;
		}
		DisplayMessage(VariantPinLabel(pin), print: false);
		return true;
	}

	(char TileType, Tileset Tileset)? FindVariantPinMaterial(Room room, Vector2 localMouse, Point2 vertex)
	{
		// Where materials meet at a corner, the one under the cursor wins.
		var under = (localMouse / Game.TileSize).FloorToPoint2();
		foreach (var cell in (ReadOnlySpan<Point2>)[under, vertex - Point2.One, vertex - Point2.UnitY, vertex - Point2.UnitX, vertex])
		{
			if (cell.X < 0 || cell.Y < 0 || cell.X >= room.Size.X || cell.Y >= room.Size.Y)
				continue;
			var tileType = activeTileLayer == Factory.TileLayer.Foreground ? room.Tiles[cell.X, cell.Y] : room.BackgroundTiles[cell.X, cell.Y];
			if (Factory.Find(tileType) is { DualTilesetName: { } name } && Assets.GetTileset(name) is { } tileset)
				return (tileType, tileset);
		}
		return null;
	}

	void DrawVariantPins(Room room, Vector2 roomTopLeft, Vector2 worldMouse, float lineWeight)
	{
		foreach (var vertex in room.VariantPinVertices(activeTileLayer))
			batcher.Circle(roomTopLeft + (Vector2)(vertex * Game.TileSize), lineWeight * 3.5f, 4, VariantPinnedOnlyColor * 0.8f);

		hoveredVariantOption = -1;
		if (variantPinPress is not { PickerOpen: true } press)
			return;

		var tileset = press.Tileset;
		var fillTiles = tileset.FillTiles;
		var count = tileset.VariantGroups + 1;
		var pinnedOnly = tileset.VariantGroups - tileset.RandomGroups;
		var thumb = VariantPickerThumbSize * lineWeight;
		var gap = thumb * 0.15f;
		var vertexWorld = roomTopLeft + (Vector2)(press.Vertex * Game.TileSize);
		var origin = vertexWorld - new Vector2((count * (thumb + gap) - gap) / 2, thumb + gap * 4);
		var anchor = room.VariantPinVertex(press.Vertex, fillTiles);
		var current = VariantPinOptionIndex(tileset, room.GetVariantPin(press.Layer, press.Vertex, fillTiles));
		var scale = thumb / (fillTiles * Game.TileSize);

		batcher.Circle(vertexWorld, lineWeight * 5, 4, Color.White);
		for (var option = 0; option < count; option++)
		{
			var rect = new Rect(origin + new Vector2(option * (thumb + gap), 0), new Vector2(thumb));
			var hovered = rect.Inflate(gap / 2).Contains(worldMouse);
			if (hovered)
				hoveredVariantOption = option;

			batcher.Rect(rect.Inflate(gap / 2), Color.Black * 0.85f);
			var pin = VariantPinOption(tileset, option);
			batcher.PushMatrix(rect.TopLeft, Vector2.One * scale, 0);
			for (var y = 0; y < fillTiles; y++)
				for (var x = 0; x < fillTiles; x++)
				{
					var vertex = anchor + new Point2(x, y);
					var materialMask = room.GetDualMaterialMask(press.TileType, press.Layer, vertex.X, vertex.Y);
					var joinMask = room.GetDualTerrainJoinMask(press.Layer, vertex.X, vertex.Y);
					if (materialMask == 0 || joinMask == 0)
						continue;
					var art = DualGrid.LookupArt(tileset, DualGrid.MaskToTile[joinMask], room.TileSpacePosition + vertex, press.TileType, pin).Tile;
					if (art.Texture != null)
						DualGrid.DrawTile(batcher, art, new Vector2(x, y) * Game.TileSize, materialMask, Color.White);
				}
			batcher.PopMatrix();

			var outline = hovered ? Color.Yellow : option == current ? Color.White : option <= pinnedOnly && option > 0 ? VariantPinnedOnlyColor : Color.White * 0.25f;
			if (option == 0)
				batcher.RectDashed(rect, lineWeight * 1.5f, outline, thumb / 6, 0);
			else
				batcher.RectLine(rect, lineWeight * (hovered || option == current ? 2.5f : 1.5f), outline);
			if (hovered)
				ImGui.SetTooltip(VariantPinLabel(pin));
		}
	}
}
#endif
