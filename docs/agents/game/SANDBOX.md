# The sandboxes

The **Kehai → Sandbox** menu opens the [sight](#the-sight-sandbox), [pursuit](#the-pursuit-sandbox),
[tricks](#the-tricks-sandbox), and [interactions](#the-interactions-sandbox) sandboxes. The rooms
are built by code when Play starts. Leave Play to return to editing.

## The sandbox island

Open **Kehai → Sandbox → Island** for the quiet store and its sight, pursuit and interactions rooms.
Leave the front door, follow the protected walkway north to the road, and turn west. The models
island is beside the sandbox island. The trick stalls are along the south edge in **Tricks** mode.

This batch check drives the store player from the lobby to the road, the trick stalls' approach,
through the three room doors, and across to the models island. It logs each route and the position
of any stop. It opens the sandbox doors before the walk.

```sh
tools/run/kehai-run unity -- -noaudio -executeMethod SandboxIslandCheck.Walk
tools/run/kehai-run unity -- -noaudio -executeMethod SandboxIslandCheck.Pursuit       # the pursuit checks in the island's pursuit room
tools/run/kehai-run unity -- -noaudio -executeMethod SandboxIslandCheck.Interactions  # the interaction checks in the island's interactions room
```

**The island against a room on its own** (23 November). Two things made her play differ there,
both fixed; the pursuit checks still pass 3 of 4 on the island against 4 of 4 alone (below):
- **Her light sense** (`LightProbe`) read only the first group named `CeilingLights`, the store's,
  so the island rooms' lamps didn't light you for her: you looked darker there and she saw you
  0.8 s later. It now reads every `CeilingLights` group, and `SandboxKit` refreshes it as a room's
  lamps are built. (After the fix she first saw the bot at 0.8 s on the island, 1.0 s alone.)
- **Her test map's 1.5 m grid** started 0.2 and 0.4 m off the standalone room's, so her search spots
  fell elsewhere. `SandboxIsland.MapBounds` is nudged onto the standalone room's grid
  (`PursuitGrid`, as `PursuitSandboxChecks` logs it), and `SandboxIslandCheck.Pursuit` checks it.

**Still different: corner-search fails on the island.** There her map covers the whole room, and
her first search spot is the shelf the bot hides behind, so she sees it. Alone, her first sighting
logs as "off the map" and she searches where she last saw you, which is likely what that check's
escape relies on (one run's log; not yet confirmed).

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
| FREEZE | F5 again | she stops where she is; her eye keeps working |
| ROUTE | panel only | switches between walking her loop and standing while turning a quarter every 3 s |

There's a panel outside by the door and one inside. Look at a button and press **E**.
**Ctrl** crouches, **Shift** sprints, **Esc** frees the mouse, **F4** hides the indicators and her floor cone
(and shows them again); in the pursuit sandbox V on the F1 map toggles the cone too.

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

**The bot's checks** (C11.6): **Kehai → Sandbox → Run Sight Checks**, or headless on a safe copy:

```bash
tools/run/kehai-run unity -- -executeMethod SandboxChecks.Sight
```

A bot plays eight checks with the player's real body: standing in the open at 8 m; behind a short
shelf, standing and crouched; in front of and crouched under the rack; inside the fridge; walking
into the fridge while she watches, and behind her back. Each row says what was expected and what
her eye did (her highest awareness, and how soon she noticed). The report is
`sandbox_checks/sight_<time>.md` in the game's data folder; `kehai-run` keeps it in the run's
folder. Each row links to a case recording in the sibling `sight_<time>/` directory.
**Kehai → Sandbox → Watch Sight Check Replay** reconstructs the sight room and plays the
recorded actors, fridge hinge and observation. Headless, Unity exits 0 when every check passes and 1 when one fails.

Both sandboxes' checks fix each frame at 1/60 s of game time (`SandboxBot.FixFrames`, as KehaiEnv
does for ablations). Without it a batch Unity runs hundreds of frames a second, each frame's step
falls under the CharacterController's least move and is dropped, and the bot crawls: the unseen
fridge walk-in once stopped 8% inside for that reason alone. A walk-in that stops short of hidden
fails as "the check itself failed", never as her eye's fault.

The furniture is plain boxes at real sizes.

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
| FREEZE | F5 again | her brain stops where it is |

**Things to try:** let her see you, then break her line of sight round a shelf end: the corner
box says "lost you 2.3 s ago, running on" and then "searching, 41 s of the hunt left, 2 spots
searched". Hide in the fridge after she's lost you; cut through the gap; let her catch you.

**Run the recorded pursuit checks** from **Kehai → Sandbox → Run Pursuit Checks**. For a batch
run, use the following command with the editor closed and the checks on the selected ref.

```sh
tools/run/kehai-run unity -- -executeMethod PursuitSandboxChecks.Run
```

The bot sprints through the shelf gap to check loss of sight, running on and searching. It takes
the east aisle into the fridge to check that an unseen entry holds and that the hunt ends 60 seconds
after the last sighting. It peeks out from behind the shelf in a separate run to check that a fresh
sighting renews the window, then tests contact and Restart in a fourth run.

Each run writes `report.md`, `report.json` and the case replays under
`<persistent data>/sandbox_checks/pursuit_<time>/`. The clone runner copies that folder out with
the other run results. Exit 0 means all rows passed, 1 means a check failed, and 2 means the run
timed out. **Kehai → Sandbox → Watch Pursuit Check Replay** opens a `.krec` in the generated room.

For sight and pursuit recordings made with the silent renderer, the runner uses the source
commit and reconstructs the recorded room. Preview the command below. With a go for that
render, omit `--dry-run`. The new run contains `clip.mp4`, `clip.json` and the render log.

```sh
tools/run/kehai-run sandbox-clip "$KEHAI_RECORDING" --start 0 --end 12 --dry-run
```

**What the runs found (21 November, on a safe copy): 4 of 4 pass at 744b734.**

| Check | Result | Why |
|---|---|---|
| corner-search | pass | ran on 8.5 m with sight blocked, then searched |
| fridge-expiry | pass | the unseen entry held, she searched 30 spots, and the hunt ended 60.0 s after the last sighting. It starts with her sure of you, frozen but seeing, as the sight checks do: left to her first plan she turned away and never confirmed you, and at the first setup's 16 m her eye's strength is a fifth (`SightSensor.Falloff`). |
| reacquire | pass | a fresh sighting renewed the 60 s window (the check reads the renewal, not whether she still sees you the next frame) |
| catch-restart | pass | contact froze the sandbox; Restart cleared her |

The first run passed only catch-restart. The other two failures were a real bug in her brain, in
the game too: a forced re-decision while she hunted (a sprint step close by is one, `OnNoise`)
picked the hunt again, `AbortPlan` ended the hunt, which retired its sighting, and the new hunt
started with 0 s left and ended the next frame, over and over. So sprinting away from her killed
her 60 s pursuit. Fixed by `KarenBrain.AbortPlanFor` (a hunt chosen again goes on, its window
kept) and `EndHeldHunt` (a held hunt that no hunt plan takes up still ends), with a test in
`Kehai051PursuitTests`.

The sight suite's [eight silent clips](../../../tools/run/README.md#sight-check-review-clips) have
been rendered and verified. The pursuit suite has replays and still needs its clip render.

## The tricks sandbox

**Kehai → Sandbox → Tricks** (C11.8) opens the real store in Play, muted, on a private shift
four: its own career numbers and a fresh temporary Ledger each time, so your save is never read
or written. A row of stalls stands on the sandbox island (out of the front door, north across the parking lot to the road, then west), near its south edge: one plate per
live trick (the same list the trick check uses, `TrickCheck.Tricks`).

- **E at a plate** makes her start that trick now (`KarenBrain.Force`: past the Director's
  pacing, not past the trick's own needs; overtime, for one, still waits for the last two
  minutes). Frozen, she runs just that trick and freezes again.
- **Keys:** F5 starts the shift and lets her move, or freezes her; F6 restarts (a new shift four);
  `[` and `]` choose a trick from anywhere, Return fires it. The box at the top right says
  what she's doing and where the trick's chore is.
- Her own tricks stay off (`sandboxNoTricks`): only the plates start one. A catch freezes the
  sandbox instead of ending the shift.

**Its recorded checks** are the trick check's (`-kehai-trick-check`): for each live trick, did
she start it, did it land (its own evidence, not just an EFFECT line), and did the bot answer it
(a repair, a dodge, a turn back), each in a replay. It writes `trick_check_<time>.md`, `.json`
and the `.tsv` the clip tools read. **It runs in the recorded test pass after 0.5.2 merges**
(Q1), not before.

```sh
tools/run/kehai-run unity -- -noaudio -executeMethod EvalBatch.Play -kehai-trick-check
```

## The interactions sandbox

Open **Kehai → Sandbox → Interactions**, or walk to the room north of the sight area in
**Kehai → Sandbox → Island**. Its panel switches the camera feed, asks the nearby shopper,
and clears the readings. Pick up the torch with E. Switch it on and aim out through the open
fridge door while hidden to see whether the light reaches a surface Karen can see.

**Kehai → Sandbox → Run Interaction Checks** drives nine cases. It writes a result report and
one replay per case under
`sandbox_checks/interactions_<time>/`. The cases cover camera on, off and turned away,
shopper facing toward and away from the approach, torch on and off, and the Ledger's weight
when Karen sees the entry or faces away. The testimony case
asks from outside and then inside talking distance. The recordings use the sight room replay
viewer. The evidence appears in the report and replay annotations.

```sh
tools/run/kehai-run unity -- -noaudio -executeMethod SandboxChecks.Interactions
```

The room uses the game's sensors. Its findings stay in the room. The pursuit room keeps the
island's one full Karen brain. The fridge and rack search actions have separate acceptance in
[the 0.5.2 plan](../../you/plans/0.5.2-plan.md#phase-9-interactions-c-with-you--opus-55--this-mac-checks-through-kehai-run-on-the-pc).

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
