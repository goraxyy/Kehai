"""Drafts become edits: what the checks catch (each one a line the model can act on), how text,
translations and voice lines carry over, and arrows and circles aimed from a shot's track."""
import copy

import pytest

from km import drafts, schemas, timeline

from helpers import ctx, draft, ov, track, vis


def test_a_good_draft_has_no_problems_and_makes_a_valid_edit():
    d, c = draft(), ctx()
    assert drafts.problems(d, c) == []
    edit, missing = drafts.to_edit(d, c)
    assert schemas.errors("edit", edit) == []
    assert missing == set() and edit["languages"] == ["en"]
    assert timeline.total(edit) == pytest.approx(24.0), "21.4 s of scenes, less the 0.4 s fade, and the 3 s end card"
    assert edit["scenes"][3]["visual"]["speed"][1] == {"at": 1.4, "rate": 0.35}
    assert edit["scenes"][2]["pip"]["transparent"] is True, "her mind keeps its alpha"
    assert edit["scenes"][4]["visual"]["type"] == "split"
    assert edit["music"]["src"] == "assets/music/night-shift.wav"
    assert all(isinstance(t["text"], dict) and "en" in t["text"] for t in edit["script"])


def test_line_ids_come_from_what_is_said():
    a, _ = drafts.to_edit(draft(), ctx())
    d = draft()
    d["script"][1]["text"] = "The lights always flicker first."
    b, _ = drafts.to_edit(d, ctx())
    ids_a = [l["id"] for l in a["script"]]
    ids_b = [l["id"] for l in b["script"]]
    assert ids_a[0] == ids_b[0] and ids_a[2:] == ids_b[2:], "unchanged lines keep their audio"
    assert ids_a[1] != ids_b[1]
    twice = drafts.line_ids([{"speaker": "narrator", "text": "Again."}] * 2)
    assert twice[0][0] != twice[1][0]


def test_known_translations_carry_over_and_the_language_is_added_when_complete():
    d, c = draft(), ctx()
    english, _ = drafts.to_edit(d, c)
    every = set()

    def collect(node):
        if isinstance(node, dict):
            if set(node) == {"en"}:
                every.add(node["en"])
            for v in node.values():
                collect(v)
        elif isinstance(node, list):
            for v in node:
                collect(v)

    collect(english)
    table = {e: f"RU {e}" for e in every}
    edit, missing = drafts.to_edit(d, c, {"ru": table})
    assert missing == set() and edit["languages"] == ["en", "ru"]
    assert edit["scenes"][0]["overlays"][0]["text"]["ru"] == "RU One sprint was *all* it took."
    table.pop("This will be noted in your file.")
    edit, missing = drafts.to_edit(d, c, {"ru": table})
    assert missing == {"ru"} and edit["languages"] == ["en"]


def problems_after(change) -> list[str]:
    d = draft()
    change(d)
    return drafts.problems(d, ctx())


