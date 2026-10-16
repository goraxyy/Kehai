#!/usr/bin/env python3
"""Train the tiny eye-state CNN and export it to ONNX (IDEAS.md "Own training path").

Input:  1x24x24 grayscale eye crop, values 0..1, rows top-down   (name "eye")
Output: one logit, sigmoid = probability the eye is shut           (name "closed_logit")

The same contract is used by blink_server.py --method cnn and by SentisBlinkSource.cs, so
one exported file works in both. ~6k parameters; trains in a minute or two on a laptop CPU.

Honest numbers: whole sessions (or whole people, --split-by person) go to train, validation
or test, never single frames, since neighbouring frames at 30 fps are near copies of each
other. It learns from a teacher (a helper's reading, calibrated per session) plus your own
labels where a training session has them; the best epoch is picked on the validation
sessions; the numbers it reports come only from the test sessions, scored against your labels
by eval_blink.py, next to the teachers on the same frames. Which sessions went where is
written beside the model (<out>.json), so eval_blink.py never scores it on what it saw.

    python train_eye_cnn.py --data eye_dataset --epochs 25 --out eye_cnn.onnx
"""
from __future__ import annotations

import argparse
import inspect
import json
import sys
import time
import warnings

import numpy as np

import blink_data as bd
import eval_blink as ev

TEACHER_ORDER = ("blendshapes", "ear", "vision")   # vision comes from a second camera stream


def build_model():
    import torch.nn as nn

    return nn.Sequential(
        nn.Conv2d(1, 8, 3, padding=1), nn.BatchNorm2d(8), nn.ReLU(), nn.MaxPool2d(2),     # 12x12
        nn.Conv2d(8, 16, 3, padding=1), nn.BatchNorm2d(16), nn.ReLU(), nn.MaxPool2d(2),   # 6x6
        nn.Conv2d(16, 32, 3, padding=1), nn.BatchNorm2d(32), nn.ReLU(), nn.AdaptiveAvgPool2d(1),
        nn.Flatten(), nn.Dropout(0.2), nn.Linear(32, 1))


def augment(x):
    """Brightness/contrast jitter, a horizontal flip (left and right eyes look alike
    mirrored) and a small shift, so the model survives a different room or camera."""
    import torch

    b = x.shape[0]
    gain = 1.0 + 0.3 * (torch.rand(b, 1, 1, 1) - 0.5)
    bias = 0.15 * (torch.rand(b, 1, 1, 1) - 0.5)
    x = (x * gain + bias).clamp(0, 1)
    flip = torch.rand(b) < 0.5
    x[flip] = x[flip].flip(-1)
    dx, dy = [int(v) for v in torch.randint(-2, 3, (2,))]
    return torch.roll(x, shifts=(dy, dx), dims=(-2, -1))


# ---- splits --------------------------------------------------------------------------------------


def assign_splits(sessions, by: str = "auto", test=None, val=None):
    """Whole sessions (or people) to train / val / test. Defaults: the test set is the latest
    labelled quarter, validation the next latest (labelled if possible), training the rest.
    Returns ({split: [session ids]}, by, [notes])."""
    people = sorted({s.person for s in sessions})
    if by == "auto":
        by = "person" if len(people) >= 3 else "session"
    key = (lambda s: s.person) if by == "person" else (lambda s: s.id)
    units = sorted({key(s) for s in sessions}, key=lambda u: min(s.id for s in sessions if key(s) == u))
    labelled = [u for u in units if any(s.labelled for s in sessions if key(s) == u)]
    notes = []
    if by == "session" and len(people) > 1:
        notes.append(f"{len(people)} people, split by session: the same person can be in train and test, "
                     "so this measures new sessions, not new faces (--split-by person with 3+ people)")

    if test or val:
        test, val = list(test or []), list(val or [])
        for u in test + val:
            if u not in units:
                raise SystemExit(f"--test/--val: no {by} {u!r} (have {', '.join(units)})")
        if set(test) & set(val):
            raise SystemExit("--test and --val overlap")
    else:
        if len(units) < 3:
            raise SystemExit(f"need at least 3 {by}s to train, pick a model and test it on unseen data "
                             f"(have {len(units)}); record more with record_dataset.py")
        n_test = max(1, round(0.25 * len(units)))
        test = labelled[-n_test:] if labelled else units[-n_test:]
        rest = [u for u in units if u not in test]
        n_val = max(1, round(0.15 * len(units)))
        val = [u for u in rest if u in labelled][-n_val:]
        for u in reversed(rest):
            if len(val) < n_val and u not in val:
                val.insert(0, u)
    train = [u for u in units if u not in test and u not in val]
    if not train:
        raise SystemExit("nothing left to train on; record more sessions")
    if not any(u in labelled for u in test):
        notes.append("no labelled session in the test set, so there's no accuracy to report, only agreement "
                     "with the teacher. Label one with label_blinks.py")
    ids = lambda chosen: [s.id for s in sessions if key(s) in chosen]
    return {"train": ids(train), "val": ids(val), "test": ids(test)}, by, notes


