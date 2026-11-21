# The sandboxes

Small test scenes for checking one thing at a time (0.5.2 plan, Phase 11). Each is built by code
when Play starts, from an empty scene: no scene file, no art, nothing in git but code. Built so far:
the **sight sandbox** and the **pursuit sandbox**; tricks and interactions follow, from the same kit.

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

**What it isn't yet:** the bot that runs these checks by itself and records them (C11.6). The
furniture is plain boxes at real sizes.

## The pursuit sandbox

**Open it:** **Kehai → Sandbox → Pursuit**, the same way.

**What's there:** a 24 × 16 m room with two long rows of tall shelves, a gap through the middle of
the north row, aisles all round, a stock fridge on the east wall and a stock rack on the north
wall. You start outside, by the door.

**Karen is her real brain here,** so her hunt and her 60 s pursuit after losing you are the game's
own. Three switches make it a sandbox, and all three are off in the game:

| Switch | Means |
|---|---|
| No tricks | only her movement tactics: patrol, investigate, sweep, ask, withdraw, hunt |
| A catch freezes | touching you shows CAUGHT and freezes the test; no death screen, no stats |
| A temporary Ledger | her memory of you is a temporary file, so your own save is never read or written |

Pacing and the Director's hint are off too: she hunts as soon as she's sure, and only her senses
say where you are. **F1** opens her map with her belief, as in the store.

| Panel button | Key | Does |
|---|---|---|
| START | F5 | her brain runs: she roams, and hunts you once she's sure |
| RESTART | F6 | a new shift for her: back on her spot, frozen, her belief and hunt cleared |
| FREEZE | F7 | her brain stops where it is |

**Things to try:** let her see you, then break her line of sight round a shelf end: the corner
box says "lost you 2.3 s ago, running on" and then "searching, 41 s of the hunt left, 2 spots
searched". Hide in the fridge after she's lost you; cut through the gap; let her catch you.

## For agents

- Code: `Assets/!_Project/_Game/Sandbox/` (`SandboxKit` the parts, `SandboxKaren` her
  walk-and-see mode, `SandboxPlayer` the player, `SandboxIndicators` the readouts, `SightSandbox`
  and `PursuitSandbox` the rooms) and `Editor/SandboxMenu.cs`.
- The pursuit sandbox runs `KarenBrain` without `KarenBootstrap` (no recorders, no blink tracker)
  and calls `BeginShift()` itself; no `ShiftManager` is needed. Its switches are
  `KarenConfig.sandboxNoTricks` (checked in `HtnPlanner.Gated`) and `sandboxCatchFreezes` (in
  `KarenBrain.Kill`, raising `SandboxCaught`). It sits at sales-floor coordinates so `StoreMap.AreaAt`
  counts its cells as hers.
- Tests: `SandboxTests` and `PursuitSandboxTests` (edit mode) and `SightSandboxPlayTests` and
  `PursuitSandboxPlayTests` (play mode: each builds its whole room and drives her).
- Her eye is never changed here; the meters only read it. Hiding places are authored with
  `HidingPlace.Author`, as in the store.
