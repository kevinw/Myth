using Foster.Framework;
namespace Myth;

public class Controls(Input input)
{
	public readonly VirtualAction Rewind = new(input, "Rewind",
		new ActionBindingSet()
			.Add(Keys.Backslash)
	);

	public readonly VirtualStick Move = new(input, "Move",
		new StickBindingSet()
			.AddArrowKeys()
			.AddWasd()
			.AddDPad()
			.Add(Axes.LeftX, 0.25f, Axes.LeftY, 0.50f, 0.25f)
	);

	public readonly VirtualAction Jump = new(input, "Jump",
		new ActionBindingSet()
			.Add(Keys.Space)
			.Add(Buttons.South)
	);

	public readonly VirtualAction Attack = new(input, "Attack",
		new ActionBindingSet()
			.Add(Keys.E)
			.Add(Buttons.West)
	);

	public readonly VirtualAction BlinkStrike = new(input, "BlinkStrike",
		new ActionBindingSet()
			.Add(Keys.Q)
			.Add(Buttons.East)
	);

	public readonly VirtualAction Dash = new(input, "Dash",
		new ActionBindingSet()
	//.Add(Keys.F)
	//.Add(Axes.RightTrigger, 1, 0.25f)
	);

	public readonly VirtualAction Saw = new(input, "Saw",
		new ActionBindingSet()
	//.Add(Keys.Z)
	//.Add(Buttons.North)
	);

	public readonly VirtualAction Zoom = new(input, "Zoom",
		new ActionBindingSet()
			.Add(Keys.X)
			.Add(Buttons.LeftStick)
	);

	public readonly VirtualAction Die = new(input, "Die",
		new ActionBindingSet()
	);
}
