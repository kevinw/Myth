#:project ../Myth/Myth.csproj
#:property AssemblyName=Mycelon.Scenario
#:property PublishAot=false

using System.Numerics;
using System.Runtime.CompilerServices;
using Foster.Framework;
using Myth;

UseGameWorkingDirectory();
var room = args.Length > 0 ? args[0] : "start";
var position = new Point2(
	args.Length > 1 ? int.Parse(args[1]) : 120,
	args.Length > 2 ? int.Parse(args[2]) : 120);
var output = Path.GetFullPath(args.Length > 3 ? args[3] : "../tmp/scenario-capture.png");
var manager = Manager.Create(new StartArgs(["--play-room", room, "--no-warmup"])
{
	audio = false
});
var frame = 0;
var moved = false;

manager.ScenarioUpdate += it =>
{
	if (it.game?.GetFirst<Player>() is not { } player)
		return;

	frame++;
	if (moved)
		return;

	player.Position = it.game.CurrentRoom!.WorldBounds.TopLeft + position;
	player.Velocity = Vector2.Zero;
	it.game.SnapCameraToGameplayTarget();
	moved = true;
};

manager.ScenarioRender += it =>
{
	if (!moved || frame < 30)
		return;

	if (!it.CaptureScreenshot(output, frame))
		throw new IOException($"Screenshot was not written: {output}");
	it.RequestQuit();
};

manager.Run();

static void UseGameWorkingDirectory([CallerFilePath] string source = "")
	=> Directory.SetCurrentDirectory(Path.GetFullPath(Path.Join(Path.GetDirectoryName(source)!, "../Myth")));
