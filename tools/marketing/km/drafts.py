"""Claude's drafts (schemas/llm/draft.schema.json) and the edits they become.

A draft names shots and assets by id and holds English text; `to_edit` turns it into an
edit.json (paths, {"en": …} text, translations carried over), and `problems` lists everything
that would make a bad video, for the repair round: ranges the API schema can't express, ids that
don't exist, shots too short for their trim and speed, overlays outside their scene, lines that
can't be said in the time they're given.

The context (<edit folder>/context.json) is what the draft was written against:
  {"kind", "format", "fps", "source": {...}, "shots": [{"id", "src", "seconds", "alpha", ...}],
   "assets": [{"id", "type", "file", ...}]}
"""
from __future__ import annotations

import hashlib
import re

from . import timeline

TRANSITION = 0.4
WORDS_PER_SECOND = {"narrator": 2.6, "karen": 2.2}
LIMITS = {"short": (12.0, 60.0), "long": (180.0, 1200.0)}
SLUG = re.compile(r"^[a-z0-9][a-z0-9-]{2,59}$")


def _shots(ctx: dict) -> dict:
    return {s["id"]: s for s in ctx.get("shots", [])}


def _assets(ctx: dict) -> dict:
    return {a["id"]: a for a in ctx.get("assets", [])}


class _Text:
    """Makes {"en": …} text objects, adding the translations already known for the same English."""

    def __init__(self, translations: dict | None):
        self.translations = translations or {}
        self.missing: set[str] = set()

    def __call__(self, english: str) -> dict:
        t = {"en": english}
        for lang, table in self.translations.items():
            if english in table:
                t[lang] = table[english]
            else:
                self.missing.add(lang)
        return t


def line_ids(lines: list[dict]) -> list[tuple[str, dict]]:
    """Ids from what a line says, so an unchanged line keeps its audio through a revision."""
    out, seen = [], {}
    for line in lines:
        base = line["speaker"][0] + hashlib.sha1(line["text"].strip().encode("utf-8")).hexdigest()[:8]
        seen[base] = seen.get(base, 0) + 1
        out.append((base if seen[base] == 1 else f"{base}-{seen[base]}", line))
    return out


def speed_of(keys: list[dict]):
    if not keys:
        return None
    if len(keys) == 1:
        return keys[0]["rate"]
    return [{"at": k["at"], "rate": k["rate"]} for k in sorted(keys, key=lambda k: k["at"])]


def _shot(ctx_shot: dict, trim: float, keys: list[dict], zoom: list[dict], volume: float, label: str, T) -> dict:
    shot = {"type": "shot", "src": ctx_shot["src"]}
    if trim > 0:
        shot["trim"] = round(trim, 3)
    speed = speed_of(keys)
    if speed is not None and speed != 1:
        shot["speed"] = speed
    if zoom:
        shot["zoom"] = [{"at": z["at"], "scale": z["scale"], "x": z["x"], "y": z["y"]} for z in sorted(zoom, key=lambda z: z["at"])]
    if abs(volume - 0.7) > 1e-6:
        shot["volume"] = round(volume, 3)
    if label:
        shot["label"] = T(label)
    return shot


def _span(o: dict, into: dict) -> dict:
    if o["from"] > 0:
        into["from"] = o["from"]
    if o["to"] > 0:
        into["to"] = o["to"]
    return into


