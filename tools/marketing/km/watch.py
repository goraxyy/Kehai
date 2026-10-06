"""Reading a video the way Claude can: frames as pictures, and numbers for what pictures miss.

Claude takes images, not video, so a reference is turned into
- **frames:** twice a second through the hook (the first 3 s), then about once a second, plus the
  first moment of every shot, at most `MAX_FRAMES`, 384 px wide;
- **cuts:** when the picture changes all at once (tiny grey frames ten times a second, compared
  one to the next), which gives the pacing;
- **loudness:** the sound's level every half second, with the moments it jumps or drops out (a
  beat, a drop, a pause): Claude can't hear the audio;
- **audio** as 16 kHz mono WAV for the transcript (km/stt.py).

Everything goes through the ffmpeg and ffprobe Remotion ships (run from their own folder): it has
no scene or loudness filters, so the cuts and the loudness are worked out here.
"""
from __future__ import annotations

import array
import json
import math
import subprocess
import wave
from pathlib import Path

from . import media

MAX_SECONDS = 180           # longer videos: only the start is studied
MAX_FRAMES = 48
FRAME_WIDTH = 384
GREY = (40, 40)             # the cut detector's frames
CUT_FPS = 10


class VideoError(Exception):
    """The file isn't a video ffmpeg can read."""


def _tool(exe: Path, args: list[str], timeout: int = 600, binary: bool = False) -> subprocess.CompletedProcess:
    r = subprocess.run([str(exe), *args], cwd=exe.parent, capture_output=True, text=not binary, timeout=timeout)
    if r.returncode != 0:
        err = r.stderr if not binary else r.stderr.decode("utf-8", "replace")
        raise VideoError(f"{exe.name} {args[0] if args else ''}: {err.strip()[-300:]}")
    return r


def probe(path: Path) -> dict:
    """Length, picture size and whether there's sound."""
    r = _tool(media.ffprobe(), ["-v", "error", "-show_entries", "format=duration:stream=codec_type,width,height",
                                "-of", "json", str(path.resolve())])
    info = json.loads(r.stdout or "{}")
    streams = info.get("streams", [])
    picture = next((s for s in streams if s.get("codec_type") == "video"), None)
    if not picture:
        raise VideoError(f"{path.name} has no picture")
    try:
        seconds = float(info["format"]["duration"])
    except (KeyError, ValueError) as e:
        raise VideoError(f"{path.name}: no duration") from e
    return {"seconds": round(seconds, 2), "width": picture["width"], "height": picture["height"],
            "audio": any(s.get("codec_type") == "audio" for s in streams)}


def grey_frames(path: Path, seconds: float) -> list[bytes]:
    w, h = GREY
    r = _tool(media.ffmpeg(), ["-hide_banner", "-loglevel", "error", "-t", str(seconds), "-i", str(path.resolve()), "-an",
                               "-vf", f"scale={w}:{h},format=gray", "-r", str(CUT_FPS), "-c:v", "rawvideo",
                               "-f", "image2pipe", "-"], binary=True)
    size = w * h
    data = r.stdout
    return [data[i:i + size] for i in range(0, len(data) - size + 1, size)]


