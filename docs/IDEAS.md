# Ideas

The ideas that make Kehai more than a store game, and what each still needs. Karen's full design
is in [Karen.md](Karen.md); the release to-do list in [RELEASE_PLAN.md](RELEASE_PLAN.md).

They fit together: the **endless maze** gives the **agent test environment** endless new layouts;
the test environment runs the **ablation**; the **thought log** shows why each version of Karen
did what it did; and the **blink** gives Karen an information edge to test.

1. [The blink mechanic](#1-the-blink-mechanic) · built
2. [The endless maze](#2-the-endless-maze) · built, parked
3. [An agent test environment](#3-an-agent-test-environment) · built
4. [Ablation](#4-ablation) · built, results in
5. [The thought log](#5-the-thought-log) · built

---

## 1. The blink mechanic

**The idea.** With your permission, the webcam watches your eyes, and when you blink, Karen
*dashes*. It's a horror instrument for wave dramaturgy, not a rung of the ablation ladder.

**The two dashes** ([`BlinkDash.cs`](../Assets/!_Project/_Game/AI/Scripts/Decision/BlinkDash.cs)):
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
  ([`tools/blink`](../tools/blink/README.md)). It sends the game one number per frame over
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
[DECISIONS.md](DECISIONS.md#4-the-endless-maze).

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

**Built** ([`tools/eval`](../tools/eval/README.md)):
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

- **Players:** scripted bots, and Claude playing a shift ([`llm_agent.py`](../tools/eval/llm_agent.py)).

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
| F | a trained network picks the trick instead of D's learner (PPO, [`tools/rl`](../tools/rl/README.md)) | does deep RL beat the bandit? |

`-karen-rung A`…`F` picks a version on the command line. F needs the ML-Agents package and a
trained model; it is built, but not yet trained or measured.

**What we found** (216 simulated shifts, [KAREN_RESULTS.md](KAREN_RESULTS.md)): the belief map
finds players about twice as fast as patrols (51 s against about 100 s), and the fairness rules
were never broken. **Learning (D–E) didn't beat C**: the bots don't react to her tricks, so
there's little for her to learn from. (That run's rung F was the old blink rung; it's gone.)

**Next.** The bots now react to her warnings and dashes, each profile in its own way, with a
held-out fourth (`tools/eval/README.md`). Next is the full ladder rerun with them, then real
players — before rung F is trained on anything worth learning.

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
