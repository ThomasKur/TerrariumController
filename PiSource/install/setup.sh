#!/bin/bash
# Raspberry Pi Terrarium Controller Setup Script
# Run this script to install all dependencies and configure the system

set -e

echo "=== Terrarium Controller Setup ===" 

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

# Install .NET Runtime (only, since app is pre-built)
echo "Installing .NET Runtime..."
DOTNET_INSTALLED=false
for attempt in 1 2 3; do
    echo "Attempt $attempt: Downloading .NET installer..."
    if curl -L https://dot.net/v1/dotnet-install.sh -o dotnet-install.sh --max-time 60 --retry 3; then
        chmod +x dotnet-install.sh
        if ./dotnet-install.sh --channel 10.0 --install-dir /usr/local/dotnet; then
            DOTNET_INSTALLED=true
            break
        fi
    fi
    echo "Attempt $attempt failed, retrying..."
    sleep 5
done

if [ "$DOTNET_INSTALLED" = false ]; then
    echo -e "${YELLOW}Warning: .NET installation had issues, trying apt package...${NC}"
    apt install -y dotnet-sdk-10.0 || apt install -y dotnet-runtime-10.0 || true
fi

export PATH=$PATH:/usr/local/dotnet:/root/.dotnet/tools
rm -f dotnet-install.sh

# Create user and directory for the app
echo "Creating terrarium user and directories..."
useradd -m -s /bin/bash terrarium || true
mkdir -p /opt/terrarium
chown terrarium:terrarium /opt/terrarium

# Install GPIO dependencies
echo "Installing GPIO dependencies..."
# Try multiple GPIO library options for compatibility
apt install -y libgpiod-dev || apt install -y gpiod || echo "GPIO libraries may need manual installation"
apt install -y python3-gpiozero python3-rpi.gpio || echo "Python GPIO bindings optional"

# Install mjpg-streamer for camera streaming
echo "Installing mjpg-streamer..."
apt install -y mjpg-streamer || apt install -y libjpeg-turbo-progs || echo "mjpg-streamer may need manual setup"

# Copy systemd service unit
echo "Installing systemd service..."
cp terrarium.service /etc/systemd/system/
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
usermod -a -G gpio terrarium || true

# Deploy pre-built app if present
if [ -d "app" ]; then
    echo "Deploying pre-built app..."
    cp -R app/* /opt/terrarium/
    chown -R terrarium:terrarium /opt/terrarium
    chmod +x /opt/terrarium/TerrariumController || true
else
    echo -e "${YELLOW}Warning: No pre-built app found in ./app${NC}"
fi

echo -e "${GREEN}=== Setup Complete ===${NC}"
echo ""
echo "Next steps:"
echo "1. Start the service: systemctl start terrarium"
echo "2. View logs: journalctl -u terrarium -f"
echo "3. Access UI at http://localhost:5000 (or http://<pi-ip>:5000)"
echo ""
echo "To manually start for testing:"
echo "  sudo -u terrarium /usr/local/dotnet/dotnet /opt/terrarium/TerrariumController.dll"
