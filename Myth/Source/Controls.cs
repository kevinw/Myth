#if EDITOR
using System.Reflection;
#endif
using Foster.Framework;
namespace Myth;

public class Controls(Input input)
{
	const int BudControllerIndex = 1;
	static float BudControllerIdleTimeout => 15f;

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

	public readonly VirtualStick Look = new(input, "Look",
		new StickBindingSet()
			.Add(Axes.RightX, 0.25f, Axes.RightY, 0.25f, 0.25f)
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
		/* disabled intentionally. don't love this ability yet.
			.Add(Keys.Q)
			.Add(Buttons.East)
			*/
	);

	public readonly VirtualAction Shield = new(input, "Shield",
		new ActionBindingSet()
		/* disabled intentionally. don't love this ability yet.
			.Add(Keys.C)
			.Add(Axes.LeftTrigger, 1, 0.25f)
			*/
	);

	public readonly VirtualAction Saw = new(input, "Saw",
		new ActionBindingSet()
		/* disabled intentionally. don't love this ability yet.
			.Add(Keys.Z)
			.Add(Buttons.North)
			*/
	);

	public readonly VirtualAction Dash = new(input, "Dash",
		new ActionBindingSet()
		.Add(Keys.F)
		.Add(Keys.LeftShift)
		.Add(Axes.RightTrigger, 1, 0.25f)
	);

	public readonly VirtualAction Zoom = new(input, "Zoom"
		/*
		// not sure this is useful, disabling it.
		new ActionBindingSet()
			.Add(Keys.X)
			.Add(Buttons.LeftStick)
			*/
	);

	public readonly VirtualAction Map = new(input, "Map",
		new ActionBindingSet()
			.Add(Keys.Escape)
			.Add(Buttons.Start)
	);

	public readonly VirtualAction Die = new(input, "Die",
		new ActionBindingSet()
	);

	readonly VirtualStick budMove = new(input, "BudMove",
		new StickBindingSet()
			.AddDPad()
			.Add(Axes.LeftX, 0.25f, Axes.LeftY, 0.50f, 0.25f),
		BudControllerIndex
	);

	readonly VirtualAction budJump = new(input, "BudJump",
		new ActionBindingSet()
			.Add(Buttons.South),
		BudControllerIndex
	);

	internal bool TryGetBudControllerInput(TimeSpan now, out PawnInput value)
	{
		var controller = input.Controllers[BudControllerIndex];
		if (!Bud.SecondControllerControlEnabled || !controller.Connected || controller.InputTimestamp == TimeSpan.Zero ||
			(now - controller.InputTimestamp).TotalSeconds >= BudControllerIdleTimeout)
		{
			value = default;
			return false;
		}

		value = new(
			MoveX: budMove.IntValue.X,
			MoveY: budMove.IntValue.Y,
			JumpPressed: budJump.Pressed,
			JumpDown: budJump.Down,
			DashPressed: false
		);
		return true;
	}

#if EDITOR
	static readonly FieldInfo[] ClearableFields = GetClearableFields();

	public void Clear()
	{
		foreach (var field in ClearableFields)
			switch (field.GetValue(this))
			{
				case VirtualAction action:
					action.Clear();
					break;
				case VirtualStick stick:
					stick.Clear();
					break;
				case VirtualAxis axis:
					axis.Clear();
					break;
			}
	}

	static FieldInfo[] GetClearableFields()
	{
		var fields = typeof(Controls).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		var clearable = new List<FieldInfo>();
		foreach (var field in fields)
			if (field.FieldType == typeof(VirtualAction) || field.FieldType == typeof(VirtualStick) || field.FieldType == typeof(VirtualAxis))
				clearable.Add(field);
		return clearable.ToArray();
	}
#endif
}
