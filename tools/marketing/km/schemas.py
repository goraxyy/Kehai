"""The JSON Schemas in tools/marketing/schemas, and checking files against them.

The same schemas the editor (TypeScript) and Unity's outputs are written to; draft 2020-12.
"""
from __future__ import annotations

import json
from functools import lru_cache
from pathlib import Path

from jsonschema import Draft202012Validator

from . import paths

KINDS = {
    "edit": "edit.schema.json",
    "markers": "markers.schema.json",
    "shot": "shot.schema.json",
    "shot-path": "shot-path.schema.json",
    "brand": "brand.schema.json",
    "manifest": "asset-manifest.schema.json",
    "pipeline": "pipeline.schema.json",
    "facts": "facts.schema.json",
}


@lru_cache(maxsize=None)
def validator(kind: str) -> Draft202012Validator:
    schema = json.loads((paths.SCHEMAS / KINDS[kind]).read_text(encoding="utf-8"))
    Draft202012Validator.check_schema(schema)
    return Draft202012Validator(schema)


def guess_kind(file: Path, data: object) -> str | None:
    """Which schema a file is written to, from its name or its shape."""
    name = file.name
    if name.endswith(".markers.json"):
        return "markers"
    if name.endswith(".path.json"):
        return "shot-path"
    if name == "brand.json":
        return "brand"
    if name == "manifest.json":
        return "manifest"
    if name == "pipeline.json":
        return "pipeline"
    if isinstance(data, dict):
        if "scenes" in data:
            return "edit"
        if "frames" in data and "shot" in data:
            return "shot"
    return None


def errors(kind: str, data: object) -> list[str]:
    """Every way `data` breaks the schema, as '<path>: <message>', most specific first."""
    found = []
    for e in sorted(validator(kind).iter_errors(data), key=lambda e: (-len(e.absolute_path), e.message)):
        where = "/" + "/".join(str(p) for p in e.absolute_path)
        found.append(f"{where}: {e.message}")
    return found


def edit_files(edit: dict) -> list[str]:
    """Every file an edit plays (the editor's filesOf, in Python)."""
    files: list[str] = []

    def add(f: str) -> None:
        if f not in files:
            files.append(f)

    for scene in edit.get("scenes", []):
        v = scene.get("visual", {})
        if v.get("type") == "shot":
            add(v["src"])
        elif v.get("type") == "split":
            add(v["a"]["src"])
            add(v["b"]["src"])
        elif v.get("type") == "image":
            add(v["src"])
        if "pip" in scene:
            add(scene["pip"]["src"])
        for o in scene.get("overlays", []):
            if isinstance(o.get("src"), str):
                add(o["src"])
        for s in scene.get("sfx", []):
            add(s["src"])
    for clips in edit.get("voice", {}).values():
        for c in clips:
            add(c["src"])
    if "music" in edit:
        add(edit["music"]["src"])
    return files
