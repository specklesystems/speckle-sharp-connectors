#!/usr/bin/env bash
# Builds Speckle.Connectors.Rhino8.Mac and installs it into Rhino 8's macOS plug-ins folder.
# See ../README-mac.md for prerequisites and the one-time load step (Rhino Mac has no Install UI).
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJ="$SCRIPT_DIR/Speckle.Connectors.Rhino8.Mac.csproj"
DEST="$HOME/Library/Application Support/McNeel/Rhinoceros/8.0/Plug-ins/Speckle.Connectors.Rhino8.Mac"
CONFIG="${1:-Release}"

dotnet build "$PROJ" -c "$CONFIG"

OUT="$SCRIPT_DIR/bin/$CONFIG/net8.0"
rm -rf "$DEST"
mkdir -p "$DEST"
cp -R "$OUT"/. "$DEST"/

echo
echo "Installed: $DEST/Speckle.Connectors.Rhino8.Mac.rhp"
echo "Natives:"
find "$DEST/runtimes" -path '*osx*' -name '*.dylib' 2>/dev/null || echo "  NONE (build problem — see README-mac.md)"
echo
echo "Rhino 8 Mac has no plug-in Install button. One-time load, from Rhino's ScriptEditor (Python):"
echo "  import Rhino; print(Rhino.PlugIns.PlugIn.LoadPlugIn(r'$DEST/Speckle.Connectors.Rhino8.Mac.rhp'))"
echo "Then run the command: Speckle"
