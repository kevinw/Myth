#:project ../Myth/Myth.csproj
#:property AssemblyName=Mycelon.Scenario
#:property PublishAot=false

using System.Runtime.CompilerServices;
using System.Text.Json;
using Myth;

UseGameWorkingDirectory();
var room = args.Length > 0 ? args[0] : "start";
var frameLimit = args.Length > 1 ? int.Parse(args[1]) : 600;
var output = Path.GetFullPath(args.Length > 2 ? args[2] : "../tmp/scenario-simulation.json");
var manager = Manager.Create(new StartArgs(["--headless", "--unthrottled", "--play-room", room])
{
	audio = false
});
var frame = 0;

manager.ScenarioUpdate += it =>
{
	if (it.game is not { } game || ++frame < frameLimit)
		return;

	var player = game.GetFirst<Player>();
	var result = new
	{
		room = game.CurrentRoom?.Id,
		frames = frame,
		actors = game.Actors.Count,
		player = player == null ? null : new
		{
			position = new[] { player.Position.X, player.Position.Y },
			velocity = new[] { player.Velocity.X, player.Velocity.Y },
			health = player.Health
		}
	};

	Directory.CreateDirectory(Path.GetDirectoryName(output)!);
	File.WriteAllText(output, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
	Console.WriteLine($"[Scenario] wrote {output}");
	it.RequestQuit();
};

manager.Run();

static void UseGameWorkingDirectory([CallerFilePath] string source = "")
	=> Directory.SetCurrentDirectory(Path.GetFullPath(Path.Join(Path.GetDirectoryName(source)!, "../Myth")));
