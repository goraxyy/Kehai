"""Building blocks for the Phase 6 tests: a working folder with shots and assets, and a draft."""
from __future__ import annotations

import copy
import json
from pathlib import Path

FIXTURES = Path(__file__).resolve().parent / "fixtures"
STEM = "shift_03_20260930_055451"


def track(seconds: float, aiko=lambda t: (0.5, 0.4, 0.05), you=lambda t: None, hz: int = 10) -> dict:
    n = int(seconds * hz) + 1

    def sample(f, i):
        v = f(i / hz)
        return None if v is None else list(v)

    return {"hz": hz, "aiko": [sample(aiko, i) for i in range(n)], "you": [sample(you, i) for i in range(n)]}


def shot(root: Path, folder: str, name: str, seconds: float = 20.0, camera: str = "topdown", alpha: bool = False,
         layers=(), with_track: dict | None = None, fps: int = 30) -> Path:
    """A rendered shot as render_shot.sh leaves it: a (placeholder) video and its sidecar."""
    d = root / folder
    d.mkdir(parents=True, exist_ok=True)
    ext = "webm" if alpha else "mp4"
    (d / f"{name}.{ext}").write_bytes(b"not really a video")
    side = {"version": 1, "game": "Kehai", "krec": f"{STEM}.krec", "stem": STEM, "shift": 3, "from": 126.73,
            "to": 126.73 + seconds, "moment": None, "shot": camera, "subject": "aiko", "layers": list(layers),
            "alpha": alpha, "dof": False, "width": 1080, "height": 1920, "fps": fps, "frames": int(seconds * fps),
            "video": f"{name}.{ext}", "audio": None if alpha else f"{name}.wav", "rendered": "2026-10-01T21:57:42",
            "renderSeconds": 30.0}
    if with_track is not None:
        side["track"] = with_track
    (d / f"{name}.json").write_text(json.dumps(side), encoding="utf-8")
    return d / f"{name}.json"


ASSETS = [
    {"id": "night-shift", "type": "music", "file": "assets/music/night-shift.wav"},
    {"id": "whoosh", "type": "sfx", "file": "assets/sfx/whoosh.wav"},
    {"id": "boom", "type": "sfx", "file": "assets/sfx/boom.wav"},
    {"id": "pulse", "type": "lottie", "file": "assets/lottie/pulse.json"},
    {"id": "store", "type": "image", "file": "assets/images/store.png"},
]


def library(root: Path) -> None:
    """A small asset library (placeholder files, a valid manifest)."""
    entries = []
    for a in ASSETS:
        f = root / a["file"]
        f.parent.mkdir(parents=True, exist_ok=True)
        f.write_bytes(b"x")
        entries.append({"id": a["id"], "type": a["type"], "file": a["file"], "sha256": "0" * 64, "bytes": 1,
                        "added": "2026-09-30", "licence": "Own work", "source": "own",
                        "cleared": {"youtube": True, "tiktok": True, "instagram": True},
                        "mood": ["dark"], "tags": [], "drive": {"status": "pending"}})
    (root / "assets" / "manifest.json").write_text(json.dumps({"version": 1, "assets": entries}), encoding="utf-8")


def ctx(shots: list[dict] | None = None, kind: str = "short", fmt: str = "9:16") -> dict:
    if shots is None:
        shots = [{"id": n, "src": f"shots/w/{n}.mp4", "seconds": 20.13, "alpha": n == "mind"}
                 for n in ("top", "pov", "cctv", "mind")]
    return {"kind": kind, "format": fmt, "fps": 30, "source": {"stem": STEM, "moments": [1]},
            "shots": shots, "assets": copy.deepcopy(ASSETS)}


def ov(kind: str, **k) -> dict:
    o = {"kind": kind, "from": 0, "to": 0, "target": "none", "text": "", "text2": "", "template": "none", "asset": "",
         "x": 0.5, "y": 0.5, "x2": 0, "y2": 0, "size": 1, "style": "crimson"}
    o.update({("from" if key == "start" else key): v for key, v in k.items()})
    return o


def vis(kind: str = "shot", **k) -> dict:
    v = {"kind": kind, "source": "", "trim": 0, "speed": [], "zoom": [], "label": "", "second_source": "",
         "second_trim": 0, "second_label": "", "split": "none", "colour": "none", "game_volume": 0.7}
    v.update(k)
    return v


NO_PIP = {"source": "", "trim": 0, "corner": "top-right", "size": 0.38, "label": "", "from": 0, "to": 0}


