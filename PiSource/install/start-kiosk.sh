#!/bin/bash
set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
URL="${1:-http://localhost:5000}"
WAIT_SECONDS="${KIOSK_WAIT_SECONDS:-60}"

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
            break
        fi

        sleep 1
    done
fi

exec "$SCRIPT_DIR/kiosk.sh" "$URL"
