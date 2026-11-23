import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest
from unittest.mock import patch
from zipfile import ZipFile

from tools.assets import data_transfer as transfer


class DataTransferTests(unittest.TestCase):
    def test_upload_and_download_round_trip_against_private_release_standin(self):
        with tempfile.TemporaryDirectory() as temp:
            folder = Path(temp)
            ledger = folder / 'ledger.json'
            ledger.write_bytes(b'{}')
            results = folder / 'results'
            results.mkdir()
            (results / 'shift 1.krec').write_bytes(b'fixture')
            remote = folder / 'release.zip'

            def gh(args, **_):
                self.assertEqual('gh', args[0])
                if args[1:3] == ['repo', 'view']:
                    self.assertEqual('owner/assets', args[3])
                    return subprocess.CompletedProcess(args, 0, '{"nameWithOwner":"owner/assets","visibility":"PRIVATE"}')
                self.assertEqual('owner/assets', args[args.index('--repo') + 1])
                self.assertEqual('game-data-test', args[3])
                if args[2] == 'create':
                    shutil.copyfile(args[4], remote)
                else:
                    self.assertEqual('download', args[2])
                    self.assertEqual(transfer.ARCHIVE, args[args.index('--pattern') + 1])
                    shutil.copyfile(remote, Path(args[args.index('--dir') + 1]) / transfer.ARCHIVE)

            flags = ['--repo', 'owner/assets', '--tag', 'game-data-test']
            restored = folder / 'restored'
            with patch.object(transfer.subprocess, 'run', side_effect=gh):
                self.assertEqual(0, transfer.main(['upload', *flags, '--include', f'ledger={ledger}',
                                                   '--include', f'results={results}']))
                self.assertEqual(0, transfer.main(['download', *flags, '--destination', str(restored)]))
            self.assertEqual(b'{}', (restored / 'ledger/ledger.json').read_bytes())
            self.assertEqual(b'fixture', (restored / 'results/shift 1.krec').read_bytes())
            with self.assertRaisesRegex(ValueError, 'choose a new restore folder'):
                transfer.unpack(remote, restored)

    def test_public_release_is_refused_before_upload(self):
        response = subprocess.CompletedProcess([], 0, '{"nameWithOwner":"owner/assets","visibility":"PUBLIC"}')
        with patch.object(transfer.subprocess, 'run', return_value=response) as gh:
            with self.assertRaises(SystemExit):
                transfer.main(['upload', '--repo', 'owner/assets', '--tag', 'game-data-test', '--include', 'data=ignored'])
        self.assertEqual(1, gh.call_count)
        self.assertEqual(['repo', 'view'], gh.call_args.args[0][1:3])

    def test_bad_archive_leaves_no_restored_directory(self):
        checks = [('ledger/../escape', b'bad', 'unsupported data path'),
                  ('ledger/entry.json', b'bad', 'data verification failed')]
        for name, content, message in checks:
            with self.subTest(name=name), tempfile.TemporaryDirectory() as temp:
                archive = Path(temp) / transfer.ARCHIVE
                destination = Path(temp) / 'restored'
                with ZipFile(archive, 'w') as zip_file:
                    zip_file.writestr(name, content)
                    zip_file.writestr(transfer.MANIFEST, json.dumps({'schema': 1, 'files': {
                        name: {'bytes': len(content), 'sha256': hashlib.sha256(b'old').hexdigest()},
                    }}))
                with self.assertRaisesRegex(ValueError, message):
                    transfer.unpack(archive, destination)
                self.assertFalse(destination.exists())


if __name__ == '__main__':
    unittest.main()
