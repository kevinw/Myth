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

	private const float StorySpeechMaxWidth = 120.0f;
	private static int storyReloadVersion;
	private StoryDeck? companionStorylets;
	private readonly Dictionary<string, int> storyRoomVisits = [];
	private readonly List<StorySpeechBubble> storySpeechBubbles = [];
	private float storyRoomTime;
	private int loadedStoryReloadVersion;

	internal static void RequestStoryReload()
		=> Interlocked.Increment(ref storyReloadVersion);

	private void LoadStory()
	{
		loadedStoryReloadVersion = storyReloadVersion;
		companionStorylets = new StoryDeck { UseSpecificity = true };
		BuildCompanionStory(new StoryBuilder(companionStorylets));

		var path = Path.Join(Assets.AssetsPath, "Storylets", "companion.json");
		if (!File.Exists(path))
			return;

		try
		{
			var jsonDeck = StoryletJson.DeckFromFile(path);
			foreach (var (key, value) in jsonDeck.Context)
				companionStorylets.Context[key] = value;
			foreach (var storylet in jsonDeck.Storylets)
				companionStorylets.Add(storylet);
		}
		catch (Exception e)
		{
			Console.Error.WriteLine($"Failed to load companion storylets: {e}");
		}
	}

	private static void BuildCompanionStory(StoryBuilder story)
	{
		story.Companion("companion_start_first", s => s
			.On(StoryTrigger.RoomEntered)
			.When(ctx => ctx.RoomVisits == 1)
			.Once()
			.Priority(10)
			.Say("I think this is where we start."));

		story.CompanionBarks("companion_room_arrival", s => s
			.On(StoryTrigger.CompanionEntered)
			.Redraw(4)
			.Priority(1)
			.Say(
				"I'm here.",
				"Wait up.",
				"I thought you lost me."));
	}

	private void UpdateStory()
	{
		if (loadedStoryReloadVersion != storyReloadVersion)
			ReloadStory();

		storyRoomTime += Time.Delta;
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
		var roomId = change.To.Id;
		storyRoomVisits.TryGetValue(roomId, out var visits);
		storyRoomVisits[roomId] = visits + 1;

			if (change.Kind == RoomChangeKind.InitialLoad)
				TryCompanionStorylet("room_entered");
		}

	private void ReloadStory()
	{
		LoadStory();
		DisplayMessage("Reloaded story");
		Console.WriteLine("Reloaded story.");
	}

	internal void TryCompanionStorylet(string trigger)
	{
		if (companionStorylets == null || GetFirst<Companion>() is not { } companion || !CanShowCompanionSpeech())
			return;

		RefreshStoryContext(trigger);
		var storylet = companionStorylets.DrawAndPlaySingle(IsCompanionStorylet);
		if (storylet == null)
			return;

		if (storylet.Content.TryGetValue("text", out var text) && text is string line && !string.IsNullOrWhiteSpace(line))
			companion.Say(line);
	}

	internal bool CanShowCompanionSpeech()
		=> storySpeechBubbles.Count == 0;

	internal void ShowCompanionSpeech(Vector2 position, string text)
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

	private void RefreshStoryContext(string trigger)
	{
		if (companionStorylets == null)
			return;

		var typedTrigger = ParseStoryTrigger(trigger);
		var roomId = room?.Id ?? "";
		var roomVisits = room != null && storyRoomVisits.TryGetValue(room.Id, out var visits) ? visits : 0;
		var hasCompanion = GetFirst<Companion>() != null;
		var playerHealth = GetFirst<Player>() is { } player ? player.Health : 0;
		var companionDistance = GetFirst<Player>() is { } p && GetFirst<Companion>() is { } c
			? Vector2.Distance(p.Position, c.Position)
			: 0;

		companionStorylets.TypedContext = new(
			Trigger: typedTrigger,
			WorldId: Assets.CurrentWorldId,
			RoomId: roomId,
			RoomTime: storyRoomTime,
			RoomVisits: roomVisits,
			RoomsSeen: storyRoomVisits.Count,
			HasCompanion: hasCompanion,
			PlayerHealth: playerHealth,
			CompanionDistance: companionDistance);

		var context = companionStorylets.Context;
		context["speaker"] = "companion";
		context["trigger"] = trigger;
		context["world_id"] = Assets.CurrentWorldId;
		context["room_id"] = roomId;
		context["room_time"] = storyRoomTime;
		context["rooms_seen"] = storyRoomVisits.Count;
		context["room_visits"] = roomVisits;
		context["has_companion"] = hasCompanion;
		context["player_health"] = playerHealth;
		context["companion_distance"] = companionDistance;
	}

	private static StoryTrigger ParseStoryTrigger(string trigger)
	{
		return trigger switch
		{
			"room_entered" => StoryTrigger.RoomEntered,
			"companion_entered" => StoryTrigger.CompanionEntered,
			_ => StoryTrigger.None
		};
	}

	private static bool IsCompanionStorylet(Storylet storylet)
		=> storylet.Content.TryGetValue("speaker", out var speaker) &&
		   string.Equals(speaker?.ToString(), "companion", StringComparison.OrdinalIgnoreCase);
}
