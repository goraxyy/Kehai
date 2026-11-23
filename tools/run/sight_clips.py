"""Prepare a retained sight check suite for the stock replay renderer."""
from __future__ import annotations

import gzip
import json
import math
import os
import re
import shutil
import struct
import subprocess
import zlib
from dataclasses import dataclass
from io import BytesIO
from pathlib import Path
from urllib.parse import quote

from tools.run.trick_clip import recorded_commit, verify_movie

FPS = 30
WIDTH, HEIGHT = 1280, 720


@dataclass(frozen=True)
class SightCase:
    replay: Path
    duration: float
    expected: str
    passed: bool
    awareness: float
    seconds_to_see: float

    @property
    def name(self) -> str:
        return self.replay.stem

    def shot(self) -> list[str]:
        return ['-krec', str(self.replay), '-from', '0', '-to', str(self.duration),
                '-shot', 'topdown', '-layers', 'none', '-size', f'{WIDTH}x{HEIGHT}', '-fps', str(FPS)]


def read_string(stream: BytesIO) -> bytes:
    # BinaryWriter.Write(string) uses a 7-bit encoded byte count.
    size = 0
    for shift in range(0, 35, 7):
        byte = stream.read(1)[0]
        size |= (byte & 127) << shift
        if byte < 128:
            result = stream.read(size)
            if len(result) == size:
                return result
            break
    raise ValueError('invalid recording header string')


def sight_duration(path: Path) -> float:
    """Check the header and finish record of a short sandbox replay. Unity decodes the poses."""
    limit = 1024 * 1024
    try:
        with gzip.open(path, 'rb') as source:
            raw = source.read(limit + 1)
        if len(raw) > limit:
            raise ValueError('recording exceeds the sight clip size limit')
        header = BytesIO(raw)
        if header.read(4) != b'KREC' or header.read(2) not in (b'\x04\x00', b'\x05\x00'):
            raise ValueError('unsupported KREC header')
        read_string(header)  # Game name.
        stem = read_string(header)
        header.read(4)  # Shift number.
        read_string(header)  # Recording start date.
        if read_string(header) != b'Sight sandbox' or stem != path.stem.encode():
            raise ValueError('expected a sight recording with the check name in its header')
        # SandboxCheckRecording writes End (255), the duration (float32), then false.
        tag, duration, clocked_out = struct.unpack('<BfB', raw[-6:])
        if len(raw) <= header.tell() + 6 or tag != 255 or clocked_out != 0 or not 0.1 <= duration <= 30:
            raise ValueError('missing or out-of-range sight recording end')
        return duration
    except (OSError, EOFError, IndexError, struct.error, ValueError, zlib.error) as err:
        raise ValueError(f'{path}: {err}') from err


@dataclass(frozen=True)
class SightReview:
    source: Path
    commit: str
    cases: tuple[SightCase, ...]

    def write(self, out: Path, results: dict[str, str | None]) -> None:
        source = quote(os.path.relpath(self.source.with_suffix('.md'), out))
        rows = ['# Sight check clips', '', f'[Source bot report]({source}) · commit `{self.commit}`', '',
                '| Check | Expected | Bot result | Max awareness | Time to notice | Clip |',
                '|---|---|---|---:|---:|---|']
        for case in self.cases:
            name = case.name
            result = results.get(name, 'Pending')
            clip = f'[MP4]({name}/clip.mp4) · [details]({name}/clip.json)' if result is None else result
            if (out / name / 'unity.log').is_file():
                clip += f' · [render log]({name}/unity.log)'
            noticed = f'{case.seconds_to_see:.2f} s' if case.seconds_to_see >= 0 else 'Never'
            rows.append(f'| {name} | {case.expected} | {"Pass" if case.passed else "Fail"} | '
                        f'{case.awareness:.3f} | {noticed} | {clip} |')
        path = out / 'review.md'
        tmp = path.with_suffix('.tmp')
        tmp.write_text('\n'.join(rows) + '\n', encoding='utf-8')
        tmp.replace(path)


