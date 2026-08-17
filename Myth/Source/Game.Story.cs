using System.Numerics;
using Foster.Framework;
using OutLoud;

namespace Myth;

public partial class Game
{
	// The line is pinned to where it was spoken: the speaker walks on, the words stay put.
	internal sealed class StorySpeechBubble(Vector2 position, string text, Color color, float duration, Utterance utterance, ISyllableSink sink)
	{
		public readonly Vector2 Position = position;
		public readonly string Text = text;
		public readonly Color Color = color; // whose voice this is, at a glance
		public readonly float Duration = duration;
		public float Age;
		public int VisibleLength;
		public bool Finished;

		public void Tick(float dt)
		{
			Age += dt;
			VisibleLength = utterance.Advance(Age, sink);
		}
	}

	private readonly record struct StoryEventContext(
		string? ActorType = null,
		string? SourceType = null,
		string? DoorId = null,
		Vector2? EventPosition = null,
		float? Duration = null);

	private const float StorySpeechMaxWidth = 120.0f;
	private const int StorySpeechRoomSlack = 24; // a line spoken at the room's lip still counts as inside
	private const float StoryIdleSeconds = 10.0f;
	private const float StoryIdleMovementTolerance = 0.5f;
	private static int storyReloadVersion;
	private StoryDeck? budStorylets;
	private readonly Dictionary<string, int> storyRoomVisits = [];
	internal readonly List<StorySpeechBubble> storySpeechBubbles = [];
	private readonly HashSet<Actor> storyPlayerHitEnemies = [];
	private BudStorySequence? activeBudStorySequence;
	private float storyRoomTime;
	private float storyIdleTime;
	private bool storyIdleTriggered;
	private Vector2? storyLastPlayerPosition;
	private int loadedStoryReloadVersion;

	internal static void RequestStoryReload()
		=> Interlocked.Increment(ref storyReloadVersion);

	private void LoadStory()
	{
		loadedStoryReloadVersion = storyReloadVersion;
		LoadVoice();
		budStorylets = new StoryDeck { UseSpecificity = true };
		BuildStory(new StoryBuilder(budStorylets));

		const string budStoryletsKey = "Storylets/bud.json";
		if (!Assets.Content.FileExists(budStoryletsKey))
			return;

		try
		{
			var jsonDeck = StoryletJson.DeckFromText(Assets.Content.ReadAllText(budStoryletsKey));
			foreach (var (key, value) in jsonDeck.Context)
				budStorylets.Context[key] = value;
			foreach (var storylet in jsonDeck.Storylets)
				budStorylets.Add(storylet);
		}
		catch (Exception e)
		{
			Console.Error.WriteLine($"Failed to load bud storylets: {e}");
		}
	}

	private void UpdateStory()
	{
		if (loadedStoryReloadVersion != storyReloadVersion)
			ReloadStory();

		storyRoomTime += Time.Delta;
		UpdateStoryIdle();
		for (var i = storySpeechBubbles.Count - 1; i >= 0; i--)
		{
			var bubble = storySpeechBubbles[i];
			bubble.Tick(Time.Delta);
			if (bubble.Age >= bubble.Duration || !IsSpeechInCurrentRoom(bubble))
			{
				bubble.Finished = true;
				storySpeechBubbles.RemoveAt(i);
			}
		}
	}

	// A pinned line belongs to the spot it was spoken in: leaving that room ends it, rather
	// than parking it against the edge of the screen for the rest of its duration.
	private bool IsSpeechInCurrentRoom(StorySpeechBubble bubble)
		=> room == null || room.WorldBounds.Inflate(StorySpeechRoomSlack).Contains(bubble.Position);

	private void OnStoryRoomChanged(RoomChange change)
	{
		storyRoomTime = 0;
		ResetStoryIdle();
		var roomId = change.To.Id;
		storyRoomVisits.TryGetValue(roomId, out var visits);
		storyRoomVisits[roomId] = visits + 1;

		if (change.Kind == RoomChangeKind.InitialLoad)
			TryBudStorylet("room_entered");
	}

	private void ReloadStory()
	{
		LoadStory();
		Console.WriteLine("Reloaded story.");
	}

	internal void TryBudStorylet(string trigger)
		=> TryBudStorylet(trigger, default);

