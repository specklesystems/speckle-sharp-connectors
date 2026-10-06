$ErrorActionPreference = "Stop";

# clean-locks must be able to recover from a broken Build/packages.lock.json, so it restores unlocked
$lockedMode = "true"
if ($args -contains "clean-locks") {
  $lockedMode = "false"
  Remove-Item -Force -ErrorAction SilentlyContinue build/packages.lock.json
}

dotnet run --project build/build.csproj --property:RestoreLockedMode=$lockedMode -- $args