"""Unity .meta files for what the exporters write into Assets/ (CLAUDE.md: every file needs its
.meta). A file that already has one keeps its GUID, so a re-export doesn't break references."""
import os
import uuid

FOLDER = "fileFormatVersion: 2\nguid: {}\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {{}}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n"


def _under_assets(path: str) -> bool:
    return f"{os.sep}Assets{os.sep}" in os.path.abspath(path)


def write_meta(path: str) -> None:
    """A .meta beside `path` (and its folders up to Assets) if it's inside a Unity project and has none."""
    if not _under_assets(path):
        return
    p = os.path.abspath(path)
    if not os.path.exists(p + ".meta"):
        with open(p + ".meta", "w") as f:
            f.write(FOLDER.format(uuid.uuid4().hex) if os.path.isdir(p) else f"fileFormatVersion: 2\nguid: {uuid.uuid4().hex}\n")
    parent = os.path.dirname(p)
    if os.path.basename(parent) != "Assets" and _under_assets(parent):
        write_meta(parent)
