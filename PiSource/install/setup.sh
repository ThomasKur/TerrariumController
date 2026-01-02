#!/bin/bash
# Raspberry Pi Terrarium Controller Setup Script
# Run this script to install all dependencies and configure the system

set -e

echo "=== Terrarium Controller Setup ===" 

# Check if running as root
if [ "$EUID" -ne 0 ]; then 
    echo "This script must be run as root (sudo)"
    exit 1
fi

# Update system
echo "Updating system packages..."
apt update
apt upgrade -y

# Install .NET SDK
echo "Installing .NET SDK..."
curl https://dot.net/v1/dotnet-install.sh -o dotnet-install.sh
chmod +x dotnet-install.sh
./dotnet-install.sh --channel lts --install-dir /usr/local/dotnet
export PATH=$PATH:/usr/local/dotnet
rm dotnet-install.sh

# Create user and directory for the app
echo "Creating terrarium user and directories..."
useradd -m -s /bin/bash terrarium || true
mkdir -p /opt/terrarium
chown terrarium:terrarium /opt/terrarium

# Install GPIO dependencies
echo "Installing GPIO dependencies..."
apt install -y libgpiod0 libgpiod-dev python3-libgpiod

# Install mjpg-streamer for camera streaming
echo "Installing mjpg-streamer..."
apt install -y mjpg-streamer libjpeg-turbo-progs

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
Exec=bash -c 'sleep 5; /usr/bin/chromium --kiosk http://localhost:5000'
Hidden=false
NoDisplay=false
X-GNOME-Autostart-enabled=true
EOF

chown terrarium:terrarium /home/terrarium/.config/autostart/terrarium-kiosk.desktop

# Set GPIO permissions for non-root access
echo "Configuring GPIO permissions..."
usermod -a -G dialout terrarium
usermod -a -G video terrarium

echo "=== Setup Complete ==="
echo ""
echo "Next steps:"
echo "1. Build the app: cd /opt/terrarium && dotnet publish"
echo "2. Copy published files to /opt/terrarium"
echo "3. Start the service: systemctl start terrarium"
echo "4. View logs: journalctl -u terrarium -f"
echo "5. Access UI at http://localhost:5000 (or http://<pi-ip>:5000 from remote)"
