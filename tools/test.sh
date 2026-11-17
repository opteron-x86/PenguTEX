#!/usr/bin/env bash
set -euo pipefail
source "$(dirname -- "${BASH_SOURCE[0]}")/env.sh"
python3 tools/build-native.py
"$DOTNET" test PenguTEX.slnx -c Release --disable-build-servers -m:1 -nr:false -p:UseSharedCompilation=false --logger 'trx;LogFilePrefix=tests' --results-directory .artifacts/tests "$@"
