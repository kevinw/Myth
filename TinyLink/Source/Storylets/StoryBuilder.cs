using System.Numerics;

namespace TinyLink;

public enum StoryTrigger
{
	None,
	RoomEntered,
	BudEntered,
	PlayerEnteredRevealDoor,
	PlayerHurt,
	EnemyKilled,
	PlayerIdle
}

public readonly record struct StoryContext(
	StoryTrigger Trigger,
	string WorldId,
	string RoomId,
	float RoomTime,
	int RoomVisits,
	int RoomsSeen,
	bool HasBud,
	int PlayerHealth,
	float BudDistance,
	string? ActorType,
	string? SourceType,
	string? DoorId,
	Vector2? EventPosition,
	float? Duration);

public sealed class StoryBuilder
{
	private readonly StoryDeck deck;

	public StoryBuilder(StoryDeck deck)
	{
		this.deck = deck;
	}

	public StoryBuilder Bud(string id, Action<StoryletBuilder> configure)
	{
		var builder = new StoryletBuilder(id)
			.Speaker("bud");
		configure(builder);
		deck.Add(builder.Build());
		return this;
	}

	public StoryBuilder BudBarks(string idPrefix, Action<StoryletGroupBuilder> configure)
	{
		var builder = new StoryletGroupBuilder();
		configure(builder);

		var lines = builder.Lines;
		for (var i = 0; i < lines.Count; i++)
		{
			var line = lines[i];
			Bud($"{idPrefix}_{i}", storylet =>
			{
				builder.Apply(storylet);
				storylet.Say(line);
			});
		}
		return this;
	}
}

public sealed class StoryletBuilder
{
	private readonly Storylet storylet;
	private Func<StoryContext, bool>? predicate;

	public StoryletBuilder(string id)
	{
		storylet = new Storylet(id);
	}

	public StoryletBuilder Speaker(string speaker)
	{
		storylet.Content["speaker"] = speaker;
		return this;
	}

	public StoryletBuilder Say(string text)
	{
		storylet.Content["text"] = text;
		return this;
	}

	public StoryletBuilder On(StoryTrigger trigger)
		=> When(ctx => ctx.Trigger == trigger);

	public StoryletBuilder OnEnemyKilled(string? actorType = null)
	{
		On(StoryTrigger.EnemyKilled);
		if (!string.IsNullOrWhiteSpace(actorType))
			When(ctx => string.Equals(ctx.ActorType, actorType, StringComparison.OrdinalIgnoreCase));
		return this;
	}

	public StoryletBuilder OnPlayerHurt(string? sourceType = null)
	{
		On(StoryTrigger.PlayerHurt);
		if (!string.IsNullOrWhiteSpace(sourceType))
			When(ctx => string.Equals(ctx.SourceType, sourceType, StringComparison.OrdinalIgnoreCase));
		return this;
	}

	public StoryletBuilder OnRevealDoorEntered(string? doorId = null)
	{
		On(StoryTrigger.PlayerEnteredRevealDoor);
		if (!string.IsNullOrWhiteSpace(doorId))
			When(ctx => string.Equals(ctx.DoorId, doorId, StringComparison.OrdinalIgnoreCase));
		return this;
	}

	public StoryletBuilder OnPlayerIdle(float seconds = 10.0f)
		=> On(StoryTrigger.PlayerIdle).When(ctx => ctx.Duration >= seconds);

	public StoryletBuilder InRoom(string roomId)
		=> When(ctx => string.Equals(ctx.RoomId, roomId, StringComparison.OrdinalIgnoreCase));

	public StoryletBuilder InWorld(string worldId)
		=> When(ctx => string.Equals(ctx.WorldId, worldId, StringComparison.OrdinalIgnoreCase));

	public StoryletBuilder When(Func<StoryContext, bool> condition)
	{
		var previous = predicate;
		predicate = previous == null
			? condition
			: ctx => previous(ctx) && condition(ctx);
		return this;
	}

	public StoryletBuilder Once()
	{
		storylet.Redraw = Storylet.RedrawNever;
		return this;
	}

	public StoryletBuilder Redraw(int draws)
	{
		storylet.Redraw = draws;
		return this;
	}

	public StoryletBuilder Always()
	{
		storylet.Redraw = Storylet.RedrawAlways;
		return this;
	}

	public StoryletBuilder Priority(int priority)
	{
		storylet.FixedPriority = priority;
		return this;
	}

	public Storylet Build()
	{
		storylet.Predicate = predicate;
		return storylet;
	}
}

public sealed class StoryletGroupBuilder
{
	private readonly List<Action<StoryletBuilder>> configurators = [];
	private readonly List<string> lines = [];
	public IReadOnlyList<string> Lines => lines;

	public StoryletGroupBuilder On(StoryTrigger trigger)
	{
		configurators.Add(builder => builder.On(trigger));
		return this;
	}

	public StoryletGroupBuilder OnEnemyKilled(string? actorType = null)
	{
		configurators.Add(builder => builder.OnEnemyKilled(actorType));
		return this;
	}

	public StoryletGroupBuilder OnPlayerHurt(string? sourceType = null)
	{
		configurators.Add(builder => builder.OnPlayerHurt(sourceType));
		return this;
	}

	public StoryletGroupBuilder OnRevealDoorEntered(string? doorId = null)
	{
		configurators.Add(builder => builder.OnRevealDoorEntered(doorId));
		return this;
	}

	public StoryletGroupBuilder OnPlayerIdle(float seconds = 10.0f)
	{
		configurators.Add(builder => builder.OnPlayerIdle(seconds));
		return this;
	}

	public StoryletGroupBuilder InRoom(string roomId)
	{
		configurators.Add(builder => builder.InRoom(roomId));
		return this;
	}

	public StoryletGroupBuilder InWorld(string worldId)
	{
		configurators.Add(builder => builder.InWorld(worldId));
		return this;
	}

	public StoryletGroupBuilder When(Func<StoryContext, bool> condition)
	{
		configurators.Add(builder => builder.When(condition));
		return this;
	}

	public StoryletGroupBuilder Once()
	{
		configurators.Add(builder => builder.Once());
		return this;
	}

	public StoryletGroupBuilder Redraw(int draws)
	{
		configurators.Add(builder => builder.Redraw(draws));
		return this;
	}

	public StoryletGroupBuilder Always()
	{
		configurators.Add(builder => builder.Always());
		return this;
	}

	public StoryletGroupBuilder Priority(int priority)
	{
		configurators.Add(builder => builder.Priority(priority));
		return this;
	}

	public StoryletGroupBuilder Say(params string[] text)
	{
		lines.AddRange(text.Where(it => !string.IsNullOrWhiteSpace(it)));
		return this;
	}

	internal void Apply(StoryletBuilder builder)
	{
		foreach (var configurator in configurators)
			configurator(builder);
	}
}