# ---- what each crop should read ---------------------------------------------------------------


def targets(session: bd.Session, teacher: str):
    """Per frame, 0 open .. 1 shut, NaN = don't use: the teacher's reading scaled by the session's
    own calibration, replaced by your labels wherever you labelled. Returns (target, from_you)."""
    raw = session.signals[teacher].astype(np.float64)
    o, s, _ = ev.calibrate(np.nan_to_num(raw, nan=0.0), session.t, session.phase)
    y = np.clip((raw - o) / (s - o), 0.0, 1.0)
    mine = np.zeros(session.n, bool)
    if session.labels is not None:
        lab = session.labels
        mine = lab.reviewed & ~lab.ignore
        y[mine] = lab.closed[mine]
        y[lab.reviewed & lab.ignore] = np.nan
    return y.astype(np.float32), mine


def crops(sessions, teacher: str):
    xs, ys, mine = [], [], []
    for s in sessions:
        y, from_you = targets(s, teacher)
        for eye in (0, 1):
            use = s.eye_ok[:, eye] & np.isfinite(y)
            xs.append(s.x[use, eye])
            ys.append(y[use])
            mine.append(from_you[use])
    x = np.concatenate(xs).astype(np.float32)[:, None] / 255.0
    return x, np.concatenate(ys), np.concatenate(mine)


def f1_at_half(p, y):
    truth, guess = y >= 0.5, p >= 0.5
    tp = int((truth & guess).sum())
    precision = tp / max(1, int(guess.sum()))
    recall = tp / max(1, int(truth.sum()))
    return precision, recall, 2 * precision * recall / max(1e-9, precision + recall)


def export_onnx(model, path: str) -> None:
    """Opset 13 through the TorchScript exporter, which Sentis reads. PyTorch 2.9+ defaults to
    the torch.export one (newer opsets, and it needs onnxscript), so ask for the old one."""
    import torch

    kwargs = dict(input_names=["eye"], output_names=["closed_logit"],
                  dynamic_axes={"eye": {0: "n"}, "closed_logit": {0: "n"}}, opset_version=13)
    if "dynamo" in inspect.signature(torch.onnx.export).parameters:
        kwargs["dynamo"] = False
    with warnings.catch_warnings():
        warnings.simplefilter("ignore")
        torch.onnx.export(model, torch.zeros(1, 1, bd.CROP, bd.CROP), path, **kwargs)


