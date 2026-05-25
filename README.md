# Terrarium Controller

Raspberry Pi project that controls lighting and heating devices in a terrarium. The Raspberry Pi reads 1–3 DHT22 temperature/humidity sensors and uses them to drive relays 1–3 when the temperature falls below a user-defined threshold (default 29°C). Sensor 1 is for "Nest 1", Sensor 2 is for "Nest 2", and Sensor 3 is for the "Arena". Sensor 1 includes a second slider that defines a humidity threshold; if humidity falls below this value, Relay 5 switches on for 1 second and then stays locked out for 6 hours so humidity can recover. Thresholds are adjustable via a slider in the UI, with a 1°C hysteresis to reduce rapid on/off switching.

The control panel presents two rows of controls:

- Row 1: per-sensor live temperature and humidity plus a threshold slider to control the assigned relay.
- Row 2: three controls — (1) a scheduler for relay 4 (daylight simulation on/off times), (2) a live camera feed, and (3) a settings toggle.

## Logging

Logging records all relay state changes with the sensor values that triggered them. As well as hourly log entries with all sensor values. A single log function is responsible for writing the log entries to the SQLite database. Log entries older than the configured retention period (1–24 months, default 12 months) are automatically deleted. The application provides a settings page to adjust the retention period and view the current database size with a compact action.

## Setup

