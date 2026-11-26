"""kehai-run release (C10.4) on stand-in repos: a code repo, an art repo in `.git-assets` beside it,
and the stand-in Unity. Nothing here touches the real repos, tags or project.

    python3 -m unittest tools.run.tests.test_release
"""
import json
import shutil
import subprocess
import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(ROOT))
from tools.run import kehai_run as kr  # noqa: E402
from tools.run import release as rel  # noqa: E402
from tools.run.tests.test_run import Fixture, sh  # noqa: E402

GIT = ('git', '-c', 'user.email=t@t', '-c', 'user.name=t')


def art(main, *args):
    return subprocess.run([*GIT, f'--git-dir={main / ".git-assets"}', f'--work-tree={main}', *args],
                          cwd=main, check=True, capture_output=True, text=True).stdout.strip()


class VersionTests(unittest.TestCase):
    def test_versions_tags_and_channels_follow_the_release_plan(self):
        self.assertEqual('v0.5.2', rel.tag_name('0.5.2'))
        self.assertEqual('v0.5.2-beta.1', rel.tag_name('v0.5.2-beta.1'))
        self.assertEqual(['-channel', 'prod', '-release-version', '1.0.0'], rel.channel_args('1.0.0'))
        self.assertEqual(['-channel', 'testing', '-release-version', '0.6.0', '-beta', '2'],
                         rel.channel_args('0.6.0-beta.2'))
        for bad in ('0.5', '0.5.2.1', '05.2.0', '0.5.2-beta.0', '0.5.2-beta', '0.5.2-rc.1', '0.5.2-dev+abc', ''):
            with self.assertRaises(rel.ReleaseError, msg=bad):
                rel.tag_name(bad)


