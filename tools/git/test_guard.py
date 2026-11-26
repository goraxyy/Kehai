"""C7.2: a commit of a .png to the code repo is refused; code commits go through.

    python3 -m unittest tools.git.test_guard
"""
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT))
from tools.git import guard  # noqa: E402

GIT = ['git', '-c', 'user.email=t@t', '-c', 'user.name=t']


class GuardTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory(prefix='kehai-guard-')
        repo = self.repo = Path(self.tmp.name)
        subprocess.run(['git', 'init', '-q'], cwd=repo, check=True)
        shutil.copytree(ROOT / 'tools' / 'git', repo / 'tools' / 'git',
                        ignore=shutil.ignore_patterns('__pycache__', 'test_*'))
        subprocess.run(['sh', 'tools/git/install-hooks.sh'], cwd=repo, check=True, capture_output=True)

    def tearDown(self):
        self.tmp.cleanup()

    def commit(self, *files: str) -> subprocess.CompletedProcess:
        for f in files:
            (self.repo / f).parent.mkdir(parents=True, exist_ok=True)
            (self.repo / f).write_text('x')
        subprocess.run(['git', 'add', *files], cwd=self.repo, check=True)
        return subprocess.run([*GIT, 'commit', '-q', '-m', 'test'], cwd=self.repo, capture_output=True, text=True)

    def test_a_png_commit_is_refused_and_nothing_is_committed(self):
        result = self.commit('Assets/Art/Sign.png', 'Assets/Art/Sign.png.meta')
        self.assertNotEqual(0, result.returncode)
        self.assertIn('Assets/Art/Sign.png: .png is art', result.stderr)
        log = subprocess.run(['git', 'log', '--oneline'], cwd=self.repo, capture_output=True, text=True)
        self.assertNotEqual(0, log.returncode, 'no commit was made')

    def test_code_and_docs_commit(self):
        self.assertEqual(0, self.commit('Assets/Code/Door.cs', 'Assets/Code/Door.cs.meta', 'docs/a.md',
                                        'tools/x.py', 'tools/marketing/.env.example').returncode)

    def test_every_kind_the_rules_forbid(self):
        for path in ('Packages/manifest.json', 'ProjectSettings/TagManager.asset', 'tools/marketing/.env',
                     'Assets/Scenes/Store.unity', 'Assets/Props/Mop.prefab', 'Assets/Art/Floor.mat',
                     'Assets/Audio/Hum.wav', 'Assets/Models/Shelf.fbx', 'Assets/Data/Stock.asset'):
            self.assertIsNotNone(guard.why(path), path)
        for path in ('Assets/Code/A.cs', 'Assets/Code.meta', 'Assets/Shaders/Fog.shader', 'README.md', 'tools/rl/x.yaml'):
            self.assertIsNone(guard.why(path), path)


if __name__ == '__main__':
    unittest.main()
