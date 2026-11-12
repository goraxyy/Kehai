# Karen, in plain words

One page. Every number, file and test is in the full design,
[`docs/agents/karen/Karen.md`](../agents/karen/Karen.md).

**What she is.** The store's AI, with a body. She never knows where you are. She only uses what
she sees, hears and finds, and she acts on her best guess.

```
 what she senses ─────► where she thinks you are ─────► what she does ─────► what you do
 (eyes, ears, traces,   (the belief map: a chance       (roam, check, search,      │
  cameras, customers)    for every 1.5 m cell)           hunt, or a trick)         │
         ▲                                                                         │
         └──── the Director paces it, and the Ledger remembers you ◄───────────────┘
```

## Her parts

| Part | What it does | Example | Detail |
|---|---|---|---|
| Senses | sight that builds up, hearing, traces you leave, her cameras, what customers tell her | a wet footprint says "you were near here a moment ago" | [§3](../agents/karen/Karen.md#3-perception-the-sensorium) |
| The belief map | a chance for every cell that you're there. It spreads while she can't see you, and sharpens when she senses something | she looks down an aisle, sees nothing, and that aisle's chance drops | [§4](../agents/karen/Karen.md#4-belief-where-it-thinks-you-are) |
| Deciding | picks an action, then a trick, then the steps to carry it out | "Flush with Blackout": cut the lights where she thinks you are | [§6](../agents/karen/Karen.md#6-thinking-the-deliberation-loop) |
| Tricks | 15 live ones. Most leave you a chore | Fog: you see 2 m until you reset the fridge box | [§17](../agents/karen/Karen.md#17-karen-030-the-tricks-come-back) |
| The Director | paces the shift: builds tension, then gives you room | no big trick for 45 s after a chase | [§2](../agents/karen/Karen.md#2-anatomy-three-minds) |
| The Ledger | remembers you between shifts: your routes, your hiding spots, which tricks worked on you | you always hide in the lockers, so she checks them first | [§7](../agents/karen/Karen.md#7-adaptability) |
| The fairness rules | ten rules in code, checked by tests | she can't read your position, and a shift can always be won | [§9](../agents/karen/Karen.md#9-the-fairness-contract) |

## Is she machine learning?

Not yet. Today she is hand-built probability and planning, plus a small picker that learns which
tricks work on you. **Nothing has been trained.** The
[0.4.0 plan](plans/0.4.0-build-plan.md) trains three things: where you go when she can't see you,
the bots that play against her, and two versions of her brain.

## Her versions

| Version | What changed | State |
|---|---|---|
| 0.2.0 | her eyes, the Hunt, guesses at your route, two new tricks | merged |
| 0.3.0 | the tricks rebuilt; no new chore after closing; she finishes what she starts | merged |
| 0.4.0 | two trained Karens: Vital (paid for catching you) and Basic (paid for pacing your fear) | being built |

## How she's tested

Simulated players run seeded shifts against each version of her, in pairs, and one kind of player
(the cautious one) is never used for tuning. What the tests found, bad results included:
[`KAREN_RESULTS.md`](../agents/results/KAREN_RESULTS.md).
