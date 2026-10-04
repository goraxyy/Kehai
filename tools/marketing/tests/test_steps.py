"""Each step script end to end, with Claude's answers replayed from files: pick_moments,
write_short, translate, revise and undo, package, weekly_report, long_video's outline and script,
and the dry runs that send nothing."""
import json
import shutil
import sys

import pytest

import long_video
import package
import pick_moments
import revise
import translate
import weekly_report
import write_short
from km import edits, patterns, schemas
from km.llm import client, ledger

from helpers import FIXTURES, STEM, STUDY, draft, library, replay, shot, track

WEEK = "2026-W40"
PICKS = {
    "week_theme": "She hunts by ear.",
    "shorts": [
        {"name": "one-sprint", "moment": f"{STEM}#1", "pattern": "", "angle": "One sprint was enough.", "hook_idea": "One sprint.",
         "why": "A catch with a turn.", "shots": [
             {"name": "top", "camera": "topdown", "subject": "aiko", "layers": ["cone", "sound"], "alpha": False,
              "start_offset": 0, "end_offset": 0},
             {"name": "mind", "camera": "topdown", "subject": "aiko", "layers": ["belief", "guess"], "alpha": True,
              "start_offset": 0, "end_offset": 0}]},
        {"name": "borrowed", "moment": f"{STEM}#3", "pattern": "", "angle": "That customer isn't shopping.", "hook_idea": "Look again.",
         "why": "Marked good.", "shots": [
             {"name": "pov", "camera": "pov", "subject": "you", "layers": [], "alpha": False, "start_offset": -2, "end_offset": 0}]},
        {"name": "got-away", "moment": f"{STEM}#2", "pattern": "", "angle": "She can't outrun a sprint.", "hook_idea": "Run.",
         "why": "Fairness.", "shots": [
             {"name": "chase", "camera": "chase", "subject": "aiko", "layers": [], "alpha": False, "start_offset": 0, "end_offset": 0}]},
    ],
    "pattern_fit": [],
    "kept": [{"moment": f"{STEM}#3", "use": "short", "reason": "Picked."},
             {"moment": f"{STEM}#7", "use": "bug", "reason": "Shift+F7."}],
}


@pytest.fixture
def world(tmp_path, monkeypatch):
    """A working folder with the asset library, the fixture shift's records, and replayed answers."""
    root = tmp_path / "marketing"
    library(root)
    monkeypatch.setenv("KEHAI_SHIFT_RECORDS", str(FIXTURES))
    monkeypatch.setenv("KEHAI_LLM_REPLAY", str(tmp_path / "replay"))
    monkeypatch.delenv("KEHAI_LLM_MONTHLY_USD", raising=False)
    return root


def run(module, monkeypatch, *args) -> int:
    monkeypatch.setattr(sys, "argv", [module.__name__ + ".py", *args])
    return module.main() or 0


def written_short(root, tmp_path, monkeypatch) -> str:
    """A short written from a folder of rendered shots: one-sprint's draft, replayed."""
    for name, alpha in (("top", False), ("pov", False), ("cctv", False), ("mind", True)):
        shot(root, "shots/sample", name, seconds=20.13, alpha=alpha,
             with_track=track(20.13) if name == "top" else None)
    replay(tmp_path / "replay", {"write_short": draft()})
    assert run(write_short, monkeypatch, "--shots", str(root / "shots/sample"), "--brief", "One sprint was enough",
               "--moment", f"{STEM}#1", "--root", str(root)) == 0
    return "one-sprint"


def test_pick_moments_picks_decides_every_kept_moment_and_plans_the_renders(world, tmp_path, monkeypatch):
    wrong = json.loads(json.dumps(PICKS))
    wrong["kept"] = wrong["kept"][:1]                         # forgot the bug report
    replay(tmp_path / "replay", {"pick_moments.1": wrong, "pick_moments.2": PICKS})
    assert run(pick_moments, monkeypatch, "--week", WEEK, "--root", str(world)) == 0
    plan = json.loads((world / "plans" / WEEK / "picks.json").read_text())
    assert [p["name"] for p in plan["answer"]["shorts"]] == ["one-sprint", "borrowed", "got-away"]
    assert len(ledger.rows(world)) == 2, "the missing kept moment was sent back once"
    renders = {(r["pick"], r["name"]): r for r in plan["renders"]}
    mind = renders[("one-sprint", "mind")]
    assert mind["out"] == f"shots/{WEEK}/one-sprint/mind.webm" and "-alpha" in mind["args"]
    assert mind["args"][mind["args"].index("-size") + 1] == "960x960"
    pov = renders[("borrowed", "pov")]
    assert pov["from"] == pytest.approx(226.0), "two seconds before the moment"
    assert pov["args"][pov["args"].index("-subject") + 1] == "you"


