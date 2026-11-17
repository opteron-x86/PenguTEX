#!/usr/bin/env python3
"""Read the single source of application version and validate release tags."""
import argparse
import pathlib
import xml.etree.ElementTree as ET

parser = argparse.ArgumentParser()
parser.add_argument('--check-tag')
args = parser.parse_args()
root = pathlib.Path(__file__).resolve().parents[1]
version = ET.parse(root / 'Directory.Build.props').findtext('.//Version')
if not version:
    raise SystemExit('Missing application Version')
metainfo = ET.parse(root / 'packaging/io.github.opteron_x86.PenguTEX.metainfo.xml').find('.//releases/release')
if metainfo is None or metainfo.get('version') != version:
    raise SystemExit(f'AppStream metainfo release {None if metainfo is None else metainfo.get("version")} does not match {version}; '
                     'add a <release> entry to packaging/io.github.opteron_x86.PenguTEX.metainfo.xml')
if args.check_tag and f'## [{version}]' not in (root / 'CHANGELOG.md').read_text(encoding='utf-8'):
    raise SystemExit(f'CHANGELOG.md needs a "## [{version}] - YYYY-MM-DD" section before tagging')
if args.check_tag and args.check_tag != 'v' + version:
    raise SystemExit(f'Tag {args.check_tag} does not match v{version}')
print(version)
