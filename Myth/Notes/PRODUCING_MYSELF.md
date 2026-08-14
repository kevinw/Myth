# Producing Myself For Talk & Play

Talk & Play #57 is Wednesday, September 9, 2026. Applications start being reviewed
August 19.

The goal is not to finish Mycelon. The goal is for a stranger to have a satisfying
6–10 minute encounter with it without Kevin explaining how to play.

Available development time: 4 hours a day, 6 days a week, or 24 hours per week.
Treat that as a limit, not a target to exceed.

## Two Deliverables

### Application

Submit before August 19 with:

- A strong 1920x1080 screenshot.
- A public URL, even if the build is clearly labeled as a prototype.
- A one- or two-sentence description.
- The laptop/controller setup.

Possible description:

> Mycelon is an atmospheric action-platformer about exploring a strange living
> castle with Bud, an unpredictable little companion. Burrow through surfaces,
> uncover hidden paths, and turn expressive movement into combat.

Do not make the application wait for demo improvements.

### Showcase Demo

The demo is a curated route through existing material, not a condensed version of
the whole game.

It should:

- Look alive immediately.
- Give the player control within 15 seconds.
- Introduce burrowing within 60–90 seconds.
- Use burrowing in one traversal-combat encounter.
- Contain one optional secret.
- Give Bud one memorable moment.
- End with a clear climax and an unmistakable completion moment.
- Take 6–10 minutes on the main route and no more than 15 when exploring.
- Lose less than a minute of progress on death.
- Reset completely with one obvious input.

The next product decision is to name the exact existing room sequence which comes
closest to this arc.

## Schedule

- August 16: submit the application.
- August 17: lock the demo route and ending.
- August 20: first uncoached playtest.
- August 24: feature freeze.
- August 27: second uncoached playtest.
- August 31: content freeze and release candidate.
- September 3: rehearse the complete table and hardware setup.
- September 7: final build freeze.
- September 8: pack and test the demo offline.
- September 9: arrive before 18:00; showcase from 18:30–22:00.

Put the playtests on the calendar now. External appointments are stronger stopping
rules than intentions.

## Scope Firewall

Until September 9, freeze:

- Renderer research.
- Editor work which does not block demo content.
- iOS and mobile work.
- New mechanics and worlds.
- Dashboard expansion.
- Bugs a demo player cannot encounter.

Keep a visible `Not Before September 9` list. Moving an idea there is not rejecting
it; it is protecting the demo.

## Producer Kevin And Developer Kevin

Producer Kevin gets ten minutes at the end of each day:

1. Play the current demo.
2. Choose tomorrow's single most important playable result.
3. Write its first physical action.
4. Put its work block on the calendar.
5. Move unrelated ideas to the parking lot.

Developer Kevin executes that decision and does not renegotiate the project during
the work block.

Daily rules:

- Keep one task in progress.
- Begin by playing, not by reading the full TODO.
- End with a playable difference.
- If stuck for 20 minutes, choose a workaround, a cut, or an explicit timebox.
- Shrink the result instead of silently extending the deadline.
- Do not implement playtest suggestions unless they address observed confusion or
  failure.
- Leave one plain next action for tomorrow.

## Workday Shape

The four hours do not all need to be undisturbed deep work. A useful default is:

- 10 minutes: play the demo and confirm today's result.
- 90 minutes: first focused block.
- Break.
- 90 minutes: second focused block.
- 30 minutes: test in the actual demo route.
- 10 minutes: producer review and tomorrow's first action.
- Remaining time: release build, notes, communication, or contingency.

At least one day each week should emphasize complete runs, playtesting, and triage
instead of feature work.

## Lightweight Task And Time Tracking

Track time to improve prediction, not to judge productivity.

Use three task sizes:

- Small: 30 minutes.
- Medium: 60–90 minutes.
- Large: half a day; split anything larger.
- Uncertain: give it a timebox rather than pretending it has a reliable estimate.

Keep only four lists:

```md
## Now
- [ ] Demo can reset cleanly — estimate 60m — actual:
  Next action: add reset command to pause screen

## Next
- [ ] Choose screenshot room — 30m
- [ ] Record first blind playtest — 90m

## Done
- [x] Draft application copy — estimate 30m — actual 20m
  Result: ready for form

## Not Before September 9
- Improve dashboard
- New enemy behavior
- Renderer experiment
```

For each work block, record one line:

```text
14:00–15:10 | demo reset | works after death and completion
Next: verify it in an offline release build
```

Review estimates after five working days. Useful weekly measures are:

- Complete demo runs.
- Uncoached playtests.
- Known progression blockers.
- Focus blocks spent on the demo.
- Percentage of time spent on showcase-critical work.

Do not use commits, lines changed, or hours at the computer as the measure of
progress.

## Tools

Start with this Markdown note, Calendar, and a timer.

If manual timers are too easy to forget, try an automatic tracker such as
ActivityWatch or Timing and categorize the work afterward. If a combined local task
list and timer would help, try Super Productivity.

Do not build a productivity application before the showcase. If the Markdown log is
genuinely irritating after three days, the maximum useful custom tool is an
append-only command:

```text
work start demo-reset
work stop "reset works after death"
work report
```

No UI, accounts, synchronization, graphs, or integrations.

## Event Reliability

Bring:

- Laptop and charger.
- Wired controller and a spare if available.
- Headphones.
- Power strip.
- A small printed control card.
- Offline desktop build and browser-build backup.

Disable notifications and sleep. Rehearse the startup and full-reset procedures.
The build shown on September 9 should be the exact frozen build tested on the full
event setup.
