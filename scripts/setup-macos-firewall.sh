#!/usr/bin/env bash
# Allow inbound TCP 5050 for StoreAPI (LAN web dashboard + phones on Wi‑Fi).
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
APP="${STORE_POS_APP:-$ROOT/dist/StorePOS-macOS/Store POS.app}"
API="$APP/Contents/MacOS/StoreAPI"

if [[ ! -f "$API" ]]; then
  echo "StoreAPI not found at: $API" >&2
  echo "Run ./scripts/publish-macos.sh first, or set STORE_POS_APP." >&2
  exit 1
fi

if ! /usr/libexec/ApplicationFirewall/socketfilterfw --getglobalstate 2>/dev/null | grep -q "enabled"; then
  echo "macOS firewall is off — LAN access should already work if the phone is on the same Wi‑Fi."
  echo "If mobile still cannot connect, check router guest/AP isolation (not a Mac firewall issue)."
  exit 0
fi

echo "Adding StoreAPI to macOS firewall (requires sudo)…"
sudo /usr/libexec/ApplicationFirewall/socketfilterfw --add "$API"
sudo /usr/libexec/ApplicationFirewall/socketfilterfw --unblockapp "$API"
echo "Done. StoreAPI can accept LAN connections on port 5050."
