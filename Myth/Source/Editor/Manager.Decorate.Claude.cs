#if EDITOR
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Anthropic;
using Foster.Framework;
using B = Anthropic.Models.Beta.Messages;

namespace Myth;

// The vision decider for the Decorate window: Claude looks at renders of nearby decorated rooms and of the
// graybox, and returns a whole decoration plan in tile coordinates, using only vocabulary mined from the
// world. Code places the plan with the same rules as the TypeSafe passes (collision locked, lights spaced,
// accents only in open tiles). Each critique round renders the result and asks for a revised plan.
public partial class Manager
{
	const string DecorClaudeModel = "claude-opus-5-5";
	const int DecorRenderMaxSize = 1024; // px on the long edge of each image sent

	sealed record DecorVocabulary(Dictionary<string, char> Solids, Dictionary<string, char> Backgrounds, Dictionary<string, char> Accents,
		Dictionary<string, char> BackgroundAccents, Dictionary<string, DecorLight> Lights, Dictionary<string, DecorProp> Props);
	sealed record DecorClaudeRequest(string ApiKey, string Prompt, List<(string Label, byte[] Png)> Images, JsonObject Schema);

	enum DecorDecider { TypeSafe, Claude }
	DecorDecider decorDecider;
	string decorDirection = "";
	int decorRounds = 1;
	int decorRoundsLeft;
	JsonObject? decorClaudePlan;
	DecorVocabulary? decorVocabulary;
	List<(string Label, byte[] Png)>? decorReferences;
	byte[]? decorGrayboxPng;
	Task<JsonObject>? decorClaudeTask;
	readonly StringBuilder decorClaudeReport = new();

	static string ReadAnthropicKey()
	{
		if (FindSourceFile(".env") is { } path)
			foreach (var line in File.ReadAllLines(path))
				if (line.StartsWith("ANTHROPIC_API_KEY=", StringComparison.Ordinal))
					return line["ANTHROPIC_API_KEY=".Length..].Trim().Trim('"');
		return Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")
			?? throw new InvalidOperationException("Add ANTHROPIC_API_KEY to Myth/.env (it's git-ignored).");
	}

	// Renders a room the way the game draws it, scaled to fit DecorRenderMaxSize, as PNG bytes.
	byte[] RenderRoomPng(Room room)
	{
		var bounds = room.WorldBounds;
		var scale = MathF.Min(2, DecorRenderMaxSize / (float)Math.Max(bounds.Width, bounds.Height));
		var (width, height) = ((int)MathF.Ceiling(bounds.Width * scale), (int)MathF.Ceiling(bounds.Height * scale));
		using var game = new Game(this, room, audioEventsEnabled: false, spawnBud: false, enableParticles: false, loadGoals: true);
		var target = new Target(GraphicsDevice, width, height, name: "DecorateRender");
		try
		{
			game.RenderWorldToTarget(target, new Rect(bounds.X, bounds.Y, bounds.Width, bounds.Height), zoom: scale);
			var pixels = new Color[width * height];
			target.Attachments[0].GetData<Color>(pixels);
			using var image = new Image(width, height, pixels);
			using var png = new MemoryStream();
			image.WritePng(png);
			return png.ToArray();
		}
		finally
		{
			target.Dispose();
		}
	}

	// Called from RebaseDecoration, while the room is still the graybox.
	void PrepareClaudeDecoration(Room room, DecorCorpus corpus)
	{
		decorClaudePlan = null;
		decorGrayboxPng = null;
		decorReferences = null;
		static Dictionary<string, char> Named(IEnumerable<char> chars)
			=> chars.Where(c => c != '0').Distinct().ToDictionary(TileName, c => c);
		decorVocabulary = new(
			Named(corpus.Palettes.Select(p => p.Solid)),
			Named(corpus.Palettes.Select(p => p.Background)),
			Named(corpus.Accents.Select(a => a.Tile)),
			Named(corpus.Motifs.SelectMany(m => m.Tiles.Cast<char>())),
			corpus.Lights.GroupBy(l => l.Label).ToDictionary(g => g.Key, g => g.First()),
			corpus.Props.GroupBy(p => p.Label).ToDictionary(g => g.Key, g => g.First()));
	}

