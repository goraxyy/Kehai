# What still needs an answer before 0.5.0 is built

Latest owner review, 14 November. This page supersedes older waiting lists in the plan.

## Settled

- Q1: long training happens after 0.5.0; remove the vertical-slice prerequisite.
- Q8: Screen loop, Room lights, Freezer alarm, The complaint. Do not ask again.
- Layouts: keep Racetrack, Labyrinth, Fishbone, Rings, Split, Brick bond. Park the other four.
- Look: realistic store and lighting, with simple low-poly products; materials wanted.
- Q14: engineering owns the shelf investigation and remodel proposal; don't ask the owner to
  explain the kit. The proposed joint appearance still needs their design approval.
- Game: 4 shifts; no Karen in Gameplay shift 1, but she is in Testing shift 1. The store records
  habits either way. Layout changes at shifts 3 and 4. Text-only guidance on shift 1.
- Cheat training: central refit plus local escape counts, committed at shift end, without telling
  the player about those counts. Real uploads remain consented as planned in playtest v2.

## Q9, expanded again: how capable should the ML players be in this release?

An **agent** observes, chooses an action and sees what happened. Kehai's ML-Agents bots already
qualify: they pick jobs and routes, much as a racing game's AI picks its line. Calling them
agents alone adds nothing; better memory and better evidence of realism are the actual work.

| Choice | What is built | Speed, cost and realism |
|---|---|---|
| **A. Adaptive trained bots now (recommended)** | keep scripted bodily fear/reflexes, train decisions; add memory of recent sightings and a record of her across shifts; fit personas to your sessions and later real players | fast local inference, no per-shift API bill; needs additional Windows training and comparison against people |
| B. Existing 0.4.0 trained bots first | train the approved persona/choice system, keep its scripted habits; learned memory comes later | less 0.5.0 work; adaptation remains largely scripted |
| C. Human imitation first | wait for enough consenting player sessions, train bots to copy their choices, then improve survival in simulation | closest direct link to human behaviour; delays training and needs enough data, not a promise that 100 players automatically suffices |

**Concrete A example:** a bot remembers hearing Karen near the cold room, takes another route
this shift, then avoids the clock approach where she caught it in the previous shift. It still
only knows what it could see/hear, and its fear reactions cannot be trained into a poker face.
The realism scorecard compares routes, task ordering, reactions and adaptation with human shifts.

**Current answer:** A in substance (both forms of memory previously selected). Reopened at the
owner's request: confirm A, or change scope to B/C. This is the ML decision needed before building.

## Q10, expanded again: does any outside language model play the game?

A **language-model player** reads a description and chooses an action via a paid service. In
Kehai this would be `llm_agent.py`, unlike a small trained network running on the PC; an everyday
example is a chat assistant using tools to perform a task.

| Choice | Effect |
|---|---|
| **A. None (current answer; recommended for this scope)** | Employee bots and human testers play; no AI subscription/API needed per bot shift |
| B. Optional final judge | a separate language model plays a few finished-game shifts, never supplies training/tuning data; requires a provider, credentials and measured budget |
| C. Language model steers the training bots | API latency and cost in training; redesign of inputs/rewards; not recommended for millions of decisions |

You explicitly rejected Claude-played players. That remains binding unless changed; we are not
substituting another paid provider silently. Choosing A still permits sophisticated AI/ML bots
and ordinary ML training compute. It does not remove the code-writing assistants or dashboard
summaries. No need to choose an API budget with A.

## Three other decisions before the full build

1. **Shelf remodel:** accept shared lower boards plus a tall upper extension and capped mixed-height
   joints as the prototype direction, or prefer separated end-capped units? See the measured
   [proposal](shelf-remodel.md). Recommend shared lower frame. Later approve prototype pictures
   before rebuilding all private art.
2. **Game layout pair:** recommend Labyrinth for shift 3 and Brick bond for shift 4. Recommend
   training on Racetrack/Labyrinth/Fishbone/Brick bond, holding out Rings/Split for the benchmark.
   The owner may choose another pair; reserve held-out layouts after that choice. Today's store
   remains an additional baseline. Held-out means never used to train or tune, like an exam's
   unseen questions; the game pair should not accidentally become tuning on the exam maps.
3. **Scope approval:** approve this plan including the single Karen ML PR (adaptive bots, escape
   model, cross-layout training and benchmark), once Q9/Q10 and the two design choices are settled.
   This is not permission to launch long training before 0.5.0 is built.

## Engineering decisions we should resolve, not send back as owner questions

- Resolve every prefab transform, real thickness and connector width. No solid-square shelf
  collisions; validate sight and navigation against actual geometry.
- Finish the six-layout schedule and saved-place migration; preserve old saves or clearly
  version/migrate their layout keys rather than reusing old region names for new places.
- Keep training on game-like 4-shift careers; mark Testing's Karen-on-shift-1 sessions separately
  so training/benchmark reports do not confuse those conditions.
- Pre-register benchmark definitions, held-out splits and failure thresholds before test runs.
  Freeze versions; don't turn repeated hidden-test feedback into tuning.
- Check four-shift trick availability. Old unlocks at shifts 8–10 cannot remain unreachable;
  propose a four-shift schedule in the ML PR while preserving the currently disabled unlock table.
- Calibrate headless Windows throughput and memory before choosing 8–12 parallel environments.
- Compare real-time lights against layout-specific bakes; verify Blackout/Room lights can remove
  illumination. Camera effects need controls and a neutral baseline, then Mac measurements.
- Verify a feasible authenticated/private dashboard update mechanism before promising automatic
  writes to a chat page. If unavailable, document the limitation and use the already-discussed
  private generated-report option for review rather than invent an integration.

Old-build deletion, moving data and account setup remain separate permissions/setup tasks. They
need not block ordinary code work. No asset movement or deletion is authorised by this plan.
