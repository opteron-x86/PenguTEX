#!/usr/bin/env python3
"""Download and verify a published GitHub release for the manual Nexus workflow."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import stat
import subprocess
import tarfile
import time
import uuid
import zipfile

WORKFLOW = '.github/workflows/build.yml'
NUMBER = r'(?:0|[1-9][0-9]*)'
IDENTIFIER = rf'(?:{NUMBER}|[0-9]*[A-Za-z-][0-9A-Za-z-]*)'
VERSION = rf'{NUMBER}\.{NUMBER}\.{NUMBER}(?:-{IDENTIFIER}(?:\.{IDENTIFIER})*)?'


def release_version(tag):
    if not re.fullmatch('v' + VERSION, tag):
        raise ValueError('Choose a release tag, such as v0.2.0 or v0.1.0-alpha.11')
    return tag[1:]


def validate_release(tag, release, commit, runs):
    """The tag is a published release, and the build and test workflow passed on its commit."""
    version = release_version(tag)
    if release.get('tagName') != tag or release.get('isDraft') is not False:
        raise ValueError('Nexus uploads require a published release')
    if release.get('isPrerelease') is not ('-' in version):
        raise ValueError('The release pre-release flag does not match its version')
    if not re.fullmatch(r'[0-9a-f]{40}', commit):
        raise ValueError('The release tag does not name a commit')
    for run in runs:
        if (run.get('path') == WORKFLOW and run.get('event') == 'push' and run.get('head_sha') == commit
                and run.get('head_branch') == tag and run.get('status') == 'completed'
                and run.get('conclusion') == 'success'):
            return
    raise ValueError('The tagged build and test workflow has not completed successfully')


def files(version):
    return {'windows': f'PenguTEX-{version}-win-x64.zip', 'linux': f'PenguTEX-{version}-linux-x64.tar.gz'}


def verify_downloads(directory, version):
    """The two packages match the release checksums and are the builds of this version."""
    sums = {}
    for line in (directory / 'SHA256SUMS').read_text(encoding='utf-8').splitlines():
        checksum, _, name = line.partition('  ')
        if name in sums or not name or not re.fullmatch(r'[0-9a-f]{64}', checksum):
            raise ValueError('Malformed or duplicate release checksum')
        sums[name] = checksum
    names = files(version)
    for name in names.values():
        with (directory / name).open('rb') as stream:
            if sums.get(name) != hashlib.file_digest(stream, 'sha256').hexdigest():
                raise ValueError(f'Release checksum mismatch: {name}')

    def packaged_version(text, name):
        if text.decode('utf-8').strip() != version:
            raise ValueError(f'{name} was built from another version')

    with zipfile.ZipFile(directory / names['windows']) as archive:
        if archive.testzip() is not None:
            raise ValueError('Windows ZIP is corrupt')
        present = set(archive.namelist())
        for name in ('win-x64/PenguTEX.Desktop.exe', 'win-x64/PenguTEX.exe', 'win-x64/pengutex_bc7.dll'):
            if name not in present:
                raise ValueError(f'Windows ZIP is missing {name}')
        packaged_version(archive.read('win-x64/VERSION'), names['windows'])
    with tarfile.open(directory / names['linux']) as archive:
        present = set(archive.getnames())
        for name in ('linux-x64/PenguTEX', 'linux-x64/PenguTEX.Desktop', 'linux-x64/libpengutex_bc7.so'):
            if name not in present:
                raise ValueError(f'Linux archive is missing {name}')
        packaged_version(archive.extractfile('linux-x64/VERSION').read(), names['linux'])
    return {key: directory / name for key, name in names.items()}


def repack_linux(tarball, output):
    """Nexus accepts ZIP, 7z and RAR archives, so the Linux .tar.gz is repacked as a ZIP.

    The ZIP holds the same files with the same bytes and Unix permissions, which unzip and the
    desktop archive managers restore, so the launcher stays executable.
    """
    expected = {}
    with tarfile.open(tarball) as source, zipfile.ZipFile(output, 'x', zipfile.ZIP_DEFLATED, compresslevel=6) as target:
        for member in source:
            name = member.name
            if (name != 'linux-x64' and not name.startswith('linux-x64/')) or '..' in name.split('/'):
                raise ValueError(f'Unexpected path in Linux archive: {name}')
            if not (member.isfile() or member.isdir()):
                raise ValueError(f'Unsupported entry in Linux archive: {name}')
            modified = max((1980, 1, 1, 0, 0, 0), tuple(time.gmtime(member.mtime)[:6]))
            info = zipfile.ZipInfo(name + '/' if member.isdir() else name, date_time=modified)
            info.create_system = 3  # Unix: the high half of external_attr is the file mode
            mode = (stat.S_IFDIR if member.isdir() else stat.S_IFREG) | (member.mode & 0o777)
            info.external_attr = mode << 16 | (0x10 if member.isdir() else 0)
            if member.isdir():
                target.writestr(info, b'')
                continue
            info.compress_type = zipfile.ZIP_DEFLATED
            data = source.extractfile(member).read()
            target.writestr(info, data, compresslevel=6)
            expected[name] = (hashlib.sha256(data).hexdigest(), member.mode & 0o777)
    with zipfile.ZipFile(output) as archive:
        if archive.testzip() is not None:
            raise ValueError('Repacked Linux ZIP is corrupt')
        actual = {info.filename: (hashlib.sha256(archive.read(info)).hexdigest(), info.external_attr >> 16 & 0o777)
                  for info in archive.infolist() if not info.is_dir()}
    if actual != expected or not expected:
        raise ValueError('Repacked Linux ZIP differs from the release archive')
    return output


def changelog_section(changelog, version):
    """The notes under this version's heading in CHANGELOG.md."""
    heading = f'## [{version}]'
    if changelog.count(heading) != 1:
        raise ValueError(f'CHANGELOG.md needs one section for {version}')
    return changelog.split(heading, 1)[1].split('\n', 1)[1].split('\n## ', 1)[0].strip()