	void StartClaudeAsk()
	{
		if (decorRoom is not { } room || decorTarget is not { } target || decorVocabulary is not { } vocab || decorClaudeTask != null)
			return;
		decorDecider = DecorDecider.Claude;
		decorClaudeReport.Clear();

		// Renders happen here on the main thread; the graybox is rendered from the snapshot.
		if (decorGrayboxPng == null)
		{
			var current = room.ToJsonString();
			room.LoadJsonState(decorSnapshot);
			decorGrayboxPng = RenderRoomPng(room);
			room.LoadJsonState(current);
		}
		decorReferences ??= [.. ClaudeReferenceRooms(room).Select(r => (ReferenceLabel(r), RenderRoomPng(r)))];
		SaveDecorRenders([.. decorReferences, ("graybox", decorGrayboxPng)]);
		if (TryReadKey(ReadAnthropicKey) is not { } apiKey)
			return;
		decorRoundsLeft = decorRounds;
		decorStatus = "Asking Claude for a plan...";
		var request = ClaudeRequest(apiKey, target, vocab, previousPlan: null);
		decorClaudeTask = Task.Run(() => AskClaude(request));
	}

	// A missing key shows up in the window instead of throwing out of a button press.
	string? TryReadKey(Func<string> read)
	{
		try
		{
			return read();
		}
		catch (InvalidOperationException e)
		{
			decorStatus = e.Message;
			return null;
		}
	}

	// The exact images Claude is shown, in obj/decorate-renders/, to see what it saw.
	static void SaveDecorRenders(List<(string Label, byte[] Png)> renders)
	{
		var folder = Path.Combine(Path.GetDirectoryName(FindSourceFile("Myth.csproj") ?? ".")!, "obj", "decorate-renders");
		Directory.CreateDirectory(folder);
		foreach (var (label, png) in renders)
			File.WriteAllBytes(Path.Combine(folder, $"{new string([.. label.Select(c => char.IsLetterOrDigit(c) ? c : '_')]).Trim('_')}.png"), png);
	}

	// Nearby rooms that are actually decorated: they show the model what this part of the world looks like.
	List<Room> ClaudeReferenceRooms(Room room)
	{
		var center = room.WorldBounds.Center;
		return [.. Assets.RoomsById.Values
			.Where(r => r != room && r.Sublayer == room.Sublayer && r.Actors.Count(a => ActorSpecs.GetTypeName(a) == nameof(Light)) >= 2)
			.OrderBy(r => System.Numerics.Vector2.DistanceSquared(r.WorldBounds.Center, center)).Take(3)];
	}

	string ReferenceLabel(Room room)
	{
		var (solid, bg) = RoomPalette(room);
		return $"Reference room '{room.Id}' (solid: {TileNote(solid)}; background: {TileNote(bg)})";
	}

