"""What a reference video says: Azure speech-to-text on its audio, phrase by phrase with times.

The same key as the voices (AZURE_SPEECH_KEY, AZURE_SPEECH_REGION). The free tier (F0) gives five
audio hours a month (KEHAI_AZURE_STT_MONTHLY_SECONDS, 18000); the seconds are logged with the
voices' characters in logs/tts_usage.csv (backend `azure-stt`). The language is detected among
English and Russian. A transcript is a help, not a must: without the key, past the quota, or on an
error, `transcribe` says why and the study goes on without one.
"""
from __future__ import annotations

import datetime as dt
import threading
from pathlib import Path

from . import env
from .tts import usage
from .tts.wav import duration

TICKS = 10_000_000
BACKEND = "azure-stt"
LANGUAGES = ["en-US", "ru-RU"]


def month_seconds(root: Path) -> float:
    path = usage.usage_file(root)
    if not path.exists():
        return 0.0
    import csv
    start = dt.datetime.now().replace(day=1, hour=0, minute=0, second=0, microsecond=0)
    end = (start + dt.timedelta(days=32)).replace(day=1)
    total = 0.0
    with path.open(newline="", encoding="utf-8") as f:
        for r in csv.DictReader(f):
            try:
                if r["backend"] == BACKEND and start <= dt.datetime.fromisoformat(r["time"]) < end:
                    total += float(r["seconds"] or 0)
            except (KeyError, ValueError):
                continue
    return total


def phrases(results: list[dict]) -> list[dict]:
    """Recognised phrases as [{from, to, text}], in order, empty ones dropped."""
    out = []
    for r in sorted(results, key=lambda r: r["offset"]):
        text = (r.get("text") or "").strip()
        if text:
            start = r["offset"] / TICKS
            out.append({"from": round(start, 1), "to": round(start + r["duration"] / TICKS, 1), "text": text})
    return out


def _recognise(wav: Path, key: str, region: str) -> tuple[list[dict], str]:
    import azure.cognitiveservices.speech as sdk

    config = sdk.SpeechConfig(subscription=key, region=region)
    auto = sdk.languageconfig.AutoDetectSourceLanguageConfig(languages=LANGUAGES)
    recogniser = sdk.SpeechRecognizer(speech_config=config, auto_detect_source_language_config=auto,
                                      audio_config=sdk.audio.AudioConfig(filename=str(wav)))
    results: list[dict] = []
    language = {"value": ""}
    failure = {"value": ""}
    done = threading.Event()

    def recognised(evt):
        r = evt.result
        if r.reason == sdk.ResultReason.RecognizedSpeech:
            results.append({"offset": r.offset, "duration": r.duration, "text": r.text})
            if not language["value"]:
                language["value"] = sdk.AutoDetectSourceLanguageResult(r).language or ""

    def canceled(evt):
        d = evt.cancellation_details
        if d.reason == sdk.CancellationReason.Error:
            code = getattr(d, "code", None) or getattr(d, "error_code", None)
            failure["value"] = ("Azure refused the key" if code == sdk.CancellationErrorCode.AuthenticationFailure
                                else f"{code}: {d.error_details}")
        done.set()

    recogniser.recognized.connect(recognised)
    recogniser.canceled.connect(canceled)
    recogniser.session_stopped.connect(lambda evt: done.set())
    recogniser.start_continuous_recognition()
    done.wait(timeout=600)
    recogniser.stop_continuous_recognition()
    if failure["value"]:
        raise RuntimeError(failure["value"])
    return results, language["value"]


def transcribe(root: Path, wav: Path, ref: str, recognise=_recognise) -> dict:
    """{"language", "phrases"} or {"none": why}."""
    key, region = env.get("AZURE_SPEECH_KEY"), env.get("AZURE_SPEECH_REGION")
    if not key or not region:
        return {"none": "no transcript: Azure Speech isn't set up (AZURE_SPEECH_KEY)"}
    seconds = duration(wav)
    quota = float(env.get("KEHAI_AZURE_STT_MONTHLY_SECONDS", "18000") or 18000)
    used = month_seconds(root)
    if used + seconds > quota:
        return {"none": f"no transcript: {used / 60:.0f} of {quota / 60:.0f} speech-to-text minutes used this month"}
    try:
        results, language = recognise(wav, key, region)
    except Exception as e:                  # the study goes on without it
        return {"none": f"no transcript: {e}"}
    usage.record(root, {"time": dt.datetime.now().isoformat(timespec="seconds"), "backend": BACKEND, "voice": "",
                        "lang": language, "chars": 0, "seconds": round(seconds, 1), "ref": ref})
    return {"language": language, "phrases": phrases(results)}
