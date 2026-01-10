#!/bin/bash
# Raspberry Pi Terrarium Controller Setup Script
# This script MUST be run on Raspberry Pi OS with sudo
# Usage: sudo bash setup.sh
#
# Do NOT run this on non-Raspberry Pi systems - it will modify system configuration

set -e

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

# Check if running as root
if [ "$EUID" -ne 0 ]; then 
    echo -e "${RED}Error: This script must be run as root (sudo)${NC}"
    exit 1
fi

# Update system
echo "Updating system packages..."
apt update
apt upgrade -y

# Install .NET ASP.NET Core Runtime (runtime only; app is pre-built)
echo "Installing .NET ASP.NET Core Runtime..."

# Check if dotnet is already installed
DOTNET_INSTALLED=false
if command -v dotnet >/dev/null 2>&1 || [ -x "/usr/local/dotnet/dotnet" ]; then
    DOTNET_CMD=""
    if [ -x "/usr/local/dotnet/dotnet" ]; then
        DOTNET_CMD="/usr/local/dotnet/dotnet"
    else
        DOTNET_CMD="dotnet"
    fi
    
    # Check if ASP.NET Core runtime 10.0 is installed
    if $DOTNET_CMD --list-runtimes 2>/dev/null | grep -q "Microsoft.AspNetCore.App 10\."; then
        echo -e "${GREEN}.NET ASP.NET Core Runtime 10.0 already installed, skipping...${NC}"
        DOTNET_INSTALLED=true
    else
        echo "dotnet found but ASP.NET Core 10.0 runtime not detected, will install..."
    fi
fi

# Detect architecture for the installer (prefer 64-bit on Raspberry Pi OS)
UNAME_ARCH="$(uname -m)"
DOTNET_ARCH="arm64"
if [ "$UNAME_ARCH" = "aarch64" ]; then
    DOTNET_ARCH="arm64"
elif [ "$UNAME_ARCH" = "armv7l" ]; then
    # Fall back to arm if running 32-bit OS (not recommended for ASP.NET Core)
    DOTNET_ARCH="arm"
fi

if [ "$DOTNET_INSTALLED" = false ]; then
for attempt in 1 2 3; do
    echo "Attempt $attempt: Downloading .NET installer..."
    if curl -fSL https://dot.net/v1/dotnet-install.sh -o dotnet-install.sh --max-time 60 --retry 3; then
        chmod +x dotnet-install.sh
        # Install ASP.NET Core runtime only to reduce memory footprint
        if ./dotnet-install.sh \
            --runtime aspnetcore \
            --channel 10.0 \
            --install-dir /usr/local/dotnet \
            --architecture "$DOTNET_ARCH"; then
            DOTNET_INSTALLED=true
            break
        fi
    fi
    echo "Attempt $attempt failed, retrying..."
    sleep 5
done

    if [ "$DOTNET_INSTALLED" = false ]; then
        echo -e "${YELLOW}Warning: .NET installation had issues, trying apt package (runtime only)...${NC}"
        # Prefer ASP.NET Core runtime package; fall back to generic runtime if needed
        apt install -y aspnetcore-runtime-10.0 || apt install -y dotnet-runtime-10.0 || true
    fi
fi

export PATH=$PATH:/usr/local/dotnet:/root/.dotnet/tools
rm -f dotnet-install.sh

# Create user and directory for the app
echo "Creating terrarium user and directories..."
useradd -m -s /bin/bash terrarium || true
mkdir -p /opt/terrarium
chown terrarium:terrarium /opt/terrarium

# Create app launcher script to handle self-contained or framework-dependent deployments
echo "Creating app launcher script..."
cat > /opt/terrarium/run.sh << 'EOF'
#!/bin/bash
set -e

# Prefer self-contained binary if present
if [ -x "/opt/terrarium/TerrariumController" ]; then
    exec /opt/terrarium/TerrariumController
elif [ -f "/opt/terrarium/TerrariumController.dll" ]; then
    # Try local dotnet install dir first, then system dotnet
    if [ -x "/usr/local/dotnet/dotnet" ]; then
        exec /usr/local/dotnet/dotnet /opt/terrarium/TerrariumController.dll
    elif command -v dotnet >/dev/null 2>&1; then
        exec dotnet /opt/terrarium/TerrariumController.dll
    else
        echo "dotnet runtime not found; install ASP.NET Core runtime or deploy self-contained binary" >&2
        exit 1
    fi
