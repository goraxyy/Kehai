# Private run reports

Standard-library Python tooling. Run modules from the repository root. Generated registers,
cards, HTML, recordings and fixtures must be **outside every git checkout**, including ignored
folders. New files use mode 0600 and new directories 0700. No Unity process is started.

On this Mac:

```sh
PY=/Users/kablanbekali/Developer/Kehai/tools/rl/.venv/bin/python
PRIVATE=/private/tmp/kehai-reports-demo
"$PY" -B -m tools.reports.fixtures "$PRIVATE"
"$PY" -B -m tools.reports.report --card "$PRIVATE/brain.json" --out "$PRIVATE/run.html"
"$PY" -B -m tools.reports.replay_cli "$PRIVATE/shift.json" \
  --decisions "$PRIVATE/trace.json" --model "$PRIVATE/brain.json" --out "$PRIVATE/replay.html"
```

These inputs are synthetic and labelled as such. Open the HTML locally; it has no CDN,
external images or network requests. The [status server](../status/README.md) can serve one
HTML file with authentication on loopback.

## Register existing commands

```sh
"$PY" -B -m tools.reports.register --register "$PRIVATE/runs.jsonl" run \
  --kind test --input path/to/config.json --card path/to/result-card.json \
  -- "$PY" -B path/to/existing_test_command.py
"$PY" -B -m tools.reports.register --register "$PRIVATE/runs.jsonl" list
"$PY" -B -m tools.reports.register --register "$PRIVATE/runs.jsonl" import /path/from/windows/runs.jsonl
```

Arguments after `--` are literal process arguments, never a shell command. Start and terminal
events share an id and include source commit/dirty state, computer, timestamps, explicit input
hashes, snapshot hash, seeds, exit status and optional result-card provenance. A missing result
card is unavailable, not an invented success. Import is idempotent; conflicting outcomes and
malformed JSONL fail. A last recorded `running` event does not prove a process is alive.
Interrupted/spawn-failed runs record exit codes 130/127. A killed wrapper may leave only a start
event. The short directory lock is never automatically stolen if stale.

`record`, `fit`, `train` and `tune` require `--manifest`; the gate rejects missing records,
unknown split metadata and held-out seeds/players/layouts before starting the command.
`fit`/`record` use the training split; `tune` uses validation. A minimal manifest is:

```json
{"learning_records":["record.json"],"careers":[{"seed":10000,"player":"efficient","shifts":[{"shift":1,"layout":"today"},{"shift":2,"layout":"today"},{"shift":3,"layout":"labyrinth"},{"shift":4,"layout":"brick-bond"}]}]}
```

Record paths resolve relative to the manifest. Additional configuration/model inputs must be
listed with repeated `--input`. The manifest is a declaration, not an audit of what arbitrary
child code actually reads. Existing learning commands are unchanged: use the wrapper or call
`append`, `latest`, `audit` explicitly; unwrapped commands do not become gated automatically.

## Render real evidence

```sh
"$PY" -B -m tools.reports.report --card /path/to/belief040/fit_report.json \
  --out "$PRIVATE/historical-fit.html"
"$PY" -B -m tools.reports.report --register "$PRIVATE/runs.jsonl" --run RUN_ID \
  --manifest /path/to/data.json --card /path/to/card.md --curves /path/to/curves.jsonl \
  --score /path/to/score.json --comparison /path/to/comparison.json --out "$PRIVATE/full.html"
```

Curves are JSONL rows such as `{"step":100,"loss":0.7}`. Metrics must be finite numbers or
null. TensorBoard event files need an explicit export; curves are never inferred from weights.
Historical JSON/Markdown cards retain negative results and provenance. Historical aggregate
validation values have no career standard errors or held-out benchmark claims. Missing records,
too few declared shifts and isolation failures are shown in the report.

Replay accepts actual `ShiftRecording` JSON (`frames: [[t,px,pz,...], ...]`) and recorded `plan`
geometry. Two inputs require the **same explicit seed, shift and layout**; old recordings
without layout metadata cannot be verified as pairs. Decisions need an optional sidecar:

```json
{"seed":20000,"shift":2,"model_sha256":"SHA256_OF_EXACT_PART_FILE","decisions":[{"t":1.2,"inputs":[0.1,0.2],"branch":0,"choice":1,"probabilities":[0.3,0.7]}]}
```

Use repeated `--decisions`/`--model` in recording order. Only an exact model hash and supported
feed-forward Swish brain export permit recomputed activations and analytic input derivatives.
The top three rank the absolute derivative of the **chosen head logit**; this is local
sensitivity, not causal attribution or a masked action probability explanation. A finite
difference test checks the derivative. Recorded probabilities are shown only when supplied and
normalized; they are never manufactured from logits. Recurrent/LSTM attribution is unavailable
without complete recorded state. C# summary metrics alone supply none of this decision data.

## Tests

```sh
"$PY" -B -m unittest discover -s tools/reports/tests -v
```

See [the integration note](../../docs/agents/integration/0.5.0-reports.md) for actual validation
counts and remaining integration limits.
