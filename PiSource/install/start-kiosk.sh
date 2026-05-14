#!/bin/bash
set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
URL="${1:-http://localhost:5000}"
WAIT_SECONDS="${KIOSK_WAIT_SECONDS:-60}"
LOG_FILE="${KIOSK_LOG_FILE:-/tmp/terrarium-kiosk.log}"

# Ensure a desktop session context exists when launched from startup scripts.
if [ -z "${DISPLAY:-}" ]; then
    export DISPLAY=:0
fi

if [ -z "${XDG_RUNTIME_DIR:-}" ]; then
    export XDG_RUNTIME_DIR="/run/user/$(id -u)"
fi

echo "[$(date -Is)] start-kiosk.sh invoked (url=$URL wait=${WAIT_SECONDS}s display=${DISPLAY:-unset})" >> "$LOG_FILE"

is_url_ready() {
    local target_url="$1"

    if command -v curl >/dev/null 2>&1; then
        curl -fsS --max-time 2 "$target_url" >/dev/null 2>&1
        return $?
    fi

    if command -v wget >/dev/null 2>&1; then
        wget -q --spider --timeout=2 "$target_url" >/dev/null 2>&1
        return $?
    fi

    return 1
}

if [ "$WAIT_SECONDS" -gt 0 ]; then
    for ((i = 1; i <= WAIT_SECONDS; i++)); do
        if is_url_ready "$URL"; then
            echo "[$(date -Is)] URL became ready after ${i}s: $URL" >> "$LOG_FILE"
            break
        fi

        sleep 1
    done
fi

exec "$SCRIPT_DIR/kiosk.sh" "$URL" >> "$LOG_FILE" 2>&1