else
    echo "Terrarium app not found in /opt/terrarium (expected TerrariumController or TerrariumController.dll)" >&2
    exit 1
fi
EOF
chown terrarium:terrarium /opt/terrarium/run.sh
chmod +x /opt/terrarium/run.sh

# Install GPIO dependencies
echo "Installing GPIO dependencies..."
# Try multiple GPIO library options for compatibility
if ! apt install -y libgpiod-dev; then
    echo -e "${YELLOW}Warning: libgpiod-dev installation failed, trying gpiod...${NC}"
    apt install -y gpiod || echo -e "${YELLOW}GPIO libraries may need manual installation${NC}"
fi
if ! apt install -y python3-gpiozero python3-rpi.gpio; then
    echo -e "${YELLOW}Warning: Python GPIO bindings not available (optional)${NC}"
fi

# Install mjpg-streamer for camera streaming
echo "Installing mjpg-streamer..."
if ! apt install -y mjpg-streamer; then
    echo -e "${YELLOW}Warning: mjpg-streamer not available, trying libjpeg-turbo-progs...${NC}"
    apt install -y libjpeg-turbo-progs || echo -e "${YELLOW}mjpg-streamer may need manual setup${NC}"
fi

# Copy systemd service unit
echo "Installing systemd service..."
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
if [ ! -f "$SCRIPT_DIR/terrarium.service" ]; then
    echo -e "${RED}Error: terrarium.service not found in $SCRIPT_DIR${NC}"
    exit 1
fi
cp "$SCRIPT_DIR/terrarium.service" /etc/systemd/system/
systemctl daemon-reload
systemctl enable terrarium

# Create kiosk autostart script
echo "Creating Chromium kiosk launcher..."
mkdir -p /home/terrarium/.config/autostart
cat > /home/terrarium/.config/autostart/terrarium-kiosk.desktop << EOF
[Desktop Entry]
Type=Application
Exec=bash -c 'sleep 5; chromium-browser --kiosk http://localhost:5000'
Hidden=false
NoDisplay=false
X-GNOME-Autostart-enabled=true
EOF

chown terrarium:terrarium /home/terrarium/.config/autostart/terrarium-kiosk.desktop

# Set GPIO permissions for non-root access
echo "Configuring GPIO permissions..."
usermod -a -G dialout terrarium
usermod -a -G video terrarium
# Add gpio group if it exists
if getent group gpio > /dev/null; then
    usermod -a -G gpio terrarium
    echo "Added terrarium to gpio group"
else
    echo -e "${YELLOW}Note: gpio group not found, skipping gpio group assignment${NC}"
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

# Verify app deployment (binary or DLL)
if [ -x "/opt/terrarium/TerrariumController" ] || [ -f "/opt/terrarium/TerrariumController.dll" ]; then
    echo -e "${GREEN}App deployed successfully${NC}"
    
    # Start the service automatically
    echo ""
    echo "Starting terrarium service..."
    systemctl start terrarium
    
    # Wait a moment for service to start
    sleep 2
    
    # Check service status
    echo ""
    echo "Service status:"
    if systemctl is-active --quiet terrarium; then
        echo -e "${GREEN}✓ Terrarium service is running${NC}"
        systemctl status terrarium --no-pager -l | head -n 15
    else
        echo -e "${RED}✗ Terrarium service failed to start${NC}"
        echo "Recent logs:"
        journalctl -u terrarium -n 20 --no-pager
        echo ""
        echo -e "${RED}Service failed - see logs above${NC}"
    fi
else
    echo -e "${YELLOW}Note: App not yet deployed to /opt/terrarium${NC}"
    echo -e "${YELLOW}Build and deploy the app before starting the service${NC}"
fi

echo -e "${GREEN}=== Setup Complete ===${NC}"
echo ""
echo "Access the UI at:"
echo "  http://localhost:5000"
echo "  http://$(hostname -I | awk '{print $1}'):5000"
echo ""
echo "Useful commands:"
echo "  Restart service:  sudo systemctl restart terrarium"
echo "  View logs:        sudo journalctl -u terrarium -f"
echo "  Stop service:     sudo systemctl stop terrarium"
echo "  Manual test:      sudo -u terrarium /opt/terrarium/run.sh"
