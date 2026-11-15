# 0.5.0 build status

Source implementation is on **`0.5.0`**, in [PR #82](https://github.com/goraxyy/Kehai/pull/82).
The owner authorised building on 14 November. Nothing has been merged or trained in this build.

Unity is prepared at `~/Developer/Kehai/.claude/worktrees/0.5.0-build`, with game version
`0.5.0` and the store scene loaded. The editor defaults to **Testing** (Karen from shift 1);
the Gameplay build uses the approved Karen-free first shift. The original main folder is intact.

The plan's checkboxes measure their **full “Done when” conditions**. A box stays open when it
requires a played shift, trained model, Windows run or visual review, even if its code exists.

| Area | Implemented | Still to verify or supply |
|---|---|---|
| Five tricks | Sale cap 2→4; 20-second microphone repair; screen loop, room lights, freezer alarm, complaint; chores, bot actions, records and guide | Unity trick checks, fairness/stuck results and game feel |
| Adaptive bots | Fair encounter/escape history across shifts; versioned observations; per-mind recurrent state; export/parity support | Train the plain/recurrent arms once, then measure adaptation and human realism |
| Escape learning | Recorder-only truth; local counts committed at shift end; layout isolation and safe old-save migration; consent/split-gated central refit | Real consented data, enough escapes and held-out paired evaluation |
| Six layouts | C# topology, thin source shelf modules, shared low/tall boards, stable doors, scheduled shifts, reserved layouts blocked from training; native map/stock/navigation checks and generated maps | Visual shelf joins and bot/trick checks |
| Training reports | Private run register, reports, replay, supported network inspection and explicit missing-data messages | Live fitted models, recorded neural evidence and external private-page connection |
| Benchmark v1 | Frozen schedules/scores; paired career errors; hidden-run reservation; TCP adapter and actual SimulatedPlayer career handler | Compiled-player smoke test; trained players/Basic/Vital; full measured scoreboard |
| First shift | Tired/wry text, slow outline blinking, route prompts, optional skip; native Gameplay presence and shift-1 habit-save checks | Visual/readability check and human playthrough; wording can be edited later |
| Editions | One project, three stamped editions; four successful shifts; retries after death; training presentation guards | Real Mac/Windows player builds, headless startup, trainer connection and parallel throughput |
| Look Lab | Private slice exporter, three style controls, lighting/fog/effects and comparison capture | External storage/project, private art imports, shelf contact sheet and measured graphics budgets |
| Private summary | Local dashboard, truthful collectors, history, authenticated loopback serving and scheduler template | Real account data, remote private hosting and a week of observed history; no job installed |

Training/Testing builds can run the benchmark's **Gameplay career rules** without exposing an
evaluation endpoint in the retail Gameplay build. Missing trained models are unavailable,
never substituted with a weaker player under the same label. Shift 1 still records local habits.

Astra/high handled the ML and memory review; Sol/high handled mechanical UI, reports and
trick integration. All heavy training remains on Windows, after this source build.
The run-register wrapper is opt-in for legacy commands; the tool READMEs give registered
invocations. Existing raw commands are not automatically instrumented. Missing prediction,
calibration, human-reference and neural-state evidence remains unavailable in reports.

## Checks performed

- **Passed in Unity 6000.5.10f1: 542 / 542 native EditMode tests**, including short PlayMode
  lifecycle and record/playback tests. **10 mechanical scene cases passed** across the career
  schedule and six layouts; every catalogue product is stocked and every shelf stand reachable.
  [Native evidence and generated maps](../../../agents/integration/0.5.0-verification.md#native-scene-checks).
- **Passed: 131 Python/offline tests plus 20 subtests; 19 production career/guide tests; 74 production trick-policy checks.**
- **Five C# configurations compile:** editor with/without ML, Training+ML, Testing and Gameplay.
  Offline C# compilation uses installed Unity 6000.5 libraries without opening Unity. Editor
  configurations also compile the EditMode test sources; player configurations check edition
  branches. This compile check is separate from the native suite and is not a player build.
- Python/offline probes cover recurrent export parity and state isolation, bot observations,
  Ledger migration, escape staging/refit, split gates, report arithmetic, benchmark transport
  and dashboard authentication. Pure production career/guide and trick rules run under .NET.
- All six source layout footprints have reachable door aprons with a 0.3 m body clearance,
  and a bay for every ambient section. Cold fixtures and checkout sweets remain authored.
  This checker excludes the scene's walls, tills and collision proxies; it does not certify
  that the built scene is navigable.
- Local status collection and report rendering ran against actual available sources. Missing
  runs, account metrics and historical days remain unavailable; no growth was fabricated.

Detailed evidence and commands: [AI](../../../agents/integration/0.5.0-ai.md),
[editions/guide](../../../agents/integration/0.5.0-editions-guide.md),
[reports](../../../agents/integration/0.5.0-reports.md),
[tricks](../../../agents/integration/0.5.0-tricks.md), and the tool READMEs.

## Next checks, in order

1. Check all five tricks on all six layouts in played shifts. Review shelf
   joins and the guided first shift at the target window sizes.
2. Build Training, Testing and Gameplay from one clean commit on Mac/Windows. Verify training
   startup/connection, edition contents and the benchmark capability/one-career transport.
3. Create the separate private Look Lab once suitable storage is available. Review its slices
   and measure frame time/memory on both computers.
4. After 0.5.0, start the agreed long training/evaluation sequence manually on Windows. Keep
   the cautious player, careful corner, Rings and Split out of training and tuning.

There are **no new scope questions**. These are execution and review steps, not a request to
re-decide the approved design. No purchase, scheduler install, remote deployment or long
training was started. The local 0.5.0 project includes a private copy of the store assets;
the main project and human saves were preserved. Native checks used isolated verification saves.
