# TerrariumController Development

This folder contains the rebuilt Raspberry Pi-side Blazor Server application.

## Build Requirements

- **.NET 10** SDK or later
- **SQLite3** (for local development database)
- **libgpiod** or **WiringPi** (for GPIO testing on non-Pi hardware, optional)

## Project Structure

```
PiSource/
├── PiSource.sln
├── TerrariumController/
│   ├── TerrariumController.csproj
│   ├── Program.cs
│   ├── appsettings.json
│   ├── Data/
│   │   └── AppDbContext.cs
│   ├── Models/
│   ├── Services/
│   │   ├── ControlBackgroundService.cs
│   │   ├── SensorService.cs
│   │   ├── RelayService.cs
│   │   ├── SchedulerService.cs
│   │   ├── HumidityService.cs
│   │   ├── LoggingService.cs
│   │   └── SettingsService.cs
│   ├── Components/
│   │   ├── Pages/
│   │   │   ├── Home.razor
│   │   │   ├── Settings.razor
│   │   │   └── LogHistory.razor
│   │   └── Shared/
│   │       ├── SensorRow.razor
│   │       ├── DaylightScheduler.razor
│   │       └── CameraFeed.razor
│   └── wwwroot/css/site.css
├── TerrariumController.Tests/
│   ├── TerrariumController.Tests.csproj
│   ├── TemperatureControlPolicyTests.cs
│   └── SchedulerServiceTests.cs
└── install/
  ├── build-for-pi.ps1
  ├── setup.sh
  └── terrarium.service
```

## Build & Run

### Restore dependencies
```bash
dotnet restore PiSource.sln
```

### Build
```bash
dotnet build PiSource.sln --configuration Release
```

### Run for development
```bash
dotnet run --project TerrariumController/TerrariumController.csproj
```
Access at `https://localhost:7000` or `http://localhost:5000` (depending on launchSettings.json).

The app will create a SQLite database in the current directory: `terrarium.db`

### Publish for Raspberry Pi
```bash
dotnet publish TerrariumController/TerrariumController.csproj -c Release -r linux-arm64 --self-contained true -o ../publish
```
This creates a self-contained ARM64 payload with the required .NET runtime bundled, so no shared runtime is required on the Pi. Copy the contents of `publish/` to `/opt/terrarium/`.

### Build using repo script
```powershell
pwsh -File PiSource/install/build-for-pi.ps1
```
The script defaults to a self-contained ARM64 build for the Pi.

### Run tests
```bash
dotnet test PiSource.sln --configuration Release
```

## Migrations

When you modify model classes:

```bash
# Create a new migration
dotnet ef migrations add <MigrationName>

# Apply migrations (done automatically on app startup)
dotnet ef database update
```

## Key Service Implementations

### SettingsService
- Reads/writes application settings to SQLite
- Manages GPIO pin mappings, thresholds, schedules
- Provides database size and compact (VACUUM) functionality

### SensorService
- Generates mock sensor data by default for development and stores readings in SQLite
- Tracks latest values for all three sensors in memory for UI refresh

### RelayService
- Controls relay state transitions and logs each state change
- Implements 1°C hysteresis logic to prevent rapid switching
- Supports pulse execution for Relay 5 humidity behavior

### SchedulerService
- Manages Relay 4 (daylight) on/off schedule, including overnight windows

### HumidityService
- Monitors Sensor 1 humidity against threshold
- Triggers Relay 5 for 1 second when threshold is exceeded
- Enforces 6-hour lockout after trigger to allow humidity recovery

### LoggingService
- Records relay state changes with sensor values
- Records hourly sensor snapshots
- Prunes entries older than configurable retention (1-24 months, default 12)
- Provides paginated log history

### ControlBackgroundService
- Runs the control loop in the background
- Polls sensors, applies temperature relay decisions, applies daylight scheduler, evaluates humidity lockout, writes hourly snapshots, and prunes retention

## Testing

To test end-to-end:

1. Run the app locally: `dotnet run`
2. Navigate to `https://localhost:7000`
3. Adjust thresholds, schedules, and camera params in settings
4. View logs in the Log History page
5. Compact the database via Settings page
6. Run `dotnet test PiSource.sln --configuration Release`

On actual Raspberry Pi hardware:
- Replace mock sensor reads with UART or direct GPIO sensor reads in `SensorService`
- Integrate real GPIO writes in `RelayService`
- Point `CameraUrl` setting to the active MJPEG endpoint

## Common Issues

### Database locked
If you see "database is locked" errors:
```bash
# Stop the app
# Delete or move terrarium.db
# Restart the app
```

### Port already in use
If port 5000/7000 is in use, modify `appsettings.Development.json`:
```json
{
  "Urls": "https://localhost:7002;http://localhost:5002"
}
```

### Entity Framework Core errors
Ensure you have the latest EF Core tools:
```bash
dotnet tool install --global dotnet-ef --version 10.0.0
```

## Contributing

- Keep models simple and focused on data storage
- Services should handle business logic (hysteresis, lockouts, scheduling)
- Components (Razor pages) should delegate to services for state management
- Follow async/await patterns throughout
- Add unit tests for service logic when possible

## Current Status

- [x] Blazor Server app skeleton and pages
- [x] SQLite models and persistence context
- [x] Background control loop with hysteresis, scheduler, and humidity lockout
- [x] Filterable log history and retention pruning
- [x] Settings persistence and DB compact action
- [x] Build and install scripts under `install/`
- [x] Core logic unit tests for hysteresis and scheduler
