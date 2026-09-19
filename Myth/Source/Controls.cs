#if EDITOR
using System.Reflection;
#endif
using Foster.Framework;
namespace Myth;

public class Controls(Input input)
{
	// Slot defaults, and where the virtual inputs are built. ResolveControllerSlots reassigns
	// them at runtime; these only decide what the first frame looks like.
	const int InitialPlayerControllerIndex = 0;
	const int InitialBudControllerIndex = 1;

	/// Which SDL controller drives the player, and which drives Bud as a second player.
	public int PlayerControllerIndex { get; private set; } = InitialPlayerControllerIndex;
	public int BudControllerIndex { get; private set; } = InitialBudControllerIndex;

	static float BudControllerIdleTimeout => 15f;
	internal static float MoveStickDeadzone => 0.25f;
	static float MoveStickFullSpeed => 0.75f;

	public readonly VirtualAction Rewind = new(input, "Rewind",
		new ActionBindingSet()
			.Add(Keys.Backslash)
	);

	public readonly VirtualStick Move = new(input, "Move",
		new StickBindingSet()
			.AddArrowKeys()
			.AddWasd()
			.AddDPad()
			.Add(Axes.LeftX, MoveStickDeadzone, Axes.LeftY, 0.50f, MoveStickDeadzone)
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
		.Add(Keys.LeftShift)
		.Add(Keys.F)
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
			.Add(Axes.LeftX, MoveStickDeadzone, Axes.LeftY, 0.50f, MoveStickDeadzone),
		InitialBudControllerIndex
	);

	readonly VirtualAction budJump = new(input, "BudJump",
		new ActionBindingSet()
			.Add(Buttons.South),
		InitialBudControllerIndex
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
			MoveX: ShapeMoveX(budMove.Value.X),
			MoveY: budMove.IntValue.Y,
			JumpPressed: budJump.Pressed,
			JumpDown: budJump.Down,
			DashPressed: false
		);
		return true;
	}

	// The binding has already remapped the physical deadzone to zero. Saturating its remaining
	// range early makes 75% physical stick travel reliably produce full-speed movement.
	internal static float ShapeMoveX(float value)
		=> Math.Clamp(value / ((MoveStickFullSpeed - MoveStickDeadzone) / (1 - MoveStickDeadzone)), -1, 1);

	// Everything driven by player 1. Built once, on demand: instance field initializers cannot
	// reference each other, and reflection is not an option in the AOT'd ship build.
	VirtualInput[]? playerInputs;
	VirtualInput[] PlayerInputs => playerInputs ??=
		[Rewind, Move, Look, Jump, Attack, BlinkStrike, Shield, Saw, Dash, Zoom, Map, Die];

	int connectedControllers = -1;

	/// Re-resolves the player slots when the set of connected controllers changes. Called once a
	/// frame before anything reads input.
	public void UpdateControllerAssignment()
	{
		var connected = 0;
		for (var i = 0; i < input.Controllers.Length && i < 32; i++)
			if (input.Controllers[i].Connected)
				connected |= 1 << i;
		if (connected == connectedControllers)
			return;

		connectedControllers = connected;
		ResolveControllerSlots();
	}

	// External pads take the player slots in the order they arrived, and the Deck's own controls
	// take whichever slot is left over: nothing paired makes them player 1, one pad makes them
	// player 2, two pads leave them out. Everywhere else this is the old order untouched, since
	// only a Deck has controls that are not a pad someone picked up.
	void ResolveControllerSlots()
	{
		Span<int> order = stackalloc int[input.Controllers.Length];
		var count = 0;
		var deckControls = -1;
		for (var i = 0; i < input.Controllers.Length; i++)
		{
			if (!input.Controllers[i].Connected)
				continue;
			if (deckControls < 0 && IsDeckControls(input.Controllers[i]))
				deckControls = i;
			else
				order[count++] = i;
		}
		if (deckControls >= 0)
			order[count++] = deckControls;

		PlayerControllerIndex = count > 0 ? order[0] : InitialPlayerControllerIndex;
		BudControllerIndex = count > 1 ? order[1] : InitialBudControllerIndex;

		foreach (var it in PlayerInputs)
			it.ControllerIndex = PlayerControllerIndex;
		budMove.ControllerIndex = BudControllerIndex;
		budJump.ControllerIndex = BudControllerIndex;

		Console.WriteLine($"Controllers: player 1 = {ControllerLabel(PlayerControllerIndex)}, " +
			$"player 2 = {ControllerLabel(BudControllerIndex)}");
	}

	// Steam Input republishes the built-in controls AND every paired pad as identical virtual
	// "Microsoft X-Box 360 pad N" devices (Valve 28de:11ff), so under Steam the only thing that
	// marks the built-in is being enumerated first -- it exists before anything can pair. Without
	// Steam Input in the way it shows up as the real Valve device and is matched outright.
	internal static bool IsDeckControls(ControllerState controller)
		=> controller.Vendor == 0x28de && controller.Product == 0x1205 ||
			controller.Name.Contains("Steam Deck", StringComparison.OrdinalIgnoreCase) ||
			(Manager.IsSteamDeck && controller.Index == 0);

	string ControllerLabel(int index)
	{
		var controller = input.Controllers[index];
		return controller.Connected
			? $"#{index} {controller.Name} ({controller.Vendor:x4}:{controller.Product:x4})"
			: $"#{index} (none)";
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
