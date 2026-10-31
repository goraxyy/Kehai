# Reactive simulated players

> **Status:** built and merged (PR #62); the full ladder rerun with these players is PR #63 ([KAREN_RESULTS.md](../KAREN_RESULTS.md)). Kept for the record.

## Context

Learning can't beat hand-tuning in the ablation (bandit D/E, now policy F): the simulated
players don't react to Karen's tricks.
- They don't perceive her tells, PA lines, blackouts, HUD lies or dashes.
- Only the skittish bot reacts at all, and only by fleeing when it sees her.
- So the Panic Index barely moves after a tactic, and the reward is about zero.

Since #60 her touch kills, and the bots walk into her: 10 of 12 smoke shifts ended in a kill.

Goal: bots that perceive what a person would, get scared in profile-specific ways, show it
through the same body channels the Panic Index reads, and stay out of her reach.

Agreed: one PR, a short check (not a full ladder rerun), plus a fourth, held-out "cautious" bot.

## 1. What a bot can sense (the shared observation; the LLM agent gets it too)

`EnvWorld.Observe` / `KarenAsSeen` (`Eval/Scripts/EnvWorld.cs`) gain only what a person would
notice — never her internals:
- `heard`: Karen's sounds in the last 3 s, each with kind, distance, region and age.
  - Tells come from `KarenBrain.Told` (`Core/KarenBrain.cs`, already raised for every tell), which `KehaiEnv` subscribes to at reset.
  - A tell is audible within `TellFar` (60 m, as `KarenWorld.PlayTell` plays it). PA, HUD and blackout tells reach the whole store.
  - Footsteps keep the existing 12 m rule.
- `karen` gains `last_seen_s` and `last_seen_region`, plus two dash cues:
  - `closed_in`: seen again ≥ 3 m nearer within a blink.
  - `vanished`: in view, then gone across a blink.
- Already there: `lights_on`, `hud_tasks`, `pa_subtitle`.
- `EnvWorld.AsText` (the LLM prose) gets a line for heard tells and dash events.

## 2. Fear — new `Eval/Scripts/BotFear.cs` (plain C#, testable)

- **Stimuli:** seeing her (scaled by nearness and her mood), her footsteps, tells by category (light, sound, PA, HUD, space, social), a blackout, a close-in, a vanish, a reveal, a chase.
- **Fear rule:** `fear ∈ [0,1]`; each stimulus adds `amount × susceptibility × (1 − fear)`; fear decays with a per-profile time constant τ. A jump of more than 0.25 in one tick is a **startle**.
- **Per-seed jitter:** susceptibilities vary ±25% per seed, so no two careers' bots are identical.

| profile | most sensitive to | τ | how its fear shows |
|---|---|---|---|
| Efficient | her close by, HUD/time lies | 10 s | detours, hurries, rarely hides |
| Skittish | everything, darkness and sound most | 30 s | looks round, freezes, drops things, abandons jobs, hides |
| Reckless | being followed or chased only | 8 s | sprints, keeps working, never hides |
| **Cautious** (held out) | seeing her, dashes | 20 s | checks behind, avoids where she was, hides early, slower |

## 3. Reactions — through the existing verbs and body, i.e. the Panic Index's own channels

`KarenDirector.PanicIndex` reads sprint bursts, yaw jitter, path inefficiency, task abandonment,
freezing, drops, burnout and blinks. Each bot reaction lands on one of them:

| channel | reaction |
|---|---|
| yaw jitter | `Driver.lookJitter` = profile base + k·fear |
| sprint bursts | sprint when fear passes the profile's threshold |
| freeze | on a startle, `wait` 1–3 s (skittish, cautious) |
| task abandonment | on a big startle, `env.Cancel()` the job in hand |
| path inefficiency | job choice penalises jobs near her last-seen spot and recent tells (the cautious bot shuns those regions for 30 s) |
| drops | skittish only: on a big startle, drop what it carries (p 0.3) |
| hiding | above the hide threshold, the existing `Flee()` (far region, crouch). All profiles but reckless have one |

The skittish-only `Watch`/`Threatened` loop becomes one fear-driven monitor for every profile.
Fear decays, so bots go back to work and can still clock out.

## 4. Staying away from her, honestly

- **Avoidance needs sight:** `AgentDriver.AroundKaren` only steers when she's visible to the employee. A shared `EnvWorld.CanSee(...)` (the line-of-sight test `KarenAsSeen` already does) serves both.
- **No true position:** `Flee()` uses the observation's last-seen position instead of `KarenBrain.Instance.Body.Position`.
- **Source scan:** a test checks that `SimulatedPlayers.cs` and `BotFear.cs` never touch `KarenBrain`, except through the observation.

## 5. Wiring

- `PlayerProfile` gains `Cautious`. `AblationPlan`'s default profiles stay the three, so training (`tools/rl`, which uses the defaults) never sees it. It runs only with `-ablation-profiles …,cautious`.
- **New metrics** in `EpisodeMetrics`, each mean taken over the shift:
  - `bot_mean_fear`, `bot_startles`, `bot_hides`;
  - `karen_reward_mean_abs`: the mean |Δpanic| over her rewarded tactics, from `KarenStats.TacticRewards`. This is the signal size learning gets.
- `tools/eval/analyze_ablation.py`: `PROFILES` adds `cautious`, plus a small table of the reward signal per rung and profile.
- **Docs:** `tools/eval/README.md` (what bots sense, the held-out profile), `Karen.md` §13 (four profiles), `IDEAS.md` ablation "Next".

## Files

New:
- `Eval/Scripts/BotFear.cs`
- `_Tests/Editor/BotFearTests.cs`
- their `.meta` files

Changed:
- `Eval/Scripts/SimulatedPlayers.cs`
- `Eval/Scripts/EnvWorld.cs`
- `Eval/Scripts/KehaiEnv.cs`
- `Eval/Scripts/AgentDriver.cs`
- `Eval/Scripts/EpisodeMetrics.cs`
- `tools/eval/analyze_ablation.py`
- docs

Karen's own code is not touched.

## Verification (the short check)

- Offline compile (Unity's csc), then the full edit-mode suite in batch with the editor closed, including new `BotFearTests`:
  - per-profile ordering (skittish > efficient for tells; reckless lowest);
  - decay and startles;
  - per-seed variety;
  - reaction thresholds;
  - the source scan.
- **Ablation:** rungs C and E × 4 profiles (with cautious) × 1 career × 3 shifts = 24 shifts, about 30 min. Compared with the last run before this change (12 shifts, C and E), expect:
  1. **The reward signal is up:** `karen_reward_mean_abs` well above the old run's.
  2. **Panic differs by profile:** skittish highest, reckless lowest.
  3. **The tactic mix differs more across profiles:** higher Jensen–Shannon divergence on rung E than on C.
  4. **Fewer kills** than 10/12.
  5. **0 fairness violations**, and bots still finish jobs and sometimes clock out.
- Numbers go in the PR. `KAREN_RESULTS.md` isn't rewritten (that's the full rerun, later).
- Then one PR against `main`, merged after the check, and main pulled. No Claude attribution.
