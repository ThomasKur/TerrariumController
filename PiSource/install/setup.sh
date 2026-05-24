#!/bin/bash
# Raspberry Pi Terrarium Controller Setup Script
# This script MUST be run on Raspberry Pi OS with sudo
# Usage: sudo bash setup.sh
#
# Do NOT run this on non-Raspberry Pi systems - it will modify system configuration

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

echo "=== Terrarium Controller Setup ===" 

# Check if running on Raspberry Pi OS or ARM Debian (Raspberry Pi OS is Debian-based)
if [ ! -f /etc/os-release ]; then
    echo -e "${RED}Error: Cannot detect OS${NC}"
    exit 1
fi

# Check for ARM architecture (Raspberry Pi indicator) - supports both 32-bit (arm) and 64-bit (aarch64)
ARCH=$(uname -m)
if ! echo "$ARCH" | grep -qE "arm|aarch64"; then
    echo -e "${RED}Error: This script is designed for ARM-based Raspberry Pi systems only${NC}"
    echo "Detected architecture: $ARCH"
    echo "Detected OS: $(grep PRETTY_NAME /etc/os-release)"
    echo "Aborting to prevent system damage on non-Pi systems."
    exit 1
fi

echo "Detected ARM-based system ($ARCH) - proceeding with setup..."

# Color output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

contains_user() {
    local candidate="$1"
    shift

    for existing in "$@"; do
        if [ "$existing" = "$candidate" ]; then
            return 0
        fi
    done

    return 1
}

print_readyz_details() {
    local readyz_output="$1"

    if [ -z "$readyz_output" ]; then
        echo "  (no readiness payload returned)"
        return
    fi

    if command -v python3 >/dev/null 2>&1; then
        READYZ_OUTPUT="$readyz_output" python3 - <<'PY'
import json
import os

payload = os.environ.get("READYZ_OUTPUT", "")
try:
    data = json.loads(payload)
except Exception:
    print(payload)
    raise SystemExit(0)

print(f"  ready: {'yes' if data.get('isReady') else 'no'}")
print(f"  databaseReady: {data.get('databaseReady')}")
print(f"  gpioReady: {data.get('gpioReady')}")
print(f"  controlLoopStarted: {data.get('controlLoopStarted')}")
print(f"  lastSuccessfulCycleUtc: {data.get('lastSuccessfulCycleUtc')}")
print(f"  lastCycleStatus: {data.get('lastCycleStatus')}")
print(f"  snapshotUtc: {data.get('snapshotUtc')}")
PY
        return
    fi

    echo "$readyz_output" | sed 's/[{}]//g; s/,/\
/g; s/"//g; s/^/  /'
}

build_target_users() {
    TARGET_USERS=("pi" "terrarium")
    local active_user="${SUDO_USER:-$(logname 2>/dev/null || true)}"

    if [ -n "$active_user" ] && [ "$active_user" != "root" ]; then
        if ! contains_user "$active_user" "${TARGET_USERS[@]}"; then
            TARGET_USERS+=("$active_user")
        fi
    fi
}

# Check if running as root
if [ "$EUID" -ne 0 ]; then 
    echo -e "${RED}Error: This script must be run as root (sudo)${NC}"
    exit 1
fi

# Update system (once per day)
echo "Checking system packages..."
LAST_UPDATE_FILE="/var/lib/apt/periodic/update-success-timestamp"
CURRENT_TIME=$(date +%s)
LAST_UPDATE_TIME=0

if [ -f "$LAST_UPDATE_FILE" ]; then
    # Prefer GNU stat on Linux; fall back to GNU date if needed
    LAST_UPDATE_TIME=$(stat -c "%Y" "$LAST_UPDATE_FILE" 2>/dev/null || date -r "$LAST_UPDATE_FILE" +%s 2>/dev/null || echo 0)
fi

SECONDS_PER_DAY=86400
SECONDS_SINCE_UPDATE=$((CURRENT_TIME - LAST_UPDATE_TIME))

if [ "$LAST_UPDATE_TIME" -eq 0 ] || [ "$SECONDS_SINCE_UPDATE" -ge "$SECONDS_PER_DAY" ]; then
    echo "Running system package update..."
    apt update
    apt upgrade -y
    # Mark last update time (ignore errors if apt manages this file)
    touch "$LAST_UPDATE_FILE" 2>/dev/null || true
else
    HOURS_UNTIL_NEXT=$(( (SECONDS_PER_DAY - SECONDS_SINCE_UPDATE) / 3600 ))
    echo "System was updated less than 24 hours ago (next update in ~${HOURS_UNTIL_NEXT}h), skipping..."
fi

# Stop existing service if present to avoid conflicts during redeploy
if systemctl list-unit-files | grep -q '^terrarium.service'; then
    echo "Stopping terrarium service before redeploy..."
    systemctl stop terrarium 2>/dev/null || true
fi

# .NET runtime installation removed — default deployment uses self-contained binaries.
# If you choose framework-dependent deployment, ensure the ASP.NET Core runtime 10.x
# is installed manually (apt install aspnetcore-runtime-10.0) before starting the service.

