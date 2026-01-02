# Terrarium Controller - Implementation Status

## Project Overview

ASP.NET Core Blazor Server web application for controlling terrarium lighting and heating via temperature/humidity sensors and relay control on Raspberry Pi OS.

## Completed Components

### 1. Project Structure ✅
- Scaffolded Blazor Server project with .NET 10
- Created folder structure for Models, Services, Data, and Components
- Added project dependencies (Entity Framework Core, SQLite, GPIO libraries)

### 2. Data Models ✅
- `SensorReading.cs`: Stores DHT22 sensor readings (temp, humidity, validity)
- `RelayState.cs`: Logs relay state changes with trigger sources and sensor values
- `LogEntry.cs`: Combined logging for state changes and hourly snapshots
- `Settings.cs`: Persists GPIO mappings, thresholds, schedules, camera params, retention months
- `HumidityLockoutState.cs`: Tracks 6-hour humidity lockout state

### 3. Entity Framework Core Setup ✅
- `AppDbContext.cs`: Database context with DbSets for all entities
- Indexed queries for fast lookups (Timestamp descending)
- Default Settings seed data
- Ready for migrations

### 4. Service Layer ✅
Implemented interfaces and base services:

- **`ISettingsService`**: Read/write settings, get DB size, compact database
- **`ILoggingService`**: Log relay changes and hourly snapshots, prune by date, paginate history
- **`ISensorService`**: Mock sensor reading (ready for DHT22 integration)
- **`IRelayService`**: Relay control with hysteresis logic (1°C threshold + deadband)
- **`ISchedulerService`**: Daylight schedule for Relay 4 (on/off times)
- **`IHumidityService`**: Humidity threshold detection + 6-hour lockout mechanism

### 5. Program Configuration ✅
- SQLite database auto-initialization in install directory (`terrarium.db`)
- Service dependency injection registered
- SignalR configured for real-time updates (scaffolding only)
- Database migration on startup

### 6. Blazor Components (Partial) ✅
- **`Home.razor`**: Two-row dashboard layout
  - Row 1: Sensor cards with live T/RH display and adjustable sliders
  - Row 2: Scheduler, camera feed placeholder, settings button

## Remaining Implementation Tasks

### 7. Complete Blazor Pages
- **`Settings.razor`**: Configure GPIO map, thresholds, schedules, camera params, retention, DB compact action
- **`LogHistory.razor`**: Paged/filterable log view with date range picker
- **Shared components**: `SensorRow.razor`, `CameraFeed.razor`, `DaylightScheduler.razor`

### 8. Hardware Integration
- Replace mock sensor readings with actual DHT22 via `Iot.Device.DHTxx`
- Implement GPIO relay control using `System.Device.Gpio`
- Configure GPIO pins for Raspberry Pi (BOARD numbering)

### 9. Camera & MJPEG
- Install mjpg-streamer on Raspberry Pi
- Create systemd service/supervisor for mjpg-streamer
- Test MJPEG stream endpoint integration in Blazor

### 10. Background Services
- Implement `IHostedService` for periodic sensor polling (e.g., every 30 seconds)
- Add scheduled hysteresis checks and relay control
- Add hourly log snapshot background job
- Add daily log retention pruning job

### 11. SignalR Integration
- Create SignalR Hub for real-time sensor updates to clients
- Push relay state changes to connected browsers
- Optional: broadcast current relay states and sensor readings

### 12. System Integration & Setup
- Create Raspberry Pi setup script (`setup.sh`): install .NET, dependencies, systemd unit
- Create systemd service unit (`terrarium.service`): start app, supervise mjpg-streamer
- Create Chromium kiosk launcher script
- Document GPIO permission setup (group dialout or run as root)
- Test on actual Raspberry Pi hardware or emulator

## Database Schema (Auto-Created via EF Core)

### SensorReadings
- Id (PK)
- SensorId (1-3)
- Timestamp (indexed, descending)
- Temperature, Humidity
- IsValid (for validity flag)
- Label

### RelayStates
- Id (PK)
- RelayId (1-6)
- Timestamp (indexed)
- State (on/off)
- TriggerSource, SourceSensorId
- SensorTemperature, SensorHumidity

### LogEntries
- Id (PK)
- Timestamp (indexed)
- LogType ("StateChange" or "HourlySnapshot")
- Details
- RelayId, RelayState (for changes)
- Sensor1/2/3 Temperature/Humidity (snapshots)

### Settings (singleton row, id=1)
- Relay thresholds (1-3, default 29°C)
- Sensor1 humidity threshold
- GPIO mappings (Relay 1-6)
- Schedule times (Relay 4 on/off)
- Camera params (width, height, fps)
- Log retention months (1-24, default 12)
- LastModified

### HumidityLockoutStates
- Id (PK)
- SensorId (1, only Sensor 1)
- LastTriggeredTime
- IsLocked
- LockExpiresAt

## Key Features Implemented

✅ **Hysteresis Control**: 1°C dead band prevents rapid relay switching
✅ **Humidity Lockout**: 6-hour timeout after Relay 5 pulse (1 s)
✅ **Configurable Retention**: Delete logs older than 1–24 months (default 12)
✅ **DB Size Display**: Get database file size; VACUUM compact action
✅ **Safe Sensor Handling**: Disable controls when sensor data absent
✅ **Settings Persistence**: All thresholds, schedules, GPIO saved to SQLite
✅ **Logging**: State changes + hourly snapshots, pruning support

## Build & Run (Local Development)

```bash
cd PiSource/TerrariumController

# Restore and build
dotnet build

# Create initial migration (first time)
dotnet ef migrations add InitialCreate

# Run
dotnet run
```

Access at `https://localhost:7000` (or `http://localhost:5000` if no HTTPS).

## Next Steps for Completion

1. **Implement hardware layers**: Replace mock sensor code with actual DHT22 reads
2. **Finish Blazor pages**: Settings, LogHistory, and shared components
3. **Add background services**: Polling, scheduling, retention jobs
4. **Integrate camera**: mjpg-streamer supervision and MJPEG streaming
5. **Setup systemd**: Service unit for app + kiosk launcher
6. **Test on Pi**: Validate GPIO control, camera feed, real-time updates
7. **Documentation**: Update README with deployment steps

## Known Limitations / TODO

- DHT22 sensor reading is mocked (returns fixed values)
- GPIO relay control not yet implemented (placeholder methods)
- Camera feed URL hardcoded (`http://localhost:8080/?action=stream`)
- No real-time SignalR updates (scaffolding only)
- Blazor Settings and LogHistory pages need completion
- mjpg-streamer supervisor process not yet wired

## Architecture Notes

- **Separation of Concerns**: Services handle business logic; Blazor components handle UI
- **Async/await throughout**: All DB and service calls are async
- **Entity Framework Core**: Type-safe LINQ queries with automatic migration
- **Dependency Injection**: Services registered in Program.cs, injected into components
- **GPIO Safety**: Default mappings preserved; overridable via Settings

## References

- [Raspberry Pi GPIO pinout](https://pinout.xyz/)
- [PiRelay 6 wiring guide](https://www.pi-shop.ch/pirelay-6)
- [Iot.Device.DHTxx library](https://github.com/dotnet/iot/)
- [Blazor Server docs](https://learn.microsoft.com/en-us/aspnet/core/blazor/hosting-models)
