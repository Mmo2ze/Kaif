#!/usr/bin/env bash
# Launch Store POS on macOS (published build). Starts StoreAPI automatically on port 5050.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
TFM="net10.0-maccatalyst"
CONFIGURATION="${CONFIGURATION:-Release}"
ARCH="$(uname -m)"
if [[ "$ARCH" == "arm64" ]]; then
  RID="maccatalyst-arm64"
else
  RID="maccatalyst-x64"
fi

find_app() {
  local dir="$1"
  local app="$dir/Store POS.app"
  if [[ -d "$app" ]]; then
    printf '%s\n' "$app"
    return 0
  fi
  return 1
}

resolve_app() {
  local candidates=()

  if [[ -n "${STORE_POS_APP:-}" ]]; then
    candidates+=("$STORE_POS_APP")
  fi

  if [[ $# -gt 0 && -n "${1:-}" ]]; then
    candidates+=("$1")
  fi

  candidates+=(
    "$ROOT/dist/StorePOS-macOS/Store POS.app"
    "$ROOT/StorePOS/bin/$CONFIGURATION/$TFM/$RID/publish/Store POS.app"
    "$ROOT/StorePOS/bin/$CONFIGURATION/$TFM/$RID/Store POS.app"
    "$ROOT/StorePOS/bin/Debug/$TFM/$RID/publish/Store POS.app"
  )

  local path
  for path in "${candidates[@]}"; do
    if [[ -d "$path" ]]; then
      printf '%s\n' "$path"
      return 0
    fi
  done
  return 1
}

APP="$(resolve_app "${1:-}")" || {
  echo "Store POS.app not found." >&2
  echo >&2
  echo "Publish first:" >&2
  echo "  ./scripts/publish-macos.sh" >&2
  echo >&2
  echo "Or point to the app bundle:" >&2
  echo "  ./scripts/start-macos.sh \"/path/to/Store POS.app\"" >&2
  echo "  STORE_POS_APP=\"/path/to/Store POS.app\" ./scripts/start-macos.sh" >&2
  exit 1
}

if pgrep -x "Store POS" >/dev/null 2>&1; then
  echo "Store POS is already running."
  echo "Quit it from the menu bar (Store POS → Quit) before starting again."
  exit 0
fi

echo "Starting Store POS..."
echo "  $APP"
open "$APP"
echo
echo "StoreAPI will start on port 5050 when the app opens."
echo "LAN web app: http://$(ipconfig getifaddr en0 2>/dev/null || hostname):5050"
