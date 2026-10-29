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
if args.check_tag and args.check_tag != 'v' + version:
    raise SystemExit(f'Tag {args.check_tag} does not match v{version}')
print(version)
