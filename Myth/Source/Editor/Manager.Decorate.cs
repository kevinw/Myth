#if EDITOR
using System.Net.Http.Headers;
using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;
using Foster.Framework;
using ImGuiNET;

namespace Myth;

// Experimental: decorates a graybox room from the rest of the world, one kind of decoration at a time.
// Code mines every other room for what it actually does -- its look, which lights it hangs where, which
// accents grow on which edges, its background motifs, its floor props -- and TypeSafe's Jev (a decision
// model: it picks, it doesn't generate) makes each narrow call: the look, how lit each region is, which
// accent fits each edge and how thickly, which motif (if any) each region gets. Code then places
// everything. Collision never changes: solid tiles only change material, and non-solid decoration only
// lands in empty tiles. `decorate` opens a window: Ask fetches the model's answers once, then the sliders
// and Reapply re-place from those cached answers on the room as it was when the window opened, instantly.
// Every decision is written to obj/decorate-<room>.txt.
public partial class Manager
{
	const int DecorRegion = 8;
	const int DecorQuestionsPerRequest = 30;
	static float DecorLookMixShare => 0.02f; // the top three looks with at least this probability lend their decoration
	const string TypeSafeUrl = "https://api.typesafe.ai/v1/systemone";
	static readonly string[] DecorLightLevels = ["unlit: no lights", "dim: one small light", "lit: a light or two", "bright: well lit"];
	static readonly string[] DecorDensities = ["sparse: a few scattered tufts", "some: patches along it", "dense: most of it covered"];
	static readonly HashSet<string> DecorPropTypes = [nameof(Prop), nameof(RigPuppetProp), nameof(Wildlife)];
	static readonly HttpClient typeSafeHttp = new() { Timeout = TimeSpan.FromSeconds(120) };

	enum DecorEdge { Floor, Ceiling, Wall }

	sealed record DecorPalette(string Key, char Solid, char Background, List<string> Rooms, string Description);
	sealed record DecorLight(string Palette, JsonObject Actor, string Label, string Mount);
	sealed record DecorAccent(string Palette, string RoomId, DecorEdge Edge, char Tile);
	sealed record DecorMotif(string Palette, char[,] Tiles, string Description);
	sealed record DecorProp(string Palette, JsonObject Actor, Point2 FloorOffset, string Label);
	sealed record DecorCorpus(List<DecorPalette> Palettes, List<DecorLight> Lights, List<DecorAccent> Accents, List<DecorMotif> Motifs, List<DecorProp> Props);
	sealed record DecorRun(DecorEdge Edge, List<Point2> Tiles, string Description);
	sealed record DecorTarget(Room Room, bool[,] Solid, List<RectInt> Regions, List<string> RegionDescriptions, List<DecorRun> Runs, List<Point2> Lights, JsonObject State);
	sealed record DecorPlan(DecorPalette Palette, bool Material, List<(Point2 Tile, char Ch)> Fg, List<(Point2 Tile, char Ch)> Bg, List<JsonObject> Actors);

	// What the model said, with the options it chose between: everything Place needs, so sliders re-place for free.
	sealed record DecorAnswers(List<DecorPalette> Looks, float[] LookOdds, DecorPalette Palette, HashSet<string> Mix,
		List<DecorLight> Lights, float[] MainLightOdds, float[] AccentLightOdds, float[] Brightness,
		List<DecorRun> Runs, List<List<char>> RunOptions, float[][] EdgeOdds, float[] Thickness,
		List<DecorMotif> Motifs, float[][] MotifOdds, List<DecorRun> Floors, List<DecorProp> Props, float[][] PropOdds, string Report);

	// The window's knobs. Look and mix change which options are offered, so they take effect on the next Ask.
	sealed class DecorSettings
	{
		public int Look = -1;              // -1: the model's pick
		public int MixLooks = 3;
		public bool Material = true;       // swap the graybox's solid tiles and background for the look's
		public float LightAmount = 0.5f;   // share of regions that get a light
		public float DoubleLights = 0.3f;  // share of those that also get an accent light
		public int LightSpacing = 10;      // tiles between any two lights, counting ones already in the room
		public int MainLight = -1, AccentLight = -1;
		public float AccentAmount = 0.5f;  // share of edges that grow an accent
		public float AccentThickness = 0.6f;
		public float MotifAmount = 0.3f;   // share of regions with a background motif
		public float PropAmount = 0.3f;    // share of floors with a prop or creature
		public int Seed;
	}

	bool decorWindowOpen;
	Room? decorRoom;
	string decorSnapshot = "";         // the room as it was when the window opened: every Reapply starts from it
	DecorCorpus? decorCorpus;
	DecorTarget? decorTarget;
	DecorAnswers? decorAnswers;
	Task<DecorAnswers>? decorAsking;
	readonly DecorSettings decorSettings = new();
	bool decorDirty;
	string decorStatus = "";

	// Hand-written notes on what tiles and actors look like (Source/Editor/decor-notes.json): the model only
	// sees names, so this is how it knows that "gates" reads like spikes.
	static Dictionary<string, string> decorTileNotes = [], decorActorNotes = [];

	static void LoadDecorNotes()
	{
		if (FindSourceFile("Source", "Editor", "decor-notes.json") is not { } path || JsonNode.Parse(File.ReadAllText(path)) is not JsonObject notes)
			return;
		static Dictionary<string, string> Read(JsonNode? node)
			=> node is JsonObject map ? map.ToDictionary(it => it.Key, it => it.Value?.GetValue<string>() ?? "", StringComparer.OrdinalIgnoreCase) : [];
		decorTileNotes = Read(notes["tiles"]);
		decorActorNotes = Read(notes["actors"]);
	}

	static string TileNote(char c) => decorTileNotes.TryGetValue(TileName(c), out var note) ? $"{TileName(c)} ({note})" : TileName(c);
	static string ActorNote(string label) => decorActorNotes.TryGetValue(label, out var note) ? $"{label} ({note})" : label;

