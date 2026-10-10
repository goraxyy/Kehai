"""The voice: SSML for Azure, ElevenLabs' alignment into words, the `say` stand-in and its word
estimates, speaking an edit's script (cached, within the quota, behind the lock), and fitting the
lines so they don't overlap."""
import json
import os
import shutil
import subprocess
import sys

import pytest

from km import drafts, lock, voicing
from km.tts import attach_punctuation, speakable, usage, wav
from km.tts.azure import ssml
from km.tts.elevenlabs import words_from_alignment
from km.tts.say import estimate_words

from helpers import ctx, draft

HAS_SAY = sys.platform == "darwin" and shutil.which("say") is not None
BRAND = {"voices": {"backend": "say",
                    "en": {"narrator": {"azure": "en-US-AndrewNeural", "say": "Samantha"},
                           "karen": {"azure": "en-US-AvaNeural", "rate": "-6%", "pitch": "-3%", "say": "Samantha"}},
                    "ru": {"narrator": {"azure": "ru-RU-DmitryNeural", "say": "Milena"},
                           "karen": {"azure": "ru-RU-SvetlanaNeural", "say": "Milena"}}}}


def test_ssml_escapes_and_shapes_the_voice():
    doc = ssml("Tom & *Jerry* <3", "en", {"azure": "en-US-AvaNeural", "rate": "-6%", "pitch": "-3%", "style": "whispering"})
    assert 'xml:lang="en-US"' in doc and '<voice name="en-US-AvaNeural">' in doc
    assert "Tom &amp; Jerry &lt;3" in doc, "escaped, and no stars spoken"
    assert '<prosody rate="-6%" pitch="-3%">' in doc
    assert '<mstts:express-as style="whispering">' in doc
    assert "express-as" not in ssml("hi", "ru", {"azure": "ru-RU-DmitryNeural"})


def test_punctuation_folds_into_the_word_before():
    words = attach_punctuation([{"text": "there", "start": 0, "end": 0.4}, {"text": ".", "start": 0.4, "end": 0.45},
                                {"text": "Now", "start": 0.7, "end": 0.9}])
    assert [w["text"] for w in words] == ["there.", "Now"] and words[0]["end"] == 0.45


def test_elevenlabs_characters_become_words():
    text = "She heard it."
    starts = [i * 0.1 for i in range(len(text))]
    words = words_from_alignment({"characters": list(text), "character_start_times_seconds": starts,
                                  "character_end_times_seconds": [s + 0.1 for s in starts]})
    assert [w["text"] for w in words] == ["She", "heard", "it."]
    assert words[1]["start"] == pytest.approx(0.4) and words[1]["end"] == pytest.approx(0.9)


def test_estimated_words_break_phrases_at_the_silences():
    spans = [(0.1, 1.0), (1.3, 2.0), (2.05, 2.6)]   # the widest gap (1.0–1.3) is the sentence break
    words = estimate_words("I sprinted once. She heard it.", spans, 2.8)
    assert [w["text"] for w in words] == ["I", "sprinted", "once.", "She", "heard", "it."]
    assert words[2]["end"] == pytest.approx(1.0) and words[3]["start"] == pytest.approx(1.3)
    assert words[-1]["end"] == pytest.approx(2.6)
    assert speakable("  *Two*   words ") == "Two words"


def test_fitting_moves_lines_later_and_reports_an_overrun():
    clips = [{"src": "a/n1.wav", "at": 0.0, "duration": 3.0}, {"src": "a/n2.wav", "at": 2.5, "duration": 2.0},
             {"src": "a/n3.wav", "at": 9.0, "duration": 2.0}]
    notes = voicing.fit(clips, end=10.5)
    assert clips[1]["at"] == pytest.approx(3.12) and clips[2]["at"] == 9.0
    assert any("n2 moved" in n for n in notes) and any("past the last scene" in n for n in notes)
    assert voicing.overruns(notes)


def test_quota_stops_a_voice_job_before_it_starts(tmp_path, monkeypatch):
    monkeypatch.setenv("KEHAI_AZURE_MONTHLY_CHARS", "100")
    usage.record(tmp_path, {"time": "2099-01-01T00:00:00", "backend": "azure", "chars": 10})
    import datetime as dt
    usage.record(tmp_path, {"time": dt.datetime.now().isoformat(timespec="seconds"), "backend": "azure", "chars": 90})
    usage.check(tmp_path, "say", 10_000)                       # free and unlimited
    usage.check(tmp_path, "azure", 10)
    with pytest.raises(usage.OverQuota):
        usage.check(tmp_path, "azure", 11)


def test_the_heavy_lock_is_one_job_at_a_time(tmp_path):
    with lock.heavy(tmp_path, "voice test"):
        assert (tmp_path / "state" / "heavy.lock" / "job").read_text() == "voice test"
        with pytest.raises(lock.Busy, match="voice test"):
            with lock.heavy(tmp_path, "another", wait=0):
                pass
    assert not (tmp_path / "state" / "heavy.lock").exists()


def test_a_lock_left_by_a_dead_job_is_taken_over(tmp_path):
    dead = subprocess.Popen([sys.executable, "-c", "pass"])
    dead.wait()
    held = tmp_path / "state" / "heavy.lock"
    held.mkdir(parents=True)
    (held / "pid").write_text(str(dead.pid))
    with lock.heavy(tmp_path, "mine", wait=0):
        assert (held / "pid").read_text() == str(os.getpid())


@pytest.mark.skipif(not HAS_SAY, reason="macOS `say` only")
def test_an_edits_script_is_spoken_cached_and_placed(tmp_path):
    edit, _ = drafts.to_edit(draft(), ctx())
    edit["script"] = edit["script"][:2]
    p = voicing.plan(tmp_path, edit, "en", BRAND, "say")
    assert len(p.to_speak) == 2 and p.chars == sum(len(l["text"]["en"]) for l in edit["script"])
    voicing.speak(tmp_path, edit, p)
    notes = voicing.apply(tmp_path, edit, p)
    clips = edit["voice"]["en"]
    assert [c["at"] for c in clips] == [0.2, 3.6] and notes == []
    assert all(wav.duration(tmp_path / c["src"]) == pytest.approx(c["duration"], abs=1e-3) for c in clips)
    assert [w["text"] for w in clips[0]["words"]] == ["I", "sprinted", "once.", "Just", "once."]
    assert usage.month_chars(tmp_path, "say") == p.chars
    again = voicing.plan(tmp_path, edit, "en", BRAND, "say")
    assert again.to_speak == [], "spoken lines are reused"
    other_voice = json.loads(json.dumps(BRAND))
    other_voice["voices"]["en"]["narrator"]["say"] = "Fred"
    assert len(voicing.plan(tmp_path, edit, "en", other_voice, "say").to_speak) == 2, "a new voice speaks again"
    ru = voicing.plan(tmp_path, edit, "ru", BRAND, "say")
    assert len(ru.missing) == 2, "no Russian text yet"