@pytest.mark.parametrize("change, expect", [
    (lambda d: d["scenes"][1]["visual"].update(trim=17.5), "needs 21.10s of 'cctv'"),
    (lambda d: d["scenes"][3]["visual"].update(trim=18.0), "but it is 20.13s long"),
    (lambda d: d["scenes"][0]["visual"].update(source="drone"), "no shot 'drone'"),
    (lambda d: d["scenes"][0]["overlays"].clear(), "needs a hook overlay"),
    (lambda d: d["scenes"][0]["overlays"][0].update(text="I sprinted *once*."), "repeats what the voice says"),
    (lambda d: d["scenes"][1]["overlays"][0].update(to=9.0), "doesn't fit the scene"),
    (lambda d: d["scenes"][1]["sfx"][0].update(asset="night-shift"), "is a music, not a sfx"),
    (lambda d: d["scenes"][1]["sfx"][0].update(asset="nope"), "no asset 'nope'"),
    (lambda d: d["scenes"][4]["visual"].update(split="none"), "split 'row' or 'column'"),
    (lambda d: d["scenes"][3]["overlays"].append(ov("meme", template="expectation-reality", text="a", text2="b")),
     "goes on a split scene"),
    (lambda d: d["script"][2].update(text="She heard me, worked out roughly where I must be hiding, and then quietly walked straight over to me."),
     "runs into the next line"),
    (lambda d: d["script"][4].update(text="She never saw me. She listened, and she waited, and she came for me anyway."),
     "runs into the last scene"),
    (lambda d: d["script"][0].update(text="I sprinted *once*."), "no *stars* in spoken lines"),
    (lambda d: d["end_card"].update(cta="Follow us for the next night shift, it gets worse"), "at most 32"),
    (lambda d: d["end_card"].update(duration=9), "outside 2 to 4"),
    (lambda d: d["scenes"][0].update(duration=50), "a short runs 12 to 60 s"),
    (lambda d: d["scenes"][3]["visual"]["speed"].append({"at": 4.0, "rate": 12}), "outside 0.1 to 8"),
    (lambda d: d["scenes"][1]["visual"]["zoom"].append({"at": 1, "scale": 9, "x": 0.5, "y": 0.5}), "out of range"),
    (lambda d: d["scenes"][2]["pip"].update(size=0.9), "outside 0.15 to 0.7"),
    (lambda d: d.update(id="One Sprint!"), "lowercase words joined by dashes"),
])
def test_the_checks_catch_what_would_make_a_bad_video(change, expect):
    found = problems_after(change)
    assert any(expect in p for p in found), found


def test_known_line_lengths_replace_the_estimate():
    d = draft()
    line = d["script"][2]
    assert drafts.problems(d, ctx(), {line["text"]: 6.8}) == []
    found = drafts.problems(d, ctx(), {line["text"]: 7.5})
    assert any("takes 7.50s to say" in p for p in found), found


def tracked_ctx(karen=lambda t: (0.3 + 0.02 * t, 0.4, 0.04), you=lambda t: None):
    c = ctx()
    for s in c["shots"]:
        if s["id"] == "top":
            s["track"] = track(20.13, karen, you)
    return c


def aimed(kind: str, scene: int = 2, **k) -> dict:
    d = draft()
    d["scenes"][scene]["overlays"] = [ov(kind, start=1.0, to=2.0, target="karen", x=0, y=0, **k)]
    return d


def test_a_circle_with_a_target_stays_around_her():
    d, c = aimed("circle"), tracked_ctx()
    assert drafts.problems(d, c) == []
    edit, _ = drafts.to_edit(d, c)
    assert schemas.errors("edit", edit) == []
    circle = edit["scenes"][2]["overlays"][0]
    # Scene 3 shows `top` from 7.0 s at 1x; the circle is up from 1.0 to 2.0 s: shot time 8.0 to 9.0 s.
    assert circle["x"] == pytest.approx(0.3 + 0.02 * 8.0, abs=0.002)
    follow = circle["follow"]
    assert [k["at"] for k in follow] == pytest.approx([i / 10 for i in range(11)])
    assert follow[-1]["x"] == pytest.approx(0.3 + 0.02 * 9.0, abs=0.002), "it moves with her"
    assert circle["radius"] == pytest.approx(0.04 * 1.7, abs=0.002)


def test_an_arrow_with_a_target_ends_at_her_edge_and_follows_her():
    d, c = aimed("arrow"), tracked_ctx(karen=lambda t: (0.6 + 0.01 * t, 0.3, 0.05))
    edit, _ = drafts.to_edit(d, c)
    a = edit["scenes"][2]["overlays"][0]
    assert a["from_x"] < a["to_x"] < 0.68 and a["from_y"] > a["to_y"] > 0.3, "from towards the middle, stopping short of her"
    assert 0.07 <= a["from_x"] <= 0.93 and 0.10 <= a["from_y"] <= 0.70, "starts inside the safe area"
    assert a["follow"][-1]["x"] > a["follow"][0]["x"], "the head moves with her; the tail stays"


