using TerrariumController.Models;

namespace TerrariumController.Services;

public sealed class ControlStateStore
{
    private readonly object _syncRoot = new();
    private IReadOnlyList<LiveSensorState> _sensors = Array.Empty<LiveSensorState>();
    private IReadOnlyList<LiveRelayState> _relays = Enumerable.Range(1, 6)
        .Select(id => new LiveRelayState { RelayId = id, IsOn = false, TimestampUtc = DateTime.UtcNow, TriggerSource = "startup" })
        .ToArray();

    public DateTime LastControlCycleUtc { get; private set; }

    public IReadOnlyList<LiveSensorState> GetSensors()
    {
        lock (_syncRoot)
        {
            return _sensors.Select(CloneSensor).ToArray();
        }
    }

    public IReadOnlyList<LiveRelayState> GetRelays()
    {
        lock (_syncRoot)
        {
            return _relays.Select(CloneRelay).ToArray();
        }
    }

    public void UpdateSensors(IReadOnlyList<LiveSensorState> sensors)
    {
        lock (_syncRoot)
        {
            _sensors = sensors.Select(CloneSensor).ToArray();
            LastControlCycleUtc = DateTime.UtcNow;
        }
    }

    public void UpdateRelay(LiveRelayState relayState)
    {
        lock (_syncRoot)
        {
            var updated = _relays.ToDictionary(item => item.RelayId, CloneRelay);
            updated[relayState.RelayId] = CloneRelay(relayState);
            _relays = updated.Values.OrderBy(item => item.RelayId).ToArray();
            LastControlCycleUtc = DateTime.UtcNow;
        }
    }

    private static LiveSensorState CloneSensor(LiveSensorState source)
    {
        return new LiveSensorState
        {
            SensorId = source.SensorId,
            TemperatureC = source.TemperatureC,
            HumidityPercent = source.HumidityPercent,
            IsValid = source.IsValid,
            TimestampUtc = source.TimestampUtc,
            Source = source.Source
        };
    }

    private static LiveRelayState CloneRelay(LiveRelayState source)
    {
        return new LiveRelayState
        {
            RelayId = source.RelayId,
            IsOn = source.IsOn,
            TimestampUtc = source.TimestampUtc,
            TriggerSource = source.TriggerSource
        };
    }
}
