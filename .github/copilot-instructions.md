<!-- Use this file to provide workspace-specific custom instructions to Copilot. -->

# Terrarium Controller - Development Instructions

## Project Overview
ASP.NET Core Blazor Server application for Raspberry Pi terrarium control with DHT22 sensors, relay control, SQLite persistence, MJPEG camera streaming, and web-based dashboard.

## Core Requirements
- Blazor Server web UI with real-time sensor updates via SignalR
- SQLite database for settings and logs with configurable retention (1–24 months, default 12)
- GPIO control for 1–3 DHT22 sensors and PiRelay 6 (6 relays)
- Temperature thresholds with 1°C hysteresis for relays 1–3
- Humidity-triggered pulse (1 s) + 6-hour lockout for Sensor 1 → Relay 5
- Daylight scheduler for Relay 4 (configurable on/off times)
- mjpg-streamer supervision with configurable width/height/fps
- Log history with filterable view; DB size display and compact action
- LAN-only access; no authentication
- Kiosk autostart (Chromium full-screen) on Raspberry Pi OS

## Development Constraints
- Preserve GPIO mappings and default PiRelay6 table without explicit approval
- Keep 1°C hysteresis and humidity lockout logic in all documentation
- Assume Raspberry Pi OS environment
- Keep README aligned with implemented behavior
- No secrets or credentials in examples
- Use ASCII-only content unless context already contains non-ASCII

## Tech Stack
- **.NET 8+ (ASP.NET Core)**
- **Blazor Server** for UI
- **SQLite** for persistence
- **System.Device.Gpio** + **Iot.Device.DHTxx** for hardware
- **mjpg-streamer** supervised by app service
- **systemd** service unit + **Chromium** kiosk on Raspberry Pi OS

## Directory Structure
```
TerrariumController/
├── README.md
├── LICENSE
├── .github/
│   ├── copilot-instructions.md (this file)
│   └── instructions/
│       └── Instructions.instructions.md
├── 3DPrint/
├── PiSource/
│   ├── TerrariumController.sln
│   ├── src/
│   │   ├── TerrariumController/
│   │   │   ├── TerrariumController.csproj
│   │   │   ├── Program.cs
│   │   │   ├── appsettings.json
│   │   │   ├── Data/
│   │   │   │   ├── AppDbContext.cs
│   │   │   │   ├── Migrations/
│   │   │   │   └── SeedData.cs
│   │   │   ├── Models/
│   │   │   │   ├── SensorReading.cs
│   │   │   │   ├── RelayState.cs
│   │   │   │   ├── LogEntry.cs
│   │   │   │   └── Settings.cs
│   │   │   ├── Services/
│   │   │   │   ├── SensorService.cs
│   │   │   │   ├── RelayService.cs
│   │   │   │   ├── SchedulerService.cs
│   │   │   │   ├── LoggingService.cs
│   │   │   │   ├── CameraService.cs
│   │   │   │   └── SettingsService.cs
│   │   │   ├── Components/
│   │   │   │   ├── Layout/
│   │   │   │   │   └── MainLayout.razor
│   │   │   │   ├── Pages/
│   │   │   │   │   ├── Home.razor
│   │   │   │   │   ├── Settings.razor
│   │   │   │   │   └── LogHistory.razor
│   │   │   │   └── Shared/
│   │   │   │       ├── SensorRow.razor
│   │   │   │       ├── CameraFeed.razor
│   │   │   │       └── DaylightScheduler.razor
│   │   │   ├── wwwroot/
│   │   │   │   ├── css/
│   │   │   │   │   └── site.css
│   │   │   │   └── js/
│   │   │   │       └── site.js
│   │   │   ├── Properties/
│   │   │   │   └── launchSettings.json
│   │   │   └── appsettings.Development.json
│   │   └── ...
│   └── install/
│       ├── setup.sh (Raspberry Pi setup script)
│       ├── terrarium.service (systemd unit)
│       └── kiosk.sh (Chromium kiosk launcher)
└── ...
```

## Implementation Steps (Checklist)

- [ ] Step 1: Create Blazor Server project scaffold
- [ ] Step 2: Design and create SQLite schema (models, migrations)
- [ ] Step 3: Implement core services (GPIO, sensors, relays, logging, scheduler)
- [ ] Step 4: Build Blazor components (dashboard layout, settings, log history)
- [ ] Step 5: Wire SignalR for real-time updates
- [ ] Step 6: Integrate mjpg-streamer supervision
- [ ] Step 7: Add camera feed embedding and settings
- [ ] Step 8: Test on Raspberry Pi or emulator
- [ ] Step 9: Create setup/install scripts and systemd unit
- [ ] Step 10: Update README with deployment instructions
- [ ] Step 11: Verify build and document any gotchas

## Key Classes/Services to Implement

### Data Models
- `SensorReading`: timestamp, sensor id, temperature, humidity, validity
- `RelayState`: timestamp, relay id, state (on/off), trigger source, sensor values
- `LogEntry`: timestamp, type (relay change/hourly snapshot), details, sensor data
- `Settings`: GPIO map, thresholds, schedules, retention months, camera params

### Services
- `SensorService`: polls DHT22, validates readings, broadcasts updates
- `RelayService`: controls relay GPIO, applies hysteresis, tracks lockout state
- `SchedulerService`: manages Relay 4 daylight schedule
- `LoggingService`: writes to SQLite, prunes by date, calculates DB size
- `CameraService`: supervises mjpg-streamer, exposes MJPEG URL
- `SettingsService`: reads/writes SQLite settings, persists UI changes

### Blazor Components
- `Home.razor`: two-row dashboard (sensor row + control row)
- `Settings.razor`: threshold/schedule/retention/camera config
- `LogHistory.razor`: filterable log view with pagination
- `SensorRow.razor`: live T/RH display + slider per sensor
- `CameraFeed.razor`: embedded MJPEG tile
- `DaylightScheduler.razor`: time picker for Relay 4

## Build & Deployment

**Build:** `dotnet build` in `PiSource/`

**Run locally:** `dotnet run` (assumes .NET 8+ and localhost access to GPIO mock)

**Deploy to Pi:** 
1. Copy built artifacts to `/opt/terrarium/`
2. Install mjpg-streamer: `sudo apt install mjpg-streamer`
3. Copy systemd unit to `/etc/systemd/system/terrarium.service`
4. `sudo systemctl enable --now terrarium`
5. Access at `http://localhost:5000` in Chromium kiosk

## Testing Checklist
- [ ] Sensor polling and display update in real-time
- [ ] Relay state changes persist to log
- [ ] Hysteresis prevents rapid on/off switching
- [ ] Humidity lockout timer works (6 hours)
- [ ] Scheduler correctly triggers Relay 4
- [ ] Log retention prunes entries older than configured months
- [ ] DB compact action works and updates size display
- [ ] Camera feed loads and updates in UI
- [ ] Settings persist across restarts
- [ ] mjpg-streamer restarts with app

---

**Status:** In progress. Follow the implementation steps sequentially and update this checklist as you progress.