	// `decorate` opens the window; `decorate ask|baseline|reapply|restore` presses its buttons, for scripting.
	[ConsoleCommand("decorate")]
	void DecorateRoom(string action)
	{
		if (CurrentRoom is not { } room)
			throw new InvalidOperationException("No room selected.");
		OpenDecorWindow(room);
		switch (action.Trim())
		{
			case "ask": StartDecorAsk(baseline: false); break;
			case "baseline": StartDecorAsk(baseline: true); break;
			case "reapply": ReapplyDecoration(); break;
			case "restore": RestoreDecorSnapshot(); break;
		}
	}

	void OpenDecorWindow(Room room)
	{
		decorWindowOpen = true;
		if (decorRoom != room)
			RebaseDecoration(room);
	}

	// Takes the room as it is now as the base every Reapply starts from.
	void RebaseDecoration(Room room)
	{
		LoadDecorNotes();
		decorRoom = room;
		decorSnapshot = room.ToJsonString();
		decorCorpus = MineDecorCorpus(room);
		decorTarget = AnalyzeDecorTarget(room, decorCorpus.Palettes);
		decorAnswers = null;
		decorStatus = $"{decorTarget.Regions.Count} regions, {decorTarget.Runs.Count} edges. Ask to start.";
	}

	void StartDecorAsk(bool baseline)
	{
		if (decorCorpus == null || decorTarget == null || decorAsking != null)
			return;
		var apiKey = baseline ? "" : ReadTypeSafeKey();
		var (corpus, target, look, mix) = (decorCorpus, decorTarget, decorSettings.Look, decorSettings.MixLooks);
		decorStatus = baseline ? "Taking the most common answers..." : "Asking TypeSafe...";
		decorAsking = Task.Run(() => AskDecoration(corpus, target, apiKey, baseline, look, mix));
	}

	// Called every editor frame: picks up finished answers, re-places after a slider settles, draws the window.
	void UpdateRoomDecoration()
	{
		if (decorAsking is { IsCompleted: true } task)
		{
			decorAsking = null;
			if (task.IsFaulted)
				decorStatus = $"Failed: {task.Exception?.GetBaseException().Message}";
			else
			{
				decorAnswers = task.Result;
				decorSettings.MainLight = decorSettings.AccentLight = -1;
				decorStatus = $"Answers in for {decorAnswers.Palette.Description.Split(';')[0]}.";
				ReapplyDecoration();
			}
		}
		if (decorDirty && !Input.Mouse.LeftDown)
		{
			decorDirty = false;
			ReapplyDecoration();
		}
	}

	void DrawDecorWindow()
	{
		if (!decorWindowOpen || decorRoom == null)
			return;
		var s = decorSettings;
		ImGui.SetNextWindowSize(new Vector2(420, 620), ImGuiCond.FirstUseEver);
		if (ImGui.Begin($"Decorate {decorRoom.Id}###Decorate", ref decorWindowOpen))
		{
			ImGui.TextWrapped(decorStatus);
			ImGui.BeginDisabled(decorAsking != null);
			if (ImGui.Button("Ask TypeSafe"))
				StartDecorAsk(baseline: false);
			ImGui.SameLine();
			if (ImGui.Button("Baseline"))
				StartDecorAsk(baseline: true);
			ImGui.EndDisabled();
			ImGui.SameLine();
			ImGui.BeginDisabled(decorAnswers == null);
			if (ImGui.Button("Reapply"))
				ReapplyDecoration();
			ImGui.EndDisabled();
			ImGui.SameLine();
			if (ImGui.Button("Restore"))
				RestoreDecorSnapshot();
			if (ImGui.Button("Use current room as base"))
				RebaseDecoration(decorRoom);

			ImGui.SeparatorText("Look (takes effect on Ask)");
			if (decorAnswers is { } answers)
				LookCombo(answers);
			ImGui.SliderInt("Mix looks", ref s.MixLooks, 1, 3);
			Changed(ImGui.Checkbox("Swap material and background", ref s.Material));

			ImGui.SeparatorText("Lighting");
			Changed(ImGui.SliderFloat("Lit regions", ref s.LightAmount, 0, 1));
			Changed(ImGui.SliderFloat("Second lights", ref s.DoubleLights, 0, 1));
			Changed(ImGui.SliderInt("Light spacing", ref s.LightSpacing, 3, 24));
			if (decorAnswers is { Lights.Count: > 0 } lit)
			{
				LightCombo("Main light", lit, lit.MainLightOdds, ref s.MainLight);
				LightCombo("Accent light", lit, lit.AccentLightOdds, ref s.AccentLight);
			}

			ImGui.SeparatorText("Accents and dressing");
			Changed(ImGui.SliderFloat("Edges with accents", ref s.AccentAmount, 0, 1));
			Changed(ImGui.SliderFloat("Accent thickness", ref s.AccentThickness, 0, 1));
			Changed(ImGui.SliderFloat("Background motifs", ref s.MotifAmount, 0, 1));
			Changed(ImGui.SliderFloat("Props", ref s.PropAmount, 0, 1));
			Changed(ImGui.SliderInt("Seed", ref s.Seed, 0, 99));
		}
		ImGui.End();

		void Changed(bool changed) => decorDirty |= changed && decorAnswers != null;
	}

	void LookCombo(DecorAnswers answers)
	{
		var label = decorSettings.Look < 0 ? $"Model's pick: {Short(answers.Palette)}" : Short(answers.Looks[decorSettings.Look]);
		if (!ImGui.BeginCombo("Look", label))
			return;
		if (ImGui.Selectable("Model's pick", decorSettings.Look < 0))
			decorSettings.Look = -1;
		foreach (var i in Enumerable.Range(0, answers.Looks.Count).OrderByDescending(i => answers.LookOdds[i]).Take(20))
			if (ImGui.Selectable($"{Short(answers.Looks[i])}  (p {answers.LookOdds[i]:0.00})##{i}", decorSettings.Look == i))
				decorSettings.Look = i;
		ImGui.EndCombo();

		static string Short(DecorPalette look) => $"{TileName(look.Solid)} / {TileName(look.Background)}";
	}

