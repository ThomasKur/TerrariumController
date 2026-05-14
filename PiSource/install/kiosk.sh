#!/bin/bash
set -e

URL="${1:-http://localhost:5000}"

find_chromium_bin() {
	local candidate

	for candidate in \
		"/usr/bin/chromium" \
		"/usr/bin/chromium-browser" \
		"$(command -v chromium 2>/dev/null || true)" \
		"$(command -v chromium-browser 2>/dev/null || true)" \
		"$(command -v google-chrome 2>/dev/null || true)" \
		"$(command -v google-chrome-stable 2>/dev/null || true)"; do
		if [ -n "$candidate" ] && [ -x "$candidate" ]; then
			echo "$candidate"
			return 0
		fi
	done

	return 1
}

if CHROMIUM_BIN="$(find_chromium_bin)"; then
	echo "Using browser binary: $CHROMIUM_BIN"
else
	echo "Chromium/Chrome not found. Install with: sudo apt install chromium-browser"
	exit 1
fi

exec "$CHROMIUM_BIN" \
	--kiosk \
	--new-window \
	--no-first-run \
	--no-default-browser-check \
	--password-store=basic \
	--disable-infobars \
	"$URL"
