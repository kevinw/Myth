#if EDITOR
using System.Reflection;
using Foster.Framework;
using ImGuiNET;

namespace Myth;

[AttributeUsage(AttributeTargets.Method)]
sealed class ConsoleCommandAttribute(string name) : Attribute
{
	public string Name { get; } = name;
	public string? Completer { get; init; }
}

public partial class Manager
{
	readonly record struct ConsoleCompletion(string Value, string Label);
	readonly record struct RoomDestination(string World, string Room)
	{
		public string Value => $"{World}/{Room}";
		public string Label => $"{Room} ({World})";
	}

	sealed record ConsoleCommand(MethodInfo Executor, MethodInfo? Completer);

	readonly Dictionary<string, ConsoleCommand> consoleCommands = BuildConsoleCommands();
	readonly List<ConsoleCompletion> consoleCompletions = [];
	readonly List<RoomDestination> consoleRooms = [];
	bool commandConsoleOpen;
	bool focusCommandConsole;
	string commandConsoleInput = "";
	string commandConsoleError = "";
	int commandConsoleSelection = -1;
	string commandConsoleCompletionInput = "";

	static Dictionary<string, ConsoleCommand> BuildConsoleCommands()
	{
		var commands = new Dictionary<string, ConsoleCommand>(StringComparer.OrdinalIgnoreCase);
		foreach (var method in typeof(Manager).GetMethods(BindingFlags.Instance | BindingFlags.NonPublic))
			if (method.GetCustomAttribute<ConsoleCommandAttribute>() is { } attribute)
			{
				var completer = attribute.Completer == null
					? null
					: typeof(Manager).GetMethod(attribute.Completer, BindingFlags.Instance | BindingFlags.NonPublic);
				commands.Add(attribute.Name, new(method, completer));
			}
		return commands;
	}

	void UpdateCommandConsoleToggle()
	{
		if (!Input.Keyboard.Pressed(Keys.F1))
			return;
		commandConsoleOpen = !commandConsoleOpen;
		focusCommandConsole = commandConsoleOpen;
		commandConsoleError = "";
		if (commandConsoleOpen)
			RefreshConsoleRooms();
	}

	void DrawCommandConsole()
	{
		if (!commandConsoleOpen)
			return;

		RefreshConsoleCompletions();
		var viewport = ImGui.GetMainViewport();
		var height = consoleCompletions.Count > 0 ? Math.Min(300, 58 + consoleCompletions.Count * ImGui.GetTextLineHeightWithSpacing()) : 58;
		if (!string.IsNullOrEmpty(commandConsoleError))
			height += ImGui.GetTextLineHeightWithSpacing();
		ImGui.SetNextWindowPos(viewport.WorkPos + new System.Numerics.Vector2(viewport.WorkSize.X * 0.5f, viewport.WorkSize.Y - 16), ImGuiCond.Always, new(0.5f, 1));
		ImGui.SetNextWindowSize(new(620, height), ImGuiCond.Always);
		ImGui.Begin("Command Console", ref commandConsoleOpen, ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse);

		if (focusCommandConsole)
		{
			ImGui.SetKeyboardFocusHere();
			focusCommandConsole = false;
		}

		ImGui.SetNextItemWidth(-1);
		var submit = ImGui.InputText("##Command", ref commandConsoleInput, 512, ImGuiInputTextFlags.EnterReturnsTrue);
		if (ImGui.IsKeyPressed(ImGuiKey.Escape))
			commandConsoleOpen = false;
		else
		{
			if (consoleCompletions.Count > 0 && ImGui.IsKeyPressed(ImGuiKey.DownArrow))
				commandConsoleSelection = (commandConsoleSelection + 1) % consoleCompletions.Count;
			if (consoleCompletions.Count > 0 && ImGui.IsKeyPressed(ImGuiKey.UpArrow))
				commandConsoleSelection = commandConsoleSelection < 0 ? consoleCompletions.Count - 1 : (commandConsoleSelection - 1 + consoleCompletions.Count) % consoleCompletions.Count;
			if (submit)
			{
				if (commandConsoleSelection >= 0)
					commandConsoleInput = CommandName(commandConsoleInput) + " " + consoleCompletions[commandConsoleSelection].Value;
				ExecuteConsoleCommand();
			}
		}

		if (!string.IsNullOrEmpty(commandConsoleError))
			ImGui.TextColored(new(1, 0.35f, 0.35f, 1), commandConsoleError);

		for (var i = 0; i < consoleCompletions.Count; i++)
		{
			var selected = i == commandConsoleSelection;
			if (ImGui.Selectable(consoleCompletions[i].Label, selected))
			{
				commandConsoleInput = CommandName(commandConsoleInput) + " " + consoleCompletions[i].Value;
				ExecuteConsoleCommand();
				break;
			}
		}

		ImGui.End();
	}