class ReleaseTests(Fixture):
    def setUp(self):
        super().setUp()
        main, repo = self.s.main, self.s.repo
        (main / 'ProjectSettings' / 'ProjectVersion.txt').write_text('m_EditorVersion: 6000.5.10f1\n')
        # The art repo, as Phase 7 lays it out: a second git folder over the same project.
        sh('git', 'init', '-q', '--bare', str(main / '.git-assets'))
        art(main, 'config', 'core.bare', 'false')
        art(main, 'add', 'Assets/art.png')
        art(main, 'commit', '-q', '-m', 'art 1')
        self.art1 = art(main, 'rev-parse', 'HEAD')
        # Code the main folder has but no commit does (it must not reach a rebuild).
        stray = main / 'Assets' / '!_Project' / 'Stray.cs'
        stray.parent.mkdir(parents=True)
        stray.write_text('stray')
        (stray.parent / 'Stray.cs.meta').write_text('meta')
        self.code1 = subprocess.run(['git', '-C', str(repo), 'rev-parse', 'HEAD'],
                                    capture_output=True, text=True).stdout.strip()

    def tag(self, version='0.5.2-beta.1', **kw):
        return rel.tag(self.s.repo, self.s.main, version, base='HEAD', log=lambda *_: None, **kw)

    def later_work(self):
        """After the tag: the code and the art both move on."""
        repo, main = self.s.repo, self.s.main
        (repo / 'Assets' / 'keep.cs').write_text('keep, changed later')
        (repo / 'Assets' / 'later.cs').write_text('later')
        sh('git', 'add', '-A', cwd=repo)
        sh(*GIT, 'commit', '-q', '-m', 'later', cwd=repo)
        (main / 'Assets' / 'art.png').write_text('art, repainted')
        (main / 'Assets' / 'art2.png').write_text('new art')
        art(main, 'add', 'Assets/art.png', 'Assets/art2.png')
        art(main, 'commit', '-q', '-m', 'art 2')

    def seen(self):
        return json.loads((next((self.s.home / 'out').iterdir()) / 'unity.log').read_text().splitlines()[1])

    def test_a_tag_records_the_code_the_art_and_unity_in_both_repos(self):
        info = self.tag()
        self.assertEqual(('v0.5.2-beta.1', self.code1, self.art1, '6000.5.10f1'),
                         (info.name, info.code, info.art, info.unity))
        read = rel.read_tag(self.s.repo, 'v0.5.2-beta.1')
        self.assertEqual((self.code1, self.art1, '6000.5.10f1'), (read.code, read.art, read.unity))
        self.assertEqual(self.art1, art(self.s.main, 'rev-parse', 'v0.5.2-beta.1^{commit}'), 'the art repo is tagged too')
        remote = subprocess.run(['git', '-C', str(self.s.main), 'tag', '-l'], capture_output=True, text=True).stdout
        self.assertEqual('', remote.strip(), 'nothing pushed to the code remote without --push')

    def test_tagging_refuses_a_dirty_tree_a_commit_off_main_and_a_second_tag(self):
        (self.s.repo / 'Assets' / 'keep.cs').write_text('edited')
        with self.assertRaisesRegex(rel.ReleaseError, 'uncommitted'):
            self.tag()
        sh('git', 'checkout', '-q', '--', '.', cwd=self.s.repo)
        with self.assertRaisesRegex(rel.ReleaseError, 'not on'):
            rel.tag(self.s.repo, self.s.main, '0.5.2', base='HEAD~1', log=lambda *_: None)
        self.tag()
        with self.assertRaisesRegex(rel.ReleaseError, 'already exists'):
            self.tag()

    def test_a_tagged_version_laid_from_scratch_has_the_same_files_as_at_the_tag(self):
        self.tag()
        self.later_work()
        lines = []
        self.assertEqual(0, rel.check(self.s, 'v0.5.2-beta.1', keep=True, log=lines.append), lines)
        clone = Path(lines[-1].split('kept ', 1)[1])
        self.assertEqual('keep', (clone / 'Assets' / 'keep.cs').read_text(), 'code as at the tag, not as now')
        self.assertFalse((clone / 'Assets' / 'later.cs').exists(), 'code added after the tag is gone')
        self.assertEqual('art', (clone / 'Assets' / 'art.png').read_text(), 'art as at the tag, not as now')
        self.assertFalse((clone / 'Assets' / 'art2.png').exists(), 'art added after the tag is gone')
        self.assertFalse((clone / 'Assets' / '!_Project' / 'Stray.cs').exists(), 'stray code is removed')
        self.assertFalse((clone / 'Assets' / '!_Project' / 'Stray.cs.meta').exists())
        text = '\n'.join(lines)
        self.assertIn('removed (code the tag lacks): Assets/!_Project/Stray.cs', text)
        self.assertIn('result: the same files as at the tag', text)
        self.assertEqual('art, repainted', (self.s.main / 'Assets' / 'art.png').read_text(), 'the main folder is untouched')
        self.assertTrue((self.s.main / 'Assets' / '!_Project' / 'Stray.cs').exists())
        self.assertEqual('', art(self.s.main, 'status', '--porcelain', '--untracked-files=no'),
                         "the art repo's index is untouched")
        shutil.rmtree(clone)

    def test_a_file_that_differs_from_the_tag_is_caught(self):
        info = rel.read_tag(self.s.repo, self.tag().name)
        clone = self.t / 'clone'
        kr.make_clone(self.s, clone, info.code)
        (clone / 'Assets' / 'keep.cs').write_text('tampered')
        (clone / 'Assets' / 'new.cs').unlink()
        result = rel.prepare(self.s.repo, self.s.main, clone, info, use_art=False)
        self.assertEqual(['Assets/keep.cs: differs from the tag', 'Assets/new.cs: missing'], sorted(result.problems))

    def test_rebuild_checks_then_builds_the_tag_with_its_channel_and_art(self):
        self.tag()
        self.later_work()
        out = self.t / 'builds' / 'Kehai.app'
        self.assertEqual(0, rel.rebuild(self.s, '0.5.2-beta.1', 'macos', out=out, log=lambda *_: None))
        self.assert_clean()
        seen = self.seen()
        self.assertEqual(self.code1, seen['source_commit'], 'Unity stamps the tagged commit')
        self.assertEqual(self.art1[:7], seen['art_commit'], 'and the tagged art')
        args = seen['args']
        for pair in (['-executeMethod', 'KehaiBuild.Build'], ['-channel', 'testing'], ['-release-version', '0.5.2'],
                     ['-beta', '1'], ['-kehai-build-out', str(out)], ['-platform', 'macos']):
            i = args.index(pair[0])
            self.assertEqual(pair, args[i:i + 2])
        run_out = next((self.s.home / 'out').iterdir())
        self.assertIn('result: the same files as at the tag', (run_out / 'release_check.txt').read_text())

    def test_a_tag_without_art_needs_art_today_and_is_stamped_unversioned(self):
        shutil.rmtree(self.s.main / '.git-assets')
        self.tag('0.5.2')
        out = self.t / 'b' / 'Kehai.exe'
        self.assertEqual(2, rel.rebuild(self.s, '0.5.2', 'windows', out=out, log=lambda *_: None))
        self.assertFalse((self.s.home / 'out').exists(), 'nothing ran')
        self.assertEqual(0, rel.rebuild(self.s, '0.5.2', 'windows', out=out, art_today=True, log=lambda *_: None))
        seen = self.seen()
        self.assertEqual('unversioned', seen['art_commit'])
        args = seen['args']
        self.assertEqual(['-buildTarget', 'Win64'], args[args.index('-buildTarget'):][:2])
        self.assertEqual(['-channel', 'prod'], args[args.index('-channel'):][:2])

    def test_a_tag_from_another_unity_is_refused(self):
        self.tag()
        (self.s.main / 'ProjectSettings' / 'ProjectVersion.txt').write_text('m_EditorVersion: 6000.6.0f1\n')
        lines = []
        self.assertEqual(2, rel.rebuild(self.s, '0.5.2-beta.1', 'macos', out=self.t / 'b' / 'Kehai.app',
                                        log=lines.append))
        self.assertIn('6000.5.10f1', lines[-1])

    def test_the_cli_checks_a_tag(self):
        self.tag()
        p = self.run_cli('release', 'check', 'v0.5.2-beta.1')
        text, _ = p.communicate(timeout=60)
        self.assertEqual(0, p.returncode, text)
        self.assertIn('the same files as at the tag', text)


if __name__ == '__main__':
    unittest.main()
