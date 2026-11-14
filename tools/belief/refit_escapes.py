#!/usr/bin/env python3
"""Extract post-loss examples and refit the EXISTING P1 predictor, centrally.

python tools/belief/refit_escapes.py manifest.json --out /private/run --dry-run
python tools/belief/refit_escapes.py manifest.json --out /private/run

A manifest has version=1, optional dataset (the existing tools/learn dataset), and
sources: [{folder, player, split: train|val|test, bot: bool, consent: bool}]. Paths
are relative to the manifest. Each source folder is a single player's belief traces
and its map.json. Train/val/test are player-disjoint; cautious/careful-corner players
and Rings/Split layouts are test-only. Real players require explicit consent receipts
and complete belief traces: shift movies alone cannot reconstruct P1's inputs.
Dry-run extracts and evaluates hand P1 without fitting or writing model weights.
Actual refit uses train.fit_p1, replay parity, player-balanced weighted post-loss
log likelihood, and the existing PredictionWeights export. It never publishes.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import math
import os
from pathlib import Path
import sys

import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parent))
import features
import replay
import trace as traces
import train
sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "learn"))
import dataset


def load_manifest(path):
    path = Path(path).resolve()
    data = json.loads(path.read_text())
    if data.get("version") != 1 or not data.get("sources"):
        raise ValueError("expected manifest v1 with sources")
    gone = dataset.forgotten(str(path.parent / data["dataset"])) if data.get("dataset") else set()
    by_player, files, sources, careers = {}, set(), [], {}
    for item in data["sources"]:
        player, split = item.get("player"), item.get("split")
        if not player or split not in ("train", "val", "test"):
            raise ValueError("every source needs a player and train/val/test split")
        if dataset.safe_id(player) in gone:
            continue
        if not isinstance(item.get("bot"), bool):
            raise ValueError("each source must explicitly identify bot or human")
        if not item["bot"] and item.get("consent") is not True:
            raise ValueError(f"no explicit consent for {player}")
        if player in by_player and by_player[player] != split:
            raise ValueError(f"player leakage across splits: {player}")
        by_player[player] = split
        folder = (path.parent / item["folder"]).resolve()
        paths = traces.shifts(str(folder))
        if not paths:
            raise ValueError(f"no belief traces in {folder}; shift recordings alone are insufficient")
        # Validate the map now, before fitting or writing any artifacts.
        traces.load_map(str(folder))
        for p in paths:
            if p in files:
                raise ValueError(f"duplicate trace: {p}")
            files.add(p)
            header = traces.header(p)
            label = header.get("agent", "")
            if item["bot"]:
                career = (header.get("seed"), label)
                if career in careers and careers[career] != split:
                    raise ValueError("bot career leakage across splits (including aliases)")
                careers[career] = split
            layout = header.get("layout", "today")
            if layout not in {"today", "racetrack", "labyrinth", "fishbone", "rings", "split", "brick-bond"}:
                raise ValueError(f"unknown layout {layout}")
            if split != "test" and (dataset.always_held_out(player) or dataset.always_held_out(label) or layout in {"rings", "split"}):
                raise ValueError(f"held-out player/layout used for fitting or selection: {p}")
            if item["bot"] and not label:
                raise ValueError("bot trace missing agent identity")
        sources.append(dict(item, folder=str(folder), paths=paths))
    if not any(s["split"] == "train" for s in sources) or not any(s["split"] == "val" for s in sources):
        raise ValueError("need nonempty player-disjoint training and validation sources")
    return sources


def extract(sources):
    """Central answer dataset. No truths are fed back into the replay's belief state."""
    rows = []
    for source in sources:
        for path in source["paths"]:
            r = replay.Replay(traces.load_map(source["folder"]), motion="p1", use_hint=False).run(path)
            header = traces.header(path)
            for loss in r.losses:
                rows.append(dict(loss, player=source["player"], split=source["split"],
                                 trace=os.path.abspath(path), shift=header.get("shift"), seed=header.get("seed"),
                                 layout=header.get("layout", "today")))
    return rows


