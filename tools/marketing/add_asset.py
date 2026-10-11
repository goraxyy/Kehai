#!/usr/bin/env python3
"""Adds a file to the asset library: the only way music, sound effects, images, GIFs, Lottie
animations, video or fonts get into an edit (docs/MARKETING.md).

    uv run add_asset.py <file> --type music --licence "CC0 1.0" --source <url or own> \\
        --cleared youtube,tiktok,instagram [--author ...] [--attribution ...] \\
        [--mood tense,quiet] [--tags ...] [--id name] [--notes ...] [--dry-run] [--no-upload]
    uv run add_asset.py --list
    uv run add_asset.py --sync          # upload what's waiting to Google Drive

It refuses a file without a licence or a source, and music that isn't cleared for YouTube,
TikTok and Instagram. Files are copied into <working folder>/assets/<kind>/ and listed in
assets/manifest.json; with rclone set up (remote 'gdrive'), both go to Drive too.

Exit codes: 0 added (or already there) · 1 refused.
"""
from __future__ import annotations

import argparse
import sys
from pathlib import Path

from km import assets, paths


def main(argv: list[str] | None = None) -> int:
    p = argparse.ArgumentParser(description=__doc__.split("\n\n")[0], formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("file", nargs="?", type=Path)
    p.add_argument("--type", dest="kind", choices=sorted(assets.FOLDERS))
    p.add_argument("--licence", "--license", default="")
    p.add_argument("--source", default="")
    p.add_argument("--author", default="")
    p.add_argument("--attribution", default="", help="the credit line, if the licence asks for one")
    p.add_argument("--cleared", default="", help="platforms the licence covers: youtube,tiktok,instagram")
    p.add_argument("--mood", default="")
    p.add_argument("--tags", default="")
    p.add_argument("--id", default=None)
    p.add_argument("--notes", default="")
    p.add_argument("--root", default=None, help="the working folder (default: KEHAI_MARKETING or ~/TokenLimit/marketing)")
    p.add_argument("--dry-run", action="store_true")
    p.add_argument("--no-upload", action="store_true")
    p.add_argument("--list", action="store_true")
    p.add_argument("--sync", action="store_true")
    a = p.parse_args(argv)
    root = paths.root(a.root)
    split = lambda s: [x.strip() for x in s.split(",") if x.strip()]

    try:
        if a.list:
            for e in assets.load(root)["assets"]:
                print(f"{e['id']:<28} {e['type']:<7} {e['licence']:<28} {e['drive']['status']:<9} {e['file']}")
            return 0
        if a.sync:
            for line in assets.sync(root, dry_run=a.dry_run):
                print(line)
            return 0
        if a.file is None or a.kind is None:
            p.error("a file and --type are needed")
        req = assets.Request(
            file=a.file, kind=a.kind, licence=a.licence, source=a.source, author=a.author, attribution=a.attribution,
            cleared=tuple(split(a.cleared)), mood=split(a.mood), tags=split(a.tags), notes=a.notes, id=a.id,
        )
        result = assets.add(root, req, dry_run=a.dry_run, upload=not a.no_upload)
    except assets.Refused as e:
        print(f"add_asset: refused: {e}", file=sys.stderr)
        return 1
    for line in result.actions:
        print(line)
    if result.added:
        print(f"use it in an edit as \"{result.entry['file']}\"")
    return 0


if __name__ == "__main__":
    sys.exit(main())
