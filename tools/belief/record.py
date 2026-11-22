"""Resume belief recording by league, parity check and legs export."""

import fcntl
from pathlib import Path
import shutil
import subprocess
import sys


def git_head(repo: Path) -> str:
    return subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=repo, text=True).strip()


def record_league(repo: Path, output: Path, split: str, revision: str) -> None:
    final = output / split
    if final.is_dir():
        print(f"{split} league: already recorded", flush=True)
        return
    if git_head(repo) != revision:
        raise RuntimeError("The checkout changed. Rerun from the original commit to continue.")

    pending = output / f".{split}.recording"
    if pending.exists():
        shutil.rmtree(pending)
    league = repo / "tools" / "rl" / "leagues" / f"record_{split}.json"
    command = [
        repo / "tools" / "belief" / "run_unity.sh", league, pending,
        "-belief-trace", pending / "traces",
    ]
    if split == "train":
        command.append("-kehai-timing")
    subprocess.run(command, check=True)
    if git_head(repo) != revision:
        raise RuntimeError("The checkout changed during recording. Rerun from the original commit to continue.")
    traces = pending / "traces"
    if not (traces / "map.json").is_file() or not any(traces.glob("trace_*.jsonl")):
        raise RuntimeError(f"The {split} league produced no complete trace set. See {pending / 'unity.log'}.")
    if split == "train" and not any((pending / "shift_records").glob("shift_*.json")):
        raise RuntimeError(f"The training league produced no shift records. See {pending / 'unity.log'}.")
    pending.rename(final)


def record(repo: Path, output: Path, python: Path) -> None:
    output.mkdir(parents=True, exist_ok=True)
    with (output / ".record.lock").open("a") as lock:
        try:
            fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
        except BlockingIOError as exc:
            raise RuntimeError(f"A recording is already running in {output}.") from exc

        revision = git_head(repo)
        receipt = output / ".record-revision"
        if receipt.exists():
            previous = receipt.read_text().strip()
            if previous != revision:
                raise RuntimeError(f"These recordings started at {previous}. Use that commit or a new output folder.")
        else:
            if any((output / split).exists() for split in ("train", "val")):
                raise RuntimeError(f"{output} already holds recordings. Use a new output folder.")
            pending = receipt.with_suffix(".tmp")
            pending.write_text(revision + "\n")
            pending.replace(receipt)

        for split in ("train", "val"):
            record_league(repo, output, split, revision)
        for split in ("train", "val"):
            folder = output / split
            passed = folder / ".parity-passed"
            if passed.is_file():
                print(f"{split} parity check: already passed", flush=True)
                continue
            subprocess.run([python, repo / "tools" / "belief" / "replay.py", folder / "traces", "--parity"], check=True)
            passed.touch()

        train = output / "train"
        legs = train / "legs.jsonl"
        if not legs.is_file():
            pending = train / ".legs.jsonl.tmp"
            subprocess.run([python, repo / "tools" / "learn" / "legs.py", train / "shift_records",
                            "--out", pending], check=True)
            pending.replace(legs)
        print(f"Recordings in {output}.")


def main() -> int:
    repo = Path(__file__).resolve().parents[2]
    output = Path(sys.argv[1]).resolve()
    python = Path(sys.argv[2]).absolute()
    if not python.is_file():
        print(f"record: Python environment missing at {python}. Run tools/rl/setup.sh first.", file=sys.stderr)
        return 1
    try:
        record(repo, output, python)
    except KeyboardInterrupt:
        return 130
    except subprocess.CalledProcessError as exc:
        print(f"record: stage exited with {exc.returncode}. Rerun the same command to resume.", file=sys.stderr)
        return exc.returncode if exc.returncode > 0 else 128 - exc.returncode
    except (OSError, RuntimeError) as exc:
        print(f"record: {exc}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
