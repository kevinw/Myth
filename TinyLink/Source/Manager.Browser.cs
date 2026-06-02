#if BROWSER
using Foster.Framework;

namespace TinyLink;

public partial class Manager
{
	private void ResolveSelectedRoom()
	{
		if (selectedRoomId != null && Assets.GetRoom(selectedRoomId) is { } byId)
		{
			selectedRoomId = byId.Id;
			cell = byId.Cell;
			return;
		}

		if (Assets.GetRoom(cell) is { } byCell)
		{
			selectedRoomId = byCell.Id;
			cell = byCell.Cell;
			return;
		}

		var first = Assets.GetRoom(Point2.Zero) ?? Assets.RoomsById.Values.FirstOrDefault();
		selectedRoomId = first?.Id;
		cell = first?.Cell ?? Point2.Zero;
	}
}
#endif
