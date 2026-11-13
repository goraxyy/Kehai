#!/usr/bin/env bash
# C4.10: fit, choose and export (about 4–5 h on the CPU with 4 workers, your go; no Unity, editor closed). In this order: her
# senses (S1, S2), frozen; the mirror map's numbers; P1; P2 and P3 (stage 1 from legs, stage 2
# through the filter). Each is chosen on the validation traces. Writes SenseWeights.g.cs,
# MirrorWeights.g.cs, PredictionWeights.g.cs, their cards and pack parts (Belief/Generated/).
#   tools/belief/train_all.sh [recordings folder]
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"; root="$(cd "$here/../.." && pwd)"
rec="${1:-$HOME/Library/Application Support/TokenLimit/Kehai/kehai_eval/belief040}"
py="$root/tools/rl/.venv/bin/python"; [ -x "$py" ] || py="$HOME/Developer/Kehai/tools/rl/.venv/bin/python"
# One stage at a time, each adding to $rec/fit_report.json: a stopped run starts again where it
# stopped (a finished stage is skipped). Delete the report to fit everything again.
report="$rec/fit_report.json"
version="fit-$(date +%Y%m%d)"
done_() { [ -f "$report" ] && "$py" -c "import json,sys; r=json.load(open(sys.argv[1])); sys.exit(0 if eval(sys.argv[2]) else 1)" "$report" "$1"; }
for stage in senses mirror p1 p2; do
  case $stage in
    senses) test='"senses" in r' ;;
    mirror) test='"mirror" in r' ;;
    p1) test='"p1" in r.get("motion", {})' ;;
    p2) test='"p2" in r.get("motion", {})' ;;
  esac
  if done_ "$test"; then echo "$stage: already done ($report)"; continue; fi
  "$py" "$here/train.py" $stage --train "$rec/train/traces" --val "$rec/val/traces" --legs "$rec/train/legs.jsonl" \
    --version "$version" --report "$report" ${KEHAI_FIT_OUT:+--out "$KEHAI_FIT_OUT"} > "$rec/fit_$stage.json"
done
"$py" -c "import json,sys; r=json.load(open(sys.argv[1])); print(json.dumps({k: r[k] for k in r if k != 'gate'}, indent=1, default=str))" "$report"
echo "Then: the C# models and the Python ones on a fixed state (python3 tools/belief/make_fixture.py; the EditMode suite)."