def cuts_in(frames: list[bytes], fps: float = CUT_FPS) -> list[float]:
    """Times (s) where the picture changes at once: a frame unlike the one before, far more than
    the motion around it."""
    if len(frames) < 3:
        return []
    diffs = [0.0] + [sum(abs(a - b) for a, b in zip(frames[i], frames[i - 1])) / len(frames[i])
                     for i in range(1, len(frames))]
    ordered = sorted(diffs[1:])
    typical = ordered[len(ordered) // 2]
    threshold = max(22.0, 3.5 * typical)
    out: list[float] = []
    for i in range(1, len(diffs)):
        d = diffs[i]
        if d < threshold or d < diffs[i - 1] or (i + 1 < len(diffs) and d < diffs[i + 1]):
            continue
        t = round(i / fps, 1)
        if out and t - out[-1] < 0.3:
            continue
        out.append(t)
    return out


def frame_times(seconds: float, cuts: list[float], most: int = MAX_FRAMES) -> list[float]:
    """When to take a frame: the hook closely and each shot's first moment, then steadily between."""
    def within(times):
        return sorted({round(t, 2) for t in times if 0 <= t < seconds - 0.05})

    hook = within(t / 2 for t in range(0, 7))
    starts = within(c + 0.2 for c in cuts)
    step = 1.0 if seconds <= 60 else 2.0
    steady = within(3.0 + step * (i + 1) for i in range(int((seconds - 3.0) / step) + 1))
    must = sorted(set(hook) | set(starts))
    steady = [t for t in steady if all(abs(t - m) >= 0.35 for m in must)]
    if len(must) >= most:
        late = [t for t in must if t > 3.0]
        room = max(most - len(hook), 1)
        return sorted(set(hook) | {late[round(i * (len(late) - 1) / max(room - 1, 1))] for i in range(min(room, len(late)))})
    room = most - len(must)
    if len(steady) > room:
        steady = [steady[round(i * (len(steady) - 1) / max(room - 1, 1))] for i in range(room)] if room else []
    return sorted(set(must) | set(steady))


def frame(path: Path, t: float, out: Path) -> Path:
    out.parent.mkdir(parents=True, exist_ok=True)
    _tool(media.ffmpeg(), ["-hide_banner", "-loglevel", "error", "-y", "-ss", f"{t:.2f}", "-i", str(path.resolve()),
                           "-frames:v", "1", "-vf", f"scale={FRAME_WIDTH}:-2", "-q:v", "5", str(out.resolve())])
    return out


def jpeg_size(path: Path) -> tuple[int, int]:
    """Width and height from a JPEG's header (no imaging library needed)."""
    data = path.read_bytes()
    i = 2
    while i + 9 < len(data):
        if data[i] != 0xFF:
            i += 1
            continue
        marker = data[i + 1]
        if marker == 0xFF:                  # fill byte
            i += 1
            continue
        length = int.from_bytes(data[i + 2:i + 4], "big")
        if marker in (0xC0, 0xC1, 0xC2):
            return int.from_bytes(data[i + 7:i + 9], "big"), int.from_bytes(data[i + 5:i + 7], "big")
        i += 2 + length
    raise VideoError(f"{path.name}: not a JPEG ffmpeg wrote")


def audio_wav(path: Path, out: Path, seconds: float) -> Path:
    out.parent.mkdir(parents=True, exist_ok=True)
    _tool(media.ffmpeg(), ["-hide_banner", "-loglevel", "error", "-y", "-t", str(seconds), "-i", str(path.resolve()),
                           "-vn", "-ac", "1", "-ar", "16000", "-c:a", "pcm_s16le", str(out.resolve())])
    return out


def loudness(wav: Path, step: float = 0.5) -> dict:
    """The sound's level (dBFS, every `step` s) and where it jumps up or drops out."""
    with wave.open(str(wav), "rb") as w:
        rate = w.getframerate()
        samples = array.array("h", w.readframes(w.getnframes()))
    per = max(int(rate * step), 1)
    levels = []
    for i in range(0, len(samples), per):
        chunk = samples[i:i + per]
        if not chunk:
            break
        rms = math.sqrt(sum(s * s for s in chunk) / len(chunk))
        levels.append(round(20 * math.log10(rms / 32768), 1) if rms > 0 else -90.0)
    jumps, drops = [], []
    for i in range(1, len(levels)):
        change = levels[i] - levels[i - 1]
        if change >= 9 and levels[i] > -35:
            jumps.append(round(i * step, 1))
        elif change <= -12 or (levels[i] < -45 <= levels[i - 1]):
            drops.append(round(i * step, 1))
    return {"step": step, "dbfs": levels, "jumps": jumps, "drops": drops}


def read(path: Path, folder: Path) -> dict:
    """Everything above for one video, kept in `folder` (frames/, audio.wav, watch.json) and reused
    when it's already there."""
    done = folder / "watch.json"
    if done.exists():
        return json.loads(done.read_text(encoding="utf-8"))
    info = probe(path)
    seconds = min(info["seconds"], MAX_SECONDS)
    cuts = cuts_in(grey_frames(path, seconds))
    frames = []
    for t in frame_times(seconds, cuts):
        f = frame(path, t, folder / "frames" / f"{int(round(t * 100)):06d}.jpg")
        w, h = jpeg_size(f)
        frames.append({"t": t, "file": f"frames/{f.name}", "width": w, "height": h})
    sound = None
    if info["audio"]:
        sound = loudness(audio_wav(path, folder / "audio.wav", seconds))
    shots = [round(b - a, 1) for a, b in zip([0.0, *cuts], [*cuts, seconds])]
    out = {"seconds": info["seconds"], "studied_seconds": seconds, "size": f"{info['width']}x{info['height']}",
           "audio": info["audio"], "cuts": cuts, "shots": len(shots),
           "average_shot": round(sum(shots) / len(shots), 2) if shots else seconds,
           "frames": frames, "loudness": sound}
    done.write_text(json.dumps(out, ensure_ascii=False, indent=1), encoding="utf-8")
    return out
