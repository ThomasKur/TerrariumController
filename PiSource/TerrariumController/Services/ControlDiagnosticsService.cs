using System.Collections.Concurrent;

namespace TerrariumController.Services
{
    public record SensorDiagnostic(
        int SensorId,
        DateTime LastTimestampUtc,
        bool IsValid,
        double? Temperature,
        double? Humidity,
        string Status);

    public record RelayDecisionDiagnostic(
        int RelayId,
        DateTime LastDecisionUtc,
        bool TargetState,
        bool Applied,
        string Reason,
        string CycleId);

    public record ControlDiagnosticsSnapshot(
        DateTime SnapshotUtc,
        IReadOnlyDictionary<int, SensorDiagnostic> Sensors,
        IReadOnlyDictionary<int, RelayDecisionDiagnostic> Relays,
        int QueueDepth,
        double LastCommandLatencyMs);

    public interface IControlDiagnosticsService
    {
        void UpdateSensor(int sensorId, DateTime timestampUtc, bool isValid, double? temperature, double? humidity, string status);
        void UpdateRelayDecision(int relayId, bool targetState, bool applied, string reason, string cycleId, DateTime timestampUtc);
        void UpdateQueueMetrics(int queueDepth, double lastCommandLatencyMs);
        ControlDiagnosticsSnapshot GetSnapshot();
    }

    public class ControlDiagnosticsService : IControlDiagnosticsService
    {
        private readonly ConcurrentDictionary<int, SensorDiagnostic> _sensors = new();
        private readonly ConcurrentDictionary<int, RelayDecisionDiagnostic> _relays = new();
        private readonly object _metricsLock = new();
        private int _queueDepth;
        private double _lastCommandLatencyMs;

        public void UpdateSensor(int sensorId, DateTime timestampUtc, bool isValid, double? temperature, double? humidity, string status)
        {
            _sensors[sensorId] = new SensorDiagnostic(sensorId, timestampUtc, isValid, temperature, humidity, status);
        }

        public void UpdateRelayDecision(int relayId, bool targetState, bool applied, string reason, string cycleId, DateTime timestampUtc)
        {
            _relays[relayId] = new RelayDecisionDiagnostic(relayId, timestampUtc, targetState, applied, reason, cycleId);
        }

        public void UpdateQueueMetrics(int queueDepth, double lastCommandLatencyMs)
        {
            lock (_metricsLock)
            {
                _queueDepth = queueDepth;
                _lastCommandLatencyMs = lastCommandLatencyMs;
            }
        }

        public ControlDiagnosticsSnapshot GetSnapshot()
        {
            int queueDepth;
            double lastCommandLatencyMs;

            lock (_metricsLock)
            {
                queueDepth = _queueDepth;
                lastCommandLatencyMs = _lastCommandLatencyMs;
            }

            return new ControlDiagnosticsSnapshot(
                DateTime.UtcNow,
                _sensors.ToDictionary(k => k.Key, v => v.Value),
                _relays.ToDictionary(k => k.Key, v => v.Value),
                queueDepth,
                lastCommandLatencyMs);
        }
    }
}