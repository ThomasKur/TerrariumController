#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/../.." && pwd)"
app_dir="/opt/terrarium"

sudo mkdir -p "$app_dir"
sudo cp -R "$repo_root/publish/." "$app_dir/"
sudo cp "$script_dir/terrarium.service" /etc/systemd/system/terrarium.service
sudo systemctl daemon-reload
sudo systemctl enable --now terrarium.service