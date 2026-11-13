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
"$py" "$here/train.py" all --train "$rec/train/traces" --val "$rec/val/traces" --legs "$rec/train/legs.jsonl" --version "fit-$(date +%Y%m%d)"
echo "Then: the C# models and the Python ones on a fixed state (python3 tools/belief/make_fixture.py; the EditMode suite)."
