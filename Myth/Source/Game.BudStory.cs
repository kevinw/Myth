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

		/*
		story.BudBarks("bud_room_arrival", s => s
			.On(StoryTrigger.BudEntered)
			.Redraw(4)
			.Priority(1)
			.Say(
				"I'm here.",
				"Still me.",
				"Smells weird in here.",
				"What's that?"
			));
			*/

		story.BudBarks("bud_reveal_door", s => s
			.OnRevealDoorEntered()
			.Redraw(3)
			.Priority(2)
			.Say(
				"Oh."));

		story.BudBarks("bud_player_hurt", s => s
			.OnPlayerHurt()
			.Redraw(4)
			.Priority(2)
			.Say(
				"Ouch!",
				"Still with me?"));

		story.BudBarks("bud_enemy_killed", s => s
			.OnEnemyKilled()
			.Chance(0.2)
			.Redraw(5)
			.Priority(1)
			.Say(
				"Nice.",
				"Hmm.",
				"Woah.",
				"Hm..."
			));

		story.BudBarks("bud_back_dash", s => s
			.On(StoryTrigger.BudStruck)
			.Redraw(6)
			.Priority(3)
			.Say(
				"Ow. Really?",
				"Hey! Watch it.",
				"That's my head.",
				"Point that thing somewhere else.",
				"I'm on your side, remember?",
				"You could just ask me to move.",
				"Do that again and I walk.",
				"Hilarious.",
				"I'll remember that.",
				"Cut it out."
			));

		story.BudBarks("bud_idle", s => s
			.OnPlayerIdle(StoryIdleSeconds)
			.Redraw(3)
			.Priority(1)
			.Say(
				"I'm hungry. I told you we should have packed a lunch."
			));
	}
}