def test_pick_moments_has_nothing_to_do_in_an_empty_week(world, monkeypatch, capsys):
    assert run(pick_moments, monkeypatch, "--week", "2026-W01", "--root", str(world)) == 0
    assert "nothing to pick" in capsys.readouterr().out
    assert not ledger.ledger_file(world).exists()


def test_a_short_is_written_from_its_shots(world, tmp_path, monkeypatch):
    edit_id = written_short(world, tmp_path, monkeypatch)
    folder = world / "edits" / edit_id
    edit = json.loads((folder / "edit.json").read_text())
    assert schemas.errors("edit", edit) == []
    assert edit["scenes"][0]["visual"]["src"] == "shots/sample/top.mp4"
    assert edit["source"]["stem"] == STEM
    ctx = json.loads((folder / "context.json").read_text())
    top = next(s for s in ctx["shots"] if s["id"] == "top")
    assert top["where"][0] == [0, [0.5, 0.4], None] and top["events"], "positions and events for the writer"
    sent = (world / "logs" / "llm_costs.csv").read_text()
    assert "write_short" in sent


def test_a_second_short_with_the_same_id_gets_its_own_folder(world, tmp_path, monkeypatch):
    written_short(world, tmp_path, monkeypatch)
    assert run(write_short, monkeypatch, "--shots", str(world / "shots/sample"), "--brief", "again", "--root", str(world)) == 0
    assert (world / "edits" / "one-sprint-2" / "edit.json").exists()


def test_translation_fills_every_text_and_is_remembered(world, tmp_path, monkeypatch):
    edit_id = written_short(world, tmp_path, monkeypatch)
    edit = json.loads((world / "edits" / edit_id / "edit.json").read_text())
    items = [f for f in translate.texts(edit) if "ru" not in f["text"]]
    answer = {"items": [{"id": f"t{n + 1}", "text": f"РУ {f['text']['en']}".replace("Aiko", "Айко")}
                        for n, f in enumerate(translate.texts(edit))]}
    for x, f in zip(answer["items"], translate.texts(edit)):
        if f["kind"].startswith("spoken"):
            x["text"] = "Коротко и по-русски."
    replay(tmp_path / "replay", {"translate": answer})
    assert run(translate, monkeypatch, edit_id, "--root", str(world)) == 0
    edit = json.loads((world / "edits" / edit_id / "edit.json").read_text())
    assert edit["languages"] == ["en", "ru"] and len(items) == 12
    assert all("ru" in f["text"] for f in translate.texts(edit))
    assert edits.versions(world / "edits" / edit_id), "the English-only version is kept"
    memory = json.loads((world / "edits" / edit_id / "translations.json").read_text())
    assert memory["ru"]["Follow for the next shift"].startswith("РУ")


def test_translation_checks_names_stars_and_spoken_length():
    names = {"game": "Kehai", "game_ru": "Кэхай", "aiko": "Aiko", "aiko_ru": "Айко"}
    items = [{"id": "t1", "en": "*Kehai* is out", "kind": "hook"},
             {"id": "t2", "en": "Aiko heard me.", "kind": "spoken by the narrator", "seconds": 1.0},
             {"id": "t3", "en": "Play Kehai", "kind": "spoken by the narrator", "seconds": 5.0}]
    found = translate.checks({"items": [{"id": "t1", "text": "Кэхай вышла"},
                                        {"id": "t2", "text": "Она услышала меня, когда я бежал через весь магазин."},
                                        {"id": "t3", "text": "Играй в Kehai"}]}, items, "ru", names)
    text = " | ".join(found)
    assert "stars" in text and "Latin letters" in text
    assert "write her name as Айко" in text and "say it shorter" in text and "Кэхай (it is spoken)" in text


def test_revise_applies_a_note_keeps_history_and_undo_puts_it_back(world, tmp_path, monkeypatch):
    edit_id = written_short(world, tmp_path, monkeypatch)
    revised = draft()
    revised["scenes"][0]["overlays"][0]["text"] = "She *heard* one sprint."
    revised["script"][0]["text"] = "One sprint. That's all."
    replay(tmp_path / "replay", {"revise": revised})
    assert run(revise, monkeypatch, edit_id, "--note", "make the hook about hearing", "--root", str(world)) == 0
    folder = world / "edits" / edit_id
    now = json.loads((folder / "edit.json").read_text())
    assert now["scenes"][0]["overlays"][0]["text"]["en"] == "She *heard* one sprint."
    assert "make the hook about hearing" in (edits.versions(folder)[-1] / "why.txt").read_text()
    assert run(revise, monkeypatch, edit_id, "--undo", "--root", str(world)) == 0
    back = json.loads((folder / "edit.json").read_text())
    assert back["scenes"][0]["overlays"][0]["text"]["en"] == "One sprint was *all* it took."
    assert edits.versions(folder) == []


