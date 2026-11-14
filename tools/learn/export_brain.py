#!/usr/bin/env python3
"""Turn a trained ML-Agents checkpoint into generated C# with a model card (0.4.0, §5.5, C5.7).

    python3 tools/learn/export_brain.py tools/rl/results/vital_r2_a --as KarenBrainVital \\
        --outputs <kehai_eval>/brain_spec_KarenVital.json --trained-against "vital_r2.json" --validation "..."

Writes, beside the game's code (Decision/Policy/Generated/):
    <Name>.g.cs        the numbers, as a class BrainNet runs (no ML-Agents, no model file)
    <Name>.card.md     what it was trained against, for how long, its scores, the command
    <Name>.part.json   the same numbers for the model pack (tools/learn/pack.py)
and checks itself: the numbers it wrote, run the way the C# runs them, give the trainer's own
network's chances on 1,000 random inputs (to 4 decimals), or it writes nothing.

The network is ML-Agents' actor: normaliser → [Linear → Swish] × layers → one Linear head per
discrete branch. Run with tools/rl/.venv/bin/python (torch; mlagents for the self-check).
"""
from __future__ import annotations

import argparse
import datetime
import glob
import json
import os
import shlex
import sys

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import unity_meta  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
GENERATED = os.path.join(ROOT, "Assets", "!_Project", "_Game", "AI", "Scripts", "Decision", "Policy", "Generated")


# ---- reading a checkpoint ---------------------------------------------------------------------------

def find_checkpoint(path: str, behavior: str | None) -> str:
    if os.path.isfile(path):
        return path
    pattern = os.path.join(path, behavior or "*", "checkpoint.pt")
    found = sorted(glob.glob(pattern))
    if not found:
        sys.exit(f"export_brain: no checkpoint.pt under {path}")
    return found[0]


def actor_state(ckpt: dict) -> dict:
    """The actor's state dict from what TorchModelSaver saved ({module name: state dict})."""
    if "network_body._body_endoder.seq_layers.0.weight" in ckpt:
        return ckpt
    for name in ("Policy", "policy", "Actor"):
        if name in ckpt:
            return ckpt[name]
    for v in ckpt.values():
        if isinstance(v, dict) and "network_body._body_endoder.seq_layers.0.weight" in v:
            return v
    raise ValueError("no actor in this checkpoint")


def to_numpy(state: dict) -> dict:
    out = {}
    for k, v in state.items():
        out[k] = v.detach().cpu().numpy() if hasattr(v, "detach") else np.asarray(v)
    return out


def layers_from_state(s: dict) -> dict:
    """mean, std, hidden [(W, b)], heads [(W, b)] from an actor state dict (numpy arrays)."""
    hidden, i = [], 0
    while f"network_body._body_endoder.seq_layers.{i}.weight" in s:
        hidden.append((s[f"network_body._body_endoder.seq_layers.{i}.weight"], s[f"network_body._body_endoder.seq_layers.{i}.bias"]))
        i += 2      # Linear, Swish, Linear, Swish…
    heads, j = [], 0
    while f"action_model._discrete_distribution.branches.{j}.weight" in s:
        heads.append((s[f"action_model._discrete_distribution.branches.{j}.weight"], s[f"action_model._discrete_distribution.branches.{j}.bias"]))
        j += 1
    if not hidden or not heads:
        raise ValueError("expected a discrete actor with a body (network_body._body_endoder, action_model branches)")
    n = hidden[0][0].shape[1]
    key = "network_body.observation_encoder.processors.0.normalizer."
    if key + "running_mean" in s:
        steps = float(np.asarray(s[key + "normalization_steps"]).reshape(-1)[0])
        mean = s[key + "running_mean"].astype(np.float64)
        std = np.sqrt(s[key + "running_variance"].astype(np.float64) / max(steps, 1.0))
    else:
        mean, std = np.zeros(n), np.ones(n)
    return {"mean": mean, "std": std, "hidden": hidden, "heads": heads}


# ---- running it as the C# does ------------------------------------------------------------------------

def forward(net: dict, x: np.ndarray) -> list:
    """BrainNet.Logits, in numpy (doubles): the same arithmetic in the same order."""
    std = np.where(net["std"] > 1e-8, net["std"], 1.0)
    h = np.clip((x.astype(np.float64) - net["mean"]) / std, -5.0, 5.0)
    for w, b in net["hidden"]:
        h = h @ w.astype(np.float64).T + b.astype(np.float64)
        h = h / (1.0 + np.exp(-h))
    return [h @ w.astype(np.float64).T + b.astype(np.float64) for w, b in net["heads"]]


def softmax(z):
    z = z - z.max(axis=-1, keepdims=True)
    e = np.exp(z)
    return e / e.sum(axis=-1, keepdims=True)


