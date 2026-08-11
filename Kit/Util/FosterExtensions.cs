using Foster.Framework;
using System.Numerics;
namespace Myth;

public static class FosterExtensions {
	extension(RectInt rect)
	{
		public Vector2 Center => new(rect.X + rect.Width * 0.5f, rect.Y + rect.Height * 0.5f);
	}

	extension(Input input) {
		public bool AnyControllerButtonPressed(Buttons buttons) {
			foreach (var controller in input.Controllers)
				if (controller.Connected && controller.Pressed(buttons))
					return true;
			return false;
		}
	}
}
