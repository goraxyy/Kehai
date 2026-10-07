#!/usr/bin/env python3
"""Train the tiny eye-state CNN and export it to ONNX (IDEAS.md "Own training path").

Input:  1x24x24 grayscale eye crop, values 0..1, rows top-down   (name "eye")
Output: one logit, sigmoid = probability the eye is shut           (name "closed_logit")

The same contract is used by blink_server.py --method cnn and by SentisBlinkSource.cs, so
one exported file works in both. ~12k parameters; trains in a minute or two on a laptop CPU.

    python train_eye_cnn.py --data eye_dataset --epochs 25 --out eye_cnn.onnx
"""
from __future__ import annotations

import argparse
import glob
import os
import sys


def load(folder: str):
    import numpy as np

    xs, ys = [], []
    for path in sorted(glob.glob(os.path.join(folder, "*.npz"))):
        d = np.load(path)
        xs.append(d["x"].astype(np.float32) / 255.0)
        ys.append(d["y"].astype(np.float32))
    if not xs:
        sys.exit(f"no .npz files in {folder} — record some with record_dataset.py")
    return np.concatenate(xs), np.concatenate(ys)


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


def main(argv=None) -> None:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--data", default="eye_dataset")
    ap.add_argument("--epochs", type=int, default=25)
    ap.add_argument("--batch", type=int, default=128)
    ap.add_argument("--lr", type=float, default=3e-3)
    ap.add_argument("--out", default="eye_cnn.onnx")
    ap.add_argument("--seed", type=int, default=0)
    args = ap.parse_args(argv)

    import numpy as np
    import torch
    import torch.nn.functional as F

    torch.manual_seed(args.seed)
    x, y = load(args.data)
    rng = np.random.default_rng(args.seed)
    order = rng.permutation(len(x))
    split = int(len(x) * 0.85)
    tr, va = order[:split], order[split:]
    xt = torch.from_numpy(x[tr])[:, None]
    yt = torch.from_numpy(y[tr])[:, None]
    xv = torch.from_numpy(x[va])[:, None]
    yv = torch.from_numpy(y[va])[:, None]
    print(f"{len(tr)} training crops, {len(va)} validation; {100 * (y > 0.5).mean():.1f}% shut")

    model = build_model()
    opt = torch.optim.AdamW(model.parameters(), lr=args.lr, weight_decay=1e-4)
    sched = torch.optim.lr_scheduler.CosineAnnealingLR(opt, args.epochs)
    # Soft labels from the teacher; shut frames are rare, so weight them up.
    pos_weight = torch.tensor([max(1.0, float((yt < 0.5).sum() / max(1, (yt >= 0.5).sum())))])

    best, best_state = -1.0, None
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
            p = torch.sigmoid(model(xv))
        truth, guess = yv >= 0.5, p >= 0.5
        tp = (truth & guess).sum().item()
        precision = tp / max(1, guess.sum().item())
        recall = tp / max(1, truth.sum().item())
        f1 = 2 * precision * recall / max(1e-9, precision + recall)
        print(f"epoch {epoch + 1:2d}  loss {loss.item():.3f}  val P {precision:.2f} R {recall:.2f} F1 {f1:.2f}")
        if f1 > best:
            best, best_state = f1, {k: v.clone() for k, v in model.state_dict().items()}

    model.load_state_dict(best_state)
    model.eval()
    torch.onnx.export(model, torch.zeros(1, 1, 24, 24), args.out, input_names=["eye"], output_names=["closed_logit"],
                      dynamic_axes={"eye": {0: "n"}, "closed_logit": {0: "n"}}, opset_version=13)
    params = sum(p.numel() for p in model.parameters())
    print(f"best val F1 {best:.2f}; {params} parameters -> {args.out}")
    print("Use it: python blink_server.py --method cnn --onnx " + args.out +
          "   (or import it into Unity for SentisBlinkSource)")


if __name__ == "__main__":
    main()
