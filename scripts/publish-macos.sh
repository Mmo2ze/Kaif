#!/usr/bin/env bash
# One-shot publish: React web + StoreAPI + Store POS.app (Mac Catalyst).
# Open Store POS.app from the output folder — it starts StoreAPI (and the LAN web app) automatically.
set -euo pipefail

CONFIGURATION="${1:-Release}"
SKIP_WEB_BUILD="${SKIP_WEB_BUILD:-0}"

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
TFM="net10.0-maccatalyst"
ARCH="$(uname -m)"
if [[ "$ARCH" == "arm64" ]]; then
  RID="maccatalyst-arm64"
else
  RID="maccatalyst-x64"
fi

STORE_WEB="$ROOT/StoreWeb"
PUBLISH_OUT="$ROOT/StorePOS/bin/$CONFIGURATION/$TFM/$RID/publish"
DIST_OUT="$ROOT/dist/StorePOS-macOS"

test_publish_output() {
  local folder="$1"
  local app="$folder/Store POS.app"
  local missing=()
  [[ -d "$app" ]] || missing+=("Store POS.app")
  [[ -f "$app/Contents/MacOS/StoreAPI" ]] || missing+=("Store POS.app/Contents/MacOS/StoreAPI")
  [[ -f "$app/Contents/MacOS/browserwww/index.html" ]] || missing+=("Store POS.app/Contents/MacOS/browserwww/index.html")
  if ((${#missing[@]} > 0)); then
    echo "Publish folder is incomplete. Missing: ${missing[*]}" >&2
    exit 1
  fi
}

echo "=== Kaif Store - macOS publish ($RID) ==="
echo

if pgrep -x "Store POS" >/dev/null 2>&1 || pgrep -f "StoreAPI" >/dev/null 2>&1; then
  echo "WARNING: Store POS or StoreAPI may still be running. Quit them first or the build may fail."
  echo
fi

if [[ "$SKIP_WEB_BUILD" != "1" ]]; then
  echo "[1/3] Building React web app (StoreWeb)..."
  if [[ ! -f "$STORE_WEB/package.json" ]]; then
    echo "StoreWeb/package.json not found." >&2
    exit 1
  fi
  pushd "$STORE_WEB" >/dev/null
  if [[ -f package-lock.json ]]; then
    npm ci
  else
    npm install
  fi
  npm run build
  if [[ ! -f dist/index.html ]]; then
    echo "StoreWeb build did not produce dist/index.html" >&2
    exit 1
  fi
  popd >/dev/null
  echo "      Web build OK."
else
  echo "[1/3] Skipping web build (SKIP_WEB_BUILD=1)."
  if [[ ! -f "$STORE_WEB/dist/index.html" ]]; then
    echo "No StoreWeb/dist/index.html — run without SKIP_WEB_BUILD=1 first." >&2
    exit 1
  fi
fi

echo "[2/3] Publishing Store POS + StoreAPI (self-contained, $RID)..."
dotnet publish "$ROOT/StorePOS/StorePOS.csproj" \
  -f "$TFM" \
  -c "$CONFIGURATION" \
  -r "$RID" \
  --self-contained true \
  -p:BuildReact=false

test_publish_output "$PUBLISH_OUT"

echo "[3/3] Copying to dist/StorePOS-macOS..."
rm -rf "$DIST_OUT"
mkdir -p "$DIST_OUT"
cp -R "$PUBLISH_OUT/"* "$DIST_OUT/"
chmod +x "$DIST_OUT/RunStore.sh" 2>/dev/null || true

test_publish_output "$DIST_OUT"

echo
echo "=== Done ==="
echo
echo "  Portable folder (recommended):"
echo "    $DIST_OUT"
echo
echo "  Build output (same files):"
echo "    $PUBLISH_OUT"
echo
echo "How to run:"
echo "  1. Open the folder above"
echo "  2. Double-click Store POS.app (or: ./RunStore.sh)"
echo "     -> StoreAPI starts automatically on port 5050"
echo "     -> LAN web app: http://<this-Mac-IP>:5050"
echo
echo "Note: Native receipt/barcode printing uses the browser print dialog on macOS."
echo "      Windows builds keep direct thermal printer support."