def trainer_logits(state, x: np.ndarray, hidden_units: int, num_layers: int, normalize: bool, branches) -> list | None:
    """The trainer's own actor on the same inputs, or None without mlagents."""
    try:
        import torch
        from mlagents_envs.base_env import ActionSpec, ObservationSpec, DimensionProperty, ObservationType
        from mlagents.trainers.torch_entities.networks import SimpleActor
        from mlagents.trainers.settings import NetworkSettings
    except ImportError:
        return None
    spec = [ObservationSpec(shape=(x.shape[1],), dimension_property=(DimensionProperty.NONE,),
                            observation_type=ObservationType.DEFAULT, name="obs")]
    actor = SimpleActor(spec, NetworkSettings(normalize=normalize, hidden_units=hidden_units, num_layers=num_layers),
                        ActionSpec(0, tuple(branches)))
    actor.load_state_dict(state)
    actor.eval()
    with torch.no_grad():
        enc, _ = actor.network_body([torch.as_tensor(x, dtype=torch.float32)])
        return [b(enc).numpy().astype(np.float64) for b in actor.action_model._discrete_distribution.branches]


def parity(net: dict, state, n: int = 1000, seed: int = 0) -> float:
    """Largest difference in chances between our arithmetic and the trainer's actor."""
    rng = np.random.default_rng(seed)
    x = rng.uniform(-1.0, 1.0, size=(n, len(net["mean"])))
    ours = forward(net, x)
    theirs = trainer_logits(state, x, net["hidden"][0][0].shape[0], len(net["hidden"]),
                            not np.allclose(net["std"], 1.0) or not np.allclose(net["mean"], 0.0),
                            [w.shape[0] for w, _ in net["heads"]])
    if theirs is None:
        return 0.0
    return max(float(np.abs(softmax(a) - softmax(b)).max()) for a, b in zip(ours, theirs))


# ---- writing ----------------------------------------------------------------------------------------

def f(v: float) -> str:
    s = repr(float(np.float32(v)))
    return (s if ("e" in s or "." in s) else s + ".0") + "f"


def arr(a) -> str:
    flat = np.asarray(a, dtype=np.float32).reshape(-1)
    body = ", ".join(f(x) for x in flat)
    return "new float[] { " + body + " }"


def cs_string(s: str) -> str:
    return '"' + (s or "").replace("\\", "\\\\").replace('"', '\\"').replace("\n", "\\n") + '"'


def generated_cs(name: str, net: dict, card: dict, namespace: str = "Kehai.Karen.Generated") -> str:
    hidden, heads = net["hidden"], net["heads"]
    outputs = card.get("outputs") or [[f"{h}:{i}" for i in range(w.shape[0])] for h, (w, _) in enumerate(heads)]
    names = "new string[][] { " + ", ".join("new[] { " + ", ".join(cs_string(o) for o in head) + " }" for head in outputs) + " }"
    lines = [
        "// <auto-generated> by tools/learn/export_brain.py — do not edit; re-export instead.",
        f"// {name}: {card.get('version', '')}. Its card: {name}.card.md",
        "namespace " + namespace,
        "{",
        f"    public static class {name}",
        "    {",
        "        public static Kehai.Karen.BrainCard Card => new Kehai.Karen.BrainCard",
        "        {",
        f"            Name = {cs_string(name)}, Version = {cs_string(card.get('version', ''))}, InputsVersion = {int(card.get('inputs_version', 0))},",
        f"            Trained = {str(bool(card.get('trained', True))).lower()}, TrainedAgainst = {cs_string(card.get('trained_against', ''))}, Steps = {int(card.get('steps', 0))}L,",
        f"            Validation = {cs_string(card.get('validation', ''))}, Command = {cs_string(card.get('command', ''))},",
        f"            OutputNames = {names}",
        "        };",
        "",
        "        public static Kehai.Karen.BrainNet Build() => new Kehai.Karen.BrainNet(Card,",
        f"            {arr(net['mean'])},",
        f"            {arr(net['std'])},",
        "            new[] { " + ", ".join(str(w.shape[0]) for w, _ in hidden) + " },",
        "            new float[][] { " + ", ".join(arr(w) for w, _ in hidden) + " },",
        "            new float[][] { " + ", ".join(arr(b) for _, b in hidden) + " },",
        "            new[] { " + ", ".join(str(w.shape[0]) for w, _ in heads) + " },",
        "            new float[][] { " + ", ".join(arr(w) for w, _ in heads) + " },",
        "            new float[][] { " + ", ".join(arr(b) for _, b in heads) + " });",
        "    }",
        "}",
        "",
    ]
    return "\n".join(lines)