	void LightCombo(string name, DecorAnswers answers, float[] odds, ref int pick)
	{
		var shown = pick < 0 ? ArgMax(odds) : pick;
		if (!ImGui.BeginCombo(name, $"{(pick < 0 ? "Model's pick: " : "")}{answers.Lights[shown].Label}"))
			return;
		if (ImGui.Selectable("Model's pick", pick < 0))
			(pick, decorDirty) = (-1, true);
		for (var i = 0; i < answers.Lights.Count; i++)
			if (ImGui.Selectable($"{answers.Lights[i].Label}  (p {odds[i]:0.00})##{i}", pick == i))
				(pick, decorDirty) = (i, true);
		ImGui.EndCombo();
	}

	void RestoreDecorSnapshot()
	{
		if (decorRoom is not { } room)
			return;
		using (CaptureRoomUndo("Restore Before Decorating", room))
			room.LoadJsonState(decorSnapshot);
		room.Save();
		MarkRoomPreviewDirty(room);
		editorGameDirty = true;
	}

	void ReapplyDecoration()
	{
		if (decorRoom is not { } room || decorAnswers is not { } answers || decorTarget is not { } target)
			return;
		var plan = PlaceDecoration(answers, target, decorSettings);
		using (CaptureRoomUndo("Decorate Room", room))
		{
			room.LoadJsonState(decorSnapshot);
			ApplyDecoration(room, plan);
		}
		room.Save();
		MarkRoomPreviewDirty(room);
		editorGameDirty = true;
		decorStatus = $"{TileName(plan.Palette.Solid)} / {TileName(plan.Palette.Background)}: {plan.Fg.Count} accent tiles, " +
			$"{plan.Bg.Count} motif tiles, {plan.Actors.Count} actors.";

		var reportPath = Path.Combine(Path.GetDirectoryName(FindSourceFile(".env") ?? FindSourceFile("Myth.csproj") ?? ".")!, "obj", $"decorate-{room.Id}.txt");
		Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
		File.WriteAllText(reportPath, answers.Report);
	}

	static string ReadTypeSafeKey()
	{
		if (FindSourceFile(".env") is { } path)
			foreach (var line in File.ReadAllLines(path))
				if (line.StartsWith("TYPESAFE_API_KEY=", StringComparison.Ordinal))
					return line["TYPESAFE_API_KEY=".Length..].Trim().Trim('"');
		return Environment.GetEnvironmentVariable("TYPESAFE_API_KEY")
			?? throw new InvalidOperationException("TYPESAFE_API_KEY not found in Myth/.env or the environment.");
	}

	// --- Mining ---

	static bool IsSolidAt(Room room, int x, int y)
		=> x < 0 || y < 0 || x >= room.Size.X || y >= room.Size.Y || room.GetEntry(new(x, y)) is { BlocksMovement: true };

	static bool IsSolidAt(bool[,] solid, int x, int y)
		=> x < 0 || y < 0 || x >= solid.GetLength(0) || y >= solid.GetLength(1) || solid[x, y];

	static bool IsDecorTile(Factory.Entry? entry)
		=> entry is { Layer: Factory.TileLayer.Foreground, BlocksMovement: false, DrawsVisual: true } && entry.Name != "water";

	static char MostCommon(IEnumerable<char> chars)
		=> chars.GroupBy(c => c).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault('0');

	static (char Solid, char Background) RoomPalette(Room room)
	{
		var solids = new List<char>();
		var backgrounds = new List<char>();
		for (var y = 0; y < room.Size.Y; y++)
			for (var x = 0; x < room.Size.X; x++)
			{
				if (room.GetEntry(new(x, y)) is { BlocksMovement: true })
					solids.Add(room.Tiles[x, y]);
				if (room.BackgroundTiles[x, y] != '0')
					backgrounds.Add(room.BackgroundTiles[x, y]);
			}
		return (MostCommon(solids), MostCommon(backgrounds));
	}

	static string TileName(char c) => Factory.Find(c)?.Name ?? (c == '0' ? "none" : c.ToString());

	static string ActorLabel(JsonObject actor)
		=> ActorSpecs.ReadString(actor, "From") is { } from ? from
			: ActorSpecs.GetTypeName(actor) is nameof(Prop) ? $"Prop {ActorSpecs.ReadString(actor, nameof(Prop.SpriteName))}"
			: ActorSpecs.GetTypeName(actor) ?? "Actor";

	static bool IsAttached(JsonObject actor) => ActorSpecs.ReadString(actor, nameof(Actor.AttachedToActorId)) is { Length: > 0 };

	// Where a tile sits against the terrain: what a light is mounted on, or what an accent grows from.
	static DecorEdge? EdgeAt(Func<int, int, bool> solid, int x, int y)
		=> solid(x, y) ? null : solid(x, y + 1) ? DecorEdge.Floor : solid(x, y - 1) ? DecorEdge.Ceiling
			: solid(x - 1, y) || solid(x + 1, y) ? DecorEdge.Wall : null;

	DecorCorpus MineDecorCorpus(Room target)
	{
		var members = new Dictionary<string, (char Solid, char Bg, List<Room> Rooms)>();
		var lights = new List<DecorLight>();
		var accents = new List<DecorAccent>();
		var motifs = new List<DecorMotif>();
		var props = new List<DecorProp>();
		foreach (var room in Assets.RoomsById.Values)
		{
			if (room == target || room.Sublayer != target.Sublayer)
				continue;
			var (solid, bg) = RoomPalette(room);
			if (solid == '0')
				continue;
			var key = $"{solid}{bg}";
			if (!members.TryGetValue(key, out var group))
				members[key] = group = (solid, bg, []);
			group.Rooms.Add(room);
			bool Solid(int x, int y) => IsSolidAt(room, x, y);

			for (var y = 0; y < room.Size.Y; y++)
				for (var x = 0; x < room.Size.X; x++)
					if (IsDecorTile(room.GetEntry(new(x, y))) && EdgeAt(Solid, x, y) is { } edge)
						accents.Add(new(key, room.Id, edge, room.Tiles[x, y]));

			foreach (var actor in room.Actors)
			{
				if (IsAttached(actor) || ActorSpecs.ReadPoint(actor, "Position") is not { } position)
					continue;
				var tile = position / Game.TileSize;
				var type = ActorSpecs.GetTypeName(actor);
				if (type == nameof(Light))
					lights.Add(new(key, actor, ActorLabel(actor), EdgeAt(Solid, tile.X, tile.Y)?.ToString().ToLowerInvariant() ?? "open air"));
				else if (DecorPropTypes.Contains(type ?? ""))
					for (var down = 0; down <= 3; down++)
						if (Solid(tile.X, tile.Y + down + 1) && !Solid(tile.X, tile.Y + down))
						{
							props.Add(new(key, actor, position - new Point2(tile.X * Game.TileSize, (tile.Y + down + 1) * Game.TileSize), ActorLabel(actor)));
							break;
						}
			}

			// Background motifs: 8x8 windows holding something beyond the room's base background.
			for (var y = 0; y + DecorRegion <= room.Size.Y; y += DecorRegion / 2)
				for (var x = 0; x + DecorRegion <= room.Size.X; x += DecorRegion / 2)
				{
					var tiles = new char[DecorRegion, DecorRegion];
					var counts = new Dictionary<char, int>();
					for (var dy = 0; dy < DecorRegion; dy++)
						for (var dx = 0; dx < DecorRegion; dx++)
						{
							var c = room.BackgroundTiles[x + dx, y + dy];
							tiles[dx, dy] = c != bg ? c : '0';
							if (c != '0' && c != bg)
								counts[c] = counts.GetValueOrDefault(c) + 1;
						}
					if (counts.Values.Sum() >= 4)
						motifs.Add(new(key, tiles, string.Join(", ", counts.OrderByDescending(c => c.Value)
							.Select(c => $"{TileNote(c.Key)}: {c.Value} tiles, {MotifShape(tiles, c.Key)}"))));
				}
		}

		var palettes = new List<DecorPalette>();
		foreach (var (key, (solid, bg, rooms)) in members.OrderByDescending(it => it.Value.Rooms.Count))
		{
			var accentNames = accents.Where(a => a.Palette == key).GroupBy(a => a.Tile).OrderByDescending(g => g.Count()).Take(3).Select(g => TileNote(g.Key));
			var lightNames = lights.Where(l => l.Palette == key).GroupBy(l => l.Label).OrderByDescending(g => g.Count()).Take(3).Select(g => ActorNote(g.Key));
			palettes.Add(new(key, solid, bg, [.. rooms.Select(r => r.Id)],
				$"solid tiles: {TileNote(solid)}; background: {TileNote(bg)}; used by {rooms.Count} rooms " +
				$"(e.g. {string.Join(", ", rooms.Take(4).Select(r => r.Id))}); accents: {string.Join(", ", accentNames)}; lights: {string.Join(", ", lightNames)}"));
		}
		return new(palettes, lights, accents, motifs, props);
	}

	static DecorMotif? WithoutWalls(DecorMotif motif, HashSet<char> walls)
	{
		var tiles = (char[,])motif.Tiles.Clone();
		var counts = new Dictionary<char, int>();
		for (var y = 0; y < tiles.GetLength(1); y++)
			for (var x = 0; x < tiles.GetLength(0); x++)
				if (walls.Contains(tiles[x, y]))
					tiles[x, y] = '0';
				else if (tiles[x, y] != '0')
					counts[tiles[x, y]] = counts.GetValueOrDefault(tiles[x, y]) + 1;
		return counts.Values.Sum() < 4 ? null : motif with
		{
			Tiles = tiles,
			Description = string.Join(", ", counts.OrderByDescending(c => c.Value).Select(c => $"{TileNote(c.Key)}: {c.Value} tiles, {MotifShape(tiles, c.Key)}")),
		};
	}

	static string MotifShape(char[,] tiles, char c)
	{
		int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
		for (var y = 0; y < tiles.GetLength(1); y++)
			for (var x = 0; x < tiles.GetLength(0); x++)
				if (tiles[x, y] == c)
					(minX, minY, maxX, maxY) = (Math.Min(minX, x), Math.Min(minY, y), Math.Max(maxX, x), Math.Max(maxY, y));
		var (w, h) = (maxX - minX + 1, maxY - minY + 1);
		return h >= w * 2 ? $"a {w}-wide vertical strip" : w >= h * 2 ? $"a {h}-tall horizontal band" : $"a {w}x{h} patch";
	}

	// --- The target room ---

	DecorTarget AnalyzeDecorTarget(Room room, List<DecorPalette> palettes)
	{
		var solid = new bool[room.Size.X, room.Size.Y];
		for (var y = 0; y < room.Size.Y; y++)
			for (var x = 0; x < room.Size.X; x++)
				solid[x, y] = IsSolidAt(room, x, y);
		bool Solid(int x, int y) => IsSolidAt(solid, x, y);

		var gameplay = room.Actors.Where(a => ActorSpecs.GetTypeName(a) is not nameof(Light))
			.Select(a => (Label: ActorLabel(a), Tile: (ActorSpecs.ReadPoint(a, "Position") ?? Point2.Zero) / Game.TileSize)).ToList();

		var regions = new List<RectInt>();
		var regionDescriptions = new List<string>();
		for (var y = 0; y < room.Size.Y; y += DecorRegion)
			for (var x = 0; x < room.Size.X; x += DecorRegion)
			{
				var rect = RectInt.Between(new(x, y), Point2.Min(new(x + DecorRegion, y + DecorRegion), room.Size));
				var open = 0;
				var floors = 0;
				for (var ty = rect.Top; ty < rect.Bottom; ty++)
					for (var tx = rect.Left; tx < rect.Right; tx++)
						if (!solid[tx, ty])
						{
							open++;
							if (Solid(tx, ty + 1))
								floors++;
						}
				if (open < rect.Width * rect.Height / 5)
					continue;
				var inside = gameplay.Where(g => rect.Contains(g.Tile)).Select(g => g.Label).ToList();
				regions.Add(rect);
				regionDescriptions.Add($"tiles x {rect.Left}-{rect.Right - 1}, y {rect.Top}-{rect.Bottom - 1}: {open * 100 / (rect.Width * rect.Height)}% open, " +
					$"{floors} floor tiles to stand on{(inside.Count > 0 ? $", gameplay here: {string.Join(", ", inside)}" : "")}");
			}

		var runs = new List<DecorRun>();
		CollectRuns(DecorEdge.Floor, horizontal: true, (x, y) => !Solid(x, y) && Solid(x, y + 1));
		CollectRuns(DecorEdge.Ceiling, horizontal: true, (x, y) => !Solid(x, y) && Solid(x, y - 1) && !Solid(x, y + 1));
		CollectRuns(DecorEdge.Wall, horizontal: false, (x, y) => !Solid(x, y) && (Solid(x - 1, y) || Solid(x + 1, y)) && !Solid(x, y + 1) && !Solid(x, y - 1));

		void CollectRuns(DecorEdge edge, bool horizontal, Func<int, int, bool> on)
		{
			var (outer, inner) = horizontal ? (room.Size.Y, room.Size.X) : (room.Size.X, room.Size.Y);
			for (var a = 0; a < outer; a++)
			{
				var run = new List<Point2>();
				for (var b = 0; b <= inner; b++)
				{
					var tile = horizontal ? new Point2(b, a) : new Point2(a, b);
					if (b < inner && on(tile.X, tile.Y))
					{
						run.Add(tile);
						continue;
					}
					if (run.Count >= 2)
						runs.Add(new(edge, run, $"a {run.Count}-tile {edge.ToString().ToLowerInvariant()} from tile {run[0]} to {run[^1]}"));
					run = [];
				}
			}
		}

		var map = new StringBuilder();
		for (var y = 0; y < room.Size.Y; y++)
		{
			for (var x = 0; x < room.Size.X; x++)
				map.Append(solid[x, y] ? '#' : '.');
			map.Append('\n');
		}
		var center = room.WorldBounds.Center;
		var neighbors = Assets.RoomsById.Values.Where(r => r != room && r.Sublayer == room.Sublayer)
			.OrderBy(r => Vector2.DistanceSquared(r.WorldBounds.Center, center)).Take(6)
			.Select(r => $"{r.Id}: {palettes.FirstOrDefault(p => p.Rooms.Contains(r.Id))?.Description ?? "unknown look"}");
		var state = new JsonObject
		{
			["game"] = "A 2D pixel-art platformer. Rooms are tile grids, 8px tiles, y grows downward; '#' is solid terrain, '.' is open space.",
			["room"] = room.Id,
			["size_tiles"] = $"{room.Size.X} x {room.Size.Y}",
			["gameplay_actors"] = new JsonArray([.. gameplay.Select(g => (JsonNode)$"{g.Label} at tile {g.Tile}")]),
			["nearby_rooms_in_world"] = new JsonArray([.. neighbors.Select(n => (JsonNode)n)]),
			["map"] = map.ToString(),
		};
		var lights = room.Actors.Where(a => ActorSpecs.GetTypeName(a) == nameof(Light))
			.Select(a => (ActorSpecs.ReadPoint(a, "Position") ?? Point2.Zero) / Game.TileSize).ToList();
		return new(room, solid, regions, regionDescriptions, runs, lights, state);
	}

	// --- Asking (off the main thread) ---

	static async Task<DecorAnswers> AskDecoration(DecorCorpus corpus, DecorTarget target, string apiKey, bool baseline, int lookOverride, int mixLooks)
	{
		var report = new StringBuilder();
		var ask = baseline ? null : new DecorAsker(apiKey, target.State.DeepClone().AsObject());

		// The look sets the base material and background; the top looks by probability lend their lights,
		// accents, motifs and props.
		var offered = corpus.Palettes.Take(255).ToList();
		var lookOdds = ask == null
			? [.. offered.Select((_, i) => i == 0 ? 1f : 0)]
			: (await ask.Choices(["Which existing look (solid tile material and background, with its usual accents and lights) should this graybox room " +
				"take on? Keep the world coherent with nearby rooms, and fit what happens in this room."],
				[[.. offered.Select(p => p.Description)]], "look", report))[0];
		var palette = offered[lookOverride >= 0 && lookOverride < offered.Count ? lookOverride : ArgMax(lookOdds)];
		var mix = Enumerable.Range(0, offered.Count).Where(i => lookOdds[i] >= DecorLookMixShare).OrderByDescending(i => lookOdds[i]).Take(mixLooks - 1)
			.Select(i => offered[i].Key).Append(palette.Key).ToHashSet();
		report.AppendLine($"Look: {palette.Description}");
		foreach (var key in mix.Where(k => k != palette.Key))
			report.AppendLine($"Mixed in: {offered.First(p => p.Key == key).Description}");
		report.AppendLine();
		ask?.SetState("chosen_look", palette.Description);

		// Prefer what the mixed looks' rooms do, then rooms sharing the base material or background, then the whole world.
		List<T> OfLook<T>(List<T> all, Func<T, string> paletteOf, int enough)
		{
			List<T> same = [.. all.Where(it => mix.Contains(paletteOf(it)))];
			if (same.Count >= enough)
				return same;
			List<T> similar = [.. all.Where(it => paletteOf(it)[0] == palette.Solid || paletteOf(it)[1] == palette.Background)];
			return similar.Count >= enough ? similar : all;
		}

		var lights = OfLook(corpus.Lights, l => l.Palette, 2).GroupBy(l => l.Label).OrderByDescending(g => g.Count()).Take(8)
			.Select(g => g.First()).ToList();
		var accents = OfLook(corpus.Accents, a => a.Palette, 10);
		// A motif made of some look's base wall would read as a hole in this room's wall, not an accent.
		var walls = corpus.Palettes.Select(p => p.Background).ToHashSet();
		var motifs = OfLook(corpus.Motifs, m => m.Palette, 3).Select(m => WithoutWalls(m, walls)).OfType<DecorMotif>()
			.DistinctBy(m => m.Description).Take(12).ToList();
		var props = OfLook(corpus.Props, p => p.Palette, 2).DistinctBy(p => p.Label).Take(10).ToList();

		float[] OneHot(int count, int at) => [.. Enumerable.Range(0, count).Select(i => i == at ? 1f : 0)];

		// Lighting: a main and an accent light for the room, and how lit each region should be.
		var labels = lights.Select(LightDescription).ToList();
		var mainOdds = lights.Count == 0 ? [] : ask == null ? OneHot(lights.Count, 0)
			: (await ask.Choices(["Which light should be this room's main light source?"], [labels], "main_light", report))[0];
		var accentOdds = lights.Count == 0 ? [] : ask == null ? OneHot(lights.Count, Math.Min(1, lights.Count - 1))
			: (await ask.Choices([$"Which light makes the best occasional accent next to {lights[ArgMax(mainOdds)].Label}?"], [labels], "accent_light", report))[0];
		var brightness = ask == null || lights.Count == 0
			? [.. target.Regions.Select((_, i) => DecorNoise(new(i, 7), 0))]
			: await ask.Scores([.. target.RegionDescriptions.Select(d =>
				$"How lit should this region be? Light draws the eye to the route and to danger, darkness gives contrast. Region {d}")], DecorLightLevels, "light", report);

		// Edge accents: only ones that mostly grow on that kind of edge, described by how the world uses them.
		var accentNotes = new Dictionary<char, string>();
		foreach (var group in accents.GroupBy(a => a.Tile))
		{
			var where = group.GroupBy(a => a.Edge).OrderByDescending(g => g.Count()).Select(g => $"{g.Key.ToString().ToLowerInvariant()}s {g.Count() * 100 / group.Count()}%");
			var rooms = group.Select(a => a.RoomId).Distinct().ToList();
			accentNotes[group.Key] = $"{TileNote(group.Key)}: grows on {string.Join(", ", where)}; used in {rooms.Count} rooms like {string.Join(", ", rooms.Take(3))}";
		}
		var edgeOptions = Enum.GetValues<DecorEdge>().ToDictionary(edge => edge, edge => accents.GroupBy(a => a.Tile)
			.Where(g => g.Count(a => a.Edge == edge) >= g.Count() * 0.3f)
			.OrderByDescending(g => g.Count(a => a.Edge == edge)).Take(6).Select(g => g.Key).ToList());
		foreach (var (edge, options) in edgeOptions)
			report.AppendLine($"{edge} accents offered: {string.Join("; ", options.Select(t => accentNotes[t]))}");
		report.AppendLine($"Motifs offered: {motifs.Count}\n");
		var runs = target.Runs.Where(r => edgeOptions[r.Edge].Count > 0).ToList();
		var edgeOdds = ask == null
			? [.. runs.Select(r => (float[])[DecorNoise(r.Tiles[0], 0), 1, .. edgeOptions[r.Edge].Skip(1).Select(_ => 0f)])]
			: await ask.Choices([.. runs.Select(r => $"Which accent, if any, suits this edge: {r.Description}? " +
				"Decoration must never be mistaken for a hazard, so avoid anything that reads like spikes.")],
				[.. runs.Select(r => (List<string>)["none", .. edgeOptions[r.Edge].Select(t => accentNotes[t])])], "edge", report);
		var thickness = ask == null
			? [.. runs.Select(r => (float)r.Tiles.Count)]
			: await ask.Scores([.. runs.Select(r => $"How thickly should accents grow along this edge? Edge: {r.Description}")], DecorDensities, "density", report);

		var motifOdds = motifs.Count == 0 ? [] : ask == null
			? [.. target.Regions.Select((_, i) => (float[])[DecorNoise(new(i, 3), 0), .. motifs.Select((_, m) => m == i % motifs.Count ? 1f : 0)])]
			: await ask.Choices([.. target.RegionDescriptions.Select(d => $"Which background motif, if any, should this region show? Region {d}")],
				[.. target.Regions.Select(_ => (List<string>)["none", .. motifs.Select(m => m.Description)])], "motif", report);

		var floors = target.Runs.Where(r => r.Edge == DecorEdge.Floor && r.Tiles.Count >= 3).ToList();
		var propOdds = props.Count == 0 || floors.Count == 0 ? [] : ask == null
			? [.. floors.Select((r, i) => (float[])[DecorNoise(r.Tiles[0], 1), .. props.Select((_, p) => p == i % props.Count ? 1f : 0)])]
			: await ask.Choices([.. floors.Select(r => $"Which prop or creature, if any, belongs on this floor: {r.Description}?")],
				[.. floors.Select(_ => (List<string>)["none", .. props.Select(p => ActorNote(p.Label))])], "prop", report);

		return new(offered, lookOdds, palette, mix, lights, mainOdds, accentOdds, brightness,
			runs, [.. runs.Select(r => edgeOptions[r.Edge])], edgeOdds, thickness,
			motifs, motifOdds, floors, props, propOdds, report.ToString());
	}

	// --- Placing (pure: cached answers + settings) ---

	static DecorPlan PlaceDecoration(DecorAnswers answers, DecorTarget target, DecorSettings settings)
	{
		var fg = new List<(Point2, char)>();
		var bg = new List<(Point2, char)>();
		var actors = new List<JsonObject>();
		var seed = settings.Seed;

		// Lights: the brightest-ranked regions first, a second (accent) light in the very brightest.
		if (answers.Lights.Count > 0)
		{
			var main = answers.Lights[settings.MainLight >= 0 ? settings.MainLight : ArgMax(answers.MainLightOdds)];
			var accent = answers.Lights[settings.AccentLight >= 0 ? settings.AccentLight : ArgMax(answers.AccentLightOdds)];
			var doubled = settings.LightAmount * settings.DoubleLights;
			var tiers = DecorTiers(answers.Brightness, doubled, settings.LightAmount - doubled);
			var placed = new List<Point2>(target.Lights);
			foreach (var i in Enumerable.Range(0, target.Regions.Count).OrderBy(i => tiers[i]))
				for (var n = 0; n < 2 - tiers[i]; n++)
					if (LightAnchor(target, target.Regions[i], placed, settings.LightSpacing) is { } anchor)
					{
						placed.Add(anchor);
						actors.Add(Place((n == 1 ? accent : main).Actor, anchor * Game.TileSize + new Point2(Game.TileSize / 2, Game.TileSize / 2)));
					}
		}

		// Accents: the edges the model was least sure should stay bare, thickest where it ranked them thickest.
		var accentTiers = DecorTiers([.. answers.EdgeOdds.Select(p => 1 - p[0])], settings.AccentAmount);
		var thickTiers = DecorTiers(answers.Thickness, 0.33f, 0.33f);
		for (var i = 0; i < answers.Runs.Count; i++)
		{
			if (accentTiers[i] > 0)
				continue;
			var tile = answers.RunOptions[i][ArgMax(answers.EdgeOdds[i][1..])];
			var cover = Math.Clamp(settings.AccentThickness * thickTiers[i] switch { 0 => 1.4f, 1 => 1f, _ => 0.6f }, 0, 1);
			foreach (var at in answers.Runs[i].Tiles)
				if (DecorNoise(at, seed) < cover)
					fg.Add((at, tile));
		}

		if (answers.Motifs.Count > 0)
		{
			var tiers = DecorTiers([.. answers.MotifOdds.Select(p => 1 - p[0])], settings.MotifAmount);
			for (var i = 0; i < target.Regions.Count; i++)
			{
				if (tiers[i] > 0)
					continue;
				var motif = answers.Motifs[ArgMax(answers.MotifOdds[i][1..])];
				var region = target.Regions[i];
				for (var y = 0; y < DecorRegion; y++)
					for (var x = 0; x < DecorRegion; x++)
						if (motif.Tiles[x, y] != '0' && region.Contains(region.TopLeft + new Point2(x, y)))
							bg.Add((region.TopLeft + new Point2(x, y), motif.Tiles[x, y]));
			}
		}

		if (answers.Props.Count > 0)
		{
			var tiers = DecorTiers([.. answers.PropOdds.Select(p => 1 - p[0])], settings.PropAmount);
			for (var i = 0; i < answers.Floors.Count; i++)
				if (tiers[i] == 0)
				{
					var prop = answers.Props[ArgMax(answers.PropOdds[i][1..])];
					var tiles = answers.Floors[i].Tiles;
					var at = tiles[(int)(DecorNoise(tiles[0], seed + 17) * tiles.Count) % tiles.Count];
					actors.Add(Place(prop.Actor, new Point2(at.X, at.Y + 1) * Game.TileSize + prop.FloorOffset));
				}
		}

		return new(answers.Palette, settings.Material, fg, bg, actors);
	}

	static string LightDescription(DecorLight light)
	{
		var color = ActorSpecs.ReadString(light.Actor, nameof(Light.LightColor)) ?? "default color";
		var radius = light.Actor[nameof(Light.Radius)]?.ToString() ?? "default radius";
		return $"{ActorNote(light.Label)}, color {color}, radius {radius}; usually mounted: {light.Mount}";
	}

	// A preset (From) is placed as designed, without the overrides one room gave its instance.
	static JsonObject Place(JsonObject actor, Point2 position)
	{
		var copy = ActorSpecs.ReadString(actor, "From") is { } from ? new JsonObject { ["From"] = from } : actor.DeepClone().AsObject();
		copy.Remove("Id");
		copy["Position"] = ActorSpecs.ToJsonNode(position);
		ActorSpecs.EnsureActorId(copy);
		return copy;
	}

	// A wall or ceiling tile in the region, preferring a wall a little above a floor, kept clear of other lights.
	static Point2? LightAnchor(DecorTarget target, RectInt region, List<Point2> placed, int spacing)
	{
		bool Solid(int x, int y) => IsSolidAt(target.Solid, x, y);
		Point2? best = null;
		var bestScore = float.MinValue;
		for (var y = region.Top; y < region.Bottom; y++)
			for (var x = region.Left; x < region.Right; x++)
			{
				if (Solid(x, y) || placed.Any(p => Vector2.Distance(p, new Vector2(x, y)) < spacing))
					continue;
				var wall = Solid(x - 1, y) || Solid(x + 1, y);
				var ceiling = Solid(x, y - 1);
				if (!wall && !ceiling)
					continue;
				var floorBelow = Enumerable.Range(1, 4).Any(d => Solid(x, y + d));
				var score = (wall ? 2 : 1) + (floorBelow && !Solid(x, y + 1) ? 2 : 0) - Vector2.Distance(region.Center, new Vector2(x, y)) * 0.1f;
				if (score > bestScore)
					(best, bestScore) = (new(x, y), score);
			}
		return best;
	}

	// Smooth-ish per-tile noise in 0..1, so accents cluster instead of salt-and-peppering.
	static float DecorNoise(Point2 tile, int seed)
	{
		float Hash(int x, int y) => (Hash32.Mix((uint)(x * 73856093 ^ y * 19349663 ^ seed * 83492791)) & 0xffff) / 65535f;
		var fx = tile.X / 3f;
		var x0 = (int)MathF.Floor(fx);
		return Calc.Lerp(Hash(x0, tile.Y), Hash(x0 + 1, tile.Y), fx - x0) * 0.7f + Hash(tile.X, tile.Y) * 0.3f;
	}

	static int ArgMax(float[] values) => Array.IndexOf(values, values.Max());

	// Jev answers cautiously in absolute terms -- "none", "unlit" -- but ranks things sensibly against each other,
	// so decisions are made by rank: the first fraction of items (highest value first) gets tier 0, the next
	// tier 1, and so on; anything past the fractions gets tiers.Length.
	static int[] DecorTiers(float[] values, params float[] fractions)
	{
		var tiers = new int[values.Length];
		var order = Enumerable.Range(0, values.Length).OrderByDescending(i => values[i]).ToList();
		var start = 0;
		for (var t = 0; t <= fractions.Length; t++)
		{
			var end = t < fractions.Length ? start + (int)MathF.Round(fractions[t] * values.Length) : values.Length;
			for (var r = start; r < Math.Min(end, values.Length); r++)
				tiers[order[r]] = t;
			start = end;
		}
		return tiers;
	}

	// Batches questions to TypeSafe and records every answer in the report.
	sealed class DecorAsker(string apiKey, JsonObject state)
	{
		public void SetState(string key, string value) => state[key] = value;

		public async Task<int> Choice(string id, string instructions, List<string> options, StringBuilder report)
			=> ArgMax((await Choices([instructions], [options], id, report))[0]);

		// Each question's probability for every option, in order.
		public async Task<float[][]> Choices(List<string> instructions, List<List<string>> options, string prefix, StringBuilder report)
		{
			var questions = new List<(string, JsonObject)>();
			for (var i = 0; i < instructions.Count; i++)
			{
				var criteria = new JsonObject();
				for (var o = 0; o < options[i].Count && o < 255; o++)
					criteria[$"o{o}"] = options[i][o];
				questions.Add(($"{prefix}{i}", new JsonObject { ["type"] = "choice", ["instructions"] = instructions[i], ["criteria"] = criteria }));
			}
			var answers = await Ask(questions);
			var probabilities = new float[instructions.Count][];
			for (var i = 0; i < probabilities.Length; i++)
			{
				var answer = answers[$"{prefix}{i}"]!;
				probabilities[i] = new float[Math.Min(options[i].Count, 255)];
				foreach (var (key, value) in answer["probabilities"]!.AsObject())
					probabilities[i][int.Parse(key[1..])] = value!.GetValue<float>();
				report.AppendLine($"{prefix}{i}: {instructions[i]}\n  -> {options[i][ArgMax(probabilities[i])]} " +
					$"(p {probabilities[i].Max():0.00}, confidence {answer["confidence"]?.GetValue<float>():0.00})");
			}
			return probabilities;
		}

		// Each question's raw score, 0 to levels-1 (it can fall between levels).
		public async Task<float[]> Scores(List<string> instructions, string[] levels, string prefix, StringBuilder report)
		{
			var questions = instructions.Select((text, i) => ($"{prefix}{i}", new JsonObject
			{
				["type"] = "score",
				["instructions"] = text,
				["criteria"] = new JsonArray([.. levels.Select(l => (JsonNode)l)]),
			})).ToList();
			var answers = await Ask(questions);
			var scores = new float[instructions.Count];
			for (var i = 0; i < scores.Length; i++)
			{
				var answer = answers[$"{prefix}{i}"]!;
				scores[i] = answer["score"]!.GetValue<float>();
				report.AppendLine($"{prefix}{i}: {instructions[i]}\n  -> score {scores[i]:0.00} (confidence {answer["confidence"]?.GetValue<float>():0.00})");
			}
			return scores;
		}

		async Task<JsonObject> Ask(List<(string Id, JsonObject Question)> questions)
		{
			var answers = new JsonObject();
			for (var start = 0; start < questions.Count; start += DecorQuestionsPerRequest)
			{
				var batch = new JsonObject();
				foreach (var (id, question) in questions.Skip(start).Take(DecorQuestionsPerRequest))
					batch[id] = question;
				foreach (var (id, answer) in await AskTypeSafe(apiKey, state, batch))
					answers[id] = answer?.DeepClone();
			}
			return answers;
		}
	}

	static async Task<JsonObject> AskTypeSafe(string apiKey, JsonObject state, JsonObject questions)
	{
		var body = new JsonObject { ["state"] = state.DeepClone(), ["model"] = "jev-latest", ["questions"] = questions }.ToJsonString();
		for (var attempt = 0; ; attempt++)
		{
			using var request = new HttpRequestMessage(HttpMethod.Post, TypeSafeUrl) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
			request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
			using var response = await typeSafeHttp.SendAsync(request);
			var text = await response.Content.ReadAsStringAsync();
			if ((int)response.StatusCode is 429 or 529 && attempt < 4)
			{
				await Task.Delay(1000 << attempt);
				continue;
			}
			if (!response.IsSuccessStatusCode)
				throw new InvalidOperationException($"TypeSafe {(int)response.StatusCode}: {text}");
			return JsonNode.Parse(text)!["answers"]!.AsObject();
		}
	}

	// --- Applying (main thread) ---

	void ApplyDecoration(Room room, DecorPlan plan)
	{
		// Setting a tile clears its flags (slope, breakable, ...), which would change collision.
		var flags = new Room.TileFlags[room.Size.X, room.Size.Y];
		for (var y = 0; y < room.Size.Y; y++)
			for (var x = 0; x < room.Size.X; x++)
				flags[x, y] = room.GetTileFlags(new(x, y));

		if (plan.Material)
			for (var y = 0; y < room.Size.Y; y++)
				for (var x = 0; x < room.Size.X; x++)
				{
					var tile = new Point2(x, y);
					if (room.GetEntry(tile) is { BlocksMovement: true } &&
						(!room.IsSlope(tile) || Factory.Find(plan.Palette.Solid) is { SupportsSlope: true }))
						room.SetTile(Factory.TileLayer.Foreground, tile, plan.Palette.Solid, rebuildGeometry: false);
					room.SetTile(Factory.TileLayer.Background, tile, plan.Palette.Background, rebuildGeometry: false);
				}
		foreach (var (tile, ch) in plan.Bg)
			room.SetTile(Factory.TileLayer.Background, tile, ch, rebuildGeometry: false);
		foreach (var (tile, ch) in plan.Fg)
			if (room.Tiles[tile.X, tile.Y] == '0')
				room.SetTile(Factory.TileLayer.Foreground, tile, ch, rebuildGeometry: false);

		for (var y = 0; y < room.Size.Y; y++)
			for (var x = 0; x < room.Size.X; x++)
				room.SetTileFlags(new(x, y), flags[x, y]);
		room.Actors.AddRange(plan.Actors);
		room.RebuildGeometry();
	}
}
#endif