# Create user and directory for the app
echo "Creating terrarium user and directories..."
useradd -m -s /bin/bash terrarium || true
mkdir -p /opt/terrarium
mkdir -p /opt/terrarium/logs
mkdir -p /opt/terrarium-hw-sidecar
chown terrarium:terrarium /opt/terrarium
chown terrarium:terrarium /opt/terrarium/logs
chown terrarium:terrarium /opt/terrarium-hw-sidecar
chmod 755 /opt/terrarium
chmod 2775 /opt/terrarium/logs
chmod 755 /opt/terrarium-hw-sidecar

echo "Creating service environment file..."
mkdir -p /etc/terrarium
ENV_FILE="/etc/terrarium/terrarium.env"

if [ -f "$ENV_FILE" ]; then
    DELETE_ENV_FILE=false

    if [ -t 0 ]; then
        read -r -p "Environment file exists at $ENV_FILE. Delete and recreate it? [y/N]: " DELETE_ENV_INPUT
        case "$DELETE_ENV_INPUT" in
            [yY]|[yY][eE][sS])
                DELETE_ENV_FILE=true
                ;;
        esac
    else
        echo "Non-interactive setup detected; keeping existing $ENV_FILE"
    fi

    if [ "$DELETE_ENV_FILE" = true ]; then
        rm -f "$ENV_FILE"
        echo "Deleted $ENV_FILE"
    fi
fi

if [ ! -f "$ENV_FILE" ]; then
    cat > "$ENV_FILE" << 'EOF'
# Terrarium Controller environment configuration
ASPNETCORE_URLS=http://0.0.0.0:5000
ASPNETCORE_ENVIRONMENT=Production
HardwareSidecar__Mode=Embedded
CAMERA_WIDTH=1920
CAMERA_HEIGHT=1080
CAMERA_FPS=15
CAMERA_STREAM_PORT=5001
EOF
    echo "Created $ENV_FILE"
else
    echo "Keeping existing $ENV_FILE"
fi

if ! grep -q '^CAMERA_STREAM_PORT=' "$ENV_FILE"; then
    echo 'CAMERA_STREAM_PORT=5001' >> "$ENV_FILE"
    echo "Added CAMERA_STREAM_PORT=5001 to $ENV_FILE"
fi

if ! grep -q '^HardwareSidecar__Mode=' "$ENV_FILE"; then
    echo 'HardwareSidecar__Mode=Embedded' >> "$ENV_FILE"
    echo "Added HardwareSidecar__Mode=Embedded to $ENV_FILE"
fi

# Create app launcher script to handle self-contained or framework-dependent deployments
echo "Creating app launcher script..."
cat > /opt/terrarium/run.sh << 'EOF'
#!/bin/bash
set -e

# Require self-contained binary
if [ -x "/opt/terrarium/TerrariumController" ]; then
    exec /opt/terrarium/TerrariumController
else
    echo "Self-contained binary not found at /opt/terrarium/TerrariumController" >&2
    echo "Rebuild with self-contained publish (linux-arm64) and rerun setup." >&2
    exit 1
fi
EOF
chown terrarium:terrarium /opt/terrarium/run.sh
chmod +x /opt/terrarium/run.sh

# Copy kiosk launcher scripts to /opt/terrarium
echo "Deploying kiosk launcher scripts..."

# Remove old wrapper script if it exists
if [ -f "/opt/terrarium/start-kiosk-desktop.sh" ]; then
    echo "Removing old wrapper script..."
    rm -f /opt/terrarium/start-kiosk-desktop.sh
fi

if [ ! -f "$SCRIPT_DIR/start-kiosk.sh" ]; then
    echo -e "${YELLOW}Warning: start-kiosk.sh not found in $SCRIPT_DIR${NC}"
else
    cp "$SCRIPT_DIR/start-kiosk.sh" /opt/terrarium/start-kiosk.sh
    chmod +x /opt/terrarium/start-kiosk.sh
    echo "Deployed start-kiosk.sh"
fi

if [ ! -f "$SCRIPT_DIR/kiosk.sh" ]; then
    echo -e "${YELLOW}Warning: kiosk.sh not found in $SCRIPT_DIR${NC}"
else
    cp "$SCRIPT_DIR/kiosk.sh" /opt/terrarium/kiosk.sh
    chmod +x /opt/terrarium/kiosk.sh
    echo "Deployed kiosk.sh"
fi

# Create camera runner script using Python HTTP server for MJPEG
# Modern Raspberry Pi OS (Bookworm+) does not include mjpeg-streamer.
# This script uses rpicam-vid to generate MJPEG and serves it via Python HTTP server.
echo "Creating camera runner script..."
cat > /opt/terrarium/camera.sh << 'EOF'
#!/bin/bash

mkdir -p /opt/terrarium/logs

WIDTH=${CAMERA_WIDTH:-640}
HEIGHT=${CAMERA_HEIGHT:-480}
FPS=${CAMERA_FPS:-15}
STREAM_PORT=${CAMERA_STREAM_PORT:-5001}

LOG_FILE="/opt/terrarium/logs/camera-stream.log"

