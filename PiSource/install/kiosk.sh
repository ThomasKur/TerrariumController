#!/bin/bash
# Chromium Kiosk Launcher for Terrarium Controller
# This script launches Chromium in kiosk mode for the web app

DISPLAY=:0
export DISPLAY

# Wait for network and app to be ready
sleep 5

# Kill any existing Chromium instances
killall chromium chromium-browser || true

# Launch Chromium in kiosk mode
/usr/bin/chromium \
    --kiosk \
    --no-first-run \
    --disable-background-networking \
    --disable-client-side-phishing-detection \
    --disable-default-apps \
    --disable-hang-monitor \
    --disable-popup-blocking \
    --disable-prompt-on-repost \
    --disable-sync \
    --enable-automation \
    --no-default-browser-check \
    --autoplay-policy=user-gesture-required \
    http://localhost:5000

# Restart if it closes
while true; do
    sleep 1
done
