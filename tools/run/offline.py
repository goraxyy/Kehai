"""Working-tree source and production logic checks that can run with the editor open."""
from pathlib import Path
import subprocess
import sys


def check(repo: Path, unity_app: Path, references: Path) -> int:
    checks = [
        ('Editor runtime and EditMode test sources',
         ['tools/build/check_csharp.py', '--unity', str(unity_app), '--references', str(references)]),
        ('Gameplay player sources',
         ['tools/build/check_csharp.py', '--unity', str(unity_app), '--references', str(references),
          '--player-edition', 'gameplay']),
        ('Edition, career and first-shift guide NUnit cases',
         ['tools/build/check_phase67.py', '--unity', str(unity_app)]),
        ('Flash sale and complaint policies', ['tools/tricks/check_phase1_rules.py', '--unity', str(unity_app)]),
    ]
    print('Unity-free checks use the files currently in', repo, flush=True)
    print('Scene, physics, NavMesh, rendering and other EditMode tests require Unity.', flush=True)
    for label, args in checks:
        print('Checking', label, flush=True)
        result = subprocess.run([sys.executable, *args], cwd=repo)
        if result.returncode:
            return result.returncode
    return 0
