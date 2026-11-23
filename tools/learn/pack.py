#!/usr/bin/env python3
"""The model pack (0.4.0, build plan §6.5, C3.7): the general model's numbers in one signed file.

    python3 tools/learn/pack.py keygen                      # once: a key pair; the private half stays in ~/.kehai/
    python3 tools/learn/pack.py build out.kpack --version 7 part.json…   # signs with that key
    python3 tools/learn/pack.py check out.kpack             # the game's own checks, in Python

A pack is two lines: the body (JSON: format, pack_version, inputs_version, created, parts) and
its signature (RSA PKCS#1 v1.5, SHA-256, base64). Parts are what the fits and exports write:
    start    LedgerStart.part.json (fit_start.py)        brain  KarenBrainBasic.part.json (export_brain.py)
    weights  SenseWeights / MirrorWeights / PredictionWeights .part.json (tools/belief)
Vital is never put in a pack (D11). Nothing here uploads anything: publishing is Phase 7, and
each publish waits for your yes.
"""
from __future__ import annotations

import argparse
import datetime
import json
import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import rsa_sign  # noqa: E402

FORMAT = "kehai-model-pack"
INPUTS_VERSION = 2            # BrainObservation.Version in the game
MODEL_MAJOR = 1               # ModelPack.SupportedMajor in the game
MAX_BYTES = 16 * 1024 * 1024
KEY_PATH = os.path.expanduser("~/.kehai/model_pack_key.json")
NEVER_IN_A_PACK = ("KarenBrainVital",)


def load_key(path: str = KEY_PATH) -> dict:
    with open(path) as f:
        k = json.load(f)
    return {"n": int(k["n"]), "e": int(k["e"]), "d": int(k["d"])}


def keygen(path: str = KEY_PATH, bits: int = 2048) -> dict:
    if os.path.exists(path):
        sys.exit(f"pack: {path} exists; a new key would orphan every pack signed with it")
    key = rsa_sign.generate(bits)
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w") as f:
        json.dump({k: str(v) for k, v in key.items()}, f)
    os.chmod(path, 0o600)
    return key


def model_major(version) -> int:
    component = r"(?:0|[1-9][0-9]{0,9})"
    if not isinstance(version, str) or not re.fullmatch(rf"{component}\.{component}\.{component}", version):
        raise ValueError("model version must be MAJOR.MINOR.PATCH")
    major, minor, patch = map(int, version.split("."))
    if max(major, minor, patch) > 2147483647:
        raise ValueError("model version component is out of range")
    return major


def body(parts: list, version: int, inputs_version: int = INPUTS_VERSION, created: str | None = None,
         model_version: str | None = None) -> str:
    model_version = f"{MODEL_MAJOR}.{version}.0" if model_version is None else model_version
    major = model_major(model_version)
    versioned_parts = []
    for p in parts:
        if p.get("name") in NEVER_IN_A_PACK:
            raise ValueError(f"{p['name']} is never put in a pack (D11)")
        if p.get("kind") not in ("start", "brain", "weights"):
            raise ValueError(f"unknown part kind {p.get('kind')}")
        if p.get("kind") == "brain" and p.get("name") == "KarenBrainBasic":
            card = dict(p.get("card") or {})
            card.setdefault("model_version", model_version)
            if model_major(card["model_version"]) != major:
                raise ValueError("KarenBrainBasic and its pack must share a model MAJOR")
            p = dict(p, card=card)
        versioned_parts.append(p)
    doc = {"format": FORMAT, "pack_version": int(version), "inputs_version": int(inputs_version),
           "model_version": model_version, "created": created or datetime.datetime.now(datetime.timezone.utc).isoformat(timespec="seconds"),
           "parts": versioned_parts}
    return json.dumps(doc, separators=(",", ":"), sort_keys=True)


def build(parts: list, version: int, key: dict, **kw) -> str:
    b = body(parts, version, **kw)
    return b + "\n" + rsa_sign.sign(b.encode("utf-8"), key) + "\n"


def check(text: str, key: dict, inputs_version: int = INPUTS_VERSION, known_tricks=None,
          supported_major: int = MODEL_MAJOR) -> tuple:
    """The game's checks: (ok, why, body)."""
    if len(text.encode("utf-8")) > MAX_BYTES:
        return False, "too big", None
    lines = text.replace("\r", "").split("\n")
    if len(lines) < 2 or not lines[0]:
        return False, "not a pack", None
    if not rsa_sign.verify(lines[0].encode("utf-8"), lines[1].strip(), key):
        return False, "the signature doesn't match", None
    try:
        b = json.loads(lines[0])
    except ValueError:
        return False, "not valid pack JSON", None
    if not isinstance(b, dict) or b.get("format") != FORMAT:
        return False, "not a Kehai model pack", None
    if b.get("inputs_version") != inputs_version:
        return False, f"made for inputs version {b.get('inputs_version')}", None
    parts = b.get("parts")
    if not isinstance(parts, list) or any(not isinstance(part, dict) for part in parts):
        return False, "invalid pack parts", None
    try:
        versions = [b["model_version"]] if "model_version" in b else []
        for part in parts:
            card = part.get("card")
            employee = str(part.get("name", "")).startswith("EmployeeBrain")
            if part.get("kind") == "brain" and not employee and isinstance(card, dict) and "model_version" in card:
                versions.append(card["model_version"])
        for version in versions:
            major = model_major(version)
            if major != supported_major:
                return False, f"made for model MAJOR {major}, this build reads {supported_major}", None
    except ValueError as error:
        return False, str(error), None
    if known_tricks is not None:
        for p in parts:
            for t in (p.get("tricks") or {}):
                if t not in known_tricks:
                    return False, f"names a trick this build doesn't have: {t}", None
    return True, "", b


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = ap.add_subparsers(dest="cmd", required=True)
    g = sub.add_parser("keygen")
    g.add_argument("--key", default=KEY_PATH)
    b = sub.add_parser("build")
    b.add_argument("out")
    b.add_argument("parts", nargs="+")
    b.add_argument("--version", type=int, required=True)
    b.add_argument("--model-version", help="Karen compatibility version, default 1.<pack version>.0")
    b.add_argument("--key", default=KEY_PATH)
    c = sub.add_parser("check")
    c.add_argument("pack")
    c.add_argument("--key", default=KEY_PATH)
    a = ap.parse_args(argv)
    if a.cmd == "keygen":
        k = keygen(a.key)
        print(f"Private key written to {a.key} (keep it out of git). Paste into PackKeys in ModelPack.cs:")
        print(f'    public const string Modulus = "{rsa_sign.b64int(k["n"])}";')
        print(f'    public const string Exponent = "{rsa_sign.b64int(k["e"])}";')
    elif a.cmd == "build":
        parts = [json.load(open(p)) for p in a.parts]
        text = build(parts, a.version, load_key(a.key), model_version=a.model_version)
        with open(a.out, "w") as f:
            f.write(text)
        print(f"pack {a.version}: {len(parts)} parts, {len(text)} bytes → {a.out}")
    else:
        ok, why, b = check(open(a.pack).read(), load_key(a.key))
        print("ok" if ok else "refused: " + why)
        return 0 if ok else 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
