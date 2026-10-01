#!/usr/bin/env bash
set -euo pipefail
PROJECT_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$PROJECT_ROOT"
MODE="${1:-run}"
case "$MODE" in run|--debug|--logs|--telemetry|--verify) ;; *) echo "Usage: $0 [--debug|--logs|--telemetry|--verify]" >&2; exit 2;; esac
pkill -x Parvathi >/dev/null 2>&1 || true
./scripts/package.sh debug
APP="$PROJECT_ROOT/dist/Parvathi.app"
case "$MODE" in
  run) open -n "$APP" ;;
  --debug) lldb -- "$APP/Contents/MacOS/Parvathi" ;;
  --logs) open -n "$APP"; /usr/bin/log stream --level info --style compact --predicate 'process == "Parvathi"' ;;
  --telemetry) open -n "$APP"; /usr/bin/log stream --level info --style compact --predicate 'subsystem == "dev.parvathi.assistant"' ;;
  --verify) open -n "$APP"; sleep 2; pgrep -x Parvathi >/dev/null; echo 'Parvathi launched.' ;;
esac
