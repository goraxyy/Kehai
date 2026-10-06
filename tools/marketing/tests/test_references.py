"""Phase 8: reference videos the owner sends become patterns, and a week's short follows one.

The video is a real one made here (dark for 2 s, then bright, with a tone), read with Remotion's
ffmpeg; Claude's study is replayed from a file; Telegram is the outbox."""
import datetime as dt
import json
import math
import struct
import subprocess
import wave
import zlib

import pytest

import pick_moments
from km import media, patterns, production, references, watch
from km.approvals import Approvals
from km.housekeeping import Keeper
from km.llm import client
from km.llm.steps import step
from km.store import Store
from km.telegram import Bot

from helpers import STUDY



def png(path, w, h, value):
    rows = b"".join(b"\x00" + bytes([value]) * w for _ in range(h))

    def chunk(kind, data):
        return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data) & 0xFFFFFFFF)

    path.write_bytes(b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 0, 0, 0, 0))
                     + chunk(b"IDAT", zlib.compress(rows)) + chunk(b"IEND", b""))


@pytest.fixture(scope="module")
def film(tmp_path_factory):
    """A 4-second video: dark, a hard cut at 2 s, bright; a 440 Hz tone."""
    try:
        exe = media.ffmpeg()
    except media.NotSetUp:
        pytest.skip("no ffmpeg (npm install in tools/marketing/editor)")
    d = tmp_path_factory.mktemp("film")
    for i in range(40):
        png(d / f"f{i:03d}.png", 64, 112, 30 if i < 20 else 220)
    out = d / "dark-light.mp4"
    subprocess.run([str(exe), "-hide_banner", "-loglevel", "error", "-y", "-framerate", "10", "-i", str(d / "f%03d.png"),
                    "-f", "lavfi", "-i", "sine=frequency=440:duration=4", "-c:v", "libx264", "-pix_fmt", "yuv420p",
                    "-c:a", "aac", "-shortest", str(out)], cwd=exe.parent, check=True)
    return out


@pytest.fixture
def world(tmp_path, monkeypatch):
    for k in ("TELEGRAM_BOT_TOKEN", "TELEGRAM_CHAT_ID", "ANTHROPIC_API_KEY"):
        monkeypatch.delenv(k, raising=False)
    for k in ("AZURE_SPEECH_KEY", "AZURE_SPEECH_REGION"):     # set empty: .env can't switch the transcript on
        monkeypatch.setenv(k, "")
    monkeypatch.setattr("km.env.ENV_FILE", tmp_path / "no.env")
    monkeypatch.setattr("km.env._loaded", False)
    replay = tmp_path / "replay"
    replay.mkdir()
    (replay / "study_reference.json").write_text(json.dumps(STUDY), encoding="utf-8")
    monkeypatch.setenv("KEHAI_LLM_REPLAY", str(replay))
    root = tmp_path / "marketing"
    store = Store(root)
    bot = Bot(root, store)
    yield root, store, bot, Approvals(root, store, bot)
    store.close()


def studied(root, store, bot, approvals, film, caption=""):
    """A reference sent in Telegram and studied: its id."""
    approvals.handle(bot.fake_video(film, caption))
    rid = store.refs()[-1]["id"]
    code, text = references.work(root, store, bot, store.take_job())
    assert code == 0, text
    return rid


# ---- reading a video -------------------------------------------------------------------------

def test_watch_finds_the_cut_takes_the_frames_and_measures_the_sound(film, tmp_path):
    seen = watch.read(film, tmp_path / "ref")
    assert seen["seconds"] == pytest.approx(4.0, abs=0.15)
    assert seen["cuts"] == [pytest.approx(2.0, abs=0.15)]
    assert seen["shots"] == 2
    assert seen["frames"][0]["t"] == 0 and seen["frames"][0]["width"] == watch.FRAME_WIDTH
    assert all((tmp_path / "ref" / f["file"]).is_file() for f in seen["frames"])
    assert any(abs(f["t"] - 2.2) < 0.01 for f in seen["frames"]), "the new shot's first moment"
    assert seen["audio"] and max(seen["loudness"]["dbfs"]) > -30
    assert watch.read(film, tmp_path / "ref") == seen, "read once, then reused"


def test_frames_are_close_through_the_hook_and_capped():
    times = watch.frame_times(100, [10.0, 20.0, 30.0], most=30)
    assert times[:7] == [0, 0.5, 1.0, 1.5, 2.0, 2.5, 3.0]
    assert len(times) <= 30 and times == sorted(times)
    assert 4.2 in watch.frame_times(10, [4.0])


def test_cuts_are_sudden_changes_not_motion():
    steady = [bytes([(i % 5) * 3]) * 1600 for i in range(30)]
    assert watch.cuts_in(steady) == []
    snap = [bytes([20]) * 1600] * 10 + [bytes([200]) * 1600] * 10
    assert watch.cuts_in(snap) == [1.0]


