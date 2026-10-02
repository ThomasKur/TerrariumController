#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/../.." && pwd)"
app_dir="/opt/terrarium"
camera_script="$app_dir/camera.sh"

if [[ -d "$script_dir/app" ]]; then
	app_source_dir="$script_dir/app"
elif [[ -d "$repo_root/publish" ]]; then
	app_source_dir="$repo_root/publish"
else
	echo "No application payload found. Build first or use the packaged artifact containing app/." >&2
	exit 1
fi

sudo apt-get update
sudo apt-get install -y ffmpeg rpicam-apps

sudo mkdir -p "$app_dir"
sudo cp -R "$app_source_dir/." "$app_dir/"
sudo cp "$script_dir/camera.sh" "$camera_script"
sudo chmod +x "$camera_script"

sudo cp "$script_dir/terrarium.service" /etc/systemd/system/terrarium.service
sudo cp "$script_dir/terrarium-camera.service" /etc/systemd/system/terrarium-camera.service

sudo systemctl daemon-reload
sudo systemctl enable --now terrarium-camera.service
sudo systemctl enable --now terrarium.service