def draft() -> dict:
    """The one-sprint short: every kind of scene the editor has, and a working script."""
    return {
        "id": "one-sprint", "title": "One sprint was enough", "hook": "one sprint; she heard it",
        "scenes": [
            {"name": "from above", "duration": 3.5, "transition": "cut", "visual": vis(source="top", trim=1.2),
             "pip": NO_PIP, "overlays": [ov("hook", text="One sprint was *all* it took.")], "sfx": []},
            {"name": "the flicker", "duration": 3.6, "transition": "cut",
             "visual": vis(source="cctv", trim=3.6, zoom=[{"at": 0, "scale": 1, "x": 0.5, "y": 0.45},
                                                           {"at": 3.6, "scale": 1.15, "x": 0.5, "y": 0.45}]),
             "pip": NO_PIP, "overlays": [ov("label", text="*Flicker* = warning", y=0.22)],
             "sfx": [{"asset": "whoosh", "at": 0, "volume": 0.8}]},
            {"name": "her guess", "duration": 5.0, "transition": "fade", "visual": vis(source="top", trim=7.0),
             "pip": {**NO_PIP, "source": "mind", "trim": 7.0, "size": 0.42, "label": "her guess"},
             "overlays": [ov("arrow", start=0.8, to=2.6, x=0.24, y=0.64, x2=0.44, y2=0.53, style="aiko")], "sfx": []},
            {"name": "the catch", "duration": 4.3, "transition": "cut",
             "visual": vis(source="pov", trim=11.6, speed=[{"at": 0, "rate": 1}, {"at": 1.4, "rate": 0.35},
                                                            {"at": 3.2, "rate": 0.35}, {"at": 4.3, "rate": 1}]),
             "pip": NO_PIP, "overlays": [], "sfx": [{"asset": "boom", "at": 2.7, "volume": 0.9}]},
            {"name": "saw / knew", "duration": 5.0, "transition": "cut",
             "visual": vis("split", source="pov", trim=8.0, second_source="top", second_trim=8.0, split="column"),
             "pip": NO_PIP, "overlays": [ov("meme", template="expectation-reality", text="What I saw", text2="What she knew")],
             "sfx": []},
        ],
        "script": [
            {"speaker": "narrator", "at": 0.2, "text": "I sprinted once. Just once."},
            {"speaker": "narrator", "at": 3.6, "text": "The lights flicker first. That's her warning."},
            {"speaker": "narrator", "at": 7.4, "text": "She heard me, and guessed where I'd hide."},
            {"speaker": "aiko", "at": 14.4, "text": "This will be noted in your file."},
            {"speaker": "narrator", "at": 18.0, "text": "She never saw me. She listened."},
        ],
        "music": {"asset": "night-shift", "offset": 0, "volume": 0.3},
        "end_card": {"duration": 3, "cta": "Follow for the next shift"},
        "captions": "words",
    }


def replay(folder: Path, answers: dict[str, object]) -> Path:
    """A replay folder: {"<step>" or "<step>.<n>": answer}."""
    folder.mkdir(parents=True, exist_ok=True)
    for name, answer in answers.items():
        (folder / f"{name}.json").write_text(json.dumps(answer, ensure_ascii=False), encoding="utf-8")
    return folder


# A reference video's study (dark for 2 s, then bright), as Claude would answer it.
STUDY = {
    "name": "dark-then-light", "title": "Dark, then light",
    "summary": "A dark screen snaps to bright at two seconds.",
    "why_it_works": ["The snap is a surprise."],
    "hook": {"until": 2.0, "kind": "visual-shock", "on_screen": "", "spoken": "", "picture": "A dark frame."},
    "beats": [{"from": 0, "to": 2.0, "role": "hook", "picture": "dark", "on_screen": "", "spoken": "", "edit": "a hold"},
              {"from": 2.0, "to": 4.0, "role": "payoff", "picture": "bright", "on_screen": "", "spoken": "", "edit": "a hard cut"}],
    "pacing": {"length": 4.0, "shots": 2, "average_shot": 2.0, "rhythm": "one hard cut"},
    "format": {"layout": "Full frame.", "text": "None.", "captions": "None.", "motion": "None.", "transitions": "A hard cut.",
               "sound": "A steady tone.", "ending": "On the bright frame."},
    "recipe": [{"element": "the hard cut", "editor": "cut", "how": "cut at 2 s"}],
    "shots": [{"camera": "cctv", "subject": "aiko", "layers": [], "use": "the reveal"}],
    "missing": [],
    "kehai": {"moments": "a blackout that ends with her close", "tags": ["blackout"], "idea": "The lights come back and she is there."},
    "avoid": ["its tone"],
}
