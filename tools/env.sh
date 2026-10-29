#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname -- "${BASH_SOURCE[0]}")/.."
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
export DOTNET_CLI_HOME="${PENGUTEX_CLI_HOME:-$PWD/.tools/cli-home}"
DOTNET="${PENGUTEX_DOTNET:-dotnet}"
