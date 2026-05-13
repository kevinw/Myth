# Game Design Brainstorm

Goal: make the platformer feel less like a conventional action platformer and more like emotional interactive fiction, without losing the immediacy of movement, rooms, tiles, sword, light, and sound.

## Text As Physical Space

Avoid treating story as a separate dialogue box layer. Put text into the world:

- words as tiles, platforms, fog, water reflections, inscriptions, or background marks
- text that only becomes readable from certain positions
- text revealed by light, reflection, death, revisiting, or destruction
- destructible tiles revealing partial sentences
- room text that changes after the player breaks something or returns later

This keeps narrative tied to play instead of pausing the platforming.

## Light As Interpretation

Lighting can reveal meaning, not just visibility.

- player light reveals hidden text but may wake enemies
- warm/cold lights reveal different emotional layers of the same room
- placed lights can change what an NPC or memory says
- the same room can have different collision/story affordances depending on what is illuminated

This fits the existing lighting work and gives story snippets a spatial rule.

## Sword As A Verb

The sword should not only be a damage tool. It can mean:

- cut: remove text, vines, illusions, bindings
- interrupt: stop enemies, silence loops, cancel memory patterns
- answer: choose a response by striking a word/object, without a dialogue UI
- revise: break sentences or memories to alter room state

Example: a room contains the line "I never wanted you here." The player can cut away "never" or "you", changing the room or music. This makes combat feel like authorship.

## Movement: Commitment Echo

A possible fresh movement mechanic:

- jumps, sword swings, or landings leave a temporary echo
- the echo can become a platform, ledge, light trace, or repeated action
- the player is helped or haunted by previous actions

This could be framed as memory instead of as a standard dash/double-jump upgrade.

## Movement: Weight And Lightness

Another possible movement mechanic:

- carrying a thought/memory makes the player heavier
- heavier means lower jump, stronger stomp, perhaps more stable footing
- letting go makes the player lighter and more mobile but vulnerable
- text snippets can be picked up, carried, dropped, or placed into rooms

This gives movement an emotional state without needing abstract meters.

## Combat And Spatial Audio

Since the game already has FMOD and spatial positioning, sound can become a mechanic:

- enemies react to player-relative sound rather than only position
- the sword can strike/cut sound loops: buzzes, whispers, bells, music layers
- hidden text becomes legible only when standing where audio sources align
- some enemies are calmed by stillness, facing away, or rhythm

This makes combat more about listening and presence than only hitboxes.

## Dialogue Without Dialogue Trees

Use player behavior as dialogue input:

- NPCs react to entering, leaving, waiting, attacking, lighting, or standing close
- sword, light, crouch, silence, and proximity become response verbs
- repeated deaths alter narrator/NPC lines
- optional lines are earned by expressive actions, not collected

This avoids branching menus but still supports emotional interactivity.

## Best First Prototypes

### 1. Light-Revealed Text

Add a placeable text fragment that can be:

- always visible
- visible only when lit
- visible only in reflection
- visible after death
- visible after being cut

This is a direct extension of the lighting system.

### 2. Sword As Edit Tool

Extend sword/destructible logic so the sword can alter text or memory objects:

- cut a word
- reveal a hidden passage
- change music area
- change a room flag
- change future dialogue

Prototype room:

- text says "I am safe here."
- sword cuts "safe"
- music changes from Calm to Curious
- hidden passage opens

Together, these systems suggest an identity: the player explores by illuminating and revising the world.