def test_revise_must_keep_the_id(world, tmp_path, monkeypatch):
    edit_id = written_short(world, tmp_path, monkeypatch)
    other = draft()
    other["id"] = "something-else"
    replay(tmp_path / "replay", {"revise": other})
    from km.llm.client import Invalid
    with pytest.raises(Invalid, match="keep the id"):
        run(revise, monkeypatch, edit_id, "--note", "rename it", "--root", str(world))


def package_block(**changes):
    block = {"youtube": {"title": "She heard one sprint #Shorts", "description": "One sprint was enough.", "tags": ["indie horror", "gamedev"]},
             "instagram": {"caption": "One sprint.", "hashtags": ["#indiehorror", "#gamedev"]},
             "tiktok": {"caption": "Would you have run?", "hashtags": ["#horror"]},
             "x": "One sprint was enough. #indiedev", "bluesky": "One sprint was enough. #indiedev",
             "pinned_comment": "Where would you have hidden?", "thumbnail_text": ""}
    block.update(changes)
    return block


def test_package_writes_every_platform_in_every_language(world, tmp_path, monkeypatch):
    edit_id = written_short(world, tmp_path, monkeypatch)
    bad = {"en": package_block(x="Follow @kehaigame https://kehai.example " + "x" * 300)}
    replay(tmp_path / "replay", {"package.1": bad, "package.2": {"en": package_block()}})
    assert run(package, monkeypatch, edit_id, "--root", str(world)) == 0
    out = json.loads((world / "edits" / edit_id / "package.json").read_text())
    assert out["languages"]["en"]["youtube"]["title"] == "She heard one sprint #Shorts"


def test_package_checks_lengths_handles_links_and_her_name():
    brand = {"handles": {"youtube": None}, "links": {"website": None}}
    found = " | ".join(package.checks({"en": package_block(
        x="y" * 281, tiktok={"caption": "c", "hashtags": ["#a b", "#c"]},
        pinned_comment="Her name means something, you know: it means love.",
        youtube={"title": "t", "description": "Find us at @kehaigame or https://kehai.example", "tags": ["#x"]})},
        ["en"], "short", brand))
    for expect in ("x: 281 characters", "hashtags are '#' and one word", "never explain what her name means",
                   "@kehaigame isn't one of our handles", "https://kehai.example isn't one of our links", "no '#' in tags"):
        assert expect in found, expect
    assert package.checks({"en": package_block()}, ["en"], "short", brand) == []
    assert any("2 to 5 words" in p for p in package.checks({"en": package_block()}, ["en"], "long", brand))


def test_the_weekly_report_counts_and_writes(world, tmp_path, monkeypatch):
    ledger.record(world, {"time": "2026-09-30T10:00:00", "step": "write_short", "usd": "0.25"})
    replay(tmp_path / "replay", {"weekly_report": {"headline": "One shift, three picks.", "summary": "A quiet week.",
                                                   "highlights": ["The catch"], "problems": [], "next_week": ["Record a blink"]}})
    assert run(weekly_report, monkeypatch, "--week", WEEK, "--root", str(world)) == 0
    report = (world / "reports" / f"{WEEK}.md").read_text()
    assert "One shift, three picks." in report and "• Record a blink" in report
    numbers = json.loads((world / "reports" / f"{WEEK}.json").read_text())["numbers"]
    assert numbers["shifts"]["recorded"] == 1 and numbers["shifts"]["kept"] == 2
    assert numbers["claude"]["week_usd"] == pytest.approx(0.25)


def test_dry_runs_send_nothing(world, tmp_path, monkeypatch, capsys):
    monkeypatch.delenv("KEHAI_LLM_REPLAY")
    assert run(pick_moments, monkeypatch, "--week", WEEK, "--root", str(world), "--dry-run") == 0
    assert run(weekly_report, monkeypatch, "--week", WEEK, "--root", str(world), "--dry-run") == 0
    assert run(long_video, monkeypatch, "outline", "--month", "2026-09", "--root", str(world), "--dry-run") == 0
    out = capsys.readouterr().out
    assert out.count("dry run:") == 3
    assert not ledger.ledger_file(world).exists()
    assert len(list((world / "logs" / "llm_requests").glob("*.json"))) == 3


