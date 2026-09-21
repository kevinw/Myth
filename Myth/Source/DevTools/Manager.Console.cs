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
		if (!debugControls.Console.Pressed)
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

	[ConsoleCommand("add", Completer = nameof(CompleteActor))]
	void AddActor(string name)
	{
		#if EDITOR
		if (FindActorPaletteEntry(name) is not { } entry)
			throw new InvalidOperationException(string.IsNullOrWhiteSpace(name) ? "Usage: add <actor>" : $"Actor not found: {name}");

		var live = playing || startingPlay;
		if (live && game?.CurrentRoom == null)
			throw new InvalidOperationException("No room is currently playing");
		var center = live ? GameImGuiToWorld(GameViewRect, game!, GameViewRect.Center) : editorCamera;
		var room = live ? Assets.GetRoom(game!.CurrentRoom!.Id) : GetRoomAtWorld(center) ?? CurrentRoom;
		if (room == null)
			throw new InvalidOperationException("No room at the center of the screen");

		using (CaptureRoomUndo("Add Actor", room))
		{
			AddActorPaletteEntry(room, entry, center.RoundToPoint2());
			var actor = room.Actors[^1];
			CenterRoomActorAt(room, actor, center);
			ClampRoomActorToRoom(room, actor);

			if (live)
			{
				var position = room.WorldBounds.TopLeft + (ActorSpecs.ReadResolvedPosition(actor) ?? Point2.Zero);
				if (ActorSpecs.Create(game!, actor, position) is { } runtime)
				{
					liveSelectedActorId = runtime.ActorId;
					OpenLiveActorInspector(runtime);
				}
				room.Save();
				MarkRoomPreviewDirty(room);
			}
			else
			{
				SelectRoom(room);
				selectedRoomActorIndex = room.Actors.Count - 1;
				SelectEditorActorTool();
				ClearEditorDragState();
				editorSelectionRect = null;
			}
			editorGameDirty = true;
		}

		if (!live)
			SyncEditorGame();
		DisplayMessage($"Added {entry.Name}");
		#else
		throw new InvalidOperationException("The add command requires an editor build");
		#endif
	}

	void CompleteActor(string filter, List<ConsoleCompletion> result)
	{
		#if EDITOR
		foreach (var entry in Factory.ActorPalette)
			if (string.IsNullOrEmpty(filter) || entry.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
				result.Add(new(entry.Name, entry.Name));
		result.Sort(static (a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Label, b.Label));
		#endif
	}

	#if EDITOR
	static Factory.ActorPaletteEntry? FindActorPaletteEntry(string name)
	{
		foreach (var entry in Factory.ActorPalette)
			if (string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase))
				return entry;
		return null;
	}
	#endif

	[ConsoleCommand("go", Completer = nameof(CompleteRoom))]
	void Go(string target)
	{
		if (FindRoomDestination(target) is not { } destination)
			throw new InvalidOperationException($"Room not found: {target}");

		var wasPlaying = playing || startingPlay;
		#if EDITOR
		if (!string.Equals(destination.World, Assets.CurrentWorldId, StringComparison.OrdinalIgnoreCase))
			SwitchWorld(destination.World);
		#endif
		if (Assets.GetRoom(destination.Room) is not { } room)
			throw new InvalidOperationException($"Room not found: {destination.Value}");

			#if EDITOR
		SelectRoom(room);
		#endif
		if (wasPlaying)
			StartGame(immediate: true);
		else
		{
			#if EDITOR
			SyncEditorGame();
			TweenEditorCameraToRoom(room);
			#endif
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
			var worldKey = $"Rooms/{world}";
			if (!Assets.Content.DirectoryExists(worldKey))
				continue;
			foreach (var key in Assets.Content.EnumerateDirectory(worldKey, "*.json", SearchOption.AllDirectories))
				consoleRooms.Add(new(world, Path.GetFileNameWithoutExtension(key)));
		}
		consoleRooms.Sort(static (a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Label, b.Label));
	}

	// Jot a line into Notes/TODO.md without leaving the game: the point is to not break flow when
	// something occurs to you mid-playtest. Newest first, matching how the file is kept by hand.
	[ConsoleCommand("todo")]
	void AddTodo(string text)
	{
		text = text.Trim();
		if (text.Length == 0)
			throw new InvalidOperationException("Usage: todo <text>");
		if (FindSourceFile("Notes", "TODO.md") is not { } path)
			throw new InvalidOperationException("Notes/TODO.md not found (needs the source tree)");

		File.WriteAllText(path, text + Environment.NewLine + File.ReadAllText(path));
		DisplayMessage("New TODO added.");
	}

	// Walks up from the binary to find a file in the source tree; null in a shipped build, where
	// there is none to write to.
	static string? FindSourceFile(params string[] parts)
	{
		for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
		{
			var path = Path.Combine([dir.FullName, .. parts]);
			if (File.Exists(path))
				return path;
		}
		return null;
	}

	[ConsoleCommand("quit")]
	void Quit(string _) => RequestQuit();

	[ConsoleCommand("reload_assets")]
	void ReloadAssetsCommand(string _)
	{
		ReloadAssets(false);
	}

	[ConsoleCommand("fps")]
	void SetFps(string argument)
	{
		if (!int.TryParse(argument, out var fps))
			throw new InvalidOperationException("Usage: fps <1-1000>, or fps 0 to reset");
		if (fps is < 0 or > 1000)
			throw new InvalidOperationException("FPS must be between 1 and 1000, or 0 to reset");

		UpdateMode = fps == 0 ? UpdateMode.UnlockedStep() : UpdateMode.FixedStep(fps, waitForNextUpdate: !OperatingSystem.IsBrowser());
		DisplayMessage(fps == 0 ? "Artificial slowdown disabled" : $"Artificial slowdown: {fps} FPS");
	}

	[ConsoleCommand("head_slide")]
	void ToggleHatSlide(string _)
	{
		PerfToggles.HeadSlide = !PerfToggles.HeadSlide;
		game?.DisplayMessage($"Head slide: {PerfToggles.HeadSlide}");
	}

	[ConsoleCommand("reveal_map")]
	void RevealMap(string _)
	{
		if (game?.CurrentRoom is not { } currentRoom)
			throw new InvalidOperationException("Start the game first");

		var map = new MinimapState();
		var savePointsAdded = 0;
		foreach (var room in Assets.RoomsById.Values)
		{
			map.VisitedRoomIds.Add(room.Id);
			map.SeenTiles[room.Id] = new RectInt(room.Size);

			foreach (var actor in room.Actors)
			{
					var id = ActorSpecs.GetActorId(actor);
					if (ActorSpecs.GetTypeName(actor) == nameof(SavePoint) &&
						!string.IsNullOrWhiteSpace(id) && !game.Save.SavePoints.Contains(id))
				{
					game.Save.SavePoints.Add(id);
					savePointsAdded++;
				}
			}
		}

		game.Minimap.ApplyState(map);
		game.Minimap.ApplyRevealedDoors(game.Save.RevealedDoors);
		game.Minimap.Rebuild(currentRoom);
		game.Save.Map = game.Minimap.CaptureState();
		MarkSaveDirty();
		game.DisplayMessage($"Map revealed; {savePointsAdded} save points activated");
	}

	// Cut the nearest rope at the player's height (the cutscene beat, testable by hand).
	[ConsoleCommand("rope_break")]
	void RopeBreak(string _)
	{
		if (game?.GetFirst<Player>() is not { } player)
			return;

		Rope? best = null;
		var bestDistSq = float.MaxValue;
		foreach (var actor in game.Actors)
		{
			if (actor is not Rope rope || rope.Broken)
				continue;
			var distSq = (rope.Position - player.Position).LengthSquared();
			if (distSq < bestDistSq)
			{
				bestDistSq = distSq;
				best = rope;
			}
		}

		if (best == null)
		{
			game.DisplayMessage("No unbroken rope in room");
			return;
		}
		best.Break(player.Position);
		game.DisplayMessage("Rope cut");
	}

	// Clears the screen so death sequences can be watched without fighting through the room.
	[ConsoleCommand("kill_all")]
	void KillAll(string _)
	{
		if (game?.GetFirst<Player>() is not { } player)
			throw new InvalidOperationException("Start the game first");

		var view = game.WorldView;
		List<Actor> targets = [];
		foreach (var actor in game.Actors)
			if (actor.Mask.Has(Actor.Masks.Enemy) && view.Overlaps(actor.GetVisualBounds()))
				targets.Add(actor);

		foreach (var actor in targets)
		{
			// Puppets go through their own Die() so death sequences play out; the rest just vanish.
			if (actor is RigPuppet puppet)
			{
				puppet.Health = 1;
				if (player.Hit(actor, new(actor.Hitbox.Rect + actor.Position)))
					continue;
			}
			game.Destroy(actor);
		}
		game.DisplayMessage($"Killed {targets.Count} on screen");
	}
}