def _overlay(o: dict, assets: dict, T, scene: dict, shots: dict, fmt: str) -> dict:
    k = o["kind"]
    colour = o["style"]
    if k in ("arrow", "circle") and o["target"] != "none":
        path = aim(scene, o, shots, fmt)
        _, x, y, r = path[0]
        if k == "circle":
            radius = min(0.35, max(0.04, max(p[3] for p in path) * 1.7))
            return _span(o, {"type": "circle", "x": round(x, 3), "y": round(y, 3), "radius": round(radius, 3), "color": colour,
                             "follow": [{"at": t, "x": round(px, 3), "y": round(py, 3)} for t, px, py, _ in path]})
        tail = _arrow_from((x, y, r), (o["x"], o["y"]), fmt)
        tips = [(t, *_arrow_tip(tail, (px, py, pr), fmt)) for t, px, py, pr in path]
        return _span(o, {"type": "arrow", "from_x": tail[0], "from_y": tail[1], "to_x": tips[0][1], "to_y": tips[0][2],
                         "color": colour, "follow": [{"at": t, "x": tx, "y": ty} for t, tx, ty in tips]})
    if k == "hook":
        return _span(o, {"type": "hook", "text": T(o["text"])})
    if k == "label":
        size = "s" if o["size"] < 0.85 else "l" if o["size"] > 1.2 else "m"
        style = colour if colour in ("crimson", "ink", "paper") else "crimson"
        return _span(o, {"type": "label", "text": T(o["text"]), "x": o["x"], "y": o["y"], "style": style, "size": size})
    if k == "lower-third":
        out = {"type": "lowerThird", "title": T(o["text"])}
        if o["text2"]:
            out["subtitle"] = T(o["text2"])
        return _span(o, out)
    if k == "arrow":
        return _span(o, {"type": "arrow", "from_x": o["x"], "from_y": o["y"], "to_x": o["x2"], "to_y": o["y2"], "color": colour})
    if k == "circle":
        return _span(o, {"type": "circle", "x": o["x"], "y": o["y"], "radius": o["size"], "color": colour})
    if k == "meme":
        out = {"type": "meme", "template": o["template"]}
        if o["template"] in ("top-bottom", "expectation-reality"):
            if o["text"]:
                out["top"] = T(o["text"])
            if o["text2"]:
                out["bottom"] = T(o["text2"])
        else:
            out["text"] = T(o["text"])
        return _span(o, out)
    return _span(o, {"type": k, "src": assets[o["asset"]]["file"], "x": o["x"], "y": o["y"], "width": o["size"]})


SIZES = {"9:16": (1080, 1920), "16:9": (1920, 1080)}


def _track_at(shot: dict, who: str, source_t: float):
    """The body at a moment of the shot, between the track's samples; None when off screen."""
    track = shot.get("track")
    if not track:
        return None
    samples, hz = track[who], track["hz"]
    f = source_t * hz
    i = int(f)
    if i < 0 or i >= len(samples) or samples[i] is None:
        return None
    nxt = samples[i + 1] if i + 1 < len(samples) else None
    if nxt is None:
        return samples[i]
    u = f - i
    return [a + (b - a) * u for a, b in zip(samples[i], nxt)]


def _visible_spans(shot: dict, who: str) -> str:
    track = shot.get("track") or {}
    samples, hz = track.get(who, []), track.get("hz", 10)
    spans, start = [], None
    for i, s in enumerate(samples + [None]):
        if s is not None and start is None:
            start = i
        elif s is None and start is not None:
            spans.append(f"{start / hz:.1f}–{i / hz:.1f}s")
            start = None
    return ", ".join(spans) or "never"


FOLLOW_STEP = 0.1


def aim(scene: dict, o: dict, shots: dict, fmt: str):
    """Where an arrow or circle with a target points through its time on screen: a list of
    (seconds since it appeared, x, y, r) every 0.1 s, or why it can't be aimed."""
    v = scene["visual"]
    if v["kind"] != "shot":
        return f"a target only works on a single-shot scene (this one is {v['kind']}); give x and y and target none"
    shot = shots.get(v["source"])
    if shot is None:
        return None                      # reported elsewhere
    if not shot.get("track"):
        return f"shot {v['source']!r} has no track (rendered before tracking existed); give x and y and target none"
    end = min(o["to"] or scene["duration"], scene["duration"])
    if end <= o["from"]:
        return None                      # reported elsewhere
    path = []                            # (seconds since it appeared, where or None)
    steps = max(1, round((end - o["from"]) / FOLLOW_STEP))
    for k in range(steps + 1):
        t = o["from"] + (end - o["from"]) * k / steps
        source_t = v["trim"] + timeline.source_seconds(speed_of(v["speed"]), t)
        where = _track_at(shot, o["target"], source_t)
        placed = None
        if where is not None:
            scale, zx, zy = timeline.zoom_at(v["zoom"], t)
            x, y, r = zx + (where[0] - zx) * scale, zy + (where[1] - zy) * scale, where[2] * scale
            if 0.02 <= x <= 0.98 and 0.02 <= y <= 0.98:
                placed = (x, y, r)
        path.append((round(t - o["from"], 3), placed))
    seen = sum(1 for _, p in path if p is not None)
    if path[0][1] is None or seen < len(path) / 2:
        first = v["trim"] + timeline.source_seconds(speed_of(v["speed"]), o["from"])
        return (f"{o['target']} isn't in the picture of {v['source']!r} for most of {o['from']:.1f}–{end:.1f}s of the "
                f"scene ({first:.1f}s into the shot at the start); in that shot {o['target']} is on screen "
                f"{_visible_spans(shot, o['target'])}")
    out, last = [], path[0][1]
    for t, p in path:                    # off screen for a moment: the mark waits where it was
        last = p or last
        out.append((t, *last))
    return out


