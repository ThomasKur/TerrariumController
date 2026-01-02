# TerrariumController Development

This is the source folder for the Blazor Server application.

## Build Requirements

- **.NET 10** SDK or later
- **SQLite3** (for local development database)
- **libgpiod** or **WiringPi** (for GPIO testing on non-Pi hardware, optional)

## Project Structure

```
TerrariumController/
├── TerrariumController.csproj       # Project file with dependencies
├── Program.cs                        # Service registration and app setup
├── appsettings.json                 # Configuration
├── appsettings.Development.json      # Development overrides
├── Data/
│   ├── AppDbContext.cs              # Entity Framework Core database context
│   └── Migrations/                  # EF Core migrations folder
├── Models/
│   ├── SensorReading.cs
│   ├── RelayState.cs
│   ├── LogEntry.cs
│   ├── Settings.cs
│   └── HumidityLockoutState.cs
├── Services/
│   ├── ISettingsService.cs          # Interfaces and implementations
│   ├── SettingsService.cs
│   ├── ILoggingService.cs
│   ├── LoggingService.cs
│   ├── ISensorService.cs
│   ├── SensorService.cs
│   ├── IRelayService.cs
│   ├── RelayService.cs
│   ├── ISchedulerService.cs
│   ├── SchedulerService.cs
│   ├── IHumidityService.cs
│   └── HumidityService.cs
├── Components/
│   ├── App.razor                    # Root component
│   ├── Layout/
│   │   └── MainLayout.razor         # App shell
│   ├── Pages/
│   │   ├── Home.razor               # Dashboard (2 rows)
│   │   ├── Settings.razor           # TODO: Settings page
│   │   └── LogHistory.razor         # TODO: Log history page
│   └── Shared/
│       └── ...                       # TODO: Shared UI components
├── wwwroot/
│   ├── css/
│   │   ├── app.css
│   │   └── site.css
│   └── js/
│       └── site.js
└── Properties/
    └── launchSettings.json          # Debug launch config
```

## Build & Run

### Restore dependencies
```bash
dotnet restore
```

### Build
```bash
dotnet build
```

### Run for development
```bash
dotnet run
```
Access at `https://localhost:7000` or `http://localhost:5000` (depending on launchSettings.json).

The app will create a SQLite database in the current directory: `terrarium.db`

### Publish for Raspberry Pi
```bash
dotnet publish -c Release -o ./publish
```
Copy the contents of `publish/` to `/opt/terrarium/` on the Pi.

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
- **Mock implementation**: Returns fixed sensor values
- **Real implementation**: Read DHT22 via `Iot.Device.DHTxx` on GPIO 23/24/25
- Stores readings in database with validity flag

### RelayService
- Controls relay on/off via GPIO
- Implements 1°C hysteresis logic to prevent rapid switching
- Logs all state changes with trigger source

### SchedulerService
- Manages Relay 4 (daylight) on/off schedule
- Runs in background, checks time and applies schedule

### HumidityService
- Monitors Sensor 1 humidity against threshold
- Triggers Relay 5 for 1 second when threshold is exceeded
- Enforces 6-hour lockout after trigger to allow humidity recovery

### LoggingService
- Records relay state changes with sensor values
- Records hourly sensor snapshots
- Prunes entries older than configurable retention (1-24 months, default 12)
- Provides paginated log history

## Testing

Currently, services use mock implementations (especially SensorService). To test:

1. Run the app locally: `dotnet run`
2. Navigate to `https://localhost:7000`
3. Adjust thresholds, schedules, and camera params in settings
4. View logs in the Log History page
5. Compact the database via Settings page

On actual Raspberry Pi hardware:
- Replace mock sensor code with real DHT22 reads
- GPIO control will auto-enable (currently stubbed)
- Camera feed will work if mjpg-streamer is running

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

## Future Enhancements

- [ ] Real DHT22 sensor integration
- [ ] GPIO relay control implementation
- [ ] SignalR hubs for real-time sensor updates
- [ ] Blazor Settings and LogHistory pages
- [ ] Background hosted services for polling and scheduling
- [ ] mjpg-streamer integration
- [ ] Unit tests for services
- [ ] API endpoints for remote access (optional)
