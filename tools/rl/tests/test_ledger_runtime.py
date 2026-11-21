"""Run the real Ledger tests with native Unity boundaries replaced, without Unity."""
from pathlib import Path
import shutil
import sys

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import offline_csharp

ROOT = Path(__file__).resolve().parents[3]


def test_ledger_persistence_migration_and_shift_end(tmp_path):
    if not shutil.which("dotnet"):
        pytest.skip("offline .NET SDK unavailable")
    core = ROOT / "Assets/!_Project/_Game/AI/Scripts/Core"
    sources = [core / f"{name}.cs" for name in (
        "KarenLedger", "ComboRoom", "PlacementMemory", "EscapeHabits", "LegacyDoorKeys")]
    sources += [ROOT / "Assets/!_Project/_Game/AI/Scripts/Diagnostics/EscapeRecorder.cs",
                ROOT / "Assets/!_Project/_Tests/Editor/Karen050LearningTests.cs",
                Path(__file__).parent / "fixtures/LedgerSupport.cs"]
    assert "passed 9 ledger tests" in offline_csharp.run(sources, tmp_path)
