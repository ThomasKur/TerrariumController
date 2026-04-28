using TerrariumController.Models;
using TerrariumController.Services;

namespace TerrariumController.Tests;

internal sealed class TestSettingsService : ISettingsService
{
    private readonly Settings _settings;

    public TestSettingsService(Settings? settings = null)
    {
        _settings = settings ?? new Settings();
    }

    public Task<Settings> GetSettingsAsync()
    {
        return Task.FromResult(_settings);
    }

    public Task UpdateSettingsAsync(Settings settings)
    {
        _settings.Threshold1Temperature = settings.Threshold1Temperature;
        _settings.Threshold2Temperature = settings.Threshold2Temperature;
        _settings.Threshold3Temperature = settings.Threshold3Temperature;
        _settings.Sensor1HumidityThreshold = settings.Sensor1HumidityThreshold;
        _settings.TemperatureHysteresis = settings.TemperatureHysteresis;
        _settings.Relay4OnTime = settings.Relay4OnTime;
        _settings.Relay4OffTime = settings.Relay4OffTime;
        _settings.HumidityLockoutHours = settings.HumidityLockoutHours;
        _settings.LastModified = DateTime.UtcNow;
        return Task.CompletedTask;
    }

    public Task<long> GetDatabaseSizeAsync()
    {
        return Task.FromResult(0L);
    }

    public Task CompactDatabaseAsync()
    {
        return Task.CompletedTask;
    }
}

internal sealed class NoOpLoggingService : ILoggingService
{
    public Task LogRelayStateChangeAsync(int relayId, bool newState, string triggerSource, int? sensorId = null, double? temperature = null, double? humidity = null)
    {
        return Task.CompletedTask;
    }

    public Task LogHourlySnapshotAsync(Dictionary<int, (double? temp, double? humidity)> sensorReadings)
    {
        return Task.CompletedTask;
    }

    public Task PruneOldEntriesAsync()
    {
        return Task.CompletedTask;
    }

    public Task<List<LogEntry>> GetLogEntriesAsync(int pageNumber, int pageSize)
    {
        return Task.FromResult(new List<LogEntry>());
    }

    public Task<int> GetLogCountAsync()
    {
        return Task.FromResult(0);
    }
}

internal sealed class RecordingRelayService : IRelayService
{
    private readonly Dictionary<int, bool> _relayStates = new();

    public List<(int RelayId, bool State, string Trigger)> Calls { get; } = new();

    public Task<bool> GetRelayStateAsync(int relayId)
    {
        return Task.FromResult(_relayStates.TryGetValue(relayId, out var state) && state);
    }

    public Task SetRelayStateAsync(int relayId, bool state, string triggerSource)
    {
        _relayStates[relayId] = state;
        Calls.Add((relayId, state, triggerSource));
        return Task.CompletedTask;
    }

    public Task<Dictionary<int, bool>> GetAllRelayStatesAsync()
    {
        return Task.FromResult(new Dictionary<int, bool>(_relayStates));
    }

    public Task<bool> ShouldRelayBeOnAsync(int relayId, double? temperature, double? humidity)
    {
        return Task.FromResult(false);
    }

    public Task InitializeGpioAsync()
    {
        return Task.CompletedTask;
    }

    public Task CleanupGpioAsync()
    {
        return Task.CompletedTask;
    }
}

internal sealed class PollingSensorService : ISensorService
{
    private readonly List<SensorReading> _readings;

    public PollingSensorService()
    {
        _readings =
        [
            new SensorReading
            {
                SensorId = 1,
                Label = "Nest 1",
                Temperature = 28.0,
                Humidity = 55.0,
                IsValid = true,
                Timestamp = DateTime.UtcNow
            }
        ];
    }

    public int GetLatestReadingsCallCount { get; private set; }

    public Task<SensorReading?> ReadSensorAsync(int sensorId)
    {
        return Task.FromResult(_readings.FirstOrDefault(r => r.SensorId == sensorId));
    }

    public Task<List<SensorReading>> GetLatestReadingsAsync()
    {
        GetLatestReadingsCallCount++;

        foreach (var reading in _readings)
        {
            reading.Timestamp = DateTime.UtcNow;
        }

        return Task.FromResult(_readings.ToList());
    }

    public Task StoreSensorReadingAsync(SensorReading reading)
    {
        return Task.CompletedTask;
    }
}
