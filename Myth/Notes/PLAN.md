# Myth plan

write 20 minutes a day

## Compass

Myth is a small atmospheric metroidvania about mystery, movement, and companionship with Bud. The emotional center is a love story; the mechanical center is expressive traversal verbs that also matter in combat.

For now, the goal is not "the whole game." The goal is to finish one playable loop:

Safe beauty with Bud -> mysterious blocked path -> traversal-combat gauntlet -> secret -> new area -> new verb or upgrade -> boss that tests it -> quiet Bud payoff.

That is the slice. If it works for friends, decide whether to push toward Steam. If it does not, the project still answered the "can I finish something small?" question.

## Design Rule

Do not build combat and traversal as separate modes. Build encounters: traversal problems with enemies attached.

Every required enemy in the slice should test one player verb:

- ShieldKnight: spacing, facing, roll/burrow/blink access to the weak side.
- Mobile nuisance: keeps traversal pressure alive while moving through terrain.
- Reactive enemy: changes behavior when hit, so attacks feel expressive instead of purely subtractive.
- Boss: recombines the slice verbs and makes the new verb matter.

## Player Rhythm

Use compression and release:

- Breath: safe room, save point, beauty, Bud behavior, storylet.
- Tension: traversal challenge, enemy pressure, risky path.
- Reward: secret, new room, shortcut, upgrade, reveal, Bud moment.

Place the breaths first. Then put tension between them. Do not stack two high-tension rooms without a release beat unless the point is an intentional climax.

## Scope Firewall

Until the slice is playable, freeze work that does not directly support the loop above.

Allowed:

- Combat feel.
- One release-path area.
- One secret branch.
- One upgrade or new verb payoff.
- One boss.
- Bud moments that support the emotional core.
- Tools only when a specific slice task is blocked.

Deferred:

- More worlds.
- Broad enemy frameworks.
- New currencies.
- Renderer research.
- Platform polish beyond browser-blocking bugs.
- Editor polish beyond content-blocking bugs.
- Speculative mechanics not needed by the slice.

## Roadmap

### 1. Combat Toy

Make one empty-room fight that is fun before it is useful.

Use ShieldKnight as the toy:

- Patrol.
- Notice player.
- Telegraph.
- Commit to attack.
- Recovery.
- Punishable weak side.
- Clear block behavior.
- Ledge awareness only if the test room needs it.

Done when fighting one ShieldKnight for two minutes is readable, fair, and a little expressive.

### 2. Traversal-Combat Gauntlet

Build a short stretch where movement and enemies share the same problem space.

Done when the player has to move well because enemies exist, not after enemies are cleared.

### 3. Secret To New Area

Hide a branch that asks the player to probe the space. The reward is not a coin pile; it is a new area with one new challenge vocabulary.

Done when discovering it feels like "there is more world here than I thought."

### 4. Upgrade Or Verb Payoff

Add one payoff that changes how the player reads earlier space. Prefer improving an existing verb over adding a new broad system.

Done when the player immediately wants to revisit one earlier obstacle.

### 5. Boss

Build a boss from the same verbs as the slice, not a separate game. The boss should be impossible or miserable without the new payoff, then satisfying with it.

Done when the boss is readable, beatable, and tests the slice instead of just testing HP.

### 6. Beauty, Bud, And Breath Pass

Only after the loop is playable, do the pacing pass:

- One quiet opening.
- One midpoint breath.
- One pre-boss breath.
- One post-boss Bud payoff.

Done when the slice has emotional punctuation, not just rooms.

## Daily Loop

Weekdays, 3-4 hours:

1. Play the current slice or toy for 5 minutes.
2. Pick one vertical task that changes what can be played today.
3. Keep engine/tool work under 30 percent unless content is blocked.
4. End with something playable that differs from the morning.
5. Leave one plain next task for tomorrow.

Friday:

- Play the current build start to finish.
- Write what felt good, flat, confusing, or broken.
- Pick 3-5 tasks for next week.

Monthly:

- Put the browser build in front of one person.
- Watch without coaching.
- Fix blockers before suggestions.

## First Week

### Day 1: ShieldKnight Test Room

- Make or choose one tiny room for the combat toy.
- Put the player, one ShieldKnight, simple terrain, and a fast reset path in it.
- Do not tune the whole game.

### Day 2: Telegraph And Charge

- Give ShieldKnight an explicit state/timer loop.
- Use the existing Charging state for telegraph -> attack -> recovery.
- Make the attack readable before it is dangerous.

### Day 3: Punish Window

- Make blocking and weak-side punishment obvious.
- Add hit reaction and recovery timing.
- Decide which player verb makes the fight interesting.

### Day 4: Terrain Interaction

- Add the smallest terrain wrinkle that makes the fight spatial.
- Only add ledge awareness if the toy needs it.

### Day 5: Friday Play Pass

- Fight the toy repeatedly.
- Record three numbers: deaths, successful punishes, cheap hits.
- Keep only the next changes that improve readability or expression.

## Enemy Toolkit Rule

Do not build an enemy framework first.

Build three concrete enemies. Extract only what appears twice:

- Telegraph/active/recovery timing.
- Perception.
- Ledge or edge checks.
- Hit reactions.
- Shared death/pop behavior.

The toolkit should fall out of content, not precede it.

## Slice Done

The first slice is done when:

- A new player can finish it in 15-25 minutes.
- It contains one secret worth finding.
- It contains one new area or new challenge vocabulary.
- It contains one boss.
- Bud matters emotionally at least once.
- Browser build is playable start to finish.
- One friend finishes without coaching.