	DecorClaudeRequest ClaudeRequest(string apiKey, DecorTarget target, DecorVocabulary vocab, JsonObject? previousPlan)
	{
		var map = new StringBuilder();
		var width = target.Solid.GetLength(0);
		map.Append("    ");
		for (var x = 0; x < width; x++)
			map.Append(x % 10 == 0 ? (char)('0' + x / 10 % 10) : ' ');
		map.Append("\n    ");
		for (var x = 0; x < width; x++)
			map.Append((char)('0' + x % 10));
		map.Append('\n');
		for (var y = 0; y < target.Solid.GetLength(1); y++)
		{
			map.Append($"{y,3} ");
			for (var x = 0; x < width; x++)
				map.Append(target.Solid[x, y] ? '#' : '.');
			map.Append('\n');
		}

		string List(IEnumerable<string> names, Func<string, string> note) => string.Join("\n", names.Select(n => $"- {note(n)}"));
		var prompt = new StringBuilder();
		prompt.AppendLine($"Decorate the graybox room '{target.Room.Id}' ({width} x {target.Solid.GetLength(1)} tiles).");
		prompt.AppendLine("""
			Coordinates are tiles: x grows right, y grows down, (0,0) is the top-left of the room. '#' is solid, '.' is open.
			Collision is final. Your plan chooses:
			- solid: the material every solid tile becomes; background: the wall behind the open space.
			- accents: runs of small decoration tiles in OPEN tiles, growing along a floor (the open tile right above solid), under a ceiling, or up a wall.
			  Give a start tile, a length, and whether the run is vertical.
			- background_accents: rectangles of background detail (panels, strips) painted over the wall.
			- lights: wall- or ceiling-mounted lights in OPEN tiles, at least the given spacing apart.
			- props: things standing on floors (give the open tile they stand in).
			Make it read like a finished, hand-made room in the same world as the references: a coherent look, light that leads the eye along
			the route and to danger, contrast between lit and dark, restraint over clutter. Never let decoration read as a hazard.
			Explain your intent briefly in `intent`.
			""");
		prompt.AppendLine($"\nArt direction from the designer: {(string.IsNullOrWhiteSpace(decorDirection) ? "(none -- use your judgement)" : decorDirection)}");
		prompt.AppendLine($"Light spacing: at least {decorSettings.LightSpacing} tiles between lights.");
		prompt.AppendLine($"\nGameplay in the room:\n{string.Join("\n", target.State["gameplay_actors"]!.AsArray().Select(a => $"- {a}"))}");
		prompt.AppendLine($"\nSolid materials:\n{List(vocab.Solids.Keys, n => TileNote(vocab.Solids[n]))}");
		prompt.AppendLine($"\nBackgrounds:\n{List(vocab.Backgrounds.Keys, n => TileNote(vocab.Backgrounds[n]))}");
		prompt.AppendLine($"\nAccent tiles:\n{List(vocab.Accents.Keys, n => TileNote(vocab.Accents[n]))}");
		prompt.AppendLine($"\nBackground accent tiles:\n{List(vocab.BackgroundAccents.Keys, n => TileNote(vocab.BackgroundAccents[n]))}");
		prompt.AppendLine($"\nLights:\n{List(vocab.Lights.Keys, ActorNote)}");
		prompt.AppendLine($"\nProps:\n{List(vocab.Props.Keys, ActorNote)}");
		prompt.AppendLine($"\nMap:\n{map}");

		var images = new List<(string, byte[])>(decorReferences ?? []) { ("The graybox room to decorate", decorGrayboxPng!) };
		if (previousPlan != null && decorRoom != null)
		{
			images.Add(("Your previous plan, as it renders now", RenderRoomPng(decorRoom)));
			SaveDecorRenders([($"round {decorRounds - decorRoundsLeft}", images[^1].Item2)]);
			prompt.AppendLine($"\nYour previous plan:\n{previousPlan.ToJsonString()}");
			prompt.AppendLine("Critique the render against the references and the art direction, then return a complete, improved plan.");
		}

		JsonObject Enum(IEnumerable<string> names) => new() { ["type"] = "string", ["enum"] = new JsonArray([.. names.Select(n => (JsonNode)n)]) };
		JsonObject Item(params (string Name, JsonObject Type)[] fields) => new()
		{
			["type"] = "object",
			["additionalProperties"] = false,
			["properties"] = new JsonObject(fields.Select(f => KeyValuePair.Create(f.Name, (JsonNode?)f.Type))),
			["required"] = new JsonArray([.. fields.Select(f => (JsonNode)f.Name)]),
		};
		JsonObject Int() => new() { ["type"] = "integer" };
		JsonObject ArrayOf(JsonObject item) => new() { ["type"] = "array", ["items"] = item };
		var schema = Item(
			("intent", new JsonObject { ["type"] = "string" }),
			("solid", Enum(vocab.Solids.Keys)),
			("background", Enum(vocab.Backgrounds.Keys)),
			("accents", ArrayOf(Item(("tile", Enum(vocab.Accents.Keys)), ("x", Int()), ("y", Int()), ("length", Int()), ("vertical", new JsonObject { ["type"] = "boolean" })))),
			("background_accents", ArrayOf(Item(("tile", Enum(vocab.BackgroundAccents.Keys)), ("x", Int()), ("y", Int()), ("width", Int()), ("height", Int())))),
			("lights", ArrayOf(Item(("kind", Enum(vocab.Lights.Keys)), ("x", Int()), ("y", Int())))),
			("props", ArrayOf(Item(("kind", Enum(vocab.Props.Keys)), ("x", Int()), ("y", Int())))));
		return new(apiKey, prompt.ToString(), images, schema);
	}

