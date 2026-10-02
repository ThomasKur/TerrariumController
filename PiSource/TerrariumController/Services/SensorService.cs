using TerrariumController.Data;
using TerrariumController.Models;

namespace TerrariumController.Services;

public sealed class SensorService : ISensorService
{
    private readonly AppDbContext _dbContext;
    private readonly ControlStateStore _stateStore;
    private readonly Random _random = new();

    public SensorService(AppDbContext dbContext, ControlStateStore stateStore)
    {
        _dbContext = dbContext;
        _stateStore = stateStore;
    }

    public async Task<IReadOnlyList<LiveSensorState>> PollAsync(Settings settings, CancellationToken cancellationToken = default)
    {
        var existing = _stateStore.GetSensors().ToDictionary(item => item.SensorId, item => item);
        var now = DateTime.UtcNow;

        var output = new List<LiveSensorState>(3);
        for (var sensorId = 1; sensorId <= 3; sensorId++)
        {
            existing.TryGetValue(sensorId, out var previous);

            var previousTemp = previous?.TemperatureC ?? 28.5m + sensorId;
            var previousHumidity = previous?.HumidityPercent ?? 58m;
            var tempDelta = (decimal)_random.NextDouble() - 0.5m;
            var humidityDelta = ((decimal)_random.NextDouble() - 0.5m) * 2m;

            var state = new LiveSensorState
            {
                SensorId = sensorId,
                TemperatureC = decimal.Round(previousTemp + tempDelta, 1),
                HumidityPercent = decimal.Round(previousHumidity + humidityDelta, 1),
                IsValid = true,
                TimestampUtc = now,
                Source = settings.UseMockSensors ? "mock" : "uart"
            };

            output.Add(state);
            _dbContext.SensorReadings.Add(new SensorReading
            {
                SensorId = state.SensorId,
                TemperatureC = state.TemperatureC,
                HumidityPercent = state.HumidityPercent,
                IsValid = state.IsValid,
                TimestampUtc = state.TimestampUtc,
                Source = state.Source
            });
        }

        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        _stateStore.UpdateSensors(output);

        return output;
    }

    public IReadOnlyList<LiveSensorState> GetLatest() => _stateStore.GetSensors();
}