def plain_changelog(markdown):
    """Release notes as one line per paragraph or list item, without Markdown markup.

    Nexus shows each line of a changelog as its own entry, so wrapped lines are joined.
    """
    entries, current = [], []

    def flush():
        if current:
            entries.append(' '.join(current))
            current.clear()

    for line in markdown.splitlines():
        text = line.strip()
        if not text:
            flush()
            continue
        if text.startswith(('- ', '* ', '#')):
            flush()
            text = text.lstrip('#*- ').strip()
        current.append(text)
    flush()
    cleaned = []
    for entry in entries:
        entry = re.sub(r'\[([^\]]+)\]\([^)]+\)', r'\1', entry)
        entry = re.sub(r'\*\*|`', '', entry)
        if entry:
            cleaned.append(entry)
    return '\n'.join(cleaned)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--tag', required=True)
    parser.add_argument('--repo', required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    version = release_version(args.tag)
    if not re.fullmatch(r'[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+', args.repo):
        raise ValueError('Invalid GitHub repository')
    if args.output.exists() and any(args.output.iterdir()):
        raise ValueError('Download directory must be empty')
    args.output.mkdir(parents=True, exist_ok=True)

    def gh_json(*arguments):
        return json.loads(subprocess.check_output(['gh', *arguments], text=True))

    release = gh_json('release', 'view', args.tag, '--repo', args.repo, '--json', 'tagName,isDraft,isPrerelease')
    commit = gh_json('api', f'repos/{args.repo}/commits/{args.tag}')['sha']
    runs = gh_json('api', '--method', 'GET', f'repos/{args.repo}/actions/workflows/{Path(WORKFLOW).name}/runs',
                   '-f', f'head_sha={commit}', '-f', 'event=push')['workflow_runs']
    validate_release(args.tag, release, commit, runs)
    patterns = [argument for name in (*files(version).values(), 'SHA256SUMS') for argument in ('--pattern', name)]
    subprocess.run(['gh', 'release', 'download', args.tag, '--repo', args.repo, '--dir', str(args.output), *patterns],
                   check=True)
    packages = verify_downloads(args.output, version)
    packages['linux'] = repack_linux(packages['linux'], args.output / f'PenguTEX-{version}-linux-x64.zip')
    # The checked-out CHANGELOG.md, so a correction made to the notes after the release reaches Nexus.
    notes = (Path(__file__).resolve().parents[1] / 'CHANGELOG.md').read_text(encoding='utf-8')
    changelog = plain_changelog(changelog_section(notes, version))
    if not changelog:
        raise ValueError('Release changelog is empty')
    output = os.environ.get('GITHUB_OUTPUT')
    if output:
        delimiter = 'changelog_' + uuid.uuid4().hex
        with open(output, 'a', encoding='utf-8') as stream:
            stream.write(f'version={version}\n')
            for key, path in packages.items():
                stream.write(f'{key}={path}\n')
            stream.write(f'changelog<<{delimiter}\n{changelog}\n{delimiter}\n')
    for path in packages.values():
        print(f'Verified {path}')
    print('Published tag, successful build and test run, release checksums and package versions match')


if __name__ == '__main__':
    main()