def test_loudness_marks_where_the_sound_comes_in(tmp_path):
    path = tmp_path / "a.wav"
    with wave.open(str(path), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(16000)
        loud = [int(12000 * math.sin(2 * math.pi * 440 * i / 16000)) for i in range(16000)]
        w.writeframes(struct.pack(f"<{16000 + 16000}h", *([0] * 16000 + loud)))
    sound = watch.loudness(path)
    assert sound["dbfs"][0] == -90.0
    assert sound["jumps"] == [1.0]


def test_frames_go_to_claude_as_pictures_before_the_text(tmp_path):
    img = tmp_path / "f.jpg"
    img.write_bytes(b"\xff\xd8not really a jpeg")
    request = client.Request(step=step("study_reference"), system=[{"type": "text", "text": "s"}], user="Study it.",
                             schema={}, images=[{"label": "frame at 0.0 s", "path": str(img), "width": 384, "height": 682}])
    content = request.params(False)["messages"][0]["content"]
    assert [b["type"] for b in content] == ["text", "image", "text"]
    assert content[1]["source"]["media_type"] == "image/jpeg" and content[2]["text"] == "Study it."
    assert request.params(False, inline=False)["messages"][0]["content"][1]["source"]["data"] == f"<{img}>"
    assert request.estimate()[0] >= client.image_tokens(384, 682)


# ---- studying -------------------------------------------------------------------------------

def test_a_video_file_is_studied_into_a_pattern_and_again_with_a_correction(world, film):
    root = world[0]
    r = production.script(root, "study_reference.py", str(film), "--note", "the snap")
    assert r.ok, r.output
    p = patterns.load(root, "dark-then-light")
    assert p["status"] == "active" and p["note"] == "the snap" and p["measured"]["shots"] == 2
    rid = p["reference"]
    here = references.folder(root, rid)
    assert (here / "study.json").exists() and (here / "frames").is_dir()
    assert json.loads((here / "meta.json").read_text())["pattern"] == "dark-then-light"

    r = production.script(root, "study_reference.py", rid, "--note", "it's about the timing")
    assert r.ok, r.output
    assert sorted(f.name for f in patterns.folder(root).glob("*.json")) == ["dark-then-light.json"], "same name again"
    assert json.loads((here / "meta.json").read_text())["corrections"] == ["it's about the timing"]


def test_a_study_that_misreads_the_video_is_sent_back():
    wrong = json.loads(json.dumps(STUDY))
    wrong["beats"][-1]["to"] = 9.0
    wrong["name"] = "Dark Then Light"
    import study_reference
    problems = study_reference.checks(wrong, 4.0)
    assert any("name" in p for p in problems) and any("the video runs 4.0s" in p for p in problems)
    assert study_reference.checks(STUDY, 4.0) == []


# ---- Telegram -------------------------------------------------------------------------------

def test_a_video_sent_in_telegram_comes_back_as_a_pattern_with_buttons(world, film):
    root, store, bot, approvals = world
    assert approvals.handle(bot.fake_video(film, "steal the snap")).endswith("received")
    ref = store.refs()[0]
    assert ref["status"] == "studying" and ref["note"] == "steal the snap"
    assert (references.folder(root, ref["id"]) / "source.mp4").is_file()
    assert "Got it" in bot.sent()[-1]["params"]["text"]
    job = store.take_job()
    assert job["kind"] == "reference" and job["video"] == ref["id"]
    code, text = references.work(root, store, bot, job)
    assert code == 0, text
    ref = store.ref(ref["id"])
    assert ref["status"] == "ready" and ref["pattern"] == "dark-then-light"
    shown = bot.sent()[-1]["params"]
    assert "Dark, then light" in shown["text"] and "The lights come back" in shown["text"]
    data = [b["callback_data"] for row in shown["reply_markup"]["inline_keyboard"] for b in row]
    assert data == [f"rf:{ref['id']}:e", f"rf:{ref['id']}:x"]


def test_too_big_a_video_is_turned_away(world):
    root, store, bot, approvals = world
    chat = {"id": "owner"}
    approvals.handle({"update_id": 1, "message": {"message_id": 5, "chat": chat, "video": {
        "file_id": "x", "duration": 60, "mime_type": "video/mp4", "file_size": 31_000_000}}})
    assert store.refs() == [] and "20 MB" in bot.sent()[-1]["params"]["text"]


def test_a_correction_studies_it_again_and_x_drops_it(world, film):
    root, store, bot, approvals = world
    rid = studied(root, store, bot, approvals, film)
    shown = store.ref(rid)["message_id"]
    approvals.handle(bot.fake_tap(f"rf:{rid}:e", shown))
    asked = bot.sent()[-1]
    assert asked["params"]["reply_markup"]["force_reply"]
    approvals.handle(bot.fake_text("it's the timing, not the colour", reply_to=asked["message_id"]))
    assert store.ref(rid)["status"] == "studying"
    job = store.take_job()
    assert job["kind"] == "reference" and job["payload"]["note"] == "it's the timing, not the colour"
    assert references.work(root, store, bot, job)[0] == 0
    approvals.handle(bot.fake_tap(f"rf:{rid}:x", store.ref(rid)["message_id"]))
    assert store.ref(rid)["status"] == "dropped"
    assert patterns.load(root, "dark-then-light")["status"] == "dropped"
    assert references.due(root, store) == []


def test_patterns_command_lists_the_library(world):
    root, store, bot, approvals = world
    approvals.handle(bot.fake_text("/patterns"))
    assert "No patterns yet" in bot.sent()[-1]["params"]["text"]
    patterns.save(root, STUDY, "ref-1", "", {})
    approvals.handle(bot.fake_text("/patterns"))
    assert "Dark, then light" in bot.sent()[-1]["params"]["text"]


# ---- the week's picks -----------------------------------------------------------------------

def ready_ref(root, store, rid, name):
    study = {**STUDY, "name": name, "title": name.replace("-", " ")}
    patterns.save(root, study, rid, "", {})
    store.add_ref(rid)
    store.update_ref(rid, status="ready", pattern=name)


def test_a_week_gives_its_reference_a_short_or_says_what_to_play(world):
    root, store, bot, _ = world
    ready_ref(root, store, "ref-1", "snap-cut")
    ready_ref(root, store, "ref-2", "slow-reveal")
    assert [r["id"] for r in references.due(root, store)] == ["ref-1"], "one a week, the oldest first"
    plan = {"answer": {"shorts": [{"name": "lights", "pattern": "snap-cut"}, {"name": "other", "pattern": ""}],
                       "pattern_fit": [{"pattern": "snap-cut", "fits": True, "play": ""}]}}
    references.after_pick(root, store, bot, plan, "2026-W41", references.due(root, store))
    assert store.ref("ref-1")["status"] == "used" and store.ref("ref-1")["pick"] == "lights"
    plan = {"answer": {"shorts": [{"name": "x", "pattern": ""}],
                       "pattern_fit": [{"pattern": "slow-reveal", "fits": False, "play": "she walks out of the dark"}]}}
    references.after_pick(root, store, bot, plan, "2026-W42", references.due(root, store))
    assert store.ref("ref-2")["status"] == "ready"
    assert "play a shift where she walks out of the dark" in bot.sent()[-1]["params"]["text"]


def test_picking_holds_the_week_to_its_patterns():
    empty = {"shorts": [], "pattern_fit": [], "kept": []}
    assert any("pattern_fit" in p for p in pick_moments.checks(empty, {}, [], 0, {"p"}, ["p"]))
    unmade = {**empty, "pattern_fit": [{"pattern": "p", "fits": True, "play": ""}]}
    assert any("exactly one short follows it" in p for p in pick_moments.checks(unmade, {}, [], 0, {"p"}, ["p"]))
    silent = {**empty, "pattern_fit": [{"pattern": "p", "fits": False, "play": ""}]}
    assert any("`play`" in p for p in pick_moments.checks(silent, {}, [], 0, {"p"}, ["p"]))
    told = {**empty, "pattern_fit": [{"pattern": "p", "fits": False, "play": "a blackout"}]}
    assert pick_moments.checks(told, {}, [], 0, {"p"}, ["p"]) == []
    stranger = {**empty, "shorts": [{"name": "a-b-c", "moment": "x#1", "pattern": "q", "shots": []}]}
    assert any("no pattern 'q'" in p for p in pick_moments.checks(stranger, {}, [], 1, {"p"}, []))


def test_a_note_can_name_a_pattern(world):
    root = world[0]
    patterns.save(root, STUDY, "ref-1", "", {})
    assert [p["name"] for p in patterns.named_in(root, "make it like Dark, then light")] == ["dark-then-light"]
    assert [p["name"] for p in patterns.named_in(root, "use dark-then-light")] == ["dark-then-light"]
    assert patterns.named_in(root, "punchier") == []


# ---- housekeeping ---------------------------------------------------------------------------

def test_reference_videos_go_after_a_month_and_their_patterns_stay(world, film):
    root, store, bot, approvals = world
    rid = studied(root, store, bot, approvals, film)
    here = references.folder(root, rid)
    old = (dt.datetime.now() - dt.timedelta(days=40)).isoformat(timespec="seconds")
    store.db.execute("UPDATE refs SET created = ? WHERE id = ?", (old, rid))
    keeper = Keeper(root, store, apply=True)
    keeper.retention()
    assert not (here / "source.mp4").exists() and not (here / "frames").exists() and not (here / "audio.wav").exists()
    assert (here / "meta.json").exists() and (here / "study.json").exists()
    assert patterns.load(root, "dark-then-light")["status"] == "active"
    approvals.handle(bot.fake_tap(f"rf:{rid}:e", store.ref(rid)["message_id"]))
    approvals.handle(bot.fake_text("more", reply_to=bot.sent()[-1]["message_id"]))
    assert "send it again" in bot.sent()[-1]["params"]["text"]