def part_json(name: str, net: dict, card: dict) -> dict:
    return {
        "kind": "brain", "name": name, "card": card,
        "mean": [float(x) for x in net["mean"]], "std": [float(x) for x in net["std"]],
        "hidden": [{"w": np.asarray(w, np.float32).reshape(-1).tolist(), "b": np.asarray(b, np.float32).tolist(), "out": int(w.shape[0])} for w, b in net["hidden"]],
        "heads": [{"w": np.asarray(w, np.float32).reshape(-1).tolist(), "b": np.asarray(b, np.float32).tolist(), "out": int(w.shape[0])} for w, b in net["heads"]],
    }


def card_md(name: str, card: dict, max_diff: float) -> str:
    return "\n".join([
        f"# Model card: {name}", "",
        "| | |", "|---|---|",
        f"| Version | {card.get('version', '')} |",
        f"| Trained | {'yes' if card.get('trained', True) else 'no (a stand-in)'} |",
        f"| Trained against | {card.get('trained_against', '')} |",
        f"| Decisions | {card.get('steps', 0):,} |",
        f"| Inputs version | {card.get('inputs_version', 0)} |",
        f"| Outputs | {', '.join(str(len(h)) for h in card.get('outputs', []))} per head |",
        f"| Validation | {card.get('validation', '') or '—'} |",
        f"| Parity with the trainer (largest chance difference, 1,000 inputs) | {max_diff:.2e} |",
        f"| Exported | {card.get('exported', '')} |",
        f"| Command | `{card.get('command', '')}` |", "",
    ])


def export(state, name: str, card: dict, out_dir: str, namespace: str = "Kehai.Karen.Generated", tolerance: float = 1e-4) -> dict:
    s = to_numpy(state)
    net = layers_from_state(s)
    if card.get("outputs") and [len(h) for h in card["outputs"]] != [w.shape[0] for w, _ in net["heads"]]:
        raise SystemExit(f"export_brain: the outputs file names {[len(h) for h in card['outputs']]} outputs, the network has "
                         f"{[w.shape[0] for w, _ in net['heads']]}")
    if card.get("input_size") and int(card["input_size"]) != len(net["mean"]):
        raise SystemExit(f"export_brain: the game's inputs are {card['input_size']} numbers, the network takes {len(net['mean'])}")
    diff = parity(net, state)
    if diff > tolerance:
        raise SystemExit(f"export_brain: our arithmetic and the trainer's differ by {diff:.2e} in a chance; nothing written")
    os.makedirs(out_dir, exist_ok=True)
    with open(os.path.join(out_dir, name + ".g.cs"), "w") as fh:
        fh.write(generated_cs(name, net, card, namespace))
    with open(os.path.join(out_dir, name + ".card.md"), "w") as fh:
        fh.write(card_md(name, card, diff))
    with open(os.path.join(out_dir, name + ".part.json"), "w") as fh:
        json.dump(part_json(name, net, card), fh)
    for ext in (".g.cs", ".card.md", ".part.json"):
        unity_meta.write_meta(os.path.join(out_dir, name + ext))
    return {"net": net, "parity": diff}


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("run", help="a results/<run-id> folder or a checkpoint .pt")
    ap.add_argument("--as", dest="name", required=True, help="KarenBrainBasic, KarenBrainVital, EmployeeBrain…")
    ap.add_argument("--behavior", help="the behaviour folder inside the run (KarenBasic, KarenVital, Employee)")
    ap.add_argument("--outputs", help="the game's brain spec (brain_spec_<Behavior>.json): output names and input version")
    ap.add_argument("--trained-against", default="")
    ap.add_argument("--validation", default="")
    ap.add_argument("--version", default="")
    ap.add_argument("--out", default=GENERATED)
    a = ap.parse_args(argv)

    import torch
    path = find_checkpoint(a.run, a.behavior)
    ckpt = torch.load(path, map_location="cpu", weights_only=False)
    state = actor_state(ckpt)
    card = {"version": a.version or os.path.basename(os.path.normpath(a.run)), "trained": True,
            "trained_against": a.trained_against, "validation": a.validation,
            "command": "python3 tools/learn/export_brain.py " + " ".join(shlex.quote(x) for x in (argv or sys.argv[1:])),
            "exported": datetime.date.today().isoformat()}
    if a.outputs:
        spec = json.load(open(a.outputs))
        card["outputs"] = spec["outputs"]
        card["inputs_version"] = spec.get("inputs_version", 0)
        card["input_size"] = spec.get("input_size", 0)
    steps = [int(os.path.basename(p).split("-")[-1].split(".")[0]) for p in glob.glob(os.path.join(os.path.dirname(path), "*-*.pt"))
             if os.path.basename(p).split("-")[-1].split(".")[0].isdigit()]
    card["steps"] = max(steps) if steps else 0
    r = export(state, a.name, card, a.out)
    print(f"export_brain: {a.name} written to {a.out} (parity {r['parity']:.2e})")


if __name__ == "__main__":
    main()
