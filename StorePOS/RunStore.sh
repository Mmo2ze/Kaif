#!/usr/bin/env bash
set -euo pipefail
DIR="$(cd "$(dirname "$0")" && pwd)"
APP="$(find "$DIR" -maxdepth 1 -name '*.app' -print -quit)"
if [[ -n "$APP" ]]; then
  open "$APP"
else
  echo "Store POS.app not found in $DIR"
  echo "Run scripts/publish-macos.sh first."
  exit 1
fi
