# Terrarium Controller

Raspberry Pi project that controls lighting and heating devices in a terrarium. The Raspberry Pi reads 1–3 DHT22 temperature/humidity sensors and uses them to drive relays 1–3 when the temperature falls below a user-defined threshold (default 29°C). Sensor 1 is for "Nest 1", Sensor 2 is for "Nest 2", and Sensor 3 is for the "Arena". Sensor 1 includes a second slider that defines a humidity threshold; if humidity falls below this value, Relay 5 switches on for 1 second and then stays locked out for 6 hours so humidity can recover. Thresholds are adjustable via a slider in the UI, with a 1°C hysteresis to reduce rapid on/off switching.

The control panel presents two rows of controls:

- Row 1: per-sensor live temperature and humidity plus a threshold slider to control the assigned relay.
- Row 2: three controls — (1) a scheduler for relay 4 (daylight simulation on/off times), (2) a live camera feed, and (3) a settings toggle.

## Logging

Logging records all relay state changes with the sensor values that triggered them. As well as hourly log entries with all sensor values. A single log function is responsible for writing the log entries to the SQLite database. Log entries older than the configured retention period (1–24 months, default 12 months) are automatically deleted. The application provides a settings page to adjust the retention period and view the current database size with a compact action.

## Setup

Initial setup configures the application to auto-start in full-screen (kiosk) mode. The installer installs all prerequisites such as .NET runtime, GPIO libraries, and mjpg-streamer for camera streaming. See [Deployment](#deployment) section below.

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

Sensors connect to GPIO 23/24/25. Controls for sensors with no data are disabled in the UI, and the corresponding relays remain off by default.

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
   - .NET 8 SDK and runtime
   - GPIO libraries
   - mjpg-streamer for camera streaming
   - Systemd service unit
   - Chromium kiosk launcher

3. **Build and publish the application**:
   ```bash
   cd ..
   dotnet publish -c Release -o /opt/terrarium
   ```

4. **Set permissions**:
   ```bash
   sudo chown -R terrarium:terrarium /opt/terrarium
   ```

5. **Start the service**:
   ```bash
   sudo systemctl start terrarium
   sudo systemctl enable terrarium  # Auto-start on boot
   ```

6. **Verify it's running**:
   ```bash
   sudo systemctl status terrarium
   sudo journalctl -u terrarium -f  # View logs
   ```

7. **Access the UI**:
   - **Local**: Touch screen will auto-launch Chromium in kiosk mode
   - **Remote**: Open browser and navigate to `http://<pi-ip>:5000`

### Configuration

All settings are stored in SQLite and managed via the web UI:
- **Thresholds**: Temperature thresholds for Relays 1-3 (default 29°C)
- **Humidity Threshold**: Sensor 1 humidity threshold for Relay 5 (default 60%)
- **Schedules**: Daylight on/off times for Relay 4
- **GPIO Map**: Customize relay-to-GPIO pin assignments (default PiRelay 6 mapping)
- **Camera Params**: MJPEG streamer width, height, and framerate (defaults 1280x720@15fps)
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
# Restart the service
sudo systemctl restart terrarium
```

**Camera not streaming**:
```bash
# Check mjpg-streamer is running
ps aux | grep mjpg
# Test camera manually
libcamera-hello -t 5
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
- `http://localhost:8080/?action=stream` - MJPEG camera feed (via mjpg-streamer)

SignalR hub (optional, for future real-time integrations):
- Hub URL: `/sensorHub` (for WebSocket updates)

## Contributing

When making changes, please follow:
- Keep documentation concise and in present tense
- Preserve GPIO mappings and hysteresis/lockout logic
- Test on actual Raspberry Pi hardware if possible
- Avoid adding secrets or credentials to code examples

For detailed implementation status and remaining tasks, see [IMPLEMENTATION_STATUS.md](IMPLEMENTATION_STATUS.md).

