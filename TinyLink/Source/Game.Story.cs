using System.Numerics;
using Foster.Framework;

namespace TinyLink;

public partial class Game
{
	private sealed class StorySpeechBubble(Vector2 position, string text, float duration)
	{
		public readonly Vector2 Position = position;
		public readonly string Text = text;
		public readonly float Duration = duration;
		public float Age;
	}

	private readonly record struct StoryEventContext(
		string? ActorType = null,
		string? SourceType = null,
		string? DoorId = null,
		Vector2? EventPosition = null,
		float? Duration = null);

	private const float StorySpeechMaxWidth = 120.0f;
	private const float StoryIdleSeconds = 10.0f;
	private const float StoryIdleMovementTolerance = 0.5f;
	private static int storyReloadVersion;
	private StoryDeck? budStorylets;
	private readonly Dictionary<string, int> storyRoomVisits = [];
	private readonly List<StorySpeechBubble> storySpeechBubbles = [];
	private readonly HashSet<Actor> storyPlayerHitEnemies = [];
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
		budStorylets = new StoryDeck { UseSpecificity = true };
		BuildStory(new StoryBuilder(budStorylets));

		var path = Path.Join(Assets.AssetsPath, "Storylets", "bud.json");
		if (!File.Exists(path))
			return;

		try
		{
			var jsonDeck = StoryletJson.DeckFromFile(path);
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
			bubble.Age += Time.Delta;
			if (bubble.Age >= bubble.Duration)
				storySpeechBubbles.RemoveAt(i);
		}
	}

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
		DisplayMessage("Reloaded story");
		Console.WriteLine("Reloaded story.");
	}

	internal void TryBudStorylet(string trigger)
		=> TryBudStorylet(trigger, default);

	private void TryBudStorylet(string trigger, StoryEventContext eventContext)
	{
		if (budStorylets == null || GetFirst<Bud>() is not { } bud || !CanShowBudSpeech())
			return;

		RefreshStoryContext(trigger, eventContext);
		var storylet = budStorylets.DrawAndPlaySingle(IsBudStorylet);
		if (storylet == null)
			return;

		if (storylet.Content.TryGetValue("text", out var text) && text is string line && !string.IsNullOrWhiteSpace(line))
			bud.Say(line);
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
		=> storySpeechBubbles.Count == 0;

	internal void ShowBudSpeech(Vector2 position, string text)
	{
		var duration = Math.Clamp(1.3f + text.Length * 0.045f, 1.8f, 4.5f);
		storySpeechBubbles.Add(new(ClampStorySpeechPosition(position, text), text, duration));
	}

	private void DrawStorySpeech(Target target)
	{
		if (storySpeechBubbles.Count == 0)
			return;

		var font = Assets.SpeechFont ?? Assets.Font;
		if (font == null)
			return;

		foreach (var bubble in storySpeechBubbles)
			DrawStorySpeechBubble(font, bubble);

		Batcher.Render(target);
		Batcher.Clear();
	}

	private void DrawStorySpeechBubble(SpriteFont font, StorySpeechBubble bubble)
	{
		var layout = MeasureStorySpeech(font, bubble.Text);
		if (layout.Lines.Count == 0)
			return;

		var anchor = (Point2)(bubble.Position - RenderCamera);
		var box = new Rect(
			anchor.X - layout.Width - layout.Padding.X,
			anchor.Y - layout.Lines.Count * layout.LineHeight - layout.Padding.Y,
			layout.Width + layout.Padding.X * 2,
			layout.Lines.Count * layout.LineHeight + layout.Padding.Y * 2);

		for (var i = 0; i < layout.Lines.Count; i++)
		{
			var lineWidth = MathF.Ceiling(font.WidthOf(layout.Lines[i]));
			var at = new Point2(
				(int)MathF.Round(box.Left + layout.Padding.X + (layout.Width - lineWidth) * 0.5f),
				(int)MathF.Round(box.Top + layout.Padding.Y + i * layout.LineHeight));
			Batcher.Text(font, layout.Lines[i], at, font.Size, Color.White);
		}
	}

	private Vector2 ClampStorySpeechPosition(Vector2 position, string text)
	{
		var font = Assets.SpeechFont ?? Assets.Font;
		if (font == null || room == null)
			return position;

		var layout = MeasureStorySpeech(font, text);
		if (layout.Lines.Count == 0)
			return position;

		var bounds = room.WorldBounds;
		var minX = bounds.Left + layout.Width + layout.Padding.X;
		var maxX = bounds.Right - layout.Padding.X;
		var minY = bounds.Top + layout.Lines.Count * layout.LineHeight + layout.Padding.Y;
		var maxY = bounds.Bottom - layout.Padding.Y;
		return new Vector2(
			Math.Clamp(position.X, minX, maxX),
			Math.Clamp(position.Y, minY, maxY));
	}

	private static StorySpeechLayout MeasureStorySpeech(SpriteFont font, string text)
	{
		var lines = WrapStorySpeech(font, text, StorySpeechMaxWidth);
		var lineHeight = MathF.Ceiling(font.LineHeight) + 1;
		var width = 0.0f;
		foreach (var line in lines)
			width = MathF.Max(width, MathF.Ceiling(font.WidthOf(line)));
		return new(lines, width, lineHeight, new Vector2(6, 5));
	}

	private readonly record struct StorySpeechLayout(List<string> Lines, float Width, float LineHeight, Vector2 Padding);

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
			_ => StoryTrigger.None
		};
	}

	private static string? StoryActorType(Actor? actor)
		=> actor?.GetType().Name;

	private static bool IsBudStorylet(Storylet storylet)
		=> storylet.Content.TryGetValue("speaker", out var speaker) &&
		   string.Equals(speaker?.ToString(), "bud", StringComparison.OrdinalIgnoreCase);
}