# Ensure standard paths are available (systemd service may have limited PATH)
export PATH="/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin:$PATH"

{
    echo "=== Camera Service Started at $(date) ==="
    echo "Environment: PATH=$PATH"
    echo "USER=$(whoami), UID=$(id -u)"
    
    # Check for required tools
    if ! command -v rpicam-vid >/dev/null 2>&1; then
        echo "ERROR: rpicam-vid not found. Install with: sudo apt install rpicam-apps"
        exit 1
    fi
    
    if ! command -v python3 >/dev/null 2>&1; then
        echo "ERROR: python3 not found. Install with: sudo apt install python3"
        exit 1
    fi
    
    echo "rpicam-vid found at: $(command -v rpicam-vid)"
    echo "python3 found at: $(command -v python3)"
    echo "Starting camera stream on port $STREAM_PORT"
    
    # Create Python HTTP server wrapper for MJPEG streaming
    # This reads MJPEG from rpicam-vid and serves it via HTTP
    python3 << 'PYTHON'
import os
import sys
import socket
import subprocess
import threading
from threading import Thread

# Get configuration from environment variables
stream_port = int(os.environ.get('CAMERA_STREAM_PORT', '5001'))
width = int(os.environ.get('CAMERA_WIDTH', '640'))
height = int(os.environ.get('CAMERA_HEIGHT', '480'))
fps = int(os.environ.get('CAMERA_FPS', '15'))
boundary = b'frame'

# Keep latest frame in memory so each client gets complete JPEG parts.
latest_frame = None
latest_lock = threading.Lock()

def run_camera_stream():
    """Start rpicam-vid to generate MJPEG stream"""
    camera_cmd = [
        'rpicam-vid',
        '--codec', 'mjpeg',
        '-t', '0',
        '-n',
        '--width', str(width),
        '--height', str(height),
        '--framerate', str(fps),
        '-o', '-'
    ]
    camera_process = subprocess.Popen(camera_cmd, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    return camera_process

def frame_reader(camera_stream):
    """Parse JPEG frames from rpicam-vid stdout and store latest frame."""
    global latest_frame
    buffer = bytearray()
    while True:
        chunk = camera_stream.stdout.read(8192)
        if not chunk:
            break
        buffer.extend(chunk)

        while True:
            start = buffer.find(b'\xff\xd8')
            if start < 0:
                if len(buffer) > 1048576:
                    del buffer[:-65536]
                break

            end = buffer.find(b'\xff\xd9', start + 2)
            if end < 0:
                if start > 0:
                    del buffer[:start]
                break

            frame = bytes(buffer[start:end + 2])
            del buffer[:end + 2]
            with latest_lock:
                latest_frame = frame

def handle_http_request(client_socket, camera_stream):
    """Handle incoming HTTP request and stream MJPEG data"""
    try:
        # Send HTTP headers for MJPEG stream
        http_header = b'HTTP/1.0 200 OK\r\n'
        http_header += b'Content-Type: multipart/x-mixed-replace; boundary=' + boundary + b'\r\n'
        http_header += b'Connection: close\r\n'
        http_header += b'Expires: Fri, 01 Jan 1999 00:00:00 GMT\r\n'
        http_header += b'Pragma: no-cache\r\n'
        http_header += b'\r\n'
        
        client_socket.sendall(http_header)
        
        # Stream proper multipart JPEG frames.
        while True:
            with latest_lock:
                frame = latest_frame

            if frame is None:
                continue

            part_header = b'--' + boundary + b'\r\n'
            part_header += b'Content-Type: image/jpeg\r\n'
            part_header += b'Content-Length: ' + str(len(frame)).encode('ascii') + b'\r\n\r\n'
            client_socket.sendall(part_header)
            client_socket.sendall(frame)
            client_socket.sendall(b'\r\n')
    except BrokenPipeError:
        # Client disconnected while streaming; this is expected.
        pass
    except ConnectionResetError:
        # Browser/network reset during stream; safe to ignore.
        pass
    except Exception as e:
        print(f"Error: {e}", file=sys.stderr)
    finally:
        client_socket.close()

# Start camera stream process
camera_proc = run_camera_stream()
frame_thread = Thread(target=frame_reader, args=(camera_proc,), daemon=True)
frame_thread.start()

# Create HTTP server socket
server_socket = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
server_socket.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
server_socket.bind(('0.0.0.0', stream_port))
server_socket.listen(5)

print(f"MJPEG server listening on port {stream_port}")
sys.stdout.flush()

try:
    while True:
        client_socket, client_address = server_socket.accept()
        client_handler = Thread(target=handle_http_request, args=(client_socket, camera_proc))
        client_handler.daemon = True
        client_handler.start()
except KeyboardInterrupt:
    print("Shutting down camera service")
finally:
    server_socket.close()
    camera_proc.terminate()
    camera_proc.wait()
PYTHON
} >> "$LOG_FILE" 2>&1
EOF
chown terrarium:terrarium /opt/terrarium/camera.sh
chmod +x /opt/terrarium/camera.sh

# Install GPIO dependencies
echo "Installing GPIO dependencies..."
# Package names differ by distro/release (for example libgpiod2 vs libgpiod3).
RUNTIME_GPIO_PKG=""
for pkg in libgpiod3 libgpiod2 libgpiod; do
    if apt-cache show "$pkg" >/dev/null 2>&1; then
        RUNTIME_GPIO_PKG="$pkg"
        break
    fi
done

if [ -n "$RUNTIME_GPIO_PKG" ]; then
    if apt install -y "$RUNTIME_GPIO_PKG"; then
        echo -e "${GREEN}Installed GPIO runtime package: $RUNTIME_GPIO_PKG${NC}"
    else
        echo -e "${YELLOW}Warning: failed to install GPIO runtime package $RUNTIME_GPIO_PKG${NC}"
    fi
else
    echo -e "${YELLOW}Warning: no libgpiod runtime package found in repositories (trying fallback GPIO driver at runtime)${NC}"
fi

# Optional: headers for native builds/debugging.
if apt-cache show libgpiod-dev >/dev/null 2>&1; then
    if apt install -y libgpiod-dev; then
        echo -e "${GREEN}Installed optional package: libgpiod-dev${NC}"
    else
        echo -e "${YELLOW}Warning: failed to install optional package libgpiod-dev${NC}"
    fi
fi

# Userspace GPIO tools (includes gpiodetect on most distros).
if apt-cache show gpiod >/dev/null 2>&1; then
    if apt install -y gpiod; then
        echo -e "${GREEN}Installed GPIO tools package: gpiod${NC}"
    else
        echo -e "${YELLOW}Warning: failed to install gpiod tools${NC}"
    fi
else
    echo -e "${YELLOW}Warning: gpiod tools package not available in repositories${NC}"
fi

if command -v gpiodetect >/dev/null 2>&1; then
    echo "Detected GPIO chips:"
    gpiodetect || true
else
    echo -e "${YELLOW}Warning: gpiodetect command not found. .NET GPIO may still work via fallback driver.${NC}"
fi

if ! apt install -y python3-gpiozero python3-rpi.gpio; then
    echo -e "${YELLOW}Warning: Python GPIO bindings not available (optional)${NC}"
fi

# Python sidecar prerequisites (optional, only needed when HardwareSidecar.Mode=PythonSidecar).
if ! apt install -y python3-venv python3-pip; then
    echo -e "${YELLOW}Warning: python3-venv/python3-pip installation failed; sidecar mode may not work${NC}"
fi

echo "Deploying Python hardware sidecar files..."
if [ -d "$SCRIPT_DIR/hw-sidecar" ]; then
    cp -R "$SCRIPT_DIR/hw-sidecar"/* /opt/terrarium-hw-sidecar/
    chown -R terrarium:terrarium /opt/terrarium-hw-sidecar
    chmod +x /opt/terrarium-hw-sidecar/run-sidecar.sh 2>/dev/null || true
    echo -e "${GREEN}Python hardware sidecar files deployed to /opt/terrarium-hw-sidecar${NC}"
else
    echo -e "${YELLOW}Warning: $SCRIPT_DIR/hw-sidecar not found; sidecar mode files were not deployed${NC}"
fi

# Install camera streaming tools
echo "Installing Pi camera streaming tools..."
# Install rpicam-apps (python3 is pre-installed on Raspberry Pi OS)
if apt install -y rpicam-apps; then
    echo -e "${GREEN}Camera tools installed${NC}"
else
    echo -e "${YELLOW}Warning: rpicam-apps installation failed${NC}"
    echo -e "${YELLOW}Camera streaming will not work. Install manually: sudo apt install rpicam-apps${NC}"
fi

echo "Installing Chromium browser for kiosk mode (optional)..."
# Try chromium package first (Pi OS Bookworm+), then chromium-browser (older versions)
if apt install -y chromium; then
    echo -e "${GREEN}Chromium installed successfully${NC}"
elif apt install -y chromium-browser; then
    echo -e "${GREEN}Chromium browser installed successfully${NC}"
else
    echo -e "${YELLOW}Warning: Chromium not available in default repositories.${NC}"
    echo -e "${YELLOW}Kiosk autostart will not work. To enable kiosk mode manually:${NC}"
    echo -e "${YELLOW}  1. Install Chromium: sudo apt install chromium${NC}"
    echo -e "${YELLOW}  2. See start-kiosk.sh script for autostart configuration${NC}"
fi

# Verify camera is accessible
if timeout 3 rpicam-hello 2>/dev/null | head -1 | grep -q "Camera"; then
    echo -e "${GREEN}rpicam tools found and camera is accessible${NC}"
else
    echo -e "${YELLOW}Warning: rpicam test inconclusive; verify with: rpicam-hello${NC}"
fi

echo "Test command to verify camera stream:"
echo "  rpicam-vid --codec mjpeg -t 5 --width 640 --height 480 --framerate 15 -o /tmp/test.mjpeg"

# Copy systemd service unit
echo "Installing systemd service..."
if [ ! -f "$SCRIPT_DIR/terrarium.service" ]; then
    echo -e "${RED}Error: terrarium.service not found in $SCRIPT_DIR${NC}"
    exit 1
fi
cp "$SCRIPT_DIR/terrarium.service" /etc/systemd/system/

if [ ! -f "$SCRIPT_DIR/terrarium-camera.service" ]; then
    echo -e "${RED}Error: terrarium-camera.service not found in $SCRIPT_DIR${NC}"
    exit 1
fi
cp "$SCRIPT_DIR/terrarium-camera.service" /etc/systemd/system/

if [ -f "$SCRIPT_DIR/terrarium-hw-sidecar.service" ]; then
    cp "$SCRIPT_DIR/terrarium-hw-sidecar.service" /etc/systemd/system/
else
    echo -e "${YELLOW}Warning: terrarium-hw-sidecar.service not found in $SCRIPT_DIR${NC}"
fi

systemctl daemon-reload
systemctl enable terrarium
systemctl enable terrarium-camera

ENABLE_SIDECAR=false

# Environment variable in /etc/terrarium/terrarium.env takes precedence.
if [ -f "$ENV_FILE" ]; then
    SIDECAR_MODE_ENV=$(grep '^HardwareSidecar__Mode=' "$ENV_FILE" | tail -n 1 | cut -d '=' -f2- | tr -d '[:space:]' || true)
    if [ "$(printf '%s' "$SIDECAR_MODE_ENV" | tr '[:upper:]' '[:lower:]')" = "pythonsidecar" ]; then
        ENABLE_SIDECAR=true
    fi
fi

if [ "$ENABLE_SIDECAR" = false ] && command -v python3 >/dev/null 2>&1 && [ -f "/opt/terrarium/appsettings.json" ]; then
    if python3 - <<'PY'
import json
from pathlib import Path

config_path = Path('/opt/terrarium/appsettings.json')
try:
    data = json.loads(config_path.read_text())
except Exception:
    raise SystemExit(1)

hardware = data.get('HardwareSidecar', {})
mode = str(hardware.get('Mode', '')).strip().lower()
raise SystemExit(0 if mode == 'pythonsidecar' else 1)
PY
    then
        ENABLE_SIDECAR=true
    fi
fi

if [ "$ENABLE_SIDECAR" = true ] && systemctl list-unit-files | grep -q '^terrarium-hw-sidecar.service'; then
    systemctl enable terrarium-hw-sidecar
    echo -e "${GREEN}Python sidecar service enabled (HardwareSidecar mode detected as PythonSidecar)${NC}"
else
    echo "Python sidecar service installed but not enabled. Set HardwareSidecar__Mode=PythonSidecar in /etc/terrarium/terrarium.env (or HardwareSidecar.Mode in appsettings.json) and rerun setup.sh."
fi

# Create kiosk autostart script(s)
echo "Creating Chromium kiosk launcher..."

if [ ! -f "$SCRIPT_DIR/start-kiosk.sh" ]; then
    echo -e "${YELLOW}Skipping kiosk autostart creation because $SCRIPT_DIR/start-kiosk.sh is missing.${NC}"
else

build_target_users

for TARGET_USER in "${TARGET_USERS[@]}"; do
    if ! id "$TARGET_USER" >/dev/null 2>&1; then
        echo -e "${YELLOW}Skipping kiosk autostart for missing user: $TARGET_USER${NC}"
        continue
    fi

    TARGET_HOME="$(getent passwd "$TARGET_USER" | cut -d: -f6)"
    if [ -z "$TARGET_HOME" ]; then
        TARGET_HOME="/home/$TARGET_USER"
    fi

    mkdir -p "$TARGET_HOME/.config/autostart"

    cat > "$TARGET_HOME/.config/autostart/terrarium-kiosk.desktop" << EOF
[Desktop Entry]
Type=Application
Name=Terrarium Kiosk
Exec=env KIOSK_WAIT_SECONDS=90 KIOSK_LOG_FILE=/opt/terrarium/logs/terrarium-kiosk-desktop.log /bin/bash /opt/terrarium/start-kiosk.sh http://localhost:5000
Hidden=false
NoDisplay=false
X-GNOME-Autostart-enabled=true
EOF

    chown "$TARGET_USER:$TARGET_USER" "$TARGET_HOME/.config/autostart/terrarium-kiosk.desktop"
    echo "Configured kiosk autostart for user: $TARGET_USER ($TARGET_HOME)"
done
fi

# Create desktop update and kiosk launcher scripts for local users
echo "Creating desktop update and kiosk launchers..."
build_target_users

for TARGET_USER in "${TARGET_USERS[@]}"; do
    if ! id "$TARGET_USER" >/dev/null 2>&1; then
        echo -e "${YELLOW}Skipping desktop launchers for missing user: $TARGET_USER${NC}"
        continue
    fi

    TARGET_HOME="$(getent passwd "$TARGET_USER" | cut -d: -f6)"
    if [ -z "$TARGET_HOME" ]; then
        TARGET_HOME="/home/$TARGET_USER"
    fi

    DESKTOP_DIR="$TARGET_HOME/Desktop"
    mkdir -p "$DESKTOP_DIR"

    cat > "$DESKTOP_DIR/update.sh" << EOF
#!/bin/bash
set -e

INSTALL_DIR="$SCRIPT_DIR"

if [ "\$EUID" -ne 0 ]; then
    exec sudo -E bash "\$0" "\$@"
fi

if ! git -C "\$INSTALL_DIR" rev-parse --is-inside-work-tree >/dev/null 2>&1; then
    echo "Git repository not found for \$INSTALL_DIR"
    exit 1
fi

REPO_ROOT="\$(git -C "\$INSTALL_DIR" rev-parse --show-toplevel)"
git config --global --add safe.directory "\$REPO_ROOT" >/dev/null 2>&1 || true

echo "Updating repository in \$INSTALL_DIR..."
cd "\$INSTALL_DIR"
git pull --ff-only

echo "Re-running setup.sh..."
bash "\$INSTALL_DIR/setup.sh"
EOF

    cat > "$DESKTOP_DIR/start-kiosk.sh" << EOF
#!/bin/bash
# Desktop launcher for Terrarium Kiosk Mode
# This executable script can be run directly by the desktop manager without prompts
exec env KIOSK_WAIT_SECONDS=15 KIOSK_LOG_FILE=/opt/terrarium/logs/terrarium-kiosk-desktop.log /bin/bash /opt/terrarium/start-kiosk.sh http://localhost:5000
EOF

    chown "$TARGET_USER:$TARGET_USER" "$DESKTOP_DIR/update.sh" "$DESKTOP_DIR/start-kiosk.sh"
    chmod +x "$DESKTOP_DIR/update.sh" "$DESKTOP_DIR/start-kiosk.sh"

    # Remove stale .desktop file from previous setup runs.
    rm -f "$DESKTOP_DIR/start-kiosk.desktop"

    echo "Created desktop launchers for user: $TARGET_USER ($DESKTOP_DIR)"
done

# Set GPIO permissions for non-root access
echo "Configuring GPIO permissions..."
build_target_users

echo "Target users for group membership: ${TARGET_USERS[*]}"

for TARGET_USER in "${TARGET_USERS[@]}"; do
    if id "$TARGET_USER" >/dev/null 2>&1; then
        usermod -a -G terrarium "$TARGET_USER"
        echo "Added $TARGET_USER to terrarium group"
    fi
done

usermod -a -G dialout terrarium
usermod -a -G video terrarium
# Add gpio group if it exists
if getent group gpio > /dev/null; then
    usermod -a -G gpio terrarium
    echo "Added terrarium to gpio group"
else
    echo -e "${YELLOW}Note: gpio group not found, skipping gpio group assignment${NC}"
fi

# Configure firewall if active
echo "Checking firewall configuration..."
FIREWALL_FOUND=false
CAMERA_PORT=5001

if [ -f /etc/terrarium/terrarium.env ]; then
    CAMERA_PORT_FROM_ENV=$(grep -E '^CAMERA_STREAM_PORT=' /etc/terrarium/terrarium.env | tail -n 1 | cut -d'=' -f2 | tr -d '[:space:]')
    if [[ "$CAMERA_PORT_FROM_ENV" =~ ^[0-9]+$ ]]; then
        CAMERA_PORT="$CAMERA_PORT_FROM_ENV"
    fi
fi

# Check UFW (Ubuntu/Debian)
if command -v ufw >/dev/null 2>&1; then
    if ufw status | grep -q "Status: active"; then
        echo "UFW firewall is active, opening ports 5000 and $CAMERA_PORT..."
        ufw allow 5000/tcp
        if [ "$CAMERA_PORT" != "5000" ]; then
            ufw allow "$CAMERA_PORT"/tcp
        fi
        echo -e "${GREEN}Ports 5000 and $CAMERA_PORT opened in UFW firewall${NC}"
        FIREWALL_FOUND=true
    fi
# Check firewalld (RHEL/CentOS)
elif command -v firewall-cmd >/dev/null 2>&1; then
    if firewall-cmd --state 2>/dev/null | grep -q "running"; then
        echo "firewalld is active, opening ports 5000 and $CAMERA_PORT..."
        firewall-cmd --permanent --add-port=5000/tcp
        if [ "$CAMERA_PORT" != "5000" ]; then
            firewall-cmd --permanent --add-port="$CAMERA_PORT"/tcp
        fi
        firewall-cmd --reload
        echo -e "${GREEN}Ports 5000 and $CAMERA_PORT opened in firewalld${NC}"
        FIREWALL_FOUND=true
    fi
# Check iptables (most common on Raspberry Pi OS)
elif command -v iptables >/dev/null 2>&1; then
    # Check if iptables has rules (if it returns more than just headers, there are rules)
    if [ "$(iptables -L -n | wc -l)" -gt 8 ]; then
        IPTABLES_RULE_CHANGED=false

        echo "iptables firewall detected, checking for port 5000 rule..."
        if ! iptables -C INPUT -p tcp --dport 5000 -j ACCEPT 2>/dev/null; then
            echo "Adding iptables rule for port 5000..."
            iptables -I INPUT -p tcp --dport 5000 -j ACCEPT
            IPTABLES_RULE_CHANGED=true
            echo -e "${GREEN}Port 5000 opened in iptables${NC}"
        else
            echo "Port 5000 already allowed in iptables"
        fi

        if [ "$CAMERA_PORT" != "5000" ]; then
            echo "Checking iptables rule for camera port $CAMERA_PORT..."
            if ! iptables -C INPUT -p tcp --dport "$CAMERA_PORT" -j ACCEPT 2>/dev/null; then
                echo "Adding iptables rule for camera port $CAMERA_PORT..."
                iptables -I INPUT -p tcp --dport "$CAMERA_PORT" -j ACCEPT
                IPTABLES_RULE_CHANGED=true
                echo -e "${GREEN}Port $CAMERA_PORT opened in iptables${NC}"
            else
                echo "Port $CAMERA_PORT already allowed in iptables"
            fi
        fi

        if [ "$IPTABLES_RULE_CHANGED" = true ]; then
            # Save rules permanently
            if command -v netfilter-persistent >/dev/null 2>&1; then
                netfilter-persistent save
            elif command -v iptables-save >/dev/null 2>&1; then
                iptables-save > /etc/iptables/rules.v4 || true
            fi
        fi

        FIREWALL_FOUND=true
    else
        echo "iptables present but no restrictive rules detected"
    fi
fi

if [ "$FIREWALL_FOUND" = false ]; then
    echo "No active firewall detected - ports 5000 and $CAMERA_PORT should be accessible"
fi

# Deploy pre-built app if present
echo "Looking for pre-built app..."
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PARENT_DIR="$(dirname "$SCRIPT_DIR")"
PROJECT_DIR="$PARENT_DIR/TerrariumController"

# Check multiple possible locations
APP_SOURCE=""
if [ -d "$SCRIPT_DIR/app" ]; then
    APP_SOURCE="$SCRIPT_DIR/app"
elif [ -d "$PROJECT_DIR/bin/Release/net10.0/publish" ]; then
    APP_SOURCE="$PROJECT_DIR/bin/Release/net10.0/publish"
elif [ -d "$PROJECT_DIR/bin/Release/net10.0/linux-arm64/publish" ]; then
    APP_SOURCE="$PROJECT_DIR/bin/Release/net10.0/linux-arm64/publish"
elif [ -d "$PROJECT_DIR/bin/Release/net10.0" ]; then
    APP_SOURCE="$PROJECT_DIR/bin/Release/net10.0"
elif [ -d "$PARENT_DIR/bin/Release/net10.0/publish" ]; then
    APP_SOURCE="$PARENT_DIR/bin/Release/net10.0/publish"
elif [ -d "$PARENT_DIR/bin/Release/net10.0" ]; then
    APP_SOURCE="$PARENT_DIR/bin/Release/net10.0"
fi

if [ -n "$APP_SOURCE" ] && [ -d "$APP_SOURCE" ]; then
    echo "Deploying pre-built app from $APP_SOURCE..."
    cp -R "$APP_SOURCE"/* /opt/terrarium/
    chown -R terrarium:terrarium /opt/terrarium
    chmod +x /opt/terrarium/TerrariumController 2>/dev/null || true
    chmod +x /opt/terrarium/run.sh
else
    echo -e "${YELLOW}Warning: No pre-built app found${NC}"
    echo "Expected locations (in order of preference):"
    echo "  - $SCRIPT_DIR/app"
    echo "  - $PROJECT_DIR/bin/Release/net10.0/publish"
    echo "  - $PROJECT_DIR/bin/Release/net10.0/linux-arm64/publish"
    echo ""
    echo "Build with one of:"
    echo "  cd $PARENT_DIR"
    echo "  dotnet publish TerrariumController/TerrariumController.csproj -c Release"
    echo "  # OR for self-contained:"
    echo "  dotnet publish TerrariumController/TerrariumController.csproj -c Release -r linux-arm64 --self-contained"
    echo ""
    echo -e "${YELLOW}After building, re-run this setup script OR manually copy:${NC}"
    echo -e "${YELLOW}  sudo cp -R <publish-folder>/* /opt/terrarium/${NC}"
    echo -e "${YELLOW}  sudo chown -R terrarium:terrarium /opt/terrarium${NC}"
    echo -e "${YELLOW}  sudo chmod +x /opt/terrarium/run.sh${NC}"
fi

# Verify app deployment (self-contained binary)
if [ -x "/opt/terrarium/TerrariumController" ]; then
    echo -e "${GREEN}App deployed successfully${NC}"
    
    # Start the service automatically
    echo ""
    echo "Starting terrarium services..."
    systemctl start terrarium-camera
    systemctl start terrarium
    
    # Wait a moment for service to start
    sleep 2
    
    # Check service status
    echo ""
    echo "Service status:"
    if systemctl is-active --quiet terrarium; then
        echo -e "${GREEN}✓ Terrarium service is running${NC}"
        systemctl status terrarium --no-pager -l | head -n 15

        echo ""
        echo "Camera service status:"
        if systemctl is-active --quiet terrarium-camera; then
            echo -e "${GREEN}✓ Terrarium camera service is running${NC}"
            systemctl status terrarium-camera --no-pager -l | head -n 15
        else
            echo -e "${RED}✗ Terrarium camera service failed to start${NC}"
            journalctl -u terrarium-camera -n 20 --no-pager
            exit 1
        fi
        
        # Check if app and camera ports are listening
        echo ""
        echo "Network status:"
        CAMERA_PORT_RUNTIME="${CAMERA_STREAM_PORT:-5001}"
        if command -v netstat >/dev/null 2>&1; then
            netstat -tlnp | grep :5000 || echo -e "${YELLOW}Port 5000 not yet listening${NC}"
            netstat -tlnp | grep ":${CAMERA_PORT_RUNTIME}" || echo -e "${YELLOW}Port ${CAMERA_PORT_RUNTIME} (camera stream) not yet listening${NC}"
        elif command -v ss >/dev/null 2>&1; then
            ss -tlnp | grep :5000 || echo -e "${YELLOW}Port 5000 not yet listening${NC}"
            ss -tlnp | grep ":${CAMERA_PORT_RUNTIME}" || echo -e "${YELLOW}Port ${CAMERA_PORT_RUNTIME} (camera stream) not yet listening${NC}"
        fi

        # Try to connect to app and camera services
        echo ""
        if command -v curl >/dev/null 2>&1; then
            CAMERA_URL="http://localhost:${CAMERA_STREAM_PORT:-5001}/"
            CAMERA_OK=false

            for attempt in $(seq 1 20); do
                # MJPEG streams are long-lived responses; probe headers only.
                if curl -sS -I --connect-timeout 2 --max-time 4 "$CAMERA_URL" >/dev/null 2>&1; then
                    CAMERA_OK=true
                    echo -e "${GREEN}✓ Camera endpoint responded${NC}"
                    break
                fi

                sleep 1
            done

            if [ "$CAMERA_OK" = false ]; then
                echo -e "${YELLOW}⚠ Camera endpoint did not respond yet at $CAMERA_URL${NC}"
                echo "Camera service logs:"
                journalctl -u terrarium-camera -n 20 --no-pager
                echo -e "${YELLOW}Continuing setup; camera may still be initializing.${NC}"
            fi

            echo "Testing health endpoints..."

            HEALTH_OK=false
            READY_OK=false

            for attempt in $(seq 1 12); do
                if curl -fsS http://localhost:5000/healthz --connect-timeout 5 --max-time 10 >/dev/null 2>&1; then
                    HEALTH_OK=true
                    echo -e "${GREEN}✓ Liveness endpoint responded${NC}"
                    break
                fi
                sleep 2
            done

            for attempt in $(seq 1 20); do
                if curl -fsS http://localhost:5000/readyz --connect-timeout 5 --max-time 10 >/dev/null 2>&1; then
                    READY_OK=true
                    echo -e "${GREEN}✓ Readiness endpoint responded${NC}"
                    break
                fi
                sleep 2
            done

            if [ "$HEALTH_OK" = false ]; then
                echo -e "${RED}✗ Liveness endpoint did not respond${NC}"
                journalctl -u terrarium -n 20 --no-pager
                exit 1
            fi

            if [ "$READY_OK" = false ]; then
                echo -e "${YELLOW}⚠ Service is alive but not ready yet${NC}"
                echo "Readiness details:"
                READYZ_OUTPUT=$(curl -sS http://localhost:5000/readyz || true)
                print_readyz_details "$READYZ_OUTPUT"
            else
                echo "Application root check:"
                curl -s -o /dev/null -w "HTTP %{http_code}\n" http://localhost:5000 --connect-timeout 5 --max-time 10 || true
            fi
        fi
    else
        echo -e "${RED}✗ Terrarium service failed to start${NC}"
        echo "Recent logs:"
        journalctl -u terrarium -n 20 --no-pager
        echo ""
        echo -e "${RED}Service failed - see logs above${NC}"
    fi
else
    echo -e "${YELLOW}Note: Self-contained app not yet deployed to /opt/terrarium${NC}"
    echo -e "${YELLOW}Build and deploy the self-contained app before starting the service${NC}"
fi

echo -e "${GREEN}=== Setup Complete ===${NC}"
echo ""
echo "Access the UI at:"
echo "  http://localhost:5000"
echo "  http://$(hostname -I | awk '{print $1}'):5000"
echo ""
if [ -n "$SUDO_USER" ] && [ "$SUDO_USER" != "root" ]; then
    echo -e "${YELLOW}Note: You ($SUDO_USER) have been added to the terrarium group.${NC}"
    echo -e "${YELLOW}To apply group membership, log out and back in.${NC}"
    echo ""
fi
echo "Useful commands:"
echo "  Restart service:  sudo systemctl restart terrarium"
echo "  View logs:        sudo journalctl -u terrarium -f"
echo "  Camera logs:      sudo journalctl -u terrarium-camera -f"
echo "  Stop service:     sudo systemctl stop terrarium"
echo "  Stop camera:      sudo systemctl stop terrarium-camera"
echo "  Manual test:      sudo -u terrarium /opt/terrarium/run.sh"
