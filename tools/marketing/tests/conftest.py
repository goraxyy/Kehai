import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

import pytest


@pytest.fixture(autouse=True)
def _no_real_playtests(tmp_path, monkeypatch):
    """No test reads the real playtests folder or tools/playtest/.env (the work job pulls playtests)."""
    monkeypatch.setenv("KEHAI_PLAYTESTS", str(tmp_path / "playtests-root"))
    monkeypatch.setattr("km.playtest.ENV_FILE", tmp_path / "no-playtest.env")
