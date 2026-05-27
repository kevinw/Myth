
using Foster.Framework;

namespace TinyLink;

public class Controls(Input input)
{
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

	public readonly VirtualAction Saw = new(input, "Saw",
		new ActionBindingSet()
			.Add(Keys.Z)
	);

	public readonly VirtualAction Die = new(input, "Die",
		new ActionBindingSet()
			.Add(Keys.K)
	);
}