	private void TryBudStorylet(string trigger, StoryEventContext eventContext)
	{
		if (ActiveCutscene != null || budStorylets == null || GetFirst<Bud>() is not { } bud || !CanShowBudSpeech())
			return;

		RefreshStoryContext(trigger, eventContext);
		var storylet = budStorylets.DrawAndPlaySingle(IsBudStorylet);
		if (storylet == null)
			return;

		if (storylet.Content.TryGetValue("text", out var text) && text is string line && !string.IsNullOrWhiteSpace(line))
			bud.Say(line, StoryEmotion(storylet));
	}

	internal void NotifyPlayerEnteredRevealDoor(string doorId, Vector2 position)
		=> TryBudStorylet("player_entered_reveal_door", new(DoorId: doorId, EventPosition: position));

	internal void NotifyPlayerHurt(Actor? source, Vector2 position)
		=> TryBudStorylet("player_hurt", new(SourceType: StoryActorType(source), EventPosition: position));

	internal void NotifyEnemyHitByPlayer(Actor enemy)
	{
		if (enemy.Mask.Has(Actor.Masks.Enemy))
			storyPlayerHitEnemies.Add(enemy);
	}

	internal void NotifyEnemyKilled(Actor enemy)
		=> TryBudStorylet("enemy_killed", new(ActorType: StoryActorType(enemy), EventPosition: enemy.Position));

	private void NotifyEnemyKilledIfPlayerCaused(Actor enemy)
	{
		if (!storyPlayerHitEnemies.Remove(enemy))
			return;
		NotifyEnemyKilled(enemy);
	}

	internal bool CanShowBudSpeech()
		=> activeBudStorySequence == null && CanShowSpeech();

	internal bool BeginBudStorySequence(BudStorySequence sequence, bool interrupt)
	{
		if (activeBudStorySequence == sequence)
			return true;
		if (activeBudStorySequence != null)
		{
			if (!interrupt)
				return false;
			activeBudStorySequence.Cancel();
		}
		activeBudStorySequence = sequence;
		return true;
	}

	internal void EndBudStorySequence(BudStorySequence sequence)
	{
		if (activeBudStorySequence == sequence)
			activeBudStorySequence = null;
	}

	internal void CancelSpeech(StorySpeechBubble? speech)
	{
		if (speech == null)
			return;
		speech.Finished = true;
		storySpeechBubbles.Remove(speech);
	}

	internal bool CanShowSpeech()
		=> storySpeechBubbles.Count == 0;

	internal StorySpeechBubble? ShowSpeech(Actor speaker, Vector2 offset, string text, VoiceProfile profile,
		Color color, Emotion emotion = Emotion.Neutral, string? voice = null)
	{
		if (!Game.VoiceEnabled) return null;
		text = NormalizeSpeech(text);
		var utterance = Utterance.Build(text, emotion, profile, GermanFor(text));
		var duration = utterance.RevealSeconds + Math.Clamp(text.Length * 0.04f, 1.2f, 3f);
		// Snapshot where the speaker stood: both the bubble and the voice stay at that spot
		// for the whole line rather than trailing the speaker around.
		var origin = (Vector2)speaker.Position + offset;
		var bubble = new StorySpeechBubble(origin, text, color, duration, utterance, CreateVoiceSink(origin, voice ?? BudVoiceName));
		storySpeechBubbles.Add(bubble);
		return bubble;
	}

	// Canonical form so the reveal index, wrap layout, utterance, and German lookup key all
	// measure the same string (see DrawStorySpeechBubble).
	internal static string NormalizeSpeech(string text)
		=> string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

	internal static StorySpeechLayout MeasureStorySpeech(SpriteFont font, string text)
	{
		var lines = WrapStorySpeech(font, text, StorySpeechMaxWidth);
		var lineHeight = MathF.Ceiling(font.LineHeight) + 1;
		var width = 0.0f;
		foreach (var line in lines)
			width = MathF.Max(width, MathF.Ceiling(font.WidthOf(line)));
		return new(lines, width, lineHeight, new Vector2(6, 5));
	}

	internal readonly record struct StorySpeechLayout(List<string> Lines, float Width, float LineHeight, Vector2 Padding);

	private static List<string> WrapStorySpeech(SpriteFont font, string text, float maxWidth)
	{
		var lines = new List<string>();
		var current = "";
		foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
		{
			var test = current.Length == 0 ? word : $"{current} {word}";
			if (current.Length > 0 && font.WidthOf(test) > maxWidth)
			{
				lines.Add(current);
				current = word;
			}
			else
			{
				current = test;
			}
		}
		if (current.Length > 0)
			lines.Add(current);
		return lines;
	}

