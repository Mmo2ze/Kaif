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
MAC_BUILD="$ROOT/StorePOS/bin/$CONFIGURATION/$TFM/$RID"
PUBLISH_OUT="$MAC_BUILD/publish"
DIST_OUT="$ROOT/dist/StorePOS-macOS"

clear_mac_build() {
  if [[ ! -e "$MAC_BUILD" ]]; then
    return 0
  fi
  if rm -rf "$MAC_BUILD" 2>/dev/null; then
    return 0
  fi
  echo "Cannot clear old Mac Catalyst build output (often root-owned after sudo):" >&2
  echo "  sudo chown -R \$(whoami) \"$MAC_BUILD\"" >&2
  echo "  rm -rf \"$MAC_BUILD\"" >&2
  echo "Then run ./scripts/publish-macos.sh again." >&2
  exit 1
}

clear_dist_out() {
  local stage="${DIST_OUT}.staging.$$"
  local backup="${DIST_OUT}.old.$$"

  rm -rf "$stage"
  mkdir -p "$stage"
  cp -R "$PUBLISH_OUT/"* "$stage/"
  chmod +x "$stage/RunStore.sh" 2>/dev/null || true

  rm -rf "$backup"
  if [[ -e "$DIST_OUT" ]]; then
    if mv "$DIST_OUT" "$backup" 2>/dev/null; then
      :
    elif rm -rf "$DIST_OUT" 2>/dev/null; then
      :
    else
      chmod -R u+w "$DIST_OUT" 2>/dev/null || true
      chflags -R nouchg "$DIST_OUT" 2>/dev/null || true
      if ! rm -rf "$DIST_OUT" 2>/dev/null; then
        rm -rf "$stage"
        echo "Cannot replace $DIST_OUT (Store POS may still be running)." >&2
        echo "  Quit Store POS completely, then run:" >&2
        echo "  rm -rf \"$DIST_OUT\"" >&2
        echo "  ./scripts/publish-macos.sh" >&2
        exit 1
      fi
    fi
  fi

  mv "$stage" "$DIST_OUT"
  rm -rf "$backup" &
}

test_publish_output() {
  local folder="$1"
  local app="$folder/Store POS.app"
  local missing=()
  [[ -d "$app" ]] || missing+=("Store POS.app")
  [[ -f "$app/Contents/MacOS/StorePOS" ]] || missing+=("Store POS.app/Contents/MacOS/StorePOS")
  [[ -f "$app/Contents/MacOS/StoreAPI" ]] || missing+=("Store POS.app/Contents/MacOS/StoreAPI")
  [[ -f "$app/Contents/MacOS/browserwww/index.html" ]] || missing+=("Store POS.app/Contents/MacOS/browserwww/index.html")
  if ((${#missing[@]} > 0)); then
    echo "Publish folder is incomplete. Missing: ${missing[*]}" >&2
    exit 1
  fi
}

echo "=== Kaif Store - macOS publish ($RID) ==="
echo

if pgrep -x "Store POS" >/dev/null 2>&1 || pgrep -x "StoreAPI" >/dev/null 2>&1; then
  echo "WARNING: Store POS or StoreAPI is still running."
  echo "         Quit Store POS completely before publishing, or the old server may keep serving on port 5050."
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
clear_mac_build
dotnet publish "$ROOT/StorePOS/StorePOS.csproj" \
  -f "$TFM" \
  -c "$CONFIGURATION" \
  -r "$RID" \
  --self-contained true \
  -p:BuildReact=false

test_publish_output "$PUBLISH_OUT"

echo "[3/3] Copying to dist/StorePOS-macOS..."
clear_dist_out

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
echo "  ./scripts/start-macos.sh"
echo "  Or open the folder and double-click Store POS.app (or RunStore.sh in dist/)"
echo "     -> StoreAPI starts automatically on port 5050"
echo "     -> LAN web app: http://<this-Mac-IP>:5050"
echo
echo "Note: Native barcode/receipt printing uses ESC/POS raw on macOS (same thermal path as Windows)."

install_desktop_alias() {
  local app="$DIST_OUT/Store POS.app"
  [[ -d "$app" ]] || return 0
  local desktop="$HOME/Desktop"
  local label="Kaif Store POS.app"
  rm -f "$desktop/$label" 2>/dev/null || true
  osascript <<EOF 2>/dev/null || ln -sf "$app" "$desktop/$label"
tell application "Finder"
  set targetApp to POSIX file "$app"
  set desktopFolder to POSIX file "$desktop"
  make alias file to targetApp at desktopFolder with properties {name:"$label"}
end tell
EOF
  echo "Desktop: $desktop/$label"
}

install_desktop_alias
