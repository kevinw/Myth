#:project ../Myth/Myth.csproj
#:property AssemblyName=Mycelon.Scenario
#:property PublishAot=false

using System.Numerics;
using System.Runtime.CompilerServices;
using Foster.Framework;
using Myth;

// dotnet run Scenarios/check-render-views.cs -- <output-dir> [--world <id>] [room ...]
// Captures screen-sized views scrolled across each room, so view-dependent work (tile culling)
// is actually exercised -- a room-sized capture renders everything and would hide it.
// Animation is time-driven, so a handful of pixels differ between runs of the same build:
// capture the same build twice to get the noise floor before reading a before/after diff.
UseGameWorkingDirectory();
var output = args[0];
Directory.CreateDirectory(output);
var world = Array.IndexOf(args, "--world") is var w and >= 0 ? args[w + 1] : "Myth";
string[] rooms = args[1..].Where(it => it != "--world" && it != world).ToArray() is { Length: > 0 } named ? named :
	["start", "water_maze", "bud_moving_platform_middle", "moving_platform_circle", "tunnel_dark_hidden"];
var manager = Manager.Create(new StartArgs(["--world", world, "--play-room", rooms[0], "--no-warmup"]) { audio = false });
manager.ScenarioRender += it =>
{
	foreach (var id in rooms)
	{
		var room = Assets.RoomsById[id];
		using var game = new Game(it, room, audioEventsEnabled: false, spawnBud: false, enableParticles: false);
		game.AdvanceReplayClock(0);
		using var target = new Target(it.GraphicsDevice, Game.Width, Game.Height);
		var span = room.WorldBounds.Size - new Vector2(Game.Width, Game.Height);
		for (var step = 0; step < 4; step++)
		{
			target.Clear(Color.Transparent);
			game.RenderWorldToTarget(target, new Rect(room.WorldBounds.TopLeft + span * (step / 3f), new Vector2(Game.Width, Game.Height)));
			using var image = new Image(target.Width, target.Height);
			target.Attachments[0].GetData(image.Data);
			image.WritePng(Path.Join(output, $"{id}-{step}.png"));
		}
	}
	it.RequestQuit();
};
manager.Run();

static void UseGameWorkingDirectory([CallerFilePath] string source = "")
	=> Directory.SetCurrentDirectory(Path.GetFullPath(Path.Join(Path.GetDirectoryName(source)!, "../Myth")));
