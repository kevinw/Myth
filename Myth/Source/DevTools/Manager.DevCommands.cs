#if !BROWSER
using Foster.Framework;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Numerics;
using System.Text;

namespace Myth;

// Verbs the dev host answers over its named pipe, so tooling can inspect and poke the session you
// are already playing instead of launching a fresh one:
//
//   dotnet bin/Debug/net10.0/Myth.dll --dev-command state
//   dotnet bin/Debug/net10.0/Myth.dll --dev-command "eval go Myth/Cellar"
//
// These run on the game thread (drained by DevHost.Pump in Update), so reading game state is safe.
// For arbitrary C# against the live session, see DevScript (`cs`, code on stdin).
public partial class Manager
{
	internal string RunDevCommand(string line)
	{
		var space = line.IndexOf(' ');
		var verb = space < 0 ? line : line[..space];
		var argument = space < 0 ? "" : line[(space + 1)..].Trim();
		return verb switch
		{
			"state" => DevState(),
			"sample" => DevSample(argument),
			"input" => DevInput(argument),
			"play" => DevPlay(argument),
			"stop" => DevStop(),
			"wake" => DevWake(),
			"warp" => DevWarp(argument),
			"set" => DevSet(argument),
			"view" => DevView(argument),
			"shot" => DevShot(argument),
			"eval" => TryRunConsoleCommand(argument, out var error) ? $"ok: {argument}" : $"error: {error}",
			"commands" => string.Join(' ', consoleCommands.Keys.Order()),
			_ => $"error: unknown dev command '{verb}' (state, sample, input, play, stop, wake, warp, set, view, shot, eval, commands)",
		};
	}

	// Enters play mode, optionally in a named room -- an editor build otherwise boots to the editor,
	// where there is no player to inspect. NOTE: this saves the current room to disk (Manager.StartGame).
	string DevPlay(string room)
	{
		if (room.Length > 0 && !TryRunConsoleCommand($"go {room}", out var error))
			return $"error: {error}";
		if (!playing)
			StartGame(immediate: true);
		return playing || startingPlay ? $"playing{(room.Length > 0 ? $" {room}" : "")}" : "error: play did not start.";
	}

	// Shuts the whole dev host down -- the counterpart to quitting the game, which only parks it.
	string DevStop()
	{
		UserQuitRequested = true;
		Exit();
		return "stopping the dev host";
	}

	// What a `run` request means once the host already owns an App: unpark it (see Manager.QuitNow).
	string DevWake()
	{
		if (!DevHostIdle)
			return "Myth is already running.";
		DevHostWake();
		return "Myth resumed.";
	}

	// `warp <localX> <localY>`: drop the player at a room-local pixel, the same coordinates `state`
	// reports and room files store. Sets position directly -- no collision resolve, so it can place
	// the player inside geometry.
	string DevWarp(string argument)
	{
		var space = argument.IndexOf(' ');
		if (space < 0 || game?.GetFirst<Player>() is not { } player)
			return "error: warp takes <localX> <localY>, and needs a player in play mode";
		if (!int.TryParse(argument[..space], out var x) || !int.TryParse(argument[(space + 1)..].Trim(), out var y))
			return "error: warp takes two integers";

		player.Position = new Point2(x, y) + (game.CurrentRoom?.WorldBounds.TopLeft ?? Point2.Zero);
		player.Velocity = Vector2.Zero;
		return $"warped to {x},{y}";
	}

	// What the player is standing on, for `state` and `sample`: the actor's type and id, or "room"
	// for plain geometry.
	static string GroundLabel(Player player) => player.GroundActor() is { } ground
		? $"{ground.GetType().Name}:{ground.ActorId}"
		: player.IsGrounded ? "room" : "";

