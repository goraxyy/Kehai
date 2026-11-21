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
**Ctrl** crouches, **Shift** sprints, **Esc** frees the mouse, **F3** toggles her floor cone, **F4**
hides the other indicators (and shows them again).

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
folder. Headless, Unity exits 0 when every check passes and 1 when one fails.

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
| FREEZE | F7 | her brain stops where it is |

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

The replay renderer can make a silent clip from an individual check in a graphics-enabled clone
under the heavy-work lock. Set the recording path, output path and time range for the clip.

```sh
"$KEHAI_UNITY" -batchmode -projectPath "$KEHAI_CLONE" \
  -executeMethod PursuitSandboxChecks.Render -krec "$KEHAI_RECORDING" \
  -from 0 -to 12 -shot chase -size 1280x720 -fps 30 -out "$KEHAI_CLIP"
```

**What the first runs found (21 November, on a safe copy):**

| Check | Result | Why |
|---|---|---|
| corner-search | pass | ran on 8.5 m with sight blocked, then searched |
| fridge-expiry | **fails: the check's setup** | at the start she sweeps toward her belief's peak, turns away down her path and never looks along the aisle, so no hunt starts within 8 s. Her eye is fine: at the first setup's 16 m her eye's strength is a fifth (`SightSensor.Falloff`) and a still player is never confirmed, so it now starts at 10 m. It still needs a setup that makes sure she confirms you first. |
| reacquire | pass | a fresh sighting renewed the 60 s window |
| catch-restart | pass | contact froze the sandbox; Restart cleared her |

The first run passed only catch-restart. The other two failures were a real bug in her brain, in
the game too: a forced re-decision while she hunted (a sprint step close by is one, `OnNoise`)
picked the hunt again, `AbortPlan` ended the hunt, which retired its sighting, and the new hunt
started with 0 s left and ended the next frame, over and over. So sprinting away from her killed
her 60 s pursuit. Fixed by `KarenBrain.AbortPlanFor` (a hunt chosen again goes on, its window
kept) and `EndHeldHunt` (a held hunt that no hunt plan takes up still ends), with a test in
`Kehai051PursuitTests`.

The silent video clips are code only: none has been rendered yet.

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
