namespace Myth;

public partial class Game
{
	private static void BuildStory(StoryBuilder story)
	{
		story.BudBarks("bud_start_first", s => s
			.On(StoryTrigger.RoomEntered)
			.When(ctx => ctx.RoomVisits == 1)
			.Once()
			.Priority(10)
			.Say("Come on.",
				 "OK..."
			));

		story.BudBarks("bud_room_arrival", s => s
			.On(StoryTrigger.BudEntered)
			.Redraw(4)
			.Priority(1)
			.Say(
				"I'm here.",
				"Still me.",
				"Smells weird in here."));

		story.BudBarks("bud_reveal_door", s => s
			.OnRevealDoorEntered()
			.Redraw(3)
			.Priority(2)
			.Say(
				"That was hidden.",
				"I wonder who covered that up.",
				"Good eye."));

		story.BudBarks("bud_player_hurt", s => s
			.OnPlayerHurt()
			.Redraw(4)
			.Priority(2)
			.Say(
				"Careful.",
				"Ouch",
				"Still with me?"));

		story.BudBarks("bud_enemy_killed", s => s
			.OnEnemyKilled()
			.Redraw(5)
			.Priority(1)
			.Say(
				"Nice.",
				"IS violence the answer?",
				"Woah."));

		story.BudBarks("bud_idle", s => s
			.OnPlayerIdle(StoryIdleSeconds)
			.Redraw(3)
			.Priority(1)
			.Say(
				"Are we waiting for something?",
				"It's quiet.",
				"I can wait too."));
	}
}