def _arrow_from(target: tuple[float, float, float], start: tuple[float, float], fmt: str) -> tuple[float, float]:
    """Where an arrow at the body starts: `start`, or 0.22 of the frame's width away towards the
    middle when start is (0, 0); inside the safe area either way."""
    w, h = SIZES[fmt]
    tx, ty, r = target
    if start == (0, 0):
        dx, dy = (0.5 - tx) * w, (0.45 - ty) * h
        if (dx * dx + dy * dy) ** 0.5 < 0.08 * w:
            dx, dy = -0.6 * w, 0.8 * w
        n = (dx * dx + dy * dy) ** 0.5
        reach = 0.22 * w                  # pixels
        sx, sy = tx + dx / n * reach / w, ty + dy / n * reach / h
    else:
        sx, sy = start
    return round(min(0.93, max(0.07, sx)), 3), round(min(0.70, max(0.10, sy)), 3)


def _arrow_tip(tail: tuple[float, float], target: tuple[float, float, float], fmt: str) -> tuple[float, float]:
    """Where the arrow's head stops: at the body's edge, on the side it comes from."""
    w, h = SIZES[fmt]
    tx, ty, r = target
    dx, dy = (tail[0] - tx) * w, (tail[1] - ty) * h
    n = max(1e-6, (dx * dx + dy * dy) ** 0.5)
    stop = min(r * w * 1.25, n * 0.5)
    return round(tx + dx / n * stop / w, 3), round(ty + dy / n * stop / h, 3)


def to_edit(draft: dict, ctx: dict, translations: dict | None = None) -> tuple[dict, set[str]]:
    """The edit, and the languages some text is still missing in. Assumes `problems` found none."""
    shots, assets = _shots(ctx), _assets(ctx)
    T = _Text(translations)
    scenes = []
    for i, s in enumerate(draft["scenes"]):
        v = s["visual"]
        if v["kind"] == "shot":
            visual = _shot(shots[v["source"]], v["trim"], v["speed"], v["zoom"], v["game_volume"], v["label"], T)
        elif v["kind"] == "split":
            visual = {"type": "split", "direction": v["split"] if v["split"] != "none" else "column",
                      "a": _shot(shots[v["source"]], v["trim"], v["speed"], [], v["game_volume"], v["label"], T),
                      "b": _shot(shots[v["second_source"]], v["second_trim"], v["speed"], [], v["game_volume"], v["second_label"], T)}
        elif v["kind"] == "image":
            visual = {"type": "image", "src": assets[v["source"]]["file"]}
            if v["zoom"]:
                visual["zoom"] = [{"at": z["at"], "scale": z["scale"], "x": z["x"], "y": z["y"]} for z in v["zoom"]]
        else:
            visual = {"type": "color", "color": v["colour"] if v["colour"] != "none" else "ink"}
        scene = {"id": f"s{i + 1:02d}", "duration": round(s["duration"], 3), "visual": visual}
        if i > 0 and s["transition"] != "cut":
            scene["transition"] = {"type": s["transition"], "duration": TRANSITION}
        p = s["pip"]
        if p["source"]:
            pip_shot = shots[p["source"]]
            pip = {"src": pip_shot["src"], "corner": p["corner"], "size": p["size"], "transparent": bool(pip_shot.get("alpha"))}
            if p["trim"] > 0:
                pip["trim"] = p["trim"]
            if p["label"]:
                pip["label"] = T(p["label"])
            if p["from"] > 0:
                pip["from"] = p["from"]
            if p["to"] > 0:
                pip["to"] = p["to"]
            scene["pip"] = pip
        if s["overlays"]:
            scene["overlays"] = [_overlay(o, assets, T, s, shots, ctx["format"]) for o in s["overlays"]]
        if s["sfx"]:
            scene["sfx"] = [{"src": assets[x["asset"]]["file"], "at": x["at"], **({"volume": x["volume"]} if x["volume"] != 1 else {})}
                            for x in s["sfx"]]
        scenes.append(scene)

    edit: dict = {"version": 1, "id": draft["id"], "kind": ctx["kind"], "format": ctx["format"], "fps": ctx["fps"],
                  "languages": ["en"], "title": T(draft["title"])}
    if ctx.get("source"):
        edit["source"] = ctx["source"]
    edit["scenes"] = scenes
    if draft["script"]:
        edit["script"] = [{"id": i, "speaker": l["speaker"], "at": round(l["at"], 3), "text": T(l["text"])}
                          for i, l in line_ids(sorted(draft["script"], key=lambda l: l["at"]))]
    m = draft["music"]
    if m["asset"]:
        music = {"src": assets[m["asset"]]["file"], "volume": m["volume"]}
        if m["offset"] > 0:
            music["offset"] = m["offset"]
        edit["music"] = music
    edit["captions"] = {"mode": draft["captions"]}
    edit["endCard"] = {"duration": draft["end_card"]["duration"], "cta": T(draft["end_card"]["cta"])}
    complete = [lang for lang in (translations or {}) if lang not in T.missing]
    edit["languages"] = ["en"] + [l for l in complete if l != "en"]
    return edit, T.missing


