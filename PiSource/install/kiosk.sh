#!/bin/bash
set -e

URL="${1:-http://localhost:5000}"

if [ -x "/usr/bin/chromium" ]; then
	CHROMIUM_BIN="/usr/bin/chromium"
elif [ -x "/usr/bin/chromium-browser" ]; then
	CHROMIUM_BIN="/usr/bin/chromium-browser"
else
	echo "Chromium not found. Install with: sudo apt install chromium-browser"
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