	void RefreshConsoleCompletions()
	{
		consoleCompletions.Clear();
		if (commandConsoleCompletionInput != commandConsoleInput)
		{
			commandConsoleCompletionInput = commandConsoleInput;
			commandConsoleSelection = -1;
		}
		var space = commandConsoleInput.IndexOf(' ');
		if (space < 0 || !consoleCommands.TryGetValue(commandConsoleInput[..space], out var command) || command.Completer == null)
			return;

		var argument = commandConsoleInput[(space + 1)..].Trim();
		command.Completer.Invoke(this, [argument, consoleCompletions]);
		if (commandConsoleSelection >= consoleCompletions.Count)
			commandConsoleSelection = -1;
	}

	void ExecuteConsoleCommand()
	{
		var input = commandConsoleInput.Trim();
		var space = input.IndexOf(' ');
		var name = space < 0 ? input : input[..space];
		var argument = space < 0 ? "" : input[(space + 1)..].Trim();
		if (!consoleCommands.TryGetValue(name, out var command))
		{
			commandConsoleError = $"Unknown command: {name}";
			focusCommandConsole = true;
			return;
		}

		try
		{
			command.Executor.Invoke(this, [argument]);
			commandConsoleError = "";
			commandConsoleInput = "";
			commandConsoleOpen = false;
		}
		catch (TargetInvocationException e)
		{
			commandConsoleError = e.InnerException?.Message ?? e.Message;
			focusCommandConsole = true;
		}
	}

	static string CommandName(string input)
	{
		var space = input.IndexOf(' ');
		return space < 0 ? input : input[..space];
	}

	[ConsoleCommand("go", Completer = nameof(CompleteRoom))]
	void Go(string target)
	{
		if (FindRoomDestination(target) is not { } destination)
			throw new InvalidOperationException($"Room not found: {target}");

		var wasPlaying = playing || startingPlay;
		if (!string.Equals(destination.World, Assets.CurrentWorldId, StringComparison.OrdinalIgnoreCase))
			SwitchWorld(destination.World);
		if (Assets.GetRoom(destination.Room) is not { } room)
			throw new InvalidOperationException($"Room not found: {destination.Value}");

		SelectRoom(room);
		if (wasPlaying)
			StartGame(immediate: true);
		else
		{
			SyncEditorGame();
			TweenEditorCameraToRoom(room);
		}
	}

	void CompleteRoom(string filter, List<ConsoleCompletion> result)
	{
		foreach (var room in consoleRooms)
			if (string.IsNullOrEmpty(filter) || room.Room.Contains(filter, StringComparison.OrdinalIgnoreCase) || room.World.Contains(filter, StringComparison.OrdinalIgnoreCase))
				result.Add(new(room.Value, room.Label));
	}

	RoomDestination? FindRoomDestination(string target)
	{
		foreach (var room in consoleRooms)
			if (string.Equals(room.Value, target, StringComparison.OrdinalIgnoreCase) || string.Equals(room.Room, target, StringComparison.OrdinalIgnoreCase))
				return room;
		return null;
	}

	void RefreshConsoleRooms()
	{
		consoleRooms.Clear();
		foreach (var world in Assets.GetWorldIds())
		{
			var path = Path.Join(Assets.RoomsPath, world);
			if (!Directory.Exists(path))
				continue;
			foreach (var file in Directory.EnumerateFiles(path, "*.json"))
				consoleRooms.Add(new(world, Path.GetFileNameWithoutExtension(file)));
		}
		consoleRooms.Sort(static (a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Label, b.Label));
	}

	[ConsoleCommand("quit")]
	void Quit(string _) => Exit();
}
#endif