	static async Task<JsonObject> AskClaude(DecorClaudeRequest request)
	{
		var client = new AnthropicClient { ApiKey = request.ApiKey };
		var content = new List<B.BetaContentBlockParam>();
		foreach (var (label, png) in request.Images)
		{
			content.Add(new B.BetaTextBlockParam { Text = label });
			content.Add(new B.BetaImageBlockParam { Source = new B.BetaBase64ImageSource { Data = Convert.ToBase64String(png), MediaType = B.MediaType.ImagePng } });
		}
		content.Add(new B.BetaTextBlockParam { Text = request.Prompt });

		var parameters = new B.MessageCreateParams
		{
			Model = DecorClaudeModel,
			MaxTokens = 64000,
			Betas = ["server-side-fallback-2026-07-01"],
			Fallbacks = new B.Default(),
			System = "You are the level artist for Human Questions, a 2D pixel-art platformer with 8px tiles. You decorate graybox rooms: their " +
				"collision is final, you choose how they look.",
			OutputConfig = new B.BetaOutputConfig
			{
				Effort = B.Effort.High,
				Format = new B.BetaJsonOutputFormat { Schema = request.Schema.ToDictionary(p => p.Key, p => JsonSerializer.SerializeToElement(p.Value)) },
			},
			Messages = [new B.BetaMessageParam { Role = B.Role.User, Content = content }],
		};

		var text = new StringBuilder();
		string? stopReason = null;
		await foreach (var streamEvent in client.Beta.Messages.CreateStreaming(parameters))
		{
			if (streamEvent.TryPickContentBlockDelta(out var delta) && delta.Delta.TryPickText(out var chunk))
				text.Append(chunk.Text);
			else if (streamEvent.TryPickDelta(out var messageDelta) && messageDelta.Delta.StopReason is { } reason)
				stopReason = reason.ToString();
		}
		if (stopReason is "refusal")
			throw new InvalidOperationException("Claude declined to plan this room.");
		if (stopReason is "max_tokens")
			throw new InvalidOperationException("Claude's plan ran past max_tokens.");
		return JsonNode.Parse(text.ToString())!.AsObject();
	}

	// Called every editor frame alongside the TypeSafe pickup: places each plan, then chains critique rounds.
	void UpdateClaudeDecoration()
	{
		if (decorClaudeTask is not { IsCompleted: true } task)
			return;
		decorClaudeTask = null;
		if (task.IsFaulted)
		{
			decorStatus = $"Claude failed: {task.Exception?.GetBaseException().Message}";
			return;
		}
		decorClaudePlan = task.Result;
		var round = decorRounds - decorRoundsLeft + 1;
		decorClaudeReport.AppendLine($"Round {round}: {decorClaudePlan["intent"]}\n{decorClaudePlan.ToJsonString()}\n");
		ReapplyDecoration();
		if (decorRoundsLeft-- > 0 && decorTarget is { } target && decorVocabulary is { } vocab)
		{
			decorStatus = $"Round {round} placed. Asking Claude to critique it...";
			var request = ClaudeRequest(ReadAnthropicKey(), target, vocab, decorClaudePlan);
			decorClaudeTask = Task.Run(() => AskClaude(request));
		}
		else
			decorStatus = $"Claude: {decorClaudePlan["intent"]}";
	}

