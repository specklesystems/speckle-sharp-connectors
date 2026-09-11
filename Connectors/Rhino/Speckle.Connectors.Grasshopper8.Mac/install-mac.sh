#!/usr/bin/env bash
# Builds Speckle.Connectors.Grasshopper8.Mac and installs it into Grasshopper's macOS Libraries folder.
# See ../README-mac.md for prerequisites.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJ="$SCRIPT_DIR/Speckle.Connectors.Grasshopper8.Mac.csproj"
# b45a29b1-4343-4035-989e-044e8580d9cf is Grasshopper's own plug-in id, not user- or install-specific —
# stable across every Rhino 8 Mac install (rhino-mac-connector spec, ticket 02/09).
DEST="$HOME/Library/Application Support/McNeel/Rhinoceros/8.0/Plug-ins/Grasshopper (b45a29b1-4343-4035-989e-044e8580d9cf)/Libraries/Speckle.Connectors.Grasshopper8.Mac"
CONFIG="${1:-Release}"

dotnet build "$PROJ" -c "$CONFIG"

OUT="$SCRIPT_DIR/bin/$CONFIG/net8.0"
rm -rf "$DEST"
mkdir -p "$DEST"
cp -R "$OUT"/. "$DEST"/

echo
echo "Installed to: $DEST"
echo "gha: $(ls "$DEST"/*.gha)"
echo "Natives:"
find "$DEST/runtimes" -path '*osx*' -name '*.dylib' 2>/dev/null || echo "  NONE (build problem — see README-mac.md)"
echo "Stray WinForms/Drawing dlls (should be none — McNeel supplies these on Mac):"
ls "$DEST" | grep -iE "System.Windows.Forms|System.Drawing.Common" || echo "  none"
echo
echo "Quit and restart Rhino 8 → Grasshopper → Speckle tab."
