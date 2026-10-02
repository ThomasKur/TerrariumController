#!/usr/bin/env bash
set -euo pipefail

CAMERA_WIDTH="${CAMERA_WIDTH:-1920}"
CAMERA_HEIGHT="${CAMERA_HEIGHT:-1080}"
CAMERA_FPS="${CAMERA_FPS:-15}"
CAMERA_PORT="${CAMERA_PORT:-8080}"

rpicam-vid \
  --nopreview \
  --inline \
  --codec mjpeg \
  --width "${CAMERA_WIDTH}" \
  --height "${CAMERA_HEIGHT}" \
  --framerate "${CAMERA_FPS}" \
  --timeout 0 \
  --output - \
| ffmpeg -hide_banner -loglevel error \
  -f mjpeg \
  -i - \
  -c copy \
  -f mpjpeg \
  -listen 1 "http://0.0.0.0:${CAMERA_PORT}/"