	// Turns the plan into tiles and actors, dropping anything that breaks the rules.
	DecorPlan PlaceClaudePlan(JsonObject plan, DecorTarget target, DecorVocabulary vocab, DecorSettings settings)
	{
		var (w, h) = (target.Solid.GetLength(0), target.Solid.GetLength(1));
		bool Open(int x, int y) => x >= 0 && y >= 0 && x < w && y < h && !target.Solid[x, y];
		static string Str(JsonNode? node) => node?.GetValue<string>() ?? "";
		static int Num(JsonNode? node) => node?.GetValue<int>() ?? 0;

		var solid = vocab.Solids.GetValueOrDefault(Str(plan["solid"]), target.Room.Tiles[0, 0]);
		var background = vocab.Backgrounds.GetValueOrDefault(Str(plan["background"]), '0');
		var palette = new DecorPalette($"{solid}{background}", solid, background, [], "");

		var fg = new List<(Point2, char)>();
		foreach (var accent in plan["accents"]?.AsArray() ?? [])
			if (vocab.Accents.TryGetValue(Str(accent?["tile"]), out var tile))
				for (var i = 0; i < Math.Min(Num(accent?["length"]), 64); i++)
				{
					var at = new Point2(Num(accent?["x"]), Num(accent?["y"])) + (accent?["vertical"]?.GetValue<bool>() == true ? new Point2(0, i) : new Point2(i, 0));
					if (Open(at.X, at.Y))
						fg.Add((at, tile));
				}

		var bg = new List<(Point2, char)>();
		foreach (var rect in plan["background_accents"]?.AsArray() ?? [])
			if (vocab.BackgroundAccents.TryGetValue(Str(rect?["tile"]), out var tile))
				for (var y = 0; y < Math.Min(Num(rect?["height"]), 32); y++)
					for (var x = 0; x < Math.Min(Num(rect?["width"]), 32); x++)
					{
						var at = new Point2(Num(rect?["x"]) + x, Num(rect?["y"]) + y);
						if (at.X >= 0 && at.Y >= 0 && at.X < w && at.Y < h)
							bg.Add((at, tile));
					}

		var actors = new List<JsonObject>();
		var placed = new List<Point2>(target.Lights);
		foreach (var light in plan["lights"]?.AsArray() ?? [])
		{
			var at = new Point2(Num(light?["x"]), Num(light?["y"]));
			if (!vocab.Lights.TryGetValue(Str(light?["kind"]), out var kind) || !Open(at.X, at.Y) ||
				placed.Any(p => System.Numerics.Vector2.Distance(p, at) < settings.LightSpacing))
				continue;
			placed.Add(at);
			actors.Add(Place(kind.Actor, at * Game.TileSize + new Point2(Game.TileSize / 2, Game.TileSize / 2)));
		}
		foreach (var prop in plan["props"]?.AsArray() ?? [])
		{
			if (!vocab.Props.TryGetValue(Str(prop?["kind"]), out var kind))
				continue;
			var at = new Point2(Num(prop?["x"]), Num(prop?["y"]));
			for (var down = 0; down <= 3; down++)
				if (Open(at.X, at.Y + down) && !Open(at.X, at.Y + down + 1) && at.Y + down + 1 < h)
				{
					actors.Add(Place(kind.Actor, new Point2(at.X, at.Y + down + 1) * Game.TileSize + kind.FloorOffset));
					break;
				}
		}
		return new(palette, settings.Material, fg, bg, actors);
	}
}
#endif