def read_review(report: Path) -> SightReview:
    report = report.resolve()
    if (report.parent.name != 'sandbox_checks' or report.suffix != '.json'
            or not re.fullmatch(r'sight_\d{8}_\d{6}(?:_\d{3})?', report.stem)):
        raise ValueError('choose the sight_<time>.json report under a retained run’s sandbox_checks/')
    commit = recorded_commit(report.parent.parent)
    if not report.with_suffix('.md').is_file():
        raise ValueError('missing the source Markdown bot report beside the JSON')
    rows = json.loads(report.read_text(encoding='utf-8'))
    if not isinstance(rows, list) or len(rows) != 8:
        raise ValueError('expected eight sight check rows')

    cases = []
    names = set()
    for row in rows:
        name = row.get('name') if isinstance(row, dict) else None
        if not isinstance(name, str) or not re.fullmatch(r'[a-z0-9-]+', name) or name in names:
            raise ValueError(f'invalid or duplicate sight check name: {name!r}')
        names.add(name)
        relative = Path(report.stem, name + '.krec')
        if row.get('replay') != relative.as_posix():
            raise ValueError(f'{name}: expected replay at {relative}')
        path = report.parent / relative
        if path.resolve() != path:
            raise ValueError(f'{name}: the replay must be inside the retained run')
        expected, passed = row.get('expected'), row.get('pass')
        awareness, seconds = row.get('max_awareness'), row.get('seconds_to_see')
        if expected not in ('Seen', 'Unseen', 'Witnessed', 'NotWitnessed') or type(passed) is not bool:
            raise ValueError(f'{name}: invalid bot result')
        duration = sight_duration(path)
        if (type(awareness) not in (int, float) or not 0 <= awareness <= 1
                or type(seconds) not in (int, float) or not (seconds == -1 or 0 <= seconds <= duration + 0.01)):
            raise ValueError(f'{name}: invalid recorded sight metrics')
        cases.append(SightCase(path, duration, expected, passed, awareness, seconds))
    return SightReview(report, commit, tuple(cases))


def video_tools(main: Path) -> tuple[Path, Path]:
    """Use Homebrew/PATH or the Remotion pair already installed in the private project."""
    configured = os.environ.get('KEHAI_FFMPEG')
    system = shutil.which('ffmpeg')
    system_probe = shutil.which('ffprobe')
    remotion = sorted((main / 'tools/marketing/editor/node_modules/@remotion').glob('compositor-*/ffmpeg'))
    candidates = [Path(configured)] if configured else ([Path(system)] if system else []) + remotion
    for candidate in candidates:
        if not candidate.is_file() or not os.access(candidate, os.X_OK):
            continue
        encoder = candidate.resolve()
        probe = candidate.absolute().with_name('ffprobe')
        if not os.access(probe, os.X_OK) and system_probe:
            probe = Path(system_probe).resolve()
        if probe.is_file() and os.access(probe, os.X_OK):
            return encoder, probe.resolve()
    raise ValueError('sight clips need ffmpeg and ffprobe (Homebrew, or the Remotion pair under the main project)')


def verify_sight_movie(out: Path, case: SightCase, probe: Path) -> None:
    shot = verify_movie(out)
    frames = math.floor(case.duration * FPS + 0.001) + 1
    expected = {'krec': case.replay.name, 'video': 'clip.mp4', 'from': 0, 'frames': frames,
                'shot': 'topdown', 'width': WIDTH, 'height': HEIGHT, 'fps': FPS}
    if any(shot.get(key) != value for key, value in expected.items()):
        raise ValueError('the renderer sidecar does not match the requested replay and frame range')

    result = subprocess.run([str(probe), '-v', 'error', '-count_frames', '-show_entries',
                             'stream=codec_type,codec_name,width,height,nb_read_frames', '-of', 'json',
                             str((out / 'clip.mp4').resolve())],
                            cwd=probe.parent, capture_output=True, text=True, timeout=30)
    if result.returncode or result.stderr:
        raise ValueError('ffprobe could not read the MP4 cleanly: ' + result.stderr.strip())
    streams = json.loads(result.stdout).get('streams')
    video = {'codec_type': 'video', 'codec_name': 'h264', 'width': WIDTH,
             'height': HEIGHT, 'nb_read_frames': str(frames)}
    if streams != [video]:
        raise ValueError('expected one H.264 stream with every requested frame and no audio')
