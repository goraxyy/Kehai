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

**The idea.** With your permission, the webcam watches your eyes, and Karen can move while
they're shut.

**How it works.**
- A small helper reads your eyes: Apple Vision on macOS, or MediaPipe
  ([`tools/blink`](../tools/blink/README.md)). It sends the game one number per frame over
  your own computer (localhost).
- The game calibrates to your eyes (**F9**, 12 s), allows for the helper's delay, and lets Karen
  act inside the ~300 ms a blink lasts. Blinks are always about that long, so once one is seen
  ~120 ms in, the game can *predict* when your eyes open instead of racing to react.
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

**For Karen** there are six versions, each adding exactly one thing to the one before. They play
the same shifts (same seeds) against the same simulated players, so any difference comes from
that one thing:

| version | adds | the question |
|---|---|---|
| A | random patrol | the floor |
| B | fixed patrol routes | does structure beat chance? |
| C | the belief map (where you are, and where you aren't) | does guessing well help? |
| D | learning which tricks work, during a shift | does short-term learning help? |
| E | memory across shifts (the Ledger) | does long-term learning help? |
| F | the blink | does acting while your eyes are shut help? |

`-karen-rung A`…`F` picks a version on the command line.

**What we found** (216 simulated shifts, [KAREN_RESULTS.md](KAREN_RESULTS.md)): the belief map
finds players about twice as fast as patrols (51 s against about 100 s), and the fairness rules
were never broken. **Learning (D–F) didn't beat C**: the bots don't react to her tricks, so
there's little for her to learn from.

**Next.** Bots that react to her warnings, and then real players.

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