	// `set <actorId> <member> <value>`: poke a public field or property on a live actor, so a test can
	// drive authored content without an editor round-trip. Ids are what `state` reports as `ground`.
	[UnconditionalSuppressMessage("Trimming", "IL2075", Justification =
		"Actors are registered via ActorSpecs.RegisterActor, which preserves public fields and properties.")]
	// set <actorId|player> <member[.nested...]> <value>, e.g. `set player Rig.PixelsPerMeter 40`.
	string DevSet(string argument)
	{
		var parts = argument.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
		if (parts.Length < 3)
			return "error: set takes <actorId|player> <member[.nested]> <value>";
		if ((parts[0] == "player" ? game?.GetFirst<Player>() : game?.GetActorById(parts[0])) is not Actor actor)
			return $"error: no actor '{parts[0]}' in the current room";

		object target = actor;
		var path = parts[1].Split('.');
		for (var i = 0; ; i++)
		{
			var type = target.GetType();
			object? member = type.GetField(path[i], BindingFlags.Public | BindingFlags.Instance)
				?? (object?)type.GetProperty(path[i], BindingFlags.Public | BindingFlags.Instance);
			if (member == null)
				return $"error: {type.Name} has no public member '{path[i]}'";
			if (i < path.Length - 1)
			{
				target = (member is FieldInfo nested ? nested.GetValue(target) : ((PropertyInfo)member).GetValue(target))
					?? throw new InvalidOperationException($"{path[i]} is null");
				continue;
			}

			var memberType = member is FieldInfo field ? field.FieldType : ((PropertyInfo)member).PropertyType;
			try
			{
				// Content references and other wrappers build from their string constructor.
				var value = memberType.IsEnum ? Enum.Parse(memberType, parts[2], ignoreCase: true)
					: memberType.GetConstructor([typeof(string)]) is { } fromString ? fromString.Invoke([parts[2]])
					: Convert.ChangeType(parts[2], memberType, CultureInfo.InvariantCulture);
				if (member is FieldInfo assign)
					assign.SetValue(target, value);
				else
					((PropertyInfo)member).SetValue(target, value);
			}
			catch (Exception e)
			{
				return $"error: {e.Message}";
			}
			return $"{actor.GetType().Name}.{parts[1]} = {parts[2]}";
		}
	}

	// How much the surface under the player faces up: 1 flat, 0 a vertical wall. Sizes the rider
	// follow budget (see Pawn.FollowDeformingGround), so it is worth being able to read directly.
	static float GroundFacing(Player player)
		=> player.GroundActor() is { } ground ? -ground.SurfaceNormalAt(player.Position).Y : 1;

	// `view <DebugView>`: the debug overlay a screenshot should show, e.g. `view CollisionRects`.
	string DevView(string argument)
	{
		if (game is not { } g)
			return "error: no game in the current session.";
		if (!Enum.TryParse<Game.DebugView>(argument, ignoreCase: true, out var view))
			return $"error: unknown view '{argument}' ({string.Join(", ", Enum.GetNames<Game.DebugView>())})";

		g.debugView = view;
		return $"view: {view}";
	}

	// Pins the player's input so a measurement isn't at the mercy of a controller resting off-center.
	// `off` hands control back. Rides the same ScriptedInput hook cutscenes and replays use.
	string DevInput(string argument)
	{
		if (game?.GetFirst<Player>() is not { } player)
			return "error: no player in the current session.";

		player.ScriptedInput = argument switch
		{
			"off" => null,
			"none" => new PawnInput(0, 0, false, false, false),
			"left" => new PawnInput(-1, 0, false, false, false),
			"right" => new PawnInput(1, 0, false, false, false),
			"dash-left" => new PawnInput(-1, 0, false, false, true, true),
			"dash-right" => new PawnInput(1, 0, false, false, true, true),
			"jump-right" => new PawnInput(1, 0, true, true, false),
			"up" => new PawnInput(0, -1, false, false, false),
			"down" => new PawnInput(0, 1, false, false, false),
			"attack" => new PawnInput(0, 0, false, false, false, AttackPressed: true),
			"jump-up" => new PawnInput(0, -1, true, true, false),
			"roll-left" => new PawnInput(-1, 1, false, false, false),
			"roll-right" => new PawnInput(1, 1, false, false, false),
			_ => player.ScriptedInput,
		};
		return argument is "off" or "none" or "left" or "right" or "dash-left" or "dash-right" or "jump-right" or "up" or "down" or "attack" or "jump-up" or "roll-left" or "roll-right"
			? $"input: {argument}"
			: $"error: unknown input '{argument}' (off, none, left, right, dash-left, dash-right, jump-right, up, down, attack, jump-up, roll-left, roll-right)";
	}