def test_the_long_video_outline_and_script(world, tmp_path, monkeypatch):
    outline = {"title_ideas": ["A", "B", "C"], "logline": "A month of her.", "cta": "Follow.",
               "sections": [{"name": "Cold open", "minutes": 4, "purpose": "Hook.", "beats": ["The catch"],
                             "shots": [{"name": "open-top", "moment": f"{STEM}#1", "camera": "topdown", "subject": "aiko",
                                        "layers": ["cone"], "alpha": False, "start_offset": 0, "end_offset": 0}]},
                            {"name": "How she hears", "minutes": 4, "purpose": "Explain.", "beats": ["Sound rings"],
                             "shots": [{"name": "rings", "moment": f"{STEM}#2", "camera": "topdown", "subject": "aiko",
                                        "layers": ["sound"], "alpha": False, "start_offset": 0, "end_offset": 0}]}]}
    words = " ".join(["word"] * 25)
    script = {"sections": [{"name": s["name"], "lines": [{"speaker": "narrator", "text": words + ".", "cue": "open-top"}] * 24}
                           for s in outline["sections"]]}
    replay(tmp_path / "replay", {"long_outline": outline, "long_script": script})
    assert run(long_video, monkeypatch, "outline", "--month", "2026-09", "--root", str(world)) == 0
    saved = json.loads((world / "long" / "2026-09" / "outline.json").read_text())
    assert [r["out"] for r in saved["renders"]] == ["shots/long-2026-09/open-top.mp4", "shots/long-2026-09/rings.mp4"]
    assert saved["renders"][0]["args"][saved["renders"][0]["args"].index("-size") + 1] == "1920x1080"
    assert run(long_video, monkeypatch, "script", "--month", "2026-09", "--root", str(world)) == 0
    assert (world / "long" / "2026-09" / "script.json").exists()
    problems = long_video.script_checks({"sections": [{"name": "Cold open", "lines": []}]}, outline)
    assert any("the outline's, in order" in p for p in problems)


@pytest.mark.skipif(shutil.which("say") is None, reason="macOS `say` only")
def test_voice_speaks_both_languages_into_the_edit(world, tmp_path, monkeypatch):
    import voice
    edit_id = written_short(world, tmp_path, monkeypatch)
    assert run(voice, monkeypatch, edit_id, "--backend", "say", "--lang", "en", "--root", str(world)) == 0
    edit = json.loads((world / "edits" / edit_id / "edit.json").read_text())
    assert len(edit["voice"]["en"]) == 5 and schemas.errors("edit", edit) == []
    from km.cli import BadInput
    with pytest.raises(BadInput, match="isn't in ru"):
        run(voice, monkeypatch, edit_id, "--backend", "say", "--lang", "ru", "--root", str(world))


@pytest.mark.skipif(shutil.which("say") is None, reason="macOS `say` only")
def test_the_long_video_is_voiced_then_cut_to_its_voice(world, tmp_path, monkeypatch):
    from km import drafts
    from helpers import ctx as make_ctx, ov, vis, NO_PIP
    monkeypatch.setitem(drafts.LIMITS, "long", (5.0, 1200.0))      # a tiny "long" video, to keep the test quick
    month = world / "long" / "2026-09"
    lines = [{"speaker": "narrator", "text": "This month she learned to listen.", "cue": "open-top"},
             {"speaker": "aiko", "text": "Your footsteps are noted.", "cue": "open-top"}]
    (month).mkdir(parents=True)
    (month / "outline.json").write_text(json.dumps({"answer": {"logline": "She listens.", "cta": "Follow.", "sections": [
        {"name": "Cold open", "minutes": 1, "purpose": "Hook.", "beats": [], "shots": []}]}}))
    (month / "script.json").write_text(json.dumps({"answer": {"sections": [{"name": "Cold open", "lines": lines}]}}))
    assert run(long_video, monkeypatch, "voice", "--month", "2026-09", "--backend", "say", "--root", str(world)) == 0
    voiced = json.loads((month / "voiced.json").read_text())["lines"]
    assert [v["text"] for v in voiced] == [l["text"] for l in lines] and all(v["seconds"] > 0.5 for v in voiced)

    shot(world, "shots/long-2026-09", "open-top", seconds=20.0, with_track=track(20.0))
    first, second = voiced[0]["seconds"], voiced[1]["seconds"]
    cut = {"id": "long-2026-09", "title": "She listens", "hook": "", "captions": "words",
           "scenes": [{"name": "open", "duration": round(first + second + 1.5, 2), "transition": "cut",
                       "visual": vis(source="open-top"), "pip": NO_PIP,
                       "overlays": [ov("lower-third", text="Cold open")], "sfx": []}],
           "script": [{"speaker": "narrator", "at": 0.2, "text": lines[0]["text"]},
                      {"speaker": "aiko", "at": round(0.2 + first + 0.3, 2), "text": lines[1]["text"]}],
           "music": {"asset": "", "offset": 0, "volume": 0.3}, "end_card": {"duration": 3, "cta": "Follow."}}
    wrong = json.loads(json.dumps(cut))
    wrong["script"][1]["text"] = "Your steps are noted."             # not what was recorded
    replay(tmp_path / "replay", {"long_edit.1": wrong, "long_edit.2": cut})
    assert run(long_video, monkeypatch, "edit", "--month", "2026-09", "--root", str(world)) == 0
    edit = json.loads((world / "edits" / "long-2026-09" / "edit.json").read_text())
    assert edit["kind"] == "long" and edit["format"] == "16:9" and schemas.errors("edit", edit) == []
    import voice
    assert run(voice, monkeypatch, "long-2026-09", "--backend", "say", "--root", str(world), "--dry-run") == 0
    from km import voicing
    p = voicing.plan(world, edit, "en", json.loads((schemas.paths.BRAND).read_text()), "say")
    assert p.to_speak == [], "the edit's lines are the ones already voiced"


