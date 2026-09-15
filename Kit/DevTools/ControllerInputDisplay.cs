using System.Numerics;
using Foster.Framework;

namespace Kit;

public static class ControllerInputDisplay
{
	static Vector2 Size => new(180, 78);
	static Color Panel => new Color(0x111820) * 0.93f;
	static Color Idle => new(0x526170);
	static Color Active => new(0x80ffd0);

	public static Rect Bounds(Rect viewport, Rect framebuffer)
	{
		var scale = MathF.Min(Math.Clamp(viewport.Height / 540f, 1, 2),
			MathF.Min(viewport.Width / (Size.X + 12), viewport.Height / (Size.Y + 12)));
		var size = Size * scale;
		var margin = 6 * scale;
		var bottom = MathF.Min(viewport.Bottom, framebuffer.Bottom);
		var right = MathF.Min(viewport.Right, framebuffer.Right);
		var position = viewport.Bottom - bottom >= size.Y + margin * 2
			? new Vector2(viewport.Center.X - size.X * 0.5f, bottom + margin)
			: new Vector2(right, bottom) - size - new Vector2(margin);
		return new(position, size);
	}

	public static void Draw(Batcher batcher, ControllerState controller, Rect bounds, SpriteFont? font = null)
	{
		if (bounds.Width <= 0 || bounds.Height <= 0)
			return;
		batcher.PushMatrix(bounds.TopLeft, bounds.Size / Size, 0);
		batcher.Rect(new Rect(Vector2.Zero, Size), Panel);
		var connected = controller.Connected && controller.IsGamepad;
		if (!connected && font != null)
		{
			batcher.Text(font, controller.Connected ? "Not a gamepad" : "No controller", new Vector2(12, 35), 8, Color.White);
			batcher.PopMatrix();
			return;
		}
		Trigger(new(14, 6), Axes.LeftTrigger);
		Trigger(new(140, 6), Axes.RightTrigger);
		ButtonRect(new(14, 16, 26, 5), Buttons.LeftShoulder);
		ButtonRect(new(140, 16, 26, 5), Buttons.RightShoulder);
		Stick(new(30, 39), Axes.LeftX, Axes.LeftY, Buttons.LeftStick);
		Stick(new(113, 58), Axes.RightX, Axes.RightY, Buttons.RightStick);
		ButtonRect(new(57, 44, 7, 9), Buttons.Up);
		ButtonRect(new(57, 61, 7, 9), Buttons.Down);
		ButtonRect(new(48, 53, 9, 8), Buttons.Left);
		ButtonRect(new(64, 53, 9, 8), Buttons.Right);
		ButtonCircle(new(151, 27), 5, Buttons.North);
		ButtonCircle(new(163, 39), 5, Buttons.East);
		ButtonCircle(new(151, 51), 5, Buttons.South);
		ButtonCircle(new(139, 39), 5, Buttons.West);
		ButtonRect(new(72, 27, 8, 4), Buttons.Back);
		ButtonRect(new(100, 27, 8, 4), Buttons.Start);
		ButtonCircle(new(90, 29), 4, Buttons.Guide);
		batcher.PopMatrix();

		Color ButtonColor(Buttons button) => connected && controller.Down(button) ? Active : Idle;
		void ButtonRect(Rect rect, Buttons button) => batcher.Rect(rect, ButtonColor(button));
		void ButtonCircle(Vector2 center, float radius, Buttons button) => batcher.Circle(center, radius, 16, ButtonColor(button));
		void Trigger(Vector2 position, Axes axis)
		{
			batcher.Rect(new Rect(position, new Vector2(26, 6)), Idle);
			var value = connected ? Math.Clamp(controller.Axis(axis), 0, 1) : 0;
			batcher.Rect(new Rect(position, new Vector2(26 * value, 6)), Active);
		}
		void Stick(Vector2 center, Axes x, Axes y, Buttons button)
		{
			batcher.CircleLine(center, 11, 1, 24, Idle);
			var offset = connected ? new Vector2(controller.Axis(x), controller.Axis(y)) * 8 : Vector2.Zero;
			batcher.Line(center, center + offset, 1, Active);
			batcher.Circle(center + offset, 4, 16, ButtonColor(button));
		}
	}
}
