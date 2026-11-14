# The benchmark

Part of the [Kehai 0.5.0 plan](../0.5.0-plan.md), item 5. **A draft for you to approve** (Y5.2):
you asked for "a proper benchmark, so I could also track her progress", and for it to track all
four groups below from the start (Q37).

> **Benchmark.** *What:* a fixed set of tests, run the same way on every version, so the scores
> compare over time. *In Kehai:* every version of Karen plays the same players, on the same
> layouts and seeds, and her scores go on one chart per measure. *Elsewhere:* a car's crash-test
> rating, run the same way on every model, year after year.

1. [What it's for](#1-what-its-for)
2. [What it scores](#2-what-it-scores)
3. [What she plays against](#3-what-she-plays-against)
4. [The rules that keep it honest](#4-the-rules-that-keep-it-honest)
5. [Where you see it](#5-where-you-see-it)
6. [What exists already](#6-what-exists-already)

---

## 1. What it's for

- **Her progress:** one chart per score, every version of her on it (0.2.0, 0.3.0, today's
  planner, the trained Basic and Vital, later versions).
- **A fair comparison:** two versions are only compared on the same benchmark version, same
  players, same seeds, paired.
- **A result to show:** "a fair game AI, scored on a fixed benchmark, version after version" is
  the kind of evidence a behaviour-engine company looks for.

## 2. What it scores

A first draft (v1). The [0.4.0 plan's §11](../0.4.0-build-plan.md#11-how-its-judged) already
defines most of these; the benchmark fixes them in one place.

| Group | Score | Better is | Example (made up) |
|---|---|---|---|
| **The game's Karen** (pacing on) | catches a shift | inside your target, 0.20–0.35 | 0.28: about one shift in four ends with her catching you |
| | how close your fear stays to its target (setpoint error) | lower | 0.11 |
| | fairness violations | **always 0**; anything else is a failure | 0 |
| | stuck shifts (a shift that can't end) | lower | 2 of 400 |
| | trick variety (no trick over 40% of her picks) | inside the limit | the top trick 31% |
| **The hunting Karen** (Vital, pacing off) | time to a catch (counting shifts with no catch fairly: Kaplan–Meier) | shorter | 48 s |
| **Her guesses** | the chance she gives your true spot when she can't see you, and 5, 10 and 20 s after she loses you | higher | 1.5 times today's at 10 s |
| | calibration: when she says 70%, are you there 70% of the time? | closer | 66% |
| **Her learning you** | shifts 2–4 with your Ledger against the same shifts with it wiped | better with it | setpoint error 0.03 lower with your Ledger |
| **The players** | how human the bots are (the scorecard's "bot or human" check, and adaptation) | closer to people | a classifier tells bots from you 62% of the time (50% would be perfect) |
| **Her speed** | milliseconds per frame of her own code | inside 0.7 ms for her belief | 0.5 ms |

## 3. What she plays against

| Players | Seen in training? | Notes |
|---|---|---|
| Three scripted bots (efficient, skittish, reckless) | yes | the oldest baseline |
| The scripted cautious bot | **never** | held out, as always |
| Trained personas from the training ranges | yes | the adaptable bots of Phase 2 |
| Trained personas from the careful corner | **never** | held out |
| Real players, once there are some | **never** (split by player) | the most important group, later |

| Store | Seen in training? |
|---|---|
| today's store | yes |
| the eight training layouts | yes |
| **the two held-out layouts** | **never** |

- **Careers like the game:** 4 shifts, no Karen in the first, new layouts at shifts 3 and 4.
- **Seeds in two halves:** a practice half, used while building; a hidden half, run once per
  version of her.
- **Size and time:** about 400 shifts per version of her. On the Windows PC, with 8–12 copies at
  once, that's under an hour (an estimate; C5.6 measures it).

## 4. The rules that keep it honest

1. **Frozen definitions.** v1's scores, players, layouts and seeds are written down before its
   first run and never changed. A change makes v2, and v1 and v2 scores are never compared.
2. **The hidden half runs once per version of her,** and the result is kept whatever it is.
3. **Nothing is tuned on the hidden half, the held-out players or the held-out layouts.** A test
   enforces it (C5.6).
4. **Paired by seed, with standard errors by career,** the way every test in this repo is run.
5. **Bad scores are kept and shown.** A version that got worse stays on the chart.
6. **The bots' own scores are shown beside hers,** so a better score from weaker bots is visible.

## 5. Where you see it

- **The web report** (Phase 5): one chart per score, her versions along the bottom, with error
  bars; click a version for its report and replays.
- **The status page** (Phase 9): the latest version's headline scores, and how they moved.

## 6. What exists already

| Piece | Where | What the benchmark adds |
|---|---|---|
| The two ladders and their primary metrics | [0.4.0 plan §11](../0.4.0-build-plan.md#11-how-its-judged) | fixed as v1, run on every version, not once |
| Seeds and held-out players in code | `EvalSplits.cs` (0.4.0) | a hidden half, and held-out layouts |
| The scorecard of how human the bots are | `tools/learn/scorecard.py` (0.4.0) | adaptation, and shown beside her scores |
| Ablation analysis with standard errors | `tools/eval/analyze_ablation.py` | the scoreboard over versions |
| The testing order | [TESTING.md](../../TESTING.md) | one benchmark run per version, on Windows |

**Later:** the same suite could score other AIs playing the store (the agent test environment,
[IDEAS.md §3](../../IDEAS.md#3-an-agent-test-environment)), and a public version could sit on the
website. Neither is in 0.5.0.
