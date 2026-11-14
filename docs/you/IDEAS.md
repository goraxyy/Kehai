# Ideas

The ideas that make Kehai more than a store game, and what each still needs. Karen's full design
is in [Karen.md](../agents/karen/Karen.md); the release to-do list in [RELEASE_PLAN.md](RELEASE_PLAN.md).

They fit together: the **endless maze** gives the **agent test environment** endless new layouts;
the test environment runs the **ablation**; the **thought log** shows why each version of Karen
did what it did; and the **blink** gives Karen an information edge to test.

1. [The blink mechanic](#1-the-blink-mechanic) · built
2. [The endless maze](#2-the-endless-maze) · built, parked
3. [An agent test environment](#3-an-agent-test-environment) · built
4. [Ablation](#4-ablation) · built, results in
5. [The thought log](#5-the-thought-log) · built
6. [How she searches, sees and remembers](#6-how-she-searches-sees-and-remembers) · ideas (added 2026-11-05)
7. [Two versions of Karen](#7-two-versions-of-karen) · idea (added 2026-11-06); planned as 0.4.0 since 2026-11-10
8. [A style vector for each player](#8-a-style-vector-for-each-player) · idea (added 2026-11-11)
9. [Learned pacing](#9-learned-pacing) · idea (added 2026-11-11)

---

## 1. The blink mechanic

**The idea.** With your permission, the webcam watches your eyes, and when you blink, Karen
*dashes*. It's a horror instrument for wave dramaturgy, not a rung of the ablation ladder.

**The two dashes** ([`BlinkDash.cs`](../../Assets/!_Project/_Game/AI/Scripts/Decision/BlinkDash.cs)):
- **Close in.** She's in your view and far away. You blink, and she is suddenly much nearer:
  a jump of about 45% of the gap along her path to you (2.5–9 m), never ending closer than 6 m.
- **Vanish.** She's in your view. You blink, and she's gone. She reappears 8–16 m from where she
  last saw you, out of your sight, usually ahead of where you were heading (sometimes behind
  you), and waits there in silence. Come within 6 m and she steps out with a sting: that's the
  peak of the wave.

**The waves.** The Director now runs repeated waves: settle → build → spike → recover → build
→ … (`KarenDirector.UpdatePhase`).
- **Settle and recover:** no dashes at all, so the tension really drops between waves.
- **Build:** up to two close-ins, rising dread. If she's already near you, the wave's one
  vanish happens instead.
- **Spike:** the wave's one vanish. Close-ins only if the vanish is spent and she's far.
- **Crunch** (the end of the shift): close-ins, and a vanish if the wave hasn't had one.

When she steps out after a vanish, the wave breaks: the Director starts a recovery, and she
backs off.

**Fair, like every trick:**
- Each wave starts with her eye-flicker tell, at least 0.8 s before any dash.
- She only dashes while she can see you and you're looking her way.
- Never during a chase, never in recovery, and never to within 6 m, so a blink can't kill you.
  Only touch kills.

**How it works.**
- A small helper reads your eyes: Apple Vision on macOS, or MediaPipe
  ([`tools/blink`](../../tools/blink/README.md)). It sends the game one number per frame over
  your own computer (localhost).
- The game calibrates to your eyes (**F9**, 12 s) and allows for the helper's delay. A dash is
  instant, so it lands inside the ~300 ms a blink lasts. The simulated players blink too (a
  synthetic, human-shaped trace), so the dash runs in the eval harness.
- **B** blinks from the keyboard, with or without a camera.

**Rules we don't break.**
- It's an extra, never required: everything works with the camera off (dark rooms, no camera,
  accessibility).
- Opt-in, stays on your computer, never recorded, and the game says so plainly.

**Next.**
- A tiny model of our own (an eye-crop CNN, under 2 ms): label a few minutes of your own video
  automatically with MediaPipe, then train on that. Costs about nothing.
- A Windows helper (Windows has keyboard blink only for now).
- Running the model inside Unity (Sentis); the code is written, not switched on.

---

## 2. The endless maze

**The idea.** A store with no end: aisles generated forever from a seed.

**Built** in its own scene, and parked: seeded 25 m chunks, mostly loops, streamed around you
one chunk a frame, with the store's own shelves and products. How it works:
[DECISIONS.md](../agents/DECISIONS.md#4-the-endless-maze).

**Why it's worth it.** New players get a new store. AI agents can't memorise a map, so "beat
this level" becomes "beat this kind of level", which makes the test environment far more
convincing.

**The choice still to make.**

| option | what it means |
|---|---|
| **A separate game mode** | an "Endless" mode beside the normal career: survive as long as you can |
| **A new store every shift** | each shift takes a bounded piece of the maze from a seed. Karen's herding needs a map with edges, and this keeps one |
| **The store opens into it** | the normal store, with a door into the endless part |

**Still to build** for any of them: shoppers, Karen and tasks in the maze; aisle signs per
chunk; a floating origin (moving the world back near zero) for very long walks.

---

## 3. An agent test environment

**The idea.** Use Kehai to test AI agents. Most agent tests are text puzzles or single tasks.
Here the jobs get in each other's way (a spill while you restock, a queue while you mop) and the
clock never stops, which is exactly where today's agents fail.

**Built** ([`tools/eval`](../../tools/eval/README.md)):
- **What the agent sees:** the store as JSON and plain text, the way the player would (Karen only
  when in view).
- **What it can do:** 17 actions (walk to, pick up, place, mop, serve, bag the bin…), all done
  by the real player body, so walls, doors and stamina are real.
- **Speed:** headless, a 150 s shift in 5–15 s. The same seed gives the same first shift.
- **Scores:** jobs done, customers lost, spills left, waits, burnout, and a label for *how* the
  agent failed:

| failure | what it looks like |
|---|---|
| starvation | sticks to one job until time runs out |
| thrashing | switches job every few steps, finishes nothing |
| interference blindness | restocks while a customer gives up at the till |
| clock blindness | no time left for the walk to clock out |
| resource mismanagement | runs out of energy, loses sprint |
| false finish | clocks out thinking the jobs are done |

- **Players:** scripted bots, and Claude playing a shift ([`llm_agent.py`](../../tools/eval/llm_agent.py)).

**Next.** A results table across several AI models; a set of seeds never used while building
(so we don't tune to our own test); maybe a version that sees pixels instead of text.

---

## 4. Ablation

**What it is.** Turning off parts of a system to see which parts matter. Like taking one
ingredient out of a recipe at a time to find out what each adds. If the dish tastes the same
without it, it wasn't doing anything.

**For Karen** there are six versions, each changing exactly one thing from the one before. They play
the same shifts (same seeds) against the same simulated players, so any difference comes from
that one thing. (The blink dash isn't a rung: it's part of the Director, on every rung from C.)

| version | adds | the question |
|---|---|---|
| A | random patrol | the floor |
| B | fixed patrol routes | does structure beat chance? |
| C | the belief map (where you are, and where you aren't), goals, the Director and its blink dash | does guessing well help? |
| D | learning which tricks work, during a shift | does short-term learning help? |
| E | memory across shifts (the Ledger) | does long-term learning help? |
| F | a trained network picks the trick instead of D's learner (PPO, [`tools/rl`](../../tools/rl/README.md)) | does deep RL beat the bandit? |

`-karen-rung A`…`F` picks a version on the command line. F needs the ML-Agents package and a
trained model; it is built, but not yet trained or measured.

**What we found** (240 simulated shifts with players that react to her, 2026-10-16,
[KAREN_RESULTS.md](../agents/results/KAREN_RESULTS.md)):
- **The belief map** finds players about three times as fast as patrols (31 s against 85–101 s).
- **The fairness rules** were never broken.
- **Learning (D–E) still doesn't beat C.** It makes her a little calmer (mean panic −0.03 to −0.04)
  without tracking the Director's setpoint any better.
- **A first sign of tailoring.** At E her tactic mix differs by player about twice as much as at
  C, most of all against the held-out cautious player.

**Next.** Real players, and a trained rung F — before claiming learning helps.

---

## 5. The thought log

**The idea.** Karen writes down every decision she makes, as data:

```json
{ "t": 184.3, "believes": "aisle 7 (62%)",
  "options": [ { "tactic": "sweep_aisle_7", "score": 0.71 },
               { "tactic": "camp_coffee",   "score": 0.66 } ],
  "chose": "camp_coffee",
  "because": "the sweep failed twice this shift; she trusts it less now",
  "panic": 0.38, "target": 0.55 }
```

**Why.** It shows *why* one version of Karen beats another, not just that it does. It's the
best debugging tool for her. And players can see it: **F1** shows what she's thinking live,
**F2** scrubs back through the shift, and the performance review after each shift reads from it.

**How.** Kept in memory while you play, saved as one file per shift (`karen_logs/`). Structured,
not sentences, so it can be counted and charted; only `because` is free text.

---

## 6. How she searches, sees and remembers

Three ideas from the 2026-11-05 whiteboard ([notes](../agents/research/whiteboard-2026-11-05.md)). None is
built. Each would go into the [0.2.0 build plan](../agents/archive/0.2.0-build-plan.md) only once you pick it.

### 6.1 Search every spot, on purpose not perfectly

**The idea.** Alien: Isolation's search visits all of its nav points eventually, in a deliberately
imperfect order: it picks places at random and sometimes backtracks to one it has just checked.
Use that for Karen's patrol instead of always going to the place she hasn't visited for longest.

**Today.** Patrol scores every indoor region by time since her last visit (capped at 4 min), plus
belief, minus walking distance, plus a little randomness, and takes the best
([`TacticsMovement.cs`](../../Assets/!_Project/_Game/AI/Scripts/Decision/Tactics/TacticsMovement.cs),
`PatrolTactic.Plan`). The best score wins every time, so her route is close to the same each time.

**Why change it.** A perfect sweep is predictable, and a player who learns it can stay one region
ahead forever. Backtracking is what makes "she was just here" stop being safe.

**How it could work.**
- Keep a list of the spots she hasn't checked in this round.
- Pick the next one at random, weighted by the score she uses today (a softmax with a
  temperature), rather than always the top one.
- Now and then (say one pick in five) go back to a spot from the last two or three, if the belief
  there has grown again.
- When every spot is checked, start a new round.

**Example.** She checks aisles 3, 5 and the cold room, then goes back to aisle 3 because a noise
came from near it, then the stockroom.

**What it needs.** A temperature and a backtrack chance to tune; an ablation against today's
patrol (time to first detection, and how often a player can stay unseen for 60 s).

### 6.2 Room-by-room lights

**The idea.** Each room's lights go on and off on their own, not only by breaker circuit. You can
switch a room off to hide in it; she can too, as a trick, or to lie in wait.

**Today.** The store has three circuits, East, West and Back
([`LightsAndBreakers.cs`](../../Assets/!_Project/_Game/AI/Scripts/World/LightsAndBreakers.cs),
`CircuitOf`). "Back" is the stockroom, cold room, staff room, office, WC and backstreet all together.
Lights already stay in their own room for rendering (`RoomLighting.cs`), so the rooms exist.

**Why.** Light is already a term in how fast she sees you (`SightSensor.Score`: her darkness floor is
0.35). A switch per room makes darkness a choice you make and she reads, not just a breaker event.

**How it could work.**
- A wall switch per room (an `IInteractable`, like `BreakerSwitch`).
- A room switched off by you is evidence for her (a light she didn't switch off went off), so it
  reaches her belief as a clue, with a short delay.
- A room she switches off is a trick with a warning (the fairness rules still apply).

**What it needs.** Switch models (ask about materials), a light map per room, and a decision on
whether the breaker circuits stay.

### 6.3 Key spots that warm up (from Hello Neighbor), tied to the Ledger

**The idea.** Objects you use (a locker, a toilet cabin, the space behind a pallet), doors you go
through and routes you take each carry a weight. Every use raises it. The warmer a spot, the more
often she checks it, and the more likely she is to answer it: a door you always use gets blocked,
a route you always take gets a camera.

**Today.** The Ledger already remembers, per region, where you dwell and hide, and the routes you
take between jobs ([`KarenLedger.cs`](../../Assets/!_Project/_Game/AI/Scripts/Core/KarenLedger.cs):
`regions`, `routes`, `FillHabitPrior`, `ConcealmentRegions`). It feeds her starting belief, Ambush
and camera bolt-on. What's missing is the object and door level, and the visible answers.

**How it could work.**
- Add `spots` to the Ledger: an id (`locker_3`, `door_staff_2`), a use count and the last shift,
  fading the way the rest of the Ledger fades (rule 7: changing how you play works within two shifts).
- The search in 6.1 weights spots by warmth, so a warm locker is opened more often.
- A warm door or route offers a trick: block the door, bolt a camera on the route.
- Make it visible (pillar 3, "make it obvious"): the first time she answers a spot, the player
  should be able to tell why, for example a note on the locker or the camera's light.

**Example.** You hide in staff locker 3 in three shifts running. In shift 4 she opens it first
when she loses you in the staff room. You switch to the toilet cabins, and after two shifts the
locker has cooled.

**What it needs.** Ids on the hiding objects (they're in the scene, not in code yet), a fairness
check that a spot she answers still leaves the shift winnable, and an ablation of the Ledger with
and without spots.

---

## 7. Two versions of Karen

From the 2026-11-06 talk about how she learns. Not built, and not part of the learning plan: that
plan keeps one Karen who learns from every player and adapts to each one within a session. This
idea is a second reward on top of it, for later.

**The idea.** Two Karens with the same tricks and the same fairness rules, told apart only by what
they're rewarded for:

| | Killing machine | General (the game) |
|---|---|---|
| Rewarded for | catching you, the sooner the better | keeping your fear near a target that rises through the shift, plus a smaller reward for a catch |
| Pacing (the Director) | off, or nearly: no breathing room, she hunts whenever she can | on: builds tension, gives you breaks, saves the kill for tense moments |
| Tricks | the same | the same |
| Fairness | the same: she never knows where you really are | the same |
| For | a benchmark and a hard mode; "how good can a fair AI get" | the game |
| Measured by | catch rate, time to a catch | fear against its target; "scary" and "unfair" answers; how often players quit |

**Today.** There's one Karen. Her trick picker is rewarded by the change in your panic in the 20 s
after a trick (`KarenBrain.SettleRewards`), and the Director paces her toward a fear setpoint
([Karen.md §7.2–7.4](../agents/karen/Karen.md#72-tactic-selection-as-a-bandit)). The 0.2.0 ablation
found she catches the bots in 97–100% of shifts already
([KAREN_RESULTS.md](../agents/results/KAREN_RESULTS.md)), so today's Karen is closer to the killing machine.

**Why.** The killing machine shows how strong a fair AI can be, which is the portfolio showpiece,
and gives the learners a hard opponent to measure against. The general one is what makes the game
fun: a Karen who always kills is a short game.

**How it could work.**
- **One learner, two dials (the likely pick).** The learner learns two numbers per trick and
  situation: how much it scares, and how likely it leads to a catch (the approved
  [capture plan](../agents/archive/karen-capture.md)'s two heads). A version is only how much each number
  counts when she chooses. One training run serves both, and a difficulty slider in between comes
  for free.
- **Or two trained brains:** rung F trained twice, once per reward. Each is fully specialised, but
  it's twice the training, and rung F hasn't been trained even once.
- The Director's pacing is rules, not a reward, so the killing machine also needs its own Director
  setting.

**Example.** Same seed, same shift: the killing machine cuts you off at the first sighting and
catches you in 40 s. The general Karen loses you on purpose twice, blacks out the stockroom when
your fear dips, and catches you at the end, or lets you clock out shaken.

**What it needs.** The learning plan built first (her general and per-player learning); then the
second reward, a Director setting per version, and an ablation of the two against the same
players, the held-out cautious one included.


---

## 8. A style vector for each player

From the 2026-11-11 talk about a deeper ML layer. Not built, and not in the
[0.4.0 plan](../plans/0.4.0-build-plan.md): you asked for it to wait here.

> **Embedding.** *What:* a short list of numbers that sums something up.
> *In Kehai:* about 8 numbers that describe how you play.
> *Elsewhere:* Spotify's taste profile, which places you near listeners like you.

**The idea.** A small model reads your first few minutes (how you move, when you sprint, how often
you look behind, how you answer her chores) and writes about 8 numbers: your style. Her brain and
her prediction model read those numbers from the first shift.

**Today.** She knows you only through the Ledger, which fills slowly: she uses about 3.6 tricks a
shift. For most of your first shift she plays "everyone's" Karen.

**Why.** The Ledger says what worked on *you*. A style vector says who you play *like*, so she can
borrow from similar players at once.

| | The Ledger (built) | A style vector (this idea) |
|---|---|---|
| Says | what each trick did to you | which kind of player you are |
| Fills in | several shifts | a few minutes |
| Made by | counting | a trained model |
| Can be read by a person | yes | not directly |

**How it could work.**
- Train the model so that two halves of one player's shift land close together, and two different
  players land apart.
- The 0.4.0 bots have a hidden 14-number persona. A first test is free: can the model recover it
  from a few minutes of play?
- It ships in the model pack, and the vector stays on your computer beside the Ledger.

**Example.** Ninety seconds in, you've sneaked everywhere and checked behind you six times. Your
vector lands among the careful players, so she opens with slow tricks that work on them, not with
a chase.

**What it needs.** 0.4.0 built first (the personas, the dataset, the pack). Then real players in
numbers: a style learned from bots only describes bots.

## 9. Learned pacing

From the same talk. Not built, and not in the 0.4.0 plan.

**The idea.** The Director learns which tension curve suits each kind of player, judged by what
players do next: finish the shift, start another, or quit.

**Today.** One curve for everyone. The Director holds your panic near a target that rises through
the shift, by rule (a PI controller), and Basic is paid for staying near that target
([0.4.0 plan §5.4](../plans/0.4.0-build-plan.md#54-what-each-is-paid-for)). Nobody has checked
that this curve is the one people enjoy.

**Why.** "Fear near the target" is a stand-in for "a good night". The target itself is a guess.

**How it could work.**
- A few named curves (a slow burn, an early scare then calm, a steady climb), not free-form.
- A bandit picks a curve for each player and learns from what they do next, the same way she
  already picks tricks.
- With a style vector (§8), it becomes one choice per kind of player.
- The fairness rules and the 45 s recovery stay as code, whatever curve is picked.

**Example.** Players who quit in their first ten minutes mostly met a hunt before minute three.
For new careful players the Director learns to hold the first hunt back, and more of them finish
the shift.

**What it needs.** Real players in numbers, and a plain signal of a good night (finished the
shift, came back). Bots can't supply it: they never quit.
