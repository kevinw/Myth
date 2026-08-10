using System.Numerics;
using Foster.Framework;
using FosterGames;

new SokobanGame().Run();

sealed class SokobanGame() : App(new AppConfig("Sokoban", "Sokoban", 960, 540, Resizable: true))
{
	const int Tile = 32;
	const int Columns = 10;
	const int Rows = 8;

	Batcher batcher = null!;
	readonly Stack<MoveUndo> undo = [];
	StateMachine<PlayState> state = new(PlayState.Playing);
	Point2 player;
	char[,] map = null!;

	static readonly string[] Level =
	[
		"##########",
		"#        #",
		"#  .     #",
		"#  $     #",
		"#  @ $$ .#",
		"#     .  #",
		"#        #",
		"##########",
	];

	protected override void Startup()
	{
		batcher = new Batcher(GraphicsDevice, "SokobanBatcher");
		Reset();
	}

	protected override void Shutdown()
		=> batcher.Dispose();

	protected override void Update()
	{
		state.Update(Time.Delta);

		if (Input.Keyboard.Pressed(Keys.Escape))
			Exit();
		if (Input.Keyboard.Pressed(Keys.R))
			Reset();
		if (Input.Keyboard.Pressed(Keys.Z))
			Undo();

		var move = Point2.Zero;
		if (Input.Keyboard.Pressed(Keys.W, Keys.Up)) move = Point2.Up;
		else if (Input.Keyboard.Pressed(Keys.S, Keys.Down)) move = Point2.Down;
		else if (Input.Keyboard.Pressed(Keys.A, Keys.Left)) move = Point2.Left;
		else if (Input.Keyboard.Pressed(Keys.D, Keys.Right)) move = Point2.Right;

		if (move != Point2.Zero && state.Current == PlayState.Playing)
			TryMove(move);
	}

	protected override void Render()
	{
		if (Window == null)
			return;

		Window.Clear(new Color(0x16181d));

		var origin = new Vector2(
			(Window.WidthInPixels - Columns * Tile) * 0.5f,
			(Window.HeightInPixels - Rows * Tile) * 0.5f);

		for (var y = 0; y < Rows; y++)
			for (var x = 0; x < Columns; x++)
				DrawCell(origin, new Point2(x, y));

		batcher.Render(Window);
		batcher.Clear();
	}

	void Reset()
	{
		map = new char[Columns, Rows];
		undo.Clear();
		for (var y = 0; y < Rows; y++)
			for (var x = 0; x < Columns; x++)
			{
				var c = Level[y][x];
				map[x, y] = c == '@' ? ' ' : c;
				if (c == '@')
					player = new Point2(x, y);
			}
		state.Change(PlayState.Playing);
	}

	void TryMove(Point2 direction)
	{
		var next = player + direction;
		var beyond = next + direction;
		var box = map[next.X, next.Y] == '$';
		if (map[next.X, next.Y] == '#' || box && map[beyond.X, beyond.Y] is '#' or '$')
			return;

		undo.Push(new MoveUndo(player, box ? next : null));
		player = next;
		if (box)
		{
			map[next.X, next.Y] = ' ';
			map[beyond.X, beyond.Y] = '$';
		}
		if (Won())
			state.Change(PlayState.Won);
	}

	void Undo()
	{
		if (!undo.TryPop(out var move))
			return;

		var direction = player - move.Player;
		if (move.Box is { } box)
		{
			map[(box + direction).X, (box + direction).Y] = ' ';
			map[box.X, box.Y] = '$';
		}
		player = move.Player;
		state.Change(PlayState.Playing);
	}

	bool Won()
	{
		for (var y = 0; y < Rows; y++)
			for (var x = 0; x < Columns; x++)
				if (Level[y][x] == '.' && map[x, y] != '$')
					return false;
		return true;
	}

	void DrawCell(Vector2 origin, Point2 cell)
	{
		var pos = origin + (Vector2)(cell * Tile);
		var baseRect = new Rect(pos, new Vector2(Tile));
		var floor = ((cell.X + cell.Y) & 1) == 0 ? new Color(0x242a31) : new Color(0x20262d);
		batcher.Rect(baseRect, map[cell.X, cell.Y] == '#' ? new Color(0x59616d) : floor);
		if (Level[cell.Y][cell.X] == '.')
			batcher.Rect(baseRect.Inflate(-10), new Color(0xf2cc60));
		if (map[cell.X, cell.Y] == '$')
			batcher.Rect(baseRect.Inflate(-5), new Color(0xc17f59));
		if (cell == player)
			batcher.Rect(baseRect.Inflate(-7), state.Current == PlayState.Won ? new Color(0x7bd88f) : new Color(0x5fcde4));
	}

	readonly record struct MoveUndo(Point2 Player, Point2? Box);
	enum PlayState { Playing, Won }
}
