# Frozen Karen benchmark

Run from the repository root with Python 3.10+; standard library only. The machine-readable
definition, metric keys, directions and thresholds live in [v1.json](v1.json). Change the
benchmark version for future definition changes. No historical performance is manufactured.

```sh
PY=/Users/kablanbekali/Developer/Kehai/tools/rl/.venv/bin/python
PRIVATE=/private/tmp/kehai-benchmark-demo
"$PY" -B -m tools.benchmark.cli schedule --out "$PRIVATE/practice.json"
"$PY" -B -m tools.benchmark.cli run --fixture --home "$PRIVATE" --model FIXTURE \
  -- "$PY" -B -m tools.benchmark.fixture_adapter
```

The fixture command exercises the runner/register/scorer, labels every score synthetic, and
plays no game. All generated files, including schedules, must be outside every git checkout.

## Play a real suite later

With an explicitly started local evaluation build already listening:

```sh
"$PY" -B -m tools.benchmark.cli run --home /private/path --model IMMUTABLE_RELEASE_ID \
  --arm planner --env-port 5555
```

The default runner is `tools.benchmark.env_adapter`. It connects only; it never launches Unity.
An explicit adapter can follow `--`; its final arguments are `request.json rows.jsonl`.
The adapter must run every exact career and both Ledger conditions. A zero exit with missing
or incomplete rows is a failed run; its raw files remain available for offline inspection.

The protocol is specified in the [integration note](../../docs/agents/integration/0.5.0-reports.md).
Preflight requires protocol 1, evaluation-enabled Training/Testing build, Gameplay career
semantics, matching compiled commit provenance and every requested arm/player/layout. A missing
trained mind fails rather than replacing it with a heuristic. `--model-file path/to/part.json`
also requires the server's actual selected model SHA-256; absence is an explicit failure.
The parent C# hook is implemented as source; no live build run is claimed by this Python slice.

`--model` names an immutable release, not an attempt. `--half hidden` reserves one attempt per
definition/release/arm **before** execution; failed attempts consume it too. Keep the same private
home and claims file between attempts. This is a local procedural guard, not access control:
it cannot prevent changing labels, deleting claims or reading the public seed definition.

Each half has 50 four-shift careers (200 shift identities). Running retained and wiped gives
400 executed rows per half, 800 across both halves. Shift 1 has no Karen but records habits;
shifts 3 and 4 use the scheduled layouts. This exceeds the draft's rough 400-shift estimate
once the paired wiped condition is counted. The runner is serial; runtime and parallel speedup
have not been measured. Scripted profiles have fixed server personas; trained profiles carry
14 seeded traits. Only trait echoes tolerate float32 roundoff, absolute difference <= 1e-6
with exactly matching keys. All other execution identity checks use exact equality. Raw replies,
including `actual_persona`, remain in the run's `rows-evidence/` directory.

`schedule --purpose train|tune` produces safe source schedules, excluding cautious, careful
corner, Rings and Split; hidden schedules are rejected for these purposes. The bundled adapter
accepts only frozen `evaluate` requests. The [register wrapper](../reports/README.md) applies the
same split checks to explicit manifests before commands that fit or tune Karen.

## Result contract and scoring

Each JSONL row needs `model`, `arm`, `definition_sha256`, `seed`, `player`, `shift`, `layout`,
`edition:"gameplay"`, `ledger:"retained"|"wiped"`, and `metrics:{key:finite_number|null}`.
Unknown/duplicate row identities, wrong model/arm/hash, negative sentinels and nonfinite values
fail. Real and synthetic rows cannot mix. Missing measurements remain null.

The bundled adapter normalizes only actual EpisodeMetrics evidence:

- `caught` from the catch counter, `fairness_violations` from its explicit counter.
- `stuck` is the recorded `timed_out` flag: an overtime-cap proxy, not a measured pathfinding
  diagnosis. Bot jobs/minute counts six recorded job counters over actual `sim_seconds`.
- Panic RMSE needs a verified positive numeric `metric_samples.panic_setpoint_rmse`; a default
  zero without sample count remains unavailable. Trick share needs complete per-tactic counts.
- Vital survival needs `pacing:false` and either an actual nonnegative catch time or uncaught
  follow-up. A `time_to_catch_s:-1` sentinel is never a catch. Early censored follow-up may leave
  the 150-second restricted mean unidentified.
- Prediction probabilities, calibration, bot/human accuracy and belief timings remain unknown
  without dedicated evidence. Human classification also requires consented reference and
  held-out-person provenance. Summary counters do not recover trajectories or neural inputs.

Scores stay stratified by player and seen/held-out layouts. Only complete careers enter each
metric's mean and SE; fewer than two careers have no SE. Retained-minus-wiped RMSE pairs the
same seed/player/layout; model comparisons report second minus first on matching complete
careers. Vital uses Kaplan–Meier restricted mean with career jackknife SE. Fairness fails on
any observed violation; missing evidence cannot pass. Losing results remain visible.

```sh
"$PY" -B -m tools.benchmark.cli score /path/to/request.json /path/to/rows.jsonl --out "$PRIVATE/score.json"
"$PY" -B -m tools.benchmark.cli compare /path/to/first-run /path/to/second-run --out "$PRIVATE/comparison.json"
"$PY" -B -m tools.benchmark.cli board /path/to/first-score.json /path/to/second-score.json --out "$PRIVATE/board.html"
```

Offline scoring supports incomplete evidence and reports missing rows. Charts partition arms,
halves, definition hashes and synthetic status; incompatible results never share a chart.
Use repeated `--score` in the [report](../reports/README.md) or [status](../status/README.md).

## Tests

```sh
"$PY" -B -m unittest discover -s tools/benchmark/tests -v
```

The fake TCP integration binds ephemeral loopback only and covers full scheduling, float32
echoes, raw evidence and normalization. It proves transport plumbing, not gameplay, model
quality, fairness or runtime. See the integration note for measured test counts.
