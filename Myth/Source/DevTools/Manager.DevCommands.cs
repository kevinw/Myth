#if !BROWSER
using Foster.Framework;
using System.Globalization;
using System.Text;

namespace Myth;

// Verbs the dev host answers over its named pipe, so tooling can inspect and poke the session you
// are already playing instead of launching a fresh one:
//
//   dotnet bin/Debug/net10.0/Myth.dll --dev-command state
//   dotnet bin/Debug/net10.0/Myth.dll --dev-command "eval go Myth/Cellar"
//
// These run on the game thread (drained by DevHost.Pump in Update), so reading game state is safe.
// Answers must be a single line -- the pipe protocol is line-delimited.
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
			"shot" => DevShot(argument),
			"eval" => TryRunConsoleCommand(argument, out var error) ? $"ok: {argument}" : $"error: {error}",
			"commands" => string.Join(' ', consoleCommands.Keys.Order()),
			_ => $"error: unknown dev command '{verb}' (state, shot, eval, commands)",
		};
	}

	string DevState()
	{
		var json = new StringBuilder();
		json.Append(CultureInfo.InvariantCulture,
			$"{{\"world\":\"{Assets.CurrentWorldId}\",\"time\":{Time.Seconds:0.###},\"frame\":{Time.Frame}");

		if (game is not { } g)
			return json.Append(",\"playing\":false}").ToString();

		json.Append(CultureInfo.InvariantCulture, $",\"playing\":true,\"room\":\"{g.CurrentRoom?.Id}\"");
		if (g.GetFirst<Player>() is { } player)
		{
			// Room-local pixels alongside world pixels: local is what lines up with the room file.
			var local = player.Position - (g.CurrentRoom?.WorldBounds.TopLeft ?? Point2.Zero);
			json.Append(CultureInfo.InvariantCulture,
				$",\"player\":{{\"x\":{player.Position.X},\"y\":{player.Position.Y}," +
				$"\"localX\":{local.X},\"localY\":{local.Y}," +
				$"\"vx\":{player.Velocity.X:0.##},\"vy\":{player.Velocity.Y:0.##}," +
				$"\"state\":\"{player.State}\",\"grounded\":{(player.IsGrounded ? "true" : "false")},\"health\":{player.Health}}}");
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
