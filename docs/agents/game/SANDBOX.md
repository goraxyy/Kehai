# The sandboxes

Small test scenes for checking one thing at a time (0.5.2 plan, Phase 11). Each is built by code
when Play starts, from an empty scene: no scene file, no art, nothing in git but code. The first is
the **sight sandbox**; pursuit, tricks and interactions follow, from the same kit.

## The sight sandbox

**Open it:** in the Unity editor, **Kehai → Sandbox → Sight**. Unity offers to save your open
scene, opens an empty one and enters Play. Leave Play to stop; open the store scene again as usual.

**What's there:** a 16 × 12 m room with one door in the south wall. Inside: a stock fridge against
the east wall (you can stand inside it), a stock rack by the north wall (crouch under its lowest
board), two tall shelves (2.2 m), three short shelves (1.2 m) and four lamps. You start outside,
by the door.

**Karen walks and sees, nothing else:** her real body and her real eye with the game's settings,
and no tricks, no hunt, no catch. She starts frozen on her spot in the north-west corner.

| Panel button | Key | Does |
|---|---|---|
| START | F5 | she walks her loop round the room (or turns on her spot, see ROUTE) |
| RESTART | F6 | back to her spot, frozen, her awareness and memory cleared |
| FREEZE | F7 | she stops where she is; her eye keeps working |
| ROUTE | F8 | switches between walking her loop and standing while turning a quarter every 3 s |

There's a panel outside by the door and one inside. Look at a button and press **E**.
**Ctrl** crouches, **Shift** sprints, **Esc** frees the mouse, **F3** toggles her floor cone.

**What it shows you:**

| Indicator | Means |
|---|---|
| Her floor cone | where she can see: calm white, amber when she's noticing you, red when she's sure |
| The number over her head | her awareness, 0 to 1, and "sees" while she sees you this frame |
| The line from her eye to yours | green: the line is clear and you're in her cone; red: blocked, hidden, or behind her |
| The text over the fridge and the rack | how much of you is inside; HIDDEN at 80% (the rack only when you crouch) |
| The box in the top-left corner | all of it in numbers: her state, your distance and angle, your light, how you're moving, which hiding place you're in, and what E would do |

**Things to try:** stand in the open and walk; crouch behind a short shelf (hidden) and stand up
(seen: your eyes show over it); crouch under the rack; get into the fridge while she watches (the
corner box says SAW YOU GO IN) and while her back is turned.

**What it isn't yet:** the bot that runs these checks by itself and records them (C11.6), and the
pursuit, tricks and interactions sandboxes (C11.7–C11.9). The furniture is plain boxes at real sizes.

## For agents

- Code: `Assets/!_Project/_Game/Sandbox/` (`SandboxKit` the parts, `SandboxKaren` her mode,
  `SandboxPlayer` the player, `SandboxIndicators` the readouts, `SightSandbox` the room) and
  `Editor/SandboxMenu.cs`.
- Tests: `SandboxTests` (edit mode: the fridge, the rack, the shelves' eye lines, the layout, the
  readouts, the panel) and `SightSandboxPlayTests` (play mode: builds the whole room, drives her).
- Her eye is never changed here; the meters only read it. Hiding places are authored with
  `HidingPlace.Author`, as in the store.
