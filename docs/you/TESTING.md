# Testing and training left for 0.4.0

Updated 2026-11-14. What still has to run before 0.4.0's two Karens and players can be claimed, in
order, with time on the Mac and on the Windows PC. Every run waits for your go and for **0.4.1 to
merge** (your call, 14 November). The commands are in
[the plan, §12](plans/0.4.0-build-plan.md#12-the-work-phase-by-phase).

## Testing is not training

| | Training | Testing |
|---|---|---|
| **What it is** | A model plays many shifts and changes its own numbers to do better | A model that's finished plays shifts it has never seen, and nothing changes; you only measure |
| **How Kehai uses it** | The Employee bot, Vital and Basic learn by playing (millions of decisions); her senses and P1 are fitted to recordings | The belief tests, the two ladders (catch and fear), the exploit check, the trick check, the C# suite |
| **Real-world example** | A spam filter learning from emails people marked as spam | Checking that filter on a fresh week of email it never saw |

- **Training is slow** (days to weeks). **Testing is quick** (minutes to nights).
- **The held-out players are only for testing**, once. Testing on them twice, or tuning after
  seeing them, makes the result meaningless.
- **Fitting** (C4.10) is a small kind of training: a few numbers, hours, not days.

## Why everything waits for 0.4.1

0.4.1 changes what Karen and the players do:
- her sight (twice the reach, twice as fast, eye-to-eye cover);
- six store cameras, and a camera trick that feeds her belief;
- two new tricks, Camera watch and Camera break, which change the brains' outputs;
- three new chores (the PA announcement, the camera fix, 20 s holds), which change the bots' actions.

Anything trained or tested before then would be about a different game.

## The list, in order

**Times:**
- **The Mac** is measured where it says so, otherwise estimated.
- **Windows** isn't measured. It assumes a 16-core PC running 8 copies at once, and that the training
  player connects there. On the Mac it doesn't yet: [HANDOVER §5](../agents/HANDOVER.md#5-whats-next-in-order).
- **"You"** marks your own time.

| # | What | Kind | Mac | Windows | Necessary? | Why it matters |
|---|---|---|---|---|---|---|
| 1 | **Check the merge:** the C# suite, the trick check, the default game against `main` | Testing | 15 min (measured) | 15 min | **Yes** | Nothing broke when the two branches met |
| 2 | **New baseline:** 0.3.0's ablation run again on the merged game | Testing | 66 min (measured, 12 Nov) | ~30 min | **Yes** | 0.4.1's eyes make the 12 November baseline out of date; every later result is compared with this |
| 3 | **You play 5–10 shifts** (Y1.1), then I score them (C1.4) | Testing | 1 h for you + minutes | the same | **Yes**, for the bots | The only human reference the bots are judged against |
| 4 | **Train Employee-1** (C2.7): 20,000 demos, then 1 M decisions | **Training** | ~5 h + **~11 days** (measured speed) | ~1–2 days | **Yes**, for "players that feel real" | Karen trains against these bots; without them she trains against the scripted four |
| 5 | **Record her belief data again** (C4.9) with Employee-1 | Recording | ~80 min (measured) | ~40 min | **Yes** | Tonight's fits used the stand-in bot and the old eyes |
| 6 | **Fit again** (C4.10), with P1's speed range widened, and **find out why P2 and P3 lost** | Fitting | 7–8 h (measured 7 h 41 min) | ~4 h | **Yes** | P1 was held back by its bound; P2/P3 lost even to today's step |
| 7 | **The belief tests** (C4.11): senses, mirror map and prediction on the held-out players, then the models freeze | Testing | 3–5 h | ~2–3 h | **Yes**, once | The real test of tonight's "S1 and P1 win": 95% intervals by career |
| 8 | **Train Vital-1 and Basic-1** (C5.9), two runs each | **Training** | 7 h to 6 nights per run, 4 runs (not measured: her speed falls as she learns) | ~1.5 days in all | **Yes** | The two trained Karens themselves |
| 9 | **Rounds 3 and 4** (C5.10): Employee-2, then Vital-2 and Basic-2 | **Training** | ~1–2 weeks | ~2–3 days | Optional | A second round, each side against the other's newer self. Without it the claim is weaker, not wrong |
| 10 | **The two ladders** (C6.2): catch (Vital against the hand-built hunter) and fear (Basic against today's game) | Testing | 2 nights | ~1 night | **Yes**, once | The headline results: is Vital faster to catch, does Basic hold fear closer to its target |
| 11 | **The exploit check** (C6.3): a fresh Survivor trained to beat each frozen Karen | Training, then testing | ~3 days each, 6 days (300,000 decisions at the measured Employee speed) | ~10 h each | **Yes** for Vital, nice to have for Basic | A single cheap trick that beats her (a spot she never looks) is a failure to fix |
| 12 | **You play both** (Y6.1), and **decide what ships** (Y6.2) | Testing | a few shifts each for you | the same | **Yes** | What it feels like to play against them |
| 13 | C0.2: find 0.3.0's batch slowdown in the Profiler; C0.3: speed with two copies | Measuring | 1–2 h; 20 min | the same | No | Speeds up everything above. The slowdown multiplies every run by up to 3.6 |

## Totals

| | Mac | Windows (8 copies) |
|---|---|---|
| Testing only (1–3, 5, 7, 10, 12) | about 1 day of machine time + 2 nights, plus your shifts | about 1 day |
| Fitting (6) | 7–8 h | ~4 h |
| Training (4, 8, 9, 11) | **about 5–7 weeks** | **about 1.5–2 weeks** |
| **All** | **about 6–8 weeks** | **about 2 weeks**, with the Mac doing 5–7 |

**Suggested split:**
- **The Mac:** the light, exact jobs (1, 2, 5, 6, 7, the C# suite).
- **Windows:** training (4, 8, 9, 11) and the ladders (10).
- **Before Windows can train,** the training player has to connect. On the Mac it floods
  TextMeshPro errors with no graphics device, on `main` too. Windows has a real GPU, so it may not
  have the problem. Check that first, in about 20 minutes (13).

## Will 0.5.0 make this out of date?

**Only if its 10 store layouts are approved.** [The 0.5.0 plan](https://github.com/goraxyy/Kehai/pull/82)
isn't approved to build. Two of its ideas would change what's trained:

| 0.5.0 idea | What it would redo |
|---|---|
| 10 maze layouts, maybe a new one each shift | All training (4, 8, 9, 11), the recordings and fits (5, 6), and the tests on the new layouts (7, 10) |
| Bots as agents | Employee-1 (4) and everything trained against it (8–11) |
| "Cheat training" on real players' routes | Adds to the fits (6); it needs real players first |

- **If the layouts are likely:** do the quick testing (1–3) after 0.4.1 merges, and hold the training
  (4, 8–11) until the layouts exist. Training on one store and then switching to ten wastes the weeks.
- **If they aren't:** run the whole list after 0.4.1.
