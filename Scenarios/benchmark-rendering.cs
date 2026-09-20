#:project ../Myth/Myth.csproj
#:property AssemblyName=Mycelon.Scenario
#:property PublishAot=false

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Foster.Framework;
using Myth;

// dotnet run Scenarios/benchmark-rendering.cs -- [frames-per-sample] [--world <id>] [room ...]
// Completed CPU+GPU throughput, not GPU timestamps. No simulation, particles, PNG encoding,
// or swapchain presentation. A one-pixel readback drains the GPU at each sample boundary.
UseGameWorkingDirectory();
var frames = args.Length > 0 ? int.Parse(args[0]) : 120;
if (frames < 1)
	throw new ArgumentException("Frames per sample must be positive.");
var world = Array.IndexOf(args, "--world") is var w and >= 0 ? args[w + 1] : "Myth";
string[] rooms = args[1..].Where(it => it != "--world" && it != world).ToArray() is { Length: > 0 } named ? named :
	["start", "water_maze", "bud_moving_platform_middle", "moving_platform_circle", "tunnel_dark_hidden"];
var manager = Manager.Create(new StartArgs(["--world", world, "--play-room", rooms[0], "--no-warmup"])
{
	audio = false
});
manager.ScenarioRender += it =>
{
	foreach (var id in rooms)
	{
		using var game = new Game(it, Assets.RoomsById[id], audioEventsEnabled: false, spawnBud: false, enableParticles: false);
		game.AdvanceReplayClock(0);
		using var target = new Target(it.GraphicsDevice, Game.Width, Game.Height);
		target.Clear(Color.Transparent);
		var worldView = game.WorldView;
		var window = it.Window!;
		var viewport = new RectInt(Point2.Zero, window.SizeInPixels);
		var pixel = new Color[1];
		var walls = new List<Actor>();
		foreach (var actor in game.Actors)
			if (actor is ProceduralWall && actor.Visible)
				walls.Add(actor);
		foreach (var mode in new[] { "world", "world-no-walls", "present", "crt" })
		{
			if (mode == "world-no-walls" && walls.Count == 0)
				continue;
			foreach (var wall in walls)
				wall.Visible = mode != "world-no-walls";
			var renderWorld = mode is "world" or "world-no-walls";
			void Draw()
			{
				if (renderWorld)
					game.RenderWorldToTarget(target, worldView, zoom: game.ZoomLevel);
				else
				{
					window.Clear(Color.Black);
					game.Render(viewport, showHud: false, applyCrt: mode == "crt");
				}
				it.GraphicsDevice.SubmitPendingCommands();
			}
			void Drain() => target.Attachments[0].GetData<Color>(pixel, new RectInt(0, 0, 1, 1));
			for (var i = 0; i < 30; i++)
				Draw();
			Drain();
			var samples = new double[3];
			for (var sample = 0; sample < samples.Length; sample++)
			{
				PerfStats.Publish();
				var start = Stopwatch.GetTimestamp();
				for (var i = 0; i < frames; i++)
					Draw();
				Drain();
				samples[sample] = Stopwatch.GetElapsedTime(start).TotalMilliseconds / frames;
			}
			PerfStats.Publish();
			Console.WriteLine("RENDER_BENCH " + JsonSerializer.Serialize(new
			{
				room = id, mode, frames, window = new[] { viewport.Width, viewport.Height },
				proceduralWalls = walls.Count, millisecondsPerFrame = samples, lastSampleCpuMilliseconds = PerfStats.Snapshot
			}));
		}
	}
	it.RequestQuit();
};
manager.Run();

static void UseGameWorkingDirectory([CallerFilePath] string source = "")
	=> Directory.SetCurrentDirectory(Path.GetFullPath(Path.Join(Path.GetDirectoryName(source)!, "../Myth")));
