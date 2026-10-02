# Terrarium Controller

Raspberry Pi project that controls lighting and heating devices in a terrarium. The Raspberry Pi reads 1–3 DHT22 temperature/humidity sensors and uses them to drive relays 1–3 when the temperature falls below a user-defined threshold (default 29°C). Sensor 1 is for "Nest 1", Sensor 2 is for "Nest 2", and Sensor 3 is for the "Arena". Sensor 1 includes a second slider that defines a humidity threshold; if humidity falls below this value, Relay 5 switches on for 1 second and then stays locked out for 6 hours so humidity can recover. Thresholds are adjustable via a slider in the UI, with a 1°C hysteresis to reduce rapid on/off switching.

The control panel presents two rows of controls:

- Row 1: per-sensor live temperature and humidity plus a threshold slider to control the assigned relay.
- Row 2: three controls — (1) a scheduler for relay 4 (daylight simulation on/off times), (2) a live camera feed, and (3) a settings toggle.

## Logging

Logging records all relay state changes with the sensor values that triggered them. As well as hourly log entries with all sensor values. A single log function is responsible for writing the log entries to the SQLite database. Log entries older than the configured retention period (1–24 months, default 12 months) are automatically deleted. The application provides a settings page to adjust the retention period and view the current database size with a compact action.

## Setup

Initial setup configures the application to run on a Raspberry Pi with GPIO and camera support. Optional kiosk mode auto-starts the app in full-screen Chromium. The installer sets up GPIO libraries and rpicam-apps for live MJPEG camera streaming via Python HTTP server. See [Deployment](#deployment) section below.

Camera compatibility note: the current `terrarium-camera` service is known to run reliably on Raspberry Pi 3 class hardware, while Raspberry Pi 5 deployments can show intermittent startup/stream stability issues depending on camera stack and firmware state.

## Required Parts

* 1 x Raspberry Pi (5 or later recommended)
* 1 x Raspberry Pi Touch Screen (or use browser access)
* 1 x [PiRelay 6](https://www.pi-shop.ch/pirelay-6)
* 3 x [SEN-DHT22] connected to Raspberry Pico which is connected to Raspberry Pi via UART(https://www.bastelgarage.ch/dht22-temperature-and-humidity-sensor)
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

## Pico UART Sensor Wiring

If you move DHT22 reads onto a Pico, the Raspberry Pi reads sensor frames from the Pico over UART instead of reading DHT22 timing directly.

- Pico `GP0` TX -> Raspberry Pi UART RX, physical pin 10 (`GPIO15`, `/dev/serial0`)
- Pico `GP1` RX -> Raspberry Pi UART TX, physical pin 8 (`GPIO14`) if you want two-way serial for diagnostics later
- Pico `GND` -> Raspberry Pi `GND`
- Keep the Pico UART at `3.3V` logic only; do not use `5V` serial adapters on Pi GPIO
- The bundled Pico script in [PiSource/Pico/run.py](PiSource/Pico/run.py) uses UART0 at `115200` baud

Before deploying, make sure Raspberry Pi OS exposes the UART on `/dev/serial0` and disables the login shell on that serial port.

## Technology Stack

- **Backend**: ASP.NET Core Blazor Server (.NET 10+)
- **Database**: SQLite (local file in install directory)
- **GPIO Control**: System.Device.Gpio + Iot.Device.DHTxx
- **Camera Streaming**: MJPEG over HTTP as an idea, can be adjusted...
- **Frontend**: Blazor Server (real-time updates via SignalR)
- **HArdwareLibrary**: Create one library project whcih s used to read UART, control the relays.
- **Use multithreading** to not block the WebUi or Hardware interactions or webcam display.

## Development

See [PiSource](PiSource) for development setup, build and instructions for the App on the Raspberry Pi.

See [PicoSource](PicoSource) for development setup, build and instructions for the App on the Raspberry Pico. (No changes should be required, the sensor reads work.)

## Deployment

### Prerequisites
- Raspberry Pi running Raspberry Pi OS (Trixie)
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
   This installs everything om the device including the prerequisites.

3. **Verify it's running**:
   ```bash
   sudo systemctl status terrarium
   sudo journalctl -u terrarium -f  # View logs
   ```

4. **Access the UI**:
   - **With Chromium kiosk** (if installed): Touch screen auto-launches full-screen Chromium at startup
   - **Any browser**: Open browser and navigate to `http://<pi-ip>:5000`
   - **Remote SSH**: App runs on port 5000; access from any browser on your network

### Configuration

All settings are stored in SQLite and managed via the web UI:
- **Thresholds**: Temperature thresholds for Relays 1-3 (default 29°C)
- **Humidity Threshold**: Sensor 1 humidity threshold for Relay 5 (default 60%)
- **Schedules**: Daylight on/off times for Relay 4
- **GPIO Map**: Customize relay-to-GPIO pin assignments (default PiRelay 6 mapping)
- **Camera Params**: MJPEG stream resolution and framerate:
  - `CAMERA_WIDTH=1920` (default, Full HD)
  - `CAMERA_HEIGHT=1080` (default, Full HD)
  - `CAMERA_FPS=15` (default)
- **Pico UART Sensor Input**: The installer writes these defaults:
   - `PicoUart__Enabled=true`
   - `PicoUart__Port=/dev/serial0`
   - `PicoUart__BaudRate=115200`
   - `PicoUart__ReadTimeoutSeconds=1.0`
   - `PicoUart__StaleAfterSeconds=20`
- **Log Retention**: Delete entries older than N months (1-24, default 12)

### Log Access

- View logs in the web UI under **Log History** page
- Logs are stored in SQLite and include:
  - Relay state changes with trigger source and sensor values
  - Hourly sensor reading snapshots
  - Automatic pruning by age

### Troubleshooting



## API / Endpoints


## Contributing

When making changes, please follow:
- Keep documentation concise and in present tense
- Preserve GPIO mappings and hysteresis/lockout logic
- Test on actual Raspberry Pi hardware if possible
- Avoid adding secrets or credentials to code examples

For detailed implementation status and remaining tasks, see [IMPLEMENTATION_STATUS.md](IMPLEMENTATION_STATUS.md).