def test_a_short_follows_its_pattern_and_a_note_can_name_another(world, tmp_path, monkeypatch):
    patterns.save(world, STUDY, "ref-1", "steal the snap", {})
    patterns.save(world, {**STUDY, "name": "slow-reveal", "title": "Slow reveal"}, "ref-2", "", {})
    for name, alpha in (("top", False), ("pov", False), ("cctv", False), ("mind", True)):
        shot(world, "shots/sample", name, seconds=20.13, alpha=alpha, with_track=track(20.13) if name == "top" else None)
    replay(tmp_path / "replay", {"write_short": draft(), "revise": draft()})
    asked = []
    real = client.ask
    monkeypatch.setattr(client, "ask", lambda request, *a, **k: (asked.append(request), real(request, *a, **k))[1])
    assert run(write_short, monkeypatch, "--shots", str(world / "shots/sample"), "--brief", "One sprint was enough",
               "--moment", f"{STEM}#1", "--pattern", "dark-then-light", "--root", str(world)) == 0
    assert '"name":"dark-then-light"' in asked[0].user and '"owner_note":"steal the snap"' in asked[0].user
    folder = world / "edits" / "one-sprint"
    assert json.loads((folder / "context.json").read_text())["pattern"] == "dark-then-light"
    assert "Pattern: Dark, then light" in json.loads((folder / "edit.json").read_text())["source"]["notes"]
    assert patterns.load(world, "dark-then-light")["used_by"] == ["one-sprint"]

    assert run(revise, monkeypatch, "one-sprint", "--note", "make it a Slow reveal", "--root", str(world)) == 0
    assert '"name":"slow-reveal"' in asked[1].user and '"name":"dark-then-light"' in asked[1].user
    assert json.loads((folder / "context.json").read_text())["pattern"] == "slow-reveal"
    assert patterns.load(world, "slow-reveal")["used_by"] == ["one-sprint"]


def test_pick_moments_gives_a_must_use_pattern_to_one_short(world, tmp_path, monkeypatch, capsys):
    patterns.save(world, STUDY, "ref-1", "", {})
    picks = json.loads(json.dumps(PICKS))
    picks["shorts"][0]["pattern"] = "dark-then-light"
    picks["pattern_fit"] = [{"pattern": "dark-then-light", "fits": True, "play": ""}]
    replay(tmp_path / "replay", {"pick_moments": picks})
    asked = []
    real = client.ask
    monkeypatch.setattr(client, "ask", lambda request, *a, **k: (asked.append(request), real(request, *a, **k))[1])
    assert run(pick_moments, monkeypatch, "--week", WEEK, "--pattern", "dark-then-light", "--root", str(world)) == 0
    assert '"must_use":["dark-then-light"]' in asked[0].user and "<patterns>" in asked[0].user
    plan = json.loads((world / "plans" / WEEK / "picks.json").read_text())
    assert plan["answer"]["shorts"][0]["pattern"] == "dark-then-light"
    assert "pattern dark-then-light" in capsys.readouterr().out
    from km.cli import BadInput
    with pytest.raises(BadInput, match="no active pattern 'nope'"):
        run(pick_moments, monkeypatch, "--week", WEEK, "--pattern", "nope", "--root", str(world))
