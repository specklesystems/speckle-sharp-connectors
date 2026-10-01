#!/usr/bin/env bash
set -euo pipefail

# clean-locks must be able to recover from a broken Build/packages.lock.json, so it restores unlocked
locked_mode=true
for arg in "$@"; do
  if [[ "$arg" == "clean-locks" ]]; then
    locked_mode=false
  fi
done

if [[ "$locked_mode" == false ]]; then
  rm -f Build/packages.lock.json
fi

dotnet run --project Build/Build.csproj --property:RestoreLockedMode=$locked_mode -- "$@"