def test_a_target_follows_the_speed_and_the_zoom():
    d = aimed("circle", scene=0)
    d["scenes"][0]["visual"]["zoom"] = [{"at": 0, "scale": 2, "x": 0.5, "y": 0.5}]
    d["scenes"][0]["visual"]["speed"] = [{"at": 0, "rate": 2}]
    c = tracked_ctx(karen=lambda t: (0.55 + 0.01 * t, 0.5, 0.04))
    edit, _ = drafts.to_edit(d, c)
    circle = edit["scenes"][0]["overlays"][0]
    # From scene time 1.0 at 2x after a 1.2 s trim: shot time 3.2; 0.05 + 0.032 right of the centre, doubled.
    assert circle["x"] == pytest.approx(0.5 + 2 * (0.05 + 0.032), abs=0.003)
    assert circle["radius"] == pytest.approx(0.04 * 2 * 1.7, abs=0.003)


def test_a_mark_waits_while_she_is_briefly_off_screen():
    d = aimed("circle")
    c = tracked_ctx(karen=lambda t: None if 8.3 <= t < 8.6 else (0.4, 0.4, 0.04))
    edit, _ = drafts.to_edit(d, c)
    xs = [k["x"] for k in edit["scenes"][2]["overlays"][0]["follow"]]
    assert xs == pytest.approx([0.4] * 11)


def test_a_target_off_screen_is_refused_with_when_she_is_on_screen():
    d = aimed("circle")
    c = tracked_ctx(karen=lambda t: (0.5, 0.5, 0.04) if t < 5 else None)
    found = drafts.problems(d, c)
    assert any("isn't in the picture" in p and "0.0–5.0s" in p for p in found), found


def test_a_target_needs_a_tracked_single_shot():
    found = drafts.problems(aimed("circle", scene=4), tracked_ctx())
    assert any("only works on a single-shot scene" in p for p in found), found
    found = drafts.problems(aimed("circle", scene=1), tracked_ctx())
    assert any("has no track" in p for p in found), found
    d = draft()
    d["scenes"][0]["overlays"].append(ov("label", text="hi", target="karen"))
    assert any("only arrows and circles take a target" in p for p in drafts.problems(d, tracked_ctx()))


def test_the_timeline_matches_the_editors():
    edit, _ = drafts.to_edit(draft(), ctx())
    starts = timeline.scene_starts(edit)
    assert starts[:4] == pytest.approx([0, 3.5, 6.7, 11.7], abs=1e-6), "the fade overlaps 0.4 s"
    assert timeline.scenes_end(edit) == pytest.approx(21.0)
    assert timeline.source_seconds([{"at": 0, "rate": 1}, {"at": 2, "rate": 3}], 4) == pytest.approx(2 * 2 + 2 * 3)
    assert timeline.zoom_at([{"at": 0, "scale": 1, "x": 0.5, "y": 0.5}, {"at": 2, "scale": 2, "x": 0.5, "y": 0.5}], 1)[0] == pytest.approx(1.5)


def test_every_overlay_kind_converts():
    d = draft()
    d["scenes"][0]["overlays"] += [
        ov("lower-third", start=0.5, text="Shift 3", text2="Rung F"),
        ov("circle", start=0.5, to=1.5, x=0.4, y=0.4, size=0.1, style="you"),
        ov("lottie", asset="pulse", x=0.5, y=0.3, size=0.3),
        ov("meme", template="top-bottom", text="me", text2="her"),
        ov("meme", template="nobody", text="Please return to your station."),
        ov("label", text="small", size=0.7, style="paper"),
    ]
    d["scenes"][1]["visual"] = vis("image", source="store", zoom=[{"at": 0, "scale": 1.1, "x": 0.5, "y": 0.5}])
    assert drafts.problems(d, ctx()) == []
    edit, _ = drafts.to_edit(d, ctx())
    assert schemas.errors("edit", edit) == []
    kinds = [o["type"] for o in edit["scenes"][0]["overlays"]]
    assert kinds == ["hook", "lowerThird", "circle", "lottie", "meme", "meme", "label"]
    assert edit["scenes"][0]["overlays"][-1]["size"] == "s"
    assert edit["scenes"][1]["visual"]["type"] == "image"


def test_a_long_video_has_its_own_limits():
    d = copy.deepcopy(draft())
    c = ctx(kind="long", fmt="16:9")
    found = drafts.problems(d, c)
    assert any("a long runs 180 to 1200 s" in p for p in found)
    assert not any("hook" in p for p in found), "a long video needs no hook overlay"
