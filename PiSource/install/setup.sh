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
chown terrarium:terrarium /opt/terrarium
chown terrarium:terrarium /opt/terrarium/logs

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

MJPG_SRC_DIR="/opt/mjpg-streamer-src"
MJPG_BUILD_DIR="$MJPG_SRC_DIR/mjpg-streamer-experimental"
MJPG_INSTALLED=false

if apt install -y mjpg-streamer; then
    MJPG_INSTALLED=true
else
    echo -e "${YELLOW}apt package mjpg-streamer not available; building from source...${NC}"
    apt install -y git build-essential cmake libjpeg62-turbo-dev libv4l-dev imagemagick || echo -e "${YELLOW}Some build dependencies failed to install${NC}"

    if [ ! -d "$MJPG_SRC_DIR" ]; then
        git clone --depth 1 https://github.com/jacksonliam/mjpg-streamer.git "$MJPG_SRC_DIR" || echo -e "${YELLOW}Clone failed${NC}"
    else
        git -C "$MJPG_SRC_DIR" pull --ff-only || echo -e "${YELLOW}Git pull failed, continuing with existing source${NC}"
    fi

    if [ -d "$MJPG_BUILD_DIR" ]; then
        (cd "$MJPG_BUILD_DIR" && make clean || true)
        if (cd "$MJPG_BUILD_DIR" && make && make install); then
            MJPG_INSTALLED=true
        else
            echo -e "${YELLOW}mjpg-streamer build failed${NC}"
        fi
    else
        echo -e "${YELLOW}mjpg-streamer source directory missing after clone${NC}"
    fi
fi

if [ "$MJPG_INSTALLED" = true ]; then
    if command -v mjpg_streamer >/dev/null 2>&1; then
        echo -e "${GREEN}mjpg-streamer installed at $(command -v mjpg_streamer)${NC}"
        echo "Test command (adjust -i args for your camera):"
        echo "  mjpg_streamer -i 'input_uvc.so -f 15 -r 1280x720' -o 'output_http.so -p 8080 -w /usr/local/share/mjpg-streamer/www'"
    else
        echo -e "${YELLOW}mjpg-streamer build completed but binary not found in PATH${NC}"
    fi
else
    echo -e "${YELLOW}mjpg-streamer could not be installed automatically${NC}"
    echo "Check build logs above or install manually from source."
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

# Configure firewall if active
echo "Checking firewall configuration..."
FIREWALL_FOUND=false

# Check UFW (Ubuntu/Debian)
if command -v ufw >/dev/null 2>&1; then
    if ufw status | grep -q "Status: active"; then
        echo "UFW firewall is active, opening port 5000..."
        ufw allow 5000/tcp
        echo -e "${GREEN}Port 5000 opened in UFW firewall${NC}"
        FIREWALL_FOUND=true
    fi
# Check firewalld (RHEL/CentOS)
elif command -v firewall-cmd >/dev/null 2>&1; then
    if firewall-cmd --state 2>/dev/null | grep -q "running"; then
        echo "firewalld is active, opening port 5000..."
        firewall-cmd --permanent --add-port=5000/tcp
        firewall-cmd --reload
        echo -e "${GREEN}Port 5000 opened in firewalld${NC}"
        FIREWALL_FOUND=true
    fi
# Check iptables (most common on Raspberry Pi OS)
elif command -v iptables >/dev/null 2>&1; then
    # Check if iptables has rules (if it returns more than just headers, there are rules)
    if [ "$(iptables -L -n | wc -l)" -gt 8 ]; then
        echo "iptables firewall detected, checking for port 5000 rule..."
        if ! iptables -C INPUT -p tcp --dport 5000 -j ACCEPT 2>/dev/null; then
            echo "Adding iptables rule for port 5000..."
            iptables -I INPUT -p tcp --dport 5000 -j ACCEPT
            # Save rules permanently
            if command -v netfilter-persistent >/dev/null 2>&1; then
                netfilter-persistent save
            elif command -v iptables-save >/dev/null 2>&1; then
                iptables-save > /etc/iptables/rules.v4 || true
            fi
            echo -e "${GREEN}Port 5000 opened in iptables${NC}"
        else
            echo "Port 5000 already allowed in iptables"
        fi
        FIREWALL_FOUND=true
    else
        echo "iptables present but no restrictive rules detected"
    fi
fi

if [ "$FIREWALL_FOUND" = false ]; then
    echo "No active firewall detected - port 5000 should be accessible"
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
        
        # Check if port 5000 is listening
        echo ""
        echo "Network status:"
        if command -v netstat >/dev/null 2>&1; then
            netstat -tlnp | grep :5000 || echo -e "${YELLOW}Port 5000 not yet listening${NC}"
        elif command -v ss >/dev/null 2>&1; then
            ss -tlnp | grep :5000 || echo -e "${YELLOW}Port 5000 not yet listening${NC}"
        fi
        
        # Try to connect to the service
        echo ""
        if command -v curl >/dev/null 2>&1; then
            echo "Testing connection..."
            if curl -s -o /dev/null -w "%{http_code}" http://localhost:5000 --connect-timeout 5 --max-time 10 | grep -q "200\|302"; then
                echo -e "${GREEN}✓ Service is responding on port 5000${NC}"
            else
                echo -e "${YELLOW}⚠ Service started but not responding yet (may still be initializing)${NC}"
                echo "Wait 10 seconds and try: curl http://localhost:5000"
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
echo "Useful commands:"
echo "  Restart service:  sudo systemctl restart terrarium"
echo "  View logs:        sudo journalctl -u terrarium -f"
echo "  Stop service:     sudo systemctl stop terrarium"
echo "  Manual test:      sudo -u terrarium /opt/terrarium/run.sh"
