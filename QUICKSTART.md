# Quick Start Guide

## For Developers

### 1. Get Started Locally

```bash
cd PiSource/TerrariumController
dotnet build
dotnet run
```

Access: `https://localhost:7000` (or `http://localhost:5000`)

The SQLite database will be created automatically in the working directory.

### 2. Key Files to Modify

**To add new settings:**
1. Add property to `Models/Settings.cs`
2. Add UI control in `Components/Pages/Settings.razor` (TODO)
3. Retrieve in service: `var settings = await _settingsService.GetSettingsAsync()`

**To add sensor polling:**
1. Replace mock code in `Services/SensorService.cs` with DHT22 reads using `Iot.Device.DHTxx`
2. GPIO pins: 23 (Sensor 1), 24 (Sensor 2), 25 (Sensor 3)

**To implement relay control:**
1. Add GPIO code in `Services/RelayService.cs` using `System.Device.Gpio`
2. Use BOARD pin numbering per PiRelay 6 mapping
3. Reference `Models/Settings.cs` for GPIO pin assignments

**To add a background job:**
1. Create a class implementing `IHostedService` in `Services/`
2. Register in `Program.cs`: `builder.Services.AddHostedService<YourService>();`
3. Use `_loggingService` to record events

### 3. Database

**View schema:**
```bash
sqlite3 terrarium.db ".schema"
```

**Query data:**
```bash
sqlite3 terrarium.db "SELECT * FROM LogEntries ORDER BY Timestamp DESC LIMIT 10;"
```

**Reset database:**
```bash
rm terrarium.db
# App will recreate it on next run
```

### 4. Add a New Page

Create `Components/Pages/YourPage.razor`:

```razor
@page "/your-page"
@using TerrariumController.Services
@rendermode InteractiveServer
@inject IYourService YourService

<h1>Your Page</h1>
<p>@Message</p>

@code {
    private string Message = "";

    protected override async Task OnInitializedAsync()
    {
        Message = await YourService.GetMessageAsync();
    }
}
```

## For Raspberry Pi Deployment

### 1. Initial Setup (One-Time)

```bash
cd install
sudo bash setup.sh
```

This installs:
- .NET runtime
- GPIO libraries
- mjpg-streamer
- Systemd service unit

### 2. Build and Deploy

```bash
cd PiSource
dotnet publish -c Release -o /opt/terrarium
sudo chown -R terrarium:terrarium /opt/terrarium
```

### 3. Run and Monitor

```bash
# Start
sudo systemctl start terrarium

# Check status
sudo systemctl status terrarium

# View logs
sudo journalctl -u terrarium -f

# Stop
sudo systemctl stop terrarium
```

### 4. Access the App

- **Local Screen**: Touch screen launches Chromium automatically
- **Remote**: Open `http://<pi-ip>:5000` in any browser on your network
- **Camera Feed**: `http://<pi-ip>:8080/?action=stream` (if mjpg-streamer running)

### 5. Troubleshooting

**GPIO permission denied:**
```bash
sudo usermod -a -G dialout terrarium
sudo systemctl restart terrarium
```

**Database locked:**
```bash
sudo systemctl stop terrarium
sudo -u terrarium rm /opt/terrarium/terrarium.db
sudo systemctl start terrarium
```

**Check app logs:**
```bash
sudo journalctl -u terrarium -n 100 --no-pager
```

**SSH into Pi and check directly:**
```bash
ssh pi@<pi-ip>
ps aux | grep TerrariumController
ls -la /opt/terrarium/
```

## Architecture Quick Reference

```
User Browser (Blazor)
    ↓
Blazor Server (C#)
    ↓
Services (Business Logic)
    ├── SensorService (GPIO reads)
    ├── RelayService (GPIO writes)
    ├── SchedulerService (Timer logic)
    ├── HumidityService (Lockout logic)
    ├── LoggingService (DB writes)
    └── SettingsService (Config reads/writes)
    ↓
Entity Framework Core
    ↓
SQLite Database
    ↓
Filesystem: /opt/terrarium/terrarium.db
```

## File Locations on Raspberry Pi

| Item | Path |
|------|------|
| App | `/opt/terrarium/TerrariumController.dll` |
| Database | `/opt/terrarium/terrarium.db` |
| Service Unit | `/etc/systemd/system/terrarium.service` |
| User Home | `/home/terrarium/` |
| Kiosk Launcher | `/home/terrarium/.config/autostart/terrarium-kiosk.desktop` |
| mjpg-streamer | `/usr/bin/mjpg_streamer` |
| Logs | `sudo journalctl -u terrarium` |

## Testing Checklist

- [ ] Sensor readings appear in Home dashboard
- [ ] Relay thresholds adjustable via slider
- [ ] Scheduler on/off times save to Settings
- [ ] Log History shows relay state changes
- [ ] DB Compact action works (Settings page)
- [ ] Log entries auto-prune after configured months
- [ ] Camera feed loads in dashboard
- [ ] Humidity lockout prevents repeated pulses (6 hours)
- [ ] Hysteresis prevents rapid relay on/off switching (1°C)
- [ ] App survives reboot (auto-starts via systemd)

## Next: What to Build

See [IMPLEMENTATION_STATUS.md](../IMPLEMENTATION_STATUS.md) for the full checklist.

**Priority order:**
1. Real sensor reading (DHT22 on GPIO 23/24/25)
2. GPIO relay control (using BOARD pins)
3. Settings and LogHistory Blazor pages
4. Background services (polling, pruning)
5. mjpg-streamer integration
6. End-to-end testing on Pi

---

**Questions?** Check:
- [PiSource/README.md](PiSource/README.md) - Development guide
- [README.md](README.md) - Deployment guide
- [.github/copilot-instructions.md](.github/copilot-instructions.md) - Project guidelines
