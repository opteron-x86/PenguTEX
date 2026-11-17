#!/usr/bin/env python3
"""Check the boundary between tested GitHub release files and Nexus publishing."""
import hashlib
import importlib.util
import io
from pathlib import Path
import shutil
import subprocess
import tarfile
import tempfile
import unittest
import zipfile

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('nexus_upload', ROOT / 'tools/prepare-nexus-upload.py')
nexus = importlib.util.module_from_spec(spec)
spec.loader.exec_module(nexus)

VERSION = '0.1.0-alpha.11'


def write_zip(path, entries):
    with zipfile.ZipFile(path, 'w') as archive:
        for name, data in entries.items():
            archive.writestr(name, data)


def write_tar(path, entries):
    """entries: name -> (bytes, mode), or None for a directory."""
    with tarfile.open(path, 'w:gz') as archive:
        for name, entry in entries.items():
            info = tarfile.TarInfo(name)
            info.mtime = 1_700_000_000
            if entry is None:
                info.type, info.mode = tarfile.DIRTYPE, 0o755
                archive.addfile(info)
            else:
                data, info.mode = entry
                info.size = len(data)
                archive.addfile(info, io.BytesIO(data))


class NexusUpload(unittest.TestCase):
    def setUp(self):
        self.commit = 'a' * 40
        self.tag = 'v' + VERSION
        self.release = {'tagName': self.tag, 'isDraft': False, 'isPrerelease': True}
        self.run = {'path': '.github/workflows/build.yml', 'event': 'push', 'head_sha': self.commit,
                    'head_branch': self.tag, 'status': 'completed', 'conclusion': 'success'}

    def release_files(self, directory, windows=None, linux=None):
        names = nexus.files(VERSION)
        write_zip(directory / names['windows'], windows or {
            'win-x64/PenguTEX.Desktop.exe': b'MZ desktop', 'win-x64/PenguTEX.exe': b'MZ cli',
            'win-x64/pengutex_bc7.dll': b'MZ bc7', 'win-x64/VERSION': VERSION + '\n'})
        write_tar(directory / names['linux'], linux or {
            'linux-x64': None, 'linux-x64/PenguTEX': (b'cli', 0o755), 'linux-x64/PenguTEX.Desktop': (b'desktop', 0o755),
            'linux-x64/libpengutex_bc7.so': (b'ELF', 0o755), 'linux-x64/install-desktop.sh': (b'#!/bin/sh\n', 0o755),
            'linux-x64/VERSION': ((VERSION + '\n').encode(), 0o644), 'linux-x64/docs': None,
            'linux-x64/docs/CLI.md': (b'# CLI\n', 0o644)})
        sums = ''.join(f'{hashlib.sha256((directory / name).read_bytes()).hexdigest()}  {name}\n'
                       for name in names.values())
        (directory / 'SHA256SUMS').write_text(sums + 'f' * 64 + f'  PenguTEX-{VERSION}-source.tar.gz\n')
        return names

    def test_accepts_published_releases_and_pre_releases(self):
        nexus.validate_release(self.tag, self.release, self.commit, [self.run])
        nexus.validate_release('v0.2.0', {'tagName': 'v0.2.0', 'isDraft': False, 'isPrerelease': False}, self.commit,
                               [{**self.run, 'head_branch': 'master'}, {**self.run, 'head_branch': 'v0.2.0'}])

    def test_rejects_tags_that_are_not_versions(self):
        for tag in ('main', '0.6.0', 'v0.6', 'v0.6.0+build', 'v01.6.0', '--help', 'v0.6.0\n', 'v0.6.0-'):
            with self.subTest(tag=tag), self.assertRaises(ValueError):
                nexus.release_version(tag)

    def test_requires_a_published_release_for_the_tag(self):
        for change in ({'isDraft': True}, {'isPrerelease': False}, {'tagName': 'v0.1.0-alpha.10'}):
            with self.subTest(change=change), self.assertRaises(ValueError):
                nexus.validate_release(self.tag, {**self.release, **change}, self.commit, [self.run])

    def test_requires_a_successful_build_of_the_tag(self):
        changes = ({'path': '.github/workflows/nexus.yml'}, {'event': 'workflow_dispatch'}, {'head_sha': 'b' * 40},
                   {'head_branch': 'master'}, {'status': 'in_progress'}, {'conclusion': 'failure'})
        for change in changes:
            with self.subTest(change=change), self.assertRaises(ValueError):
                nexus.validate_release(self.tag, self.release, self.commit, [{**self.run, **change}])
        with self.assertRaises(ValueError):
            nexus.validate_release(self.tag, self.release, self.commit, [])

    def test_verifies_checksums_and_package_versions(self):
        with tempfile.TemporaryDirectory() as tmp:
            directory = Path(tmp)
            names = self.release_files(directory)
            packages = nexus.verify_downloads(directory, VERSION)
            self.assertEqual({key: directory / name for key, name in names.items()}, packages)
            with (directory / names['windows']).open('ab') as stream:
                stream.write(b'changed after release')
            with self.assertRaisesRegex(ValueError, 'checksum mismatch'):
                nexus.verify_downloads(directory, VERSION)

    def test_rejects_packages_of_another_version_or_missing_programs(self):
        cases = (
            ({'windows': {'win-x64/PenguTEX.Desktop.exe': b'MZ', 'win-x64/PenguTEX.exe': b'MZ', 'win-x64/pengutex_bc7.dll': b'MZ',
                          'win-x64/VERSION': '0.1.0-alpha.10\n'}}, 'another version'),
            ({'windows': {'win-x64/PenguTEX.exe': b'MZ', 'win-x64/pengutex_bc7.dll': b'MZ', 'win-x64/VERSION': VERSION}}, 'missing'),
            ({'windows': {'win-x64/PenguTEX.Desktop.exe': b'MZ', 'win-x64/PenguTEX.exe': b'MZ', 'win-x64/VERSION': VERSION}}, 'missing'),
            ({'linux': {'linux-x64/PenguTEX': (b'', 0o755), 'linux-x64/PenguTEX.Desktop': (b'', 0o755),
                        'linux-x64/libpengutex_bc7.so': (b'', 0o755), 'linux-x64/VERSION': (b'0.1.0-alpha.10\n', 0o644)}},
             'another version'),
            ({'linux': {'linux-x64/PenguTEX': (b'', 0o755), 'linux-x64/PenguTEX.Desktop': (b'', 0o755),
                        'linux-x64/VERSION': ((VERSION + '\n').encode(), 0o644)}}, 'missing'),
        )
        for change, message in cases:
            with self.subTest(change=list(change)), tempfile.TemporaryDirectory() as tmp:
                self.release_files(Path(tmp), **change)
                with self.assertRaisesRegex(ValueError, message):
                    nexus.verify_downloads(Path(tmp), VERSION)

    def test_linux_zip_keeps_contents_and_executable_permissions(self):
        with tempfile.TemporaryDirectory() as tmp:
            directory = Path(tmp)
            names = self.release_files(directory)
            output = nexus.repack_linux(directory / names['linux'], directory / 'linux.zip')
            with zipfile.ZipFile(output) as archive:
                modes = {info.filename: info.external_attr >> 16 & 0o777 for info in archive.infolist()}
                self.assertEqual(b'#!/bin/sh\n', archive.read('linux-x64/install-desktop.sh'))
            self.assertEqual(0o755, modes['linux-x64/PenguTEX.Desktop'])
            self.assertEqual(0o644, modes['linux-x64/VERSION'])
            self.assertIn('linux-x64/docs/', modes)
            # What a user's extractor does with it.
            if shutil.which('unzip'):
                subprocess.run(['unzip', '-q', str(output), '-d', str(directory / 'out')], check=True)
                launcher = directory / 'out/linux-x64/PenguTEX.Desktop'
                self.assertEqual(0o755, launcher.stat().st_mode & 0o777)
                self.assertEqual(0o644, (directory / 'out/linux-x64/VERSION').stat().st_mode & 0o777)
                self.assertTrue((directory / 'out/linux-x64/docs/CLI.md').is_file())
            with self.assertRaises(FileExistsError):
                nexus.repack_linux(directory / names['linux'], output)

    def test_linux_zip_refuses_links_and_paths_outside_the_package(self):
        with tempfile.TemporaryDirectory() as tmp:
            directory = Path(tmp)
            write_tar(directory / 'outside.tar.gz', {'linux-x64/../evil': (b'x', 0o644)})
            write_tar(directory / 'other.tar.gz', {'elsewhere/file': (b'x', 0o644)})
            with tarfile.open(directory / 'link.tar.gz', 'w:gz') as archive:
                info = tarfile.TarInfo('linux-x64/link')
                info.type, info.linkname = tarfile.SYMTYPE, '/etc/passwd'
                archive.addfile(info)
            for name in ('outside', 'other', 'link'):
                with self.subTest(name=name), self.assertRaises(ValueError):
                    nexus.repack_linux(directory / f'{name}.tar.gz', directory / f'{name}.zip')

    def test_changelog_is_the_versions_section_one_line_per_entry(self):
        changelog = ('# Changelog\n\n## [Unreleased]\n\n- Later.\n\n'
                     f'## [{VERSION}] - 2026-11-17\n\nRebuild existing outputs once.\n\n'
                     '- Added **native BC7** encoding for\n  mip completion.\n- Fixed `prune-cache` and the\n'
                     '  [CLI guide](docs/CLI.md).\n\n## [0.1.0-alpha.10] - 2026-10-30\n\n- Older.\n')
        section = nexus.changelog_section(changelog, VERSION)
        self.assertEqual(['Rebuild existing outputs once.', 'Added native BC7 encoding for mip completion.',
                          'Fixed prune-cache and the CLI guide.'], nexus.plain_changelog(section).splitlines())
        with self.assertRaises(ValueError):
            nexus.changelog_section(changelog, '0.2.0')

if __name__ == '__main__':
    unittest.main()