	// Pumped every frame from Manager.Update; dev work that has to tick lives here.
	internal void UpdateDevCommands()
	{
		DevHost.Pump(this);
		DevSampleTick();
	}

	int sampleFramesLeft;
	string samplePath = "";
	readonly List<string> sampleRows = [];

	// `sample <frames> [path]` records one row per update frame and writes the file when it fills.
	// The pipe answers within a single frame, so the caller gets the path up front and reads it after.
	string DevSample(string argument)
	{
		var space = argument.IndexOf(' ');
		var count = space < 0 ? argument : argument[..space];
		if (!int.TryParse(count, out var frames) || frames is < 1 or > 6000)
			return "error: sample takes a frame count between 1 and 6000";

		samplePath = Path.GetFullPath(space < 0 ? "obj/dev-samples.csv" : argument[(space + 1)..].Trim());
		sampleRows.Clear();
		sampleFramesLeft = frames;
		return $"sampling {frames} frames -> {samplePath}";
	}

	void DevSampleTick()
	{
		if (sampleFramesLeft <= 0)
			return;

		if (game?.GetFirst<Player>() is { } player)
			sampleRows.Add(string.Create(CultureInfo.InvariantCulture,
				$"{Time.Frame},{Time.Seconds:0.####},{player.Position.X},{player.Position.Y}," +
				$"{player.Velocity.X:0.##},{player.Velocity.Y:0.##},{(player.IsGrounded ? 1 : 0)},{player.State},{GroundLabel(player)}"));

		if (--sampleFramesLeft > 0)
			return;

		sampleRows.Insert(0, "frame,time,x,y,vx,vy,grounded,state,ground");
		File.WriteAllLines(samplePath, sampleRows);
		Console.WriteLine($"[Sample] wrote {samplePath} ({sampleRows.Count - 1} frames)");
	}

	string DevState()
	{
		var json = new StringBuilder();
		json.Append(CultureInfo.InvariantCulture,
			$"{{\"world\":\"{Assets.CurrentWorldId}\",\"time\":{Time.Seconds:0.###},\"frame\":{Time.Frame}" +
			$",\"idle\":{(DevHostIdle ? "true" : "false")}" +
			$",\"fullscreen\":{(Window?.Fullscreen == true ? "true" : "false")}");

		json.Append(CultureInfo.InvariantCulture, $",\"playing\":{(playing ? "true" : "false")}");
		if (game is not { } g)
			return json.Append('}').ToString();

		json.Append(CultureInfo.InvariantCulture, $",\"room\":\"{g.CurrentRoom?.Id}\"");
		if (g.GetFirst<Player>() is { } player)
		{
			// Room-local pixels alongside world pixels: local is what lines up with the room file.
			var local = player.Position - (g.CurrentRoom?.WorldBounds.TopLeft ?? Point2.Zero);
			json.Append(CultureInfo.InvariantCulture,
				$",\"player\":{{\"x\":{player.Position.X},\"y\":{player.Position.Y}," +
				$"\"localX\":{local.X},\"localY\":{local.Y}," +
				$"\"vx\":{player.Velocity.X:0.##},\"vy\":{player.Velocity.Y:0.##}," +
				$"\"state\":\"{player.State}\",\"grounded\":{(player.IsGrounded ? "true" : "false")}," +
				$"\"ground\":\"{GroundLabel(player)}\",\"facing\":{GroundFacing(player):0.###},\"health\":{player.Health}}}");
		}
		return json.Append('}').ToString();
	}

	// Defaults to a fixed path so callers can just read it back; pass one to keep a series around.
	string DevShot(string path)
	{
		path = string.IsNullOrWhiteSpace(path) ? "obj/dev-shot.png" : path;
		return CaptureScreenshot(path) ? Path.GetFullPath(path) : "error: screenshot failed (see log)";
	}
}
#endif