Initial setup configures the application to run on a Raspberry Pi with GPIO and camera support. Optional kiosk mode auto-starts the app in full-screen Chromium. The installer sets up GPIO libraries and rpicam-apps for live MJPEG camera streaming via Python HTTP server. See [Deployment](#deployment) section below.

## Required Parts

* 1 x Raspberry Pi (4B or later recommended)
* 1 x Raspberry Pi Touch Screen (or use browser access)
* 1 x [PiRelay 6](https://www.pi-shop.ch/pirelay-6)
* 1-3 x [SEN-DHT22](https://www.bastelgarage.ch/dht22-temperature-and-humidity-sensor)
* 1 x [Original Raspberry Pi Camera Module 3](https://www.pi-shop.ch/raspberry-pi-camera-3-wide-noir)

### PiRelay 6 Configuration

Default relay-to-GPIO configuration (override in-app and persist to a config file in the install directory):

| Relays | BOARD | BCM |
| ------ | ----- | --- |
| Relay 1 | 29 | GPIO 5 |
| Relay 2 | 31 | GPIO 6 |
| Relay 3 | 33 | GPIO 13 |
| Relay 4 | 35 | GPIO 19 |
| Relay 5 | 37 | GPIO 26 |
| Relay 6 | 40 | GPIO 21 |

## SEN-DHT22

Sensors connect to GPIO 23/22/25. Controls for sensors with no data are disabled in the UI, and the corresponding relays remain off by default.

## Technology Stack

- **Backend**: ASP.NET Core Blazor Server (.NET 10+)
- **Database**: SQLite (local file in install directory)
- **GPIO Control**: System.Device.Gpio + Iot.Device.DHTxx
- **Camera Streaming**: mjpg-streamer (MJPEG over HTTP)
- **Frontend**: Blazor Server (real-time updates via SignalR)

## Development

See [PiSource/README.md](PiSource/README.md) for development setup and build instructions.

## Deployment

### Prerequisites
- Raspberry Pi running Raspberry Pi OS (Bookworm or later)
- Network access for initial setup
- SD card with at least 4GB free space

### Installation Steps

1. **Clone or copy this repository** to your Raspberry Pi:
   ```bash
   cd ~
   git clone <repository-url> TerrariumController
   cd TerrariumController/PiSource
   ```

2. **Run the setup script** (requires sudo):
   ```bash
   cd install
   sudo bash setup.sh
   ```
   This installs:
   - GPIO libraries
   - rpicam-apps for camera streaming (Python3 HTTP server included)
   - Systemd service units (`terrarium` and `terrarium-camera`)
   - Chromium browser (optional, for kiosk mode autostart)

3. **Set permissions**:
   ```bash
   sudo chown -R terrarium:terrarium /opt/terrarium
   ```

4. **Start the service**:
   ```bash
   sudo systemctl start terrarium
   sudo systemctl enable terrarium  # Auto-start on boot
   ```

5. **Verify it's running**:
   ```bash
   sudo systemctl status terrarium
   sudo journalctl -u terrarium -f  # View logs
   ```

6. **Access the UI**:
   - **With Chromium kiosk** (if installed): Touch screen auto-launches full-screen Chromium at startup
   - **Any browser**: Open browser and navigate to `http://<pi-ip>:5000`
   - **Remote SSH**: App runs on port 5000; access from any browser on your network

### Configuration

All settings are stored in SQLite and managed via the web UI:
- **Thresholds**: Temperature thresholds for Relays 1-3 (default 29°C)
- **Humidity Threshold**: Sensor 1 humidity threshold for Relay 5 (default 60%)
- **Schedules**: Daylight on/off times for Relay 4
- **GPIO Map**: Customize relay-to-GPIO pin assignments (default PiRelay 6 mapping)
- **Camera Params**: MJPEG stream resolution and framerate. Can be adjusted via environment variables:
  - `CAMERA_WIDTH=1920` (default, Full HD)
  - `CAMERA_HEIGHT=1080` (default, Full HD)
  - `CAMERA_FPS=15` (default)
  
  To change, edit `/etc/terrarium/terrarium.env` and restart: `sudo systemctl restart terrarium-camera`
- **Log Retention**: Delete entries older than N months (1-24, default 12)

### Log Access

- View logs in the web UI under **Log History** page
- Logs are stored in SQLite and include:
  - Relay state changes with trigger source and sensor values
  - Hourly sensor reading snapshots
  - Automatic pruning by age

### Troubleshooting

**No GPIO access**:
```bash
# Add user to dialout and video groups
sudo usermod -a -G dialout terrarium
sudo usermod -a -G video terrarium

# Install libgpiod runtime/tools (required for gpiochip driver, especially on Pi 5)
sudo apt update
sudo apt install -y libgpiod3 gpiod || sudo apt install -y libgpiod2 gpiod || sudo apt install -y libgpiod gpiod

# If logs show "EntryPointNotFoundException" for gpiod symbols, remove manual compatibility symlinks
ldconfig -p | grep libgpiod
ARCH_LIB_DIR="/usr/lib/$(dpkg-architecture -qDEB_HOST_MULTIARCH 2>/dev/null || echo aarch64-linux-gnu)"
[ -L "$ARCH_LIB_DIR/libgpiod.so.2" ] && sudo rm -f "$ARCH_LIB_DIR/libgpiod.so.2"
[ -L "$ARCH_LIB_DIR/libgpiod.so.1" ] && sudo rm -f "$ARCH_LIB_DIR/libgpiod.so.1"
sudo ldconfig

# Verify gpiochip devices are visible
gpiodetect

# Restart the service
sudo systemctl restart terrarium
```

If you run standalone sensor tests like `test_dht22.py`, stop the app first to avoid `GPIO busy`:

```bash
sudo systemctl stop terrarium
# run python sensor tests
sudo systemctl start terrarium
```

**Camera not streaming**:
```bash
# Check if camera service is running
systemctl status terrarium-camera

# View camera service logs (shows startup errors and diagnostics)
sudo journalctl -u terrarium-camera -f -n 100

# If exit code 127 (command not found), verify dependencies:
which rpicam-vid || echo "rpicam-vid not installed"
which python3 || echo "python3 not installed"

# Install camera tools
sudo apt install rpicam-apps -y

# Verify camera hardware is accessible
rpicam-hello -t 1

# Test camera stream manually (Full HD)
rpicam-vid --codec mjpeg -t 5 --width 1920 --height 1080 --framerate 15 -o /tmp/test.mjpeg

# Or test at lower resolution
rpicam-vid --codec mjpeg -t 5 --width 640 --height 480 --framerate 15 -o /tmp/test-low.mjpeg

# Test HTTP access to camera stream
curl -v http://localhost:8080/ 2>&1 | head -10

# After installing dependencies, restart the camera service
sudo systemctl restart terrarium-camera
```

**Customize camera resolution**:
```bash
# Edit the environment configuration
sudo nano /etc/terrarium/terrarium.env

# Change these lines to desired resolution (default is Full HD 1920x1080):
# CAMERA_WIDTH=1920
# CAMERA_HEIGHT=1080
# CAMERA_FPS=15

# Save and restart camera service
sudo systemctl restart terrarium-camera

# Verify stream is working at new resolution
curl -v http://localhost:8080/ 2>&1 | head -10
```

**Database corrupted**:
```bash
# Compact database via UI Settings page, or manually:
sudo systemctl stop terrarium
sudo -u terrarium sqlite3 /opt/terrarium/terrarium.db VACUUM
sudo systemctl start terrarium
```

**View application logs**:
```bash
sudo journalctl -u terrarium -f -n 50
```

## API / Endpoints

The Blazor Server app exposes:
- `GET /` - Dashboard (two-row control panel)
- `GET /settings` - Settings page (thresholds, schedules, retention, GPIO config, DB compact)
- `GET /log-history` - Log history with pagination and filtering
- `http://localhost:8080/` - MJPEG camera feed (via ffmpeg and rpicam-vid)

SignalR hub (optional, for future real-time integrations):
- Hub URL: `/sensorHub` (for WebSocket updates)

## Contributing

When making changes, please follow:
- Keep documentation concise and in present tense
- Preserve GPIO mappings and hysteresis/lockout logic
- Test on actual Raspberry Pi hardware if possible
- Avoid adding secrets or credentials to code examples

For detailed implementation status and remaining tasks, see [IMPLEMENTATION_STATUS.md](IMPLEMENTATION_STATUS.md).