def main(argv=None) -> None:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--data", default="eye_dataset")
    ap.add_argument("--epochs", type=int, default=25)
    ap.add_argument("--batch", type=int, default=128)
    ap.add_argument("--lr", type=float, default=3e-3)
    ap.add_argument("--out", default="eye_cnn.onnx")
    ap.add_argument("--seed", type=int, default=0)
    ap.add_argument("--teacher", choices=("auto",) + TEACHER_ORDER, default="auto",
                    help="whose reading labels the frames you haven't (auto: the first of %s every session has)" % ", ".join(TEACHER_ORDER))
    ap.add_argument("--split-by", choices=("auto", "session", "person"), default="auto",
                    help="auto: person with 3+ people, else session")
    ap.add_argument("--test", help="comma-separated sessions (or people) to hold out for the reported numbers")
    ap.add_argument("--val", help="comma-separated sessions (or people) to pick the best epoch on")
    args = ap.parse_args(argv)

    try:
        import torch
        import torch.nn.functional as F
    except ImportError:
        sys.exit("PyTorch isn't installed: pip install -r requirements.txt")

    torch.manual_seed(args.seed)
    sessions = bd.load_sessions(args.data)
    splits, by, notes = assign_splits(sessions, args.split_by,
                                      args.test.split(",") if args.test else None,
                                      args.val.split(",") if args.val else None)
    pick = lambda ids: [s for s in sessions if s.id in ids]
    train_s, val_s, test_s = pick(splits["train"]), pick(splits["val"]), pick(splits["test"])
    teacher = args.teacher
    if teacher == "auto":
        teacher = next((m for m in TEACHER_ORDER if all(s.has(m) for s in train_s + val_s)), None)
        if teacher is None:
            sys.exit("no helper reading is in every training session; choose one with --teacher")
    for s in train_s + val_s:
        if not s.has(teacher):
            sys.exit(f"session {s.id} has no {teacher} reading; choose another --teacher")

    describe = lambda s: f"{s.id} ({s.person}, labelled)" if s.labelled else f"{s.id} ({s.person})"
    for name, group in (("train", train_s), ("val", val_s), ("test", test_s)):
        print(f"{name:5s} " + ", ".join(describe(s) for s in group))
    for note in notes:
        print(f"note: {note}")

    x, y, mine = crops(train_s, teacher)
    xv, yv, mine_v = crops(val_s, teacher)
    print(f"\n{len(x)} training crops ({100 * mine.mean():.0f}% labelled by you, the rest by the {teacher} teacher), "
          f"{100 * (y >= 0.5).mean():.1f}% shut; {len(xv)} validation crops, scored against "
          f"{'your labels' if mine_v.all() else 'your labels and the teacher' if mine_v.any() else 'the teacher'}")
    xt, yt = torch.from_numpy(x), torch.from_numpy(y)[:, None]
    xv_t = torch.from_numpy(xv)

    model = build_model()
    opt = torch.optim.AdamW(model.parameters(), lr=args.lr, weight_decay=1e-4)
    sched = torch.optim.lr_scheduler.CosineAnnealingLR(opt, args.epochs)
    # Soft labels from the teacher; shut frames are rare, so weight them up.
    pos_weight = torch.tensor([max(1.0, float((yt < 0.5).sum() / max(1, (yt >= 0.5).sum())))])

    best, best_epoch, best_state = -1.0, 0, None
    for epoch in range(args.epochs):
        model.train()
        perm = torch.randperm(len(xt))
        for i in range(0, len(xt), args.batch):
            idx = perm[i:i + args.batch]
            logits = model(augment(xt[idx].clone()))
            loss = F.binary_cross_entropy_with_logits(logits, yt[idx], pos_weight=pos_weight)
            opt.zero_grad()
            loss.backward()
            opt.step()
        sched.step()

        model.eval()
        with torch.no_grad():
            p = torch.sigmoid(model(xv_t)).numpy()[:, 0]
        precision, recall, f1 = f1_at_half(p, yv)
        print(f"epoch {epoch + 1:2d}  loss {loss.item():.3f}  val P {precision:.2f} R {recall:.2f} F1 {f1:.2f}")
        if f1 > best:
            best, best_epoch, best_state = f1, epoch + 1, {k: v.clone() for k, v in model.state_dict().items()}

    model.load_state_dict(best_state)
    model.eval()
    export_onnx(model, args.out)
    params = sum(p.numel() for p in model.parameters())
    print(f"\nbest val F1 {best:.2f} (epoch {best_epoch}); {params} parameters -> {args.out}")

    def torch_predict(batch):
        with torch.no_grad():
            return torch.sigmoid(model(torch.from_numpy(np.asarray(batch, np.float32)))).numpy()[:, 0]

    try:
        predict = ev.OnnxModel(args.out)
        probe = xv[:256] if len(xv) else x[:256]
        gap = float(np.abs(predict(probe) - torch_predict(probe)).max())
        print(f"the ONNX file matches PyTorch to {gap:.1e}; testing the ONNX file")
    except SystemExit:
        predict, gap = torch_predict, None
        print("onnxruntime isn't installed, so testing the PyTorch model (same weights)")

    card = {
        "model": args.out, "created": time.strftime("%Y-%m-%d %H:%M:%S"), "parameters": params,
        "contract": {"input": "eye [n,1,24,24] grayscale 0..1, rows top-down", "output": "closed_logit [n,1]"},
        "teacher": teacher, "split_by": by, "splits": splits,
        "train_crops": int(len(x)), "labelled_by_you": float(mine.mean()) if len(mine) else 0.0,
        "best_epoch": best_epoch, "val_f1": best, "epochs": args.epochs, "seed": args.seed,
        "onnx_vs_torch_max_diff": gap, "synthetic": any(s.synthetic for s in sessions),
    }

    labelled_test = [s for s in test_s if s.labelled]
    print("\nTEST (sessions it never trained or picked an epoch on):")
    if labelled_test:
        teachers = [m for m in ev.TEACHERS if all(s.has(m) for s in labelled_test)]
        result = ev.evaluate(labelled_test, teachers + ["cnn"], predict)
        print(ev.format_report(result))
        card["test"] = {"sessions": result["sessions"], "methods": result["methods"]}
    else:
        xs, ys, _ = crops(test_s, teacher)
        precision, recall, f1 = f1_at_half(predict(xs), ys)
        print(f"agreement with the {teacher} teacher (not accuracy: no labelled test session): "
              f"P {precision:.2f} R {recall:.2f} F1 {f1:.2f}")
        card["test"] = {"sessions": [s.id for s in test_s], "agreement_with_teacher": {"p": precision, "r": recall, "f1": f1}}
    with open(args.out + ".json", "w") as f:
        json.dump(card, f, indent=1)
    print(f"\nwrote {args.out}.json (which sessions it saw; eval_blink.py reads it)")
    print("Use it: python blink_server.py --method cnn --onnx " + args.out +
          "   (or import it into Unity for SentisBlinkSource)")


if __name__ == "__main__":
    main()