	private void UpdateStoryIdle()
	{
		if (GetFirst<Player>() is not { } player)
		{
			ResetStoryIdle();
			return;
		}

		var position = (Vector2)player.Position;
		if (storyLastPlayerPosition is not { } previous)
		{
			storyLastPlayerPosition = position;
			return;
		}

		var moved = Vector2.DistanceSquared(position, previous) > StoryIdleMovementTolerance * StoryIdleMovementTolerance;
		var input = Controls.Move.IntValue != Point2.Zero ||
			Controls.Jump.Down ||
			Controls.Dash.Pressed ||
			Controls.Attack.Down ||
			Controls.BlinkStrike.Down ||
			Controls.Saw.Down;

		storyLastPlayerPosition = position;
		if (moved || input)
		{
			storyIdleTime = 0;
			storyIdleTriggered = false;
			return;
		}

		storyIdleTime += Time.Delta;
		if (!storyIdleTriggered && storyIdleTime >= StoryIdleSeconds)
		{
			storyIdleTriggered = true;
			TryBudStorylet("player_idle", new(EventPosition: position, Duration: storyIdleTime));
		}
	}

	private void ResetStoryIdle()
	{
		storyIdleTime = 0;
		storyIdleTriggered = false;
		storyLastPlayerPosition = GetFirst<Player>()?.Position;
	}

	private void RefreshStoryContext(string trigger, StoryEventContext eventContext)
	{
		if (budStorylets == null)
			return;

		var typedTrigger = ParseStoryTrigger(trigger);
		var roomId = room?.Id ?? "";
		var roomVisits = room != null && storyRoomVisits.TryGetValue(room.Id, out var visits) ? visits : 0;
		var hasBud = GetFirst<Bud>() != null;
		var playerHealth = GetFirst<Player>() is { } player ? player.Health : 0;
		var budDistance = GetFirst<Player>() is { } p && GetFirst<Bud>() is { } c
			? Vector2.Distance(p.Position, c.Position)
			: 0;

		budStorylets.TypedContext = new(
			Trigger: typedTrigger,
			WorldId: Assets.CurrentWorldId,
			RoomId: roomId,
			RoomTime: storyRoomTime,
			RoomVisits: roomVisits,
			RoomsSeen: storyRoomVisits.Count,
			HasBud: hasBud,
			PlayerHealth: playerHealth,
			BudDistance: budDistance,
			ActorType: eventContext.ActorType,
			SourceType: eventContext.SourceType,
			DoorId: eventContext.DoorId,
			EventPosition: eventContext.EventPosition,
			Duration: eventContext.Duration);

		var context = budStorylets.Context;
		context["speaker"] = "bud";
		context["trigger"] = trigger;
		context["world_id"] = Assets.CurrentWorldId;
		context["room_id"] = roomId;
		context["room_time"] = storyRoomTime;
		context["rooms_seen"] = storyRoomVisits.Count;
		context["room_visits"] = roomVisits;
		context["has_bud"] = hasBud;
		context["player_health"] = playerHealth;
		context["bud_distance"] = budDistance;
		context["actor_type"] = eventContext.ActorType;
		context["source_type"] = eventContext.SourceType;
		context["door_id"] = eventContext.DoorId;
		context["event_x"] = eventContext.EventPosition?.X;
		context["event_y"] = eventContext.EventPosition?.Y;
		context["event_duration"] = eventContext.Duration;
	}

	private static StoryTrigger ParseStoryTrigger(string trigger)
	{
		return trigger switch
		{
			"room_entered" => StoryTrigger.RoomEntered,
			"bud_entered" => StoryTrigger.BudEntered,
			"player_entered_reveal_door" => StoryTrigger.PlayerEnteredRevealDoor,
			"player_hurt" => StoryTrigger.PlayerHurt,
			"enemy_killed" => StoryTrigger.EnemyKilled,
			"player_idle" => StoryTrigger.PlayerIdle,
			"bud_struck" => StoryTrigger.BudStruck,
			_ => StoryTrigger.None
		};
	}

	private static string? StoryActorType(Actor? actor)
		=> actor?.GetType().Name;

	private static Emotion StoryEmotion(Storylet storylet)
		=> storylet.Content.TryGetValue("emotion", out var v) && v is string s && Enum.TryParse<Emotion>(s, true, out var e)
			? e
			: Emotion.Neutral;

	private static bool IsBudStorylet(Storylet storylet)
		=> storylet.Content.TryGetValue("speaker", out var speaker) &&
		   string.Equals(speaker?.ToString(), "bud", StringComparison.OrdinalIgnoreCase);
}