def report_model(sources, gains, window, weight):
    summary = {}
    weights = dict(features.HAND, p1=gains)
    for split in ("train", "val", "test"):
        people = {}
        events = censored = 0
        for source in sources:
            if source["split"] != split:
                continue
            for path in source["paths"]:
                r = replay.Replay(traces.load_map(source["folder"]), motion="p1", weights=weights,
                                  use_hint=False, post_loss_weight=weight, post_loss_seconds=window).run(path)
                group = people.setdefault(source["player"], {"hit": [], "log_p": []})
                if math.isfinite(r.log_p(True)):
                    group["log_p"].append(r.log_p(True))
                for loss in r.losses:
                    events += 1
                    if loss["next"] is None:
                        censored += 1
                    else:
                        group["hit"].append(float(loss["next"] in loss["top_three"]))
        rates = [float(np.mean(v["hit"])) for v in people.values() if v["hit"]]
        lps = [float(np.mean(v["log_p"])) for v in people.values() if v["log_p"]]
        summary[split] = {"top_three_next_region_at_loss": float(np.mean(rates)) if rates else None,
                          "top_three_se_by_player": float(np.std(rates, ddof=1)/math.sqrt(len(rates))) if len(rates)>1 else None,
                          "weighted_unseen_log_p": float(np.mean(lps)) if lps else None,
                          "players_with_escapes": len(rates), "losses": events, "censored": censored}
    return summary


def run(manifest, out, dry_run=False, post_loss_weight=4.0, post_loss_seconds=15.0):
    sources = load_manifest(manifest)
    # Always gate this exact input, never reuse a stale report's gate=true bit.
    train.gate([s["folder"] for s in sources])
    rows = extract(sources)
    for split in ("train", "val"):
        if not any(r["split"] == split and r["next"] is not None for r in rows):
            raise ValueError(f"no observed post-loss crossing in {split}; record actual escapes first")
    base = report_model(sources, features.HAND["p1"], post_loss_seconds, post_loss_weight)
    report = {"version": 1, "dry_run": dry_run, "trained": False, "predictor": "p1",
              "primary_metric": "top_three_next_region_at_loss (player-balanced; censored losses excluded)",
              "objective": {"post_loss_weight": post_loss_weight, "seconds": post_loss_seconds},
              "baseline": base, "exported": False,
              "inputs": [{"file": p, "sha256": hashlib.sha256(Path(p).read_bytes()).hexdigest()}
                         for s in sources for p in [str(Path(s["folder"])/"map.json"), *s["paths"]]]}
    out = Path(out)
    if not dry_run:
        player_keys = {os.path.abspath(p): s["player"] for s in sources for p in s["paths"]}
        options = dict(post_loss_weight=post_loss_weight, post_loss_seconds=post_loss_seconds, player_keys=player_keys)
        gains, _ = train.fit_p1([s["folder"] for s in sources if s["split"] == "train"],
                              [s["folder"] for s in sources if s["split"] == "val"], {"id": "s0"}, options)
        scores = report_model(sources, gains, post_loss_seconds, post_loss_weight)
        report.update(trained=True, candidate=scores, gains=gains)
        # Select only on validation; test is a final descriptive report, never an input to this gate.
        before, after = base["val"], scores["val"]
        improves = after["weighted_unseen_log_p"] > before["weighted_unseen_log_p"]
        no_top3_regression = after["top_three_next_region_at_loss"] >= before["top_three_next_region_at_loss"]
        if improves and no_top3_regression:
            numbers = train.prediction_numbers(dict(features.HAND, p1=gains))
            card = {"version": "escape-refit", "chosen": "p1", "validation": scores["val"],
                    "manifest_sha256": hashlib.sha256(Path(manifest).read_bytes()).hexdigest()}
            train.write_weights(str(out / "candidate"), "PredictionWeights", numbers, card,
                                train.table_cs("PredictionWeights", "PredictionWeights", numbers, "escape-refit"))
            report["exported"] = True
    out.mkdir(parents=True, exist_ok=True)
    (out / "escapes.jsonl").write_text("".join(json.dumps(r, sort_keys=True)+"\n" for r in rows))
    (out / "escape_refit.json").write_text(json.dumps(report, indent=2, allow_nan=False)+"\n")
    return report


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("manifest")
    parser.add_argument("--out", required=True)
    parser.add_argument("--dry-run", action="store_true")
    parser.add_argument("--post-loss-weight", type=float, default=4.0)
    parser.add_argument("--post-loss-seconds", type=float, default=15.0)
    args = parser.parse_args(argv)
    report = run(args.manifest, args.out, args.dry_run, args.post_loss_weight, args.post_loss_seconds)
    print(json.dumps(report, indent=2, allow_nan=False))


if __name__ == "__main__":
    main()
