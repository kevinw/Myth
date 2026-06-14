namespace Myth;

public sealed class Storylet
{
	public const int RedrawAlways = 0;
	public const int RedrawNever = -1;

	public string Id { get; }
	public string? Condition { get; set; }
	public object? Priority { get; set; }
	public int? FixedPriority { get; set; }
	public int Redraw { get; set; } = RedrawAlways;

	// Probability in [0,1] that this storylet is eligible on a given draw. Storylets sharing
	// a ChanceGroupId roll once per draw and pass/fail together (so a multi-line bark set
	// plays as a unit at the given chance, rather than each line rolling independently).
	public double Chance { get; set; } = 1.0;
	public string? ChanceGroupId { get; set; }

	public Dictionary<string, object?> Content { get; set; } = [];
	public Dictionary<string, Dictionary<string, object?>> Outcomes { get; set; } = [];
	public Func<StoryContext, bool>? Predicate { get; set; }

	internal int NextPlay { get; set; }
	internal StoryDeck? Deck { get; set; }

	public Storylet(string id)
	{
		Id = id;
	}

	public bool CanDraw(int currentPlay)
	{
		if (Redraw == RedrawNever && NextPlay < 0)
			return false;
		return Redraw == RedrawAlways || currentPlay >= NextPlay;
	}

	public bool CheckCondition(Dictionary<string, object?> context, StoryContext typedContext)
		=> StoryExpression.EvalBool(Condition, context) && (Predicate == null || Predicate(typedContext));

	public int CurrentPriority(Dictionary<string, object?> context, bool useSpecificity)
	{
		var value = FixedPriority ?? (Priority == null ? 0 : Convert.ToInt32(StoryExpression.Eval(Priority, context)));
		return useSpecificity ? value * 100 + StoryExpression.Specificity(Condition) : value;
	}

	public void Play(string outcome = "default")
		=> Deck?.Play(this, outcome);
}

public sealed class StoryDeck
{
	private readonly Dictionary<string, Storylet> storylets = [];
	private readonly Random random = new();
	private int currentPlay;

	public Dictionary<string, object?> Context { get; } = [];
	public IEnumerable<Storylet> Storylets => storylets.Values;
	public StoryContext TypedContext { get; set; }
	public bool UseSpecificity { get; set; }

	public void Add(Storylet storylet)
	{
		if (storylets.ContainsKey(storylet.Id))
			throw new ArgumentException($"Duplicate storylet id '{storylet.Id}'.");
		storylets[storylet.Id] = storylet;
		storylet.Deck = this;
	}

	public Storylet? DrawSingle(Func<Storylet, bool>? filter = null)
		=> Draw(1, filter).FirstOrDefault();

	public Storylet? DrawAndPlaySingle(Func<Storylet, bool>? filter = null, string outcome = "default")
	{
		var storylet = DrawSingle(filter);
		if (storylet != null)
			Play(storylet, outcome);
		return storylet;
	}

	public List<Storylet> Draw(int count = -1, Func<Storylet, bool>? filter = null)
	{
		var buckets = new SortedDictionary<int, List<Storylet>>(Comparer<int>.Create((a, b) => b.CompareTo(a)));
		var groupRolls = new Dictionary<string, bool>();
		foreach (var storylet in storylets.Values)
		{
				if (!storylet.CanDraw(currentPlay) || filter != null && !filter(storylet) || !storylet.CheckCondition(Context, TypedContext))
					continue;

				// Roll chance once per draw, shared by all storylets in the same chance group.
				if (storylet.Chance < 1.0)
				{
					var key = storylet.ChanceGroupId ?? storylet.Id;
					if (!groupRolls.TryGetValue(key, out var passed))
						groupRolls[key] = passed = random.NextDouble() < storylet.Chance;
					if (!passed)
						continue;
				}

			var priority = storylet.CurrentPriority(Context, UseSpecificity);
			if (!buckets.TryGetValue(priority, out var bucket))
				buckets[priority] = bucket = [];
			bucket.Add(storylet);
		}

		var drawPile = new List<Storylet>();
		foreach (var bucket in buckets.Values)
		{
			Shuffle(bucket);
			drawPile.AddRange(bucket);
			if (count >= 0 && drawPile.Count >= count)
				break;
		}
		return count >= 0 ? drawPile.Take(count).ToList() : drawPile;
	}

	public void Play(Storylet storylet, string outcome = "default")
	{
		currentPlay++;
		storylet.NextPlay = storylet.Redraw == Storylet.RedrawNever ? -1 : currentPlay + storylet.Redraw;
		if (!storylet.Outcomes.TryGetValue(outcome, out var updates))
			return;

		foreach (var (key, value) in updates)
			Context[key] = StoryExpression.Eval(value, Context);
	}

	private void Shuffle<T>(List<T> list)
	{
		for (var i = list.Count - 1; i > 0; i--)
		{
			var j = random.Next(i + 1);
			(list[i], list[j]) = (list[j], list[i]);
		}
	}
}