def _in(value: float, lo: float, hi: float) -> bool:
    return lo - 1e-9 <= value <= hi + 1e-9


def _plain(text: str) -> str:
    return " ".join(re.findall(r"[\w'’]+", text.replace("*", "").lower()))


def words(text: str) -> int:
    return len(re.findall(r"[\w'’-]+", text))


def speaking_time(line: dict) -> float:
    return words(line["text"]) / WORDS_PER_SECOND[line["speaker"]] + 0.25


def problems(draft: dict, ctx: dict, spoken: dict[str, float] | None = None) -> list[str]:
    """Everything wrong with a draft, each as one line the model can act on. `spoken` gives the
    real length of lines already voiced (by their text), used instead of the estimate."""
    out: list[str] = []
    shots, assets = _shots(ctx), _assets(ctx)

    def asset(aid: str, kinds: tuple[str, ...], where: str) -> bool:
        a = assets.get(aid)
        if a is None:
            out.append(f"{where}: no asset {aid!r} (the assets are: {', '.join(sorted(x for x in assets if assets[x]['type'] in kinds)) or 'none of that kind'})")
            return False
        if a["type"] not in kinds:
            out.append(f"{where}: {aid!r} is a {a['type']}, not a {' or '.join(kinds)}")
            return False
        return True

    def shot_long_enough(sid: str, trim: float, keys: list[dict], duration: float, where: str) -> None:
        s = shots.get(sid)
        if s is None:
            out.append(f"{where}: no shot {sid!r} (the shots are: {', '.join(sorted(shots)) or 'none'})")
            return
        if trim < 0:
            out.append(f"{where}: trim can't be negative")
        need = trim + timeline.source_seconds(speed_of(keys), duration)
        if need > s["seconds"] + 0.05:
            out.append(f"{where}: needs {need:.2f}s of {sid!r} (trim {trim:.2f}s + {need - trim:.2f}s at its speed) "
                       f"but it is {s['seconds']:.2f}s long")

    if not SLUG.match(draft["id"]):
        out.append(f"id {draft['id']!r}: lowercase words joined by dashes, 3 to 60 characters")
    if not draft["scenes"]:
        out.append("there are no scenes")
        return out

    for i, s in enumerate(draft["scenes"]):
        w = f"scene {i + 1} ({s['name']})"
        d = s["duration"]
        if not _in(d, 0.4, 60):
            out.append(f"{w}: duration {d}s is outside 0.4 to 60 s")
        v = s["visual"]
        for k in v["speed"]:
            if not _in(k["rate"], 0.1, 8):
                out.append(f"{w}: speed rate {k['rate']} is outside 0.1 to 8")
            if not _in(k["at"], 0, d):
                out.append(f"{w}: a speed key at {k['at']}s is outside the scene (0 to {d}s)")
        for z in v["zoom"]:
            if not _in(z["scale"], 0.5, 6) or not _in(z["x"], 0, 1) or not _in(z["y"], 0, 1):
                out.append(f"{w}: zoom key {z} out of range (scale 0.5 to 6, x and y 0 to 1)")
            if not _in(z["at"], 0, d):
                out.append(f"{w}: a zoom key at {z['at']}s is outside the scene")
        if v["kind"] in ("shot", "split"):
            shot_long_enough(v["source"], v["trim"], v["speed"], d, f"{w} visual")
            if not _in(v["game_volume"], 0, 2):
                out.append(f"{w}: game_volume {v['game_volume']} is outside 0 to 2")
        if v["kind"] == "split":
            if v["split"] == "none":
                out.append(f"{w}: a split needs split 'row' or 'column'")
            if v["zoom"]:
                out.append(f"{w}: a split can't zoom (zoom must be [])")
            shot_long_enough(v["second_source"], v["second_trim"], v["speed"], d, f"{w} second shot")
        elif v["kind"] == "image":
            asset(v["source"], ("image",), f"{w} visual")
        elif v["kind"] == "colour" and v["colour"] == "none":
            out.append(f"{w}: a colour scene needs a colour")
        p = s["pip"]
        if p["source"]:
            span = (p["to"] or d) - p["from"]
            if span <= 0 or not _in(p["to"] or d, 0, d):
                out.append(f"{w} pip: from {p['from']} to {p['to']} doesn't fit the scene (0 to {d}s)")
            shot_long_enough(p["source"], p["trim"], [], max(span, 0), f"{w} pip")
            if not _in(p["size"], 0.15, 0.7):
                out.append(f"{w} pip: size {p['size']} is outside 0.15 to 0.7")
        for j, o in enumerate(s["overlays"]):
            ow = f"{w} overlay {j + 1} ({o['kind']})"
            end = o["to"] or d
            if o["from"] < 0 or end <= o["from"] or end > d + 0.01:
                out.append(f"{ow}: from {o['from']} to {o['to']} doesn't fit the scene (0 to {d}s; to 0 means the end)")
            k = o["kind"]
            if k in ("hook", "label", "lower-third") and not o["text"].strip():
                out.append(f"{ow}: needs text")
            if k == "hook" and len(o["text"]) > 70:
                out.append(f"{ow}: {len(o['text'])} characters; a hook is at most 70")
            if k == "label" and len(o["text"]) > 40:
                out.append(f"{ow}: {len(o['text'])} characters; a label is at most 40")
            if k == "meme":
                if o["template"] == "none":
                    out.append(f"{ow}: needs a template")
                elif o["template"] in ("pov", "nobody", "caption-bar") and not o["text"].strip():
                    out.append(f"{ow}: needs text")
                elif o["template"] == "top-bottom" and not (o["text"].strip() and o["text2"].strip()):
                    out.append(f"{ow}: needs text (top) and text2 (bottom)")
                elif o["template"] == "expectation-reality" and v["kind"] != "split":
                    out.append(f"{ow}: expectation-reality goes on a split scene")
            targeted = k in ("arrow", "circle") and o["target"] != "none"
            if o["target"] != "none" and k not in ("arrow", "circle"):
                out.append(f"{ow}: only arrows and circles take a target")
            if targeted:
                placed = aim(s, o, shots, ctx["format"])
                if isinstance(placed, str):
                    out.append(f"{ow}: {placed}")
            if k in ("label", "circle", "arrow", "image", "gif", "lottie") and not (_in(o["x"], 0, 1) and _in(o["y"], 0, 1)):
                out.append(f"{ow}: x and y must be 0 to 1")
            if k == "arrow" and not targeted and not (_in(o["x2"], 0, 1) and _in(o["y2"], 0, 1)):
                out.append(f"{ow}: x2 and y2 must be 0 to 1")
            if k == "arrow" and not targeted and abs(o["x2"] - o["x"]) + abs(o["y2"] - o["y"]) < 0.05:
                out.append(f"{ow}: starts and ends in the same place")
            if k == "circle" and not targeted and not _in(o["size"], 0.03, 0.5):
                out.append(f"{ow}: radius (size) {o['size']} is outside 0.03 to 0.5")
            if k in ("image", "gif", "lottie"):
                if asset(o["asset"], (k,), ow) and not _in(o["size"], 0.05, 1):
                    out.append(f"{ow}: width (size) {o['size']} is outside 0.05 to 1")
            if k in ("arrow", "circle") and o["style"] not in ("crimson", "ink", "paper", "karen", "you"):
                out.append(f"{ow}: style must be a colour")
        for j, x in enumerate(s["sfx"]):
            xw = f"{w} sfx {j + 1}"
            asset(x["asset"], ("sfx",), xw)
            if not _in(x["at"], 0, d):
                out.append(f"{xw}: at {x['at']}s is outside the scene")
            if not _in(x["volume"], 0, 2):
                out.append(f"{xw}: volume {x['volume']} is outside 0 to 2")

    if ctx["kind"] == "short":
        first = draft["scenes"][0]["overlays"]
        hooks = [o for o in first if o["kind"] == "hook" and o["from"] <= 0.5]
        if not hooks:
            out.append("a short opens on its hook: scene 1 needs a hook overlay from 0 (at the latest 0.5 s)")
        opening = " ".join(_plain(l["text"]) for l in draft["script"] if l["at"] < 2.5)
        for h in hooks:
            if _plain(h["text"]) and _plain(h["text"]) in opening:
                out.append(f"the hook {h['text']!r} repeats what the voice says (the captions already show those words); "
                           "word the on-screen hook differently, same idea")

    # The whole video, as the editor will time it.
    edit_like = {"fps": ctx["fps"], "scenes": [
        {"duration": s["duration"], **({"transition": {"type": s["transition"], "duration": TRANSITION}} if i and s["transition"] != "cut" else {})}
        for i, s in enumerate(draft["scenes"])]}
    end = timeline.scenes_end(edit_like) if all(s["duration"] > 0 for s in draft["scenes"]) else 0
    card = draft["end_card"]["duration"]
    lo, hi = LIMITS[ctx["kind"]]
    if not _in(end + card, lo, hi):
        out.append(f"the video runs {end + card:.1f}s with its end card; a {ctx['kind']} runs {lo:.0f} to {hi:.0f} s")
    card_range = (2, 4) if ctx["kind"] == "short" else (2, 15)
    if not _in(card, *card_range):
        out.append(f"end_card duration {card}s is outside {card_range[0]} to {card_range[1]} s")
    if not draft["end_card"]["cta"].strip():
        out.append("end_card needs a cta")
    elif len(draft["end_card"]["cta"]) > 32:
        out.append(f"end_card cta: {len(draft['end_card']['cta'])} characters; at most 32 (translations run longer)")

    lines = sorted(draft["script"], key=lambda l: l["at"])
    for i, line in enumerate(lines):
        lw = f"script line {i + 1} ({line['text'][:30]!r})"
        if not line["text"].strip():
            out.append(f"{lw}: empty")
            continue
        if "*" in line["text"]:
            out.append(f"{lw}: no *stars* in spoken lines")
        if line["at"] < 0 or line["at"] >= end:
            out.append(f"{lw}: starts at {line['at']}s, outside the scenes (0 to {end:.2f}s)")
        known = (spoken or {}).get(line["text"].strip())
        takes = known + 0.12 if known is not None else speaking_time(line)
        finish = line["at"] + takes
        limit = lines[i + 1]["at"] if i + 1 < len(lines) else end
        if finish > limit + (0.05 if known is not None else 0.3):
            nxt = "the next line" if i + 1 < len(lines) else "the last scene"
            how = f"takes {known:.2f}s to say" if known is not None else f"about {takes:.1f}s to say ({words(line['text'])} words)"
            out.append(f"{lw}: {how} from {line['at']}s, so it runs into {nxt} at {limit:.2f}s; "
                       + ("give it more time" if known is not None else "shorten it or give it more time"))
    m = draft["music"]
    if m["asset"]:
        asset(m["asset"], ("music",), "music")
        if not _in(m["volume"], 0, 1):
            out.append(f"music volume {m['volume']} is outside 0 to 1")
    return out
